using Agex.Core.Skills;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Agex.Desktop.Pages;

/// <summary>
/// Skills: browse the curated catalog, install with one click (after a clear
/// look at what the skill may do), manage installed skills, update them, and
/// add your own.
/// </summary>
public sealed class SkillsPage(MainWindow window) : AppPage(window)
{
    private readonly WrapPanel _cards = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _installed = new() { Spacing = 10 };
    private readonly StackPanel _updates = new() { Spacing = 10 };
    private readonly WrapPanel _categories = new();
    private readonly TextBox _search = new() { PlaceholderText = "Search skills", MinWidth = 240 };
    private string _category = "Recommended";
    private readonly HashSet<string> _busy = [];
    private TabControl? _tabs;

    public override string Id => "skills";
    public override string Title => "Skills";
    public override string Icon => Icons.Skills;

    private static readonly string[] Categories = ["Recommended", "All", "Developer", "Testing", "Debugging", "Security", "Git & GitHub", "Web", "Research", "Documents", "Data", "Design", "DevOps", "Productivity", "Custom"];

    protected override Control Build()
    {
        AutomationProperties.SetName(_search, "Search skills");
        _search.TextChanged += (_, _) => RefreshCatalog();
        var discover = Kit.Column(12, Kit.Row(12, _search), _categories, _cards);
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
        var intro = Kit.Text("A skill gives agents extra know-how (instructions in the open SKILL.md format) or a tool (an MCP server). AGEX never runs skill code itself: agents use skills inside their own safety limits. Everything in the catalog is pinned to an exact version and checked before install.", "small");
        var page = Kit.Column(12, Kit.PageHeader("Skills", Window.Workspace.Core.SafeMode ? "Safe mode is on: installed skills are not used until you restart normally." : null), intro, _tabs);
        Refresh();
        return Kit.Page(page, 1200);
    }

    public override void OnShown() => Refresh();

    private void Refresh()
    {
        _categories.Children.Clear();
        foreach (var category in Categories)
        {
            var button = Kit.Button(category, () => { _category = category; Refresh(); }, category == _category ? "primary" : "");
            button.Margin = new Thickness(0, 0, 6, 6);
            _categories.Children.Add(button);
        }
        RefreshCatalog();
        RefreshInstalled();
        RefreshUpdates();
    }

