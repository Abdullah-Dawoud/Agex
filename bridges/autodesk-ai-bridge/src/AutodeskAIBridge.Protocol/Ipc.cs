using System.Text.Json;

namespace AutodeskAIBridge.Protocol;

public enum IpcMessageKind
{
    AuthHello,
    AuthAccepted,
    Request,
    Response,
    Cancel,
    Heartbeat,
    Goodbye,
    Error
}

/// <summary>Authenticated local IPC handshake.</summary>
public sealed record IpcAuthHello(
    string ProtocolVersion,
    string ClientInstanceId,
    string Product,
    string ProductVersion,
    string Nonce,
    string TokenProof,
    int? ProcessId = null,
    string? ActiveDocumentId = null,
    string? ActiveDocumentName = null,
    IReadOnlyList<AutodeskAIBridge.Core.AutodeskDocumentInfo>? Documents = null,
    IReadOnlyList<string>? SupportedCapabilities = null);

public sealed record IpcAuthAccepted(
    string ProtocolVersion,
    string ServerInstanceId,
    string Nonce,
    string SessionId,
    DateTimeOffset ExpiresAt,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record IpcErrorPayload(string Code, string Message, bool Recoverable = true, object? Details = null);

/// <summary>Framed named-pipe message envelope.</summary>
public sealed record IpcEnvelope
{
    public string ProtocolVersion { get; init; } = ProtocolConstants.Version;
    public required IpcMessageKind Kind { get; init; }
    public required string MessageId { get; init; }
    public string? RequestId { get; init; }
    public string? CorrelationId { get; init; }
    public JsonElement Payload { get; init; }
}

/// <summary>Named-pipe endpoint settings. Pipe name never exposes a network listener.</summary>
public sealed record NamedPipeOptions
{
    public required string PipeName { get; init; }
    public required string SharedSecret { get; init; }
    public int MaxMessageBytes { get; init; } = ProtocolConstants.MaxMessageBytes;
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxReconnectAttempts { get; init; } = 5;
}

/// <summary>Transport abstraction for authenticated local IPC.</summary>
public interface IAuthenticatedIpcTransport : IAsyncDisposable
{
    string SessionId { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task SendAsync(IpcEnvelope message, CancellationToken cancellationToken);
    Task<IpcEnvelope?> ReceiveAsync(CancellationToken cancellationToken);
    Task CancelAsync(string requestId, string correlationId, CancellationToken cancellationToken);
}

/// <summary>Shared-secret proof helper. Never logs secret or proof.</summary>
public static class IpcAuthentication
{
    public static string CreateProof(string sharedSecret, string nonce)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(sharedSecret));
        return BitConverter.ToString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(nonce))).Replace("-", string.Empty);
    }

    public static bool Verify(string sharedSecret, string nonce, string? proof)
    {
        if (string.IsNullOrEmpty(proof)) return false;
        var expected = CreateProof(sharedSecret, nonce);
        return FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(proof));
    }

    /// <summary>Constant-time comparison (also available on .NET Framework, where CryptographicOperations does not exist).</summary>
    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        var difference = 0;
        for (var index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
        return difference == 0;
    }
}

/// <summary>Single framing and validation boundary for newline-delimited IPC.</summary>
public static class IpcFrameCodec
{
    public static string Serialize(IpcEnvelope envelope, int maxBytes = ProtocolConstants.MaxMessageBytes)
    {
        var line = JsonSerializer.Serialize(envelope, ProtocolJson.Options);
        if (System.Text.Encoding.UTF8.GetByteCount(line) > maxBytes)
            throw new InvalidDataException("IPC message exceeds configured maximum size.");
        return line;
    }

    public static IpcEnvelope Deserialize(string line, int maxBytes = ProtocolConstants.MaxMessageBytes)
    {
        if (string.IsNullOrWhiteSpace(line) || System.Text.Encoding.UTF8.GetByteCount(line) > maxBytes)
            throw new InvalidDataException("IPC message exceeds configured maximum size.");
        try
        {
            var envelope = JsonSerializer.Deserialize<IpcEnvelope>(line, ProtocolJson.Options)
                ?? throw new InvalidDataException("IPC message is invalid.");
            if (string.IsNullOrWhiteSpace(envelope.MessageId) || !Enum.IsDefined(typeof(IpcMessageKind), envelope.Kind))
                throw new InvalidDataException("IPC envelope is invalid.");
            if (!string.Equals(envelope.ProtocolVersion, ProtocolConstants.Version, StringComparison.Ordinal))
                throw new InvalidDataException("IPC protocol version is unsupported.");
            return envelope;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("IPC message is malformed.", exception);
        }
    }
}
