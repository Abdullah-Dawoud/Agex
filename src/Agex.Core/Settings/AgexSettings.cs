namespace Agex.Core.Settings;

public enum ThemeChoice { System, Light, Dark }

/// <summary>Plain-language routing presets. Custom exposes exact weights under Advanced.</summary>
public enum RoutingPreset { Automatic, Balanced, Fast, BestQuality, LowCost, LocalOnly, Custom }

/// <summary>
/// How much context AGEX gives agents. Never changes silently: the chosen mode is
/// shown on Home and in each session's timeline.
/// </summary>
public enum EfficiencyMode
{
    /// <summary>Full project evidence and higher default reasoning effort.</summary>
    MaximumQuality,
    /// <summary>The default.</summary>
    Balanced,
    /// <summary>Smaller project evidence, shorter earlier-results, brief answers, lower default reasoning effort.</summary>
    SaveTokens,
    /// <summary>Balanced context, and work goes to local models (Ollama) whenever one can do it.</summary>
    LocalFirst,
}

/// <summary>
/// User settings. Everything here is portable between computers: no machine
/// identifiers, no secrets. Secrets live in the OS secure store; per-machine
/// paths (recent projects) are kept but are optional on import.
/// </summary>
public sealed class AgexSettings
{
    public const int CurrentSchema = 5;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public bool FirstRunComplete { get; set; }
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    /// <summary>1.0 = default text size; 0.85 to 1.6 supported.</summary>
    public double TextScale { get; set; } = 1.0;
    public List<string> EnabledAgents { get; set; } = ["codex", "antigravity"];
    /// <summary>"auto" or an agent id.</summary>
    public string Leader { get; set; } = "auto";
    public RoutingPreset Routing { get; set; } = RoutingPreset.Automatic;
    /// <summary>Only used with <see cref="RoutingPreset.Custom"/>: relative share of tasks per agent id.</summary>
    public Dictionary<string, int> CustomShares { get; set; } = new();
    /// <summary>Order used by "Best quality". Users can change it; AGEX does not claim a ranking of its own.</summary>
    public List<string> QualityOrder { get; set; } = ["codex", "claude-code", "antigravity", "gemini-cli", "ollama"];
    public Dictionary<string, AgentOptions> AgentOptions { get; set; } = new()
    {
        ["codex"] = new AgentOptions(),
        ["antigravity"] = new AgentOptions(),
        ["claude-code"] = new AgentOptions(),
        ["gemini-cli"] = new AgentOptions(),
        ["ollama"] = new AgentOptions(),
    };
    public int MaxParallelTasks { get; set; } = 3;
    public EfficiencyMode Efficiency { get; set; } = EfficiencyMode.Balanced;
    /// <summary>Model endpoints the user added (OpenAI-compatible). Keys live in the secure store.</summary>
    public List<Agex.Core.Agents.ProviderProfile> Providers { get; set; } = [];
    /// <summary>Job team in use (id of a team template), or empty.</summary>
    public string ActiveJobTeam { get; set; } = "";
    /// <summary>Editor used for "Open in editor" (an id from discovery, e.g. "vscode"), or empty for the system default.</summary>
    public string PreferredEditor { get; set; } = "";
    /// <summary>Right-side workspace panel: width in pixels and whether it is open.</summary>
    public double WorkspacePanelWidth { get; set; } = 380;
    public bool WorkspacePanelOpen { get; set; } = true;
    /// <summary>Per-agent skill assignment (agent id to skill ids). A skill assigned to no agent goes to every compatible agent.</summary>
    public Dictionary<string, List<string>> AgentSkills { get; set; } = new();
    /// <summary>Skills pinned to a job team (team id to skill ids): used whenever the team is in use.</summary>
    public Dictionary<string, List<string>> TeamSkills { get; set; } = new();
    /// <summary>Saved skill selections for the composer.</summary>
    public List<Agex.Core.Skills.SkillProfile> SkillProfiles { get; set; } = [];
    /// <summary>External MCP servers the user chose to leave where they are ("Use as-is"), as "source|name".</summary>
    public List<string> AcknowledgedMcp { get; set; } = [];
    /// <summary>Recommendations the user dismissed (not shown again).</summary>
    public List<string> DismissedTips { get; set; } = [];
    public int AgentTimeoutMinutes { get; set; } = 15;
    public List<AgentTeam> Teams { get; set; } =
    [
        new() { Id = "coding", Name = "Coding team", Agents = ["codex", "antigravity"], Leader = "codex" },
        new() { Id = "research", Name = "Research team", Agents = ["claude-code", "gemini-cli"], Leader = "claude-code" },
        new() { Id = "local", Name = "Local team", Agents = ["ollama"], Leader = "ollama" },
    ];
    /// <summary>Empty = use <see cref="EnabledAgents"/> directly.</summary>
    public string ActiveTeam { get; set; } = "";
    public ApprovalPolicy Approvals { get; set; } = new();
    public PermissionSettings Permissions { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public PrivacySettings Privacy { get; set; } = new();
    public SessionSettings Sessions { get; set; } = new();
    public bool CheckForUpdates { get; set; } = true;
    public bool AutoUpdateSkills { get; set; }
    public bool StartWithSystem { get; set; }
    public bool LaunchMinimized { get; set; }
    public List<string> RecentProjects { get; set; } = [];
    public string LastProject { get; set; } = "";
}

public sealed class AgentOptions
{
    /// <summary>Model id; empty means Auto (the agent's own default).</summary>
    public string Model { get; set; } = "";
    /// <summary>True when the id was typed under Advanced; AGEX then never replaces it with Auto.</summary>
    public bool CustomModel { get; set; }
    /// <summary>Optional model endpoint (id of a provider in <see cref="AgexSettings.Providers"/>); empty = the agent's own service.</summary>
    public string ProviderId { get; set; } = "";
    public string Effort { get; set; } = "";
    /// <summary>Sampling temperature, only for agents whose API accepts it (Ollama). Null = the model's default.</summary>
    public double? Temperature { get; set; }
    /// <summary>Context window in tokens, only for agents that accept it (Ollama num_ctx). Null = the model's default.</summary>
    public int? ContextWindow { get; set; }
    /// <summary>Codex: let it edit files (workspace-write sandbox). Other agents: allow file edits at all.</summary>
    public bool AllowWrites { get; set; } = true;
}

public sealed class AgentTeam
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Agents { get; set; } = [];
    public string Leader { get; set; } = "auto";
}

