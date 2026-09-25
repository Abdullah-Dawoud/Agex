using Agex.Core.Connections;
using Agex.Core.Teams;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>
/// One place for everything AGEX can work with: agents' tools, programs on this
/// computer, web services and MCP servers, each with its state and next step.
/// MCP servers already set up in other AI tools are found and can be imported.
/// </summary>
public sealed class ConnectionsPage(MainWindow window) : AppPage(window)
{
    private readonly ContentControl _body = new();
    private string _filter = "All";
    private ConnectionUi? _ui;

    public override string Id => "connections";
    public override string Title => "Connections";
    public override string Icon => Icons.Plug;

    private ConnectionUi Ui => _ui ??= new ConnectionUi(Window);

    protected override Control Build()
    {
        Workspace.ConnectionsChanged += Refresh;
        Workspace.ScanChanged += Refresh;
        Workspace.SettingsChanged += Refresh;
        Refresh();
        return Kit.Page(_body, 1240);
    }

    public override void OnShown()
    {
        Refresh();
        if (Workspace.GhSignedIn is null) _ = Workspace.CheckGitHubCliAsync();
    }

    private void Refresh()
    {
        var all = Workspace.Connections();
        var team = JobTeamCatalog.Get(Workspace.Settings.ActiveJobTeam);
        var recommended = ConnectionService.ForTeam(all, team?.Id);
        var external = Workspace.Core.Connections.Scanner.Scan(Workspace.Project?.Path)
            .Where(server => !Workspace.Settings.AcknowledgedMcp.Contains(server.Key)).ToList();

        var connected = all.Count(item => item.State == ConnectionState.Connected);
        var attention = all.Count(item => item.State is ConnectionState.SignInRequired or ConnectionState.DependencyMissing);
        var header = Kit.PageHeader("Connections", "Programs, services and tools your agents can use. AGEX finds what is on this computer and shows the next step for each.",
            Kit.Row(8, Kit.Button("Add connection", () => _ = new AddConnectionDialog(Window, Ui).ShowAsync(), "primary", Icons.Plus), Kit.Button("Scan again", () => _ = Workspace.ScanAsync(), "", Icons.Refresh)));
        var filters = Kit.Wrap(
            Filter("All", $"All ({all.Count})"), Filter("Connected", $"Connected ({connected})"),
            Filter("Attention", $"Needs attention ({attention})"), Filter("Available", "Available"), Filter("Installed", "On this computer"));
        var content = Kit.Column(20, header, filters);

        if (external.Count > 0 && _filter is "All" or "Attention")
            content.Children.Add(ExternalSection(external));
        if (recommended.Count > 0 && _filter == "All")
        {
            var ready = recommended.Count(item => item.State == ConnectionState.Connected);
            content.Children.Add(Section($"Recommended for {team!.Name}", $"{ready} of {recommended.Count} ready. Connect what the team needs; the rest is optional.", recommended));
        }

        var shown = all.Where(Matches).ToList();
        foreach (var group in shown.GroupBy(item => item.Category).OrderBy(group => Order(group.Key)))
            content.Children.Add(Section(group.Key, null, group.OrderBy(item => item.State).ThenBy(item => item.Name).ToList()));
        if (shown.Count == 0) content.Children.Add(Kit.Text("Nothing here with this filter.", "small"));
        _body.Content = content;
    }

    private static int Order(string category) => category switch
    {
        "Architecture & engineering" => 0, "Office & documents" => 1, "Browsers" => 2, "Web services" => 3, "Search & research" => 4, "Design & 3D" => 5,
        "Editors & IDEs" => 6, "Developer tools" => 7, "Databases & cloud" => 8, "Automation" => 9, "Local models" => 10, _ => 11,
    };

    private bool Matches(ConnectionItem item) => _filter switch
    {
        "Connected" => item.State == ConnectionState.Connected,
        "Attention" => item.State is ConnectionState.SignInRequired or ConnectionState.DependencyMissing,
        "Available" => item.State is ConnectionState.AvailableToConnect or ConnectionState.InstalledNotConnected,
        "Installed" => item.State is not (ConnectionState.NotInstalled or ConnectionState.AvailableToConnect or ConnectionState.Unsupported),
        _ => true,
    };

