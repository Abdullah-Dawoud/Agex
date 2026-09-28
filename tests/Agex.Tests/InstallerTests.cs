using System.IO.Compression;
using Agex.Core;
using Agex.Core.Connections;
using Agex.Core.Skills;
using Agex.Core.Updates;

namespace Agex.Tests;

/// <summary>AGEX 2.4: one-click installs (managed packages, runtimes, the Autodesk bridge) and the connection states around them.</summary>
public class InstallerTests
{
    private static readonly ArtifactDownloader LocalOnly = new(new HttpClient());

    // ------------------------------------------------------------ downloads

    [Fact]
    public async Task Checksum_mismatch_deletes_the_download()
    {
        using var sandbox = new Sandbox("checksum");
        var source = Path.Combine(sandbox.Root, "package.zip");
        await File.WriteAllTextAsync(source, "content");
        var target = Path.Combine(sandbox.Root, "out", "package.zip");
        await Assert.ThrowsAsync<ChecksumMismatchException>(() => LocalOnly.DownloadAsync(new Uri(source), target, new string('0', 64), CancellationToken.None));
        Assert.False(File.Exists(target));
        var sha = await UpdateService.HashFileAsync(source, CancellationToken.None);
        Assert.Equal(target, await LocalOnly.DownloadAsync(new Uri(source), target, sha, CancellationToken.None));
    }

    [Fact]
    public async Task Plain_http_downloads_are_refused()
    {
        using var sandbox = new Sandbox("http");
        await Assert.ThrowsAsync<InstallFailedException>(() => LocalOnly.DownloadAsync(new Uri("http://example.com/a.zip"), Path.Combine(sandbox.Root, "a.zip"), null, CancellationToken.None));
    }

