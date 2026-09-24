using System.Text;
using Agex.Core;
using Agex.Core.Orchestration;
using Agex.Core.Platform;
using Agex.Core.Sessions;

namespace Agex.Tests;

/// <summary>
/// An isolated AGEX environment: its own data folder (AGEX_HOME), a temporary
/// project and fake agents for every supported CLI. Nothing touches the real
/// user profile or real agents.
/// </summary>
public sealed class Sandbox : IDisposable
{
    private static readonly string[] Variables =
    [
        "AGEX_HOME", "AGEX_CODEX_PATH", "AGEX_ANTIGRAVITY_PATH", "AGEX_CLAUDE_CODE_PATH", "AGEX_GEMINI_CLI_PATH", "AGEX_OLLAMA_PATH",
        "FAKE_PLAN_DIR", "FAKE_LOG", "FAKE_CODEX_MODE", "FAKE_AGY_MODE", "FAKE_CLAUDE_MODE", "FAKE_GEMINI_MODE", "OLLAMA_HOST", "AGEX_SKIP_LOGIN_SHELL",
    ];
    private readonly Dictionary<string, string?> _saved = new();

    public Sandbox(string name = "")
    {
        foreach (var variable in Variables) _saved[variable] = Environment.GetEnvironmentVariable(variable);
        Root = Path.Combine(Path.GetTempPath(), "agex-tests", (name.Length > 0 ? name + "-" : "") + Guid.NewGuid().ToString("N")[..10]);
        Home = Path.Combine(Root, "home");
        Project = Path.Combine(Root, "project with spaces é");
        Plans = Path.Combine(Root, "plans");
        LogFile = Path.Combine(Root, "fake.log");
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Project);
        Directory.CreateDirectory(Plans);
        Environment.SetEnvironmentVariable("AGEX_HOME", Home);
        Environment.SetEnvironmentVariable("FAKE_PLAN_DIR", Plans);
        Environment.SetEnvironmentVariable("FAKE_LOG", LogFile);
        Environment.SetEnvironmentVariable("AGEX_SKIP_LOGIN_SHELL", "1");
        // Nothing listens here, so Ollama is always "not running" in tests.
        Environment.SetEnvironmentVariable("OLLAMA_HOST", "127.0.0.1:1");
        foreach (var id in new[] { "CODEX", "ANTIGRAVITY", "CLAUDE_CODE", "GEMINI_CLI" }) Environment.SetEnvironmentVariable($"AGEX_{id}_PATH", FakeAgentPath);
        Environment.SetEnvironmentVariable("AGEX_OLLAMA_PATH", null);
        Platform = PlatformFactory.Create(Home);
    }

    public string Root { get; }
    public string Home { get; }
    public string Project { get; }
    public string Plans { get; }
    public string LogFile { get; }
    public IPlatformService Platform { get; }

    public static string FakeAgentPath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Agex.slnx"))) directory = directory.Parent;
            if (directory is null) throw new InvalidOperationException("Repository root not found.");
            var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
            var name = OperatingSystem.IsWindows() ? "Agex.FakeAgent.exe" : "Agex.FakeAgent";
            return Path.Combine(directory.FullName, "tests", "Agex.FakeAgent", "bin", configuration, "net10.0", name);
        }
    }

    public void Mode(string agent, string mode) => Environment.SetEnvironmentVariable($"FAKE_{agent.ToUpperInvariant()}_MODE", mode);

    /// <summary>Leader replies in order: the first call gets plans[0], and so on.</summary>
    public void LeaderPlans(params string[] plans)
    {
        for (var index = 0; index < plans.Length; index++)
            File.WriteAllText(Path.Combine(Plans, $"leader-{index + 1}.json"), plans[index], new UTF8Encoding(false));
    }

    public string[] FakeLog() => File.Exists(LogFile) ? File.ReadAllLines(LogFile) : [];

    public AgexCore Core(bool safeMode = false)
    {
        var core = new AgexCore(Platform, safeMode);
        core.Start();
        return core;
    }

    public void Dispose()
    {
        foreach (var (variable, value) in _saved) Environment.SetEnvironmentVariable(variable, value);
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Scripted user for the engine: fixed approval decision and answers.</summary>
public sealed class ScriptedHost(ApprovalDecision approval = ApprovalDecision.Allow, params string[] answers) : IEngineHost
{
    private readonly Queue<string> _answers = new(answers);
    public int ApprovalRequests { get; private set; }
    public List<string> Questions { get; } = [];

    public Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        ApprovalRequests++;
        return Task.FromResult(approval);
    }

    public Task<string?> AskUserAsync(PendingQuestion question, CancellationToken cancellationToken)
    {
        Questions.Add(question.Question);
        return Task.FromResult<string?>(_answers.Count > 0 ? _answers.Dequeue() : null);
    }
}
