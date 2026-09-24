using System.Collections.ObjectModel;
using Agex.Core;
using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Core.Runtime;
using Agex.Core.Sessions;
using Agex.Core.Settings;
using Agex.Core.Skills;

namespace Agex.Desktop;

/// <summary>
/// The desktop app's controller: current project, the running request, live
/// collections for the views, discovery results and user prompts. All events
/// are raised on the UI thread.
/// </summary>
public sealed class Workspace : IEngineHost
{
    private CancellationTokenSource? _cancel;
    private TaskCompletionSource<string?>? _answer;
    private System.Timers.Timer? _draftTimer;

    public Workspace(AgexCore core)
    {
        Core = core;
        State = core.SettingsStore.LoadState();
        var projectPath = State.Project.Length > 0 ? State.Project : core.Settings.LastProject;
        if (projectPath.Length > 0 && Directory.Exists(projectPath)) Project = core.SettingsStore.LoadProject(projectPath);
        Scan = core.Discovery.LoadCached();
    }

    public AgexCore Core { get; }
    public AgexSettings Settings => Core.Settings;
    public AppState State { get; }
    public ProjectProfile? Project { get; private set; }
    public DiscoveryResult? Scan { get; private set; }
    public bool Scanning { get; private set; }
    public RequestEngine? Engine { get; private set; }
    public Session? Session { get; private set; }
    public bool IsRunning => Engine is not null;
    public PendingQuestion? Question { get; private set; }
    public bool Offline { get; set; }

    public ObservableCollection<AgentMessage> Messages { get; } = [];
    public ObservableCollection<TimelineEntry> Timeline { get; } = [];
    public ObservableCollection<TaskItem> Tasks { get; } = [];
    /// <summary>Files the user attached in the composer, not yet sent (original paths).</summary>
    public ObservableCollection<string> PendingAttachments { get; } = [];
    /// <summary>Attachments of the current request, as prepared by AGEX.</summary>
    public IReadOnlyList<Agex.Core.Attachments.Attachment> LastAttachments { get; private set; } = [];
    public Dictionary<string, AgentLiveState> AgentStates { get; } = new();

    /// <summary>Set by the main window: shows dialogs, notifications and prompts.</summary>
    public IWorkspaceUi? Ui { get; set; }

    public event Action? ProjectChanged;
    public event Action? ScanChanged;
    public event Action? SessionChanged;
    public event Action<AgentLiveState>? AgentChanged;
    public event Action? SettingsChanged;

    // ------------------------------------------------------------ projects

    public void OpenProject(string path)
    {
        if (!Directory.Exists(path)) { Ui?.Toast("Folder not found", path, ToastKind.Error); return; }
        if (IsRunning && Project?.Path != path) { Ui?.Toast("A request is running", "Finish or cancel it before switching projects.", ToastKind.Info); return; }
        var profile = Core.SettingsStore.LoadProject(path);
        profile.LastOpened = DateTimeOffset.UtcNow;
        Core.SettingsStore.SaveProject(profile);
        Core.SettingsStore.AddRecentProject(Core.Settings, profile.Path);
        Core.SaveSettings(Core.Settings);
        Project = profile;
        State.Project = profile.Path;
        SaveState();
        ProjectChanged?.Invoke();
    }

    public void SaveProject(ProjectProfile profile)
    {
        Core.SettingsStore.SaveProject(profile);
        if (Project?.Path == profile.Path) Project = profile;
        ProjectChanged?.Invoke();
    }

    public void SaveSettings()
    {
        Core.SaveSettings(Core.Settings);
        SettingsChanged?.Invoke();
    }

    // ------------------------------------------------------------ discovery

