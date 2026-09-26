using System.Net.Http;
using System.Text.Json;
using Agex.Core;
using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Core.Runtime;
using Agex.Core.Sessions;
using Agex.Core.Settings;

namespace Agex.Tests;

/// <summary>AGEX 2.3: fast chat, modes, capability routing, local web, approvals, model settings and usage.</summary>
public class RuntimeUxTests
{
    // ------------------------------------------------------- classification

    [Theory]
    [InlineData("hi", RequestKind.Chat)]
    [InlineData("hello", RequestKind.Chat)]
    [InlineData("Hello!", RequestKind.Chat)]
    [InlineData("thanks", RequestKind.Chat)]
    [InlineData("what model are you using?", RequestKind.Chat)]
    [InlineData("can you help me?", RequestKind.Chat)]
    [InlineData("what is this project?", RequestKind.Question)]
    [InlineData("explain this file", RequestKind.Question)]
    [InlineData("explain src/game.js", RequestKind.Question)]
    [InlineData("How does the scoring work?", RequestKind.Question)]
    [InlineData("plan how to add authentication", RequestKind.Plan)]
    [InlineData("Make a plan for the database migration", RequestKind.Plan)]
    [InlineData("implement authentication", RequestKind.Build)]
    [InlineData("add a dark mode toggle to the settings page", RequestKind.Build)]
    [InlineData("can you fix the login bug?", RequestKind.Build)]
    [InlineData("open the local game and play it", RequestKind.Build)]
    [InlineData("I still cant press on start the game is not starting there is something wrong ?", RequestKind.Build)]
    public void Auto_mode_routes_requests(string request, RequestKind expected) =>
        Assert.Equal(expected, RequestClassifier.Classify(request).Kind);

    [Fact]
    public void Chosen_mode_wins_over_classification()
    {
        Assert.Equal(RequestKind.Question, RequestClassifier.Classify("implement authentication", ChatMode.Ask).Kind);
        Assert.Equal(RequestKind.Plan, RequestClassifier.Classify("implement authentication", ChatMode.Plan).Kind);
        Assert.Equal(RequestKind.Build, RequestClassifier.Classify("what is this project?", ChatMode.Build).Kind);
        Assert.Equal(RequestKind.Chat, RequestClassifier.Classify("hi", ChatMode.Ask).Kind);
        // Ask and Plan never carry change capabilities.
        Assert.False(RequestClassifier.Classify("delete all files and implement login", ChatMode.Ask).Has(NeededCapability.EditProject));
        Assert.False(RequestClassifier.Classify("implement login", ChatMode.Plan).Has(NeededCapability.EditProject));
    }

    [Fact]
    public void Game_request_needs_a_browser_on_this_computer_and_prefers_computer_control()
    {
        var intent = RequestClassifier.Classify("open the game as a normal user and try to play it and if u find any bugs fix it just use mouse and keybord to play");
        Assert.Equal(RequestKind.Build, intent.Kind);
        Assert.True(intent.Has(NeededCapability.Browser));
        Assert.True(intent.Has(NeededCapability.LocalWeb));
        Assert.True(intent.Has(NeededCapability.EditProject));
        Assert.True(intent.Wants(NeededCapability.ComputerControl));
        Assert.False(intent.Has(NeededCapability.ComputerControl));
        var local = RequestClassifier.Classify("open the local game and play it");
        Assert.True(local.Has(NeededCapability.Browser | NeededCapability.LocalWeb));
    }

    [Theory]
    [InlineData("implement authentication: a simple sign-in screen shown before the game starts, with sign out")]
    [InlineData("build a landing page for the app")]
    [InlineData("add a pause menu to the game")]
    public void Changing_a_web_project_does_not_by_itself_need_a_browser(string request)
    {
        var intent = RequestClassifier.Classify(request);
        Assert.Equal(RequestKind.Build, intent.Kind);
        Assert.False(intent.Wants(NeededCapability.Browser));
        Assert.False(intent.Wants(NeededCapability.ComputerControl));
    }

    [Fact]
    public void Desktop_work_needs_computer_control_and_urls_are_classified()
    {
        Assert.True(RequestClassifier.Classify("use the mouse to open notepad and type hello").Has(NeededCapability.ComputerControl));
        var local = RequestClassifier.Classify("test http://localhost:5173 and fix the layout");
        Assert.True(local.Has(NeededCapability.LocalWeb));
        Assert.False(local.Has(NeededCapability.ExternalNetwork));
        Assert.Contains(local.Targets, target => target.Kind == TargetKind.Loopback);
        Assert.True(RequestClassifier.Classify("open https://example.com and summarise it").Has(NeededCapability.ExternalNetwork));
    }

