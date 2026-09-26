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
    private readonly StackPanel _providers = new() { Spacing = 8 };
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
            Kit.Column(0, Kit.SectionHeader("Routing & Providers", "Optional. Point Codex at a local model server or a router you choose. Nothing is routed through a third party unless you add it here and select it for Codex.",
                Kit.Button("Add custom endpoint", () => _ = AddCustomProviderAsync(), "", Icons.Plus)), _providers),
            Kit.Column(0, Kit.SectionHeader("Other tools on this computer", "Editors you can open projects in, and agents AGEX does not drive yet."), _detected));
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
        RefreshProviders();
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
            if (installed) detail.Children.Add(UsageRow(adapter));
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
                + (adapter is OllamaAdapter && model.Location != PrivacyKind.Local ? " · Ollama cloud" : "")
                + (model.Facts.Length > 0 ? " · " + model.Facts : ""))));
        if (options.Model.Length > 0 && items.All(item => item.Item1 != options.Model)) items.Add((options.Model, options.Model + (options.CustomModel ? " (custom)" : "")));
        var picker = Kit.Combo(items, options.Model, value => { options.Model = value; options.CustomModel = false; Save(); RefreshAgents(); }, 340);
        picker.MaxWidth = 460;
        AutomationProperties.SetName(picker, $"{adapter.Name} model");
        var modelNote = discovery?.Status switch
        {
            ModelDiscoveryStatus.Ok => $"{discovery.Models.Count} models reported by {discovery.Source}, updated {Kit.Ago(discovery.RetrievedAt)}.",
            null => "Models are loaded when the agent is ready. Press Refresh models to load them now.",
            _ => discovery.Message,
        };
        if (adapter is OllamaAdapter) modelNote += " Models marked 'Ollama cloud' run on Ollama's servers, not on this computer.";
        column.Children.Add(Kit.SettingRow("Model", modelNote, Kit.Row(6, picker, Kit.IconButton(Icons.Refresh, "Refresh models", () => _ = Workspace.RefreshModelsAsync(id)))));
        var support = adapter.ModelSettings;
        if (support.SupportsCustomEndpoint && adapter is CodexAdapter)
        {
            var providers = new List<(string, string)> { ("", "Codex default (your ChatGPT or OpenAI sign-in)") };
            providers.AddRange(Workspace.Settings.Providers.Select(provider => (provider.Id, $"{provider.Name} · {(provider.Local ? "on this computer" : "cloud")}")));
            var providerPicker = Kit.Combo(providers, options.ProviderId ?? "", value =>
            {
                options.ProviderId = value;
                options.Model = "";
                options.CustomModel = false;
                Save();
                _ = Workspace.RefreshModelsAsync(id);
                RefreshAgents();
            }, 340);
            AutomationProperties.SetName(providerPicker, "Codex model provider");
            column.Children.Add(Kit.SettingRow("Model provider", "Where Codex sends requests. Add local servers or routers under Routing & Providers. Codex needs models that support the Responses API and reasoning (for Ollama, for example qwen3).", providerPicker));
        }

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
        // Only the settings this agent actually accepts (see ModelSettingsSupport); nothing is shown that it would ignore.
        if (support.SupportsReasoningEffort)
        {
            var reported = discovery?.Models.FirstOrDefault(model => model.Id == options.Model)?.Efforts ?? [];
            var efforts = reported.Count > 0 ? support.ReasoningEfforts.Where(reported.Contains).ToList() : support.ReasoningEfforts.ToList();
            if (options.Effort.Length > 0 && !efforts.Contains(options.Effort)) efforts.Add(options.Effort);
            column.Children.Add(Kit.SettingRow("Reasoning effort", reported.Count > 0 ? "Levels this model reports. Higher is slower and uses more quota." : "Higher is slower and uses more quota.",
                Kit.Combo(new[] { "" }.Concat(efforts).Select(effort => (effort, effort.Length == 0 ? "Default" : effort)), options.Effort, value => { options.Effort = value; Save(); }, 160)));
        }
        if (support.SupportsTemperature)
        {
            var temperature = new NumericUpDown { Minimum = 0, Maximum = 2, Increment = 0.1m, FormatString = "0.0", Value = options.Temperature is { } t ? (decimal)t : null, PlaceholderText = "Model default", MinWidth = 140 };
            AutomationProperties.SetName(temperature, $"{adapter.Name} temperature");
            temperature.ValueChanged += (_, e) => { options.Temperature = e.NewValue is { } value ? (double)value : null; Save(); };
            column.Children.Add(Kit.SettingRow("Temperature", "Lower is more predictable. Empty = the model's default.", temperature));
        }
        if (support.SupportsContextWindowSelection)
        {
            var sizes = new List<(int, string)> { (0, "Model default"), (4096, "4k"), (8192, "8k"), (16384, "16k"), (32768, "32k"), (65536, "64k"), (131072, "128k") };
            column.Children.Add(Kit.SettingRow("Context window", "Larger lets the model read more at once but needs more memory on this computer.",
                Kit.Combo(sizes, options.ContextWindow ?? 0, value => { options.ContextWindow = value == 0 ? null : value; Save(); }, 160)));
        }
        if (adapter.CanWriteFiles)
        {
            var toggle = new ToggleSwitch { IsChecked = options.AllowWrites, OnContent = "Yes", OffContent = "Read-only" };
            toggle.IsCheckedChanged += (_, _) => { options.AllowWrites = toggle.IsChecked == true; Save(); };
            column.Children.Add(Kit.SettingRow("Can change files", adapter is CodexAdapter ? "Uses Codex's workspace-write sandbox (edits inside the project only)." : "Off = this agent only reads and advises.", toggle));
        }
        var supported = new List<string> { "model" };
        if (support.SupportsReasoningEffort) supported.Add("reasoning effort (" + string.Join(", ", support.ReasoningEfforts) + ")");
        if (support.SupportsTemperature) supported.Add("temperature");
        if (support.SupportsContextWindowSelection) supported.Add("context window");
        if (support.SupportsCustomEndpoint) supported.Add("another model endpoint");
        if (support.SupportsVision) supported.Add("images");
        advanced.Children.Add(Kit.Text($"Settings {adapter.Name} accepts: {string.Join(", ", supported)}. Source: {support.Source}", "caption"));
        column.Children.Add(new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        return column;
    }

    /// <summary>Account usage or quota, only as the agent reports it.</summary>
    private Control UsageRow(IAgentAdapter adapter)
    {
        var id = adapter.Id;
        Control value;
        if (adapter is OllamaAdapter) value = Kit.Text("No quota: local models run on this computer. Ollama cloud models are not reported here.", "small");
        else if (adapter is IAccountUsageSource)
        {
            var usage = Workspace.AccountUsage.GetValueOrDefault(id);
            var refresh = Kit.Button(usage is null ? "Check usage" : "Refresh", () => _ = RefreshUsageAsync(id), "link", Icons.Refresh, "Asks the agent for its usage limits. Uses no model quota.");
            if (usage is null) value = Kit.Row(8, Kit.Text("Not checked yet.", "small"), refresh);
            else if (!usage.Reported) value = Kit.Row(8, Kit.Text(usage.Message, "small"), refresh);
            else
            {
                var lines = Kit.Column(4);
                foreach (var window in usage.Windows)
                {
                    var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = window.UsedPercent, Width = 140, Height = 6, [AutomationProperties.NameProperty] = $"{window.Label}: {window.UsedPercent:0}% used" };
                    lines.Children.Add(Kit.Row(8, Kit.Text(window.Label, "small"), bar, Kit.Text($"{window.UsedPercent:0}% used, {100 - window.UsedPercent:0}% left" + (window.ResetsAt is { } reset ? $" · resets {reset.ToLocalTime():ddd HH:mm}" : ""), "small")));
                }
                lines.Children.Add(Kit.Row(8, Kit.Text((usage.Plan.Length > 0 ? "Plan: " + usage.Plan + " · " : "") + $"Source: {usage.Source} · checked {Kit.Ago(usage.RefreshedAt)}", "caption"), refresh));
                value = lines;
            }
        }
        else value = Kit.Text("Usage not reported by this agent.", "small");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        grid.Children.Add(Kit.Text("Usage / quota", "small"));
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private async Task RefreshUsageAsync(string id)
    {
        await Workspace.RefreshAccountUsageAsync(id);
        Refresh();
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

    // ----------------------------------------------------------- providers

    private void RefreshProviders()
    {
        _providers.Children.Clear();
        var added = Workspace.Settings.Providers;
        var list = Kit.Column(10);
        foreach (var provider in added) list.Children.Add(ProviderRow(provider, isAdded: true));
        var available = ProviderPresets.All.Where(preset => added.All(item => item.Id != preset.Id)).ToList();
        if (available.Count > 0)
        {
            if (added.Count > 0) list.Children.Add(Kit.Divider());
            list.Children.Add(Kit.Text("Available to add", "caption"));
            foreach (var preset in available) list.Children.Add(ProviderRow(preset, isAdded: false));
        }
        _providers.Children.Add(Kit.Card(list));
    }

    private static Border CostBadge(ProviderProfile provider) => provider.Cost switch
    {
        CostLabel.Local => Kit.Badge("LOCAL", Tone.Success, Icons.Computer),
        CostLabel.Free => Kit.Badge("FREE", Tone.Success),
        CostLabel.FreeTier => Kit.Badge("FREE TIER", Tone.Info),
        CostLabel.Paid => Kit.Badge("PAID", Tone.Neutral),
        _ => Kit.Badge("Cost depends on connected providers", Tone.Neutral),
    };

    private Control ProviderRow(ProviderProfile provider, bool isAdded)
    {
        var hasKey = Workspace.Core.Platform.SecureStore.Get(ProviderService.SecretKey(provider.Id)) is { Length: > 0 };
        var usedBy = Workspace.Settings.AgentOptions.Where(pair => pair.Value.ProviderId == provider.Id).Select(pair => Workspace.Core.Registry.Get(pair.Key)?.Name ?? pair.Key).ToList();
        var badges = Kit.Wrap(CostBadge(provider),
            provider.Local ? Kit.Badge("Stays on this computer", Tone.Success, Icons.Computer) : Kit.Badge("Requests leave this computer", Tone.Neutral, Icons.Cloud),
            provider.NeedsKey ? Kit.Badge(hasKey ? "API key saved" : "API KEY REQUIRED", hasKey ? Tone.Success : Tone.Warning, Icons.Lock) : null,
            usedBy.Count > 0 ? Kit.Badge("Used by " + string.Join(", ", usedBy), Tone.Accent) : null);
        var info = Kit.Column(4,
            Kit.Text(provider.Name, "body"),
            badges,
            Kit.Text(provider.Notes, "small"),
            Kit.Text(provider.BaseUrl + (provider.Homepage.Length > 0 ? "  ·  " + provider.Homepage : ""), "caption"));
        foreach (var text in info.Children.OfType<TextBlock>()) text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var actions = isAdded
            ? Kit.Column(6,
                Kit.Button("List models", () => _ = ListProviderModelsAsync(provider), "", Icons.Search),
                provider.NeedsKey || !provider.Local ? Kit.Button(hasKey ? "Change API key" : "Add API key", () => _ = SetProviderKeyAsync(provider), provider.NeedsKey && !hasKey ? "primary" : "", Icons.Lock) : null,
                Kit.Button("Remove", () => _ = RemoveProviderAsync(provider), "subtle", Icons.Trash))
            : Kit.Column(6,
                Kit.Button("Add", () => { Workspace.Settings.Providers.Add(Clone(provider)); Workspace.SaveSettings(); Refresh(); Window.Toast($"{provider.Name} added", "Choose it for Codex under Codex > Settings > Model provider.", ToastKind.Success); }, "primary", Icons.Plus),
                provider.Homepage.Length > 0 ? Kit.Button("Learn more", () => OpenHttps(provider.Homepage), "subtle", Icons.External) : null);
        foreach (var button in actions.Children.OfType<Button>()) button.HorizontalAlignment = HorizontalAlignment.Stretch;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        row.Children.Add(info);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private static ProviderProfile Clone(ProviderProfile preset) => new()
    {
        Id = preset.Id, Name = preset.Name, BaseUrl = preset.BaseUrl, NeedsKey = preset.NeedsKey, Local = preset.Local, Cost = preset.Cost, Notes = preset.Notes, Homepage = preset.Homepage,
    };

    private void OpenHttps(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) Workspace.Core.Platform.OpenUrl(uri);
    }

    private async Task ListProviderModelsAsync(ProviderProfile provider)
    {
        var result = await Workspace.Core.Providers.ListModelsAsync(provider, CancellationToken.None);
        if (result.Status != ModelDiscoveryStatus.Ok) { await Window.Dialogs.MessageAsync($"Models from {provider.Name}", result.Message); return; }
        var lines = result.Models.Take(40).Select(model => model.Id + (model.Facts.Length > 0 ? "  ·  " + model.Facts : "")).ToList();
        if (result.Models.Count > 40) lines.Add($"... and {result.Models.Count - 40} more");
        await Window.Dialogs.MessageAsync($"{result.Models.Count} models from {provider.Name}", string.Join("\n", lines));
    }

    private async Task SetProviderKeyAsync(ProviderProfile provider)
    {
        var box = new TextBox { PasswordChar = '•', PlaceholderText = "API key", MinWidth = 320 };
        AutomationProperties.SetName(box, $"{provider.Name} API key");
        var intro = Kit.Text($"Paste the API key you created in your {provider.Name} account. It is stored in {Workspace.Core.Platform.SecureStore.Mechanism}, passed to Codex only while it runs, and never shown or logged.", "body");
        intro.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        if (await Window.Dialogs.ShowAsync($"{provider.Name} API key", Kit.Column(8, intro, box), ["Save", "Cancel"]) != 0 || string.IsNullOrWhiteSpace(box.Text)) return;
        Workspace.Core.Platform.SecureStore.Set(ProviderService.SecretKey(provider.Id), box.Text.Trim());
        Window.Toast("API key saved", $"{provider.Name} can now be used.", ToastKind.Success);
        Refresh();
    }

    private async Task RemoveProviderAsync(ProviderProfile provider)
    {
        if (!await Window.ConfirmAsync($"Remove {provider.Name}?", "Agents using it go back to their own default service. A saved API key is deleted.", "Remove", "Cancel")) return;
        Workspace.Settings.Providers.RemoveAll(item => item.Id == provider.Id);
        foreach (var options in Workspace.Settings.AgentOptions.Values.Where(options => options.ProviderId == provider.Id)) { options.ProviderId = ""; options.Model = ""; options.CustomModel = false; }
        Workspace.Core.Platform.SecureStore.Delete(ProviderService.SecretKey(provider.Id));
        Workspace.SaveSettings();
        Refresh();
    }

    private async Task AddCustomProviderAsync()
    {
        var name = new TextBox { PlaceholderText = "Name, for example My LM server", MinWidth = 320 };
        var url = new TextBox { PlaceholderText = "https://example.com/v1 or http://127.0.0.1:8080/v1", MinWidth = 320 };
        var local = new CheckBox { Content = "It runs on this computer" };
        var key = new CheckBox { Content = "It needs an API key" };
        AutomationProperties.SetName(name, "Provider name");
        AutomationProperties.SetName(url, "Base URL");
        var intro = Kit.Text("Any OpenAI-compatible endpoint that supports the Responses API. Use https for services on the internet; plain http is allowed only for this computer.", "small");
        intro.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        if (await Window.Dialogs.ShowAsync("Add a custom endpoint", Kit.Column(8, intro, name, url, local, key), ["Add", "Cancel"]) != 0) return;
        var baseUrl = (url.Text ?? "").Trim().TrimEnd('/');
        var displayName = (name.Text ?? "").Trim();
        if (displayName.Length == 0 || !ProviderService.IsValidBaseUrl(baseUrl, local.IsChecked == true))
        {
            await Window.Dialogs.MessageAsync("Not added", "Enter a name and a valid address (https, or http only for this computer).");
            return;
        }
        var id = "custom-" + new string(displayName.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if (Workspace.Settings.Providers.Any(item => item.Id == id)) id += "-" + Guid.NewGuid().ToString("N")[..4];
        Workspace.Settings.Providers.Add(new ProviderProfile { Id = id, Name = displayName, BaseUrl = baseUrl, Local = local.IsChecked == true, NeedsKey = key.IsChecked == true, Cost = local.IsChecked == true ? CostLabel.Local : CostLabel.Unknown, Notes = "Added by you." });
        Workspace.SaveSettings();
        Refresh();
    }

    // -------------------------------------------------------- other tools

    private static readonly Dictionary<string, (string LearnMore, string Why)> NotYetIntegrated = new()
    {
        ["copilot-cli"] = ("https://github.com/github/copilot-cli", "Evaluated: its non-interactive mode prints plain text only, so AGEX cannot tell tool steps and results apart reliably yet."),
        ["aider"] = ("https://aider.chat", "Evaluated: runs with --message, but reports plain text and edits through its own git commits; no structured result for AGEX to verify yet."),
        ["cursor-agent"] = ("https://cursor.com/cli", "Not evaluated in depth yet."),
        ["qwen-code"] = ("https://github.com/QwenLM/qwen-code", "Not evaluated in depth yet."),
    };

    private void OpenInEditor(DiscoveredItem item)
    {
        if (Workspace.Project is not { } project) { Window.Toast("Open a project first", "Choose a project folder, then open it in the editor.", ToastKind.Info); return; }
        if (!Editors.Open(Workspace.Core.Platform, item.Id, item.Location, project.Path, Workspace.Core.Log))
            Window.Toast($"Could not open {item.Name}", "Start the editor yourself and open the project folder from there.", ToastKind.Error);
    }

    private void RefreshDetected()
    {
        _detected.Children.Clear();
        var items = Workspace.Scan?.Items.Where(item => !item.HasAdapter && item.Status is not AgentStatus.NotInstalled).OrderBy(item => item.Kind).ThenBy(item => item.Name).ToList() ?? [];
        if (items.Count == 0) { _detected.Children.Add(Kit.Text(Workspace.Scanning ? "Scanning..." : "Nothing else found.", "small")); return; }
        var list = Kit.Column(8);
        foreach (var item in items)
        {
            var kind = item.Kind switch { DiscoveredKind.Ide => "Editor", DiscoveredKind.Tool => "Tool", DiscoveredKind.Integration => "Integration", _ => "Agent" };
            var captured = item;
            var editor = item.Kind == DiscoveredKind.Ide && Editors.IsEditor(item.Id) && item.Status == AgentStatus.DetectedUnsupported;
            var launchable = editor && Editors.Launchable(Workspace.Core.Platform.Os, item.Location) is not null;
            var preferred = Workspace.Settings.PreferredEditor == item.Id;
            var notYet = item.Kind == DiscoveredKind.Agent && NotYetIntegrated.ContainsKey(item.Id);
            var (text, tone, detail) = editor ? (preferred ? "Preferred editor" : "Editor", preferred ? Tone.Accent : Tone.Info, "AGEX opens your project in it. AGEX does not control the editor.")
                : notYet ? ("Not supported yet", Tone.Neutral, NotYetIntegrated[item.Id].Why)
                : item.Status == AgentStatus.Available ? ("Available", Tone.Success, item.Detail)
                : (Look(item.Status).Item1, Look(item.Status).Item2, item.Detail);
            var actions = Kit.Row(6);
            if (editor)
            {
                if (launchable) actions.Children.Add(Kit.Button("Open project", () => OpenInEditor(captured), "primary", Icons.External, $"Open the current project in {item.Name}"));
                actions.Children.Add(preferred
                    ? Kit.Button("Stop using as preferred", () => { Workspace.Settings.PreferredEditor = ""; Workspace.SaveSettings(); Refresh(); }, "subtle")
                    : Kit.Button("Use as preferred editor", () => { Workspace.Settings.PreferredEditor = captured.Id; Workspace.SaveSettings(); Refresh(); Window.Toast($"{captured.Name} is your editor", "Files in the workspace panel now have 'Open in editor'.", ToastKind.Success); }, "", Icons.Check));
            }
            else if (notYet)
            {
                var learnMore = NotYetIntegrated[item.Id].LearnMore;
                actions.Children.Add(Kit.Button("Learn more", () => OpenHttps(learnMore), "subtle", Icons.External));
                actions.Children.Add(Kit.Button("Request integration", () => OpenHttps("https://github.com/Abdullah-Dawoud/Agex/issues/new?title=" + Uri.EscapeDataString($"Integration request: {captured.Name}")), "subtle", Icons.External, "Opens a new GitHub issue in your browser; nothing is sent until you submit it there"));
            }
            var detailText = Kit.Text(detail, "caption");
            detailText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            var infoColumn = Kit.Column(2, Kit.Row(8, Kit.Text(item.Name, "body"), Kit.Text(kind + (item.Version.Length > 0 ? " · " + item.Version : ""), "caption"), Kit.Badge(text, tone)), detailText);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(infoColumn);
            Grid.SetColumn(actions, 1);
            actions.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(actions);
            list.Children.Add(row);
        }
        _detected.Children.Add(Kit.Card(list));
    }
}
