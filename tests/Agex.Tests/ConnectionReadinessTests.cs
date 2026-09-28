using Agex.Core;
using Agex.Core.Connections;
using Agex.Core.Orchestration;
using Agex.Core.Settings;
using Agex.Core.Skills;
using Agex.Core.Teams;
using System.Text.Json;

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
        Assert.Equal("Needs attention", ConnectionItem.StateText(item.State));
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
    public async Task Healthy_server_but_agent_fails_to_start_is_not_called_broken()
    {
        using var sandbox = new Sandbox("ready-agent-fails");
        sandbox.Mode("codex", "fail-start");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        var check = await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None);
        Assert.False(check.Ok);
        Assert.Contains("could not run the test", check.Message);
        Assert.Equal(ConnectionState.Configured, Item(core, skill.Id).State);
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
            Assert.Contains("Server check failed", check.Message);
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
    public async Task Same_connection_works_with_native_and_gateway_agents()
    {
        using var sandbox = new Sandbox("ready-per-agent");
        var core = sandbox.Core();
        core.Settings.EnabledAgents = ["codex", "antigravity", "gemini-cli", "opencode"];
        var skill = AddFakeServer(core);
        var refused = await core.ConnectionTester.TestAsync(skill.Id, "antigravity", CancellationToken.None);
        Assert.True(refused.Ok, refused.Message);
        Assert.True(refused.ToolSeen);
        var gemini = await core.ConnectionTester.TestAsync(skill.Id, "gemini-cli", CancellationToken.None);
        Assert.True(gemini.Ok, gemini.Message);
        var openCode = await core.ConnectionTester.TestAsync(skill.Id, "opencode", CancellationToken.None);
        Assert.True(openCode.Ok, openCode.Message);
        Assert.True((await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None)).Ok);
        var item = Item(core, skill.Id);
        Assert.Equal(ConnectionState.Connected, item.State);
        Assert.Contains("Usable by Codex, Antigravity, Gemini CLI, OpenCode", item.Detail);
    }

    [Fact]
    public async Task Live_Antigravity_reads_a_gateway_tool_when_explicitly_enabled()
    {
        if (Environment.GetEnvironmentVariable("AGEX_LIVE_ANTIGRAVITY") != "1") return;
        var path = Environment.GetEnvironmentVariable("AGEX_LIVE_ANTIGRAVITY_PATH")
            ?? throw new InvalidOperationException("Set AGEX_LIVE_ANTIGRAVITY_PATH to the agy executable.");
        using var sandbox = new Sandbox("live-agy-gateway");
        Environment.SetEnvironmentVariable("AGEX_ANTIGRAVITY_PATH", path);
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        var check = await core.ConnectionTester.TestAsync(skill.Id, "antigravity", CancellationToken.None, TimeSpan.FromMinutes(2));
        Assert.True(check.Ok, check.Message);
    }

    [Fact]
    public async Task Gateway_rejects_a_failed_tool_call()
    {
        using var sandbox = new Sandbox("gateway-tool-fails");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        var spec = core.Skills.SpecFor(skill)!;
        Environment.SetEnvironmentVariable("FAKE_MCP_MODE", "tool-error");
        try
        {
            var failure = await Assert.ThrowsAsync<IOException>(() => core.McpProbe.CallAsync(spec, "list_items", JsonSerializer.SerializeToElement(new { }), CancellationToken.None));
            Assert.Contains("failed call", failure.Message);
            var probe = await core.McpProbe.TestAsync(spec, CancellationToken.None, readOnlyTool: "list_items");
            Assert.False(probe.Ok);
            Assert.Contains("failed call", probe.Message);
            var check = await core.ConnectionTester.TestAsync(skill.Id, "antigravity", CancellationToken.None);
            Assert.False(check.Ok);
            Assert.Equal(ConnectionState.Broken, Item(core, skill.Id).State);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MCP_MODE", null); }
    }

    [Fact]
    public async Task Gateway_keeps_server_session_across_tool_calls()
    {
        using var sandbox = new Sandbox("gateway-session");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        Environment.SetEnvironmentVariable("FAKE_MCP_MODE", "counter");
        try
        {
            await using var session = await core.McpProbe.OpenSessionAsync(core.Skills.SpecFor(skill)!, CancellationToken.None);
            var first = await session.CallAsync("list_items", JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
            var second = await session.CallAsync("list_items", JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
            Assert.Contains("\"text\":\"1\"", first);
            Assert.Contains("\"text\":\"2\"", second);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MCP_MODE", null); }
    }

    [Theory]
    [InlineData("tools-only")]
    [InlineData("resources-list-error")]
    public async Task Working_tools_remain_usable_without_a_resource_list(string mode)
    {
        using var sandbox = new Sandbox("tool-only-health");
        Environment.SetEnvironmentVariable("FAKE_MCP_MODE", mode);
        try
        {
            var core = sandbox.Core();
            var skill = AddFakeServer(core);
            var check = await core.ConnectionTester.TestAsync(skill.Id, "antigravity", CancellationToken.None);
            Assert.True(check.Ok, check.Message);
            Assert.Equal(ConnectionState.Connected, Item(core, skill.Id).State);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MCP_MODE", null); }
    }

    [Fact]
    public async Task Agent_crash_does_not_revoke_a_working_connection()
    {
        using var sandbox = new Sandbox("agent-independent-health");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        Assert.True((await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None)).Ok);
        sandbox.Mode("codex", "fail-start");
        var failed = await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None);
        Assert.False(failed.Ok);
        Assert.True(failed.AgentFailure);
        Assert.Equal(ConnectionState.Connected, Item(core, skill.Id).State);
    }

    [Fact]
    public async Task Connection_failure_and_agent_switch_preserve_project_choices()
    {
        using var sandbox = new Sandbox("connection-state-survives");
        var core = sandbox.Core();
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        profile.Team = "saved-team";
        profile.AgentOptions["antigravity"] = new AgentOptions { Model = "auto" };
        profile.SkillOverrides["saved-skill"] = true;
        core.SettingsStore.SaveProject(profile);
        var skill = AddFakeServer(core);
        Environment.SetEnvironmentVariable("FAKE_MCP_MODE", "crash");
        try { Assert.False((await core.ConnectionTester.TestAsync(skill.Id, "codex", CancellationToken.None)).Ok); }
        finally { Environment.SetEnvironmentVariable("FAKE_MCP_MODE", null); }
        Assert.True((await core.ConnectionTester.TestAsync(skill.Id, "antigravity", CancellationToken.None)).Ok);
        var restarted = sandbox.Core();
        var restored = restarted.SettingsStore.LoadProject(sandbox.Project);
        Assert.Equal("saved-team", restored.Team);
        Assert.Equal("auto", restored.AgentOptions["antigravity"].Model);
        Assert.True(restored.SkillOverrides["saved-skill"]);
        Assert.Equal(ConnectionState.Connected, Item(restarted, skill.Id).State);
    }

    [Fact]
    public async Task Connected_resource_can_be_listed_and_read_by_gateway_agent()
    {
        using var sandbox = new Sandbox("gateway-resource");
        sandbox.Mode("agy", "resource-read");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        var spec = core.Skills.SpecFor(skill)!;
        await using (var session = await core.McpProbe.OpenSessionAsync(spec, CancellationToken.None))
        {
            Assert.Contains(session.Resources, resource => resource.Uri == "fake://current/document");
            Assert.Contains(session.ResourceTemplates, template => template.UriTemplate == "fake://documents/{id}");
            Assert.Contains("Connected document contents", await session.ReadResourceAsync("fake://current/document", CancellationToken.None));
            Assert.Contains("Connected document contents", await session.ReadResourceAsync("fake://documents/123", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadResourceAsync("fake://unknown", CancellationToken.None));
        }
        var adapter = core.Registry.Get("antigravity")!;
        var result = await new AgentCapabilityGateway(core.McpProbe).RunAsync(adapter, core.Registry.DetectionForRun(adapter.Id), new Agex.Core.Agents.AgentInvocation
        {
            Prompt = "AGEX direct reply. Read the current document.", WorkingDirectory = sandbox.Project,
            McpServers = [spec], Label = "resource test", Timeout = TimeSpan.FromSeconds(30),
        }, CancellationToken.None);
        Assert.True(result.Success, result.Reason);
        Assert.Contains("Connected document contents", result.Text);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("antigravity")]
    public async Task Resource_only_connection_is_usable_by_native_and_gateway_agents(string agent)
    {
        using var sandbox = new Sandbox("resource-only");
        Environment.SetEnvironmentVariable("FAKE_MCP_MODE", "resources-only");
        try
        {
            var core = sandbox.Core();
            var skill = AddFakeServer(core);
            var check = await core.ConnectionTester.TestAsync(skill.Id, agent, CancellationToken.None);
            Assert.True(check.Ok, check.Message);
            Assert.Equal("agex_read_resource", check.Tool);
            Assert.Equal(ConnectionState.Connected, Item(core, skill.Id).State);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MCP_MODE", null); }
    }

    [Fact]
    public async Task Resource_only_server_without_tools_capability_can_be_read()
    {
        using var sandbox = new Sandbox("resource-no-tool-capability");
        Environment.SetEnvironmentVariable("FAKE_MCP_MODE", "resources-no-tools-capability");
        try
        {
            var core = sandbox.Core();
            var skill = AddFakeServer(core);
            var check = await core.ConnectionTester.TestAsync(skill.Id, "antigravity", CancellationToken.None);
            Assert.True(check.Ok, check.Message);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MCP_MODE", null); }
    }

    [Fact]
    public async Task Listed_resources_work_when_optional_tool_list_fails()
    {
        using var sandbox = new Sandbox("resource-without-tool-list");
        Environment.SetEnvironmentVariable("FAKE_MCP_MODE", "tools-list-error");
        try
        {
            var core = sandbox.Core();
            var skill = AddFakeServer(core);
            var check = await core.ConnectionTester.TestAsync(skill.Id, "antigravity", CancellationToken.None);
            Assert.True(check.Ok, check.Message);
            Assert.Equal("agex_read_resource", check.Tool);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MCP_MODE", null); }
    }

    [Fact]
    public void Expired_connection_test_requires_a_new_check()
    {
        using var sandbox = new Sandbox("ready-expired");
        var core = sandbox.Core();
        var skill = AddFakeServer(core);
        core.ConnectionChecks.Record(new ConnectionCheck
        {
            SkillId = skill.Id, Agent = "codex", Ok = true, ToolSeen = true,
            Configuration = ConnectionTester.Configuration(skill, core.Skills.SpecFor(skill)),
            At = DateTimeOffset.UtcNow.AddHours(-25), Message = "Previously worked.",
        });
        Assert.Equal(ConnectionState.Configured, Item(core, skill.Id).State);
    }

    [Fact]
    public void A_connection_with_a_gateway_agent_is_configured()
    {
        using var sandbox = new Sandbox("ready-incompatible");
        var core = sandbox.Core();
        core.Settings.EnabledAgents = ["antigravity"];
        var skill = AddFakeServer(core);
        var item = Item(core, skill.Id);
        Assert.Equal(ConnectionState.Configured, item.State);
        Assert.Equal(ConnectionActionKind.TestWithAgent, item.Actions[0].Kind);
    }

    [Fact]
    public void Team_connection_with_gateway_agent_is_ready()
    {
        using var sandbox = new Sandbox("ready-team");
        var core = sandbox.Core();
        core.Settings.EnabledAgents = ["antigravity"];
        var playwright = core.Skills.Catalog().Skills.Single(skill => skill.Id == "playwright-mcp");
        typeof(SkillManager).GetMethod("Upsert", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(core.Skills, [new InstalledSkill { Id = playwright.Id, Manifest = playwright, Enabled = true, PermissionChoices = playwright.Permissions.ToDictionary(permission => permission, _ => PermissionChoice.AlwaysAllow) }]);
        var team = new JobTeam { Id = "t", Name = "T", Summary = "", Requirements = [new(RequirementKind.Skill, "playwright-mcp", "Browser", RequirementLevel.Required, "")] };
        var status = Assert.Single(core.Teams.Check(team));
        Assert.Equal(RequirementState.Ready, status.State);
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
