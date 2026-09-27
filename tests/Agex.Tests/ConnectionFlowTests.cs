using Agex.Core.Connections;

namespace Agex.Tests;

public class ConnectionFlowTests
{
    [Fact]
    public async Task Sign_in_waits_then_updates_to_connected()
    {
        var states = new List<ConnectionFlowState>();
        var checks = 0;
        var connected = await ConnectionFlow.RunAsync(() => true, _ => Task.FromResult(++checks == 2), states.Add,
            CancellationToken.None, attempts: 3, interval: TimeSpan.Zero);
        Assert.True(connected);
        Assert.Equal([ConnectionFlowState.Connecting, ConnectionFlowState.WaitingForSignIn, ConnectionFlowState.Connected], states);
    }

    [Fact]
    public async Task Blocked_sign_in_surface_reports_failed()
    {
        var states = new List<ConnectionFlowState>();
        var checkedAccount = false;
        var connected = await ConnectionFlow.RunAsync(() => false, _ => { checkedAccount = true; return Task.FromResult(true); },
            states.Add, CancellationToken.None);
        Assert.False(connected);
        Assert.False(checkedAccount);
        Assert.Equal([ConnectionFlowState.Connecting, ConnectionFlowState.Failed], states);
    }

    [Fact]
    public async Task Cancelled_sign_in_stops_checking()
    {
        using var cancel = new CancellationTokenSource();
        var states = new List<ConnectionFlowState>();
        var connected = await ConnectionFlow.RunAsync(() => true, _ => { cancel.Cancel(); return Task.FromResult(false); }, states.Add,
            cancel.Token, interval: TimeSpan.Zero);
        Assert.False(connected);
        Assert.Equal([ConnectionFlowState.Connecting, ConnectionFlowState.WaitingForSignIn], states);
    }

    [Fact]
    public async Task Failed_and_unfinished_sign_in_have_distinct_states()
    {
        var failed = new List<ConnectionFlowState>();
        Assert.False(await ConnectionFlow.RunAsync(() => true, _ => throw new IOException("Provider failed"), failed.Add,
            CancellationToken.None, interval: TimeSpan.Zero));
        Assert.Equal(ConnectionFlowState.Failed, failed.Last());

        var unfinished = new List<ConnectionFlowState>();
        Assert.False(await ConnectionFlow.RunAsync(() => true, _ => Task.FromResult(false), unfinished.Add,
            CancellationToken.None, attempts: 1, interval: TimeSpan.Zero));
        Assert.Equal(ConnectionFlowState.NeedsAttention, unfinished.Last());
    }
}
