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

    public override AgentSetupInfo Setup { get; } = new()
    {
        Method = InstallMethod.Npm,
        NpmPackage = "@anthropic-ai/claude-code",
        OfficialUrl = "https://code.claude.com/docs/en/setup",
        ManualCommands = new Dictionary<OsKind, string>
        {
            [OsKind.Windows] = "irm https://claude.ai/install.ps1 | iex      (in PowerShell; or: winget install Anthropic.ClaudeCode)",
            [OsKind.MacOS] = "curl -fsSL https://claude.ai/install.sh | bash      (or: brew install --cask claude-code)",
            [OsKind.Linux] = "curl -fsSL https://claude.ai/install.sh | bash",
        },
        WhatGetsInstalled = "Claude Code through Anthropic's npm package @anthropic-ai/claude-code (it downloads the native Claude Code program). Anthropic recommends Node.js 22 or later for this method.",
        SizeHint = "about 200 MB",
        AdminNote = "No administrator rights on Windows. On macOS/Linux, do not use sudo; if npm reports a permission error, use Anthropic's native installer instead.",
        AccountNote = "Needs a Claude Pro, Max, Team, Enterprise or Console account (the free plan does not include Claude Code). Your requests and the files it reads go to Anthropic.",
        LoginArguments = [],
        LoginInstructions = "A terminal opens and starts 'claude'. Follow its browser sign-in (or type /login). Claude Code stores the sign-in itself. Close the terminal afterwards.",
        LoginDocsUrl = "https://code.claude.com/docs/en/authentication",
    };

    public override Task<AuthCheck> CheckAuthAsync(AgentDetection detection, CancellationToken cancellationToken) =>
        Task.FromResult(detection.Path is null ? AuthCheck.Unknown : AuthFromMarkers(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir ? dir : Path.Combine(UserHome, ".claude"), UserHome, Platform.Os, name => HasEnvironment(name)));

    /// <summary>
    /// Claude Code has no sign-in status command. AGEX looks only for the presence
    /// of its sign-in markers (never their contents): an API key or OAuth token in
    /// the environment, the credentials file (Windows/Linux), or the account entry
    /// that accompanies a Keychain sign-in (macOS).
    /// </summary>
    internal static AuthCheck AuthFromMarkers(string configDir, string home, OsKind os, Func<string, bool> hasEnvironment)
    {
        if (hasEnvironment("ANTHROPIC_API_KEY") || hasEnvironment("ANTHROPIC_AUTH_TOKEN") || hasEnvironment("CLAUDE_CODE_OAUTH_TOKEN"))
            return new AuthCheck(AuthState.SignedIn, "", "API key or token from the environment");
        if (File.Exists(Path.Combine(configDir, ".credentials.json"))) return new AuthCheck(AuthState.SignedIn, "", "Claude account");
        if (os == OsKind.MacOS)
        {
            try
            {
                var state = Path.Combine(home, ".claude.json");
                if (File.Exists(state))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(state));
                    if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("oauthAccount", out _))
                        return new AuthCheck(AuthState.SignedIn, "Signed in through the macOS Keychain.", "Claude account");
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return AuthCheck.Unknown; }
        }
        return new AuthCheck(AuthState.SignedOut, "Claude Code is not signed in.");
    }

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
        foreach (var folder in AttachmentFolders(invocation)) args.AddRange(["--add-dir", folder]);
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
