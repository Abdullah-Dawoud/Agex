using System.IO.Compression;
using System.Text;
using Agex.Core;
using Agex.Core.Agents;
using Agex.Core.Attachments;
using Agex.Core.Orchestration;
using Agex.Core.Sessions;
using Agex.Core.Settings;
using Agex.Core.Skills;
using Agex.Core.Teams;
using Xunit.Abstractions;

namespace Agex.Tests;

/// <summary>v2.1 workspace: model metadata, OpenCode, providers, attachments, job teams, efficiency modes and skill costs.</summary>
public class WorkspaceTests(ITestOutputHelper output)
{
    [Fact]
    public void OpenCode_models_list_local_and_free_first()
    {
        var models = OpenCodeAdapter.ParseModels("anthropic/claude-sonnet-4-5\nopencode/big-pickle\nopencode/grok-code-free\nollama/qwen3:8b\nnot a model\n\nollama/qwen3:8b\n");
        Assert.Equal(["ollama/qwen3:8b", "opencode/grok-code-free", "anthropic/claude-sonnet-4-5", "opencode/big-pickle"], models.Select(model => model.Id));
        Assert.Equal(PrivacyKind.Local, models[0].Location);
        Assert.Equal("Free (OpenCode Zen)", models[1].CostHint);
        Assert.Null(models[3].CostHint);
    }

    [Fact]
    public void OpenCode_read_only_runs_use_the_plan_agent_and_pass_attachments()
    {
        var attachment = new Attachment { Path = Path.Combine(Path.GetTempPath(), "a.png"), Name = "a.png", Kind = AttachmentKind.Image };
        var readOnly = OpenCodeAdapter.BuildArguments(new AgentInvocation { Prompt = "x", WorkingDirectory = ".", Model = "opencode/big-pickle", AllowWrites = false, Attachments = [attachment] });
        Assert.Equal(["run", "--format", "json", "--model", "opencode/big-pickle", "--agent", "plan", "--file", attachment.Path], readOnly);
        var write = OpenCodeAdapter.BuildArguments(new AgentInvocation { Prompt = "x", WorkingDirectory = ".", AllowWrites = true });
        Assert.Contains("--auto", write);
        Assert.DoesNotContain("--model", write);
    }

