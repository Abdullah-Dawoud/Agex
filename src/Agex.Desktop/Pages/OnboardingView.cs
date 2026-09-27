using Agex.Core.Agents;
using Agex.Core.Skills;
using Agex.Core.Teams;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>
/// First-run setup: welcome, scan, agents found, choose agents, optional
/// skills, choose a project, ready. Every step can be skipped and changed later.
/// </summary>
public sealed class OnboardingView : UserControl
{
    private readonly MainWindow _window;
    private readonly Action _done;
    private readonly ContentControl _body = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly HashSet<string> _chosenAgents = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _chosenSkills = [];
    private readonly HashSet<string> _chosenWork = [];
    private int _step;
    private const int Steps = 4;

    private Workspace Workspace => _window.Workspace;

    public OnboardingView(MainWindow window, Action done)
    {
        _window = window;
        _done = done;
        var skip = Kit.Button("Skip setup", Finish, "link");
        skip.HorizontalAlignment = HorizontalAlignment.Right;
        var frame = new Border
        {
            MaxWidth = 720, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center,
            Child = Kit.Column(20, skip, _dots, Kit.Card(_body, 32)),
        };
        Content = new ScrollViewer { Content = frame };
        Workspace.ScanChanged += () => { if (_step == 2) Show(); };
        Show();
    }

    private void Go(int step) { _step = Math.Clamp(step, 0, Steps - 1); Show(); }

