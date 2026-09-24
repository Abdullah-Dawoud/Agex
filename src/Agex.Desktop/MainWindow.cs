using System.IO.Pipes;
using System.Runtime.InteropServices;
using Agex.Core;
using Agex.Core.Orchestration;
using Agex.Core.Settings;
using Agex.Desktop.Pages;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Agex.Desktop;

/// <summary>
/// Application shell: navigation, top bar (project, team, search), the page
/// area, the live team panel, dialogs, toasts and keyboard shortcuts.
/// </summary>
public sealed class MainWindow : Window, IWorkspaceUi
{
    private readonly Workspace _workspace;
    private readonly Grid _root = new();
    private readonly Grid _shell = new();
    private readonly ContentControl _content = new();
    private readonly StackPanel _nav = new() { Spacing = 2 };
    private readonly Border _navHost = new();
    private readonly Border _sidePanel = new();
    private readonly ActivityPanel _activity;
    private readonly WorkspacePanel _panel;
    private readonly GridSplitter _splitter = new() { Width = 5, ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
    private readonly Button _panelToggle;
    private Window? _panelWindow;
    private readonly Dictionary<string, AppPage> _pages = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly Button _projectButton = new();
    private readonly CommandPalette _palette;
    private WindowNotificationManager? _toasts;
    private AppPage? _current;
    private bool _compact;

    public DialogHost Dialogs { get; } = new();
    public Workspace Workspace => _workspace;

    public MainWindow(Workspace workspace)
    {
        _workspace = workspace;
        workspace.Ui = this;
        Title = AgexInfo.DisplayName;
        Width = 1280; Height = 820; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://AgexDesktop/Assets/agex-256.png")));
        _activity = new ActivityPanel(workspace);
        _panel = new WorkspacePanel(this, _activity);
        _panel.CollapseRequested += () => SetPanelOpen(false);
        _panel.PopOutRequested += PopOutPanel;
        _panelToggle = Kit.IconButton(Icons.SidePanel, "Show or hide the workspace panel", () => SetPanelOpen(!_workspace.Settings.WorkspacePanelOpen), Kit.ShortcutText("J"));
        _palette = new CommandPalette(this);

        foreach (var page in new AppPage[] { new HomePage(this), new RoomPage(this), new ProjectsPage(this), new TeamsPage(this), new AgentsPage(this), new SkillsPage(this), new SessionsPage(this), new SettingsPage(this) })
            _pages[page.Id] = page;

        BuildShell();
        _root.Children.Add(_shell);
        _root.Children.Add(_palette);
        _root.Children.Add(Dialogs);
        Content = _root;

        if (!workspace.Settings.FirstRunComplete) ShowOnboarding();
        else Navigate(workspace.State.LastPage is { Length: > 0 } last && _pages.ContainsKey(last) ? last : "home");

        AddShortcuts();
        if (OperatingSystem.IsMacOS()) NativeMenu.SetMenu(this, BuildMacMenu());
        workspace.ProjectChanged += UpdateProjectButton;
        workspace.SessionChanged += UpdateTitle;
        UpdateProjectButton();
        SizeChanged += (_, _) => UpdateLayoutForWidth();
        Opened += OnOpened;
        Activated += (_, _) => StopFlash();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        _toasts = new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 3 };
        ListenForActivation();
        foreach (var notice in _workspace.Core.StartupNotices) Toast("AGEX", notice, ToastKind.Info);
        // Staged discovery: the window is already usable; results arrive in the background.
        await _workspace.ScanAsync();
        await CheckForUpdatesQuietlyAsync();
    }

    /// <summary>At most once a day, and only when the user allows it. Offline is not an error.</summary>
    private async Task CheckForUpdatesQuietlyAsync()
    {
        var state = _workspace.State;
        if (!_workspace.Settings.CheckForUpdates || DateTimeOffset.UtcNow - state.LastUpdateCheck < TimeSpan.FromDays(1)) return;
        state.LastUpdateCheck = DateTimeOffset.UtcNow;
        _workspace.SaveState();
        try
        {
            var info = await _workspace.Core.Updates.CheckAsync(CancellationToken.None);
            if (info.Available) Toast($"AGEX {info.LatestVersion} is available", "Open Settings > Updates to install it.", ToastKind.Info);
        }
        catch (Exception ex) { _workspace.Core.Log.Error("update_check_failed", ex); }
    }

    // ---------------------------------------------------------------- shell

