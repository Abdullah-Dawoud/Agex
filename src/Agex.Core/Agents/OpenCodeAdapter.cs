using System.Text;
using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>
/// OpenCode (anomalyco/opencode) through <c>opencode run --format json</c>: the
/// prompt goes through stdin, events arrive as one JSON object per line
/// (step_start, tool_use, text, step_finish, error). Read-only turns use
/// OpenCode's built-in "plan" agent; writing turns auto-approve permissions that
/// OpenCode's own configuration does not deny.
/// </summary>
public sealed class OpenCodeAdapter(ProcessRunner runner, IPlatformService platform) : CliAgentAdapter(runner, platform)
{
    public override string Id => "opencode";
    public override string Name => "OpenCode";
    public override string Provider => "OpenCode (many providers)";
    public override string Description => "Open-source coding agent for many model providers, including free and local models.";
    public override IReadOnlySet<Capability> Capabilities { get; } = new HashSet<Capability>
    {
        Capability.ReadFiles, Capability.WriteFiles, Capability.RunCommands, Capability.WebResearch, Capability.CodeReview,
        Capability.Testing, Capability.Planning, Capability.Debugging, Capability.Documents,
    };
    protected override string[] CommandNames => ["opencode"];
    protected override string? NpmPackage => "opencode-ai";

    /// <summary>Models are "provider/model"; Ollama models without "cloud" run on this computer.</summary>
    public override PrivacyKind PrivacyFor(string? model) => model switch
    {
        null or "" => PrivacyKind.Mixed,
        _ when model.StartsWith("ollama/", StringComparison.OrdinalIgnoreCase) && !OllamaAdapter.IsCloudModel(model) => PrivacyKind.Local,
        _ => PrivacyKind.Cloud,
    };

    public override string DataDestination(string? model) => model switch
    {
        null or "" => "OpenCode's default model provider",
        _ when PrivacyFor(model) == PrivacyKind.Local => "This computer (Ollama through OpenCode)",
        _ when model.StartsWith("opencode/", StringComparison.OrdinalIgnoreCase) => "OpenCode Zen (cloud)",
        _ => $"{model.Split('/')[0]} cloud (through OpenCode)",
    };

    public override AgentSetupInfo Setup { get; } = new()
    {
        Method = InstallMethod.Npm,
        NpmPackage = "opencode-ai",
        OfficialUrl = "https://opencode.ai/docs/",
        ManualCommands = new Dictionary<OsKind, string>
        {
            [OsKind.Windows] = "npm install -g opencode-ai   (or: scoop install opencode / choco install opencode)",
            [OsKind.MacOS] = "curl -fsSL https://opencode.ai/install | bash   (or: brew install anomalyco/tap/opencode)",
            [OsKind.Linux] = "curl -fsSL https://opencode.ai/install | bash",
        },
        WhatGetsInstalled = "OpenCode (npm package opencode-ai) in your npm global folder.",
        SizeHint = "not published",
        AdminNote = "Usually no administrator rights. On macOS/Linux, npm needs them only if Node.js was installed system-wide.",
        AccountNote = "Works without an account with OpenCode's free models and your local Ollama models. Other providers need their own sign-in or key. Data goes to the provider of the model you pick.",
        LoginArguments = ["auth", "login"],
        LoginInstructions = "A terminal opens and runs 'opencode auth login'. Pick a provider and follow its sign-in; OpenCode stores the credentials itself. Free OpenCode models and local Ollama models need no sign-in.",
        LoginDocsUrl = "https://opencode.ai/docs/providers/",
    };

    public override async Task<AuthCheck> CheckAuthAsync(AgentDetection detection, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return AuthCheck.Unknown;
        // 'opencode auth list' shows saved provider credentials; free and local models work without any.
        var result = await RunQuietAsync(detection, ["auth", "list"], "sign-in check", cancellationToken, 30).ConfigureAwait(false);
        if (!result.Succeeded) return new AuthCheck(AuthState.Unknown, Redactor.Redact(FirstMeaningfulLine(result.Stderr + "\n" + result.Stdout) ?? result.ErrorMessage));
        return result.Stdout.Contains("No authenticated", StringComparison.OrdinalIgnoreCase)
            ? new AuthCheck(AuthState.NotRequired, "No provider connected: free OpenCode models and local models are available. Sign in to add other providers.", "Free and local models")
            : new AuthCheck(AuthState.SignedIn, "", "Providers connected");
    }

    public override async Task<ModelDiscovery> GetModelsAsync(AgentDetection detection, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return ModelDiscovery.Unavailable("OpenCode is not installed.");
        const string source = "opencode models";
        var result = await RunQuietAsync(detection, ["models"], "model list", cancellationToken, 60).ConfigureAwait(false);
        if (!result.Succeeded) return ModelDiscovery.Failed("OpenCode could not list its models: " + Redactor.Redact(FirstMeaningfulLine(result.Stderr) ?? result.ErrorMessage), source);
        return ModelDiscovery.From(ParseModels(result.Stdout), source, "OpenCode reported no models. Sign in to a provider or start Ollama.");
    }

