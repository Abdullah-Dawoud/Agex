using Agex.Core.Agents;
using Agex.Core.Sessions;

namespace Agex.Tests;

public class LiveWorkSummaryTests
{
    [Fact]
    public void Counts_real_events_once_and_keeps_file_line_totals()
    {
        var session = new Session { Changes = [new FileChange { Path = "a.cs", Added = 446, Removed = 73 }] };
        var events = new[]
        {
            new AgentActivity(ActivityKind.ToolStarted, "Running dotnet test") { Surface = AgentSurface.Terminal },
            new AgentActivity(ActivityKind.Output, "Passed 2") { Surface = AgentSurface.Terminal },
            new AgentActivity(ActivityKind.ToolFinished, "Command finished") { Surface = AgentSurface.Terminal },
            new AgentActivity(ActivityKind.ToolStarted, "Reading file"),
            new AgentActivity(ActivityKind.ToolStarted, "Browser click") { Surface = AgentSurface.Browser },
        };
        var timeline = new[] { new TimelineEntry { Kind = TimelineKind.Warning, Text = "retrying once" } };
        var summary = LiveWorkSummary.From(session, events, timeline);
        Assert.Equal((1, 446, 73, 1, 1, 1, 1, 1),
            (summary.ChangedFiles, summary.AddedLines, summary.RemovedLines, summary.Commands,
                summary.Tools, summary.BrowserActions, summary.Tests, summary.Retries));
    }
}
