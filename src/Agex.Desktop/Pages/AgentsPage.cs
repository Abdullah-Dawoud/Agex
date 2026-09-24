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
    private readonly Dictionary<string, IReadOnlyList<string>> _models = new();
    private Button? _scan;

    public override string Id => "agents";
    public override string Title => "Agents";
    public override string Icon => Icons.Agent;

    protected override Control Build()
    {
        Workspace.ScanChanged += Refresh;
        _scan = Kit.Button("Scan again", () => _ = Workspace.ScanAsync(), "", Icons.Refresh, "Look for newly installed agents and check them");
        var page = Kit.Column(24,
            Kit.PageHeader("Agents", "AGEX coordinates AI agents that are installed on this computer. It never installs, signs in to or changes them.", _scan),
            Kit.Column(0, Kit.SectionHeader("Your agents", "Enable the agents AGEX may use. Status is checked with a short version check only."), _agents),
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
            var (statusText, tone) = Look(status);
            var options = Workspace.Settings.AgentOptions.GetValueOrDefault(adapter.Id) ?? new AgentOptions();
            var enabled = Workspace.Settings.EnabledAgents.Contains(adapter.Id);
            var usable = status is AgentStatus.Supported or AgentStatus.Available or AgentStatus.AuthRequired or AgentStatus.Broken;
            var privacy = adapter.PrivacyFor(options.Model.Length > 0 ? options.Model : null);
            var health = Workspace.Core.Registry.Health(adapter.Id);

            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            top.Children.Add(Kit.Avatar(adapter.Name, 36));
            var title = Kit.Column(2,
                Kit.Row(8, Kit.Text(adapter.Name, "subtitle"), adapter.Stability == AdapterStability.Beta ? Kit.Badge("Beta adapter", Tone.Info) : null, Kit.Badge(statusText, tone)),
                Kit.Text($"{adapter.Provider}{(item?.Version is { Length: > 0 } version ? " · version " + version : "")} · {adapter.Description}", "small"));
            Grid.SetColumn(title, 1);
            top.Children.Add(title);
            var toggle = new ToggleSwitch { IsChecked = enabled, IsEnabled = usable || enabled, OnContent = "Enabled", OffContent = "Off" };
            AutomationProperties.SetName(toggle, $"Enable {adapter.Name}");
            var id = adapter.Id;
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
                PrivacyKind.Cloud => Kit.Badge("Cloud: " + adapter.DataDestination(options.Model), Tone.Neutral, Icons.Cloud),
                _ => Kit.Badge("Data location unknown", Tone.Warning, Icons.Alert),
            };
            var caps = Kit.Text("Can: " + string.Join(", ", adapter.Capabilities.Select(CapabilityText).Select(text => text.ToLowerInvariant())), "caption");
            var detail = Kit.Column(10, top, Kit.Row(8, privacyBadge, adapter.CanWriteFiles ? null : Kit.Badge("Text only - cannot open or edit files", Tone.Info)), caps);
            if (item?.Detail is { Length: > 0 } reason && status is not (AgentStatus.Supported or AgentStatus.Available)) detail.Children.Add(Kit.Text(reason, "small"));
            if (!health.Healthy) detail.Children.Add(Kit.Row(8, Kit.Badge("Paused after errors", Tone.Warning), Kit.Text(health.Reason, "small"), Kit.Button("Try again", () => { Workspace.Core.Registry.ResetHealth(id); Refresh(); }, "link")));
            if (usable) detail.Children.Add(new Expander { Header = "Options", Content = AgentOptionsEditor(adapter, options), HorizontalAlignment = HorizontalAlignment.Stretch });
            if (status == AgentStatus.NotInstalled) detail.Children.Add(Kit.Text(InstallHint(adapter.Id), "small"));
            _agents.Children.Add(Kit.Card(detail));
        }
    }

    private Control AgentOptionsEditor(IAgentAdapter adapter, AgentOptions options)
    {
        var id = adapter.Id;
        void Save() { Workspace.Settings.AgentOptions[id] = options; Workspace.SaveSettings(); }
        var column = Kit.Column(4);
        Control modelControl;
        if (_models.TryGetValue(id, out var models) && models.Count > 0)
        {
            var list = new List<(string, string)> { ("", "Default") };
            list.AddRange(models.Select(model => (model, model + (OllamaAdapter.IsCloudModel(model) ? "  (cloud)" : ""))));
            modelControl = Kit.Combo(list, options.Model, value => { options.Model = value; Save(); RefreshAgents(); }, 260);
        }
        else
        {
            var box = new TextBox { Text = options.Model, PlaceholderText = "Default model", MinWidth = 220 };
            box.LostFocus += (_, _) => { options.Model = (box.Text ?? "").Trim(); Save(); };
            modelControl = Kit.Row(6, box, adapter is OllamaAdapter ? Kit.Button("List models", async () =>
            {
                var detection = Workspace.Core.Registry.DetectionForRun(id);
                _models[id] = await adapter.ListModelsAsync(detection, CancellationToken.None);
                if (_models[id].Count == 0) Window.Toast("No models found", "Start Ollama and download a model first.", ToastKind.Info);
                RefreshAgents();
            }, "subtle") : null);
        }
        column.Children.Add(Kit.SettingRow("Model", adapter is OllamaAdapter ? "Models ending in 'cloud' run on Ollama's servers, not on this computer." : "Leave empty to use the agent's own default.", modelControl));
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
        column.Children.Add(Kit.Button("Check again", async () =>
        {
            Workspace.Core.Registry.ResetHealth(id);
            await Workspace.Core.Registry.CheckHealthAsync(id, CancellationToken.None);
            await Workspace.ScanAsync();
        }, "subtle", Icons.Refresh));
        return column;
    }

    private static string InstallHint(string id) => id switch
    {
        "codex" => "Install Codex CLI from OpenAI (npm install -g @openai/codex), sign in once, then press Scan again.",
        "antigravity" => "Install the Antigravity CLI from Google, sign in once, then press Scan again.",
        "claude-code" => "Install Claude Code from Anthropic, sign in once, then press Scan again.",
        "gemini-cli" => "Install Gemini CLI from Google (npm install -g @google/gemini-cli), sign in once, then press Scan again.",
        "ollama" => "Install Ollama from ollama.com and download a model to work fully offline.",
        _ => "Install it, then press Scan again.",
    };

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
