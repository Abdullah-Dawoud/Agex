using Agex.Core.Platform;

namespace Agex.Core.Agents;

/// <summary>Status vocabulary shown for every agent, tool and integration.</summary>
public enum AgentStatus
{
    /// <summary>AGEX has an adapter, the tool is installed and its health check passed.</summary>
    Supported,
    /// <summary>Available but not yet checked (health check pending or skipped).</summary>
    Available,
    /// <summary>AGEX has an adapter but the tool is not installed.</summary>
    NotInstalled,
    /// <summary>Installed, but AGEX has no adapter for it.</summary>
    DetectedUnsupported,
    /// <summary>The tool or integration does not exist for this operating system.</summary>
    PlatformUnsupported,
    /// <summary>Installed but the user must sign in first.</summary>
    AuthRequired,
    /// <summary>Installed but the health check failed.</summary>
    Broken,
    Unknown,
}

public static class AgentStatusText
{
    public static string Label(AgentStatus status) => status switch
    {
        AgentStatus.Supported => "Ready",
        AgentStatus.Available => "Available",
        AgentStatus.NotInstalled => "Not installed",
        AgentStatus.DetectedUnsupported => "Detected - not integrated",
        AgentStatus.PlatformUnsupported => "Not available on this system",
        AgentStatus.AuthRequired => "Sign-in required",
        AgentStatus.Broken => "Not working",
        _ => "Unknown",
    };

    /// <summary>Machine-readable code used in reports and the CLI.</summary>
    public static string Code(AgentStatus status) => status switch
    {
        AgentStatus.Supported => "SUPPORTED",
        AgentStatus.Available => "AVAILABLE",
        AgentStatus.NotInstalled => "NOT_INSTALLED",
        AgentStatus.DetectedUnsupported => "DETECTED_UNSUPPORTED",
        AgentStatus.PlatformUnsupported => "PLATFORM_UNSUPPORTED",
        AgentStatus.AuthRequired => "AUTH_REQUIRED",
        AgentStatus.Broken => "BROKEN",
        _ => "UNKNOWN",
    };
}

public enum Capability
{
    ReadFiles, WriteFiles, RunCommands, WebResearch, Browser, CodeReview, Testing, Planning, Debugging, Mcp, Images, Documents, Skills,
}

/// <summary>Where the prompt and project content are processed.</summary>
public enum PrivacyKind { Local, Cloud, Mixed, Unknown }

public enum AdapterStability { Stable, Beta }

public sealed record AgentDetection
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public AgentStatus Status { get; init; }
    public string? Path { get; init; }
    public string Version { get; init; } = "";
    public string Reason { get; init; } = "";
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Token and cost figures exactly as the agent reported them. Null fields mean "not reported".</summary>
public sealed record UsageReport
{
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? CachedInputTokens { get; init; }
    public decimal? CostUsd { get; init; }
    public string Source { get; init; } = "";

    public static UsageReport? Combine(UsageReport? a, UsageReport? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        static long? Add(long? x, long? y) => x is null && y is null ? null : (x ?? 0) + (y ?? 0);
        return new UsageReport
        {
            InputTokens = Add(a.InputTokens, b.InputTokens), OutputTokens = Add(a.OutputTokens, b.OutputTokens),
            CachedInputTokens = Add(a.CachedInputTokens, b.CachedInputTokens),
            CostUsd = a.CostUsd is null && b.CostUsd is null ? null : (a.CostUsd ?? 0) + (b.CostUsd ?? 0),
            Source = a.Source == b.Source ? a.Source : "combined",
        };
    }
}

/// <summary>
/// An MCP server made available to an agent for one run: either a local
/// command (stdio) or a hosted endpoint (<see cref="Url"/>). Secret values are
/// passed only through environment variables named here.
/// </summary>
public sealed record McpServerSpec(string Name, string Command, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> SecretEnvironment, string? Url = null, string? BearerEnvironmentVariable = null, string SkillId = "")
{
    public bool IsRemote => !string.IsNullOrEmpty(Url);
}

