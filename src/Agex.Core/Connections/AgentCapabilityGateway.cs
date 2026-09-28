using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Agents;
using Agex.Core.Runtime;

namespace Agex.Core.Connections;

/// <summary>
/// AGEX's connection adapter for agents without a per-run MCP configuration format.
/// Tool calls stay in AGEX; the agent receives only schemas and tool results.
/// </summary>
public sealed class AgentCapabilityGateway(McpProbe probe)
{
    private const string CallPrefix = "AGEX_TOOL_CALL ";
    private static readonly Regex Sensitive = new(@"(^|[_\-.])(create|update|write|edit|send|publish|deploy|delete|remove|destroy|drop|purchase|pay|merge|push|submit)([_\-.]|$)", RegexOptions.IgnoreCase);

    public async Task<AgentRunResult> RunAsync(IAgentAdapter adapter, AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        var prompt = new StringBuilder(invocation.Prompt);
        if (!adapter.Capabilities.Contains(Capability.Skills))
            foreach (var skill in invocation.Skills.Take(8))
            {
                var path = Path.Combine(skill.Folder, "SKILL.md");
                var instructions = skill.InlineInstructions ?? (File.Exists(path) ? File.ReadAllText(path) : "");
                if (instructions.Length > 12000) instructions = instructions[..12000];
                prompt.AppendLine().AppendLine($"AGEX skill: {skill.Name}").AppendLine(instructions);
            }
        if (invocation.McpServers.Count == 0 || adapter.Capabilities.Contains(Capability.Mcp) && !invocation.ForceGateway)
            return await adapter.RunAsync(detection, Copy(invocation, prompt.ToString(), invocation.McpServers), cancellationToken).ConfigureAwait(false);

        var sessions = new List<McpSession>();
        try
        {
            var tools = new Dictionary<string, (McpSession Session, McpToolDescription Tool, string Server)>(StringComparer.Ordinal);
            foreach (var server in invocation.McpServers)
            {
                McpSession session;
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                try { session = await probe.OpenSessionAsync(server, connectTimeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = $"{server.Name}: connection timed out.", FallbackEligible = true };
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException or System.ComponentModel.Win32Exception)
                {
                    return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = $"{server.Name}: {Redactor.Redact(ex.Message)}", FallbackEligible = true };
                }
                sessions.Add(session);
                foreach (var tool in session.Tools)
                    tools[server.Name + "/" + tool.Name] = (session, tool, server.Name);
                if (session.Resources.Count > 0 || session.ResourceTemplates.Count > 0)
                {
                    tools[server.Name + "/agex_list_resources"] = (session,
                        new McpToolDescription("agex_list_resources", "List files, current documents and URI templates exposed by this connection.", "{\"type\":\"object\"}"), server.Name);
                    tools[server.Name + "/agex_read_resource"] = (session,
                        new McpToolDescription("agex_read_resource", "Read one listed file or document by URI.", "{\"type\":\"object\",\"properties\":{\"uri\":{\"type\":\"string\"}},\"required\":[\"uri\"]}"), server.Name);
                }
            }
            if (tools.Count == 0) return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = "Connected servers listed no tools.", FallbackEligible = true };
            prompt.AppendLine().AppendLine("AGEX TOOLS: To use a connected tool, reply with one line only: AGEX_TOOL_CALL {\"server\":\"server name\",\"tool\":\"tool name\",\"arguments\":{}}. AGEX runs the tool and sends back its result. Do not invent results. Reply normally only when finished. AGEX asks the user before calls that send, publish, pay or delete.");
            foreach (var entry in tools.Take(80))
                prompt.AppendLine($"{entry.Value.Server}/{entry.Value.Tool.Name}: {entry.Value.Tool.Description} Input schema: {entry.Value.Tool.InputSchema}");

