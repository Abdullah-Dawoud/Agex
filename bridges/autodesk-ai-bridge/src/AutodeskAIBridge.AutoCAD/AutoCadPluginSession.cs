#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutodeskAIBridge.Protocol;

namespace AutodeskAIBridge.AutoCAD;

/// <summary>Connects AutoCAD plugin to Host. Dispatcher keeps all API work in valid command/document context.</summary>
public sealed class AutoCadPluginSession : IAsyncDisposable
{
    private readonly PluginIpcClient _client;
    private readonly CancellationTokenSource _stop = new();

    public AutoCadPluginSession(string pipeName, string secret, string version)
    {
        _client = new PluginIpcClient(new NamedPipeOptions { PipeName = pipeName, SharedSecret = secret }, "autocad", version,
            Guid.NewGuid().ToString("N"), Process.GetCurrentProcess().Id, capabilities: new List<string>(AutoCadOperationCatalog.Supported));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _client.RunWithReconnectAsync(HandleAsync, linked.Token).ConfigureAwait(false);
    }

    private static async Task<BridgeResponse> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var local = new AutoCadRequest { ProtocolVersion = request.ProtocolVersion, RequestId = request.RequestId, CorrelationId = request.CorrelationId, Operation = request.Operation, DryRun = request.Options.DryRun };
        foreach (var pair in request.Parameters) local.Parameters[pair.Key] = ParameterText(pair.Value);
        var response = await AutoCadBridgeRuntime.Dispatcher.DispatchAsync(local, cancellationToken).ConfigureAwait(false);
        return new BridgeResponse
        {
            RequestId = response.RequestId,
            Success = response.Success,
            Data = response.Data,
            Warnings = response.Warnings,
            Changes = new ChangeSetDto(response.CreatedHandles.ConvertAll(id => (object)id), response.ModifiedHandles.ConvertAll(id => (object)id), response.DeletedHandles.ConvertAll(id => (object)id)),
            Error = response.Success ? null : new BridgeErrorDto(response.ErrorCode, response.ErrorMessage, response.Recoverable, response.ErrorDetails)
        };
    }

    private static string ParameterText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
    public async ValueTask DisposeAsync() { _stop.Cancel(); await _client.DisposeAsync().ConfigureAwait(false); _stop.Dispose(); }
}