    [Fact]
    public void Sensitive_actions_are_recognised()
    {
        Assert.NotEmpty(RequestClassifier.Classify("send an email to my clients about the release").Sensitive);
        Assert.NotEmpty(RequestClassifier.Classify("delete all branches except main").Sensitive);
        Assert.NotEmpty(RequestClassifier.Classify("buy the domain and pay with my card").Sensitive);
        Assert.NotEmpty(RequestClassifier.Classify("publish the package to npm").Sensitive);
        Assert.NotEmpty(RequestClassifier.Classify("rotate the api key in .env").Sensitive);
        Assert.Empty(RequestClassifier.Classify("fix the start button").Sensitive);
        Assert.Empty(RequestClassifier.Classify("send me a summary", ChatMode.Ask).Sensitive);
    }

    [Theory]
    [InlineData("http://localhost:3000/", TargetKind.Loopback)]
    [InlineData("http://127.0.0.1:8765/index.html", TargetKind.Loopback)]
    [InlineData("http://[::1]:5000/", TargetKind.Loopback)]
    [InlineData("http://app.localhost/", TargetKind.Loopback)]
    [InlineData("http://192.168.1.20/", TargetKind.PrivateNetwork)]
    [InlineData("http://10.0.0.5:8080/", TargetKind.PrivateNetwork)]
    [InlineData("http://172.20.0.1/", TargetKind.PrivateNetwork)]
    [InlineData("http://169.254.169.254/latest/meta-data", TargetKind.PrivateNetwork)]
    [InlineData("http://nas/", TargetKind.PrivateNetwork)]
    [InlineData("http://printer.local/", TargetKind.PrivateNetwork)]
    [InlineData("https://example.com/", TargetKind.Public)]
    [InlineData("file:///C:/games/index.html", TargetKind.LocalFile)]
    public void Own_computer_is_told_apart_from_other_private_networks(string url, TargetKind expected) =>
        Assert.Equal(expected, LocalTargets.Classify(new Uri(url)));

    // ------------------------------------------------------------ approvals

    [Fact]
    public void Approval_modes_ask_for_what_they_promise()
    {
        foreach (var mode in Enum.GetValues<ApprovalMode>())
        {
            // Sensitive actions always ask.
            Assert.True(ApprovalRules.MustAsk(mode, ActionKind.ExternalCommunication, projectTrusted: true, canUndo: true));
            Assert.True(ApprovalRules.MustAsk(mode, ActionKind.Destructive, projectTrusted: true, canUndo: true));
            Assert.False(ApprovalRules.MustAsk(mode, ActionKind.ReadFiles, false, false));
        }
        Assert.True(ApprovalRules.MustAsk(ApprovalMode.AskEveryTime, ActionKind.WriteFiles, true, true));
        Assert.True(ApprovalRules.MustAsk(ApprovalMode.AskEveryTime, ActionKind.RunCommands, true, true));
        Assert.True(ApprovalRules.MustAsk(ApprovalMode.AskEveryTime, ActionKind.Browser, true, true));
        Assert.False(ApprovalRules.MustAsk(ApprovalMode.Smart, ActionKind.WriteFiles, projectTrusted: false, canUndo: true));
        Assert.True(ApprovalRules.MustAsk(ApprovalMode.Smart, ActionKind.WriteFiles, projectTrusted: false, canUndo: false));
        Assert.False(ApprovalRules.MustAsk(ApprovalMode.Smart, ActionKind.Browser, false, false));
        Assert.True(ApprovalRules.MustAsk(ApprovalMode.Smart, ActionKind.ComputerControl, false, false));
        foreach (var action in new[] { ActionKind.WriteFiles, ActionKind.RunCommands, ActionKind.Browser, ActionKind.ComputerControl, ActionKind.Network, ActionKind.McpTool })
            Assert.False(ApprovalRules.MustAsk(ApprovalMode.TrustSession, action, false, false));
    }

    // ---------------------------------------------------- capability routing

    private static McpServerSpec Spec(string id) => new(id.Replace('-', '_'), "npx", ["-y", id], new Dictionary<string, string>(), SkillId: id);

    private static (AgexCore Core, IReadOnlyList<TeamMember> Members) Team(Sandbox sandbox, params string[] agents)
    {
        var core = sandbox.Core();
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        return (core, core.BuildMembers(profile, agentIds: agents));
    }

