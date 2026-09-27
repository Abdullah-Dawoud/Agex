using Agex.Core;
using Agex.Core.Orchestration;
using Agex.Core.Settings;
using Agex.Core.Sessions;
using Agex.Core.Skills;

namespace Agex.Tests;

/// <summary>AGEX 2.5: simple chat stays simple, agents fall back, and skills follow the task and the user.</summary>
public class ChatQualityTests
{
    private static async Task<Session> Chat(Sandbox sandbox, string request, string leader, string[] agents, Action<AgexCore>? configure = null, bool projectless = false)
    {
        var core = sandbox.Core();
        core.Settings.Leader = leader;
        configure?.Invoke(core);
        var profile = projectless ? new ProjectProfile { Path = sandbox.Project, Name = "Chat", AllowWrites = false } : core.SettingsStore.LoadProject(sandbox.Project);
        var members = core.BuildMembers(profile, agentIds: agents);
        var engine = core.CreateRequest(sandbox.Project, request, new ScriptedHost(), members, profile, [], [], mode: ChatMode.Auto, projectless: projectless);
        return await engine.RunAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------ fallback

    [Fact]
    public async Task Hi_is_answered_by_another_agent_when_the_first_one_fails()
    {
        using var sandbox = new Sandbox("chat-fallback");
        sandbox.Mode("agy", "no-result");
        var session = await Chat(sandbox, "hi", "antigravity", ["antigravity", "codex"]);
        Assert.Equal(SessionStatus.CompleteWithFallback, session.Status);
        Assert.Equal("chat", session.Mode);
        Assert.Contains(session.Messages, message => message.Type == MessageType.Result && message.From == "Codex");
        Assert.Equal("Codex", session.Leader);
        Assert.Contains(session.Timeline, entry => entry.Kind == TimelineKind.Fallback);
        // The switch is quiet: no system message in the conversation.
        Assert.DoesNotContain(session.Messages, message => message.Type == MessageType.System);
        Assert.Equal(["agy", "codex"], sandbox.FakeLog().Select(line => line.Split('|')[0]));
    }

    [Fact]
    public async Task Chat_tries_every_ready_agent_before_it_fails()
    {
        using var sandbox = new Sandbox("chat-fallback-all");
        sandbox.Mode("agy", "no-result");
        sandbox.Mode("codex", "fail-start");
        var session = await Chat(sandbox, "hello", "antigravity", ["antigravity", "codex", "opencode"]);
        Assert.Equal(SessionStatus.CompleteWithFallback, session.Status);
        Assert.Contains(session.Messages, message => message.Type == MessageType.Result && message.From == "OpenCode");
    }

    [Fact]
    public async Task Chat_fails_only_when_every_agent_fails_and_offers_agent_fixes()
    {
        using var sandbox = new Sandbox("chat-all-fail");
        sandbox.Mode("agy", "no-result");
        sandbox.Mode("codex", "no-result");
        var session = await Chat(sandbox, "hi", "antigravity", ["antigravity", "codex"]);
        Assert.Contains(session.Status, new[] { SessionStatus.Failed, SessionStatus.StartFailed });
        var labels = session.Outcome!.Recovery.Select(option => option.Label).ToList();
        Assert.Equal("Retry", labels[0]);
        Assert.Contains("Use another agent", labels);
        Assert.Contains("Check agents", labels);
        Assert.DoesNotContain("Try another tool", labels);
        Assert.DoesNotContain(labels, label => label.Contains("tool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_agent_that_just_failed_is_not_asked_first_again()
    {
        using var sandbox = new Sandbox("chat-recent-failure");
        var session = await Chat(sandbox, "hi", "antigravity", ["antigravity", "codex"], core => core.Registry.RegisterResult("antigravity", false, "exited"));
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.Equal("codex", Assert.Single(sandbox.FakeLog()).Split('|')[0]);
    }

    [Fact]
    public async Task Chat_works_without_a_project()
    {
        using var sandbox = new Sandbox("chat-projectless");
        var session = await Chat(sandbox, "hi", "codex", ["codex"], projectless: true);
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.True(session.Projectless);
        Assert.Empty(session.Changes);
        Assert.Contains("|direct|", Assert.Single(sandbox.FakeLog()));
    }

    [Fact]
    public void Recovery_labels_name_agents_for_agent_problems()
    {
        Assert.Equal("Use another agent", new MissingCapability(NeededCapability.None, "", "", RecoveryKind.TryAnotherAgent).FixLabel);
        Assert.Equal("Check agents", new MissingCapability(NeededCapability.None, "", "", RecoveryKind.OpenAgents).FixLabel);
        Assert.Equal("Enable browser", new MissingCapability(NeededCapability.Browser, "", "", RecoveryKind.EnableBrowser).FixLabel);
        Assert.Equal("Connect required tool", new MissingCapability(NeededCapability.Browser, "", "", RecoveryKind.ConnectTool).FixLabel);
    }

    [Fact]
    public async Task Browser_work_the_planner_could_not_do_goes_to_an_agent_with_a_browser()
    {
        using var sandbox = new Sandbox("planner-browser");
        sandbox.LeaderPlans(
            """{"goal_status":"BLOCKED","reason":"I read index.html, but the browser blocked access to the local page because permission was declined.","tasks":[]}""",
            """{"goal_status":"COMPLETE","reason":"The page shows 2.","verification":"Clicked twice in the browser.","tasks":[]}""");
        var core = sandbox.Core();
        core.Settings.Leader = "codex";
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        var members = core.BuildMembers(profile, agentIds: ["codex", "antigravity"]);
        var intent = RequestClassifier.Classify("open the counter page in a browser and click Add one twice");
        var playwright = new ToolServer("playwright-mcp", "Browser (Playwright MCP)", ToolServerKind.Browser, true,
            new Agex.Core.Agents.McpServerSpec("playwright_mcp", "npx", ["-y", "playwright-mcp"], new Dictionary<string, string>(), SkillId: "playwright-mcp"));
        var capabilities = CapabilityRouting.Plan(intent, members, new PermissionSettings(), [playwright], windows: true);
        var engine = core.CreateRequest(sandbox.Project, "open the counter page in a browser and click Add one twice", new ScriptedHost(), members, profile, [], [], mode: ChatMode.Auto, capabilities: capabilities);
        var session = await engine.RunAsync(CancellationToken.None);
        Assert.Contains(session.Status, new[] { SessionStatus.Complete, SessionStatus.CompleteWithFallback });
        Assert.Single(session.Tasks);
        Assert.Contains(session.Timeline, entry => entry.Kind == TimelineKind.Fallback && entry.Text.Contains("could not use a browser while planning"));
    }

    // --------------------------------------------------------------- skills

    private static List<InstalledSkill> Installed(AgexCore core, params string[] ids)
    {
        var catalog = core.Skills.Catalog().Skills;
        return ids.Select(id => new InstalledSkill { Id = id, Manifest = catalog.Single(skill => skill.Id == id), Enabled = true }).ToList();
    }

    private static IReadOnlyList<string> Resolve(IReadOnlyList<InstalledSkill> installed, string request, IReadOnlyCollection<string>? pins = null, Dictionary<string, bool>? overrides = null) =>
        SkillSelection.Resolve(installed, RequestClassifier.Classify(request), request, null, pins, overrides);

    [Fact]
    public void Hi_uses_no_skills_even_with_a_Team_active()
    {
        using var sandbox = new Sandbox("skills-hi");
        var installed = Installed(sandbox.Core(), "systematic-debugging", "requesting-code-review", "test-driven-development", "pdf-documents");
        Assert.Empty(Resolve(installed, "hi", pins: ["systematic-debugging", "requesting-code-review", "test-driven-development"]));
        Assert.Empty(Resolve(installed, "what can you do?"));
    }

    [Theory]
    [InlineData("fix this bug in the login form", "systematic-debugging")]
    [InlineData("review this code before I merge it", "requesting-code-review")]
    [InlineData("summarize report.pdf", "pdf-documents")]
    public void Suggested_skills_follow_the_task(string request, string expected)
    {
        using var sandbox = new Sandbox("skills-task");
        var installed = Installed(sandbox.Core(), "systematic-debugging", "requesting-code-review", "pdf-documents");
        var chosen = Resolve(installed, request);
        Assert.Contains(expected, chosen);
        Assert.True(chosen.Count < installed.Count, string.Join(",", chosen));
    }

    [Fact]
    public void Team_pins_are_recommendations_not_a_limit()
    {
        using var sandbox = new Sandbox("skills-team");
        var installed = Installed(sandbox.Core(), "systematic-debugging", "pdf-documents");
        // A skill that is not pinned to the Team is still used when the task needs it.
        Assert.Contains("pdf-documents", Resolve(installed, "extract the tables from invoice.pdf", pins: ["systematic-debugging"]));
    }

    [Fact]
    public void A_skill_the_user_adds_is_used_even_outside_the_Team()
    {
        using var sandbox = new Sandbox("skills-add");
        var installed = Installed(sandbox.Core(), "caveman", "systematic-debugging");
        installed[0].Enabled = false; // off for Auto
        Assert.Contains("caveman", Resolve(installed, "hi", pins: ["systematic-debugging"], overrides: new() { ["caveman"] = true }));
    }

    [Fact]
    public void A_suggested_skill_the_user_removes_stays_removed()
    {
        using var sandbox = new Sandbox("skills-remove");
        var installed = Installed(sandbox.Core(), "systematic-debugging", "test-driven-development");
        Assert.Contains("systematic-debugging", Resolve(installed, "fix the crash on startup"));
        var chosen = Resolve(installed, "fix the crash on startup", overrides: new() { ["systematic-debugging"] = false });
        Assert.DoesNotContain("systematic-debugging", chosen);
        Assert.Contains("test-driven-development", chosen);
    }

    // ------------------------------------------------------------ workspace

    [Theory]
    [InlineData("Running \"C:\\\\Windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\" -Command 'git log --oneline -3'", "git log --oneline -3", "git log")]
    [InlineData("Running /bin/bash -lc 'npm test'", "npm test", "npm test")]
    [InlineData("Running node --version", "node --version", "node")]
    public void Agent_commands_show_as_typed(string raw, string display, string title)
    {
        Assert.Equal(display, CommandText.Display(raw));
        Assert.Equal(title, CommandText.Title(raw));
    }

    [Fact]
    public void A_file_rewritten_with_the_same_text_is_not_a_change()
    {
        using var sandbox = new Sandbox("same-text");
        var file = Path.Combine(sandbox.Project, "same.txt");
        File.WriteAllText(file, "one\ntwo\n");
        var before = Agex.Core.Projects.ProjectScanner.List(sandbox.Project);
        var baseline = Agex.Core.Projects.TextBaseline.Capture(sandbox.Project, before);
        File.WriteAllText(file, "one\ntwo\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        Assert.Empty(baseline.Describe(Agex.Core.Projects.ProjectScanner.Diff(before, Agex.Core.Projects.ProjectScanner.List(sandbox.Project))));
    }

    [Fact]
    public void Workspace_surfaces_appear_only_when_there_is_something_to_show()
    {
        Assert.Empty(WorkspaceSurfaces.Visible(null, running: false, 0, 0, false, 0));
        var chat = new Session { Mode = "chat" };
        Assert.Empty(WorkspaceSurfaces.Visible(chat, running: true, 0, 0, false, 0));
        var build = new Session { Mode = "build" };
        Assert.Equal([WorkspaceSurface.Changes], WorkspaceSurfaces.Visible(build, running: true, 0, 0, false, 0).Select(item => item.Surface));
        build.Changes.Add(new FileChange { Path = "a.txt", Kind = "added" });
        build.Changes.Add(new FileChange { Path = "b.txt", Kind = "modified" });
        var visible = WorkspaceSurfaces.Visible(build, running: false, browserPages: 1, commands: 2, previewOpen: false, files: 0);
        Assert.Equal([(WorkspaceSurface.Changes, 2), (WorkspaceSurface.Browser, 1), (WorkspaceSurface.Terminal, 2)], visible.Select(item => (item.Surface, item.Count)));
    }
}
