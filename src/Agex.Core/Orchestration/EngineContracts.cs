using Agex.Core.Agents;
using Agex.Core.Sessions;
using Agex.Core.Settings;

namespace Agex.Core.Orchestration;

/// <summary>AllowForSession also switches the approval mode to Trust this session.</summary>
public enum ApprovalDecision { Allow, AllowAndTrust, Deny, AllowForSession }

public sealed record ApprovalRequest(string Title, string Detail, IReadOnlyList<string> Agents);

/// <summary>What the desktop app or CLI provides so the engine can involve the user.</summary>
public interface IEngineHost
{
    /// <summary>Asked once per request before agents may change files (unless the project is trusted).</summary>
    Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken);
    /// <summary>Returns the user's answer, or null when the user declines to answer.</summary>
    Task<string?> AskUserAsync(PendingQuestion question, CancellationToken cancellationToken);
}

public sealed class RequestOptions
{
    public required string Project { get; init; }
    public required string Request { get; init; }
    public required IReadOnlyList<TeamMember> Members { get; init; }
    public required TeamMember Leader { get; init; }
    public RoutingPreset Routing { get; init; } = RoutingPreset.Automatic;
    public string RoutingGuidance { get; init; } = "";
    public string Team { get; init; } = "";
    public IReadOnlyList<SkillContext> Skills { get; init; } = [];
    public IReadOnlyList<McpServerSpec> McpServers { get; init; } = [];
    /// <summary>Skills pinned to particular agents (skill id to agent ids). A skill not listed goes to every compatible agent.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> SkillAgents { get; init; } = new Dictionary<string, IReadOnlySet<string>>();
    public IReadOnlyList<Agex.Core.Attachments.Attachment> Attachments { get; init; } = [];
    /// <summary>Brief of the job team in use (goal, workflow, approval rules). Empty when none.</summary>
    public string TeamBrief { get; init; } = "";
    public EfficiencyMode Efficiency { get; init; } = EfficiencyMode.Balanced;
    public string ProjectInstructions { get; init; } = "";
    public IReadOnlyList<string> IgnoredFolders { get; init; } = [];
    public bool AskBeforeWrites { get; init; } = true;
    public bool AllowCommands { get; init; } = true;
    public bool SnapshotBeforeWrites { get; init; } = true;
    public int MaxParallel { get; init; } = 3;
    public TimeSpan AgentTimeout { get; init; } = TimeSpan.FromMinutes(15);
    public int MaxRounds { get; init; } = 6;
    public int MaxAutoFallbacks { get; init; } = 1;
    /// <summary>Summary of an earlier session this request continues.</summary>
    public string PreviousContext { get; init; } = "";
    public string ContinuedFrom { get; init; } = "";
    public string ClonedFrom { get; init; } = "";
    /// <summary>How to handle the request (chat, question, plan, build) and what it needs. Null = classify as Build.</summary>
    public RequestIntent? Intent { get; init; }
    public ChatMode ChosenMode { get; init; } = ChatMode.Build;
    public ApprovalMode ApprovalMode { get; init; } = ApprovalMode.Smart;
    public PermissionSettings Permissions { get; init; } = new();
    /// <summary>Which agents and tools can do what the request needs.</summary>
    public CapabilityPlan Capabilities { get; init; } = new();
    /// <summary>True when the user chose the skills for this request; otherwise AGEX sends only relevant ones.</summary>
    public bool SkillsChosen { get; init; }
    /// <summary>Address where AGEX serves the project on this computer (empty when not needed).</summary>
    public string LocalUrl { get; init; } = "";
}

public enum AgentWorkState { Idle, Planning, Working, Reviewing, Answering, Done, Failed, Cancelled }

/// <summary>Live state of one agent for the side panel.</summary>
public sealed record AgentLiveState(string AgentId, string Name, AgentWorkState State, string TaskId, string Text, int Pid, DateTimeOffset Since);