    private void BuildShell()
    {
        _shell.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto");
        var brand = Kit.Row(10, new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://AgexDesktop/Assets/agex-256.png"))), Width = 26, Height = 26 }, Kit.Text("AGEX", "subtitle"));
        brand.Margin = new Thickness(6, 4, 6, 18);
        _nav.Children.Add(brand);
        foreach (var page in _pages.Values)
        {
            var id = page.Id;
            var index = _navButtons.Count + 1;
            var label = new TextBlock { Text = page.Title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            var button = new Button { Content = Kit.Row(12, Kit.Icon(page.Icon, 18), label) };
            button.Classes.Add("nav");
            button.Click += (_, _) => Navigate(id);
            ToolTip.SetTip(button, $"{page.Title}  ({Kit.ShortcutText(index.ToString())})");
            AutomationProperties.SetName(button, page.Title);
            _navButtons[id] = button;
            if (id == "settings") _nav.Children.Add(new Border { Height = 12 });
            _nav.Children.Add(button);
        }
        _navHost.Child = new DockPanel { Children = { _nav } };
        _navHost.Padding = new Thickness(12, 16);
        _navHost.Width = 212;
        _navHost.BorderThickness = new Thickness(0, 0, 1, 0);
        _navHost.Res(Border.BorderBrushProperty, "BorderBrush");
        _navHost.Res(Border.BackgroundProperty, "SurfaceBrush");
        _shell.Children.Add(_navHost);

        var main = new DockPanel();
        var top = BuildTopBar();
        DockPanel.SetDock(top, Dock.Top);
        main.Children.Add(top);
        main.Children.Add(_content);
        Grid.SetColumn(main, 1);
        _shell.Children.Add(main);

        _sidePanel.Child = _panel;
        _sidePanel.BorderThickness = new Thickness(1, 0, 0, 0);
        _sidePanel.Res(Border.BorderBrushProperty, "BorderBrush");
        _sidePanel.Res(Border.BackgroundProperty, "SurfaceBrush");
        _shell.ColumnDefinitions[3].Width = new GridLength(Math.Clamp(_workspace.Settings.WorkspacePanelWidth, PanelMinWidth, PanelMaxWidth));
        _shell.ColumnDefinitions[3].MinWidth = 0;
        _shell.ColumnDefinitions[3].MaxWidth = PanelMaxWidth;
        Grid.SetColumn(_splitter, 2);
        _splitter.DragCompleted += (_, _) =>
        {
            _workspace.Settings.WorkspacePanelWidth = Math.Round(Math.Clamp(_shell.ColumnDefinitions[3].ActualWidth, PanelMinWidth, PanelMaxWidth));
            _workspace.SaveSettings();
        };
        Avalonia.Automation.AutomationProperties.SetName(_splitter, "Resize the workspace panel");
        _shell.Children.Add(_splitter);
        Grid.SetColumn(_sidePanel, 3);
        _shell.Children.Add(_sidePanel);
    }

    private Control BuildTopBar()
    {
        _projectButton.Classes.Add("subtle");
        _projectButton.Click += (_, _) => ShowProjectMenu();
        AutomationProperties.SetName(_projectButton, "Current project");
        ToolTip.SetTip(_projectButton, $"Switch project  ({Kit.ShortcutText("O")})");
        var search = Kit.Button("Search and commands", () => _palette.Open(), "subtle", Icons.Search, shortcut: Kit.ShortcutText("K"));
        var theme = Kit.IconButton(Icons.Moon, "Switch light or dark theme", ToggleTheme, Kit.ShortcutText("L", shift: true));
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 10, 16, 6) };
        bar.Children.Add(_projectButton);
        var right = Kit.Row(4, search, _panelToggle, theme);
        Grid.SetColumn(right, 1);
        bar.Children.Add(right);
        return bar;
    }

    private void UpdateProjectButton()
    {
        var project = _workspace.Project;
        _projectButton.Content = Kit.Row(8,
            Kit.Icon(Icons.Folder, 18, project is null ? "Text3Brush" : "AccentBrush"),
            Kit.Column(0, Kit.Text(project?.Name ?? "No project open", "subtitle"), Kit.Text(project is null ? "Choose a folder to work in" : Agex.Core.Runtime.Redactor.RedactPaths(project.Path), "caption").Trimmed(420)),
            Kit.Icon(Icons.ChevronDown, 14));
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var running = _workspace.IsRunning ? " - working" : "";
        Title = _workspace.Project is { } project ? $"{project.Name} - AGEX{running}" : AgexInfo.DisplayName;
    }

