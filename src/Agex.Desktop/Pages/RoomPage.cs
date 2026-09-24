using System.Collections.ObjectModel;
using Agex.Core.Sessions;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>
/// Agent Room: the explicit messages exchanged by the user, AGEX and the
/// agents, plus the task graph. Only messages agents actually sent and
/// actions AGEX actually took appear here; hidden reasoning never does.
/// </summary>
public sealed class RoomPage(MainWindow window) : AppPage(window)
{
    private readonly ObservableCollection<AgentMessage> _visible = [];
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Single };
    private readonly TextBox _search = new() { PlaceholderText = "Search messages", MinWidth = 200 };
    private readonly ComboBox _agentFilter = new() { MinWidth = 150 };
    private readonly ComboBox _taskFilter = new() { MinWidth = 150 };
    private readonly ComboBox _typeFilter = new() { MinWidth = 150 };
    private readonly ToggleSwitch _follow = new() { IsChecked = true, OnContent = "Follow live", OffContent = "Follow live" };
    private readonly ToggleSwitch _tools = new() { IsChecked = true, OnContent = "Tool events", OffContent = "Tool events" };
    private readonly HashSet<string> _expanded = [];
    private readonly ContentControl _graph = new();
    private readonly TextBlock _count = Kit.Text("", "caption");
    private TabControl? _tabs;
    private bool _allExpanded;

    public override string Id => "room";
    public override string Title => "Agent Room";
    public override string Icon => Icons.Room;

    private static readonly (MessageType? Value, string Label)[] Types =
    [
        (null, "All types"), (MessageType.Assignment, "Assignment"), (MessageType.Question, "Question"), (MessageType.Answer, "Answer"),
        (MessageType.Result, "Result"), (MessageType.Review, "Review"), (MessageType.Revision, "Revision"), (MessageType.Status, "Status"),
        (MessageType.ToolEvent, "Tool event"), (MessageType.System, "AGEX notice"),
    ];

    protected override Control Build()
    {
        AutomationProperties.SetName(_search, "Search messages");
        AutomationProperties.SetName(_agentFilter, "Filter by agent");
        AutomationProperties.SetName(_taskFilter, "Filter by task");
        AutomationProperties.SetName(_typeFilter, "Filter by message type");
        AutomationProperties.SetName(_follow, "Follow live");
        AutomationProperties.SetName(_tools, "Show tool events");
        _typeFilter.ItemsSource = Types.Select(type => type.Label).ToList();
        _typeFilter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => ApplyFilter();
        _agentFilter.SelectionChanged += (_, _) => ApplyFilter();
        _taskFilter.SelectionChanged += (_, _) => ApplyFilter();
        _typeFilter.SelectionChanged += (_, _) => ApplyFilter();
        _tools.IsCheckedChanged += (_, _) => ApplyFilter();
        _list.ItemsSource = _visible;
        _list.ItemTemplate = new FuncDataTemplate<AgentMessage>((message, _) => message is null ? new Border() : MessageCard(message), supportsRecycling: false);
        Workspace.Messages.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (AgentMessage message in e.NewItems) if (Matches(message)) _visible.Add(message);
                UpdateCount();
                ScrollToEnd();
            }
            else ApplyFilter();
        };
        Workspace.SessionChanged += () => { RefreshFilters(); RefreshGraph(); };
        Workspace.Tasks.CollectionChanged += (_, _) => { RefreshFilters(); RefreshGraph(); };

        var toolbar = Kit.Wrap(_search, _agentFilter, _taskFilter, _typeFilter, _follow, _tools,
            Kit.Button("Expand all", ToggleExpandAll, "subtle", Icons.ChevronDown),
            Kit.Button("Copy visible", () => _ = Window.CopyAsync(string.Join("\n\n", _visible.Select(Format))), "subtle", Icons.Copy));
        var feed = new DockPanel();
        var header = Kit.Column(8, toolbar, _count);
        header.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(header, Dock.Top);
        feed.Children.Add(header);
        feed.Children.Add(_list);

        _tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Conversation", Content = feed },
                new TabItem { Header = "Task graph", Content = new ScrollViewer { Content = _graph } },
            },
        };
        var page = new DockPanel { Margin = new Thickness(32, 16, 32, 16) };
        var title = Kit.PageHeader("Agent Room", "Messages between you, AGEX and the agents. Only what agents actually sent is shown.");
        DockPanel.SetDock(title, Dock.Top);
        page.Children.Add(title);
        page.Children.Add(_tabs);
        RefreshFilters();
        ApplyFilter();
        RefreshGraph();
        return page;
    }

    public override void OnShown() { RefreshFilters(); ApplyFilter(); RefreshGraph(); }

    public void FocusSearch() => Avalonia.Threading.Dispatcher.UIThread.Post(() => _search.Focus());

    private void RefreshFilters()
    {
        var agentSelection = _agentFilter.SelectedItem as string;
        var agents = new List<string> { "All agents" };
        agents.AddRange(Workspace.Messages.SelectMany(message => new[] { message.From, message.To }).Where(name => name.Length > 0).Distinct().OrderBy(name => name));
        _agentFilter.ItemsSource = agents;
        _agentFilter.SelectedItem = agentSelection is not null && agents.Contains(agentSelection) ? agentSelection : agents[0];
        var taskSelection = _taskFilter.SelectedItem as string;
        var tasks = new List<string> { "All tasks" };
        tasks.AddRange(Workspace.Tasks.Select(task => TaskLabel(task.Id)));
        _taskFilter.ItemsSource = tasks;
        _taskFilter.SelectedItem = taskSelection is not null && tasks.Contains(taskSelection) ? taskSelection : tasks[0];
    }

    private string TaskLabel(string id) => Workspace.Tasks.FirstOrDefault(task => task.Id == id) is { } task ? $"{Number(id)} {task.Label}" : Number(id);

    private static string Number(string id) => id.StartsWith("task-", StringComparison.Ordinal) && int.TryParse(id[5..], out var n) ? "#" + n : id;

    private bool Matches(AgentMessage message)
    {
        if (_tools.IsChecked != true && message.Type == MessageType.ToolEvent) return false;
        if (_agentFilter.SelectedIndex > 0 && _agentFilter.SelectedItem is string agent && message.From != agent && message.To != agent) return false;
        if (_taskFilter.SelectedIndex > 0 && _taskFilter.SelectedItem is string task && !task.StartsWith(Number(message.TaskId) + " ", StringComparison.Ordinal)) return false;
        if (_typeFilter.SelectedIndex > 0 && Types[_typeFilter.SelectedIndex].Value is { } type && message.Type != type) return false;
        var query = _search.Text?.Trim() ?? "";
        return query.Length == 0 || message.Text.Contains(query, StringComparison.OrdinalIgnoreCase) || message.From.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilter()
    {
        _visible.Clear();
        foreach (var message in Workspace.Messages) if (Matches(message)) _visible.Add(message);
        UpdateCount();
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (_follow.IsChecked != true || _visible.Count == 0) return;
        // After layout, so it also works when the page was not visible while messages arrived.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_visible.Count > 0) _list.ScrollIntoView(_visible[^1]); }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void UpdateCount() => _count.Text = Workspace.Messages.Count == 0
        ? "No messages yet. They appear here as soon as a request starts."
        : $"{_visible.Count} of {Workspace.Messages.Count} messages";

    private void ToggleExpandAll()
    {
        _allExpanded = !_allExpanded;
        _expanded.Clear();
        if (_allExpanded) foreach (var message in _visible) _expanded.Add(message.Id);
        ApplyFilter();
    }

    private static (string Label, Tone Tone, string Icon) TypeLook(MessageType type) => type switch
    {
        MessageType.Assignment => ("Assignment", Tone.Accent, Icons.Send),
        MessageType.Question => ("Question", Tone.Warning, Icons.Question),
        MessageType.Answer => ("Answer", Tone.Info, Icons.Room),
        MessageType.Result => ("Result", Tone.Success, Icons.Check),
        MessageType.Review => ("Review", Tone.Info, Icons.Search),
        MessageType.Revision => ("Revision", Tone.Warning, Icons.Refresh),
        MessageType.ToolEvent => ("Tool event", Tone.Neutral, Icons.Tool),
        MessageType.System => ("AGEX", Tone.Neutral, Icons.Info),
        _ => ("Status", Tone.Neutral, Icons.Dot),
    };

    private static string Format(AgentMessage message) =>
        $"[{message.At.ToLocalTime():HH:mm:ss}] {message.From}{(message.To.Length > 0 ? " -> " + message.To : "")} ({message.Type}{(message.TaskId.Length > 0 ? ", " + Number(message.TaskId) : "")})\n{message.Text}";

    private Control MessageCard(AgentMessage message)
    {
        var (label, tone, icon) = TypeLook(message.Type);
        if (message.Type == MessageType.ToolEvent)
        {
            // Compact single line for tool events.
            // The grid's last column takes the remaining width so long commands end in an ellipsis instead of overflowing.
            var shown = Agex.Core.Runtime.Redactor.RedactPaths(message.Text);
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 8 };
            Control[] cells = [Kit.Icon(Icons.Tool, 13, "Text3Brush"), Kit.Text($"{message.At.ToLocalTime():HH:mm:ss}", "caption"), Kit.Text(message.From, "caption"), Kit.Text(shown, "small")];
            for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); cells[i].VerticalAlignment = VerticalAlignment.Center; line.Children.Add(cells[i]); }
            ((TextBlock)cells[3]).TextWrapping = TextWrapping.NoWrap;
            ((TextBlock)cells[3]).TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(cells[3], shown);
            return new Border { Padding = new Thickness(48, 3, 8, 3), Child = line, [AutomationProperties.NameProperty] = $"Tool event by {message.From}: {shown}" };
        }
        var expanded = _expanded.Contains(message.Id);
        var long_ = message.Text.Length > 600 || message.Text.Count(ch => ch == '\n') > 8;
        var text = Kit.Selectable(message.Text, "body");
        if (long_ && !expanded) text.MaxLines = 8;
        var header = Kit.Row(8,
            Kit.Text(message.From, "body"),
            message.To.Length > 0 ? Kit.Text("to " + message.To, "small") : null,
            Kit.Badge(label, tone, icon),
            message.TaskId.Length > 0 ? Kit.Badge(Number(message.TaskId), Tone.Neutral) : null,
            Kit.Text(message.At.ToLocalTime().ToString("HH:mm:ss"), "caption"));
        ((TextBlock)header.Children[0]).FontWeight = FontWeight.SemiBold;
        var more = long_ ? Kit.Button(expanded ? "Show less" : "Show more", () => { if (!_expanded.Remove(message.Id)) _expanded.Add(message.Id); ApplyFilter(); }, "link") : null;
        var copy = Kit.IconButton(Icons.Copy, "Copy message", () => _ = Window.CopyAsync(message.Text));
        copy.VerticalAlignment = VerticalAlignment.Top;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(Kit.Avatar(message.From, 32));
        var content = Kit.Column(4, header, text, more);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        Grid.SetColumn(copy, 2);
        grid.Children.Add(copy);
        var card = new Border { Padding = new Thickness(12, 10), Margin = new Thickness(0, 4), CornerRadius = new CornerRadius(10), Child = grid };
        card.Res(Border.BackgroundProperty, message.From == "User" ? "AccentSoftBrush" : message.Type == MessageType.System ? "Surface2Brush" : "SurfaceBrush");
        card.BorderThickness = new Thickness(1);
        card.Res(Border.BorderBrushProperty, "BorderBrush");
        AutomationProperties.SetName(card, $"{label} from {message.From}{(message.To.Length > 0 ? " to " + message.To : "")}: {message.Text}");
        return card;
    }

    // ------------------------------------------------------------- task graph

    /// <summary>Tasks arranged in columns by dependency depth: first steps on the left, what they unlock to the right.</summary>
    private void RefreshGraph()
    {
        var tasks = Workspace.Tasks.ToList();
        if (tasks.Count == 0)
        {
            _graph.Content = Kit.EmptyState(Icons.Graph, "No tasks yet", "When the leader plans a request, its tasks and their order appear here.");
            return;
        }
        var depth = new Dictionary<string, int>();
        int Depth(TaskItem task, int guard = 0)
        {
            if (depth.TryGetValue(task.Id, out var known)) return known;
            var value = guard > 50 ? 0 : task.Dependencies.Select(id => tasks.FirstOrDefault(item => item.Id == id)).Where(item => item is not null).Select(item => Depth(item!, guard + 1) + 1).DefaultIfEmpty(0).Max();
            depth[task.Id] = value;
            return value;
        }
        foreach (var task in tasks) Depth(task);
        var columns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 12) };
        foreach (var level in tasks.GroupBy(task => depth[task.Id]).OrderBy(group => group.Key))
        {
            var column = Kit.Column(10, Kit.Text(level.Key == 0 ? "Start" : $"Then (step {level.Key + 1})", "caption"));
            foreach (var task in level)
            {
                var (text, tone) = HomePage.TaskLook(task.State);
                var agent = Workspace.Core.Registry.Get(task.Agent)?.Name ?? task.Agent;
                var card = Kit.Card(Kit.Column(6,
                    Kit.Row(8, Kit.Avatar(agent, 22), Kit.Text(Number(task.Id), "caption"), Kit.Badge(text, tone)),
                    Kit.Text(task.Label, "body"),
                    task.Dependencies.Count > 0 ? Kit.Text("After " + string.Join(", ", task.Dependencies.Select(Number)), "caption") : null,
                    task.RepairFor.Count > 0 ? Kit.Text("Repairs " + string.Join(", ", task.RepairFor.Select(Number)), "caption") : null), 12);
                card.Width = 240;
                column.Children.Add(card);
            }
            columns.Children.Add(column);
        }
        _graph.Content = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = columns };
    }
}
