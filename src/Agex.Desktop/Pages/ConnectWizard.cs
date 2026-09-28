using Agex.Core.Agents;
using Agex.Core.Connections;
using Agex.Core.Runtime;
using Agex.Core.Skills;
using Agex.Core.Teams;
using Agex.Desktop.Ui;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>
/// One-click connection for reviewed MCP tools and the Autodesk bridge. AGEX
/// shows what will run and what it may do, then does every step it safely can:
/// check prerequisites, install and configure, store the key, hand the tool to
/// the agents that support it, and test it with the MCP handshake. Node.js and
/// uv are installed from their official downloads when missing, reviewed npm and
/// PyPI packages are installed at their pinned version, and the Autodesk AI
/// Bridge is installed from the prebuilt package of this AGEX release. Every
/// system change is shown first and needs a click. No JSON editing, no commands
/// to copy.
/// </summary>
public sealed class ConnectWizard(MainWindow window)
{
    private Workspace Workspace => window.Workspace;

    private sealed class Step
    {
        public required string Title { get; init; }
        public TextBlock Status { get; } = Kit.Text("", "caption");
        public ContentControl Glyph { get; } = new();
        public StackPanel Actions { get; } = new() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };

        public void Set(bool? ok, string text, params Control[] actions)
        {
            Glyph.Content = ok switch { true => Kit.Icon(Icons.Check, 16, "SuccessBrush"), false => Kit.Icon(Icons.Close, 16, "DangerBrush"), _ => Kit.Icon(Icons.Dot, 16, "Text3Brush") };
            Status.Text = text;
            Actions.Children.Clear();
            foreach (var action in actions) Actions.Children.Add(action);
        }

