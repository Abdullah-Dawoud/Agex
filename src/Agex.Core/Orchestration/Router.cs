using Agex.Core.Agents;
using Agex.Core.Settings;

namespace Agex.Core.Orchestration;

/// <summary>An agent taking part in one request, with what it may do.</summary>
public sealed record TeamMember(IAgentAdapter Adapter, string? Model, string? Effort, bool CanWrite, PrivacyKind Privacy)
{
    /// <summary>Optional model endpoint the agent is pointed at.</summary>
    public ProviderEndpoint? Provider { get; init; }
    public string Id => Adapter.Id;
    public string Name => Adapter.Name;
    public bool CanReadFiles => Adapter.Capabilities.Contains(Capability.ReadFiles);
}

/// <summary>Historic run durations per agent, used by the "Fast" preset. Empty until AGEX has run requests.</summary>
public interface IAgentStatistics
{
    double? MedianSeconds(string agentId);
}

/// <summary>
/// Turns the routing preset into (a) the leader choice, (b) preference guidance
/// for the leader and (c) hard limits (for example "Local only" removes every
/// cloud agent).
/// </summary>
public sealed class Router(AgexSettings settings, IAgentStatistics? statistics = null)
{
    public static string Describe(RoutingPreset preset) => preset switch
    {
        RoutingPreset.Automatic => "AGEX picks the leader and lets it assign each task to the best-suited agent.",
        RoutingPreset.Balanced => "Work is spread evenly across the selected agents.",
        RoutingPreset.Fast => "Prefers the agents that finished fastest in your past requests.",
        RoutingPreset.BestQuality => "Prefers agents in your quality order (Settings > Agents).",
        RoutingPreset.LowCost => "Prefers local and free agents; cloud agents only when needed.",
        RoutingPreset.LocalOnly => "Only agents that run on this computer. Nothing is sent to cloud services.",
        RoutingPreset.Custom => "Uses your exact task shares per agent.",
        _ => "",
    };

    public static string Title(RoutingPreset preset) => preset switch
    {
        RoutingPreset.BestQuality => "Best quality",
        RoutingPreset.LowCost => "Low cost",
        RoutingPreset.LocalOnly => "Local only",
        _ => preset.ToString(),
    };

    /// <summary>Applies hard limits of the preset. Returns the members that may take part.</summary>
    public IReadOnlyList<TeamMember> Filter(IReadOnlyList<TeamMember> members, RoutingPreset preset) =>
        preset == RoutingPreset.LocalOnly ? members.Where(member => member.Privacy == PrivacyKind.Local).ToList() : members;

    public IReadOnlyList<TeamMember> Order(IReadOnlyList<TeamMember> members, RoutingPreset preset)
    {
        int QualityRank(TeamMember member)
        {
            var index = settings.QualityOrder.FindIndex(id => id.Equals(member.Id, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }
        return preset switch
        {
            RoutingPreset.Fast => members.OrderBy(member => statistics?.MedianSeconds(member.Id) ?? double.MaxValue).ThenBy(QualityRank).ToList(),
            RoutingPreset.LowCost => members.OrderBy(member => member.Privacy == PrivacyKind.Local ? 0 : 1).ThenBy(QualityRank).ToList(),
            RoutingPreset.Custom => members.OrderByDescending(member => settings.CustomShares.GetValueOrDefault(member.Id)).ThenBy(QualityRank).ToList(),
            _ => members.OrderBy(QualityRank).ToList(),
        };
    }

    /// <summary>Configured leader when it is in the team and able to plan; otherwise the first suitable member in preset order.</summary>
    public TeamMember? ChooseLeader(IReadOnlyList<TeamMember> members, RoutingPreset preset, string configuredLeader)
    {
        var planners = members.Where(member => member.Adapter.Capabilities.Contains(Capability.Planning)).ToList();
        if (!configuredLeader.Equals("auto", StringComparison.OrdinalIgnoreCase))
            if (planners.FirstOrDefault(member => member.Id.Equals(configuredLeader, StringComparison.OrdinalIgnoreCase)) is { } chosen) return chosen;
        // A leader that can read the project verifies better; prefer those.
        return Order(planners, preset == RoutingPreset.Balanced ? RoutingPreset.BestQuality : preset).OrderBy(member => member.CanReadFiles ? 0 : 1).FirstOrDefault();
    }

    /// <summary>Preference guidance included in the leader prompt.</summary>
    public string Guidance(IReadOnlyList<TeamMember> members, RoutingPreset preset)
    {
        var ordered = Order(members, preset).Select(member => member.Name).ToList();
        return preset switch
        {
            RoutingPreset.Balanced => $"Spread tasks evenly across: {string.Join(", ", ordered)}.",
            RoutingPreset.Fast => $"Prefer faster agents, in this order: {string.Join(", ", ordered)}. Keep the plan short.",
            RoutingPreset.BestQuality => $"Prefer agents in this order: {string.Join(", ", ordered)}.",
            RoutingPreset.LowCost => $"Prefer local agents first ({string.Join(", ", members.Where(m => m.Privacy == PrivacyKind.Local).Select(m => m.Name).DefaultIfEmpty("none"))}); use cloud agents only when the task needs them.",
            RoutingPreset.LocalOnly => "Only local agents are available. Nothing may be sent to cloud services.",
            RoutingPreset.Custom => "Target share of tasks: " + string.Join(", ", members.Select(member => $"{member.Name} {settings.CustomShares.GetValueOrDefault(member.Id)}%")) + ".",
            _ => "Assign each task to the agent best suited to it.",
        };
    }
}