    /// <summary>Background scan: quick file check first, then health checks item by item.</summary>
    public async Task ScanAsync()
    {
        if (Scanning) return;
        Scanning = true;
        ScanChanged?.Invoke();
        try
        {
            Scan = await Task.Run(() => Core.Discovery.QuickScan());
            ScanChanged?.Invoke();
            var progress = new Progress<DiscoveredItem>(item =>
            {
                if (Scan is null) return;
                var items = Scan.Items.ToList();
                var index = items.FindIndex(existing => existing.Id == item.Id);
                if (index >= 0) items[index] = item; else items.Add(item);
                Scan = Scan with { Items = items };
                ScanChanged?.Invoke();
            });
            Scan = await Task.Run(() => Core.Discovery.FullScanAsync(progress, CancellationToken.None));
        }
        catch (Exception ex)
        {
            Core.Log.Error("scan_failed", ex);
            Ui?.Toast("Scan failed", ex.Message, ToastKind.Error);
        }
        finally
        {
            Scanning = false;
            ScanChanged?.Invoke();
        }
        // Sign-in and model checks for installed agents (quota-free; cached model lists are reused).
        var installed = Scan?.Items.Where(item => item.HasAdapter && item.Status is AgentStatus.Supported or AgentStatus.Available or AgentStatus.AuthRequired).Select(item => item.Id).ToList() ?? [];
        await Task.WhenAll(installed.Select(id => CheckAgentAsync(id, false)));
    }

    // ------------------------------------------------ agent setup and models

