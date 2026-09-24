using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Core.Settings;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Agex.Desktop.Pages;

/// <summary>Agents on this computer, how work is routed, and saved teams.</summary>
public sealed class AgentsPage(MainWindow window) : AppPage(window)
{
    private readonly StackPanel _agents = new() { Spacing = 12 };
    private readonly StackPanel _routing = new() { Spacing = 8 };
    private readonly StackPanel _teams = new() { Spacing = 8 };
    private readonly StackPanel _detected = new() { Spacing = 8 };
    private Button? _scan;

    public override string Id => "agents";
    public override string Title => "Agents";
    public override string Icon => Icons.Agent;

    protected override Control Build()
    {
        Workspace.ScanChanged += Refresh;
        _scan = Kit.Button("Scan again", () => _ = Workspace.ScanAsync(), "", Icons.Refresh, "Look for newly installed agents and check them");
        var page = Kit.Column(24,
            Kit.PageHeader("Agents", "AGEX coordinates AI agents on this computer. It can install supported agents from their official source and open their own sign-in, always after you confirm. It never sees your passwords.", _scan),
            Kit.Column(0, Kit.SectionHeader("Your agents", "Enable the agents AGEX may use. Status comes from a version check and the agent's own sign-in status; no model quota is used."), _agents),
            Kit.Column(0, Kit.SectionHeader("How work is shared", "Pick a preset. Exact numbers are under Advanced."), _routing),
            Kit.Column(0, Kit.SectionHeader("Teams", "Saved groups of agents you can pick when you start a request.", Kit.Button("New team", () => _ = EditTeamAsync(null), "", Icons.Plus)), _teams),
            Kit.Column(0, Kit.SectionHeader("Other tools on this computer", "Found, but AGEX cannot drive them (no reliable integration yet), or they are editors and tools."), _detected));
        Refresh();
        return Kit.Page(page);
    }

    public override void OnShown()
    {
        Refresh();
        if (Workspace.Scan is null && !Workspace.Scanning) _ = Workspace.ScanAsync();
    }

    private void Refresh()
    {
        if (_scan is not null) { _scan.IsEnabled = !Workspace.Scanning; ToolTip.SetTip(_scan, Workspace.Scanning ? "Scanning..." : "Look for newly installed agents and check them"); }
        RefreshAgents();
        RefreshRouting();
        RefreshTeams();
        RefreshDetected();
    }

    private (string, Tone) Look(AgentStatus status) => status switch
    {
        AgentStatus.Supported => ("Ready", Tone.Success),
        AgentStatus.Available => (Workspace.Scanning ? "Checking..." : "Installed", Tone.Info),
        AgentStatus.AuthRequired => ("Sign-in required", Tone.Warning),
        AgentStatus.Broken => ("Not working", Tone.Danger),
        AgentStatus.PlatformUnsupported => ("Not available on this system", Tone.Neutral),
        AgentStatus.DetectedUnsupported => ("Detected - not integrated", Tone.Neutral),
        _ => ("Not installed", Tone.Neutral),
    };

