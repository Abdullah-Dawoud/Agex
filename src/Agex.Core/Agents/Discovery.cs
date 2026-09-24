using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

public enum DiscoveredKind { Agent, LocalRuntime, Ide, Tool, Integration }

/// <summary>One row of the scan: an agent, IDE, tool or integration.</summary>
public sealed record DiscoveredItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DiscoveredKind Kind { get; init; }
    public string Provider { get; init; } = "";
    public AgentStatus Status { get; init; }
    public string Version { get; init; } = "";
    public string Location { get; init; } = "";
    public string Detail { get; init; } = "";
    /// <summary>True when AGEX has an adapter that can drive this item.</summary>
    public bool HasAdapter { get; init; }
    public string StatusLabel => AgentStatusText.Label(Status);
}

public sealed record DiscoveryResult(DateTimeOffset ScannedAt, IReadOnlyList<DiscoveredItem> Items)
{
    public IEnumerable<DiscoveredItem> Agents => Items.Where(item => item.Kind is DiscoveredKind.Agent or DiscoveredKind.LocalRuntime);
    public IEnumerable<DiscoveredItem> Ides => Items.Where(item => item.Kind == DiscoveredKind.Ide);
    public IEnumerable<DiscoveredItem> Tools => Items.Where(item => item.Kind is DiscoveredKind.Tool or DiscoveredKind.Integration);
}

/// <summary>
/// Allowlisted discovery. Checks known install locations and PATH for known
/// tool names; never crawls the disk and never runs a program that is not a
/// supported adapter (adapters get a bounded version check).
/// </summary>
public sealed class Discovery(IPlatformService platform, AgentRegistry registry, AgexLog? log = null)
{
    private sealed record Rule(string Id, string Name, DiscoveredKind Kind, string Provider, string[] Commands, OsKind[] Platforms);

    private static readonly OsKind[] All = [OsKind.Windows, OsKind.MacOS, OsKind.Linux];

    // Known tools AGEX recognises but does not drive (no reliable documented
    // non-interactive interface integrated yet).
    private static readonly Rule[] DetectOnly =
    [
        new("opencode", "OpenCode", DiscoveredKind.Agent, "OpenCode", ["opencode"], All),
        new("copilot-cli", "GitHub Copilot CLI", DiscoveredKind.Agent, "GitHub", ["copilot"], All),
        new("cursor-agent", "Cursor Agent CLI", DiscoveredKind.Agent, "Cursor", ["cursor-agent"], All),
        new("aider", "Aider", DiscoveredKind.Agent, "Aider", ["aider"], All),
        new("qwen-code", "Qwen Code", DiscoveredKind.Agent, "Alibaba", ["qwen"], All),
        new("vscode", "Visual Studio Code", DiscoveredKind.Ide, "Microsoft", ["code"], All),
        new("cursor", "Cursor", DiscoveredKind.Ide, "Cursor", ["cursor"], All),
        new("windsurf", "Windsurf", DiscoveredKind.Ide, "Codeium", ["windsurf"], All),
        new("antigravity-ide", "Antigravity IDE", DiscoveredKind.Ide, "Google", [], All),
        new("visual-studio", "Visual Studio", DiscoveredKind.Ide, "Microsoft", [], [OsKind.Windows]),
        new("jetbrains", "JetBrains IDE", DiscoveredKind.Ide, "JetBrains", [], All),
        new("zed", "Zed", DiscoveredKind.Ide, "Zed", ["zed"], [OsKind.MacOS, OsKind.Linux, OsKind.Windows]),
    ];

    private static readonly (string Id, string Name, string Command, string Detail)[] Tools =
    [
        ("git", "Git", "git", "Change tracking, diffs and snapshots."),
        ("node", "Node.js (npx)", "npx", "Needed by some skills (MCP servers published on npm)."),
        ("uv", "uv (uvx)", "uvx", "Needed by some skills (MCP servers published on PyPI)."),
        ("docker", "Docker", "docker", "Needed only by skills that run in containers."),
    ];

    /// <summary>Fast pass: file presence only, no programs run. Suitable while the UI is opening.</summary>
    public DiscoveryResult QuickScan()
    {
        var items = new List<DiscoveredItem>();
        foreach (var adapter in registry.Adapters)
        {
            var detection = registry.Detect(adapter.Id);
            items.Add(FromDetection(adapter, detection));
        }
        items.AddRange(ScanDetectOnly());
        items.AddRange(ScanTools());
        return new DiscoveryResult(DateTimeOffset.UtcNow, items);
    }

    /// <summary>Full pass: quick scan, then bounded health checks for supported adapters, reported item by item.</summary>
    public async Task<DiscoveryResult> FullScanAsync(IProgress<DiscoveredItem>? progress, CancellationToken cancellationToken)
    {
        var quick = QuickScan();
        foreach (var item in quick.Items) progress?.Report(item);
        var items = quick.Items.ToList();
        var checks = registry.Adapters.Select(async adapter =>
        {
            var detection = await registry.CheckHealthAsync(adapter.Id, cancellationToken).ConfigureAwait(false);
            var item = FromDetection(adapter, detection);
            lock (items)
            {
                var index = items.FindIndex(existing => existing.Id == item.Id);
                if (index >= 0) items[index] = item; else items.Add(item);
            }
            progress?.Report(item);
        });
        await Task.WhenAll(checks).ConfigureAwait(false);
        var result = new DiscoveryResult(DateTimeOffset.UtcNow, items);
        Save(result);
        log?.Write("discovery", new { ready = items.Count(item => item.Status == AgentStatus.Supported), detected = items.Count(item => item.Status == AgentStatus.DetectedUnsupported) });
        return result;
    }

