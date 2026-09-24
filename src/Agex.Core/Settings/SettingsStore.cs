using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Settings;

/// <summary>
/// Loads and saves settings, project profiles and crash-recovery state.
/// Settings carry a schema version; older files are migrated step by step
/// after a backup. A damaged file is kept aside and defaults are used, so a bad
/// file never stops AGEX from starting.
/// </summary>
public sealed class SettingsStore
{
    private readonly AppPaths _paths;
    private readonly AgexLog? _log;
    private readonly object _lock = new();

    public SettingsStore(AppPaths paths, AgexLog? log = null)
    {
        _paths = paths;
        _log = log;
    }

    /// <summary>Problems found while loading (shown in Diagnostics and fixed by Repair).</summary>
    public List<string> LoadIssues { get; } = [];

    public AgexSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_paths.Settings)) return Normalize(new AgexSettings());
            string text;
            try { text = File.ReadAllText(_paths.Settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LoadIssues.Add($"Settings could not be read ({ex.Message}); defaults are in use.");
                return Normalize(new AgexSettings());
            }
            try
            {
                var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                    ?? throw new JsonException("Settings file is not a JSON object.");
                var schema = node["schema_version"]?.GetValue<int>() ?? 0;
                if (schema > AgexSettings.CurrentSchema)
                {
                    LoadIssues.Add($"Settings were saved by a newer AGEX (schema {schema}). They are read as far as possible and not overwritten until you change something.");
                    return Normalize(node.Deserialize<AgexSettings>(Json.Options) ?? new AgexSettings());
                }
                if (schema < AgexSettings.CurrentSchema)
                {
                    Backup("before-migration");
                    var migrated = Migrations.Run(node, schema, _paths, _log);
                    var settings = Normalize(migrated);
                    Save(settings);
                    _log?.Write("settings_migrated", new { from = schema, to = AgexSettings.CurrentSchema });
                    return settings;
                }
                return Normalize(node.Deserialize<AgexSettings>(Json.Options) ?? new AgexSettings());
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                var aside = _paths.Settings + $".damaged-{DateTime.UtcNow:yyyyMMddHHmmss}";
                try { File.Copy(_paths.Settings, aside, overwrite: true); } catch (IOException) { }
                LoadIssues.Add($"Settings were damaged and have been reset. The old file was kept as {Path.GetFileName(aside)}.");
                _log?.Error("settings_damaged", ex);
                var fresh = Normalize(new AgexSettings { FirstRunComplete = true });
                Save(fresh);
                return fresh;
            }
        }
    }

    public void Save(AgexSettings settings)
    {
        lock (_lock)
        {
            settings.SchemaVersion = AgexSettings.CurrentSchema;
            Json.WriteFile(_paths.Settings, settings);
        }
    }

    /// <summary>Repairs invalid values without losing valid ones.</summary>
    public static AgexSettings Normalize(AgexSettings settings)
    {
        settings.TextScale = Math.Clamp(double.IsFinite(settings.TextScale) ? settings.TextScale : 1.0, 0.85, 1.6);
        settings.EnabledAgents = settings.EnabledAgents.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.ToLowerInvariant()).Distinct().ToList();
        settings.MaxParallelTasks = Math.Clamp(settings.MaxParallelTasks, 1, 8);
        settings.AgentTimeoutMinutes = Math.Clamp(settings.AgentTimeoutMinutes, 2, 120);
        settings.RecentProjects = settings.RecentProjects.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList();
        settings.Sessions ??= new SessionSettings();
        settings.Sessions.RetentionDays = Math.Max(0, settings.Sessions.RetentionDays);
        settings.Sessions.MaxSessions = Math.Clamp(settings.Sessions.MaxSessions, 10, 10000);
        settings.Approvals ??= new ApprovalPolicy();
        settings.Notifications ??= new NotificationSettings();
        settings.Privacy ??= new PrivacySettings();
        settings.AgentOptions ??= new();
        foreach (var id in new[] { "codex", "antigravity", "claude-code", "gemini-cli", "ollama" })
            if (!settings.AgentOptions.ContainsKey(id)) settings.AgentOptions[id] = new AgentOptions();
        settings.Teams ??= [];
        settings.Teams = settings.Teams.Where(team => !string.IsNullOrWhiteSpace(team.Id)).GroupBy(team => team.Id).Select(group => group.First()).ToList();
        settings.CustomShares ??= new();
        settings.QualityOrder ??= [];
        if (string.IsNullOrWhiteSpace(settings.Leader)) settings.Leader = "auto";
        return settings;
    }

    public void AddRecentProject(AgexSettings settings, string path)
    {
        settings.LastProject = path;
        settings.RecentProjects = new[] { path }.Concat(settings.RecentProjects.Where(item => !item.Equals(path, StringComparison.OrdinalIgnoreCase))).Take(15).ToList();
    }

    // ------------------------------------------------------------ projects

    private string ProfilePath(string projectPath)
    {
        var key = Path.GetFullPath(projectPath).TrimEnd('/', '\\');
        if (OperatingSystem.IsWindows()) key = key.ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return Path.Combine(_paths.Projects, hash + ".json");
    }

    public ProjectProfile LoadProject(string projectPath)
    {
        try
        {
            if (Json.ReadFile<ProjectProfile>(ProfilePath(projectPath)) is { } profile) return profile;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { _log?.Error("project_profile_damaged", ex); }
        return new ProjectProfile { Path = Path.GetFullPath(projectPath), Name = new DirectoryInfo(projectPath).Name };
    }

    public void SaveProject(ProjectProfile profile) => Json.WriteFile(ProfilePath(profile.Path), profile);

    public IReadOnlyList<ProjectProfile> AllProjects()
    {
        if (!Directory.Exists(_paths.Projects)) return [];
        var list = new List<ProjectProfile>();
        foreach (var file in Directory.GetFiles(_paths.Projects, "*.json"))
        {
            try { if (Json.ReadFile<ProjectProfile>(file) is { } profile) list.Add(profile); }
            catch (Exception ex) when (ex is JsonException or IOException) { }
        }
        return list.OrderByDescending(profile => profile.LastOpened).ToList();
    }

    public void RemoveProject(string projectPath)
    {
        var file = ProfilePath(projectPath);
        if (File.Exists(file)) File.Delete(file);
    }

    // --------------------------------------------------------------- state

    public AppState LoadState()
    {
        try { return Json.ReadFile<AppState>(_paths.State) ?? new AppState(); }
        catch (Exception ex) when (ex is JsonException or IOException) { return new AppState(); }
    }

    public void SaveState(AppState state)
    {
        try { Json.WriteFile(_paths.State, state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Error("state_save_failed", ex); }
    }

    // -------------------------------------------------- export and backup

    /// <summary>
    /// Settings, project profiles and the installed-skill list. Secrets are
    /// never included (they stay in the OS secure store).
    /// </summary>
    public JsonObject Export(bool includeProjects, IEnumerable<string>? installedSkills = null)
    {
        var export = new JsonObject
        {
            ["format"] = "agex-settings-export",
            ["format_version"] = 1,
            ["exported_at"] = DateTime.UtcNow.ToString("o"),
            ["settings"] = JsonSerializer.SerializeToNode(Load(), Json.Options),
        };
        if (includeProjects) export["projects"] = JsonSerializer.SerializeToNode(AllProjects(), Json.Options);
        if (installedSkills is not null) export["skills"] = JsonSerializer.SerializeToNode(installedSkills.ToList(), Json.Options);
        return export;
    }

    public void ExportTo(string file, bool includeProjects, IEnumerable<string>? installedSkills = null) =>
        File.WriteAllText(file, Export(includeProjects, installedSkills).ToJsonString(Json.Options), new UTF8Encoding(false));

    /// <summary>Imports an export file. Returns the skill ids it lists so the caller can offer to install them.</summary>
    public IReadOnlyList<string> Import(string file, bool includeProjects)
    {
        if (new FileInfo(file).Length > 5 * 1024 * 1024) throw new InvalidDataException("The file is too large to be an AGEX settings export.");
        var node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new InvalidDataException("Not an AGEX settings export.");
        if ((string?)node["format"] != "agex-settings-export") throw new InvalidDataException("Not an AGEX settings export.");
        Backup("before-import");
        var settings = node["settings"]?.Deserialize<AgexSettings>(Json.Options) ?? throw new InvalidDataException("The export has no settings.");
        Save(Normalize(settings));
        if (includeProjects && node["projects"] is JsonArray projects)
            foreach (var profile in projects.Deserialize<List<ProjectProfile>>(Json.Options) ?? [])
                if (!string.IsNullOrWhiteSpace(profile.Path)) SaveProject(profile);
        return node["skills"]?.Deserialize<List<string>>(Json.Options) ?? [];
    }

    /// <summary>Copies settings, project profiles and the skill list into backups/&lt;time&gt;-&lt;reason&gt;.</summary>
    public string? Backup(string reason)
    {
        try
        {
            var target = Path.Combine(_paths.Backups, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{reason}");
            Directory.CreateDirectory(target);
            if (File.Exists(_paths.Settings)) File.Copy(_paths.Settings, Path.Combine(target, "settings.json"), overwrite: true);
            if (Directory.Exists(_paths.Projects))
            {
                Directory.CreateDirectory(Path.Combine(target, "projects"));
                foreach (var file in Directory.GetFiles(_paths.Projects, "*.json")) File.Copy(file, Path.Combine(target, "projects", Path.GetFileName(file)), overwrite: true);
            }
            var installed = Path.Combine(_paths.Skills, "installed.json");
            if (File.Exists(installed)) File.Copy(installed, Path.Combine(target, "skills-installed.json"), overwrite: true);
            PruneBackups();
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Error("backup_failed", ex);
            return null;
        }
    }

    private void PruneBackups()
    {
        var old = new DirectoryInfo(_paths.Backups).GetDirectories().Where(dir => char.IsDigit(dir.Name[0])).OrderByDescending(dir => dir.Name).Skip(20);
        foreach (var dir in old) { try { dir.Delete(recursive: true); } catch (IOException) { } }
    }
}
