using Agex.Core.Connections;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>
/// Shared connection UI: cards and the buttons behind every next action, used by
/// the Connections page, the workspace panel and the Home page's team start.
/// </summary>
public sealed class ConnectionUi(MainWindow window)
{
    private Workspace Workspace => window.Workspace;

    /// <summary>State label; "connected" is never used for tools AGEX only exchanges files with.</summary>
    public static (string Text, Tone Tone, string Icon) Look(ConnectionItem item) => item.State == ConnectionState.Connected
        ? item.Method switch
        {
            ConnectionMethod.FilesOnly => ("Ready (works with its files)", Tone.Success, Icons.Check),
            ConnectionMethod.OpenProjectOnly => ("Ready (opens projects)", Tone.Success, Icons.Check),
            ConnectionMethod.Cli => ("Ready", Tone.Success, Icons.Check),
            _ => Look(item.State),
        }
        : Look(item.State);

    public static (string Text, Tone Tone, string Icon) Look(ConnectionState state) => state switch
    {
        ConnectionState.Connected => ("Connected", Tone.Success, Icons.Check),
        ConnectionState.AvailableToConnect => ("Available to connect", Tone.Info, Icons.Plus),
        ConnectionState.InstalledNotConnected => ("Installed, not connected", Tone.Neutral, Icons.Dot),
        ConnectionState.NotInstalled => ("Not installed", Tone.Neutral, Icons.Download),
        ConnectionState.SignInRequired => ("Sign-in required", Tone.Warning, Icons.Lock),
        ConnectionState.DependencyMissing => ("Needs another program", Tone.Warning, Icons.Alert),
        _ => ("Not supported yet", Tone.Neutral, Icons.Close),
    };

    /// <summary>Full card for the Connections page.</summary>
    public Control Card(ConnectionItem item, double width = 360)
    {
        var (text, tone, icon) = Look(item);
        var title = Kit.Text(item.Name, "subtitle");
        title.TextWrapping = TextWrapping.Wrap;
        var detail = Kit.Text(item.Detail, "small");
        detail.TextWrapping = TextWrapping.Wrap;
        var body = Kit.Column(8,
            title,
            Kit.Wrap(Kit.Badge(text, tone, icon), Kit.Badge(ConnectionItem.MethodText(item.Method), Tone.Neutral), item.Cost.Length > 0 ? CostBadge(item.Cost) : null),
            detail,
            item.FreeAlternative.Length > 0 && item.State != ConnectionState.Connected ? Kit.Badge("Free option: " + item.FreeAlternative, Tone.Success) : null,
            Buttons(item, primaryFirst: true));
        var card = Kit.Card(body, 14);
        card.Width = width;
        card.Margin = new Thickness(0, 0, 12, 12);
        AutomationProperties.SetName(card, $"{item.Name}: {text}");
        return card;
    }

    /// <summary>One compact line for the side panel and the team start: state glyph, name, primary action.</summary>
    public Control Row(ConnectionItem item)
    {
        var (text, tone, _) = Look(item);
        var keys = Kit.ToneKeys(tone);
        var name = Kit.Text(item.Name, "body");
        name.TextWrapping = TextWrapping.Wrap;
        var info = Kit.Column(0, name, Kit.Text(text, "caption"));
        ToolTip.SetTip(info, item.Detail);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(Kit.Icon(item.State == ConnectionState.Connected ? Icons.Check : Icons.Dot, 14, item.State == ConnectionState.Connected ? "SuccessBrush" : keys.Foreground));
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        if (item.State != ConnectionState.Connected && item.Actions.FirstOrDefault() is { } action)
        {
            var button = ActionButton(item, action, "subtle");
            Grid.SetColumn(button, 2);
            button.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(button);
        }
        return grid;
    }

    private static Border CostBadge(string cost) =>
        Kit.Badge(cost, cost.StartsWith("Free", StringComparison.OrdinalIgnoreCase) || cost is "LOCAL" or "FREE" ? Tone.Success : cost.Contains("tier", StringComparison.OrdinalIgnoreCase) || cost == "FREE TIER" ? Tone.Info : Tone.Neutral);

