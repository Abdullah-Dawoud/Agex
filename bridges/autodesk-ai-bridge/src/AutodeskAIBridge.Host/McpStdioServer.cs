using System.Text.Json;
using System.Text.Json.Nodes;
using AutodeskAIBridge.Core;
using AutodeskAIBridge.Protocol;

namespace AutodeskAIBridge.Host;

/// <summary>Line-delimited MCP JSON-RPC server for agent clients.</summary>
public sealed class McpStdioServer
{
    private readonly ToolDispatcher _dispatcher;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly int _maxMessageBytes;

    public McpStdioServer(ToolDispatcher dispatcher, TextReader input, TextWriter output, int maxMessageBytes = ProtocolConstants.MaxMessageBytes)
    {
        _dispatcher = dispatcher;
        _input = input;
        _output = output;
        _maxMessageBytes = maxMessageBytes;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (System.Text.Encoding.UTF8.GetByteCount(line) > _maxMessageBytes)
            {
                await WriteAsync(new JsonRpcResponse { Id = "", Error = new(-32600, "Message exceeds maximum size.") }).ConfigureAwait(false);
                continue;
            }

            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { await WriteAsync(new JsonRpcResponse { Id = "", Error = new(-32700, "Parse error.") }).ConfigureAwait(false); continue; }

            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("method", out var methodValue) || methodValue.ValueKind != JsonValueKind.String)
                {
                    await WriteAsync(new JsonRpcResponse { Id = ReadId(root) ?? string.Empty, Error = new(-32600, "Invalid Request.") }).ConfigureAwait(false);
                    continue;
                }

                var method = methodValue.GetString()!;
                var id = ReadId(root);
                var response = await HandleAsync(method, id, root, cancellationToken).ConfigureAwait(false);
                if (response is not null) await WriteAsync(response).ConfigureAwait(false);
            }
        }
    }

    private async Task<JsonRpcResponse?> HandleAsync(string method, string? id, JsonElement root, CancellationToken cancellationToken)
    {
        if (id is null)
        {
            if (method == "notifications/initialized") return null;
            return null;
        }

        try
        {
            if (method == "initialize")
            {
                var requested = root.TryGetProperty("params", out var initParams) && initParams.ValueKind == JsonValueKind.Object
                    && initParams.TryGetProperty("protocolVersion", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString() : null;
                return new JsonRpcResponse
                {
                    Id = id,
                    Result = new McpInitializeResult(
                        requested is not null && SupportedProtocolVersions.Contains(requested) ? requested : SupportedProtocolVersions[^1],
                        new McpServerCapabilities(new(true), new()),
                        new(ProtocolConstants.ServerName, typeof(McpStdioServer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"))
                };
            }

            if (method == "tools/list")
            {
                return new JsonRpcResponse { Id = id, Result = new { tools = _dispatcher.ListTools().Select(ToMcpTool).ToArray() } };
            }

            if (method == "tools/call")
            {
                var parameters = root.TryGetProperty("params", out var paramsValue)
                    ? paramsValue
                    : default;
                if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("name", out var nameValue))
                    return new JsonRpcResponse { Id = id, Error = new(-32602, "tools/call requires params.name.") };

                var name = ToolName(nameValue.GetString());
                if (string.IsNullOrWhiteSpace(name))
                    return new JsonRpcResponse { Id = id, Error = new(-32602, "tools/call name is required.") };
                var arguments = parameters.TryGetProperty("arguments", out var args)
                    ? ProtocolJson.ToPlainObject(args)
                    : new Dictionary<string, object?>();
                var timeoutMilliseconds = arguments.TryGetValue("timeoutMs", out var timeoutValue) && int.TryParse(timeoutValue?.ToString(), out var requestedTimeout)
                    ? Math.Clamp(requestedTimeout, 100, 300_000)
                    : 30_000;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(timeoutMilliseconds);
                var target = arguments.TryGetValue("instanceId", out var instanceId) && instanceId is string idValue && arguments.TryGetValue("product", out var product) && product is string productValue
                    ? new AutodeskTarget(productValue, idValue, arguments.TryGetValue("documentId", out var documentId) ? documentId as string : null)
                    : null;
                var dryRun = arguments.TryGetValue("dryRun", out var dryRunValue) && dryRunValue is bool dryRunFlag && dryRunFlag;
                var result = await _dispatcher.DispatchAsync(name, arguments, new(id, Guid.NewGuid().ToString("N"), target, timeout.Token, dryRun)).ConfigureAwait(false);
                return new JsonRpcResponse { Id = id, Result = ToMcpResult(result) };
            }

            if (method == "ping") return new JsonRpcResponse { Id = id, Result = new { ok = true } };
            return new JsonRpcResponse { Id = id, Error = new(-32601, $"Method '{method}' not found.") };
        }
        catch (Exception exception)
        {
            return new JsonRpcResponse { Id = id, Error = new(-32603, "Internal error.", new { exception = exception.GetType().Name }) };
        }
    }

    /// <summary>MCP protocol versions this server speaks; the client's version is echoed when supported.</summary>
    private static readonly string[] SupportedProtocolVersions = ["2024-11-05", "2025-03-26", "2025-06-18"];

    /// <summary>
    /// Writes a JSON-RPC 2.0 response: the request id with its original type (number or string),
    /// and either result or error, never both.
    /// </summary>
    private async Task WriteAsync(JsonRpcResponse response)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = IdNode(response.Id) };
        if (response.Error is { } error) message["error"] = JsonSerializer.SerializeToNode(error, ProtocolJson.Options);
        else message["result"] = JsonSerializer.SerializeToNode(response.Result ?? new object(), ProtocolJson.Options);
        await _output.WriteLineAsync(message.ToJsonString()).ConfigureAwait(false);
        await _output.FlushAsync().ConfigureAwait(false);
    }

    private static JsonNode? IdNode(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonNode.Parse(raw); }
        catch (JsonException) { return JsonValue.Create(raw); }
    }

    /// <summary>The id exactly as the client sent it (raw JSON), so numbers stay numbers.</summary>
    private static string? ReadId(JsonElement root)
        => root.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String or JsonValueKind.Number ? id.GetRawText() : null;

    /// <summary>Agents allow only letters, digits, '_' and '-' in tool names, so "revit.create_wall" is listed as "revit_create_wall".</summary>
    private static string McpName(string name) => name.Replace('.', '_');

    private string? ToolName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        return _dispatcher.ListTools().FirstOrDefault(tool => McpName(tool.Name) == name || tool.Name == name)?.Name ?? name;
    }

    private static McpTool ToMcpTool(ToolDescriptor descriptor)
        => new(McpName(descriptor.Name), descriptor.Description, descriptor.InputSchema, descriptor.Risk.ToString(), descriptor.TargetProduct, descriptor.RequiresConnection, descriptor.RequiresActiveDocument, descriptor.Implementation switch
        {
            ImplementationStatus.Supported => "SUPPORTED",
            ImplementationStatus.BlockedByApi => "BLOCKED_BY_API",
            ImplementationStatus.Unsupported => "UNSUPPORTED",
            _ => "SOURCE_IMPLEMENTED_RUNTIME_UNVERIFIED"
        });

    private static object ToMcpResult(ToolResult result)
        => new
        {
            isError = !result.Success,
            content = new[] { new { type = "text", text = JsonSerializer.Serialize(result, ProtocolJson.Options) } },
            structuredContent = result
        };
}
