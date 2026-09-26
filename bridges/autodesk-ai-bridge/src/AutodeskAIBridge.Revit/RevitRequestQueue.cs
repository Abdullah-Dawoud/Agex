#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace AutodeskAIBridge.Revit;

/// <summary>Queues external requests. Autodesk API execution occurs only in ExternalEventHandler.</summary>
public sealed class RevitRequestQueue : IDisposable
{
    private readonly ConcurrentQueue<PendingRequest> _pending = new();
    private ExternalEvent? _externalEvent;
    private int _disposed;

    public void Attach(ExternalEvent externalEvent) => _externalEvent = externalEvent;

    public Task<RevitResponse> EnqueueAsync(RevitRequest request, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(RevitRequestQueue));

        var pending = new PendingRequest(request);
        _pending.Enqueue(pending);
        pending.RegisterCancellation(cancellationToken);

        try
        {
            _externalEvent?.Raise();
        }
        catch (Exception exception)
        {
            pending.TrySetResult(RevitResponse.Fail(request, "TRANSACTION_FAILED", exception.Message));
        }

        return pending.Task;
    }

    internal bool TryDequeue(out PendingRequest request)
    {
        if (_pending.TryDequeue(out var value))
        {
            request = value;
            return true;
        }

        request = null!;
        return false;
    }
    internal bool HasPending => !_pending.IsEmpty;

    internal void RaiseAgain()
    {
        if (Volatile.Read(ref _disposed) == 0)
            _externalEvent?.Raise();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        while (_pending.TryDequeue(out var pending))
            pending.TrySetResult(RevitResponse.Fail(pending.Request, "PLUGIN_DISCONNECTED", "Revit bridge is shutting down."));
    }

    internal sealed class PendingRequest
    {
        private readonly TaskCompletionSource<RevitResponse> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;

        internal PendingRequest(RevitRequest request) => Request = request;
        internal RevitRequest Request { get; }
        internal Task<RevitResponse> Task => _completion.Task;
        internal bool IsCompleted => _completion.Task.IsCompleted;

        internal void RegisterCancellation(CancellationToken token)
        {
            if (token.CanBeCanceled)
                _registration = token.Register(() => _completion.TrySetCanceled(token));
        }

        internal bool TrySetResult(RevitResponse response)
        {
            var set = _completion.TrySetResult(response);
            if (set)
                _registration.Dispose();
            return set;
        }

        internal void DisposeCancellation() => _registration.Dispose();
    }
}