    [Fact]
    public void Ollama_show_adds_only_reported_capabilities()
    {
        var model = new ModelInfo { Id = "qwen3:8b", Location = PrivacyKind.Local };
        var shown = OllamaAdapter.ApplyShow(model, """{"capabilities":["completion","tools","thinking"],"model_info":{"qwen3.context_length":40960}}""");
        Assert.True(shown.Tools);
        Assert.True(shown.Reasoning);
        Assert.False(shown.Vision);
        Assert.Equal(40960, shown.ContextWindow);
        Assert.Equal("local · tools · reasoning · 40k context", shown.Facts);
        var unknown = OllamaAdapter.ApplyShow(model, "{}");
        Assert.Null(unknown.Vision);
        Assert.Equal("local", unknown.Facts);
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1", false, true)]
    [InlineData("http://127.0.0.1:11434/v1", true, true)]
    [InlineData("http://localhost:20128/v1", true, true)]
    [InlineData("http://example.com/v1", false, false)]
    [InlineData("http://192.168.1.5:8080/v1", true, false)]
    [InlineData("file:///c:/x", true, false)]
    public void Provider_addresses_must_be_https_or_this_computer(string url, bool local, bool valid) =>
        Assert.Equal(valid, ProviderService.IsValidBaseUrl(url, local));

    [Fact]
    public void Provider_presets_never_claim_free_for_cloud_services()
    {
        foreach (var preset in ProviderPresets.All)
        {
            if (preset.Cost is CostLabel.Free or CostLabel.Local) Assert.True(preset.Local, preset.Id);
            Assert.True(ProviderService.IsValidBaseUrl(preset.BaseUrl, preset.Local || preset.BaseUrl.Contains("localhost")), preset.Id);
        }
    }

    [Theory]
    [InlineData("shot.PNG", AttachmentKind.Image)]
    [InlineData("plan.pdf", AttachmentKind.Pdf)]
    [InlineData("report.docx", AttachmentKind.Office)]
    [InlineData("Program.cs", AttachmentKind.Text)]
    [InlineData("site.zip", AttachmentKind.Archive)]
    [InlineData("demo.mp4", AttachmentKind.Video)]
    [InlineData("setup.exe", AttachmentKind.Unsupported)]
    [InlineData("model.rvt", AttachmentKind.Unsupported)]
    public void Attachments_are_classified_by_type(string name, AttachmentKind kind) => Assert.Equal(kind, AttachmentService.Classify(name));

    [Fact]
    public void Word_text_is_extracted_without_running_anything()
    {
        using var sandbox = new Sandbox("docx");
        var path = Path.Combine(sandbox.Root, "brief.docx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            writer.Write("""<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Floor plan review</w:t></w:r></w:p><w:p><w:r><w:t>مرحبا</w:t></w:r></w:p></w:body></w:document>""");
        }
        var text = AttachmentService.ExtractOfficeText(path);
        Assert.Contains("Floor plan review", text);
        Assert.Contains("مرحبا", text);
    }

    [Fact]
    public async Task Attachments_are_copied_and_unsupported_files_refused()
    {
        using var sandbox = new Sandbox("attach");
        var core = sandbox.Core();
        var note = Path.Combine(sandbox.Root, "notes.md");
        var binary = Path.Combine(sandbox.Root, "tool.exe");
        File.WriteAllText(note, "# Notes");
        File.WriteAllBytes(binary, [0x4D, 0x5A]);
        var folder = Path.Combine(sandbox.Home, "attachments", "t1");
        var (attached, refused) = await core.Attachments.PrepareAsync(folder, [note, binary], false, CancellationToken.None);
        var item = Assert.Single(attached);
        Assert.StartsWith(folder, item.Path);
        Assert.True(File.Exists(item.Path));
        Assert.Single(refused);
        Assert.Contains("tool.exe", refused[0]);
    }

    [Fact]
    public void Attachment_plan_names_where_files_go()
    {
        using var sandbox = new Sandbox("plan");
        var core = sandbox.Core();
        var image = new Attachment { Path = "x.png", Name = "x.png", Kind = AttachmentKind.Image };
        var codex = core.Registry.Get("codex")!;
        var ollama = core.Registry.Get("ollama")!;
        var plan = AttachmentService.Plan([image], [(codex, null, false), (ollama, "llama3.2", false), (ollama, "llava", true)]);
        Assert.True(plan[0].LeavesComputer);
        Assert.Equal("x.png: image", plan[0].Lines.Single());
        Assert.False(plan[1].LeavesComputer);
        Assert.Contains("not sent", plan[1].Lines.Single());
        Assert.Equal("x.png: image", plan[2].Lines.Single());
        Assert.Equal("stays on this computer", plan[2].Destination);
    }

    [Fact]
    public void Text_only_agents_get_attached_text_inline()
    {
        using var sandbox = new Sandbox("inline");
        var path = Path.Combine(sandbox.Root, "spec.txt");
        File.WriteAllText(path, "Use metric units.");
        var section = AttachmentService.PromptSection([new Attachment { Path = path, Name = "spec.txt", Kind = AttachmentKind.Text, Size = 17 }], agentCanOpenFiles: false);
        Assert.Contains("Use metric units.", section);
        var withPaths = AttachmentService.PromptSection([new Attachment { Path = path, Name = "spec.txt", Kind = AttachmentKind.Text, Size = 17 }], agentCanOpenFiles: true);
        Assert.Contains(path, withPaths);
        Assert.DoesNotContain("Use metric units.", withPaths);
    }

    [Fact]
    public void Every_team_skill_is_in_the_catalog_and_teams_have_jobs()
    {
        using var sandbox = new Sandbox("teams");
        var core = sandbox.Core();
        var catalog = core.Skills.Catalog().Skills.Select(skill => skill.Id).ToHashSet();
        Assert.InRange(JobTeamCatalog.All.Count, 8, 15);
        Assert.Equal(JobTeamCatalog.All.Count, JobTeamCatalog.All.Select(team => team.Id).Distinct().Count());
        foreach (var team in JobTeamCatalog.All)
        {
            Assert.NotEmpty(team.TypicalTasks);
            Assert.NotEmpty(team.Outputs);
            foreach (var requirement in team.Requirements.Where(item => item.Kind == RequirementKind.Skill)) Assert.True(catalog.Contains(requirement.Id), $"{team.Id} needs {requirement.Id}");
            var statuses = core.Teams.Check(team);
            Assert.Equal(team.Requirements.Count, statuses.Count);
            var (ready, total, _) = JobTeamService.Progress(statuses);
            Assert.InRange(ready, 0, total);
        }
    }

    [Fact]
    public void Teams_that_act_outside_require_confirmation_in_their_brief()
    {
        foreach (var id in new[] { "computer-operator", "job-search" })
        {
            var team = JobTeamCatalog.Get(id)!;
            Assert.Contains("ask the user first", JobTeamCatalog.Brief(team));
        }
        Assert.Contains("Do not create, change or delete any file.", JobTeamCatalog.Brief(JobTeamCatalog.Get("security-review")!));
    }

    [Fact]
    public void Autodesk_team_never_claims_a_connection_without_the_bridge()
    {
        using var sandbox = new Sandbox("bim");
        var core = sandbox.Core();
        var status = core.Teams.Check(JobTeamCatalog.Get("architecture-bim")!).Single(item => item.Requirement.Id == AutodeskBridge.SkillId);
        if (AutodeskBridge.HostPath() is null) Assert.NotEqual(RequirementState.Ready, status.State);
        if (AutodeskBridge.HostPath() is null) Assert.Null(core.Teams.ConnectAutodeskBridge());
    }

    [Fact]
    public void Skill_costs_are_labelled_and_caveman_is_local()
    {
        using var sandbox = new Sandbox("costs");
        var skills = sandbox.Core().Skills.Catalog().Skills;
        Assert.Equal(SkillCost.Local, SkillCosts.Of(skills.Single(skill => skill.Id == "caveman")));
        Assert.Equal(SkillCost.FreeTier, SkillCosts.Of(skills.Single(skill => skill.Id == "exa-search")));
        Assert.Equal(SkillCost.Free, SkillCosts.Of(skills.Single(skill => skill.Id == "playwright-mcp")));
        Assert.Equal("windows", Assert.Single(skills.Single(skill => skill.Id == "windows-mcp").SupportedPlatforms));
        Assert.Equal(SkillTrust.Community, skills.Single(skill => skill.Id == "windows-mcp").Trust);
        Assert.Contains(sandbox.Core().Skills.Catalog().Packs, pack => pack.Id == "token-saver");
        foreach (var skill in skills.Where(skill => skill.Auth?.Type == SkillAuthType.ApiKey)) Assert.NotEqual(SkillCost.Local, SkillCosts.Of(skill));
    }

    [Fact]
    public void Codex_provider_arguments_never_contain_the_key()
    {
        var args = CodexAdapter.BuildArguments(new AgentInvocation
        {
            Prompt = "x", WorkingDirectory = ".", Model = "qwen3:8b",
            Provider = new ProviderEndpoint("ollama-local", "Ollama on this computer", "http://127.0.0.1:11434/v1", true, "sk-secret-value"),
        }, "last.txt");
        var joined = string.Join(' ', args);
        Assert.Contains("model_provider=\"agexprovider\"", joined);
        Assert.Contains("wire_api=\"responses\"", joined);
        Assert.DoesNotContain("sk-secret-value", joined);
    }

    [Fact]
    public async Task Save_tokens_sends_a_shorter_leader_prompt_and_says_so()
    {
        async Task<(int Length, Session Session)> RunWith(EfficiencyMode mode)
        {
            using var sandbox = new Sandbox("eff-" + mode);
            for (var index = 0; index < 250; index++)
            {
                var folder = Path.Combine(sandbox.Project, "src", "module" + index / 25);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, $"file{index}.cs"), "class C {}");
            }
            sandbox.LeaderPlans("""{"goal_status":"COMPLETE","reason":"Done.","verification":"Checked.","tasks":[]}""");
            var core = sandbox.Core();
            core.Settings.Efficiency = mode;
            core.Settings.Leader = "codex";
            var profile = core.SettingsStore.LoadProject(sandbox.Project);
            var engine = core.CreateRequest(sandbox.Project, "Summarise the modules", new ScriptedHost(), core.BuildMembers(profile, agentIds: ["codex"]), profile, [], []);
            var session = await engine.RunAsync(CancellationToken.None);
            var leader = sandbox.FakeLog().First(line => line.Contains("|leader|"));
            return (int.Parse(leader.Split('|')[2]), session);
        }
        var balanced = await RunWith(EfficiencyMode.Balanced);
        var save = await RunWith(EfficiencyMode.SaveTokens);
        output.WriteLine($"Leader prompt: Balanced {balanced.Length} chars, Save tokens {save.Length} chars ({100.0 * (balanced.Length - save.Length) / balanced.Length:0}% shorter).");
        Assert.True(save.Length < balanced.Length * 0.8, $"{save.Length} vs {balanced.Length}");
        Assert.Contains(save.Session.Timeline, entry => entry.Text.StartsWith("Efficiency: Save tokens"));
        Assert.DoesNotContain(balanced.Session.Timeline, entry => entry.Text.StartsWith("Efficiency:"));
    }
}
