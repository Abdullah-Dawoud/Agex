using Agex.Core;
using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Core.Projects;
using Agex.Core.Runtime;
using Agex.Core.Sessions;
using Agex.Core.Settings;

namespace Agex.Tests;

public class StorageTests
{
    [Fact]
    public void Powershell_edition_settings_are_migrated_with_a_backup()
    {
        using var sandbox = new Sandbox("migrate");
        File.WriteAllText(sandbox.Platform.Paths.Settings, """
            {"version":2,"last_project":"C:\\work\\app","last_modes":["Orchestrator"],"leader":"Codex","codex_share":10,"antigravity_share":90,
             "codex_model":"gpt-x","codex_effort":"high","antigravity_model":"gemini-y","antigravity_effort":"high"}
            """);
        File.WriteAllText(Path.Combine(sandbox.Home, "projects.json"), """{"version":1,"projects":[{"name":"app","path":"C:\\work\\app"}]}""");
        var store = new SettingsStore(sandbox.Platform.Paths);
        var settings = store.Load();
        Assert.Equal(AgexSettings.CurrentSchema, settings.SchemaVersion);
        Assert.Equal(RoutingPreset.Custom, settings.Routing);
        Assert.Equal(10, settings.CustomShares["codex"]);
        Assert.Equal("gpt-x", settings.AgentOptions["codex"].Model);
        Assert.False(settings.AgentOptions["codex"].AllowWrites);
        Assert.Equal("codex", settings.Leader);
        Assert.True(settings.FirstRunComplete);
        Assert.Contains(@"C:\work\app", settings.RecentProjects);
        Assert.NotEmpty(Directory.GetDirectories(sandbox.Platform.Paths.Backups));
        Assert.Single(store.AllProjects());
        // Second load: already current, nothing migrates again.
        Assert.Equal(settings.Routing, new SettingsStore(sandbox.Platform.Paths).Load().Routing);
    }

    [Fact]
    public void Damaged_settings_are_kept_aside_and_defaults_used()
    {
        using var sandbox = new Sandbox("damaged");
        File.WriteAllText(sandbox.Platform.Paths.Settings, "{ this is not json");
        var store = new SettingsStore(sandbox.Platform.Paths);
        var settings = store.Load();
        Assert.NotNull(settings);
        Assert.Single(store.LoadIssues);
        Assert.Single(Directory.GetFiles(sandbox.Home, "settings.json.damaged-*"));
    }

    [Fact]
    public void Settings_export_excludes_secrets_and_round_trips()
    {
        using var sandbox = new Sandbox("export");
        var core = sandbox.Core();
        core.Platform.SecureStore.Set("skill:x:TOKEN", "super-secret-value");
        core.Settings.Theme = ThemeChoice.Dark;
        core.SaveSettings(core.Settings);
        var file = Path.Combine(sandbox.Root, "export.json");
        core.SettingsStore.ExportTo(file, includeProjects: true, ["context7"]);
        Assert.DoesNotContain("super-secret-value", File.ReadAllText(file));
        core.Settings.Theme = ThemeChoice.Light;
        core.SaveSettings(core.Settings);
        var skills = core.SettingsStore.Import(file, includeProjects: true);
        Assert.Equal(["context7"], skills);
        Assert.Equal(ThemeChoice.Dark, core.SettingsStore.Load().Theme);
        Assert.Equal("super-secret-value", core.Platform.SecureStore.Get("skill:x:TOKEN"));
    }

    [Fact]
    public void Sessions_are_listed_searched_retained_and_recovered()
    {
        using var sandbox = new Sandbox("sessions");
        var store = new SessionStore(sandbox.Platform.Paths.Sessions);
        var old = new Session { Request = "old request", Status = SessionStatus.Complete, CreatedAt = DateTimeOffset.UtcNow.AddDays(-100) };
        var running = new Session { Request = "running request", Status = SessionStatus.Running };
        running.Messages.Add(new AgentMessage { From = "Codex", To = "User", Text = "The needle is here" });
        store.Save(old);
        store.Save(running);
        Assert.Equal(2, store.List().Count);
        Assert.Contains(store.Search("NEEDLE"), hit => hit.SessionId == running.Id);
        Assert.Equal(1, store.ApplyRetention(90, 500));
        Assert.Equal([running.Id], store.MarkInterrupted());
        Assert.Equal(SessionStatus.Interrupted, store.Load(running.Id)!.Status);
        Assert.Contains("# running request", SessionStore.ExportMarkdown(store.Load(running.Id)!));
        Assert.Throws<ArgumentException>(() => store.Load("../../etc/passwd") ?? throw new ArgumentException());
    }

    [Fact]
    public void Scanner_skips_dependency_and_build_folders()
    {
        using var sandbox = new Sandbox("scan");
        foreach (var folder in new[] { "node_modules/pkg", ".git/objects", "bin/Debug", "src" }) Directory.CreateDirectory(Path.Combine(sandbox.Project, folder));
        File.WriteAllText(Path.Combine(sandbox.Project, "node_modules/pkg/index.js"), "x");
        File.WriteAllText(Path.Combine(sandbox.Project, "src/app.cs"), "x");
        var files = ProjectScanner.List(sandbox.Project);
        Assert.Equal(["src/app.cs"], files.Select(file => file.RelativePath));
        Assert.False(ProjectScanner.IsInside(sandbox.Project, "../outside.txt"));
        Assert.True(ProjectScanner.IsInside(sandbox.Project, "src/../src/app.cs"));
    }

