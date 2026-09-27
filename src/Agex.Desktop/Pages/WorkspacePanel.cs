using Agex.Core.Attachments;
using Agex.Core.Agents;
using Agex.Core.Sessions;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Agex.Desktop.Pages;

/// <summary>A surface with something in it, for the collapsed rail and the panel's strip ("Changes 3").</summary>
public sealed record WorkspaceIndicator(WorkspaceSurface Surface, string Label, string Icon, int Count);

/// <summary>
/// The optional workspace beside the chat. It shows what the current work has
/// produced (changes, browser pages, commands, previews) and nothing else up
/// front; Activity, Files and Connections stay one click away under More. It
/// observes the agents and never sits on their critical path; it never shows an
/// agent's hidden reasoning.
/// </summary>
public sealed partial class WorkspacePanel : UserControl
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".log", ".html", ".htm", ".css", ".js", ".ts", ".tsx", ".jsx",
        ".cs", ".py", ".java", ".kt", ".go", ".rs", ".c", ".h", ".cpp", ".hpp", ".rb", ".php", ".sh", ".ps1", ".sql", ".swift", ".vue", ".svelte", ".csproj", ".slnx", ".props",
    };

    private readonly Workspace _workspace;
    private readonly MainWindow _window;
    private readonly ContentControl _host = new();
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Dictionary<WorkspaceSurface, Control> _surfaces = new();
    private WorkspaceSurface _selected = WorkspaceSurface.Activity;
    private readonly TabControl _browserTabs = new();
    private readonly TabControl _terminalTabs = new();
    private readonly Dictionary<string, ObservedTerminalTab> _agentTerminals = new();
    private readonly HashSet<string> _observedUrls = new(StringComparer.OrdinalIgnoreCase);
    // Agent pages are opened in the panel only when the user looks at Browser: observing never loads pages on its own.
    private readonly List<Uri> _pendingUrls = [];
    private readonly StackPanel _files = new() { Spacing = 8, Margin = new Thickness(16) };
    private readonly ContentControl _preview = new();
    private readonly ContentControl _diff = new();
    private readonly StackPanel _computerControls = new() { Spacing = 10 };
    private readonly StackPanel _computerLog = new() { Spacing = 10 };
    private readonly ComboBox _liveMode = new() { ItemsSource = new[] { "Off", "Normal", "Detailed" }, MinWidth = 110 };
    private string? _selectedFile;
    private bool _previewUsed;
    // One web preview for the panel; it is moved between pages only through this fixed container.
    private WebPreview? _web;
    private readonly ContentControl _webHeader = new();
    private DockPanel? _webPage;
    private bool _pending;
    private readonly Button _expandButton;

    public event Action? CollapseRequested;
    public event Action? PopOutRequested;
    public event Action? ExpandRequested;
    /// <summary>Raised when a surface appears, disappears or changes its count (the collapsed rail follows it).</summary>
    public event Action? IndicatorsChanged;

    public WorkspacePanel(MainWindow window, ActivityPanel activity)
    {
        _window = window;
        _workspace = window.Workspace;
        _liveMode.SelectedIndex = (int)_workspace.Settings.LiveView;
        Avalonia.Automation.AutomationProperties.SetName(_liveMode, "Live detail");
        _liveMode.SelectionChanged += (_, _) =>
        {
            if (_liveMode.SelectedIndex < 0) return;
            _workspace.Settings.LiveView = (Agex.Core.Settings.LiveViewMode)_liveMode.SelectedIndex;
            _workspace.SaveSettings();
            RefreshSoon();
        };
        // Activity: who is working, run controls, and the recent steps. Everything technical lives here, not in the chat.
        _surfaces[WorkspaceSurface.Activity] = new ScrollViewer
        {
            Content = Kit.Column(14, activity, new Border { Padding = new Thickness(16, 0, 16, 16), Child = Kit.Column(12, _computerControls, _computerLog) }),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        _surfaces[WorkspaceSurface.Changes] = _diff;
        _surfaces[WorkspaceSurface.Files] = BuildFilesTab();
        _surfaces[WorkspaceSurface.Preview] = _preview;
        _surfaces[WorkspaceSurface.Browser] = new DockPanel { Children = { _browserTabs } };
        _surfaces[WorkspaceSurface.Terminal] = new DockPanel { Children = { _terminalTabs } };
        _surfaces[WorkspaceSurface.Connections] = new ScrollViewer { Content = _connections };

        var more = Kit.IconButton(Icons.ChevronDown, "More", () => { });
        more.Click += (_, _) => ShowMore(more);
        _expandButton = Kit.IconButton(Icons.Expand, "Wider panel", () => ExpandRequested?.Invoke());
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 6, 4, 0) };
        header.Children.Add(new ScrollViewer { Content = _strip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var actions = Kit.Row(0, more, _expandButton, Kit.IconButton(Icons.Chevron, "Hide the workspace", () => CollapseRequested?.Invoke(), Kit.ShortcutText("J")));
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        var divider = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 0) };
        divider.Res(Border.BackgroundProperty, "BorderBrush");
        var top = Kit.Column(0, header, divider);
        var layout = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(_host);
        Content = layout;

        _workspace.SessionChanged += () =>
        {
            if (!_workspace.IsRunning) foreach (var terminal in _agentTerminals.Values) terminal.MarkEnded();
            RefreshSoon();
        };
        _workspace.Timeline.CollectionChanged += (_, _) => RefreshSoon();
        _workspace.Messages.CollectionChanged += (_, _) => { if (_workspace.Settings.LiveView == Agex.Core.Settings.LiveViewMode.Detailed) RefreshSoon(); };
        _workspace.PendingAttachments.CollectionChanged += (_, _) => RefreshSoon();
        _workspace.ProjectChanged += () => { _treeProject = null; RefreshSoon(); };
        _workspace.ConnectionsChanged += RefreshSoon;
        _workspace.SettingsChanged += RefreshSoon;
        _workspace.AgentActivityReceived += ObserveAgentActivity;
        ShowPreview(null);
        Refresh();
    }

    /// <summary>Panel width mode, set by the window: the expand button shows what pressing it does.</summary>
    public void SetExpanded(bool expanded)
    {
        _expandButton.Content = Kit.Icon(Icons.Expand, 16);
        Avalonia.Automation.AutomationProperties.SetName(_expandButton, expanded ? "Narrower panel" : "Wider panel");
        ToolTip.SetTip(_expandButton, expanded ? "Narrower panel" : "Wider panel");
    }

    private void RefreshSoon()
    {
        if (_pending) return;
        _pending = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { _pending = false; Refresh(); }, TimeSpan.FromMilliseconds(300));
    }

    public void Refresh()
    {
        BuildFiles();
        BuildComputer();
        if (!_showingDiff) BuildChanges();
        if (_selected == WorkspaceSurface.Files) BuildTree();
        if (_selected == WorkspaceSurface.Connections) BuildConnections();
        BuildStrip();
    }

    // ------------------------------------------------------------ surfaces

    /// <summary>Surfaces with something in them, most useful first.</summary>
    public IReadOnlyList<WorkspaceIndicator> Indicators() =>
        WorkspaceSurfaces.Visible(_workspace.Session, _workspace.IsRunning, BrowserTabs().Count + _pendingUrls.Count, TerminalTabs().Count, _previewUsed, _files.Children.Count)
            .Select(item => new WorkspaceIndicator(item.Surface, SurfaceName(item.Surface), item.Surface switch
            {
                WorkspaceSurface.Changes => Icons.Diff, WorkspaceSurface.Browser => Icons.Globe, WorkspaceSurface.Terminal => Icons.Terminal,
                WorkspaceSurface.Preview => Icons.Eye, WorkspaceSurface.Files => Icons.File, _ => Icons.Pulse,
            }, item.Count)).ToList();

    private static string SurfaceName(WorkspaceSurface surface) => surface switch
    {
        WorkspaceSurface.Changes => "Changes", WorkspaceSurface.Browser => "Browser", WorkspaceSurface.Terminal => "Terminal", WorkspaceSurface.Preview => "Preview",
        WorkspaceSurface.Files => "Files", WorkspaceSurface.Connections => "Connections", _ => "Activity",
    };

    private string _lastStrip = "";

    /// <summary>The strip shows the surfaces that exist now (and the one being viewed); the rest is under More.</summary>
    private void BuildStrip()
    {
        var shown = Indicators().ToList();
        if (shown.All(item => item.Surface != _selected)) shown.Add(new(_selected, SurfaceName(_selected), Icons.Pulse, 0));
        if (shown.All(item => item.Surface != WorkspaceSurface.Activity)) shown.Insert(0, new(WorkspaceSurface.Activity, "Activity", Icons.Pulse, 0));
        var key = string.Join(",", shown.Select(item => $"{item.Surface}:{item.Count}")) + "|" + _selected;
        if (key != _lastStrip)
        {
            _lastStrip = key;
            _strip.Children.Clear();
            foreach (var item in shown)
            {
                var captured = item.Surface;
                var label = item.Count > 0 ? $"{item.Label} {item.Count}" : item.Label;
                var button = Kit.Button(label, () => Select(captured), "tab");
                button.Classes.Set("active", item.Surface == _selected);
                _strip.Children.Add(button);
            }
            IndicatorsChanged?.Invoke();
        }
        if (!ReferenceEquals(_host.Content, _surfaces[_selected])) _host.Content = _surfaces[_selected];
    }

    /// <summary>Shows one surface (from the strip, the rail, More or other pages).</summary>
    public void Select(WorkspaceSurface surface)
    {
        _selected = surface;
        if (surface == WorkspaceSurface.Browser) OpenPendingPages();
        if (surface == WorkspaceSurface.Files) BuildTree();
        if (surface == WorkspaceSurface.Connections) BuildConnections();
        if (surface == WorkspaceSurface.Changes && !_showingDiff) BuildChanges();
        BuildStrip();
    }

    private void ShowMore(Button anchor)
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action click)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => click();
            return item;
        }
        menu.Items.Add(Item("Activity", () => Select(WorkspaceSurface.Activity)));
        menu.Items.Add(Item("Changes", () => Select(WorkspaceSurface.Changes)));
        menu.Items.Add(Item("Files", () => Select(WorkspaceSurface.Files)));
        menu.Items.Add(Item("Preview", () => { _previewUsed = true; Select(WorkspaceSurface.Preview); }));
        menu.Items.Add(Item("Connections", () => Select(WorkspaceSurface.Connections)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("New browser tab", () => OpenWeb(new Uri("about:blank"))));
        if (_workspace.Project is not null) menu.Items.Add(Item("New terminal", OpenTerminal));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open in its own window", () => PopOutRequested?.Invoke()));
        menu.Open(anchor);
    }

    private List<TabItem> BrowserTabs() => _browserTabs.ItemsSource as List<TabItem> ?? [];
    private List<TabItem> TerminalTabs() => _terminalTabs.ItemsSource as List<TabItem> ?? [];

    private static void Reset(TabControl tabs, List<TabItem> items, TabItem? selected)
    {
        tabs.ItemsSource = null;
        tabs.ItemsSource = items;
        if (selected is not null) tabs.SelectedItem = selected;
    }

    /// <summary>Opens a file in Preview (used by Files and by other pages).</summary>
    public void Open(string path)
    {
        ShowPreview(path);
        _previewUsed = true;
        Select(WorkspaceSurface.Preview);
    }

    /// <summary>Opens a user-chosen HTTPS site in a separate embedded browser tab.</summary>
    public void OpenWeb(Uri uri, bool observed = false)
    {
        if (!Agex.Core.Runtime.PreviewPolicy.IsAllowedWebTab(uri)) return;
        var tabs = BrowserTabs();
        if (tabs.Count >= 8) return;
        var web = new WebPreview(_window, browseWeb: true, agentObserved: observed);
        var title = uri.Host.Length > 0 ? uri.Host : "New tab";
        var tab = new TabItem { Content = web };
        tab.Header = Kit.Row(4, Kit.Text(title, "small"), Kit.IconButton(Icons.Close, "Close " + title, () =>
        {
            tabs.Remove(tab);
            Reset(_browserTabs, tabs, null);
            if (tabs.Count == 0 && _selected == WorkspaceSurface.Browser) _selected = WorkspaceSurface.Activity;
            BuildStrip();
        }));
        tabs.Add(tab);
        Reset(_browserTabs, tabs, tab);
        web.Show(uri, null);
        Select(WorkspaceSurface.Browser);
    }

    private void OpenPendingPages()
    {
        var pending = _pendingUrls.ToList();
        _pendingUrls.Clear();
        foreach (var url in pending) OpenWeb(url, observed: true);
    }

    public void OpenTerminal()
    {
        var tabs = TerminalTabs();
        if (tabs.Count >= 8) return;
        var title = "Terminal " + (tabs.Count + 1);
        var terminal = new TerminalTab(_window);
        var tab = new TabItem { Content = terminal };
        tab.Header = Kit.Row(4, Kit.Text(title, "small"), Kit.IconButton(Icons.Close, "Close " + title, () =>
        {
            terminal.Close();
            tabs.Remove(tab);
            Reset(_terminalTabs, tabs, null);
            if (tabs.Count == 0 && _selected == WorkspaceSurface.Terminal) _selected = WorkspaceSurface.Activity;
            BuildStrip();
        }));
        tabs.Add(tab);
        Reset(_terminalTabs, tabs, tab);
        Select(WorkspaceSurface.Terminal);
    }

    /// <summary>
    /// Agent browser pages and commands become Browser and Terminal entries. The panel does not jump to them
    /// (the user may be reading something else) and pages load only when Browser is opened.
    /// </summary>
    private void ObserveAgentActivity(string agent, AgentActivity activity)
    {
        if (_workspace.Settings.LiveView == Agex.Core.Settings.LiveViewMode.Off) return;
        if (activity.Surface == AgentSurface.Browser && activity.Url is { } url &&
            Agex.Core.Runtime.PreviewPolicy.IsAllowedWebTab(url) && _observedUrls.Add(url.AbsoluteUri))
        {
            if (_selected == WorkspaceSurface.Browser && IsVisible) OpenWeb(url, observed: true);
            else if (BrowserTabs().Count + _pendingUrls.Count < 8) _pendingUrls.Add(url);
            BuildStrip();
            return;
        }
        if (activity.Surface != AgentSurface.Terminal) return;
        if (!_agentTerminals.TryGetValue(activity.Id, out var terminal))
        {
            if (activity.Kind != ActivityKind.ToolStarted) return;
            var tabs = TerminalTabs();
            if (tabs.Count >= 8) return;
            terminal = new ObservedTerminalTab(agent, activity.WorkingDirectory, activity.Text);
            _agentTerminals[activity.Id] = terminal;
            var tab = new TabItem { Content = terminal };
            tab.Header = Kit.Row(4, Kit.Text(CommandText.Title(activity.Text), "small").Trimmed(140), Kit.IconButton(Icons.Close, "Close command tab", () =>
            {
                _agentTerminals.Remove(activity.Id);
                tabs.Remove(tab);
                Reset(_terminalTabs, tabs, null);
                if (tabs.Count == 0 && _selected == WorkspaceSurface.Terminal) _selected = WorkspaceSurface.Activity;
                BuildStrip();
            }));
            tabs.Add(tab);
            Reset(_terminalTabs, tabs, tab);
            BuildStrip();
        }
        terminal.Observe(activity);
    }

    // ---------------------------------------------------------------- files

    private void BuildFiles()
    {
        _files.Children.Clear();
        var session = _workspace.Session;
        var pending = _workspace.PendingAttachments.ToList();
        var attached = _workspace.LastAttachments;
        var changes = session?.Changes.ToList() ?? [];
        var reports = session?.Artifacts.Where(item => item.Kind is not ArtifactKind.Snapshot && File.Exists(item.Path)).ToList() ?? [];

        if (pending.Count + attached.Count + reports.Count == 0) return;
        if (pending.Count > 0)
        {
            _files.Children.Add(Kit.Text("Ready to send", "caption"));
            foreach (var path in pending) _files.Children.Add(FileRow(path, AttachmentService.KindLabel(AttachmentService.Classify(path)), diff: false));
        }
        if (attached.Count > 0)
        {
            _files.Children.Add(Kit.Text("Attached to this request", "caption"));
            foreach (var item in attached) _files.Children.Add(FileRow(item.Path, AttachmentService.KindLabel(item.Kind) + (item.Note.Length > 0 ? " - " + item.Note : ""), diff: false));
        }
        if (reports.Count > 0)
        {
            _files.Children.Add(Kit.Text("Outputs", "caption"));
            foreach (var report in reports) _files.Children.Add(FileRow(report.Path, report.Detail.Length > 0 ? report.Detail : report.Kind.ToString(), diff: false));
        }
    }

    private Control FileRow(string path, string detail, bool diff, string? relative = null)
    {
        var name = Kit.Text(relative ?? Path.GetFileName(path), "body").Trimmed(240);
        var open = new Button { Content = Kit.Column(0, name, Kit.Text(detail, "caption").Trimmed(240)), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        open.Classes.Add("subtle");
        open.Click += (_, _) => Open(path);
        ToolTip.SetTip(open, "Preview");
        Avalonia.Automation.AutomationProperties.SetName(open, "Preview " + Path.GetFileName(path));
        var buttons = Kit.Row(0,
            diff && relative is not null ? Kit.IconButton(Icons.Diff, "Show changes", () => _ = ShowFileDiffAsync(relative)) : null,
            File.Exists(path) ? Kit.IconButton(Icons.External, "Open with the default app", () => OpenExternally(path)) : null,
            File.Exists(path) || Directory.Exists(Path.GetDirectoryName(path)) ? Kit.IconButton(Icons.Folder, "Show in folder", () => Reveal(path)) : null);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(open);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        return row;
    }

    private void OpenExternally(string path)
    {
        try { _workspace.Core.Platform.OpenPath(path); }
        catch (Exception ex) { _window.Toast("Could not open the file", ex.Message, ToastKind.Error); }
    }

    private void Reveal(string path)
    {
        try { _workspace.Core.Platform.RevealInFileManager(path); }
        catch (Exception ex) { _window.Toast("Could not show the folder", ex.Message, ToastKind.Error); }
    }

    // -------------------------------------------------------------- preview

    private void ShowPreview(string? path)
    {
        _selectedFile = path;
        if (path is null)
        {
            _preview.Content = Kit.EmptyState(Icons.Search, "Preview", "Choose a file in Files. Web pages, images, documents, spreadsheets, video, text and code show here.",
                Kit.Button("Preview a local server", () => ShowWeb(null, Kit.Column(2, Kit.Text("Local server", "subtitle"), Kit.Text("Enter an address on this computer, for example http://localhost:5173.", "caption"))), "", Icons.Computer));
            return;
        }
        if (Path.GetExtension(path) is var web && (web.Equals(".html", StringComparison.OrdinalIgnoreCase) || web.Equals(".htm", StringComparison.OrdinalIgnoreCase)) && File.Exists(path))
        {
            ShowWeb(new Uri(path), Kit.Column(4,
                Kit.Text(Path.GetFileName(path), "subtitle").Trimmed(320),
                Kit.Row(6,
                    Kit.Button("Source", () => ShowSource(path), "", Icons.Copy, "Show the page's HTML"),
                    Kit.Button("Show in folder", () => Reveal(path), "", Icons.Folder))));
            return;
        }
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            ShowWeb(new Uri(path), Kit.Column(4,
                Kit.Text(Path.GetFileName(path), "subtitle"),
                Kit.Button("Open externally", () => OpenExternally(path), "subtle", Icons.External)));
            return;
        }
        if (Path.GetExtension(path) is var video && (video.Equals(".mp4", StringComparison.OrdinalIgnoreCase) || video.Equals(".webm", StringComparison.OrdinalIgnoreCase)) && File.Exists(path))
        {
            ShowWeb(new Uri(path), Kit.Column(4,
                Kit.Text(Path.GetFileName(path), "subtitle"),
                Kit.Text("Playback uses the system web engine; supported codecs depend on this computer.", "caption"),
                Kit.Button("Open externally", () => OpenExternally(path), "subtle", Icons.External)));
            return;
        }
        var header = Kit.Column(4,
            Kit.Text(Path.GetFileName(path), "subtitle").Trimmed(320),
            Kit.Text(Agex.Core.Runtime.Redactor.RedactPaths(path), "caption").Trimmed(320),
            Kit.Row(6,
                Kit.Button("Open", () => OpenExternally(path), "", Icons.External, "Open with the default app"),
                _workspace.PreferredEditor is { } editor ? Kit.Button("Open in " + editor.Name, () => { if (!_workspace.OpenInEditor(path)) _window.Toast("Could not open the editor", "Open the file from the editor itself.", ToastKind.Error); }, "", Icons.Tool) : null,
                Kit.Button("Show in folder", () => Reveal(path), "", Icons.Folder)));
        Control body;
        var extension = Path.GetExtension(path);
        try
        {
            if (!File.Exists(path)) body = Kit.Text("This file no longer exists.", "small");
            else if (ImageExtensions.Contains(extension) && new FileInfo(path).Length < 40L * 1024 * 1024)
            {
                using var stream = File.OpenRead(path);
                var bitmap = new Bitmap(stream);
                body = Kit.Column(6, new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = 900, HorizontalAlignment = HorizontalAlignment.Left }, Kit.Text($"{bitmap.PixelSize.Width} x {bitmap.PixelSize.Height} pixels", "caption"));
            }
            else if (TextExtensions.Contains(extension) || AttachmentService.Classify(path) is AttachmentKind.Text)
            {
                var info = new FileInfo(path);
                var lines = File.ReadLines(path).Take(600).ToList();
                var shortened = lines.Count == 600 || info.Length > 400_000;
                var text = Kit.Selectable(string.Join('\n', lines), "mono");
                text.TextWrapping = TextWrapping.Wrap;
                body = Kit.Column(6, shortened ? Kit.Text("Showing the first 600 lines. Open the file to see all of it.", "caption") : null, text);
            }
            else if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                var info = new FileInfo(path);
                if (info.Length > 40L * 1024 * 1024) body = Kit.Text("This file is too large to preview. Open it in its own app.", "small");
                else
                {
                    var extracted = AttachmentService.ExtractOfficeText(path);
                    var lines = extracted.Split('\n').Take(600).ToList();
                    var preview = Kit.Selectable(string.Join('\n', lines), "mono");
                    preview.TextWrapping = TextWrapping.Wrap;
                    body = Kit.Column(6,
                        Kit.Text(extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? "Spreadsheet values (first 600 rows; formatting and formulas are not shown)" : "Document text (first 600 paragraphs; formatting is not shown)", "caption"),
                        preview);
                }
            }
            else body = Kit.Text("AGEX cannot preview this kind of file. Use Open to view it in its own app.", "small");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            body = Kit.Text("Could not read this file: " + ex.Message, "small");
        }
        _preview.Content = new ScrollViewer { Content = Kit.Column(12, header, body), Padding = new Thickness(16) };
    }

    private void ShowWeb(Uri? uri, Control header)
    {
        _web ??= new WebPreview(_window);
        if (_webPage is null)
        {
            _webPage = new DockPanel { Margin = new Thickness(16) };
            DockPanel.SetDock(_webHeader, Dock.Top);
            _webHeader.Margin = new Thickness(0, 0, 0, 8);
            _webPage.Children.Add(_webHeader);
            _webPage.Children.Add(_web);
        }
        _webHeader.Content = header;
        _preview.Content = _webPage;
        if (uri is null) return;
        // A page inside the project opens through AGEX's local server: browsers block JavaScript modules and fetch() from file://.
        if (uri.IsFile && _window.Workspace.Project is { } project && Agex.Core.Projects.ProjectScanner.IsInside(project.Path, uri.LocalPath)
            && _window.Workspace.LocalServer(project.Path) is { } server)
        {
            var relative = Path.GetRelativePath(project.Path, uri.LocalPath);
            _web.Show(server.UrlFor(relative), null);
            return;
        }
        _web.Show(uri, uri.IsFile ? Path.GetDirectoryName(uri.LocalPath) : null);
    }

    private void ShowSource(string path)
    {
        var text = Kit.Selectable(string.Join('\n', File.ReadLines(path).Take(600)), "mono");
        text.TextWrapping = TextWrapping.Wrap;
        _preview.Content = new ScrollViewer
        {
            Padding = new Thickness(16),
            Content = Kit.Column(12, Kit.Column(4, Kit.Text(Path.GetFileName(path), "subtitle"), Kit.Button("Show the page", () => ShowPreview(path), "", Icons.Search)), text),
        };
    }

    // ------------------------------------------------------------- computer

    private void BuildComputer()
    {
        _computerControls.Children.Clear();
        _computerLog.Children.Clear();
        _computerLog.IsVisible = _workspace.Settings.LiveView != Agex.Core.Settings.LiveViewMode.Off;
        var engine = _workspace.Engine;
        var (state, tone) = engine is null ? ("Not running", Tone.Neutral) : engine.IsPaused ? ("Paused", Tone.Warning) : ("Working", Tone.Accent);
        _computerControls.Children.Add(Kit.Row(8, Kit.Text("Run", "subtitle"), Kit.Badge(state, tone)));
        if (_liveMode.Parent is Panel owner) owner.Children.Remove(_liveMode);
        _computerControls.Children.Add(Kit.Row(8, Kit.Text("Live detail", "caption"), _liveMode));
        if (engine is not null)
        {
            _computerControls.Children.Add(Kit.Wrap(
                engine.IsPaused
                    ? Kit.Button("Return control to AGEX", () => { _workspace.Resume(); Refresh(); }, "primary", Icons.Play)
                    : Kit.Button("Pause", () => { _workspace.Pause(); Refresh(); }, "", Icons.Pause, "Agents finish their current step, then wait"),
                Kit.Button("Take control", () => { _workspace.Pause(); Refresh(); _window.Toast("Paused", "AGEX paused the team. Work in your own apps, then press Resume.", ToastKind.Info); }, "", Icons.Keyboard, "Pause the team so you can work yourself"),
                Kit.Button("Stop", () => { _workspace.Cancel(); Refresh(); }, "danger", Icons.Stop)));
        }
        var intro = Kit.Text("Agents act through their own tools. Actions appear below; AGEX asks before sensitive actions.", "small");
        intro.TextWrapping = TextWrapping.Wrap;
        _computerControls.Children.Add(intro);
        var entries = _workspace.Timeline.TakeLast(_workspace.Settings.LiveView == Agex.Core.Settings.LiveViewMode.Detailed ? 80 : 20).Reverse().ToList();
        if (entries.Count == 0) { _computerLog.Children.Add(Kit.Text("Actions appear here while the team works.", "caption")); return; }
        _computerLog.Children.Add(Kit.Text("Recent actions", "caption"));
        foreach (var entry in entries)
        {
            var tone2 = entry.Kind switch { TimelineKind.Failed => Tone.Danger, TimelineKind.Done => Tone.Success, TimelineKind.Warning or TimelineKind.Approval or TimelineKind.Input => Tone.Warning, _ => Tone.Neutral };
            var text = Kit.Text(Agex.Core.Runtime.Redactor.RedactPaths(entry.Text), "small");
            text.MaxLines = 3;
            text.TextWrapping = TextWrapping.Wrap;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            row.Children.Add(Kit.Icon(Kit.ToneKeys(tone2).Icon, 12, Kit.ToneKeys(tone2).Foreground));
            var column = Kit.Column(0, text, Kit.Text(entry.At.ToLocalTime().ToString("HH:mm:ss"), "caption"));
            Grid.SetColumn(column, 1);
            row.Children.Add(column);
            _computerLog.Children.Add(row);
        }
        if (_workspace.Settings.LiveView == Agex.Core.Settings.LiveViewMode.Detailed)
        {
            _computerLog.Children.Add(Kit.Text("Tool actions", "caption"));
            foreach (var message in _workspace.Messages.Where(message => message.Type == MessageType.ToolEvent).TakeLast(40).Reverse())
                _computerLog.Children.Add(Kit.Text(Agex.Core.Runtime.Redactor.ForSharing($"{message.From}: {message.Text}"), "small"));
        }
    }
}
