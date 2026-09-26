using System.Runtime.InteropServices;
using System.Text;
using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Core.Platform;
using Agex.Core.Projects;
using Agex.Core.Runtime;
using Agex.Core.Sessions;
using Agex.Core.Settings;
using Agex.Core.Skills;
using Agex.Core.Updates;

namespace Agex.Core;

public sealed record RepairAction(string Area, string Status, string Detail);

/// <summary>
/// Composition root shared by the desktop app and the CLI: creates every
/// service once, runs startup checks, and builds requests from settings.
/// </summary>
public sealed class AgexCore : IAgentStatistics
{
    private readonly Dictionary<string, double> _medians = new(StringComparer.OrdinalIgnoreCase);

    public AgexCore(IPlatformService? platform = null, bool safeMode = false)
    {
        Platform = platform ?? PlatformFactory.Create();
        SafeMode = safeMode;
        Log = new AgexLog(Platform.Paths.LogsRoot);
        Runner = new ProcessRunner(Platform, Log);
        SettingsStore = new SettingsStore(Platform.Paths, Log);
        Settings = SettingsStore.Load();
        Registry = AgentRegistry.CreateDefault(Platform, Runner, () => Settings.AgentOptions.GetValueOrDefault("ollama")?.Model);
        Sessions = new SessionStore(Platform.Paths.Sessions, Log);
        Skills = new SkillManager(Platform, Log, safeMode);
        Git = new GitService(Platform, Runner);
        Discovery = new Discovery(Platform, Registry, Log);
        Updates = new UpdateService(Platform, Log);
        Providers = new ProviderService(Platform, Log);
        Models = new ModelCatalog(Platform, Registry, Log, ProviderFor, Providers);
        Installer = new AgentInstaller(Platform, Runner, Log);
        Attachments = new Agex.Core.Attachments.AttachmentService(Platform, Runner, Log);
        Teams = new Agex.Core.Teams.JobTeamService(Platform, Skills, Registry, () => Settings.EnabledAgents);
        Connections = new Agex.Core.Connections.ConnectionService(Platform, Skills, Registry, () => Settings.EnabledAgents, () => Settings.PreferredEditor);
        McpRegistry = new Agex.Core.Connections.McpRegistry(Log);
    }

    public IPlatformService Platform { get; }
    public bool SafeMode { get; }
    public AgexLog Log { get; }
    public ProcessRunner Runner { get; }
    private Agex.Core.Connections.McpProbe? _probe;
    /// <summary>Tests MCP servers with the MCP handshake (no tool is called).</summary>
    public Agex.Core.Connections.McpProbe McpProbe => _probe ??= new(Platform, Runner, Log);
    public SettingsStore SettingsStore { get; }
    public AgexSettings Settings { get; private set; }
    public AgentRegistry Registry { get; }
    public SessionStore Sessions { get; }
    public SkillManager Skills { get; }
    public GitService Git { get; }
    public Discovery Discovery { get; }
    public UpdateService Updates { get; }
    /// <summary>Model lists reported by each agent, cached.</summary>
    public ModelCatalog Models { get; }
    /// <summary>Installs agents through their official npm packages after the user confirms.</summary>
    public AgentInstaller Installer { get; }
    public ProviderService Providers { get; }
    public Agex.Core.Attachments.AttachmentService Attachments { get; }
    /// <summary>Job teams: what each needs and what is ready on this computer.</summary>
    public Agex.Core.Teams.JobTeamService Teams { get; }
    /// <summary>Programs, services, agents and MCP servers: what is connected and the next step for each.</summary>
    public Agex.Core.Connections.ConnectionService Connections { get; }
    public Agex.Core.Connections.McpRegistry McpRegistry { get; }

    /// <summary>The provider profile chosen for an agent, if the adapter supports providers.</summary>
    public ProviderProfile? ProviderFor(string agentId) =>
        Registry.Get(agentId) is CodexAdapter && Settings.AgentOptions.GetValueOrDefault(agentId)?.ProviderId is { Length: > 0 } id
            ? Settings.Providers.FirstOrDefault(provider => provider.Id == id) : null;
    public List<string> StartupNotices { get; } = [];

