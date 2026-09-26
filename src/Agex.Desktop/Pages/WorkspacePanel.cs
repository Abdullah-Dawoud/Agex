using Agex.Core.Attachments;
using Agex.Core.Sessions;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Agex.Desktop.Pages;

/// <summary>
/// Right-hand workspace: the team's live activity, the files a request touched,
/// a preview of the selected file, its diff, and the run controls ("Computer").
/// It only shows what AGEX really knows; it never shows an agent's hidden reasoning.
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
    private readonly TabControl _tabs;
    private readonly TabItem _previewTab, _diffTab;
    private readonly StackPanel _files = new() { Spacing = 8, Margin = new Thickness(16) };
    private readonly ContentControl _preview = new();
    private readonly ContentControl _diff = new();
    private readonly StackPanel _computer = new() { Spacing = 10, Margin = new Thickness(16) };
    // Live Computer view: built once, updated by a timer while it is on screen.
    private readonly StackPanel _computerControls = new() { Spacing = 10 };
    private readonly StackPanel _computerLog = new() { Spacing = 10 };
    private readonly Image _liveImage = new() { Stretch = Stretch.Uniform, MaxHeight = 420, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _liveWindow = Kit.Text("", "small");
    private readonly TextBlock _liveAction = Kit.Text("", "small");
    private readonly TextBlock _liveNote = Kit.Text("", "caption");
    private readonly ToggleSwitch _liveToggle = new() { IsChecked = true, OnContent = "Live screen on", OffContent = "Live screen off" };
    private readonly Avalonia.Threading.DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private TabItem? _computerTab;
    private bool _attached;
    private string? _selected;
    // One web preview for the panel; it is moved between pages only through this fixed container.
    private WebPreview? _web;
    private readonly ContentControl _webHeader = new();
    private DockPanel? _webPage;
    private bool _pending;

    public event Action? CollapseRequested;
    public event Action? PopOutRequested;

    public WorkspacePanel(MainWindow window, ActivityPanel activity)
    {
        _window = window;
        _workspace = window.Workspace;
        _previewTab = new TabItem { Header = "Preview", Content = _preview };
        _diffTab = new TabItem { Header = "Changes", Content = _diff };
        _filesTab = new TabItem { Header = "Files", Content = BuildFilesTab() };
        _connectionsTab = new TabItem { Header = "Connections", Content = new ScrollViewer { Content = _connections } };
        _tabs = new TabControl
        {
            Padding = new Thickness(0),
            ItemsSource = new[]
            {
                new TabItem { Header = "Activity", Content = activity },
                _diffTab,
                _filesTab,
                _previewTab,
                (_computerTab = new TabItem { Header = "Computer", Content = new ScrollViewer { Content = _computer } }),
                _connectionsTab,
            },
        };
        foreach (TabItem tab in _tabs.ItemsSource!) { tab.FontSize = 13; tab.Padding = new Thickness(8, 0); tab.MinHeight = 34; }

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 6, 6, 0) };
        header.Children.Add(Kit.Text("Workspace", "subtitle").Left());
        var actions = Kit.Row(2,
            Kit.IconButton(Icons.External, "Open the workspace panel in its own window", () => PopOutRequested?.Invoke()),
            Kit.IconButton(Icons.Chevron, "Hide the workspace panel", () => CollapseRequested?.Invoke()));
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_tabs);
        Content = layout;

        _workspace.SessionChanged += RefreshSoon;
        _workspace.Timeline.CollectionChanged += (_, _) => RefreshSoon();
        _workspace.PendingAttachments.CollectionChanged += (_, _) => RefreshSoon();
        _workspace.ProjectChanged += () => { _treeProject = null; RefreshSoon(); };
        _workspace.ConnectionsChanged += RefreshSoon;
        _workspace.SettingsChanged += RefreshSoon;
        BuildLiveView();
        ShowPreview(null);
        Refresh();
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
        BuildTree();
        BuildConnections();
    }

    /// <summary>Opens a file in the Preview tab (used by the Files tab and by other pages).</summary>
    public void Open(string path)
    {
        ShowPreview(path);
        _tabs.SelectedItem = _previewTab;
    }

    // ---------------------------------------------------------------- files

    private void BuildFiles()
    {
        _files.Children.Clear();
        var session = _workspace.Session;
        var pending = _workspace.PendingAttachments.ToList();
        var attached = _workspace.LastAttachments;
        var changes = session?.Changes.ToList() ?? [];
        var reports = session?.Artifacts.Where(item => item.Kind is ArtifactKind.Report or ArtifactKind.Patch && File.Exists(item.Path)).ToList() ?? [];

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
            _files.Children.Add(Kit.Text("Reports", "caption"));
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
            diff && relative is not null ? Kit.IconButton(Icons.Graph, "Show changes", () => _ = ShowFileDiffAsync(relative)) : null,
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
        _selected = path;
        if (path is null)
        {
            _preview.Content = Kit.EmptyState(Icons.Search, "Preview", "Choose a file in Files. Web pages, images, text and code show here; other files open in their own app.",
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

    private void BuildLiveView()
    {
        _liveWindow.TextWrapping = TextWrapping.Wrap;
        _liveAction.TextWrapping = TextWrapping.Wrap;
        _liveNote.TextWrapping = TextWrapping.Wrap;
        Avalonia.Automation.AutomationProperties.SetName(_liveToggle, "Live screen");
        Avalonia.Automation.AutomationProperties.SetName(_liveImage, "Current screen");
        _liveToggle.IsCheckedChanged += (_, _) => { UpdateLive(); if (_liveToggle.IsChecked == true) CaptureFrame(); };
        _liveTimer.Tick += (_, _) => CaptureFrame();
        _tabs.SelectionChanged += (_, _) => UpdateLive();
        AttachedToVisualTree += (_, _) => { _attached = true; UpdateLive(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; _liveTimer.Stop(); };
        var frame = new Border { Child = _liveImage, CornerRadius = new CornerRadius(6), ClipToBounds = true, BorderThickness = new Thickness(1), MinHeight = 60 };
        frame.Res(Border.BorderBrushProperty, "BorderBrush");
        var live = Kit.Column(6,
            Kit.Row(8, Kit.Text("Screen", "subtitle"), _liveToggle),
            _liveNote,
            frame,
            Kit.Row(6, Kit.Text("Window:", "caption"), _liveWindow),
            Kit.Row(6, Kit.Text("Latest action:", "caption"), _liveAction),
            Kit.Button("Show the screen now", CaptureFrame, "subtle", Icons.Refresh, "Take one screenshot now"));
        _computer.Children.Add(_computerControls);
        _computer.Children.Add(Kit.Card(live, 12));
        _computer.Children.Add(_computerLog);
        UpdateLive();
    }

    /// <summary>Screenshots run only while the Computer tab is shown, the switch is on and a request is running.</summary>
    private void UpdateLive()
    {
        var visible = _tabs.SelectedItem == _computerTab && _attached;
        var on = _liveToggle.IsChecked == true && ScreenCapture.IsSupported;
        _liveNote.Text = !ScreenCapture.IsSupported ? ScreenCapture.UnsupportedReason + " The actions below still show what agents do."
            : !on ? "Live screen is off. Turn it on to see what the agents do on this computer."
            : _workspace.Engine is null ? "Screenshots start when a request runs. They stay in memory on this computer: they are not saved or sent."
            : "A screenshot every 2 seconds while the team works. Screenshots stay in memory on this computer: they are not saved or sent.";
        if (visible && on && _workspace.Engine is not null) _liveTimer.Start(); else _liveTimer.Stop();
        UpdateLatestAction();
    }

    private void CaptureFrame()
    {
        if (!ScreenCapture.IsSupported || _liveToggle.IsChecked != true) return;
        var old = _liveImage.Source as IDisposable;
        _liveImage.Source = ScreenCapture.Capture(Path.Combine(_workspace.Core.Platform.Paths.CacheRoot, "screen"));
        old?.Dispose();
        _liveWindow.Text = ScreenCapture.ForegroundWindowTitle() is { Length: > 0 } title ? Agex.Core.Runtime.Redactor.RedactPaths(title) : "Not reported on this system";
        if (_liveImage.Source is null) _liveNote.Text = OperatingSystem.IsMacOS() ? "macOS did not allow the screenshot. Allow AGEX under System Settings > Privacy & Security > Screen Recording." : "The screenshot failed.";
        UpdateLatestAction();
    }

    /// <summary>The newest tool event an agent reported, or the newest timeline step. Never an agent's reasoning.</summary>
    private void UpdateLatestAction()
    {
        var tool = _workspace.Messages.LastOrDefault(message => message.Type == Agex.Core.Sessions.MessageType.ToolEvent);
        var step = _workspace.Timeline.LastOrDefault();
        _liveAction.Text = tool is not null && (step is null || tool.At >= step.At) ? $"{tool.From}: {Agex.Core.Runtime.Redactor.RedactPaths(tool.Text)}"
            : step is not null ? Agex.Core.Runtime.Redactor.RedactPaths(step.Text) : "None yet";
        _liveAction.MaxLines = 3;
    }

    private void BuildComputer()
    {
        _computerControls.Children.Clear();
        _computerLog.Children.Clear();
        var engine = _workspace.Engine;
        var (state, tone) = engine is null ? ("Not running", Tone.Neutral) : engine.IsPaused ? ("Paused", Tone.Warning) : ("Working", Tone.Accent);
        _computerControls.Children.Add(Kit.Row(8, Kit.Text("Run", "subtitle"), Kit.Badge(state, tone)));
        if (engine is not null)
        {
            _computerControls.Children.Add(Kit.Wrap(
                engine.IsPaused
                    ? Kit.Button("Resume", () => { _workspace.Resume(); Refresh(); }, "primary", Icons.Play)
                    : Kit.Button("Pause", () => { _workspace.Pause(); Refresh(); }, "", Icons.Pause, "Agents finish their current step, then wait"),
                Kit.Button("Take control", () => { _workspace.Pause(); Refresh(); _window.Toast("Paused", "AGEX paused the team. Work in your own apps, then press Resume.", ToastKind.Info); }, "", Icons.Keyboard, "Pause the team so you can work yourself"),
                Kit.Button("Stop", () => { _workspace.Cancel(); Refresh(); }, "danger", Icons.Stop)));
        }
        var intro = Kit.Text("AGEX itself does not move your mouse or type into other apps. Agents act through their own tools (a browser, or a computer-use skill you installed); the screen and the list below show what they do. Actions that need your approval always ask first.", "small");
        intro.TextWrapping = TextWrapping.Wrap;
        _computerControls.Children.Add(intro);
        UpdateLive();
        var entries = _workspace.Timeline.TakeLast(40).Reverse().ToList();
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
    }
}
