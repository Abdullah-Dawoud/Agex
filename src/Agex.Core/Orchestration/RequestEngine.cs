using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Settings;
using Agex.Core.Agents;
using Agex.Core.Projects;
using Agex.Core.Runtime;
using Agex.Core.Sessions;

namespace Agex.Core.Orchestration;

/// <summary>
/// Runs one request: the leader plans, AGEX schedules the task graph across the
/// team (parallel where dependencies and file ownership allow), verifies claimed
/// file changes independently, relays explicit messages between agents, asks the
/// user when the leader needs a decision, and decides the final status.
/// Everything shown to the user comes from explicit agent output or from what
/// AGEX itself did; hidden model reasoning is never read or shown.
/// </summary>
public sealed partial class RequestEngine
{
    private readonly RequestOptions _options;
    private readonly AgentRegistry _registry;
    private readonly SessionStore? _store;
    private readonly GitService? _git;
    private readonly IEngineHost _host;
    private readonly AgexLog? _log;
    private readonly List<ChatItem> _chat = [];
    private readonly Dictionary<string, int> _toolEvents = new();
    private readonly List<(string From, string To, string Purpose, bool Recovered)> _fallbacks = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _failureQueue = new();
    private IEnumerable<string> Failures => _failureQueue;
    private readonly Dictionary<string, int> _runningPerAgent = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pauseLock = new();
    private TaskCompletionSource _resume = CompletedSource();
    private TeamMember _leader;
    private bool? _writesAllowed;
    private bool _snapshotTaken;
    private int _mailboxTurns;
    private List<ProjectFile> _before = [];

    /// <summary>Project text at the start of the request (memory only), for line counts and diffs.</summary>
    public TextBaseline? Baseline { get; private set; }

    public RequestEngine(RequestOptions options, AgentRegistry registry, IEngineHost host, SessionStore? store = null, GitService? git = null, AgexLog? log = null, Session? session = null)
    {
        _options = options;
        _registry = registry;
        _host = host;
        _store = store;
        _git = git;
        _log = log;
        _leader = options.Leader;
        Session = session ?? new Session();
        Session.Project = options.Project;
        Session.Request = options.Request;
        Session.Title = SessionStore.MakeTitle(options.Request);
        Session.Team = options.Team;
        Session.Leader = options.Leader.Name;
        Session.Agents = options.Members.Select(member => member.Name).ToList();
        Session.Skills = options.Skills.Select(skill => skill.Name).ToList();
        Session.ContinuedFrom = options.ContinuedFrom;
        Session.ClonedFrom = options.ClonedFrom;
        Intent = options.Intent ?? RequestClassifier.Classify(options.Request, ChatMode.Build, options.Project, options.Attachments.Count > 0);
        Session.Mode = Intent.Kind switch { RequestKind.Chat => "chat", RequestKind.Question => "ask", RequestKind.Plan => "plan", _ => "build" };
        Session.ChosenMode = options.ChosenMode.ToString().ToLowerInvariant();
        Session.Needs = RequestIntent.Each(Intent.Needs).Select(RequestIntent.CapabilityText).ToList();
        Session.Efficiency = options.Efficiency.ToString();
        // Auto skills: only those that can matter for this request (fewer tokens in every prompt).
        var context = options.Request + " " + string.Join(" ", options.Attachments.Select(item => item.Name));
        _skills = options.SkillsChosen ? options.Skills : options.Skills.Where(skill => SkillRelevance.IsRelevant(skill.Id, Intent, context)).ToList();
        _servers = options.SkillsChosen ? options.McpServers : options.McpServers.Where(server => SkillRelevance.IsRelevant(server.SkillId, Intent, context)).ToList();
        if (!options.Permissions.McpTools) _servers = [];
        if (_skills.Count != options.Skills.Count) Session.Skills = _skills.Select(skill => skill.Name).ToList();
    }

    /// <summary>How AGEX handles this request and what it needs.</summary>
    public RequestIntent Intent { get; }
    private readonly IReadOnlyList<SkillContext> _skills;
    private readonly IReadOnlyList<McpServerSpec> _servers;

    public Session Session { get; }
    public bool IsPaused { get; private set; }

    public event Action<AgentMessage>? MessageAdded;
    public event Action<TimelineEntry>? TimelineAdded;
    public event Action<TaskItem>? TaskChanged;
    public event Action<AgentLiveState>? AgentChanged;
    /// <summary>Status, question, outcome or change list changed.</summary>
    public event Action<Session>? SessionChanged;

    private sealed class ChatItem
    {
        public required AgentMessage Message { get; init; }
        public string State { get; set; } = "QUEUED";
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    /// <summary>Stops new tasks and review rounds from starting. Running agents finish their current step.</summary>
    public void Pause()
    {
        lock (_pauseLock)
        {
            if (IsPaused) return;
            IsPaused = true;
            _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        SetStatus(SessionStatus.Paused);
        AddTimeline(TimelineKind.Info, "Paused. Running agents finish their current step; nothing new starts.");
    }

    public void Resume()
    {
        lock (_pauseLock)
        {
            if (!IsPaused) return;
            IsPaused = false;
            _resume.TrySetResult();
        }
        SetStatus(SessionStatus.Running);
        AddTimeline(TimelineKind.Info, "Resumed.");
    }

    private Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        lock (_pauseLock) { return IsPaused ? _resume.Task.WaitAsync(cancellationToken) : Task.CompletedTask; }
    }

    // ================================================================= run

