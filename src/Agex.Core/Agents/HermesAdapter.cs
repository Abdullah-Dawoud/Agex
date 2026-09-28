using System.Text;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>Hermes one-shot JSONL adapter. AGEX owns connected tools and permission checks.</summary>
public sealed class HermesAdapter(ProcessRunner runner, IPlatformService platform) : CliAgentAdapter(runner, platform)
{
    public override string Id => "hermes";
    public override string Name => "Hermes";
    public override string Provider => "Nous Research (many providers)";
    public override string Description => "One-shot chat, planning and review through Hermes. Connected tools run through AGEX.";
    public override IReadOnlySet<Capability> Capabilities { get; } = new HashSet<Capability>
    {
        Capability.Planning, Capability.CodeReview, Capability.Debugging, Capability.Documents,
    };
    public override string DataDestination(string? model) => "The provider selected in Hermes";
    protected override string[] CommandNames => ["hermes"];
    public override ModelSettingsSupport ModelSettings { get; } = new()
    {
        Source = "hermes chat --help: --model and --format stream-json.",
    };

    public override AgentSetupInfo Setup { get; } = new()
    {
        Method = InstallMethod.Manual,
        OfficialUrl = "https://hermes-agent.nousresearch.com/docs/getting-started/quickstart/",
        ManualCommands = new Dictionary<OsKind, string>
        {
            [OsKind.Windows] = "Follow the official Hermes installation guide",
            [OsKind.MacOS] = "Follow the official Hermes installation guide",
            [OsKind.Linux] = "Follow the official Hermes installation guide",
        },
        WhatGetsInstalled = "Hermes Agent CLI.",
        SizeHint = "varies",
        AdminNote = "Install through Hermes' official instructions.",
        AccountNote = "Use Hermes setup to choose and sign in to a model provider. AGEX never reads Hermes credentials.",
        LoginArguments = ["setup"],
        LoginInstructions = "A terminal opens with Hermes setup. Choose a provider and complete its sign-in.",
        LoginDocsUrl = "https://hermes-agent.nousresearch.com/docs/getting-started/quickstart/",
    };

    public override async Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = "Hermes is not installed.", FallbackEligible = true };
        var answer = new StringBuilder();
        string? final = null;
        string? error = null;
        UsageReport? usage = null;
        var run = await Runner.RunAsync(new ProcessRequest
        {
            FileName = detection.Path,
            Arguments = BuildArguments(invocation),
            WorkingDirectory = invocation.WorkingDirectory,
            StdinText = invocation.Prompt,
            Timeout = invocation.Timeout,
            Label = $"Hermes {invocation.Label}",
            OnStarted = handle => invocation.OnProcessStarted?.Invoke(handle.Pid),
            OnStdoutLine = line =>
            {
                if (TryParseJson(line) is not { } evt) return;
                switch (Str(evt, "type"))
                {
                    case "text" when Str(evt, "text") is { } chunk: answer.Append(chunk); break;
                    case "result":
                        final = Str(evt, "text");
                        error = Str(evt, "error");
                        if (Obj(evt, "tokens") is { } tokens)
                            usage = new UsageReport { InputTokens = Num(tokens, "input"), OutputTokens = Num(tokens, "output"), CachedInputTokens = Num(tokens, "cache_read"), Source = "Hermes" };
                        break;
                    case "tool_use" when Str(evt, "name") is { } tool:
                        invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.ToolStarted, tool));
                        break;
                }
            },
        }, cancellationToken).ConfigureAwait(false);
        var text = final ?? answer.ToString();
        if (run.Succeeded && !string.IsNullOrWhiteSpace(text))
            return new AgentRunResult { Outcome = RunOutcome.Ok, Text = text.Trim(), Usage = usage, CommandLine = run.CommandLine, Pid = run.Pid, ExitCode = run.ExitCode, Duration = run.Duration };
        return FailureFrom(run, error) with { Usage = usage };
    }

    internal static IReadOnlyList<string> BuildArguments(AgentInvocation invocation)
    {
        var arguments = new List<string> { "chat", "--query-file", "-", "--oneshot", "--format", "stream-json", "--safe-mode", "--max-turns", "1" };
        if (ModelName.IsValid(invocation.Model)) arguments.AddRange(["--model", invocation.Model!]);
        return arguments;
    }
}