    public Control Buttons(ConnectionItem item, bool primaryFirst)
    {
        var row = Kit.Wrap();
        var first = true;
        foreach (var action in item.Actions)
        {
            row.Children.Add(ActionButton(item, action, first && primaryFirst && item.State != ConnectionState.Connected ? "primary" : "subtle"));
            first = false;
        }
        return row;
    }

    private Button ActionButton(ConnectionItem item, ConnectionAction action, string classes)
    {
        var icon = action.Kind switch
        {
            ConnectionActionKind.Download or ConnectionActionKind.InstallDependency or ConnectionActionKind.LearnMore => Icons.External,
            ConnectionActionKind.SignIn or ConnectionActionKind.AddKey => Icons.Lock,
            ConnectionActionKind.Disconnect => Icons.Close,
            ConnectionActionKind.Test => Icons.Refresh,
            ConnectionActionKind.SearchRegistry => Icons.Search,
            _ => (string?)null,
        };
        var button = Kit.Button(action.Label, () => _ = RunAsync(item, action), classes, icon);
        AutomationProperties.SetName(button, $"{action.Label}: {item.Name}");
        return button;
    }

    public async Task RunAsync(ConnectionItem item, ConnectionAction action)
    {
        try
        {
            switch (action.Kind)
            {
                case ConnectionActionKind.Connect:
                    await window.Page<SkillsPage>("skills").InstallByIdAsync(action.Argument);
                    break;
                case ConnectionActionKind.AddKey or ConnectionActionKind.Configure:
                    await window.Page<SkillsPage>("skills").ShowByIdAsync(action.Argument);
                    break;
                case ConnectionActionKind.Test when action.Argument == "gh":
                    await Workspace.CheckGitHubCliAsync();
                    window.Toast("GitHub CLI", Workspace.GhSignedIn == true ? "Signed in." : "Not signed in. Use Sign in.", Workspace.GhSignedIn == true ? ToastKind.Success : ToastKind.Info);
                    break;
                case ConnectionActionKind.Test:
                    await window.Page<SkillsPage>("skills").TestByIdAsync(action.Argument);
                    break;
                case ConnectionActionKind.Disconnect:
                    await window.Page<SkillsPage>("skills").RemoveByIdAsync(action.Argument);
                    break;
                case ConnectionActionKind.Enable:
                    Workspace.Core.Skills.SetEnabled(action.Argument, true);
                    window.Toast(item.Name + " is on", "Agents can use it again.", ToastKind.Success);
                    break;
                case ConnectionActionKind.SignIn when action.Argument == "gh" && Workspace.Core.Platform.FindExecutable("gh") is { } gh:
                    // GitHub's own sign-in in a terminal: AGEX never sees the password or the token.
                    Workspace.Core.Platform.RunInTerminal(gh, ["auth", "login"], Workspace.Project?.Path ?? Workspace.Core.Platform.Paths.DataRoot);
                    window.Toast("Sign in with GitHub", "Finish the steps in the terminal window, then press Check sign-in.", ToastKind.Info);
                    break;
                case ConnectionActionKind.ConnectBridge:
                    await window.Page<TeamsPage>("teams").ConnectBridgeAsync();
                    break;
                case ConnectionActionKind.LearnMore when action.Argument == "bridge":
                    await window.Page<TeamsPage>("teams").BridgeHelpAsync();
                    break;
                case ConnectionActionKind.Download or ConnectionActionKind.LearnMore or ConnectionActionKind.InstallDependency:
                    OpenHttps(action.Argument);
                    break;
                case ConnectionActionKind.UseAsEditor:
                    Workspace.Settings.PreferredEditor = action.Argument;
                    Workspace.SaveSettings();
                    window.Toast($"{item.Name} is your editor", "Files in AGEX now open in it.", ToastKind.Success);
                    break;
                case ConnectionActionKind.OpenProject when action.Argument.Length > 0:
                    if (Workspace.Project is null) { await window.PickProjectAsync(); if (Workspace.Project is null) break; }
                    var location = Workspace.Scan?.Items.FirstOrDefault(scan => scan.Id == action.Argument)?.Location ?? "";
                    if (!Agex.Core.Agents.Editors.Open(Workspace.Core.Platform, action.Argument, location, Workspace.Project!.Path, Workspace.Core.Log))
                        window.Toast($"Could not open {item.Name}", "Start it yourself and open the project folder from there.", ToastKind.Error);
                    break;
                case ConnectionActionKind.OpenProject:
                    await window.PickProjectAsync();
                    break;
                case ConnectionActionKind.SearchRegistry:
                    await new AddConnectionDialog(window, this).ShowAsync(action.Argument, searchRegistry: true);
                    break;
                case ConnectionActionKind.OpenAgents:
                    window.Navigate("agents");
                    break;
                case ConnectionActionKind.UseTeam:
                    Workspace.Settings.ActiveJobTeam = action.Argument;
                    Workspace.SaveSettings();
                    window.Navigate("home");
                    break;
            }
        }
        finally
        {
            Workspace.NotifyConnectionsChanged();
        }
    }

