using System.Collections.Concurrent;
using System.Text;
using Agex.Core.Runtime;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Agex.Desktop.Pages;

/// <summary>A project-scoped interactive command tab. Output is queued off the UI thread and rendered in batches.</summary>
public sealed class TerminalTab : UserControl
{
    private readonly MainWindow _window;
    private readonly TextBox _command = new() { PlaceholderText = "Command", MinWidth = 180 };
    private readonly TextBlock _status = Kit.Text("Stopped", "caption");
    private readonly TextBlock _directory = Kit.Text("", "caption");
    private readonly TextBlock _output = Kit.Selectable("", "mono");
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly StringBuilder _shown = new();
    private readonly Avalonia.Threading.DispatcherTimer _flush = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private ProcessHandle? _handle;
    private CancellationTokenSource? _cancel;
    private string _lastCommand = "";
    private int _queued;
    private bool _running;

    public TerminalTab(MainWindow window)
    {
        _window = window;
        _output.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _command.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; _ = RunAsync(_command.Text ?? ""); }
        };
        var outputScroll = new ScrollViewer { Content = _output, Padding = new Thickness(8), VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var buttons = Kit.Wrap(
            Kit.Button("Run", () => _ = RunAsync(_command.Text ?? ""), "primary", Icons.Play),
            Kit.Button("Stop", Stop, "subtle", Icons.Stop),
            Kit.Button("Restart", () => _ = RestartAsync(), "subtle", Icons.Refresh),
            Kit.Button("Clear", Clear, "subtle"),
            Kit.Button("Open externally", OpenExternally, "subtle", Icons.External));
        var top = Kit.Column(6, _command, buttons, _status, _directory);
        var layout = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(outputScroll);
        Content = layout;
        _flush.Tick += (_, _) => Flush();
        AttachedToVisualTree += (_, _) => { _flush.Start(); UpdateDirectory(); };
        DetachedFromVisualTree += (_, _) => _flush.Stop();
        UpdateDirectory();
    }

    private void UpdateDirectory()
    {
        _directory.Text = _window.Workspace.Project is { } project
            ? "Working directory: " + Redactor.RedactPaths(project.Path)
            : "Choose a project before running commands.";
    }

    private (string File, string[] Args)? Shell(string command)
    {
        var platform = _window.Workspace.Core.Platform;
        if (OperatingSystem.IsWindows())
        {
            var shell = platform.FindExecutable("pwsh") ?? platform.FindExecutable("powershell");
            return shell is null ? null : (shell, ["-NoProfile", "-NonInteractive", "-Command", command]);
        }
        var file = OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/sh";
        return File.Exists(file) ? (file, ["-lc", command]) : null;
    }

    private async Task RunAsync(string command)
    {
        if (_running || string.IsNullOrWhiteSpace(command)) return;
        if (_window.Workspace.Project is not { } project || !Directory.Exists(project.Path))
        {
            _status.Text = "Choose a project first.";
            return;
        }
        if (Shell(command) is not { } shell)
        {
            _status.Text = "No supported shell found.";
            return;
        }
        _running = true;
        _lastCommand = command;
        _cancel = new CancellationTokenSource();
        _status.Text = "Running";
        UpdateDirectory();
        Enqueue("> " + command);
        try
        {
            var result = await _window.Workspace.Core.Runner.RunAsync(new ProcessRequest
            {
                FileName = shell.File,
                Arguments = shell.Args,
                WorkingDirectory = project.Path,
                Label = "Workspace terminal",
                HideCommandArguments = true,
                Timeout = TimeSpan.FromHours(2),
                MaxCaptureChars = 1024,
                OnStarted = handle => _handle = handle,
                OnStdoutLine = Enqueue,
                OnStderrLine = line => Enqueue("stderr: " + line),
            }, _cancel.Token);
            Enqueue(result.Outcome == ProcessOutcome.Ok ? "Exited: 0" : $"Stopped: {result.Outcome} (exit {result.ExitCode?.ToString() ?? "unknown"})");
            _status.Text = result.Outcome == ProcessOutcome.Ok ? "Exited: 0" : $"Stopped: {result.Outcome}";
        }
        catch (Exception ex)
        {
            Enqueue("Failed: " + Redactor.Redact(ex.Message));
            _status.Text = "Failed";
        }
        finally
        {
            _handle = null;
            _cancel.Dispose();
            _cancel = null;
            _running = false;
            Flush();
        }
    }

    private void Enqueue(string line)
    {
        _pending.Enqueue(line);
        if (Interlocked.Increment(ref _queued) <= 2000) return;
        if (_pending.TryDequeue(out _)) Interlocked.Decrement(ref _queued);
    }

    private void Flush()
    {
        if (!IsVisible) return;
        var count = 0;
        while (count++ < 200 && _pending.TryDequeue(out var line))
        {
            Interlocked.Decrement(ref _queued);
            _shown.AppendLine(Redactor.Redact(line));
        }
        if (count == 1) return;
        if (_shown.Length > 100_000) _shown.Remove(0, _shown.Length - 80_000);
        _output.Text = _shown.ToString();
    }

    private void Stop()
    {
        _handle?.Kill();
        _cancel?.Cancel();
    }

    public void Close() => Stop();

    private async Task RestartAsync()
    {
        if (_running || _lastCommand.Length == 0) return;
        if (await _window.ConfirmAsync("Run this command again?", "Restarting a command may repeat its changes.", "Run again", "Cancel"))
            await RunAsync(_lastCommand);
    }

    private void Clear()
    {
        _shown.Clear();
        while (_pending.TryDequeue(out _)) Interlocked.Decrement(ref _queued);
        _output.Text = "";
    }

    private void OpenExternally()
    {
        if (_window.Workspace.Project is not { } project || Shell(_command.Text ?? "") is not { } shell) return;
        _window.Workspace.Core.Platform.RunInTerminal(shell.File, shell.Args, project.Path);
    }
}