    /// <summary>Last sign-in check per agent (never contains credentials).</summary>
    public Dictionary<string, AuthCheck> Auth { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Agents with an install, sign-in or model check in progress.</summary>
    public HashSet<string> AgentBusy { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AgentReadiness Readiness(string id)
    {
        var status = Scan?.Items.FirstOrDefault(item => item.Id == id)?.Status ?? Core.Registry.LastDetection(id)?.Status ?? AgentStatus.Unknown;
        return AgentReadinessText.From(status, Auth.GetValueOrDefault(id));
    }

    /// <summary>
    /// Checks sign-in and, when the agent can be used, refreshes its model list if
    /// the cached one expired. Checks that could start a sign-in by themselves run
    /// only when the user asked (<paramref name="userInitiated"/>) or the agent was signed in before.
    /// </summary>
    public async Task CheckAgentAsync(string id, bool userInitiated)
    {
        if (Core.Registry.Get(id) is not { } adapter || !AgentBusy.Add(adapter.Id)) return;
        ScanChanged?.Invoke();
        try
        {
            var detection = Core.Registry.DetectionForRun(adapter.Id);
            if (detection.Status is AgentStatus.NotInstalled or AgentStatus.PlatformUnsupported) { Auth.Remove(adapter.Id); return; }
            var known = Auth.GetValueOrDefault(adapter.Id)?.State == AuthState.SignedIn || Core.Models.Cached(adapter.Id)?.Status == ModelDiscoveryStatus.Ok;
            if (!adapter.PassiveAuthCheck && !userInitiated && !known) return;
            var auth = await adapter.CheckAuthAsync(detection, CancellationToken.None);
            Auth[adapter.Id] = auth;
            if (auth.State == AuthState.SignedIn) Core.Registry.ResetHealth(adapter.Id);
            if (auth.State is AuthState.SignedIn or AuthState.NotRequired or AuthState.Unknown && (userInitiated || Core.Models.IsStale(adapter.Id)))
                await RefreshModelsCoreAsync(adapter, userInitiated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Core.Log.Error("agent_check_failed", ex, new { agent = adapter.Id });
        }
        finally
        {
            AgentBusy.Remove(adapter.Id);
            ScanChanged?.Invoke();
        }
    }

    public async Task<ModelDiscovery?> RefreshModelsAsync(string id)
    {
        if (Core.Registry.Get(id) is not { } adapter || !AgentBusy.Add(adapter.Id)) return null;
        ScanChanged?.Invoke();
        try { return await RefreshModelsCoreAsync(adapter, true); }
        finally { AgentBusy.Remove(adapter.Id); ScanChanged?.Invoke(); }
    }

    private async Task<ModelDiscovery> RefreshModelsCoreAsync(IAgentAdapter adapter, bool force)
    {
        var discovery = await Core.Models.RefreshAsync(adapter.Id, force, CancellationToken.None);
        if (Settings.AgentOptions.GetValueOrDefault(adapter.Id) is { } options)
        {
            var (_, disappeared) = ModelSelection.Resolve(options.Model, options.CustomModel, discovery);
            if (disappeared)
            {
                var previous = options.Model;
                options.Model = "";
                SaveSettings();
                Core.Log.Write("model_disappeared", new { agent = adapter.Id, model = previous });
                Ui?.Toast("Model no longer available", $"Previously selected model {previous} is no longer available. {adapter.Name} now uses Auto.", ToastKind.Info);
            }
        }
        return discovery;
    }

    /// <summary>Opens the agent's own sign-in in a terminal, then watches for the sign-in to complete.</summary>
    public async Task<bool> StartSignInAsync(string id)
    {
        if (Core.Registry.Get(id) is not { } adapter || adapter.Setup.LoginArguments is not { } arguments) return false;
        var detection = Core.Registry.DetectionForRun(adapter.Id);
        if (detection.Path is null) return false;
        var folder = Project?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Core.Platform.RunInTerminal(detection.Path, arguments, folder))
        {
            Ui?.Toast("Could not open a terminal", $"Open a terminal yourself and run: {Path.GetFileNameWithoutExtension(detection.Path)} {string.Join(' ', arguments)}", ToastKind.Error);
            return false;
        }
        Core.Log.Write("agent_sign_in_started", new { agent = adapter.Id });
        if (!adapter.PassiveAuthCheck) return true; // the user presses "Check sign-in" when done
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            var auth = await adapter.CheckAuthAsync(detection, CancellationToken.None);
            if (auth.State != AuthState.SignedIn) continue;
            Auth[adapter.Id] = auth;
            Core.Registry.ResetHealth(adapter.Id);
            Ui?.Toast("Signed in", $"{adapter.Name} account ready.", ToastKind.Success);
            await ScanAsync();
            await CheckAgentAsync(adapter.Id, true);
            return true;
        }
        return true;
    }

    public async Task<bool> InstallAgentAsync(string id)
    {
        if (Core.Registry.Get(id) is not { } adapter || !AgentBusy.Add(adapter.Id)) return false;
        ScanChanged?.Invoke();
        try
        {
            var (success, message) = await Core.Installer.InstallAsync(adapter, CancellationToken.None);
            Ui?.Toast(success ? $"{adapter.Name} installed" : $"{adapter.Name} was not installed", success ? "Next: sign in." : message, success ? ToastKind.Success : ToastKind.Error);
            return success;
        }
        finally
        {
            AgentBusy.Remove(adapter.Id);
            ScanChanged?.Invoke();
            await ScanAsync();
        }
    }

    public IReadOnlyList<TeamMember> Members(string? teamId = null) => Core.BuildMembers(Project, teamId);

    // ------------------------------------------------------------- requests

    /// <summary>Starts a request. Returns false (with a message) when it cannot start.</summary>
    public async Task<bool> StartAsync(string request, string? teamId = null, IReadOnlyCollection<string>? agentIds = null, Session? continueFrom = null, Session? cloneOf = null)
    {
        if (IsRunning) { Ui?.Toast("A request is already running", "Wait for it to finish or cancel it first.", ToastKind.Info); return false; }
        if (Project is null || !Directory.Exists(Project.Path)) { Ui?.Toast("Choose a project first", "Open a project folder so agents know where to work.", ToastKind.Info); return false; }
        if (string.IsNullOrWhiteSpace(request)) return false;
        var members = Core.BuildMembers(Project, teamId, agentIds);
        // Job team: its rules go into every prompt; a read-only team never lets agents write, a local team uses local agents only.
        var jobTeam = Agex.Core.Teams.JobTeamCatalog.Get(Settings.ActiveJobTeam);
        if (jobTeam?.Approval == Agex.Core.Teams.ApprovalLevel.ReadOnly) members = members.Select(member => member with { CanWrite = false }).ToList();
        if (jobTeam?.Efficiency == Agex.Core.Teams.EfficiencyHint.LocalFirst)
        {
            // An agent on Auto may use a local or a cloud model: pin it to the first local model it reported.
            members = members.Select(member => member.Privacy == Agex.Core.Agents.PrivacyKind.Mixed && member.Provider is null
                    && Core.Models.Cached(member.Id)?.Models.FirstOrDefault(model => model.Location == Agex.Core.Agents.PrivacyKind.Local) is { } local
                    ? member with { Model = local.Id, Privacy = Agex.Core.Agents.PrivacyKind.Local }
                    : member)
                .Where(member => member.Privacy == Agex.Core.Agents.PrivacyKind.Local).ToList();
        }
        if (members.Count == 0)
        {
            Ui?.Toast("No agent is ready", jobTeam?.Efficiency == Agex.Core.Teams.EfficiencyHint.LocalFirst ? $"{jobTeam.Name} uses local models only. Start Ollama and turn it on in Agents." : "Open Agents to enable or install an agent.", ToastKind.Error);
            return false;
        }

        // Attachments: say what each agent receives and where it goes, then copy and convert.
        IReadOnlyList<Agex.Core.Attachments.Attachment> attachments = [];
        if (PendingAttachments.Count > 0)
        {
            var filtered = Core.Router().Filter(members, Core.RoutingFor(Project));
            var kinds = PendingAttachments.Select(path => Agex.Core.Attachments.AttachmentService.Classify(path)).ToList();
            var extractFrames = false;
            if (kinds.Contains(Agex.Core.Attachments.AttachmentKind.Video) && Core.Attachments.CanExtractVideoFrames && Ui is not null)
                extractFrames = await Ui.ConfirmAsync("Prepare the video for the agents?", "Agents cannot watch videos. AGEX can take 6 still frames and read the video details (duration, size, codecs) with ffmpeg on this computer. The video itself is not sent.", "Extract frames", "Details only");
            var folder = Path.Combine(Core.Platform.Paths.DataRoot, "attachments", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
            var (prepared, refused) = await Core.Attachments.PrepareAsync(folder, PendingAttachments, extractFrames, CancellationToken.None);
            var plan = Agex.Core.Attachments.AttachmentService.Plan(prepared, filtered.Select(member => (member.Adapter, member.Model, Core.Models.Cached(member.Id)?.Models.FirstOrDefault(model => model.Id == member.Model)?.Vision == true)));
            var cloud = plan.Where(item => item.LeavesComputer).Select(item => item.Destination).Distinct().ToList();
            if (Ui is not null && prepared.Count > 0)
            {
                const string nl = "\n";
                var text = (cloud.Count > 0 ? $"These files may be sent to {string.Join(", ", cloud)}." : "These files stay on this computer (local agents only).") + nl + nl
                    + string.Join(nl + nl, plan.Select(item => $"{item.AgentName} ({item.Destination}):" + nl + "  " + string.Join(nl + "  ", item.Lines)))
                    + (refused.Count > 0 ? nl + nl + "Not attached:" + nl + "  " + string.Join(nl + "  ", refused) : "");
                if (!await Ui.ConfirmAsync($"Send {prepared.Count} attached file(s)?", text, "Send", "Cancel")) return false;
            }
            else if (refused.Count > 0) Ui?.Toast("Some files were not attached", string.Join(" ", refused), ToastKind.Info);
            attachments = prepared;
        }

        // Privacy: once per project, say which cloud services will receive project data.
        var destinations = AgexCore.CloudDestinations(Core.Router().Filter(members, Core.RoutingFor(Project)));
        if (Settings.Privacy.ExplainCloudUse && destinations.Count > 0 && !Project.CloudUseAcknowledged && Ui is not null)
        {
            var ok = await Ui.ConfirmAsync("Your request goes to cloud services",
                $"To work on \"{Project.Name}\", AGEX sends your request and the project files the agents read to: {string.Join(", ", destinations)}. Their privacy terms apply. Local-only agents (Ollama) keep data on this computer.",
                "Continue", "Cancel");
            if (!ok) return false;
            Project.CloudUseAcknowledged = true;
            Core.SettingsStore.SaveProject(Project);
        }

        // Skills: include allowed ones; ask about "ask each time" permissions.
        var active = Core.Skills.ForRequest(Project.Skills);
        var instructions = active.Instructions.ToList();
        var servers = active.McpServers.ToList();
        foreach (var (skill, ask) in active.NeedApproval)
        {
            if (Ui is null) break;
            var allow = await Ui.ConfirmAsync($"Use the skill \"{skill.Manifest.Name}\"?",
                $"For this request it wants to: {string.Join(", ", ask.Select(SkillText.Permission))}. {SkillText.Trust(skill.Manifest.Trust)}.", "Allow for this request", "Skip");
            if (allow) Core.Skills.AddApproved(skill, instructions, servers);
        }

        RequestEngine engine;
        try
        {
            var previous = continueFrom is null ? "" : $"Earlier request: {continueFrom.Request}\nOutcome: {continueFrom.Outcome?.Headline} {continueFrom.Outcome?.Reason}";
            var teamName = teamId is not null ? Settings.Teams.FirstOrDefault(team => team.Id == teamId)?.Name ?? "" : "";
            engine = Core.CreateRequest(Project.Path, request.Trim(), this, members, Project, instructions, servers, previous, continueFrom?.Id ?? "", cloneOf?.Id ?? "", jobTeam?.Name ?? teamName,
                attachments, jobTeam is null ? "" : Agex.Core.Teams.JobTeamCatalog.Brief(jobTeam));
        }
        catch (InvalidOperationException ex)
        {
            Ui?.Toast("Could not start", ex.Message, ToastKind.Error);
            return false;
        }

        Messages.Clear(); Timeline.Clear(); Tasks.Clear(); AgentStates.Clear();
        PendingAttachments.Clear();
        LastAttachments = attachments;
        Engine = engine;
        Session = engine.Session;
        Question = null;
        engine.MessageAdded += message => App.Post(() => Messages.Add(message));
        engine.TimelineAdded += entry => App.Post(() => Timeline.Add(entry));
        engine.TaskChanged += task => App.Post(() => UpsertTask(task));
        engine.AgentChanged += state => App.Post(() => { AgentStates[state.AgentId] = state; AgentChanged?.Invoke(state); });
        engine.SessionChanged += _ => App.Post(() => SessionChanged?.Invoke());
        _cancel = new CancellationTokenSource();
        State.UnfinishedSession = engine.Session.Id;
        State.DraftRequest = "";
        SaveState();
        SessionChanged?.Invoke();
        var started = DateTimeOffset.UtcNow;
        var token = _cancel.Token;
        _ = Task.Run(async () =>
        {
            Session result;
            try { result = await engine.RunAsync(token); }
            catch (Exception ex)
            {
                Core.Log.Error("request_crash", ex);
                result = engine.Session;
            }
            App.Post(() => Finish(result, started));
        });
        return true;
    }

    private void Finish(Session session, DateTimeOffset started)
    {
        Engine = null;
        _cancel?.Dispose();
        _cancel = null;
        Question = null;
        State.UnfinishedSession = "";
        SaveState();
        SessionChanged?.Invoke();
        var seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
        var failed = session.Status is SessionStatus.Failed or SessionStatus.StartFailed or SessionStatus.Partial;
        var n = Settings.Notifications;
        if (n.Enabled && seconds >= n.MinimumSeconds && ((failed && n.OnFailure) || (!failed && n.OnComplete)) && session.Status != SessionStatus.Cancelled)
            Ui?.Notify(SessionStatusText.Label(session.Status), session.Outcome?.Headline ?? session.Title, failed ? ToastKind.Error : ToastKind.Success);
    }

    private void UpsertTask(TaskItem task)
    {
        var index = Tasks.ToList().FindIndex(item => item.Id == task.Id);
        if (index >= 0) Tasks[index] = task; else Tasks.Add(task);
    }

    public void Cancel()
    {
        _answer?.TrySetResult(null);
        _cancel?.Cancel();
    }

    /// <summary>The editor the user chose on the Agents page, when the scan still finds it.</summary>
    public Agex.Core.Agents.DiscoveredItem? PreferredEditor =>
        Settings.PreferredEditor.Length > 0 ? Scan?.Items.FirstOrDefault(item => item.Id == Settings.PreferredEditor && item.Location.Length > 0) : null;

    public bool OpenInEditor(string path) =>
        PreferredEditor is { } editor && Agex.Core.Agents.Editors.Open(Core.Platform, editor.Id, editor.Location, path, Core.Log);

    public void Pause() => Engine?.Pause();
    public void Resume() => Engine?.Resume();

    /// <summary>Shows a stored session in Home and Agent Room (read-only).</summary>
    public void ShowSession(Session session)
    {
        if (IsRunning) return;
        Session = session;
        Messages.Clear(); Timeline.Clear(); Tasks.Clear(); AgentStates.Clear();
        foreach (var message in session.Messages) Messages.Add(message);
        foreach (var entry in session.Timeline) Timeline.Add(entry);
        foreach (var task in session.Tasks) Tasks.Add(task);
        SessionChanged?.Invoke();
    }

    // -------------------------------------------------------- IEngineHost

    public Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        App.Post(async () =>
        {
            if (Settings.Notifications.Enabled && Settings.Notifications.OnApproval) Ui?.Notify("Approval needed", request.Title, ToastKind.Info, onlyWhenInactive: true);
            var decision = Ui is null ? ApprovalDecision.Deny : await Ui.ApprovalAsync(request);
            if (decision == ApprovalDecision.AllowAndTrust && Project is not null)
            {
                Project.Trusted = true;
                Core.SettingsStore.SaveProject(Project);
            }
            completion.TrySetResult(decision);
        });
        cancellationToken.Register(() => completion.TrySetResult(ApprovalDecision.Deny));
        return completion.Task;
    }

    public Task<string?> AskUserAsync(PendingQuestion question, CancellationToken cancellationToken)
    {
        _answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => _answer?.TrySetResult(null));
        App.Post(() =>
        {
            Question = question;
            SessionChanged?.Invoke();
            if (Settings.Notifications.Enabled && Settings.Notifications.OnInputNeeded) Ui?.Notify($"{question.From} has a question", question.Question, ToastKind.Info, onlyWhenInactive: true);
        });
        return _answer.Task;
    }

