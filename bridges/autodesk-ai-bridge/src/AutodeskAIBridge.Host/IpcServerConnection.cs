using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using AutodeskAIBridge.Core;
using AutodeskAIBridge.Protocol;

namespace AutodeskAIBridge.Host;

public sealed record PluginConnectionInfo(
    string SessionId,
    string ClientInstanceId,
    string Product,
    string ProductVersion,
    int? ProcessId,
    string? ActiveDocumentId,
    string? ActiveDocumentName,
    IReadOnlyList<AutodeskDocumentInfo> Documents,
    IReadOnlyList<string> SupportedCapabilities,
    DateTimeOffset ConnectedAt);

/// <summary>Authenticated duplex connection owned by one plugin process.</summary>
public sealed class IpcServerConnection : IAsyncDisposable
{
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly int _maxMessageBytes;
    private readonly TimeSpan _heartbeatInterval;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeResponse>> _pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _closed = new();
    private Stream? _stream;

    internal IpcServerConnection(Stream stream, PluginConnectionInfo info, NamedPipeOptions options)
    {
        _stream = stream;
        Info = info;
        _maxMessageBytes = options.MaxMessageBytes;
        _heartbeatInterval = options.HeartbeatInterval;
        _reader = new StreamReader(stream, System.Text.Encoding.UTF8, false, 4096, true);
        _writer = new StreamWriter(stream, System.Text.Encoding.UTF8, 4096, true) { AutoFlush = true };
    }

    public PluginConnectionInfo Info { get; }
    public bool IsConnected => _stream?.CanRead == true;
    public DateTimeOffset LastHeartbeatUtc { get; private set; } = DateTimeOffset.UtcNow;