    private Control Filter(string id, string label)
    {
        var button = Kit.Button(label, () => { _filter = id; Refresh(); }, _filter == id ? "primary" : "subtle");
        AutomationProperties.SetName(button, "Show " + label);
        return button;
    }

    private Control Section(string title, string? description, IReadOnlyList<ConnectionItem> items)
    {
        var cards = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items) cards.Children.Add(Ui.Card(item, 340));
        return Kit.Column(0, Kit.SectionHeader(title, description), cards);
    }

    // ------------------------------------------------ existing MCP servers

    private Control ExternalSection(IReadOnlyList<ExternalMcpServer> servers)
    {
        var list = Kit.Column(8);
        foreach (var server in servers.Take(30))
        {
            var captured = server;
            var summary = Kit.Text(Agex.Core.Runtime.Redactor.RedactPaths(server.Summary), "caption");
            summary.TextWrapping = TextWrapping.Wrap;
            var names = server.EnvNames.Concat(server.HeaderNames).ToList();
            var settings = server.HasSettings ? Kit.Text($"{names.Count} setting{(names.Count == 1 ? "" : "s")}: " + string.Join(", ", names.Take(4)) + (names.Count > 4 ? $" and {names.Count - 4} more" : "") + " (values stay in that file until you import)", "caption") : null;
            if (settings is not null) settings.TextWrapping = TextWrapping.Wrap;
            var reviewed = ReviewedMatch(server);
            var info = Kit.Column(2, Kit.Row(8, Kit.Text(server.Name, "body"), Kit.Badge("Found in " + server.Source, Tone.Info)), summary, settings,
                reviewed is null ? null : Kit.Badge($"AGEX has a reviewed, pinned version: {reviewed.Name}", Tone.Success, Icons.Shield));
            var actions = Kit.Wrap(
                reviewed is not null
                    ? Kit.Button("Use reviewed version", () => _ = UseReviewedAsync(captured, reviewed.Id), "primary", Icons.Shield, "Installs AGEX's reviewed entry instead of copying this configuration")
                    : Kit.Button("Import into AGEX", () => _ = ImportAsync(captured), "primary", Icons.Download, "Use it with every agent AGEX runs (Codex and Claude Code receive MCP servers per request)"),
                reviewed is not null ? Kit.Button("Import as-is", () => _ = ImportAsync(captured), "subtle", Icons.Download) : null,
                Kit.Button("Use as-is", () => UseAsIs(captured), "subtle", tooltip: $"Leave it in {server.Source} only and stop showing it here"),
                Kit.Button("View details", () => _ = DetailsAsync(captured), "subtle"));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(info);
            Grid.SetColumn(actions, 1);
            actions.MaxWidth = 420;
            row.Children.Add(actions);
            list.Children.Add(Kit.Panel(new Border { Padding = new Thickness(12, 10), Child = row }));
        }
        return Kit.Column(0, Kit.SectionHeader("Found existing MCP connections", $"{servers.Count} server{(servers.Count == 1 ? "" : "s")} already set up in your other AI tools. Import one to use it with every agent, or keep it where it is."), list);
    }

    /// <summary>A catalog MCP entry that starts the same package (for example @upstash/context7-mcp), if any.</summary>
    private Agex.Core.Skills.SkillManifest? ReviewedMatch(ExternalMcpServer server)
    {
        var words = server.Args.Append(server.Command).Append(server.Url).Select(Package).Where(word => word.Length > 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Workspace.Core.Skills.Catalog().Skills.FirstOrDefault(skill => skill.Mcp is { } mcp
            && (mcp.Url.Length > 0 && words.Contains(Package(mcp.Url)) || mcp.Args.Select(Package).Any(package => package.Length > 3 && words.Contains(package))));
    }

    /// <summary>Package name without version: "@scope/name@1.2" and "name==1.2" become "@scope/name" and "name".</summary>
    private static string Package(string value)
    {
        var text = value.Trim();
        var at = text.LastIndexOf('@');
        if (at > 0) text = text[..at];
        var equals = text.IndexOf("==", StringComparison.Ordinal);
        if (equals > 0) text = text[..equals];
        return text.TrimEnd('/');
    }

    private async Task UseReviewedAsync(ExternalMcpServer server, string skillId)
    {
        await Window.Page<SkillsPage>("skills").InstallByIdAsync(skillId);
        if (Workspace.Core.Skills.Installed().Any(skill => skill.Id == skillId))
        {
            Workspace.Settings.AcknowledgedMcp.Add(server.Key);
            Workspace.SaveSettings();
        }
        Refresh();
    }

    private void UseAsIs(ExternalMcpServer server)
    {
        Workspace.Settings.AcknowledgedMcp.Add(server.Key);
        Workspace.SaveSettings();
        Window.Toast(server.Name, $"Left in {server.Source}. {server.Source} keeps using it as before.", ToastKind.Info);
        Refresh();
    }

    private Task DetailsAsync(ExternalMcpServer server)
    {
        var lines = Kit.Column(6,
            Fact("Found in", server.Source), Fact("File", Agex.Core.Runtime.Redactor.RedactPaths(server.ConfigPath)), Fact("Kind", server.Transport == "http" ? "Hosted (https)" : "Local command"),
            Fact(server.Transport == "http" ? "Address" : "Command", Agex.Core.Runtime.Redactor.RedactPaths(server.Summary)),
            Fact("Settings", server.HasSettings ? string.Join(", ", server.EnvNames.Concat(server.HeaderNames)) + " (values are not shown)" : "None"));
        return Window.Dialogs.ShowAsync(server.Name, lines, ["Close"]);
    }

    private static Control Fact(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 8 };
        grid.Children.Add(Kit.Text(label, "caption"));
        var text = Kit.Selectable(value, "small");
        text.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>
    /// Copies the server into AGEX. Settings (tokens, keys) are copied into the
    /// system key store only after the user sees their names and agrees; otherwise
    /// the server is imported without them and asks for them later.
    /// </summary>
    private async Task ImportAsync(ExternalMcpServer server)
    {
        var copy = new CheckBox { Content = $"Copy its settings ({string.Join(", ", server.EnvNames.Concat(server.HeaderNames))}) into {Workspace.Core.Platform.SecureStore.Mechanism}", IsChecked = true, IsVisible = server.HasSettings };
        var body = Kit.Column(10,
            Kit.Text($"AGEX will add \"{server.Name}\" from {server.Source} to your connections. AGEX asks before each use. {server.Source} keeps its own copy; nothing is changed there.", "body"),
            Kit.Text((server.Transport == "http" ? "Address: " : "Runs: ") + Agex.Core.Runtime.Redactor.RedactPaths(server.Summary), "small"),
            copy);
        foreach (var text in body.Children.OfType<TextBlock>()) text.TextWrapping = TextWrapping.Wrap;
        if (await Window.Dialogs.ShowAsync($"Import {server.Name}?", body, ["Import", "Cancel"]) != 0) return;
        try
        {
            var values = server.HasSettings && copy.IsChecked == true ? McpConfigScanner.ReadSettings(server) : new Dictionary<string, string>();
            var description = $"Imported from {server.Source} ({Agex.Core.Runtime.Redactor.RedactPaths(server.ConfigPath)}).";
            if (server.Transport == "http")
            {
                var token = values.TryGetValue("Authorization", out var header) ? header.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim() : values.Values.FirstOrDefault();
                Workspace.Core.Skills.AddRemoteMcpServer(server.Name, server.Url, token, description);
            }
            else
            {
                Workspace.Core.Skills.AddMcpServer(server.Name, server.Command, server.Args, values.Where(pair => server.EnvNames.Contains(pair.Key)).ToDictionary(), description: description);
            }
            Workspace.Settings.AcknowledgedMcp.Add(server.Key);
            Workspace.SaveSettings();
            Window.Toast($"{server.Name} imported", "Find it under Your MCP servers. Agents that support MCP get it with each request.", ToastKind.Success);
        }
        catch (Agex.Core.Skills.SkillException ex) { await Window.Dialogs.MessageAsync("Not imported", ex.Message); }
        Workspace.NotifyConnectionsChanged();
    }
}
