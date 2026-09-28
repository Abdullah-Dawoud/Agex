using Agex.Core.Sessions;
using Agex.Core.Orchestration;
using Agex.Core.Settings;

namespace Agex.Tests;

public class ConversationWorkspaceTests
{
    [Fact]
    public void New_conversations_get_distinct_writable_folders_and_followups_reuse_one()
    {
        using var sandbox = new Sandbox("conversation-workspaces");
        var first = ConversationWorkspace.GetOrCreate(sandbox.Home);
        var second = ConversationWorkspace.GetOrCreate(sandbox.Home);
        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first));
        var report = Path.Combine(first, "report.md");
        File.WriteAllText(report, "research output");
        var continued = ConversationWorkspace.GetOrCreate(sandbox.Home, new Session { Projectless = true, Project = first });
        Assert.Equal(first, continued);
        Assert.Equal("research output", File.ReadAllText(Path.Combine(continued, "report.md")));
        Assert.NotEqual(sandbox.Project, first);
        Assert.NotEqual(sandbox.Project, second);
    }

    [Fact]
    public void Refuses_to_reuse_a_folder_outside_private_workspaces()
    {
        using var sandbox = new Sandbox("conversation-boundary");
        var folder = ConversationWorkspace.GetOrCreate(sandbox.Home, new Session { Projectless = true, Project = sandbox.Project });
        Assert.NotEqual(sandbox.Project, folder);
        Assert.StartsWith(Path.Combine(sandbox.Home, "workspaces"), folder);
    }

    [Fact]
    public async Task Projectless_build_produces_an_artifact_in_its_conversation_folder()
    {
        using var sandbox = new Sandbox("conversation-artifact");
        sandbox.LeaderPlans("""{"goal_status":"CONTINUE","reason":"Write report.","tasks":[{"id":"report","title":"Write report","objective":"Create report.md","executor":"Codex","dependencies":[],"affected_files":["report.md"]}]}""",
            """{"goal_status":"COMPLETE","reason":"Report ready.","verification":"report.md exists.","tasks":[]}""");
        var core = sandbox.Core();
        var folder = ConversationWorkspace.GetOrCreate(sandbox.Home);
        var profile = new ProjectProfile { Path = folder, Name = "Conversation workspace", AllowWrites = true };
        var engine = core.CreateRequest(folder, "Create a report", new ScriptedHost(), core.BuildMembers(profile, agentIds: ["codex"]),
            profile, [], [], mode: ChatMode.Build, projectless: true);

        var session = await engine.RunAsync(CancellationToken.None);

        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.True(File.Exists(Path.Combine(folder, "report.md")));
        Assert.Contains(session.Artifacts, artifact => artifact.Kind == ArtifactKind.File && artifact.Path == Path.Combine(folder, "report.md"));
    }
}