    public async Task RunAsync(Func<IpcEnvelope, CancellationToken, Task<IpcEnvelope?>> handler, CancellationToken cancellationToken)
    {
        LastHeartbeatUtc = Info.ConnectedAt;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
        var heartbeat = HeartbeatLoopAsync(linked.Token);
        var incoming = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var active = new List<Task>();
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
                if (line is null) break;
                IpcEnvelope message;
                try { message = IpcFrameCodec.Deserialize(line, _maxMessageBytes); }
                catch (InvalidDataException exception)
                {
                    var code = exception.Message.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0 ? BridgeErrorCodes.ProtocolVersionMismatch : BridgeErrorCodes.InvalidRequest;
                    await SendErrorAsync(code, exception.Message, linked.Token).ConfigureAwait(false);
                    break;
                }
                if (message.Kind == IpcMessageKind.Response && message.RequestId is not null)
                {
                    if (_pending.TryRemove(message.RequestId, out var completion))
                    {
                        var response = message.Payload.Deserialize<BridgeResponse>(ProtocolJson.Options);
                        completion.TrySetResult(response ?? new BridgeResponse { RequestId = message.RequestId, Success = false, Error = new BridgeErrorDto(BridgeErrorCodes.InvalidRequest, "Plugin response is invalid.", true) });
                    }
                    continue;
                }
                if (message.Kind == IpcMessageKind.Heartbeat)
                {
                    LastHeartbeatUtc = DateTimeOffset.UtcNow;
                    await SendAsync(message with { Kind = IpcMessageKind.Heartbeat, Payload = JsonSerializer.SerializeToElement(new { utc = DateTimeOffset.UtcNow }, ProtocolJson.Options) }, linked.Token).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == IpcMessageKind.Cancel && message.RequestId is not null)
                {
                    if (incoming.TryGetValue(message.RequestId, out var requestCancellation)) requestCancellation.Cancel();
                    continue;
                }
                active.Add(ProcessIncomingAsync(message, handler, linked.Token));
            }
        }
        finally
        {
            try { await SendAsync(new IpcEnvelope { Kind = IpcMessageKind.Goodbye, MessageId = Guid.NewGuid().ToString("N"), Payload = JsonSerializer.SerializeToElement(new { reason = "server_shutdown" }, ProtocolJson.Options) }, CancellationToken.None).ConfigureAwait(false); } catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"IPC goodbye failed: {exception.GetType().Name}"); }
            linked.Cancel();
            try { await heartbeat.ConfigureAwait(false); } catch (OperationCanceledException exception) { System.Diagnostics.Debug.WriteLine($"IPC heartbeat stopped: {exception.GetType().Name}"); }
            try { await Task.WhenAll(active).ConfigureAwait(false); } catch (OperationCanceledException exception) { System.Diagnostics.Debug.WriteLine($"IPC request drain stopped: {exception.GetType().Name}"); }
            FailPending(BridgeErrorCodes.PluginDisconnected, "Plugin connection was closed.");
        }

        async Task ProcessIncomingAsync(IpcEnvelope message, Func<IpcEnvelope, CancellationToken, Task<IpcEnvelope?>> callback, CancellationToken token)
        {
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (message.RequestId is not null) incoming[message.RequestId] = requestCancellation;
            try
            {
                var reply = await callback(message, requestCancellation.Token).ConfigureAwait(false);
                if (reply is not null) await SendAsync(reply, token).ConfigureAwait(false);
            }
            finally { if (message.RequestId is not null) incoming.TryRemove(message.RequestId, out _); }
        }
    }

    public async Task<BridgeResponse> SendRequestAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        if (!IsConnected) return Failure(request, BridgeErrorCodes.PluginDisconnected, "Plugin is disconnected.");
        var completion = new TaskCompletionSource<BridgeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.RequestId, completion)) return Failure(request, BridgeErrorCodes.InvalidRequest, "Duplicate request ID.");
        try
        {
            await SendAsync(new IpcEnvelope { Kind = IpcMessageKind.Request, MessageId = Guid.NewGuid().ToString("N"), RequestId = request.RequestId, CorrelationId = request.CorrelationId, Payload = JsonSerializer.SerializeToElement(request, ProtocolJson.Options) }, cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
            timeout.CancelAfter(request.Options.TimeoutMilliseconds is > 0 and <= 300_000 ? request.Options.TimeoutMilliseconds : 30_000);
            try { return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { await SendCancelAsync(request, CancellationToken.None).ConfigureAwait(false); return Failure(request, BridgeErrorCodes.Cancelled, "Request was cancelled."); }
            catch (OperationCanceledException) { await SendCancelAsync(request, CancellationToken.None).ConfigureAwait(false); return Failure(request, BridgeErrorCodes.Timeout, "Request timed out."); }
        }
        finally { _pending.TryRemove(request.RequestId, out _); }
    }

    private async Task SendCancelAsync(BridgeRequest request, CancellationToken cancellationToken)
        => await SendAsync(new IpcEnvelope { Kind = IpcMessageKind.Cancel, MessageId = Guid.NewGuid().ToString("N"), RequestId = request.RequestId, CorrelationId = request.CorrelationId, Payload = JsonSerializer.SerializeToElement(new { requestId = request.RequestId, correlationId = request.CorrelationId }, ProtocolJson.Options) }, cancellationToken).ConfigureAwait(false);

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_heartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            await SendAsync(new IpcEnvelope { Kind = IpcMessageKind.Heartbeat, MessageId = Guid.NewGuid().ToString("N"), Payload = JsonSerializer.SerializeToElement(new { utc = DateTimeOffset.UtcNow }, ProtocolJson.Options) }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendErrorAsync(string code, string message, CancellationToken token)
        => await SendAsync(new IpcEnvelope { Kind = IpcMessageKind.Error, MessageId = Guid.NewGuid().ToString("N"), Payload = JsonSerializer.SerializeToElement(new IpcErrorPayload(code, message), ProtocolJson.Options) }, token).ConfigureAwait(false);

    private async Task SendAsync(IpcEnvelope message, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _writer.WriteLineAsync(IpcFrameCodec.Serialize(message, _maxMessageBytes)).ConfigureAwait(false); await _writer.FlushAsync(cancellationToken).ConfigureAwait(false); }
        finally { _sendGate.Release(); }
    }

    private void FailPending(string code, string message)
    {
        foreach (var pair in _pending.ToArray())
            if (_pending.TryRemove(pair.Key, out var completion)) completion.TrySetResult(new BridgeResponse { RequestId = pair.Key, Success = false, Error = new BridgeErrorDto(code, message, true) });
    }

    private static BridgeResponse Failure(BridgeRequest request, string code, string message) => new() { RequestId = request.RequestId, Success = false, Error = new BridgeErrorDto(code, message, true) };

    public async ValueTask DisposeAsync()
    {
        _closed.Cancel();
        FailPending(BridgeErrorCodes.PluginDisconnected, "Plugin connection was closed.");
        _reader.Dispose();
        await _writer.DisposeAsync().ConfigureAwait(false);
        if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false);
        _closed.Dispose(); _sendGate.Dispose(); _stream = null;
    }
}
