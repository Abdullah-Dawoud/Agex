using Agex.Core.Connections;
using Agex.Core.Projects;
using Agex.Core.Sessions;
using Agex.Core.Teams;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>Changes (with line counts and diffs), the project tree and team-aware connections.</summary>
public sealed partial class WorkspacePanel
{
    private readonly TabItem _filesTab, _connectionsTab;
    private readonly StackPanel _connections = new() { Spacing = 10, Margin = new Thickness(16) };
    private readonly TreeView _tree = new() { MinHeight = 200 };
    private readonly StackPanel _treeNotes = new() { Spacing = 4 };
    private string? _treeProject;
    private bool _showingDiff;
    private bool _sideBySide;
    private ConnectionUi? _connectionUi;

    /// <summary>Opens the Changes tab (the change summary on Home links here).</summary>
    public void ShowChanges()
    {
        _showingDiff = false;
        BuildChanges();
        _tabs.SelectedItem = _diffTab;
    }

    private Control BuildFilesTab() => new ScrollViewer
    {
        Content = new Border { Padding = new Thickness(4, 12, 4, 12), Child = Kit.Column(10, _files, _treeNotes, _tree) },
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };

    // -------------------------------------------------------------- changes

    private static (string Letter, string Brush, string Label) Look(FileChange change) => change.Kind switch
    {
        "added" => ("A", "SuccessBrush", "Added"),
        "deleted" => ("D", "DangerBrush", "Deleted"),
        "renamed" => ("R", "InfoBrush", "Renamed"),
        _ => ("M", "WarningBrush", "Modified"),
    };

