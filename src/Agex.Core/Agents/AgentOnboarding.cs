using System.Collections.Concurrent;
using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>What the user sees for one agent: installation and sign-in combined.</summary>
public enum AgentReadiness
{
    InstalledReady,
    InstalledAuthRequired,
    InstalledBroken,
    NotInstalled,
    PlatformUnsupported,
    DetectedUnsupported,
    /// <summary>Installed; the version or sign-in check has not finished yet.</summary>
    Checking,
}

public enum AuthState
{
    /// <summary>AGEX cannot tell without using the agent (no status command, no reliable marker).</summary>
    Unknown,
    SignedIn,
    SignedOut,
    /// <summary>The agent needs no account (for example local Ollama models).</summary>
    NotRequired,
}

/// <summary>
/// Result of a sign-in check. <see cref="Identity"/> is minimal and only what the
/// agent itself prints (for example "Logged in using ChatGPT"); never a token.
/// </summary>
public sealed record AuthCheck(AuthState State, string Detail = "", string Identity = "")
{
    public static readonly AuthCheck Unknown = new(AuthState.Unknown);
}

public enum InstallMethod
{
    /// <summary>The vendor publishes the agent as an npm package: AGEX can run the install after the user confirms.</summary>
    Npm,
    /// <summary>The vendor's installer is a script, app or store package: AGEX shows the official instructions.</summary>
    Manual,
}

/// <summary>
/// How an agent is installed and signed in, from the vendor's official
/// documentation. AGEX never downloads installers from anywhere else.
/// </summary>
public sealed record AgentSetupInfo
{
    public InstallMethod Method { get; init; } = InstallMethod.Manual;
    /// <summary>npm package (only for <see cref="InstallMethod.Npm"/>).</summary>
    public string? NpmPackage { get; init; }
    /// <summary>The vendor's installation page.</summary>
    public required string OfficialUrl { get; init; }
    /// <summary>The vendor's documented commands per system, shown for manual installs.</summary>
    public IReadOnlyDictionary<OsKind, string> ManualCommands { get; init; } = new Dictionary<OsKind, string>();
    public string WhatGetsInstalled { get; init; } = "";
    public string SizeHint { get; init; } = "";
    public string AdminNote { get; init; } = "No administrator rights needed.";
    public bool RequiresRestart { get; init; }
    public string AccountNote { get; init; } = "";
    /// <summary>Arguments for the agent's own sign-in command, run in a visible terminal. Null when there is none.</summary>
    public IReadOnlyList<string>? LoginArguments { get; init; }
    public string LoginInstructions { get; init; } = "";
    public string LoginDocsUrl { get; init; } = "";
    public bool CanInstall => Method == InstallMethod.Npm && NpmPackage is { Length: > 0 };
    public bool CanSignIn => LoginArguments is not null;
}

public enum ModelDiscoveryStatus { Ok, Empty, Unavailable, Failed, NotReady }

/// <summary>A model exactly as the agent or runtime describes it. Unknown fields stay null.</summary>
public sealed record ModelInfo
{
    public required string Id { get; init; }
    public string DisplayName { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public long? ContextWindow { get; init; }
    public PrivacyKind Location { get; init; } = PrivacyKind.Cloud;
    /// <summary>Only when the agent itself marks the model as its default or recommended choice.</summary>
    public bool Recommended { get; init; }
    public string Availability { get; init; } = "Available";
    public string? CostHint { get; init; }
    public string? SpeedHint { get; init; }
    /// <summary>Reasoning efforts the agent reports for this model.</summary>
    public IReadOnlyList<string> Efforts { get; init; } = [];
    /// <summary>True/false only when the agent or runtime reports it; null = not reported.</summary>
    public bool? Vision { get; init; }
    public bool? Tools { get; init; }
    public bool? Reasoning { get; init; }
    /// <summary>"Name (id)", or just the id when the name only repeats it (for example "provider/name").</summary>
    public string Label => DisplayName.Length > 0 && DisplayName != Id && !Id.EndsWith("/" + DisplayName, StringComparison.Ordinal) ? $"{DisplayName} ({Id})" : Id;

    /// <summary>Short facts for the model picker, only from reported data.</summary>
    public string Facts
    {
        get
        {
            var facts = new List<string>();
            if (Location == PrivacyKind.Local) facts.Add("local");
            if (CostHint is { Length: > 0 } cost && cost != "Local") facts.Add(cost);
            if (Vision == true) facts.Add("sees images");
            if (Tools == true) facts.Add("tools");
            if (Reasoning == true || Efforts.Count > 0) facts.Add("reasoning");
            if (ContextWindow is { } context) facts.Add($"{context / 1000:N0}k context");
            return string.Join(" · ", facts);
        }
    }
}

public sealed record ModelDiscovery(ModelDiscoveryStatus Status, IReadOnlyList<ModelInfo> Models, string Message, string Source, DateTimeOffset RetrievedAt)
{
    public static ModelDiscovery Unavailable(string message) => new(ModelDiscoveryStatus.Unavailable, [], message, "", DateTimeOffset.UtcNow);
    public static ModelDiscovery Failed(string message, string source = "") => new(ModelDiscoveryStatus.Failed, [], message, source, DateTimeOffset.UtcNow);
    /// <summary>Version of the agent that produced the list; a new version makes the list stale.</summary>
    public string AgentVersion { get; init; } = "";
    public static ModelDiscovery From(IReadOnlyList<ModelInfo> models, string source, string emptyMessage) =>
        new(models.Count > 0 ? ModelDiscoveryStatus.Ok : ModelDiscoveryStatus.Empty, models, models.Count > 0 ? "" : emptyMessage, source, DateTimeOffset.UtcNow);
}

public static class AgentReadinessText
{
    public static AgentReadiness From(AgentStatus status, AuthCheck? auth) => status switch
    {
        AgentStatus.NotInstalled => AgentReadiness.NotInstalled,
        AgentStatus.PlatformUnsupported => AgentReadiness.PlatformUnsupported,
        AgentStatus.DetectedUnsupported => AgentReadiness.DetectedUnsupported,
        AgentStatus.Broken => AgentReadiness.InstalledBroken,
        AgentStatus.AuthRequired => AgentReadiness.InstalledAuthRequired,
        AgentStatus.Supported when auth?.State == AuthState.SignedOut => AgentReadiness.InstalledAuthRequired,
        AgentStatus.Supported => AgentReadiness.InstalledReady,
        AgentStatus.Available when auth?.State == AuthState.SignedOut => AgentReadiness.InstalledAuthRequired,
        _ => AgentReadiness.Checking,
    };

