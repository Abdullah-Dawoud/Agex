using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>
/// OpenAI Codex CLI through <c>codex exec</c> (documented non-interactive
/// mode). The prompt goes through stdin; events are read from <c>--json</c>.
/// </summary>
public sealed partial class CodexAdapter(ProcessRunner runner, IPlatformService platform) : CliAgentAdapter(runner, platform)
{
    public override string Id => "codex";
    public override string Name => "Codex";
    public override string Provider => "OpenAI";
    public override string Description => "Coding, code review and planning.";
    public override AdapterStability Stability => AdapterStability.Stable;
    public override IReadOnlySet<Capability> Capabilities { get; } = new HashSet<Capability>
    {
        Capability.ReadFiles, Capability.WriteFiles, Capability.RunCommands, Capability.CodeReview, Capability.Testing,
        Capability.Planning, Capability.Debugging, Capability.Mcp, Capability.Skills,
    };
    // Parallel Codex runs in one project have caused state conflicts; keep one at a time.
    public override int MaxConcurrentRuns => 1;
    public override string DataDestination(string? model) => "OpenAI cloud (Codex)";
    protected override string[] CommandNames => ["codex"];
    protected override string? NpmPackage => "@openai/codex";

    public override AgentSetupInfo Setup { get; } = new()
    {
        Method = InstallMethod.Npm,
        NpmPackage = "@openai/codex",
        OfficialUrl = "https://github.com/openai/codex#installing-and-running-codex-cli",
        ManualCommands = new Dictionary<OsKind, string>
        {
            [OsKind.Windows] = "npm install -g @openai/codex",
            [OsKind.MacOS] = "npm install -g @openai/codex   (or: brew install --cask codex)",
            [OsKind.Linux] = "npm install -g @openai/codex",
        },
        WhatGetsInstalled = "The Codex CLI (npm package @openai/codex) in your npm global folder.",
        SizeHint = "about 100 MB",
        AdminNote = "Usually no administrator rights. On macOS/Linux, npm needs them only if Node.js was installed system-wide.",
        AccountNote = "Needs a ChatGPT plan or an OpenAI API key. Your requests and the files Codex reads go to OpenAI.",
        LoginArguments = ["login"],
        LoginInstructions = "A terminal opens and runs 'codex login'. Your browser opens the OpenAI sign-in page; Codex stores the sign-in itself. AGEX never sees your password.",
        LoginDocsUrl = "https://github.com/openai/codex#using-codex-with-your-chatgpt-plan",
    };

    public override async Task<AuthCheck> CheckAuthAsync(AgentDetection detection, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return AuthCheck.Unknown;
        // 'codex login status' reads Codex's own stored sign-in; it uses no model quota.
        var result = await RunQuietAsync(detection, ["login", "status"], "sign-in check", cancellationToken, 20).ConfigureAwait(false);
        var text = (result.Stdout + "\n" + result.Stderr).Trim();
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(item => item.StartsWith("Logged in", StringComparison.OrdinalIgnoreCase)) ?? "";
        if (result.Succeeded && line.Length > 0) return new AuthCheck(AuthState.SignedIn, "", Redactor.Redact(line.Replace("Logged in using ", "", StringComparison.OrdinalIgnoreCase)));
        if (text.Contains("Not logged in", StringComparison.OrdinalIgnoreCase) || result.Outcome == ProcessOutcome.ExitNonZero)
            return new AuthCheck(AuthState.SignedOut, "Codex is not signed in.");
        return new AuthCheck(AuthState.Unknown, FirstMeaningfulLine(text) ?? "Codex did not report its sign-in state.");
    }

    public override async Task<ModelDiscovery> GetModelsAsync(AgentDetection detection, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return ModelDiscovery.Unavailable("Codex is not installed.");
        // Codex's own model catalog. 'codex debug' is marked as a debugging tool, so
        // a change in its format is reported as "could not read", never guessed.
        const string source = "codex debug models";
        var result = await RunQuietAsync(detection, ["debug", "models"], "model list", cancellationToken, 45).ConfigureAwait(false);
        if (!result.Succeeded) return ModelDiscovery.Failed("Codex could not list its models: " + Redactor.Redact(FirstMeaningfulLine(result.Stderr) ?? FirstMeaningfulLine(result.Stdout) ?? result.ErrorMessage), source);
        try { return ModelDiscovery.From(ParseModels(result.Stdout), source, "Codex reported no models."); }
        catch (JsonException) { return ModelDiscovery.Failed("Codex's model list is in a format AGEX does not understand.", source); }
    }

