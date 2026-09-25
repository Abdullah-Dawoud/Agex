using Agex.Core.Attachments;
using Agex.Core.Connections;
using Agex.Core.Orchestration;
using Agex.Core.Projects;
using Agex.Core.Sessions;
using Agex.Core.Settings;
using Agex.Core.Skills;
using Agex.Core.Teams;

namespace Agex.Tests;

/// <summary>AGEX 2.2: connections, MCP discovery, skill selection, change counts and recommendations.</summary>
public class ConnectionTests
{
    // ------------------------------------------------------------ line diff

    [Fact]
    public void Line_diff_counts_added_and_removed_lines()
    {
        var before = new[] { "a", "b", "c", "d" };
        var after = new[] { "a", "B", "c", "d", "e", "f" };
        Assert.Equal((3, 1), LineDiff.Count(before, after));
        var diff = LineDiff.Compute(before, after)!;
        Assert.Equal(["a", "b", "B", "c", "d", "e", "f"], diff.Select(line => line.Text));
        Assert.Equal(DiffLineKind.Removed, diff[1].Kind);
        Assert.Equal(2, diff[1].OldNumber);
        Assert.Equal(DiffLineKind.Added, diff[2].Kind);
        Assert.Equal(2, diff[2].NewNumber);
        Assert.Equal((0, 0), LineDiff.Count(before, before));
        Assert.Equal((2, 0), LineDiff.Count([], ["x", "y"]));
    }

    [Fact]
    public void Line_diff_handles_windows_line_endings_and_trailing_newline()
    {
        Assert.Equal(["one", "two"], LineDiff.SplitLines("one\r\ntwo\r\n"));
        Assert.Empty(LineDiff.SplitLines(""));
    }

    [Fact]
    public void Baseline_describes_added_modified_deleted_and_renamed_files_with_line_counts()
    {
        using var sandbox = new Sandbox("baseline");
        var root = sandbox.Project;
        File.WriteAllText(Path.Combine(root, "keep.txt"), "1\n2\n3\n");
        File.WriteAllText(Path.Combine(root, "old.txt"), "x\ny\n");
        File.WriteAllText(Path.Combine(root, "move.txt"), "same content\n");
        File.WriteAllBytes(Path.Combine(root, "image.bin"), [1, 0, 2, 3]);
        var before = ProjectScanner.List(root);
        var baseline = TextBaseline.Capture(root, before);
        File.WriteAllText(Path.Combine(root, "keep.txt"), "1\ntwo\n3\n4\n");
        File.Delete(Path.Combine(root, "old.txt"));
        File.Move(Path.Combine(root, "move.txt"), Path.Combine(root, "moved.txt"));
        File.WriteAllText(Path.Combine(root, "new.txt"), "a\nb\nc\n");
        // Make sure the modification is seen even on file systems with coarse timestamps.
        File.SetLastWriteTimeUtc(Path.Combine(root, "keep.txt"), DateTime.UtcNow.AddMinutes(1));
        var changes = baseline.Describe(ProjectScanner.Diff(before, ProjectScanner.List(root)));
        var byPath = changes.ToDictionary(change => change.Path);
        Assert.Equal(("modified", 2, 1), (byPath["keep.txt"].Kind, byPath["keep.txt"].Added!.Value, byPath["keep.txt"].Removed!.Value));
        Assert.Equal(("deleted", 0, 2), (byPath["old.txt"].Kind, byPath["old.txt"].Added!.Value, byPath["old.txt"].Removed!.Value));
        Assert.Equal(("added", 3, 0), (byPath["new.txt"].Kind, byPath["new.txt"].Added!.Value, byPath["new.txt"].Removed!.Value));
        Assert.Equal("renamed", byPath["moved.txt"].Kind);
        Assert.Equal("move.txt", byPath["moved.txt"].OldPath);
        Assert.Equal(0, byPath["moved.txt"].Added);
        Assert.False(byPath.ContainsKey("move.txt"));
    }

    // -------------------------------------------------------- MCP discovery

