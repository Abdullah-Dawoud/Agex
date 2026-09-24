using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Core.Settings;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Agex.Desktop.Pages;

/// <summary>Projects and their own settings: team, routing, file-change policy, trust, ignored folders, instructions and skills.</summary>
public sealed class ProjectsPage(MainWindow window) : AppPage(window)
{
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly ContentControl _detail = new();
    private string? _selected;

    public override string Id => "projects";
    public override string Title => "Projects";
    public override string Icon => Icons.Folder;

    protected override Control Build()
    {
        Workspace.ProjectChanged += Refresh;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*"), ColumnSpacing = 20 };
        grid.Children.Add(Kit.Column(12, Kit.Button("Add project folder...", () => _ = Window.PickProjectAsync(), "primary", Icons.Plus), _list));
        Grid.SetColumn(_detail, 1);
        grid.Children.Add(_detail);
        var page = Kit.Column(0, Kit.PageHeader("Projects", "Each project keeps its own agents, rules and instructions. Settings are stored on this computer, not in the project."), grid);
        var view = Kit.Page(page, 1200);
        view.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 820;
            grid.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("320,*");
            grid.RowDefinitions = narrow ? new RowDefinitions("Auto,Auto") : new RowDefinitions("*");
            Grid.SetColumn(_detail, narrow ? 0 : 1);
            Grid.SetRow(_detail, narrow ? 1 : 0);
            grid.RowSpacing = 20;
        };
        Refresh();
        return view;
    }

    public override void OnShown() => Refresh();

    private void Refresh()
    {
        _list.Children.Clear();
        var profiles = Workspace.Core.SettingsStore.AllProjects().ToList();
        if (Workspace.Project is { } current && profiles.All(profile => profile.Path != current.Path)) profiles.Insert(0, current);
        _selected ??= Workspace.Project?.Path ?? profiles.FirstOrDefault()?.Path;
        if (profiles.Count == 0)
        {
            _list.Children.Add(Kit.EmptyState(Icons.Folder, "No projects yet", "Add a folder to start."));
            _detail.Content = null;
            return;
        }
        foreach (var profile in profiles)
        {
            var exists = Directory.Exists(profile.Path);
            var isCurrent = Workspace.Project?.Path == profile.Path;
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 10),
                Content = Kit.Column(2,
                    Kit.Row(8, Kit.Text(profile.Name, "body"), isCurrent ? Kit.Badge("Open", Tone.Accent) : null, !exists ? Kit.Badge("Folder missing", Tone.Danger) : null, profile.Trusted ? Kit.Badge("Trusted", Tone.Success, Icons.Shield) : null),
                    Kit.Text(Agex.Core.Runtime.Redactor.RedactPaths(profile.Path), "caption").Trimmed(300)),
            };
            button.Classes.Add("nav");
            if (profile.Path == _selected) button.Classes.Add("active");
            var path = profile.Path;
            button.Click += (_, _) => { _selected = path; Refresh(); };
            AutomationProperties.SetName(button, profile.Name);
            _list.Children.Add(button);
        }
        var selected = profiles.FirstOrDefault(profile => profile.Path == _selected) ?? profiles[0];
        _detail.Content = Detail(selected);
    }

    private Control Detail(ProjectProfile profile)
    {
        var exists = Directory.Exists(profile.Path);
        void Save() { Workspace.SaveProject(profile); }
        var header = Kit.Column(4, Kit.Text(profile.Name, "title"), Kit.Text(Agex.Core.Runtime.Redactor.RedactPaths(profile.Path), "small"));
        var open = Kit.Wrap(
            Workspace.Project?.Path == profile.Path ? null : Kit.Button("Open project", () => Workspace.OpenProject(profile.Path), "primary", Icons.Folder),
            Kit.Button($"Show in {Workspace.Core.Platform.FileManagerName}", () => Workspace.Core.Platform.RevealInFileManager(profile.Path), "", Icons.External),
            Kit.Button("Open terminal here", () => { if (!Workspace.Core.Platform.OpenTerminal(profile.Path)) Window.Toast("No terminal found", "AGEX could not find a terminal app.", ToastKind.Error); }, "", Icons.Keyboard),
            OpenInIde(profile.Path),
            Kit.Button("Remove from list", async () =>
            {
                if (!await Window.ConfirmAsync("Remove this project from AGEX?", "Only AGEX's settings for it are removed. The folder and its files are not touched.", "Remove", "Cancel")) return;
                Workspace.Core.SettingsStore.RemoveProject(profile.Path);
                _selected = null;
                Refresh();
            }, "subtle danger", Icons.Trash));

        var teams = new List<(string, string)> { ("", "Use the global choice") };
        teams.AddRange(Workspace.Settings.Teams.Select(team => (team.Id, team.Name)));
        var routing = new List<(RoutingPreset?, string)> { (null, "Use the global choice") };
        routing.AddRange(Enum.GetValues<RoutingPreset>().Select(preset => ((RoutingPreset?)preset, Router.Title(preset))));
        var instructions = new TextBox { Text = profile.Instructions, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90, PlaceholderText = "For example: Use British English. Never touch the migrations folder." };
        AutomationProperties.SetName(instructions, "Project instructions");
        instructions.LostFocus += (_, _) => { profile.Instructions = instructions.Text ?? ""; Save(); };
        var ignored = new TextBox { Text = string.Join(", ", profile.IgnoredFolders), PlaceholderText = "e.g. data, fixtures/large" };
        AutomationProperties.SetName(ignored, "Ignored folders");
        ignored.LostFocus += (_, _) => { profile.IgnoredFolders = (ignored.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); Save(); };

        var settings = Kit.Column(4,
            Kit.SettingRow("Team", "Which agents work on this project.", Kit.Combo(teams, profile.Team, value => { profile.Team = value; Save(); })),
            Kit.SettingRow("How work is shared", "Overrides the routing preset for this project.", Kit.Combo(routing, profile.Routing, value => { profile.Routing = value; Save(); })),
            Kit.SettingRow("Let agents change files", "Off = agents can read and advise, but not edit.", Toggle(profile.AllowWrites ?? true, value => { profile.AllowWrites = value; Save(); })),
            Kit.SettingRow("Trusted project", "Skip the 'allow changes?' question for this project. A Git snapshot is still taken first.", Toggle(profile.Trusted, value => { profile.Trusted = value; Save(); })),
            Kit.SettingRow("Cloud notice shown", "AGEX told you which cloud services receive this project's data.", Toggle(profile.CloudUseAcknowledged, value => { profile.CloudUseAcknowledged = value; Save(); })),
            Kit.Divider(),
            Kit.Text("Instructions for every agent", "body"), instructions,
            Kit.Text("Folders agents and AGEX should ignore (dependency and build folders are already ignored)", "body"), ignored,
            Kit.Divider(),
            SkillChoices(profile));

        return Kit.Card(Kit.Column(16, header, !exists ? Kit.Badge("This folder no longer exists. Remove it or restore the folder.", Tone.Danger) : null, open, settings), 20);
    }

    private static ToggleSwitch Toggle(bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = "On", OffContent = "Off" };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return toggle;
    }

    private Control? OpenInIde(string path)
    {
        var ide = Workspace.Scan?.Ides.FirstOrDefault(item => item.Status == AgentStatus.DetectedUnsupported && item.Id is "vscode" or "cursor" or "zed" or "windsurf" && item.Location.Length > 0);
        if (ide is null) return null;
        return Kit.Button($"Open in {ide.Name}", () =>
        {
            try
            {
                var psi = ide.Location.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                    ? new System.Diagnostics.ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-a", ide.Location, path } }
                    : new System.Diagnostics.ProcessStartInfo(ide.Location) { ArgumentList = { path } };
                psi.UseShellExecute = false;
                System.Diagnostics.Process.Start(psi)?.Dispose();
            }
            catch (Exception ex) { Window.Toast("Could not open the editor", ex.Message, ToastKind.Error); }
        }, "", Icons.External);
    }

    private Control SkillChoices(ProjectProfile profile)
    {
        var installed = Workspace.Core.Skills.Installed();
        var column = Kit.Column(6, Kit.Text("Skills for this project", "body"));
        if (installed.Count == 0) { column.Children.Add(Kit.Text("No skills installed. Browse Skills to add some.", "small")); return column; }
        var all = new CheckBox { Content = "Use all enabled skills", IsChecked = profile.Skills is null };
        var boxes = installed.Select(skill => new CheckBox { Content = skill.Manifest.Name, Tag = skill.Id, IsChecked = profile.Skills?.Contains(skill.Id) ?? true, IsEnabled = profile.Skills is not null }).ToList();
        void Save()
        {
            profile.Skills = all.IsChecked == true ? null : boxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag!).ToList();
            foreach (var box in boxes) box.IsEnabled = all.IsChecked != true;
            Workspace.SaveProject(profile);
        }
        all.IsCheckedChanged += (_, _) => Save();
        foreach (var box in boxes) { box.IsCheckedChanged += (_, _) => Save(); column.Children.Add(box); }
        column.Children.Insert(1, all);
        return column;
    }
}