    [Fact]
    public void Browser_work_goes_to_agents_that_have_a_browser()
    {
        using var sandbox = new Sandbox("route-browser");
        var (core, members) = Team(sandbox, "codex", "antigravity", "gemini-cli");
        var intent = RequestClassifier.Classify("open the local game and play it");
        var playwright = new ToolServer("playwright-mcp", "Browser (Playwright MCP)", ToolServerKind.Browser, true, Spec("playwright-mcp"));
        var plan = CapabilityRouting.Plan(intent, members, new PermissionSettings(), [playwright], windows: true);
        Assert.Empty(plan.Blocking);
        Assert.Contains("antigravity", plan.BrowserAgents); // built-in browser
        Assert.Contains("codex", plan.BrowserAgents);       // through the browser tool
        Assert.DoesNotContain("gemini-cli", plan.BrowserAgents); // file access only: never gets browser work
        Assert.Single(plan.Tools);
        Assert.True(plan.AllowNetwork);
        Assert.Contains("real browser", CapabilityRouting.Abilities(members.First(member => member.Id == "codex"), plan, true));
        Assert.DoesNotContain("browser", CapabilityRouting.Abilities(members.First(member => member.Id == "gemini-cli"), plan, true));
    }

    [Fact]
    public async Task Installed_browser_tool_is_offered_with_its_output_outside_the_project()
    {
        using var sandbox = new Sandbox("tool-servers");
        var core = sandbox.Core();
        Assert.Empty(core.ToolServers());
        var manifest = core.Skills.Catalog().Skills.First(skill => skill.Id == "playwright-mcp");
        await core.Skills.InstallAsync(manifest, manifest.Permissions.ToDictionary(permission => permission, _ => Agex.Core.Skills.PermissionChoice.AlwaysAllow), null, CancellationToken.None);
        var tool = Assert.Single(core.ToolServers());
        Assert.Equal(ToolServerKind.Browser, tool.Kind);
        Assert.True(tool.Enabled);
        Assert.False(tool.AskFirst);
        var args = tool.Spec!.Arguments.ToList();
        var output = args[args.IndexOf("--output-dir") + 1];
        Assert.StartsWith(sandbox.Platform.Paths.Temp, output);
        core.Skills.SetEnabled("playwright-mcp", false);
        Assert.False(Assert.Single(core.ToolServers()).Enabled);
    }

    [Fact]
    public void Missing_capabilities_say_exactly_what_is_missing_and_how_to_fix_it()
    {
        using var sandbox = new Sandbox("route-missing");
        var (_, members) = Team(sandbox, "codex", "gemini-cli");
        var game = RequestClassifier.Classify("open the local game and play it");
        var none = CapabilityRouting.Plan(game, members, new PermissionSettings(), [], windows: true);
        var connect = Assert.Single(none.Blocking);
        Assert.Equal(RecoveryKind.ConnectTool, connect.Fix);
        Assert.Equal("playwright-mcp", connect.Argument);
        Assert.Equal("Connect required tool", connect.FixLabel);

        var off = CapabilityRouting.Plan(game, members, new PermissionSettings { Browser = false }, [], windows: true);
        Assert.Equal(RecoveryKind.EnableBrowser, Assert.Single(off.Blocking).Fix);

        var disabled = new ToolServer("playwright-mcp", "Browser (Playwright MCP)", ToolServerKind.Browser, false, Spec("playwright-mcp"));
        Assert.Equal(RecoveryKind.TurnOnTool, Assert.Single(CapabilityRouting.Plan(game, members, new PermissionSettings(), [disabled], true).Blocking).Fix);

        var desktop = RequestClassifier.Classify("use the mouse to open notepad and type hello");
        Assert.Equal(RecoveryKind.EnableComputerControl, Assert.Single(CapabilityRouting.Plan(desktop, members, new PermissionSettings(), [], true).Blocking).Fix);
        var computer = new ToolServer("windows-mcp", "Windows Computer Use", ToolServerKind.Computer, true, Spec("windows-mcp"));
        var allowed = CapabilityRouting.Plan(desktop, members, new PermissionSettings { ComputerControl = true }, [computer], true);
        Assert.Empty(allowed.Missing);
        Assert.Equal(["codex"], allowed.ComputerAgents);

        var build = RequestClassifier.Classify("implement authentication");
        Assert.Equal(RecoveryKind.AllowFileChanges, Assert.Single(CapabilityRouting.Plan(build, members, new PermissionSettings { WriteProject = false }, [], true).Blocking).Fix);
    }

    [Fact]
    public void Web_game_with_a_browser_tool_does_not_require_computer_control()
    {
        using var sandbox = new Sandbox("route-prefers");
        var (_, members) = Team(sandbox, "codex");
        var intent = RequestClassifier.Classify("open the game and play it with mouse and keyboard");
        var playwright = new ToolServer("playwright-mcp", "Browser (Playwright MCP)", ToolServerKind.Browser, true, Spec("playwright-mcp"));
        var plan = CapabilityRouting.Plan(intent, members, new PermissionSettings(), [playwright], true);
        Assert.Empty(plan.Blocking);
        Assert.Contains(plan.Missing, item => item.Fix == RecoveryKind.EnableComputerControl && !item.Blocking);
    }

