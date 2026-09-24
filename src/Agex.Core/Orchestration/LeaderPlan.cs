using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Sessions;

namespace Agex.Core.Orchestration;

public enum GoalStatus { Continue, Complete, Blocked, NeedsInput }

public sealed class PlannedTask
{
    public string Id { get; set; } = "";
    public string OriginalId { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    /// <summary>Agent id resolved from the leader's executor name.</summary>
    public string Agent { get; set; } = "";
    public List<string> Dependencies { get; set; } = [];
    public List<string> AffectedFiles { get; set; } = [];
    public List<string> RepairFor { get; set; } = [];
}

public sealed class LeaderPlan
{
    public GoalStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public string Verification { get; set; } = "";
    public string Question { get; set; } = "";
    public List<PlannedTask> Tasks { get; set; } = [];
}

public sealed class PlanException(string message) : Exception(message);

/// <summary>
/// Validates the leader's JSON plan and maps it onto canonical task ids.
/// Dependencies may refer to earlier tasks by id, title or alias; ambiguous,
/// unknown, self and cyclic references are rejected so the leader can repair
/// the plan once.
/// </summary>
public static partial class LeaderPlanParser
{
    [GeneratedRegex(@"^```(?:json)?\s*|\s*```$")]
    private static partial Regex Fence();

    [GeneratedRegex(@"[\s_\-]+")]
    private static partial Regex Separators();

    /// <summary>
    /// The first complete top-level JSON object in the reply. Models often wrap
    /// JSON in code fences or add a sentence before or after it.
    /// </summary>
    public static string ExtractJson(string text)
    {
        var trimmed = Fence().Replace(text.Trim(), "");
        var start = trimmed.IndexOf('{');
        if (start < 0) return trimmed;
        int depth = 0;
        bool quoted = false, escaped = false;
        for (var index = start; index < trimmed.Length; index++)
        {
            var ch = trimmed[index];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') quoted = false;
                continue;
            }
            if (ch == '"') quoted = true;
            else if (ch == '{') depth++;
            else if (ch == '}' && --depth == 0) return trimmed[start..(index + 1)];
        }
        return trimmed[start..];
    }

    private static string Alias(string reference) => Separators().Replace(reference.Trim().ToLowerInvariant(), "-").Trim('-');