            UsageReport? total = null;
            for (var turn = 0; turn < 20; turn++)
            {
                var run = await adapter.RunAsync(detection, Copy(invocation, prompt.ToString(), []), cancellationToken).ConfigureAwait(false);
                total = UsageReport.Combine(total, run.Usage);
                if (!run.Success) return run with { Usage = total };
                if (!TryCall(run.Text, out var serverName, out var toolName, out var arguments)) return run with { Usage = total };
                if (!tools.TryGetValue(serverName + "/" + toolName, out var selected))
                {
                    prompt.AppendLine().AppendLine("AGEX TOOL ERROR: This tool is not listed. Choose a listed tool.");
                    continue;
                }
                if (Sensitive.IsMatch(toolName))
                {
                    var approved = invocation.ApproveSensitiveTool is not null
                        && await invocation.ApproveSensitiveTool(selected.Server, toolName, cancellationToken).ConfigureAwait(false);
                    if (!approved)
                    {
                        prompt.AppendLine().AppendLine("AGEX TOOL ERROR: User approval was not granted. Do not retry this action. Report it as blocked.");
                        continue;
                    }
                }
                invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.ToolStarted, selected.Server + "/" + toolName));
                string output;
                using var callTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                callTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                try
                {
                    output = toolName switch
                    {
                        "agex_list_resources" => JsonSerializer.Serialize(new { resources = selected.Session.Resources, templates = selected.Session.ResourceTemplates }),
                        "agex_read_resource" => await selected.Session.ReadResourceAsync(arguments.GetProperty("uri").GetString() ?? "", callTimeout.Token).ConfigureAwait(false),
                        _ => await selected.Session.CallAsync(toolName, arguments, callTimeout.Token).ConfigureAwait(false),
                    };
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    output = "Tool failed: connection timed out.";
                    if (invocation.Label.StartsWith("connection test ", StringComparison.Ordinal))
                        return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = output, Usage = total };
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException or JsonException or KeyNotFoundException)
                {
                    output = "Tool failed: " + Redactor.Redact(ex.Message);
                    if (invocation.Label.StartsWith("connection test ", StringComparison.Ordinal))
                        return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = output, Usage = total };
                }
                invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.ToolFinished, selected.Server + "/" + toolName));
                if (output.Length > 24000) output = output[..24000] + " [truncated]";
                prompt.AppendLine().AppendLine("AGENT TOOL REQUEST:").AppendLine(run.Text).AppendLine("AGEX TOOL RESULT:").AppendLine(output);
            }
            return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = "AGEX stopped after 20 tool calls.", Usage = total };
        }
        finally { foreach (var session in sessions) await session.DisposeAsync(); }
    }

    private static bool TryCall(string text, out string server, out string tool, out JsonElement arguments)
    {
        server = tool = "";
        arguments = default;
        var line = text.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault(value => value.StartsWith(CallPrefix, StringComparison.Ordinal));
        if (line is null) return false;
        try
        {
            using var document = JsonDocument.Parse(line[CallPrefix.Length..]);
            var root = document.RootElement;
            server = root.GetProperty("server").GetString() ?? "";
            tool = root.GetProperty("tool").GetString() ?? "";
            arguments = root.TryGetProperty("arguments", out var supplied) && supplied.ValueKind == JsonValueKind.Object
                ? supplied.Clone() : JsonSerializer.SerializeToElement(new { });
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return false; }
    }

    private static AgentInvocation Copy(AgentInvocation source, string prompt, IReadOnlyList<McpServerSpec> servers) => new()
    {
        Prompt = prompt, WorkingDirectory = source.WorkingDirectory, AllowWrites = source.AllowWrites,
        AllowCommands = source.AllowCommands, AllowNetwork = source.AllowNetwork, Model = source.Model,
        Effort = source.Effort, Temperature = source.Temperature, ContextWindow = source.ContextWindow,
        Provider = source.Provider, Timeout = source.Timeout, Skills = source.Skills, McpServers = servers,
        Attachments = source.Attachments, Label = source.Label, OnActivity = source.OnActivity,
        OnProcessStarted = source.OnProcessStarted, ApproveSensitiveTool = source.ApproveSensitiveTool, ForceGateway = source.ForceGateway,
    };
}