    private void RefreshAgents()
    {
        _agents.Children.Clear();
        foreach (var adapter in Workspace.Core.Registry.Adapters)
        {
            var item = Workspace.Scan?.Items.FirstOrDefault(scan => scan.Id == adapter.Id);
            var status = item?.Status ?? AgentStatus.Unknown;
            var auth = Workspace.Auth.GetValueOrDefault(adapter.Id);
            var readiness = Workspace.Readiness(adapter.Id);
            var busy = Workspace.AgentBusy.Contains(adapter.Id);
            var options = Workspace.Settings.AgentOptions.GetValueOrDefault(adapter.Id) ?? new AgentOptions();
            var enabled = Workspace.Settings.EnabledAgents.Contains(adapter.Id);
            var installed = status is AgentStatus.Supported or AgentStatus.Available or AgentStatus.AuthRequired or AgentStatus.Broken;
            var discovery = Workspace.Core.Models.Cached(adapter.Id);
            var (effectiveModel, _) = ModelSelection.Resolve(options.Model, options.CustomModel, discovery);
            var privacy = adapter.PrivacyFor(effectiveModel);
            var health = Workspace.Core.Registry.Health(adapter.Id);
            var id = adapter.Id;

            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            top.Children.Add(Kit.Avatar(adapter.Name, 36));
            var readinessTone = readiness switch
            {
                AgentReadiness.InstalledReady => Tone.Success,
                AgentReadiness.InstalledAuthRequired => Tone.Warning,
                AgentReadiness.InstalledBroken => Tone.Danger,
                AgentReadiness.Checking => Tone.Info,
                _ => Tone.Neutral,
            };
            var title = Kit.Column(2,
                Kit.Row(8, Kit.Text(adapter.Name, "subtitle"), adapter.Stability == AdapterStability.Beta ? Kit.Badge("Beta adapter", Tone.Info) : null,
                    Kit.Badge(busy ? "Checking..." : AgentReadinessText.Label(readiness), busy ? Tone.Info : readinessTone)),
                Kit.Text($"{adapter.Provider}{(item?.Version is { Length: > 0 } version ? " · version " + version : "")} · {adapter.Description}", "small"));
            Grid.SetColumn(title, 1);
            top.Children.Add(title);
            var toggle = new ToggleSwitch { IsChecked = enabled, IsEnabled = installed || enabled, OnContent = "Enabled", OffContent = "Off" };
            AutomationProperties.SetName(toggle, $"Enable {adapter.Name}");
            toggle.IsCheckedChanged += (_, _) =>
            {
                Workspace.Settings.EnabledAgents.RemoveAll(existing => existing == id);
                if (toggle.IsChecked == true) Workspace.Settings.EnabledAgents.Add(id);
                Workspace.SaveSettings();
            };
            Grid.SetColumn(toggle, 2);
            top.Children.Add(toggle);

            var privacyBadge = privacy switch
            {
                PrivacyKind.Local => Kit.Badge("Runs on this computer", Tone.Success, Icons.Computer),
                PrivacyKind.Mixed => Kit.Badge("Local or cloud, depending on the model", Tone.Warning, Icons.Cloud),
                PrivacyKind.Cloud => Kit.Badge("Cloud: " + adapter.DataDestination(effectiveModel), Tone.Neutral, Icons.Cloud),
                _ => Kit.Badge("Data location unknown", Tone.Warning, Icons.Alert),
            };
            var authTone = auth?.State switch { AuthState.SignedIn or AuthState.NotRequired => Tone.Success, AuthState.SignedOut => Tone.Warning, _ => Tone.Neutral };
            var authText = AgentReadinessText.Auth(auth) + (auth is { State: AuthState.SignedIn, Identity.Length: > 0 } ? " · " + auth.Identity : "");
            var facts = Kit.Wrap(
                Kit.Badge(installed ? "Installed" : status == AgentStatus.PlatformUnsupported ? "Not for this system" : "Not installed", Tone.Neutral),
                installed ? Kit.Badge(authText, authTone) : null,
                installed ? Kit.Badge("Model: " + (effectiveModel ?? "Auto"), Tone.Neutral) : null,
                installed ? Kit.Badge(ModelCountText(discovery), Tone.Neutral) : null,
                privacyBadge,
                adapter.CanWriteFiles ? null : Kit.Badge("Text only - cannot open or edit files", Tone.Info));
            var caps = Kit.Text("Can: " + string.Join(", ", adapter.Capabilities.Select(CapabilityText).Select(text => text.ToLowerInvariant())), "caption");
            var detail = Kit.Column(10, top, facts, caps);
            if (auth is { State: AuthState.SignedOut or AuthState.Unknown, Detail.Length: > 0 }) detail.Children.Add(Kit.Text(auth.Detail, "small"));
            else if (item?.Detail is { Length: > 0 } reason && status is not (AgentStatus.Supported or AgentStatus.Available)) detail.Children.Add(Kit.Text(reason, "small"));
            if (!health.Healthy) detail.Children.Add(Kit.Row(8, Kit.Badge("Paused after errors", Tone.Warning), Kit.Text(health.Reason, "small"), Kit.Button("Try again", () => { Workspace.Core.Registry.ResetHealth(id); Refresh(); }, "link")));
            detail.Children.Add(Actions(adapter, readiness, auth, discovery, installed, busy));
            if (installed) detail.Children.Add(new Expander { Header = "Settings", Content = AgentOptionsEditor(adapter, options, discovery), HorizontalAlignment = HorizontalAlignment.Stretch });
            _agents.Children.Add(Kit.Card(detail));
        }
    }

