using Agex.Core.Agents;
using Agex.Core.Skills;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
    private int _step;
    private const int Steps = 7;

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
        Workspace.ScanChanged += () => { if (_step is 1 or 2 or 3) Show(); };
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
        _body.Content = _step switch
        {
            0 => Welcome(),
            1 => Scanning(),
            2 => Found(),
            3 => ChooseAgents(),
            4 => ChooseSkills(),
            5 => ChooseProject(),
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
            Kit.Text("AGEX lets multiple AI agents work together on your projects.", "subtitle"),
            Kit.Text("You describe what you want. AGEX plans the work, hands tasks to the agents you choose, checks their results and shows you every step. Your projects and history stay on this computer.", "body"),
            Nav("Get started", () => { Go(1); _ = Workspace.ScanAsync(); }, canGoBack: false));
    }

    private static readonly (string Title, Func<DiscoveredItem, bool> Match)[] Groups =
    [
        ("Agents", item => item.Kind is DiscoveredKind.Agent or DiscoveredKind.LocalRuntime),
        ("IDEs", item => item.Kind == DiscoveredKind.Ide),
        ("Tools", item => item.Kind == DiscoveredKind.Tool),
        ("Integrations", item => item.Kind == DiscoveredKind.Integration),
    ];

    private Control Scanning()
    {
        var scan = Workspace.Scan;
        var list = Kit.Column(10);
        foreach (var (title, match) in Groups)
        {
            var found = scan?.Items.Where(match).Count(item => item.Status is not (AgentStatus.NotInstalled or AgentStatus.PlatformUnsupported)) ?? 0;
            list.Children.Add(Kit.Row(10, Kit.Icon(Workspace.Scanning ? Icons.Refresh : Icons.Check, 16, Workspace.Scanning ? "Text3Brush" : "SuccessBrush"), Kit.Text(title, "body"), Kit.Text(scan is null ? "checking..." : $"{found} found", "small")));
        }
        return Kit.Column(16,
            Kit.Text(Workspace.Scanning ? "Scanning your computer..." : "Scan complete", "title"),
            Kit.Text("AGEX looks only in known install locations for known tools. It does not read your files or send anything anywhere.", "small"),
            Workspace.Scanning ? new ProgressBar { IsIndeterminate = true, Height = 4 } : null,
            list,
            Nav("Continue", enabled: !Workspace.Scanning));
    }

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

    private Control Found()
    {
        var agents = Workspace.Scan?.Agents.ToList() ?? [];
        var supported = agents.Where(item => item.HasAdapter).ToList();
        var detected = agents.Where(item => !item.HasAdapter && item.Status == AgentStatus.DetectedUnsupported).ToList();
        var list = Kit.Column(12);
        foreach (var item in supported) list.Children.Add(ItemRow(item));
        if (detected.Count > 0)
        {
            list.Children.Add(Kit.Text("Also found (AGEX cannot drive these yet)", "small"));
            foreach (var item in detected) list.Children.Add(ItemRow(item));
        }
        var ready = supported.Count(item => item.Status == AgentStatus.Supported);
        return Kit.Column(16,
            Kit.Text("Agents found", "title"),
            Kit.Text(ready == 0 ? "No agent is ready yet. You can still finish setup and add one later: install Codex, Antigravity, Claude Code, Gemini CLI or Ollama, then press Scan again in Agents." : $"{ready} agent{(ready == 1 ? " is" : "s are")} ready to work with AGEX.", "small"),
            list,
            Nav("Continue"));
    }

    private Control ChooseAgents()
    {
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

    private Control ChooseSkills()
    {
        var recommended = Workspace.Core.Skills.Catalog().Skills.Where(skill => skill.Recommended).ToList();
        var installed = Workspace.Core.Skills.Installed().Select(skill => skill.Id).ToHashSet();
        var list = Kit.Column(8);
        Control? navigation = null;
        foreach (var skill in recommended)
        {
            var box = new CheckBox { IsChecked = _chosenSkills.Contains(skill.Id) || installed.Contains(skill.Id), IsEnabled = !installed.Contains(skill.Id), Content = Kit.Column(0, Kit.Text(skill.Name + (installed.Contains(skill.Id) ? "  (installed)" : ""), "body"), Kit.Text($"{skill.Description} By {skill.Author}. {skill.License}.", "caption")) };
            box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) _chosenSkills.Add(skill.Id); else _chosenSkills.Remove(skill.Id); UpdateSkillButton(); };
            AutomationProperties.SetName(box, skill.Name);
            list.Children.Add(box);
        }
        var status = Kit.Text("", "small");
        var progress = new ProgressBar { IsVisible = false, Height = 4 };
        navigation = Nav(_chosenSkills.Count == 0 ? "Skip" : "Install selected", async () =>
        {
            if (_chosenSkills.Count == 0) { Go(_step + 1); return; }
            navigation!.IsEnabled = false;
            progress.IsVisible = true;
            var failures = new List<string>();
            foreach (var id in _chosenSkills.ToList())
            {
                var manifest = recommended.First(skill => skill.Id == id);
                status.Text = $"Installing {manifest.Name}...";
                try { await Workspace.Core.Skills.InstallAsync(manifest, null, null, CancellationToken.None); }
                catch (Exception ex) { failures.Add($"{manifest.Name}: {ex.Message}"); }
            }
            progress.IsVisible = false;
            navigation.IsEnabled = true;
            if (failures.Count > 0) await _window.Dialogs.MessageAsync("Some skills were not installed", string.Join("\n", failures) + "\n\nYou can retry from the Skills page.");
            Go(_step + 1);
        });
        void UpdateSkillButton()
        {
            if (navigation is Grid grid && grid.Children.OfType<Button>().LastOrDefault() is { } next)
                next.Content = Kit.Text(_chosenSkills.Count == 0 ? "Skip" : $"Install selected ({_chosenSkills.Count})", "body");
        }
        return Kit.Column(16, Kit.Text("Recommended skills (optional)", "title"),
            Kit.Text("Skills add abilities such as a browser or library documentation. Nothing is installed unless you select it. Each skill shows who made it and what it may do.", "small"),
            list, progress, status, navigation);
    }

    private Control ChooseProject()
    {
        var current = Kit.Text(Workspace.Project is { } project ? "Selected: " + project.Name + " (" + Agex.Core.Runtime.Redactor.RedactPaths(project.Path) + ")" : "No project selected yet.", "small");
        var recent = Kit.Column(6);
        foreach (var path in Workspace.Settings.RecentProjects.Where(Directory.Exists).Take(5))
        {
            var button = Kit.Button(Path.GetFileName(path.TrimEnd('/', '\\')) + "  -  " + Agex.Core.Runtime.Redactor.RedactPaths(path), () => { Workspace.OpenProject(path); Show(); }, "subtle", Icons.Folder);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            recent.Children.Add(button);
        }
        return Kit.Column(16, Kit.Text("Choose a project", "title"),
            Kit.Text("A project is a folder the agents work in, for example a code repository or a documents folder. AGEX never works outside it.", "small"),
            Kit.Button("Choose folder...", async () => { await _window.PickProjectAsync(); Show(); }, "", Icons.Folder),
            recent.Children.Count > 0 ? Kit.Column(6, Kit.Text("Recent", "caption"), recent) : null,
            current,
            Nav(Workspace.Project is null ? "Skip for now" : "Continue"));
    }

    private Control Ready() => Kit.Column(16,
        Kit.Text("Ready", "display"),
        Kit.Text("Tell AGEX what you want done.", "subtitle"),
        Kit.Text("Examples: \"Add a dark mode toggle to the settings page\", \"Find why the login test fails and fix it\", \"Summarize this folder's documents\".", "small"),
        Kit.Text("Tip: press " + Kit.ShortcutText("K") + " anytime to search commands.", "caption"),
        Nav("Start using AGEX", Finish));

    private void Finish()
    {
        Workspace.Settings.FirstRunComplete = true;
        Workspace.SaveSettings();
        _done();
    }
}