    // -------------------------------------------------------------- engine

    private static async Task<(Session Session, RequestEngine Engine)> Run(Sandbox sandbox, string request, ChatMode mode, string[]? agents = null, Action<AgexCore>? configure = null, IEngineHost? host = null, CapabilityPlan? capabilities = null, string localUrl = "")
    {
        var core = sandbox.Core();
        core.Settings.Leader = "codex";
        configure?.Invoke(core);
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        var members = core.BuildMembers(profile, agentIds: agents ?? ["codex", "antigravity"]);
        var engine = core.CreateRequest(sandbox.Project, request, host ?? new ScriptedHost(), members, profile, [], [], mode: mode, localUrl: localUrl, capabilities: capabilities);
        return (await engine.RunAsync(CancellationToken.None), engine);
    }

    [Fact]
    public async Task Hi_gets_an_instant_reply_without_planning()
    {
        using var sandbox = new Sandbox("fast-chat");
        File.WriteAllText(Path.Combine(sandbox.Project, "big.txt"), new string('x', 200_000));
        var (session, engine) = await Run(sandbox, "hi", ChatMode.Auto);
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.Equal("chat", session.Mode);
        Assert.Empty(session.Tasks);
        Assert.Contains(session.Messages, message => message.Type == MessageType.Result && message.Text == "Hello! I am a fake agent.");
        var line = Assert.Single(sandbox.FakeLog());
        Assert.Contains("|direct|", line);
        Assert.True(int.Parse(line.Split('|')[2]) < 800, line);
        Assert.Null(engine.Baseline); // no project scan for a greeting
        Assert.Empty(session.Changes);
        Assert.Single(session.Runs);
    }

    [Fact]
    public async Task Project_question_is_answered_read_only_in_one_run()
    {
        using var sandbox = new Sandbox("ask");
        var promptDir = Path.Combine(sandbox.Root, "prompts");
        Environment.SetEnvironmentVariable("FAKE_PROMPT_DIR", promptDir);
        var (session, _) = await Run(sandbox, "what is this project?", ChatMode.Auto);
        Assert.Equal("ask", session.Mode);
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.Single(session.Runs);
        var line = Assert.Single(sandbox.FakeLog());
        Assert.Contains("--sandbox read-only", line); // Codex runs read-only
        var prompt = File.ReadAllText(Directory.GetFiles(promptDir).Single());
        Assert.Contains("PROJECT FOLDER (use only this folder): " + sandbox.Project, prompt);
        Assert.DoesNotContain("TEAM:", prompt);
        Assert.DoesNotContain("INDEPENDENT PROJECT EVIDENCE", prompt);
    }

    [Fact]
    public async Task Plan_mode_writes_a_plan_and_changes_nothing()
    {
        using var sandbox = new Sandbox("plan");
        var (session, _) = await Run(sandbox, "plan how to add authentication", ChatMode.Auto);
        Assert.Equal("plan", session.Mode);
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.Contains("1. Add login.", session.Outcome!.Reason);
        Assert.Contains("Build this plan", session.Outcome.Verification);
        Assert.Empty(session.Tasks);
        Assert.Empty(session.Changes);
    }

    [Fact]
    public async Task Build_mode_still_plans_and_executes()
    {
        using var sandbox = new Sandbox("build");
        sandbox.LeaderPlans("""{"goal_status":"CONTINUE","reason":"One file.","tasks":[{"id":"a","title":"Write A","objective":"Create a.txt","executor":"Codex","dependencies":[],"affected_files":["a.txt"]}]}""",
            """{"goal_status":"COMPLETE","reason":"Done.","verification":"a.txt exists.","tasks":[]}""");
        var (session, _) = await Run(sandbox, "implement authentication", ChatMode.Auto, agents: ["codex"]);
        Assert.Equal("build", session.Mode);
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.True(File.Exists(Path.Combine(sandbox.Project, "a.txt")));
    }