    /// <summary>Parses the JSON of 'codex debug models'. Only models Codex itself lists (visibility "list") are returned.</summary>
    internal static IReadOnlyList<ModelInfo> ParseModels(string json)
    {
        var start = json.IndexOf('{');
        if (start < 0) throw new JsonException("No JSON object.");
        using var document = JsonDocument.Parse(json[start..]);
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) throw new JsonException("No models array.");
        var list = new List<(int Priority, ModelInfo Model)>();
        foreach (var model in models.EnumerateArray())
        {
            var id = Str(model, "slug");
            if (id is null || !ModelName.IsValid(id)) continue;
            if (Str(model, "visibility") is { } visibility && visibility != "list") continue;
            var efforts = model.TryGetProperty("supported_reasoning_levels", out var levels) && levels.ValueKind == JsonValueKind.Array
                ? levels.EnumerateArray().Select(level => Str(level, "effort")).OfType<string>().ToList() : [];
            var priority = model.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : int.MaxValue;
            list.Add((priority, new ModelInfo
            {
                Id = id, DisplayName = Str(model, "display_name") ?? id, Provider = "OpenAI", Description = Str(model, "description") ?? "",
                ContextWindow = Num(model, "context_window"), Location = PrivacyKind.Cloud, Efforts = efforts,
            }));
        }
        return list.OrderBy(item => item.Priority).Select(item => item.Model).ToList();
    }

    private static readonly string[] Efforts = ["minimal", "low", "medium", "high", "xhigh"];
    private static readonly JsonSerializerOptions TomlStrings = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static List<string> BuildArguments(AgentInvocation invocation, string lastMessagePath)
    {
        var args = new List<string>
        {
            "exec", "--ephemeral", "--skip-git-repo-check", "--json",
            "-C", invocation.WorkingDirectory,
            "--sandbox", invocation.AllowWrites ? "workspace-write" : "read-only",
            "-o", lastMessagePath,
        };
        if (ModelName.IsValid(invocation.Model)) args.AddRange(["--model", invocation.Model!]);
        if (invocation.Effort is { } effort && Efforts.Contains(effort)) args.AddRange(["-c", $"model_reasoning_effort=\"{effort}\""]);
        foreach (var server in invocation.McpServers)
        {
            if (!ModelName.IsSafeKey(server.Name)) continue;
            var prefix = "mcp_servers." + server.Name;
            if (server.IsRemote)
            {
                args.AddRange(["-c", $"{prefix}.url={Toml(server.Url!)}"]);
                // Codex reads the token from this environment variable; the value never appears in arguments.
                if (server.BearerEnvironmentVariable is { Length: > 0 } bearer) args.AddRange(["-c", $"{prefix}.bearer_token_env_var={Toml(bearer)}"]);
                continue;
            }
            args.AddRange(["-c", $"{prefix}.command={Toml(server.Command)}"]);
            args.AddRange(["-c", $"{prefix}.args=[{string.Join(", ", server.Arguments.Select(Toml))}]"]);
            // Secrets are passed by environment variable name, never as a value on the command line.
            if (server.SecretEnvironment.Count > 0)
                args.AddRange(["-c", $"{prefix}.env_vars=[{string.Join(", ", server.SecretEnvironment.Keys.Select(Toml))}]"]);
        }
        args.Add("-");
        return args;
    }

    private static string Toml(string value) => JsonSerializer.Serialize(value, TomlStrings);

    public override async Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = "Codex is not installed.", FallbackEligible = true };
        var lastMessage = Path.Combine(Platform.Paths.Temp.EnsureDirectory(), $"codex-{Guid.NewGuid():N}.txt");
        var messages = new List<string>();
        UsageReport? usage = null;
        string? streamError = null;
        var environment = Platform.ChildEnvironment();
        foreach (var server in invocation.McpServers)
            foreach (var (name, value) in server.SecretEnvironment) environment[name] = value;

        try
        {
            var run = await Runner.RunAsync(new ProcessRequest
            {
                FileName = detection.Path,
                Arguments = BuildArguments(invocation, lastMessage),
                WorkingDirectory = invocation.WorkingDirectory,
                StdinText = WithSkills(invocation.Prompt, invocation.Skills),
                Timeout = invocation.Timeout,
                Environment = environment,
                Label = $"Codex {invocation.Label}",
                OnStarted = handle => invocation.OnProcessStarted?.Invoke(handle.Pid),
                OnStdoutLine = line =>
                {
                    if (TryParseJson(line) is not { } evt) return;
                    var type = Str(evt, "type") ?? "";
                    if (Obj(evt, "item") is { } item) HandleItem(type, item, invocation, messages);
                    else if (type == "turn.completed" && Obj(evt, "usage") is { } u)
                        usage = UsageReport.Combine(usage, new UsageReport { InputTokens = Num(u, "input_tokens"), OutputTokens = Num(u, "output_tokens"), CachedInputTokens = Num(u, "cached_input_tokens"), Source = "Codex" });
                    else if (type is "turn.failed" or "error")
                        streamError = Str(evt, "message") ?? (Obj(evt, "error") is { } e ? Str(e, "message") : null) ?? streamError;
                },
            }, cancellationToken).ConfigureAwait(false);

            var text = File.Exists(lastMessage) ? (await File.ReadAllTextAsync(lastMessage, cancellationToken).ConfigureAwait(false)).Trim() : "";
            if (text.Length == 0 && messages.Count > 0) text = messages[^1].Trim();
            if (run.Outcome == ProcessOutcome.Ok && text.Length > 0)
                return new AgentRunResult { Outcome = RunOutcome.Ok, Text = text, Usage = usage, CommandLine = run.CommandLine, Pid = run.Pid, ExitCode = run.ExitCode, Duration = run.Duration };
            var failure = FailureFrom(run, streamError);
            return failure with { Usage = usage, FallbackEligible = failure.FallbackEligible && usage is null };
        }
        finally
        {
            try { File.Delete(lastMessage); } catch (IOException) { }
        }
    }

    private static void HandleItem(string type, JsonElement item, AgentInvocation invocation, List<string> messages)
    {
        var kind = Str(item, "type") ?? Str(item, "item_type") ?? "";
        var started = type == "item.started";
        var completed = type == "item.completed";
        switch (kind)
        {
            case "agent_message" when completed:
                if (Str(item, "text") is { Length: > 0 } text) messages.Add(text);
                break;
            case "command_execution":
                if (Str(item, "command") is { } command)
                    invocation.OnActivity?.Invoke(new AgentActivity(started ? ActivityKind.ToolStarted : ActivityKind.ToolFinished,
                        (started ? "Running " : "Ran ") + Shorten(Redactor.Redact(command))));
                break;
            case "file_change" when completed:
                if (item.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
                    foreach (var change in changes.EnumerateArray())
                        invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.ToolFinished, $"{Capitalize(Str(change, "kind") ?? "changed")} {Str(change, "path")}"));
                break;
            case "mcp_tool_call" when started:
                invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.ToolStarted, $"Using {Str(item, "server")}: {Str(item, "tool")}"));
                break;
            case "web_search" when started:
                invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.ToolStarted, "Searching the web: " + Shorten(Str(item, "query") ?? "")));
                break;
            // "reasoning" items are the model's internal reasoning summary and are never shown.
        }
    }

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

/// <summary>Validation for values that end up in agent command lines.</summary>
public static partial class ModelName
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:/\-]{0,99}$")]
    private static partial Regex Pattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-]{0,63}$")]
    private static partial Regex KeyPattern();

    public static bool IsValid(string? model) => model is not null && Pattern().IsMatch(model);
    public static bool IsSafeKey(string? key) => key is not null && KeyPattern().IsMatch(key);
}