    /// <param name="resolveAgent">Maps an executor name from the plan to an allowed agent id, or null.</param>
    public static LeaderPlan Parse(string text, IReadOnlyList<TaskItem> existing, Func<string, string?> resolveAgent)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(ExtractJson(text), new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 32 });
            root = document.RootElement.Clone();
        }
        catch (JsonException ex) { throw new PlanException("The leader's reply is not valid JSON: " + ex.Message); }
        if (root.ValueKind != JsonValueKind.Object) throw new PlanException("The leader's reply is not a JSON object.");

        var plan = new LeaderPlan
        {
            Status = (Str(root, "goal_status") ?? "").ToUpperInvariant() switch
            {
                "CONTINUE" => GoalStatus.Continue,
                "COMPLETE" => GoalStatus.Complete,
                "BLOCKED" => GoalStatus.Blocked,
                "NEEDS_INPUT" => GoalStatus.NeedsInput,
                var other => throw new PlanException($"Invalid goal_status '{other}'."),
            },
            Reason = Str(root, "reason") ?? "",
            Verification = Str(root, "verification") ?? "",
            Question = Str(root, "question") ?? "",
        };

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddReference(string? reference, string canonical)
        {
            foreach (var key in new[] { reference, reference is null ? null : Alias(reference) })
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (references.TryGetValue(key, out var current) && current != canonical) ambiguous.Add(key);
                else references[key] = canonical;
            }
        }
        var next = 1;
        foreach (var task in existing)
        {
            if (string.IsNullOrWhiteSpace(task.Id) || !used.Add(task.Id)) throw new PlanException("Existing task list has a missing or duplicate id.");
            foreach (var reference in task.Aliases.Append(task.Id).Append(task.OriginalId).Append(task.Title).Append(task.Objective)) AddReference(reference, task.Id);
            if (Regex.Match(task.Id, @"^task-(\d+)$") is { Success: true } match) next = Math.Max(next, int.Parse(match.Groups[1].Value) + 1);
        }

        var rawTasks = root.TryGetProperty("tasks", out var tasksElement) && tasksElement.ValueKind == JsonValueKind.Array ? tasksElement.EnumerateArray().ToList() : [];
        foreach (var raw in rawTasks)
        {
            var objective = Str(raw, "objective") ?? "";
            var executor = Str(raw, "executor") ?? "";
            if (objective.Length == 0) throw new PlanException("A task has no objective.");
            var agent = resolveAgent(executor) ?? throw new PlanException($"Task executor '{executor}' is not an available agent.");
            string id;
            do { id = $"task-{next++:d4}"; } while (!used.Add(id));
            var aliases = new[] { Str(raw, "id"), Str(raw, "title"), objective }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct().ToList();
            plan.Tasks.Add(new PlannedTask
            {
                Id = id, OriginalId = Str(raw, "id") ?? "", Aliases = aliases, Title = Str(raw, "title") ?? "", Objective = objective, Agent = agent,
                AffectedFiles = Strings(raw, "affected_files").Where(path => path.Length > 0).ToList(),
            });
            AddReference(id, id);
            foreach (var alias in aliases) AddReference(alias, id);
        }

        for (var index = 0; index < rawTasks.Count; index++)
        {
            var task = plan.Tasks[index];
            string Resolve(string reference, string what)
            {
                if (string.IsNullOrWhiteSpace(reference)) throw new PlanException($"Empty {what} on {task.Id}.");
                if (ambiguous.Contains(reference) || ambiguous.Contains(Alias(reference))) throw new PlanException($"Ambiguous {what} '{reference}' on {task.Id}.");
                if (references.TryGetValue(reference, out var found) || references.TryGetValue(Alias(reference), out found)) return found;
                throw new PlanException($"Unknown {what} '{reference}' on {task.Id}.");
            }
            foreach (var reference in Strings(rawTasks[index], "dependencies"))
            {
                var dependency = Resolve(reference, "dependency");
                if (dependency == task.Id) throw new PlanException($"Task {task.Id} depends on itself.");
                if (task.Dependencies.Contains(dependency)) throw new PlanException($"Duplicate dependency '{reference}' on {task.Id}.");
                task.Dependencies.Add(dependency);
            }
            foreach (var reference in Strings(rawTasks[index], "repair_for"))
            {
                var target = Resolve(reference, "repair target");
                if (!task.RepairFor.Contains(target)) task.RepairFor.Add(target);
            }
        }

        var graph = existing.Select(task => (task.Id, (IReadOnlyList<string>)task.Dependencies))
            .Concat(plan.Tasks.Select(task => (task.Id, (IReadOnlyList<string>)task.Dependencies))).ToList();
        if (FindCycle(graph) is { } cycle) throw new PlanException("Dependency cycle: " + cycle);

        if (plan.Status == GoalStatus.Continue)
        {
            foreach (var failed in existing.Where(task => task.State is TaskState.Failed or TaskState.RepairRequired))
                if (!plan.Tasks.Any(task => task.RepairFor.Contains(failed.Id)))
                    throw new PlanException($"The plan has no repair task (repair_for) for failed task {failed.Id}.");
            if (plan.Tasks.Count == 0) throw new PlanException("CONTINUE needs at least one task.");
        }
        if (plan.Status == GoalStatus.Complete && (plan.Verification.Length == 0 || plan.Tasks.Count > 0))
            throw new PlanException("COMPLETE requires verification and no new tasks.");
        if (plan.Status == GoalStatus.NeedsInput && plan.Question.Length == 0)
            throw new PlanException("NEEDS_INPUT requires a question for the user.");
        return plan;
    }

    public static string? FindCycle(IReadOnlyList<(string Id, IReadOnlyList<string> Dependencies)> tasks)
    {
        var byId = tasks.GroupBy(task => task.Id).ToDictionary(group => group.Key, group => group.First().Dependencies);
        var marks = new Dictionary<string, int>();
        var path = new List<string>();
        string? Visit(string id)
        {
            marks[id] = 1;
            path.Add(id);
            foreach (var dependency in byId.GetValueOrDefault(id) ?? [])
            {
                if (marks.GetValueOrDefault(dependency) == 1)
                {
                    var start = path.IndexOf(dependency);
                    return string.Join(" -> ", path.Skip(start).Append(dependency));
                }
                if (marks.GetValueOrDefault(dependency) != 2 && Visit(dependency) is { } found) return found;
            }
            path.RemoveAt(path.Count - 1);
            marks[id] = 2;
            return null;
        }
        foreach (var (id, _) in tasks)
            if (marks.GetValueOrDefault(id) != 2 && Visit(id) is { } cycle) return cycle;
        return null;
    }

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<string> Strings(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString()).ToList();
    }
}

/// <summary>Executor replies: {"result": "...", "messages": [{"to","type","content"}]}.</summary>
public static partial class ExecutorReply
{
    public sealed record OperationalMessage(string To, string Type, string Content);

    public static readonly string[] MessageTypes = ["QUESTION", "ANSWER", "REQUEST", "RESULT", "BLOCKER", "HANDOFF", "REVIEW"];

    [GeneratedRegex("\"result\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex ResultField();

    /// <summary>The human-readable result, or the raw text when the reply is not the JSON contract.</summary>
    public static string ResultText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        try
        {
            using var document = JsonDocument.Parse(LeaderPlanParser.ExtractJson(text));
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
                return result.GetString() ?? "";
        }
        catch (JsonException) { }
        if (ResultField().Match(text) is { Success: true } match)
        {
            try { return JsonSerializer.Deserialize<string>("\"" + match.Groups[1].Value + "\"") ?? text.Trim(); }
            catch (JsonException) { }
        }
        return text.Trim();
    }

    /// <summary>Finds every top-level JSON object with a "messages" array in the reply.</summary>
    public static List<OperationalMessage> Messages(string text)
    {
        var list = new List<OperationalMessage>();
        int depth = 0, start = -1;
        bool quoted = false, escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') quoted = false;
                continue;
            }
            if (ch == '"') { quoted = true; continue; }
            if (ch == '{') { if (depth++ == 0) start = index; continue; }
            if (ch != '}' || depth == 0) continue;
            if (--depth != 0 || start < 0) continue;
            try
            {
                using var document = JsonDocument.Parse(text[start..(index + 1)]);
                if (document.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                    foreach (var message in messages.EnumerateArray())
                    {
                        if (message.ValueKind != JsonValueKind.Object) continue;
                        string Get(string name) => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
                        var type = Get("type").ToUpperInvariant();
                        if (Get("to").Length > 0 && Get("content").Length > 0 && MessageTypes.Contains(type))
                            list.Add(new OperationalMessage(Get("to"), type, Get("content")));
                    }
            }
            catch (JsonException) { }
            start = -1;
        }
        return list;
    }
}
