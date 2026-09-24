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
public sealed class WorkspacePanel : UserControl
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
    private string? _selected;
    private bool _pending;

    public event Action? CollapseRequested;
    public event Action? PopOutRequested;

    public WorkspacePanel(MainWindow window, ActivityPanel activity)
    {
        _window = window;
        _workspace = window.Workspace;
        _previewTab = new TabItem { Header = "Preview", Content = _preview };
        _diffTab = new TabItem { Header = "Diff", Content = _diff };
        _tabs = new TabControl
        {
            Padding = new Thickness(0),
            ItemsSource = new[]
            {
                new TabItem { Header = "Activity", Content = activity },
                new TabItem { Header = "Files", Content = new ScrollViewer { Content = _files } },
                _previewTab,
                _diffTab,
                new TabItem { Header = "Computer", Content = new ScrollViewer { Content = _computer } },
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
        _workspace.ProjectChanged += RefreshSoon;
        ShowPreview(null);
        ShowDiffPlaceholder("Select a changed file in Files to see what changed.");
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

        if (pending.Count + attached.Count + changes.Count + reports.Count == 0)
        {
            _files.Children.Add(Kit.EmptyState(Icons.Folder, "Nothing here yet", "Files you attach, files the team changes and reports it writes appear here."));
            return;
        }
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
        if (changes.Count > 0 && _workspace.Project is { } project)
        {
            _files.Children.Add(Kit.Text($"Changed by the team ({changes.Count})", "caption"));
            foreach (var change in changes.Take(200)) _files.Children.Add(FileRow(Path.Combine(project.Path, change.Path), change.Kind, diff: change.Kind != "deleted", relative: change.Path));
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
            diff && relative is not null ? Kit.IconButton(Icons.Graph, "Show changes", () => _ = ShowDiffAsync(relative)) : null,
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
            _preview.Content = Kit.EmptyState(Icons.Search, "Preview", "Choose a file in Files. Images, text and code show here; other files open in their own app.");
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
                if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
                    body = Kit.Column(6, Kit.Text("This is the page's source. Use Open to see it rendered in your browser.", "caption"), body);
            }
            else body = Kit.Text("AGEX cannot preview this kind of file. Use Open to view it in its own app.", "small");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            body = Kit.Text("Could not read this file: " + ex.Message, "small");
        }
        _preview.Content = new ScrollViewer { Content = Kit.Column(12, header, body), Padding = new Thickness(16) };
    }

    // ----------------------------------------------------------------- diff

    private void ShowDiffPlaceholder(string text) => _diff.Content = Kit.EmptyState(Icons.Graph, "Changes", text);

    private async Task ShowDiffAsync(string relative)
    {
        _tabs.SelectedItem = _diffTab;
        if (_workspace.Project is not { } project) { ShowDiffPlaceholder("Open a project first."); return; }
        _diff.Content = Kit.Text("Loading changes...", "small");
        // Compare with the snapshot AGEX took before the team edited files, when there is one.
        var baseRef = _workspace.Session?.SnapshotRef is { Length: > 0 } snapshot ? snapshot : null;
        string text;
        try { text = await _workspace.Core.Git.DiffAsync(project.Path, relative, baseRef); }
        catch (Exception ex) { text = ""; _workspace.Core.Log.Error("panel_diff_failed", ex); }
        if (text.Length == 0) { ShowDiffPlaceholder(_workspace.Core.Git.IsRepository(project.Path) ? "No changes to show for this file." : "Diffs need a Git repository. Use Preview to see the file."); return; }
        var lines = new StackPanel();
        foreach (var line in text.Split('\n').Take(3000))
        {
            var block = Kit.Text(line.Length == 0 ? " " : line, "mono");
            block.TextWrapping = TextWrapping.Wrap;
            if (line.StartsWith('+') && !line.StartsWith("+++")) { block.Res(TextBlock.ForegroundProperty, "SuccessBrush"); }
            else if (line.StartsWith('-') && !line.StartsWith("---")) { block.Res(TextBlock.ForegroundProperty, "DangerBrush"); }
            else if (line.StartsWith("@@")) block.Res(TextBlock.ForegroundProperty, "InfoBrush");
            lines.Children.Add(block);
        }
        var header = Kit.Column(4, Kit.Text(relative, "subtitle").Trimmed(320), Kit.Text(baseRef is null ? "Compared with the last commit" : "Compared with the snapshot taken before this request", "caption"),
            Kit.Row(6, Kit.Button("Preview", () => Open(Path.Combine(project.Path, relative)), "", Icons.Search)));
        _diff.Content = new ScrollViewer { Content = Kit.Column(12, header, lines), Padding = new Thickness(16), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    // ------------------------------------------------------------- computer

    private void BuildComputer()
    {
        _computer.Children.Clear();
        var engine = _workspace.Engine;
        var (state, tone) = engine is null ? ("Not running", Tone.Neutral) : engine.IsPaused ? ("Paused", Tone.Warning) : ("Working", Tone.Accent);
        _computer.Children.Add(Kit.Row(8, Kit.Text("Run", "subtitle"), Kit.Badge(state, tone)));
        if (engine is not null)
        {
            _computer.Children.Add(Kit.Wrap(
                engine.IsPaused
                    ? Kit.Button("Resume", () => { _workspace.Resume(); Refresh(); }, "primary", Icons.Play)
                    : Kit.Button("Pause", () => { _workspace.Pause(); Refresh(); }, "", Icons.Pause, "Agents finish their current step, then wait"),
                Kit.Button("Take control", () => { _workspace.Pause(); Refresh(); _window.Toast("Paused", "AGEX paused the team. Work in your own apps, then press Resume.", ToastKind.Info); }, "", Icons.Keyboard, "Pause the team so you can work yourself"),
                Kit.Button("Stop", () => { _workspace.Cancel(); Refresh(); }, "danger", Icons.Stop)));
        }
        _computer.Children.Add(Kit.Text("AGEX does not move your mouse or type into other apps. Agents act only through their own tools, which this list reports. Actions that need your approval always ask first.", "small"));
        var entries = _workspace.Timeline.TakeLast(40).Reverse().ToList();
        if (entries.Count == 0) { _computer.Children.Add(Kit.Text("Actions appear here while the team works.", "caption")); return; }
        _computer.Children.Add(Kit.Text("Recent actions", "caption"));
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
            _computer.Children.Add(row);
        }
    }
}
