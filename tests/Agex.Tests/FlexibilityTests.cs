using System.Net;
using System.Net.Sockets;
using System.Text;
using Agex.Core.Agents;
using Agex.Core.Settings;

namespace Agex.Tests;

public class FlexibilityTests
{
    [Fact]
    public void Two_projects_keep_independent_agents_models_skills_and_drafts()
    {
        using var sandbox = new Sandbox("project-state");
        var second = Path.Combine(sandbox.Root, "second project");
        Directory.CreateDirectory(second);
        var core = sandbox.Core();
        var first = core.SettingsStore.LoadProject(sandbox.Project);
        var other = core.SettingsStore.LoadProject(second);
        first.PreferredAgents = ["codex"];
        first.AgentOptions["codex"] = new AgentOptions { Model = "model-one", CustomModel = true };
        first.SkillOverrides["sample"] = true;
        first.DraftRequest = "first draft";
        other.PreferredAgents = ["hermes"];
        other.AgentOptions["hermes"] = new AgentOptions { Model = "model-two", CustomModel = true };
        other.SkillOverrides["sample"] = false;
        other.DraftRequest = "second draft";
        core.SettingsStore.SaveProject(first);
        core.SettingsStore.SaveProject(other);
        var restoredFirst = core.SettingsStore.LoadProject(sandbox.Project);
        var restoredOther = core.SettingsStore.LoadProject(second);
        Assert.Equal("first draft", restoredFirst.DraftRequest);
        Assert.Equal("second draft", restoredOther.DraftRequest);
        Assert.True(restoredFirst.SkillOverrides["sample"]);
        Assert.False(restoredOther.SkillOverrides["sample"]);
        Assert.Equal("model-one", Assert.Single(core.BuildMembers(restoredFirst)).Model);
        Assert.Equal("model-two", Assert.Single(core.BuildMembers(restoredOther)).Model);
    }

    [Fact]
    public async Task AGEX_model_adapter_uses_provider_model_and_auto_route()
    {
        using var sandbox = new Sandbox("provider-route");
        using var server = new FakeProvider();
        var core = sandbox.Core();
        core.Settings.Providers.Add(new ProviderProfile { Id = "omniroute", Name = "OmniRoute", BaseUrl = server.Url });
        var profile = core.SettingsStore.LoadProject(sandbox.Project);
        profile.PreferredAgents = ["agex-models"];
        profile.AgentOptions["agex-models"] = new AgentOptions { ProviderId = "omniroute", Model = "auto", CustomModel = true };
        var member = Assert.Single(core.BuildMembers(profile));
        Assert.Equal("auto", member.Model);
        Assert.Equal(server.Url, member.Provider!.BaseUrl);
        var adapter = Assert.IsType<ProviderAdapter>(member.Adapter);
        var detection = await core.Registry.CheckHealthAsync(adapter.Id, CancellationToken.None);
        Assert.Equal(AgentStatus.Supported, detection.Status);
        var result = await adapter.RunAsync(detection, new AgentInvocation
        {
            Prompt = "What is in this project?", WorkingDirectory = sandbox.Project, Model = member.Model, Provider = member.Provider,
        }, CancellationToken.None);
        Assert.True(result.Success, result.Reason);
        Assert.Equal("Provider answered", result.Text);
        Assert.Contains("\"model\":\"auto\"", server.LastBody);
    }

    private sealed class FakeProvider : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Url { get; }
        public string LastBody { get; private set; } = "";

        public FakeProvider()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/v1";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); } catch { return; }
                    LastBody = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
                    var body = context.Request.Url!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
                        ? """{"data":[{"id":"auto"},{"id":"provider/model"}]}"""
                        : """{"choices":[{"message":{"content":"Provider answered"}}],"usage":{"prompt_tokens":10,"completion_tokens":2}}""";
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            });
        }
        public void Dispose() { _listener.Stop(); _listener.Close(); }
    }
}
