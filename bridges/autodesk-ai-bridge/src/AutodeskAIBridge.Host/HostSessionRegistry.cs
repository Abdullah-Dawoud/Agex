using AutodeskAIBridge.Core;

namespace AutodeskAIBridge.Host;

/// <summary>Central registry for live and mock plugin sessions.</summary>
public sealed class HostSessionRegistry : IAutodeskSessionRegistry
{
    private sealed record Entry(IAutodeskAdapter Adapter, DateTimeOffset ConnectedAt, DateTimeOffset LastHeartbeat);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _staleAfter;

    public HostSessionRegistry(TimeSpan? staleAfter = null) => _staleAfter = staleAfter ?? TimeSpan.FromSeconds(45);

    public void Register(IAutodeskAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var info = adapter.GetInstanceInfo();
        var key = Key(info.Product, info.InstanceId);
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            _entries[key] = new Entry(adapter, info.ConnectedAt ?? now, now);
        }
    }

    public bool Unregister(string product, string instanceId) { lock (_gate) return _entries.Remove(Key(product, instanceId)); }

    public void MarkHeartbeat(string product, string instanceId, AutodeskInstanceInfo? refreshedInfo = null)
    {
        lock (_gate)
        {
            var key = Key(product, instanceId);
            if (_entries.TryGetValue(key, out var entry))
                _entries[key] = entry with { LastHeartbeat = DateTimeOffset.UtcNow };
        }
    }

    public int ExpireStale(DateTimeOffset now)
    {
        lock (_gate)
        {
            var stale = _entries.Where(pair => now - pair.Value.LastHeartbeat > _staleAfter).Select(pair => pair.Key).ToArray();
            foreach (var key in stale) _entries.Remove(key);
            return stale.Length;
        }
    }

    public IReadOnlyList<AutodeskInstanceInfo> ListInstances()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var stale = _entries.Where(pair => now - (pair.Value.Adapter.GetInstanceInfo().LastHeartbeat ?? pair.Value.LastHeartbeat) > _staleAfter).Select(pair => pair.Key).ToArray();
            foreach (var key in stale) _entries.Remove(key);
            return _entries.Values.Select(entry => Enrich(entry, entry.Adapter.GetInstanceInfo())).ToArray();
        }
    }

    public IAutodeskAdapter? Resolve(AutodeskTarget target)
    {
        lock (_gate) return _entries.TryGetValue(Key(target.Product, target.InstanceId), out var entry) ? entry.Adapter : null;
    }

    public AutodeskInstanceInfo? GetActive(string product)
    {
        lock (_gate)
        {
            var matches = _entries.Values.Where(e => e.Adapter.Product.Equals(product, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? Enrich(matches[0], matches[0].Adapter.GetInstanceInfo()) : null;
        }
    }

    private static AutodeskInstanceInfo Enrich(Entry entry, AutodeskInstanceInfo info)
        => info with { ConnectedAt = info.ConnectedAt ?? entry.ConnectedAt, LastHeartbeat = entry.LastHeartbeat };
    private static string Key(string product, string instanceId) => $"{product.Trim().ToLowerInvariant()}:{instanceId.Trim()}";
}

/// <summary>Explicit per-product target selection. No implicit guessing when multiple targets exist.</summary>
public sealed class InstanceSelection
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AutodeskTarget> _selected = new(StringComparer.OrdinalIgnoreCase);
    public void Select(AutodeskTarget target) { lock (_gate) _selected[target.Product] = target; }
    public AutodeskTarget? Get(string product) { lock (_gate) return _selected.TryGetValue(product, out var target) ? target : null; }
    public void Clear(string product) { lock (_gate) _selected.Remove(product); }
}