    private static string ModelCountText(ModelDiscovery? discovery) => discovery?.Status switch
    {
        ModelDiscoveryStatus.Ok => $"{discovery.Models.Count} models available",
        ModelDiscoveryStatus.Empty => "No models yet",
        ModelDiscoveryStatus.Unavailable => "Model list not offered",
        ModelDiscoveryStatus.Failed => "Model list not loaded",
        _ => "Models not checked",
    };

    /// <summary>Only the actions that apply to this agent right now.</summary>
    private Control Actions(IAgentAdapter adapter, AgentReadiness readiness, AuthCheck? auth, ModelDiscovery? discovery, bool installed, bool busy)
    {
        var setup = adapter.Setup;
        var id = adapter.Id;
        var row = new WrapPanel();
        void Add(Button button) { button.IsEnabled = !busy; button.Margin = new Thickness(0, 0, 8, 6); row.Children.Add(button); }
        if (readiness == AgentReadiness.NotInstalled)
        {
            Add(setup.CanInstall ? Kit.Button("Install", () => _ = InstallAsync(adapter), "primary", Icons.Download) : Kit.Button("Install manually", () => _ = ManualInstallAsync(adapter), "primary", Icons.External));
            if (setup.CanInstall) Add(Kit.Button("Setup instructions", () => _ = ManualInstallAsync(adapter), "subtle", Icons.External));
        }
        if (installed && setup.CanSignIn && (auth?.State == AuthState.SignedOut || readiness == AgentReadiness.InstalledAuthRequired))
            Add(Kit.Button("Sign in", () => _ = SignInAsync(adapter), readiness == AgentReadiness.InstalledAuthRequired ? "primary" : "", Icons.Shield));
        if (installed && !adapter.PassiveAuthCheck && auth?.State != AuthState.SignedIn)
            Add(Kit.Button("Check sign-in", () => _ = TestAsync(adapter), "subtle", Icons.Refresh));
        if (installed) Add(Kit.Button("Test connection", () => _ = TestAsync(adapter), "subtle", Icons.Refresh, "Checks the version and sign-in. Uses no model quota."));
        if (installed && discovery?.Status is not ModelDiscoveryStatus.Unavailable)
            Add(Kit.Button("Refresh models", () => _ = Workspace.RefreshModelsAsync(id), "subtle", Icons.Refresh));
        if (readiness is AgentReadiness.NotInstalled or AgentReadiness.InstalledBroken)
            Add(Kit.Button("Retry detection", () => _ = Workspace.ScanAsync(), "subtle", Icons.Refresh));
        return row;
    }

    private async Task InstallAsync(IAgentAdapter adapter)
    {
        var plan = Workspace.Core.Installer.Plan(adapter);
        var setup = adapter.Setup;
        var body = Kit.Column(8,
            Kit.SettingRow("Agent", null, Kit.Text($"{adapter.Name} ({adapter.Provider})", "body")),
            Kit.SettingRow("Source", null, Kit.Text($"npm package {setup.NpmPackage}, published by {adapter.Provider}", "body")),
            Kit.SettingRow("What will be installed", null, Kit.Text(setup.WhatGetsInstalled, "small")),
            Kit.SettingRow("Size", null, Kit.Text(setup.SizeHint.Length > 0 ? setup.SizeHint : "not published", "small")),
            Kit.SettingRow("Administrator rights", null, Kit.Text(setup.AdminNote, "small")),
            Kit.SettingRow("Account and data", null, Kit.Text(setup.AccountNote, "small")),
            Kit.Text(plan.Possible ? $"AGEX will run: npm install --global {setup.NpmPackage}" : plan.Reason, "small"),
            Kit.Button("Official instructions", () => Workspace.Core.Platform.OpenUrl(new Uri(setup.OfficialUrl)), "link", Icons.External));
        if (!plan.Possible)
        {
            var choice = await Window.Dialogs.ShowAsync($"Install {adapter.Name}", body, ["Open Node.js download", "Close"]);
            if (choice == 0) Workspace.Core.Platform.OpenUrl(new Uri(AgentInstaller.NodeDownloadUrl));
            return;
        }
        if (await Window.Dialogs.ShowAsync($"Install {adapter.Name}?", body, ["Install", "Cancel"]) != 0) return;
        Window.Toast($"Installing {adapter.Name}", "This can take a few minutes.", ToastKind.Info);
        if (await Workspace.InstallAgentAsync(adapter.Id) && setup.CanSignIn) await SignInAsync(adapter);
    }

