using System.IO.Pipes;
using System.Text.Json;
using AutodeskAIBridge.Core;
using AutodeskAIBridge.Protocol;

namespace AutodeskAIBridge.Host;

/// <summary>Authenticated local IPC server. One authenticated connection maps to one plugin instance.</summary>
public sealed class NamedPipeServer
{
    private readonly NamedPipeOptions _options;
    private readonly Func<IpcEnvelope, CancellationToken, Task<IpcEnvelope?>> _handler;
    private readonly Func<IpcServerConnection, Task>? _onConnected;
    private readonly Func<IpcServerConnection, Task>? _onDisconnected;
    private readonly string _serverInstanceId = Guid.NewGuid().ToString("N");

    public NamedPipeServer(NamedPipeOptions options, Func<IpcEnvelope, CancellationToken, Task<IpcEnvelope?>> handler, Func<IpcServerConnection, Task>? onConnected = null, Func<IpcServerConnection, Task>? onDisconnected = null)
    {
        _options = options;
        _handler = handler;
        _onConnected = onConnected;
        _onDisconnected = onDisconnected;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var slots = new SemaphoreSlim(16, 16);
        var active = new List<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await slots.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            var pipe = new NamedPipeServerStream(_options.PipeName, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                slots.Release();
                break;
            }
            active.Add(HandleConnectedAsync(pipe));
            active.RemoveAll(task => task.IsCompletedSuccessfully);
        }
        try { await Task.WhenAll(active).ConfigureAwait(false); } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

        async Task HandleConnectedAsync(NamedPipeServerStream pipe)
        {
            try { await using (pipe) await HandleClientAsync(pipe, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) { /* A plugin or host closed its pipe. The accept loop stays available. */ }
            catch (ObjectDisposedException) { /* Shutdown closed the pipe while its writer drained. */ }
            finally { slots.Release(); }
        }
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, false, 4096, true);
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 4096, true);
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshake.CancelAfter(_options.HandshakeTimeout);
        var helloLine = await reader.ReadLineAsync(handshake.Token).ConfigureAwait(false);
        if (helloLine is null) return;
        IpcEnvelope hello;
        IpcAuthHello? auth;
        try
        {
            if (System.Text.Encoding.UTF8.GetByteCount(helloLine) > _options.MaxMessageBytes) return;
            hello = JsonSerializer.Deserialize<IpcEnvelope>(helloLine, ProtocolJson.Options) ?? throw new InvalidDataException("IPC message is invalid.");
            auth = hello.Payload.Deserialize<IpcAuthHello>(ProtocolJson.Options);
        }
        catch (Exception) { return; }
        if (hello.Kind != IpcMessageKind.AuthHello || auth is null) return;
        if (!string.Equals(auth.ProtocolVersion, ProtocolConstants.Version, StringComparison.Ordinal))
        {
            await WriteAcceptedAsync(writer, hello, auth, string.Empty, BridgeErrorCodes.ProtocolVersionMismatch, "Unsupported IPC protocol version.").ConfigureAwait(false);
            return;
        }
        if (string.IsNullOrWhiteSpace(auth.Nonce) || !IpcAuthentication.Verify(_options.SharedSecret, auth.Nonce, auth.TokenProof))
        {
            await WriteAcceptedAsync(writer, hello, auth, string.Empty, BridgeErrorCodes.AuthenticationFailed, "Named-pipe authentication failed.").ConfigureAwait(false);
            return;
        }
        if (!auth.Product.Equals("revit", StringComparison.OrdinalIgnoreCase) && !auth.Product.Equals("autocad", StringComparison.OrdinalIgnoreCase))
        {
            await WriteAcceptedAsync(writer, hello, auth, string.Empty, BridgeErrorCodes.UnsupportedOperation, "Only Revit and AutoCAD plugins may connect.").ConfigureAwait(false);
            return;
        }
        var sessionId = Guid.NewGuid().ToString("N");
        await WriteAcceptedAsync(writer, hello, auth, sessionId, null, null).ConfigureAwait(false);
        var info = new PluginConnectionInfo(sessionId, auth.ClientInstanceId, auth.Product, auth.ProductVersion, auth.ProcessId, auth.ActiveDocumentId, auth.ActiveDocumentName, auth.Documents ?? [], auth.SupportedCapabilities ?? [], DateTimeOffset.UtcNow);
        var connection = new IpcServerConnection(stream, info, _options);
        try { if (_onConnected is not null) await _onConnected(connection).ConfigureAwait(false); await connection.RunAsync(_handler, cancellationToken).ConfigureAwait(false); }
        finally { if (_onDisconnected is not null) await _onDisconnected(connection).ConfigureAwait(false); await connection.DisposeAsync().ConfigureAwait(false); }
    }

    private async Task WriteAcceptedAsync(StreamWriter writer, IpcEnvelope hello, IpcAuthHello auth, string sessionId, string? errorCode, string? errorMessage)
    {
        var accepted = new IpcEnvelope { Kind = IpcMessageKind.AuthAccepted, MessageId = Guid.NewGuid().ToString("N"), CorrelationId = hello.CorrelationId, Payload = JsonSerializer.SerializeToElement(new IpcAuthAccepted(ProtocolConstants.Version, _serverInstanceId, auth.Nonce, sessionId, DateTimeOffset.UtcNow.AddHours(8), errorCode, errorMessage), ProtocolJson.Options) };
        await writer.WriteLineAsync(IpcFrameCodec.Serialize(accepted, _options.MaxMessageBytes)).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }
}