    [Fact]
    public void Archives_cannot_write_outside_their_folder()
    {
        using var sandbox = new Sandbox("zip-slip");
        var archive = Path.Combine(sandbox.Root, "evil.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntry("../evil.txt");
        Assert.Throws<InstallFailedException>(() => ArtifactDownloader.Extract(archive, Path.Combine(sandbox.Root, "x")));
        Assert.False(File.Exists(Path.Combine(sandbox.Root, "evil.txt")));
    }

    [Fact]
    public void Install_journal_restores_replaced_files_and_removes_new_ones()
    {
        using var sandbox = new Sandbox("journal");
        var existing = Path.Combine(sandbox.Root, "target", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "old");
        var added = Path.Combine(sandbox.Root, "new", "file.txt");
        using (var journal = new InstallJournal(Path.Combine(sandbox.Root, "backup")))
        {
            journal.WriteFile(existing, "new");
            journal.WriteFile(added, "x");
            Assert.Equal("new", File.ReadAllText(existing));
        } // not committed: rolled back
        Assert.Equal("old", File.ReadAllText(existing));
        Assert.False(File.Exists(added));
    }

    // ------------------------------------------------------ managed packages

    [Fact]
    public void Pinned_npm_and_pypi_commands_are_recognised_and_unpinned_ones_are_not()
    {
        var npm = PackageRef.Parse("npx", ["-y", "@supabase/mcp-server-supabase@0.13.0", "--read-only"])!;
        Assert.Equal(("npm", "@supabase/mcp-server-supabase", "0.13.0"), (npm.Kind, npm.Name, npm.Version));
        Assert.Equal(["--read-only"], npm.RestArguments);
        var uv = PackageRef.Parse("uvx", ["--from", "serena-agent==1.7.0", "serena", "start-mcp-server"])!;
        Assert.Equal(("pypi", "serena-agent", "1.7.0", "serena"), (uv.Kind, uv.Name, uv.Version, uv.Executable));
        Assert.Equal("mcp-server-fetch==2026.8.18", PackageRef.Parse("uvx", ["mcp-server-fetch==2026.8.18"])!.Spec);
        Assert.Null(PackageRef.Parse("npx", ["-y", "@playwright/mcp"]));
        Assert.Null(PackageRef.Parse("npx", ["-y", "evil;rm -rf@1.0.0"]));
        Assert.Null(PackageRef.Parse("uvx", ["tool"]));
        Assert.Null(PackageRef.Parse("docker", ["run", "x"]));
        // Every local reviewed catalog server is installable by AGEX.
        using var sandbox = new Sandbox("catalog-pins");
        foreach (var skill in sandbox.Core().Skills.Catalog().Skills.Where(skill => skill.Mcp is { Transport: not "http" }))
            Assert.True(PackageRef.Parse(skill.Mcp!.Command, skill.Mcp.Args) is { } pinned && pinned.Version == skill.Version, skill.Id + " is not a pinned package at its catalog version");
    }

    [Fact]
    public async Task Missing_runtime_fails_the_install_and_keeps_the_previous_copy()
    {
        using var sandbox = new Sandbox("managed-missing");
        Environment.SetEnvironmentVariable("AGEX_HIDE_TOOLS", "node,uv");
        var core = sandbox.Core();
        var package = PackageRef.Parse("npx", ["-y", "@upstash/context7-mcp@4.1.1"])!;
        // Nothing installed before: nothing left behind.
        var error = await Assert.ThrowsAsync<InstallFailedException>(() => core.Packages.InstallAsync("context7", package, null, CancellationToken.None));
        Assert.Contains("Node.js", error.Message);
        Assert.False(Directory.Exists(Path.Combine(core.Packages.Location("context7"), "4.1.1")));
        // The same version already there: rolled back to it.
        var previous = Path.Combine(core.Packages.Location("context7"), "4.1.1", "marker.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
        File.WriteAllText(previous, "previous");
        await Assert.ThrowsAsync<InstallFailedException>(() => core.Packages.InstallAsync("context7", package, null, CancellationToken.None));
        Assert.Equal("previous", File.ReadAllText(previous));
        await Assert.ThrowsAsync<InstallFailedException>(() => core.Packages.InstallAsync("web-fetch", PackageRef.Parse("uvx", ["mcp-server-fetch==2026.8.18"])!, null, CancellationToken.None));
        Assert.Single(Directory.GetDirectories(core.Packages.Location("context7")));
    }

    [Fact]
    public void Missing_dependency_offers_install_dependency_and_names_the_skill()
    {
        using var sandbox = new Sandbox("dependency-state");
        Environment.SetEnvironmentVariable("AGEX_HIDE_TOOLS", "node,uv");
        var core = sandbox.Core();
        var context7 = core.Skills.Catalog().Skills.Single(skill => skill.Id == "context7");
        Register(core, context7);
        var item = core.Connections.Build(new Dictionary<string, string>()).First(entry => entry.SkillId == "context7");
        Assert.Equal(ConnectionState.DependencyMissing, item.State);
        var action = Assert.Single(item.Actions);
        Assert.Equal((ConnectionActionKind.InstallDependency, "node"), (action.Kind, action.Argument));
        Assert.StartsWith("Install Node.js", action.Label);
        Assert.True(DependencyInstaller.CanInstall("node") && DependencyInstaller.CanInstall("uv") && !DependencyInstaller.CanInstall("docker"));
        Assert.Contains("SHA256", core.Dependencies.Describe("node"));
    }

    [Fact]
    public void Runtimes_agex_installed_are_found_even_when_system_tools_are_hidden()
    {
        using var sandbox = new Sandbox("runtime-path");
        Environment.SetEnvironmentVariable("AGEX_HIDE_TOOLS", "node");
        var core = sandbox.Core();
        var context7 = core.Skills.Catalog().Skills.Single(skill => skill.Id == "context7");
        Assert.NotEmpty(core.Skills.MissingTools(context7));
        var folder = Path.Combine(sandbox.Platform.Paths.DataRoot, "runtimes", "node");
        Directory.CreateDirectory(folder);
        var npx = Path.Combine(folder, OperatingSystem.IsWindows() ? "npx.cmd" : "npx");
        File.WriteAllText(npx, "");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(npx, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Assert.Equal(npx, sandbox.Platform.FindExecutable("npx"));
        Assert.Empty(core.Skills.MissingTools(context7));
    }

    [Fact]
    public void Already_installed_package_starts_directly_and_an_older_one_offers_update()
    {
        using var sandbox = new Sandbox("managed-state");
        var core = sandbox.Core();
        var context7 = core.Skills.Catalog().Skills.Single(skill => skill.Id == "context7");
        Register(core, context7);
        var script = Path.Combine(core.Packages.Location("context7"), "4.1.1", "dist", "index.js");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, "");
        var node = Path.Combine(sandbox.Root, OperatingSystem.IsWindows() ? "node.exe" : "node");
        File.WriteAllText(node, "");
        void Record(string version) => Json.WriteFile(Path.Combine(core.Packages.Location("context7"), "installed.json"),
            new ManagedInstall("context7", "npm", "@upstash/context7-mcp", version, node, [script], DateTimeOffset.UtcNow));

        Record("4.1.1");
        var installed = core.Skills.Installed().Single(skill => skill.Id == "context7");
        var spec = core.Skills.SpecFor(installed)!;
        Assert.Equal(node, spec.Command);
        Assert.Equal([script], spec.Arguments);
        var item = Item(core, "context7");
        Assert.DoesNotContain(item.Actions, action => action.Kind == ConnectionActionKind.Update);

        Record("4.0.0");
        Assert.NotEqual(node, core.Skills.SpecFor(installed)!.Command); // falls back to the pinned npx command
        item = Item(core, "context7");
        if (item.State == ConnectionState.Configured)
            Assert.Equal(["Test with agent", "Update", "Settings", "Disconnect"], item.Actions.Select(action => action.Label));
    }

    [Fact]
    public void Disconnect_removes_the_package_and_reconnect_turns_it_back_on()
    {
        using var sandbox = new Sandbox("managed-remove");
        var core = sandbox.Core();
        var notion = core.Skills.Catalog().Skills.Single(skill => skill.Id == "notion-mcp");
        // The key store can be the system's own (macOS keychain): start without a key left by another test.
        core.Skills.Disconnect("notion-mcp", notion);
        Register(core, notion);
        // Sign-in required: an API key skill without its key asks for it.
        var item = Item(core, "notion-mcp");
        if (item.State != ConnectionState.DependencyMissing)
        {
            Assert.Equal(ConnectionState.SignInRequired, item.State);
            Assert.Equal(ConnectionActionKind.AddKey, item.Actions[0].Kind);
            core.Skills.SetSecret("notion-mcp", notion.Auth!.Secret, "ntn_test");
            // Switched off, then connected again.
            core.Skills.SetEnabled("notion-mcp", false);
            item = Item(core, "notion-mcp");
            Assert.Equal((ConnectionState.InstalledNotConnected, ConnectionActionKind.Enable, "Connect"), (item.State, item.Actions[0].Kind, item.Actions[0].Label));
            core.Skills.SetEnabled("notion-mcp", true);
            Assert.Equal(ConnectionState.Configured, Item(core, "notion-mcp").State);
        }
        var folder = Path.Combine(core.Packages.Location("notion-mcp"), "2.5.2");
        Directory.CreateDirectory(folder);
        core.Skills.Remove("notion-mcp");
        Assert.False(Directory.Exists(core.Packages.Location("notion-mcp")));
        item = Item(core, "notion-mcp");
        Assert.Contains(item.State, new[] { ConnectionState.NotInstalled, ConnectionState.DependencyMissing });
        if (item.State == ConnectionState.NotInstalled)
        {
            Assert.Equal(ConnectionActionKind.Connect, item.Actions[0].Kind);
            Assert.Contains(item.Actions, action => action.Label == "Install & Connect");
        }
    }

    // ------------------------------------------------------- Autodesk bridge

    private sealed class BridgeFixture : IDisposable
    {
        private readonly Sandbox _sandbox = new("bridge");
        public string Programs => Path.Combine(_sandbox.Root, "Program Files");
        public string LocalAppData => Path.Combine(_sandbox.Root, "Local");
        public string AppData => Path.Combine(_sandbox.Root, "Roaming");
        public string Package { get; }
        public string Sha { get; }
        public AutodeskBridgeInstaller Installer { get; }

        public BridgeFixture(bool withHost = true, Func<AutodeskProduct, bool>? isRunning = null)
        {
            foreach (var (folder, exe) in new[] { ("Revit 2026", "Revit.exe"), ("Revit 2024", "Revit.exe"), ("AutoCAD 2027", "acad.exe") })
            {
                Directory.CreateDirectory(Path.Combine(Programs, "Autodesk", folder));
                File.WriteAllText(Path.Combine(Programs, "Autodesk", folder, exe), "");
            }
            Directory.CreateDirectory(Path.Combine(Programs, "Autodesk", "Revit 2025")); // no Revit.exe: not a real install
            var content = Path.Combine(_sandbox.Root, "package");
            void Put(string relative, string text)
            {
                var path = Path.Combine(content, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            if (withHost) Put(Path.Combine("host", "AutodeskAIBridge.Host.exe"), "host");
            foreach (var year in new[] { "2025", "2026", "2027" }) Put(Path.Combine("revit", year, "AutodeskAIBridge.Revit.dll"), "revit " + year);
            Put(Path.Combine("autocad", "AutodeskAIBridge.bundle", "PackageContents.xml"), "<ApplicationPackage/>");
            Put("bridge.json", """{"version":"1.2.3"}""");
            Package = Path.Combine(_sandbox.Root, AutodeskBridgeInstaller.PackageName);
            ZipFile.CreateFromDirectory(content, Package);
            Sha = UpdateService.HashFileAsync(Package, CancellationToken.None).GetAwaiter().GetResult();
            Installer = new AutodeskBridgeInstaller(_sandbox.Platform, LocalOnly, null, [Programs], LocalAppData, AppData, isRunning ?? (_ => false));
        }

        public BridgePackageSource Source(string? sha = null) => new(new Uri(Package), sha ?? Sha, "test package");

        public void Dispose() => _sandbox.Dispose();
    }

    [Fact]
    public void Bridge_detects_supported_programs_per_year()
    {
        using var fixture = new BridgeFixture();
        var found = fixture.Installer.Detect();
        Assert.Equal(["AutoCAD 2027", "Revit 2024", "Revit 2026"], found.Select(product => product.Name));
        Assert.Equal([true, false, true], found.Select(product => product.Supported));
        var status = fixture.Installer.Status();
        Assert.False(status.HostInstalled);
        Assert.All(status.Products, product => Assert.False(product.Installed));
    }

    [Fact]
    public async Task Bridge_installs_host_and_plug_ins_for_the_user_and_uninstalls_cleanly()
    {
        using var fixture = new BridgeFixture();
        var installer = fixture.Installer;
        if (!OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<InstallFailedException>(() => installer.InstallAsync(fixture.Source(), null, CancellationToken.None));
            return;
        }
        var steps = new List<string>();
        Assert.Equal("1.2.3", await installer.InstallAsync(fixture.Source(), new SyncProgress(steps.Add), CancellationToken.None));
        Assert.Contains("Checksum verified", steps);
        Assert.True(File.Exists(installer.HostPath));
        var manifest = File.ReadAllText(installer.RevitManifest("2026"));
        Assert.Contains(AutodeskBridgeInstaller.RevitAddInId, manifest);
        Assert.Contains(Path.Combine(installer.RevitFolder("2026"), "AutodeskAIBridge.Revit.dll"), manifest);
        Assert.True(File.Exists(Path.Combine(installer.RevitFolder("2026"), "AutodeskAIBridge.Revit.dll")));
        Assert.False(File.Exists(installer.RevitManifest("2025"))); // Revit 2025 is not installed
        Assert.False(File.Exists(installer.RevitManifest("2024"))); // not supported
        Assert.True(File.Exists(Path.Combine(installer.AutoCadBundle, "PackageContents.xml")));
        var secret = File.ReadAllText(Path.Combine(installer.Root, "config", "ipc-secret")).Trim();
        Assert.Equal(64, secret.Length);
        Assert.StartsWith("AutodeskAIBridge-", File.ReadAllText(Path.Combine(installer.Root, "config", "ipc-pipe")));
        var status = installer.Status(AutodeskBridgeInstaller.ConnectedProducts("""{"instances":[{"product":"autocad","version":"2027"}]}"""));
        Assert.Equal(("1.2.3", true), (status.Version, status.HostInstalled));
        Assert.Equal([false, false, false], status.Products.Select(product => product.Connected == true));
        Assert.Equal([true, false, true], status.Products.Select(product => product.Installed));

        // Already installed: installing again keeps the pairing secret the plug-ins use.
        await installer.InstallAsync(fixture.Source(), null, CancellationToken.None);
        Assert.Equal(secret, File.ReadAllText(Path.Combine(installer.Root, "config", "ipc-secret")).Trim());

        installer.Uninstall();
        Assert.False(Directory.Exists(installer.Root));
        Assert.False(File.Exists(installer.RevitManifest("2026")));
        Assert.False(Directory.Exists(installer.AutoCadBundle));
        Assert.Null(installer.InstalledVersion());
    }

    [Fact]
    public async Task Bridge_with_a_wrong_checksum_installs_nothing()
    {
        using var fixture = new BridgeFixture();
        if (!OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<ChecksumMismatchException>(() => fixture.Installer.InstallAsync(fixture.Source(new string('a', 64)), null, CancellationToken.None));
        Assert.False(Directory.Exists(fixture.Installer.Root));
        Assert.False(File.Exists(fixture.Installer.RevitManifest("2026")));
    }

    [Fact]
    public async Task Bridge_package_without_a_host_is_refused()
    {
        using var fixture = new BridgeFixture(withHost: false);
        if (!OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<InstallFailedException>(() => fixture.Installer.InstallAsync(fixture.Source(), null, CancellationToken.None));
        Assert.False(Directory.Exists(fixture.Installer.Root));
    }

    [Fact]
    public async Task Failed_bridge_install_rolls_every_change_back()
    {
        using var fixture = new BridgeFixture();
        if (!OperatingSystem.IsWindows()) return;
        var installer = fixture.Installer;
        // An older host is there; the AutoCAD plug-in folder cannot be created (a file is in the way).
        var old = Path.Combine(Path.GetDirectoryName(installer.HostPath)!, "old.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, "old host");
        var plugins = Path.GetDirectoryName(installer.AutoCadBundle)!;
        Directory.CreateDirectory(Path.GetDirectoryName(plugins)!);
        File.WriteAllText(plugins, "not a folder");
        var error = await Assert.ThrowsAsync<InstallFailedException>(() => installer.InstallAsync(fixture.Source(), null, CancellationToken.None));
        Assert.Contains("undone", error.Message);
        Assert.Equal("old host", File.ReadAllText(old));
        Assert.False(File.Exists(installer.HostPath));
        Assert.False(File.Exists(installer.RevitManifest("2026")));
        Assert.False(Directory.Exists(installer.RevitFolder("2026")));
        Assert.False(Directory.Exists(Path.Combine(installer.Root, "config")));
    }

    [Fact]
    public void Bridge_reports_connected_programs_from_its_tool_output()
    {
        Assert.Empty(AutodeskBridgeInstaller.ConnectedProducts(""));
        Assert.Equal(["revit"], AutodeskBridgeInstaller.ConnectedProducts("""{"content":[{"type":"text","text":"[{\"product\":\"revit\",\"version\":\"2026\"}]"}]}"""));
        Assert.Equal(2, AutodeskBridgeInstaller.ConnectedProducts("""[{"product":"Revit"},{"product":"autocad"}]""").Count);
        var response = """{"result":{"structuredContent":{"success":true,"data":{"instances":[{"product":"autocad","activeDocumentId":"drawing-1","documents":[{"name":"Drawing.dwg"}]},{"product":"revit","activeDocumentId":null,"documents":[]}]}}}}""";
        Assert.Equal(["autocad"], AutodeskBridgeInstaller.DocumentProducts(response));
    }

    [Fact]
    public void Bridge_status_distinguishes_install_running_plugin_and_document()
    {
        using var fixture = new BridgeFixture(isRunning: product => product.Product == "autocad");
        var installer = fixture.Installer;
        var missing = installer.Status();
        Assert.Contains("integration not installed", missing.Products.Single(product => product.Product.Product == "autocad").Detail);
        // Status detection is platform independent; package installation is Windows-only.
        var bundleManifest = Path.Combine(installer.AutoCadBundle, "PackageContents.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(bundleManifest)!);
        File.WriteAllText(bundleManifest, "<ApplicationPackage/>");
        var revitManifest = installer.RevitManifest("2026");
        Directory.CreateDirectory(Path.GetDirectoryName(revitManifest)!);
        File.WriteAllText(revitManifest, "<RevitAddIns/>");
        var running = installer.Status(new HashSet<string>());
        Assert.True(running.Products.Single(product => product.Product.Product == "autocad").Running);
        Assert.Contains("restart it to load the AGEX integration", running.Products.Single(product => product.Product.Product == "autocad").Detail);
        Assert.Contains("open Revit", running.Products.Single(product => product.Product.Product == "revit" && product.Product.Year == "2026").Detail);
        var stopped = installer.Status(new HashSet<string> { "revit" }, new HashSet<string> { "revit" });
        Assert.False(stopped.Products.Single(product => product.Product.Product == "revit" && product.Product.Year == "2026").Connected);
        Assert.False(stopped.Products.Single(product => product.Product.Product == "revit" && product.Product.Year == "2026").DocumentAvailable);
        var connected = installer.Status(new HashSet<string> { "autocad" }, new HashSet<string>());
        Assert.Contains("open a document", connected.Products.Single(product => product.Product.Product == "autocad").Detail);
        var ready = installer.Status(new HashSet<string> { "autocad" }, new HashSet<string> { "autocad" });
        Assert.Equal("Connected", ready.Products.Single(product => product.Product.Product == "autocad").Detail);
    }

    // --------------------------------------------------------------- helpers

    private static void Register(AgexCore core, SkillManifest manifest) =>
        typeof(SkillManager).GetMethod("Upsert", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(core.Skills, [new InstalledSkill { Id = manifest.Id, Manifest = manifest, Enabled = true, PermissionChoices = manifest.Permissions.ToDictionary(permission => permission, _ => PermissionChoice.AlwaysAllow) }]);

    private static ConnectionItem Item(AgexCore core, string skillId) =>
        core.Connections.Build(new Dictionary<string, string>()).First(entry => entry.SkillId == skillId);

    /// <summary>Reports progress synchronously (Progress&lt;T&gt; posts to the thread pool).</summary>
    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