    public static string Code(AgentReadiness readiness) => readiness switch
    {
        AgentReadiness.InstalledReady => "INSTALLED_READY",
        AgentReadiness.InstalledAuthRequired => "INSTALLED_AUTH_REQUIRED",
        AgentReadiness.InstalledBroken => "INSTALLED_BROKEN",
        AgentReadiness.NotInstalled => "NOT_INSTALLED",
        AgentReadiness.PlatformUnsupported => "PLATFORM_UNSUPPORTED",
        AgentReadiness.DetectedUnsupported => "DETECTED_UNSUPPORTED",
        _ => "CHECKING",
    };

    public static string Label(AgentReadiness readiness) => readiness switch
    {
        AgentReadiness.InstalledReady => "Ready",
        AgentReadiness.InstalledAuthRequired => "Sign-in required",
        AgentReadiness.InstalledBroken => "Not working",
        AgentReadiness.NotInstalled => "Not installed",
        AgentReadiness.PlatformUnsupported => "Not available on this system",
        AgentReadiness.DetectedUnsupported => "Detected - not integrated",
        _ => "Checking...",
    };

    public static string Auth(AuthCheck? auth) => auth?.State switch
    {
        AuthState.SignedIn => "Signed in",
        AuthState.SignedOut => "Sign-in required",
        AuthState.NotRequired => "No account needed",
        _ => "Sign-in not checked",
    };
}

/// <summary>Chooses the model to send: the saved one, or Auto when it disappeared from the agent's list.</summary>
public static class ModelSelection
{
    /// <param name="saved">The user's saved model id ("" = Auto).</param>
    /// <param name="custom">True when the user typed the id under Advanced; such ids are never replaced.</param>
    public static (string? Model, bool Disappeared) Resolve(string saved, bool custom, ModelDiscovery? discovery)
    {
        if (saved.Length == 0) return (null, false);
        if (custom || discovery is not { Status: ModelDiscoveryStatus.Ok }) return (saved, false);
        return discovery.Models.Any(model => model.Id.Equals(saved, StringComparison.OrdinalIgnoreCase)) ? (saved, false) : (null, true);
    }
}

/// <summary>
/// Remembers model lists per agent (in memory and in the cache folder) so the
/// interface never asks an agent on every repaint. Lists expire after
/// <see cref="Lifetime"/>; <see cref="RefreshAsync"/> with force asks again.
/// </summary>
public sealed class ModelCatalog(IPlatformService platform, AgentRegistry registry, AgexLog? log = null, Func<string, ProviderProfile?>? providerFor = null, ProviderService? providers = null)
{
    /// <summary>Cache key: the agent, plus the provider it is pointed at (a provider has its own model list).</summary>
    private string Key(string id) => providerFor?.Invoke(id) is { } provider ? $"{id}@{provider.Id}" : id;

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, ModelDiscovery> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    private string FileFor(string id) => Path.Combine(platform.Paths.CacheRoot, "models", id + ".json");