    private void Show()
    {
        _dots.Children.Clear();
        for (var index = 0; index < Steps; index++)
        {
            var dot = new Border { Width = index == _step ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4) };
            dot.Res(Border.BackgroundProperty, index <= _step ? "AccentBrush" : "BorderStrongBrush");
            _dots.Children.Add(dot);
        }
        AutomationProperties.SetName(_dots, $"Step {_step + 1} of {Steps}");
        // Four short steps. Skills, connections and a project are optional and come later, when a task needs them.
        _body.Content = _step switch
        {
            0 => Welcome(),
            1 => ChooseWork(),
            2 => ChooseAgents(),
            _ => Ready(),
        };
    }

    private Control Nav(string next, Action? onNext = null, bool canGoBack = true, bool enabled = true)
    {
        var nextButton = Kit.Button(next, onNext ?? (() => Go(_step + 1)), "primary");
        nextButton.IsEnabled = enabled;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        if (canGoBack) row.Children.Add(Kit.Button("Back", () => Go(_step - 1), "subtle"));
        Grid.SetColumn(nextButton, 2);
        row.Children.Add(nextButton);
        return row;
    }

    private Control Welcome()
    {
        var logo = new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://AgexDesktop/Assets/agex-256.png"))), Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left };
        return Kit.Column(16,
            logo,
            Kit.Text("Welcome to AGEX", "display"),
            Kit.Text("Chat with your AI agents, or let them work together on a task.", "subtitle"),
            Kit.Text("Ask anything right away. When there is work to do, AGEX plans it, hands it to the right agents, checks the result and shows every change. Your history stays on this computer.", "body"),
            Nav("Get started", () => Go(1), canGoBack: false));
    }

    /// <summary>Plain-language interests; each maps to a Team AGEX can suggest.</summary>
    private static readonly (string Label, string Team)[] Interests =
    [
        ("Software", "software-builder"), ("Architecture / BIM", "architecture-bim"), ("Marketing", "marketing-growth"),
        ("Research", "research-lab"), ("Documents", "document-office"), ("Data", "data-analyst"),
        ("Automation", "computer-operator"), ("Job search", "job-search"), ("Private / local AI", "local-private"), ("Other", ""),
    ];

    private Control ChooseWork()
    {
        var grid = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, team) in Interests)
        {
            var key = team.Length > 0 ? team : "other";
            var chip = new ToggleButton { Content = label, IsChecked = _chosenWork.Contains(key), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8), MinWidth = 140, HorizontalContentAlignment = HorizontalAlignment.Center };
            chip.IsCheckedChanged += (_, _) => { if (chip.IsChecked == true) _chosenWork.Add(key); else _chosenWork.Remove(key); };
            AutomationProperties.SetName(chip, label);
            grid.Children.Add(chip);
        }
        return Kit.Column(16,
            Kit.Text("What would you like AGEX to help you with?", "title"),
            Kit.Text("Pick any that fit. AGEX suggests Teams, connections and skills for them. Nothing is installed now.", "small"),
            grid,
            Nav("Continue", () =>
            {
                Workspace.Settings.ActiveJobTeam = Interests.Select(item => item.Team).FirstOrDefault(team => team.Length > 0 && _chosenWork.Contains(team)) ?? "";
                Workspace.SaveSettings();
                Go(2);
                _ = Workspace.ScanAsync();
            }));
    }

    private static readonly (string Title, Func<DiscoveredItem, bool> Match)[] Groups =
    [
        ("Agents", item => item.Kind is DiscoveredKind.Agent or DiscoveredKind.LocalRuntime),
        ("IDEs", item => item.Kind == DiscoveredKind.Ide),
        ("Tools", item => item.Kind == DiscoveredKind.Tool),
        ("Integrations", item => item.Kind == DiscoveredKind.Integration),
    ];


    private static (string Text, Tone Tone) StatusOf(DiscoveredItem item) => item.Status switch
    {
        AgentStatus.Supported => ("Ready", Tone.Success),
        AgentStatus.Available => ("Checking...", Tone.Info),
        AgentStatus.AuthRequired => ("Sign-in required", Tone.Warning),
        AgentStatus.Broken => ("Not working", Tone.Danger),
        AgentStatus.DetectedUnsupported => ("Detected - not integrated", Tone.Neutral),
        AgentStatus.PlatformUnsupported => ("Not available on this system", Tone.Neutral),
        _ => ("Not installed", Tone.Neutral),
    };

    private Control ItemRow(DiscoveredItem item)
    {
        var (text, tone) = StatusOf(item);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(Kit.Avatar(item.Name, 30));
        var name = Kit.Column(0, Kit.Text(item.Name + (item.Version.Length > 0 ? "  " + item.Version : ""), "body"), Kit.Text(item.Detail, "caption"));
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var badge = Kit.Badge(text, tone);
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);
        return grid;
    }


    private Control ChooseAgents()
    {
        if (Workspace.Scanning || Workspace.Scan is null)
            return Kit.Column(16, Kit.Text("Finding your agents...", "title"),
                Kit.Text("AGEX looks only in known install locations for known tools. It does not read your files or send anything anywhere.", "small"),
                new ProgressBar { IsIndeterminate = true, Height = 4 }, Nav("Continue", enabled: false));
        var ready = Workspace.Scan?.Agents.Where(item => item.HasAdapter && item.Status is AgentStatus.Supported or AgentStatus.Available).ToList() ?? [];
        if (_chosenAgents.Count == 0)
            foreach (var item in ready.Where(item => item.Id is "codex" or "antigravity" || ready.Count <= 2)) _chosenAgents.Add(item.Id);
        var list = Kit.Column(8);
        foreach (var item in ready)
        {
            var adapter = Workspace.Core.Registry.Get(item.Id)!;
            var title = Kit.Row(8, Kit.Text(item.Name, "body"),
                item.Id is "codex" or "antigravity" ? Kit.Badge("Recommended", Tone.Accent) : null,
                adapter.Stability == AdapterStability.Beta ? Kit.Badge("Beta", Tone.Info) : null);
            var box = new CheckBox { IsChecked = _chosenAgents.Contains(item.Id), Content = Kit.Column(2, title, Kit.Text(adapter.Description + " Data goes to: " + adapter.DataDestination(null) + ".", "caption")) };
            box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) _chosenAgents.Add(item.Id); else _chosenAgents.Remove(item.Id); };
            AutomationProperties.SetName(box, item.Name);
            list.Children.Add(box);
        }
        if (ready.Count == 0) list.Children.Add(Kit.Text("No agent is ready yet. Continue; you can choose agents later in Agents.", "small"));
        return Kit.Column(16, Kit.Text("Choose your agents", "title"), Kit.Text("AGEX coordinates the agents you pick. You can change this at any time.", "small"), list,
            Nav("Continue", () =>
            {
                if (_chosenAgents.Count > 0) Workspace.Settings.EnabledAgents = _chosenAgents.ToList();
                Workspace.SaveSettings();
                Go(_step + 1);
            }));
    }



    /// <summary>Done: a short list of what AGEX suggests for the chosen interests, each one click away (never forced).</summary>
    private Control Ready()
    {
        var teams = Interests.Where(item => item.Team.Length > 0 && _chosenWork.Contains(item.Team)).Select(item => JobTeamCatalog.Get(item.Team)).OfType<JobTeam>().ToList();
        var list = Kit.Column(8);
        foreach (var team in teams)
        {
            var id = team.Id;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            var text = Kit.Column(2, Kit.Text(team.Name, "body"), Kit.Text(team.Summary, "caption"));
            ((TextBlock)text.Children[1]).TextWrapping = TextWrapping.Wrap;
            row.Children.Add(text);
            var setup = Kit.Button("Set up", () => { Finish(); _window.Navigate("teams"); _window.Page<TeamsPage>("teams").OpenSetup(id); }, "subtle", Icons.Tool);
            AutomationProperties.SetName(setup, "Set up " + team.Name);
            Grid.SetColumn(setup, 1);
            row.Children.Add(setup);
            list.Children.Add(row);
        }
        return Kit.Column(16,
            Kit.Text("You're ready", "display"),
            Kit.Text("Say hi, ask a question, or describe a task. Choose a project folder only when agents should work on files.", "subtitle"),
            teams.Count > 0 ? Kit.Column(8, Kit.Text("Suggested for you (optional)", "caption"), list) : null,
            Kit.Text("Tip: press " + Kit.ShortcutText("K") + " anytime to search commands.", "caption"),
            Nav("Start chatting", Finish));
    }

    private void Finish()
    {
        Workspace.Settings.FirstRunComplete = true;
        Workspace.SaveSettings();
        _done();
    }
}
