using Agex.Core;
using Agex.Core.Settings;
using Agex.Core.Updates;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Agex.Desktop.Pages;

/// <summary>Settings, grouped in plain-language sections. Diagnostics live here too.</summary>
public sealed class SettingsPage(MainWindow window) : AppPage(window)
{
    private readonly StackPanel _sections = new() { Spacing = 2 };
    private readonly ContentControl _body = new();
    private string _section = "general";
    private UpdateInfo? _update;

    public override string Id => "settings";
    public override string Title => "Settings";
    public override string Icon => Icons.Settings;

    private static readonly (string Id, string Title)[] Sections =
    [
        ("general", "General"), ("appearance", "Appearance"), ("safety", "Approvals and safety"), ("privacy", "Privacy"),
        ("sessions", "Sessions"), ("notifications", "Notifications"), ("updates", "Updates"), ("backup", "Backup and import"),
        ("advanced", "Advanced"), ("diagnostics", "Diagnostics"),
    ];

    protected override Control Build()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*"), ColumnSpacing = 24 };
        grid.Children.Add(_sections);
        Grid.SetColumn(_body, 1);
        grid.Children.Add(_body);
        var view = Kit.Page(Kit.Column(0, Kit.PageHeader("Settings"), grid), 1100);
        view.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 760;
            grid.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("200,*");
            grid.RowDefinitions = narrow ? new RowDefinitions("Auto,Auto") : new RowDefinitions("*");
            Grid.SetColumn(_body, narrow ? 0 : 1);
            Grid.SetRow(_body, narrow ? 1 : 0);
            grid.RowSpacing = 16;
        };
        Show();
        return view;
    }

    public void ShowSection(string id) { _section = id; if (_body.Content is not null || true) Show(); }

    public override void OnShown() => Show();

    private void Show()
    {
        _sections.Children.Clear();
        foreach (var (id, title) in Sections)
        {
            var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Classes.Add("nav");
            if (id == _section) button.Classes.Add("active");
            button.Click += (_, _) => { _section = id; Show(); };
            AutomationProperties.SetName(button, title);
            _sections.Children.Add(button);
        }
        _body.Content = Kit.Card(_section switch
        {
            "appearance" => Appearance(),
            "safety" => Safety(),
            "privacy" => Privacy(),
            "sessions" => SessionsSection(),
            "notifications" => Notifications(),
            "updates" => Updates(),
            "backup" => Backup(),
            "advanced" => Advanced(),
            "diagnostics" => Diagnostics(),
            _ => General(),
        }, 20);
    }

    private AgexSettings S => Workspace.Settings;
    private void Save() => Workspace.SaveSettings();

    private static ToggleSwitch Toggle(bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = "On", OffContent = "Off" };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return toggle;
    }

    private Control General()
    {
        var executable = Environment.ProcessPath ?? "";
        return Kit.Column(4,
            Kit.SectionHeader("General"),
            Kit.SettingRow("Start AGEX when you sign in", OperatingSystem.IsMacOS() ? "Adds a login item for your user only." : "Starts for your user only; no administrator rights.", Toggle(S.StartWithSystem, value =>
            {
                S.StartWithSystem = value;
                try { Workspace.Core.Platform.SetStartWithSystem(value, executable); Save(); }
                catch (Exception ex) { Window.Toast("Could not change start-up setting", ex.Message, ToastKind.Error); }
            })),
            Kit.SettingRow("Start minimized", null, Toggle(S.LaunchMinimized, value => { S.LaunchMinimized = value; Save(); })),
            Kit.SettingRow("Run setup again", "Scan, choose agents, skills and a project.", Kit.Button("Open setup", () => Window.ShowOnboarding())));
    }

    private Control Appearance()
    {
        var scale = new Slider { Minimum = 0.85, Maximum = 1.6, Value = S.TextScale, Width = 220, TickFrequency = 0.05, IsSnapToTickEnabled = true };
        AutomationProperties.SetName(scale, "Text size");
        var label = Kit.Text($"{S.TextScale:P0}", "small");
        scale.ValueChanged += (_, _) => { S.TextScale = Math.Round(scale.Value, 2); label.Text = $"{S.TextScale:P0}"; App.ApplyTextScale(S.TextScale); Save(); };
        return Kit.Column(4,
            Kit.SectionHeader("Appearance"),
            Kit.SettingRow("Theme", "System follows your computer, including high-contrast mode. Changes apply immediately.",
                Kit.Combo([(ThemeChoice.System, "System"), (ThemeChoice.Light, "Light"), (ThemeChoice.Dark, "Dark")], S.Theme, value => { S.Theme = value; Save(); App.ApplyTheme(value); })),
            Kit.SettingRow("Text size", "Makes all text larger or smaller.", Kit.Row(8, scale, label)));
    }

    private Control Safety() => Kit.Column(4,
        Kit.SectionHeader("Approvals and safety", "Safe defaults that stay out of your way."),
        Kit.SettingRow("Ask before agents change files", "Asked once per request. Trusted projects skip the question.", Toggle(S.Approvals.AskBeforeWrites, value => { S.Approvals.AskBeforeWrites = value; Save(); })),
        Kit.SettingRow("Allow agents to run commands", "Off = agents that support it run without shell commands (Claude Code, Gemini CLI). Codex always runs commands inside its sandbox.", Toggle(S.Approvals.AllowCommands, value => { S.Approvals.AllowCommands = value; Save(); })),
        Kit.SettingRow("Save a Git snapshot before changes", "Lets you undo a request's changes from Sessions. Only for projects that use Git; your branches and staged files are not touched.", Toggle(S.Approvals.SnapshotBeforeWrites, value => { S.Approvals.SnapshotBeforeWrites = value; Save(); })));

    private Control Privacy() => Kit.Column(10,
        Kit.SectionHeader("Privacy"),
        Kit.Text("AGEX itself sends no telemetry and has no account. Settings, sessions and logs stay on this computer.", "body"),
        Kit.Text("Cloud agents (Codex, Antigravity, Claude Code, Gemini CLI, Ollama cloud models) send your requests and the files they read to their providers under the providers' terms. Ollama with local models keeps everything on this computer. Each agent's destination is shown in Agents.", "small"),
        Kit.Text("Network use by AGEX: checking GitHub for AGEX updates (if on), downloading skills you install, and nothing else.", "small"),
        Kit.SettingRow("Explain cloud use before the first request in a project", null, Toggle(S.Privacy.ExplainCloudUse, value => { S.Privacy.ExplainCloudUse = value; Save(); })),
        Kit.SettingRow("Secure storage for tokens", Workspace.Core.Platform.SecureStore.IsOsProtected ? "Protected by the operating system." : "No system keyring was found: tokens are stored in a private file that is not encrypted.", Kit.Text(Workspace.Core.Platform.SecureStore.Mechanism, "small")));

    private Control SessionsSection() => Kit.Column(4,
        Kit.SectionHeader("Sessions"),
        Kit.SettingRow("Keep sessions", "Older sessions are deleted automatically. Running sessions are never deleted.",
            Kit.Combo([(30, "30 days"), (90, "90 days"), (365, "1 year"), (0, "Forever")], S.Sessions.RetentionDays, value => { S.Sessions.RetentionDays = value; Save(); })),
        Kit.SettingRow("Delete all sessions", "Removes AGEX history only. Project files are not touched.", Kit.Button("Delete all...", async () =>
        {
            if (!await Window.ConfirmAsync("Delete all sessions?", "This cannot be undone. Project files are not touched.", "Delete all", "Cancel")) return;
            foreach (var session in Workspace.Core.Sessions.List()) Workspace.Core.Sessions.Delete(session.Id);
            Window.Toast("Sessions deleted", "History is empty.", ToastKind.Success);
        }, "danger", Icons.Trash)));

    private Control Notifications() => Kit.Column(4,
        Kit.SectionHeader("Notifications", OperatingSystem.IsWindows() ? "When AGEX is in the background it flashes its taskbar button and shows the message inside the window." : "Shown by your system when AGEX is in the background."),
        Kit.SettingRow("Notifications", null, Toggle(S.Notifications.Enabled, value => { S.Notifications.Enabled = value; Save(); })),
        Kit.SettingRow("When a request finishes", null, Toggle(S.Notifications.OnComplete, value => { S.Notifications.OnComplete = value; Save(); })),
        Kit.SettingRow("When a request fails", null, Toggle(S.Notifications.OnFailure, value => { S.Notifications.OnFailure = value; Save(); })),
        Kit.SettingRow("When an agent needs your answer", null, Toggle(S.Notifications.OnInputNeeded, value => { S.Notifications.OnInputNeeded = value; Save(); })),
        Kit.SettingRow("When approval is needed", null, Toggle(S.Notifications.OnApproval, value => { S.Notifications.OnApproval = value; Save(); })));

    private Control Updates()
    {
        var status = Kit.Text(_update?.Message ?? $"You have AGEX {AgexInfo.Version}.", "body");
        var progress = new ProgressBar { IsVisible = false, Minimum = 0, Maximum = 1, Height = 6 };
        var install = Kit.Button("Download and install", async () =>
        {
            if (_update is null) return;
            try
            {
                progress.IsVisible = true;
                var downloaded = await Workspace.Core.Updates.DownloadAsync(_update, new Progress<double>(value => progress.Value = value), CancellationToken.None);
                var how = downloaded.SignatureVerified ? "Its signature and checksum were verified." : "Its checksum was verified (this release is not signed).";
                if (!await Window.ConfirmAsync($"Install AGEX {downloaded.Version}?", $"{how} AGEX closes, updates only its own files, and starts again. Your settings and sessions are kept.", "Install now", "Later")) return;
                if (!Workspace.Core.Updates.StartInstaller(downloaded)) { await Window.Dialogs.MessageAsync("Update not started", "The installer that came with AGEX is missing. Download the new version from the releases page instead."); return; }
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or InvalidOperationException)
            {
                await Window.Dialogs.MessageAsync("Update failed", ex.Message + "\nNothing was changed.");
            }
            finally { progress.IsVisible = false; }
        }, "primary", Icons.Download);
        install.IsVisible = _update?.Available == true;
        return Kit.Column(10,
            Kit.SectionHeader("Updates", "Updates come from AGEX's GitHub releases and are checked before anything runs."),
            status,
            _update?.Available == true && _update.Notes.Length > 0 ? new Expander { Header = "What's new", Content = Kit.Selectable(_update.Notes, "small"), HorizontalAlignment = HorizontalAlignment.Stretch } : null,
            progress,
            Kit.Row(8, Kit.Button("Check now", async () => { status.Text = "Checking..."; _update = await Workspace.Core.Updates.CheckAsync(CancellationToken.None); Show(); }, "", Icons.Refresh), install),
            Kit.SettingRow("Check for updates automatically", "Once a day, when AGEX starts.", Toggle(S.CheckForUpdates, value => { S.CheckForUpdates = value; Save(); })),
            Kit.Text(UpdateService.ReleasePublicKeyPem.Length > 0 ? "Releases must carry a valid signature from the AGEX release key." : "This build verifies release checksums. Signature checking turns on when the project publishes a release signing key.", "caption"));
    }

    private Control Backup()
    {
        var includeProjects = new CheckBox { Content = "Include project list and project settings", IsChecked = true };
        return Kit.Column(10,
            Kit.SectionHeader("Backup and import", "Settings files contain no tokens or passwords. Move them to another computer or keep a copy."),
            includeProjects,
            Kit.Wrap(
                Kit.Button("Export settings...", async () =>
                {
                    var file = await Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export AGEX settings", SuggestedFileName = "agex-settings.json", DefaultExtension = "json" });
                    if (file?.TryGetLocalPath() is not { } path) return;
                    Workspace.Core.SettingsStore.ExportTo(path, includeProjects.IsChecked == true, Workspace.Core.Skills.Installed().Select(skill => skill.Id));
                    Window.Toast("Settings exported", Path.GetFileName(path), ToastKind.Success);
                }, "", Icons.Download),
                Kit.Button("Import settings...", async () =>
                {
                    var files = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import AGEX settings", FileTypeFilter = [new FilePickerFileType("AGEX settings") { Patterns = ["*.json"] }] });
                    if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                    try
                    {
                        var skills = Workspace.Core.SettingsStore.Import(path, includeProjects.IsChecked == true);
                        Workspace.Core.ReloadSettings();
                        App.ApplyTheme(Workspace.Settings.Theme);
                        App.ApplyTextScale(Workspace.Settings.TextScale);
                        var missing = skills.Except(Workspace.Core.Skills.Installed().Select(skill => skill.Id)).ToList();
                        await Window.Dialogs.MessageAsync("Settings imported", "A backup of your previous settings was saved first." + (missing.Count > 0 ? $"\nThe export also lists skills you do not have yet: {string.Join(", ", missing)}. Install them from Skills." : ""));
                        Show();
                    }
                    catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException) { await Window.Dialogs.MessageAsync("Import failed", ex.Message); }
                }, "", Icons.External),
                Kit.Button("Back up now", () =>
                {
                    var folder = Workspace.Core.SettingsStore.Backup("manual");
                    Window.Toast(folder is null ? "Backup failed" : "Backup saved", folder is null ? "See Diagnostics." : Agex.Core.Runtime.Redactor.RedactPaths(folder), folder is null ? ToastKind.Error : ToastKind.Success);
                }, "", Icons.Shield),
                Kit.Button("Open backups folder", () => { Directory.CreateDirectory(Workspace.Core.Platform.Paths.Backups); Workspace.Core.Platform.OpenPath(Workspace.Core.Platform.Paths.Backups); }, "subtle", Icons.Folder)));
    }

    private Control Advanced() => Kit.Column(6,
        Kit.SectionHeader("Advanced"),
        Kit.SettingRow("AGEX data folder", Agex.Core.Runtime.Redactor.RedactPaths(Workspace.Core.Platform.Paths.DataRoot), Kit.Button("Open", () => Workspace.Core.Platform.OpenPath(Workspace.Core.Platform.Paths.DataRoot), "subtle", Icons.Folder)),
        Kit.SettingRow("Logs", Agex.Core.Runtime.Redactor.RedactPaths(Workspace.Core.Platform.Paths.LogsRoot), Kit.Button("Open", () => { Directory.CreateDirectory(Workspace.Core.Platform.Paths.LogsRoot); Workspace.Core.Platform.OpenPath(Workspace.Core.Platform.Paths.LogsRoot); }, "subtle", Icons.Folder)),
        Kit.SettingRow("Restart in safe mode", "Turns off all skills and restores defaults for this run. Use it if a skill causes problems.", Kit.Button("Restart in safe mode", RestartSafeMode, "", Icons.Shield)),
        Kit.Text(Workspace.Core.SafeMode ? "AGEX is running in safe mode now. Restart normally to use skills again." : "", "small"),
        Kit.Text("Agent routing, timeouts and parallel tasks are under Agents > How work is shared > Advanced.", "caption"));

    private void RestartSafeMode()
    {
        if (Environment.ProcessPath is not { } path) return;
        var psi = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = false };
        psi.ArgumentList.Add("--safe-mode");
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        lifetime?.Shutdown();
        System.Diagnostics.Process.Start(psi)?.Dispose();
    }

    private Control Diagnostics()
    {
        var report = Workspace.Core.DiagnosticsReport(Workspace.Scan);
        var text = Kit.Selectable(report, "mono");
        var repairOutput = Kit.Column(4);
        return Kit.Column(10,
            Kit.SectionHeader("Diagnostics", "Share this when asking for help. Tokens, passwords and your user name are removed."),
            Kit.Wrap(
                Kit.Button("Copy diagnostics", () => _ = Window.CopyAsync(report), "primary", Icons.Copy),
                Kit.Button("Repair AGEX", async () =>
                {
                    var actions = await Task.Run(() => Workspace.Core.Repair());
                    repairOutput.Children.Clear();
                    foreach (var action in actions)
                        repairOutput.Children.Add(Kit.Row(8, Kit.Badge(action.Status, action.Status switch { "OK" => Tone.Success, "FIXED" => Tone.Info, "FAILED" => Tone.Danger, _ => Tone.Neutral }), Kit.Text($"{action.Area}: {action.Detail}", "small")));
                    await Workspace.ScanAsync();
                }, "", Icons.Refresh, "Fixes AGEX's own files and settings. Never changes your agents."),
                Kit.Button("Scan again", () => _ = Workspace.ScanAsync(), "subtle", Icons.Search)),
            repairOutput,
            Kit.Panel(text));
    }
}