    public void Answer(string? text)
    {
        Question = null;
        _answer?.TrySetResult(string.IsNullOrWhiteSpace(text) ? null : text.Trim());
        SessionChanged?.Invoke();
    }

    // ---------------------------------------------------------------- state

    /// <summary>Keeps the unsent request so a crash or restart never loses it.</summary>
    public void SaveDraft(string text)
    {
        State.DraftRequest = text;
        _draftTimer ??= new System.Timers.Timer(800) { AutoReset = false };
        _draftTimer.Elapsed -= OnDraftTimer;
        _draftTimer.Elapsed += OnDraftTimer;
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private void OnDraftTimer(object? sender, System.Timers.ElapsedEventArgs e) => SaveState();

    public void SaveState() => Core.SettingsStore.SaveState(State);

    public void Shutdown()
    {
        _cancel?.Cancel();
        SaveState();
        Core.Stop(clean: true);
    }
}

public enum ToastKind { Info, Success, Error }

/// <summary>What the workspace needs from the window.</summary>
public interface IWorkspaceUi
{
    void Toast(string title, string message, ToastKind kind);
    /// <summary>In-app toast plus an OS notification when the window is not active.</summary>
    void Notify(string title, string message, ToastKind kind, bool onlyWhenInactive = false);
    Task<bool> ConfirmAsync(string title, string message, string confirm, string cancel);
    Task<ApprovalDecision> ApprovalAsync(ApprovalRequest request);
}