    public ModelDiscovery? Cached(string id)
    {
        id = Key(id);
        if (_cache.TryGetValue(id, out var discovery)) return discovery;
        try
        {
            if (Json.ReadFile<ModelDiscovery>(FileFor(id)) is { } stored) return _cache[id] = stored;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { log?.Error("model_cache_invalid", ex); }
        return null;
    }

    public bool IsStale(string id) => Cached(id) is not { } cached || cached.Status is not ModelDiscoveryStatus.Ok and not ModelDiscoveryStatus.Unavailable || DateTimeOffset.UtcNow - cached.RetrievedAt > Lifetime
        // An updated agent may offer different models.
        || registry.LastDetection(id) is { Version.Length: > 0 } detection && cached.AgentVersion.Length > 0 && detection.Version != cached.AgentVersion;

    public async Task<ModelDiscovery> RefreshAsync(string id, bool force, CancellationToken cancellationToken)
    {
        var adapter = registry.Get(id) ?? throw new ArgumentException($"Unknown agent '{id}'.");
        var gate = _gates.GetOrAdd(adapter.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && !IsStale(adapter.Id) && Cached(adapter.Id) is { } fresh) return fresh;
            var detection = registry.DetectionForRun(adapter.Id);
            var key = Key(adapter.Id);
            ModelDiscovery discovery;
            if (providerFor?.Invoke(adapter.Id) is { } provider && providers is not null)
                discovery = await providers.ListModelsAsync(provider, cancellationToken).ConfigureAwait(false);
            else if (detection.Path is null && adapter is not OllamaAdapter)
                discovery = new ModelDiscovery(ModelDiscoveryStatus.NotReady, [], $"{adapter.Name} is not installed.", "", DateTimeOffset.UtcNow);
            else
            {
                try { discovery = (await adapter.GetModelsAsync(detection, cancellationToken).ConfigureAwait(false)) with { AgentVersion = detection.Version }; }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log?.Error("model_discovery_failed", ex, new { agent = adapter.Id });
                    discovery = ModelDiscovery.Failed($"AGEX could not read {adapter.Name}'s models: {Redactor.Redact(ex.Message)}");
                }
            }
            _cache[key] = discovery;
            // Keep the last good list on disk; a failure (offline) must not erase it.
            if (discovery.Status is ModelDiscoveryStatus.Ok or ModelDiscoveryStatus.Empty or ModelDiscoveryStatus.Unavailable)
            {
                try { Json.WriteFile(FileFor(key), discovery); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log?.Error("model_cache_write_failed", ex); }
            }
            else if (Json.ReadFile<ModelDiscovery>(FileFor(key)) is { Status: ModelDiscoveryStatus.Ok } lastGood)
            {
                discovery = lastGood with { Message = "Could not refresh models: " + discovery.Message + " Showing the list from " + lastGood.RetrievedAt.ToLocalTime().ToString("g") + "." };
                _cache[key] = discovery;
            }
            return discovery;
        }
        finally { gate.Release(); }
    }

    public void Forget(string id)
    {
        _cache.TryRemove(id, out _);
        try { File.Delete(FileFor(id)); } catch (IOException) { }
    }
}

public sealed record AgentInstallPlan(bool Possible, string Reason, string? NpmPath, AgentSetupInfo Setup);

/// <summary>
/// Installs agents only through the vendor's npm package, with the exact
/// package name from the adapter, after the user confirmed. Everything else is
/// a manual install with the vendor's own instructions.
/// </summary>
public sealed class AgentInstaller(IPlatformService platform, ProcessRunner runner, AgexLog? log = null)
{
    public const string NodeDownloadUrl = "https://nodejs.org/en/download";

    public AgentInstallPlan Plan(IAgentAdapter adapter)
    {
        var setup = adapter.Setup;
        if (!adapter.SupportedPlatforms.Contains(platform.Os)) return new(false, $"{adapter.Name} is not available for this system.", null, setup);
        if (!setup.CanInstall) return new(false, "This agent is installed with the vendor's own installer. Follow the official instructions.", null, setup);
        var npm = platform.FindExecutable("npm");
        if (npm is null) return new(false, "Node.js (which includes npm) is needed first. Install it from nodejs.org, then try again.", null, setup);
        return new(true, "", npm, setup);
    }

    public async Task<(bool Success, string Message)> InstallAsync(IAgentAdapter adapter, CancellationToken cancellationToken)
    {
        var plan = Plan(adapter);
        if (!plan.Possible || plan.NpmPath is null) return (false, plan.Reason);
        log?.Write("agent_install_started", new { agent = adapter.Id, package = plan.Setup.NpmPackage });
        var result = await runner.RunAsync(new ProcessRequest
        {
            FileName = plan.NpmPath,
            Arguments = ["install", "--global", plan.Setup.NpmPackage!],
            WorkingDirectory = platform.Paths.DataRoot.EnsureDirectory(),
            Timeout = TimeSpan.FromMinutes(10),
            Label = $"Install {adapter.Name}",
        }, cancellationToken).ConfigureAwait(false);
        var output = result.Stdout + "\n" + result.Stderr;
        log?.Write("agent_install_finished", new { agent = adapter.Id, outcome = result.Outcome.ToString(), exit = result.ExitCode });
        if (result.Succeeded) return (true, $"{adapter.Name} was installed.");
        if (output.Contains("EACCES", StringComparison.OrdinalIgnoreCase) || output.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
            return (false, "npm needs administrator rights on this computer (Node.js was installed system-wide). Install it yourself with the official command: npm install -g " + plan.Setup.NpmPackage);
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(text => text.Contains("ERR", StringComparison.Ordinal)) ?? result.ErrorMessage;
        return (false, $"npm could not install {adapter.Name}: {Redactor.Redact(line)}");
    }
}
