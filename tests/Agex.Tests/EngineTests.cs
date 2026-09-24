using Agex.Core;
using Agex.Core.Orchestration;
using Agex.Core.Sessions;

namespace Agex.Tests;

public class EngineTests
{
    private static async Task<Session> Run(Sandbox sandbox, string request, IEngineHost? host = null, string leader = "codex", string[]? agents = null, Action<AgexCore>? configure = null, CancellationToken cancellationToken = default, Action<RequestEngine>? onEngine = null)
    {
        var core = sandbox.Core();
        core.Settings.Leader = leader;
        core.Settings.Approvals.AskBeforeWrites = host is not null;
        configure?.Invoke(core);
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        var members = core.BuildMembers(profile, agentIds: agents ?? ["codex", "antigravity"]);
        var engine = core.CreateRequest(sandbox.Project, request, host ?? new ScriptedHost(), members, profile, [], []);
        onEngine?.Invoke(engine);
        return await engine.RunAsync(cancellationToken);
    }

    private const string TwoTasks = """
        {"goal_status":"CONTINUE","reason":"Two files.","tasks":[
          {"id":"a","title":"Write A","objective":"Create a.txt","executor":"Antigravity","dependencies":[],"affected_files":["a.txt"]},
          {"id":"b","title":"Write B","objective":"Create docs/b.txt","executor":"Codex","dependencies":[],"affected_files":["docs/b.txt"]}]}
        """;
    private const string Done = """{"goal_status":"COMPLETE","reason":"Both files exist.","verification":"a.txt and docs/b.txt checked.","tasks":[]}""";

    [Fact]
    public async Task Question_is_answered_directly_without_tasks()
    {
        using var sandbox = new Sandbox("direct");
        sandbox.LeaderPlans("""{"goal_status":"COMPLETE","reason":"The answer is 42.","verification":"Read README.","tasks":[]}""");
        var session = await Run(sandbox, "What is the answer?");
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.Empty(session.Tasks);
        Assert.Contains(session.Messages, message => message.Type == MessageType.Result && message.Text.Contains("42"));
        Assert.Contains(session.Timeline, entry => entry.Text == "Request received");
    }

    [Fact]
    public async Task Parallel_tasks_write_files_and_are_verified()
    {
        using var sandbox = new Sandbox("parallel");
        sandbox.LeaderPlans(TwoTasks, Done);
        var session = await Run(sandbox, "Create two files");
        Assert.True(session.Status == SessionStatus.Complete, string.Join("\n", session.Timeline.Select(entry => entry.Text).Concat(session.Runs.Select(run => $"{run.Agent} {run.Outcome} {run.Reason}"))));
        Assert.All(session.Tasks, task => Assert.Equal(TaskState.Done, task.State));
        Assert.True(File.Exists(Path.Combine(sandbox.Project, "a.txt")));
        Assert.Contains("مرحبا ✓ café", File.ReadAllText(Path.Combine(sandbox.Project, "docs", "b.txt")));
        Assert.Contains(session.Changes, change => change.Path == "docs/b.txt" && change.Kind == "added");
        Assert.Contains(session.Messages, message => message.Type == MessageType.Assignment && message.To == "Antigravity");
        Assert.Contains(session.Messages, message => message.Type == MessageType.ToolEvent);
        // Hidden reasoning emitted by the agent must never reach the Agent Room.
        Assert.DoesNotContain(session.Messages, message => message.Text.Contains("SECRET REASONING"));
        Assert.Contains(session.Usage, pair => pair.Key == "codex" && pair.Value.InputTokens == 360);
        Assert.Contains(session.Timeline, entry => entry.Text.Contains("→ Antigravity"));
        Assert.Equal(session.Id, new SessionStore(sandbox.Platform.Paths.Sessions).List().Single().Id);
    }

    [Fact]
    public async Task Leader_falls_back_when_first_agent_cannot_start()
    {
        using var sandbox = new Sandbox("fallback");
        sandbox.Mode("codex", "fail-start");
        sandbox.LeaderPlans(Done);
        var session = await Run(sandbox, "Hello");
        Assert.Equal(SessionStatus.CompleteWithFallback, session.Status);
        Assert.Equal("Antigravity", session.Leader);
        Assert.Contains(session.Timeline, entry => entry.Kind == TimelineKind.Fallback);
        Assert.Contains(session.Outcome!.WhatHappened, line => line.Contains("recovered"));
    }

    [Fact]
    public async Task Request_start_fails_when_no_agent_can_plan()
    {
        using var sandbox = new Sandbox("startfail");
        sandbox.Mode("codex", "fail-start");
        sandbox.Mode("agy", "fail-start");
        var session = await Run(sandbox, "Hello");
        Assert.Equal(SessionStatus.StartFailed, session.Status);
        Assert.Contains("No agent could plan", session.Outcome!.Reason);
    }

