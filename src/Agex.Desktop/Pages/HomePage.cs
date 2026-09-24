using Agex.Core.Attachments;
using Agex.Core.Orchestration;
using Agex.Core.Sessions;
using Agex.Core.Settings;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Agex.Desktop.Pages;

/// <summary>
/// Home answers four questions at a glance: which project, which agents,
/// what is happening now, and what to type next.
/// </summary>
public sealed class HomePage(MainWindow window) : AppPage(window)
{
    private readonly TextBox _composer = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96, MaxHeight = 260,
        PlaceholderText = "Tell AGEX what you want done...",
    };
    private readonly StackPanel _agentsRow = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ContentControl _question = new();
    private readonly ContentControl _status = new();
    private readonly StackPanel _timeline = new() { Spacing = 6 };
    private readonly StackPanel _tasks = new() { Spacing = 6 };
    private readonly ContentControl _result = new();
    private readonly ContentControl _projectHint = new();
    private readonly Grid _columns = new() { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
    private ComboBox? _team;
    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal };
    private readonly Avalonia.Threading.DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public override string Id => "home";
    public override string Title => "Home";
    public override string Icon => Icons.Home;

    protected override Control Build()
    {
        AutomationProperties.SetName(_composer, "Request");
        _composer.Text = Workspace.State.DraftRequest;
        _composer.TextChanged += (_, _) => { if (!Workspace.IsRunning) Workspace.SaveDraft(_composer.Text ?? ""); };
        _composer.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) { e.Handled = true; _ = SendAsync(); }
        };
        // Paste: a screenshot or copied files become attachments; text pastes as usual.
        _composer.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.V && e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) _ = PasteAttachmentsAsync();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var composer = Kit.Card(Kit.Column(10,
            Kit.Text("What should the agents do?", "subtitle"),
            _composer,
            _chips,
            BuildComposerFooter()));
        DragDrop.SetAllowDrop(composer, true);
        composer.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && !Workspace.IsRunning ? DragDropEffects.Copy : DragDropEffects.None);
        composer.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (Workspace.IsRunning) return;
            AddAttachments(e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>() ?? []);
        });
        Workspace.PendingAttachments.CollectionChanged += (_, _) => RefreshChips();
        RefreshChips();

        var timelineCard = Kit.Card(Kit.Column(8, Kit.SectionHeader("Timeline", "What happened, step by step", Kit.Button("Agent Room", () => Window.Navigate("room"), "link")), _timeline));
        var tasksCard = Kit.Card(Kit.Column(8, Kit.SectionHeader("Tasks", "The plan and who does what"), _tasks));
        _columns.Children.Add(timelineCard);
        Grid.SetColumn(tasksCard, 1);
        _columns.Children.Add(tasksCard);

        Workspace.SessionChanged += Refresh;
        Workspace.ProjectChanged += Refresh;
        Workspace.ScanChanged += RefreshAgents;
        Workspace.SettingsChanged += RefreshAgents;
        Workspace.Timeline.CollectionChanged += (_, _) => RefreshTimeline();
        Workspace.Tasks.CollectionChanged += (_, _) => RefreshTasks();
        _clock.Tick += (_, _) => RefreshStatus();
        var page = Kit.Column(16, _projectHint, composer, _question, _status, _columns, _result);
        var view = Kit.Page(page);
        view.SizeChanged += (_, e) => Stack(e.NewSize.Width < 900);
        Refresh();
        return view;
    }

    private void Stack(bool narrow)
    {
        _columns.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("*,*");
        if (_columns.Children.Count < 2) return;
        Grid.SetColumn(_columns.Children[1], narrow ? 0 : 1);
        Grid.SetRow(_columns.Children[1], narrow ? 1 : 0);
        _columns.RowDefinitions = narrow ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        _columns.RowSpacing = 16;
    }

    private Control BuildComposerFooter()
    {
        var teams = new List<(string Value, string Label)> { ("", "Selected agents") };
        teams.AddRange(Workspace.Settings.Teams.Select(team => (team.Id, team.Name)));
        _team = Kit.Combo(teams, Workspace.Settings.ActiveTeam, value => { Workspace.Settings.ActiveTeam = value; Workspace.SaveSettings(); RefreshAgents(); }, 160);
        AutomationProperties.SetName(_team, "Agents");
        ToolTip.SetTip(_team, "Which agents work on the next request");

        var jobs = new List<(string Value, string Label)> { ("", "General work") };
        jobs.AddRange(Agex.Core.Teams.JobTeamCatalog.All.Select(team => (team.Id, team.Name)));
        _job = Kit.Combo(jobs, Workspace.Settings.ActiveJobTeam, value => { Workspace.Settings.ActiveJobTeam = value; Workspace.SaveSettings(); RefreshAgents(); }, 170);
        AutomationProperties.SetName(_job, "Team");
        ToolTip.SetTip(_job, "The kind of work: sets the team's rules, approvals and tools. Set teams up on the Teams page.");

        var modes = new List<(EfficiencyMode Value, string Label)>
        {
            (EfficiencyMode.MaximumQuality, "Maximum quality"), (EfficiencyMode.Balanced, "Balanced"), (EfficiencyMode.SaveTokens, "Save tokens"), (EfficiencyMode.LocalFirst, "Local-first"),
        };
        _efficiency = Kit.Combo(modes, Workspace.Settings.Efficiency, value => { Workspace.Settings.Efficiency = value; Workspace.SaveSettings(); }, 150);
        AutomationProperties.SetName(_efficiency, "Efficiency");
        ToolTip.SetTip(_efficiency, "Maximum quality: full context, more reasoning. Balanced: default. Save tokens: shorter context and brief answers (shown in the timeline). Local-first: prefer local models.");

        var attach = Kit.Button("Attach", () => _ = PickAttachmentsAsync(), "", Icons.Attach, "Attach files, images, documents or videos (you can also drop or paste them)");
        var tools = Kit.Wrap(attach, _job, _efficiency, _team);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(_agentsRow);
        _agentsRow.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_actions, 1);
        grid.Children.Add(_actions);
        return Kit.Column(6, tools, grid);
    }

    private ComboBox? _job, _efficiency;

    /// <summary>Keeps the pickers in step when the team or mode is changed elsewhere (Teams page, Settings).</summary>
    private void SyncPickers()
    {
        if (_job is not null)
        {
            var index = Agex.Core.Teams.JobTeamCatalog.All.ToList().FindIndex(team => team.Id == Workspace.Settings.ActiveJobTeam) + 1;
            if (_job.SelectedIndex != index) _job.SelectedIndex = index;
        }
        if (_efficiency is not null && _efficiency.SelectedIndex != (int)Workspace.Settings.Efficiency) _efficiency.SelectedIndex = (int)Workspace.Settings.Efficiency;
    }

    // ------------------------------------------------------------ attachments

    private async Task PickAttachmentsAsync()
    {
        if (Workspace.IsRunning) return;
        var files = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Attach files", AllowMultiple = true });
        AddAttachments(files.Select(file => file.TryGetLocalPath()).OfType<string>());
    }

    private async Task PasteAttachmentsAsync()
    {
        if (Workspace.IsRunning || TopLevel.GetTopLevel(_composer)?.Clipboard is not { } clipboard) return;
        try
        {
            var files = await clipboard.TryGetFilesAsync();
            if (files is { Length: > 0 }) { AddAttachments(files.Select(file => file.TryGetLocalPath()).OfType<string>()); return; }
            using var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is null) return;
            var folder = Path.Combine(Workspace.Core.Platform.Paths.DataRoot, "attachments", "pasted");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            bitmap.Save(path);
            AddAttachments([path]);
        }
        catch (Exception ex) { Workspace.Core.Log.Error("paste_attachment_failed", ex); }
    }

    public void AddAttachments(IEnumerable<string> paths)
    {
        var refused = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path)) { refused.Add($"{Path.GetFileName(path)} is a folder"); continue; }
            if (!File.Exists(path) || Workspace.PendingAttachments.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            if (AttachmentService.Classify(path) == AttachmentKind.Unsupported) { refused.Add($"{Path.GetFileName(path)} (this file type is not supported)"); continue; }
            if (Workspace.PendingAttachments.Count >= 20) { refused.Add($"{Path.GetFileName(path)} (at most 20 files)"); continue; }
            Workspace.PendingAttachments.Add(path);
        }
        if (refused.Count > 0) Window.Toast("Not attached", string.Join("; ", refused), ToastKind.Info);
    }

    private void RefreshChips()
    {
        _chips.Children.Clear();
        foreach (var path in Workspace.PendingAttachments.ToList())
        {
            var kind = AttachmentService.Classify(path);
            long size = 0;
            try { size = new FileInfo(path).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            var icon = kind switch { AttachmentKind.Image => Icons.Search, AttachmentKind.Video => Icons.Play, AttachmentKind.Archive => Icons.Download, _ => Icons.Copy };
            var name = Kit.Text(Path.GetFileName(path), "small").Trimmed(200);
            var open = new Button { Content = Kit.Row(6, Kit.Icon(icon, 14), Kit.Column(0, name, Kit.Text($"{AttachmentService.KindLabel(kind)} - {FormatSize(size)}", "caption"))), Padding = new Thickness(8, 4) };
            open.Classes.Add("subtle");
            open.Click += (_, _) => Window.WorkspacePanel.Open(path);
            ToolTip.SetTip(open, "Preview in the workspace panel");
            AutomationProperties.SetName(open, "Preview " + Path.GetFileName(path));
            var remove = Kit.IconButton(Icons.Close, "Remove " + Path.GetFileName(path), () => Workspace.PendingAttachments.Remove(path));
            var chip = new Border { Child = Kit.Row(0, open, remove), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 6) };
            chip.Res(Border.BorderBrushProperty, "BorderBrush");
            chip.Res(Border.BackgroundProperty, "Surface2Brush");
            _chips.Children.Add(chip);
        }
        _chips.IsVisible = _chips.Children.Count > 0;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024:0.#} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} bytes",
    };

    public void FocusComposer(bool clear = false)
    {
        if (clear && !Workspace.IsRunning) _composer.Text = "";
        _composer.Focus();
    }

    public void SetRequest(string text)
    {
        _composer.Text = text;
        _composer.Focus();
    }

    public override void OnShown() { SyncPickers(); Refresh(); FocusComposer(); }

    private bool _sending;

    private async Task SendAsync()
    {
        // A double click or Enter pressed twice must not start two requests.
        if (_sending) return;
        _sending = true;
        try
        {
            var text = _composer.Text ?? "";
            if (Workspace.Project is null) { await Window.PickProjectAsync(); if (Workspace.Project is null) return; }
            var team = Workspace.Settings.ActiveTeam.Length > 0 ? Workspace.Settings.ActiveTeam : null;
            if (await Workspace.StartAsync(text, team, continueFrom: _continue)) { _composer.Text = ""; _continue = null; _composer.PlaceholderText = "Tell AGEX what you want done..."; }
        }
        finally
        {
            _sending = false;
            Refresh();
        }
    }

    // ---------------------------------------------------------------- refresh

    private void Refresh()
    {
        _projectHint.Content = Workspace.Project is null
            ? Kit.Card(Kit.Row(12, Kit.Icon(Icons.Folder, 24, "AccentBrush"), Kit.Column(2, Kit.Text("Choose a project to start", "subtitle"), Kit.Text("Agents work inside one folder you choose.", "small")), Kit.Button("Choose folder...", () => _ = Window.PickProjectAsync(), "primary")))
            : null;
        _composer.IsEnabled = !Workspace.IsRunning;
        RefreshActions();
        RefreshAgents();
        RefreshQuestion();
        RefreshStatus();
        RefreshTimeline();
        RefreshTasks();
        RefreshResult();
        if (Workspace.IsRunning) _clock.Start(); else _clock.Stop();
        // Empty sections take no space.
        foreach (var slot in new[] { _projectHint, _question, _status, _result }) slot.IsVisible = slot.Content is not null;
    }

    private void RefreshActions()
    {
        _actions.Children.Clear();
        if (Workspace.IsRunning)
        {
            var paused = Workspace.Engine?.IsPaused == true;
            _actions.Children.Add(Kit.Button(paused ? "Resume" : "Pause", () => { if (paused) Workspace.Resume(); else Workspace.Pause(); Refresh(); }, "", paused ? Icons.Play : Icons.Pause, "Pause stops new tasks from starting; running agents finish their current step."));
            _actions.Children.Add(Kit.Button("Cancel", async () =>
            {
                if (await Window.ConfirmAsync("Cancel this request?", "Running agents are stopped. Files they already changed stay changed (use Undo changes in Sessions if a snapshot exists).", "Cancel request", "Keep running"))
                    Workspace.Cancel();
            }, "danger", Icons.Stop));
        }
        else
        {
            _actions.Children.Add(Kit.Button("Send", () => _ = SendAsync(), "primary", Icons.Send, "Start the request", Kit.ShortcutText("Enter")));
        }
    }

    private void RefreshAgents()
    {
        _agentsRow.Children.Clear();
        var members = Workspace.Members(Workspace.Settings.ActiveTeam.Length > 0 ? Workspace.Settings.ActiveTeam : null);
        foreach (var member in members.Take(6))
        {
            var avatar = Kit.Avatar(member.Name, 24);
            ToolTip.SetTip(avatar, $"{member.Name}: {(member.CanWrite ? "can edit files" : "read-only")}, {(member.Privacy == Agex.Core.Agents.PrivacyKind.Local ? "runs on this computer" : member.Adapter.DataDestination(member.Model))}");
            _agentsRow.Children.Add(avatar);
        }
        if (members.Count == 0) _agentsRow.Children.Add(Kit.Button("No agent ready - set up agents", () => Window.Navigate("agents"), "link"));
        var skills = Workspace.Core.Skills.Installed().Count(skill => skill.Enabled);
        if (skills > 0) _agentsRow.Children.Add(Kit.Badge($"{skills} skill{(skills == 1 ? "" : "s")}", Tone.Neutral, Icons.Skills));
        var preset = Workspace.Core.RoutingFor(Workspace.Project);
        if (preset != RoutingPreset.Automatic) _agentsRow.Children.Add(Kit.Badge(Router.Title(preset), Tone.Info));
    }

    private void RefreshQuestion()
    {
        if (Workspace.Question is not { } question) { _question.Content = null; return; }
        var answer = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, PlaceholderText = "Your answer..." };
        AutomationProperties.SetName(answer, "Answer");
        var card = Kit.Card(Kit.Column(10,
            Kit.Row(8, Kit.Icon(Icons.Question, 20, "WarningBrush"), Kit.Text($"{question.From} needs your answer", "subtitle")),
            Kit.Selectable(question.Question, "body"),
            answer,
            Kit.Row(8, Kit.Button("Send answer", () => Workspace.Answer(answer.Text), "primary", Icons.Send), Kit.Button("Skip", () => Workspace.Answer(null), "subtle"))));
        card.Res(Border.BorderBrushProperty, "WarningBrush");
        card.BorderThickness = new Thickness(2);
        _question.Content = card;
        _question.IsVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => answer.Focus());
    }

    public static (string Text, Tone Tone, string Icon) StatusLook(SessionStatus status) => status switch
    {
        SessionStatus.Running => ("Running", Tone.Accent, Icons.Refresh),
        SessionStatus.Paused => ("Paused", Tone.Warning, Icons.Pause),
        SessionStatus.WaitingForInput => ("Needs your answer", Tone.Warning, Icons.Question),
        SessionStatus.WaitingForApproval => ("Needs your approval", Tone.Warning, Icons.Lock),
        SessionStatus.Complete or SessionStatus.CompleteWithFallback => (SessionStatusText.Label(status), Tone.Success, Icons.Check),
        SessionStatus.Partial or SessionStatus.Unverified => (SessionStatusText.Label(status), Tone.Warning, Icons.Alert),
        SessionStatus.Cancelled or SessionStatus.Interrupted => (SessionStatusText.Label(status), Tone.Neutral, Icons.Stop),
        _ => (SessionStatusText.Label(status), Tone.Danger, Icons.Close),
    };

    private void RefreshStatus()
    {
        if (Workspace.Session is not { } session) { _status.Content = null; return; }
        var (text, tone, icon) = StatusLook(session.Status);
        var latest = Workspace.Timeline.LastOrDefault()?.Text ?? "";
        var done = Workspace.Tasks.Count(task => task.State == TaskState.Done);
        var total = Workspace.Tasks.Count;
        var elapsed = DateTimeOffset.UtcNow - session.CreatedAt;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(Kit.Column(4, Kit.Text(session.Title, "subtitle"), Kit.Text(Workspace.IsRunning ? latest : $"Started {Kit.Ago(session.CreatedAt)} · {session.Agents.Count} agents · leader {session.Leader}", "small")));
        var badge = Kit.Badge(text, tone, icon);
        Grid.SetColumn(badge, 1);
        header.Children.Add(badge);
        var progress = total > 0 ? new ProgressBar { Minimum = 0, Maximum = total, Value = done, Height = 6, [AutomationProperties.NameProperty] = $"{done} of {total} tasks done" } : Workspace.IsRunning ? new ProgressBar { IsIndeterminate = true, Height = 6 } : null;
        var meta = Workspace.IsRunning ? Kit.Text($"{done} of {total} tasks done · {elapsed:mm\\:ss} elapsed", "caption") : null;
        _status.Content = Kit.Card(Kit.Column(10, header, progress, meta));
        _status.IsVisible = true;
    }

    private void RefreshTimeline()
    {
        _timeline.Children.Clear();
        var entries = Workspace.Timeline.TakeLast(12).ToList();
        if (entries.Count == 0) { _timeline.Children.Add(Kit.Text("Nothing yet. Send a request to start.", "small")); return; }
        foreach (var entry in entries)
        {
            var (icon, brush) = entry.Kind switch
            {
                TimelineKind.Done => (Icons.Check, "SuccessBrush"),
                TimelineKind.Failed => (Icons.Close, "DangerBrush"),
                TimelineKind.Fallback or TimelineKind.Warning => (Icons.Alert, "WarningBrush"),
                TimelineKind.Input => (Icons.Question, "WarningBrush"),
                TimelineKind.Approval => (Icons.Lock, "InfoBrush"),
                TimelineKind.Start => (Icons.Play, "AccentBrush"),
                _ => (Icons.Dot, "Text3Brush"),
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8 };
            row.Children.Add(Kit.Text(entry.At.ToLocalTime().ToString("HH:mm"), "caption"));
            var glyph = Kit.Icon(icon, 14, brush);
            Grid.SetColumn(glyph, 1);
            row.Children.Add(glyph);
            var text = Kit.Text(entry.Text, "small");
            text.Foreground = null;
            text.Res(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(text, 2);
            row.Children.Add(text);
            _timeline.Children.Add(row);
        }
    }

    public static (string Text, Tone Tone) TaskLook(TaskState state) => state switch
    {
        TaskState.Done => ("Done", Tone.Success),
        TaskState.Running or TaskState.Starting => ("Working", Tone.Accent),
        TaskState.Verifying => ("Checking", Tone.Info),
        TaskState.Queued => ("Queued", Tone.Neutral),
        TaskState.Waiting => ("Blocked", Tone.Warning),
        TaskState.RepairRequired => ("Needs repair", Tone.Warning),
        TaskState.Skipped => ("Skipped", Tone.Neutral),
        TaskState.Cancelled => ("Cancelled", Tone.Neutral),
        _ => ("Failed", Tone.Danger),
    };

    private void RefreshTasks()
    {
        _tasks.Children.Clear();
        if (Workspace.Tasks.Count == 0) { _tasks.Children.Add(Kit.Text(Workspace.Session is null || Workspace.IsRunning ? "The leader agent creates tasks when it plans your request." : "No tasks: the leader answered directly.", "small")); return; }
        foreach (var task in Workspace.Tasks)
        {
            var (text, tone) = TaskLook(task.State);
            var agent = Workspace.Core.Registry.Get(task.Agent)?.Name ?? task.Agent;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(Kit.Avatar(agent, 22));
            var label = Kit.Column(0, Kit.Text($"{task.Id.Replace("task-000", "#").Replace("task-00", "#")}  {task.Label}", "small"), task.Error.Length > 0 && task.State != TaskState.Done ? Kit.Text(task.Error, "caption") : null);
            ((TextBlock)label.Children[0]).Res(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var badge = Kit.Badge(text, tone);
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);
            _tasks.Children.Add(row);
        }
    }

    private void RefreshResult()
    {
        if (Workspace.IsRunning || Workspace.Session is not { Outcome: { } outcome } session) { _result.Content = null; return; }
        var (text, tone, icon) = StatusLook(session.Status);
        var body = Kit.Column(10,
            Kit.Row(10, Kit.Badge(text, tone, icon), Kit.Text(outcome.Headline, "subtitle")),
            outcome.Reason.Length > 0 ? Kit.Selectable(outcome.Reason, "body") : null,
            outcome.Verification.Length > 0 ? Kit.Text("How it was checked: " + outcome.Verification, "small") : null,
            outcome.PrimaryFailure.Length > 0 ? Kit.Text("Problem: " + outcome.PrimaryFailure, "small") : null);
        foreach (var line in outcome.WhatHappened) body.Children.Add(Kit.Text("• " + line, "small"));
        if (outcome.Results.Count > 0)
        {
            var results = Kit.Column(4);
            foreach (var line in outcome.Results.Take(12)) results.Children.Add(Kit.Selectable(line, "small"));
            body.Children.Add(new Expander { Header = $"Task results ({outcome.Results.Count})", Content = results, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        if (session.Changes.Count > 0)
        {
            var changes = Kit.Column(4);
            foreach (var change in session.Changes.Take(50))
            {
                var path = change.Path;
                var row = Kit.Row(8, Kit.Badge(change.Kind, change.Kind == "deleted" ? Tone.Danger : change.Kind == "added" ? Tone.Success : Tone.Info), Kit.Text(path, "mono"));
                if (change.Kind != "deleted")
                    row.Children.Add(Kit.Button("View changes", () => _ = ShowDiffAsync(session, path), "link"));
                changes.Children.Add(row);
            }
            body.Children.Add(new Expander { Header = $"Changed files ({session.Changes.Count})", Content = changes, IsExpanded = session.Changes.Count <= 8, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        var usage = session.Usage.Where(pair => pair.Value.InputTokens is not null || pair.Value.CostUsd is not null).ToList();
        body.Children.Add(Kit.Text(usage.Count == 0 ? "Usage: not reported by these agents." : "Usage reported by agents: " + string.Join("; ", usage.Select(pair => $"{Workspace.Core.Registry.Get(pair.Key)?.Name ?? pair.Key} {pair.Value.InputTokens ?? 0:N0} in / {pair.Value.OutputTokens ?? 0:N0} out tokens{(pair.Value.CostUsd is { } cost ? $", ${cost:0.####}" : "")}")), "caption"));
        var actions = Kit.Wrap(
            Kit.Button("Copy result", () => _ = Window.CopyAsync(outcome.Headline + "\n\n" + outcome.Reason + "\n\n" + string.Join("\n", outcome.Results)), "", Icons.Copy),
            Kit.Button("Continue", () => ContinueFrom(session), "", Icons.Send, "Start a follow-up request that knows about this one"),
            Kit.Button("Retry", () => _ = Workspace.StartAsync(session.Request), "", Icons.Refresh),
            Kit.Button("Retry with...", () => _ = RetryWithAsync(session), "", Icons.Agent),
            session.SnapshotRef.Length > 0 ? Kit.Button("Undo changes", () => _ = Window.Page<SessionsPage>("sessions").UndoAsync(session), "danger", Icons.Undo) : null,
            Kit.Button("Open in Agent Room", () => Window.Navigate("room"), "subtle", Icons.Room));
        body.Children.Add(actions);
        _result.Content = Kit.Card(body);
    }

    private void ContinueFrom(Session session)
    {
        _composer.PlaceholderText = $"Follow-up to \"{session.Title}\"...";
        _composer.Text = "";
        _composer.Focus();
        _continue = session;
        RefreshActions();
    }

    private Session? _continue;

    private async Task RetryWithAsync(Session session)
    {
        var members = Workspace.Core.BuildMembers(Workspace.Project, agentIds: Workspace.Core.Registry.Adapters.Select(adapter => adapter.Id).ToList());
        var boxes = members.Select(member => new CheckBox { Content = member.Name, Tag = member.Id, IsChecked = !session.Agents.Contains(member.Name) }).ToList();
        var result = await Window.Dialogs.ShowAsync("Retry with different agents", Kit.Column(8, [Kit.Text("Choose the agents for this retry.", "small"), .. boxes]), ["Retry", "Cancel"]);
        var chosen = boxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag!).ToList();
        if (result == 0 && chosen.Count > 0) await Workspace.StartAsync(session.Request, agentIds: chosen);
    }

    private async Task ShowDiffAsync(Session session, string path)
    {
        var diff = await Workspace.Core.Git.DiffAsync(session.Project, path, session.SnapshotRef.Length > 0 ? session.SnapshotRef : null);
        if (diff.Length == 0) diff = "No Git diff is available for this file (the project may not use Git).";
        var lines = new StackPanel();
        foreach (var line in diff.Split('\n').Take(1500))
        {
            var block = Kit.Selectable(line, "mono");
            var border = new Border { Child = block, Padding = new Thickness(6, 0) };
            if (line.StartsWith('+') && !line.StartsWith("+++")) border.Res(Border.BackgroundProperty, "DiffAddBrush");
            else if (line.StartsWith('-') && !line.StartsWith("---")) border.Res(Border.BackgroundProperty, "DiffRemoveBrush");
            lines.Children.Add(border);
        }
        await Window.Dialogs.ShowAsync(path, lines, ["Close"], maxWidth: 900);
    }
}
