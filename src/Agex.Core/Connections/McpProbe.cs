using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Agex.Core.Agents;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Connections;

public sealed record McpProbeResult(bool Ok, string ServerName, IReadOnlyList<string> Tools, string Message, TimeSpan Duration)
{
    /// <summary>Text returned by the optional read-only tool call (for example the bridge's list of connected programs).</summary>
    public string ToolOutput { get; init; } = "";
    public IReadOnlyList<McpToolDescription> Descriptions { get; init; } = [];
}

public sealed record McpToolDescription(string Name, string Description, string InputSchema);
public sealed record McpResourceDescription(string Uri, string Name, string Description);
public sealed record McpResourceTemplateDescription(string UriTemplate, string Name, string Description);

/// <summary>
/// Tests an MCP server the way an agent would start it: the MCP handshake
/// (initialize), then tools/list. Nothing else is called, so no tool runs.
/// Local servers are stopped right after the answer.
/// </summary>
public sealed class McpProbe(IPlatformService platform, ProcessRunner runner, AgexLog? log = null)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    internal const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"agex","version":"2.3"}}}""";
    internal const string Initialized = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
    internal const string ListTools = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

    /// <param name="readOnlyTool">A read-only tool to call after the handshake (for example "autodesk_list_instances"); it is called
    /// every two seconds for up to <paramref name="toolWait"/> until it returns something, so programs that connect late are seen.</param>
    public Task<McpProbeResult> TestAsync(McpServerSpec spec, CancellationToken cancellationToken, TimeSpan? timeout = null, string? readOnlyTool = null, TimeSpan? toolWait = null) =>
        spec.IsRemote ? TestRemoteAsync(spec, cancellationToken) : TestLocalAsync(spec, timeout ?? TimeSpan.FromSeconds(120), cancellationToken, readOnlyTool, toolWait ?? TimeSpan.Zero);

    public Task<McpSession> OpenSessionAsync(McpServerSpec spec, CancellationToken cancellationToken) =>
        McpSession.OpenAsync(platform, runner, spec, cancellationToken);

    /// <summary>Calls one listed tool through AGEX. The caller enforces user permissions before invoking this method.</summary>
    public async Task<string> CallAsync(McpServerSpec spec, string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        var result = spec.IsRemote
            ? await TestRemoteAsync(spec, cancellationToken, tool, arguments).ConfigureAwait(false)
            : await TestLocalAsync(spec, TimeSpan.FromSeconds(90), cancellationToken, tool, TimeSpan.Zero, arguments).ConfigureAwait(false);
        if (!result.Ok) throw new IOException(result.Message);
        if (!result.Tools.Contains(tool, StringComparer.Ordinal)) throw new InvalidOperationException("The server does not list this tool.");
        if (string.IsNullOrWhiteSpace(result.ToolOutput)) throw new IOException("The tool did not answer.");
        using (var reply = JsonDocument.Parse(result.ToolOutput))
        {
            if (reply.RootElement.TryGetProperty("error", out var error)) throw new IOException("The tool returned an error: " + Redactor.Redact(error.ToString()));
            if (reply.RootElement.TryGetProperty("result", out var body) && body.TryGetProperty("isError", out var failed) && failed.ValueKind == JsonValueKind.True)
                throw new IOException("The tool reported a failed call: " + Redactor.Redact(body.ToString()));
        }
        return result.ToolOutput;
    }

    private async Task<McpProbeResult> TestLocalAsync(McpServerSpec spec, TimeSpan timeout, CancellationToken cancellationToken, string? readOnlyTool, TimeSpan toolWait, JsonElement? arguments = null)
    {
        var watch = Stopwatch.StartNew();
        ProcessRunner.LaunchTarget target;
        try { target = runner.ResolveLaunchTarget(spec.Command, spec.Arguments); }
        catch (InvalidOperationException ex) { return new(false, "", [], ex.Message, watch.Elapsed); }
        var info = new ProcessStartInfo(target.FileName)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = platform.Paths.DataRoot,
        };
        if (target.RawArguments is { } raw) info.Arguments = raw;
        else foreach (var argument in target.Arguments) info.ArgumentList.Add(argument);
        foreach (var (name, value) in platform.ChildEnvironment()) info.Environment[name] = value;
        foreach (var (name, value) in spec.SecretEnvironment) info.Environment[name] = value;
        Process process;
        try { process = Process.Start(info) ?? throw new InvalidOperationException("The server did not start."); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new(false, "", [], $"Could not start {Path.GetFileName(spec.Command)}: {ex.Message}", watch.Elapsed);
        }
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line && errors.Length < 4000) errors.AppendLine(line); };
        process.BeginErrorReadLine();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.StandardInput.WriteLineAsync(Initialize.AsMemory(), limit.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(limit.Token).ConfigureAwait(false);
            var hello = await ReadResponseAsync(process.StandardOutput, 1, limit.Token).ConfigureAwait(false);
            if (hello is null) return Failed("The server stopped before answering.", errors, watch);
            if (Error(hello.Value) is { } helloError) return Failed("The server refused the connection: " + helloError, errors, watch);
            await process.StandardInput.WriteLineAsync(Initialized.AsMemory(), limit.Token).ConfigureAwait(false);
            await process.StandardInput.WriteLineAsync(ListTools.AsMemory(), limit.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(limit.Token).ConfigureAwait(false);
            var tools = await ReadResponseAsync(process.StandardOutput, 2, limit.Token).ConfigureAwait(false);
            var result = Result(hello.Value, tools, watch);
            if (readOnlyTool is null || !result.Tools.Contains(readOnlyTool)) return result;
            var output = "";
            var until = DateTimeOffset.UtcNow + toolWait;
            for (var id = 3; ; id++)
            {
                var call = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = readOnlyTool, arguments = arguments ?? JsonSerializer.SerializeToElement(new { }) } });
                await process.StandardInput.WriteLineAsync(call.AsMemory(), limit.Token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(limit.Token).ConfigureAwait(false);
                output = (await ReadResponseAsync(process.StandardOutput, id, limit.Token).ConfigureAwait(false))?.ToString() ?? "";
                if (output.Contains("product", StringComparison.OrdinalIgnoreCase) || DateTimeOffset.UtcNow >= until) break;
                await Task.Delay(TimeSpan.FromSeconds(2), limit.Token).ConfigureAwait(false);
            }
            return result with { ToolOutput = output };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed($"The server did not answer within {timeout.TotalSeconds:0} seconds.", errors, watch);
        }
        catch (IOException ex) { return Failed("The server closed the connection: " + ex.Message, errors, watch); }
        finally
        {
            ProcessRunner.KillTree(process);
            process.Dispose();
        }
    }

    private async Task<McpProbeResult> TestRemoteAsync(McpServerSpec spec, CancellationToken cancellationToken, string? tool = null, JsonElement? arguments = null)
    {
        var watch = Stopwatch.StartNew();
        var token = spec.BearerEnvironmentVariable is { Length: > 0 } variable ? spec.SecretEnvironment.GetValueOrDefault(variable) : null;
        string? session = null;
        async Task<JsonElement?> PostAsync(string body, int id)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, spec.Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
            if (token is { Length: > 0 }) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (session is not null) request.Headers.Add("Mcp-Session-Id", session);
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var values)) session = values.FirstOrDefault() ?? session;
            if (id == 0) return null;
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException((int)response.StatusCode is 401 or 403 ? "The service did not accept the key or token (HTTP " + (int)response.StatusCode + ")." : "HTTP " + (int)response.StatusCode);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            foreach (var line in text.Split('\n'))
            {
                var json = line.StartsWith("data:", StringComparison.Ordinal) ? line[5..].Trim() : line.Trim();
                if (Parse(json) is { } message && IdOf(message) == id) return message;
            }
            return null;
        }
        try
        {
            var hello = await PostAsync(Initialize, 1).ConfigureAwait(false);
            if (hello is null) return new(false, "", [], "The service answered, but not with an MCP handshake.", watch.Elapsed);
            if (Error(hello.Value) is { } error) return new(false, "", [], "The service refused the connection: " + error, watch.Elapsed);
            await PostAsync(Initialized, 0).ConfigureAwait(false);
            var result = Result(hello.Value, await PostAsync(ListTools, 2).ConfigureAwait(false), watch);
            if (tool is null || !result.Tools.Contains(tool, StringComparer.Ordinal)) return result;
            var call = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = tool, arguments = arguments ?? JsonSerializer.SerializeToElement(new { }) } });
            var reply = await PostAsync(call, 3).ConfigureAwait(false);
            return result with { ToolOutput = reply?.ToString() ?? "" };
        }
        catch (HttpRequestException ex) { return new(false, "", [], "Could not reach the service: " + Redactor.Redact(ex.Message), watch.Elapsed); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "", [], "The service did not answer in time.", watch.Elapsed); }
    }

    /// <summary>Reads newline-delimited JSON-RPC until the response with the given id (notifications and logs are skipped).</summary>
    internal static async Task<JsonElement?> ReadResponseAsync(TextReader reader, int id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return null;
            if (Parse(line.Trim()) is { } message && IdOf(message) == id) return message;
        }
    }

    internal static McpProbeResult Result(JsonElement hello, JsonElement? tools, Stopwatch watch)
    {
        var name = hello.TryGetProperty("result", out var result) && result.TryGetProperty("serverInfo", out var info) && info.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
        var names = new List<string>();
        var descriptions = new List<McpToolDescription>();
        if (tools is { } list && list.TryGetProperty("result", out var toolResult) && toolResult.TryGetProperty("tools", out var array) && array.ValueKind == JsonValueKind.Array)
            foreach (var tool in array.EnumerateArray())
                if (tool.TryGetProperty("name", out var toolName) && toolName.ValueKind == JsonValueKind.String)
                {
                    var toolNameText = toolName.GetString()!;
                    names.Add(toolNameText);
                    descriptions.Add(new McpToolDescription(toolNameText,
                        tool.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String ? description.GetString() ?? "" : "",
                        tool.TryGetProperty("inputSchema", out var schema) ? schema.GetRawText() : "{}"));
                }
        var message = names.Count > 0 ? $"Connected{(name.Length > 0 ? " to " + name : "")}: {names.Count} tool{(names.Count == 1 ? "" : "s")} available." : $"Connected{(name.Length > 0 ? " to " + name : "")}, but it listed no tools.";
        return new McpProbeResult(true, name, names, message, watch.Elapsed) { Descriptions = descriptions };
    }

    private McpProbeResult Failed(string message, StringBuilder errors, Stopwatch watch)
    {
        var detail = errors.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("not found", StringComparison.OrdinalIgnoreCase));
        log?.Write("mcp_probe_failed", new { message });
        return new(false, "", [], message + (detail is { Length: > 0 } ? " " + Redactor.Redact(detail.Length > 300 ? detail[..300] : detail) : ""), watch.Elapsed);
    }

    private static JsonElement? Parse(string line)
    {
        if (line.Length == 0 || line[0] != '{') return null;
        try { using var document = JsonDocument.Parse(line); return document.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    /// <summary>The response id; servers that answer a numeric id with a string ("1") are accepted too.</summary>
    private static int? IdOf(JsonElement message) =>
        !message.TryGetProperty("id", out var id) ? null
        : id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var value) ? value
        : id.ValueKind == JsonValueKind.String && int.TryParse(id.GetString(), out var text) ? text : null;

    private static string? Error(JsonElement message) =>
        message.TryGetProperty("error", out var error) ? error.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : error.ToString() : null;
}
