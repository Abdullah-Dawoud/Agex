using System.IO.Pipes;
using System.Text.Json;
using AutodeskAIBridge.Protocol;

namespace AutodeskAIBridge.Host;

/// <summary>Named-pipe client with authenticated handshake and correlation-preserving frames.</summary>
public sealed class NamedPipeClientTransport : IAuthenticatedIpcTransport
{
    private readonly NamedPipeOptions _options;
    private readonly string _clientInstanceId = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public NamedPipeClientTransport(NamedPipeOptions options) => _options = options;

    public string SessionId { get; private set; } = string.Empty;

    public async Task ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (true)
        {
            try { await ConnectAsync(cancellationToken).ConfigureAwait(false); return; }
            catch when (++attempts < _options.MaxReconnectAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, attempts * 500)), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_pipe is not null) throw new InvalidOperationException("Transport is already connected.");
        _pipe = new NamedPipeClientStream(".", _options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await _pipe.ConnectAsync(_options.HandshakeTimeout, cancellationToken).ConfigureAwait(false); }
        catch { await _pipe.DisposeAsync().ConfigureAwait(false); _pipe = null; throw; }
        _reader = new StreamReader(_pipe, System.Text.Encoding.UTF8, false, 4096, true);
        _writer = new StreamWriter(_pipe, System.Text.Encoding.UTF8, 4096, true) { AutoFlush = true };

        var nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var hello = new IpcEnvelope
        {
            Kind = IpcMessageKind.AuthHello,
            MessageId = Guid.NewGuid().ToString("N"),
            CorrelationId = Guid.NewGuid().ToString("N"),
            Payload = JsonSerializer.SerializeToElement(new IpcAuthHello(
                ProtocolConstants.Version,
                _clientInstanceId,
                "autodesk-plugin",
                "unknown",
                nonce,
                IpcAuthentication.CreateProof(_options.SharedSecret, nonce)), ProtocolJson.Options)
        };
        await SendAsync(hello, cancellationToken).ConfigureAwait(false);
        var acceptedLine = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (acceptedLine is null) throw new IOException("Named-pipe authentication closed by server.");
        var accepted = JsonSerializer.Deserialize<IpcEnvelope>(acceptedLine, ProtocolJson.Options);
        if (accepted is null || accepted.Kind != IpcMessageKind.AuthAccepted)
            throw new UnauthorizedAccessException("Named-pipe authentication failed.");
        var auth = accepted.Payload.Deserialize<IpcAuthAccepted>(ProtocolJson.Options)
            ?? throw new UnauthorizedAccessException("Named-pipe authentication response is invalid.");
        if (!string.Equals(auth.Nonce, nonce, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Named-pipe authentication nonce mismatch.");
        SessionId = auth.SessionId;
    }

    public async Task SendAsync(IpcEnvelope message, CancellationToken cancellationToken)
    {
        if (_writer is null) throw new InvalidOperationException("Transport is not connected.");
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var serialized = JsonSerializer.Serialize(message, ProtocolJson.Options);
            if (System.Text.Encoding.UTF8.GetByteCount(serialized) > _options.MaxMessageBytes)
                throw new InvalidDataException("IPC message exceeds configured maximum size.");
            await _writer.WriteLineAsync(serialized).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _sendGate.Release(); }
    }

    public async Task<IpcEnvelope?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_reader is null) throw new InvalidOperationException("Transport is not connected.");
        var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null) return null;
        if (System.Text.Encoding.UTF8.GetByteCount(line) > _options.MaxMessageBytes)
            throw new InvalidDataException("IPC message exceeds configured maximum size.");
        return JsonSerializer.Deserialize<IpcEnvelope>(line, ProtocolJson.Options)
            ?? throw new InvalidDataException("IPC message is invalid.");
    }

    public Task CancelAsync(string requestId, string correlationId, CancellationToken cancellationToken)
        => SendAsync(new IpcEnvelope
        {
            Kind = IpcMessageKind.Cancel,
            MessageId = Guid.NewGuid().ToString("N"),
            RequestId = requestId,
            CorrelationId = correlationId,
            Payload = JsonSerializer.SerializeToElement(new { requestId, correlationId }, ProtocolJson.Options)
        }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _reader?.Dispose();
        if (_writer is not null) await _writer.DisposeAsync().ConfigureAwait(false);
        if (_pipe is not null) await _pipe.DisposeAsync().ConfigureAwait(false);
        _sendGate.Dispose();
    }
}