    [Fact]
    public async Task Browser_task_gets_the_browser_tool_the_local_address_and_network()
    {
        using var sandbox = new Sandbox("browser-task");
        var promptDir = Path.Combine(sandbox.Root, "prompts");
        Environment.SetEnvironmentVariable("FAKE_PROMPT_DIR", promptDir);
        sandbox.LeaderPlans("""{"goal_status":"CONTINUE","reason":"Play.","tasks":[{"id":"p","title":"Play the game","objective":"Open the game and play it","executor":"Codex","dependencies":[],"affected_files":["notes.txt"]}]}""",
            """{"goal_status":"COMPLETE","reason":"Played.","verification":"Checked.","tasks":[]}""");
        var intent = RequestClassifier.Classify("open the local game and play it");
        var (_, members) = Team(sandbox, "codex");
        var playwright = new ToolServer("playwright-mcp", "Browser (Playwright MCP)", ToolServerKind.Browser, true, Spec("playwright-mcp"));
        var plan = CapabilityRouting.Plan(intent, members, new PermissionSettings(), [playwright], true);
        var (session, _) = await Run(sandbox, "open the local game and play it", ChatMode.Auto, agents: ["codex"], capabilities: plan, localUrl: "http://127.0.0.1:4567/");
        Assert.Equal(SessionStatus.Complete, session.Status);
        var log = sandbox.FakeLog();
        var planning = log.First(line => line.Contains("|leader|"));
        var task = log.First(line => line.Contains("|executor|"));
        Assert.DoesNotContain("mcp_servers.playwright_mcp", planning); // the leader only plans
        Assert.Contains("mcp_servers.playwright_mcp", task);
        Assert.Contains("mcp_servers.playwright_mcp.default_tools_approval_mode=\"approve\"", task);
        Assert.Contains("sandbox_workspace_write.network_access=true", task);
        var leaderPrompt = File.ReadAllText(Directory.GetFiles(promptDir, "*leader*").First());
        Assert.Contains("LOCAL WEB: AGEX serves the project folder at http://127.0.0.1:4567/", leaderPrompt);
        Assert.Contains("Do not carry out the work yourself while planning", leaderPrompt);
        Assert.Contains("can open and use pages in a real browser", leaderPrompt);
        var taskPrompt = File.ReadAllText(Directory.GetFiles(promptDir, "*executor*").First());
        Assert.Contains("TOOLS AGEX GAVE YOU FOR THIS TASK: Browser (Playwright MCP)", taskPrompt);
        Assert.Contains("never with file://", taskPrompt);
    }

    [Fact]
    public async Task Missing_browser_shows_recovery_options()
    {
        using var sandbox = new Sandbox("recovery");
        sandbox.LeaderPlans("""{"goal_status":"BLOCKED","reason":"Could not open the browser: access was blocked.","tasks":[]}""");
        var (session, _) = await Run(sandbox, "open the local game and play it", ChatMode.Auto, agents: ["codex"]);
        Assert.Equal(SessionStatus.Failed, session.Status);
        var kinds = session.Outcome!.Recovery.Select(item => item.Kind).ToList();
        Assert.Contains(nameof(RecoveryKind.ConnectTool), kinds);
        Assert.Contains(nameof(RecoveryKind.Retry), kinds);
        Assert.Equal("playwright-mcp", session.Outcome.Recovery.First(item => item.Kind == nameof(RecoveryKind.ConnectTool)).Argument);
    }

    [Fact]
    public async Task Ask_every_time_asks_per_task_and_trust_this_session_never_asks()
    {
        const string plan = """{"goal_status":"CONTINUE","reason":"Two.","tasks":[{"id":"a","title":"A","objective":"Create a.txt","executor":"Codex","dependencies":[],"affected_files":["a.txt"]},{"id":"b","title":"B","objective":"Create b.txt","executor":"Codex","dependencies":["a"],"affected_files":["b.txt"]}]}""";
        const string done = """{"goal_status":"COMPLETE","reason":"Done.","verification":"Both exist.","tasks":[]}""";
        using (var sandbox = new Sandbox("ask-every"))
        {
            sandbox.LeaderPlans(plan, done);
            var host = new ScriptedHost();
            var (session, _) = await Run(sandbox, "implement it", ChatMode.Build, agents: ["codex"], configure: core => core.Settings.Approvals.Mode = ApprovalMode.AskEveryTime, host: host);
            Assert.Equal(SessionStatus.Complete, session.Status);
            Assert.True(host.ApprovalRequests >= 3, $"asked {host.ApprovalRequests} times"); // file changes, then each task
        }
        using (var sandbox = new Sandbox("trust"))
        {
            sandbox.LeaderPlans(plan, done);
            var host = new ScriptedHost();
            var (session, _) = await Run(sandbox, "implement it", ChatMode.Build, agents: ["codex"], configure: core => core.Settings.Approvals.Mode = ApprovalMode.TrustSession, host: host);
            Assert.Equal(SessionStatus.Complete, session.Status);
            Assert.Equal(0, host.ApprovalRequests);
        }
        using (var sandbox = new Sandbox("trust-sensitive"))
        {
            var host = new ScriptedHost(ApprovalDecision.Deny);
            var (session, _) = await Run(sandbox, "send an email to my clients about the release", ChatMode.Build, agents: ["codex"], configure: core => core.Settings.Approvals.Mode = ApprovalMode.TrustSession, host: host);
            Assert.Equal(1, host.ApprovalRequests); // sensitive actions still ask
            Assert.Contains("did not allow", session.Outcome!.Reason);
            Assert.Empty(sandbox.FakeLog());
        }
    }

