using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Platform;

namespace Agex.Core.Connections;

/// <summary>
/// An MCP server that another program (Claude, Codex, Gemini, an editor) already
/// has in its own settings. Only names are kept for display: the values of
/// environment variables and headers stay in the source file until the user
/// explicitly imports them.
/// </summary>
public sealed record ExternalMcpServer
{
    public required string Source { get; init; }
    public required string ConfigPath { get; init; }
    public required string Name { get; init; }
    /// <summary>"stdio" or "http".</summary>
    public string Transport { get; init; } = "stdio";
    public string Command { get; init; } = "";
    public IReadOnlyList<string> Args { get; init; } = [];
    public string Url { get; init; } = "";
    public IReadOnlyList<string> EnvNames { get; init; } = [];
    public IReadOnlyList<string> HeaderNames { get; init; } = [];
    public string Key => Source + "|" + Name;
    public bool HasSettings => EnvNames.Count + HeaderNames.Count > 0;
    public string Summary => Transport == "http" ? Url : (Command + " " + string.Join(' ', Args)).Trim();
}

/// <summary>Finds MCP servers configured in other AI tools on this computer (read only; never changes those files).</summary>
public sealed partial class McpConfigScanner(IPlatformService platform)
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Config files per tool for this system, plus project-level files when a project is open.</summary>
    public IReadOnlyList<(string Source, string Path, string Format)> Locations(string? project)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = platform.Os switch
        {
            OsKind.Windows => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            OsKind.MacOS => Path.Combine(home, "Library", "Application Support"),
            _ => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".config"),
        };
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } codex ? codex : Path.Combine(home, ".codex");
        var list = new List<(string, string, string)>
        {
            ("Claude Code", Path.Combine(home, ".claude.json"), "mcpServers"),
            ("Claude Desktop", Path.Combine(appData, "Claude", "claude_desktop_config.json"), "mcpServers"),
            ("Codex", Path.Combine(codexHome, "config.toml"), "toml"),
            ("Gemini CLI", Path.Combine(home, ".gemini", "settings.json"), "mcpServers"),
            ("Antigravity", Path.Combine(home, ".gemini", "antigravity", "mcp_config.json"), "mcpServers"),
            ("VS Code", Path.Combine(appData, "Code", "User", "mcp.json"), "servers"),
            ("VS Code", Path.Combine(appData, "Code", "User", "settings.json"), "mcp.servers"),
            ("Cursor", Path.Combine(home, ".cursor", "mcp.json"), "mcpServers"),
            ("Windsurf", Path.Combine(home, ".codeium", "windsurf", "mcp_config.json"), "mcpServers"),
            ("OpenCode", Path.Combine(home, ".config", "opencode", "opencode.json"), "opencode"),
        };
        if (project is { Length: > 0 })
        {
            list.Add(("Project (.mcp.json)", Path.Combine(project, ".mcp.json"), "mcpServers"));
            list.Add(("Project (VS Code)", Path.Combine(project, ".vscode", "mcp.json"), "servers"));
            list.Add(("Project (Cursor)", Path.Combine(project, ".cursor", "mcp.json"), "mcpServers"));
            list.Add(("Project (Gemini CLI)", Path.Combine(project, ".gemini", "settings.json"), "mcpServers"));
        }
        return list;
    }

    public IReadOnlyList<ExternalMcpServer> Scan(string? project)
    {
        var found = new List<ExternalMcpServer>();
        foreach (var (source, path, format) in Locations(project))
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length is 0 or > 5_000_000) continue;
                var text = File.ReadAllText(path);
                if (format == "toml") found.AddRange(ParseCodexToml(text, source, path));
                else found.AddRange(ParseJson(text, source, path, format, project));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return found;
    }

    // ------------------------------------------------------------------ JSON

    internal static IEnumerable<ExternalMcpServer> ParseJson(string text, string source, string path, string format, string? project = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        using var document = JsonDocument.Parse(text, Lenient);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return [];
        var result = new List<ExternalMcpServer>();
        if (format == "opencode")
        {
            if (root.TryGetProperty("mcp", out var mcp) && mcp.ValueKind == JsonValueKind.Object)
                foreach (var server in mcp.EnumerateObject()) if (FromOpenCode(server, source, path) is { } item) result.Add(item);
            return result;
        }
        JsonElement servers = default;
        var ok = format switch
        {
            "servers" => root.TryGetProperty("servers", out servers),
            "mcp.servers" => root.TryGetProperty("mcp", out var mcp) && mcp.ValueKind == JsonValueKind.Object && mcp.TryGetProperty("servers", out servers),
            _ => root.TryGetProperty("mcpServers", out servers),
        };
        if (ok && servers.ValueKind == JsonValueKind.Object)
            foreach (var server in servers.EnumerateObject()) if (FromJson(server, source, path) is { } item) result.Add(item);
        // Claude Code keeps per-project servers under "projects".
        if (project is { Length: > 0 } && format == "mcpServers" && root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
            foreach (var entry in projects.EnumerateObject())
                if (SamePath(entry.Name, project) && entry.Value.TryGetProperty("mcpServers", out var own) && own.ValueKind == JsonValueKind.Object)
                    foreach (var server in own.EnumerateObject()) if (FromJson(server, source + " (this project)", path) is { } item) result.Add(item);
        return result;
    }

    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static ExternalMcpServer? FromJson(JsonProperty server, string source, string path)
    {
        if (server.Value.ValueKind != JsonValueKind.Object) return null;
        var value = server.Value;
        var url = Str(value, "url") ?? Str(value, "httpUrl") ?? Str(value, "serverUrl") ?? "";
        var command = Str(value, "command") ?? "";
        if (url.Length == 0 && command.Length == 0) return null;
        return new ExternalMcpServer
        {
            Source = source, ConfigPath = path, Name = server.Name,
            Transport = url.Length > 0 ? "http" : "stdio", Command = command, Url = url,
            Args = value.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array ? args.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList() : [],
            EnvNames = Names(value, "env"), HeaderNames = Names(value, "headers"),
        };
    }

    private static ExternalMcpServer? FromOpenCode(JsonProperty server, string source, string path)
    {
        var value = server.Value;
        if (value.ValueKind != JsonValueKind.Object) return null;
        if (Str(value, "type") == "remote" && Str(value, "url") is { Length: > 0 } url)
            return new ExternalMcpServer { Source = source, ConfigPath = path, Name = server.Name, Transport = "http", Url = url, HeaderNames = Names(value, "headers") };
        if (!value.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.Array) return null;
        var parts = command.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList();
        if (parts.Count == 0) return null;
        return new ExternalMcpServer { Source = source, ConfigPath = path, Name = server.Name, Command = parts[0], Args = parts.Skip(1).ToList(), EnvNames = Names(value, "environment") };
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> Names(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().Select(item => item.Name).ToList() : [];

    /// <summary>Reads the values of an external server's environment variables and headers (only when the user imports it).</summary>
    public static IReadOnlyDictionary<string, string> ReadSettings(ExternalMcpServer server)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var text = File.ReadAllText(server.ConfigPath);
            if (server.ConfigPath.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var table in TomlTables(text).Where(table => table.Name == $"mcp_servers.{server.Name}.env"))
                    foreach (var (key, raw) in table.Values) if (TomlString(raw) is { } value) values[key] = value;
                return values;
            }
            using var document = JsonDocument.Parse(text, Lenient);
            foreach (var element in Candidates(document.RootElement, server.Name))
                foreach (var group in new[] { "env", "headers", "environment" })
                    if (element.TryGetProperty(group, out var items) && items.ValueKind == JsonValueKind.Object)
                        foreach (var item in items.EnumerateObject()) if (item.Value.ValueKind == JsonValueKind.String) values[item.Name] = item.Value.GetString()!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return values;
    }

    private static IEnumerable<JsonElement> Candidates(JsonElement root, string name)
    {
        foreach (var path in new[] { new[] { "mcpServers" }, ["servers"], ["mcp", "servers"], ["mcp"] })
        {
            var current = root;
            var ok = true;
            foreach (var part in path) { if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) { ok = false; break; } }
            if (ok && current.ValueKind == JsonValueKind.Object && current.TryGetProperty(name, out var server) && server.ValueKind == JsonValueKind.Object) yield return server;
        }
    }

    // ------------------------------------------------------------------ TOML

    /// <summary>Codex's config.toml: [mcp_servers.NAME] with command, args, url and an [mcp_servers.NAME.env] table.</summary>
    internal static IEnumerable<ExternalMcpServer> ParseCodexToml(string text, string source, string path)
    {
        var tables = TomlTables(text).ToList();
        foreach (var table in tables.Where(table => table.Name.StartsWith("mcp_servers.", StringComparison.Ordinal) && table.Name.Count(ch => ch == '.') == 1))
        {
            var name = table.Name["mcp_servers.".Length..];
            var command = table.Values.TryGetValue("command", out var c) ? TomlString(c) ?? "" : "";
            var url = table.Values.TryGetValue("url", out var u) ? TomlString(u) ?? "" : "";
            if (command.Length == 0 && url.Length == 0) continue;
            var env = tables.FirstOrDefault(item => item.Name == $"mcp_servers.{name}.env");
            yield return new ExternalMcpServer
            {
                Source = source, ConfigPath = path, Name = name, Transport = url.Length > 0 ? "http" : "stdio", Command = command, Url = url,
                Args = table.Values.TryGetValue("args", out var a) ? TomlArray(a) : [],
                EnvNames = env?.Values.Keys.ToList() ?? [],
            };
        }
    }

    internal sealed record TomlTable(string Name, Dictionary<string, string> Values);

    private static IEnumerable<TomlTable> TomlTables(string text)
    {
        var current = new TomlTable("", new Dictionary<string, string>(StringComparer.Ordinal));
        string? pendingKey = null;
        var pending = "";
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (pendingKey is not null)
            {
                // Multi-line array: collect until the closing bracket.
                pending += " " + line;
                if (line.Contains(']')) { current.Values[pendingKey] = pending; pendingKey = null; }
                continue;
            }
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var header = TableHeader().Match(line);
            if (header.Success)
            {
                yield return current;
                var parts = HeaderPart().Matches(header.Groups["name"].Value).Select(match => match.Groups["q"].Success ? match.Groups["q"].Value : match.Groups["b"].Value.Trim());
                current = new TomlTable(string.Join('.', parts), new Dictionary<string, string>(StringComparer.Ordinal));
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var key = line[..equals].Trim().Trim('"', '\'');
            var value = line[(equals + 1)..].Trim();
            if (value.StartsWith('[') && !value.Contains(']')) { pendingKey = key; pending = value; continue; }
            current.Values[key] = value;
        }
        yield return current;
    }

    [GeneratedRegex(@"^\[(?<name>[^\[\]]+)\]\s*(#.*)?$")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"""(?<q>[^""]*)""|(?<b>[^.""]+)")]
    private static partial Regex HeaderPart();

    [GeneratedRegex(@"""((?:[^""\\]|\\.)*)""|'([^']*)'")]
    private static partial Regex TomlStringPattern();

    private static string? TomlString(string raw)
    {
        var match = TomlStringPattern().Match(raw);
        if (!match.Success) return null;
        return match.Groups[1].Success ? Regex.Unescape(match.Groups[1].Value) : match.Groups[2].Value;
    }

    private static IReadOnlyList<string> TomlArray(string raw) =>
        TomlStringPattern().Matches(raw).Select(match => match.Groups[1].Success ? Regex.Unescape(match.Groups[1].Value) : match.Groups[2].Value).ToList();
}