    private void RefreshCatalog()
    {
        _cards.Children.Clear();
        var installed = Workspace.Core.Skills.Installed().ToDictionary(skill => skill.Id);
        var query = _search.Text?.Trim() ?? "";
        var skills = Workspace.Core.Skills.Catalog().Skills
            .Where(skill => _category switch { "All" => true, "Recommended" => skill.Recommended, _ => skill.Categories.Contains(_category) })
            .Where(skill => query.Length == 0 || skill.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || skill.Description.Contains(query, StringComparison.OrdinalIgnoreCase) || skill.Author.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (_category == "Custom") { _cards.Children.Add(Kit.Text("Your own skills are listed under Installed. Use 'Add your own' to add one.", "small")); return; }
        if (skills.Count == 0) { _cards.Children.Add(Kit.Text("No skills match.", "small")); return; }
        foreach (var skill in skills) _cards.Children.Add(Card(skill, installed.GetValueOrDefault(skill.Id)));
    }

    private Control Card(SkillManifest skill, InstalledSkill? installed)
    {
        var compatibility = Workspace.Core.Skills.CheckCompatibility(skill, Workspace.Settings.EnabledAgents);
        var busy = _busy.Contains(skill.Id);
        Control action = installed is not null
            ? Kit.Badge(installed.Enabled ? "Installed" : "Installed (off)", installed.Enabled ? Tone.Success : Tone.Neutral)
            : Kit.Button(busy ? "Installing..." : "Install", () => _ = InstallAsync(skill), "primary", Icons.Download);
        if (action is Button button) { button.IsEnabled = !busy && compatibility.Installable; AutomationProperties.SetName(button, $"Install {skill.Name}"); }
        var body = Kit.Column(8,
            Kit.Row(8, Kit.Icon(skill.Kind == SkillKind.Mcp ? Icons.Tool : Icons.Skills, 18, "AccentBrush"), Kit.Text(skill.Name, "subtitle")),
            Kit.Text($"{skill.Author} · {skill.License} · {(skill.Kind == SkillKind.Mcp ? "Tool (MCP) · " : "")}{(skill.Version == "remote" ? "hosted service" : "v" + skill.Version)}", "caption"),
            Kit.Text(skill.Description, "small"),
            Kit.Wrap(skill.Permissions.Select(permission => (Control?)Kit.Badge(SkillText.Permission(permission), SkillText.IsHighRisk(permission) ? Tone.Warning : Tone.Neutral)).ToArray()),
            Kit.Badge(SkillText.Trust(skill.Trust), skill.Trust is SkillTrust.Curated or SkillTrust.Verified ? Tone.Success : Tone.Warning, Icons.Shield).Left(),
            Kit.Text("Works with: " + string.Join(", ", skill.SupportedAgents.Select(id => Workspace.Core.Registry.Get(id)?.Name ?? id)), "caption"),
            skill.Popularity is { } popularity ? Kit.Text($"{popularity.Label}: {popularity.Value:N0} ({popularity.AsOf})", "caption") : null,
            compatibility.Problems.Count > 0 ? Kit.Text(string.Join(" ", compatibility.Problems), "small") : null,
            compatibility.Warnings.Count > 0 ? Kit.Text(string.Join(" ", compatibility.Warnings), "caption") : null,
            action);
        var card = Kit.Card(body);
        card.Width = 340;
        card.Margin = new Thickness(0, 0, 12, 12);
        return card;
    }

    private async Task InstallAsync(SkillManifest skill)
    {
        var compatibility = Workspace.Core.Skills.CheckCompatibility(skill, Workspace.Settings.EnabledAgents);
        var choices = new Dictionary<SkillPermission, PermissionChoice>();
        var list = Kit.Column(8);
        foreach (var permission in skill.Permissions)
        {
            var choice = PermissionChoice.AlwaysAllow;
            choices[permission] = choice;
            var captured = permission;
            var combo = Kit.Combo([(PermissionChoice.AlwaysAllow, "Always allow"), (PermissionChoice.AskEachTime, "Ask each time"), (PermissionChoice.Deny, "Don't allow")], choice, value => choices[captured] = value, 170);
            list.Children.Add(Kit.SettingRow(SkillText.Permission(permission), SkillText.IsHighRisk(permission) ? "Higher risk" : null, combo));
        }
        var secretBoxes = new Dictionary<string, TextBox>();
        foreach (var secret in skill.Mcp?.SecretEnv ?? [])
        {
            var box = new TextBox { PasswordChar = '•', PlaceholderText = secret, MinWidth = 260 };
            AutomationProperties.SetName(box, secret);
            secretBoxes[secret] = box;
            list.Children.Add(Kit.SettingRow(secret == "GITHUB_PERSONAL_ACCESS_TOKEN" ? "GitHub token" : secret, "Stored in " + Workspace.Core.Platform.SecureStore.Mechanism + ". Never logged or shown again.", box));
        }
        var body = Kit.Column(10,
            Kit.Text(skill.Description, "body"),
            Kit.Text($"From {skill.Author} ({SkillText.Trust(skill.Trust)}), licence {skill.License}. " + (skill.Kind == SkillKind.Instructions ? $"Pinned to commit {skill.Source?.Commit[..7]}; every file is checked against its SHA-256 before install." : $"Runs '{skill.Mcp?.Command} {string.Join(' ', skill.Mcp?.Args ?? [])}'{(skill.Mcp?.Url is { Length: > 0 } url ? " / " + url : "")} when an agent uses it."), "small"),
            skill.CompatibilityNote.Length > 0 ? Kit.Text(skill.CompatibilityNote, "small") : null,
            compatibility.Warnings.Count > 0 ? Kit.Text(string.Join(" ", compatibility.Warnings), "small") : null,
            Kit.Text("What this skill may do", "body"),
            list);
        if (await Window.Dialogs.ShowAsync($"Install {skill.Name}?", body, ["Install", "Cancel"]) != 0) return;
        _busy.Add(skill.Id);
        RefreshCatalog();
        try
        {
            await Workspace.Core.Skills.InstallAsync(skill, choices, null, CancellationToken.None);
            foreach (var (name, box) in secretBoxes)
                if (!string.IsNullOrWhiteSpace(box.Text)) Workspace.Core.Skills.SetSecret(skill.Id, name, box.Text.Trim());
            Window.Toast("Skill installed", $"{skill.Name} is ready and enabled.", ToastKind.Success);
        }
        catch (SkillException ex) { await Window.Dialogs.MessageAsync("The skill was not installed", ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Window.Dialogs.MessageAsync("The skill was not installed", "AGEX could not write the skill files: " + ex.Message); }
        finally
        {
            _busy.Remove(skill.Id);
            Refresh();
        }
    }

    private void RefreshInstalled()
    {
        _installed.Children.Clear();
        var skills = Workspace.Core.Skills.Installed();
        if (skills.Count == 0) { _installed.Children.Add(Kit.EmptyState(Icons.Skills, "No skills installed", "Browse Discover and install what you need.")); return; }
        foreach (var skill in skills)
        {
            var id = skill.Id;
            var toggle = new ToggleSwitch { IsChecked = skill.Enabled, OnContent = "On", OffContent = "Off" };
            AutomationProperties.SetName(toggle, $"Enable {skill.Manifest.Name}");
            toggle.IsCheckedChanged += (_, _) => { Workspace.Core.Skills.SetEnabled(id, toggle.IsChecked == true); RefreshInstalled(); };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(Kit.Column(2,
                Kit.Row(8, Kit.Text(skill.Manifest.Name, "subtitle"), Kit.Badge(SkillText.Trust(skill.Manifest.Trust), skill.Manifest.Trust is SkillTrust.Curated or SkillTrust.Verified ? Tone.Success : Tone.Warning, Icons.Shield)),
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
            var body = Kit.Column(8, header,
                skill.DisabledReason.Length > 0 ? Kit.Badge(skill.DisabledReason, Tone.Danger, Icons.Alert) : null,
                Workspace.Core.Skills.MissingSecret(skill) ? Kit.Row(8, Kit.Badge("Needs a token before agents can use it", Tone.Warning), Kit.Button("Set token", () => _ = SetSecretAsync(skill), "link")) : null,
                Kit.Text(skill.Manifest.Description, "small"),
                new Expander { Header = "Permissions", Content = permissions, HorizontalAlignment = HorizontalAlignment.Stretch },
                Kit.Row(8,
                    skill.Folder.Length > 0 ? Kit.Button($"Show in {Workspace.Core.Platform.FileManagerName}", () => Workspace.Core.Platform.RevealInFileManager(skill.Folder), "subtle", Icons.External) : null,
                    skill.Manifest.Homepage.StartsWith("https://", StringComparison.Ordinal) ? Kit.Button("Source", () => Workspace.Core.Platform.OpenUrl(new Uri(skill.Manifest.Homepage)), "subtle", Icons.External) : null,
                    Kit.Button("Remove", async () =>
                    {
                        if (!await Window.ConfirmAsync($"Remove {skill.Manifest.Name}?", "Its files and any token saved for it are deleted.", "Remove", "Cancel")) return;
                        Workspace.Core.Skills.Remove(id);
                        Refresh();
                    }, "subtle danger", Icons.Trash)));
            _installed.Children.Add(Kit.Card(body));
        }
    }

    private async Task SetSecretAsync(InstalledSkill skill)
    {
        var boxes = (skill.Manifest.Mcp?.SecretEnv ?? []).ToDictionary(name => name, name => new TextBox { PasswordChar = '•', PlaceholderText = name, MinWidth = 280 });
        var body = Kit.Column(8, [Kit.Text("Saved in " + Workspace.Core.Platform.SecureStore.Mechanism + (Workspace.Core.Platform.SecureStore.IsOsProtected ? "." : ". Warning: this system has no keyring, so the file is private but not encrypted."), "small"), .. boxes.Select(pair => Kit.SettingRow(pair.Key, null, pair.Value))]);
        if (await Window.Dialogs.ShowAsync($"Token for {skill.Manifest.Name}", body, ["Save", "Cancel"]) != 0) return;
        foreach (var (name, box) in boxes) if (!string.IsNullOrWhiteSpace(box.Text)) Workspace.Core.Skills.SetSecret(skill.Id, name, box.Text.Trim());
        RefreshInstalled();
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

    private Control BuildAddOwn()
    {
        var url = new TextBox { PlaceholderText = "https://github.com/owner/repo/tree/main/skills/my-skill", MinWidth = 420 };
        AutomationProperties.SetName(url, "GitHub folder link");
        var status = Kit.Text("", "small");
        var fromGitHub = Kit.Card(Kit.Column(8,
            Kit.Text("From a GitHub folder", "subtitle"),
            Kit.Text("Community skills are not reviewed by AGEX. AGEX pins the exact version you install and warns you if its files change later.", "small"),
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
