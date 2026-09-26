using Agex.Core.Sessions;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>
/// Home's side list, like a chat app: New chat, the project, and recent
/// conversations (a follow-up joins its conversation). Click one to reopen it;
/// typing then continues it.
/// </summary>
public sealed class ConversationList : UserControl
{
    private readonly MainWindow _window;
    private readonly StackPanel _items = new() { Spacing = 2 };
    private readonly ComboBox _projects = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _allProjects;
    private bool _syncing;

    public event Action? Picked;
    public event Action? CloseRequested;

    public ConversationList(MainWindow window)
    {
        _window = window;
        AutomationProperties.SetName(this, "Conversations");
        AutomationProperties.SetName(_projects, "Project");
        _projects.SelectionChanged += (_, _) =>
        {
            if (_syncing || _projects.SelectedItem is not ComboBoxItem { Tag: string path }) return;
            if (path.Length == 0) { _ = _window.PickProjectAsync(); return; }
            if (path != Workspace.Project?.Path) Workspace.OpenProject(path);
        };
        var scope = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "This project", "All projects" }, SelectedIndex = 0 };
        AutomationProperties.SetName(scope, "Show conversations from");
        scope.SelectionChanged += (_, _) => { _allProjects = scope.SelectedIndex == 1; Refresh(); };
        var newChat = Kit.Button("New chat", () => { _window.Page<HomePage>("home").NewConversation(); Picked?.Invoke(); }, "primary", Icons.Plus, "Start a new conversation", Kit.ShortcutText("N"));
        var close = Kit.IconButton(Icons.Close, "Hide conversations", () => CloseRequested?.Invoke());
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        top.Children.Add(newChat);
        Grid.SetColumn(close, 1);
        top.Children.Add(close);
        newChat.HorizontalAlignment = HorizontalAlignment.Stretch;
        var header = Kit.Column(8, top, Kit.Text("Project", "caption"), _projects, Kit.Text("Recent conversations", "caption"), scope);
        var scroll = new ScrollViewer { Content = _items, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        header.Margin = new Thickness(0, 0, 0, 8);
        layout.Children.Add(header);
        layout.Children.Add(scroll);
        var border = new Border { Child = layout, Padding = new Thickness(12), Width = 248, BorderThickness = new Thickness(0, 0, 1, 0) };
        border.Res(Border.BorderBrushProperty, "BorderBrush");
        border.Res(Border.BackgroundProperty, "SurfaceBrush");
        Content = border;
        Workspace.SessionChanged += RefreshSoon;
        Workspace.ProjectChanged += Refresh;
        Refresh();
    }

    private Workspace Workspace => _window.Workspace;

    private bool _pending;

    private void RefreshSoon()
    {
        if (_pending) return;
        _pending = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { _pending = false; Refresh(); }, TimeSpan.FromMilliseconds(400));
    }

    public void Refresh()
    {
        _syncing = true;
        var recent = Workspace.Settings.RecentProjects.Where(Directory.Exists).Take(10).ToList();
        if (Workspace.Project is { } project && !recent.Contains(project.Path, StringComparer.OrdinalIgnoreCase)) recent.Insert(0, project.Path);
        var items = recent.Select(path => new ComboBoxItem { Content = Path.GetFileName(path.TrimEnd('/', '\\')), Tag = path }).ToList();
        foreach (var item in items) ToolTip.SetTip(item, item.Tag);
        items.Add(new ComboBoxItem { Content = "Open folder...", Tag = "" });
        _projects.ItemsSource = items;
        _projects.SelectedItem = items.FirstOrDefault(item => Workspace.Project is { } current && SessionStore.SamePath((string)item.Tag!, current.Path));
        _syncing = false;

        _items.Children.Clear();
        var all = Workspace.Core.Sessions.List(_allProjects ? null : Workspace.Project?.Path);
        // A conversation is shown by its latest turn; turns continued by a later one are folded into it.
        var continued = all.Select(item => item.ContinuedFrom).Where(id => id.Length > 0).ToHashSet();
        var heads = all.Where(item => !continued.Contains(item.Id)).Take(60).ToList();
        if (heads.Count == 0) { _items.Children.Add(Kit.Text(Workspace.Project is null ? "Choose a project to start." : "No conversations yet.", "small")); return; }
        foreach (var head in heads) _items.Children.Add(Row(head, all));
    }

    private Control Row(SessionSummary head, IReadOnlyList<SessionSummary> all)
    {
        var turns = 1;
        var id = head.ContinuedFrom;
        var root = head;
        while (id.Length > 0 && turns < 50 && all.FirstOrDefault(item => item.Id == id) is { } earlier) { turns++; root = earlier; id = earlier.ContinuedFrom; }
        var title = Kit.Text(root.Title, "small");
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Res(TextBlock.ForegroundProperty, "TextBrush");
        var (_, tone, _) = HomePage.StatusLook(head.Status);
        var meta = Kit.Text($"{Kit.Ago(head.UpdatedAt)}{(turns > 1 ? $" · {turns} turns" : "")}{(_allProjects ? " · " + Path.GetFileName(head.Project.TrimEnd('/', '\\')) : "")}", "caption");
        var current = Workspace.Session is { } session && (session.Id == head.Id || Workspace.Thread.Any(turn => turn.Id == head.Id));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        var glyph = Kit.Icon(ModeIcon(head.Mode), 14, Kit.ToneKeys(tone).Foreground);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 2, 0, 0);
        grid.Children.Add(glyph);
        var text = Kit.Column(0, title, meta);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var button = new Button { Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 6) };
        button.Classes.Add("subtle");
        if (current) button.Classes.Add("active");
        ToolTip.SetTip(button, $"{root.Title}\n{SessionStatusText.Label(head.Status)} · {ModeName(head.Mode)}");
        AutomationProperties.SetName(button, $"{root.Title}, {Kit.Ago(head.UpdatedAt)}, {SessionStatusText.Label(head.Status)}");
        button.Click += (_, _) =>
        {
            if (Workspace.IsRunning) { _window.Toast("A request is running", "Open this conversation when it has finished.", ToastKind.Info); return; }
            if (Workspace.Core.Sessions.Load(head.Id) is not { } loaded) return;
            if (!SessionStore.SamePath(loaded.Project, Workspace.Project?.Path ?? "") && Directory.Exists(loaded.Project)) Workspace.OpenProject(loaded.Project);
            Workspace.ShowSession(loaded);
            _window.Navigate("home");
            Picked?.Invoke();
        };
        return button;
    }

    public static string ModeName(string mode) => mode switch { "chat" => "Chat", "ask" => "Ask", "plan" => "Plan", "build" => "Build", _ => "Request" };

    public static string ModeIcon(string mode) => mode switch { "chat" => Icons.Send, "ask" => Icons.Question, "plan" => Icons.Graph, _ => Icons.Tool };
}