/// <summary>How often AGEX asks before agents act.</summary>
public enum ApprovalMode
{
    /// <summary>Ask before every meaningful action (changing files, commands, browser, computer control).</summary>
    AskEveryTime,
    /// <summary>Ask only for sensitive actions, and before changes that cannot be undone.</summary>
    Smart,
    /// <summary>Allow everything turned on below for this AGEX session. Sensitive actions still ask. Resets to Smart when AGEX restarts.</summary>
    TrustSession,
}

/// <summary>What agents may do at all, in plain words. Turned-off capabilities are never given to agents.</summary>
public sealed class PermissionSettings
{
    public bool ReadFiles { get; set; } = true;
    public bool WriteProject { get; set; } = true;
    public bool RunCommands { get; set; } = true;
    public bool Browser { get; set; } = true;
    public bool ComputerControl { get; set; }
    public bool Network { get; set; } = true;
    public bool McpTools { get; set; } = true;
    /// <summary>Sending email, messages or posts. Even when on, each send asks first.</summary>
    public bool ExternalCommunication { get; set; }
    /// <summary>Deleting significant data. Even when on, each deletion asks first.</summary>
    public bool DestructiveActions { get; set; }
}

public sealed class ApprovalPolicy
{
    public ApprovalMode Mode { get; set; } = ApprovalMode.Smart;
    /// <summary>Ask once per request before agents may change files (skipped for trusted projects).</summary>
    public bool AskBeforeWrites { get; set; } = true;
    /// <summary>When false, agents run without shell commands where the agent supports that restriction.</summary>
    public bool AllowCommands { get; set; } = true;
    /// <summary>Take a Git snapshot before agents change files (Git projects only).</summary>
    public bool SnapshotBeforeWrites { get; set; } = true;
}

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;
    public bool OnComplete { get; set; } = true;
    public bool OnFailure { get; set; } = true;
    public bool OnInputNeeded { get; set; } = true;
    public bool OnApproval { get; set; } = true;
    /// <summary>Only notify about requests that ran at least this long.</summary>
    public int MinimumSeconds { get; set; } = 20;
}

public sealed class PrivacySettings
{
    /// <summary>Tell the user, once per project, which cloud services will receive project data.</summary>
    public bool ExplainCloudUse { get; set; } = true;
}

public sealed class SessionSettings
{
    /// <summary>0 = keep forever.</summary>
    public int RetentionDays { get; set; } = 90;
    public int MaxSessions { get; set; } = 500;
}

/// <summary>Per-project preferences, stored locally in the AGEX data folder (never in the project).</summary>
public sealed class ProjectProfile
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Empty = use the global setting.</summary>
    public List<string> PreferredAgents { get; set; } = [];
    public string Team { get; set; } = "";
    public RoutingPreset? Routing { get; set; }
    /// <summary>Skill ids enabled for this project; null = all globally enabled skills.</summary>
    public List<string>? Skills { get; set; }
    public bool? AllowWrites { get; set; }
    public bool Trusted { get; set; }
    public bool CloudUseAcknowledged { get; set; }
    public List<string> IgnoredFolders { get; set; } = [];
    /// <summary>Extra instructions sent to every agent for this project.</summary>
    public string Instructions { get; set; } = "";
    /// <summary>Non-secret environment variables for agent processes in this project.</summary>
    public Dictionary<string, string> Environment { get; set; } = new();
    public DateTimeOffset LastOpened { get; set; }
}

/// <summary>Crash-recovery state, saved while the app runs.</summary>
public sealed class AppState
{
    public string DraftRequest { get; set; } = "";
    public string Project { get; set; } = "";
    public List<string> SelectedAgents { get; set; } = [];
    public string Team { get; set; } = "";
    /// <summary>Session that was running when the app last stopped; offered for review, never restarted automatically.</summary>
    public string UnfinishedSession { get; set; } = "";
    public bool CleanExit { get; set; } = true;
    public string LastPage { get; set; } = "home";
    /// <summary>Composer mode (Auto, Ask, Plan, Build).</summary>
    public string ChatMode { get; set; } = "Auto";
    /// <summary>Home's conversation list is open.</summary>
    public bool HistoryOpen { get; set; } = true;
    public DateTimeOffset LastUpdateCheck { get; set; }
}