    /// <summary>Fast startup work (no network, no agent processes). The UI can open right after.</summary>
    public void Start()
    {
        foreach (var folder in new[] { Platform.Paths.DataRoot, Platform.Paths.LogsRoot, Platform.Paths.CacheRoot, Platform.Paths.Sessions, Platform.Paths.Projects })
        {
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StartupNotices.Add($"AGEX cannot write to {Redactor.RedactPaths(folder)}: {ex.Message}"); }
        }
        StartupNotices.AddRange(SettingsStore.LoadIssues);
        var state = SettingsStore.LoadState();
        var interrupted = Sessions.MarkInterrupted();
        if (interrupted.Count > 0)
        {
            state.UnfinishedSession = interrupted[0];
            StartupNotices.Add("AGEX stopped while a request was running. It was marked as interrupted and was not restarted.");
        }
        state.CleanExit = false;
        SettingsStore.SaveState(state);
        if (SafeMode) StartupNotices.Add("Safe mode: third-party skills are off and only built-in adapters are used.");
        else
        {
            foreach (var id in Skills.ValidateInstalled())
                StartupNotices.Add($"The skill '{id}' was disabled because it failed during startup. See Skills for details.");
        }
        try { Sessions.ApplyRetention(Settings.Sessions.RetentionDays, Settings.Sessions.MaxSessions); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("retention_failed", ex); }
        CleanTemp();
        InstallMarker.MigrateRepository(AppContext.BaseDirectory, Log);
        LoadStatistics();
        Log.Write("app_start", new { version = AgexInfo.Version, os = Platform.RuntimeId, safe_mode = SafeMode });
    }

    public void Stop(bool clean = true)
    {
        Runner.StopAll();
        var state = SettingsStore.LoadState();
        state.CleanExit = clean;
        SettingsStore.SaveState(state);
        Log.Write("app_stop", new { clean });
    }

    public void SaveSettings(AgexSettings settings)
    {
        Settings = SettingsStore.Normalize(settings);
        SettingsStore.Save(Settings);
    }

    public void ReloadSettings() => Settings = SettingsStore.Load();

    private void CleanTemp()
    {
        try
        {
            if (!Directory.Exists(Platform.Paths.Temp)) return;
            foreach (var entry in new DirectoryInfo(Platform.Paths.Temp).EnumerateFileSystemInfos())
            {
                if (entry.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-12)) continue;
                if (entry is DirectoryInfo dir) dir.Delete(recursive: true); else entry.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------- teams

    /// <summary>Agents for a request: team (or enabled agents), limited to supported, installed agents.</summary>
    public List<TeamMember> BuildMembers(ProjectProfile? profile = null, string? teamId = null, IReadOnlyCollection<string>? agentIds = null)
    {
        IEnumerable<string> ids = agentIds is { Count: > 0 } ? agentIds
            : (teamId ?? profile?.Team ?? Settings.ActiveTeam) is { Length: > 0 } team && Settings.Teams.FirstOrDefault(item => item.Id == team) is { } found ? found.Agents
            : profile?.PreferredAgents is { Count: > 0 } preferred ? preferred
            : Settings.EnabledAgents;
        var members = new List<TeamMember>();
        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Registry.Get(id) is not { } adapter) continue;
            if (!adapter.SupportedPlatforms.Contains(Platform.Os)) continue;
            var detection = Registry.DetectionForRun(adapter.Id);
            if (detection.Status is AgentStatus.NotInstalled or AgentStatus.PlatformUnsupported) continue;
            var options = Settings.AgentOptions.GetValueOrDefault(adapter.Id) ?? new AgentOptions();
            // A saved model that the agent no longer lists falls back to Auto instead of failing the request.
            var (model, _) = ModelSelection.Resolve(options.Model, options.CustomModel, Models.Cached(adapter.Id));
            var canWrite = adapter.CanWriteFiles && options.AllowWrites && (profile?.AllowWrites ?? true) && Settings.Permissions.WriteProject;
            var provider = Providers.Endpoint(ProviderFor(adapter.Id));
            var privacy = provider is null ? adapter.PrivacyFor(model) : provider.Local ? PrivacyKind.Local : PrivacyKind.Cloud;
            var support = adapter.ModelSettings;
            var effort = options.Effort.Length > 0 && support.ReasoningEfforts.Contains(options.Effort) ? options.Effort : null;
            members.Add(new TeamMember(adapter, model, effort, canWrite, privacy)
            {
                Provider = provider,
                Temperature = support.SupportsTemperature ? options.Temperature : null,
                ContextWindow = support.SupportsContextWindowSelection ? options.ContextWindow : null,
            });
        }
        return members;
    }

    /// <summary>Installed browser and computer-control tools, with the MCP server AGEX would hand to agents.</summary>
    public IReadOnlyList<ToolServer> ToolServers() =>
        Skills.Installed().Where(skill => CapabilityRouting.KnownTools.ContainsKey(skill.Id)).Select(skill => new ToolServer(
            skill.Id, skill.Manifest.Name, CapabilityRouting.KnownTools[skill.Id],
            skill.Enabled && skill.DisabledReason.Length == 0 && !skill.PermissionChoices.Values.Any(choice => choice == PermissionChoice.Deny),
            WithOutputFolder(Skills.SpecFor(skill)))
        { AskFirst = skill.PermissionChoices.Values.Any(choice => choice == PermissionChoice.AskEachTime) }).ToList();

    /// <summary>
    /// The browser tool saves screenshots and page snapshots; they go to AGEX's
    /// temporary folder instead of the user's project (Playwright MCP --output-dir).
    /// </summary>
    private McpServerSpec? WithOutputFolder(McpServerSpec? spec)
    {
        if (spec is null || spec.SkillId != "playwright-mcp" || spec.Arguments.Contains("--output-dir")) return spec;
        var folder = Path.Combine(Platform.Paths.Temp, "browser-output");
        try { Directory.CreateDirectory(folder); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return spec; }
        return spec with { Arguments = [.. spec.Arguments, "--output-dir", folder] };
    }

    public RoutingPreset RoutingFor(ProjectProfile? profile) => profile?.Routing ?? Settings.Routing;

    public Router Router() => new(Settings, this);

    /// <summary>Cloud services that would receive data for these members (for the privacy notice).</summary>
    public static IReadOnlyList<string> CloudDestinations(IEnumerable<TeamMember> members) =>
        members.Where(member => member.Privacy is PrivacyKind.Cloud or PrivacyKind.Mixed).Select(member => member.Adapter.DataDestination(member.Model)).Distinct().ToList();

    /// <summary>Builds the engine for one request. The caller runs it and subscribes to its events.</summary>
    public RequestEngine CreateRequest(string project, string request, IEngineHost host, IReadOnlyList<TeamMember> members, ProjectProfile profile,
        IReadOnlyList<SkillContext> skills, IReadOnlyList<McpServerSpec> mcpServers, string previousContext = "", string continuedFrom = "", string clonedFrom = "", string teamName = "",
        IReadOnlyList<Agex.Core.Attachments.Attachment>? attachments = null, string teamBrief = "", ChatMode mode = ChatMode.Build, bool skillsChosen = false,
        string localUrl = "", CapabilityPlan? capabilities = null)
    {
        var preset = RoutingFor(profile);
        var router = new Router(Settings, this);
        var allowed = router.Filter(members, preset);
        if (allowed.Count == 0)
            throw new InvalidOperationException(preset == RoutingPreset.LocalOnly ? "Local only is selected, but no local agent (Ollama) is ready." : "No agent is ready. Open Agents to enable or install one.");
        var leader = router.ChooseLeader(allowed, preset, Settings.Leader) ?? throw new InvalidOperationException("None of the selected agents can plan a request.");
        var intent = RequestClassifier.Classify(request, mode, project, attachments is { Count: > 0 });
        // A text-only team cannot read the project: its questions and plans are answered from the context AGEX sends.
        capabilities ??= CapabilityRouting.Plan(intent, allowed, Settings.Permissions, ToolServers(), Platform.Os == OsKind.Windows);
        var approval = Settings.Approvals.Mode;
        var canUndo = Settings.Approvals.SnapshotBeforeWrites && Git.IsRepository(project);
        var askBeforeWrites = approval switch
        {
            ApprovalMode.AskEveryTime => true,
            ApprovalMode.TrustSession => false,
            _ => Settings.Approvals.AskBeforeWrites && ApprovalRules.MustAsk(approval, ActionKind.WriteFiles, profile.Trusted, canUndo),
        };
        var options = new RequestOptions
        {
            Intent = intent, ChosenMode = mode, ApprovalMode = approval, Permissions = Settings.Permissions, Capabilities = capabilities,
            SkillsChosen = skillsChosen, LocalUrl = localUrl,
            Project = project, Request = request, Members = allowed, Leader = leader, Routing = preset,
            RoutingGuidance = router.Guidance(allowed, preset) + (Settings.Efficiency == EfficiencyMode.LocalFirst && allowed.Any(member => member.Privacy == PrivacyKind.Local)
                ? " Local-first is on: give every task a local agent can do to the local agent; use cloud agents only for work it cannot do (for example editing files)." : ""),
            Attachments = attachments ?? [], TeamBrief = teamBrief, Efficiency = Settings.Efficiency,
            SkillAgents = Agex.Core.Skills.SkillProfiles.AgentsBySkill(Settings.AgentSkills),
            Team = teamName, Skills = skills, McpServers = mcpServers, ProjectInstructions = profile.Instructions, IgnoredFolders = profile.IgnoredFolders,
            AskBeforeWrites = askBeforeWrites, AllowCommands = Settings.Permissions.RunCommands,
            SnapshotBeforeWrites = Settings.Approvals.SnapshotBeforeWrites, MaxParallel = Settings.MaxParallelTasks,
            AgentTimeout = TimeSpan.FromMinutes(Settings.AgentTimeoutMinutes), PreviousContext = previousContext, ContinuedFrom = continuedFrom, ClonedFrom = clonedFrom,
        };
        return new RequestEngine(options, Registry, host, Sessions, Git, Log);
    }

    // -------------------------------------------------------- statistics

    private void LoadStatistics()
    {
        try
        {
            var durations = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var summary in Sessions.List().Take(40))
            {
                if (Sessions.Load(summary.Id) is not { } session) continue;
                foreach (var run in session.Runs.Where(run => run.Outcome == "Ok" && run.Seconds > 0))
                {
                    var id = Registry.Get(run.Agent)?.Id ?? run.Agent;
                    if (!durations.TryGetValue(id, out var list)) durations[id] = list = [];
                    list.Add(run.Seconds);
                }
            }
            foreach (var (id, list) in durations) { list.Sort(); _medians[id] = list[list.Count / 2]; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public double? MedianSeconds(string agentId) => _medians.TryGetValue(agentId, out var value) ? value : null;

    // --------------------------------------------------- diagnostics

    /// <summary>Plain-text diagnostics with secrets and the user name removed, ready to copy.</summary>
    public string DiagnosticsReport(DiscoveryResult? scan = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"AGEX {AgexInfo.Version}{(SafeMode ? " (safe mode)" : "")}");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Architecture: {Platform.Architecture} ({Platform.RuntimeId}), .NET {Environment.Version}");
        builder.AppendLine($"Data folder: {Platform.Paths.DataRoot}");
        builder.AppendLine($"Logs: {Platform.Paths.LogsRoot}");
        builder.AppendLine($"Secure storage: {Platform.SecureStore.Mechanism}");
        builder.AppendLine();
        builder.AppendLine("Agents:");
        foreach (var adapter in Registry.Adapters)
        {
            var detection = Registry.LastDetection(adapter.Id) ?? Registry.Detect(adapter.Id);
            var health = Registry.Health(adapter.Id);
            builder.AppendLine($"  {adapter.Name,-12} {AgentStatusText.Code(detection.Status),-22} {detection.Version,-12} {(Settings.EnabledAgents.Contains(adapter.Id) ? "enabled" : "disabled")}{(health.Healthy ? "" : " cooldown: " + health.Reason)}");
            if (detection.Reason.Length > 0) builder.AppendLine("               " + detection.Reason);
        }
        if (scan is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Other tools found:");
            foreach (var item in scan.Items.Where(item => !item.HasAdapter && item.Status is AgentStatus.DetectedUnsupported or AgentStatus.Available))
                builder.AppendLine($"  {item.Name} {item.Version} ({item.StatusLabel})");
        }
        builder.AppendLine();
        builder.AppendLine("Skills:");
        var installed = Skills.Installed();
        if (installed.Count == 0) builder.AppendLine("  none installed");
        foreach (var skill in installed)
            builder.AppendLine($"  {skill.Id,-28} {skill.Manifest.Version,-10} {(skill.Enabled ? "enabled" : "disabled")} {SkillText.Trust(skill.Manifest.Trust)} {skill.DisabledReason}");
        builder.AppendLine();
        builder.AppendLine("Storage:");
        builder.AppendLine($"  Sessions: {Sessions.List().Count} (retention {(Settings.Sessions.RetentionDays == 0 ? "forever" : Settings.Sessions.RetentionDays + " days")})");
        builder.AppendLine($"  Settings schema: {Settings.SchemaVersion}");
        foreach (var issue in SettingsStore.LoadIssues) builder.AppendLine("  Issue: " + issue);
        builder.AppendLine($"  Free disk space: {FreeSpace(Platform.Paths.DataRoot)}");
        builder.AppendLine();
        builder.AppendLine($"Updates: automatic check {(Settings.CheckForUpdates ? "on" : "off")}; release signing key {(UpdateService.ReleasePublicKeyPem.Length > 0 ? "present" : "not configured (checksum verification only)")}");
        return Redactor.ForSharing(builder.ToString());
    }

    private static string FreeSpace(string path)
    {
        try { return $"{new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace / 1024 / 1024 / 1024} GB"; }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return "unknown"; }
    }

    // --------------------------------------------------------- repair

    /// <summary>
    /// Fixes what AGEX owns: folders, damaged settings, the session index,
    /// invalid skills, stale caches and agent cooldowns. Never installs,
    /// changes or removes external agents.
    /// </summary>
    public IReadOnlyList<RepairAction> Repair()
    {
        var actions = new List<RepairAction>();
        foreach (var folder in new[] { Platform.Paths.DataRoot, Platform.Paths.LogsRoot, Platform.Paths.CacheRoot, Platform.Paths.Sessions, Platform.Paths.Projects, Platform.Paths.Skills })
        {
            try
            {
                var existed = Directory.Exists(folder);
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".agex-write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                if (!existed) actions.Add(new("Folders", "FIXED", $"Created {Redactor.RedactPaths(folder)}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                actions.Add(new("Folders", "FAILED", $"{Redactor.RedactPaths(folder)} is not writable: {ex.Message}"));
            }
        }
        var store = new SettingsStore(Platform.Paths, Log);
        var settings = store.Load();
        actions.Add(store.LoadIssues.Count > 0 ? new("Settings", "FIXED", string.Join(" ", store.LoadIssues)) : new("Settings", "OK", "Settings are valid."));
        SaveSettings(settings);
        var sessions = Sessions.RebuildIndex();
        actions.Add(new("Sessions", "OK", $"Session index rebuilt ({sessions.Count} sessions)."));
        var disabled = Skills.ValidateInstalled();
        actions.Add(disabled.Count > 0 ? new("Skills", "FIXED", "Disabled broken skills: " + string.Join(", ", disabled)) : new("Skills", "OK", $"{Skills.Installed().Count} installed skills are valid."));
        foreach (var file in Directory.GetFiles(Platform.Paths.Skills, "*.old-*", SearchOption.TopDirectoryOnly)) File.Delete(file);
        foreach (var dir in Directory.GetDirectories(Platform.Paths.Skills, "*.old-*")) { Directory.Delete(dir, recursive: true); actions.Add(new("Skills", "FIXED", "Removed a leftover update folder.")); }
        try
        {
            if (File.Exists(Platform.Paths.Scan)) File.Delete(Platform.Paths.Scan);
            if (Directory.Exists(Platform.Paths.Temp)) Directory.Delete(Platform.Paths.Temp, recursive: true);
            actions.Add(new("Cache", "FIXED", "Cleared the scan cache and temporary files."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { actions.Add(new("Cache", "WARN", ex.Message)); }
        Registry.ResetHealth();
        foreach (var adapter in Registry.Adapters)
        {
            var detection = Registry.Detect(adapter.Id);
            actions.Add(new("Agents", detection.Status is AgentStatus.NotInstalled ? "INFO" : "OK", $"{adapter.Name}: {AgentStatusText.Label(detection.Status)}. AGEX does not install or change agents."));
        }
        Log.Write("repair", new { actions = actions.Count });
        return actions;
    }
}
