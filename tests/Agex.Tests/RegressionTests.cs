using System.Net;
using System.Net.Sockets;
using System.Text;
using Agex.Core.Orchestration;
using Agex.Core.Sessions;

namespace Agex.Tests;

/// <summary>Bugs found by real-agent runs; each test pins the fix.</summary>
public class RegressionTests
{
    [Theory]
    [InlineData("```json\n{\"goal_status\":\"COMPLETE\",\"reason\":\"ok\",\"verification\":\"v\",\"tasks\":[]}\n```\nDone.")]
    [InlineData("Here is the plan:\n{\"goal_status\":\"COMPLETE\",\"reason\":\"a } in text\",\"verification\":\"v\",\"tasks\":[]} ```")]
    public void Leader_json_followed_by_text_is_accepted(string reply)
    {
        var plan = LeaderPlanParser.Parse(reply, [], _ => "codex");
        Assert.Equal(GoalStatus.Complete, plan.Status);
    }

    [Fact]
    public async Task Gemini_without_credentials_is_reported_as_sign_in_required()
    {
        // Real Gemini CLI 0.9 prints its startup error as a JSON document on stderr; AGEX showed only '"error": {'.
        using var sandbox = new Sandbox("gemini-auth");
        sandbox.Mode("gemini", "auth-json");
        var core = sandbox.Core();
        core.Settings.Leader = "gemini-cli";
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        var engine = core.CreateRequest(sandbox.Project, "Hello", new ScriptedHost(), core.BuildMembers(profile, agentIds: ["gemini-cli"]), profile, [], []);
        var session = await engine.RunAsync(CancellationToken.None);
        Assert.Equal(SessionStatus.StartFailed, session.Status);
        Assert.Contains("needs you to sign in", session.Outcome!.Reason);
        Assert.DoesNotContain("\"error\": {", session.Outcome.Reason);
    }

    [Fact]
    public void A_second_process_does_not_interrupt_a_session_that_is_still_running()
    {
        using var sandbox = new Sandbox("owner");
        var store = new SessionStore(sandbox.Platform.Paths.Sessions);
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var live = new Session { Request = "live", Status = SessionStatus.Running, OwnerPid = self.Id, OwnerStarted = new DateTimeOffset(self.StartTime) };
        var dead = new Session { Request = "dead", Status = SessionStatus.Running, OwnerPid = self.Id, OwnerStarted = DateTimeOffset.UtcNow.AddDays(-3) };
        store.Save(live);
        store.Save(dead);
        Assert.Equal([dead.Id], store.MarkInterrupted());
        Assert.Equal(SessionStatus.Running, store.Load(live.Id)!.Status);
    }

    /// <summary>Minimal Ollama API: /api/version, /api/tags and a streaming /api/chat.</summary>
    private sealed class FakeOllama : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string LastPrompt { get; private set; } = "";
        public int Port { get; }

        public FakeOllama(string answer)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); } catch (Exception) { return; }
                    var path = context.Request.Url!.AbsolutePath;
                    string body;
                    if (path == "/api/version") body = "{\"version\":\"9.9.9\"}";
                    else if (path == "/api/tags") body = "{\"models\":[{\"name\":\"tiny:1b\"},{\"name\":\"big:cloud\"}]}";
                    else
                    {
                        LastPrompt = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
                        body = "{\"message\":{\"role\":\"assistant\",\"content\":\"<think>hidden</think>\"},\"done\":false}\n" +
                               "{\"message\":{\"role\":\"assistant\",\"content\":" + System.Text.Json.JsonSerializer.Serialize(answer) + "},\"done\":false}\n" +
                               "{\"done\":true,\"prompt_eval_count\":12,\"eval_count\":3}\n";
                    }
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            });
        }

        public void Dispose() { _listener.Stop(); _listener.Close(); }
    }

    [Fact]
    public async Task Text_only_team_gets_a_direct_answer_and_hidden_thinking_is_removed()
    {
        using var sandbox = new Sandbox("ollama");
        using var ollama = new FakeOllama("391 مرحبا");
        Environment.SetEnvironmentVariable("OLLAMA_HOST", $"127.0.0.1:{ollama.Port}");
        Environment.SetEnvironmentVariable("AGEX_OLLAMA_PATH", Sandbox.FakeAgentPath);
        var core = sandbox.Core();
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        var members = core.BuildMembers(profile, agentIds: ["ollama"]);
        Assert.Equal(Agex.Core.Agents.PrivacyKind.Mixed, Assert.Single(members).Privacy);
        var engine = core.CreateRequest(sandbox.Project, "What is 17 x 23?", new ScriptedHost(), members, profile, [], []);
        var session = await engine.RunAsync(CancellationToken.None);
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.Equal("391 مرحبا", session.Outcome!.Reason);
        Assert.DoesNotContain(session.Messages, message => message.Text.Contains("hidden"));
        Assert.Contains("What is 17 x 23?", ollama.LastPrompt);
        Assert.Equal(12, session.Usage["ollama"].InputTokens);
        Assert.Equal(0m, session.Usage["ollama"].CostUsd);
    }
}
