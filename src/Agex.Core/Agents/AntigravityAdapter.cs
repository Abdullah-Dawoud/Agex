using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>
/// Google Antigravity CLI (<c>agy</c>) in print mode with stream-json input and
/// output. One user message is written to stdin; the run ends at the
/// <c>result</c> event, after which stdin is closed.
/// </summary>
public sealed partial class AntigravityAdapter(ProcessRunner runner, IPlatformService platform) : CliAgentAdapter(runner, platform)
{
    public override string Id => "antigravity";
    public override string Name => "Antigravity";
    public override string Provider => "Google";
    public override string Description => "Coding, browsing and implementation.";
    public override AdapterStability Stability => AdapterStability.Stable;
    public override IReadOnlySet<Capability> Capabilities { get; } = new HashSet<Capability>
    {
        Capability.ReadFiles, Capability.WriteFiles, Capability.RunCommands, Capability.WebResearch, Capability.Browser,
        Capability.CodeReview, Capability.Testing, Capability.Planning, Capability.Debugging, Capability.Images, Capability.Documents, Capability.Skills,
    };
    public override int MaxConcurrentRuns => 3;
    public override string DataDestination(string? model) => "Google cloud (Antigravity)";
    protected override string[] CommandNames => ["agy"];

    private static readonly string[] Efforts = ["low", "medium", "high"];

