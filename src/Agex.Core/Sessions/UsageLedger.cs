using Agex.Core.Agents;

namespace Agex.Core.Sessions;

/// <summary>Locally recorded request usage. Null token counts mean the agent did not report them.</summary>
public sealed record AgentUsageSnapshot(UsageReport? Current, UsageReport? Today, UsageReport? SevenDays,
    int RequestsToday, int RequestsSevenDays, int UnreportedRequestsSevenDays, string Provider, string Model);

public static class UsageLedger
{
    public static AgentUsageSnapshot For(SessionStore sessions, string agentId, string agentName, Session? current = null, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        var today = at.LocalDateTime.Date;
        var week = at.AddDays(-7);
        UsageReport? todayUsage = null, weekUsage = null;
        var todayCount = 0;
        var weekCount = 0;
        var unknown = 0;
        var foundCurrent = false;
        var summaries = sessions.List();
        foreach (var summary in summaries)
        {
            if (summary.CreatedAt < week || !summary.Agents.Contains(agentName, StringComparer.OrdinalIgnoreCase)
                && !summary.Usage.ContainsKey(agentId)) continue;
            if (current?.Id == summary.Id) foundCurrent = true;
            Count(summary.CreatedAt, summary.Usage.GetValueOrDefault(agentId));
        }
        if (current is not null && !foundCurrent) Count(current.CreatedAt, current.Usage.GetValueOrDefault(agentId));
        var latest = current?.Runs.LastOrDefault(run => run.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase));
        return new(current?.Usage.GetValueOrDefault(agentId), todayUsage, weekUsage, todayCount, weekCount, unknown,
            latest?.Provider ?? "", latest?.Model ?? "");

        void Count(DateTimeOffset when, UsageReport? report)
        {
            if (when < week) return;
            weekCount++;
            weekUsage = UsageReport.Combine(weekUsage, report);
            if (report is null || report.InputTokens is null && report.OutputTokens is null) unknown++;
            if (when.LocalDateTime.Date != today) return;
            todayCount++;
            todayUsage = UsageReport.Combine(todayUsage, report);
        }
    }
}
