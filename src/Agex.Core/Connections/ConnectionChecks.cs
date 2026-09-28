using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Agents;
using Agex.Core.Platform;
using Agex.Core.Runtime;
using Agex.Core.Skills;

namespace Agex.Core.Connections;

/// <summary>
/// The result of testing a connection the way a request uses it: the agent gets
/// the real connection, sees its tools and calls one harmless read-only tool.
/// A connection is shown as Connected only after such a test succeeded.
/// </summary>
public sealed record ConnectionCheck
{
    public required string SkillId { get; init; }
    /// <summary>Agent id the test ran with.</summary>
    public required string Agent { get; init; }
    public bool Ok { get; init; }
    /// <summary>The agent reported using a tool of this server.</summary>
    public bool ToolSeen { get; init; }
    /// <summary>The read-only tool that was called, when one was.</summary>
    public string Tool { get; init; } = "";
    public string Message { get; init; } = "";
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>What was tested (package version and command); a changed connection needs a new test.</summary>
    public string Configuration { get; init; } = "";
}

/// <summary>AGEX provides connected tools to enabled agents through native MCP or its tool gateway.</summary>
public static class AgentToolSupport
{
    public static bool CanReceive(IAgentAdapter adapter, SkillManifest manifest) => true;

    /// <summary>The enabled agents that can use the connection, and the ones that cannot.</summary>
    public static (IReadOnlyList<IAgentAdapter> Usable, IReadOnlyList<IAgentAdapter> NotUsable) Split(AgentRegistry registry, IEnumerable<string> enabledAgents, SkillManifest manifest)
    {
        var enabled = enabledAgents.Select(registry.Get).OfType<IAgentAdapter>().ToList();
        return (enabled.Where(adapter => CanReceive(adapter, manifest)).ToList(), enabled.Where(adapter => !CanReceive(adapter, manifest)).ToList());
    }
}

/// <summary>Stores the last connection test per connection (it survives restarts; disconnecting forgets it).</summary>
public sealed class ConnectionCheckStore(IPlatformService platform)
{
    private readonly object _lock = new();
    private string FilePath => Path.Combine(platform.Paths.DataRoot, "connection-checks.json");

    private Dictionary<string, ConnectionCheck> Load()
    {
        try { return Json.ReadFile<Dictionary<string, ConnectionCheck>>(FilePath) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new(); }
    }

    public ConnectionCheck? Get(string skillId) { lock (_lock) return Load().GetValueOrDefault(skillId); }

    public void Record(ConnectionCheck check)
    {
        lock (_lock)
        {
            var all = Load();
            all[check.SkillId] = check;
            Json.WriteFile(FilePath, all);
        }
    }

    public void Forget(string skillId)
    {
        lock (_lock)
        {
            var all = Load();
            if (all.Remove(skillId)) Json.WriteFile(FilePath, all);
        }
    }
}

/// <summary>Picks a tool that only reads, from the tools a server lists.</summary>
public static class ReadOnlyTools
{
    /// <summary>Known harmless calls for reviewed servers (tool name, arguments).</summary>
    private static readonly Dictionary<string, (string Tool, string Arguments)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["playwright-mcp"] = ("browser_tabs", """{"action":"list"}"""),
        ["chrome-devtools-mcp"] = ("list_pages", "{}"),
        ["context7"] = ("resolve-library-id", """{"libraryName":"react"}"""),
        ["web-fetch"] = ("fetch", """{"url":"https://example.com"}"""),
        [Agex.Core.Teams.AutodeskBridge.SkillId] = ("autodesk_list_instances", "{}"),
    };

    private static readonly Regex Reading = new(@"(^|[_\-.])(list|get|search|read|find|resolve|fetch|status|version|info|describe|lookup|query)([_\-.]|$)", RegexOptions.IgnoreCase);
    private static readonly Regex Changing = new(@"(create|update|delete|remove|write|send|post|submit|click|type|fill|press|drag|upload|move|rename|set|add|insert|execute|run|install|deploy|merge|push|close|kill|navigate)", RegexOptions.IgnoreCase);

    /// <returns>The tool to call and its arguments, or null when the server lists no tool that clearly only reads.</returns>
    public static (string Tool, string Arguments)? Pick(string skillId, IReadOnlyList<string> tools)
    {
        if (Known.TryGetValue(skillId, out var known) && tools.Contains(known.Tool)) return known;
        var reading = tools.FirstOrDefault(tool => Reading.IsMatch(tool) && !Changing.IsMatch(tool));
        return reading is null ? null : (reading, "");
    }
}

/// <summary>
/// "Test with agent": starts the chosen agent with only this connection, in an
/// empty folder with no file changes and no commands, and asks it to call one
/// read-only tool. Passes only when the agent reported using the server's tool
/// and the call returned without an error.
/// </summary>
public sealed class ConnectionTester(IPlatformService platform, SkillManager skills, AgentRegistry registry, McpProbe probe, ConnectionCheckStore store, AgexLog? log = null)
{
    public const string OkMarker = "AGEX_TEST_OK";
    public const string FailedMarker = "AGEX_TEST_FAILED";

    /// <summary>A short fingerprint of what the connection runs; a different one means the last test no longer applies.</summary>
    public static string Configuration(InstalledSkill skill, McpServerSpec? spec) =>
        skill.Manifest.Version + "|" + (spec is null ? "" : spec.IsRemote ? spec.Url : spec.Command + " " + string.Join(' ', spec.Arguments));