    private async Task ManualInstallAsync(IAgentAdapter adapter)
    {
        var setup = adapter.Setup;
        var command = setup.ManualCommands.GetValueOrDefault(Workspace.Core.Platform.Os) ?? "See the official instructions.";
        var body = Kit.Column(8,
            Kit.Text($"These are {adapter.Provider}'s official instructions. " + (setup.CanInstall ? "AGEX can also install it for you with the Install button." : "AGEX does not run this installer for you."), "body"),
            Kit.SettingRow("What gets installed", null, Kit.Text(setup.WhatGetsInstalled, "small")),
            Kit.SettingRow("Administrator rights", null, Kit.Text(setup.AdminNote, "small")),
            Kit.SettingRow("Account and data", null, Kit.Text(setup.AccountNote, "small")),
            Kit.Text("Official command for this system:", "small"),
            Kit.Card(Kit.Text(command, "small"), 10),
            Kit.Text("After installing, come back and press Retry detection.", "caption"));
        var choice = await Window.Dialogs.ShowAsync($"Install {adapter.Name}", body, ["Open official page", "Copy command", "Close"]);
        if (choice == 0) Workspace.Core.Platform.OpenUrl(new Uri(setup.OfficialUrl));
        else if (choice == 1) await Window.CopyAsync(command);
    }

    private async Task SignInAsync(IAgentAdapter adapter)
    {
        var setup = adapter.Setup;
        var body = Kit.Column(8,
            Kit.Text(setup.LoginInstructions, "body"),
            Kit.Text(adapter.PassiveAuthCheck ? "AGEX notices when you are signed in." : "When you are done, press Check sign-in on the agent card.", "small"),
            setup.LoginDocsUrl.Length > 0 ? Kit.Button("About signing in", () => Workspace.Core.Platform.OpenUrl(new Uri(setup.LoginDocsUrl)), "link", Icons.External) : null);
        if (await Window.Dialogs.ShowAsync($"Sign in to {adapter.Name}", body, ["Open sign-in", "Cancel"]) != 0) return;
        await Workspace.StartSignInAsync(adapter.Id);
    }

    private async Task TestAsync(IAgentAdapter adapter)
    {
        Workspace.Core.Registry.ResetHealth(adapter.Id);
        await Workspace.Core.Registry.CheckHealthAsync(adapter.Id, CancellationToken.None);
        await Workspace.ScanAsync();
        await Workspace.CheckAgentAsync(adapter.Id, true);
        var readiness = Workspace.Readiness(adapter.Id);
        var auth = Workspace.Auth.GetValueOrDefault(adapter.Id);
        Window.Toast($"{adapter.Name}: {AgentReadinessText.Label(readiness)}", AgentReadinessText.Auth(auth) + (auth?.Detail is { Length: > 0 } detail ? ". " + detail : ""),
            readiness == AgentReadiness.InstalledReady ? ToastKind.Success : ToastKind.Info);
        Refresh();
    }

