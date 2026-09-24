using Agex.Core.Orchestration;
using Agex.Core.Sessions;

namespace Agex.Tests;

public class PlanTests
{
    private static string? Resolve(string name) => name.ToLowerInvariant() switch { "codex" => "codex", "antigravity" => "antigravity", _ => null };

    private static LeaderPlan Parse(string json, List<TaskItem>? existing = null) => LeaderPlanParser.Parse(json, existing ?? [], Resolve);

    [Fact]
    public void Tasks_get_canonical_ids_and_dependencies_resolve_by_title()
    {
        var plan = Parse("""
            ```json
            {"goal_status":"CONTINUE","reason":"r","tasks":[
              {"id":"x","title":"Build API","objective":"o1","executor":"Codex","dependencies":[],"affected_files":["api.cs"]},
              {"id":"x2","title":"Test API","objective":"o2","executor":"antigravity","dependencies":["build api"],"affected_files":[]}]}
            ```
            """);
        Assert.Equal(["task-0001", "task-0002"], plan.Tasks.Select(task => task.Id));
        Assert.Equal(["task-0001"], plan.Tasks[1].Dependencies);
        Assert.Equal("antigravity", plan.Tasks[1].Agent);
    }

    [Theory]
    [InlineData("""{"goal_status":"CONTINUE","reason":"r","tasks":[{"id":"a","objective":"o","executor":"Codex","dependencies":["nope"]}]}""", "Unknown dependency")]
    [InlineData("""{"goal_status":"CONTINUE","reason":"r","tasks":[{"id":"a","objective":"o","executor":"Codex","dependencies":["a"]}]}""", "depends on itself")]
    [InlineData("""{"goal_status":"CONTINUE","reason":"r","tasks":[{"id":"a","objective":"o","executor":"Codex","dependencies":["b"]},{"id":"b","objective":"p","executor":"Codex","dependencies":["a"]}]}""", "cycle")]
    [InlineData("""{"goal_status":"CONTINUE","reason":"r","tasks":[{"id":"a","objective":"o","executor":"Robot"}]}""", "not an available agent")]
    [InlineData("""{"goal_status":"COMPLETE","reason":"r","tasks":[]}""", "requires verification")]
    [InlineData("""{"goal_status":"NEEDS_INPUT","reason":"r","tasks":[]}""", "requires a question")]
    [InlineData("""{"goal_status":"MAYBE","reason":"r"}""", "Invalid goal_status")]
    [InlineData("not json at all", "not valid JSON")]
    public void Invalid_plans_are_rejected_with_a_reason(string json, string expected)
    {
        var error = Assert.Throws<PlanException>(() => Parse(json));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Failed_tasks_need_a_repair_task()
    {
        var existing = new List<TaskItem> { new() { Id = "task-0001", Title = "Write", State = TaskState.RepairRequired } };
        Assert.Throws<PlanException>(() => Parse("""{"goal_status":"CONTINUE","reason":"r","tasks":[{"id":"n","objective":"other","executor":"Codex"}]}""", existing));
        var plan = Parse("""{"goal_status":"CONTINUE","reason":"r","tasks":[{"id":"n","objective":"fix","executor":"Codex","repair_for":["Write"]}]}""", existing);
        Assert.Equal("task-0002", plan.Tasks[0].Id);
        Assert.Equal(["task-0001"], plan.Tasks[0].RepairFor);
    }

    [Fact]
    public void Executor_reply_contract_is_parsed()
    {
        const string reply = """Done. {"result":"Created a.txt","messages":[{"to":"Codex","type":"QUESTION","content":"Which DB?"},{"to":"Codex","type":"BOGUS","content":"x"}]}""";
        Assert.Equal("Created a.txt", ExecutorReply.ResultText("""{"result":"Created a.txt"}"""));
        var message = Assert.Single(ExecutorReply.Messages(reply));
        Assert.Equal(("Codex", "QUESTION", "Which DB?"), (message.To, message.Type, message.Content));
        Assert.Equal("plain text", ExecutorReply.ResultText("plain text"));
    }
}