    private void BuildChanges()
    {
        var session = _workspace.Session;
        var changes = session?.Changes.ToList() ?? [];
        if (changes.Count == 0)
        {
            _diff.Content = Kit.EmptyState(Icons.Graph, "No changes yet", _workspace.IsRunning ? "Files the team creates, edits or deletes appear here as it works." : "When agents create, edit or delete files, you see them here with the lines added and removed.");
            return;
        }
        var added = changes.Sum(change => change.Added ?? 0);
        var removed = changes.Sum(change => change.Removed ?? 0);
        var list = Kit.Column(2);
        foreach (var group in changes.GroupBy(change => change.Kind switch { "added" => 0, "renamed" => 1, "deleted" => 3, _ => 2 }).OrderBy(group => group.Key))
            foreach (var change in group.OrderBy(change => change.Path, StringComparer.OrdinalIgnoreCase).Take(300))
                list.Children.Add(ChangeRow(change));
        var header = Kit.Row(10, Kit.Text($"{changes.Count} file{(changes.Count == 1 ? "" : "s")}", "subtitle"), Counts(added, removed, "body"));
        _diff.Content = new ScrollViewer { Padding = new Thickness(12), Content = Kit.Column(10, header, list), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private static Control Counts(int? added, int? removed, string classes = "small")
    {
        if (added is null && removed is null) return Kit.Text("binary", "caption");
        var plus = Kit.Text($"+{added ?? 0}", classes);
        plus.Res(TextBlock.ForegroundProperty, "SuccessBrush");
        var minus = Kit.Text($"-{removed ?? 0}", classes);
        minus.Res(TextBlock.ForegroundProperty, "DangerBrush");
        return Kit.Row(6, plus, minus);
    }

    private Control ChangeRow(FileChange change)
    {
        var (letter, brush, label) = Look(change);
        var badge = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Child = new TextBlock { Text = letter, FontWeight = FontWeight.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        badge.Res(Border.BackgroundProperty, brush);
        ((TextBlock)badge.Child).Foreground = Brushes.White;
        ToolTip.SetTip(badge, label);
        var name = Kit.Text(change.Path, "small").Trimmed(220);
        var detail = change.Kind == "renamed" ? Kit.Text("from " + change.OldPath, "caption").Trimmed(220) : null;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(badge);
        var info = Kit.Column(0, name, detail);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        var counts = Counts(change.Added, change.Removed);
        Grid.SetColumn(counts, 2);
        counts.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(counts);
        var button = new Button { Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 4) };
        button.Classes.Add("subtle");
        button.Click += (_, _) => _ = ShowFileDiffAsync(change.Path);
        AutomationProperties.SetName(button, $"{label} {change.Path}, {change.Added ?? 0} lines added, {change.Removed ?? 0} removed. View diff.");
        return button;
    }

    /// <summary>
    /// Diff for one file: from the text AGEX kept when the request started
    /// (works without Git), else from Git. Unified or side by side.
    /// </summary>
    private async Task ShowFileDiffAsync(string relative)
    {
        _tabs.SelectedItem = _diffTab;
        _showingDiff = true;
        if (_workspace.Project is not { } project) return;
        var change = _workspace.Session?.Changes.FirstOrDefault(item => item.Path == relative) ?? new FileChange { Path = relative, Kind = "modified" };
        var baseline = _workspace.Baseline;
        var beforePath = change.Kind == "renamed" ? change.OldPath : relative;
        var before = change.Kind == "added" ? "" : baseline?.Before(beforePath);
        var after = change.Kind == "deleted" ? "" : TextBaseline.ReadText(Path.Combine(project.Path, relative));
        var (_, _, label) = Look(change);
        var back = Kit.Button("All changes", ShowChanges, "subtle", Icons.Undo);
        var toggle = Kit.Button(_sideBySide ? "Unified" : "Side by side", () => { _sideBySide = !_sideBySide; _ = ShowFileDiffAsync(relative); }, "subtle", Icons.SidePanel, "Switch the diff layout");
        var header = Kit.Column(4,
            Kit.Wrap(back, toggle, change.Kind != "deleted" ? Kit.Button("Preview", () => Open(Path.Combine(project.Path, relative)), "subtle", Icons.Search) : null),
            Kit.Text(relative, "subtitle").Trimmed(360),
            Kit.Row(8, Kit.Text(label, "caption"), Counts(change.Added, change.Removed)));
        Control body;
        if (before is not null && after is not null)
        {
            var diff = LineDiff.Compute(LineDiff.SplitLines(before), LineDiff.SplitLines(after));
            body = diff is null ? Kit.Text("This file changed too much to compare line by line. Use Preview to see it.", "small")
                : _sideBySide ? SideBySide(diff) : Unified(diff);
        }
        else
        {
            body = await GitDiffAsync(project.Path, relative);
        }
        _diff.Content = new ScrollViewer { Content = Kit.Column(10, header, body), Padding = new Thickness(12) };
    }

    private static Control Unified(IReadOnlyList<DiffLine> diff)
    {
        var lines = new StackPanel();
        foreach (var line in Collapse(diff).Take(4000))
        {
            if (line is null) { lines.Children.Add(Kit.Text("   ...", "caption")); continue; }
            var prefix = line.Kind switch { DiffLineKind.Added => "+ ", DiffLineKind.Removed => "- ", _ => "  " };
            var number = (line.NewNumber ?? line.OldNumber)?.ToString().PadLeft(5) ?? "     ";
            var block = Kit.Text($"{number} {prefix}{line.Text}", "mono");
            block.TextWrapping = TextWrapping.Wrap;
            var row = new Border { Child = block, Padding = new Thickness(4, 0) };
            if (line.Kind == DiffLineKind.Added) row.Res(Border.BackgroundProperty, "SuccessSoftBrush");
            else if (line.Kind == DiffLineKind.Removed) row.Res(Border.BackgroundProperty, "DangerSoftBrush");
            lines.Children.Add(row);
        }
        return lines;
    }

    /// <summary>Old on the left, new on the right; removed and added lines of one edit are paired row by row.</summary>
    private static Control SideBySide(IReadOnlyList<DiffLine> diff)
    {
        var rows = new StackPanel();
        var removed = new List<DiffLine>();
        var added = new List<DiffLine>();
        void Flush()
        {
            for (var index = 0; index < Math.Max(removed.Count, added.Count); index++)
                rows.Children.Add(PairRow(index < removed.Count ? removed[index] : null, index < added.Count ? added[index] : null));
            removed.Clear();
            added.Clear();
        }
        foreach (var line in Collapse(diff).Take(4000))
        {
            if (line is null) { Flush(); rows.Children.Add(Kit.Text("   ...", "caption")); continue; }
            if (line.Kind == DiffLineKind.Removed) { removed.Add(line); continue; }
            if (line.Kind == DiffLineKind.Added) { added.Add(line); continue; }
            Flush();
            rows.Children.Add(PairRow(line, line));
        }
        Flush();
        return rows;
    }

    private static Control PairRow(DiffLine? left, DiffLine? right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 2 };
        grid.Children.Add(Cell(left, left?.OldNumber, left?.Kind == DiffLineKind.Removed ? "DangerSoftBrush" : null));
        var rightCell = Cell(right, right?.NewNumber, right?.Kind == DiffLineKind.Added ? "SuccessSoftBrush" : null);
        Grid.SetColumn(rightCell, 1);
        grid.Children.Add(rightCell);
        return grid;
    }

    private static Control Cell(DiffLine? line, int? number, string? background)
    {
        var text = Kit.Text(line is null ? "" : $"{number?.ToString().PadLeft(4) ?? "    "} {line.Text}", "mono");
        text.TextWrapping = TextWrapping.Wrap;
        var border = new Border { Child = text, Padding = new Thickness(4, 0), MinHeight = 16 };
        if (background is not null) border.Res(Border.BackgroundProperty, background);
        return border;
    }

    /// <summary>Shows changes with 3 lines of context; long unchanged stretches become "...".</summary>
    private static IEnumerable<DiffLine?> Collapse(IReadOnlyList<DiffLine> diff)
    {
        const int context = 3;
        var keep = new bool[diff.Count];
        for (var index = 0; index < diff.Count; index++)
            if (diff[index].Kind != DiffLineKind.Same)
                for (var near = Math.Max(0, index - context); near <= Math.Min(diff.Count - 1, index + context); near++) keep[near] = true;
        var gap = false;
        for (var index = 0; index < diff.Count; index++)
        {
            if (keep[index]) { gap = false; yield return diff[index]; }
            else if (!gap) { gap = true; yield return null; }
        }
    }

    private async Task<Control> GitDiffAsync(string project, string relative)
    {
        var baseRef = _workspace.Session?.SnapshotRef is { Length: > 0 } snapshot ? snapshot : null;
        string text;
        try { text = await _workspace.Core.Git.DiffAsync(project, relative, baseRef); }
        catch (Exception ex) { text = ""; _workspace.Core.Log.Error("panel_diff_failed", ex); }
        if (text.Length == 0)
            return Kit.Text(_workspace.Core.Git.IsRepository(project)
                ? "No saved copy of this file from before the request (AGEX keeps one in memory while it is open) and Git shows no change."
                : "No saved copy of this file from before the request. AGEX keeps one in memory for requests made while it is open.", "small");
        var lines = new StackPanel();
        foreach (var line in text.Split('\n').Take(3000))
        {
            var block = Kit.Text(line.Length == 0 ? " " : line, "mono");
            block.TextWrapping = TextWrapping.Wrap;
            if (line.StartsWith('+') && !line.StartsWith("+++")) block.Res(TextBlock.ForegroundProperty, "SuccessBrush");
            else if (line.StartsWith('-') && !line.StartsWith("---")) block.Res(TextBlock.ForegroundProperty, "DangerBrush");
            else if (line.StartsWith("@@")) block.Res(TextBlock.ForegroundProperty, "InfoBrush");
            lines.Children.Add(block);
        }
        return Kit.Column(6, Kit.Text(baseRef is null ? "From Git: compared with the last commit" : "From Git: compared with the snapshot taken before this request", "caption"), lines);
    }

    // ---------------------------------------------------------------- tree

    /// <summary>Project folders and files (lazy), with created and modified files highlighted. Not an editor: a click previews.</summary>
    private void BuildTree()
    {
        var project = _workspace.Project;
        var changes = _workspace.Session?.Changes.ToDictionary(change => change.Path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, FileChange>(StringComparer.OrdinalIgnoreCase);
        _treeNotes.Children.Clear();
        if (project is null) { _tree.ItemsSource = null; _treeNotes.Children.Add(Kit.Text("Open a project to see its files.", "small")); return; }
        _treeNotes.Children.Add(Kit.Row(8, Kit.Text(project.Name, "subtitle"), Kit.IconButton(Icons.Refresh, "Reload the file tree", () => { _treeProject = null; BuildTree(); })));
        var deleted = changes.Values.Where(change => change.Kind == "deleted").ToList();
        if (deleted.Count > 0) _treeNotes.Children.Add(Kit.Text("Deleted: " + string.Join(", ", deleted.Take(8).Select(change => change.Path)) + (deleted.Count > 8 ? $" and {deleted.Count - 8} more" : ""), "caption"));
        var key = project.Path + "|" + string.Join(",", changes.Keys);
        if (_treeProject == key) return;
        _treeProject = key;
        _tree.ItemsSource = Children(project.Path, project.Path, changes, project.IgnoredFolders);
    }

    private List<TreeViewItem> Children(string root, string folder, IReadOnlyDictionary<string, FileChange> changes, IReadOnlyList<string> ignored)
    {
        var items = new List<TreeViewItem>();
        try
        {
            var ignore = new HashSet<string>(ProjectScanner.DefaultIgnored.Concat(ignored), StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.GetDirectories(folder).Where(dir => !ignore.Contains(Path.GetFileName(dir))).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Take(400))
            {
                var relative = Path.GetRelativePath(root, directory).Replace('\\', '/');
                var touched = changes.Keys.Any(path => path.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase));
                var header = Kit.Row(6, Kit.Icon(Icons.Folder, 14, touched ? "WarningBrush" : "Text3Brush"), Kit.Text(Path.GetFileName(directory), "small"));
                var item = new TreeViewItem { Header = header, ItemsSource = new[] { new TreeViewItem { Header = Kit.Text("Loading...", "caption") } } };
                AutomationProperties.SetName(item, Path.GetFileName(directory) + (touched ? " (has changes)" : ""));
                var loaded = false;
                var captured = directory;
                item.PropertyChanged += (_, e) =>
                {
                    if (e.Property != TreeViewItem.IsExpandedProperty || !item.IsExpanded || loaded) return;
                    loaded = true;
                    item.ItemsSource = Children(root, captured, changes, ignored);
                };
                items.Add(item);
            }
            foreach (var file in Directory.GetFiles(folder).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Take(1000))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                changes.TryGetValue(relative, out var change);
                var name = Kit.Text(Path.GetFileName(file), "small");
                var row = Kit.Row(6, name);
                if (change is not null)
                {
                    var (letter, brush, label) = Look(change);
                    name.Res(TextBlock.ForegroundProperty, brush);
                    var mark = Kit.Text(letter, "caption");
                    mark.Res(TextBlock.ForegroundProperty, brush);
                    row.Children.Add(mark);
                    ToolTip.SetTip(row, $"{label}: +{change.Added ?? 0} -{change.Removed ?? 0}");
                }
                var item = new TreeViewItem { Header = row };
                AutomationProperties.SetName(item, Path.GetFileName(file) + (change is null ? "" : $" ({Look(change).Label})"));
                var path = file;
                item.Tapped += (_, e) => { e.Handled = true; if (change is not null && change.Kind != "added") _ = ShowFileDiffAsync(relative); else Open(path); };
                items.Add(item);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return items;
    }

    // --------------------------------------------------------- connections

    /// <summary>Connections for the team in use first, then everything already connected.</summary>
    private void BuildConnections()
    {
        _connections.Children.Clear();
        _connectionUi ??= new ConnectionUi(_window);
        var all = _workspace.Connections();
        var team = JobTeamCatalog.Get(_workspace.Settings.ActiveJobTeam);
        var recommended = ConnectionService.ForTeam(all, team?.Id);
        if (recommended.Count > 0)
        {
            var ready = recommended.Count(item => item.State == ConnectionState.Connected);
            _connections.Children.Add(Kit.Text($"Recommended for {team!.Name} ({ready}/{recommended.Count} ready)", "subtitle"));
            foreach (var item in recommended) _connections.Children.Add(_connectionUi.Row(item));
        }
        else _connections.Children.Add(Kit.Text("Pick a team on Home to see the connections it needs.", "small"));
        var connected = all.Where(item => item.State == ConnectionState.Connected && recommended.All(other => other.Id != item.Id)).ToList();
        if (connected.Count > 0)
        {
            _connections.Children.Add(Kit.Text("Also connected", "caption"));
            foreach (var item in connected.Take(12)) _connections.Children.Add(_connectionUi.Row(item));
        }
        _connections.Children.Add(Kit.Wrap(
            Kit.Button("All connections", () => _window.Navigate("connections"), "subtle", Icons.Plug),
            Kit.Button("Add connection", () => _ = new AddConnectionDialog(_window, _connectionUi).ShowAsync(), "subtle", Icons.Plus)));
    }
}
