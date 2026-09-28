using Agex.Core.Sessions;

namespace Agex.Tests;

public class TaskGraphLayoutTests
{
    [Fact]
    public void Dependencies_create_edges_and_increasing_stages()
    {
        var tasks = new List<TaskItem>
        {
            new() { Id = "a", Title = "Inspect" },
            new() { Id = "b", Title = "Build", Dependencies = ["a"] },
            new() { Id = "c", Title = "Review", Dependencies = ["b"] },
        };
        var graph = TaskGraphLayout.Build(tasks);
        Assert.Equal([new TaskGraphEdge("a", "b"), new TaskGraphEdge("b", "c")], graph.Edges);
        Assert.Equal([0, 1, 2], graph.Nodes.OrderBy(node => node.X).Select(node => node.Level));
        Assert.True(graph.Width > TaskGraphLayout.NodeWidth * 3);
        tasks[1].State = TaskState.Running;
        Assert.Equal(graph.Nodes, TaskGraphLayout.Build(tasks).Nodes); // state changes keep positions stable
    }
}
