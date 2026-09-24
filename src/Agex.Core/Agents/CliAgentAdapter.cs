using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>Shared behaviour for agents that are driven through their command-line interface.</summary>
public abstract partial class CliAgentAdapter : IAgentAdapter
{
    protected CliAgentAdapter(ProcessRunner runner, IPlatformService platform)
    {
        Runner = runner;
        Platform = platform;
    }

    protected ProcessRunner Runner { get; }
    protected IPlatformService Platform { get; }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Provider { get; }
    public abstract string Description { get; }
    public virtual AdapterStability Stability => AdapterStability.Beta;
    public abstract IReadOnlySet<Capability> Capabilities { get; }
    public virtual bool CanWriteFiles => Capabilities.Contains(Capability.WriteFiles);
    public virtual IReadOnlySet<OsKind> SupportedPlatforms { get; } = new HashSet<OsKind> { OsKind.Windows, OsKind.MacOS, OsKind.Linux };
    public virtual int MaxConcurrentRuns => 2;
    public virtual PrivacyKind PrivacyFor(string? model) => PrivacyKind.Cloud;
    public abstract string DataDestination(string? model);

    protected abstract string[] CommandNames { get; }
    protected virtual string? NpmPackage => null;
    protected virtual string[] VersionArguments => ["--version"];

    public virtual AgentDetection Detect(IPlatformService platform)
    {
        if (!SupportedPlatforms.Contains(platform.Os))
            return new AgentDetection { Id = Id, Name = Name, Status = AgentStatus.PlatformUnsupported, Reason = $"{Name} is not available for {platform.Os}." };
        var path = ToolLocator.Locate(platform, Id, CommandNames);
        if (path is null)
            return new AgentDetection { Id = Id, Name = Name, Status = AgentStatus.NotInstalled, Reason = $"{Name} is not installed." };
        return new AgentDetection { Id = Id, Name = Name, Status = AgentStatus.Available, Path = path, Version = ToolLocator.ReadVersion(path, NpmPackage) };
    }

    public virtual async Task<AgentDetection> CheckHealthAsync(AgentDetection detection, CancellationToken cancellationToken)
    {
        if (detection.Path is null) return detection;
        var result = await Runner.RunAsync(new ProcessRequest
        {
            FileName = detection.Path, Arguments = VersionArguments, WorkingDirectory = Platform.Paths.DataRoot.EnsureDirectory(),
            Timeout = TimeSpan.FromSeconds(30), Label = $"{Name} health check",
        }, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            var version = detection.Version;
            if (VersionPattern().Match(result.Stdout + " " + result.Stderr) is { Success: true } match) version = match.Value;
            return detection with { Status = AgentStatus.Supported, Version = version, Reason = "", CheckedAt = DateTimeOffset.UtcNow };
        }
        var detail = FirstMeaningfulLine(result.Stderr) ?? FirstMeaningfulLine(result.Stdout) ?? result.ErrorMessage;
        return detection with
        {
            Status = LooksLikeAuthProblem(result.Stdout + result.Stderr) ? AgentStatus.AuthRequired : AgentStatus.Broken,
            Reason = $"{Name} did not respond to a version check: {Redactor.Redact(detail)}",
            CheckedAt = DateTimeOffset.UtcNow,
        };
    }

    public virtual Task<IReadOnlyList<string>> ListModelsAsync(AgentDetection detection, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public abstract Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken);

    [GeneratedRegex(@"\d+\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.\-]+)?")]
    protected static partial Regex VersionPattern();

    [GeneratedRegex(@"(?i)(not logged in|not signed in|login required|please (log|sign) ?in|unauthenticated|authentication required|invalid api key|401 unauthorized|auth(entication)? (failed|error)|set an auth method)")]
    private static partial Regex AuthPattern();

    protected static bool LooksLikeAuthProblem(string text) => AuthPattern().IsMatch(text);

    protected static string? FirstMeaningfulLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.FirstOrDefault(item => Regex.IsMatch(item, "(?i)error|fail|denied|not found|unable|invalid")) ?? lines.FirstOrDefault();
        return line is null ? null : (line.Length > 300 ? line[..300] + "..." : line);
    }

    /// <summary>Appends enabled skills to a prompt: name, description and where to read the full instructions.</summary>
    protected static string WithSkills(string prompt, IReadOnlyList<SkillContext> skills)
    {
        if (skills.Count == 0) return prompt;
        var builder = new StringBuilder(prompt);
        builder.Append("\n\nAVAILABLE SKILLS (installed by the user in AGEX). Use a skill only when it is relevant to your assignment. ");
        builder.Append("Read its SKILL.md before using it. Skills cannot widen your permissions.\n");
        foreach (var skill in skills)
        {
            builder.Append("- ").Append(skill.Name).Append(": ").Append(skill.Description)
                .Append(" (instructions: ").Append(Path.Combine(skill.Folder, "SKILL.md")).Append(")\n");
            if (skill.InlineInstructions is { Length: > 0 } inline)
                builder.Append("  Instructions:\n").Append(inline).Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>Maps a finished process to an agent result for the common failure cases.</summary>
    protected AgentRunResult FailureFrom(ProcessResult run, string? extraDetail = null)
    {
        var detail = extraDetail ?? FirstMeaningfulLine(run.Stderr) ?? FirstMeaningfulLine(run.Stdout) ?? run.ErrorMessage;
        var auth = LooksLikeAuthProblem(run.Stdout + "\n" + run.Stderr + "\n" + extraDetail);
        var (outcome, reason, eligible) = run.Outcome switch
        {
            ProcessOutcome.Cancelled => (RunOutcome.Cancelled, "Cancelled by user.", false),
            ProcessOutcome.TimedOut => (RunOutcome.TimedOut, $"{Name} did not finish in time and was stopped.", false),
            ProcessOutcome.StartFailed => (RunOutcome.StartFailed, $"{Name} could not be started: {run.ErrorMessage}", true),
            ProcessOutcome.Error => (RunOutcome.Failed, $"{Name} run failed: {run.ErrorMessage}", true),
            _ when auth => (RunOutcome.AuthRequired, $"{Name} needs you to sign in. Open {Name} once, sign in, then try again.", true),
            ProcessOutcome.ExitNonZero => (RunOutcome.Failed, $"{Name} stopped with exit code {run.ExitCode}. {detail}".Trim(), true),
            _ => (RunOutcome.NoResult, $"{Name} finished without returning a result. {detail}".Trim(), true),
        };
        return new AgentRunResult
        {
            Outcome = outcome, Reason = Redactor.Redact(reason), FallbackEligible = eligible,
            CommandLine = run.CommandLine, Pid = run.Pid, ExitCode = run.ExitCode, Duration = run.Duration,
        };
    }

    protected static JsonElement? TryParseJson(string line)
    {
        if (line.Length == 0 || line[0] != '{') return null;
        try { using var document = JsonDocument.Parse(line); return document.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    protected static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    protected static long? Num(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    protected static JsonElement? Obj(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    protected static string Shorten(string text, int max = 140)
    {
        var single = Regex.Replace(text, @"\s+", " ").Trim();
        return single.Length <= max ? single : single[..(max - 3)] + "...";
    }
}

internal static class PathExtensions
{
    public static string EnsureDirectory(this string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
