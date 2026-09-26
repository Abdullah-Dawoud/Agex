using System.Text.Json;
using System.Diagnostics;
using AutodeskAIBridge.Core;
using AutodeskAIBridge.Protocol;

namespace AutodeskAIBridge.Host;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };

        var adapters = args.Contains("--mock", StringComparer.OrdinalIgnoreCase)
            ? new IAutodeskAdapter[]
            {
                new MockAutodeskAdapter("revit", "mock-revit-2026", "2026", "MockProject.rvt"),
                new MockAutodeskAdapter("autocad", "mock-autocad-2026", "2026", "MockDrawing.dwg")
            }
            : Array.Empty<IAutodeskAdapter>();
        var instances = new HostSessionRegistry();
        foreach (var adapter in adapters) instances.Register(adapter);
        var registry = new ToolRegistry();
        BuiltInTools.RegisterAll(registry, instances);
        var dispatcher = new ToolDispatcher(registry, new PermissionPolicy(new PermissionOptions { Read = true, Edit = true }));

        var pipeRequested = args.Contains("--pipe", StringComparer.OrdinalIgnoreCase) || args.Contains("--stdio", StringComparer.OrdinalIgnoreCase) || args.Length == 0;
        Task? pipeTask = null;
        if (pipeRequested)
        {
            var settings = BridgeRuntimeSettings.TryLoad();
            var pipeName = Environment.GetEnvironmentVariable("AUTODESK_AI_BRIDGE_PIPE") ?? settings?.PipeName ?? "AutodeskAIBridge";
            var secret = Environment.GetEnvironmentVariable("AUTODESK_AI_BRIDGE_SECRET") ?? settings?.SharedSecret;
            if (string.IsNullOrWhiteSpace(secret) && args.Contains("--pipe", StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("AUTODESK_AI_BRIDGE_SECRET is required for named-pipe mode.");
            if (!string.IsNullOrWhiteSpace(secret))
            {
                pipeTask = new NamedPipeServer(
                    new NamedPipeOptions { PipeName = pipeName, SharedSecret = secret },
                    (message, token) => HandleIpcAsync(dispatcher, message, token),
                    connection => { instances.Register(new IpcAutodeskAdapter(connection)); return Task.CompletedTask; },
                    connection => { instances.Unregister(connection.Info.Product, connection.Info.ClientInstanceId); return Task.CompletedTask; })
                    .RunAsync(cancellation.Token);
            }
        }

        if (args.Contains("--stdio", StringComparer.OrdinalIgnoreCase) || args.Length == 0)
        {
            await new McpStdioServer(dispatcher, Console.In, Console.Out).RunAsync(cancellation.Token).ConfigureAwait(false);
            cancellation.Cancel();
            if (pipeTask is not null) await pipeTask.ConfigureAwait(false);
            return;
        }
        if (pipeTask is not null) await pipeTask.ConfigureAwait(false);
    }

    private static async Task<IpcEnvelope?> HandleIpcAsync(ToolDispatcher dispatcher, IpcEnvelope message, CancellationToken cancellationToken)
    {
        if (message.Kind == IpcMessageKind.Heartbeat)
            return message with { Kind = IpcMessageKind.Heartbeat, Payload = JsonSerializer.SerializeToElement(new { utc = DateTimeOffset.UtcNow }, ProtocolJson.Options) };
        if (message.Kind != IpcMessageKind.Request || message.RequestId is null)
            return null;

        BridgeRequest? request;
        try { request = message.Payload.Deserialize<BridgeRequest>(ProtocolJson.Options); }
        catch (JsonException) { request = null; }
        if (request is null)
            return message with { Kind = IpcMessageKind.Response, Payload = JsonSerializer.SerializeToElement(new BridgeResponse { RequestId = message.RequestId, Success = false, Error = new BridgeErrorDto(BridgeErrorCodes.InvalidRequest, "Bridge request is malformed.", true) }, ProtocolJson.Options) };
        if (!string.Equals(request.ProtocolVersion, ProtocolConstants.Version, StringComparison.Ordinal))
            return message with { Kind = IpcMessageKind.Response, Payload = JsonSerializer.SerializeToElement(new BridgeResponse { RequestId = request.RequestId, Success = false, Error = new BridgeErrorDto(BridgeErrorCodes.ProtocolVersionMismatch, "Unsupported bridge protocol version.", false) }, ProtocolJson.Options) };
        if (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.CorrelationId) || request.Target is null || string.IsNullOrWhiteSpace(request.Operation))
            return message with { Kind = IpcMessageKind.Response, Payload = JsonSerializer.SerializeToElement(new BridgeResponse { RequestId = request.RequestId, Success = false, Error = new BridgeErrorDto(BridgeErrorCodes.InvalidRequest, "Request ID, correlation ID, target, and operation are required.", true) }, ProtocolJson.Options) };
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(request.Options.TimeoutMilliseconds, 100, 300_000)));
        var result = await dispatcher.DispatchAsync(
            request.Operation,
            request.Parameters.ToDictionary(p => p.Key, p => ProtocolJson.ToPlainValue(p.Value)),
            new ToolCallContext(request.RequestId, request.CorrelationId, request.Target, timeout.Token, request.Options.DryRun)).ConfigureAwait(false);
        var response = new BridgeResponse
        {
            RequestId = request.RequestId,
            Success = result.Success,
            Data = result.Data,
            Warnings = result.EffectiveWarnings,
            Changes = new ChangeSetDto(result.EffectiveChanges.Created, result.EffectiveChanges.Modified, result.EffectiveChanges.Deleted),
            Timing = new TimingDto { DurationMilliseconds = stopwatch.ElapsedMilliseconds },
            Error = result.Error is null ? null : new BridgeErrorDto(result.Error.Code, result.Error.Message, result.Error.Recoverable, result.Error.Details, result.Error.Suggestions)
        };
        return message with
        {
            Kind = IpcMessageKind.Response,
            Payload = JsonSerializer.SerializeToElement(response, ProtocolJson.Options)
        };
    }
}