    public void OpenHttps(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) Workspace.Core.Platform.OpenUrl(uri);
    }
}

/// <summary>
/// "Add connection": search what AGEX knows, then (only if the user asks) the
/// public MCP Registry. Registry servers are unreviewed: AGEX says so, shows
/// exactly what will run, asks for any key, and starts them with "ask each time".
/// </summary>
public sealed class AddConnectionDialog(MainWindow window, ConnectionUi ui)
{
    private Workspace Workspace => window.Workspace;

    public async Task ShowAsync(string query = "", bool searchRegistry = false)
    {
        var box = new TextBox { Text = query, PlaceholderText = "Search: Revit, Notion, GitHub, browser, database...", MinWidth = 420 };
        AutomationProperties.SetName(box, "Search connections");
        var results = Kit.Column(8);
        var registry = Kit.Column(8);
        var registryButton = Kit.Button("Search the public MCP Registry", () => _ = SearchRegistryAsync(box.Text ?? "", registry), "", Icons.Search,
            "Community servers published by anyone. AGEX has not reviewed them.");
        void Refresh()
        {
            results.Children.Clear();
            var text = (box.Text ?? "").Trim();
            var all = Workspace.Connections();
            var matches = text.Length == 0 ? all.Where(item => item.State != ConnectionState.Connected).Take(8).ToList()
                : all.Where(item => item.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || item.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || item.Keywords.Any(keyword => keyword.Contains(text, StringComparison.OrdinalIgnoreCase))).Take(12).ToList();
            if (matches.Count == 0) results.Children.Add(Kit.Text($"AGEX has no reviewed connection for \"{text}\". Search the public MCP Registry below, or add your own server under Skills > Add your own.", "small"));
            foreach (var item in matches)
            {
                var (state, tone, icon) = ConnectionUi.Look(item);
                var detail = Kit.Text(item.Detail, "caption");
                detail.TextWrapping = TextWrapping.Wrap;
                results.Children.Add(Kit.Panel(new Border
                {
                    Padding = new Thickness(10, 8),
                    Child = Kit.Column(4, Kit.Row(8, Kit.Text(item.Name, "body"), Kit.Badge(state, tone, icon), Kit.Text(ConnectionItem.MethodText(item.Method), "caption")), detail, ui.Buttons(item, primaryFirst: true)),
                }));
            }
            foreach (var text2 in results.Children.OfType<TextBlock>()) text2.TextWrapping = TextWrapping.Wrap;
        }
        box.TextChanged += (_, _) => { Refresh(); registry.Children.Clear(); };
        Refresh();
        if (searchRegistry && query.Length > 0) _ = SearchRegistryAsync(query, registry);
        var body = Kit.Column(10,
            Kit.Text("Find a program, service or tool. AGEX explains how it can connect and walks you through it.", "small"),
            box,
            new ScrollViewer { Content = Kit.Column(10, results, Kit.Divider(), registryButton, registry), MaxHeight = 280 });
        await window.Dialogs.ShowAsync("Add connection", body, ["Close"], maxWidth: 720);
    }