/// <summary>Instructions from an enabled skill, offered to the agent.</summary>
public sealed record SkillContext(string Id, string Name, string Description, string Folder, string? InlineInstructions);

public sealed class AgentInvocation
{
    public required string Prompt { get; init; }
    public required string WorkingDirectory { get; init; }
    public bool AllowWrites { get; init; }
    public bool AllowCommands { get; init; } = true;
    public string? Model { get; init; }
    public string? Effort { get; init; }
    /// <summary>Optional OpenAI-compatible endpoint (only adapters that declare support use it).</summary>
    public ProviderEndpoint? Provider { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);
    public IReadOnlyList<SkillContext> Skills { get; init; } = [];
    public IReadOnlyList<McpServerSpec> McpServers { get; init; } = [];
    /// <summary>Files the user attached, already copied and converted by AGEX.</summary>
    public IReadOnlyList<Agex.Core.Attachments.Attachment> Attachments { get; init; } = [];
    public string Label { get; init; } = "";
    /// <summary>Explicit, user-visible progress: tool steps and short status lines. Never model reasoning.</summary>
    public Action<AgentActivity>? OnActivity { get; init; }
    public Action<int>? OnProcessStarted { get; init; }
}

public enum ActivityKind { Status, ToolStarted, ToolFinished, Output }

public sealed record AgentActivity(ActivityKind Kind, string Text);

public enum RunOutcome { Ok, NoResult, Failed, TimedOut, Cancelled, StartFailed, AuthRequired, Unavailable }

public sealed record AgentRunResult
{
    public RunOutcome Outcome { get; init; }
    public string Text { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>True when the failure happened before meaningful work, so another agent may retry.</summary>
    public bool FallbackEligible { get; init; }
    public UsageReport? Usage { get; init; }
    public string CommandLine { get; init; } = "";
    public int Pid { get; init; }
    public int? ExitCode { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Success => Outcome == RunOutcome.Ok && !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// Connects AGEX to one AI agent or runtime through a fixed, documented
/// interface. Adapters never run arbitrary commands.
/// </summary>
public interface IAgentAdapter
{
    string Id { get; }
    string Name { get; }
    string Provider { get; }
    string Description { get; }
    AdapterStability Stability { get; }
    IReadOnlySet<Capability> Capabilities { get; }
    /// <summary>True when the adapter can let the agent edit project files.</summary>
    bool CanWriteFiles { get; }
    /// <summary>Operating systems where the agent itself exists.</summary>
    IReadOnlySet<OsKind> SupportedPlatforms { get; }
    /// <summary>How many runs of this agent may execute at the same time.</summary>
    int MaxConcurrentRuns { get; }
    PrivacyKind PrivacyFor(string? model);
    /// <summary>Where the data goes, in plain words (e.g. "OpenAI cloud").</summary>
    string DataDestination(string? model);

    AgentDetection Detect(IPlatformService platform);
    Task<AgentDetection> CheckHealthAsync(AgentDetection detection, CancellationToken cancellationToken);
    /// <summary>Official install and sign-in strategy for this agent.</summary>
    AgentSetupInfo Setup { get; }
    /// <summary>False when the sign-in check could start a sign-in itself; AGEX then runs it only when the user asks.</summary>
    bool PassiveAuthCheck { get; }
    /// <summary>Checks sign-in without using model quota (status command or local markers only).</summary>
    Task<AuthCheck> CheckAuthAsync(AgentDetection detection, CancellationToken cancellationToken);
    /// <summary>Models the agent itself reports; <see cref="ModelDiscoveryStatus.Unavailable"/> when it has no reliable list.</summary>
    Task<ModelDiscovery> GetModelsAsync(AgentDetection detection, CancellationToken cancellationToken);
    Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken);
}