    // ------------------------------------------------------ path handling

    [Theory]
    [InlineData("hoollaaa")]
    [InlineData("my game (copy) é ü 游戏")]
    [InlineData("project with spaces")]
    public async Task Agents_always_work_in_the_active_project_folder(string name)
    {
        using var sandbox = new Sandbox("paths");
        // Similar names next to each other: agents must never land in the neighbour.
        var project = Path.Combine(sandbox.Root, name);
        var similar = Path.Combine(sandbox.Root, name.Replace('o', '0').Replace('l', '1') + "-other");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(similar);
        var core = sandbox.Core();
        core.Settings.Leader = "codex";
        var profile = core.SettingsStore.LoadProject(project);
        var engine = core.CreateRequest(project, "what does this project do?", new ScriptedHost(), core.BuildMembers(profile, agentIds: ["codex"]), profile, [], [], mode: ChatMode.Auto);
        var session = await engine.RunAsync(CancellationToken.None);
        Assert.Equal(project, session.Project);
        var line = Assert.Single(sandbox.FakeLog());
        Assert.Equal(Folder(project), Folder(line.Split('|')[3]));
        Assert.Contains("-C " + project, line);
        Assert.Equal(project, new SessionStore(sandbox.Platform.Paths.Sessions).Load(session.Id)!.Project);
    }

    /// <summary>A folder as the process sees it. On macOS the temp folder /var is a link to /private/var.</summary>
    private static string Folder(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return OperatingSystem.IsMacOS() && full.StartsWith("/private/", StringComparison.Ordinal) ? full["/private".Length..] : full;
    }

    [Fact]
    public async Task Renamed_project_folder_is_used_under_its_new_name()
    {
        using var sandbox = new Sandbox("renamed");
        var before = Path.Combine(sandbox.Root, "game");
        var after = Path.Combine(sandbox.Root, "game renamed");
        Directory.CreateDirectory(before);
        Directory.Move(before, after);
        var core = sandbox.Core();
        core.Settings.Leader = "codex";
        var profile = core.SettingsStore.LoadProject(after);
        var session = await core.CreateRequest(after, "hi", new ScriptedHost(), core.BuildMembers(profile, agentIds: ["codex"]), profile, [], [], mode: ChatMode.Build).RunAsync(CancellationToken.None);
        Assert.Equal(after, session.Project);
        Assert.All(sandbox.FakeLog(), line => Assert.Equal(Folder(after), Folder(line.Split('|')[3])));
    }

    // ------------------------------------------------------------ local web

