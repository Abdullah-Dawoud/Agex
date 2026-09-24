using Agex.Core.Skills;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Agex.Desktop.Pages;

/// <summary>
/// Skills: browse the curated catalog (search, filters, packs), look at the
/// details before installing, install with one click after a clear look at what
/// the skill may do, connect accounts, manage installed skills, update them, and
/// add your own.
/// </summary>
public sealed class SkillsPage(MainWindow window) : AppPage(window)
{
    private readonly WrapPanel _cards = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _packs = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _installed = new() { Spacing = 10 };
    private readonly StackPanel _updates = new() { Spacing = 10 };
    private readonly TextBox _search = new() { PlaceholderText = "Search skills", MinWidth = 240 };
    private readonly TextBlock _count = Kit.Text("", "caption");
    private readonly Panel _filters = new StackPanel { Spacing = 8 };
    private readonly HashSet<string> _busy = [];
    private string _category = "All";
    private string _tier = "All";
    private string _trust = "All";
    private string _show = "All";
    private string _agent = "any";
    private string _account = "Any";
    private bool _thisSystemOnly = true;
    private string _sort = "Recommended";
    private TabControl? _tabs;

    public override string Id => "skills";
    public override string Title => "Skills";
    public override string Icon => Icons.Skills;

    private static readonly string[] Categories = ["All", "Developer", "Testing", "Debugging", "Security", "Git & GitHub", "Web", "Research", "Documents", "Data", "Design", "DevOps", "Productivity"];
    private static readonly string[] Tiers = ["All", "Recommended", "Popular", "Community", "Advanced", "Requires account", "Requires local dependency"];

