using Agex.Core;
using Agex.Core.Connections;
using Agex.Core.Orchestration;
using Agex.Core.Settings;
using Agex.Core.Skills;
using Agex.Core.Teams;

namespace Agex.Tests;

/// <summary>AGEX 2.5: "Connected" means an agent really received the connection and a read-only call through it worked.</summary>
public class ConnectionReadinessTests
{
    private static InstalledSkill AddFakeServer(AgexCore core, string name = "Fake Items") =>
        core.Skills.AddMcpServer(name, Sandbox.FakeAgentPath, ["mcp-server"], new Dictionary<string, string>());

    private static ConnectionItem Item(AgexCore core, string skillId) =>
        core.Connections.Build(new Dictionary<string, string>()).Single(item => item.SkillId == skillId);

    [Fact]
    public void A_configured_server_is_not_called_connected_before_an_agent_used_it()
    {
        using var sandbox = new Sandbox("ready-configured");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        var item = Item(core, skill.Id);
        Assert.Equal(ConnectionState.Configured, item.State);
        Assert.Equal(ConnectionActionKind.TestWithAgent, item.Actions[0].Kind);
        Assert.Equal("Set up, not tested", ConnectionItem.StateText(item.State));
    }

    [Fact]
    public async Task Agent_test_that_calls_a_read_only_tool_makes_it_connected_and_survives_a_restart()
    {
        using var sandbox = new Sandbox("ready-ok");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        var check = await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None);
        Assert.True(check.Ok, check.Message);
        Assert.True(check.ToolSeen);
        Assert.Equal("list_items", check.Tool); // the tool that only reads, never create_item
        Assert.Contains("mcp_servers.", sandbox.FakeLog().Last()); // the real connection config reached the agent
        var item = Item(core, skill.Id);
        Assert.Equal(ConnectionState.Connected, item.State);
        Assert.Contains("Works with Codex", item.Detail);

        // Restart: the result is kept.
        var again = sandbox.Core();
        Assert.Equal(ConnectionState.Connected, Item(again, skill.Id).State);