        public Control View()
        {
            Status.TextWrapping = TextWrapping.Wrap;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            grid.Children.Add(Glyph);
            var right = Kit.Column(2, Kit.Text(Title, "body"), Status, Actions);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            Set(null, "Waiting");
            AutomationProperties.SetName(grid, Title);
            return grid;
        }
    }

    /// <summary>Connects a catalog MCP tool. Returns true when it ends installed and the test passed.</summary>
    public async Task<bool> RunAsync(string skillId)
    {
        if (skillId == AutodeskBridge.SkillId) return await BridgeAsync();
        var manifest = Workspace.Core.Skills.Catalog().Skills.FirstOrDefault(skill => skill.Id == skillId);
        if (manifest is null) { await window.Dialogs.MessageAsync("Not found", "AGEX has no reviewed connection with this name."); return false; }
        if (manifest.Kind != SkillKind.Mcp) { await window.Page<SkillsPage>("skills").InstallByIdAsync(skillId); return Installed(skillId) is not null; }

        // 1. Review: source, package, what gets installed where, permissions, network, account, affected agents.
        var skills = Workspace.Core.Skills;
        var enabled = Workspace.Settings.EnabledAgents.Select(Workspace.Core.Registry.Get).OfType<IAgentAdapter>().Select(adapter => adapter.Name).ToList();
        var package = Package(manifest);
        var runs = manifest.Mcp?.Transport == "http" ? "Hosted service at " + manifest.Mcp.Url + ". Nothing is installed on this computer."
            : package is not null ? $"{package.Spec} from {(package.Kind == "npm" ? "the npm registry" : "PyPI")}, installed at this pinned version into {Redactor.RedactPaths(Workspace.Core.Packages.Location(manifest.Id))}. Started only while an agent uses it; removed when you disconnect."
            : $"{manifest.Mcp?.Command} {string.Join(' ', manifest.Mcp?.Args ?? [])} (on this computer, started only while an agent uses it)";
        var missing = skills.MissingTools(manifest);
        TextBox? keyBox = null;
        var review = Kit.Column(8,
            Wrapped(manifest.Description, "body"),
            Fact("Source", $"{manifest.Author}, {SkillText.Trust(manifest.Trust)}, licence {manifest.License}. {manifest.Homepage}"),
            Fact("Version", manifest.Version),
            Fact("Installs", runs),
            missing.Count > 0 ? Fact("Also installs", string.Join(" ", missing.Select(tool => DependencyInstaller.CanInstall(tool.Id)
                ? Workspace.Core.Dependencies.Describe(tool.Id)
                : tool.Label + " is needed; AGEX opens its official download page."))) : null,
            Fact("It may", manifest.Permissions.Count == 0 ? "Nothing beyond reading what agents pass to it." : string.Join(", ", manifest.Permissions.Select(SkillText.Permission))),
            Fact("Network", manifest.Permissions.Contains(SkillPermission.Network) ? "Uses the internet or pages you open." : "Works on this computer only."),
            Fact("Account", manifest.Auth switch { { Type: SkillAuthType.ApiKey } auth => auth.Label + ". Kept in " + Workspace.Core.Platform.SecureStore.Mechanism + ".", { Type: SkillAuthType.CliLogin } auth => auth.Label, _ => "None needed." }),
            Fact("Agents", enabled.Count > 0 ? "AGEX makes this tool available to " + string.Join(", ", enabled) + ". " + manifest.CompatibilityNote : "Enable an agent to use this tool. " + manifest.CompatibilityNote),
            manifest.Trust == SkillTrust.Community ? Kit.Badge("Community tool: its risky permissions start as 'Ask each time'.", Tone.Warning, Icons.Alert) : null);
        if (manifest.Auth is { Type: SkillAuthType.ApiKey } key && !skills.HasAccountKey(skillId, manifest))
        {
            keyBox = new TextBox { PasswordChar = '•', PlaceholderText = key.Label, MinWidth = 300 };
            AutomationProperties.SetName(keyBox, key.Label);
            review.Children.Add(Kit.SettingRow(key.Label, key.Note.Length > 0 ? key.Note : "Stored in your system key store; never shown again.", keyBox));
            if (key.SetupUrl is { Length: > 0 } setup) review.Children.Add(Kit.Button("Get a key", () => Open(setup), "link", Icons.External));
        }
        var installedAlready = Installed(skillId) is not null && (package is null || Workspace.Core.Packages.Installed(skillId)?.Version == package.Version);
        if (await window.Dialogs.ShowAsync($"Connect {manifest.Name}?", new ScrollViewer { Content = review, MaxHeight = 420 }, [installedAlready ? "Test and connect" : "Install & Connect", "Cancel"]) != 0) return false;
        var keyValue = keyBox?.Text?.Trim() ?? "";
        return await ShowStepsAsync(manifest, keyValue, enabled, update: false);
    }

    /// <summary>Installs the newer pinned version of a connected tool, then tests it.</summary>
    public async Task<bool> UpdateAsync(string skillId)
    {
        var manifest = Workspace.Core.Skills.Catalog().Skills.FirstOrDefault(skill => skill.Id == skillId);
        if (manifest is null || Installed(skillId) is not { } installed) return false;
        var from = Workspace.Core.Packages.Installed(skillId)?.Version ?? installed.Manifest.Version;
        var package = Package(manifest);
        var text = Wrapped($"{manifest.Name} {from} is installed. AGEX installs {manifest.Version}"
            + (package is not null ? $" ({package.Spec}) into {Redactor.RedactPaths(Workspace.Core.Packages.Location(skillId))}" : "")
            + ". The current version stays until the new one is installed; if that fails nothing changes. Your key and settings are kept.", "body");
        if (await window.Dialogs.ShowAsync($"Update {manifest.Name}?", text, ["Update", "Cancel"]) != 0) return false;
        var agents = Workspace.Settings.EnabledAgents.Select(Workspace.Core.Registry.Get).OfType<IAgentAdapter>().Select(adapter => adapter.Name).ToList();
        return await ShowStepsAsync(manifest, "", agents, update: true);
    }

    /// <summary>
    /// Installs a runtime AGEX can install itself (Node.js, uv) after showing where it comes from, then
    /// continues with the connection that needed it.
    /// </summary>
    public async Task<bool> InstallDependencyAsync(string toolId, string? thenConnect)
    {
        var tool = SkillManager.Tool(toolId);
        if (!DependencyInstaller.CanInstall(toolId))
        {
            Open(tool.InstallUrl);
            return false;
        }
        var then = thenConnect is { Length: > 0 } ? Workspace.Core.Skills.Catalog().Skills.FirstOrDefault(skill => skill.Id == thenConnect) : null;
        var body = Kit.Column(8,
            Wrapped(Workspace.Core.Dependencies.Describe(toolId), "body"),
            Wrapped(then is not null ? $"Then AGEX continues with {then.Name}." : "", "small"));
        if (await window.Dialogs.ShowAsync($"Install {tool.Label}?", body, ["Install", "Cancel"]) != 0) return false;
        var step = new Step { Title = "Install " + tool.Label };
        var ok = false;
        var work = RunDependencyAsync(toolId, step, value => ok = value);
        await window.Dialogs.ShowAsync("Installing " + tool.Label, Kit.Column(12, step.View()), ["Close"]);
        await work;
        Workspace.NotifyConnectionsChanged();
        if (ok && then is not null && Installed(then.Id) is null) return await RunAsync(then.Id);
        return ok;
    }

    private async Task RunDependencyAsync(string toolId, Step step, Action<bool> done)
    {
        var tool = SkillManager.Tool(toolId);
        var progress = new Progress<string>(text => step.Set(null, text + "..."));
        step.Set(null, "Starting...");
        try
        {
            var folder = await Task.Run(() => Workspace.Core.Dependencies.InstallAsync(toolId, progress, CancellationToken.None));
            step.Set(true, $"{tool.Label} installed for your user in {Redactor.RedactPaths(folder)}. Close this window to continue.");
            done(true);
        }
        catch (Exception ex) when (ex is InstallFailedException or ChecksumMismatchException or HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            step.Set(false, "Not installed: " + Redactor.Redact(ex.Message) + " Nothing was changed.",
                Kit.Button("Try again", () => _ = RunDependencyAsync(toolId, step, done), "subtle", Icons.Refresh),
                Kit.Button("Official download", () => Open(tool.InstallUrl), "subtle", Icons.External));
            done(false);
        }
    }

    private async Task<bool> ShowStepsAsync(SkillManifest manifest, string keyValue, List<string> enabled, bool update)
    {
        var prerequisites = new Step { Title = "Check what it needs" };
        var install = new Step { Title = update ? "Install version " + manifest.Version : "Install and configure" };
        var secret = new Step { Title = "Store settings securely" };
        var connect = new Step { Title = "Connect to AGEX and your agents" };
        var test = new Step { Title = "Test the connection" };
        var agentTest = new Step { Title = "Test with an agent" };
        var steps = new[] { prerequisites, install, secret, connect, test, agentTest };
        var panel = Kit.Column(12, steps.Select(step => (Control?)step.View()).ToArray());
        var result = false;
        var work = RunStepsAsync(manifest, keyValue, prerequisites, install, secret, connect, test, enabled, update)
            .ContinueWith(async task => task is { IsCompletedSuccessfully: true, Result: true } && await AgentStepAsync(manifest.Id, agentTest), TaskScheduler.FromCurrentSynchronizationContext()).Unwrap()
            .ContinueWith(task =>
            {
                result = task is { IsCompletedSuccessfully: true, Result: true };
                Workspace.NotifyConnectionsChanged();
                window.Toast(manifest.Name, result ? "Connected and tested with an agent." : "Connection needs attention. Open its card to retry or see what is missing.",
                    result ? ToastKind.Success : ToastKind.Info);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        await window.Dialogs.ShowAsync($"{(update ? "Updating" : "Connecting")} {manifest.Name}", new ScrollViewer { Content = panel, MaxHeight = 460 }, ["Close"]);
        if (!work.IsCompleted) window.Toast($"Connecting {manifest.Name}", "AGEX finishes the steps in the background.", ToastKind.Info);
        else if (result) window.Toast($"{manifest.Name} is ready", "Agents use it automatically when a request needs it.", ToastKind.Success);
        Workspace.NotifyConnectionsChanged();
        return work.IsCompleted && result;
    }

    /// <summary>Runs on the UI thread; long work awaits background tasks, so the dialog stays responsive.</summary>
    private async Task<bool> RunStepsAsync(SkillManifest manifest, string keyValue, Step prerequisites, Step install, Step secret, Step connect, Step test, List<string> agents, bool update)
    {
        var skills = Workspace.Core.Skills;

        // Prerequisites: runtimes AGEX installs itself; anything else opens its official download.
        foreach (var tool in skills.MissingTools(manifest))
        {
            if (!DependencyInstaller.CanInstall(tool.Id))
            {
                prerequisites.Set(false, $"Needs {tool.Label}, which AGEX cannot install itself. Install it from the official page, then press Check again.",
                    Kit.Button("Get " + tool.Label, () => Open(tool.InstallUrl), "primary", Icons.External),
                    Kit.Button("Check again", () => _ = RunStepsAsync(manifest, keyValue, prerequisites, install, secret, connect, test, agents, update), "subtle", Icons.Refresh));
                return false;
            }
            prerequisites.Set(null, $"Installing {tool.Label} (official download, checksum verified)...");
            try
            {
                var progress = new Progress<string>(text => prerequisites.Set(null, $"{tool.Label}: {text}..."));
                await Task.Run(() => Workspace.Core.Dependencies.InstallAsync(tool.Id, progress, CancellationToken.None));
            }
            catch (Exception ex) when (ex is InstallFailedException or ChecksumMismatchException or HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                prerequisites.Set(false, $"{tool.Label} was not installed: {Redactor.Redact(ex.Message)} Nothing was changed.",
                    Kit.Button("Try again", () => _ = RunStepsAsync(manifest, keyValue, prerequisites, install, secret, connect, test, agents, update), "primary", Icons.Refresh),
                    Kit.Button("Official download", () => Open(tool.InstallUrl), "subtle", Icons.External));
                return false;
            }
        }
        prerequisites.Set(true, manifest.RequiredTools.Count == 0 ? "Nothing else needed." : string.Join(", ", manifest.RequiredTools.Select(tool => SkillManager.Tool(tool).Label)) + " ready.");

        // Install: the pinned package into AGEX's folder, then the connection entry. A failure undoes both.
        var installed = Installed(manifest.Id);
        var package = Package(manifest);
        var packageInstalled = false;
        if (package is not null && Workspace.Core.Packages.Installed(manifest.Id)?.Version != package.Version)
        {
            install.Set(null, $"Installing {package.Spec}...");
            try
            {
                var progress = new Progress<string>(text => install.Set(null, text + "..."));
                await Task.Run(() => Workspace.Core.Packages.InstallAsync(manifest.Id, package, progress, CancellationToken.None));
                packageInstalled = true;
            }
            catch (Exception ex) when (ex is InstallFailedException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                install.Set(false, "Not installed: " + Redactor.Redact(ex.Message) + (installed is null ? " Nothing was left behind." : " The previous version is unchanged."),
                    Kit.Button("Try again", () => _ = RunStepsAsync(manifest, keyValue, prerequisites, install, secret, connect, test, agents, update), "primary", Icons.Refresh));
                return false;
            }
        }
        if (installed is null || update || installed.Manifest.Version != manifest.Version)
        {
            install.Set(null, "Adding the connection...");
            try
            {
                var choices = installed?.PermissionChoices ?? manifest.Permissions.ToDictionary(permission => permission, permission => manifest.Trust == SkillTrust.Community && SkillText.IsHighRisk(permission) ? PermissionChoice.AskEachTime : PermissionChoice.AlwaysAllow);
                installed = await skills.InstallAsync(manifest, choices, null, CancellationToken.None);
            }
            catch (Exception ex) when (ex is SkillException or IOException or UnauthorizedAccessException or HttpRequestException)
            {
                if (packageInstalled && Installed(manifest.Id) is null) Workspace.Core.Packages.Remove(manifest.Id);
                install.Set(false, "Not installed: " + ex.Message, Kit.Button("Try again", () => _ = RunStepsAsync(manifest, keyValue, prerequisites, install, secret, connect, test, agents, update), "subtle", Icons.Refresh));
                return false;
            }
        }
        if (!installed.Enabled) skills.SetEnabled(installed.Id, true);
        install.Set(true, package is not null ? $"Installed {package.Spec}." : manifest.Mcp?.Transport == "http" ? "Nothing to install (hosted service)." : $"Installed {manifest.Name} {manifest.Version}.");

        if (manifest.Auth is { Type: SkillAuthType.ApiKey } auth)
        {
            if (keyValue.Length > 0) skills.SetSecret(installed.Id, auth.Secret, keyValue);
            if (skills.HasAccountKey(installed.Id, manifest)) secret.Set(true, "Key stored in " + Workspace.Core.Platform.SecureStore.Mechanism + ".");
            else
            {
                secret.Set(false, auth.Label + " is still needed. Add it, then test again.", Kit.Button("Add key", () => { window.Dialogs.Close(-1); _ = window.Page<SkillsPage>("skills").ShowByIdAsync(manifest.Id); }, "primary", Icons.Lock));
                connect.Set(null, "Waiting for the key.");
                test.Set(null, "Waiting for the key.");
                return false;
            }
        }
        else if (manifest.Auth is { Type: SkillAuthType.CliLogin } login)
        {
            secret.Set(null, "Checking sign-in...");
            var verified = await Workspace.Core.CliConnections.VerifyAsync(manifest, CancellationToken.None);
            if (!verified.Ok)
            {
                var command = Workspace.Core.CliConnections.LoginCommand(manifest);
                if (command.Path is null)
                {
                    secret.Set(false, "Sign-in program is missing. Install its CLI, then try again.",
                        Kit.Button("Setup guide", () => Open(login.SetupUrl), "subtle", Icons.External));
                    return false;
                }
                var folder = Workspace.Project?.Path ?? Workspace.Core.Platform.Paths.DataRoot;
                var signedIn = await ConnectionFlow.RunAsync(
                    () => Workspace.Core.Platform.RunInTerminal(command.Path, command.Arguments, folder),
                    async token => (await Workspace.Core.CliConnections.VerifyAsync(manifest, token)).Ok,
                    state => secret.Set(null, state switch
                    {
                        ConnectionFlowState.Connecting => "Opening sign-in...",
                        ConnectionFlowState.WaitingForSignIn => "Waiting for sign-in. Complete it in the terminal or browser; AGEX checks automatically.",
                        ConnectionFlowState.Connected => "Sign-in verified.",
                        _ => "Sign-in could not be verified. Check the terminal or open the setup guide.",
                    }), CancellationToken.None);
                if (!signedIn)
                {
                    secret.Set(false, "Sign-in still needed. AGEX has not connected this service.",
                        Kit.Button("Try sign-in again", () => _ = RunStepsAsync(manifest, keyValue, prerequisites, install, secret, connect, test, agents, update), "subtle", Icons.Refresh),
                        Kit.Button("Setup guide", () => Open(login.SetupUrl), "subtle", Icons.External));
                    return false;
                }
            }
            secret.Set(true, "Sign-in verified.");
            Workspace.NotifyConnectionsChanged();
        }
        else secret.Set(true, "No settings needed.");

        connect.Set(true, agents.Count > 0
            ? "AGEX hands it to " + string.Join(" and ", agents) + " whenever a request needs it. Nothing is written into their own settings."
            : "Installed. Enable an agent to use it.",
            agents.Count > 0 ? [] : [Kit.Button("Open Agents", () => { window.Dialogs.Close(-1); window.Navigate("agents"); }, "subtle", Icons.Agent)]);

        if (skills.SpecFor(skills.Installed().First(skill => skill.Id == installed.Id)) is not { } spec)
        {
            test.Set(false, "AGEX could not build the connection (a setting is missing).");
            return false;
        }
        test.Set(null, "Starting it and asking for its tools...");
        var probe = await Task.Run(() => Workspace.Core.McpProbe.TestAsync(spec, CancellationToken.None));
        test.Set(probe.Ok, probe.Ok ? probe.Message : probe.Message, probe.Ok ? [] : [Kit.Button("Test again", () => _ = RetestAsync(manifest.Id, test), "subtle", Icons.Refresh)]);
        return probe.Ok;
    }

    /// <summary>The agent AGEX tests a connection with: the leader when it can use connected tools, else the first enabled one that can.</summary>
    private Agex.Core.Agents.IAgentAdapter? TestAgent(string skillId)
    {
        if (Installed(skillId) is not { } skill) return null;
        var (usable, _) = AgentToolSupport.Split(Workspace.Core.Registry, Workspace.Settings.EnabledAgents, skill.Manifest);
        return usable.FirstOrDefault(adapter => adapter.Id == Workspace.Settings.Leader) ?? usable.FirstOrDefault();
    }

    /// <summary>
    /// The last step of every connection: a real agent receives the connection and calls one read-only tool.
    /// Only this makes a connection "Connected".
    /// </summary>
    private async Task<bool> AgentStepAsync(string skillId, Step step, string? agentId = null)
    {
        var agent = agentId is null ? TestAgent(skillId) : Workspace.Core.Registry.Get(agentId);
        if (agent is null)
        {
            step.Set(false, "Enable an agent, then test again.",
                Kit.Button("Open Agents", () => { window.Dialogs.Close(-1); window.Navigate("agents"); }, "primary", Icons.Agent));
            return false;
        }
        step.Set(null, $"{agent.Name} gets this connection and calls one tool that only reads (about a minute)...");
        var check = await Task.Run(() => Workspace.Core.ConnectionTester.TestAsync(skillId, agent.Id, CancellationToken.None));
        step.Set(check.Ok, check.Ok ? check.Message + " Ready." : check.Message, check.Ok ? [] : [Kit.Button("Test again", () => _ = AgentStepAsync(skillId, step, agent.Id), "subtle", Icons.Refresh)]);
        Workspace.NotifyConnectionsChanged();
        return check.Ok;
    }

    /// <summary>"Test with agent" from a connection card: pick the agent, then run the agent test.</summary>
    public async Task<bool> TestWithAgentAsync(string skillId)
    {
        if (Installed(skillId) is not { } skill) return false;
        var (usable, _) = AgentToolSupport.Split(Workspace.Core.Registry, Workspace.Settings.EnabledAgents, skill.Manifest);
        var step = new Step { Title = "Test with an agent" };
        var chosen = TestAgent(skillId)?.Id;
        var choice = Kit.Combo(usable.Select(adapter => (adapter.Id, adapter.Name)), chosen ?? "", id => chosen = id, 220);
        AutomationProperties.SetName(choice, "Agent for the test");
        var intro = Wrapped($"The agent receives only {skill.Manifest.Name}, in an empty folder, with no file changes and no commands, and calls one tool that only reads.", "small");
        var ok = false;
        var body = Kit.Column(12, intro, usable.Count > 0 ? choice : null, step.View());
        if (usable.Count == 0) step.Set(false, "Enable an agent to test this connection.", Kit.Button("Open Agents", () => { window.Dialogs.Close(-1); window.Navigate("agents"); }, "primary", Icons.Agent));
        else step.Set(null, "Press Test to start.", Kit.Button("Test", () => _ = Run(), "primary", Icons.Play));
        async Task Run() => ok = await AgentStepAsync(skillId, step, chosen);
        await window.Dialogs.ShowAsync($"Test {skill.Manifest.Name} with an agent", body, ["Close"]);
        Workspace.NotifyConnectionsChanged();
        return ok;
    }

    private async Task RetestAsync(string skillId, Step test)
    {
        if (Installed(skillId) is not { } skill || Workspace.Core.Skills.SpecFor(skill) is not { } spec) return;
        test.Set(null, "Testing...");
        var probe = await Task.Run(() => Workspace.Core.McpProbe.TestAsync(spec, CancellationToken.None));
        test.Set(probe.Ok, probe.Message, probe.Ok ? [] : [Kit.Button("Test again", () => _ = RetestAsync(skillId, test), "subtle", Icons.Refresh)]);
    }

    /// <summary>The pinned npm or PyPI package of a local catalog server, or null (hosted, or a command AGEX does not manage).</summary>
    private static PackageRef? Package(SkillManifest manifest) =>
        manifest.Mcp is { Transport: not "http" } mcp ? PackageRef.Parse(mcp.Command, mcp.Args) : null;

    /// <summary>
    /// Revit and AutoCAD through the Autodesk AI Bridge. AGEX finds the programs, installs the prebuilt
    /// bridge (host, Revit add-in per year, AutoCAD plug-in) for the current user after the user presses
    /// Install, connects it, tests the host, and shows each program's state.
    /// </summary>
    public async Task<bool> BridgeAsync()
    {
        var installer = Workspace.Core.AutodeskBridge;
        var detect = new Step { Title = "1. Find Revit and AutoCAD" };
        var bridge = new Step { Title = "2. Install the Autodesk AI Bridge" };
        var connect = new Step { Title = "3. Configure the connection" };
        var test = new Step { Title = "4. Test the bridge" };
        var agentTest = new Step { Title = "5. Test with an agent" };
        var programs = Kit.Column(6);
        var panel = Kit.Column(12, detect.View(), bridge.View(), connect.View(), test.View(), agentTest.View(), Kit.Divider(), Kit.Text("Programs", "subtitle"), programs);
        var result = false;

        void ShowPrograms(IReadOnlySet<string>? connected)
        {
            programs.Children.Clear();
            var status = installer.Status(connected);
            if (status.Products.Count == 0) programs.Children.Add(Wrapped("No Revit or AutoCAD found.", "small"));
            foreach (var product in status.Products)
            {
                var row = Kit.Row(8,
                    Kit.Icon(product.Connected == true ? Icons.Check : product.Installed ? Icons.Check : Icons.Dot, 14, product.Connected == true ? "SuccessBrush" : product.Installed ? "Text2Brush" : "Text3Brush"),
                    Kit.Text(product.Product.Name, "body"),
                    Kit.Text((product.Installed ? "Installed" : product.Product.Supported ? "Not installed" : "Not supported")
                        + (product.Connected == true ? " · Connected" : product.Installed && product.Connected == false ? $" · Not connected (open {product.Product.Name})" : "")
                        + (!product.Product.Supported ? ": " + product.Detail : ""), "caption"));
                AutomationProperties.SetName(row, product.Product.Name + ": " + product.Detail);
                programs.Children.Add(row);
            }
        }

        void Detect()
        {
            var found = installer.Detect();
            detect.Set(found.Count > 0, found.Count > 0
                ? "Found " + string.Join(", ", found.Select(product => product.Name + (product.Supported ? "" : " (not supported)"))) + "."
                : "Neither Revit nor AutoCAD was found. You can still install the bridge; it is used once one of them is installed. The team also works with exported PDF, CSV or IFC files.");
            ShowPrograms(null);
        }

        async Task InstallAsync()
        {
            bridge.Set(null, "Checking the package...");
            BridgePackageSource source;
            try { source = await installer.ResolveSourceAsync(CancellationToken.None); }
            catch (Exception ex) when (ex is InstallFailedException or HttpRequestException or IOException or TaskCanceledException)
            {
                bridge.Set(false, "The bridge package could not be found: " + Redactor.Redact(ex.Message), Kit.Button("Try again", () => _ = InstallAsync(), "primary", Icons.Refresh));
                return;
            }
            var progress = new Progress<string>(text => bridge.Set(null, text + "..."));
            try
            {
                var version = await Task.Run(() => installer.InstallAsync(source, progress, CancellationToken.None));
                bridge.Set(true, $"Installed version {version} for your user from {source.Description}{(source.Sha256 is null ? "" : " (checksum verified)")}.",
                    Kit.Button("Uninstall", () => _ = UninstallAsync(), "subtle", Icons.Close));
            }
            catch (Exception ex) when (ex is InstallFailedException or ChecksumMismatchException or HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                bridge.Set(false, "Not installed: " + Redactor.Redact(ex.Message) + " Every change was undone.", Kit.Button("Try again", () => _ = InstallAsync(), "primary", Icons.Refresh));
                ShowPrograms(null);
                return;
            }
            await ConnectAndTestAsync();
        }

        async Task UninstallAsync()
        {
            Workspace.Core.Skills.Remove(AutodeskBridge.SkillId);
            await Task.Run(installer.Uninstall);
            bridge.Set(null, "Uninstalled: the host, the Revit add-ins and the AutoCAD plug-in were removed.", InstallButton());
            connect.Set(null, "Waiting for the bridge.");
            test.Set(null, "Waiting for the bridge.");
            ShowPrograms(null);
            Workspace.NotifyConnectionsChanged();
        }

        Button InstallButton() => Kit.Button("Install", () => _ = InstallAsync(), "primary", Icons.Download);

        async Task ConnectAndTestAsync()
        {
            connect.Set(null, "Connecting...");
            InstalledSkill? skill;
            try { skill = Installed(AutodeskBridge.SkillId) ?? Workspace.Core.Teams.ConnectAutodeskBridge(); }
            catch (SkillException ex) { connect.Set(false, ex.Message); return; }
            if (skill is null) { connect.Set(false, "The bridge host is missing. Install the bridge again."); return; }
            connect.Set(true, "Configured automatically. AGEX provides it to enabled agents when a request needs Revit or AutoCAD; every use asks you first.");
            Workspace.NotifyConnectionsChanged();
            await TestBridgeAsync(skill);
        }

        async Task TestBridgeAsync(InstalledSkill skill)
        {
            if (Workspace.Core.Skills.SpecFor(skill) is not { } spec) { test.Set(false, "The bridge connection is incomplete."); return; }
            test.Set(null, "Starting the bridge host, then asking which programs are connected...");
            var probe = await Task.Run(() => Workspace.Core.McpProbe.TestAsync(spec, CancellationToken.None, TimeSpan.FromSeconds(45), "autodesk_list_instances", TimeSpan.FromSeconds(6)));
            var connected = probe.Ok ? AutodeskBridgeInstaller.ConnectedProducts(probe.ToolOutput) : null;
            test.Set(probe.Ok, probe.Ok
                ? probe.Message + (connected!.Count > 0 ? " Ready." : " The host works. Open Revit or AutoCAD to connect them, then test again.")
                : probe.Message, Kit.Button("Test again", () => _ = TestBridgeAsync(skill), "subtle", Icons.Refresh));
            ShowPrograms(connected);
            // The host answering is not enough: an agent must receive the bridge and call its read-only tool.
            result = probe.Ok && await AgentStepAsync(skill.Id, agentTest);
        }

        Detect();
        var status = installer.Status();
        if (Workspace.Core.Platform.Os != Agex.Core.Platform.OsKind.Windows)
            bridge.Set(false, "Revit and AutoCAD run on Windows only.");
        else if (!status.HostInstalled)
        {
            // Shown before anything changes; the Install button is the confirmation.
            var targets = new List<string> { Redactor.RedactPaths(installer.Root) + " (host)" };
            foreach (var product in installer.Detect().Where(item => item.Supported))
                targets.Add(product.Product == "revit" ? Redactor.RedactPaths(installer.RevitManifest(product.Year)) : Redactor.RedactPaths(installer.AutoCadBundle));
            bridge.Set(null, $"Not installed. Install downloads the prebuilt Autodesk AI Bridge (MIT licence) from the AGEX {Agex.Core.AgexInfo.Version} release on GitHub, checks its SHA-256, "
                + "and installs it for your user only (no administrator rights, nothing to compile). It writes: " + string.Join("; ", targets.Distinct())
                + ". It needs no account and no internet after installing: the host talks to Revit and AutoCAD on this computer through a private pipe.", InstallButton());
            connect.Set(null, "Automatic after installing.");
            test.Set(null, "Automatic after installing.");
        }
        else
        {
            bridge.Set(true, $"Installed{(status.Version.Length > 0 ? " (version " + status.Version + ")" : "")} for your user.",
                Kit.Button("Reinstall", () => _ = InstallAsync(), "subtle", Icons.Refresh),
                Kit.Button("Uninstall", () => _ = UninstallAsync(), "subtle", Icons.Close));
            _ = ConnectAndTestAsync();
        }
        await window.Dialogs.ShowAsync("Autodesk AI Bridge", new ScrollViewer { Content = panel, MaxHeight = 520 }, ["Close"], maxWidth: 640);
        Workspace.NotifyConnectionsChanged();
        return result || Installed(AutodeskBridge.SkillId) is not null;
    }

    private InstalledSkill? Installed(string id) => Workspace.Core.Skills.Installed().FirstOrDefault(skill => skill.Id == id);

    private void Open(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) Workspace.Core.Platform.OpenUrl(uri);
    }

    private static TextBlock Wrapped(string text, string classes)
    {
        var block = Kit.Text(text, classes);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    private static Control Fact(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), ColumnSpacing = 10 };
        grid.Children.Add(Kit.Text(label, "small"));
        var text = Wrapped(value, "small");
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }
}