    private DiscoveredItem FromDetection(IAgentAdapter adapter, AgentDetection detection) => new()
    {
        Id = adapter.Id, Name = adapter.Name, Provider = adapter.Provider, HasAdapter = true,
        Kind = adapter is OllamaAdapter ? DiscoveredKind.LocalRuntime : DiscoveredKind.Agent,
        Status = detection.Status, Version = detection.Version, Location = detection.Path ?? "",
        Detail = detection.Reason.Length > 0 ? detection.Reason : adapter.Description + (adapter.Stability == AdapterStability.Beta ? " (beta adapter)" : ""),
    };

    private IEnumerable<DiscoveredItem> ScanDetectOnly()
    {
        foreach (var rule in DetectOnly)
        {
            if (!rule.Platforms.Contains(platform.Os))
            {
                yield return new DiscoveredItem { Id = rule.Id, Name = rule.Name, Kind = rule.Kind, Provider = rule.Provider, Status = AgentStatus.PlatformUnsupported, Detail = $"{rule.Name} is not available for this operating system." };
                continue;
            }
            var path = ToolLocator.Locate(platform, rule.Id, rule.Commands);
            yield return path is null
                ? new DiscoveredItem { Id = rule.Id, Name = rule.Name, Kind = rule.Kind, Provider = rule.Provider, Status = AgentStatus.NotInstalled, Detail = "Not found." }
                : new DiscoveredItem
                {
                    Id = rule.Id, Name = rule.Name, Kind = rule.Kind, Provider = rule.Provider, Status = AgentStatus.DetectedUnsupported,
                    Location = path, Version = ToolLocator.ReadVersion(path),
                    Detail = rule.Kind == DiscoveredKind.Ide ? "Open projects in it from AGEX; AGEX does not control it." : "AGEX has no adapter for this tool yet.",
                };
        }
    }

    private IEnumerable<DiscoveredItem> ScanTools()
    {
        foreach (var (id, name, command, detail) in Tools)
        {
            var path = platform.FindExecutable(command);
            yield return new DiscoveredItem
            {
                Id = id, Name = name, Kind = DiscoveredKind.Tool, Status = path is null ? AgentStatus.NotInstalled : AgentStatus.Available,
                Location = path ?? "", Detail = detail,
            };
        }
        var mcp = McpConfigSummary.Read(platform);
        yield return new DiscoveredItem
        {
            Id = "mcp", Name = "MCP servers in other apps", Kind = DiscoveredKind.Integration,
            Status = mcp.Count > 0 ? AgentStatus.Available : AgentStatus.NotInstalled,
            Detail = mcp.Count > 0 ? $"{mcp.Count} configured ({string.Join(", ", mcp.Select(item => item.Source).Distinct())}). AGEX reads names only." : "None configured.",
        };
    }

    public void Save(DiscoveryResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(platform.Paths.Scan)!);
            File.WriteAllText(platform.Paths.Scan, JsonSerializer.Serialize(result, Json.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log?.Error("scan_save_failed", ex); }
    }

    public DiscoveryResult? LoadCached()
    {
        try { return File.Exists(platform.Paths.Scan) ? JsonSerializer.Deserialize<DiscoveryResult>(File.ReadAllText(platform.Paths.Scan), Json.Options) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }
}

/// <summary>Reads MCP server names (never commands or secrets) from other apps' configuration files.</summary>
public static class McpConfigSummary
{
    public sealed record Entry(string Name, string Source);

    public static IReadOnlyList<Entry> Read(IPlatformService platform)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var entries = new List<Entry>();
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } ch ? ch : Path.Combine(home, ".codex");
        var codexConfig = Path.Combine(codexHome, "config.toml");
        try
        {
            if (File.Exists(codexConfig))
                foreach (var line in File.ReadLines(codexConfig))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"^\s*\[mcp_servers\.([A-Za-z0-9_.\-]+)\]");
                    if (match.Success) entries.Add(new Entry(match.Groups[1].Value, "Codex"));
                }
        }
        catch (IOException) { }
        var claudeDesktop = platform.Os switch
        {
            OsKind.Windows => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"),
            OsKind.MacOS => Path.Combine(home, "Library", "Application Support", "Claude", "claude_desktop_config.json"),
            _ => Path.Combine(home, ".config", "Claude", "claude_desktop_config.json"),
        };
        var vscodeUser = platform.Os switch
        {
            OsKind.Windows => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "mcp.json"),
            OsKind.MacOS => Path.Combine(home, "Library", "Application Support", "Code", "User", "mcp.json"),
            _ => Path.Combine(home, ".config", "Code", "User", "mcp.json"),
        };
        foreach (var (file, key, source) in new[] { (claudeDesktop, "mcpServers", "Claude Desktop"), (vscodeUser, "servers", "VS Code"), (Path.Combine(home, ".cursor", "mcp.json"), "mcpServers", "Cursor"), (Path.Combine(home, ".gemini", "settings.json"), "mcpServers", "Gemini CLI") })
        {
            try
            {
                if (!File.Exists(file)) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (document.RootElement.TryGetProperty(key, out var section) && section.ValueKind == JsonValueKind.Object)
                    entries.AddRange(section.EnumerateObject().Select(property => new Entry(property.Name, source)));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return entries;
    }
}