/// <summary>Read-only view of one command reported by an agent. It never reruns the command.</summary>
public sealed class ObservedTerminalTab : UserControl
{
    private readonly TextBlock _status = Kit.Text("Running", "caption");
    private readonly TextBlock _output = Kit.Selectable("", "mono");
    private readonly StringBuilder _shown = new();

    public ObservedTerminalTab(string agent, string directory, string command = "")
    {
        _output.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var title = Kit.Text(command.Length > 0 ? Redactor.Redact(Agex.Core.Sessions.CommandText.Display(command.Split('\n')[0])) : agent + " command", "mono");
        title.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        title.MaxLines = 3;
        var clear = Kit.Button("Clear", () => { _shown.Clear(); _output.Text = ""; }, "subtle");
        Content = new ScrollViewer
        {
            Padding = new Thickness(12, 8),
            Content = Kit.Column(6,
                title,
                Kit.Text($"{agent} · {Redactor.RedactPaths(directory)}", "caption"),
                Kit.Row(8, _status, clear),
                _output),
        };
    }

    /// <summary>The request ended: a command that never reported its end is shown as finished.</summary>
    public void MarkEnded()
    {
        if (_status.Text == "Running") _status.Text = "Finished (the agent reported no exit code)";
    }

    public void Observe(Agex.Core.Agents.AgentActivity activity)
    {
        if (activity.Kind == Agex.Core.Agents.ActivityKind.ToolStarted) return; // the command is the title
        else if (activity.Kind == Agex.Core.Agents.ActivityKind.Output) Append(activity.Text);
        else if (activity.Kind == Agex.Core.Agents.ActivityKind.ToolFinished)
            _status.Text = activity.ExitCode is { } code ? "Exited: " + code : "Finished (exit code unavailable)";
    }

    private void Append(string text)
    {
        _shown.AppendLine(Redactor.Redact(text));
        if (_shown.Length > 100_000) _shown.Remove(0, _shown.Length - 80_000);
        _output.Text = _shown.ToString();
    }
}
