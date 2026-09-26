using System.Text.Json;

namespace AutodeskAIBridge.Core;

/// <summary>Safe AntiGravity MCP configuration helpers.</summary>
public static class AntiGravityConfigManager
{
    public const string ServerName = "autodesk-ai-bridge";

    public static IReadOnlyList<string> DetectCandidatePaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData) || !Directory.Exists(appData)) return Array.Empty<string>();

        var roots = Directory.EnumerateDirectories(appData, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) >= 0)
            .Concat(new[] { Path.Combine(appData, "AntiGravity IDE"), Path.Combine(appData, "AntiGravity") })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToArray();

        var results = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                results.AddRange(Directory.EnumerateFiles(root, "mcp_settings.json", SearchOption.AllDirectories)
                    .Where(path => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(part => part.Equals("settings", StringComparison.OrdinalIgnoreCase))));
                results.AddRange(Directory.EnumerateDirectories(root, "settings", SearchOption.AllDirectories)
                    .Where(path => path.IndexOf("globalStorage", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(path => Path.Combine(path, "mcp_settings.json"))
                    .Where(path => File.Exists(path) || Directory.Exists(Path.GetDirectoryName(path)!)));
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return results.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string? DetectDefaultPath() => DetectCandidatePaths().FirstOrDefault();

    public static string CreateSnippet(string command, IEnumerable<string>? args = null, IReadOnlyDictionary<string, string>? environment = null)
        => JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object?> { [ServerName] = new { command, args = args?.ToArray() ?? [], env = environment ?? new Dictionary<string, string>() } } }, new JsonSerializerOptions { WriteIndented = true });

    public static string Merge(string existingJson, string serverName, string command, IEnumerable<string>? args = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(existingJson) ? "{}" : existingJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("AntiGravity config root must be an object.");
        var values = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        Dictionary<string, JsonElement> servers;
        if (values.TryGetValue("mcpServers", out var current))
        {
            if (current.ValueKind != JsonValueKind.Object) throw new InvalidDataException("AntiGravity mcpServers must be an object.");
            servers = current.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }
        else servers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var entry = JsonSerializer.SerializeToElement(new { command, args = args?.ToArray() ?? [], env = environment ?? new Dictionary<string, string>() });
        servers[serverName] = entry;
        values["mcpServers"] = JsonSerializer.SerializeToElement(servers);
        return JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
    }

    public static bool Validate(string json, out string error)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) { error = "Root must be an object."; return false; }
            if (!document.RootElement.TryGetProperty("mcpServers", out var servers) || servers.ValueKind != JsonValueKind.Object) { error = "mcpServers object is required."; return false; }
            foreach (var server in servers.EnumerateObject())
            {
                if (server.Value.ValueKind != JsonValueKind.Object) { error = $"Server '{server.Name}' must be an object."; return false; }
                var hasCommand = server.Value.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(command.GetString());
                var hasUrl = server.Value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(url.GetString());
                if (!hasCommand && !hasUrl) { error = $"Server '{server.Name}' must define command or url."; return false; }
            }
            error = string.Empty; return true;
        }
        catch (JsonException exception) { error = exception.Message; return false; }
    }

    public static string Backup(string path, DateTimeOffset? now = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("AntiGravity config file not found.", path);
        var stamp = (now ?? DateTimeOffset.UtcNow).ToString("yyyyMMddHHmmssfff");
        var backup = path + ".bak." + stamp;
        File.Copy(path, backup, false);
        return backup;
    }

    public static string Configure(string path, string command, IEnumerable<string>? args = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("AntiGravity config path has no directory.");
        Directory.CreateDirectory(directory);
        var hadExisting = File.Exists(path);
        var existing = hadExisting ? File.ReadAllText(path) : "{}";
        var backup = hadExisting ? Backup(path) : string.Empty;
        var merged = Merge(existing, ServerName, command, args, environment);
        if (!Validate(merged, out var error)) throw new InvalidDataException($"Merged AntiGravity configuration is invalid: {error}");
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, merged, new System.Text.UTF8Encoding(false));
            using (var verify = JsonDocument.Parse(File.ReadAllText(temporary))) { }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            var reloaded = File.ReadAllText(path);
            if (!Validate(reloaded, out error) || !ContainsServer(reloaded, command))
                throw new InvalidDataException($"AntiGravity configuration verification failed: {error}");
            return backup;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (!string.IsNullOrWhiteSpace(backup) && File.Exists(backup)) File.Copy(backup, path, true);
            else if (!hadExisting && File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public static bool Remove(string path)
    {
        if (!File.Exists(path)) return false;
        var existing = File.ReadAllText(path);
        using var document = JsonDocument.Parse(existing);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("mcpServers", out var servers) || servers.ValueKind != JsonValueKind.Object)
            return false;
        var values = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        var remaining = servers.EnumerateObject().Where(p => !p.Name.Equals(ServerName, StringComparison.OrdinalIgnoreCase)).ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        if (remaining.Count == servers.EnumerateObject().Count()) return false;
        values["mcpServers"] = JsonSerializer.SerializeToElement(remaining);
        var updated = JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
        if (!Validate(updated, out _)) throw new InvalidDataException("Updated AntiGravity configuration is invalid.");
        var backup = Backup(path);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, updated, new System.Text.UTF8Encoding(false));
            File.Replace(temporary, path, null);
            return true;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            File.Copy(backup, path, true);
            throw;
        }
    }

    private static bool ContainsServer(string json, string command)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("mcpServers", out var servers)
            && servers.TryGetProperty(ServerName, out var server)
            && server.TryGetProperty("command", out var value)
            && string.Equals(value.GetString(), command, StringComparison.OrdinalIgnoreCase);
    }
}
