using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Agex.Core.Platform;

namespace Agex.Core.Runtime;

public enum ProcessOutcome { Ok, ExitNonZero, TimedOut, Cancelled, StartFailed, Error }

public sealed class ProcessRequest
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public required string WorkingDirectory { get; init; }
    /// <summary>Written to stdin as UTF-8 without BOM, then stdin is closed.</summary>
    public string? StdinText { get; init; }
    /// <summary>When true stdin stays open after <see cref="StdinText"/>; the caller closes it through <see cref="ProcessHandle"/>.</summary>
    public bool KeepStdinOpen { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);
    public IDictionary<string, string?>? Environment { get; init; }
    /// <summary>Environment variable names whose values must never appear in diagnostics.</summary>
    public IReadOnlyCollection<string> SecretEnvironmentNames { get; init; } = [];
    public string Label { get; init; } = "process";
    public Action<string>? OnStdoutLine { get; init; }
    public Action<string>? OnStderrLine { get; init; }
    public Action<ProcessHandle>? OnStarted { get; init; }
    public int MaxCaptureChars { get; init; } = 4 * 1024 * 1024;
}

/// <summary>A running child process owned by AGEX.</summary>
public sealed class ProcessHandle(Process process)
{
    public int Pid => process.Id;
    public void CloseStdin() { try { process.StandardInput.Close(); } catch (Exception) { } }
    public bool HasExited { get { try { return process.HasExited; } catch (Exception) { return true; } } }
    public void Kill() => ProcessRunner.KillTree(process);
    public void WriteStdinLine(string text)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text + "\n");
        process.StandardInput.BaseStream.Write(bytes);
        process.StandardInput.BaseStream.Flush();
    }
}

public sealed record ProcessResult
{
    public ProcessOutcome Outcome { get; init; }
    public int? ExitCode { get; init; }
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
    public int Pid { get; init; }
    /// <summary>What was run, with secrets removed. Shown in Diagnostics.</summary>
    public string CommandLine { get; init; } = "";
    public string LaunchKind { get; init; } = "direct";
    public TimeSpan Duration { get; init; }
    public string ErrorMessage { get; init; } = "";
    public bool OutputTruncated { get; init; }
    public bool Succeeded => Outcome == ProcessOutcome.Ok;
}

/// <summary>
/// The only place AGEX starts external programs. Handles UTF-8 pipes without
/// BOM, concurrent stdout/stderr reading, timeouts, cancellation, process-tree
/// cleanup, owned-process tracking and sanitized command details.
/// </summary>
public sealed class ProcessRunner
{
    private readonly IPlatformService _platform;
    private readonly AgexLog? _log;
    private readonly ConcurrentDictionary<int, (Process Process, string Label)> _owned = new();

    public ProcessRunner(IPlatformService platform, AgexLog? log = null)
    {
        _platform = platform;
        _log = log;
    }

    public IReadOnlyCollection<int> OwnedProcessIds => _owned.Keys.ToArray();

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var display = DescribeCommand(request.FileName, request.Arguments);
        LaunchTarget target;
        try
        {
            if (!File.Exists(request.FileName)) throw new FileNotFoundException($"Program not found: {request.FileName}");
            if (!Directory.Exists(request.WorkingDirectory)) throw new DirectoryNotFoundException($"Folder not found: {request.WorkingDirectory}");
            target = ResolveLaunchTarget(request.FileName, request.Arguments);
        }
        catch (Exception ex)
        {
            return new ProcessResult { Outcome = ProcessOutcome.StartFailed, CommandLine = display, ErrorMessage = ex.Message, Duration = started.Elapsed };
        }

