using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>
/// Google Gemini CLI in non-interactive mode: prompt on stdin, one JSON
/// document (<c>--output-format json</c>) on stdout.
/// </summary>
public sealed class GeminiCliAdapter(ProcessRunner runner, IPlatformService platform) : CliAgentAdapter(runner, platform)
{
    public override string Id => "gemini-cli";
    public override string Name => "Gemini CLI";
    public override string Provider => "Google";
    public override string Description => "Research, coding and large-context reading.";
    public override IReadOnlySet<Capability> Capabilities { get; } = new HashSet<Capability>
    {
        Capability.ReadFiles, Capability.WriteFiles, Capability.RunCommands, Capability.WebResearch, Capability.CodeReview,
        Capability.Planning, Capability.Debugging, Capability.Documents,
    };
    public override string DataDestination(string? model) => "Google cloud (Gemini)";
    protected override string[] CommandNames => ["gemini"];
    protected override string? NpmPackage => "@google/gemini-cli";

    internal static List<string> BuildArguments(AgentInvocation invocation)
    {
        // Non-interactive runs cannot answer approval prompts; the approval mode
        // decides which tools are available at all.
        var mode = !invocation.AllowWrites ? "default" : invocation.AllowCommands ? "yolo" : "auto_edit";
        var args = new List<string> { "--output-format", "json", "--approval-mode", mode };
        if (ModelName.IsValid(invocation.Model)) args.AddRange(["--model", invocation.Model!]);
        foreach (var folder in invocation.Skills.Select(skill => skill.Folder).Distinct()) args.AddRange(["--include-directories", folder]);
        return args;
    }

    public override async Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = "Gemini CLI is not installed.", FallbackEligible = true };
        invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.Status, "Gemini CLI is working (it reports results when finished)"));
        var run = await Runner.RunAsync(new ProcessRequest
        {
            FileName = detection.Path,
            Arguments = BuildArguments(invocation),
            WorkingDirectory = invocation.WorkingDirectory,
            StdinText = WithSkills(invocation.Prompt, invocation.Skills),
            Timeout = invocation.Timeout,
            Label = $"Gemini CLI {invocation.Label}",
            OnStarted = handle => invocation.OnProcessStarted?.Invoke(handle.Pid),
        }, cancellationToken).ConfigureAwait(false);

        var (text, error, usage) = Parse(run.Stdout);
        if (run.Outcome == ProcessOutcome.Ok && text is { Length: > 0 })
            return new AgentRunResult { Outcome = RunOutcome.Ok, Text = text.Trim(), Usage = usage, CommandLine = run.CommandLine, Pid = run.Pid, ExitCode = run.ExitCode, Duration = run.Duration };
        // Startup errors (for example "no auth method configured") arrive as a JSON document on stderr.
        error ??= Parse(run.Stderr).Error;
        return FailureFrom(run, error) with { Usage = usage };
    }

    internal static (string? Text, string? Error, UsageReport? Usage) Parse(string stdout)
    {
        var start = stdout.IndexOf('{');
        if (start < 0) return (null, null, null);
        try
        {
            using var document = JsonDocument.Parse(stdout[start..]);
            var root = document.RootElement;
            var text = Str(root, "response");
            var error = Obj(root, "error") is { } e ? Str(e, "message") : null;
            UsageReport? usage = null;
            if (Obj(root, "stats") is { } stats && Obj(stats, "models") is { } models)
            {
                foreach (var model in models.EnumerateObject())
                {
                    if (Obj(model.Value, "tokens") is not { } tokens) continue;
                    usage = UsageReport.Combine(usage, new UsageReport { InputTokens = Num(tokens, "prompt"), OutputTokens = Num(tokens, "candidates"), CachedInputTokens = Num(tokens, "cached"), Source = "Gemini CLI" });
                }
            }
            return (text, error, usage);
        }
        catch (JsonException) { return (null, null, null); }
    }
}