    private Control AgentOptionsEditor(IAgentAdapter adapter, AgentOptions options, ModelDiscovery? discovery)
    {
        var id = adapter.Id;
        void Save() { Workspace.Settings.AgentOptions[id] = options; Workspace.SaveSettings(); }
        var column = Kit.Column(4);

        // Model picker: Auto plus what the agent itself reports. Free text only under Advanced.
        var items = new List<(string, string)> { ("", "Auto (the agent's own default)") };
        if (discovery?.Status == ModelDiscoveryStatus.Ok)
            items.AddRange(discovery.Models.Select(model => (model.Id, model.Label
                + (adapter is OllamaAdapter ? (model.Location == PrivacyKind.Local ? " · on this computer" : " · Ollama cloud") : "")
                + (model.ContextWindow is { } context ? $" · {context / 1000:N0}k context" : ""))));
        if (options.Model.Length > 0 && items.All(item => item.Item1 != options.Model)) items.Add((options.Model, options.Model + (options.CustomModel ? " (custom)" : "")));
        var picker = Kit.Combo(items, options.Model, value => { options.Model = value; options.CustomModel = false; Save(); RefreshAgents(); }, 340);
        AutomationProperties.SetName(picker, $"{adapter.Name} model");
        var modelNote = discovery?.Status switch
        {
            ModelDiscoveryStatus.Ok => $"{discovery.Models.Count} models reported by {discovery.Source}, updated {Kit.Ago(discovery.RetrievedAt)}.",
            null => "Models are loaded when the agent is ready. Press Refresh models to load them now.",
            _ => discovery.Message,
        };
        if (adapter is OllamaAdapter) modelNote += " Models marked 'Ollama cloud' run on Ollama's servers, not on this computer.";
        column.Children.Add(Kit.SettingRow("Model", modelNote, picker));

        var custom = new TextBox { Text = options.CustomModel ? options.Model : "", PlaceholderText = "Exact model ID", MinWidth = 240 };
        AutomationProperties.SetName(custom, $"{adapter.Name} custom model ID");
        var advanced = Kit.Column(6,
            Kit.SettingRow("Custom model ID", "For models the agent does not list. AGEX sends the ID as typed and never replaces it.",
                Kit.Row(6, custom, Kit.Button("Use", () =>
                {
                    var value = (custom.Text ?? "").Trim();
                    if (value.Length > 0 && !ModelName.IsValid(value)) { Window.Toast("Model ID not valid", "Use letters, digits and . _ : / - only.", ToastKind.Error); return; }
                    options.Model = value;
                    options.CustomModel = value.Length > 0;
                    Save();
                    RefreshAgents();
                }, "subtle"))));
        if (adapter is CodexAdapter or AntigravityAdapter)
        {
            var efforts = adapter is CodexAdapter ? new[] { "", "minimal", "low", "medium", "high", "xhigh" } : ["", "low", "medium", "high"];
            column.Children.Add(Kit.SettingRow("Reasoning effort", "Higher is slower and uses more quota.", Kit.Combo(efforts.Select(effort => (effort, effort.Length == 0 ? "Default" : effort)), options.Effort, value => { options.Effort = value; Save(); }, 160)));
        }
        if (adapter.CanWriteFiles)
        {
            var toggle = new ToggleSwitch { IsChecked = options.AllowWrites, OnContent = "Yes", OffContent = "Read-only" };
            toggle.IsCheckedChanged += (_, _) => { options.AllowWrites = toggle.IsChecked == true; Save(); };
            column.Children.Add(Kit.SettingRow("Can change files", adapter is CodexAdapter ? "Uses Codex's workspace-write sandbox (edits inside the project only)." : "Off = this agent only reads and advises.", toggle));
        }
        column.Children.Add(new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        return column;
    }

    public static string CapabilityText(Capability capability) => capability switch
    {
        Capability.ReadFiles => "Reads files",
        Capability.WriteFiles => "Edits files",
        Capability.RunCommands => "Runs commands",
        Capability.WebResearch => "Web research",
        Capability.Browser => "Browser",
        Capability.CodeReview => "Code review",
        Capability.Testing => "Testing",
        Capability.Planning => "Planning",
        Capability.Debugging => "Debugging",
        Capability.Mcp => "MCP tools",
        Capability.Images => "Images",
        Capability.Documents => "Documents",
        Capability.Skills => "Skills",
        _ => capability.ToString(),
    };

    private void RefreshRouting()
    {
        _routing.Children.Clear();
        var group = Guid.NewGuid().ToString("N");
        var presets = Kit.Column(6);
        foreach (var preset in Enum.GetValues<RoutingPreset>())
        {
            var radio = new RadioButton { GroupName = group, IsChecked = Workspace.Settings.Routing == preset, Content = Kit.Column(0, Kit.Text(Router.Title(preset), "body"), Kit.Text(Router.Describe(preset), "caption")) };
            var value = preset;
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) { Workspace.Settings.Routing = value; Workspace.SaveSettings(); } };
            AutomationProperties.SetName(radio, Router.Title(preset));
            presets.Children.Add(radio);
        }
        var leaders = new List<(string, string)> { ("auto", "Automatic") };
        leaders.AddRange(Workspace.Core.Registry.Adapters.Where(adapter => adapter.Capabilities.Contains(Capability.Planning)).Select(adapter => (adapter.Id, adapter.Name)));
        var leader = Kit.SettingRow("Leader", "The agent that plans and reviews. Automatic picks a suitable enabled agent.", Kit.Combo(leaders, Workspace.Settings.Leader, value => { Workspace.Settings.Leader = value; Workspace.SaveSettings(); }));