    private void UpdateLayoutForWidth()
    {
        var compact = Bounds.Width < 980;
        if (compact != _compact)
        {
            _compact = compact;
            _navHost.Width = compact ? 64 : 212;
            foreach (var button in _navButtons.Values)
                if (button.Content is StackPanel row && row.Children.Count > 1) row.Children[1].IsVisible = !compact;
            if (_nav.Children[0] is StackPanel brand && brand.Children.Count > 1) brand.Children[1].IsVisible = !compact;
        }
        // The panel docks on the right when there is room for it next to the page (1366 x 768 screens included).
        // 1040 DIPs covers a 1366 x 768 screen at 125% scaling (about 1093 DIPs wide).
        var show = _panelWindow is null && _workspace.Settings.WorkspacePanelOpen && Bounds.Width >= 1040 && _current?.Id is "home" or "room";
        _sidePanel.IsVisible = show;
        _splitter.IsVisible = show;
        var column = _shell.ColumnDefinitions[3];
        if (!show) { column.MinWidth = 0; column.Width = new GridLength(0); }
        else
        {
            // Small windows keep at least about 60% for the page.
            var limit = Math.Max(PanelMinWidth, Math.Min(PanelMaxWidth, Bounds.Width * (Bounds.Width < 1300 ? 0.3 : 0.42)));
            var width = Math.Clamp(_workspace.Settings.WorkspacePanelWidth, PanelMinWidth, limit);
            if (column.Width.Value < 1 || column.Width.Value > limit) column.Width = new GridLength(width);
            column.MinWidth = PanelMinWidth;
            column.MaxWidth = Math.Max(PanelMinWidth, Math.Min(PanelMaxWidth, Bounds.Width * 0.5));
        }
        _panelToggle.IsVisible = _current?.Id is "home" or "room";
    }

    private const double PanelMinWidth = 280, PanelMaxWidth = 900;

    public WorkspacePanel WorkspacePanel => _panel;

    public void SetPanelOpen(bool open)
    {
        if (_panelWindow is not null) { _panelWindow.Activate(); return; }
        _workspace.Settings.WorkspacePanelOpen = open;
        _workspace.SaveSettings();
        _shell.ColumnDefinitions[3].Width = new GridLength(0);
        UpdateLayoutForWidth();
    }

    /// <summary>Moves the panel into its own resizable window; closing that window docks it again.</summary>
    private void PopOutPanel()
    {
        if (_panelWindow is not null) { _panelWindow.Activate(); return; }
        _sidePanel.Child = null;
        _panelWindow = new Window
        {
            Title = "AGEX workspace", Width = 760, Height = 820, MinWidth = 420, MinHeight = 400, Icon = Icon,
            Content = new Border { Child = _panel }.Res(Border.BackgroundProperty, "SurfaceBrush"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        _panelWindow.Closed += (_, _) =>
        {
            if (_panelWindow?.Content is Border border) border.Child = null;
            _panelWindow = null;
            _sidePanel.Child = _panel;
            _shell.ColumnDefinitions[3].Width = new GridLength(0);
            UpdateLayoutForWidth();
        };
        _panelWindow.Show(this);
        UpdateLayoutForWidth();
    }

    public void Navigate(string id)
    {
        if (!_pages.TryGetValue(id, out var page)) return;
        _current = page;
        _content.Content = page.View;
        foreach (var (key, button) in _navButtons) button.Classes.Set("active", key == id);
        page.OnShown();
        _workspace.State.LastPage = id;
        UpdateLayoutForWidth();
    }

    public T Page<T>(string id) where T : AppPage => (T)_pages[id];

    public void ShowOnboarding()
    {
        var onboarding = new OnboardingView(this, () =>
        {
            _root.Children.RemoveAt(0);
            _root.Children.Insert(0, _shell);
            Navigate("home");
        });
        _root.Children.RemoveAt(0);
        _root.Children.Insert(0, onboarding);
    }

    // -------------------------------------------------------------- projects

    public async Task PickProjectAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a project folder", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _workspace.OpenProject(path);
    }

    private void ShowProjectMenu()
    {
        var menu = new ContextMenu();
        foreach (var path in _workspace.Settings.RecentProjects.Take(8))
        {
            var item = new MenuItem { Header = $"{Path.GetFileName(path.TrimEnd('/', '\\'))}  —  {Agex.Core.Runtime.Redactor.RedactPaths(path)}" };
            item.Click += (_, _) => _workspace.OpenProject(path);
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var open = new MenuItem { Header = "Open folder...", InputGesture = Kit.Gesture(Key.O) };
        open.Click += async (_, _) => await PickProjectAsync();
        menu.Items.Add(open);
        var manage = new MenuItem { Header = "Manage projects" };
        manage.Click += (_, _) => Navigate("projects");
        menu.Items.Add(manage);
        menu.Open(_projectButton);
    }

    // ----------------------------------------------------------------- theme

    public void ToggleTheme()
    {
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark || ActualThemeVariant.InheritVariant == Avalonia.Styling.ThemeVariant.Dark;
        _workspace.Settings.Theme = dark ? ThemeChoice.Light : ThemeChoice.Dark;
        _workspace.SaveSettings();
        App.ApplyTheme(_workspace.Settings.Theme);
    }

    // ------------------------------------------------------------- shortcuts

    private void AddShortcuts()
    {
        void Bind(KeyGesture gesture, Action action) => KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new RelayCommand(action) });
        Bind(Kit.Gesture(Key.K), () => _palette.Open());
        Bind(Kit.Gesture(Key.P, shift: true), () => _palette.Open());
        Bind(Kit.Gesture(Key.O), () => _ = PickProjectAsync());
        Bind(Kit.Gesture(Key.N), () => { Navigate("home"); Page<HomePage>("home").FocusComposer(clear: true); });
        Bind(Kit.Gesture(Key.OemComma), () => Navigate("settings"));
        Bind(Kit.Gesture(Key.L, shift: true), ToggleTheme);
        Bind(Kit.Gesture(Key.F), () => { Navigate("room"); Page<RoomPage>("room").FocusSearch(); });
        Bind(Kit.Gesture(Key.J), () => SetPanelOpen(!_workspace.Settings.WorkspacePanelOpen));
        var keys = new[] { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7 };
        var ids = _pages.Keys.ToList();
        for (var index = 0; index < Math.Min(keys.Length, ids.Count); index++)
        {
            var id = ids[index];
            Bind(Kit.Gesture(keys[index]), () => Navigate(id));
        }
    }

