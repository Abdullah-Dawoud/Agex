using System.Text.Json.Nodes;
using Agex.Core.Runtime;

namespace Agex.Core.Updates;

/// <summary>
/// The installers write install.json next to the program. Version 2.0.0
/// installers recorded the repository under its earlier name; this one-time
/// migration rewrites it to the current name and records the old value.
/// </summary>
public static class InstallMarker
{
    public const string FileName = "install.json";

    /// <summary>Returns true when the marker was migrated. Never throws: a read-only install folder is left as it is.</summary>
    public static bool MigrateRepository(string installFolder, AgexLog? log = null)
    {
        var path = Path.Combine(installFolder, FileName);
        try
        {
            if (!File.Exists(path)) return false;
            if (JsonNode.Parse(File.ReadAllText(path).TrimStart('﻿')) is not JsonObject marker) return false;
            var repository = marker["repository"]?.GetValue<string>() ?? "";
            if (!AgexInfo.LegacyRepositories.Contains(repository, StringComparer.OrdinalIgnoreCase)) return false;
            marker["repository"] = AgexInfo.Repository;
            marker["migrated_from_repository"] = repository;
            if (marker["source"]?.GetValue<string>() is { } source && source.Contains(repository, StringComparison.OrdinalIgnoreCase))
                marker["source"] = source.Replace(repository, AgexInfo.Repository, StringComparison.OrdinalIgnoreCase);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, marker.ToJsonString(Json.Options));
            File.Move(temporary, path, overwrite: true);
            log?.Write("install_marker_migrated", new { from = repository, to = AgexInfo.Repository });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            log?.Error("install_marker_migration_failed", ex);
            return false;
        }
    }
}
