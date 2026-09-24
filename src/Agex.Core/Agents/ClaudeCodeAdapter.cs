using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>
/// Anthropic Claude Code in print mode (<c>claude -p --output-format stream-json</c>).
/// The prompt goes through stdin. Built from the CLI's documented flags.
/// </summary>
public sealed class ClaudeCodeAdapter(ProcessRunner runner, IPlatformService platform) : CliAgentAdapter(runner, platform)
{
    public override string Id => "claude-code";
    public override string Name => "Claude Code";
    public override string Provider => "Anthropic";
    public override string Description => "Coding, review, planning and documentation.";
    public override IReadOnlySet<Capability> Capabilities { get; } = new HashSet<Capability>
    {
        Capability.ReadFiles, Capability.WriteFiles, Capability.RunCommands, Capability.WebResearch, Capability.CodeReview,
        Capability.Testing, Capability.Planning, Capability.Debugging, Capability.Mcp, Capability.Documents, Capability.Skills,
    };
    public override string DataDestination(string? model) => "Anthropic cloud (Claude)";
    protected override string[] CommandNames => ["claude"];
    protected override string? NpmPackage => "@anthropic-ai/claude-code";

    private static readonly string[] ReadOnlyDenied = ["Edit", "MultiEdit", "Write", "NotebookEdit"];

    internal static List<string> BuildArguments(AgentInvocation invocation)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose" };
        // Claude Code has no OS sandbox here: permissions are expressed through its
        // permission mode and tool allow/deny lists.
        if (invocation.AllowWrites) args.AddRange(["--permission-mode", "acceptEdits"]);
        var denied = invocation.AllowWrites ? new List<string>() : [.. ReadOnlyDenied];
        if (invocation.AllowCommands) args.AddRange(["--allowedTools", "Bash"]);
        else denied.Add("Bash");
        if (denied.Count > 0) { args.Add("--disallowedTools"); args.AddRange(denied); }
        if (ModelName.IsValid(invocation.Model)) args.AddRange(["--model", invocation.Model!]);
        foreach (var folder in invocation.Skills.Select(skill => skill.Folder).Distinct()) args.AddRange(["--add-dir", folder]);
        if (invocation.McpServers.Count > 0)
        {
            // "${NAME}" is expanded by Claude Code from its own environment, so secret values stay off the command line.
            var servers = invocation.McpServers.Where(server => ModelName.IsSafeKey(server.Name)).ToDictionary(
                server => server.Name,
                server => server.IsRemote
                    ? (object)new
                    {
                        type = "http",
                        url = server.Url,
                        headers = server.BearerEnvironmentVariable is { Length: > 0 } bearer ? new Dictionary<string, string> { ["Authorization"] = "Bearer ${" + bearer + "}" } : new Dictionary<string, string>(),
                    }
                    : new
                    {
                        command = server.Command,
                        args = server.Arguments,
                        env = server.SecretEnvironment.Keys.ToDictionary(name => name, name => "${" + name + "}"),
                    });
            args.AddRange(["--mcp-config", JsonSerializer.Serialize(new { mcpServers = servers })]);
        }
        return args;
    }

    public override async Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = "Claude Code is not installed.", FallbackEligible = true };
        string? result = null;
        string? error = null;
        var isError = false;
        var toolUses = 0;
        UsageReport? usage = null;
        var environment = Platform.ChildEnvironment();
        foreach (var server in invocation.McpServers)
            foreach (var (name, value) in server.SecretEnvironment) environment[name] = value;

        var run = await Runner.RunAsync(new ProcessRequest
        {
            FileName = detection.Path,
            Arguments = BuildArguments(invocation),
            WorkingDirectory = invocation.WorkingDirectory,
            StdinText = WithSkills(invocation.Prompt, invocation.Skills),
            Timeout = invocation.Timeout,
            Environment = environment,
            Label = $"Claude Code {invocation.Label}",
            OnStarted = handle => invocation.OnProcessStarted?.Invoke(handle.Pid),
            OnStdoutLine = line =>
            {
                if (TryParseJson(line) is not { } evt) return;
                switch (Str(evt, "type"))
                {
                    case "assistant" when Obj(evt, "message") is { } message && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array:
                        foreach (var part in content.EnumerateArray())
                        {
                            if (Str(part, "type") != "tool_use") continue;
                            toolUses++;
                            var input = Obj(part, "input");
                            var target = input is { } i ? Str(i, "file_path") ?? Str(i, "command") ?? Str(i, "pattern") ?? Str(i, "url") ?? "" : "";
                            invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.ToolStarted, $"{Str(part, "name")}{(target.Length > 0 ? ": " + Shorten(Redactor.Redact(target), 120) : "")}"));
                        }
                        break;
                    case "result":
                        result = Str(evt, "result");
                        isError = evt.TryGetProperty("is_error", out var flag) && flag.ValueKind == JsonValueKind.True;
                        if (isError) error = result ?? Str(evt, "subtype");
                        decimal? cost = evt.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDecimal() : null;
                        var u = Obj(evt, "usage");
                        usage = new UsageReport
                        {
                            InputTokens = u is { } x ? Num(x, "input_tokens") : null, OutputTokens = u is { } y ? Num(y, "output_tokens") : null,
                            CachedInputTokens = u is { } z ? Num(z, "cache_read_input_tokens") : null, CostUsd = cost, Source = "Claude Code",
                        };
                        break;
                }
            },
        }, cancellationToken).ConfigureAwait(false);

        if (!isError && result is { Length: > 0 } && run.Outcome == ProcessOutcome.Ok)
            return new AgentRunResult { Outcome = RunOutcome.Ok, Text = result.Trim(), Usage = usage, CommandLine = run.CommandLine, Pid = run.Pid, ExitCode = run.ExitCode, Duration = run.Duration };
        var failure = FailureFrom(run, error);
        return failure with { Usage = usage, FallbackEligible = failure.FallbackEligible && toolUses == 0 };
    }
}
