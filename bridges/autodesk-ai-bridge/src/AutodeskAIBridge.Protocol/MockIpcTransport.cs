using System.Collections.Concurrent;

namespace AutodeskAIBridge.Protocol;

/// <summary>Deterministic in-memory duplex transport for IPC tests.</summary>
public sealed class MockIpcTransport : IAuthenticatedIpcTransport
{
    private readonly ConcurrentQueue<IpcEnvelope> _inbound = new();
    private readonly SemaphoreSlim _available = new(0);
    private MockIpcTransport? _peer;
    private int _connected;

    public static (MockIpcTransport A, MockIpcTransport B) CreatePair()
    {
        var a = new MockIpcTransport();
        var b = new MockIpcTransport();
        a._peer = b; b._peer = a;
        return (a, b);
    }

    public string SessionId { get; private set; } = string.Empty;
    public bool IsConnected => Volatile.Read(ref _connected) != 0;
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_peer is null) throw new InvalidOperationException("Mock transport has no peer.");
        Volatile.Write(ref _connected, 1); SessionId = Guid.NewGuid().ToString("N"); return Task.CompletedTask;
    }

    public Task SendAsync(IpcEnvelope message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected || _peer is null || !_peer.IsConnected) throw new IOException("Mock transport is disconnected.");
        _peer._inbound.Enqueue(message); _peer._available.Release(); return Task.CompletedTask;
    }

    public async Task<IpcEnvelope?> ReceiveAsync(CancellationToken cancellationToken)
    {
        await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _inbound.TryDequeue(out var message) ? message : null;
    }

    public Task CancelAsync(string requestId, string correlationId, CancellationToken cancellationToken)
        => SendAsync(new IpcEnvelope { Kind = IpcMessageKind.Cancel, MessageId = Guid.NewGuid().ToString("N"), RequestId = requestId, CorrelationId = correlationId, Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { requestId, correlationId }, ProtocolJson.Options) }, cancellationToken);

    public ValueTask DisposeAsync() { Volatile.Write(ref _connected, 0); _available.Dispose(); return default; }
}