    private async Task SearchRegistryAsync(string query, StackPanel panel)
    {
        panel.Children.Clear();
        if (string.IsNullOrWhiteSpace(query)) { panel.Children.Add(Kit.Text("Type what you are looking for first.", "small")); return; }
        panel.Children.Add(Kit.Text("Searching the MCP Registry...", "caption"));
        var (servers, error) = await Workspace.Core.McpRegistry.SearchAsync(query, CancellationToken.None);
        panel.Children.Clear();
        var warning = Kit.Badge("Community servers from the public MCP Registry: anyone can publish there and AGEX has not reviewed them", Tone.Warning, Icons.Alert);
        panel.Children.Add(warning);
        if (error.Length > 0) { panel.Children.Add(Kit.Text(error, "small")); return; }
        if (servers.Count == 0) { panel.Children.Add(Kit.Text("No servers found.", "small")); return; }
        foreach (var server in servers.Take(15))
        {
            var description = Kit.Text(server.Description, "caption");
            description.TextWrapping = TextWrapping.Wrap;
            description.MaxLines = 3;
            var what = server.Unsupported.Length > 0 ? server.Unsupported : server.Transport == "http" ? "Hosted at " + server.Url : "Runs " + server.Command + " " + string.Join(' ', server.Args);
            var whatText = Kit.Text(what, "caption");
            whatText.TextWrapping = TextWrapping.Wrap;
            var captured = server;
            panel.Children.Add(Kit.Panel(new Border
            {
                Padding = new Thickness(10, 8),
                Child = Kit.Column(4,
                    Kit.Row(8, Kit.Text(server.DisplayName, "body"), Kit.Text(server.Version, "caption")),
                    Kit.Text(server.Name, "caption"), description, whatText,
                    Kit.Wrap(
                        server.Unsupported.Length == 0 ? Kit.Button("Add", () => _ = AddAsync(captured), "", Icons.Plus) : null,
                        server.Repository.Length > 0 ? Kit.Button("Source", () => ui.OpenHttps(captured.Repository), "subtle", Icons.External) : null)),
            }));
        }
    }

    private async Task AddAsync(RegistryServer server)
    {
        // The dialog host shows one dialog at a time: collect the settings inline in a second step.
        var fields = new List<(RegistrySetting Setting, TextBox Box)>();
        var list = Kit.Column(8);
        foreach (var setting in server.Settings.Where(setting => setting.Required || setting.Secret))
        {
            var box = new TextBox { PlaceholderText = setting.Description.Length > 0 ? setting.Description : setting.Name, MinWidth = 320 };
            if (setting.Secret) box.PasswordChar = '•';
            AutomationProperties.SetName(box, setting.Name);
            list.Children.Add(Kit.Column(2, Kit.Text(setting.Name + (setting.Secret ? " (kept in your system key store)" : ""), "small"), box));
            fields.Add((setting, box));
        }
        var intro = Kit.Text($"{server.DisplayName} is a community server that AGEX has not reviewed. It will run {(server.Transport == "http" ? "at " + server.Url : "as: " + server.Command + " " + string.Join(' ', server.Args))} whenever an agent uses it. AGEX will ask you before each use.", "body");
        intro.TextWrapping = TextWrapping.Wrap;
        window.Dialogs.Close(-1);
        if (await window.Dialogs.ShowAsync($"Add {server.DisplayName}?", Kit.Column(10, intro, list), ["Add", "Cancel"]) != 0) return;
        try
        {
            var values = fields.Where(field => !string.IsNullOrWhiteSpace(field.Box.Text)).ToDictionary(field => field.Setting.Name, field => field.Box.Text!.Trim());
            var description = $"From the public MCP Registry ({server.Name} {server.Version}). Not reviewed by AGEX.";
            if (server.Transport == "http")
                Workspace.Core.Skills.AddRemoteMcpServer(server.DisplayName, server.Url, values.Values.FirstOrDefault(), description);
            else
                Workspace.Core.Skills.AddMcpServer(server.DisplayName, server.Command, server.Args, values, description: description);
            window.Toast($"{server.DisplayName} added", "It appears under Connections > Your MCP servers. AGEX asks before each use.", ToastKind.Success);
            Workspace.NotifyConnectionsChanged();
        }
        catch (Agex.Core.Skills.SkillException ex) { await window.Dialogs.MessageAsync("Not added", ex.Message); }
    }
}
