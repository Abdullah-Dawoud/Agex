using System.Text.Json;
using AutodeskAIBridge.Core;
using AutodeskAIBridge.Protocol;

namespace AutodeskAIBridge.Host;

/// <summary>Host adapter that forwards typed requests to one authenticated plugin connection.</summary>
public sealed class IpcAutodeskAdapter : IAutodeskAdapter
{
    private readonly IpcServerConnection _connection;
    private readonly object _gate = new();
    private AutodeskInstanceInfo _info;

    public IpcAutodeskAdapter(IpcServerConnection connection)
    {
        _connection = connection;
        var identity = connection.Info;
        _info = new AutodeskInstanceInfo(identity.Product.ToLowerInvariant(), identity.ClientInstanceId, identity.ProductVersion,
            identity.ActiveDocumentId, identity.ActiveDocumentName, true, identity.Documents, identity.ProcessId, identity.ConnectedAt,
            identity.ConnectedAt, identity.SupportedCapabilities);
    }

    public string Product => _info.Product;
    public AutodeskInstanceInfo GetInstanceInfo() { lock (_gate) return _info with { ApiReady = _connection.IsConnected, LastHeartbeat = _connection.LastHeartbeatUtc }; }

    public async Task<ToolResult> ExecuteAsync(string operation, IReadOnlyDictionary<string, object?> parameters, ToolCallContext context)
    {
        if (!_connection.IsConnected) return ToolResult.Fail(BridgeErrorCodes.PluginDisconnected, "Plugin is disconnected.");
        if (context.CancellationToken.IsCancellationRequested) return ToolResult.Fail(BridgeErrorCodes.Cancelled, "Operation was cancelled.");
        var target = context.Target ?? new AutodeskTarget(Product, _info.InstanceId);
        var request = new BridgeRequest
        {
            RequestId = context.RequestId,
            CorrelationId = context.CorrelationId,
            Target = target,
            Operation = operation,
            Parameters = parameters.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value, ProtocolJson.Options), StringComparer.OrdinalIgnoreCase),
            Options = new BridgeRequestOptions { DryRun = context.DryRun }
        };
        BridgeResponse response;
        try { response = await _connection.SendRequestAsync(request, context.CancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { return new ToolResult(false, Error: BridgeErrorMapper.FromException(exception)); }
        if (response.Success)
        {
            lock (_gate) _info = _info with { LastHeartbeat = DateTimeOffset.UtcNow };
            return ToolResult.Ok(response.Data, new ChangeSet(response.Changes.Created, response.Changes.Modified, response.Changes.Deleted), response.Warnings);
        }
        var error = response.Error;
        return new ToolResult(false, Warnings: response.Warnings, Error: error is null
            ? new BridgeError(BridgeErrorCodes.InvalidRequest, "Plugin returned an invalid error.")
            : new BridgeError(error.Code, error.Message, error.Recoverable, error.Details, error.Suggestions));
    }
}