        var advanced = Kit.Column(6);
        foreach (var adapter in Workspace.Core.Registry.Adapters)
        {
            var id = adapter.Id;
            var share = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 5, Value = Workspace.Settings.CustomShares.GetValueOrDefault(id), Width = 140, FormatString = "0" };
            share.ValueChanged += (_, _) => { Workspace.Settings.CustomShares[id] = (int)(share.Value ?? 0); Workspace.SaveSettings(); };
            advanced.Children.Add(Kit.SettingRow($"{adapter.Name} share (%)", "Used by the Custom preset.", share));
        }
        var order = new TextBox { Text = string.Join(", ", Workspace.Settings.QualityOrder), MinWidth = 320 };
        order.LostFocus += (_, _) => { Workspace.Settings.QualityOrder = (order.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); Workspace.SaveSettings(); };
        advanced.Children.Add(Kit.SettingRow("Best-quality order", "Agent ids, best first. This is your preference; AGEX does not rank agents itself.", order));
        var parallel = new NumericUpDown { Minimum = 1, Maximum = 8, Value = Workspace.Settings.MaxParallelTasks, Width = 140, FormatString = "0" };
        parallel.ValueChanged += (_, _) => { Workspace.Settings.MaxParallelTasks = (int)(parallel.Value ?? 3); Workspace.SaveSettings(); };
        advanced.Children.Add(Kit.SettingRow("Tasks at the same time", "How many tasks may run in parallel.", parallel));
        var timeout = new NumericUpDown { Minimum = 2, Maximum = 120, Value = Workspace.Settings.AgentTimeoutMinutes, Width = 140, FormatString = "0" };
        timeout.ValueChanged += (_, _) => { Workspace.Settings.AgentTimeoutMinutes = (int)(timeout.Value ?? 15); Workspace.SaveSettings(); };
        advanced.Children.Add(Kit.SettingRow("Time limit per agent step (minutes)", "An agent that runs longer is stopped.", timeout));

        _routing.Children.Add(Kit.Card(Kit.Column(10, presets, Kit.Divider(), leader, new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch })));
    }

    private void RefreshTeams()
    {
        _teams.Children.Clear();
        foreach (var team in Workspace.Settings.Teams)
        {
            var available = team.Agents.Count(id => Workspace.Scan?.Items.FirstOrDefault(item => item.Id == id)?.Status == AgentStatus.Supported);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(Kit.Column(4,
                Kit.Row(8, Kit.Text(team.Name, "body"), Workspace.Settings.ActiveTeam == team.Id ? Kit.Badge("In use", Tone.Accent) : null, available < team.Agents.Count ? Kit.Badge($"{available} of {team.Agents.Count} ready", Tone.Warning) : Kit.Badge("Ready", Tone.Success)),
                Kit.Row(4, team.Agents.Select(id => (Control?)Kit.Avatar(Workspace.Core.Registry.Get(id)?.Name ?? id, 22)).ToArray()),
                Kit.Text("Leader: " + (team.Leader == "auto" ? "automatic" : Workspace.Core.Registry.Get(team.Leader)?.Name ?? team.Leader), "caption")));
            var captured = team;
            var actions = Kit.Row(4,
                Kit.Button("Use", () => { Workspace.Settings.ActiveTeam = captured.Id; Workspace.SaveSettings(); RefreshTeams(); }, "subtle"),
                Kit.Button("Edit", () => _ = EditTeamAsync(captured), "subtle"),
                Kit.IconButton(Icons.Trash, $"Delete team {team.Name}", () => { Workspace.Settings.Teams.Remove(captured); if (Workspace.Settings.ActiveTeam == captured.Id) Workspace.Settings.ActiveTeam = ""; Workspace.SaveSettings(); RefreshTeams(); }));
            Grid.SetColumn(actions, 1);
            row.Children.Add(actions);
            _teams.Children.Add(Kit.Card(row, 12));
        }
        if (Workspace.Settings.Teams.Count == 0) _teams.Children.Add(Kit.Text("No teams yet.", "small"));
    }

    private async Task EditTeamAsync(AgentTeam? team)
    {
        var name = new TextBox { Text = team?.Name ?? "", PlaceholderText = "Team name" };
        AutomationProperties.SetName(name, "Team name");
        var boxes = Workspace.Core.Registry.Adapters.Select(adapter => new CheckBox { Content = adapter.Name, Tag = adapter.Id, IsChecked = team?.Agents.Contains(adapter.Id) ?? false }).ToList();
        var leaders = new List<(string, string)> { ("auto", "Automatic") };
        leaders.AddRange(Workspace.Core.Registry.Adapters.Select(adapter => (adapter.Id, adapter.Name)));
        var leader = team?.Leader ?? "auto";
        var leaderBox = Kit.Combo(leaders, leader, value => leader = value);
        var body = Kit.Column(10, name, Kit.Text("Agents", "small"), Kit.Column(4, boxes.ToArray<Control?>()), Kit.SettingRow("Leader", null, leaderBox));
        if (await Window.Dialogs.ShowAsync(team is null ? "New team" : "Edit team", body, ["Save", "Cancel"]) != 0) return;
        var agents = boxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag!).ToList();
        if (string.IsNullOrWhiteSpace(name.Text) || agents.Count == 0) { Window.Toast("Team not saved", "Give the team a name and at least one agent.", ToastKind.Info); return; }
        team ??= new AgentTeam { Id = "team-" + Guid.NewGuid().ToString("N")[..8] };
        team.Name = name.Text.Trim();
        team.Agents = agents;
        team.Leader = leader;
        if (!Workspace.Settings.Teams.Contains(team)) Workspace.Settings.Teams.Add(team);
        Workspace.SaveSettings();
        RefreshTeams();
    }

    private void RefreshDetected()
    {
        _detected.Children.Clear();
        var items = Workspace.Scan?.Items.Where(item => !item.HasAdapter && item.Status is not AgentStatus.NotInstalled).OrderBy(item => item.Kind).ThenBy(item => item.Name).ToList() ?? [];
        if (items.Count == 0) { _detected.Children.Add(Kit.Text(Workspace.Scanning ? "Scanning..." : "Nothing else found.", "small")); return; }
        var list = Kit.Column(8);
        foreach (var item in items)
        {
            var (text, tone) = Look(item.Status);
            var kind = item.Kind switch { DiscoveredKind.Ide => "Editor", DiscoveredKind.Tool => "Tool", DiscoveredKind.Integration => "Integration", _ => "Agent" };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(Kit.Column(0, Kit.Row(8, Kit.Text(item.Name, "body"), Kit.Text(kind + (item.Version.Length > 0 ? " · " + item.Version : ""), "caption")), Kit.Text(item.Detail, "caption")));
            var badge = Kit.Badge(item.Status == AgentStatus.Available ? "Available" : text, item.Status == AgentStatus.Available ? Tone.Success : tone);
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            list.Children.Add(row);
        }
        _detected.Children.Add(Kit.Card(list));
    }
}
