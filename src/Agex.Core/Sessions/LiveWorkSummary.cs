using Agex.Core.Agents;

namespace Agex.Core.Sessions;

/// <summary>Counts from structured agent and session events; never asks an agent to narrate work.</summary>
public sealed record LiveWorkSummary(int ChangedFiles, int AddedLines, int RemovedLines, int Commands, int Tools,
    int BrowserActions, int Tests, int Retries)
{
    public static LiveWorkSummary From(Session session, IEnumerable<AgentActivity> activities, IEnumerable<TimelineEntry> timeline)
    {
        var started = activities.Where(activity => activity.Kind == ActivityKind.ToolStarted).ToList();
        return new LiveWorkSummary(session.Changes.Count, session.Changes.Sum(change => change.Added ?? 0),
            session.Changes.Sum(change => change.Removed ?? 0),
            started.Count(activity => activity.Surface == AgentSurface.Terminal),
            started.Count(activity => activity.Surface == AgentSurface.Activity),
            started.Count(activity => activity.Surface == AgentSurface.Browser),
            started.Count(IsTestActivity),
            timeline.Count(entry => entry.Kind == TimelineKind.Fallback
                || entry.Kind == TimelineKind.Warning && entry.Text.Contains("retry", StringComparison.OrdinalIgnoreCase)));
    }

    public static bool IsTestActivity(AgentActivity activity) => activity.Kind == ActivityKind.ToolStarted
        && activity.Surface == AgentSurface.Terminal
        && (activity.Text.Contains(" test", StringComparison.OrdinalIgnoreCase)
            || activity.Text.Contains("pytest", StringComparison.OrdinalIgnoreCase)
            || activity.Text.Contains("verify", StringComparison.OrdinalIgnoreCase));
}
