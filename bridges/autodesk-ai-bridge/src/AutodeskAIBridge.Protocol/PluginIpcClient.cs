using System.IO.Pipes;
using System.Collections.Concurrent;
using System.Text.Json;
using AutodeskAIBridge.Core;

namespace AutodeskAIBridge.Protocol;

/// <summary>Plugin-side named-pipe client. It receives host requests and never calls Autodesk APIs itself.</summary>
public sealed class PluginIpcClient : IAsyncDisposable
{
    private readonly NamedPipeOptions _options;
    private readonly IpcAuthHello _identity;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public PluginIpcClient(NamedPipeOptions options, string product, string productVersion, string instanceId,
        int? processId = null, string? activeDocumentId = null, string? activeDocumentName = null,
        IReadOnlyList<AutodeskDocumentInfo>? documents = null, IReadOnlyList<string>? capabilities = null)
    {
        _options = options;
        var nonceBytes = new byte[32];
        using (var random = System.Security.Cryptography.RandomNumberGenerator.Create()) random.GetBytes(nonceBytes);
        var nonce = BitConverter.ToString(nonceBytes).Replace("-", string.Empty);
        _identity = new IpcAuthHello(ProtocolConstants.Version, instanceId, product, productVersion, nonce,
            IpcAuthentication.CreateProof(options.SharedSecret, nonce), processId, activeDocumentId, activeDocumentName, documents, capabilities);
    }

