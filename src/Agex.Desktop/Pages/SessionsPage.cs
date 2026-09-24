using Agex.Core.Sessions;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Agex.Desktop.Pages;

/// <summary>Session history: search, review, continue, retry, clone, export, undo and delete.</summary>
public sealed class SessionsPage(MainWindow window) : AppPage(window)
{
    private readonly TextBox _search = new() { PlaceholderText = "Search all sessions, messages and results", MinWidth = 320 };
    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly ContentControl _detail = new();
    private string? _selected;

    public override string Id => "sessions";
    public override string Title => "Sessions";
    public override string Icon => Icons.History;

    protected override Control Build()
    {
        AutomationProperties.SetName(_search, "Search sessions");
        _search.TextChanged += (_, _) => RefreshList();
        Workspace.SessionChanged += () => { if (!Workspace.IsRunning) RefreshList(); };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("360,*"), ColumnSpacing = 20 };
        grid.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 2000 });
        Grid.SetColumn(_detail, 1);
        grid.Children.Add(_detail);
        var retention = Workspace.Settings.Sessions.RetentionDays == 0 ? "kept until you delete them" : $"kept for {Workspace.Settings.Sessions.RetentionDays} days";
        var page = Kit.Column(12, Kit.PageHeader("Sessions", $"Every request is saved on this computer ({retention}; change in Settings)."), _search, grid);
        var view = Kit.Page(page, 1300);
        view.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 860;
            grid.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("360,*");
            grid.RowDefinitions = narrow ? new RowDefinitions("Auto,Auto") : new RowDefinitions("*");
            Grid.SetColumn(_detail, narrow ? 0 : 1);
            Grid.SetRow(_detail, narrow ? 1 : 0);
            grid.RowSpacing = 20;
        };
        RefreshList();
        return view;
    }

    public override void OnShown() => RefreshList();
    public void FocusSearch() => Avalonia.Threading.Dispatcher.UIThread.Post(() => _search.Focus());
    public void Select(string id) { _selected = id; RefreshList(); }

    private void RefreshList()
    {
        _list.Children.Clear();
        var query = _search.Text?.Trim() ?? "";
        if (query.Length > 0)
        {
            var hits = Workspace.Core.Sessions.Search(query);
            if (hits.Count == 0) _list.Children.Add(Kit.Text("No matches.", "small"));
            foreach (var group in hits.GroupBy(hit => hit.SessionId))
            {
                var first = group.First();
                var button = ListButton(first.SessionId, first.SessionTitle, $"{group.Count()} match{(group.Count() == 1 ? "" : "es")} · {Path.GetFileName(first.Project.TrimEnd('/', '\\'))}", null);
                ((StackPanel)button.Content!).Children.Add(Kit.Text($"{first.Where}: {first.Snippet}", "caption"));
                _list.Children.Add(button);
            }
        }
        else
        {
            var sessions = Workspace.Core.Sessions.List();
            if (sessions.Count == 0) { _list.Children.Add(Kit.EmptyState(Icons.History, "No sessions yet", "Your requests appear here.")); _detail.Content = null; return; }
            foreach (var session in sessions.Take(300))
                _list.Children.Add(ListButton(session.Id, session.Title, $"{Kit.Ago(session.CreatedAt)} · {Path.GetFileName(session.Project.TrimEnd('/', '\\'))} · {session.Tasks} tasks", session.Status));
            _selected ??= sessions[0].Id;
        }
        _detail.Content = _selected is not null && Workspace.Core.Sessions.Load(_selected) is { } selected ? Detail(selected) : null;
    }

    private Button ListButton(string id, string title, string meta, SessionStatus? status)
    {
        var titleBlock = Kit.Text(title, "body");
        titleBlock.MaxLines = 2;
        titleBlock.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        Control? badge = null;
        if (status is { } value) { var (text, tone, icon) = HomePage.StatusLook(value); badge = Kit.Badge(text, tone, icon); badge.HorizontalAlignment = HorizontalAlignment.Left; }
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8), Content = Kit.Column(4, titleBlock, Kit.Row(8, badge, Kit.Text(meta, "caption"))) };
        button.Classes.Add("nav");
        if (id == _selected) button.Classes.Add("active");
        button.Click += (_, _) => { _selected = id; RefreshList(); };
        AutomationProperties.SetName(button, title);
        return button;
    }

    private Control Detail(Session session)
    {
        var (text, tone, icon) = HomePage.StatusLook(session.Status);
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        titleRow.Children.Add(Kit.Text(session.Title, "title"));
        var statusBadge = Kit.Badge(text, tone, icon);
        statusBadge.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(statusBadge, 1);
        titleRow.Children.Add(statusBadge);
        var header = Kit.Column(6, titleRow,
            Kit.Text($"{session.CreatedAt.ToLocalTime():d MMM yyyy HH:mm} · {Agex.Core.Runtime.Redactor.RedactPaths(session.Project)} · agents: {string.Join(", ", session.Agents)} · leader {session.Leader}", "small"));
        var actions = Kit.Wrap(
            Kit.Button("Show in Agent Room", () => { Workspace.ShowSession(session); Window.Navigate("room"); }, "", Icons.Room),
            Kit.Button("Continue", () => { Workspace.ShowSession(session); Window.Navigate("home"); Window.Page<HomePage>("home").FocusComposer(clear: true); }, "", Icons.Send, "Open it in Home, then use Continue on the result"),
            Kit.Button("Retry", () => { if (Workspace.Project?.Path != session.Project && Directory.Exists(session.Project)) Workspace.OpenProject(session.Project); _ = Workspace.StartAsync(session.Request); Window.Navigate("home"); }, "", Icons.Refresh),
            Kit.Button("Clone", () => { if (Directory.Exists(session.Project)) Workspace.OpenProject(session.Project); Window.Navigate("home"); Window.Page<HomePage>("home").SetRequest(session.Request); }, "", Icons.Copy, "Copy the request into a new draft you can edit"),
            Kit.Button("Export summary", () => _ = ExportAsync(session), "", Icons.Download),
            session.SnapshotRef.Length > 0 ? Kit.Button("Undo changes", () => _ = UndoAsync(session), "danger", Icons.Undo) : null,
            Kit.Button("Delete", async () =>
            {
                if (!await Window.ConfirmAsync("Delete this session?", "Its history is removed from AGEX. Project files are not touched.", "Delete", "Cancel")) return;
                Workspace.Core.Sessions.Delete(session.Id);
                _selected = null;
                RefreshList();
            }, "subtle danger", Icons.Trash));
        var request = Kit.Card(Kit.Column(6, Kit.Text("Request", "caption"), Kit.Selectable(session.Request, "body")), 12);
        var result = session.Outcome is { } outcome ? Kit.Card(Kit.Column(6, Kit.Text("Result", "caption"), Kit.Text(outcome.Headline, "subtitle"), outcome.Reason.Length > 0 ? Kit.Selectable(outcome.Reason, "body") : null), 12) : null;
        var timeline = Kit.Column(4);
        foreach (var entry in session.Timeline) timeline.Children.Add(Kit.Row(8, Kit.Text(entry.At.ToLocalTime().ToString("HH:mm:ss"), "caption"), Kit.Text(entry.Text, "small")));
        var artifacts = Kit.Column(4);
        foreach (var artifact in session.Artifacts.Take(100))
        {
            var path = artifact.Path;
            var row = Kit.Row(8, Kit.Badge(artifact.Kind.ToString(), artifact.Kind == ArtifactKind.Snapshot ? Tone.Info : Tone.Neutral), Kit.Text(artifact.Name, "small"), Kit.Text(artifact.Detail, "caption"));
            if (artifact.Kind == ArtifactKind.File && File.Exists(path))
            {
                row.Children.Add(Kit.Button("Open", () => Workspace.Core.Platform.OpenPath(path), "link"));
                row.Children.Add(Kit.Button("Show", () => Workspace.Core.Platform.RevealInFileManager(path), "link"));
            }
            artifacts.Children.Add(row);
        }
        var runs = Kit.Column(4);
        foreach (var run in session.Runs.TakeLast(40))
            runs.Children.Add(Kit.Selectable($"{run.At.ToLocalTime():HH:mm:ss} {run.Agent} ({run.Purpose}) {run.Outcome} in {run.Seconds}s, exit {run.ExitCode?.ToString() ?? "-"}{(run.Reason.Length > 0 ? ": " + run.Reason : "")}\n    {run.CommandLine}", "mono"));
        return Kit.Card(Kit.Column(14, header, actions, request, result,
            new Expander { Header = $"Timeline ({session.Timeline.Count})", Content = timeline, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch },
            new Expander { Header = $"Artifacts and changed files ({session.Artifacts.Count})", Content = artifacts.Children.Count > 0 ? artifacts : Kit.Text("None.", "small"), HorizontalAlignment = HorizontalAlignment.Stretch },
            new Expander { Header = $"What ran ({session.Runs.Count} agent runs)", Content = runs.Children.Count > 0 ? runs : Kit.Text("None.", "small"), HorizontalAlignment = HorizontalAlignment.Stretch }), 20);
    }

    private async Task ExportAsync(Session session)
    {
        var file = await Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export session summary", SuggestedFileName = $"agex-session-{session.Id}.md", DefaultExtension = "md" });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllTextAsync(path, SessionStore.ExportMarkdown(session));
        Window.Toast("Summary exported", Path.GetFileName(path), ToastKind.Success);
    }

    /// <summary>Restores the Git snapshot taken before agents changed files. Always asks first.</summary>
    public async Task UndoAsync(Session session)
    {
        if (session.SnapshotRef.Length == 0 || !Directory.Exists(session.Project)) return;
        var changed = await Workspace.Core.Git.ChangedSinceAsync(session.Project, session.SnapshotRef);
        var body = Kit.Column(8,
            Kit.Text("These files will be put back to how they were before this request. Files created since then will be deleted. Changes you made yourself after the request are also undone.", "body"),
            Kit.Selectable(string.Join("\n", changed.Take(60)) + (changed.Count > 60 ? $"\n... and {changed.Count - 60} more" : ""), "mono"));
        if (changed.Count == 0) { await Window.Dialogs.MessageAsync("Nothing to undo", "The project already matches the snapshot."); return; }
        if (await Window.Dialogs.ShowAsync($"Undo {changed.Count} changes?", body, ["Undo changes", "Cancel"], defaultIndex: 1, cancelIndex: 1) != 0) return;
        var ok = await Workspace.Core.Git.RestoreAsync(session.Project, session.SnapshotRef);
        Window.Toast(ok ? "Changes undone" : "Some files could not be restored", ok ? "The project matches the snapshot again." : "Check the files listed in Git status.", ok ? ToastKind.Success : ToastKind.Error);
    }
}