    public IReadOnlyList<PaletteCommand> Commands() =>
    [
        new("New request", Icons.Plus, Kit.ShortcutText("N"), () => { Navigate("home"); Page<HomePage>("home").FocusComposer(clear: true); }),
        new("Open project...", Icons.Folder, Kit.ShortcutText("O"), () => _ = PickProjectAsync()),
        new("Go to Home", Icons.Home, Kit.ShortcutText("1"), () => Navigate("home")),
        new("Go to Agent Room", Icons.Room, Kit.ShortcutText("2"), () => Navigate("room")),
        new("Change agents and teams", Icons.Agent, "", () => Navigate("agents")),
        new("Install a skill", Icons.Skills, "", () => Navigate("skills")),
        new("Search sessions", Icons.History, "", () => { Navigate("sessions"); Page<SessionsPage>("sessions").FocusSearch(); }),
        new("Open diagnostics", Icons.Info, "", () => { Navigate("settings"); Page<SettingsPage>("settings").ShowSection("diagnostics"); }),
        new("Toggle light or dark theme", Icons.Moon, Kit.ShortcutText("L", shift: true), ToggleTheme),
        new("Scan this computer again", Icons.Refresh, "", () => _ = _workspace.ScanAsync()),
        new("Check for updates", Icons.Download, "", () => { Navigate("settings"); Page<SettingsPage>("settings").ShowSection("updates"); }),
        new("Settings", Icons.Settings, Kit.ShortcutText(","), () => Navigate("settings")),
        new("Keyboard shortcuts", Icons.Keyboard, "", ShowShortcuts),
    ];

    private void ShowShortcuts()
    {
        var rows = new (string Keys, string Action)[]
        {
            (Kit.ShortcutText("K"), "Search and commands"), (Kit.ShortcutText("Enter"), "Send request (in the request box)"),
            (Kit.ShortcutText("N"), "New request"), (Kit.ShortcutText("O"), "Open project"), (Kit.ShortcutText("F"), "Search the Agent Room"),
            (Kit.ShortcutText("1") + " ... " + Kit.ShortcutText("7"), "Go to a page"), (Kit.ShortcutText(","), "Settings"),
            (Kit.ShortcutText("L", shift: true), "Light or dark theme"), ("Esc", "Close dialogs"),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 16 };
        for (var index = 0; index < rows.Length; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var keys = Kit.Text(rows[index].Keys, "mono");
            var action = Kit.Text(rows[index].Action, "body");
            Grid.SetRow(keys, index); Grid.SetRow(action, index); Grid.SetColumn(action, 1);
            grid.Children.Add(keys); grid.Children.Add(action);
        }
        _ = Dialogs.ShowAsync("Keyboard shortcuts", Kit.Column(8, Kit.Text("Shortcuts are optional; every action is also a button.", "small"), grid), ["Close"]);
    }

