using Agex.Core.Agents;
using Agex.Core.Sessions;

namespace Agex.Tests;

public class UsageLedgerTests
{
    [Fact]
    public void Usage_is_recorded_for_multiple_agents_without_inventing_quota()
    {
        using var sandbox = new Sandbox("usage-ledger");
        var sessions = sandbox.Core().Sessions;
        var now = DateTimeOffset.Now;
        var first = new Session { CreatedAt = now.AddHours(-2), Agents = ["Codex", "Hermes"] };
        first.Usage["codex"] = new UsageReport { InputTokens = 100, OutputTokens = 20, Source = "Codex" };
        first.Usage["hermes"] = new UsageReport { InputTokens = 60, OutputTokens = 10, Source = "Hermes" };
        sessions.Save(first);
        var second = new Session { CreatedAt = now.AddMinutes(-2), Agents = ["Hermes"] };
        second.Runs.Add(new RunRecord { Agent = "Hermes", AgentId = "hermes", Provider = "Example", Model = "model-a" });
        sessions.Save(second);

        var codex = UsageLedger.For(sessions, "codex", "Codex", now: now);
        var hermes = UsageLedger.For(sessions, "hermes", "Hermes", second, now);
        Assert.Equal(100, codex.Today!.InputTokens);
        Assert.Equal(60, hermes.SevenDays!.InputTokens);
        Assert.Equal(2, hermes.RequestsToday);
        Assert.Equal(1, hermes.UnreportedRequestsSevenDays);
        Assert.Equal("Example", hermes.Provider);
        Assert.Equal("model-a", hermes.Model);
    }
}
