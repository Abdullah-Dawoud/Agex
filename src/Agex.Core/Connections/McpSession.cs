using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Agents;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Connections;

/// <summary>One MCP session kept alive for every tool call in an AGEX agent run.</summary>
public sealed class McpSession : IAsyncDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly McpServerSpec _spec;
    private readonly Process? _process;
    private string? _remoteSession;
    private int _nextId = 3;

    private McpSession(McpServerSpec spec, Process? process) { _spec = spec; _process = process; }

    public IReadOnlyList<McpToolDescription> Tools { get; private set; } = [];
    public IReadOnlyList<McpResourceDescription> Resources { get; private set; } = [];
    public IReadOnlyList<McpResourceTemplateDescription> ResourceTemplates { get; private set; } = [];

    public static async Task<McpSession> OpenAsync(IPlatformService platform, ProcessRunner runner, McpServerSpec spec, CancellationToken cancellationToken)
    {
        Process? process = null;
        if (!spec.IsRemote)
        {
            var target = runner.ResolveLaunchTarget(spec.Command, spec.Arguments);
            var info = new ProcessStartInfo(target.FileName)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = platform.Paths.DataRoot,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
            };
            if (target.RawArguments is { } raw) info.Arguments = raw;
            else foreach (var argument in target.Arguments) info.ArgumentList.Add(argument);
            foreach (var (name, value) in platform.ChildEnvironment()) info.Environment[name] = value;
            foreach (var (name, value) in spec.SecretEnvironment) info.Environment[name] = value;
            process = Process.Start(info) ?? throw new IOException("The MCP server did not start.");
            process.ErrorDataReceived += (_, _) => { }; // drain stderr without logging possible secrets
            process.BeginErrorReadLine();
        }
        var session = new McpSession(spec, process);
        try
        {
            var hello = await session.ExchangeAsync(McpProbe.Initialize, 1, cancellationToken);
            if (hello is null) throw new IOException("The MCP server did not answer initialization.");
            if (hello.Value.TryGetProperty("error", out var error))
                throw new IOException("The MCP server rejected initialization: " + Redactor.Redact(error.ToString()));
            await session.ExchangeAsync(McpProbe.Initialized, 0, cancellationToken);
            var capabilities = hello.Value.GetProperty("result").GetProperty("capabilities");
            var tools = new List<McpToolDescription>();
            if (capabilities.TryGetProperty("tools", out _))
            {
                var listed = await session.ExchangeAsync(McpProbe.ListTools, 2, cancellationToken);
                if (listed is null || listed.Value.TryGetProperty("error", out _))
                {
                    if (!capabilities.TryGetProperty("resources", out _)) throw new IOException("The MCP server did not list tools or resources.");
                }
                else
                {
                    tools.AddRange(McpProbe.Result(hello.Value, listed, Stopwatch.StartNew()).Descriptions);
                    for (var cursor = Cursor(listed.Value); cursor is not null; cursor = Cursor(listed.Value))
                    {
                        listed = await session.RequestAsync("tools/list", new { cursor }, cancellationToken);
                        if (listed is null || listed.Value.TryGetProperty("error", out _)) throw new IOException("The MCP server did not finish listing tools.");
                        tools.AddRange(McpProbe.Result(hello.Value, listed, Stopwatch.StartNew()).Descriptions);
                        if (tools.Count > 1000) throw new IOException("The MCP server listed too many tools.");
                    }
                }
            }
            session.Tools = tools;
            if (capabilities.TryGetProperty("resources", out _))
            {
                var resources = new List<McpResourceDescription>();
                string? cursor = null;
                var resourceListWorked = true;
                do
                {
                    using var optional = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    if (tools.Count > 0) optional.CancelAfter(TimeSpan.FromSeconds(8));
                    JsonElement? page;
                    try { page = await session.RequestAsync("resources/list", cursor is null ? (object)new { } : new { cursor }, optional.Token); }
                    catch (OperationCanceledException) when (tools.Count > 0 && !cancellationToken.IsCancellationRequested) { resourceListWorked = false; break; }
                    if (page is null || page.Value.TryGetProperty("error", out _))
                    {
                        // Some tool servers advertise resources but do not implement listing.
                        // Their working tools must remain usable; a resource-only server still fails.
                        if (tools.Count == 0) throw new IOException("The MCP server did not list resources or tools.");
                        resourceListWorked = false;
                        break;
                    }
                    if (page.Value.TryGetProperty("result", out var result) && result.TryGetProperty("resources", out var array) && array.ValueKind == JsonValueKind.Array)
                        foreach (var item in array.EnumerateArray())
                            if (item.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String && uri.GetString() is { Length: > 0 } address)
                                resources.Add(new McpResourceDescription(address,
                                    item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? address : address,
                                    item.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String ? description.GetString() ?? "" : ""));
                    cursor = Cursor(page.Value);
                    if (resources.Count > 1000) throw new IOException("The MCP server listed too many resources.");
                } while (cursor is not null);
                session.Resources = resources;
                if (!resourceListWorked) return session;
                var templates = new List<McpResourceTemplateDescription>();
                cursor = null;
                do
                {
                    using var optional = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    if (tools.Count > 0) optional.CancelAfter(TimeSpan.FromSeconds(8));
                    JsonElement? page;
                    try { page = await session.RequestAsync("resources/templates/list", cursor is null ? (object)new { } : new { cursor }, optional.Token); }
                    catch (OperationCanceledException) when (tools.Count > 0 && !cancellationToken.IsCancellationRequested) { break; }
                    if (page is null || page.Value.TryGetProperty("error", out _)) break; // templates are optional
                    if (page.Value.TryGetProperty("result", out var result) && result.TryGetProperty("resourceTemplates", out var array) && array.ValueKind == JsonValueKind.Array)
                        foreach (var item in array.EnumerateArray())
                            if (item.TryGetProperty("uriTemplate", out var uri) && uri.ValueKind == JsonValueKind.String && uri.GetString() is { Length: > 0 } address)
                                templates.Add(new McpResourceTemplateDescription(address,
                                    item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? address : address,
                                    item.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String ? description.GetString() ?? "" : ""));
                    cursor = Cursor(page.Value);
                    if (templates.Count > 1000) throw new IOException("The MCP server listed too many resource templates.");
                } while (cursor is not null);
                session.ResourceTemplates = templates;
            }
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    public async Task<string> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!Tools.Any(tool => tool.Name == name)) throw new InvalidOperationException("The server does not list this tool.");
        var id = _nextId++;
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name, arguments } });
        var reply = await ExchangeAsync(request, id, cancellationToken);
        if (reply is null) throw new IOException("The tool did not answer.");
        if (reply.Value.TryGetProperty("error", out var error)) throw new IOException("The tool returned an error: " + Redactor.Redact(error.ToString()));
        if (reply.Value.TryGetProperty("result", out var result) && result.TryGetProperty("isError", out var failed) && failed.ValueKind == JsonValueKind.True)
            throw new IOException("The tool reported a failed call: " + Redactor.Redact(result.ToString()));
        return reply.Value.ToString();
    }

    public async Task<string> ReadResourceAsync(string uri, CancellationToken cancellationToken)
    {
        if (!Resources.Any(resource => resource.Uri == uri) && !ResourceTemplates.Any(template => TemplateMatches(template.UriTemplate, uri)))
            throw new InvalidOperationException("The server does not list this resource or a matching template.");
        var reply = await RequestAsync("resources/read", new { uri }, cancellationToken);
        if (reply is null) throw new IOException("The resource did not answer.");
        if (reply.Value.TryGetProperty("error", out var error)) throw new IOException("The resource returned an error: " + Redactor.Redact(error.ToString()));
        return reply.Value.GetProperty("result").ToString();
    }

    private async Task<JsonElement?> RequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = _nextId++;
        return await ExchangeAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }), id, cancellationToken);
    }

    private static string? Cursor(JsonElement reply) => reply.TryGetProperty("result", out var result)
        && result.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind == JsonValueKind.String
        ? cursor.GetString() : null;

    internal static bool TemplateMatches(string template, string uri)
    {
        if (template.Length > 500 || uri.Length > 2000) return false;
        var pattern = new StringBuilder("^");
        var end = 0;
        foreach (Match variable in Regex.Matches(template, @"\{[^{}]+\}"))
        {
            pattern.Append(Regex.Escape(template[end..variable.Index])).Append(".+");
            end = variable.Index + variable.Length;
        }
        pattern.Append(Regex.Escape(template[end..])).Append('$');
        return Regex.IsMatch(uri, pattern.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private async Task<JsonElement?> ExchangeAsync(string request, int id, CancellationToken cancellationToken)
    {
        if (_process is { } process)
        {
            await process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            return id == 0 ? null : await McpProbe.ReadResponseAsync(process.StandardOutput, id, cancellationToken);
        }
        using var message = new HttpRequestMessage(HttpMethod.Post, _spec.Url) { Content = new StringContent(request, Encoding.UTF8, "application/json") };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        message.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        if (_spec.BearerEnvironmentVariable is { Length: > 0 } bearer && _spec.SecretEnvironment.GetValueOrDefault(bearer) is { Length: > 0 } token)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (_remoteSession is not null) message.Headers.TryAddWithoutValidation("Mcp-Session-Id", _remoteSession);
        using var response = await Http.SendAsync(message, cancellationToken);
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var values)) _remoteSession = values.FirstOrDefault() ?? _remoteSession;
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"MCP service returned HTTP {(int)response.StatusCode}.");
        if (id == 0) return null;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var line in body.Split('\n'))
        {
            var json = line.StartsWith("data:", StringComparison.Ordinal) ? line[5..].Trim() : line.Trim();
            if (!json.StartsWith('{')) continue;
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var value) && (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number == id
                    || value.ValueKind == JsonValueKind.String && value.GetString() == id.ToString())) return root.Clone();
            }
            catch (JsonException) { }
        }
        return null;
    }

    public ValueTask DisposeAsync()
    {
        if (_process is { } process)
        {
            ProcessRunner.KillTree(process);
            process.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
