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

    public override AgentSetupInfo Setup { get; } = new()
    {
        Method = InstallMethod.Npm,
        NpmPackage = "@google/gemini-cli",
        OfficialUrl = "https://github.com/google-gemini/gemini-cli#-installation",
        ManualCommands = new Dictionary<OsKind, string>
        {
            [OsKind.Windows] = "npm install -g @google/gemini-cli",
            [OsKind.MacOS] = "npm install -g @google/gemini-cli   (or: brew install gemini-cli)",
            [OsKind.Linux] = "npm install -g @google/gemini-cli",
        },
        WhatGetsInstalled = "The Gemini CLI (npm package @google/gemini-cli) in your npm global folder.",
        SizeHint = "about 150 MB",
        AdminNote = "Usually no administrator rights. On macOS/Linux, npm needs them only if Node.js was installed system-wide.",
        AccountNote = "Needs a Google account (free tier available) or a Gemini API key. Your requests and the files it reads go to Google.",
        LoginArguments = [],
        LoginInstructions = "A terminal opens and starts 'gemini'. Choose 'Login with Google' and finish in your browser; Gemini CLI stores the sign-in itself. Close the terminal afterwards.",
        LoginDocsUrl = "https://github.com/google-gemini/gemini-cli#-authentication-options",
    };

    public override Task<AuthCheck> CheckAuthAsync(AgentDetection detection, CancellationToken cancellationToken) =>
        Task.FromResult(detection.Path is null ? AuthCheck.Unknown : AuthFromMarkers(Path.Combine(UserHome, ".gemini"), name => HasEnvironment(name)));

    /// <summary>
    /// Gemini CLI has no sign-in status command. AGEX reads only which sign-in
    /// method is selected in ~/.gemini/settings.json, whether the matching key
    /// variable is set, and whether the Google sign-in file exists (never its contents).
    /// </summary>
    internal static AuthCheck AuthFromMarkers(string geminiDir, Func<string, bool> hasEnvironment)
    {
        if (hasEnvironment("GEMINI_API_KEY")) return new AuthCheck(AuthState.SignedIn, "", "Gemini API key from the environment");
        if (hasEnvironment("GOOGLE_GENAI_USE_VERTEXAI") || hasEnvironment("GOOGLE_GENAI_USE_GCA")) return new AuthCheck(AuthState.SignedIn, "", "Google Cloud from the environment");
        string? selected = null;
        try
        {
            var settings = Path.Combine(geminiDir, "settings.json");
            if (File.Exists(settings))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(settings), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = document.RootElement;
                selected = Str(root, "selectedAuthType")
                    ?? (Obj(root, "security") is { } security && Obj(security, "auth") is { } auth ? Str(auth, "selectedType") : null);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return AuthCheck.Unknown; }
        var googleSignIn = File.Exists(Path.Combine(geminiDir, "oauth_creds.json"));
        return selected switch
        {
            "oauth-personal" when googleSignIn => new AuthCheck(AuthState.SignedIn, "", "Google account"),
            "gemini-api-key" => new AuthCheck(AuthState.SignedOut, "Gemini CLI is set to use an API key, but GEMINI_API_KEY is not set."),
            null or "" => new AuthCheck(AuthState.SignedOut, "Gemini CLI has no sign-in method yet."),
            "vertex-ai" or "cloud-shell" => new AuthCheck(AuthState.Unknown, "Gemini CLI uses Google Cloud credentials, which AGEX cannot check without a request."),
            _ when googleSignIn => new AuthCheck(AuthState.SignedIn, "", "Google account"),
            _ => new AuthCheck(AuthState.SignedOut, "Gemini CLI is not signed in."),
        };
    }

    internal static List<string> BuildArguments(AgentInvocation invocation)
    {
        // Non-interactive runs cannot answer approval prompts; the approval mode
        // decides which tools are available at all.
        var mode = !invocation.AllowWrites ? "default" : invocation.AllowCommands ? "yolo" : "auto_edit";
        var args = new List<string> { "--output-format", "json", "--approval-mode", mode };
        if (ModelName.IsValid(invocation.Model)) args.AddRange(["--model", invocation.Model!]);
        foreach (var folder in invocation.Skills.Select(skill => skill.Folder).Distinct()) args.AddRange(["--include-directories", folder]);
        foreach (var folder in AttachmentFolders(invocation)) args.AddRange(["--include-directories", folder]);
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
                    usage = UsageReport.Combine(usage, new UsageReport { InputTokens = Num(tokens, "prompt"), OutputTokens = Num(tokens, "candidates"), CachedInputTokens = Num(tokens, "cached"), ReasoningTokens = Num(tokens, "thoughts"), Source = "Gemini CLI" });
                }
            }
            return (text, error, usage);
        }
        catch (JsonException) { return (null, null, null); }
    }
}
