using System.Collections.Concurrent;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

public sealed record AgentHealth(bool Healthy, string Reason, DateTimeOffset? RetryAfter);

/// <summary>
/// The set of adapters, their last detection and their health for this app
/// session. Two failures in a row (or one start/sign-in failure) mark an agent
/// unavailable for a cooldown so requests stop sending work to it.
/// </summary>
public sealed class AgentRegistry
{
    private readonly IPlatformService _platform;
    private readonly ConcurrentDictionary<string, AgentDetection> _detections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (int Streak, AgentHealth Health)> _health = new(StringComparer.OrdinalIgnoreCase);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    public AgentRegistry(IPlatformService platform, IEnumerable<IAgentAdapter> adapters)
    {
        _platform = platform;
        Adapters = adapters.ToList();
    }

    public static AgentRegistry CreateDefault(IPlatformService platform, ProcessRunner runner, Func<string?>? ollamaModel = null) =>
        new(platform, [
            new CodexAdapter(runner, platform),
            new AntigravityAdapter(runner, platform),
            new ClaudeCodeAdapter(runner, platform),
            new GeminiCliAdapter(runner, platform),
            new OllamaAdapter(ollamaModel),
        ]);

    public IReadOnlyList<IAgentAdapter> Adapters { get; }

    public IAgentAdapter? Get(string? idOrName) => idOrName is null ? null : Adapters.FirstOrDefault(adapter =>
        adapter.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || adapter.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

    public AgentDetection Detect(string id)
    {
        var adapter = Get(id) ?? throw new ArgumentException($"Unknown agent '{id}'.");
        var detection = adapter.Detect(_platform);
        // Keep a successful health result while the file is still there.
        if (_detections.TryGetValue(adapter.Id, out var previous) && previous.Path == detection.Path && previous.Status is AgentStatus.Supported or AgentStatus.AuthRequired or AgentStatus.Broken)
            detection = detection with { Status = previous.Status, Version = previous.Version.Length > 0 ? previous.Version : detection.Version, Reason = previous.Reason };
        _detections[adapter.Id] = detection;
        return detection;
    }

    public async Task<AgentDetection> CheckHealthAsync(string id, CancellationToken cancellationToken)
    {
        var adapter = Get(id) ?? throw new ArgumentException($"Unknown agent '{id}'.");
        var detection = adapter.Detect(_platform);
        if (detection.Status == AgentStatus.Available)
        {
            try { detection = await adapter.CheckHealthAsync(detection, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                detection = detection with { Status = AgentStatus.Broken, Reason = $"{adapter.Name} health check failed: {ex.Message}" };
            }
        }
        _detections[adapter.Id] = detection;
        if (detection.Status == AgentStatus.Supported) _health.TryRemove(adapter.Id, out _);
        return detection;
    }

    public AgentDetection? LastDetection(string id) => Get(id) is { } adapter && _detections.TryGetValue(adapter.Id, out var detection) ? detection : null;

    /// <summary>Detection for running work: cached, or a fresh file check.</summary>
    public AgentDetection DetectionForRun(string id) => LastDetection(id) is { Path: not null } cached ? cached : Detect(id);

    public AgentHealth Health(string id)
    {
        var key = Get(id)?.Id ?? id;
        var detection = LastDetection(key);
        if (detection is { Status: AgentStatus.NotInstalled or AgentStatus.PlatformUnsupported })
            return new AgentHealth(false, detection.Reason, null);
        if (_health.TryGetValue(key, out var entry) && !entry.Health.Healthy)
        {
            if (entry.Health.RetryAfter is { } retry && retry <= DateTimeOffset.UtcNow)
            {
                _health.TryRemove(key, out _);
                return new AgentHealth(true, "", null);
            }
            return entry.Health;
        }
        return new AgentHealth(true, "", null);
    }

    public void RegisterResult(string id, bool success, string reason = "", bool immediate = false)
    {
        var key = Get(id)?.Id ?? id;
        if (success) { _health[key] = (0, new AgentHealth(true, "", null)); return; }
        _health.AddOrUpdate(key,
            _ => immediate ? (1, Unhealthy(reason)) : (1, new AgentHealth(true, reason, null)),
            (_, current) =>
            {
                var streak = current.Streak + 1;
                return immediate || streak >= 2 ? (streak, Unhealthy(reason)) : (streak, current.Health with { Reason = reason });
            });
    }

    /// <summary>Clears cooldowns, for example after the user signs in and presses "Check again".</summary>
    public void ResetHealth(string? id = null)
    {
        if (id is null) _health.Clear();
        else if (Get(id) is { } adapter) _health.TryRemove(adapter.Id, out _);
    }

    private static AgentHealth Unhealthy(string reason) => new(false, reason, DateTimeOffset.UtcNow + Cooldown);
}
