using Agex.Core.Skills;
using Agex.Core.Teams;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>
/// The composer's skill selector: Auto, or the skills for this request only,
/// with profiles ("Deep research"), pins to the team in use, and (advanced)
/// which agent gets which skill. Normal use never needs Settings.
/// </summary>
public sealed class SkillPicker(MainWindow window)
{
    private Workspace Workspace => window.Workspace;

    public async Task ShowAsync()
    {
        var installed = Workspace.Core.Skills.Installed().Where(skill => skill.DisabledReason.Length == 0).OrderBy(skill => skill.Manifest.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var team = JobTeamCatalog.Get(Workspace.Settings.ActiveJobTeam);
        var pins = team is null ? [] : new HashSet<string>(Workspace.Settings.TeamSkills.GetValueOrDefault(team.Id) ?? []);
        var auto = Workspace.RequestSkills is null;
        var chosen = new HashSet<string>(Workspace.RequestSkills ?? Workspace.EffectiveSkills().Select(skill => skill.Id));

        var autoButton = new RadioButton { GroupName = "skill-mode", Content = "Auto: your switched-on skills" + (team is null ? "" : $" plus the ones pinned to {team.Name}"), IsChecked = auto };
        var chooseButton = new RadioButton { GroupName = "skill-mode", Content = "Choose for this request", IsChecked = !auto };
        var list = Kit.Column(4);
        var boxes = new List<(InstalledSkill Skill, CheckBox Box, CheckBox? Pin)>();
        foreach (var skill in installed)
        {
            var box = new CheckBox { Content = skill.Manifest.Name, IsChecked = chosen.Contains(skill.Id), Tag = skill.Id };
            AutomationProperties.SetName(box, "Use " + skill.Manifest.Name);
            box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) chooseButton.IsChecked = true; };
            var pin = team is null ? null : new CheckBox { Content = "Pin to team", IsChecked = pins.Contains(skill.Id), Tag = skill.Id };
            if (pin is not null) { AutomationProperties.SetName(pin, $"Pin {skill.Manifest.Name} to {team!.Name}"); ToolTip.SetTip(pin, $"Always use it with {team!.Name}"); }
            var hint = Kit.Text((skill.Enabled ? "" : "Off for Auto · ") + SkillCosts.Label(SkillCosts.Of(skill.Manifest)) + (skill.Manifest.Kind == SkillKind.Mcp ? " · tool" : ""), "caption");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10 };
            row.Children.Add(box);
            Grid.SetColumn(hint, 1);
            hint.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(hint);
            if (pin is not null) { Grid.SetColumn(pin, 2); row.Children.Add(pin); }
            list.Children.Add(row);
            boxes.Add((skill, box, pin));
        }
        if (installed.Count == 0) list.Children.Add(Kit.Text("No skills installed yet. Open Skills to add some; many are free.", "small"));

        // Profiles: pick one to tick its installed skills.
        var profiles = SkillProfiles.All(Workspace.Settings.SkillProfiles);
        var profileItems = new List<(string, string)> { ("", "Apply a profile...") };
        profileItems.AddRange(profiles.Select(profile => (profile.Name, profile.Name + (profile.BuiltIn ? "" : " (yours)"))));
        var missingNote = Kit.Text("", "caption");
        missingNote.TextWrapping = TextWrapping.Wrap;
        var profileCombo = Kit.Combo(profileItems, "", name =>
        {
            if (profiles.FirstOrDefault(profile => profile.Name == name) is not { } profile) return;
            chooseButton.IsChecked = true;
            foreach (var (skill, box, _) in boxes) box.IsChecked = profile.SkillIds.Contains(skill.Id);
            var missing = profile.SkillIds.Where(id => installed.All(skill => skill.Id != id)).Select(id => Workspace.Core.Skills.Catalog().Skills.FirstOrDefault(skill => skill.Id == id)?.Name ?? id).ToList();
            missingNote.Text = missing.Count == 0 ? "" : "Not installed, so not used: " + string.Join(", ", missing) + ". Install them on the Skills page.";
        }, 220);
        AutomationProperties.SetName(profileCombo, "Skill profile");
        var profileName = new TextBox { PlaceholderText = "Name for this selection", MinWidth = 200 };
        AutomationProperties.SetName(profileName, "Profile name");
        var saveProfile = Kit.Button("Save as profile", () =>
        {
            var name = (profileName.Text ?? "").Trim();
            if (name.Length == 0) { missingNote.Text = "Type a name for the profile first."; return; }
            Workspace.Settings.SkillProfiles.RemoveAll(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Workspace.Settings.SkillProfiles.Add(new SkillProfile { Name = name, SkillIds = boxes.Where(item => item.Box.IsChecked == true).Select(item => item.Skill.Id).ToList() });
            Workspace.SaveSettings();
            missingNote.Text = $"Saved \"{name}\". Pick it from the list next time.";
        }, "subtle", Icons.Check);

        var advanced = new Expander { Header = "Advanced: which agent gets which skill", Content = AgentAssignment(installed), HorizontalAlignment = HorizontalAlignment.Stretch };
        var intro = Kit.Text("Skills add know-how or tools to the agents for a request. Auto is right for most requests.", "small");
        intro.TextWrapping = TextWrapping.Wrap;
        var body = Kit.Column(10,
            intro, autoButton, chooseButton,
            Kit.Wrap(profileCombo, profileName, saveProfile), missingNote,
            new ScrollViewer { Content = list, MaxHeight = 280 },
            advanced);
        if (await window.Dialogs.ShowAsync("Skills for this request", body, ["Done", "Cancel"], maxWidth: 680) != 0) return;

        if (team is not null)
        {
            Workspace.Settings.TeamSkills[team.Id] = boxes.Where(item => item.Pin?.IsChecked == true).Select(item => item.Skill.Id).ToList();
            Workspace.SaveSettings();
        }
        Workspace.SetRequestSkills(chooseButton.IsChecked == true ? boxes.Where(item => item.Box.IsChecked == true).Select(item => item.Skill.Id).ToList() : null);
    }

    /// <summary>Per-agent assignment: a skill ticked for some agents goes only to them; untouched skills go to every compatible agent.</summary>
    private Control AgentAssignment(IReadOnlyList<InstalledSkill> installed)
    {
        var agents = Workspace.Settings.EnabledAgents.Select(id => Workspace.Core.Registry.Get(id)).OfType<Agex.Core.Agents.IAgentAdapter>()
            .Where(adapter => adapter.Capabilities.Contains(Agex.Core.Agents.Capability.Skills) || adapter.Capabilities.Contains(Agex.Core.Agents.Capability.Mcp)).ToList();
        if (agents.Count == 0 || installed.Count == 0) return Kit.Text("Turn on agents that support skills (Codex, Antigravity, Claude Code, Gemini CLI, OpenCode) to assign skills to them.", "small");
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        foreach (var _ in agents) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(Kit.Text("Skill", "caption"));
        for (var column = 0; column < agents.Count; column++)
        {
            var header = Kit.Text(agents[column].Name, "caption");
            Grid.SetColumn(header, column + 1);
            grid.Children.Add(header);
        }
        var map = Workspace.Settings.AgentSkills;
        for (var row = 0; row < installed.Count; row++)
        {
            var skill = installed[row];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = Kit.Text(skill.Manifest.Name, "small");
            Grid.SetRow(name, row + 1);
            grid.Children.Add(name);
            for (var column = 0; column < agents.Count; column++)
            {
                var agentId = agents[column].Id;
                var box = new CheckBox { IsChecked = map.GetValueOrDefault(agentId)?.Contains(skill.Id) == true, HorizontalAlignment = HorizontalAlignment.Center };
                AutomationProperties.SetName(box, $"{skill.Manifest.Name} for {agents[column].Name}");
                box.IsCheckedChanged += (_, _) =>
                {
                    var list = map.TryGetValue(agentId, out var existing) ? existing : map[agentId] = [];
                    list.Remove(skill.Id);
                    if (box.IsChecked == true) list.Add(skill.Id);
                    if (list.Count == 0) map.Remove(agentId);
                    Workspace.SaveSettings();
                };
                Grid.SetRow(box, row + 1);
                Grid.SetColumn(box, column + 1);
                grid.Children.Add(box);
            }
        }
        var note = Kit.Text("Leave a skill unticked everywhere to let AGEX give it to every agent that can use it. Tick it for some agents to give it only to them.", "caption");
        note.TextWrapping = TextWrapping.Wrap;
        return Kit.Column(8, note, grid);
    }
}