    internal static List<string> BuildArguments(AgentInvocation invocation, string logFile)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(invocation.Timeout.TotalMinutes));
        var args = new List<string>
        {
            "--log-file", logFile,
            "--input-format", "stream-json", "--output-format", "stream-json",
            "--sandbox", "--dangerously-skip-permissions",
            "--print-timeout", $"{minutes}m",
        };
        if (ModelName.IsValid(invocation.Model)) args.AddRange(["--model", invocation.Model!]);
        if (invocation.Effort is { } effort && Efforts.Contains(effort)) args.AddRange(["--effort", effort]);
        // agy otherwise works in its own scratch workspace; make the project the workspace.
        args.AddRange(["--add-dir", invocation.WorkingDirectory]);
        foreach (var skill in invocation.Skills.Select(item => item.Folder).Distinct()) args.AddRange(["--add-dir", skill]);
        return args;
    }

    internal static string BuildPayload(string prompt) =>
        JsonSerializer.Serialize(new { message = new { content = prompt }, @event = "user" });

    public override async Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = "Antigravity CLI (agy) is not installed.", FallbackEligible = true };
        var logFile = Path.Combine(Platform.Paths.Temp.EnsureDirectory(), $"agy-{Guid.NewGuid():N}.log");
        string? response = null;
        string? streamError = null;
        var steps = 0;
        UsageReport? usage = null;
        UsageReport? finalUsage = null;
        ProcessHandle? handle = null;
        var prompt = invocation.AllowWrites ? invocation.Prompt : invocation.Prompt + "\n\nDo not create, change or delete files in this turn.";
        try
        {
            var run = await Runner.RunAsync(new ProcessRequest
            {
                FileName = detection.Path,
                Arguments = BuildArguments(invocation, logFile),
                WorkingDirectory = invocation.WorkingDirectory,
                StdinText = BuildPayload(WithSkills(prompt, invocation.Skills)) + "\n",
                KeepStdinOpen = true,
                // agy enforces --print-timeout itself; AGEX adds a margin before stopping it.
                Timeout = invocation.Timeout + TimeSpan.FromMinutes(1),
                Label = $"Antigravity {invocation.Label}",
                OnStarted = started => { handle = started; invocation.OnProcessStarted?.Invoke(started.Pid); },
                OnStdoutLine = line =>
                {
                    if (TryParseJson(line) is not { } evt) return;
                    switch (Str(evt, "event"))
                    {
                        case "init":
                            if (Obj(evt, "init") is { } init && Str(init, "model") is { } model)
                                invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.Status, $"Antigravity started ({model})"));
                            break;
                        case "step_update" when Obj(evt, "step_update") is { } step:
                            steps++;
                            HandleStep(step, invocation, ref usage);
                            break;
                        case "result":
                            if (evt.TryGetProperty("result", out var result))
                            {
                                response = result.ValueKind == JsonValueKind.String ? result.GetString() : Str(result, "response");
                                if (Obj(result, "usage") is { } u) finalUsage = UsageFrom(u);
                                if (Str(result, "status") is { } status && !status.Equals("success", StringComparison.OrdinalIgnoreCase) && !status.Equals("completed", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(response))
                                    streamError ??= $"Antigravity ended with status {status}.";
                            }
                            FinishAfterResult(handle);
                            break;
                        case "error":
                            streamError = Str(evt, "error") ?? (Obj(evt, "error") is { } e ? Str(e, "message") : null) ?? "Antigravity reported an error.";
                            break;
                    }
                },
            }, cancellationToken).ConfigureAwait(false);

            usage = finalUsage ?? usage;
            var authFailure = AuthFailureInLog(logFile);
            if (response is { Length: > 0 } && run.Outcome is ProcessOutcome.Ok or ProcessOutcome.ExitNonZero)
                return new AgentRunResult { Outcome = RunOutcome.Ok, Text = response.Trim(), Usage = usage, CommandLine = run.CommandLine, Pid = run.Pid, ExitCode = run.ExitCode, Duration = run.Duration };
            if (authFailure && run.Outcome is not ProcessOutcome.Cancelled)
                return new AgentRunResult { Outcome = RunOutcome.AuthRequired, Reason = "Antigravity is not signed in. Open Antigravity, sign in, then try again.", FallbackEligible = true, CommandLine = run.CommandLine, Pid = run.Pid, ExitCode = run.ExitCode, Duration = run.Duration };
            var failure = FailureFrom(run, streamError);
            // Once the agent has taken steps it may have changed files; retrying elsewhere could duplicate work.
            return failure with { Usage = usage, FallbackEligible = failure.FallbackEligible && steps == 0 };
        }
        finally
        {
            try { File.Delete(logFile); } catch (IOException) { }
        }
    }

    private static void FinishAfterResult(ProcessHandle? handle)
    {
        if (handle is null) return;
        handle.CloseStdin();
        // agy exits once stdin closes; stop it if it lingers.
        _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ => { if (!handle.HasExited) handle.Kill(); }, TaskScheduler.Default);
    }

    private static void HandleStep(JsonElement step, AgentInvocation invocation, ref UsageReport? usage)
    {
        var type = Str(step, "step_type");
        var state = Str(step, "state");
        if (type == "tool" && Str(step, "tool_name") is { } tool)
        {
            var target = "";
            if (Obj(step, "tool_info") is { } info && Obj(info, "parameters") is { } parameters)
                target = Str(parameters, "AbsolutePath") ?? Str(parameters, "TargetFile") ?? Str(parameters, "CommandLine") ?? Str(parameters, "Query") ?? Str(parameters, "Url") ?? "";
            var text = HumanTool(tool) + (target.Length > 0 ? ": " + Shorten(Redactor.Redact(target), 120) : "");
            invocation.OnActivity?.Invoke(new AgentActivity(state == "DONE" ? ActivityKind.ToolFinished : ActivityKind.ToolStarted, text));
        }
        else if (type == "agent_response" && state == "DONE" && Obj(step, "usage") is { } u)
        {
            usage = UsageReport.Combine(usage, UsageFrom(u));
        }
    }

    private static UsageReport UsageFrom(JsonElement u) =>
        new() { InputTokens = Num(u, "input_tokens"), OutputTokens = Num(u, "output_tokens"), CachedInputTokens = Num(u, "cached_input_tokens"), Source = "Antigravity" };

    private static string HumanTool(string tool) => tool switch
    {
        "write_to_file" => "Writing file",
        "replace_file_content" or "multi_replace_file_content" => "Editing file",
        "view_file" or "read_file" => "Reading file",
        "run_command" => "Running command",
        "list_dir" => "Listing folder",
        "grep_search" or "find_by_name" => "Searching project",
        "search_web" or "read_url_content" => "Reading the web",
        _ => "Tool " + tool,
    };

    [GeneratedRegex(@"(?i)(not logged into Antigravity|authentication required|unauthenticated)")]
    private static partial Regex AuthFailure();

    [GeneratedRegex(@"(?i)(authenticated via keyring|OAuth: authenticated successfully|silent auth succeeded)")]
    private static partial Regex AuthSuccess();

    /// <summary>The CLI writes sign-in problems only to its log file; a later success line overrides an earlier failure.</summary>
    internal static bool AuthFailureInLog(string logFile)
    {
        try
        {
            if (!File.Exists(logFile)) return false;
            var lines = File.ReadAllLines(logFile);
            var lastFailure = Array.FindLastIndex(lines, line => AuthFailure().IsMatch(line));
            if (lastFailure < 0) return false;
            return lastFailure > Array.FindLastIndex(lines, line => AuthSuccess().IsMatch(line));
        }
        catch (IOException) { return false; }
    }
}
