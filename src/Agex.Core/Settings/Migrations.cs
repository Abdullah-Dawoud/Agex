using System.Text.Json;
using System.Text.Json.Nodes;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Settings;

/// <summary>
/// Settings schema migrations. Each step upgrades exactly one version so a
/// file from any older release reaches the current schema. Add a new step for
/// every schema change; never edit an old step.
/// </summary>
public static class Migrations
{
    public static AgexSettings Run(JsonObject node, int fromSchema, AppPaths paths, AgexLog? log)
    {
        var schema = fromSchema;
        if (schema < 4) { node = FromPowerShellEdition(node, paths, log); schema = 4; }
        // Future: if (schema < 5) { node = V4ToV5(node); schema = 5; }
        return node.Deserialize<AgexSettings>(Json.Options) ?? new AgexSettings();
    }

    /// <summary>
    /// Schemas 1 to 3 were written by the Windows PowerShell edition of AGEX
    /// (flat keys: leader, codex_share, codex_model, codex_task_sandbox, ...).
    /// Its project list (projects.json) becomes per-project profiles.
    /// </summary>
    internal static JsonObject FromPowerShellEdition(JsonObject old, AppPaths paths, AgexLog? log)
    {
        string S(string key) => old[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        int? I(string key) => old[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;
        bool? B(string key) => old[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
        List<string> L(string key) => old[key] is JsonArray array ? array.Select(item => item?.ToString() ?? "").Where(item => item.Length > 0).ToList() : [];

        var settings = new AgexSettings
        {
            FirstRunComplete = B("first_run_complete") ?? S("last_project").Length > 0,
            LastProject = S("last_project"),
            RecentProjects = L("recent_projects"),
            Leader = S("leader").ToLowerInvariant() switch { "antigravity" => "antigravity", "codex" => "codex", _ => "auto" },
            Theme = S("theme").ToLowerInvariant() switch { "dark" => ThemeChoice.Dark, "light" => ThemeChoice.Light, _ => ThemeChoice.System },
            CheckForUpdates = B("check_updates") ?? true,
            StartWithSystem = B("start_with_windows") ?? false,
            LaunchMinimized = B("launch_minimized") ?? false,
        };
        if (settings.LastProject.Length > 0 && !settings.RecentProjects.Contains(settings.LastProject)) settings.RecentProjects.Insert(0, settings.LastProject);
        var enabled = L("enabled_agents");
        if (enabled.Count > 0) settings.EnabledAgents = enabled.Select(item => item.ToLowerInvariant()).ToList();
        if (I("max_sessions") is { } max) settings.Sessions.MaxSessions = max;
        var timeout = Math.Max(I("codex_timeout_seconds") ?? 0, I("antigravity_timeout_seconds") ?? 0);
        if (timeout > 0) settings.AgentTimeoutMinutes = (int)Math.Ceiling(timeout / 60.0);

        var strategy = S("strategy");
        var codexShare = I("codex_share");
        if (strategy.Equals("Balanced", StringComparison.OrdinalIgnoreCase)) settings.Routing = RoutingPreset.Balanced;
        else if (codexShare is { } share)
        {
            var codex = strategy switch { "Coding-heavy" => 20, "Research-heavy" => 70, _ => Math.Clamp(share, 0, 100) };
            settings.Routing = RoutingPreset.Custom;
            settings.CustomShares = new() { ["codex"] = codex, ["antigravity"] = 100 - codex };
        }

        settings.AgentOptions["codex"] = new AgentOptions
        {
            Model = S("codex_model"), Effort = S("codex_effort"),
            // The old default kept Codex read-only unless the user chose workspace-write.
            AllowWrites = S("codex_task_sandbox").Equals("workspace-write", StringComparison.OrdinalIgnoreCase),
        };
        settings.AgentOptions["antigravity"] = new AgentOptions { Model = S("antigravity_model"), Effort = S("antigravity_effort") };

        ImportProjectList(paths, log);
        return (JsonNode.Parse(JsonSerializer.Serialize(settings, Json.Options)) as JsonObject)!;
    }

    private static void ImportProjectList(AppPaths paths, AgexLog? log)
    {
        var file = Path.Combine(paths.DataRoot, "projects.json");
        if (!File.Exists(file)) return;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(file))?["projects"] is not JsonArray projects) return;
            var store = new SettingsStore(paths, log);
            foreach (var item in projects)
            {
                var path = item?["path"]?.ToString();
                if (string.IsNullOrWhiteSpace(path)) continue;
                var profile = store.LoadProject(path);
                if (item?["name"]?.ToString() is { Length: > 0 } name) profile.Name = name;
                store.SaveProject(profile);
            }
            File.Move(file, Path.Combine(paths.DataRoot, "projects.v1-imported.json"), overwrite: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { log?.Error("project_list_import_failed", ex); }
    }
}
