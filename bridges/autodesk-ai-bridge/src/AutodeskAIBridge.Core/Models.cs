namespace AutodeskAIBridge.Core;

/// <summary>Risk class for an exposed bridge operation.</summary>
public enum RiskCategory
{
    ReadOnly,
    ModelEdit,
    FileWrite,
    Destructive
}

public enum ImplementationStatus
{
    Supported,
    SourceImplementedRuntimeUnverified,
    BlockedByApi,
    Unsupported
}

/// <summary>User-controlled permission gates for bridge operations.</summary>
public sealed record PermissionOptions
{
    public bool Read { get; init; } = true;
    public bool Edit { get; init; } = true;
    public bool Destructive { get; init; } = false;
    public bool FileWrite { get; init; } = false;
    public bool SafeMode { get; init; }
    public int MaxElementsPerDeleteWithoutConfirmation { get; init; } = 20;
}

/// <summary>Tool metadata used for MCP discovery and policy checks.</summary>
public sealed record ToolDescriptor(
    string Name,
    string Description,
    RiskCategory Risk,
    bool RequiresAutodesk,
    IReadOnlyDictionary<string, object?> InputSchema,
    string? TargetProduct = null,
    bool RequiresConnection = false,
    bool RequiresActiveDocument = false,
    ImplementationStatus Implementation = ImplementationStatus.SourceImplementedRuntimeUnverified);

/// <summary>Structured target for one Autodesk application instance.</summary>
public sealed record AutodeskTarget(
    string Product,
    string InstanceId,
    string? DocumentId = null);

/// <summary>Connected Autodesk application identity.</summary>
public sealed record AutodeskInstanceInfo(
    string Product,
    string InstanceId,
    string ProductVersion,
    string? ActiveDocumentId,
    string? ActiveDocumentName,
    bool ApiReady,
    IReadOnlyList<AutodeskDocumentInfo> Documents,
    int? ProcessId = null,
    DateTimeOffset? ConnectedAt = null,
    DateTimeOffset? LastHeartbeat = null,
    IReadOnlyList<string>? SupportedCapabilities = null)
{
    public IReadOnlyList<string> EffectiveCapabilities => SupportedCapabilities ?? [];
}

/// <summary>Open Autodesk document identity.</summary>
public sealed record AutodeskDocumentInfo(
    string DocumentId,
    string Name,
    string Path,
    bool IsActive,
    bool IsReadOnly,
    bool IsModified);

/// <summary>Context supplied to tool execution.</summary>
public sealed record ToolCallContext(
    string RequestId,
    string CorrelationId,
    AutodeskTarget? Target,
    CancellationToken CancellationToken,
    bool DryRun = false);

/// <summary>Structured changed-object summary.</summary>
public sealed record ChangeSet(
    IReadOnlyList<object> Created,
    IReadOnlyList<object> Modified,
    IReadOnlyList<object> Deleted)
{
    public static ChangeSet Empty { get; } = new([], [], []);
}

/// <summary>Tool execution result.</summary>
public sealed record ToolResult(
    bool Success,
    object? Data = null,
    IReadOnlyList<string>? Warnings = null,
    ChangeSet? Changes = null,
    BridgeError? Error = null,
    TimeSpan? Duration = null)
{
    public IReadOnlyList<string> EffectiveWarnings => Warnings ?? [];
    public ChangeSet EffectiveChanges => Changes ?? ChangeSet.Empty;

    public static ToolResult Ok(object? data = null, ChangeSet? changes = null, IReadOnlyList<string>? warnings = null)
        => new(true, data, warnings, changes);

    public static ToolResult Fail(string code, string message, bool recoverable = true, object? details = null)
        => new(false, Error: new BridgeError(code, message, recoverable, details));
}

/// <summary>Stable machine-readable bridge error.</summary>
public sealed record BridgeError(
    string Code,
    string Message,
    bool Recoverable = true,
    object? Details = null,
    IReadOnlyList<string>? Suggestions = null);

/// <summary>Adapter for one connected Autodesk product instance.</summary>
public interface IAutodeskAdapter
{
    string Product { get; }
    AutodeskInstanceInfo GetInstanceInfo();
    Task<ToolResult> ExecuteAsync(string operation, IReadOnlyDictionary<string, object?> parameters, ToolCallContext context);
}

/// <summary>Registry for connected Autodesk product adapters.</summary>
public interface IAutodeskInstanceRegistry
{
    IReadOnlyList<AutodeskInstanceInfo> ListInstances();
    IAutodeskAdapter? Resolve(AutodeskTarget target);
    AutodeskInstanceInfo? GetActive(string product);
}

/// <summary>Optional lifecycle contract for adapters backed by a live plugin session.</summary>
public interface IAutodeskSessionRegistry : IAutodeskInstanceRegistry
{
    void Register(IAutodeskAdapter adapter);
    bool Unregister(string product, string instanceId);
    int ExpireStale(DateTimeOffset now);
    void MarkHeartbeat(string product, string instanceId, AutodeskInstanceInfo? refreshedInfo = null);
}

/// <summary>Bridge tool implementation.</summary>
public interface IBridgeTool
{
    ToolDescriptor Descriptor { get; }
    Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> arguments, ToolCallContext context);
}

/// <summary>Clock abstraction for deterministic tests.</summary>
public interface IBridgeClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>System clock implementation.</summary>
public sealed class SystemBridgeClock : IBridgeClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
