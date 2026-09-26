#nullable enable
using System;
using Autodesk.Revit.UI;

namespace AutodeskAIBridge.Revit;

/// <summary>Runs queued requests on Revit's API thread. Never call Revit API from transport threads.</summary>
public sealed class RevitExternalEventHandler : IExternalEventHandler
{
    private readonly RevitRequestQueue _queue;
    private readonly RevitCommandDispatcher _dispatcher;
    private const int MaxRequestsPerRaise = 32;

    public RevitExternalEventHandler(RevitRequestQueue queue, RevitCommandDispatcher dispatcher)
    {
        _queue = queue;
        _dispatcher = dispatcher;
    }

    public void Execute(UIApplication application)
    {
        var count = 0;
        while (count++ < MaxRequestsPerRaise && _queue.TryDequeue(out var pending))
        {
            if (pending.IsCompleted)
            {
                pending.DisposeCancellation();
                continue;
            }

            RevitResponse response;
            try
            {
                response = _dispatcher.Dispatch(application, pending.Request);
            }
            catch (Exception exception)
            {
                response = RevitResponse.Fail(pending.Request, "TRANSACTION_FAILED", exception.Message);
            }

            pending.TrySetResult(response);
        }

        if (_queue.HasPending)
            _queue.RaiseAgain();
    }

    public string GetName() => "Autodesk AI Bridge request queue";
}
