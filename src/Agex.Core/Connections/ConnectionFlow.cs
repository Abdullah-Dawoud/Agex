namespace Agex.Core.Connections;

public enum ConnectionFlowState { Connecting, WaitingForSignIn, Connected, NeedsAttention, Unavailable, Failed }

/// <summary>One connection attempt. The launch and verification functions keep provider credentials outside AGEX.</summary>
public static class ConnectionFlow
{
    public static async Task<bool> RunAsync(Func<bool> launch, Func<CancellationToken, Task<bool>> verify,
        Action<ConnectionFlowState> changed, CancellationToken cancellationToken, int attempts = 90, TimeSpan? interval = null)
    {
        changed(ConnectionFlowState.Connecting);
        bool opened;
        try { opened = launch(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { opened = false; }
        if (!opened) { changed(ConnectionFlowState.Failed); return false; }
        changed(ConnectionFlowState.WaitingForSignIn);
        try
        {
            for (var index = 0; index < attempts; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await verify(cancellationToken))
                {
                    changed(ConnectionFlowState.Connected);
                    return true;
                }
                await Task.Delay(interval ?? TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
        catch (OperationCanceledException) { changed(ConnectionFlowState.Failed); return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            changed(ConnectionFlowState.Failed);
            return false;
        }
        changed(ConnectionFlowState.NeedsAttention);
        return false;
    }
}
