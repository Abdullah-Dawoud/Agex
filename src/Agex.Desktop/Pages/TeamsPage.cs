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
/// Teams are built around jobs: what the team accomplishes, what it needs and
/// how ready this computer is. Agents are chosen automatically from the ones
/// that are enabled. Setup shows every missing piece with the next step.
/// </summary>
public sealed class TeamsPage(MainWindow window) : AppPage(window)
{
    private readonly ContentControl _body = new();
    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
    private JobTeam? _open;
    private bool _busy;

    public override string Id => "teams";
    public override string Title => "Teams";
    public override string Icon => Icons.Team;

    protected override Control Build()
    {
        Workspace.SettingsChanged += Refresh;
        Workspace.ScanChanged += Refresh;
        Refresh();
        return Kit.Page(_body, 1200);
    }

    public override void OnShown() => Refresh();

    /// <summary>Opens a team's setup directly (used by search and the Home page).</summary>
    public void OpenSetup(string teamId)
    {
        _open = JobTeamCatalog.Get(teamId);
        Refresh();
    }

    private void Refresh()
    {
        if (_open is not null) { _body.Content = BuildSetup(_open); return; }
        // A new panel each time: a control can have only one parent.
        var cards = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var team in JobTeamCatalog.All) cards.Children.Add(TeamCard(team));
        var active = JobTeamCatalog.Get(Workspace.Settings.ActiveJobTeam);
        _body.Content = Kit.Column(16,
            Kit.PageHeader("Teams", "Choose the kind of work. AGEX picks the best enabled agents for each step; you only set up the tools the job needs."),
            Kit.Card(Kit.Row(10, Kit.Icon(Icons.Team, 20, "AccentBrush"),
                Kit.Column(2, Kit.Text(active is null ? "No team selected: General work" : $"In use: {active.Name}", "subtitle"),
                    Kit.Text(active is null ? "Requests use your agents without a team brief. Pick a team below for its rules, approvals and tools." : JobTeamCatalog.ApprovalText(active.Approval), "small")),
                active is null ? null : Kit.Button("Stop using", () => UseTeam(null), "subtle"))),
            cards);
    }

    private (IReadOnlyList<RequirementStatus> Statuses, int Ready, int Total, int RequiredMissing) Status(JobTeam team)
    {
        var statuses = Workspace.Core.Teams.Check(team);
        var (ready, total, missing) = JobTeamService.Progress(statuses);
        return (statuses, ready, total, missing);
    }

    private Control TeamCard(JobTeam team)
    {
        var (_, ready, total, missing) = Status(team);
        var inUse = Workspace.Settings.ActiveJobTeam == team.Id;
        var progress = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, total), Value = ready, Height = 6, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(progress, $"{ready} of {total} tools ready");
        var body = Kit.Column(8,
            Kit.Row(8, Kit.Text(team.Name, "subtitle"), inUse ? Kit.Badge("In use", Tone.Accent) : null),
            Kit.Text(team.Summary, "small"),
            Kit.Text("Typical: " + string.Join(" · ", team.TypicalTasks.Take(3)), "caption"),
            Kit.Row(8, Kit.Text($"{ready}/{total} tools ready", "caption"), missing == 0 ? Kit.Badge("Ready", Tone.Success) : Kit.Badge($"{missing} required missing", Tone.Warning)),
            progress,
            Kit.Row(8,
                Kit.Button(missing == 0 && ready == total ? "Details" : "Set up", () => OpenSetup(team.Id), missing == 0 ? "" : "primary", Icons.Tool),
                inUse ? null : Kit.Button("Use this team", () => UseTeam(team), missing == 0 ? "primary" : "", Icons.Check)));
        foreach (var child in body.Children.OfType<TextBlock>()) child.TextWrapping = TextWrapping.Wrap;
        var card = Kit.Card(body, 14);
        card.Width = 340;
        card.Margin = new Thickness(0, 0, 12, 12);
        return card;
    }

    private void UseTeam(JobTeam? team)
    {
        Workspace.Settings.ActiveJobTeam = team?.Id ?? "";
        Workspace.SaveSettings();
        if (team is not null) Window.Toast($"{team.Name} is in use", "New requests follow this team's rules. Change it any time on Home.", ToastKind.Success);
        Refresh();
    }

    // ----------------------------------------------------------------- setup

    private Control BuildSetup(JobTeam team)
    {
        var (statuses, ready, total, missing) = Status(team);
        var inUse = Workspace.Settings.ActiveJobTeam == team.Id;
        var autoInstall = AutoInstallable(statuses);
        var header = Kit.PageHeader(team.Name, team.Summary, Kit.Row(8,
            Kit.Button("All teams", () => { _open = null; Refresh(); }, "subtle", Icons.Undo),
            inUse ? Kit.Badge("In use", Tone.Accent) : Kit.Button("Use this team", () => UseTeam(team), "primary", Icons.Check)));

        var progress = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, total), Value = ready, Height = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        var summary = Kit.Card(Kit.Column(10,
            Kit.Row(10, Kit.Text($"{ready}/{total} tools ready", "subtitle"), missing == 0 ? Kit.Badge("Everything required is ready", Tone.Success) : Kit.Badge($"{missing} required item{(missing == 1 ? "" : "s")} missing", Tone.Warning)),
            progress,
            Kit.Wrap(
                autoInstall.Count > 0 ? Kit.Button(_busy ? "Setting up..." : $"Set up recommended ({autoInstall.Count} free skill{(autoInstall.Count == 1 ? "" : "s")})", () => _ = SetUpEverythingAsync(team, autoInstall), "primary", Icons.Download, "Installs the free skills that need no account, after you confirm. Programs and accounts stay your choice.") : null,
                Kit.Button("Check again", Refresh, "", Icons.Refresh)),
            Kit.Text("Set up recommended installs only free skills that need no account, after you confirm the list. Programs, accounts and connections keep their own buttons below.", "caption")));

        var checklist = Kit.Column(8);
        // Ready first, then what still needs setting up, then optional extras (the setup reads like a wizard).
        var readyItems = statuses.Where(item => item.State == RequirementState.Ready).ToList();
        var todo = statuses.Where(item => item.State != RequirementState.Ready && item.Requirement.Level != RequirementLevel.Optional).ToList();
        var optional = statuses.Where(item => item.State != RequirementState.Ready && item.Requirement.Level == RequirementLevel.Optional).ToList();
        foreach (var (title, items) in new[] { ($"READY ({readyItems.Count})", readyItems), ($"TO SET UP ({todo.Count})", todo), ($"OPTIONAL ({optional.Count})", optional) })
        {
            if (items.Count == 0) continue;
            checklist.Children.Add(Kit.Text(title, "caption"));
            foreach (var status in items) checklist.Children.Add(RequirementRow(team, status));
        }
        // Programs and services the team works with, with their connection state.
        var ui = new ConnectionUi(Window);
        var connections = Agex.Core.Connections.ConnectionService.ForTeam(Workspace.Connections(), team.Id);
        if (connections.Count > 0)
        {
            checklist.Children.Add(Kit.Text("PROGRAMS AND SERVICES", "caption"));
            foreach (var item in connections) checklist.Children.Add(Kit.Panel(new Border { Padding = new Thickness(12, 8), Child = ui.Row(item) }));
        }

        var about = Kit.Card(Kit.Column(8,
            Kit.Text("What this team does", "subtitle"),
            Kit.Text("Typical tasks", "caption"), Bullets(team.TypicalTasks),
            Kit.Text("Outputs", "caption"), Bullets(team.Outputs),
            Kit.Text("Files it works with", "caption"), Bullets(team.InputFiles),
            Kit.Text("Always asks you before", "caption"), Bullets(team.SensitiveActions),
            team.PremiumAlternatives.Count > 0 ? Kit.Text("Paid tools it works alongside", "caption") : null,
            team.PremiumAlternatives.Count > 0 ? Bullets(team.PremiumAlternatives) : null,
            Kit.Text("Permissions", "caption"), Kit.Text(JobTeamCatalog.ApprovalText(team.Approval), "small"),
            team.Approval is ApprovalLevel.ComputerControl or ApprovalLevel.ExternalCommunication or ApprovalLevel.Sensitive or ApprovalLevel.BrowserActions
                ? Kit.Badge("Submitting, sending, paying, uploading or deleting always needs your confirmation", Tone.Info, Icons.Shield) : null,
            team.Efficiency == EfficiencyHint.LocalFirst ? Kit.Badge("Local only: nothing leaves this computer", Tone.Success, Icons.Computer) : null,
            Kit.Text("Agents", "caption"),
            Kit.Text(team.Efficiency == EfficiencyHint.LocalFirst ? "Uses local agents (Ollama) only." : "AGEX picks from your enabled agents; the leader plans and the others do the tasks that fit them.", "small")), 16);
        foreach (var text in ((StackPanel)about.Child!).Children.OfType<TextBlock>()) text.TextWrapping = TextWrapping.Wrap;

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        columns.Children.Add(Kit.Card(Kit.Column(10, Kit.Text("Setup checklist", "subtitle"), checklist), 16));
        Grid.SetColumn(about, 1);
        columns.Children.Add(about);
        var content = Kit.Column(16, header, summary, columns);
        content.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 820;
            columns.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("3*,2*");
            columns.RowDefinitions = narrow ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
            columns.RowSpacing = 16;
            Grid.SetColumn(about, narrow ? 0 : 1);
            Grid.SetRow(about, narrow ? 1 : 0);
        };
        return content;
    }

    private static Control Bullets(IEnumerable<string> items) => Kit.Column(2, items.Select(item => (Control?)Kit.Text("• " + item, "small")).ToArray());

    private Control RequirementRow(JobTeam team, RequirementStatus status)
    {
        var requirement = status.Requirement;
        var skipped = _skipped.Contains(team.Id + "/" + requirement.Id);
        var (text, tone) = status.State switch
        {
            RequirementState.Ready => ("Ready", Tone.Success),
            RequirementState.NotOnThisSystem => ("Not for this system", Tone.Neutral),
            _ when skipped => ("Skipped", Tone.Neutral),
            RequirementState.NeedsAccount => ("Needs account", Tone.Warning),
            RequirementState.NeedsDependency => ("Needs a program", Tone.Warning),
            RequirementState.NotInstalled => ("Not installed", requirement.Level == RequirementLevel.Required ? Tone.Warning : Tone.Neutral),
            _ => ("Not ready", requirement.Level == RequirementLevel.Required ? Tone.Warning : Tone.Neutral),
        };
        var kind = requirement.Kind switch { RequirementKind.Skill => "Skill", RequirementKind.Program => "Program", RequirementKind.Integration => "Connection", RequirementKind.LocalModel => "Local model", _ => "Agent" };
        var info = Kit.Column(2,
            Kit.Row(8, Kit.Text(requirement.Label, "body"), Kit.Badge(text, tone)),
            Kit.Text($"{kind} · {requirement.Why}", "small"),
            status.Detail.Length > 0 && status.State != RequirementState.Ready ? Kit.Text(status.Detail, "caption") : null,
            requirement.FreeAlternative.Length > 0 && status.State != RequirementState.Ready
                ? Kit.Badge(requirement.FreeAlternative.StartsWith("Free", StringComparison.Ordinal) ? requirement.FreeAlternative : "Free option: " + requirement.FreeAlternative, Tone.Success) : null,
            requirement.PaidOption.Length > 0 && status.State != RequirementState.Ready ? Kit.Text("Advanced option: " + requirement.PaidOption, "caption") : null);
        foreach (var block in info.Children.OfType<TextBlock>()) block.TextWrapping = TextWrapping.Wrap;
        var actions = status.State is RequirementState.Ready or RequirementState.NotOnThisSystem || skipped ? Kit.Row(6) : Actions(team, status);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        row.Children.Add(Kit.Row(10, Kit.Icon(status.State == RequirementState.Ready ? Icons.Check : Icons.Dot, 16, status.State == RequirementState.Ready ? "SuccessBrush" : "Text3Brush"), info));
        Grid.SetColumn(actions, 1);
        actions.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(actions);
        return Kit.Panel(new Border { Padding = new Thickness(12, 10), Child = row });
    }

    private StackPanel Actions(JobTeam team, RequirementStatus status)
    {
        var requirement = status.Requirement;
        var buttons = new List<Control>();
        switch (requirement.Kind, status.State)
        {
            case (RequirementKind.Skill, RequirementState.NotInstalled):
                buttons.Add(Kit.Button("Install", () => _ = RunAsync(() => Window.Page<SkillsPage>("skills").InstallByIdAsync(requirement.Id)), "primary", Icons.Download));
                break;
            case (RequirementKind.Skill, RequirementState.NeedsAccount):
                buttons.Add(Kit.Button("Connect", () => _ = RunAsync(() => Window.Page<SkillsPage>("skills").ShowByIdAsync(requirement.Id)), "primary", Icons.Lock));
                break;
            case (RequirementKind.Skill, RequirementState.NeedsDependency):
                if (status.ActionUrl is { } dependency) buttons.Add(Kit.Button("Get the program", () => OpenUrl(dependency), "", Icons.External));
                buttons.Add(Kit.Button("Details", () => _ = RunAsync(() => Window.Page<SkillsPage>("skills").ShowByIdAsync(requirement.Id)), "subtle"));
                break;
            case (RequirementKind.Skill, _):
                buttons.Add(Kit.Button("Details", () => _ = RunAsync(() => Window.Page<SkillsPage>("skills").ShowByIdAsync(requirement.Id)), "subtle"));
                break;
            case (RequirementKind.Program, _):
                if (status.ActionUrl is { } download) buttons.Add(Kit.Button("Official download", () => OpenUrl(download), "", Icons.External, "Opens the maker's own page in your browser. AGEX does not install programs."));
                break;
            case (RequirementKind.Integration, RequirementState.Missing) when requirement.Id == AutodeskBridge.SkillId:
                buttons.Add(Kit.Button("Connect Revit/AutoCAD", () => _ = ConnectBridgeAsync(), "primary", Icons.Tool));
                break;
            case (RequirementKind.Integration, _) when requirement.Id == AutodeskBridge.SkillId:
                buttons.Add(Kit.Button("Learn how to connect", () => _ = BridgeHelpAsync(), "", Icons.Question));
                break;
            case (RequirementKind.LocalModel, _):
                buttons.Add(status.Detail.Contains("turn it on", StringComparison.OrdinalIgnoreCase)
                    ? Kit.Button("Open Agents", () => Window.Navigate("agents"), "primary", Icons.Agent)
                    : Kit.Button("Get Ollama", () => OpenUrl(status.ActionUrl ?? "https://ollama.com/download"), "", Icons.External));
                break;
            case (RequirementKind.EditingAgent, _):
                buttons.Add(Kit.Button("Open Agents", () => Window.Navigate("agents"), "primary", Icons.Agent));
                break;
        }
        if (requirement.FreeAlternative.Length > 0 && !requirement.FreeAlternative.StartsWith("Free", StringComparison.Ordinal))
            buttons.Add(Kit.Button("Use free alternative", () => _ = UseFreeAlternativeAsync(team, requirement), "subtle", tooltip: requirement.FreeAlternative));
        if (requirement.Level != RequirementLevel.Required)
            buttons.Add(Kit.Button("Skip", () => { _skipped.Add(team.Id + "/" + requirement.Id); Refresh(); }, "subtle", tooltip: "Hide this suggestion; the team works without it"));
        var panel = Kit.Column(6, buttons.ToArray());
        panel.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var button in buttons) button.HorizontalAlignment = HorizontalAlignment.Stretch;
        return panel;
    }

    /// <summary>The free option for a requirement: opens or installs it when AGEX can, and marks the requirement handled.</summary>
    private async Task UseFreeAlternativeAsync(JobTeam team, TeamRequirement requirement)
    {
        var text = requirement.FreeAlternative;
        if (text.Contains("LibreOffice", StringComparison.OrdinalIgnoreCase)) OpenUrl(ProgramDetector.Describe("libreoffice").Url);
        else if (text.Contains("OpenCode", StringComparison.OrdinalIgnoreCase)) Window.Navigate("agents");
        else if (Workspace.Core.Skills.Catalog().Skills.FirstOrDefault(skill => text.Contains(skill.Name, StringComparison.OrdinalIgnoreCase)) is { } skill)
            await Window.Page<SkillsPage>("skills").InstallByIdAsync(skill.Id);
        else { await Window.Dialogs.MessageAsync("Free alternative", text); }
        _skipped.Add(team.Id + "/" + requirement.Id);
        Refresh();
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        finally { Refresh(); }
    }

    private void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) Workspace.Core.Platform.OpenUrl(uri);
    }

    /// <summary>Free catalog skills that need no account and can be installed on this system.</summary>
    private List<SkillManifest> AutoInstallable(IReadOnlyList<RequirementStatus> statuses)
    {
        var catalog = Workspace.Core.Skills.Catalog();
        return statuses
            .Where(status => status.Requirement.Kind == RequirementKind.Skill && status.State == RequirementState.NotInstalled && !_skipped.Contains((_open?.Id ?? "") + "/" + status.Requirement.Id))
            .Select(status => catalog.Skills.FirstOrDefault(skill => skill.Id == status.Requirement.Id))
            .OfType<SkillManifest>()
            .Where(skill => !skill.RequiresAccount && Workspace.Core.Skills.CheckCompatibility(skill, Workspace.Settings.EnabledAgents).Installable)
            .ToList();
    }

    private async Task SetUpEverythingAsync(JobTeam team, List<SkillManifest> skills)
    {
        if (_busy) return;
        var list = Kit.Column(6, skills.Select(skill => (Control?)Kit.Text($"• {skill.Name} · {SkillText.Trust(skill.Trust)} · {(skill.Permissions.Count == 0 ? "no permissions" : string.Join(", ", skill.Permissions.Select(SkillText.Permission)))}", "small")).ToArray());
        foreach (var text in list.Children.OfType<TextBlock>()) text.TextWrapping = TextWrapping.Wrap;
        var body = Kit.Column(10,
            Kit.Text($"AGEX will install {skills.Count} free skill{(skills.Count == 1 ? "" : "s")} for {team.Name}. None needs an account. Community skills start with 'Ask each time' for risky permissions.", "body"),
            list,
            Kit.Text("Programs, accounts and the Revit/AutoCAD connection are not touched; they keep their own buttons.", "caption"));
        foreach (var text in body.Children.OfType<TextBlock>()) text.TextWrapping = TextWrapping.Wrap;
        if (await Window.Dialogs.ShowAsync($"Set up {team.Name}?", body, ["Install", "Cancel"]) != 0) return;
        _busy = true;
        Refresh();
        var failures = new List<string>();
        foreach (var skill in skills)
        {
            try { await Workspace.Core.Skills.InstallAsync(skill, null, null, CancellationToken.None); }
            catch (Exception ex) when (ex is SkillException or IOException or UnauthorizedAccessException or HttpRequestException) { failures.Add($"{skill.Name}: {ex.Message}"); }
        }
        _busy = false;
        if (failures.Count > 0) await Window.Dialogs.MessageAsync("Some skills were not installed", string.Join("\n", failures));
        else Window.Toast($"{team.Name} set up", $"{skills.Count} skill{(skills.Count == 1 ? "" : "s")} installed. Anything left needs your choice (a program or an account).", ToastKind.Success);
        Refresh();
    }

    public async Task ConnectBridgeAsync()
    {
        var body = Kit.Column(8,
            Kit.Text("AGEX will add the Autodesk AI Bridge as a local tool for agents that support MCP (Codex, Claude Code). It runs on this computer and talks to the Revit and AutoCAD plug-ins you installed with the bridge.", "body"),
            Kit.Text("Agents can then read models and propose edits. Every use asks you first ('Ask each time'); you can change this under Skills > Installed.", "small"),
            Kit.Text("Host: " + Agex.Core.Runtime.Redactor.RedactPaths(AutodeskBridge.HostPath() ?? ""), "caption"));
        foreach (var text in body.Children.OfType<TextBlock>()) text.TextWrapping = TextWrapping.Wrap;
        if (await Window.Dialogs.ShowAsync("Connect Revit and AutoCAD?", body, ["Connect", "Cancel"]) != 0) return;
        try
        {
            var skill = Workspace.Core.Teams.ConnectAutodeskBridge();
            if (skill is null) await Window.Dialogs.MessageAsync("Not connected", "The Autodesk AI Bridge Host was not found. Install the bridge first.");
            else Window.Toast("Revit/AutoCAD connected", "Open Revit or AutoCAD with the bridge plug-in loaded before asking the team to use them.", ToastKind.Success);
        }
        catch (SkillException ex) { await Window.Dialogs.MessageAsync("Not connected", ex.Message); }
        Refresh();
    }

    public Task BridgeHelpAsync()
    {
        var body = Kit.Column(8,
            Kit.Text("AGEX does not control Revit or AutoCAD by itself. It connects through the Autodesk AI Bridge, a separate local program with plug-ins for Revit and AutoCAD.", "body"),
            Kit.Text("1. Install Revit and/or AutoCAD (Windows).", "small"),
            Kit.Text("2. Install the Autodesk AI Bridge: its installer puts the Host in %LOCALAPPDATA%\\AutodeskAIBridge\\Host and adds the plug-ins to Revit and AutoCAD. The bridge has no public release yet; today it is built from its source.", "small"),
            Kit.Text("3. Come back here and press Connect Revit/AutoCAD.", "small"),
            Kit.Text("Without the bridge, this team still works with drawings and models you export as PDF, images, CSV or IFC files.", "caption"));
        foreach (var text in body.Children.OfType<TextBlock>()) text.TextWrapping = TextWrapping.Wrap;
        return Window.Dialogs.ShowAsync("Connect Revit and AutoCAD", body, ["Close"]);
    }
}
