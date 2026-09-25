using Agex.Core.Attachments;
using Agex.Core.Orchestration;
using Agex.Core.Projects;
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
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 220,
        PlaceholderText = "Tell AGEX what you want done...",
    };
    // Chat-first layout: the conversation fills the page; the composer sits at the bottom.
    private readonly ContentControl _welcome = new();
    private readonly ContentControl _userBubble = new();
    private readonly ContentControl _steps = new();
    private readonly StackPanel _tips = new();
    private readonly WrapPanel _skillChips = new() { Orientation = Orientation.Horizontal };
    private readonly Button _skillsButton = new();
    private ConnectionUi? _connectionUi;
    private ConnectionUi ConnectionUi => _connectionUi ??= new ConnectionUi(Window);
    private readonly StackPanel _agentsRow = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ContentControl _question = new();
    private readonly ContentControl _status = new();
    private readonly StackPanel _timeline = new() { Spacing = 6 };
    private readonly StackPanel _tasks = new() { Spacing = 6 };
    private readonly ContentControl _result = new();
    private readonly ContentControl _projectHint = new();
    private readonly Grid _columns = new() { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
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
        _composer.TextChanged += (_, _) => RefreshTipsSoon();
        var composer = Kit.Card(Kit.Column(8,
            _tips,
            _chips,
            _skillChips,
            _composer,
            BuildComposerFooter()), 12);
        DragDrop.SetAllowDrop(composer, true);
        composer.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && !Workspace.IsRunning ? DragDropEffects.Copy : DragDropEffects.None);
        composer.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (Workspace.IsRunning) return;
            AddAttachments(e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>() ?? []);
        });
        Workspace.PendingAttachments.CollectionChanged += (_, _) => RefreshChips();
        RefreshChips();

        var timelineCard = Kit.Column(8, Kit.SectionHeader("Timeline", "What happened, step by step", Kit.Button("Agent Room", () => Window.Navigate("room"), "link")), _timeline);
        var tasksCard = Kit.Column(8, Kit.SectionHeader("Tasks", "The plan and who does what"), _tasks);
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
        Workspace.RequestSkillsChanged += () => { RefreshSkillChips(); RefreshTipsSoon(); };
        Workspace.ConnectionsChanged += () => { RefreshWelcome(); RefreshTipsSoon(); };
        Workspace.PendingAttachments.CollectionChanged += (_, _) => RefreshTipsSoon();
        var conversation = Kit.Column(16, _projectHint, _welcome, _userBubble, _status, _question, _result, _steps);
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(Kit.Space5, Kit.Space5, Kit.Space5, Kit.Space3), MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Stretch, Child = conversation },
        };
        _conversationScroll = scroll;
        var composerHost = new Border { Padding = new Thickness(Kit.Space5, 0, Kit.Space5, Kit.Space4), MaxWidth = 920, Child = composer };
        var view = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        view.Children.Add(scroll);
        Grid.SetRow(composerHost, 1);
        view.Children.Add(composerHost);
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

    private ScrollViewer? _conversationScroll;

    private Control BuildComposerFooter()
    {
        // Compact menu buttons keep the composer to two lines at 1366 x 768; each opens a short menu.
        _jobButton = MenuButton("Team", "The kind of work: sets the team's rules, approvals and tools", ShowTeamMenu);
        _efficiencyButton = MenuButton("Efficiency", "Maximum quality, Balanced, Save tokens or Local-first", ShowEfficiencyMenu);
        _agentsButton = MenuButton("Agents", "Which agents work on the next request", ShowAgentsMenu);
        var attach = Kit.IconButton(Icons.Attach, "Attach files, images, documents or videos (you can also drop or paste them)", () => _ = PickAttachmentsAsync());
        _skillsButton.Classes.Add("subtle");
        _skillsButton.Click += async (_, _) => await new SkillPicker(Window).ShowAsync();
        AutomationProperties.SetName(_skillsButton, "Skills for this request");
        ToolTip.SetTip(_skillsButton, "Choose skills for this request: Auto, a profile, or your own selection");
        var tools = Kit.Wrap(attach, _jobButton, _skillsButton, _efficiencyButton, _agentsButton);
        foreach (var child in tools.Children) child.Margin = new Thickness(0, 0, 4, 4);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(tools);
        Grid.SetColumn(_actions, 1);
        _actions.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(_actions);
        SyncPickers();
        RefreshSkillChips();
        return grid;
    }

    private Button? _jobButton, _efficiencyButton, _agentsButton;

    private static Button MenuButton(string name, string tip, Action<Button> open)
    {
        var button = new Button { Padding = new Thickness(8, 4) };
        button.Classes.Add("subtle");
        button.Click += (_, _) => open(button);
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Control MenuLabel(string text, string? icon = null) =>
        Kit.Row(6, icon is null ? null : Kit.Icon(icon, 14), Kit.Text(text, "small"), Kit.Icon(Icons.ChevronDown, 12, "Text3Brush"));

    private static MenuItem Choice(string header, bool selected, Action pick)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = selected };
        item.Click += (_, _) => pick();
        return item;
    }

    private void ShowTeamMenu(Button button)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Choice("General work", Workspace.Settings.ActiveJobTeam.Length == 0, () => PickTeam("")));
        menu.Items.Add(new Separator());
        foreach (var team in Agex.Core.Teams.JobTeamCatalog.All)
        {
            var id = team.Id;
            menu.Items.Add(Choice(team.Name, Workspace.Settings.ActiveJobTeam == id, () => PickTeam(id)));
        }
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = "Set up teams..." };
        manage.Click += (_, _) => Window.Navigate("teams");
        menu.Items.Add(manage);
        menu.Open(button);
    }

    private void PickTeam(string id)
    {
        Workspace.Settings.ActiveJobTeam = id;
        Workspace.SaveSettings();
        SyncPickers();
        RefreshAgents();
        RefreshWelcome();
        RefreshSkillChips();
        RefreshTipsSoon();
    }

    private static string EfficiencyName(EfficiencyMode mode) => mode switch
    {
        EfficiencyMode.MaximumQuality => "Maximum quality", EfficiencyMode.SaveTokens => "Save tokens", EfficiencyMode.LocalFirst => "Local-first", _ => "Balanced",
    };

    private void ShowEfficiencyMenu(Button button)
    {
        var menu = new ContextMenu();
        foreach (var mode in new[] { EfficiencyMode.MaximumQuality, EfficiencyMode.Balanced, EfficiencyMode.SaveTokens, EfficiencyMode.LocalFirst })
        {
            var captured = mode;
            menu.Items.Add(Choice(EfficiencyName(mode), Workspace.Settings.Efficiency == mode, () => { Workspace.Settings.Efficiency = captured; Workspace.SaveSettings(); SyncPickers(); RefreshTipsSoon(); }));
        }
        menu.Open(button);
    }

    private void ShowAgentsMenu(Button button)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Choice("Enabled agents", Workspace.Settings.ActiveTeam.Length == 0, () => PickAgents("")));
        foreach (var team in Workspace.Settings.Teams)
        {
            var id = team.Id;
            menu.Items.Add(Choice(team.Name, Workspace.Settings.ActiveTeam == id, () => PickAgents(id)));
        }
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = "Manage agents..." };
        manage.Click += (_, _) => Window.Navigate("agents");
        menu.Items.Add(manage);
        menu.Open(button);
    }

    private void PickAgents(string id)
    {
        Workspace.Settings.ActiveTeam = id;
        Workspace.SaveSettings();
        RefreshAgents();
    }

    /// <summary>Keeps the menu buttons' labels in step when the team or mode is changed elsewhere (Teams page, Settings, tips).</summary>
    private void SyncPickers()
    {
        if (_jobButton is not null) _jobButton.Content = MenuLabel(Agex.Core.Teams.JobTeamCatalog.Get(Workspace.Settings.ActiveJobTeam)?.Name ?? "General work", Icons.Team);
        if (_efficiencyButton is not null) _efficiencyButton.Content = MenuLabel(EfficiencyName(Workspace.Settings.Efficiency));
        if (_agentsButton is not null)
        {
            _agentsRow.Children.Clear();
            var members = Workspace.Members(Workspace.Settings.ActiveTeam.Length > 0 ? Workspace.Settings.ActiveTeam : null);
            foreach (var member in members.Take(5))
            {
                var avatar = Kit.Avatar(member.Name, 20);
                ToolTip.SetTip(avatar, $"{member.Name}: {(member.CanWrite ? "can edit files" : "read-only")}, {(member.Privacy == Agex.Core.Agents.PrivacyKind.Local ? "runs on this computer" : member.Adapter.DataDestination(member.Model))}");
                _agentsRow.Children.Add(avatar);
            }
            if (members.Count == 0) _agentsRow.Children.Add(Kit.Text("No agent ready", "small"));
            _agentsRow.Children.Add(Kit.Icon(Icons.ChevronDown, 12, "Text3Brush"));
            _agentsRow.Spacing = 2;
            if (_agentsButton.Content != _agentsRow) _agentsButton.Content = _agentsRow;
        }
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
            bitmap.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
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

    /// <summary>Starts a fresh conversation (keeps the team, skills and attachments).</summary>
    public void NewConversation()
    {
        if (Workspace.IsRunning) return;
        Workspace.ClearSession();
        _composer.Text = "";
        Refresh();
        _composer.Focus();
    }

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
        RefreshWelcome();
        RefreshUserBubble();
        RefreshSteps();
        RefreshSkillChips();
        RefreshTipsSoon();
        RefreshActions();
        RefreshAgents();
        RefreshQuestion();
        RefreshStatus();
        RefreshTimeline();
        RefreshTasks();
        RefreshResult();
        if (Workspace.IsRunning) _clock.Start(); else _clock.Stop();
        // Empty sections take no space.
        foreach (var slot in new[] { _projectHint, _welcome, _userBubble, _question, _status, _result, _steps }) slot.IsVisible = slot.Content is not null;
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

    private void RefreshAgents() => SyncPickers();

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
            body.Children.Insert(1, ChangeSummary(session));
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
            Kit.Button("Open in Agent Room", () => Window.Navigate("room"), "subtle", Icons.Room),
            Kit.Button("New conversation", NewConversation, "subtle", Icons.Plus, shortcut: Kit.ShortcutText("N")));
        body.Children.Add(actions);
        _result.Content = Kit.Card(body);
    }

    // ------------------------------------------------------------ chat view

    /// <summary>"Changes: 5 files +182 -37" - opens the Changes panel.</summary>
    private Control ChangeSummary(Session session)
    {
        var added = session.Changes.Sum(change => change.Added ?? 0);
        var removed = session.Changes.Sum(change => change.Removed ?? 0);
        var plus = Kit.Text($"+{added:N0}", "body");
        plus.Res(TextBlock.ForegroundProperty, "SuccessBrush");
        var minus = Kit.Text($"-{removed:N0}", "body");
        minus.Res(TextBlock.ForegroundProperty, "DangerBrush");
        var content = Kit.Row(10, Kit.Icon(Icons.Graph, 16, "AccentBrush"), Kit.Text($"Changes: {session.Changes.Count} file{(session.Changes.Count == 1 ? "" : "s")}", "body"), plus, minus, Kit.Text("View", "small"));
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left };
        button.Classes.Add("subtle");
        button.Click += (_, _) => Window.ShowChanges();
        AutomationProperties.SetName(button, $"Changes: {session.Changes.Count} files, {added} lines added, {removed} removed. Open the Changes panel.");
        return button;
    }

    private void RefreshUserBubble()
    {
        if (Workspace.Session is not { } session) { _userBubble.Content = null; return; }
        var text = Kit.Selectable(session.Request, "body");
        text.TextWrapping = TextWrapping.Wrap;
        var attachments = Workspace.LastAttachments.Count > 0 ? Kit.Text("Attached: " + string.Join(", ", Workspace.LastAttachments.Select(item => item.Name)), "caption") : null;
        var bubble = new Border { Child = Kit.Column(4, text, attachments), Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 680 };
        bubble.Res(Border.BackgroundProperty, "AccentSoftBrush");
        AutomationProperties.SetName(bubble, "Your request");
        _userBubble.Content = bubble;
    }

    /// <summary>Timeline and tasks, folded away so the conversation stays in front.</summary>
    private void RefreshSteps()
    {
        if (Workspace.Session is null) { _steps.Content = null; return; }
        var done = Workspace.Tasks.Count(task => task.State == TaskState.Done);
        _stepsExpander ??= new Expander { Content = _columns, HorizontalAlignment = HorizontalAlignment.Stretch };
        _stepsExpander.Header = $"Steps ({Workspace.Timeline.Count}) and tasks ({done}/{Workspace.Tasks.Count} done)";
        _steps.Content = _stepsExpander;
    }

    private Expander? _stepsExpander;

    /// <summary>
    /// No request yet: "What do you want to do?" with team quick starts. A chosen
    /// team shows what is ready, what can be connected, and example requests.
    /// </summary>
    private void RefreshWelcome()
    {
        if (Workspace.Session is not null) { _welcome.Content = null; _welcome.IsVisible = false; return; }
        var active = Agex.Core.Teams.JobTeamCatalog.Get(Workspace.Settings.ActiveJobTeam);
        var teams = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var team in Agex.Core.Teams.JobTeamCatalog.All)
        {
            var captured = team;
            var button = Kit.Button(team.Name, () => { Workspace.Settings.ActiveJobTeam = active?.Id == captured.Id ? "" : captured.Id; Workspace.SaveSettings(); SyncPickers(); RefreshWelcome(); RefreshSkillChips(); RefreshTipsSoon(); },
                active?.Id == team.Id ? "primary" : "", tooltip: team.Summary);
            button.Margin = new Thickness(0, 0, 8, 8);
            teams.Children.Add(button);
        }
        var title = Kit.Text("What do you want to do?", "title");
        var intro = Kit.Text("Pick the kind of work. AGEX prepares the team, the tools you already have and the skills that help. Or just type below.", "small");
        intro.TextWrapping = TextWrapping.Wrap;
        // With a team chosen, its card replaces the list so the conversation keeps the space.
        var content = active is null ? Kit.Column(12, title, intro, teams)
            : Kit.Column(12, Kit.Row(8, Kit.Text("Your team", "caption"), Kit.Button("Change team", () => PickTeam(""), "link")), TeamStart(active));
        _welcome.Content = content;
        _welcome.IsVisible = true;
    }

    private Control TeamStart(Agex.Core.Teams.JobTeam team)
    {
        var statuses = Workspace.Core.Teams.Check(team);
        var (ready, total, missing) = Agex.Core.Teams.JobTeamService.Progress(statuses);
        var connections = Agex.Core.Connections.ConnectionService.ForTeam(Workspace.Connections(), team.Id).Take(6).ToList();
        var rows = Kit.Column(6, connections.Select(item => (Control?)ConnectionUi.Row(item)).ToArray());
        var examples = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var task in team.TypicalTasks.Take(4))
        {
            var captured = task;
            var example = Kit.Button(task, () => SetRequest(captured), "subtle", Icons.Send, "Use as a starting point");
            example.Margin = new Thickness(0, 0, 6, 6);
            examples.Children.Add(example);
        }
        var summary = Kit.Text(team.Summary, "small");
        summary.TextWrapping = TextWrapping.Wrap;
        var body = Kit.Column(10,
            Kit.Row(10, Kit.Text(team.Name, "subtitle"), missing == 0 ? Kit.Badge($"{ready}/{total} tools ready", Tone.Success) : Kit.Badge($"{ready}/{total} ready · {missing} required missing", Tone.Warning)),
            summary,
            Kit.Text("Your tools", "caption"), rows,
            Kit.Text("Try", "caption"), examples,
            Kit.Wrap(Kit.Button(missing == 0 ? "Team details" : "Set up this team", () => { Window.Navigate("teams"); Window.Page<TeamsPage>("teams").OpenSetup(team.Id); }, missing == 0 ? "subtle" : "primary", Icons.Tool),
                Kit.Button("All connections", () => Window.Navigate("connections"), "subtle", Icons.Plug)));
        return Kit.Card(body, 14);
    }

    private void RefreshSkillChips()
    {
        _skillChips.Children.Clear();
        var skills = Workspace.EffectiveSkills();
        _skillsButton.Content = Kit.Row(6, Kit.Icon(Icons.Skills, 14), Kit.Text(Workspace.RequestSkills is null ? $"Skills: Auto ({skills.Count})" : $"Skills: {skills.Count} chosen", "small"));
        if (Workspace.RequestSkills is null) { _skillChips.IsVisible = false; return; }
        foreach (var skill in skills)
        {
            var id = skill.Id;
            var remove = Kit.IconButton(Icons.Close, "Remove " + skill.Manifest.Name, () =>
            {
                var list = Workspace.RequestSkills?.Where(item => item != id).ToList() ?? [];
                Workspace.SetRequestSkills(list);
            });
            remove.MinHeight = 22; remove.MinWidth = 22; remove.Padding = new Thickness(3);
            var chip = new Border { Child = Kit.Row(2, Kit.Text("+ " + skill.Manifest.Name, "small"), remove), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0, 2, 0), Margin = new Thickness(0, 0, 6, 6) };
            chip.Res(Border.BackgroundProperty, "AccentSoftBrush");
            _skillChips.Children.Add(chip);
        }
        var auto = Kit.Button("Back to Auto", () => Workspace.SetRequestSkills(null), "link");
        _skillChips.Children.Add(auto);
        _skillChips.IsVisible = true;
    }

    private bool _tipsPending;

    private void RefreshTipsSoon()
    {
        if (_tipsPending) return;
        _tipsPending = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { _tipsPending = false; RefreshTips(); }, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>Optional next steps (never more than two; a dismissed tip does not come back).</summary>
    private void RefreshTips()
    {
        _tips.Children.Clear();
        if (Workspace.IsRunning) { _tips.IsVisible = false; return; }
        var team = Workspace.Settings.ActiveJobTeam;
        var context = new Agex.Core.Connections.TipContext
        {
            Request = _composer.Text ?? "",
            Attachments = Workspace.PendingAttachments.Select(path => AttachmentService.Classify(path)).ToList(),
            TeamId = team.Length > 0 ? team : null,
            ActiveSkills = Workspace.EffectiveSkills().Select(skill => skill.Id).ToHashSet(),
            InstalledSkills = Workspace.Core.Skills.Installed().Select(skill => skill.Id).ToHashSet(),
            ProjectFiles = ProjectFileCount(),
            OllamaReady = Workspace.Readiness("ollama") == Agex.Core.Agents.AgentReadiness.InstalledReady && Workspace.Settings.EnabledAgents.Contains("ollama"),
            Efficiency = Workspace.Settings.Efficiency,
            TeamConnections = team.Length > 0 ? Agex.Core.Connections.ConnectionService.ForTeam(Workspace.Connections(), team) : [],
            Dismissed = Workspace.Settings.DismissedTips,
        };
        foreach (var tip in Agex.Core.Connections.Recommendations.For(context))
        {
            var captured = tip;
            var text = Kit.Text(tip.Text, "small");
            text.TextWrapping = TextWrapping.Wrap;
            text.VerticalAlignment = VerticalAlignment.Center;
            // Text takes the free width and wraps; the buttons keep their size.
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
            var icon = Kit.Icon(Icons.Info, 14, "InfoBrush");
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var apply = Kit.Button(tip.ButtonLabel, () => _ = ApplyTipAsync(captured), "link");
            Grid.SetColumn(apply, 2);
            row.Children.Add(apply);
            var dismiss = Kit.IconButton(Icons.Close, "Don't suggest this again", () => { Workspace.Settings.DismissedTips.Add(captured.Id); Workspace.SaveSettings(); RefreshTips(); });
            Grid.SetColumn(dismiss, 3);
            row.Children.Add(dismiss);
            var chip = new Border { Child = row, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 2, 2, 2), Margin = new Thickness(0, 0, 0, 6) };
            chip.Res(Border.BackgroundProperty, "InfoSoftBrush");
            _tips.Children.Add(chip);
        }
        _tips.IsVisible = _tips.Children.Count > 0;
    }

    private int? _fileCount;
    private string? _fileCountProject;

    private int ProjectFileCount()
    {
        if (Workspace.Project is not { } project) return 0;
        if (_fileCountProject != project.Path) { _fileCountProject = project.Path; _fileCount = ProjectScanner.List(project.Path, project.IgnoredFolders, maxFiles: 20000).Count; }
        return _fileCount ?? 0;
    }

    private async Task ApplyTipAsync(Agex.Core.Connections.Tip tip)
    {
        switch (tip.Action)
        {
            case Agex.Core.Connections.TipAction.AddSkill:
                var ids = tip.Argument.Split(',');
                var installed = Workspace.Core.Skills.Installed().Select(skill => skill.Id).ToHashSet();
                foreach (var id in ids.Where(id => !installed.Contains(id))) await Window.Page<SkillsPage>("skills").InstallByIdAsync(id);
                installed = Workspace.Core.Skills.Installed().Select(skill => skill.Id).ToHashSet();
                var current = (Workspace.RequestSkills ?? Workspace.EffectiveSkills().Select(skill => skill.Id)).ToList();
                current.AddRange(ids.Where(installed.Contains).Where(id => !current.Contains(id)));
                Workspace.SetRequestSkills(current);
                break;
            case Agex.Core.Connections.TipAction.SetEfficiency when Enum.TryParse<EfficiencyMode>(tip.Argument, out var mode):
                Workspace.Settings.Efficiency = mode;
                Workspace.SaveSettings();
                SyncPickers();
                break;
            case Agex.Core.Connections.TipAction.Connect:
                if (Workspace.Connections().FirstOrDefault(item => item.Id == tip.Argument) is { } item && item.Actions.FirstOrDefault() is { } action)
                    await ConnectionUi.RunAsync(item, action);
                break;
        }
        RefreshTips();
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