    [Fact]
    public async Task Git_snapshot_restores_changes_without_touching_the_index()
    {
        using var sandbox = new Sandbox("git");
        var platform = sandbox.Platform;
        if (platform.FindExecutable("git") is null) return;
        var runner = new ProcessRunner(platform);
        async Task Git(params string[] args) => Assert.True((await runner.RunAsync(new ProcessRequest { FileName = platform.FindExecutable("git")!, Arguments = ["-C", sandbox.Project, "-c", "user.name=t", "-c", "user.email=t@t", .. args], WorkingDirectory = sandbox.Project })).Succeeded, string.Join(' ', args));
        await Git("init", "-q");
        File.WriteAllText(Path.Combine(sandbox.Project, "keep.txt"), "original");
        await Git("add", "keep.txt");
        await Git("commit", "-q", "-m", "init");
        File.WriteAllText(Path.Combine(sandbox.Project, "untracked.txt"), "mine");
        var git = new GitService(platform, runner);
        var statusBefore = await git.StatusAsync(sandbox.Project);
        var snapshot = await git.SnapshotAsync(sandbox.Project, "test-1");
        Assert.Equal("refs/agex/snapshots/test-1", snapshot);
        Assert.Equal(statusBefore, await git.StatusAsync(sandbox.Project));
        File.WriteAllText(Path.Combine(sandbox.Project, "keep.txt"), "changed by agent");
        File.WriteAllText(Path.Combine(sandbox.Project, "new.txt"), "added by agent");
        File.Delete(Path.Combine(sandbox.Project, "untracked.txt"));
        Assert.Equal(3, (await git.ChangedSinceAsync(sandbox.Project, snapshot!)).Count);
        Assert.True(await git.RestoreAsync(sandbox.Project, snapshot!));
        Assert.Equal("original", File.ReadAllText(Path.Combine(sandbox.Project, "keep.txt")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(sandbox.Project, "untracked.txt")));
        Assert.False(File.Exists(Path.Combine(sandbox.Project, "new.txt")));
    }

    [Fact]
    public void Local_only_routing_removes_cloud_agents()
    {
        using var sandbox = new Sandbox("router");
        var core = sandbox.Core();
        var members = core.BuildMembers(agentIds: ["codex", "antigravity"]);
        var router = new Router(core.Settings);
        Assert.Empty(router.Filter(members, RoutingPreset.LocalOnly));
        Assert.Equal(2, router.Filter(members, RoutingPreset.Automatic).Count);
        Assert.Equal("antigravity", router.ChooseLeader(members, RoutingPreset.Automatic, "antigravity")!.Id);
        Assert.Throws<InvalidOperationException>(() => core.CreateRequest(sandbox.Project, "x", new ScriptedHost(), members, new ProjectProfile { Path = sandbox.Project, Routing = RoutingPreset.LocalOnly }, [], []));
    }

    [Fact]
    public void Ollama_cloud_models_are_not_local()
    {
        Assert.True(OllamaAdapter.IsCloudModel("gpt-oss:120b-cloud"));
        Assert.True(OllamaAdapter.IsCloudModel("glm-5.3:cloud"));
        Assert.False(OllamaAdapter.IsCloudModel("qwen2.5-coder:7b"));
        Assert.Equal("answer", OllamaAdapter.StripThinking("<think>internal</think>answer"));
    }

    [Fact]
    public void Discovery_reports_every_status_honestly()
    {
        using var sandbox = new Sandbox("discovery");
        Environment.SetEnvironmentVariable("AGEX_GEMINI_CLI_PATH", Path.Combine(sandbox.Root, "nope.exe"));
        var core = sandbox.Core();
        var scan = core.Discovery.QuickScan();
        Assert.Equal(AgentStatus.Available, scan.Items.Single(item => item.Id == "codex").Status);
        var ollama = scan.Items.Single(item => item.Id == "ollama");
        Assert.True(ollama.HasAdapter);
        if (!OperatingSystem.IsWindows()) Assert.Equal(AgentStatus.PlatformUnsupported, scan.Items.Single(item => item.Id == "visual-studio").Status);
        Assert.Contains(scan.Items, item => item.Id == "git");
    }

    [Fact]
    public async Task Health_check_marks_supported_and_failures_cool_down()
    {
        using var sandbox = new Sandbox("health");
        var core = sandbox.Core();
        var detection = await core.Registry.CheckHealthAsync("codex", CancellationToken.None);
        Assert.Equal(AgentStatus.Supported, detection.Status);
        Assert.Equal("9.9.9", detection.Version);
        core.Registry.RegisterResult("codex", false, "boom");
        Assert.True(core.Registry.Health("codex").Healthy);
        core.Registry.RegisterResult("codex", false, "boom");
        Assert.False(core.Registry.Health("codex").Healthy);
        core.Registry.ResetHealth("codex");
        Assert.True(core.Registry.Health("codex").Healthy);
    }

    [Fact]
    public void Diagnostics_contain_no_home_folder_or_secrets()
    {
        using var sandbox = new Sandbox("diag");
        var core = sandbox.Core();
        var report = core.DiagnosticsReport(core.Discovery.QuickScan());
        Assert.Contains("AGEX", report);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), report, StringComparison.OrdinalIgnoreCase);
        var actions = core.Repair();
        Assert.DoesNotContain(actions, action => action.Status == "FAILED");
    }
}