    public async Task<Session> RunAsync(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        _log?.Write("request_start", new { session = Session.Id, leader = _leader.Id, agents = Session.Agents, chars = _options.Request.Length });
        if (!_options.AskBeforeWrites) _writesAllowed = true;
        using (var self = System.Diagnostics.Process.GetCurrentProcess())
        {
            Session.OwnerPid = self.Id;
            Session.OwnerStarted = new DateTimeOffset(self.StartTime);
        }
        AddMessage("User", "AGEX", MessageType.Assignment, _options.Request);
        AddTimeline(TimelineKind.Start, "Request received");
        AddTimeline(TimelineKind.Info, Intent.Kind switch
        {
            RequestKind.Chat => "Quick reply: no planning needed",
            RequestKind.Question => "Answering read-only: no changes",
            RequestKind.Plan => "Plan only: no changes",
            _ => "Work request: the team plans and carries it out",
        } + (Intent.Kind == RequestKind.Build && Intent.Needs != NeededCapability.None ? " (needs: " + string.Join(", ", Session.Needs) + ")" : ""));
        if (_options.Efficiency != EfficiencyMode.Balanced) AddTimeline(TimelineKind.Info, "Efficiency: " + EfficiencyText(_options.Efficiency));
        if (_options.Attachments.Count > 0) AddTimeline(TimelineKind.Info, $"{_options.Attachments.Count} attached file(s): " + string.Join(", ", _options.Attachments.Select(item => item.Name)));
        if (_options.LocalUrl.Length > 0) AddTimeline(TimelineKind.Info, "AGEX serves the project on this computer at " + _options.LocalUrl);
        foreach (var missing in _options.Capabilities.Missing) AddTimeline(TimelineKind.Warning, $"{missing.Title}. {missing.Detail}");
        if (_options.Capabilities.Tools.Count > 0) AddTimeline(TimelineKind.Info, "Tools for this request: " + string.Join(", ", _options.Capabilities.Tools.Select(tool => tool.Name)));
        Save();

        if (Intent.Kind != RequestKind.Build)
        {
            // Chat, questions and plans change nothing: no project scan, no baseline, no planning protocol.
            var direct = ("", "", "", false);
            try { direct = await DirectRouteAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { direct = ("", "Cancelled by you.", "", false); }
            catch (Exception ex)
            {
                _log?.Error("request_engine_error", ex, new { session = Session.Id });
                direct = ("", "AGEX hit an internal error: " + ex.Message, "", false);
                _failureQueue.Enqueue(direct.Item2);
            }
            finally { foreach (var member in _options.Members) SetAgent(member, AgentWorkState.Idle, "", ""); }
            Complete(direct.Item2, direct.Item1, direct.Item4, direct.Item3, cancellationToken.IsCancellationRequested, started);
            return Session;
        }

        if (Intent.Sensitive.Count > 0 && !await ConfirmSensitiveAsync(cancellationToken).ConfigureAwait(false))
        {
            Complete("Stopped: you did not allow the sensitive actions this request involves.", "BLOCKED", true, "", false, started);
            return Session;
        }
        _before = ProjectScanner.List(_options.Project, _options.IgnoredFolders);
        Baseline = TextBaseline.Capture(_options.Project, _before);

        var reason = "The review round limit was reached before the goal was verified.";
        var leaderStatus = "";
        var leaderSucceeded = false;
        var verification = "";
        try
        {
            if (_options.Members.All(member => !member.CanReadFiles))
            {
                // Text-only teams (local models): small models do not follow the
                // planning protocol reliably, so ask for a direct answer instead.
                (leaderStatus, reason, verification, leaderSucceeded) = await DirectAnswerAsync(cancellationToken).ConfigureAwait(false);
                Complete(reason, leaderStatus, leaderSucceeded, verification, cancellationToken.IsCancellationRequested, started);
                return Session;
            }
            var round = 0;
            while (round < _options.MaxRounds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                var prompt = await BuildLeaderPromptAsync(cancellationToken).ConfigureAwait(false);
                var purpose = round == 0 ? "planning" : "the progress review";
                AddTimeline(TimelineKind.Start, round == 0 ? $"{_leader.Name} is planning" : $"{_leader.Name} is reviewing (round {round + 1})");
                SetAgent(_leader, round == 0 ? AgentWorkState.Planning : AgentWorkState.Reviewing, "", round == 0 ? "Planning the request" : "Reviewing results");
                var call = await CallAgentAsync(_leader, prompt, purpose, "", allowFallback: true, needsWrite: false, allowWrites: false, requirePlanning: true, cancellationToken).ConfigureAwait(false);
                if (!call.Result.Success)
                {
                    SetAgent(call.Member, AgentWorkState.Failed, "", call.Result.Reason);
                    reason = round == 0 ? $"No agent could plan this request. {call.Result.Reason}" : $"The leader could not review the results. {call.Result.Reason}";
                    AddTimeline(TimelineKind.Failed, reason);
                    break;
                }
                if (call.Member.Id != _leader.Id) { _leader = call.Member; lock (Session) Session.Leader = _leader.Name; }
                SetAgent(_leader, AgentWorkState.Idle, "", "Waiting");
                leaderSucceeded = true;

                LeaderPlan plan;
                try { plan = ParsePlan(call.Result.Text); }
                catch (PlanException ex)
                {
                    AddTimeline(TimelineKind.Warning, $"The leader's plan was not valid ({ex.Message}). Asking it to fix the plan once.");
                    _log?.Write("leader_plan_invalid", new { session = Session.Id, error = ex.Message, reply = Truncate(call.Result.Text, 2000) });
                    var failed = Tasks().Where(task => task.State is TaskState.Failed or TaskState.RepairRequired).Select(task => task.Id).ToList();
                    var repairPrompt = prompt + $"\n\nYour previous reply was invalid: {ex.Message}\nFix it once. Every task in [{string.Join(", ", failed)}] needs a new task whose repair_for includes its exact id. Previous reply:\n{call.Result.Text}";
                    var repair = await CallAgentAsync(_leader, repairPrompt, "plan repair", "", allowFallback: false, needsWrite: false, allowWrites: false, requirePlanning: true, cancellationToken).ConfigureAwait(false);
                    if (!repair.Result.Success) { reason = $"The leader could not repair its plan. {repair.Result.Reason}"; AddTimeline(TimelineKind.Failed, reason); break; }
                    try { plan = ParsePlan(repair.Result.Text); }
                    catch (PlanException again) { reason = $"The leader returned an invalid plan twice: {again.Message}"; AddTimeline(TimelineKind.Failed, "The leader's plan was still invalid after one repair."); break; }
                }

                reason = plan.Reason;
                leaderStatus = plan.Status.ToString().ToUpperInvariant();
                if (plan.Status == GoalStatus.Complete)
                {
                    if (Tasks().Any(task => task.State is not (TaskState.Done or TaskState.Skipped)))
                    {
                        reason = "The leader claimed completion while some tasks were unfinished or unrepaired.";
                        leaderStatus = "REJECTED";
                        AddTimeline(TimelineKind.Warning, reason);
                        break;
                    }
                    verification = plan.Verification;
                    AddMessage(_leader.Name, "User", MessageType.Result, plan.Reason + (verification.Length > 0 ? "\n\nVerification: " + verification : ""));
                    break;
                }
                if (plan.Status == GoalStatus.Blocked)
                {
                    AddMessage(_leader.Name, "User", MessageType.Status, "Blocked: " + plan.Reason);
                    AddTimeline(TimelineKind.Failed, "The leader reported the request is blocked: " + plan.Reason);
                    break;
                }
                if (plan.Status == GoalStatus.NeedsInput)
                {
                    var answer = await AskUserAsync(plan.Question, cancellationToken).ConfigureAwait(false);
                    if (answer is null) { reason = "Stopped: the leader needed an answer from you."; leaderStatus = "BLOCKED"; break; }
                    continue; // asking does not use up a review round
                }

                AddPlannedTasks(plan);
                await RunTaskGraphAsync(cancellationToken).ConfigureAwait(false);
                await DeliverMessagesAsync(cancellationToken).ConfigureAwait(false);
                UpdateChanges();
                Save();
                round++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reason = "Cancelled by you.";
        }
        catch (Exception ex)
        {
            // An engine bug must end the request cleanly, not crash the app.
            _log?.Error("request_engine_error", ex, new { session = Session.Id });
            reason = "AGEX hit an internal error: " + ex.Message;
            _failureQueue.Enqueue(reason);
        }
        finally
        {
            foreach (var member in _options.Members) SetAgent(member, AgentWorkState.Idle, "", "");
        }

        Complete(reason, leaderStatus, leaderSucceeded, verification, cancellationToken.IsCancellationRequested, started);
        return Session;
    }

    private LeaderPlan ParsePlan(string text) => LeaderPlanParser.Parse(text, Tasks(), ResolveExecutor);

    private string? ResolveExecutor(string name)
    {
        var member = _options.Members.FirstOrDefault(item => item.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) || item.Id.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        return member?.Id;
    }

    private TeamMember Member(string id) => _options.Members.First(member => member.Id == id);

    private List<TaskItem> Tasks() { lock (Session) return Session.Tasks.ToList(); }

    private async Task<string?> AskUserAsync(string question, CancellationToken cancellationToken)
    {
        var pending = new PendingQuestion { From = _leader.Name, Question = question };
        lock (Session) Session.Question = pending;
        AddMessage(_leader.Name, "User", MessageType.Question, question);
        AddTimeline(TimelineKind.Input, "Waiting for your answer");
        SetStatus(SessionStatus.WaitingForInput);
        Save();
        var answer = await _host.AskUserAsync(pending, cancellationToken).ConfigureAwait(false);
        lock (Session) Session.Question = null;
        if (string.IsNullOrWhiteSpace(answer))
        {
            SetStatus(SessionStatus.Running);
            AddTimeline(TimelineKind.Warning, "No answer was given.");
            return null;
        }
        lock (Session) Session.Answers.Add($"Q: {question}\nA: {answer}");
        AddMessage("User", _leader.Name, MessageType.Answer, answer);
        AddTimeline(TimelineKind.Input, "You answered");
        SetStatus(SessionStatus.Running);
        Save();
        return answer;
    }

    private async Task<(string Status, string Reason, string Verification, bool Succeeded)> DirectAnswerAsync(CancellationToken cancellationToken)
    {
        AddTimeline(TimelineKind.Start, $"{_leader.Name} is answering");
        var builder = new StringBuilder();
        builder.AppendLine("Answer the request below directly, clearly and concisely. If you are not sure, say so.");
        if (_options.ProjectInstructions.Length > 0) builder.AppendLine("Instructions from the user:").AppendLine(_options.ProjectInstructions);
        if (_options.PreviousContext.Length > 0) builder.AppendLine("Earlier conversation:").AppendLine(_options.PreviousContext);
        AppendTeamAndStyle(builder);
        builder.AppendLine(Agex.Core.Attachments.AttachmentService.PromptSection(_options.Attachments, _leader.CanReadFiles));
        builder.AppendLine(ContextPack(new TaskItem { Objective = _options.Request }));
        builder.AppendLine("REQUEST:").AppendLine(_options.Request);
        var call = await CallAgentAsync(_leader, builder.ToString(), "the answer", "", allowFallback: true, needsWrite: false, allowWrites: false, requirePlanning: false, cancellationToken).ConfigureAwait(false);
        if (!call.Result.Success)
        {
            var reason = $"No agent could answer. {call.Result.Reason}";
            AddTimeline(TimelineKind.Failed, reason);
            return ("", reason, "", false);
        }
        var model = call.Member.Model ?? "default model";
        AddMessage(call.Member.Name, "User", MessageType.Result, call.Result.Text);
        return ("COMPLETE", call.Result.Text, $"Answered directly by {call.Member.Name} ({model}). This is a text answer; AGEX cannot check it against the project.", true);
    }

    /// <summary>Marks prompts of the direct routes (chat, questions, plans); the reply is plain text, not the planning JSON.</summary>
    internal const string DirectMarker = "AGEX direct reply.";

    /// <summary>
    /// Chat, questions and plans: one agent, one read-only run, a small prompt.
    /// No team configuration, no project map and only skills that matter.
    /// </summary>
    private async Task<(string Status, string Reason, string Verification, bool Succeeded)> DirectRouteAsync(CancellationToken cancellationToken)
    {
        var kind = Intent.Kind;
        var needsFiles = kind != RequestKind.Chat;
        var responder = PickResponder(needsFiles);
        if (responder.Id != _leader.Id) { _leader = responder; lock (Session) Session.Leader = responder.Name; }
        var label = kind switch { RequestKind.Chat => "the reply", RequestKind.Question => "the answer", _ => "the plan" };
        AddTimeline(TimelineKind.Start, kind switch
        {
            RequestKind.Chat => $"{responder.Name} is replying",
            RequestKind.Question => $"{responder.Name} is answering",
            _ => $"{responder.Name} is writing the plan",
        });
        SetAgent(responder, kind == RequestKind.Plan ? AgentWorkState.Planning : AgentWorkState.Answering, "", kind == RequestKind.Plan ? "Writing the plan" : "Answering");
        var prompt = BuildDirectPrompt(kind, responder);
        var call = await CallAgentAsync(responder, prompt, label, "", allowFallback: true, needsWrite: false, allowWrites: false, requirePlanning: false, cancellationToken, direct: true).ConfigureAwait(false);
        if (!call.Result.Success)
        {
            var reason = $"No agent could give {label}. {call.Result.Reason}";
            AddTimeline(TimelineKind.Failed, reason);
            return ("", reason, "", false);
        }
        var text = PlainReply(call.Result.Text);
        AddMessage(call.Member.Name, "User", MessageType.Result, text);
        AddTimeline(TimelineKind.Done, kind switch { RequestKind.Chat => "Replied", RequestKind.Question => "Answered", _ => "Plan ready" });
        var how = kind switch
        {
            RequestKind.Chat => $"Quick reply by {call.Member.Name}. Nothing was read or changed.",
            RequestKind.Question => $"Answered by {call.Member.Name} with read-only access{(call.Member.CanReadFiles ? " to the project" : " to the files AGEX included")}. Nothing was changed.",
            _ => $"Plan written by {call.Member.Name}. Nothing was changed. Use Build this plan to carry it out.",
        };
        return ("COMPLETE", text, how, true);
    }

    /// <summary>The leader, unless the reply needs project files and the leader cannot read them.</summary>
    private TeamMember PickResponder(bool needsFiles)
    {
        var healthy = _options.Members.Where(member => _registry.Health(member.Id).Healthy).ToList();
        if (healthy.Count == 0) return _leader;
        if (healthy.Any(member => member.Id == _leader.Id) && (!needsFiles || _leader.CanReadFiles)) return _leader;
        return healthy.FirstOrDefault(member => !needsFiles || member.CanReadFiles) ?? healthy[0];
    }

    private string BuildDirectPrompt(RequestKind kind, TeamMember member)
    {
        var builder = new StringBuilder();
        builder.AppendLine(DirectMarker);
        var model = member.Model is { Length: > 0 } chosen ? $" using the model {chosen}" : " using its default model";
        builder.AppendLine($"You are the assistant inside AGEX, a desktop app where the user works with AI agents. You are {member.Name}{model}.");
        switch (kind)
        {
            case RequestKind.Chat:
                builder.AppendLine("Reply to the user's message briefly and naturally, like a chat app. Do not read, run or change anything.");
                builder.AppendLine("If the user wants work done, say what you can do and invite them to describe the task.");
                builder.AppendLine($"The open project is \"{Path.GetFileName(_options.Project.TrimEnd('/', '\\'))}\".");
                break;
            case RequestKind.Question:
                builder.AppendLine("Answer the user's question about the project in the folder below. Read only the files you need (start with the files the question names, or the README). Do not change anything.");
                builder.AppendLine("Reply in plain text or Markdown, concise and specific. Say what you read. If you are not sure, say so.");
                break;
            default:
                builder.AppendLine("Write a clear plan for the request below. Inspect the project first, reading only what you need. Do not change files and do not run anything that changes files.");
                builder.AppendLine("Use these headings: Goal, What I found, Steps (numbered; name the files each step touches), Risks and questions, How to check it works. Keep it practical and short.");
                builder.AppendLine("Agents available to carry out the plan: " + string.Join(", ", _options.Members.Select(item => item.Name)) + ".");
                break;
        }
        if (kind != RequestKind.Chat)
        {
            builder.AppendLine($"PROJECT FOLDER (use only this folder): {_options.Project}");
            if (OperatingSystem.IsWindows()) builder.AppendLine(WindowsEncodingHint);
            if (_options.LocalUrl.Length > 0) builder.AppendLine($"The project is served on this computer at {_options.LocalUrl} (use this address, not file://).");
        }
        if (_options.ProjectInstructions.Length > 0 && kind != RequestKind.Chat) builder.AppendLine("PROJECT INSTRUCTIONS FROM THE USER:").AppendLine(_options.ProjectInstructions);
        if (_options.PreviousContext.Length > 0) builder.AppendLine("EARLIER IN THIS CONVERSATION:").AppendLine(Truncate(_options.PreviousContext, kind == RequestKind.Chat ? 1500 : 4000));
        if (_options.Efficiency == EfficiencyMode.SaveTokens) builder.AppendLine("Keep the reply short.");
        if (_options.Attachments.Count > 0) builder.AppendLine(Agex.Core.Attachments.AttachmentService.PromptSection(_options.Attachments, member.CanReadFiles));
        if (kind != RequestKind.Chat && !member.CanReadFiles) builder.AppendLine(ContextPack(new TaskItem { Objective = _options.Request }));
        builder.AppendLine(kind == RequestKind.Plan ? "REQUEST TO PLAN:" : "USER:").AppendLine(_options.Request);
        return builder.ToString();
    }

    /// <summary>Agents sometimes wrap a direct reply in the planning JSON; show the text inside.</summary>
    private static string PlainReply(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('{')) return trimmed;
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            foreach (var name in new[] { "reason", "result", "answer", "response" })
                if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } inner) return inner.Trim();
        }
        catch (JsonException) { }
        return trimmed;
    }

    /// <summary>Asks once before a request that mentions payments, sending messages, deleting data or other sensitive actions.</summary>
    private async Task<bool> ConfirmSensitiveAsync(CancellationToken cancellationToken)
    {
        SetStatus(SessionStatus.WaitingForApproval);
        AddTimeline(TimelineKind.Approval, "Waiting for your approval: this request involves sensitive actions");
        var decision = await _host.RequestApprovalAsync(new ApprovalRequest(
            "This request involves sensitive actions",
            "It may involve: " + string.Join("; ", Intent.Sensitive) + ". Agents will still ask you before each one. Continue?",
            _options.Members.Select(member => member.Name).ToList()), cancellationToken).ConfigureAwait(false);
        SetStatus(SessionStatus.Running);
        var allowed = decision != ApprovalDecision.Deny;
        AddTimeline(TimelineKind.Approval, allowed ? "You allowed the request to continue" : "You stopped the request");
        return allowed;
    }

    // ============================================================ prompts

    private async Task<string> BuildLeaderPromptAsync(CancellationToken cancellationToken)
    {
        var save = _options.Efficiency == EfficiencyMode.SaveTokens;
        // A leader that reads files lists them itself: it gets a short map without dates. Maximum quality and
        // text-only leaders get the full list with dates.
        var full = _options.Efficiency == EfficiencyMode.MaximumQuality || !_leader.CanReadFiles;
        var files = ProjectScanner.List(_options.Project, _options.IgnoredFolders, maxFiles: full ? 300 : save ? 80 : 150)
            .Select(file => full ? (object)new { path = file.RelativePath, size = file.Size, modified_utc = file.ModifiedUtc.ToString("o") } : file.RelativePath);
        var gitStatus = _git is not null && _git.IsRepository(_options.Project) ? await _git.StatusAsync(_options.Project, cancellationToken).ConfigureAwait(false) : [];
        var tasks = Tasks().Select(task => new
        {
            id = task.Id, title = task.Title, executor = Member(task.Agent).Name, status = TaskStateCode(task.State),
            result = Truncate(ExecutorReply.ResultText(task.Result), save ? 1500 : 4000), verification = task.Verification, error = task.Error,
        });
        List<object> operational;
        lock (Session) operational = _chat.TakeLast(save ? 20 : 40).Select(item => (object)new { from = item.Message.From, to = item.Message.To, type = item.Message.Type.ToString().ToUpperInvariant(), task = item.Message.TaskId, content = Truncate(item.Message.Text, save ? 600 : 1500), state = item.State }).ToList();
        List<string> answers;
        lock (Session) answers = Session.Answers.ToList();

        var agents = string.Join("\n", _options.Members.Select(DescribeMember));
        var builder = new StringBuilder();
        builder.AppendLine("You are the leader of a small team of AI agents coordinated by AGEX. You plan the work, assign tasks to the team, and check results against the user's goal.");
        if (_leader.CanReadFiles)
        {
            builder.AppendLine("You have read access to the project folder. Before you answer, open and read the files you need with your own tools; do not guess and do not rely only on the file list below.");
            builder.AppendLine("If the goal is a question you can answer by reading the project, read the files now and return COMPLETE with the answer itself in reason, what you read in verification, and no tasks.");
        }
        else
        {
            builder.AppendLine("You cannot open files. Answer from the evidence below, or assign a reading task to an agent that can read files.");
        }
        builder.AppendLine("Changes to files are always done through tasks. Tasks without dependencies run in parallel; use dependencies for order. List the files each task will change in affected_files; such tasks need an agent that can edit files.");
        builder.AppendLine("Claims made by agents are not proof. A task with status FAILED or REPAIR_REQUIRED is not complete: create a repair task whose repair_for lists its id. Return COMPLETE only for results you have checked.");
        if (OperatingSystem.IsWindows()) builder.AppendLine(WindowsEncodingHint);
        builder.AppendLine("Return NEEDS_INPUT with one clear question only when you cannot continue without a decision from the user.");
        builder.AppendLine("Do not carry out the work yourself while planning: running programs or servers, opening a browser, playing or clicking, and controlling the computer are done in tasks, by an agent listed with that ability below. Never ask the user to enable access; AGEX has already given each agent the tools listed.");
        builder.AppendLine("Return ONLY JSON: {\"goal_status\":\"CONTINUE|COMPLETE|BLOCKED|NEEDS_INPUT\",\"reason\":\"short explanation; for a question, the answer\",\"question\":\"only for NEEDS_INPUT\",\"verification\":\"evidence required for COMPLETE\",\"tasks\":[{\"id\":\"unique-id\",\"title\":\"short title\",\"objective\":\"assignment\",\"executor\":\"agent name\",\"dependencies\":[],\"affected_files\":[],\"repair_for\":[]}]}");
        builder.AppendLine($"At most {_options.MaxRounds} review rounds; no limit on task count.");
        builder.AppendLine($"PROJECT FOLDER: {_options.Project}");
        builder.AppendLine("Work only in this folder. Every relative path below is inside it. Do not search other folders or use a scratch or default workspace.");
        builder.AppendLine("TEAM:").AppendLine(agents);
        builder.Append(CapabilitySection());
        builder.AppendLine("ROUTING PREFERENCE: " + _options.RoutingGuidance);
        if (_options.ProjectInstructions.Length > 0) builder.AppendLine("PROJECT INSTRUCTIONS FROM THE USER:").AppendLine(_options.ProjectInstructions);
        if (_options.PreviousContext.Length > 0) builder.AppendLine("EARLIER SESSION THIS REQUEST CONTINUES:").AppendLine(_options.PreviousContext);
        AppendTeamAndStyle(builder);
        builder.AppendLine("ORIGINAL USER GOAL:").AppendLine(_options.Request);
        if (_options.Attachments.Count > 0) builder.AppendLine(Agex.Core.Attachments.AttachmentService.PromptSection(_options.Attachments, _leader.CanReadFiles));
        if (answers.Count > 0) builder.AppendLine("USER ANSWERS:").AppendLine(string.Join("\n", answers));
        builder.AppendLine("INDEPENDENT PROJECT EVIDENCE (collected by AGEX):");
        builder.AppendLine(JsonSerializer.Serialize(new { files, git_status = gitStatus }, Json.Compact));
        builder.AppendLine("TASK RESULTS:").AppendLine(JsonSerializer.Serialize(tasks, Json.Compact));
        builder.AppendLine("MESSAGES BETWEEN AGENTS:").AppendLine(JsonSerializer.Serialize(operational, Json.Compact));
        return builder.ToString();
    }

    /// <summary>The job team's brief and approval rules, and the answer style for Save tokens.</summary>
    private void AppendTeamAndStyle(StringBuilder builder)
    {
        if (_options.TeamBrief.Length > 0) builder.AppendLine("TEAM BRIEF (the kind of work the user chose; follow its rules):").AppendLine(_options.TeamBrief);
        if (_options.Efficiency == EfficiencyMode.SaveTokens) builder.AppendLine("Keep every reply short: do not restate the task, do not repeat file contents, report only what changed and what you checked.");
    }

    internal static string EfficiencyText(EfficiencyMode mode) => mode switch
    {
        EfficiencyMode.MaximumQuality => "Maximum quality (full context, higher reasoning effort)",
        EfficiencyMode.SaveTokens => "Save tokens (shorter context, brief answers, lower reasoning effort)",
        EfficiencyMode.LocalFirst => "Local-first (local models whenever they can do the work)",
        _ => "Balanced",
    };

    /// <summary>Quick replies use the agent's lowest effort when it has one (the user's explicit choice still wins).</summary>
    private static string? LowestEffort(TeamMember member) => member.Effort ?? (member.Adapter.ModelSettings.ReasoningEfforts.Contains("low") ? "low" : null);

    /// <summary>Default reasoning effort for the efficiency mode when the user left it at the agent's default.</summary>
    private string? EffortFor(TeamMember member) => member.Effort ?? (member.Adapter is CodexAdapter or AntigravityAdapter
        ? _options.Efficiency switch { EfficiencyMode.SaveTokens => "low", EfficiencyMode.MaximumQuality => "high", _ => null }
        : null);

    /// <summary>A skill pinned to some agents goes only to them; other skills go to every compatible agent.</summary>
    internal bool SkillFor(string skillId, TeamMember member) =>
        skillId.Length == 0 || !_options.SkillAgents.TryGetValue(skillId, out var agents) || agents.Count == 0 || agents.Contains(member.Id);

    private string DescribeMember(TeamMember member)
    {
        var health = _registry.Health(member.Id);
        if (!health.Healthy) return $"- {member.Name}: UNAVAILABLE right now. Do not assign tasks to it.";
        return $"- {member.Name}: {CapabilityRouting.Abilities(member, _options.Capabilities, _options.AllowCommands)}. {member.Adapter.Description}";
    }

    /// <summary>What this request needs, what AGEX provides for it, and what agents must never do without asking.</summary>
    private string CapabilitySection()
    {
        var builder = new StringBuilder();
        var plan = _options.Capabilities;
        if (Intent.Needs != NeededCapability.None) builder.AppendLine("THIS REQUEST NEEDS: " + string.Join(", ", Session.Needs) + ".");
        if (_options.LocalUrl.Length > 0)
            builder.AppendLine($"LOCAL WEB: AGEX serves the project folder at {_options.LocalUrl} (a web server on this computer, already running). Open pages there, never with file:// (browsers block scripts and modules from file://).");
        if (Intent.Wants(NeededCapability.Browser) && plan.BrowserAgents.Count > 0)
            builder.AppendLine("BROWSER: give tasks that open, test or play pages to " + string.Join(" or ", plan.BrowserAgents.Select(id => _options.Members.FirstOrDefault(member => member.Id == id)?.Name ?? id)) + ". They use a real browser; screenshots and clicks work there.");
        if (Intent.Wants(NeededCapability.ComputerControl) && plan.ComputerAgents.Count > 0)
            builder.AppendLine("COMPUTER: " + string.Join(" or ", plan.ComputerAgents.Select(id => _options.Members.FirstOrDefault(member => member.Id == id)?.Name ?? id)) + " can see the screen and use the mouse and keyboard. Prefer it for work in desktop programs.");
        foreach (var missing in plan.Missing) builder.AppendLine($"NOT AVAILABLE: {missing.Title}. Plan around it or return BLOCKED and say exactly this.");
        builder.AppendLine(SensitiveRules());
        return builder.ToString();
    }

    /// <summary>Actions agents must never take without the user's explicit yes, whatever the approval mode.</summary>
    private string SensitiveRules()
    {
        var never = new List<string> { "paying or buying anything", "changing passwords, keys, tokens or account and security settings", "publishing, releasing or submitting forms" };
        never.Add(_options.Permissions.ExternalCommunication ? "sending an email or message (ask first each time)" : "sending emails or messages (not allowed in this request)");
        never.Add(_options.Permissions.DestructiveActions ? "deleting significant data (ask first each time)" : "deleting files or data beyond what the task changes (not allowed in this request)");
        return "ALWAYS ASK THE USER FIRST (return NEEDS_INPUT, or send a QUESTION to User) before: " + string.Join("; ", never) + ".";
    }

    private string BuildExecutorPrompt(TaskItem task, TeamMember member, string mail)
    {
        string context;
        lock (Session)
            context = JsonSerializer.Serialize(Session.Tasks.Where(item => item.State == TaskState.Done).Select(item => new { id = item.Id, result = Truncate(ExecutorReply.ResultText(item.Result), _options.Efficiency == EfficiencyMode.SaveTokens ? 800 : 2000) }), Json.Compact);
        var builder = new StringBuilder();
        builder.AppendLine($"PROJECT FOLDER (use only this folder): {_options.Project}");
        builder.AppendLine("ORIGINAL USER GOAL:").AppendLine(_options.Request);
        builder.AppendLine("YOUR ASSIGNMENT:").AppendLine(task.Title.Length > 0 && task.Title != task.Objective ? task.Title + "\n" + task.Objective : task.Objective);
        builder.AppendLine("FILES YOU OWN: " + (task.AffectedFiles.Count > 0 ? string.Join(", ", task.AffectedFiles) : "(none; do not change files)"));
        if (_options.ProjectInstructions.Length > 0) builder.AppendLine("PROJECT INSTRUCTIONS:").AppendLine(_options.ProjectInstructions);
        AppendTeamAndStyle(builder);
        if (_options.Attachments.Count > 0) builder.AppendLine(Agex.Core.Attachments.AttachmentService.PromptSection(_options.Attachments, member.CanReadFiles));
        builder.AppendLine("RESULTS OF EARLIER TASKS: " + context);
        builder.AppendLine("MESSAGES TO YOU: " + (mail.Length > 0 ? mail : "none"));
        if (!member.CanReadFiles) builder.AppendLine(ContextPack(task));
        if (_options.LocalUrl.Length > 0) builder.AppendLine($"LOCAL WEB: AGEX serves the project at {_options.LocalUrl} on this computer. Open pages there, never with file://.");
        if (UsesTools(member) is { Count: > 0 } tools)
            builder.AppendLine("TOOLS AGEX GAVE YOU FOR THIS TASK: " + string.Join(", ", tools.Select(tool => tool.Kind == ToolServerKind.Browser ? tool.Name + " (a real browser: open pages, click, type, press keys, take screenshots)" : tool.Name + " (see the screen, move the mouse, click and type)")) + ". Use them instead of guessing from the source.");
        else if (CapabilityRouting.HasNativeBrowser(member) && _options.Capabilities.BrowserAgents.Contains(member.Id))
            builder.AppendLine("Use your built-in browser to open and test pages.");
        builder.AppendLine(SensitiveRules());
        builder.AppendLine("Stay within your assignment and the files you own. Before your final reply, check every file you claim to have changed.");
        if (OperatingSystem.IsWindows()) builder.AppendLine(WindowsEncodingHint);
        builder.AppendLine("Reply with ONLY JSON: {\"result\":\"what you actually did, what you checked, and any blocker\",\"messages\":[{\"to\":\"agent name or User\",\"type\":\"QUESTION|ANSWER|REQUEST|RESULT|BLOCKER|HANDOFF|REVIEW\",\"content\":\"short message\"}]}. The messages array is optional; AGEX delivers messages.");
        return builder.ToString();
    }

    /// <summary>
    /// Windows PowerShell 5.1 reads UTF-8 files without a BOM as the ANSI code
    /// page, so agents that read files through it see garbled non-English text.
    /// </summary>
    private const string WindowsEncodingHint = "Text files are UTF-8. When you read or write files with Windows PowerShell, always pass -Encoding UTF8 (the default garbles non-English text).";

    [GeneratedRegex(@"[A-Za-z0-9_.\-]+(?:[/\\][A-Za-z0-9_.\-]+)*\.[A-Za-z0-9]{1,10}")]
    private static partial Regex FileMention();

    /// <summary>For text-only agents: contents of project files the task mentions, bounded.</summary>
    private string ContextPack(TaskItem task)
    {
        var builder = new StringBuilder("You cannot open files. Relevant project files are included below.\n");
        var budget = 60_000;
        var names = task.AffectedFiles.Concat(FileMention().Matches(task.Objective + " " + _options.Request).Select(match => match.Value)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (!ProjectScanner.IsInside(_options.Project, name)) continue;
            var full = Path.Combine(_options.Project, name);
            try
            {
                if (!File.Exists(full) || new FileInfo(full).Length > budget) continue;
                var text = File.ReadAllText(full);
                if (text.Contains('\0')) continue;
                builder.AppendLine($"--- FILE {name} ---").AppendLine(text).AppendLine("--- END FILE ---");
                budget -= text.Length;
                if (budget <= 0) break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return builder.ToString();
    }

    // ======================================================== task graph

    private void AddPlannedTasks(LeaderPlan plan)
    {
        foreach (var planned in plan.Tasks)
        {
            var task = new TaskItem
            {
                Id = planned.Id, OriginalId = planned.OriginalId, Aliases = planned.Aliases, Title = planned.Title, Objective = planned.Objective,
                Agent = planned.Agent, Dependencies = planned.Dependencies, AffectedFiles = planned.AffectedFiles, RepairFor = planned.RepairFor,
                State = TaskState.Queued, Note = "Assigned by " + _leader.Name,
            };
            lock (Session) Session.Tasks.Add(task);
            RaiseTask(task);
        }
        if (plan.Reason.Length > 0) AddMessage(_leader.Name, "AGEX", MessageType.Status, plan.Reason);
        foreach (var planned in plan.Tasks)
        {
            var type = planned.RepairFor.Count > 0 ? MessageType.Revision : MessageType.Assignment;
            var text = planned.Title.Length > 0 && planned.Title != planned.Objective ? planned.Title + "\n" + planned.Objective : planned.Objective;
            AddMessage(_leader.Name, Member(planned.Agent).Name, type, text, planned.Id);
        }
        AddTimeline(TimelineKind.Done, $"Plan ready: {plan.Tasks.Count} {(plan.Tasks.Count == 1 ? "task" : "tasks")}");
        foreach (var planned in plan.Tasks)
        {
            var label = planned.RepairFor.Count > 0 ? "Revision requested" : "Task";
            AddTimeline(planned.RepairFor.Count > 0 ? TimelineKind.Warning : TimelineKind.Info, $"{label} {Number(planned.Id)} → {Member(planned.Agent).Name}: {(planned.Title.Length > 0 ? planned.Title : Truncate(planned.Objective, 80))}", planned.Id);
        }
        Save();
    }

    private static string Number(string taskId) => Regex.Match(taskId, @"(\d+)$") is { Success: true } match ? "#" + int.Parse(match.Groups[1].Value) : taskId;

    private bool DependenciesSatisfied(TaskItem task)
    {
        foreach (var dependency in task.Dependencies)
        {
            var prior = Tasks().FirstOrDefault(item => item.Id == dependency);
            if (prior is null) return false;
            if (prior.State is TaskState.Done) continue;
            // A repair may run against the failed task it repairs.
            if (prior.State is TaskState.RepairRequired or TaskState.Failed && task.RepairFor.Contains(dependency)) continue;
            return false;
        }
        return true;
    }

    private bool PathConflict(TaskItem a, TaskItem b)
    {
        if (a.AffectedFiles.Count == 0 || b.AffectedFiles.Count == 0) return a.NeedsWrite && b.NeedsWrite;
        foreach (var left in a.AffectedFiles)
            foreach (var right in b.AffectedFiles)
            {
                if (left.Contains('*') || right.Contains('*')) return true;
                try
                {
                    var x = Path.GetFullPath(Path.Combine(_options.Project, left)).TrimEnd('/', '\\').ToLowerInvariant();
                    var y = Path.GetFullPath(Path.Combine(_options.Project, right)).TrimEnd('/', '\\').ToLowerInvariant();
                    if (x == y || x.StartsWith(y + Path.DirectorySeparatorChar) || y.StartsWith(x + Path.DirectorySeparatorChar)) return true;
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return true; }
            }
        return false;
    }

    private async Task RunTaskGraphAsync(CancellationToken cancellationToken)
    {
        var running = new Dictionary<Task<(AgentCall Call, bool Allowed)>, TaskItem>();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pending = Tasks().Where(task => task.State == TaskState.Queued).ToList();
                if (pending.Count == 0 && running.Count == 0) break;
                var startedAny = false;
                if (!IsPaused)
                {
                    foreach (var task in pending)
                    {
                        if (running.Count >= _options.MaxParallel) break;
                        if (!DependenciesSatisfied(task)) continue;
                        if (!EnsureTaskAgent(task)) continue;
                        var member = Member(task.Agent);
                        if (_runningPerAgent.GetValueOrDefault(member.Id) >= member.Adapter.MaxConcurrentRuns) continue;
                        if (running.Values.Any(other => PathConflict(task, other))) continue;
                        if (task.NeedsWrite && !await EnsureWritesAllowedAsync(cancellationToken).ConfigureAwait(false))
                        {
                            UpdateTask(task, TaskState.Skipped, error: "Not run: you did not allow file changes for this request.");
                            AddTimeline(TimelineKind.Warning, $"Skipped {Number(task.Id)}: file changes were not allowed.", task.Id);
                            continue;
                        }
                        if (!await EnsureTaskAllowedAsync(task, member, cancellationToken).ConfigureAwait(false))
                        {
                            UpdateTask(task, TaskState.Skipped, error: "Not run: you did not allow this task.");
                            AddTimeline(TimelineKind.Warning, $"Skipped {Number(task.Id)}: you did not allow it.", task.Id);
                            continue;
                        }
                        _runningPerAgent[member.Id] = _runningPerAgent.GetValueOrDefault(member.Id) + 1;
                        running[RunTaskAsync(task, cancellationToken)] = task;
                        startedAny = true;
                    }
                }
                if (running.Count == 0)
                {
                    if (IsPaused) { await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false); continue; }
                    if (!startedAny)
                    {
                        foreach (var task in Tasks().Where(task => task.State == TaskState.Queued))
                            UpdateTask(task, TaskState.Waiting, error: "Did not run: a task it depends on did not finish.");
                        break;
                    }
                    continue;
                }
                var finished = await Task.WhenAny(running.Keys).ConfigureAwait(false);
                var finishedTask = running[finished];
                running.Remove(finished);
                var (call, _) = await finished.ConfigureAwait(false);
                _runningPerAgent[Member(finishedTask.Agent).Id] = Math.Max(0, _runningPerAgent.GetValueOrDefault(Member(finishedTask.Agent).Id) - 1);
                if (call.Member.Id != finishedTask.Agent)
                {
                    lock (Session) { finishedTask.Note = $"Ran on {call.Member.Name} after {Member(finishedTask.Agent).Name} failed"; finishedTask.Agent = call.Member.Id; }
                }
                FinishTask(finishedTask, call);
            }
        }
        finally
        {
            if (running.Count > 0)
            {
                try { await Task.WhenAll(running.Keys).WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); } catch (Exception) { }
                foreach (var task in running.Values)
                    UpdateTask(task, cancellationToken.IsCancellationRequested ? TaskState.Cancelled : TaskState.Failed,
                        error: cancellationToken.IsCancellationRequested ? "Cancelled by you." : "Stopped: the scheduler ended unexpectedly.");
            }
        }
    }

    /// <summary>Reassigns a task once when its agent is unavailable; fails it clearly when no agent can do it.</summary>
    private bool EnsureTaskAgent(TaskItem task)
    {
        var member = Member(task.Agent);
        var health = _registry.Health(member.Id);
        var fitsWrite = !task.NeedsWrite || member.CanWrite;
        if (health.Healthy && fitsWrite) return true;
        var why = !health.Healthy ? $"{member.Name} is unavailable: {health.Reason}" : $"{member.Name} cannot edit files";
        var alternative = FallbackFor(member, task.NeedsWrite, requirePlanning: false);
        if (alternative is not null)
        {
            lock (Session) { task.Note = $"Reassigned from {member.Name}: {why}"; task.Agent = alternative.Id; }
            lock (_fallbacks) _fallbacks.Add((member.Name, alternative.Name, Number(task.Id), false));
            AddTimeline(TimelineKind.Fallback, $"{why}. {alternative.Name} runs {Number(task.Id)}.", task.Id);
            AddMessage("AGEX", alternative.Name, MessageType.System, $"{why}. Task {Number(task.Id)} was reassigned to {alternative.Name}.", task.Id);
            RaiseTask(task);
            return true;
        }
        UpdateTask(task, TaskState.Failed, error: $"Not started: {why}, and no other agent can do it.");
        _failureQueue.Enqueue(task.Error);
        AddTimeline(TimelineKind.Failed, $"Not started: {task.Label}. {why}.", task.Id);
        return false;
    }

    private bool? _toolsAllowed;

    /// <summary>
    /// Ask every time: one question per task (what the agent will be able to do).
    /// Smart: once per request before an agent takes over the mouse and keyboard,
    /// or uses a tool you set to "ask each time". Trust this session: never.
    /// </summary>
    private async Task<bool> EnsureTaskAllowedAsync(TaskItem task, TeamMember member, CancellationToken cancellationToken)
    {
        var tools = UsesTools(member);
        var abilities = new List<string>();
        if (member.CanWrite && task.NeedsWrite) abilities.Add("change " + string.Join(", ", task.AffectedFiles.Take(5)));
        if (_options.AllowCommands && member.CanReadFiles) abilities.Add("run commands");
        foreach (var tool in tools) abilities.Add(tool.Kind == ToolServerKind.Browser ? "use a browser (" + tool.Name + ")" : "control the mouse and keyboard (" + tool.Name + ")");
        string? title = null;
        if (_options.ApprovalMode == ApprovalMode.AskEveryTime && abilities.Count > 0) title = $"Allow {member.Name} to run {Number(task.Id)}?";
        else if (_toolsAllowed is null && _options.ApprovalMode != ApprovalMode.TrustSession
            && tools.Any(tool => tool.AskFirst || tool.Kind == ToolServerKind.Computer && ApprovalRules.MustAsk(_options.ApprovalMode, ActionKind.ComputerControl, false, false)))
            title = $"Allow {member.Name} to use {string.Join(" and ", tools.Select(tool => tool.Name))}?";
        if (title is null) return _toolsAllowed ?? true;
        SetStatus(SessionStatus.WaitingForApproval);
        AddTimeline(TimelineKind.Approval, "Waiting for your approval: " + title, task.Id);
        var detail = $"{task.Label}\n\nIt will be able to: {string.Join("; ", abilities)}."
            + (tools.Any(tool => tool.Kind == ToolServerKind.Computer) ? " You can take over or stop at any time from the Computer view." : "");
        var decision = await _host.RequestApprovalAsync(new ApprovalRequest(title, detail, [member.Name]), cancellationToken).ConfigureAwait(false);
        SetStatus(SessionStatus.Running);
        var allowed = decision != ApprovalDecision.Deny;
        if (_options.ApprovalMode != ApprovalMode.AskEveryTime && tools.Count > 0) _toolsAllowed = allowed;
        AddTimeline(TimelineKind.Approval, allowed ? "You allowed it" : "You did not allow it", task.Id);
        return allowed;
    }

    private async Task<bool> EnsureWritesAllowedAsync(CancellationToken cancellationToken)
    {
        if (_writesAllowed is { } decided && _options.ApprovalMode != ApprovalMode.AskEveryTime) return decided;
        if (_writesAllowed == true) return true;
        if (_options.AskBeforeWrites)
        {
            SetStatus(SessionStatus.WaitingForApproval);
            AddTimeline(TimelineKind.Approval, "Waiting for your approval to change files");
            var writers = _options.Members.Where(member => member.CanWrite).Select(member => member.Name).ToList();
            var decision = await _host.RequestApprovalAsync(new ApprovalRequest(
                "Allow agents to change files?",
                $"The plan changes files in the project \"{Path.GetFileName(_options.Project.TrimEnd('/', '\\'))}\"." + (_git?.IsRepository(_options.Project) == true && _options.SnapshotBeforeWrites ? " AGEX saves a Git snapshot first so you can undo." : ""),
                writers), cancellationToken).ConfigureAwait(false);
            SetStatus(SessionStatus.Running);
            _writesAllowed = decision != ApprovalDecision.Deny;
            AddTimeline(TimelineKind.Approval, _writesAllowed.Value ? "You allowed file changes" : "You did not allow file changes");
        }
        else _writesAllowed = true;
        if (_writesAllowed.Value) await TakeSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return _writesAllowed.Value;
    }

    private readonly SemaphoreSlim _snapshotGate = new(1, 1);

    private async Task TakeSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_snapshotTaken || !_options.SnapshotBeforeWrites || _git is null || !_git.IsRepository(_options.Project)) return;
        // Parallel write tasks start together; exactly one snapshot, and both wait for it.
        await _snapshotGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_snapshotTaken) { _snapshotGate.Release(); return; }
        _snapshotTaken = true;
        try
        {
            var reference = await _git.SnapshotAsync(_options.Project, Session.Id, cancellationToken).ConfigureAwait(false);
            if (reference is null) { AddTimeline(TimelineKind.Warning, "Could not save a Git snapshot; continuing without one."); return; }
            lock (Session)
            {
                Session.SnapshotRef = reference;
                Session.Artifacts.Add(new Artifact { Kind = ArtifactKind.Snapshot, Name = "Git snapshot before changes", Path = reference, Detail = "Restore from Sessions > this session > Undo changes." });
            }
            AddTimeline(TimelineKind.Info, "Saved a Git snapshot before changes");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error("snapshot_failed", ex);
            AddTimeline(TimelineKind.Warning, "Could not save a Git snapshot; continuing without one.");
        }
        finally { _snapshotGate.Release(); }
    }

    private async Task<(AgentCall Call, bool Allowed)> RunTaskAsync(TaskItem task, CancellationToken cancellationToken)
    {
        var member = Member(task.Agent);
        lock (Session) { task.Attempts++; task.Started = DateTimeOffset.UtcNow; }
        UpdateTask(task, TaskState.Starting);
        AddTimeline(TimelineKind.Start, $"{member.Name} started {Number(task.Id)}: {task.Label}", task.Id);
        string mail;
        List<ChatItem> inbox;
        lock (Session)
        {
            inbox = _chat.Where(item => item.State == "QUEUED" && item.Message.To.Equals(member.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            mail = inbox.Count == 0 ? "" : JsonSerializer.Serialize(inbox.Select(item => new { from = item.Message.From, type = item.Message.Type.ToString().ToUpperInvariant(), content = item.Message.Text }), Json.Compact);
        }
        var prompt = BuildExecutorPrompt(task, member, mail);
        // Tasks that declared no files run read-only until the user has allowed changes in this request.
        var allowWrites = member.CanWrite && (task.NeedsWrite || _writesAllowed == true);
        if (allowWrites) await TakeSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var call = await CallAgentAsync(member, prompt, Number(task.Id), task.Id, allowFallback: true, needsWrite: task.NeedsWrite, allowWrites: allowWrites && member.CanWrite, requirePlanning: false, cancellationToken).ConfigureAwait(false);
        if (call.Result.Success) lock (Session) foreach (var item in inbox) item.State = "DELIVERED";
        return (call, allowWrites);
    }

    private void FinishTask(TaskItem task, AgentCall call)
    {
        var member = call.Member;
        lock (Session)
        {
            task.Result = call.Result.Text;
            task.Ended = DateTimeOffset.UtcNow;
            task.Usage = call.Result.Usage;
            if (!call.Result.Success) task.Error = call.Result.Reason.Length > 0 ? call.Result.Reason : "The agent did not return a result.";
        }
        if (call.Result.Outcome == RunOutcome.Cancelled)
        {
            UpdateTask(task, TaskState.Cancelled, error: "Cancelled by you.");
            SetAgent(member, AgentWorkState.Cancelled, task.Id, "Cancelled");
            return;
        }
        UpdateTask(task, TaskState.Verifying);
        var verification = Verify(task);
        lock (Session) task.Verification = verification.Reason;
        if (!call.Result.Success && TryBrowserRecovery(task, member, task.Error)) return;
        if (!call.Result.Success)
        {
            UpdateTask(task, TaskState.Failed, error: task.Error);
            _failureQueue.Enqueue($"{member.Name}: {task.Error}");
            AddMessage("AGEX", _leader.Name, MessageType.System, $"{Number(task.Id)} failed: {task.Error}", task.Id);
            AddTimeline(TimelineKind.Failed, $"{member.Name} failed {Number(task.Id)}: {task.Error}", task.Id);
            SetAgent(member, AgentWorkState.Failed, task.Id, task.Error);
        }
        // A reply from an agent without a browser that says it could not open one: hand the task to an agent that has one.
        else if (verification.Pass && !_options.Capabilities.BrowserAgents.Contains(member.Id) && TryBrowserRecovery(task, member, ExecutorReply.ResultText(task.Result)))
        {
            return;
        }
        else if (verification.Pass)
        {
            UpdateTask(task, TaskState.Done);
            AddMessage(member.Name, _leader.Name, MessageType.Result, ExecutorReply.ResultText(task.Result), task.Id);
            AddTimeline(TimelineKind.Done, $"{member.Name} completed {Number(task.Id)}", task.Id);
            SetAgent(member, AgentWorkState.Done, task.Id, "Finished " + Number(task.Id));
            foreach (var repaired in task.RepairFor)
            {
                if (Tasks().FirstOrDefault(item => item.Id == repaired) is not { } prior) continue;
                lock (Session) prior.Verification = "Repaired by " + task.Id;
                UpdateTask(prior, TaskState.Done, error: "");
            }
        }
        else
        {
            UpdateTask(task, TaskState.RepairRequired, error: verification.Reason);
            AddMessage(member.Name, _leader.Name, MessageType.Result, ExecutorReply.ResultText(task.Result), task.Id);
            AddMessage("AGEX", _leader.Name, MessageType.Review, $"AGEX could not verify {Number(task.Id)}: {verification.Reason}", task.Id);
            AddTimeline(TimelineKind.Warning, $"{Number(task.Id)} needs repair: {verification.Reason}", task.Id);
            SetAgent(member, AgentWorkState.Done, task.Id, "Needs repair");
        }
        CollectOperationalMessages(task, member);
        UpdateChanges();
        Save();
    }

    private readonly HashSet<string> _browserRetried = new(StringComparer.Ordinal);

    [GeneratedRegex(@"(?i)(browser|playwright|chrom|localhost|127\.0\.0\.1|file://|page).{0,80}(fail|block|denied|refus|not (allowed|available|reachable)|could ?n[o']t|unable|cannot|error)|(could ?n[o']t|unable to|cannot|failed to).{0,40}(open|launch|start|reach|load|access).{0,40}(browser|page|game|site|app|localhost)")]
    private static partial Regex BrowserFailure();

    /// <summary>
    /// A task that could not use a browser is re-run once by another agent that
    /// has a real browser (built in or through the browser tool), when there is one.
    /// </summary>
    private bool TryBrowserRecovery(TaskItem task, TeamMember member, string failure)
    {
        if (!BrowserFailure().IsMatch(failure) || !_browserRetried.Add(task.Id)) return false;
        var alternative = _options.Capabilities.BrowserAgents.Where(id => id != member.Id)
            .Select(id => _options.Members.FirstOrDefault(item => item.Id == id)).OfType<TeamMember>()
            .FirstOrDefault(item => _registry.Health(item.Id).Healthy && (!task.NeedsWrite || item.CanWrite));
        if (alternative is null) return false;
        lock (Session) { task.Note = $"{member.Name} could not use a browser; retried by {alternative.Name}"; task.Agent = alternative.Id; task.Error = ""; }
        lock (_fallbacks) _fallbacks.Add((member.Name, alternative.Name, Number(task.Id), false));
        AddTimeline(TimelineKind.Fallback, $"{member.Name} could not use a browser for {Number(task.Id)}. {alternative.Name} has a real browser and tries it.", task.Id);
        AddMessage("AGEX", alternative.Name, MessageType.System, $"{member.Name} could not use a browser ({Truncate(failure, 200)}). Task {Number(task.Id)} was reassigned to {alternative.Name}.", task.Id);
        UpdateTask(task, TaskState.Queued);
        SetAgent(member, AgentWorkState.Idle, "", "");
        return true;
    }

    private sealed record VerificationResult(bool Pass, string Reason);

    [GeneratedRegex(@"(?im)\b(?:deleted|removed)\s+(?:the\s+)?(?:file\s+)?[`""']?([A-Za-z0-9_.\-]+(?:[/\\][A-Za-z0-9_.\-]+)*\.[A-Za-z0-9]{1,12})")]
    private static partial Regex DeletedClaim();

    [GeneratedRegex(@"(?im)\b(?:created|modified|updated|wrote|saved|edited)\s+(?:the\s+)?(?:file\s+)?[`""']?([A-Za-z0-9_.\-]+(?:[/\\][A-Za-z0-9_.\-]+)*\.[A-Za-z0-9]{1,12})")]
    private static partial Regex WrittenClaim();

    /// <summary>
    /// Independent check of claimed file changes: every declared or stated
    /// output file must exist inside the project (or be gone when the agent says
    /// it deleted it). Claims outside the project fail.
    /// </summary>
    private VerificationResult Verify(TaskItem task)
    {
        var text = ExecutorReply.ResultText(task.Result);
        var deleted = DeletedClaim().Matches(text).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var claims = task.AffectedFiles.Where(path => !path.Contains('*'))
            .Concat(WrittenClaim().Matches(text).Select(match => match.Groups[1].Value)).Concat(deleted)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (claims.Count == 0) return new(true, "No file output claimed; result recorded.");
        var problems = new List<string>();
        foreach (var claim in claims)
        {
            if (!ProjectScanner.IsInside(_options.Project, claim)) { problems.Add($"{claim} is outside the project"); continue; }
            var exists = File.Exists(Path.Combine(_options.Project, claim)) || Directory.Exists(Path.Combine(_options.Project, claim));
            var shouldExist = !deleted.Contains(claim);
            if (exists != shouldExist) problems.Add(shouldExist ? $"{claim} does not exist" : $"{claim} still exists");
        }
        return problems.Count == 0 ? new(true, $"Verified {claims.Count} claimed {(claims.Count == 1 ? "file" : "files")}.") : new(false, string.Join("; ", problems) + ".");
    }

    private void CollectOperationalMessages(TaskItem task, TeamMember from)
    {
        foreach (var message in ExecutorReply.Messages(task.Result))
        {
            lock (Session) if (_chat.Count >= 48) return;
            var to = message.To.Equals("User", StringComparison.OrdinalIgnoreCase) ? "User" : ResolveExecutor(message.To) is { } id ? Member(id).Name : null;
            if (to is null || to == from.Name) continue;
            var type = message.Type switch
            {
                "QUESTION" or "REQUEST" or "BLOCKER" => MessageType.Question,
                "ANSWER" => MessageType.Answer,
                "REVIEW" => MessageType.Review,
                "HANDOFF" or "RESULT" => MessageType.Result,
                _ => MessageType.Status,
            };
            var text = Redactor.Redact(message.Content);
            lock (Session) if (_chat.Any(item => item.Message.From == from.Name && item.Message.To == to && item.Message.Text == text)) continue;
            var added = AddMessage(from.Name, to, type, text, task.Id);
            lock (Session) _chat.Add(new ChatItem { Message = added, State = to == "User" || type is MessageType.Answer or MessageType.Result ? "INFO" : "QUEUED" });
        }
    }

    /// <summary>Delivers queued agent-to-agent questions in short read-only communication turns (at most 8 per request).</summary>
    private async Task DeliverMessagesAsync(CancellationToken cancellationToken)
    {
        List<ChatItem> queued;
        lock (Session) queued = _chat.Where(item => item.State == "QUEUED").ToList();
        foreach (var item in queued)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_mailboxTurns >= 8) { lock (Session) item.State = "NOT_DELIVERED"; continue; }
            _mailboxTurns++;
            var target = _options.Members.FirstOrDefault(member => member.Name == item.Message.To);
            if (target is null) continue;
            var prompt = $"AGEX message delivery. Original goal: {_options.Request}\nFrom {item.Message.From} about task {item.Message.TaskId} ({item.Message.Type}):\n{item.Message.Text}\n" +
                         "Inspect the project if needed and reply concisely with a direct answer or a blocker. Do not edit files in this turn. Do not send further messages." + (OperatingSystem.IsWindows() ? " " + WindowsEncodingHint : "");
            SetAgent(target, AgentWorkState.Answering, item.Message.TaskId, "Answering " + item.Message.From);
            var call = await CallAgentAsync(target, prompt, "a message", item.Message.TaskId, allowFallback: false, needsWrite: false, allowWrites: false, requirePlanning: false, cancellationToken).ConfigureAwait(false);
            SetAgent(target, AgentWorkState.Idle, "", "");
            lock (Session) item.State = call.Result.Success ? "ANSWERED" : "NOT_DELIVERED";
            if (!call.Result.Success) continue;
            var answer = AddMessage(target.Name, item.Message.From, MessageType.Answer, Redactor.Redact(ExecutorReply.ResultText(call.Result.Text)), item.Message.TaskId);
            lock (Session) _chat.Add(new ChatItem { Message = answer, State = "INFO" });
        }
    }

    // ===================================================== agent calls

    private sealed record AgentCall(TeamMember Member, AgentRunResult Result);

    private TeamMember? FallbackFor(TeamMember failed, bool needsWrite, bool requirePlanning) =>
        _options.Members.Where(member => member.Id != failed.Id && _registry.Health(member.Id).Healthy)
            .Where(member => !needsWrite || member.CanWrite)
            .Where(member => !requirePlanning || member.Adapter.Capabilities.Contains(Capability.Planning))
            .OrderBy(member => member.CanReadFiles == failed.CanReadFiles ? 0 : 1)
            .FirstOrDefault();

    private async Task<AgentCall> CallAgentAsync(TeamMember member, string prompt, string purpose, string taskId, bool allowFallback, bool needsWrite, bool allowWrites, bool requirePlanning, CancellationToken cancellationToken, bool direct = false)
    {
        var fallbacksUsed = 0;
        var health = _registry.Health(member.Id);
        if (!health.Healthy)
        {
            var other = allowFallback && _options.MaxAutoFallbacks > 0 ? FallbackFor(member, needsWrite, requirePlanning) : null;
            if (other is null)
            {
                var unavailable = new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = $"{member.Name} is unavailable: {health.Reason}" };
                _failureQueue.Enqueue(unavailable.Reason);
                return new AgentCall(member, unavailable);
            }
            AddTimeline(TimelineKind.Fallback, $"{member.Name} is unavailable. {other.Name} handles {purpose}.", taskId);
            lock (_fallbacks) _fallbacks.Add((member.Name, other.Name, purpose, false));
            member = other;
            fallbacksUsed++;
        }
        while (true)
        {
            SetAgent(member, taskId.Length > 0 ? AgentWorkState.Working : direct ? AgentWorkState.Answering : member.Id == _leader.Id ? AgentWorkState.Planning : AgentWorkState.Working, taskId, $"Working on {purpose}");
            if (taskId.Length > 0 && Tasks().FirstOrDefault(task => task.Id == taskId) is { State: TaskState.Starting } starting) UpdateTask(starting, TaskState.Running);
            var result = await RunOnceAsync(member, prompt, purpose, taskId, allowWrites && member.CanWrite, cancellationToken, direct).ConfigureAwait(false);
            if (result.Success)
            {
                if (fallbacksUsed > 0)
                {
                    lock (_fallbacks)
                        for (var index = 0; index < _fallbacks.Count; index++)
                            if (_fallbacks[index].To == member.Name && _fallbacks[index].Purpose == purpose) _fallbacks[index] = _fallbacks[index] with { Recovered = true };
                    AddTimeline(TimelineKind.Done, $"Recovered using {member.Name}.", taskId);
                }
                return new AgentCall(member, result);
            }
            _failureQueue.Enqueue($"{member.Name} ({purpose}): {result.Reason}");
            if (cancellationToken.IsCancellationRequested || result.Outcome == RunOutcome.Cancelled || !allowFallback || fallbacksUsed >= _options.MaxAutoFallbacks || !result.FallbackEligible)
                return new AgentCall(member, result);
            var next = FallbackFor(member, needsWrite, requirePlanning);
            if (next is null)
            {
                AddTimeline(TimelineKind.Failed, $"{member.Name} could not run {purpose}, and no other agent can take it over.", taskId);
                return new AgentCall(member, result);
            }
            AddTimeline(TimelineKind.Fallback, $"{member.Name} could not run {purpose}. Trying {next.Name} automatically.", taskId);
            AddMessage("AGEX", next.Name, MessageType.System, $"{member.Name} could not run {purpose} ({result.Reason}). {next.Name} takes over.", taskId);
            lock (_fallbacks) _fallbacks.Add((member.Name, next.Name, purpose, false));
            member = next;
            fallbacksUsed++;
        }
    }

    /// <summary>Browser and computer tools this member receives in task runs (never in planning or direct replies).</summary>
    private IReadOnlyList<ToolServer> UsesTools(TeamMember member)
    {
        if (!CapabilityRouting.AcceptsMcp(member) || Intent.Kind != RequestKind.Build) return [];
        var plan = _options.Capabilities;
        return plan.Tools.Where(tool => tool.Spec is not null && (tool.Kind == ToolServerKind.Browser ? plan.BrowserAgents : plan.ComputerAgents).Contains(member.Id)).ToList();
    }

    private async Task<AgentRunResult> RunOnceAsync(TeamMember member, string prompt, string purpose, string taskId, bool allowWrites, CancellationToken cancellationToken, bool direct = false)
    {
        var detection = _registry.DetectionForRun(member.Id);
        // Direct replies are read-only and fast: chat gets no skills or tools; questions and plans get relevant skills only.
        var chat = direct && Intent.Kind == RequestKind.Chat;
        var tools = taskId.Length > 0 ? UsesTools(member).Select(tool => tool.Spec!).ToList() : [];
        var invocation = new AgentInvocation
        {
            Prompt = prompt,
            WorkingDirectory = _options.Project,
            AllowWrites = allowWrites && !direct,
            AllowCommands = _options.AllowCommands && !direct,
            AllowNetwork = _options.Capabilities.AllowNetwork && taskId.Length > 0,
            Model = member.Model,
            Effort = direct && Intent.Kind == RequestKind.Chat ? LowestEffort(member) : EffortFor(member),
            Temperature = member.Temperature,
            ContextWindow = member.ContextWindow,
            Provider = member.Provider,
            Attachments = _options.Attachments,
            Timeout = chat ? TimeSpan.FromMinutes(Math.Min(3, _options.AgentTimeout.TotalMinutes)) : _options.AgentTimeout,
            Skills = chat || !member.Adapter.Capabilities.Contains(Capability.Skills) ? [] : _skills.Where(skill => SkillFor(skill.Id, member)).ToList(),
            McpServers = chat || !member.Adapter.Capabilities.Contains(Capability.Mcp) ? [] : _servers.Where(server => SkillFor(server.SkillId, member)).Concat(tools).ToList(),
            Label = taskId.Length > 0 ? taskId : purpose,
            OnProcessStarted = pid => SetAgent(member, taskId.Length > 0 ? AgentWorkState.Working : direct ? AgentWorkState.Answering : AgentWorkState.Planning, taskId, $"Working on {purpose}", pid),
            OnActivity = activity =>
            {
                SetAgent(member, taskId.Length > 0 ? AgentWorkState.Working : direct ? AgentWorkState.Answering : AgentWorkState.Planning, taskId, activity.Text);
                if (activity.Kind != ActivityKind.ToolStarted) return;
                var key = member.Id + "/" + taskId;
                lock (_toolEvents)
                {
                    var count = _toolEvents.GetValueOrDefault(key);
                    if (count >= 60) return;
                    _toolEvents[key] = count + 1;
                }
                AddMessage(member.Name, "", MessageType.ToolEvent, activity.Text, taskId);
            },
        };
        AgentRunResult result;
        try { result = await member.Adapter.RunAsync(detection, invocation, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { result = new AgentRunResult { Outcome = RunOutcome.Cancelled, Reason = "Cancelled by you." }; }
        catch (Exception ex)
        {
            // An adapter bug fails this call only; the request and the app continue.
            _log?.Error("adapter_error", ex, new { agent = member.Id });
            result = new AgentRunResult { Outcome = RunOutcome.Failed, Reason = $"{member.Name} adapter error: {ex.Message}", FallbackEligible = true };
        }
        if (result.Outcome is not (RunOutcome.Cancelled or RunOutcome.TimedOut))
            _registry.RegisterResult(member.Id, result.Success, result.Reason, immediate: result.Outcome is RunOutcome.StartFailed or RunOutcome.AuthRequired or RunOutcome.Unavailable);
        lock (Session)
        {
            Session.Runs.Add(new RunRecord
            {
                Agent = member.Name, TaskId = taskId, Purpose = purpose, CommandLine = result.CommandLine, Pid = result.Pid, ExitCode = result.ExitCode,
                Outcome = result.Outcome.ToString(), Reason = result.Reason, Seconds = Math.Round(result.Duration.TotalSeconds, 1), Usage = result.Usage,
            });
            if (Session.Runs.Count > 200) Session.Runs.RemoveAt(0);
            if (result.Usage is { } usage) Session.Usage[member.Id] = UsageReport.Combine(Session.Usage.GetValueOrDefault(member.Id), usage)!;
        }
        _log?.Write("agent_run", new { session = Session.Id, agent = member.Id, purpose, outcome = result.Outcome.ToString(), seconds = Math.Round(result.Duration.TotalSeconds, 1), reason = result.Reason });
        return result;
    }

    // ============================================================ outcome

    private void Complete(string reason, string leaderStatus, bool leaderSucceeded, string verification, bool cancelled, DateTimeOffset started)
    {
        foreach (var task in Tasks().Where(task => task.State is TaskState.Queued or TaskState.Starting or TaskState.Running or TaskState.Verifying or TaskState.Waiting))
        {
            if (cancelled) UpdateTask(task, TaskState.Cancelled, error: task.Error.Length > 0 ? task.Error : "Cancelled by you.");
            else UpdateTask(task, TaskState.Failed, error: task.State == TaskState.Waiting ? task.Error : "Did not run before the request ended.");
        }
        var tasks = Tasks();
        var done = tasks.Count(task => task.State == TaskState.Done);
        var failed = tasks.Count(task => task.State is TaskState.Failed or TaskState.RepairRequired);
        var cancelledCount = tasks.Count(task => task.State is TaskState.Cancelled or TaskState.Skipped);
        var recovered = _fallbacks.Any(item => item.Recovered);
        var status = cancelled ? SessionStatus.Cancelled
            : leaderStatus == "COMPLETE" ? (recovered ? SessionStatus.CompleteWithFallback : SessionStatus.Complete)
            : !leaderSucceeded && tasks.Count == 0 ? SessionStatus.StartFailed
            : done > 0 && failed + cancelledCount > 0 ? SessionStatus.Partial
            : done == 0 ? SessionStatus.Failed
            : SessionStatus.Unverified;
        var headline = status switch
        {
            SessionStatus.Complete => "Request completed.",
            SessionStatus.CompleteWithFallback => "Request completed (recovered with another agent).",
            SessionStatus.Partial => $"Request partly completed: {done} of {tasks.Count} tasks done.",
            SessionStatus.Failed => "Request could not be completed.",
            SessionStatus.StartFailed => "Request could not start.",
            SessionStatus.Cancelled => "Request cancelled.",
            _ => "Tasks finished, but the leader did not confirm the goal.",
        };
        var outcome = new Outcome
        {
            Headline = headline, Reason = reason, Verification = verification, Tasks = tasks.Count, Done = done, Failed = failed, Cancelled = cancelledCount,
            Seconds = Math.Round((DateTimeOffset.UtcNow - started).TotalSeconds, 1),
            PrimaryFailure = status is SessionStatus.Failed or SessionStatus.StartFailed or SessionStatus.Partial ? Failures.FirstOrDefault() ?? "" : "",
        };
        outcome.Recovery = RecoveryFor(status, reason + " " + outcome.PrimaryFailure + " " + string.Join(" ", Failures));
        foreach (var item in _fallbacks)
            outcome.WhatHappened.Add($"{item.From} could not run {item.Purpose}; switched to {item.To} automatically ({(item.Recovered ? "recovered" : "did not recover")}).");
        foreach (var task in tasks.Where(task => task.State == TaskState.Done))
            if (ExecutorReply.ResultText(task.Result) is { Length: > 0 } text) outcome.Results.Add($"[{Number(task.Id)}] {text}");
        UpdateChanges();
        lock (Session)
        {
            Session.Outcome = outcome;
            Session.Question = null;
        }
        SetStatus(status);
        AddTimeline(status is SessionStatus.Complete or SessionStatus.CompleteWithFallback ? TimelineKind.Done : status == SessionStatus.Cancelled ? TimelineKind.Warning : TimelineKind.Failed,
            SessionStatusText.Label(status));
        Save();
        _log?.Write("request_end", new { session = Session.Id, status = SessionStatusText.Code(status), tasks = tasks.Count, done, failed, fallbacks = _fallbacks.Count, seconds = outcome.Seconds });
    }

    /// <summary>What the user can try next: fixes for missing capabilities first, then another agent, then Retry.</summary>
    private List<RecoveryOption> RecoveryFor(SessionStatus status, string failureText)
    {
        var options = new List<RecoveryOption>();
        void Add(RecoveryKind kind, string label, string detail, string argument = "")
        {
            if (options.Any(item => item.Kind == kind.ToString() && item.Argument == argument)) return;
            options.Add(new RecoveryOption { Kind = kind.ToString(), Label = label, Detail = detail, Argument = argument });
        }
        var failed = status is SessionStatus.Failed or SessionStatus.StartFailed or SessionStatus.Partial or SessionStatus.Unverified;
        foreach (var missing in _options.Capabilities.Missing.Where(item => item.Blocking || failed))
            Add(missing.Fix, missing.FixLabel, missing.Title + ". " + missing.Detail, missing.Argument);
        if (!failed) return options;
        if (BrowserFailure().IsMatch(failureText))
        {
            if (!_options.Permissions.Browser) Add(RecoveryKind.EnableBrowser, "Enable browser", "Agents need a real browser to open and use the page.");
            else if (_options.Capabilities.Tools.All(tool => tool.Kind != ToolServerKind.Browser))
                Add(RecoveryKind.ConnectTool, "Connect required tool", "Connect Browser (Playwright MCP) so Codex and Claude Code can open, click and type in a real browser.", "playwright-mcp");
            if (OperatingSystem.IsWindows() && !_options.Permissions.ComputerControl)
                Add(RecoveryKind.EnableComputerControl, "Enable computer control", "Let an agent see the screen and use the mouse and keyboard.");
        }
        if (Regex.IsMatch(failureText, @"(?i)sign in|not logged in|auth")) Add(RecoveryKind.OpenAgents, "Open Agents", "An agent needs you to sign in again.");
        if (_options.Members.Count > 1 || _registry.Adapters.Count(adapter => adapter.Id != _leader.Id) > 0)
            Add(RecoveryKind.TryAnotherAgent, "Try another tool", "Run the same request with a different agent.");
        Add(RecoveryKind.Retry, "Retry", "Run the same request again.");
        return options;
    }

    private void UpdateChanges()
    {
        // Chat, questions and plans take no snapshot of the project: there is nothing to compare.
        if (Baseline is null && _before.Count == 0 && Intent.Kind != RequestKind.Build) return;
        List<FileChange> changes;
        try { changes = ProjectScanner.Diff(_before, ProjectScanner.List(_options.Project, _options.IgnoredFolders)); if (Baseline is not null) changes = Baseline.Describe(changes); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        lock (Session)
        {
            Session.Changes = changes;
            Session.Artifacts.RemoveAll(artifact => artifact.Kind == ArtifactKind.File);
            foreach (var change in changes.Where(change => change.Kind != "deleted").Take(200))
                Session.Artifacts.Add(new Artifact { Kind = ArtifactKind.File, Name = change.Path, Path = Path.Combine(_options.Project, change.Path), Detail = change.Kind });
        }
        SessionChanged?.Invoke(Session);
    }

    // ========================================================= plumbing

    private long _seq;

    private AgentMessage AddMessage(string from, string to, MessageType type, string text, string taskId = "")
    {
        var message = new AgentMessage { From = from, To = to, Type = type, Text = text, TaskId = taskId, Seq = Interlocked.Increment(ref _seq) };
        lock (Session)
        {
            Session.Messages.Add(message);
            // Bound memory for very long sessions: old tool events go first.
            if (Session.Messages.Count > 5000)
            {
                var index = Session.Messages.FindIndex(item => item.Type == MessageType.ToolEvent);
                Session.Messages.RemoveAt(index >= 0 ? index : 0);
            }
        }
        MessageAdded?.Invoke(message);
        return message;
    }

    private void AddTimeline(TimelineKind kind, string text, string taskId = "")
    {
        var entry = new TimelineEntry { Kind = kind, Text = text, TaskId = taskId };
        lock (Session) Session.Timeline.Add(entry);
        TimelineAdded?.Invoke(entry);
    }

    private void UpdateTask(TaskItem task, TaskState state, string? error = null)
    {
        lock (Session)
        {
            task.State = state;
            if (error is not null) task.Error = error;
            if (state is TaskState.Done or TaskState.Failed or TaskState.Cancelled or TaskState.RepairRequired or TaskState.Skipped) task.Ended ??= DateTimeOffset.UtcNow;
        }
        RaiseTask(task);
    }

    private void RaiseTask(TaskItem task)
    {
        TaskItem copy;
        lock (Session) copy = JsonSerializer.Deserialize<TaskItem>(JsonSerializer.Serialize(task, Json.Compact), Json.Compact)!;
        TaskChanged?.Invoke(copy);
    }

    private void SetAgent(TeamMember member, AgentWorkState state, string taskId, string text, int pid = 0) =>
        AgentChanged?.Invoke(new AgentLiveState(member.Id, member.Name, state, taskId, Redactor.Redact(text), pid, DateTimeOffset.UtcNow));

    private void SetStatus(SessionStatus status)
    {
        lock (Session) Session.Status = status;
        SessionChanged?.Invoke(Session);
    }

    private void Save()
    {
        if (_store is null) return;
        lock (Session) _store.Save(Session);
    }

    private static string TaskStateCode(TaskState state) => state switch
    {
        TaskState.RepairRequired => "REPAIR_REQUIRED",
        _ => state.ToString().ToUpperInvariant(),
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "...";

    /// <summary>Stable short hash, used for deduplication keys.</summary>
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
}
