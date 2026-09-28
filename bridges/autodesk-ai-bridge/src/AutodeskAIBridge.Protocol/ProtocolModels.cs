using System.Text.Json;
using AutodeskAIBridge.Core;

namespace AutodeskAIBridge.Protocol;

public static class ProtocolConstants
{
    public const string Version = "1.0";
    public const string ServerName = "Autodesk AI Bridge";
    public const int MaxMessageBytes = 4 * 1024 * 1024;
}

/// <summary>Versioned request sent from MCP host to Autodesk plugin.</summary>
public sealed record BridgeRequest
{
    public string ProtocolVersion { get; init; } = ProtocolConstants.Version;
    public required string RequestId { get; init; }
    public required string CorrelationId { get; init; }
    public required AutodeskTarget Target { get; init; }
    public required string Operation { get; init; }
    public Dictionary<string, JsonElement> Parameters { get; init; } = [];
    public BridgeRequestOptions Options { get; init; } = new();
}

/// <summary>Request execution controls.</summary>
public sealed record BridgeRequestOptions
{
    public bool DryRun { get; init; }
    public int TimeoutMilliseconds { get; init; } = 30_000;
}

/// <summary>Versioned response returned by an Autodesk plugin.</summary>
public sealed record BridgeResponse
{
    public string ProtocolVersion { get; init; } = ProtocolConstants.Version;
    public required string RequestId { get; init; }
    public bool Success { get; init; }
    public object? Data { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public ChangeSetDto Changes { get; init; } = ChangeSetDto.Empty;
    public TimingDto Timing { get; init; } = new();
    public BridgeErrorDto? Error { get; init; }
}

public sealed record ChangeSetDto(
    IReadOnlyList<object> Created,
    IReadOnlyList<object> Modified,
    IReadOnlyList<object> Deleted)
{
    public static ChangeSetDto Empty { get; } = new([], [], []);
}

public sealed record TimingDto
{
    public long DurationMilliseconds { get; init; }
}

public sealed record BridgeErrorDto(
    string Code,
    string Message,
    bool Recoverable,
    object? Details = null,
    IReadOnlyList<string>? Suggestions = null);

/// <summary>MCP JSON-RPC request.</summary>
public sealed record JsonRpcRequest
{
    public string Jsonrpc { get; init; } = "2.0";
    public required string Id { get; init; }
    public required string Method { get; init; }
    public JsonElement Params { get; init; }
}

/// <summary>MCP JSON-RPC notification.</summary>
public sealed record JsonRpcNotification
{
    public string Jsonrpc { get; init; } = "2.0";
    public required string Method { get; init; }
    public JsonElement Params { get; init; }
}

/// <summary>MCP JSON-RPC response.</summary>
public sealed record JsonRpcResponse
{
    public string Jsonrpc { get; init; } = "2.0";
    public required string Id { get; init; }
    public object? Result { get; init; }
    public JsonRpcError? Error { get; init; }
}

public sealed record JsonRpcError(int Code, string Message, object? Data = null);

public sealed record McpInitializeResult(
    string ProtocolVersion,
    McpServerCapabilities Capabilities,
    McpServerInfo ServerInfo);

public sealed record McpServerCapabilities(McpToolsCapability Tools,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] McpResourcesCapability? Resources);
public sealed record McpToolsCapability(bool ListChanged = false);
public sealed record McpResourcesCapability(bool Subscribe = false, bool ListChanged = false);
public sealed record McpServerInfo(string Name, string Version);

public sealed record McpTool(
    string Name,
    string Description,
    IReadOnlyDictionary<string, object?> InputSchema,
    string RiskCategory = "ReadOnly",
    string? TargetProduct = null,
    bool RequiresConnection = false,
    bool RequiresActiveDocument = false,
    string ImplementationStatus = "SOURCE_IMPLEMENTED_RUNTIME_UNVERIFIED");

/// <summary>Converts protocol JSON into safe plain values.</summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static object? ToPlainValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => ToPlainValue(p.Value)),
            JsonValueKind.Array => value.EnumerateArray().Select(ToPlainValue).ToArray(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    public static Dictionary<string, object?> ToPlainObject(JsonElement value)
        => value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().ToDictionary(p => p.Name, p => ToPlainValue(p.Value))
            : new(StringComparer.OrdinalIgnoreCase);
}
