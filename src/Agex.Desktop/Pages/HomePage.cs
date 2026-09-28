using Agex.Core.Attachments;
using Agex.Core.Agents;
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
        PlaceholderText = "Message AGEX...",
    };
    // Chat-first layout: the conversation fills the page; the composer sits at the bottom.
    private readonly ContentControl _welcome = new();
    private readonly ContentControl _userBubble = new();
    private readonly StackPanel _thread = new() { Spacing = 16 };
    private readonly StackPanel _tips = new();
    private readonly Button _skillsButton = new();
    private ConnectionUi? _connectionUi;
    private ConnectionUi ConnectionUi => _connectionUi ??= new ConnectionUi(Window);
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ContentControl _question = new();
    private readonly ContentControl _status = new();
    private readonly ContentControl _live = new();
    private readonly ContentControl _result = new();
    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal };
    private readonly Avalonia.Threading.DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public override string Id => "home";
    public override string Title => "Home";
    public override string Icon => Icons.Home;

    protected override Control Build()
    {
        AutomationProperties.SetName(_composer, "Request");
        _composer.Classes.Add("bare");
        _composer.MinHeight = 44;
        _composer.Text = Workspace.State.DraftRequest;
        _composer.TextChanged += (_, _) => { if (!Workspace.IsRunning) Workspace.SaveDraft(_composer.Text ?? ""); };
        // Like a chat app: Enter sends, Shift+Enter starts a new line.
        _composer.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; _ = SendAsync(); return; }
            if (e.Key == Key.V && e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) _ = PasteAttachmentsAsync();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _composer.TextChanged += (_, _) => { RefreshTipsSoon(); RefreshChipsSoon(); };
        var composer = new Border { Child = Kit.Column(2, _chips, _composer, BuildComposerFooter()) };
        composer.Classes.Add("composer");
        DragDrop.SetAllowDrop(composer, true);
        composer.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && !Workspace.IsRunning ? DragDropEffects.Copy : DragDropEffects.None);
        composer.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (Workspace.IsRunning) return;
            AddAttachments(e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>() ?? []);
        });
        Workspace.PendingAttachments.CollectionChanged += (_, _) => { RefreshChips(); RefreshChipsSoon(); };
        RefreshChips();

        Workspace.SessionChanged += Refresh;
        Workspace.ProjectChanged += () => { _composer.Text = Workspace.State.DraftRequest; Refresh(); };
        Workspace.ScanChanged += RefreshAgents;
        Workspace.SettingsChanged += () => { RefreshAgents(); RefreshChipsSoon(); };
        _clock.Tick += (_, _) => RefreshStatus();
        Workspace.Timeline.CollectionChanged += (_, _) => { if (Workspace.IsRunning) RefreshStatusSoon(); };
        Workspace.Timeline.CollectionChanged += (_, _) => RefreshLiveSoon();
        Workspace.LiveEvents.CollectionChanged += (_, _) => RefreshLiveSoon();
        Workspace.Tasks.CollectionChanged += (_, _) => RefreshLiveSoon();
        Workspace.RequestSkillsChanged += () => { RefreshSkillChips(); RefreshTipsSoon(); };
        Workspace.ConnectionsChanged += () => { RefreshWelcome(); RefreshTipsSoon(); };
        Workspace.PendingAttachments.CollectionChanged += (_, _) => RefreshTipsSoon();
        Workspace.ModeChanged += () => { SyncPickers(); RefreshPlaceholder(); RefreshChipsSoon(); };
        // The conversation is the page: one reading column, no boxes around the chat.
        var conversation = Kit.Column(22, _welcome, _thread, _userBubble, _status, _live, _question, _result);
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(Kit.Space5, Kit.Space4, Kit.Space5, Kit.Space4), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Stretch, Child = conversation },
        };
        _conversationScroll = scroll;
        var composerHost = new Border { Padding = new Thickness(Kit.Space5, 0, Kit.Space5, Kit.Space4), MaxWidth = 820, Child = Kit.Column(6, _tips, composer) };
        var view = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var bar = Kit.Row(2, _historyToggle = Kit.IconButton(Icons.History, "Conversations", ToggleHistory), _barNewChat = Kit.IconButton(Icons.Plus, "New chat", NewConversation, Kit.ShortcutText("N")));
        bar.Margin = new Thickness(Kit.Space3, Kit.Space1, Kit.Space3, 0);
        view.Children.Add(bar);
        Grid.SetRow(scroll, 1);
        view.Children.Add(scroll);
        Grid.SetRow(composerHost, 2);
        view.Children.Add(composerHost);

        // The conversation list: docked beside the chat on wide windows, over it on narrow ones.
        _history = new ConversationList(Window);
        _history.Picked += () => { if (!_historyDocked) SetHistory(false); Refresh(); };
        _history.CloseRequested += ToggleHistory;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        root.Children.Add(_history);
        Grid.SetColumn(view, 1);
        root.Children.Add(view);
        _root = root;
        root.SizeChanged += (_, e) => DockHistory(e.NewSize.Width >= 980);
        SetHistory(Workspace.State.HistoryOpen);
        RefreshPlaceholder();
        Refresh();
        return root;
    }

    private ConversationList? _history;
    private Button? _barNewChat;
    private Button? _jobButton, _modeButton, _modelButton;
    private Button? _historyToggle;
    private Grid? _root;
    private bool _historyDocked = true;
    private bool _historyOpen;

    private void ToggleHistory()
    {
        SetHistory(!_historyOpen);
        if (_historyDocked) { Workspace.State.HistoryOpen = _historyOpen; Workspace.SaveState(); }
    }

    private void SetHistory(bool open)
    {
        _historyOpen = open;
        if (_history is null) return;
        _history.IsVisible = open;
        if (open) _history.Refresh();
        if (_historyToggle is not null) _historyToggle.Classes.Set("active", open);
        // The list has its own New chat button.
        if (_barNewChat is not null) _barNewChat.IsVisible = !open;
    }

    /// <summary>Wide: the list takes its own column. Narrow: it floats over the chat and closes after a pick.</summary>
    private void DockHistory(bool docked)
    {
        if (_history is null || _root is null || docked == _historyDocked && _history.ZIndex == (docked ? 0 : 10)) return;
        _historyDocked = docked;
        Grid.SetColumn(_history, docked ? 0 : 1);
        _history.ZIndex = docked ? 0 : 10;
        _history.HorizontalAlignment = HorizontalAlignment.Left;
        if (!docked && _historyOpen) SetHistory(false);
        else if (docked && Workspace.State.HistoryOpen != _historyOpen) SetHistory(Workspace.State.HistoryOpen);
    }

    private void RefreshPlaceholder()
    {
        if (_continue is not null) return;
        _composer.PlaceholderText = Workspace.Mode switch
        {
            Agex.Core.Orchestration.ChatMode.Ask => "Ask a question. Nothing will be changed...",
            Agex.Core.Orchestration.ChatMode.Plan => "Describe what to plan. Nothing will be changed...",
            Agex.Core.Orchestration.ChatMode.Build => "Describe the work to do...",
            _ => Workspace.Session is not null && !Workspace.IsRunning ? "Reply..." : "Message AGEX...",
        };
    }

    private ScrollViewer? _conversationScroll;

    private Control BuildComposerFooter()
    {
        // One quiet row: + holds everything secondary; chips appear only when they say something.
        var plus = Kit.IconButton(Icons.Plus, "Attach files and options", () => { });
        plus.Classes.Add("round");
        plus.Click += (_, _) => ShowPlusMenu(plus);
        _modeButton = Chip("Mode", "Auto picks the right way for each message. Ask answers, Plan writes a plan, Build does the work.", ShowModeMenu);
        _jobButton = Chip("Team", "The Team in use: its rules and recommendations", ShowTeamMenu);
        _modelButton = Chip("Models", "Choose a model for each agent in this project", ShowModelMenu);
        _skillsButton.Classes.Add("chip");
        _skillsButton.Click += async (_, _) => await new SkillPicker(Window).ShowAsync(_composer.Text ?? "");
        AutomationProperties.SetName(_skillsButton, "Persistent skills for this project or conversation");
        var left = Kit.Row(6, plus, _modeButton, _jobButton, _modelButton, _skillsButton);
        left.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 2, 0, 0) };
        grid.Children.Add(left);
        Grid.SetColumn(_actions, 1);
        _actions.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(_actions);
        SyncPickers();
        RefreshSkillChips();
        return grid;
    }

    private static Button Chip(string name, string tip, Action<Button> open)
    {
        var button = new Button();
        button.Classes.Add("chip");
        button.Click += (_, _) => open(button);
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>Optional request controls: attachments, team, skills, agents and approvals.</summary>
    private void ShowPlusMenu(Button button)
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action click)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => click();
            return item;
        }
        menu.Items.Add(Item("Attach files...", () => _ = PickAttachmentsAsync()));
        menu.Items.Add(Item("Skills...", () => _ = new SkillPicker(Window).ShowAsync(_composer.Text ?? "")));
        menu.Items.Add(new Separator());
        var team = new MenuItem { Header = "Team" };
        team.Items.Add(Choice("General work (no Team)", Workspace.Settings.ActiveJobTeam.Length == 0, () => PickTeam("")));
        foreach (var job in Agex.Core.Teams.JobTeamCatalog.All)
        {
            var id = job.Id;
            team.Items.Add(Choice(job.Name, Workspace.Settings.ActiveJobTeam == id, () => PickTeam(id)));
        }
        team.Items.Add(new Separator());
        team.Items.Add(Item("Teams page...", () => Window.Navigate("teams")));
        menu.Items.Add(team);
        var agents = new MenuItem { Header = "Agents" };
        agents.Items.Add(Choice("All enabled agents", Workspace.Settings.ActiveTeam.Length == 0, () => PickAgents("")));
        foreach (var saved in Workspace.Settings.Teams)
        {
            var id = saved.Id;
            agents.Items.Add(Choice(saved.Name, Workspace.Settings.ActiveTeam == id, () => PickAgents(id)));
        }
        agents.Items.Add(new Separator());
        agents.Items.Add(Item("Manage agents...", () => Window.Navigate("agents")));
        menu.Items.Add(agents);
        var approvals = new MenuItem { Header = "Approvals" };
        foreach (var mode in new[] { ApprovalMode.AskEveryTime, ApprovalMode.Smart, ApprovalMode.TrustSession })
        {
            var captured = mode;
            var item = Choice(Agex.Core.Orchestration.ApprovalRules.ModeLabel(mode), Workspace.Settings.Approvals.Mode == mode, () => { Workspace.SetApprovalMode(captured); SyncPickers(); });
            ToolTip.SetTip(item, Agex.Core.Orchestration.ApprovalRules.ModeDescription(mode));
            approvals.Items.Add(item);
        }
        approvals.Items.Add(new Separator());
        approvals.Items.Add(Item("What agents may do...", () => _ = PermissionsView.ShowAsync(Window, SyncPickers)));
        menu.Items.Add(approvals);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Workspace.Project is null ? "Open a project folder..." : "Switch project...", () => _ = Window.PickProjectAsync()));
        menu.Open(button);
    }

    private static string ModeText(Agex.Core.Orchestration.ChatMode mode) => mode switch
    {
        Agex.Core.Orchestration.ChatMode.Ask => "Ask",
        Agex.Core.Orchestration.ChatMode.Plan => "Plan",
        Agex.Core.Orchestration.ChatMode.Build => "Build",
        _ => "Auto",
    };

    private void ShowModeMenu(Button button)
    {
        var menu = new ContextMenu();
        foreach (var (mode, help) in new[]
        {
            (Agex.Core.Orchestration.ChatMode.Auto, "Auto: AGEX decides (chat, answer, plan or build)"),
            (Agex.Core.Orchestration.ChatMode.Ask, "Ask: answers and explains, changes nothing"),
            (Agex.Core.Orchestration.ChatMode.Plan, "Plan: inspects and writes a plan, changes nothing"),
            (Agex.Core.Orchestration.ChatMode.Build, "Build: the team does the work"),
        })
        {
            var captured = mode;
            menu.Items.Add(Choice(help, Workspace.Mode == mode, () => Workspace.SetMode(captured)));
        }
        menu.Open(button);
    }



    private static Control MenuLabel(string text, string? icon = null) =>
        Kit.Row(4, icon is null ? null : Kit.Icon(icon, 13), Kit.Text(text, "small"), Kit.Icon(Icons.ChevronDown, 11, "Text3Brush"));

    private static MenuItem Choice(string header, bool selected, Action pick)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = selected };
        item.Click += (_, _) => pick();
        return item;
    }

    private void ShowTeamMenu(Button button)
    {
        var menu = new ContextMenu();
        var team = Agex.Core.Teams.JobTeamCatalog.Get(Workspace.Settings.ActiveJobTeam);
        if (team is not null)
        {
            var about = new MenuItem { Header = $"About {team.Name}..." };
            about.Click += (_, _) => { Window.Navigate("teams"); Window.Page<TeamsPage>("teams").OpenSetup(team.Id); };
            menu.Items.Add(about);
            foreach (var example in team.TypicalTasks.Take(3))
            {
                var captured = example;
                var item = new MenuItem { Header = "Try: " + example };
                item.Click += (_, _) => SetRequest(captured);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
        }
        foreach (var job in Agex.Core.Teams.JobTeamCatalog.All)
        {
            var id = job.Id;
            menu.Items.Add(Choice(job.Name, Workspace.Settings.ActiveJobTeam == id, () => PickTeam(id)));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Choice("No Team (general work)", team is null, () => PickTeam("")));
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

    private void ShowModelMenu(Button button)
    {
        var menu = new ContextMenu();
        foreach (var member in Workspace.Members())
        {
            var id = member.Id;
            var options = Workspace.Project?.AgentOptions.GetValueOrDefault(id) ?? Workspace.Settings.AgentOptions.GetValueOrDefault(id);
            var submenu = new MenuItem { Header = member.Name + ": " + (options?.Model is { Length: > 0 } model ? model : "Auto") };
            submenu.Items.Add(Choice("Auto", string.IsNullOrEmpty(options?.Model), () => { Workspace.SelectModel(id, ""); SyncPickers(); }));
            foreach (var listed in Workspace.Core.Models.Cached(id)?.Models.Take(24) ?? [])
            {
                var picked = listed.Id;
                submenu.Items.Add(Choice(listed.Label, options?.Model == picked, () => { Workspace.SelectModel(id, picked); SyncPickers(); }));
            }
            if (Workspace.Settings.Providers.Any(provider => provider.Id == "omniroute") && member.Adapter is CodexAdapter or ProviderAdapter)
                submenu.Items.Add(Choice("OmniRoute Auto", options?.ProviderId == "omniroute" && options.Model == "auto", () =>
                {
                    Workspace.SelectModel(id, "auto", "omniroute"); SyncPickers();
                }));
            menu.Items.Add(submenu);
        }
        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = Workspace.Project is null ? "Manage agents and models..." : "Project models and providers..." };
        settings.Click += (_, _) => Window.Navigate(Workspace.Project is null ? "agents" : "projects");
        menu.Items.Add(settings);
        menu.Open(button);
    }



    private void PickAgents(string id)
    {
        Workspace.Settings.ActiveTeam = id;
        Workspace.SaveSettings();
        RefreshAgents();
    }

    /// <summary>Keeps the chips in step when the Team or mode is changed elsewhere (Teams page, Settings, tips).</summary>
    private void SyncPickers()
    {
        if (_modeButton is not null) _modeButton.Content = MenuLabel(ModeText(Workspace.Mode));
        if (_modelButton is not null)
        {
            var models = Workspace.Members().Select(member => member.Model).Where(model => !string.IsNullOrEmpty(model)).Distinct().ToList();
            _modelButton.Content = MenuLabel(models.Count == 1 ? models[0]! : "Models");
        }
        var team = Agex.Core.Teams.JobTeamCatalog.Get(Workspace.Settings.ActiveJobTeam);
        if (_jobButton is not null)
        {
            // No Team: no chip. A Team in use: one small chip with its name.
            _jobButton.IsVisible = team is not null;
            if (team is not null) _jobButton.Content = MenuLabel(team.Name, Icons.Team);
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
        _continue = null;
        RefreshPlaceholder();
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
            var team = Workspace.Settings.ActiveTeam.Length > 0 ? Workspace.Settings.ActiveTeam : null;
            // Like a chat app: a message sent while a conversation is open continues it.
            var continueFrom = _continue;
            if (await Workspace.StartAsync(text, team, continueFrom: continueFrom)) { _composer.Text = ""; _continue = null; RefreshPlaceholder(); }
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
        _composer.IsEnabled = !Workspace.IsRunning;
        RefreshWelcome();
        RefreshThread();
        RefreshUserBubble();
        RefreshSkillChips();
        RefreshTipsSoon();
        RefreshActions();
        RefreshAgents();
        RefreshQuestion();
        RefreshStatus();
        RefreshLive();
        RefreshResult();
        if (Workspace.IsRunning) _clock.Start(); else _clock.Stop();
        // Empty sections take no space.
        foreach (var slot in new[] { _welcome, _userBubble, _question, _status, _live, _result }) slot.IsVisible = slot.Content is not null;
    }

    private bool _statusPending, _chipsPending, _livePending, _liveExpanded;
    private readonly HashSet<string> _liveOpenGroups = [];
    private string? _liveSessionId;
    private string? _questionId;

    private void RefreshStatusSoon()
    {
        if (_statusPending) return;
        _statusPending = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { _statusPending = false; RefreshStatus(); }, TimeSpan.FromMilliseconds(250));
    }

    private void RefreshLiveSoon()
    {
        if (_livePending) return;
        _livePending = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { _livePending = false; RefreshLive(); }, TimeSpan.FromMilliseconds(250));
    }

    /// <summary>The skills chip follows the message being typed (debounced).</summary>
    private void RefreshChipsSoon()
    {
        if (_chipsPending) return;
        _chipsPending = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { _chipsPending = false; RefreshSkillChips(); }, TimeSpan.FromMilliseconds(400));
    }

    private void RefreshActions()
    {
        _actions.Children.Clear();
        if (Workspace.IsRunning)
        {
            // Running: one Stop. Pause and Take control live in the workspace panel.
            var stop = Kit.Button("", async () =>
            {
                if (Workspace.Session is { Mode: "build" } && !await Window.ConfirmAsync("Stop this request?", "Running agents are stopped. Files they already changed stay changed (Undo is in the result when a snapshot exists).", "Stop", "Keep running")) return;
                Workspace.Cancel();
            }, "round", Icons.Stop, "Stop");
            AutomationProperties.SetName(stop, "Stop");
            _actions.Children.Add(stop);
        }
        else
        {
            var send = Kit.Button("", () => _ = SendAsync(), "primary round", Icons.Send, "Send (Enter)");
            AutomationProperties.SetName(send, "Send");
            _actions.Children.Add(send);
        }
    }

    private void RefreshAgents() => SyncPickers();

    private void RefreshQuestion()
    {
        if (Workspace.Question is not { } question) { _question.Content = null; _questionId = null; return; }
        if (_questionId == question.Id && _question.Content is not null) return;
        _questionId = question.Id;
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            answer.Focus();
            if (_conversationScroll is { } scroll) scroll.Offset = new Vector(0, scroll.Extent.Height);
        }, Avalonia.Threading.DispatcherPriority.Background);
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

    /// <summary>While a request runs: one quiet line for its current state.</summary>
    private void RefreshStatus()
    {
        if (!Workspace.IsRunning || Workspace.Session is not { } session) { _status.Content = null; _status.IsVisible = false; return; }
        var latest = Workspace.Timeline.LastOrDefault()?.Text ?? "Thinking...";
        var line = Kit.Text(latest, "small");
        line.TextWrapping = TextWrapping.Wrap;
        line.MaxLines = 2;
        var elapsed = DateTimeOffset.UtcNow - session.CreatedAt;
        Control? progress = null;
        if (session.Mode == "build")
        {
            var done = Workspace.Tasks.Count(task => task.State == TaskState.Done);
            var total = Workspace.Tasks.Count;
            progress = Kit.Text(total > 0 ? $"{done} of {total} steps · {elapsed:mm\\:ss}" : $"{elapsed:mm\\:ss}", "caption");
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        var bar = new ProgressBar { IsIndeterminate = true, Width = 36, MinWidth = 0, Height = 3, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(bar);
        var text = Kit.Column(2, line, progress);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        _status.Content = grid;
        _status.IsVisible = true;
    }

    /// <summary>Structured live work under the request; folded by default and removed after completion.</summary>
    private void RefreshLive()
    {
        if (!Workspace.IsRunning || Workspace.Session is not { } session)
        {
            _live.Content = null;
            _live.IsVisible = false;
            return;
        }
        if (_liveSessionId != session.Id)
        {
            _liveSessionId = session.Id;
            _liveExpanded = false;
            _liveOpenGroups.Clear();
        }
        var activity = Workspace.LiveEvents.Where(item => item.Activity.Kind != ActivityKind.Status && item.Activity.Text.Length > 0).ToList();
        var timeline = Workspace.Timeline.Where(entry => entry.Kind != TimelineKind.Info || entry.TaskId.Length > 0).ToList();
        var summary = LiveWorkSummary.From(session, activity.Select(item => item.Activity), timeline);
        var activeTask = Workspace.Tasks.LastOrDefault(task => task.State is TaskState.Starting or TaskState.Running or TaskState.Verifying);
        var headline = activeTask is null ? "Working" : $"Working: {activeTask.Label}";
        var counts = new List<string>();
        if (summary.ChangedFiles > 0) counts.Add($"{summary.ChangedFiles} edited file{(summary.ChangedFiles == 1 ? "" : "s")}  +{summary.AddedLines:N0} / -{summary.RemovedLines:N0}");
        if (summary.Commands > 0) counts.Add($"{summary.Commands} command{(summary.Commands == 1 ? "" : "s")}");
        if (summary.Tools > 0) counts.Add($"{summary.Tools} tool{(summary.Tools == 1 ? "" : "s")}");
        if (summary.BrowserActions > 0) counts.Add($"{summary.BrowserActions} browser action{(summary.BrowserActions == 1 ? "" : "s")}");
        if (summary.Tests > 0) counts.Add($"{summary.Tests} test command{(summary.Tests == 1 ? "" : "s")}");
        if (summary.Retries > 0) counts.Add($"{summary.Retries} retr{(summary.Retries == 1 ? "y" : "ies")}");
        var toggle = Kit.Button(_liveExpanded ? "Hide details" : "Show details", () => { _liveExpanded = !_liveExpanded; RefreshLive(); }, "link",
            _liveExpanded ? Icons.ChevronDown : Icons.Chevron);
        var content = Kit.Column(7, Kit.Row(8, Kit.Icon(Icons.Pulse, 15, "AccentBrush"), Kit.Text(headline, "small"), toggle),
            Kit.Text(counts.Count == 0 ? "Activity will appear here as agents work." : string.Join("  ·  ", counts), "caption"));
        if (_liveExpanded)
        {
            var details = Kit.Column(7);
            Control ActivityRow(LiveWorkEvent item)
            {
                var raw = Agex.Core.Runtime.Redactor.Redact(item.Activity.Text);
                var line = Kit.Selectable($"{item.Agent} · {raw}", "small");
                line.TextWrapping = TextWrapping.Wrap;
                return Kit.Row(8, Kit.Text(item.At.ToLocalTime().ToString("HH:mm:ss"), "caption"), line);
            }
            void Group(string key, string title, IEnumerable<Control> rows)
            {
                var listed = rows.ToList();
                if (listed.Count == 0) return;
                var open = _liveOpenGroups.Contains(key);
                var button = Kit.Button($"{title} ({listed.Count})", () =>
                {
                    if (!_liveOpenGroups.Add(key)) _liveOpenGroups.Remove(key);
                    RefreshLive();
                }, "link", open ? Icons.ChevronDown : Icons.Chevron);
                details.Children.Add(button);
                if (open) foreach (var row in listed.TakeLast(24)) details.Children.Add(row);
            }
            Group("files", "Edited files", session.Changes.Select(change => (Control)Kit.Row(8,
                Kit.Text(change.Path, "small"), Kit.Text($"+{change.Added ?? 0:N0} / -{change.Removed ?? 0:N0}", "caption"))));
            Group("commands", "Commands and output", activity.Where(item => item.Activity.Surface == AgentSurface.Terminal).Select(ActivityRow));
            Group("tools", "Tools", activity.Where(item => item.Activity.Surface == AgentSurface.Activity).Select(ActivityRow));
            Group("browser", "Browser", activity.Where(item => item.Activity.Surface == AgentSurface.Browser).Select(ActivityRow));
            Group("tests", "Tests and checks", activity.Where(item => LiveWorkSummary.IsTestActivity(item.Activity)).Select(ActivityRow));
            Group("retries", "Retries and recovery", timeline.Where(entry => entry.Kind == TimelineKind.Fallback
                || entry.Kind == TimelineKind.Warning && entry.Text.Contains("retry", StringComparison.OrdinalIgnoreCase))
                .Select(entry => (Control)Kit.Text(entry.Text, "small")));
            Group("steps", "Task progress", timeline.Where(entry => entry.Kind is TimelineKind.Start or TimelineKind.Done or TimelineKind.Failed)
                .Select(entry => (Control)Kit.Text(entry.Text, "small")));
            if (details.Children.Count > 0) content.Children.Add(details);
        }
        _live.Content = Kit.Panel(content);
        _live.IsVisible = true;
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


    /// <summary>
    /// The finished request: an answer reads like a chat reply; finished work adds a
    /// compact change summary; a failure says what failed and offers fixes that match it.
    /// Technical detail (task results, usage) stays folded.
    /// </summary>
    private void RefreshResult()
    {
        if (Workspace.IsRunning || Workspace.Session is not { Outcome: { } outcome } session) { _result.Content = null; return; }
        var failed = session.Status is SessionStatus.Failed or SessionStatus.StartFailed or SessionStatus.Partial or SessionStatus.Unverified;
        if (session.Mode != "build" && !failed)
        {
            _result.Content = Answer(session, outcome);
            return;
        }
        var (text, tone, icon) = StatusLook(session.Status);
        var headlineText = Kit.Text(outcome.Headline, "subtitle");
        headlineText.TextWrapping = TextWrapping.Wrap;
        var body = Kit.Column(10,
            Who(session, outcome),
            Kit.Row(8, Kit.Badge(text, tone, icon), headlineText),
            failed ? Recovery(session, outcome) : outcome.Reason.Length > 0 ? Markdown.View(outcome.Reason) : null,
            session.Changes.Count > 0 ? ChangeSummary(session) : null,
            !failed && outcome.Verification.Length > 0 ? Wrapped("How it was checked: " + outcome.Verification, "caption") : null);
        var details = Kit.Column(4);
        foreach (var line in outcome.WhatHappened) details.Children.Add(Wrapped("• " + line, "small"));
        foreach (var line in outcome.Results.Take(12)) details.Children.Add(Kit.Selectable(line, "small"));
        details.Children.Add(UsageDetail(session));
        body.Children.Add(Disclosure("Details", details));
        var more = Kit.IconButton(Icons.Chevron, "More actions", () => { });
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            var retryWith = new MenuItem { Header = "Retry with other agents..." };
            retryWith.Click += (_, _) => _ = RetryWithAsync(session);
            var room = new MenuItem { Header = "Open in Agent Room" };
            room.Click += (_, _) => Window.Navigate("room");
            menu.Items.Add(retryWith);
            menu.Items.Add(room);
            menu.Open(more);
        };
        body.Children.Add(Kit.Row(2,
            Kit.IconButton(Icons.Copy, "Copy result", () => _ = Window.CopyAsync(outcome.Headline + "\n\n" + outcome.Reason + "\n\n" + string.Join("\n", outcome.Results))),
            failed ? null : Kit.IconButton(Icons.Refresh, "Run again", () => _ = Workspace.StartAsync(session.Request)),
            session.SnapshotRef.Length > 0 && session.Changes.Count > 0 ? Kit.Button("Undo changes", () => _ = Window.Page<SessionsPage>("sessions").UndoAsync(session), "subtle", Icons.Undo) : null,
            more));
        _result.Content = new Border { Child = body, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    }

    /// <summary>A quiet "Details" toggle: technical content stays folded without a box around it.</summary>
    private static Control Disclosure(string title, Control content)
    {
        content.IsVisible = false;
        var chevron = Kit.Icon(Icons.ChevronDown, 12, "Text3Brush");
        var toggle = new Button { Content = Kit.Row(4, Kit.Text(title, "small"), chevron), Padding = new Thickness(0, 2), HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Classes.Add("link");
        toggle.Click += (_, _) =>
        {
            content.IsVisible = !content.IsVisible;
            chevron.RenderTransform = content.IsVisible ? new RotateTransform(180) : null;
        };
        AutomationProperties.SetName(toggle, title);
        var host = new Border { Child = content, Padding = new Thickness(0, 4, 0, 0) };
        return Kit.Column(2, toggle, host);
    }

    private static TextBlock Wrapped(string text, string classes)
    {
        var block = Kit.Text(text, classes);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    /// <summary>Who answered, how, how long and how many tokens, on one quiet line.</summary>
    private Control Who(Session session, Outcome outcome)
    {
        var name = session.Leader.Length > 0 ? session.Leader : "AGEX";
        var usage = UsageSummary(session);
        var switched = Workspace.Session?.Id == session.Id && Workspace.Timeline.Any(entry => entry.Kind == TimelineKind.Fallback);
        var meta = string.Join(" · ", new[]
        {
            session.Mode is "chat" or "build" ? "" : ConversationList.ModeName(session.Mode),
            outcome.Seconds > 0 ? $"{outcome.Seconds:0.#} s" : "",
            usage,
            switched ? "switched agent automatically" : "",
        }.Where(part => part.Length > 0));
        var row = Kit.Row(8, Kit.Avatar(name, 20), Kit.Text(name, "small"), Kit.Text(meta, "caption"));
        if (switched) ToolTip.SetTip(row, "The first agent could not answer, so another ready agent did. See Activity in the workspace panel.");
        return row;
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
        var content = Kit.Row(10, Kit.Icon(Icons.Diff, 16, "AccentBrush"), Kit.Text($"Changes: {session.Changes.Count} file{(session.Changes.Count == 1 ? "" : "s")}", "body"), plus, minus, Kit.Text("View", "small"));
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left };
        button.Classes.Add("subtle");
        button.Click += (_, _) => Window.ShowChanges();
        AutomationProperties.SetName(button, $"Changes: {session.Changes.Count} files, {added} lines added, {removed} removed. Open the Changes panel.");
        return button;
    }

    /// <summary>Earlier turns of this conversation: your message and AGEX's answer, compact.</summary>
    private void RefreshThread()
    {
        _thread.Children.Clear();
        foreach (var turn in Workspace.Thread)
        {
            _thread.Children.Add(Bubble(turn.Request, null));
            var answer = turn.Outcome is { } outcome ? (outcome.Reason.Length > 0 ? outcome.Reason : outcome.Headline) : SessionStatusText.Label(turn.Status);
            var text = Markdown.View(answer.Length > 1500 ? answer[..1500] + "..." : answer);
            var (label, tone, icon) = StatusLook(turn.Status);
            var ok = turn.Status is SessionStatus.Complete or SessionStatus.CompleteWithFallback;
            var header = Kit.Row(8, Kit.Avatar(turn.Leader.Length > 0 ? turn.Leader : "AGEX", 20), Kit.Text(turn.Leader.Length > 0 ? turn.Leader : "AGEX", "small"),
                ok ? null : Kit.Badge(label, tone, icon),
                turn.Changes.Count > 0 ? Kit.Text($"{turn.Changes.Count} file{(turn.Changes.Count == 1 ? "" : "s")} changed", "caption") : null);
            _thread.Children.Add(new Border { Child = Kit.Column(6, header, text), Padding = new Thickness(2, 0), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left });
        }
        _thread.IsVisible = _thread.Children.Count > 0;
    }

    private static Border Bubble(string message, Control? extra)
    {
        var text = Kit.Selectable(message, "body");
        text.TextWrapping = TextWrapping.Wrap;
        var bubble = new Border { Child = Kit.Column(4, text, extra), Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 680 };
        bubble.Res(Border.BackgroundProperty, "AccentSoftBrush");
        AutomationProperties.SetName(bubble, "Your message");
        return bubble;
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


    /// <summary>
    /// No conversation yet: a short greeting and a few starting points. A Team adds
    /// its own examples; setting it up stays one quiet link.
    /// </summary>
    private void RefreshWelcome()
    {
        if (Workspace.Session is not null || Workspace.Thread.Count > 0) { _welcome.Content = null; _welcome.IsVisible = false; return; }
        var active = Agex.Core.Teams.JobTeamCatalog.Get(Workspace.Settings.ActiveJobTeam);
        var title = Kit.Text("What can I help with?", "title");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var intro = Kit.Text(active is null
            ? "Chat, ask about anything, or give the team work to do in a project folder."
            : $"{active.Name}: {active.Summary}", "small");
        intro.TextWrapping = TextWrapping.Wrap;
        intro.TextAlignment = TextAlignment.Center;
        intro.MaxWidth = 520;
        var examples = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var prompts = active?.TypicalTasks.Take(3).ToList() ?? ["What can AGEX do?", "Explain how OAuth works", "Review the project I open", "Build a landing page"];
        foreach (var prompt in prompts)
        {
            var captured = prompt;
            var example = Kit.Button(prompt, () => SetRequest(captured), "chip");
            example.Margin = new Thickness(3);
            examples.Children.Add(example);
        }
        Control? setup = null;
        if (active is not null)
        {
            var (ready, total, missing) = Agex.Core.Teams.JobTeamService.Progress(Workspace.Core.Teams.Check(active));
            if (missing > 0)
            {
                var link = Kit.Button($"Finish setting up {active.Name} ({ready} of {total} ready)", () => { Window.Navigate("teams"); Window.Page<TeamsPage>("teams").OpenSetup(active.Id); }, "link");
                link.HorizontalAlignment = HorizontalAlignment.Center;
                setup = link;
            }
        }
        var column = Kit.Column(12, title, intro, examples, setup);
        column.Margin = new Thickness(0, 72, 0, 0);
        column.HorizontalAlignment = HorizontalAlignment.Center;
        _welcome.Content = column;
        _welcome.IsVisible = true;
    }

    /// <summary>The skills chip: shown only when the message will use skills or the user chose some.</summary>
    private void RefreshSkillChips()
    {
        var draft = _composer.Text ?? "";
        var skills = Workspace.EffectiveSkills(draft);
        var chosen = Workspace.SkillOverrides.Count > 0;
        _skillsButton.IsVisible = skills.Count > 0 || chosen;
        _skillsButton.Content = Kit.Row(4, Kit.Icon(Icons.Skills, 13), Kit.Text(skills.Count == 0 ? "No skills" : $"{skills.Count} skill{(skills.Count == 1 ? "" : "s")}", "small"), Kit.Icon(Icons.ChevronDown, 11, "Text3Brush"));
        ToolTip.SetTip(_skillsButton, skills.Count == 0 ? "No skills for this message (you removed them)" : (chosen ? "Your choice: " : "Suggested for this message: ") + string.Join(", ", skills.Select(skill => skill.Manifest.Name)));
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
            ActiveSkills = Workspace.EffectiveSkills(_composer.Text ?? "").Select(skill => skill.Id).ToHashSet(),
            InstalledSkills = Workspace.Core.Skills.Installed().Select(skill => skill.Id).ToHashSet(),
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

    private async Task ApplyTipAsync(Agex.Core.Connections.Tip tip)
    {
        switch (tip.Action)
        {
            case Agex.Core.Connections.TipAction.AddSkill:
                var ids = tip.Argument.Split(',');
                var installed = Workspace.Core.Skills.Installed().Select(skill => skill.Id).ToHashSet();
                foreach (var id in ids.Where(id => !installed.Contains(id))) await Window.Page<SkillsPage>("skills").InstallByIdAsync(id);
                installed = Workspace.Core.Skills.Installed().Select(skill => skill.Id).ToHashSet();
                foreach (var id in ids.Where(installed.Contains)) Workspace.SetSkillOverride(id, true);
                break;
            case Agex.Core.Connections.TipAction.Connect:
                if (Workspace.Connections().FirstOrDefault(item => item.Id == tip.Argument) is { } item && item.Actions.FirstOrDefault() is { } action)
                    await ConnectionUi.RunAsync(item, action);
                break;
        }
        RefreshTips();
    }

    /// <summary>A chat-style answer: who answered, the text, and small actions.</summary>
    private Control Answer(Session session, Outcome outcome)
    {
        var actions = Kit.Row(2,
            Kit.IconButton(Icons.Copy, "Copy", () => _ = Window.CopyAsync(outcome.Reason)),
            Kit.IconButton(Icons.Refresh, "Ask again", () => _ = Workspace.StartAsync(session.Request, continueFrom: Workspace.Thread.LastOrDefault())),
            session.Mode == "plan" ? Kit.Button("Build this plan", () => _ = BuildPlanAsync(session), "primary", Icons.Tool, "The team carries out this plan") : null);
        var note = session.Mode is "chat" or "ask" ? null : Wrapped(outcome.Verification, "caption");
        return new Border { Child = Kit.Column(8, Who(session, outcome), Markdown.View(outcome.Reason), note, actions), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    }

    private async Task BuildPlanAsync(Session plan)
    {
        if (Workspace.Project is null) return;
        var team = Workspace.Settings.ActiveTeam.Length > 0 ? Workspace.Settings.ActiveTeam : null;
        var steps = plan.Outcome?.Reason ?? "";
        await Workspace.StartAsync($"Build this plan for: {plan.Request}\n\n{(steps.Length > 6000 ? steps[..6000] : steps)}", team, continueFrom: plan, mode: Agex.Core.Orchestration.ChatMode.Build);
        Refresh();
    }

    /// <summary>What failed and what to do next, with buttons that match the kind of failure.</summary>
    private Control Recovery(Session session, Outcome outcome)
    {
        var why = outcome.PrimaryFailure.Length > 0 ? outcome.PrimaryFailure : "";
        var buttons = Kit.Wrap();
        foreach (var option in outcome.Recovery.Take(4))
        {
            var captured = option;
            var button = Kit.Button(option.Label, () => _ = RecoverAsync(session, captured), buttons.Children.Count == 0 ? "primary" : "");
            ToolTip.SetTip(button, option.Detail);
            button.Margin = new Thickness(0, 0, 6, 6);
            buttons.Children.Add(button);
        }
        if (outcome.Recovery.Count == 0) buttons.Children.Add(Kit.Button("Retry", () => _ = Workspace.StartAsync(session.Request), "primary", Icons.Refresh));
        var reason = outcome.Reason.Length > 0 ? Markdown.View(outcome.Reason) : null;
        var whyText = why.Length > 0 && why != outcome.Reason ? Kit.Selectable(why, "small") : null;
        if (whyText is not null) whyText.TextWrapping = TextWrapping.Wrap;
        return Kit.Column(8, reason, whyText is null ? null : Disclosure("Why it failed", whyText), buttons);
    }

    private async Task RecoverAsync(Session session, RecoveryOption option)
    {
        if (!Enum.TryParse<Agex.Core.Orchestration.RecoveryKind>(option.Kind, out var kind)) return;
        switch (kind)
        {
            case Agex.Core.Orchestration.RecoveryKind.Retry:
                await Workspace.StartAsync(session.Request, continueFrom: Workspace.Thread.LastOrDefault());
                break;
            case Agex.Core.Orchestration.RecoveryKind.TryAnotherAgent:
                await RetryWithAsync(session);
                break;
            case Agex.Core.Orchestration.RecoveryKind.OpenAgents:
                Window.Navigate("agents");
                break;
            default:
                if (await Workspace.FixAsync(kind, option.Argument))
                {
                    Window.Toast(option.Label, "Done. Retrying the request.", ToastKind.Success);
                    await Workspace.StartAsync(session.Request, continueFrom: Workspace.Thread.LastOrDefault());
                }
                break;
        }
        Refresh();
    }

    /// <summary>"15k in · 397 out" for the meta line, or "" when no agent reported usage.</summary>
    private static string UsageSummary(Session session)
    {
        Agex.Core.Agents.UsageReport? total = null;
        foreach (var report in session.Usage.Values) total = Agex.Core.Agents.UsageReport.Combine(total, report);
        if (total is null || total.InputTokens is null && total.OutputTokens is null) return "";
        return $"{UsageHistory.Tokens(total.InputTokens)} in · {UsageHistory.Tokens(total.OutputTokens)} out" + (total.CostUsd is { } cost ? $" · ${cost:0.####}" : "");
    }

    /// <summary>Tokens per agent as the agents reported them (inside Details).</summary>
    private Control UsageDetail(Session session)
    {
        var usage = session.Usage.Where(pair => pair.Value.InputTokens is not null || pair.Value.OutputTokens is not null || pair.Value.CostUsd is not null).ToList();
        if (usage.Count == 0) return Kit.Text("Usage: not reported by these agents.", "caption");
        var rows = Kit.Column(2);
        foreach (var (agent, report) in usage)
            rows.Children.Add(Kit.Text($"{Workspace.Core.Registry.Get(agent)?.Name ?? agent}: {UsageHistory.Describe(report)}", "caption"));
        return rows;
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
