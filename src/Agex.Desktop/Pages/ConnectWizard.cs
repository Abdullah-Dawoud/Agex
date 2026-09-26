using Agex.Core.Agents;
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
/// the agents that support it, and test it with the MCP handshake. Steps it
/// cannot do (installing Node.js or the bridge) are shown with the exact next
/// action. No JSON editing.
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
            return grid;
        }
    }

    /// <summary>Connects a catalog MCP tool. Returns true when it ends installed and the test passed (or could not run but the tool is installed).</summary>
    public async Task<bool> RunAsync(string skillId)
    {
        if (skillId == AutodeskBridge.SkillId) return await BridgeAsync();
        var manifest = Workspace.Core.Skills.Catalog().Skills.FirstOrDefault(skill => skill.Id == skillId);
        if (manifest is null) { await window.Dialogs.MessageAsync("Not found", "AGEX has no reviewed connection with this name."); return false; }
        if (manifest.Kind != SkillKind.Mcp) { await window.Page<SkillsPage>("skills").InstallByIdAsync(skillId); return Installed(skillId) is not null; }

        // 1. Review: source, package, permissions, network, account, affected agents.
        var agents = manifest.SupportedAgents.Select(id => Workspace.Core.Registry.Get(id)).OfType<IAgentAdapter>().ToList();
        var enabled = agents.Where(adapter => Workspace.Settings.EnabledAgents.Contains(adapter.Id)).Select(adapter => adapter.Name).ToList();
        var runs = manifest.Mcp?.Transport == "http" ? "Hosted service at " + manifest.Mcp.Url : $"{manifest.Mcp?.Command} {string.Join(' ', manifest.Mcp?.Args ?? [])} (on this computer, started only while an agent uses it)";
        TextBox? keyBox = null;
        var review = Kit.Column(8,
            Wrapped(manifest.Description, "body"),
            Fact("Source", $"{manifest.Author}, {SkillText.Trust(manifest.Trust)}, licence {manifest.License}. {manifest.Homepage}"),
            Fact("What runs", runs),
            Fact("It may", manifest.Permissions.Count == 0 ? "Nothing beyond reading what agents pass to it." : string.Join(", ", manifest.Permissions.Select(SkillText.Permission))),
            Fact("Network", manifest.Permissions.Contains(SkillPermission.Network) ? "Uses the internet or pages you open." : "Works on this computer only."),
            Fact("Account", manifest.Auth switch { { Type: SkillAuthType.ApiKey } auth => auth.Label + ". Kept in " + Workspace.Core.Platform.SecureStore.Mechanism + ".", { Type: SkillAuthType.CliLogin } auth => auth.Label, _ => "None needed." }),
            Fact("Agents", (enabled.Count > 0 ? string.Join(", ", enabled) : "None of your enabled agents") + " can use it. " + manifest.CompatibilityNote),
            manifest.Trust == SkillTrust.Community ? Kit.Badge("Community tool: its risky permissions start as 'Ask each time'.", Tone.Warning, Icons.Alert) : null);
        if (manifest.Auth is { Type: SkillAuthType.ApiKey } key)
        {
            keyBox = new TextBox { PasswordChar = '•', PlaceholderText = key.Label, MinWidth = 300 };
            AutomationProperties.SetName(keyBox, key.Label);
            review.Children.Add(Kit.SettingRow(key.Label, key.Note.Length > 0 ? key.Note : "Stored in your system key store; never shown again.", keyBox));
            if (key.SetupUrl is { Length: > 0 } setup) review.Children.Add(Kit.Button("Get a key", () => Open(setup), "link", Icons.External));
        }
        var installedAlready = Installed(skillId) is not null;
        if (await window.Dialogs.ShowAsync($"Connect {manifest.Name}?", new ScrollViewer { Content = review, MaxHeight = 420 }, [installedAlready ? "Test and connect" : "Install & Connect", "Cancel"]) != 0) return false;
        var keyValue = keyBox?.Text?.Trim() ?? "";

        // 2-6. The steps, done for the user where AGEX can.
        var prerequisites = new Step { Title = "Check what it needs" };
        var install = new Step { Title = "Install and configure" };
        var secret = new Step { Title = "Store settings securely" };
        var connect = new Step { Title = "Connect to AGEX and your agents" };
        var test = new Step { Title = "Test the connection" };
        var steps = new[] { prerequisites, install, secret, connect, test };
        var panel = Kit.Column(12, steps.Select(step => (Control?)step.View()).ToArray());
        var result = false;
        var work = Task.Run(async () => result = await RunStepsAsync(manifest, keyValue, prerequisites, install, secret, connect, test, enabled));
        await window.Dialogs.ShowAsync($"Connecting {manifest.Name}", new ScrollViewer { Content = panel, MaxHeight = 460 }, ["Close"]);
        if (!work.IsCompleted) window.Toast($"Connecting {manifest.Name}", "AGEX finishes the steps in the background.", ToastKind.Info);
        else if (await work) window.Toast($"{manifest.Name} is ready", "Agents use it automatically when a request needs it.", ToastKind.Success);
        Workspace.NotifyConnectionsChanged();
        return work.IsCompleted ? result : Installed(skillId) is not null;
    }

    private async Task<bool> RunStepsAsync(SkillManifest manifest, string keyValue, Step prerequisites, Step install, Step secret, Step connect, Step test, List<string> agents)
    {
        void Ui(Action action) => App.Post(action);
        var skills = Workspace.Core.Skills;
        var missing = skills.MissingTools(manifest);
        if (missing.Count > 0)
        {
            Ui(() => prerequisites.Set(false, "Needs " + string.Join(", ", missing.Select(tool => tool.Label)) + " first. Install it from the official page, then press Connect again.",
                missing.Where(tool => tool.InstallUrl.Length > 0).Select(tool => (Control)Kit.Button("Get " + tool.Label, () => Open(tool.InstallUrl), "primary", Icons.External)).ToArray()));
            return false;
        }
        Ui(() => prerequisites.Set(true, manifest.RequiredTools.Count == 0 ? "Nothing else needed." : string.Join(", ", manifest.RequiredTools.Select(tool => SkillManager.Tool(tool).Label)) + " found."));

        var installed = Installed(manifest.Id);
        if (installed is null)
        {
            Ui(() => install.Set(null, "Installing..."));
            try
            {
                var choices = manifest.Permissions.ToDictionary(permission => permission, permission => manifest.Trust == SkillTrust.Community && SkillText.IsHighRisk(permission) ? PermissionChoice.AskEachTime : PermissionChoice.AlwaysAllow);
                installed = await skills.InstallAsync(manifest, choices, null, CancellationToken.None);
                Ui(() => install.Set(true, "Installed " + manifest.Name + " " + manifest.Version + "."));
            }
            catch (Exception ex) when (ex is SkillException or IOException or UnauthorizedAccessException or HttpRequestException)
            {
                Ui(() => install.Set(false, "Not installed: " + ex.Message, Kit.Button("Try again", () => _ = RunAsync(manifest.Id), "subtle", Icons.Refresh)));
                return false;
            }
        }
        else
        {
            if (!installed.Enabled) skills.SetEnabled(installed.Id, true);
            Ui(() => install.Set(true, "Already installed; turned on."));
        }

        if (manifest.Auth is { Type: SkillAuthType.ApiKey } auth)
        {
            if (keyValue.Length > 0) skills.SetSecret(installed.Id, auth.Secret, keyValue);
            if (skills.HasAccountKey(installed.Id, manifest)) Ui(() => secret.Set(true, "Key stored in " + Workspace.Core.Platform.SecureStore.Mechanism + "."));
            else
            {
                Ui(() => secret.Set(false, auth.Label + " is still needed. Add it, then test again.", Kit.Button("Add key", () => _ = window.Page<SkillsPage>("skills").ShowByIdAsync(manifest.Id), "primary", Icons.Lock)));
                Ui(() => connect.Set(null, "Waiting for the key."));
                Ui(() => test.Set(null, "Waiting for the key."));
                return false;
            }
        }
        else Ui(() => secret.Set(true, manifest.Auth?.Type == SkillAuthType.CliLogin ? "Uses the tool's own sign-in." : "No settings needed."));

        Ui(() => connect.Set(true, agents.Count > 0
            ? "AGEX hands it to " + string.Join(" and ", agents) + " whenever a request needs it. Nothing is written into their own settings."
            : "Installed. Enable Codex or Claude Code to let agents use it.",
            agents.Count > 0 ? [] : [Kit.Button("Open Agents", () => window.Navigate("agents"), "subtle", Icons.Agent)]));

        if (skills.SpecFor(skills.Installed().First(skill => skill.Id == installed.Id)) is not { } spec)
        {
            Ui(() => test.Set(false, "AGEX could not build the connection (a setting is missing)."));
            return false;
        }
        Ui(() => test.Set(null, "Starting it and asking for its tools (the first start can take a minute while it downloads)..."));
        var probe = await Workspace.Core.McpProbe.TestAsync(spec, CancellationToken.None);
        Ui(() => test.Set(probe.Ok, probe.Ok ? probe.Message + " Ready." : probe.Message, probe.Ok ? [] : [Kit.Button("Test again", () => _ = RetestAsync(manifest.Id, test), "subtle", Icons.Refresh)]));
        return probe.Ok;
    }

    private async Task RetestAsync(string skillId, Step test)
    {
        if (Installed(skillId) is not { } skill || Workspace.Core.Skills.SpecFor(skill) is not { } spec) return;
        test.Set(null, "Testing...");
        var probe = await Workspace.Core.McpProbe.TestAsync(spec, CancellationToken.None);
        test.Set(probe.Ok, probe.Message);
    }

    /// <summary>
    /// Revit and AutoCAD through the Autodesk AI Bridge: finds the programs and
    /// the bridge, connects it, and tests it. The bridge itself has no public
    /// installer, so that one step stays manual.
    /// </summary>
    public async Task<bool> BridgeAsync()
    {
        var programs = new ProgramDetector(Workspace.Core.Platform);
        var products = new[] { "revit", "autocad" }.Select(programs.Find).ToList();
        var detect = new Step { Title = "Find Revit and AutoCAD" };
        var bridge = new Step { Title = "Find the Autodesk AI Bridge" };
        var connect = new Step { Title = "Connect it to AGEX" };
        var test = new Step { Title = "Test the connection" };
        var panel = Kit.Column(12, detect.View(), bridge.View(), connect.View(), test.View());
        var result = false;
        var found = products.Where(product => product.Path is not null).ToList();
        detect.Set(found.Count > 0, found.Count > 0
            ? string.Join(", ", products.Select(product => product.Name + (product.Path is null ? ": not found" : ": found")))
            : "Neither Revit nor AutoCAD was found on this computer. The team still works with exported PDF, CSV or IFC files.",
            found.Count > 0 ? [] : [Kit.Button("Autodesk downloads", () => Open("https://www.autodesk.com/products/revit/"), "subtle", Icons.External)]);
        void Check()
        {
            var host = AutodeskBridge.HostPath();
            if (host is null)
            {
                bridge.Set(false, "Not installed. The Autodesk AI Bridge has no public installer yet: build and install it from its source, which puts the Host in %LOCALAPPDATA%\\AutodeskAIBridge\\Host. AGEX cannot install it for you.",
                    Kit.Button("Check again", Check, "primary", Icons.Refresh));
                connect.Set(null, "Waiting for the bridge.");
                test.Set(null, "Waiting for the bridge.");
                return;
            }
            bridge.Set(true, "Installed for your user (no administrator rights needed).");
            connect.Set(null, "Connecting...");
            try
            {
                var skill = Installed(AutodeskBridge.SkillId) ?? Workspace.Core.Teams.ConnectAutodeskBridge();
                if (skill is null) { connect.Set(false, "Could not connect the bridge."); return; }
                connect.Set(true, "Connected. Codex and Claude Code receive it when a request needs Revit or AutoCAD. Every use asks you first.");
                _ = TestBridgeAsync(skill, test, value => result = value);
            }
            catch (SkillException ex) { connect.Set(false, ex.Message); }
        }
        Check();
        await window.Dialogs.ShowAsync("Connect Revit and AutoCAD", new ScrollViewer { Content = panel, MaxHeight = 460 }, ["Close"]);
        Workspace.NotifyConnectionsChanged();
        return result || Installed(AutodeskBridge.SkillId) is not null;
    }

    private async Task TestBridgeAsync(InstalledSkill skill, Step test, Action<bool> done)
    {
        if (Workspace.Core.Skills.SpecFor(skill) is not { } spec) { test.Set(false, "The bridge connection is incomplete."); return; }
        test.Set(null, "Starting the bridge host and asking for its tools...");
        var probe = await Workspace.Core.McpProbe.TestAsync(spec, CancellationToken.None, TimeSpan.FromSeconds(30));
        test.Set(probe.Ok, probe.Ok ? probe.Message + " Open Revit or AutoCAD with the bridge plug-in loaded before asking agents to use them." : probe.Message + " Open Revit or AutoCAD with the plug-in loaded, then test again.",
            probe.Ok ? [] : [Kit.Button("Test again", () => _ = TestBridgeAsync(skill, test, done), "subtle", Icons.Refresh)]);
        done(probe.Ok);
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
