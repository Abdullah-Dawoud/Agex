using Agex.Core.Agents;

namespace Agex.Core.Sessions;

public enum UsagePeriod { Today, ThisWeek, ThisProject, All }

/// <summary>Token usage per agent over a set of requests, exactly as agents reported it.</summary>
public sealed record UsageTotals(int Requests, IReadOnlyDictionary<string, UsageReport> ByAgent, UsageReport? Total, int RequestsWithoutUsage);

/// <summary>
/// Usage history from the session index. Only figures the agents reported are
/// added up; cost appears only when an agent reported it (AGEX keeps no price list).
/// </summary>
public static class UsageHistory
{
    public static UsageTotals Summarize(IEnumerable<SessionSummary> sessions, UsagePeriod period, string? project, DateTimeOffset now, Func<string, SessionSummary?>? backfill = null)
    {
        var local = now.ToLocalTime();
        var startOfDay = new DateTimeOffset(local.Date, local.Offset);
        var startOfWeek = startOfDay.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        var byAgent = new Dictionary<string, UsageReport>(StringComparer.OrdinalIgnoreCase);
        var requests = 0;
        var without = 0;
        foreach (var item in sessions)
        {
            var at = item.CreatedAt.ToLocalTime();
            var inPeriod = period switch
            {
                UsagePeriod.Today => at >= startOfDay,
                UsagePeriod.ThisWeek => at >= startOfWeek,
                UsagePeriod.ThisProject => project is not null && SessionStore.SamePath(item.Project, project),
                _ => true,
            };
            if (!inPeriod) continue;
            var summary = item.Mode.Length == 0 && item.Usage.Count == 0 && backfill is not null ? backfill(item.Id) ?? item : item;
            requests++;
            if (summary.Usage.Count == 0) { without++; continue; }
            foreach (var (agent, usage) in summary.Usage)
                byAgent[agent] = UsageReport.Combine(byAgent.GetValueOrDefault(agent), usage)!;
        }
        UsageReport? total = null;
        foreach (var usage in byAgent.Values) total = UsageReport.Combine(total, usage);
        return new UsageTotals(requests, byAgent, total, without);
    }

    /// <summary>"564k", "6.3k", "912".</summary>
    public static string Tokens(long? value) => value switch
    {
        null => "-",
        >= 1_000_000 => $"{value / 1_000_000d:0.#}M",
        >= 10_000 => $"{value / 1000d:0}k",
        >= 1_000 => $"{value / 1000d:0.#}k",
        _ => value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>One line per agent for a request: input, output, cached and reasoning tokens, and reported cost.</summary>
    public static string Describe(UsageReport usage)
    {
        var parts = new List<string>();
        if (usage.InputTokens is not null) parts.Add($"{Tokens(usage.InputTokens)} input");
        if (usage.CachedInputTokens is > 0) parts.Add($"{Tokens(usage.CachedInputTokens)} of it cached");
        if (usage.OutputTokens is not null) parts.Add($"{Tokens(usage.OutputTokens)} output");
        if (usage.ReasoningTokens is > 0) parts.Add($"{Tokens(usage.ReasoningTokens)} reasoning");
        if (usage.CostUsd is { } cost) parts.Add($"${cost:0.####} reported cost");
        return parts.Count == 0 ? "not reported" : string.Join(" · ", parts);
    }
}
