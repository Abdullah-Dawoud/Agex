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
        if (members.Count == 0) { Ui?.Toast("No agent is ready", "Open Agents to enable or install an agent.", ToastKind.Error); return false; }

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
            engine = Core.CreateRequest(Project.Path, request.Trim(), this, members, Project, instructions, servers, previous, continueFrom?.Id ?? "", cloneOf?.Id ?? "", teamName);
        }
        catch (InvalidOperationException ex)
        {
            Ui?.Toast("Could not start", ex.Message, ToastKind.Error);
            return false;
        }

        Messages.Clear(); Timeline.Clear(); Tasks.Clear(); AgentStates.Clear();
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