        var psi = new ProcessStartInfo(target.FileName)
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in target.Arguments) psi.ArgumentList.Add(argument);
        psi.Environment.Clear();
        foreach (var pair in request.Environment ?? _platform.ChildEnvironment())
            if (pair.Value is not null) psi.Environment[pair.Key] = pair.Value;

        var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("The program did not start.");
        }
        catch (Exception ex)
        {
            process.Dispose();
            _log?.Write("process_start_failed", new { label = request.Label, command = display, error = ex.Message });
            return new ProcessResult { Outcome = ProcessOutcome.StartFailed, CommandLine = display, LaunchKind = target.Kind, ErrorMessage = ex.Message, Duration = started.Elapsed };
        }

        var pid = process.Id;
        _owned[pid] = (process, request.Label);
        _log?.Write("process_start", new { label = request.Label, pid, command = display, kind = target.Kind, cwd = Redactor.RedactPaths(request.WorkingDirectory) });
        var stdout = new CappedBuffer(request.MaxCaptureChars);
        var stderr = new CappedBuffer(request.MaxCaptureChars);
        ProcessOutcome outcome;
        string error = "";
        try
        {
            request.OnStarted?.Invoke(new ProcessHandle(process));
            var stdoutPump = PumpAsync(process.StandardOutput, stdout, request.OnStdoutLine);
            var stderrPump = PumpAsync(process.StandardError, stderr, request.OnStderrLine);
            var stdinTask = WriteStdinAsync(process, request.StdinText, request.KeepStdinOpen);

            using var timeout = new CancellationTokenSource(request.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                // Exit can precede the last pipe data; drain both pipes (bounded).
                await Task.WhenAll(stdoutPump, stderrPump).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                outcome = process.ExitCode == 0 ? ProcessOutcome.Ok : ProcessOutcome.ExitNonZero;
            }
            catch (OperationCanceledException)
            {
                outcome = cancellationToken.IsCancellationRequested ? ProcessOutcome.Cancelled : ProcessOutcome.TimedOut;
                KillTree(process);
            }
            catch (TimeoutException)
            {
                // Pipes held open by a grandchild after the main process exited.
                outcome = process.ExitCode == 0 ? ProcessOutcome.Ok : ProcessOutcome.ExitNonZero;
                KillTree(process);
            }
            try { await stdinTask.ConfigureAwait(false); } catch (Exception) { }
        }
        catch (Exception ex)
        {
            outcome = ProcessOutcome.Error;
            error = ex.Message;
            KillTree(process);
        }
        finally
        {
            _owned.TryRemove(pid, out _);
        }

        int? exitCode = null;
        try { if (process.HasExited) exitCode = process.ExitCode; } catch (Exception) { }
        process.Dispose();
        var result = new ProcessResult
        {
            Outcome = outcome, ExitCode = exitCode, Pid = pid, CommandLine = display, LaunchKind = target.Kind,
            Stdout = stdout.ToString(), Stderr = stderr.ToString(), OutputTruncated = stdout.Truncated || stderr.Truncated,
            Duration = started.Elapsed, ErrorMessage = error,
        };
        _log?.Write("process_end", new { label = request.Label, pid, outcome = outcome.ToString(), exit = exitCode, seconds = Math.Round(result.Duration.TotalSeconds, 1) });
        return result;
    }

    private static async Task WriteStdinAsync(Process process, string? text, bool keepOpen)
    {
        try
        {
            if (!string.IsNullOrEmpty(text))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                await process.StandardInput.BaseStream.WriteAsync(bytes).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (IOException) { /* child exited before reading stdin */ }
        finally
        {
            if (!keepOpen) { try { process.StandardInput.Close(); } catch (Exception) { } }
        }
    }

    private static async Task PumpAsync(StreamReader reader, CappedBuffer buffer, Action<string>? onLine)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            buffer.AppendLine(line);
            if (onLine is null) continue;
            try { onLine(line); } catch (Exception) { /* a UI callback must not stop the pump */ }
        }
    }

    public static void KillTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception) { }
    }

    /// <summary>Stops every process this runner started and has not seen exit (used on cancel and shutdown).</summary>
    public IReadOnlyList<int> StopAll()
    {
        var stopped = new List<int>();
        foreach (var (pid, entry) in _owned.ToArray())
        {
            KillTree(entry.Process);
            stopped.Add(pid);
        }
        return stopped;
    }

    internal sealed record LaunchTarget(string FileName, IReadOnlyList<string> Arguments, string Kind);

    private static readonly Regex CmdShimScript = new(@"""%(?:~)?dp0%?\\(?<script>[^""]+\.(?:js|mjs|cjs))""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // cmd.exe re-parses its command line; these characters can change what runs.
    private static readonly char[] CmdMetacharacters = ['&', '|', '<', '>', '^', '%', '!', '"', '\r', '\n', '(', ')'];

    /// <summary>
    /// Windows batch files are run through cmd.exe, which has its own parsing
    /// rules. npm shims are unwrapped to node + script; any other batch file is
    /// run only when every argument is free of cmd metacharacters.
    /// </summary>
    internal LaunchTarget ResolveLaunchTarget(string fileName, IReadOnlyList<string> arguments)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!OperatingSystem.IsWindows() || (extension != ".cmd" && extension != ".bat"))
            return new LaunchTarget(fileName, arguments, "direct");

        var directory = Path.GetDirectoryName(fileName)!;
        var match = CmdShimScript.Match(SafeRead(fileName));
        if (match.Success)
        {
            var script = Path.GetFullPath(Path.Combine(directory, match.Groups["script"].Value));
            if (File.Exists(script))
            {
                var node = Path.Combine(directory, "node.exe");
                if (!File.Exists(node)) node = _platform.FindExecutable("node") ?? "";
                if (node.Length > 0 && Path.GetExtension(node).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    return new LaunchTarget(node, [script, .. arguments], "node-shim");
            }
        }
        foreach (var argument in arguments)
        {
            if (argument.IndexOfAny(CmdMetacharacters) >= 0)
                throw new InvalidOperationException($"'{Path.GetFileName(fileName)}' is a batch file and an argument contains characters that cmd.exe would interpret. AGEX refuses to run it this way.");
        }
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return new LaunchTarget(cmd, ["/d", "/s", "/c", string.Join(' ', new[] { fileName }.Concat(arguments).Select(QuoteForCmd))], "batch");
    }

    private static string QuoteForCmd(string value) => value.Length > 0 && value.IndexOfAny([' ', '\t']) < 0 ? value : "\"" + value + "\"";

    private static string SafeRead(string path)
    {
        try { return new FileInfo(path).Length > 64 * 1024 ? "" : File.ReadAllText(path); }
        catch (Exception) { return ""; }
    }

    private static readonly Regex SecretFlag = new(@"^(--?(?:password|passwd|token|api[-_]?key|secret)(?:=))(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Human-readable command with secrets removed and long values shortened.</summary>
    public static string DescribeCommand(string fileName, IEnumerable<string> arguments)
    {
        var parts = new List<string> { Path.GetFileName(fileName) };
        foreach (var argument in arguments)
        {
            var safe = SecretFlag.Replace(Redactor.Redact(argument), "$1<redacted>");
            safe = Redactor.RedactPaths(safe);
            if (safe.Length > 160) safe = safe[..157] + "...";
            parts.Add(safe.Length == 0 || safe.IndexOfAny([' ', '"']) >= 0 ? "\"" + safe.Replace("\"", "\\\"") + "\"" : safe);
        }
        return string.Join(' ', parts);
    }

    private sealed class CappedBuffer(int maxChars)
    {
        private readonly StringBuilder _builder = new();
        private readonly object _lock = new();
        public bool Truncated { get; private set; }

        public void AppendLine(string line)
        {
            lock (_lock)
            {
                if (_builder.Length + line.Length + 1 > maxChars) { Truncated = true; return; }
                _builder.Append(line).Append('\n');
            }
        }

        public override string ToString() { lock (_lock) { return _builder.ToString(); } }
    }
}