    public async Task<ConnectionCheck> TestAsync(string skillId, string agentId, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        ConnectionCheck Result(bool ok, string message, bool seen = false, string tool = "", string configuration = "") =>
            new() { SkillId = skillId, Agent = agentId, Ok = ok, ToolSeen = seen, Tool = tool, Message = message, Configuration = configuration };

        var installed = skills.Installed().FirstOrDefault(skill => skill.Id == skillId);
        if (installed is null) return Result(false, "Not installed.");
        var adapter = registry.Get(agentId);
        if (adapter is null) return Result(false, "This agent is not available.");
        var spec = skills.SpecFor(installed);
        if (spec is null) return Result(false, "The connection is not configured: a key or setting is missing.");
        var configuration = Configuration(installed, spec);

        // 1. The server itself answers (handshake and tool list).
        var server = await probe.TestAsync(spec, cancellationToken, TimeSpan.FromSeconds(90)).ConfigureAwait(false);
        if (!server.Ok) return Record(Result(false, "The server did not start: " + server.Message, configuration: configuration));
        // 2. A resource-only server is useful too: prove AGEX can read one listed document.
        var pick = ReadOnlyTools.Pick(skillId, server.Tools);
        var forceGateway = false;
        var advertised = server.Tools;
        if (server.Tools.Count == 0)
        {
            try
            {
                await using var resourceSession = await probe.OpenSessionAsync(spec, cancellationToken).ConfigureAwait(false);
                if (resourceSession.Resources.FirstOrDefault() is not { } resource)
                    return Record(Result(false, "The server started but offers no tools or listed resources.", configuration: configuration));
                await resourceSession.ReadResourceAsync(resource.Uri, cancellationToken).ConfigureAwait(false);
                pick = ("agex_read_resource", JsonSerializer.Serialize(new { uri = resource.Uri }));
                advertised = [pick.Value.Tool];
                forceGateway = true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException)
            {
                return Record(Result(false, "The server's resource could not be read: " + Redactor.Redact(ex.Message), configuration: configuration));
            }
        }

        // 3. The agent receives it and calls one harmless read-only tool or resource.
        var folder = Path.Combine(platform.Paths.Temp, "connection-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var used = new List<string>();
        try
        {
            var detection = registry.DetectionForRun(agentId);
            var invocation = new AgentInvocation
            {
                Prompt = Prompt(spec.Name, pick, advertised),
                WorkingDirectory = folder,
                AllowWrites = false,
                AllowCommands = false,
                AllowNetwork = true,
                Timeout = timeout ?? TimeSpan.FromMinutes(4),
                McpServers = [spec],
                ForceGateway = forceGateway,
                Label = "connection test " + skillId,
                OnActivity = activity => { if (activity.Kind is ActivityKind.ToolStarted or ActivityKind.ToolFinished) lock (used) used.Add(activity.Text); },
            };
            var run = await new AgentCapabilityGateway(probe).RunAsync(adapter, detection, invocation, cancellationToken).ConfigureAwait(false);
            List<string> events;
            lock (used) events = used.ToList();
            var seen = events.Any(text => text.Contains(spec.Name, StringComparison.OrdinalIgnoreCase) || advertised.Any(tool => text.Contains(tool, StringComparison.OrdinalIgnoreCase)));
            var reply = run.Text ?? "";
            if (!run.Success) return Record(Result(false, $"{adapter.Name} could not run the test: {run.Reason}", seen, pick?.Tool ?? "", configuration));
            if (!seen) return Record(Result(false, $"{adapter.Name} did not receive the tools of this connection.", false, pick?.Tool ?? "", configuration));
            if (reply.Contains(FailedMarker, StringComparison.Ordinal) || !reply.Contains(OkMarker, StringComparison.Ordinal))
                return Record(Result(false, $"{adapter.Name} saw the tools, but the read-only call failed: {Reason(reply)}", true, pick?.Tool ?? "", configuration));
            var message = pick is null
                ? $"{adapter.Name} sees {server.Tools.Count} tools. This connection has no tool that only reads, so none was called."
                : $"{adapter.Name} called {pick.Value.Tool} successfully.";
            return Record(Result(true, message, true, pick?.Tool ?? "", configuration));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private ConnectionCheck Record(ConnectionCheck check)
    {
        store.Record(check);
        log?.Write("connection_test", new { skill = check.SkillId, agent = check.Agent, ok = check.Ok, seen = check.ToolSeen, tool = check.Tool });
        return check;
    }

    internal static string Prompt(string server, (string Tool, string Arguments)? pick, IReadOnlyList<string> tools)
    {
        var call = pick is { } chosen
            ? $"Call the tool \"{chosen.Tool}\" of the MCP server \"{server}\" exactly once" + (chosen.Arguments.Length > 0 ? $" with these arguments: {chosen.Arguments}." : " with the smallest harmless arguments it needs.")
            : $"List the tools of the MCP server \"{server}\" you can see. Do not call any of them.";
        return $"""
            AGEX connection test. This is a check, not a task.
            Do not read, create or change files. Do not run commands. Do not call any tool that creates, changes, deletes, sends, submits, clicks or types.
            {call}
            Then reply with one line only:
            {OkMarker} <tool name> if the call returned without an error (or, when told only to list tools, if you can see them),
            {FailedMarker} <short reason> if the server or the tool is not available to you or the call failed.
            The server lists these tools: {string.Join(", ", tools.Take(40))}.
            """;
    }

    private static string Reason(string reply)
    {
        var line = reply.Split('\n').Select(item => item.Trim()).FirstOrDefault(item => item.Contains(FailedMarker, StringComparison.Ordinal)) ?? reply.Trim();
        line = line.Replace(FailedMarker, "").Trim();
        return Redactor.Redact(line.Length > 200 ? line[..200] : line.Length == 0 ? "no answer" : line);
    }
}