    private NativeMenu BuildMacMenu()
    {
        NativeMenuItem Item(string header, Action action, KeyGesture? gesture = null)
        {
            var item = new NativeMenuItem(header) { Gesture = gesture };
            item.Click += (_, _) => action();
            return item;
        }
        var file = new NativeMenu();
        file.Items.Add(Item("New Request", () => { Navigate("home"); Page<HomePage>("home").FocusComposer(clear: true); }, Kit.Gesture(Key.N)));
        file.Items.Add(Item("Open Project...", () => _ = PickProjectAsync(), Kit.Gesture(Key.O)));
        var view = new NativeMenu();
        foreach (var page in _pages.Values) { var id = page.Id; view.Items.Add(Item(page.Title, () => Navigate(id))); }
        view.Items.Add(new NativeMenuItemSeparator());
        view.Items.Add(Item("Toggle Dark Mode", ToggleTheme, Kit.Gesture(Key.L, shift: true)));
        var help = new NativeMenu();
        help.Items.Add(Item("Keyboard Shortcuts", ShowShortcuts));
        help.Items.Add(Item("Diagnostics", () => { Navigate("settings"); Page<SettingsPage>("settings").ShowSection("diagnostics"); }));
        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("File") { Menu = file });
        menu.Items.Add(new NativeMenuItem("View") { Menu = view });
        menu.Items.Add(new NativeMenuItem("Help") { Menu = help });
        return menu;
    }

    // ------------------------------------------------------ IWorkspaceUi

    public void Toast(string title, string message, ToastKind kind) =>
        _toasts?.Show(new Notification(title, message, kind switch { ToastKind.Success => NotificationType.Success, ToastKind.Error => NotificationType.Error, _ => NotificationType.Information }, TimeSpan.FromSeconds(kind == ToastKind.Error ? 10 : 5)));

    public void Notify(string title, string message, ToastKind kind, bool onlyWhenInactive = false)
    {
        if (!(onlyWhenInactive && IsActive)) Toast(title, message, kind);
        if (IsActive) return;
        if (!_workspace.Core.Platform.Notify(title, message)) Flash();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirm, string cancel) =>
        await Dialogs.ShowAsync(title, Kit.Text(message, "body"), [confirm, cancel]) == 0;

    public async Task<ApprovalDecision> ApprovalAsync(ApprovalRequest request)
    {
        var body = Kit.Column(10,
            Kit.Text(request.Detail, "body"),
            Kit.Text("Agents that may change files: " + string.Join(", ", request.Agents), "small"),
            Kit.Text("You can trust a project to skip this question next time (Projects > this project).", "caption"));
        var choice = await Dialogs.ShowAsync(request.Title, body, ["Allow", "Allow and trust this project", "Don't allow"], defaultIndex: 0, cancelIndex: 2);
        return choice switch { 0 => ApprovalDecision.Allow, 1 => ApprovalDecision.AllowAndTrust, _ => ApprovalDecision.Deny };
    }

    public async Task CopyAsync(string text)
    {
        if (Clipboard is null) return;
        var item = new Avalonia.Input.DataTransferItem();
        item.Set(Avalonia.Input.DataFormat.Text, text);
        var data = new Avalonia.Input.DataTransfer();
        data.Add(item);
        await Clipboard.SetDataAsync(data);
        Toast("Copied", "Copied to the clipboard.", ToastKind.Success);
    }

    // ------------------------------------------------------ single instance

    private void ListenForActivation()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(Program.ActivationPipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    server.ReadByte();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                        Show();
                        Activate();
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(2000); }
                catch (Exception) { return; }
            }
        });
    }

    // ------------------------------------------------ Windows taskbar flash

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags; public uint Count; public uint Timeout; }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    private void Flash()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle()?.Handle is not { } handle) return;
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = handle, Flags = 3 | 12, Count = 0, Timeout = 0 };
        FlashWindowEx(ref info);
    }

    private void StopFlash()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle()?.Handle is not { } handle) return;
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = handle, Flags = 0 };
        FlashWindowEx(ref info);
    }
}

public sealed class RelayCommand(Action action) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}
