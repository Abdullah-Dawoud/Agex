using Agex.Core.Orchestration;
using Agex.Core.Settings;
using Xunit.Abstractions;

namespace Agex.Tests;

/// <summary>Prompt sizes AGEX sends before and after the 2.3 changes, on a project of 250 files.</summary>
public class PromptSizeTests(ITestOutputHelper output)
{
    private static async Task<int> PromptLength(string request, ChatMode mode, string role, string[]? skills = null)
    {
        using var sandbox = new Sandbox("tokens");
        for (var index = 0; index < 250; index++)
        {
            var folder = Path.Combine(sandbox.Project, "src", "module" + index / 25);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"file{index}.js"), "export const x = 1;");
        }
        sandbox.LeaderPlans("""{"goal_status":"COMPLETE","reason":"Done.","verification":"Checked.","tasks":[]}""");
        var core = sandbox.Core();
        core.Settings.Leader = "codex";
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        var context = (skills ?? []).Select(id => new Agex.Core.Agents.SkillContext(id, id, "Skill " + id, Path.Combine(sandbox.Home, "skills", id), null)).ToList();
        var engine = core.CreateRequest(sandbox.Project, request, new ScriptedHost(), core.BuildMembers(profile, agentIds: ["codex"]), profile, context, [], mode: mode);
        await engine.RunAsync(CancellationToken.None);
        var line = sandbox.FakeLog().First(entry => entry.Contains($"|{role}|"));
        return int.Parse(line.Split('|')[2]);
    }

    [Fact]
    public async Task Quick_routes_and_leader_prompts_are_smaller()
    {
        var leader = await PromptLength("what is this project?", ChatMode.Build, "leader");
        var ask = await PromptLength("what is this project?", ChatMode.Auto, "direct");
        var hi = await PromptLength("hi", ChatMode.Auto, "direct");
        output.WriteLine($"Leader prompt: {leader} chars; direct answer: {ask} chars; chat: {hi} chars.");
        Assert.True(ask < leader * 0.4, $"{ask} vs {leader}");
        Assert.True(hi < 800, $"{hi}");

        // Skills on Auto: the eleven skills of a real game session; only those that can matter are sent.
        string[] skills = ["caveman", "caveman-review", "differential-review", "jupyter-notebook", "pdf-documents", "requesting-code-review", "security-best-practices", "security-threat-model", "systematic-debugging", "test-driven-development", "writing-plans"];
        const string game = "open the game as a normal user and try to play it and if u find any bugs fix it just use mouse and keybord to play";
        var sent = skills.Count(id => SkillRelevance.IsRelevant(id, RequestClassifier.Classify(game), game));
        output.WriteLine($"Game request skills: {skills.Length} before, {sent} after.");
        Assert.Equal(3, sent); // caveman (a style the user turned on), debugging and tests: the task is a bug hunt
    }
}