    protected override Control Build()
    {
        AutomationProperties.SetName(_search, "Search skills");
        _search.TextChanged += (_, _) => RefreshCatalog();
        BuildFilters();
        var packs = new Expander
        {
            Header = "Skill packs: install a ready-made selection",
            Content = Kit.Column(8, Kit.Text("A pack selects several skills at once. Skills you already have are not installed twice.", "caption"), _packs),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var discover = Kit.Column(12, packs, Kit.Row(12, _search, _count), _filters, _cards);
        var addOwn = BuildAddOwn();
        _tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Discover", Content = discover },
                new TabItem { Header = "Installed", Content = _installed },
                new TabItem { Header = "Updates", Content = _updates },
                new TabItem { Header = "Add your own", Content = addOwn },
            },
        };
        var intro = Kit.Text("A skill gives agents extra know-how (instructions in the open SKILL.md format) or a tool (an MCP server). AGEX never runs skill code itself: agents use skills inside their own safety limits. Everything in the catalog is pinned to an exact version and checked before install. Accounts are connected with the provider's own keys or sign-in; keys stay in " + Workspace.Core.Platform.SecureStore.Mechanism + ".", "small");
        var page = Kit.Column(12, Kit.PageHeader("Skills", Window.Workspace.Core.SafeMode ? "Safe mode is on: installed skills are not used until you restart normally." : null), intro, _tabs);
        Refresh();
        return Kit.Page(page, 1200);
    }

    public override void OnShown() => Refresh();

    private void BuildFilters()
    {
        _filters.Children.Clear();
        var agents = new List<(string, string)> { ("any", "All agents") };
        agents.AddRange(Workspace.Core.Registry.Adapters.Select(adapter => (adapter.Id, adapter.Name)));
        var systemOnly = new CheckBox { Content = "Only skills for this system", IsChecked = _thisSystemOnly };
        systemOnly.IsCheckedChanged += (_, _) => { _thisSystemOnly = systemOnly.IsChecked == true; RefreshCatalog(); };
        _filters.Children.Add(Kit.Wrap(
            Labeled("Category", Kit.Combo(Categories.Select(c => (c, c)), _category, value => { _category = value; RefreshCatalog(); }, 160)),
            Labeled("Tier", Kit.Combo(Tiers.Select(t => (t, t)), _tier, value => { _tier = value; RefreshCatalog(); }, 210)),
            Labeled("Trust", Kit.Combo([("All", "All"), ("Official", "Official"), ("Curated", "AGEX Curated"), ("Community", "Community")], _trust, value => { _trust = value; RefreshCatalog(); }, 150)),
            Labeled("Show", Kit.Combo([("All", "All"), ("Installed", "Installed"), ("Available", "Not installed")], _show, value => { _show = value; RefreshCatalog(); }, 140)),
            Labeled("Works with", Kit.Combo(agents, _agent, value => { _agent = value; RefreshCatalog(); }, 170)),
            Labeled("Account", Kit.Combo([("Any", "Any"), ("None", "No account needed"), ("Required", "Requires account")], _account, value => { _account = value; RefreshCatalog(); }, 170)),
            Labeled("Sort", Kit.Combo([("Recommended", "Recommended"), ("Popular", "Popular"), ("Updated", "Recently updated"), ("Name", "Name")], _sort, value => { _sort = value; RefreshCatalog(); }, 170)),
            systemOnly));
    }

    private static Control Labeled(string label, Control control)
    {
        var column = Kit.Column(2, Kit.Text(label, "caption"), control);
        column.Margin = new Thickness(0, 0, 12, 8);
        AutomationProperties.SetName(control, label);
        return column;
    }

    private void Refresh()
    {
        RefreshPacks();
        RefreshCatalog();
        RefreshInstalled();
        RefreshUpdates();
    }

    // ------------------------------------------------------------ catalog

    private string OsId => Workspace.Core.Platform.Os switch { Agex.Core.Platform.OsKind.Windows => "windows", Agex.Core.Platform.OsKind.MacOS => "macos", _ => "linux" };

    private bool MatchesTier(SkillManifest skill) => _tier switch
    {
        "Recommended" => skill.Recommended,
        "Popular" => skill.Tags.Contains("popular"),
        "Community" => skill.Trust == SkillTrust.Community,
        "Advanced" => skill.Tags.Contains("advanced"),
        "Requires account" => skill.RequiresAccount,
        "Requires local dependency" => skill.RequiredTools.Count > 0,
        _ => true,
    };

    private void RefreshCatalog()
    {
        _cards.Children.Clear();
        var installed = Workspace.Core.Skills.Installed().ToDictionary(skill => skill.Id);
        var query = _search.Text?.Trim() ?? "";
        IEnumerable<SkillManifest> skills = Workspace.Core.Skills.Catalog().Skills
            .Where(skill => _category == "All" || skill.Categories.Contains(_category))
            .Where(MatchesTier)
            .Where(skill => _trust switch { "Official" => skill.Trust == SkillTrust.Verified, "Curated" => skill.Trust == SkillTrust.Curated, "Community" => skill.Trust == SkillTrust.Community, _ => true })
            .Where(skill => _show switch { "Installed" => installed.ContainsKey(skill.Id), "Available" => !installed.ContainsKey(skill.Id), _ => true })
            .Where(skill => _agent == "any" || skill.SupportedAgents.Count == 0 || skill.SupportedAgents.Contains(_agent))
            .Where(skill => _account switch { "None" => !skill.RequiresAccount, "Required" => skill.RequiresAccount, _ => true })
            .Where(skill => !_thisSystemOnly || skill.SupportedPlatforms.Count == 0 || skill.SupportedPlatforms.Contains(OsId))
            .Where(skill => query.Length == 0 || skill.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || skill.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || skill.Author.Contains(query, StringComparison.OrdinalIgnoreCase) || skill.Categories.Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)));
        skills = _sort switch
        {
            "Popular" => skills.OrderByDescending(skill => skill.Tags.Contains("popular")).ThenByDescending(skill => skill.Popularity?.Value ?? 0),
            "Updated" => skills.OrderByDescending(skill => skill.LastUpdated, StringComparer.Ordinal),
            "Name" => skills.OrderBy(skill => skill.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => skills.OrderByDescending(skill => skill.Recommended).ThenBy(skill => skill.Trust).ThenByDescending(skill => skill.Tags.Contains("popular")).ThenBy(skill => skill.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var list = skills.ToList();
        _count.Text = $"{list.Count} of {Workspace.Core.Skills.Catalog().Skills.Count} skills";
        if (list.Count == 0) { _cards.Children.Add(Kit.Text("No skills match these filters.", "small")); return; }
        foreach (var skill in list)
        {
            // One bad entry must never break the page.
            try { _cards.Children.Add(Card(skill, installed.GetValueOrDefault(skill.Id))); }
            catch (Exception ex) { Workspace.Core.Log.Error("skill_card_failed", ex, new { id = skill.Id }); }
        }
    }

    private static Tone TrustTone(SkillTrust trust) => trust switch { SkillTrust.Verified or SkillTrust.Curated => Tone.Success, SkillTrust.Community => Tone.Warning, _ => Tone.Neutral };

    private static (string, Tone) ReadinessLook(SkillState state) => state.Readiness switch
    {
        SkillReadiness.Ready => ("Ready", Tone.Success),
        SkillReadiness.AccountRequired => ("Account required", Tone.Warning),
        SkillReadiness.DependencyMissing => ("Dependency missing", Tone.Warning),
        SkillReadiness.PlatformUnsupported => ("Not for this system", Tone.Neutral),
        SkillReadiness.AgentIncompatible => ("No compatible agent enabled", Tone.Neutral),
        SkillReadiness.Disabled => ("Installed (off)", Tone.Neutral),
        SkillReadiness.Broken => ("Needs attention", Tone.Danger),
        _ => ("Not installed", Tone.Neutral),
    };

    private Control Card(SkillManifest skill, InstalledSkill? installed)
    {
        var state = Workspace.Core.Skills.State(skill, installed, Workspace.Settings.EnabledAgents);
        var (stateText, stateTone) = ReadinessLook(state);
        var risk = SkillText.Risk(skill);
        var busy = _busy.Contains(skill.Id);
        var body = Kit.Column(8,
            Kit.Row(8, Kit.Icon(skill.Kind == SkillKind.Mcp ? Icons.Tool : Icons.Skills, 18, "AccentBrush"), Kit.Text(skill.Name, "subtitle")),
            Kit.Text($"{skill.Author} · {skill.License} · updated {skill.LastUpdated}", "caption"),
            Kit.Text(skill.Description, "small"),
            Kit.Wrap(
                Kit.Badge(SkillText.Trust(skill.Trust), TrustTone(skill.Trust), Icons.Shield),
                Kit.Badge(stateText, stateTone),
                skill.RequiresAccount ? Kit.Badge("Requires account", Tone.Info) : null,
                skill.RequiredTools.Count > 0 ? Kit.Badge("Needs " + string.Join(", ", skill.RequiredTools.Select(tool => SkillManager.Tool(tool).Label)), Tone.Neutral) : null,
                Kit.Badge($"{risk} risk", risk == SkillRisk.High ? Tone.Warning : Tone.Neutral),
                skill.Kind == SkillKind.Mcp ? Kit.Badge("Tool (MCP)", Tone.Neutral) : null),
            state.Readiness is SkillReadiness.DependencyMissing or SkillReadiness.AgentIncompatible or SkillReadiness.PlatformUnsupported or SkillReadiness.AccountRequired or SkillReadiness.Broken ? Kit.Text(state.Detail, "caption") : null,
            Kit.Row(8, PrimaryAction(skill, installed, state, busy), Kit.Button("Details", () => _ = DetailsAsync(skill), "subtle")));
        var card = Kit.Card(body);
        card.Width = 340;
        card.Margin = new Thickness(0, 0, 12, 12);
        return card;
    }

    /// <summary>The one next step for a skill, as a button.</summary>
    private Control? PrimaryAction(SkillManifest skill, InstalledSkill? installed, SkillState state, bool busy)
    {
        Button? button = state.Readiness switch
        {
            SkillReadiness.NotInstalled => Kit.Button(busy ? "Installing..." : "Install", () => _ = InstallAsync(skill), "primary", Icons.Download),
            SkillReadiness.AccountRequired when installed is not null => Kit.Button(skill.Auth?.Type == SkillAuthType.ApiKey ? "Add key" : "Connect", () => _ = ConnectAsync(installed), "primary", Icons.Shield),
            SkillReadiness.DependencyMissing when state.MissingTools.FirstOrDefault(tool => tool.InstallUrl.Length > 0) is { } tool => Kit.Button($"Get {tool.Label}", () => Workspace.Core.Platform.OpenUrl(new Uri(tool.InstallUrl)), "", Icons.External, "Opens the official download page"),
            SkillReadiness.Disabled when installed is not null => Kit.Button("Turn on", () => { Workspace.Core.Skills.SetEnabled(installed.Id, true); Refresh(); }, ""),
            _ => null,
        };
        if (button is null) return installed is not null ? Kit.Badge(installed.Enabled ? "Installed" : "Installed (off)", installed.Enabled ? Tone.Success : Tone.Neutral) : null;
        button.IsEnabled = !busy;
        return button;
    }

    private void RefreshPacks()
    {
        _packs.Children.Clear();
        var catalog = Workspace.Core.Skills.Catalog();
        var installed = Workspace.Core.Skills.Installed().Select(skill => skill.Id).ToHashSet();
        foreach (var pack in catalog.Packs)
        {
            var missing = pack.Skills.Count(id => !installed.Contains(id));
            var names = pack.Skills.Select(id => catalog.Skills.FirstOrDefault(skill => skill.Id == id)?.Name ?? id);
            var captured = pack;
            var body = Kit.Column(6,
                Kit.Text(pack.Name, "body"),
                Kit.Text(pack.Description, "caption"),
                Kit.Text(string.Join(" · ", names), "caption"),
                missing == 0 ? Kit.Badge("All installed", Tone.Success) : Kit.Button($"Install {missing} skill{(missing == 1 ? "" : "s")}", () => _ = InstallPackAsync(captured), "subtle", Icons.Download));
            var card = Kit.Card(body, 12);
            card.Width = 270;
            card.Margin = new Thickness(0, 0, 10, 10);
            _packs.Children.Add(card);
        }
    }

    private async Task InstallPackAsync(SkillPack pack)
    {
        var catalog = Workspace.Core.Skills.Catalog();
        var installed = Workspace.Core.Skills.Installed().Select(skill => skill.Id).ToHashSet();
        var todo = pack.Skills.Where(id => !installed.Contains(id)).Select(id => catalog.Skills.FirstOrDefault(skill => skill.Id == id)).OfType<SkillManifest>()
            .Where(skill => Workspace.Core.Skills.CheckCompatibility(skill, Workspace.Settings.EnabledAgents).Installable).ToList();
        if (todo.Count == 0) { Window.Toast("Nothing to install", "Every skill in this pack is installed or not available for this system.", ToastKind.Info); return; }
        var list = Kit.Column(6, todo.Select(skill => (Control?)Kit.Text($"{skill.Name} · {SkillText.Trust(skill.Trust)} · {string.Join(", ", skill.Permissions.Select(SkillText.Permission))}{(skill.RequiresAccount ? " · requires an account (connect it afterwards)" : "")}", "small")).ToArray());
        var body = Kit.Column(10, Kit.Text($"{todo.Count} skills will be installed with the permissions shown. You can change any permission later under Installed.", "body"), list);
        if (await Window.Dialogs.ShowAsync($"Install {pack.Name}?", body, ["Install", "Cancel"]) != 0) return;
        var failures = new List<string>();
        foreach (var skill in todo)
        {
            _busy.Add(skill.Id);
            try { await Workspace.Core.Skills.InstallAsync(skill, null, null, CancellationToken.None); }
            catch (Exception ex) when (ex is SkillException or IOException or UnauthorizedAccessException or HttpRequestException) { failures.Add($"{skill.Name}: {ex.Message}"); }
            finally { _busy.Remove(skill.Id); }
        }
        if (failures.Count > 0) await Window.Dialogs.MessageAsync("Some skills were not installed", string.Join("\n", failures));
        else Window.Toast("Pack installed", $"{todo.Count} skills from {pack.Name} are ready.", ToastKind.Success);
        Refresh();
    }

    private async Task DetailsAsync(SkillManifest skill)
    {
        var installed = Workspace.Core.Skills.Installed().FirstOrDefault(item => item.Id == skill.Id);
        var state = Workspace.Core.Skills.State(skill, installed, Workspace.Settings.EnabledAgents);
        var (stateText, _) = ReadinessLook(state);
        var agents = string.Join(", ", skill.SupportedAgents.Select(id => Workspace.Core.Registry.Get(id)?.Name ?? id));
        var source = skill.Kind == SkillKind.Instructions
            ? $"{skill.Source?.Repository} at commit {skill.Source?.Commit[..7]}; {skill.Source?.Files.Count} files, each checked against its SHA-256."
            : skill.Mcp?.Transport == "http" ? $"Hosted endpoint {skill.Mcp.Url}" : $"Runs '{skill.Mcp?.Command} {string.Join(' ', skill.Mcp?.Args ?? [])}' when an agent uses it.";
        var account = skill.Auth switch
        {
            { Type: SkillAuthType.ApiKey } auth => $"{auth.Label}. Stored in {Workspace.Core.Platform.SecureStore.Mechanism}, passed to the tool as an environment variable, never shown again or logged. {auth.Note}",
            { Type: SkillAuthType.CliLogin } auth => $"{auth.Label}. The tool keeps its own sign-in; AGEX never sees it.",
            _ => "No account needed.",
        };
        var body = Kit.Column(8,
            Kit.Text(skill.Description, "body"),
            Fact("Status", null, Kit.Text(stateText + (state.Detail.Length > 0 ? " - " + state.Detail : ""), "small")),
            Fact("Trust", SkillText.TrustExplanation(skill.Trust), Kit.Text(SkillText.Trust(skill.Trust), "small")),
            Fact("Author", null, Kit.Text(skill.Author, "small")),
            Fact("Licence", null, Kit.Text(skill.License, "small")),
            Fact("Version", null, Kit.Text($"{skill.Version} (updated {skill.LastUpdated})", "small")),
            Fact("Source", null, Kit.Text(source, "small")),
            Fact("Works on", null, Kit.Text(string.Join(", ", skill.SupportedPlatforms), "small")),
            Fact("Works with", null, Kit.Text(agents, "small")),
            Fact("May", null, Kit.Text(skill.Permissions.Count == 0 ? "Only change how agents write." : string.Join(", ", skill.Permissions.Select(SkillText.Permission)), "small")),
            Fact("Account", null, Kit.Text(account, "small")),
            Fact("Needs", null, Kit.Text(skill.RequiredTools.Count == 0 ? "Nothing else." : string.Join(", ", skill.RequiredTools.Select(tool => SkillManager.Tool(tool).Label)), "small")),
            Fact("Risk", null, Kit.Text($"{SkillText.Risk(skill)}. {skill.RiskNote} {skill.CompatibilityNote}".Trim(), "small")),
            skill.Popularity is { } popularity ? Fact("Popularity", null, Kit.Text($"{popularity.Label}: {popularity.Value:N0} ({popularity.AsOf})", "small")) : null);
        var buttons = new List<(string Label, Func<Task> Action)>();
        if (installed is null && state.Readiness == SkillReadiness.NotInstalled) buttons.Add(("Install", () => InstallAsync(skill)));
        if (installed is not null && skill.Auth?.Type == SkillAuthType.ApiKey)
        {
            var connected = Workspace.Core.Skills.HasAccountKey(installed.Id, installed.Manifest);
            buttons.Add((connected ? "Change key" : "Add key", () => ConnectAsync(installed)));
            if (connected && skill.Auth.Test is not null) buttons.Add(("Test connection", () => TestConnectionAsync(installed)));
            if (connected) buttons.Add(("Disconnect", () => { Workspace.Core.Skills.Disconnect(installed.Id, installed.Manifest); Refresh(); return Task.CompletedTask; }));
        }
        if (installed is not null && skill.Auth?.Type == SkillAuthType.CliLogin) buttons.Add(("Sign in", () => CliSignInAsync(skill)));
        if (skill.Auth?.SetupUrl is { Length: > 0 } setupUrl) buttons.Add(("Open provider setup", () => { Workspace.Core.Platform.OpenUrl(new Uri(setupUrl)); return Task.CompletedTask; }));
        foreach (var tool in state.MissingTools.Where(tool => tool.InstallUrl.Length > 0)) buttons.Add(($"Get {tool.Label}", () => { Workspace.Core.Platform.OpenUrl(new Uri(tool.InstallUrl)); return Task.CompletedTask; }));
        if (installed is not null) buttons.Add((installed.Enabled ? "Turn off" : "Turn on", () => { Workspace.Core.Skills.SetEnabled(installed.Id, !installed.Enabled); Refresh(); return Task.CompletedTask; }));
        if (installed is not null && Workspace.Core.Skills.AvailableUpdates().Any(update => update.Installed.Id == installed.Id)) buttons.Add(("Update", async () => { await Workspace.Core.Skills.UpdateAllAsync(null, CancellationToken.None); Refresh(); }));
        if (installed is not null) buttons.Add(("Remove", () => RemoveAsync(installed)));
        if (skill.Homepage.StartsWith("https://", StringComparison.Ordinal)) buttons.Add(("Open source", () => { Workspace.Core.Platform.OpenUrl(new Uri(skill.Homepage)); return Task.CompletedTask; }));
        buttons.Add(("Close", () => Task.CompletedTask));
        var choice = await Window.Dialogs.ShowAsync(skill.Name, new ScrollViewer { Content = body, MaxHeight = 520 }, buttons.Select(button => button.Label).ToArray());
        if (choice >= 0 && choice < buttons.Count) await buttons[choice].Action();
    }

    /// <summary>A label and a wrapping value, for the details view.</summary>
    private static Control Fact(string label, string? note, TextBlock value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), ColumnSpacing = 12 };
        grid.Children.Add(Kit.Text(label, "body"));
        value.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var right = Kit.Column(2, value, note is { Length: > 0 } ? Kit.Text(note, "caption") : null);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private async Task InstallAsync(SkillManifest skill)
    {
        var compatibility = Workspace.Core.Skills.CheckCompatibility(skill, Workspace.Settings.EnabledAgents);
        var choices = new Dictionary<SkillPermission, PermissionChoice>();
        var list = Kit.Column(8);
        foreach (var permission in skill.Permissions)
        {
            // Community skills start with "ask each time" for anything risky.
            var choice = skill.Trust == SkillTrust.Community && SkillText.IsHighRisk(permission) ? PermissionChoice.AskEachTime : PermissionChoice.AlwaysAllow;
            choices[permission] = choice;
            var captured = permission;
            var combo = Kit.Combo([(PermissionChoice.AlwaysAllow, "Always allow"), (PermissionChoice.AskEachTime, "Ask each time"), (PermissionChoice.Deny, "Don't allow")], choice, value => choices[captured] = value, 170);
            list.Children.Add(Kit.SettingRow(SkillText.Permission(permission), SkillText.IsHighRisk(permission) ? "Higher risk" : null, combo));
        }
        if (skill.Permissions.Count == 0) list.Children.Add(Kit.Text("This skill only changes how agents write. It needs no permission.", "small"));
        TextBox? keyBox = null;
        if (skill.Auth is { Type: SkillAuthType.ApiKey } auth)
        {
            keyBox = new TextBox { PasswordChar = '•', PlaceholderText = auth.Label + " (optional now)", MinWidth = 280 };
            AutomationProperties.SetName(keyBox, auth.Label);
            list.Children.Add(Kit.SettingRow(auth.Label, "Stored in " + Workspace.Core.Platform.SecureStore.Mechanism + ". Never logged or shown again. You can also add it later.", keyBox));
            if (!Workspace.Core.Platform.SecureStore.IsOsProtected) list.Children.Add(Kit.Badge("This system has no keyring: the key would be kept in a private file that is not encrypted.", Tone.Warning, Icons.Alert));
        }
        var body = Kit.Column(10,
            Kit.Text(skill.Description, "body"),
            skill.Trust == SkillTrust.Community ? Kit.Badge("Community skill: reviewed less deeply than Official or AGEX Curated skills. Risky permissions start as 'Ask each time'.", Tone.Warning, Icons.Alert) : null,
            Kit.Text($"From {skill.Author} ({SkillText.Trust(skill.Trust)}), licence {skill.License}. " + (skill.Kind == SkillKind.Instructions ? $"Pinned to commit {skill.Source?.Commit[..7]}; every file is checked against its SHA-256 before install." : skill.Mcp?.Transport == "http" ? $"Uses the hosted endpoint {skill.Mcp.Url}." : $"Runs '{skill.Mcp?.Command} {string.Join(' ', skill.Mcp?.Args ?? [])}' when an agent uses it."), "small"),
            skill.CompatibilityNote.Length > 0 ? Kit.Text(skill.CompatibilityNote, "small") : null,
            skill.RiskNote.Length > 0 ? Kit.Text(skill.RiskNote, "small") : null,
            compatibility.Warnings.Count > 0 ? Kit.Text(string.Join(" ", compatibility.Warnings), "small") : null,
            Kit.Text("What this skill may do", "body"),
            list);
        if (await Window.Dialogs.ShowAsync($"Install {skill.Name}?", body, ["Install", "Cancel"]) != 0) return;
        _busy.Add(skill.Id);
        RefreshCatalog();
        try
        {
            var installed = await Workspace.Core.Skills.InstallAsync(skill, choices, null, CancellationToken.None);
            if (keyBox is not null && !string.IsNullOrWhiteSpace(keyBox.Text) && skill.Auth is { } auth2) Workspace.Core.Skills.SetSecret(installed.Id, auth2.Secret, keyBox.Text.Trim());
            var state = Workspace.Core.Skills.State(skill, installed, Workspace.Settings.EnabledAgents);
            Window.Toast("Skill installed", state.Readiness == SkillReadiness.Ready ? $"{skill.Name} is ready and enabled." : $"{skill.Name} is installed. Next: {state.Detail}", state.Readiness == SkillReadiness.Ready ? ToastKind.Success : ToastKind.Info);
            if (skill.Auth?.Type == SkillAuthType.CliLogin) await CliSignInAsync(skill);
        }
        catch (SkillException ex) { await Window.Dialogs.MessageAsync("The skill was not installed", ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Window.Dialogs.MessageAsync("The skill was not installed", "AGEX could not write the skill files: " + ex.Message); }
        catch (HttpRequestException) { await Window.Dialogs.MessageAsync("The skill was not installed", "The skill source could not be reached. Check your internet connection and try again."); }
        finally
        {
            _busy.Remove(skill.Id);
            Refresh();
        }
    }

    private async Task ConnectAsync(InstalledSkill skill)
    {
        if (skill.Manifest.Auth is not { Type: SkillAuthType.ApiKey } auth) { await CliSignInAsync(skill.Manifest); return; }
        var box = new TextBox { PasswordChar = '•', PlaceholderText = auth.Label, MinWidth = 300 };
        AutomationProperties.SetName(box, auth.Label);
        var body = Kit.Column(8,
            Kit.Text($"Paste your {auth.Label}. {auth.Note}", "body"),
            Kit.Text("Saved in " + Workspace.Core.Platform.SecureStore.Mechanism + (Workspace.Core.Platform.SecureStore.IsOsProtected ? "." : ". Warning: this system has no keyring, so the file is private to your user but not encrypted."), "small"),
            auth.SetupUrl.Length > 0 ? Kit.Button("Create a key on the provider's site", () => Workspace.Core.Platform.OpenUrl(new Uri(auth.SetupUrl)), "link", Icons.External) : null,
            box);
        if (await Window.Dialogs.ShowAsync($"Connect {skill.Manifest.Name}", body, ["Save", "Cancel"]) != 0 || string.IsNullOrWhiteSpace(box.Text)) return;
        Workspace.Core.Skills.SetSecret(skill.Id, auth.Secret, box.Text.Trim());
        box.Text = "";
        if (auth.Test is not null) await TestConnectionAsync(skill);
        else Window.Toast("Key saved", $"{skill.Manifest.Name} is connected. The key is checked the first time an agent uses it.", ToastKind.Success);
        Refresh();
    }

    private async Task TestConnectionAsync(InstalledSkill skill)
    {
        var (success, message) = await Workspace.Core.Skills.TestConnectionAsync(skill.Id, skill.Manifest, CancellationToken.None);
        Window.Toast(success ? $"{skill.Manifest.Name}: account ready" : $"{skill.Manifest.Name}: not connected", message, success ? ToastKind.Success : ToastKind.Error);
    }

    /// <summary>Runs the tool's own sign-in (for example 'vercel login') in a visible terminal.</summary>
    private async Task CliSignInAsync(SkillManifest skill)
    {
        if (skill.Auth is not { Type: SkillAuthType.CliLogin } auth) return;
        var tool = Workspace.Core.Skills.ToolPath(auth.LoginTool);
        var display = (auth.LoginTool == "node" ? "npx" : auth.LoginTool) + " " + string.Join(' ', auth.LoginArgs);
        if (tool is null) { await Window.Dialogs.MessageAsync($"Sign in for {skill.Name}", $"{SkillManager.Tool(auth.LoginTool).Label} is needed first. After installing it, sign in with: {display}"); return; }
        var body = Kit.Column(8, Kit.Text($"{auth.Label}: a terminal opens and runs '{display}'. Follow its steps; the tool keeps its own sign-in and AGEX never sees it.", "body"),
            auth.SetupUrl.Length > 0 ? Kit.Button("About this sign-in", () => Workspace.Core.Platform.OpenUrl(new Uri(auth.SetupUrl)), "link", Icons.External) : null);
        if (await Window.Dialogs.ShowAsync($"Sign in for {skill.Name}", body, ["Open sign-in", "Later"]) != 0) return;
        var folder = Workspace.Project?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Workspace.Core.Platform.RunInTerminal(tool, auth.LoginArgs, folder))
            Window.Toast("Could not open a terminal", "Open a terminal yourself and run: " + display, ToastKind.Error);
    }

    private async Task RemoveAsync(InstalledSkill skill)
    {
        if (!await Window.ConfirmAsync($"Remove {skill.Manifest.Name}?", "Its files and any key saved for it are deleted.", "Remove", "Cancel")) return;
        Workspace.Core.Skills.Remove(skill.Id);
        Refresh();
    }

    // ----------------------------------------------------------- installed

    private void RefreshInstalled()
    {
        _installed.Children.Clear();
        var skills = Workspace.Core.Skills.Installed();
        if (skills.Count == 0) { _installed.Children.Add(Kit.EmptyState(Icons.Skills, "No skills installed", "Browse Discover and install what you need.")); return; }
        foreach (var skill in skills)
        {
            try { _installed.Children.Add(InstalledCard(skill)); }
            catch (Exception ex) { Workspace.Core.Log.Error("skill_card_failed", ex, new { id = skill.Id }); }
        }
    }

    private Control InstalledCard(InstalledSkill skill)
    {
        var id = skill.Id;
        var state = Workspace.Core.Skills.State(skill.Manifest, skill, Workspace.Settings.EnabledAgents);
        var (stateText, stateTone) = ReadinessLook(state);
        var toggle = new ToggleSwitch { IsChecked = skill.Enabled, OnContent = "On", OffContent = "Off" };
        AutomationProperties.SetName(toggle, $"Enable {skill.Manifest.Name}");
        toggle.IsCheckedChanged += (_, _) => { Workspace.Core.Skills.SetEnabled(id, toggle.IsChecked == true); Refresh(); };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(Kit.Column(2,
            Kit.Row(8, Kit.Text(skill.Manifest.Name, "subtitle"), Kit.Badge(SkillText.Trust(skill.Manifest.Trust), TrustTone(skill.Manifest.Trust), Icons.Shield), Kit.Badge(stateText, stateTone)),
            Kit.Text($"{skill.Manifest.Author} · v{skill.Manifest.Version} · installed {Kit.Ago(skill.InstalledAt)}", "caption")));
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);
        var permissions = Kit.Column(4);
        foreach (var (permission, choice) in skill.PermissionChoices)
        {
            var captured = permission;
            permissions.Children.Add(Kit.SettingRow(SkillText.Permission(permission), null,
                Kit.Combo([(PermissionChoice.AlwaysAllow, "Always allow"), (PermissionChoice.AskEachTime, "Ask each time"), (PermissionChoice.Deny, "Don't allow")], choice, value => Workspace.Core.Skills.SetPermission(id, captured, value), 170)));
        }
        var actions = new WrapPanel();
        void Add(Control? control) { if (control is null) return; control.Margin = new Thickness(0, 0, 8, 6); actions.Children.Add(control); }
        if (skill.Manifest.Auth?.Type == SkillAuthType.ApiKey)
        {
            var connected = Workspace.Core.Skills.HasAccountKey(id, skill.Manifest);
            Add(Kit.Button(connected ? "Change key" : "Add key", () => _ = ConnectAsync(skill), connected ? "subtle" : "primary", Icons.Shield));
            if (connected && skill.Manifest.Auth.Test is not null) Add(Kit.Button("Test connection", () => _ = TestConnectionAsync(skill), "subtle", Icons.Check));
            if (connected) Add(Kit.Button("Disconnect", () => { Workspace.Core.Skills.Disconnect(id, skill.Manifest); Refresh(); }, "subtle"));
        }
        else if (skill.Manifest.Auth?.Type == SkillAuthType.CliLogin) Add(Kit.Button("Sign in", () => _ = CliSignInAsync(skill.Manifest), "subtle", Icons.Shield));
        foreach (var tool in state.MissingTools.Where(tool => tool.InstallUrl.Length > 0)) Add(Kit.Button($"Get {tool.Label}", () => Workspace.Core.Platform.OpenUrl(new Uri(tool.InstallUrl)), "subtle", Icons.External));
        if (skill.Folder.Length > 0) Add(Kit.Button($"Show in {Workspace.Core.Platform.FileManagerName}", () => Workspace.Core.Platform.RevealInFileManager(skill.Folder), "subtle", Icons.External));
        if (skill.Manifest.Homepage.StartsWith("https://", StringComparison.Ordinal)) Add(Kit.Button("Source", () => Workspace.Core.Platform.OpenUrl(new Uri(skill.Manifest.Homepage)), "subtle", Icons.External));
        Add(Kit.Button("Remove", () => _ = RemoveAsync(skill), "subtle danger", Icons.Trash));
        var body = Kit.Column(8, header,
            skill.DisabledReason.Length > 0 ? Kit.Badge(skill.DisabledReason, Tone.Danger, Icons.Alert) : null,
            state.Readiness is SkillReadiness.AccountRequired or SkillReadiness.DependencyMissing or SkillReadiness.AgentIncompatible ? Kit.Text(state.Detail, "small") : null,
            Kit.Text(skill.Manifest.Description, "small"),
            new Expander { Header = "Permissions", Content = permissions, HorizontalAlignment = HorizontalAlignment.Stretch },
            actions);
        return Kit.Card(body);
    }

    private void RefreshUpdates()
    {
        _updates.Children.Clear();
        var updates = Workspace.Core.Skills.AvailableUpdates();
        var auto = new ToggleSwitch { IsChecked = Workspace.Settings.AutoUpdateSkills, OnContent = "On", OffContent = "Off" };
        auto.IsCheckedChanged += (_, _) => { Workspace.Settings.AutoUpdateSkills = auto.IsChecked == true; Workspace.SaveSettings(); };
        _updates.Children.Add(Kit.SettingRow("Update curated skills automatically", "Updates come with new AGEX catalogs and are verified the same way as installs.", auto));
        if (updates.Count == 0) { _updates.Children.Add(Kit.Text("All installed skills are up to date.", "small")); return; }
        _updates.Children.Add(Kit.Button($"Update all ({updates.Count})", async () =>
        {
            try { var count = await Workspace.Core.Skills.UpdateAllAsync(null, CancellationToken.None); Window.Toast("Skills updated", $"{count} updated.", ToastKind.Success); }
            catch (SkillException ex) { await Window.Dialogs.MessageAsync("Update failed", ex.Message); }
            Refresh();
        }, "primary", Icons.Download));
        foreach (var (installed, available) in updates)
            _updates.Children.Add(Kit.Card(Kit.Column(4, Kit.Text($"{available.Name}: {installed.Manifest.Version} → {available.Version}", "body"), Kit.Text(available.ReleaseNotes, "small")), 12));
    }

    // ------------------------------------------------------------ your own

    private Control BuildAddOwn()
    {
        var url = new TextBox { PlaceholderText = "https://github.com/owner/repo/tree/main/skills/my-skill", MinWidth = 420 };
        AutomationProperties.SetName(url, "GitHub folder link");
        var status = Kit.Text("", "small");
        var fromGitHub = Kit.Card(Kit.Column(8,
            Kit.Text("From a GitHub folder", "subtitle"),
            Kit.Text("Skills you add yourself are not reviewed by AGEX. AGEX pins the exact version you install and warns you if its files change later.", "small"),
            Kit.Row(8, url, Kit.Button("Add", async () =>
            {
                status.Text = "Downloading...";
                try { var skill = await Workspace.Core.Skills.AddFromGitHubAsync(url.Text ?? "", null, CancellationToken.None); status.Text = $"Added {skill.Manifest.Name}. It is off-limits for commands until you allow them."; Refresh(); }
                catch (SkillException ex) { status.Text = ex.Message; }
            }, "primary")), status));
        var fromFolder = Kit.Card(Kit.Column(8,
            Kit.Text("From a folder on this computer", "subtitle"),
            Kit.Text("The folder must contain a SKILL.md file. It is copied into AGEX.", "small"),
            Kit.Button("Choose folder...", async () =>
            {
                var folders = await Window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a skill folder" });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                try { var skill = Workspace.Core.Skills.AddFromFolder(path); Window.Toast("Skill added", skill.Manifest.Name, ToastKind.Success); Refresh(); }
                catch (SkillException ex) { await Window.Dialogs.MessageAsync("The skill was not added", ex.Message); }
            }, "", Icons.Folder)));
        var fromZip = Kit.Card(Kit.Column(8,
            Kit.Text("Import a skill package (.zip)", "subtitle"),
            Kit.Text("Packages are unpacked safely: no paths outside the skill, no links, 20 MB limit.", "small"),
            Kit.Button("Choose file...", async () =>
            {
                var files = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a skill package", FileTypeFilter = [new FilePickerFileType("Skill package") { Patterns = ["*.zip"] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                try { var skill = Workspace.Core.Skills.ImportPackage(path); Window.Toast("Skill added", skill.Manifest.Name, ToastKind.Success); Refresh(); }
                catch (SkillException ex) { await Window.Dialogs.MessageAsync("The package was not imported", ex.Message); }
            }, "", Icons.Download)));
        var name = new TextBox { PlaceholderText = "Name" };
        var command = new TextBox { PlaceholderText = "Command, e.g. npx" };
        var args = new TextBox { PlaceholderText = "Arguments, e.g. -y some-mcp-server@1.2.3" };
        var secretName = new TextBox { PlaceholderText = "Secret variable name (optional), e.g. API_KEY" };
        var secretValue = new TextBox { PlaceholderText = "Secret value (stored in the system keychain)", PasswordChar = '•' };
        foreach (var (box, label) in new[] { (name, "MCP server name"), (command, "Command"), (args, "Arguments"), (secretName, "Secret variable name"), (secretValue, "Secret value") }) AutomationProperties.SetName(box, label);
        var mcp = Kit.Card(Kit.Column(8,
            Kit.Text("Add an MCP server", "subtitle"),
            Kit.Text("For advanced users. The server runs with your account whenever Codex or Claude Code uses it. Pin a version in the arguments.", "small"),
            name, command, args, secretName, secretValue,
            Kit.Button("Add server", async () =>
            {
                try
                {
                    var secrets = new Dictionary<string, string>();
                    if (!string.IsNullOrWhiteSpace(secretName.Text) && !string.IsNullOrWhiteSpace(secretValue.Text)) secrets[secretName.Text.Trim()] = secretValue.Text.Trim();
                    var skill = Workspace.Core.Skills.AddMcpServer((name.Text ?? "").Trim(), (command.Text ?? "").Trim(), (args.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries), secrets);
                    secretValue.Text = "";
                    Window.Toast("MCP server added", skill.Manifest.Name + " (asks before each use)", ToastKind.Success);
                    Refresh();
                }
                catch (SkillException ex) { await Window.Dialogs.MessageAsync("The server was not added", ex.Message); }
            }, "primary", Icons.Plus)));
        return Kit.Column(12, fromGitHub, fromFolder, fromZip, mcp);
    }
}
