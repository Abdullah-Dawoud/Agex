using System.Text.Json;

namespace Agex.Core.Agents;

/// <summary>One usage window as the provider reports it (for example "5 hours": 12% used, resets at 14:30).</summary>
public sealed record UsageWindow(string Label, double UsedPercent, DateTimeOffset? ResetsAt);

/// <summary>
/// Account usage or quota exactly as an agent reports it. AGEX never estimates
/// quota: when an agent does not report it, <see cref="Reported"/> is false.
/// </summary>
public sealed record AccountUsage
{
    public bool Reported { get; init; }
    public string Plan { get; init; } = "";
    public IReadOnlyList<UsageWindow> Windows { get; init; } = [];
    /// <summary>Where the numbers come from (for example "codex app-server").</summary>
    public string Source { get; init; } = "";
    public string Message { get; init; } = "";
    public DateTimeOffset RefreshedAt { get; init; } = DateTimeOffset.UtcNow;

    public static AccountUsage NotReported(string message = "Usage not reported by this agent.") => new() { Message = message };
}

/// <summary>Adapters whose agent reports account usage through a documented, quota-free call.</summary>
public interface IAccountUsageSource
{
    Task<AccountUsage> GetAccountUsageAsync(AgentDetection detection, CancellationToken cancellationToken);
}

public static class AccountUsageParser
{
    /// <summary>
    /// Parses the reply to Codex's app-server request "account/rateLimits/read".
    /// Only plan and usage windows are kept; account identifiers and e-mail are never read.
    /// </summary>
    public static AccountUsage FromCodex(JsonElement result)
    {
        var windows = new List<UsageWindow>();
        var plan = "";
        if (result.TryGetProperty("rateLimits", out var limits) && limits.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) continue;
                if (!window.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number) continue;
                var minutes = window.TryGetProperty("windowDurationMins", out var duration) && duration.ValueKind == JsonValueKind.Number ? duration.GetInt64() : 0;
                DateTimeOffset? resets = window.TryGetProperty("resetsAt", out var at) && at.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(at.GetInt64()) : null;
                windows.Add(new UsageWindow(WindowLabel(minutes, name), used.GetDouble(), resets));
            }
            if (limits.TryGetProperty("planType", out var type) && type.ValueKind == JsonValueKind.String) plan = type.GetString() ?? "";
        }
        return windows.Count == 0
            ? AccountUsage.NotReported("Codex did not report usage limits for this account.")
            : new AccountUsage { Reported = true, Plan = plan, Windows = windows, Source = "Codex (codex app-server, account/rateLimits/read)" };
    }

    private static string WindowLabel(long minutes, string fallback) => minutes switch
    {
        <= 0 => fallback == "primary" ? "Short window" : "Long window",
        10080 => "Weekly",
        < 60 => $"{minutes}-minute",
        _ when minutes % 1440 == 0 => $"{minutes / 1440}-day",
        _ when minutes % 60 == 0 => $"{minutes / 60}-hour",
        _ => $"{minutes}-minute",
    };
}