    [Fact]
    public async Task One_failed_task_gives_partial()
    {
        using var sandbox = new Sandbox("partial");
        sandbox.Mode("codex", "no-result");
        sandbox.LeaderPlans(TwoTasks.Replace("\"Codex\"", "\"Codex\""), """{"goal_status":"BLOCKED","reason":"Codex failed.","tasks":[]}""");
        var session = await Run(sandbox, "Create two files", leader: "antigravity");
        Assert.Equal(SessionStatus.Partial, session.Status);
        Assert.Equal(1, session.Outcome!.Done);
    }

    [Fact]
    public async Task Leader_question_is_answered_by_the_user_and_the_session_continues()
    {
        using var sandbox = new Sandbox("input");
        sandbox.LeaderPlans("""{"goal_status":"NEEDS_INPUT","reason":"Need a choice.","question":"Which database should I use?","tasks":[]}""", Done.Replace("Both files exist.", "Using PostgreSQL."));
        var host = new ScriptedHost(ApprovalDecision.Allow, "PostgreSQL");
        var session = await Run(sandbox, "Set up the database", host);
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.Equal("Which database should I use?", Assert.Single(host.Questions));
        Assert.Contains(session.Messages, message => message.Type == MessageType.Answer && message.From == "User" && message.Text == "PostgreSQL");
        Assert.Contains("PostgreSQL", Assert.Single(session.Answers));
    }

    [Fact]
    public async Task Denied_file_changes_skip_writing_tasks()
    {
        using var sandbox = new Sandbox("deny");
        sandbox.LeaderPlans(TwoTasks, """{"goal_status":"BLOCKED","reason":"Writes were not allowed.","tasks":[]}""");
        var host = new ScriptedHost(ApprovalDecision.Deny);
        var session = await Run(sandbox, "Create two files", host);
        Assert.Equal(1, host.ApprovalRequests);
        Assert.All(session.Tasks, task => Assert.Equal(TaskState.Skipped, task.State));
        Assert.False(File.Exists(Path.Combine(sandbox.Project, "a.txt")));
    }

    [Fact]
    public async Task Cancel_stops_agents_and_marks_the_session_cancelled()
    {
        using var sandbox = new Sandbox("cancel");
        sandbox.Mode("codex", "slow");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var started = DateTime.UtcNow;
        var session = await Run(sandbox, "Take forever", cancellationToken: cancellation.Token);
        Assert.Equal(SessionStatus.Cancelled, session.Status);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30));
        await Task.Delay(500);
        Assert.Empty(System.Diagnostics.Process.GetProcessesByName("Agex.FakeAgent"));
    }

    [Fact]
    public async Task Agent_questions_are_delivered_to_the_other_agent()
    {
        using var sandbox = new Sandbox("mail");
        sandbox.Mode("agy", "ask-agent");
        sandbox.LeaderPlans("""{"goal_status":"CONTINUE","reason":"One task.","tasks":[{"id":"a","title":"Check","objective":"Check config","executor":"Antigravity","dependencies":[],"affected_files":[]}]}""", Done);
        var session = await Run(sandbox, "Check the config");
        Assert.Contains(session.Messages, message => message.Type == MessageType.Question && message.From == "Antigravity" && message.To == "Codex");
        Assert.Contains(session.Messages, message => message.Type == MessageType.Answer && message.From == "Codex" && message.Text.Contains("PostgreSQL"));
    }

    [Fact]
    public async Task Dependencies_run_in_order()
    {
        using var sandbox = new Sandbox("deps");
        sandbox.LeaderPlans("""
            {"goal_status":"CONTINUE","reason":"Chain.","tasks":[
              {"id":"first","title":"First","objective":"Create one.txt","executor":"Codex","dependencies":[],"affected_files":["one.txt"]},
              {"id":"second","title":"Second","objective":"Create two.txt","executor":"Antigravity","dependencies":["First"],"affected_files":["two.txt"]}]}
            """, Done);
        var session = await Run(sandbox, "Chain");
        var first = session.Tasks.Single(task => task.Title == "First");
        var second = session.Tasks.Single(task => task.Title == "Second");
        Assert.Equal([first.Id], second.Dependencies);
        Assert.True(second.Started >= first.Ended);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("antigravity")]
    [InlineData("claude-code")]
    [InlineData("gemini-cli")]
    public async Task Unicode_survives_every_agent_protocol(string leader)
    {
        using var sandbox = new Sandbox("utf8-" + leader);
        sandbox.LeaderPlans("""{"goal_status":"COMPLETE","reason":"{{ECHO}}","verification":"echo","tasks":[]}""");
        const string request = "اكتب مرحبا — emoji 🎉, accents café, CJK 你好, quote \" and backslash \\";
        var session = await Run(sandbox, request, leader: leader, agents: [leader]);
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.Contains(session.Messages, message => message.Type == MessageType.Result && message.Text.StartsWith(request, StringComparison.Ordinal));
        Assert.All(sandbox.FakeLog(), line => Assert.EndsWith("nobom", line));
    }
}