    public string SessionId { get; private set; } = string.Empty;
    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected) return;
        _pipe = new NamedPipeClientStream(".", _options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectTimeout.CancelAfter(_options.HandshakeTimeout);
                await _pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);
            }
            _reader = new StreamReader(_pipe, System.Text.Encoding.UTF8, false, 4096, true);
            _writer = new StreamWriter(_pipe, new System.Text.UTF8Encoding(false), 4096, true);
            await SendAsync(new IpcEnvelope { Kind = IpcMessageKind.AuthHello, MessageId = Guid.NewGuid().ToString("N"), CorrelationId = Guid.NewGuid().ToString("N"), Payload = JsonSerializer.SerializeToElement(_identity, ProtocolJson.Options) }, cancellationToken).ConfigureAwait(false);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(_options.HandshakeTimeout);
            var line = await ReadLineAsync(handshake.Token).ConfigureAwait(false);
            if (line is null) throw new IOException("Named-pipe authentication closed by server.");
            var accepted = IpcFrameCodec.Deserialize(line, _options.MaxMessageBytes);
            if (accepted.Kind != IpcMessageKind.AuthAccepted) throw new UnauthorizedAccessException("Named-pipe authentication failed.");
            var auth = accepted.Payload.Deserialize<IpcAuthAccepted>(ProtocolJson.Options) ?? throw new UnauthorizedAccessException("Named-pipe authentication response is invalid.");
            if (!string.Equals(auth.Nonce, _identity.Nonce, StringComparison.Ordinal) || !string.IsNullOrEmpty(auth.ErrorCode)) throw new UnauthorizedAccessException(auth.ErrorMessage ?? "Named-pipe authentication failed.");
            SessionId = auth.SessionId;
        }
        catch
        {
            await DisposeConnectionAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (true)
        {
            try { await ConnectAsync(cancellationToken).ConfigureAwait(false); return; }
            catch when (++attempts < _options.MaxReconnectAttempts) { await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, attempts * 500)), cancellationToken).ConfigureAwait(false); }
        }
    }

    /// <summary>Keep a loaded plugin available when AGEX starts, stops, or restarts its bridge host.</summary>
    public async Task RunWithReconnectAsync(Func<BridgeRequest, CancellationToken, Task<BridgeResponse>> handler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectWithRetryAsync(cancellationToken).ConfigureAwait(false);
                await RunAsync(handler, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or ObjectDisposedException)
            {
                System.Diagnostics.Debug.WriteLine($"Autodesk bridge reconnecting: {exception.GetType().Name}");
            }
            finally { await DisposeConnectionAsync().ConfigureAwait(false); }
            try { await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    public async Task RunAsync(Func<BridgeRequest, CancellationToken, Task<BridgeResponse>> handler, CancellationToken cancellationToken)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        if (_reader is null) throw new InvalidOperationException("Plugin IPC client is not connected.");
        var requests = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var active = new List<Task>();
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            var message = IpcFrameCodec.Deserialize(line, _options.MaxMessageBytes);
            if (message.Kind == IpcMessageKind.Heartbeat)
            {
                await SendAsync(message with { Kind = IpcMessageKind.Heartbeat, Payload = JsonSerializer.SerializeToElement(new { utc = DateTimeOffset.UtcNow }, ProtocolJson.Options) }, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (message.Kind == IpcMessageKind.Goodbye) break;
            if (message.Kind == IpcMessageKind.Cancel && message.RequestId is not null)
            {
                if (requests.TryGetValue(message.RequestId, out var requestCancellation)) requestCancellation.Cancel();
                continue;
            }
            if (message.Kind != IpcMessageKind.Request || message.RequestId is null)
                continue;
            active.Add(ProcessRequestAsync(message, handler, cancellationToken));
        }
        try { await Task.WhenAll(active).ConfigureAwait(false); } catch (OperationCanceledException exception) { System.Diagnostics.Debug.WriteLine($"Plugin request drain stopped: {exception.GetType().Name}"); }

        async Task ProcessRequestAsync(IpcEnvelope message, Func<BridgeRequest, CancellationToken, Task<BridgeResponse>> callback, CancellationToken token)
        {
            using var requestToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            requests[message.RequestId!] = requestToken;
            BridgeResponse response;
            try
            {
                var request = message.Payload.Deserialize<BridgeRequest>(ProtocolJson.Options);
                response = request is null
                    ? new BridgeResponse { RequestId = message.RequestId!, Success = false, Error = new BridgeErrorDto(BridgeErrorCodes.InvalidRequest, "Bridge request is invalid.", true) }
                    : await callback(request, requestToken.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
            {
                response = new BridgeResponse { RequestId = message.RequestId!, Success = false, Error = new BridgeErrorDto(BridgeErrorCodes.Cancelled, "Request was cancelled.", true) };
            }
            catch (Exception exception)
            {
                response = new BridgeResponse { RequestId = message.RequestId!, Success = false, Error = new BridgeErrorDto(BridgeErrorCodes.InvalidRequest, "Plugin request failed.", true, new { exception = exception.GetType().Name }) };
            }
            finally { requests.TryRemove(message.RequestId!, out _); }
            await SendAsync(new IpcEnvelope { Kind = IpcMessageKind.Response, MessageId = Guid.NewGuid().ToString("N"), RequestId = response.RequestId, CorrelationId = message.CorrelationId, Payload = JsonSerializer.SerializeToElement(response, ProtocolJson.Options) }, token).ConfigureAwait(false);
        }
    }

    public Task SendAsync(IpcEnvelope message, CancellationToken cancellationToken)
    {
        if (_writer is null) throw new InvalidOperationException("Plugin IPC client is not connected.");
        return SendCoreAsync(message, cancellationToken);
    }

    private async Task SendCoreAsync(IpcEnvelope message, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { cancellationToken.ThrowIfCancellationRequested(); await _writer!.WriteLineAsync(IpcFrameCodec.Serialize(message, _options.MaxMessageBytes)).ConfigureAwait(false); await _writer.FlushAsync().ConfigureAwait(false); }
        finally { _sendGate.Release(); }
    }

    public async ValueTask DisposeAsync() { await DisposeConnectionAsync().ConfigureAwait(false); _sendGate.Dispose(); }
    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        var read = _reader!.ReadLineAsync();
        await Task.WhenAny(read, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await read.ConfigureAwait(false);
#else
        return await _reader!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
#endif
    }
    private ValueTask DisposeConnectionAsync()
    {
        try { _reader?.Dispose(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        try { _writer?.Dispose(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        try { _pipe?.Dispose(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        _reader = null; _writer = null; _pipe = null; SessionId = string.Empty;
        return default;
    }
}
