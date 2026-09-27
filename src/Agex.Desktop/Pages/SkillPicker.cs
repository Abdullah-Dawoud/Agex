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
/// The composer's skill selector. AGEX suggests skills from the message; the
/// active Team only recommends. Every installed skill can be added, and a
/// suggested one can be removed for this request. Profiles, Team pins and
/// per-agent assignment stay under Advanced.
/// </summary>
public sealed class SkillPicker(MainWindow window)
{
    private Workspace Workspace => window.Workspace;

    public async Task ShowAsync(string draft = "")
    {
        var installed = Workspace.Core.Skills.Installed().Where(skill => skill.DisabledReason.Length == 0).OrderBy(skill => skill.Manifest.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var team = JobTeamCatalog.Get(Workspace.Settings.ActiveJobTeam);
        var pins = team is null ? new HashSet<string>() : new HashSet<string>(Workspace.Settings.TeamSkills.GetValueOrDefault(team.Id) ?? []);
        var suggested = Workspace.SuggestedSkills(draft).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, bool>(Workspace.SkillOverrides, StringComparer.OrdinalIgnoreCase);
        bool Selected(string id) => overrides.TryGetValue(id, out var add) ? add : suggested.Contains(id);
        var recommended = team?.Requirements.Where(item => item.Kind == RequirementKind.Skill).Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var search = new TextBox { PlaceholderText = "Search skills", MinWidth = 260 };
        AutomationProperties.SetName(search, "Search skills");
        var sections = Kit.Column(14);
        var rows = new List<(InstalledSkill Skill, Control Row)>();

        Control Row(InstalledSkill skill)
        {
            var id = skill.Id;
            var box = new CheckBox { Content = skill.Manifest.Name, IsChecked = Selected(id) };
            AutomationProperties.SetName(box, "Use " + skill.Manifest.Name);
            var origin = Kit.Text("", "caption");
            void Describe() => origin.Text = overrides.TryGetValue(id, out var add) ? add ? "Added by you" : "Removed by you" : suggested.Contains(id) ? "Suggested" : "";
            Describe();
            box.IsCheckedChanged += (_, _) =>
            {
                var on = box.IsChecked == true;
                // Differences from the suggestion are the user's choice; matching it again returns to Auto.
                if (on == suggested.Contains(id)) overrides.Remove(id); else overrides[id] = on;
                Describe();
            };
            ToolTip.SetTip(box, skill.Manifest.Description);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(box);
            origin.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(origin, 1);
            row.Children.Add(origin);
            rows.Add((skill, row));
            return row;
        }

        Control Section(string title, IEnumerable<InstalledSkill> skills, string? note = null)
        {
            var list = Kit.Column(2);
            foreach (var skill in skills) list.Children.Add(Row(skill));
            var header = Kit.Text(title, "caption");
            var noteText = note is null ? null : Kit.Text(note, "caption");
            if (noteText is not null) noteText.TextWrapping = TextWrapping.Wrap;
            return Kit.Column(4, header, noteText, list);
        }

        var suggestedSkills = installed.Where(skill => suggested.Contains(skill.Id)).ToList();
        sections.Children.Add(suggestedSkills.Count > 0
            ? Section("Suggested for this message", suggestedSkills)
            : Kit.Text(draft.Trim().Length == 0 ? "Type your message first: AGEX suggests skills that fit it. You can still add any skill below." : "No skill is needed for this message. Add one below if you want it.", "small"));
        if (team is not null)
        {
            var teamSkills = installed.Where(skill => recommended.Contains(skill.Id) && !suggested.Contains(skill.Id)).ToList();
            var missing = team.Requirements.Where(item => item.Kind == RequirementKind.Skill && installed.All(skill => skill.Id != item.Id)).ToList();
            var teamSection = Kit.Column(4, Section($"Recommended for {team.Name}", teamSkills, "Recommendations only: add them when the task needs them."));
            if (missing.Count > 0)
                teamSection.Children.Add(Kit.Button($"Install {missing.Count} recommended skill{(missing.Count == 1 ? "" : "s")}...", () => { window.Dialogs.Close(-1); window.Navigate("teams"); window.Page<TeamsPage>("teams").OpenSetup(team.Id); }, "link"));
            if (teamSkills.Count > 0 || missing.Count > 0) sections.Children.Add(teamSection);
        }
        var others = installed.Where(skill => !suggested.Contains(skill.Id) && !(team is not null && recommended.Contains(skill.Id))).ToList();
        if (others.Count > 0) sections.Children.Add(Section("All skills", others));
        else if (installed.Count == 0) sections.Children.Add(Kit.Text("No skills installed yet. Many are free on the Skills page.", "small"));
        sections.Children.Add(Kit.Button("Browse more skills...", () => { window.Dialogs.Close(-1); window.Navigate("skills"); }, "link", Icons.Skills));

        search.TextChanged += (_, _) =>
        {
            var text = (search.Text ?? "").Trim();
            foreach (var (skill, row) in rows)
                row.IsVisible = text.Length == 0 || skill.Manifest.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || skill.Manifest.Description.Contains(text, StringComparison.OrdinalIgnoreCase);
        };

        // Advanced: pins to the Team (candidates for Auto), profiles, and which agent gets which skill.
        var pinList = Kit.Column(2);
        var pinBoxes = new List<(string Id, CheckBox Box)>();
        if (team is not null)
            foreach (var skill in installed)
            {
                var pin = new CheckBox { Content = skill.Manifest.Name, IsChecked = pins.Contains(skill.Id) };
                AutomationProperties.SetName(pin, $"Pin {skill.Manifest.Name} to {team.Name}");
                pinBoxes.Add((skill.Id, pin));
                pinList.Children.Add(pin);
            }
        var profiles = SkillProfiles.All(Workspace.Settings.SkillProfiles);
        var profileItems = new List<(string, string)> { ("", "Apply a profile...") };
        profileItems.AddRange(profiles.Select(profile => (profile.Name, profile.Name + (profile.BuiltIn ? "" : " (yours)"))));
        var profileCombo = Kit.Combo(profileItems, "", name =>
        {
            if (profiles.FirstOrDefault(profile => profile.Name == name) is not { } profile) return;
            foreach (var (skill, row) in rows)
                if (row is Grid grid && grid.Children[0] is CheckBox box) box.IsChecked = profile.SkillIds.Contains(skill.Id);
        }, 220);
        AutomationProperties.SetName(profileCombo, "Skill profile");
        var advanced = new Expander
        {
            Header = "Advanced",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Kit.Column(12,
                profileCombo,
                team is null ? null : Kit.Column(4, Kit.Text($"Pinned to {team.Name}: AGEX considers these for every {team.Name} request and uses them when they fit the task.", "caption"), new ScrollViewer { Content = pinList, MaxHeight = 160 }),
                Kit.Text("Which agent gets which skill", "caption"),
                AgentAssignment(installed)),
        };
        var body = Kit.Column(12,
            search,
            new ScrollViewer { Content = sections, MaxHeight = 320 },
            advanced);
        var result = await window.Dialogs.ShowAsync("Skills for this message", body, ["Done", "Use Auto", "Cancel"], maxWidth: 640);
        if (result == 2 || result < 0) return;
        if (team is not null)
        {
            Workspace.Settings.TeamSkills[team.Id] = pinBoxes.Where(item => item.Box.IsChecked == true).Select(item => item.Id).ToList();
            Workspace.SaveSettings();
        }
        Workspace.ReplaceSkillOverrides(result == 0 ? overrides : new Dictionary<string, bool>());
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
        var note = Kit.Text("Leave a skill unticked everywhere to give it to every agent that can use it. Tick it for some agents to give it only to them.", "caption");
        note.TextWrapping = TextWrapping.Wrap;
        return Kit.Column(8, note, new ScrollViewer { Content = grid, MaxHeight = 200 });
    }
}