        // Disconnect and connect again: a new test is needed.
        again.Skills.Remove(skill.Id);
        var readded = AddFakeServer(again);
        Assert.Equal(ConnectionState.Configured, Item(again, readded.Id).State);
    }

    [Fact]
    public async Task Agent_that_does_not_receive_the_tools_breaks_the_connection()
    {
        using var sandbox = new Sandbox("ready-no-tools");
        sandbox.Mode("codex", "no-tools");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        var check = await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None);
        Assert.False(check.Ok);
        Assert.Contains("did not receive the tools", check.Message);
        var item = Item(core, skill.Id);
        Assert.Equal(ConnectionState.Broken, item.State);
        Assert.Equal("Test again", item.Actions[0].Label);
    }

    [Fact]
    public async Task Healthy_server_but_agent_fails_to_start_is_not_connected()
    {
        using var sandbox = new Sandbox("ready-agent-fails");
        sandbox.Mode("codex", "fail-start");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        var check = await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None);
        Assert.False(check.Ok);
        Assert.Contains("could not run the test", check.Message);
        Assert.Equal(ConnectionState.Broken, Item(core, skill.Id).State);
    }

    [Fact]
    public async Task Tool_seen_but_read_only_call_failing_is_broken()
    {
        using var sandbox = new Sandbox("ready-call-fails");
        sandbox.Mode("codex", "tool-fails");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        var check = await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None);
        Assert.False(check.Ok);
        Assert.True(check.ToolSeen);
        Assert.Contains("read-only call failed", check.Message);
        Assert.Contains("not signed in", check.Message);
    }

    [Fact]
    public async Task Server_that_does_not_start_is_broken_before_any_agent_runs()
    {
        using var sandbox = new Sandbox("ready-server-crash");
        Environment.SetEnvironmentVariable("FAKE_MCP_MODE", "crash");
        try
        {
            var core = sandbox.Core();
            var skill = AddFakeServer(core);
            var check = await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None);
            Assert.False(check.Ok);
            Assert.Contains("server did not start", check.Message);
            Assert.DoesNotContain(sandbox.FakeLog(), line => line.StartsWith("codex|"));
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MCP_MODE", null); }
    }

    [Fact]
    public async Task Missing_key_means_not_configured_and_no_test_runs()
    {
        using var sandbox = new Sandbox("ready-key");
        var core = sandbox.Core();
        var notion = core.Skills.Catalog().Skills.Single(skill => skill.Id == "notion-mcp");
        core.Skills.Disconnect("notion-mcp", notion);
        typeof(SkillManager).GetMethod("Upsert", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(core.Skills, [new InstalledSkill { Id = notion.Id, Manifest = notion, Enabled = true, PermissionChoices = notion.Permissions.ToDictionary(permission => permission, _ => PermissionChoice.AlwaysAllow) }]);
        var check = await core.ConnectionTester.TestAsync("notion-mcp", "codex", CancellationToken.None);
        Assert.False(check.Ok);
        Assert.Contains("not configured", check.Message);
        Assert.Empty(sandbox.FakeLog());
    }

    [Fact]
    public async Task Works_with_one_agent_and_says_which_agents_cannot_use_it()
    {
        using var sandbox = new Sandbox("ready-per-agent");
        var core = sandbox.Core();
        core.Settings.EnabledAgents = ["codex", "antigravity"];
        var skill = AddFakeServer(core);
        var refused = await core.ConnectionTester.TestAsync(skill.Id, "antigravity", CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Contains("Antigravity cannot use connected tools", refused.Message);
        Assert.True((await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None)).Ok);
        var item = Item(core, skill.Id);
        Assert.Equal(ConnectionState.Connected, item.State);
        Assert.Contains("Not available to Antigravity", item.Detail);
    }

    [Fact]
    public void A_connection_no_enabled_agent_can_use_is_not_available()
    {
        using var sandbox = new Sandbox("ready-incompatible");
        var core = sandbox.Core();
        core.Settings.EnabledAgents = ["antigravity"];
        var skill = AddFakeServer(core);
        var item = Item(core, skill.Id);
        Assert.Equal(ConnectionState.AgentUnavailable, item.State);
        Assert.Equal(ConnectionActionKind.OpenAgents, item.Actions[0].Kind);
        Assert.Contains("Antigravity cannot use it", item.Detail);
    }

    [Fact]
    public void Team_connection_with_only_incompatible_agents_is_not_ready()
    {
        using var sandbox = new Sandbox("ready-team");
        var core = sandbox.Core();
        core.Settings.EnabledAgents = ["antigravity"];
        var playwright = core.Skills.Catalog().Skills.Single(skill => skill.Id == "playwright-mcp");
        typeof(SkillManager).GetMethod("Upsert", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(core.Skills, [new InstalledSkill { Id = playwright.Id, Manifest = playwright, Enabled = true, PermissionChoices = playwright.Permissions.ToDictionary(permission => permission, _ => PermissionChoice.AlwaysAllow) }]);
        var team = new JobTeam { Id = "t", Name = "T", Summary = "", Requirements = [new(RequirementKind.Skill, "playwright-mcp", "Browser", RequirementLevel.Required, "")] };
        var status = Assert.Single(core.Teams.Check(team));
        Assert.NotEqual(RequirementState.Ready, status.State);
        Assert.Contains("none of your enabled agents can use it", status.Detail);
    }

    [Theory]
    [InlineData("what's the weather like in Paris? search the web", true)]
    [InlineData("open example.com in the browser and tell me the page title", true)]
    [InlineData("hi", true)]
    [InlineData("open google.com in the browser and read the headline. Do not change any files.", true)]
    [InlineData("fix the bug in the login form", false)]
    [InlineData("plan how to add authentication", false)]
    public void System_connections_do_not_need_a_project_folder(string request, bool allowed)
    {
        var intent = RequestClassifier.Classify(request);
        var projectless = RequestClassifier.WithoutProject(intent);
        Assert.Equal(allowed, projectless is not null);
        if (projectless is null) return;
        Assert.False(projectless.Has(NeededCapability.ReadProject) || projectless.Has(NeededCapability.EditProject));
        Assert.Equal(intent.Has(NeededCapability.Browser), projectless.Has(NeededCapability.Browser));
        Assert.Equal(intent.Has(NeededCapability.ExternalNetwork), projectless.Has(NeededCapability.ExternalNetwork));
    }

    [Fact]
    public void Do_not_change_files_is_not_a_request_to_change_files()
    {
        Assert.False(RequestClassifier.Classify("Open the counter page in a real browser, click Add one twice and tell me the number. Do not change any files.").Has(NeededCapability.EditProject));
        Assert.True(RequestClassifier.Classify("change the button color to blue").Has(NeededCapability.EditProject));
    }

    [Fact]
    public async Task Connected_tools_reach_the_agent_without_a_project()
    {
        using var sandbox = new Sandbox("ready-projectless");
        var core = sandbox.Core();
        core.Settings.Leader = "codex";
        var skill = AddFakeServer(core);
        core.Skills.SetPermission(skill.Id, SkillPermission.RunCommands, PermissionChoice.AlwaysAllow);
        var profile = new ProjectProfile { Path = sandbox.Project, Name = "Chat", AllowWrites = false };
        var active = core.Skills.ForRequest(null, [skill.Id]);
        var intent = RequestClassifier.WithoutProject(RequestClassifier.Classify("list the items in Fake Items"))!;
        var engine = core.CreateRequest(sandbox.Project, "list the items in Fake Items", new ScriptedHost(), core.BuildMembers(profile, agentIds: ["codex"]), profile,
            active.Instructions, active.McpServers.Concat(active.NeedApproval.Select(pair => core.Skills.SpecFor(pair.Item1)!)).ToList(), mode: ChatMode.Ask, projectless: true, intentOverride: intent, skillsChosen: true);
        await engine.RunAsync(CancellationToken.None);
        Assert.Contains(sandbox.FakeLog(), line => line.StartsWith("codex|") && line.Contains("mcp_servers."));
    }
}