    /// <summary>Parses 'opencode models': one "provider/model" per line. The "-free" suffix is OpenCode's own naming for its free models.</summary>
    internal static IReadOnlyList<ModelInfo> ParseModels(string output)
    {
        var models = new List<ModelInfo>();
        foreach (var raw in output.Split('\n'))
        {
            var id = raw.Trim();
            if (id.Length == 0 || !id.Contains('/') || !ModelName.IsValid(id) || models.Any(model => model.Id == id)) continue;
            var provider = id[..id.IndexOf('/')];
            var local = provider == "ollama" && !OllamaAdapter.IsCloudModel(id);
            var free = provider == "opencode" && id.EndsWith("-free", StringComparison.OrdinalIgnoreCase);
            models.Add(new ModelInfo
            {
                Id = id, DisplayName = id[(id.IndexOf('/') + 1)..], Provider = provider == "opencode" ? "OpenCode Zen" : provider,
                Location = local ? PrivacyKind.Local : PrivacyKind.Cloud,
                CostHint = local ? "Local" : free ? "Free (OpenCode Zen)" : null,
            });
        }
        // Local first, then free, then the rest: the cheapest useful choice is on top.
        return models.OrderBy(model => model.Location == PrivacyKind.Local ? 0 : model.CostHint is not null ? 1 : 2).ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static List<string> BuildArguments(AgentInvocation invocation)
    {
        var args = new List<string> { "run", "--format", "json" };
        if (ModelName.IsValid(invocation.Model)) args.AddRange(["--model", invocation.Model!]);
        // Read-only turns use OpenCode's built-in read-only "plan" agent.
        if (invocation.AllowWrites) args.Add("--auto");
        else args.AddRange(["--agent", "plan"]);
        foreach (var file in invocation.Attachments.SelectMany(Agex.Core.Attachments.AttachmentService.ReadableFiles)) args.AddRange(["--file", file]);
        return args;
    }

    public override async Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = "OpenCode is not installed.", FallbackEligible = true };
        var text = new StringBuilder();
        string? error = null;
        var tools = 0;
        UsageReport? usage = null;
        var run = await Runner.RunAsync(new ProcessRequest
        {
            FileName = detection.Path,
            Arguments = BuildArguments(invocation),
            WorkingDirectory = invocation.WorkingDirectory,
            StdinText = WithSkills(invocation.Prompt, invocation.Skills),
            Timeout = invocation.Timeout,
            Label = $"OpenCode {invocation.Label}",
            OnStarted = handle => invocation.OnProcessStarted?.Invoke(handle.Pid),
            OnStdoutLine = line =>
            {
                if (TryParseJson(line) is not { } evt) return;
                var part = Obj(evt, "part");
                switch (Str(evt, "type"))
                {
                    case "text" when part is { } p && Str(p, "text") is { } chunk:
                        // Only the explicit answer text; "reasoning" parts are never used.
                        text.Append(chunk);
                        break;
                    case "tool_use" when part is { } p:
                        tools++;
                        var input = Obj(p, "state") is { } state ? Obj(state, "input") : null;
                        var target = input is { } i ? Str(i, "filePath") ?? Str(i, "path") ?? Str(i, "command") ?? Str(i, "pattern") ?? Str(i, "url") ?? "" : "";
                        invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.ToolStarted, $"{Str(p, "tool")}{(target.Length > 0 ? ": " + Redactor.Redact(target.Length > 120 ? target[..117] + "..." : target) : "")}"));
                        break;
                    case "step_finish" when part is { } p && Obj(p, "tokens") is { } tokens:
                        var cache = Obj(tokens, "cache");
                        usage = UsageReport.Combine(usage, new UsageReport
                        {
                            InputTokens = Num(tokens, "input"), OutputTokens = Num(tokens, "output"),
                            CachedInputTokens = cache is { } c ? Num(c, "read") : null, ReasoningTokens = Num(tokens, "reasoning"),
                            CostUsd = p.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number && cost.GetDecimal() > 0 ? cost.GetDecimal() : null, Source = "OpenCode",
                        });
                        break;
                    case "error":
                        error = Obj(evt, "error") is { } e ? Str(e, "message") ?? (Obj(e, "data") is { } d ? Str(d, "message") : null) : Str(evt, "message");
                        break;
                }
            },
        }, cancellationToken).ConfigureAwait(false);

        var answer = text.ToString().Trim();
        if (error is null && answer.Length > 0 && run.Outcome == ProcessOutcome.Ok)
            return new AgentRunResult { Outcome = RunOutcome.Ok, Text = answer, Usage = usage, CommandLine = run.CommandLine, Pid = run.Pid, ExitCode = run.ExitCode, Duration = run.Duration };
        var failure = FailureFrom(run, error);
        return failure with { Usage = usage, FallbackEligible = failure.FallbackEligible && tools == 0 };
    }
}
