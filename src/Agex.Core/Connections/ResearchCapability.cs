using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Core.Settings;

namespace Agex.Core.Connections;

/// <summary>AGEX's no-account search and fetch fallback for any enabled agent.</summary>
public static class ResearchCapability
{
    public const string Endpoint = "https://mcp.exa.ai/mcp";

    public static IReadOnlyList<McpServerSpec> AddDefault(RequestIntent intent, PermissionSettings permissions, IReadOnlyList<McpServerSpec> selected)
    {
        if (!intent.Has(NeededCapability.ExternalNetwork) || !permissions.Network || !permissions.McpTools
            || selected.Any(server => server.Url?.TrimEnd('/').Equals(Endpoint, StringComparison.OrdinalIgnoreCase) == true))
            return selected;
        return [.. selected, new McpServerSpec("agex_web", "", [], new Dictionary<string, string>(), Endpoint)];
    }
}
