#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutodeskAIBridge.Protocol;

namespace AutodeskAIBridge.Revit;

/// <summary>Connects Revit add-in to Host. Requests enter Revit only through ExternalEvent queue.</summary>
public sealed class RevitPluginSession : IAsyncDisposable
{
    private readonly PluginIpcClient _client;
    private readonly CancellationTokenSource _stop = new();

    public RevitPluginSession(string pipeName, string secret, string version)
    {
        _client = new PluginIpcClient(new NamedPipeOptions { PipeName = pipeName, SharedSecret = secret }, "revit", version,
            Guid.NewGuid().ToString("N"), Process.GetCurrentProcess().Id, capabilities: Capabilities());
    }

    private static IReadOnlyList<string> Capabilities()
    {
        return new List<string>(RevitOperationCatalog.Supported);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _client.RunWithReconnectAsync(HandleAsync, linked.Token).ConfigureAwait(false);
    }

    private static async Task<BridgeResponse> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var queue = RevitBridgeRuntime.Queue;
        if (queue is null) return Failure(request, "NO_ACTIVE_SESSION", "Revit bridge queue is not ready.");
        var local = new RevitRequest
        {
            ProtocolVersion = request.ProtocolVersion,
            RequestId = request.RequestId,
            CorrelationId = request.CorrelationId,
            Operation = request.Operation,
            DryRun = request.Options.DryRun
        };
        foreach (var pair in request.Parameters) local.Parameters[pair.Key] = ParameterText(pair.Value);
        try
        {
            var response = await queue.EnqueueAsync(local, cancellationToken).ConfigureAwait(false);
            return new BridgeResponse
            {
                RequestId = response.RequestId,
                Success = response.Success,
                Data = response.Data,
                Warnings = response.Warnings,
                Changes = new ChangeSetDto(response.CreatedElementIds.ConvertAll(id => (object)id), response.ModifiedElementIds.ConvertAll(id => (object)id), response.DeletedElementIds.ConvertAll(id => (object)id)),
                Error = response.Success ? null : new BridgeErrorDto(response.ErrorCode, response.ErrorMessage, response.Recoverable, response.ErrorDetails)
            };
        }
        catch (OperationCanceledException)
        {
            return Failure(request, "CANCELLED", "Request was cancelled.");
        }
    }

    private static string ParameterText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
    private static BridgeResponse Failure(BridgeRequest request, string code, string message) => new() { RequestId = request.RequestId, Success = false, Error = new BridgeErrorDto(code, message, true) };
    public async ValueTask DisposeAsync() { _stop.Cancel(); await _client.DisposeAsync().ConfigureAwait(false); _stop.Dispose(); }
}
