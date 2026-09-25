using System.Text.Json;
using Agex.Core.Runtime;

namespace Agex.Core.Connections;

/// <summary>A setting an MCP server needs (environment variable or header).</summary>
public sealed record RegistrySetting(string Name, string Description, bool Secret, bool Required);

/// <summary>One server from the public MCP Registry, reduced to what AGEX can start.</summary>
public sealed record RegistryServer
{
    public required string Name { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Version { get; init; } = "";
    public string Repository { get; init; } = "";
    /// <summary>"stdio" (npm or PyPI package) or "http" (hosted).</summary>
    public string Transport { get; init; } = "";
    public string Command { get; init; } = "";
    public IReadOnlyList<string> Args { get; init; } = [];
    public string Url { get; init; } = "";
    public IReadOnlyList<RegistrySetting> Settings { get; init; } = [];
    /// <summary>Why AGEX cannot add it automatically (another package type or header scheme), or empty.</summary>
    public string Unsupported { get; init; } = "";
    public string DisplayName => Title.Length > 0 ? Title : Name;
    /// <summary>Hosted servers that need a key other than a bearer token are not supported.</summary>
    public bool NeedsToken => Transport == "http" && Settings.Any(setting => setting.Required && setting.Secret);
}

/// <summary>
/// Search of the official public MCP Registry (registry.modelcontextprotocol.io).
/// Anyone can publish there, so AGEX labels every result "not reviewed" and adds
/// it only after the user confirms, with "ask each time" permissions.
/// </summary>
public sealed partial class McpRegistry(AgexLog? log = null)
{
    public const string BaseUrl = "https://registry.modelcontextprotocol.io/v0/servers";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<(IReadOnlyList<RegistryServer> Servers, string Error)> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return ([], "");
        try
        {
            var url = $"{BaseUrl}?search={Uri.EscapeDataString(query.Trim())}&limit=30&version=latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd($"AGEX/{AgexInfo.Version}");
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return ([], $"The MCP Registry answered {(int)response.StatusCode}.");
            return (Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)), "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log?.Error("mcp_registry_search_failed", ex);
            return ([], "The MCP Registry could not be reached. Check your internet connection.");
        }
    }

    internal static IReadOnlyList<RegistryServer> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var list = new List<RegistryServer>();
        if (!document.RootElement.TryGetProperty("servers", out var servers) || servers.ValueKind != JsonValueKind.Array) return list;
        foreach (var entry in servers.EnumerateArray())
        {
            var server = entry.TryGetProperty("server", out var inner) ? inner : entry;
            if (Str(server, "name") is not { Length: > 0 } name) continue;
            var item = new RegistryServer
            {
                Name = name, Title = Str(server, "title") ?? "", Description = Str(server, "description") ?? "", Version = Str(server, "version") ?? "",
                Repository = server.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.Object ? Str(repo, "url") ?? "" : "",
            };
            list.Add(Resolve(server, item));
        }
        return list;
    }

    private static RegistryServer Resolve(JsonElement server, RegistryServer item)
    {
        if (server.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Array)
        {
            foreach (var package in packages.EnumerateArray())
            {
                var type = Str(package, "registryType") ?? "";
                var id = Str(package, "identifier") ?? "";
                var version = Str(package, "version") ?? "";
                var transport = package.TryGetProperty("transport", out var t) && t.ValueKind == JsonValueKind.Object ? Str(t, "type") ?? "stdio" : "stdio";
                if (transport != "stdio" || !PackageId().IsMatch(id) || version.Length > 0 && !PackageVersion().IsMatch(version)) continue;
                var settings = Settings(package, "environmentVariables");
                var extra = package.TryGetProperty("packageArguments", out var args) && args.ValueKind == JsonValueKind.Array
                    ? args.EnumerateArray().Select(arg => Str(arg, "value") ?? Str(arg, "default")).OfType<string>().Where(value => value.IndexOfAny(['&', '|', ';', '<', '>', '`', '$']) < 0).ToList()
                    : [];
                if (type == "npm")
                    return item with { Transport = "stdio", Command = "npx", Args = ["-y", version.Length > 0 ? $"{id}@{version}" : id, .. extra], Settings = settings };
                if (type == "pypi")
                    return item with { Transport = "stdio", Command = "uvx", Args = [version.Length > 0 ? $"{id}=={version}" : id, .. extra], Settings = settings };
            }
        }
        if (server.TryGetProperty("remotes", out var remotes) && remotes.ValueKind == JsonValueKind.Array)
        {
            foreach (var remote in remotes.EnumerateArray())
            {
                var url = Str(remote, "url") ?? "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || url.Contains('{')) continue;
                var headers = Settings(remote, "headers");
                if (headers.Any(header => !header.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)))
                    return item with { Unsupported = "It needs a custom header, which AGEX cannot send yet." };
                return item with { Transport = "http", Url = url, Settings = headers };
            }
        }
        return item with { Unsupported = "It is published in a form AGEX cannot start (for example a container image or a desktop bundle)." };
    }

    private static IReadOnlyList<RegistrySetting> Settings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => Str(item, "name") is { Length: > 0 })
                .Select(item => new RegistrySetting(Str(item, "name")!, Str(item, "description") ?? "", Bool(item, "isSecret"), Bool(item, "isRequired"))).ToList()
            : [];

    private static string? Str(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>npm (optionally scoped) or PyPI package names only: nothing a shell could interpret.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^(@[a-z0-9][a-z0-9._-]*/)?[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial System.Text.RegularExpressions.Regex PackageId();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_-]*$")]
    private static partial System.Text.RegularExpressions.Regex PackageVersion();
}