    [Fact]
    public void Existing_mcp_servers_are_read_from_json_and_jsonc_without_secret_values()
    {
        const string claude = """
            { "mcpServers": { "github": { "type": "stdio", "command": "npx", "args": ["-y", "@x/github"], "env": { "GITHUB_TOKEN": "ghp_secretvalue" } } },
              "projects": { "C:/work/app": { "mcpServers": { "db": { "command": "uvx", "args": ["db-mcp"] } } } } }
            """;
        var found = McpConfigScanner.ParseJson(claude, "Claude Code", "x.json", "mcpServers", "C:/work/app").ToList();
        Assert.Equal(2, found.Count);
        var github = found.Single(item => item.Name == "github");
        Assert.Equal(["GITHUB_TOKEN"], github.EnvNames);
        Assert.DoesNotContain("ghp_secretvalue", github.Summary);
        Assert.Equal("npx -y @x/github", github.Summary);
        Assert.Contains(found, item => item.Name == "db" && item.Source.Contains("this project"));

        const string vscode = """
            // VS Code allows comments
            { "servers": { "playwright": { "type": "stdio", "command": "npx", "args": ["@playwright/mcp"], }, "remote": { "type": "http", "url": "https://example.com/mcp", "headers": { "Authorization": "Bearer abc" } } } }
            """;
        var fromVsCode = McpConfigScanner.ParseJson(vscode, "VS Code", "mcp.json", "servers").ToList();
        Assert.Equal(2, fromVsCode.Count);
        var remote = fromVsCode.Single(item => item.Name == "remote");
        Assert.Equal(("http", "https://example.com/mcp"), (remote.Transport, remote.Url));
        Assert.Equal(["Authorization"], remote.HeaderNames);

        const string opencode = """{ "mcp": { "local": { "type": "local", "command": ["npx", "-y", "tool"], "environment": { "KEY": "v" } }, "web": { "type": "remote", "url": "https://mcp.example.com" } } }""";
        var fromOpenCode = McpConfigScanner.ParseJson(opencode, "OpenCode", "opencode.json", "opencode").ToList();
        Assert.Equal("npx -y tool", fromOpenCode.Single(item => item.Name == "local").Summary);
        Assert.Equal("http", fromOpenCode.Single(item => item.Name == "web").Transport);
        Assert.Empty(McpConfigScanner.ParseJson("", "Empty", "e.json", "mcpServers"));
    }

    [Fact]
    public void Codex_toml_servers_are_read_with_quoted_names_and_multiline_arrays()
    {
        const string toml = """
            model = "gpt-6"
            [mcp_servers.context7]
            command = "npx"
            args = ["-y", "@upstash/context7-mcp"]

            [mcp_servers."my server"]
            command = 'C:\tools\server.exe'
            args = [
              "--port",
              "3000",
            ]
            [mcp_servers."my server".env]
            API_KEY = "secret"

            [mcp_servers.remote]
            url = "https://mcp.example.com/mcp"
            [profiles.work]
            model = "x"
            """;
        var found = McpConfigScanner.ParseCodexToml(toml, "Codex", "config.toml").ToList();
        Assert.Equal(["context7", "my server", "remote"], found.Select(item => item.Name));
        Assert.Equal(@"C:\tools\server.exe --port 3000", found[1].Summary);
        Assert.Equal(["API_KEY"], found[1].EnvNames);
        Assert.Equal("http", found[2].Transport);
    }

    [Fact]
    public void Imported_settings_are_read_only_when_the_user_imports()
    {
        using var sandbox = new Sandbox("import");
        var path = Path.Combine(sandbox.Root, "claude.json");
        File.WriteAllText(path, """{ "mcpServers": { "gh": { "command": "npx", "env": { "GITHUB_TOKEN": "ghp_x" } } } }""");
        var server = McpConfigScanner.ParseJson(File.ReadAllText(path), "Claude Code", path, "mcpServers").Single();
        Assert.Equal("ghp_x", McpConfigScanner.ReadSettings(server)["GITHUB_TOKEN"]);
    }