    [Fact]
    public async Task Local_web_server_serves_the_project_and_nothing_else()
    {
        using var sandbox = new Sandbox("web");
        var root = Path.Combine(sandbox.Root, "game é folder");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "index.html"), "<script type=module src=src/game.js></script>");
        File.WriteAllText(Path.Combine(root, "src", "game.js"), "export const x = 1;");
        File.WriteAllText(Path.Combine(root, ".env"), "SECRET=1");
        File.WriteAllText(Path.Combine(sandbox.Root, "outside.txt"), "outside");
        Assert.Equal("index.html", LocalWebServer.EntryPage(root));
        await using var server = LocalWebServer.Start(root);
        Assert.Equal("127.0.0.1", server.BaseUri.Host);
        using var http = new HttpClient();
        var page = await http.GetAsync(server.BaseUri);
        Assert.True(page.IsSuccessStatusCode);
        Assert.Contains("module", await page.Content.ReadAsStringAsync());
        var module = await http.GetAsync(new Uri(server.BaseUri, "src/game.js"));
        Assert.Equal("text/javascript", module.Content.Headers.ContentType!.MediaType);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, ".env"))).StatusCode);
        Assert.Null(server.Resolve("/../outside.txt"));
        Assert.Null(server.Resolve("/%2e%2e/outside.txt"));
        Assert.Null(server.Resolve("/.env"));
        Assert.Null(server.Resolve("/C:/Windows/win.ini"));
        Assert.NotNull(server.Resolve("/src/game.js?v=2"));
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, (await http.PostAsync(server.BaseUri, new StringContent("x"))).StatusCode);
    }

    // -------------------------------------------------------- model settings

    [Fact]
    public void Model_settings_follow_what_each_agent_supports()
    {
        using var sandbox = new Sandbox("model-settings");
        var core = sandbox.Core();
        var codex = core.Registry.Get("codex")!.ModelSettings;
        var antigravity = core.Registry.Get("antigravity")!.ModelSettings;
        var gemini = core.Registry.Get("gemini-cli")!.ModelSettings;
        var opencode = core.Registry.Get("opencode")!.ModelSettings;
        var claude = core.Registry.Get("claude-code")!.ModelSettings;
        var ollama = core.Registry.Get("ollama")!.ModelSettings;
        Assert.True(codex.SupportsReasoningEffort);
        Assert.True(codex.SupportsCustomEndpoint);
        Assert.Equal(["low", "medium", "high", "max"], antigravity.ReasoningEfforts);
        Assert.False(gemini.SupportsReasoningEffort);
        Assert.False(opencode.SupportsReasoningEffort);
        Assert.False(claude.SupportsReasoningEffort);
        Assert.False(ollama.SupportsReasoningEffort);
        Assert.True(ollama.SupportsTemperature);
        Assert.True(ollama.SupportsContextWindowSelection);
        Assert.False(codex.SupportsTemperature);
        Assert.All(new[] { codex, antigravity, gemini, opencode, claude, ollama }, support => Assert.True(support.SupportsModelSelection));

        // A saved setting the agent cannot use is never sent.
        core.Settings.AgentOptions["gemini-cli"] = new AgentOptions { Effort = "high", Temperature = 0.2 };
        core.Settings.AgentOptions["antigravity"] = new AgentOptions { Effort = "max" };
        var members = core.BuildMembers(core.SettingsStore.LoadProject(sandbox.Project), agentIds: ["gemini-cli", "antigravity"]);
        Assert.Null(members.First(member => member.Id == "gemini-cli").Effort);
        Assert.Null(members.First(member => member.Id == "gemini-cli").Temperature);
        Assert.Equal("max", members.First(member => member.Id == "antigravity").Effort);
        Assert.Contains("--effort", string.Join(' ', AntigravityAdapter.BuildArguments(new AgentInvocation { Prompt = "", WorkingDirectory = sandbox.Project, Effort = "max" }, "log")));
    }

    [Fact]
    public void Ollama_sends_temperature_and_context_only_when_set()
    {
        Assert.Null(OllamaAdapter.Options(new AgentInvocation { Prompt = "", WorkingDirectory = "." }));
        var options = OllamaAdapter.Options(new AgentInvocation { Prompt = "", WorkingDirectory = ".", Temperature = 0.3, ContextWindow = 16384 })!;
        Assert.Equal(0.3, options["temperature"]);
        Assert.Equal(16384, options["num_ctx"]);
        Assert.Null(OllamaAdapter.Options(new AgentInvocation { Prompt = "", WorkingDirectory = ".", Temperature = 9, ContextWindow = 3 }));
    }

    [Fact]
    public void Network_and_sandbox_switches_follow_the_request()
    {
        var project = Path.Combine(Path.GetTempPath(), "a b é");
        var codex = string.Join(' ', CodexAdapter.BuildArguments(new AgentInvocation { Prompt = "", WorkingDirectory = project, AllowWrites = true, AllowNetwork = true }, "out.txt"));
        Assert.Contains("sandbox_workspace_write.network_access=true", codex);
        Assert.DoesNotContain("network_access", string.Join(' ', CodexAdapter.BuildArguments(new AgentInvocation { Prompt = "", WorkingDirectory = project, AllowWrites = true }, "out.txt")));
        Assert.Contains("--sandbox", AntigravityAdapter.BuildArguments(new AgentInvocation { Prompt = "", WorkingDirectory = project }, "log"));
        Assert.DoesNotContain("--sandbox", AntigravityAdapter.BuildArguments(new AgentInvocation { Prompt = "", WorkingDirectory = project, AllowCommands = true, AllowNetwork = true }, "log"));
    }

    // ----------------------------------------------------------------- usage

    [Fact]
    public void Codex_quota_is_read_without_account_details()
    {
        const string reply = """{"ordinaryUsageAllowed":true,"rateLimits":{"limitId":"codex","primary":{"usedPercent":12,"windowDurationMins":300,"resetsAt":1790456861},"secondary":{"usedPercent":5,"windowDurationMins":10080,"resetsAt":1790972411},"planType":"plus"},"accountId":"hidden","email":"someone@example.com"}""";
        using var document = JsonDocument.Parse(reply);
        var usage = AccountUsageParser.FromCodex(document.RootElement);
        Assert.True(usage.Reported);
        Assert.Equal("plus", usage.Plan);
        Assert.Equal(["5-hour", "Weekly"], usage.Windows.Select(window => window.Label).ToArray());
        Assert.Equal(12, usage.Windows[0].UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790456861), usage.Windows[0].ResetsAt);
        Assert.DoesNotContain("example.com", JsonSerializer.Serialize(usage));
        using var empty = JsonDocument.Parse("{}");
        Assert.False(AccountUsageParser.FromCodex(empty.RootElement).Reported);
        Assert.Contains(typeof(IAccountUsageSource), typeof(CodexAdapter).GetInterfaces());
    }

    [Fact]
    public void Usage_history_adds_up_what_agents_reported()
    {
        var now = new DateTimeOffset(2026, 9, 26, 15, 0, 0, TimeSpan.Zero);
        SessionSummary Item(DateTimeOffset at, string project, long input, long output, decimal? cost = null) => new()
        {
            Id = Guid.NewGuid().ToString("N"), CreatedAt = at, Project = project, Mode = "build",
            Usage = new() { ["codex"] = new UsageReport { InputTokens = input, OutputTokens = output, CachedInputTokens = input / 2, ReasoningTokens = 10, CostUsd = cost, Source = "Codex" } },
        };
        var project = Path.Combine(Path.GetTempPath(), "usage-project");
        var sessions = new[]
        {
            Item(now.AddHours(-1), project, 1000, 100),
            Item(now.AddDays(-2), project, 2000, 200),
            Item(now.AddDays(-20), Path.Combine(Path.GetTempPath(), "other"), 4000, 400, 0.5m),
            new SessionSummary { Id = "old", CreatedAt = now.AddHours(-2), Project = project, Mode = "chat" },
        };
        var today = UsageHistory.Summarize(sessions, UsagePeriod.Today, project, now);
        Assert.Equal(2, today.Requests);
        Assert.Equal(1, today.RequestsWithoutUsage);
        Assert.Equal(1000, today.Total!.InputTokens);
        Assert.Equal(3000, UsageHistory.Summarize(sessions, UsagePeriod.ThisProject, project, now).Total!.InputTokens);
        var all = UsageHistory.Summarize(sessions, UsagePeriod.All, null, now);
        Assert.Equal(7000, all.ByAgent["codex"].InputTokens);
        Assert.Equal(0.5m, all.Total!.CostUsd);
        Assert.Equal("564k", UsageHistory.Tokens(564_000));
        Assert.Equal("6.3k", UsageHistory.Tokens(6_300));
        Assert.Contains("reasoning", UsageHistory.Describe(sessions[0].Usage["codex"]));
        Assert.DoesNotContain("cost", UsageHistory.Describe(sessions[0].Usage["codex"]));
    }

    // -------------------------------------------------------- token savings

    [Fact]
    public void Auto_skills_skip_what_cannot_matter()
    {
        var game = RequestClassifier.Classify("open the game as a normal user and play it and if you find any bugs fix it");
        const string context = "open the game as a normal user and play it and if you find any bugs fix it";
        Assert.False(SkillRelevance.IsRelevant("pdf-documents", game, context));
        Assert.False(SkillRelevance.IsRelevant("jupyter-notebook", game, context));
        Assert.False(SkillRelevance.IsRelevant("security-threat-model", game, context));
        Assert.False(SkillRelevance.IsRelevant("web-fetch", game, context));
        Assert.False(SkillRelevance.IsRelevant("playwright-mcp", game, context)); // handed out by capability routing
        Assert.True(SkillRelevance.IsRelevant("systematic-debugging", game, context));
        Assert.True(SkillRelevance.IsRelevant("requesting-code-review", game, context));
        Assert.True(SkillRelevance.IsRelevant("webapp-testing", game, context));
        Assert.True(SkillRelevance.IsRelevant("pdf-documents", game, context + " report.pdf"));
        Assert.False(SkillRelevance.IsRelevant("systematic-debugging", RequestClassifier.Classify("hi"), "hi"));
    }

    [Fact]
    public void Settings_from_2_2_keep_their_command_choice()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("""{"schema_version":4,"approvals":{"allow_commands":false,"ask_before_writes":true}}""")!.AsObject();
        using var sandbox = new Sandbox("migrate");
        var settings = Migrations.Run(node, 4, sandbox.Platform.Paths, null);
        Assert.False(settings.Permissions.RunCommands);
        Assert.Equal(ApprovalMode.Smart, settings.Approvals.Mode);
        Assert.False(settings.Permissions.ComputerControl);
        Assert.False(settings.Permissions.ExternalCommunication);
    }
}
