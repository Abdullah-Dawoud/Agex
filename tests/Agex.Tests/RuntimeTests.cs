using System.Diagnostics;
using Agex.Core.Runtime;

namespace Agex.Tests;

public class RuntimeTests
{
    private static ProcessRunner Runner(Sandbox sandbox) => new(sandbox.Platform);

    [Fact]
    public async Task Missing_program_is_reported_as_start_failure_with_command_details()
    {
        using var sandbox = new Sandbox("missing");
        var result = await Runner(sandbox).RunAsync(new ProcessRequest { FileName = Path.Combine(sandbox.Root, "missing.exe"), Arguments = ["exec", "--model", "m1"], WorkingDirectory = sandbox.Project });
        Assert.Equal(ProcessOutcome.StartFailed, result.Outcome);
        Assert.Contains("missing.exe exec --model m1", result.CommandLine);
    }

    [Fact]
    public async Task Utf8_stdin_round_trips_without_bom()
    {
        using var sandbox = new Sandbox("utf8");
        sandbox.Mode("codex", "echo");
        const string goal = "مرحبا 🎉 café 你好";
        var output = Path.Combine(sandbox.Root, "out.txt");
        var result = await Runner(sandbox).RunAsync(new ProcessRequest
        {
            FileName = Sandbox.FakeAgentPath, Arguments = ["exec", "-o", output, "-"], WorkingDirectory = sandbox.Project,
            StdinText = $"ORIGINAL USER GOAL:\n{goal}\nFILES YOU OWN: (none)\n",
        });
        Assert.Equal(ProcessOutcome.Ok, result.Outcome);
        Assert.Equal("ECHO:" + goal, Agex.Core.Orchestration.ExecutorReply.ResultText(File.ReadAllText(output)));
        Assert.EndsWith("nobom", Assert.Single(sandbox.FakeLog()));
    }

    [Fact]
    public async Task Timeout_kills_the_process()
    {
        using var sandbox = new Sandbox("timeout");
        sandbox.Mode("codex", "slow");
        int pid = 0;
        var watch = Stopwatch.StartNew();
        var result = await Runner(sandbox).RunAsync(new ProcessRequest
        {
            FileName = Sandbox.FakeAgentPath, Arguments = ["exec", "-"], WorkingDirectory = sandbox.Project, StdinText = "x",
            Timeout = TimeSpan.FromSeconds(2), OnStarted = handle => pid = handle.Pid,
        });
        Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
        await Task.Delay(300);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Fact]
    public async Task Cancellation_stops_the_process()
    {
        using var sandbox = new Sandbox("cancelrun");
        sandbox.Mode("codex", "slow");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        var runner = Runner(sandbox);
        var result = await runner.RunAsync(new ProcessRequest { FileName = Sandbox.FakeAgentPath, Arguments = ["exec", "-"], WorkingDirectory = sandbox.Project, StdinText = "x" }, cancellation.Token);
        Assert.Equal(ProcessOutcome.Cancelled, result.Outcome);
        Assert.Empty(runner.OwnedProcessIds);
    }

    [Fact]
    public async Task Huge_output_does_not_deadlock_and_is_capped()
    {
        using var sandbox = new Sandbox("huge");
        sandbox.Mode("codex", "huge");
        var lines = 0;
        var result = await Runner(sandbox).RunAsync(new ProcessRequest
        {
            FileName = Sandbox.FakeAgentPath, Arguments = ["exec", "-"], WorkingDirectory = sandbox.Project, StdinText = "x",
            MaxCaptureChars = 1_000_000, OnStdoutLine = _ => lines++, Timeout = TimeSpan.FromMinutes(2),
        });
        Assert.Equal(ProcessOutcome.Ok, result.Outcome);
        Assert.True(result.OutputTruncated);
        Assert.True(lines > 200_000);
        Assert.True(result.Stdout.Length <= 1_000_000);
    }

    [Fact]
    public async Task Batch_files_with_cmd_metacharacters_are_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var sandbox = new Sandbox("batch");
        var script = Path.Combine(sandbox.Root, "tool.cmd");
        File.WriteAllText(script, "@echo %*\r\n");
        var result = await Runner(sandbox).RunAsync(new ProcessRequest { FileName = script, Arguments = ["safe", "a&calc"], WorkingDirectory = sandbox.Project });
        Assert.Equal(ProcessOutcome.StartFailed, result.Outcome);
        Assert.Contains("cmd.exe", result.ErrorMessage);
        var ok = await Runner(sandbox).RunAsync(new ProcessRequest { FileName = script, Arguments = ["hello", "world"], WorkingDirectory = sandbox.Project });
        Assert.Equal(ProcessOutcome.Ok, ok.Outcome);
        Assert.Contains("hello world", ok.Stdout);
    }

    [Fact]
    public async Task Batch_files_in_folders_with_spaces_run()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var sandbox = new Sandbox("batch space");
        var folder = Path.Combine(sandbox.Root, "Author Software", "bin");
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "tool.cmd");
        File.WriteAllText(script, "@echo %*\r\n");
        var result = await Runner(sandbox).RunAsync(new ProcessRequest { FileName = script, Arguments = ["--version", "two words"], WorkingDirectory = sandbox.Project });
        Assert.Equal(ProcessOutcome.Ok, result.Outcome);
        Assert.Contains("--version \"two words\"", result.Stdout);
    }

    [Fact]
    public void Npm_shims_for_native_programs_run_the_program_directly()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var sandbox = new Sandbox("exe shim");
        var folder = Path.Combine(sandbox.Root, "nvm installs");
        var exe = Path.Combine(folder, "node_modules", "@opencode", "cli", "bin", "opencode.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllBytes(exe, [0x4D, 0x5A]);
        var shim = Path.Combine(folder, "opencode.cmd");
        File.WriteAllText(shim, "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\nSETLOCAL\r\nCALL :find_dp0\r\n\"%dp0%\\node_modules\\@opencode\\cli\\bin\\opencode.exe\"   %*\r\n");
        var target = Runner(sandbox).ResolveLaunchTarget(shim, ["run", "a&b"]);
        Assert.Equal("exe-shim", target.Kind);
        Assert.Equal(exe, target.FileName);
        Assert.Equal(["run", "a&b"], target.Arguments);
    }

    [Theory]
    [InlineData("key sk-proj-abcdefghijklmnopqrstuvwxyz0123", "sk-proj")]
    [InlineData("token ghp_abcdefghijklmnopqrstuvwxyz0123456789", "ghp_")]
    [InlineData("Authorization: Bearer abcdefghijklmnop.qrstuv", "abcdefghijklmnop")]
    [InlineData("password=hunter22secret", "hunter22")]
    [InlineData("AIzaSyA1234567890abcdefghijklmnopqrstu", "AIzaSy")]
    public void Redactor_removes_secrets(string input, string secretPart)
    {
        var output = Redactor.Redact(input);
        Assert.DoesNotContain(secretPart, output.Replace("[REDACTED]", ""));
        Assert.Contains("[REDACTED]", output);
    }

    [Fact]
    public void Command_description_hides_secret_flags()
    {
        var text = ProcessRunner.DescribeCommand("/usr/bin/tool", ["--token=abcdefgh123", "--model", "m"]);
        Assert.Contains("--token=<redacted>", text);
        Assert.DoesNotContain("abcdefgh123", text);
    }
}