    [Fact]
    public void Registry_results_become_safe_commands_or_are_marked_unsupported()
    {
        const string json = """
            { "servers": [
              { "server": { "name": "io.github.x/files", "title": "Files", "version": "1.2.0", "packages": [ { "registryType": "npm", "identifier": "@x/files-mcp", "version": "1.2.0", "transport": { "type": "stdio" },
                  "environmentVariables": [ { "name": "ROOT", "isRequired": true }, { "name": "TOKEN", "isSecret": true, "isRequired": true } ] } ] } },
              { "server": { "name": "io.github.y/py", "version": "0.3.0", "packages": [ { "registryType": "pypi", "identifier": "py-mcp", "version": "0.3.0", "transport": { "type": "stdio" } } ] } },
              { "server": { "name": "com.z/hosted", "version": "1", "remotes": [ { "type": "streamable-http", "url": "https://mcp.z.com/mcp", "headers": [ { "name": "Authorization", "isSecret": true, "isRequired": true } ] } ] } },
              { "server": { "name": "com.w/custom-header", "version": "1", "remotes": [ { "type": "streamable-http", "url": "https://w.com/mcp", "headers": [ { "name": "X-Api-Key", "isSecret": true } ] } ] } },
              { "server": { "name": "com.evil/inject", "version": "1", "packages": [ { "registryType": "npm", "identifier": "x; rm -rf /", "transport": { "type": "stdio" } } ] } },
              { "server": { "name": "com.docker/image", "version": "1", "packages": [ { "registryType": "oci", "identifier": "docker.io/x/y", "transport": { "type": "stdio" } } ] } }
            ] }
            """;
        var list = McpRegistry.Parse(json);
        Assert.Equal("npx", list[0].Command);
        Assert.Equal(["-y", "@x/files-mcp@1.2.0"], list[0].Args);
        Assert.Equal(2, list[0].Settings.Count);
        Assert.Equal(("uvx", "py-mcp==0.3.0"), (list[1].Command, list[1].Args[0]));
        Assert.Equal(("http", "https://mcp.z.com/mcp"), (list[2].Transport, list[2].Url));
        Assert.True(list[2].NeedsToken);
        Assert.NotEqual("", list[3].Unsupported);
        Assert.NotEqual("", list[4].Unsupported);
        Assert.NotEqual("", list[5].Unsupported);
    }

    // ---------------------------------------------------------- connections

    [Fact]
    public void Every_connection_has_a_next_action_or_is_connected()
    {
        using var sandbox = new Sandbox("connections");
        var core = sandbox.Core();
        var items = core.Connections.Build(new Dictionary<string, string> { ["vscode"] = "C:/x/Code.exe" });
        Assert.Contains(items, item => item.Id == "editor-vscode" && item.Actions.Any(action => action.Kind == ConnectionActionKind.UseAsEditor));
        foreach (var item in items)
            Assert.True(item.State is ConnectionState.Connected or ConnectionState.Unsupported || item.Actions.Count > 0, $"{item.Id} ({item.State}) has no next action");
        var notion = items.Single(item => item.Id == "notion");
        Assert.Equal(ConnectionState.AvailableToConnect, notion.State);
        Assert.Equal(ConnectionActionKind.Connect, notion.Actions[0].Kind);
        // Services without a reviewed server never claim a connection.
        var slack = items.Single(item => item.Id == "slack");
        Assert.Equal(ConnectionMethod.NotControllable, slack.Method);
        Assert.Contains(slack.Actions, action => action.Kind == ConnectionActionKind.SearchRegistry);
    }

    [Fact]
    public void Connection_states_follow_skill_state_sign_in_and_disconnect()
    {
        using var sandbox = new Sandbox("connection-states");
        var core = sandbox.Core();
        var catalog = core.Skills.Catalog().Skills;
        // An installed account skill without a key needs sign-in.
        var notion = catalog.Single(skill => skill.Id == "notion-mcp");
        Assert.Equal(ConnectionState.SignInRequired, StateAfterInstall(core, notion, "notion"));
        core.Skills.SetSecret("notion-mcp", notion.Auth!.Secret, "ntn_test");
        var items = core.Connections.Build(new Dictionary<string, string>());
        Assert.Equal(ConnectionState.Connected, items.Single(item => item.Id == "notion").State);
        Assert.Contains(items.Single(item => item.Id == "notion").Actions, action => action.Kind == ConnectionActionKind.Disconnect);
        // Switched off: installed but not connected, with "Turn on".
        core.Skills.SetEnabled("notion-mcp", false);
        var off = core.Connections.Build(new Dictionary<string, string>()).Single(item => item.Id == "notion");
        Assert.Equal(ConnectionState.InstalledNotConnected, off.State);
        Assert.Equal(ConnectionActionKind.Enable, off.Actions[0].Kind);
    }

