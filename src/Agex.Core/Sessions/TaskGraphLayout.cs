namespace Agex.Core.Sessions;

public sealed record TaskGraphNode(string Id, int Level, double X, double Y);
public sealed record TaskGraphEdge(string From, string To);
public sealed record TaskGraph(double Width, double Height, IReadOnlyList<TaskGraphNode> Nodes, IReadOnlyList<TaskGraphEdge> Edges);

/// <summary>Deterministic layered dependency layout. Pure data so live UI and tests use the same graph.</summary>
public static class TaskGraphLayout
{
    public const double NodeWidth = 272;
    public const double NodeHeight = 154;
    public const double ColumnGap = 90;
    public const double RowGap = 28;

    public static TaskGraph Build(IReadOnlyList<TaskItem> tasks)
    {
        var byId = tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        int Level(string id, HashSet<string> visiting)
        {
            if (levels.TryGetValue(id, out var known)) return known;
            if (!byId.TryGetValue(id, out var task) || !visiting.Add(id)) return 0;
            var level = task.Dependencies.Where(byId.ContainsKey).Select(parent => Level(parent, visiting) + 1).DefaultIfEmpty(0).Max();
            visiting.Remove(id);
            return levels[id] = Math.Min(level, tasks.Count);
        }
        foreach (var task in tasks) Level(task.Id, []);
        var positions = new Dictionary<string, TaskGraphNode>(StringComparer.Ordinal);
        foreach (var group in tasks.GroupBy(task => levels[task.Id]).OrderBy(group => group.Key))
        {
            var ordered = group.OrderBy(task => task.Dependencies.Where(positions.ContainsKey).Select(parent => positions[parent].Y).DefaultIfEmpty(0).Average())
                .ThenBy(task => task.Id, StringComparer.Ordinal).ToList();
            for (var row = 0; row < ordered.Count; row++)
                positions[ordered[row].Id] = new TaskGraphNode(ordered[row].Id, group.Key,
                    28 + group.Key * (NodeWidth + ColumnGap), 50 + row * (NodeHeight + RowGap));
        }
        var edges = tasks.SelectMany(task => task.Dependencies.Where(positions.ContainsKey).Select(parent => new TaskGraphEdge(parent, task.Id))).ToList();
        return new TaskGraph(positions.Count == 0 ? 0 : positions.Values.Max(node => node.X) + NodeWidth + 28,
            positions.Count == 0 ? 0 : positions.Values.Max(node => node.Y) + NodeHeight + 32,
            positions.Values.ToList(), edges);
    }
}