    private static ConnectionState StateAfterInstall(Agex.Core.AgexCore core, SkillManifest manifest, string connectionId)
    {
        // Register the skill without downloading anything (MCP entries have no files).
        var method = typeof(SkillManager).GetMethod("Upsert", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        method.Invoke(core.Skills, [new InstalledSkill { Id = manifest.Id, Manifest = manifest, Enabled = true, PermissionChoices = manifest.Permissions.ToDictionary(permission => permission, _ => PermissionChoice.AlwaysAllow) }]);
        return core.Connections.Build(new Dictionary<string, string>()).Single(item => item.Id == connectionId).State;
    }

    [Fact]
    public void Free_options_come_before_ones_that_need_a_key()
    {
        using var sandbox = new Sandbox("free-first");
        var core = sandbox.Core();
        var brave = core.Skills.Catalog().Skills.Single(skill => skill.Id == "brave-search");
        StateAfterInstall(core, brave, "web-search");
        var search = core.Connections.Build(new Dictionary<string, string>()).Single(item => item.Id == "web-search");
        Assert.Equal(ConnectionState.AvailableToConnect, search.State);
        Assert.Equal("exa-search", search.Actions[0].Argument);
    }

    [Fact]
    public void Team_connections_are_listed_in_the_team_order()
    {
        using var sandbox = new Sandbox("team-connections");
        var core = sandbox.Core();
        var all = core.Connections.Build(new Dictionary<string, string>());
        var architecture = ConnectionService.ForTeam(all, "architecture-bim");
        Assert.Equal("revit", architecture[0].Id);
        Assert.Contains(architecture, item => item.Id == "pdf");
        Assert.Empty(ConnectionService.ForTeam(all, null));
        foreach (var (team, ids) in ConnectionCatalog.ForTeam)
        {
            Assert.NotNull(JobTeamCatalog.Get(team));
            Assert.All(ids, id => Assert.NotNull(ConnectionCatalog.Get(id)));
        }
    }

    [Fact]
    public void Autodesk_bridge_connects_with_its_own_id()
    {
        using var sandbox = new Sandbox("bridge-id");
        var core = sandbox.Core();
        var skill = core.Skills.AddMcpServer("Autodesk AI Bridge", "C:/bridge/host.exe", [], new Dictionary<string, string>(), AutodeskBridge.SkillId);
        Assert.Equal(AutodeskBridge.SkillId, skill.Id);
        var revit = core.Connections.Build(new Dictionary<string, string>()).Single(item => item.Id == "revit");
        if (revit.State != ConnectionState.NotInstalled && revit.State != ConnectionState.Unsupported) Assert.Equal(ConnectionState.Connected, revit.State);
    }

    // --------------------------------------------------------------- skills

    [Fact]
    public void Request_skill_selection_overrides_automatic_skills()
    {
        using var sandbox = new Sandbox("skill-select");
        var core = sandbox.Core();
        var catalog = core.Skills.Catalog().Skills;
        foreach (var id in new[] { "caveman", "systematic-debugging" })
        {
            var manifest = catalog.Single(skill => skill.Id == id);
            typeof(SkillManager).GetMethod("Upsert", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(core.Skills, [new InstalledSkill { Id = id, Manifest = manifest, Enabled = id == "caveman" }]);
        }
        Assert.Equal(["caveman"], core.Skills.ForRequest(null).Instructions.Select(skill => skill.Id));
        // "Use for this request only": a switched-off skill can be picked for one request.
        Assert.Equal(["systematic-debugging"], core.Skills.ForRequest(null, ["systematic-debugging"]).Instructions.Select(skill => skill.Id));
        Assert.Empty(core.Skills.ForRequest(null, []).Instructions);
        // Pinned to the team in use: added to the automatic set.
        Assert.Equal(2, core.Skills.ForRequest(null, null, ["systematic-debugging"]).Instructions.Count);
    }

    [Fact]
    public void Skills_assigned_to_agents_go_only_to_those_agents()
    {
        var map = SkillProfiles.AgentsBySkill(new Dictionary<string, List<string>> { ["codex"] = ["requesting-code-review"], ["antigravity"] = ["web-fetch", "requesting-code-review"] });
        Assert.Equal(2, map["requesting-code-review"].Count);
        Assert.Single(map["web-fetch"]);
        Assert.False(map.ContainsKey("caveman"));
    }

    [Fact]
    public async Task Per_agent_skills_reach_only_the_assigned_agent()
    {
        using var sandbox = new Sandbox("agent-skills");
        sandbox.LeaderPlans("""{"goal_status":"COMPLETE","reason":"Done.","verification":"Checked.","tasks":[]}""");
        var core = sandbox.Core();
        core.Settings.AgentSkills["antigravity"] = ["my-skill"];
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        var engine = core.CreateRequest(sandbox.Project, "Hello", new ScriptedHost(), core.BuildMembers(profile, agentIds: ["codex", "antigravity"]), profile,
            [new Agex.Core.Agents.SkillContext("my-skill", "Mine", "d", sandbox.Root, null), new Agex.Core.Agents.SkillContext("shared", "Shared", "d", sandbox.Root, null)], []);
        var codex = core.BuildMembers(profile, agentIds: ["codex"]).Single();
        var antigravity = core.BuildMembers(profile, agentIds: ["antigravity"]).Single();
        Assert.False(engine.SkillFor("my-skill", codex));
        Assert.True(engine.SkillFor("my-skill", antigravity));
        Assert.True(engine.SkillFor("shared", codex));
        await engine.RunAsync(CancellationToken.None);
    }

    [Fact]
    public void Built_in_skill_profiles_exist_and_user_profiles_replace_them_by_name()
    {
        Assert.Contains(SkillProfiles.BuiltIn, profile => profile.Name == "Architecture review");
        var all = SkillProfiles.All([new SkillProfile { Name = "Deep research", SkillIds = ["web-fetch"] }, new SkillProfile { Name = "Mine", SkillIds = ["caveman"] }]);
        Assert.Single(all, profile => profile.Name == "Deep research");
        Assert.Equal(["web-fetch"], all.Single(profile => profile.Name == "Deep research").SkillIds);
        Assert.Contains(all, profile => profile.Name == "Mine");
    }

    // ---------------------------------------------------------------- teams

    [Fact]
    public void Teams_document_inputs_sensitive_actions_and_put_them_in_the_brief()
    {
        foreach (var team in JobTeamCatalog.All)
        {
            Assert.NotEmpty(team.InputFiles);
            Assert.NotEmpty(team.SensitiveActions);
        }
        var bim = JobTeamCatalog.Get("architecture-bim")!;
        Assert.Contains("Changing a Revit or AutoCAD model", JobTeamCatalog.Brief(bim));
        Assert.Contains(bim.PremiumAlternatives, item => item.Contains("Navisworks"));
    }

    // ------------------------------------------------------ recommendations

    [Fact]
    public void Recommendations_follow_what_the_user_is_doing_and_respect_dismissal()
    {
        var pdf = Recommendations.For(new TipContext { Attachments = [AttachmentKind.Pdf], InstalledSkills = new HashSet<string> { "pdf-documents" } });
        Assert.Equal("skill:pdf", pdf.Single().Id);
        Assert.Equal("Add PDF skill", pdf.Single().ButtonLabel);

        var research = Recommendations.For(new TipContext { Request = "Research our top 5 competitors' pricing" });
        Assert.Contains(research, tip => tip.Id == "skill:research");

        var code = Recommendations.For(new TipContext { Request = "Fix the login bug and add a unit test" });
        Assert.Contains(code, tip => tip.Id == "skill:code" && tip.Argument.Contains("test-driven-development"));

        var large = Recommendations.For(new TipContext { ProjectFiles = 12000 });
        Assert.Equal(nameof(EfficiencyMode.SaveTokens), large.Single().Argument);

        var local = Recommendations.For(new TipContext { OllamaReady = true });
        Assert.Equal("efficiency:local", local.Single().Id);
        Assert.Empty(Recommendations.For(new TipContext { OllamaReady = true, Dismissed = ["efficiency:local"] }));
        Assert.Empty(Recommendations.For(new TipContext { Request = "Research markets", ActiveSkills = new HashSet<string> { "exa-search" } }));

        var many = Recommendations.For(new TipContext { Request = "research and fix the bug", Attachments = [AttachmentKind.Pdf], ProjectFiles = 9999, OllamaReady = true });
        Assert.Equal(2, many.Count);
    }
}
