using System.Runtime.Versioning;
using System.Text;

namespace Agex.Core.Platform;

/// <summary>Behaviour shared by macOS and Linux.</summary>
[UnsupportedOSPlatform("windows")]
public abstract class UnixPlatformServiceBase(AppPaths paths) : PlatformServiceBase(paths)
{
    protected static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    protected override IReadOnlyList<string> BuildSearchPath()
    {
        var list = new List<string>();
        AddPathEntries(list, Environment.GetEnvironmentVariable("PATH"));
        // Apps started from the Dock, Finder or a desktop launcher get a minimal
        // PATH. Ask the user's login shell for the PATH a terminal would have.
        AddPathEntries(list, ReadLoginShellPath());
        foreach (var extra in ExtraSearchDirectories()) AddPathEntry(list, extra);
        return list;
    }

    private static string? ReadLoginShellPath()
    {
        if (Environment.GetEnvironmentVariable("AGEX_SKIP_LOGIN_SHELL") == "1") return null;
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shell) || !File.Exists(shell)) shell = OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/sh";
        const string marker = "__AGEX_PATH__";
        var output = RunHelper(shell, ["-ilc", $"printf '{marker}%s{marker}' \"$PATH\""], timeoutMs: 4000);
        if (output is null) return null;
        var start = output.IndexOf(marker, StringComparison.Ordinal);
        var end = start < 0 ? -1 : output.IndexOf(marker, start + marker.Length, StringComparison.Ordinal);
        return start >= 0 && end > start ? output[(start + marker.Length)..end] : null;
    }

    protected override IEnumerable<string> ExtraSearchDirectories()
    {
        yield return "/opt/homebrew/bin";
        yield return "/usr/local/bin";
        yield return "~/.local/bin";
        yield return "~/bin";
        yield return "~/.npm-global/bin";
        yield return "~/.bun/bin";
        yield return "~/.volta/bin";
        yield return "~/.cargo/bin";
        var nvm = Path.Combine(Home, ".nvm", "versions", "node");
        if (Directory.Exists(nvm))
        {
            foreach (var version in Directory.GetDirectories(nvm).OrderByDescending(item => item, StringComparer.Ordinal))
                yield return Path.Combine(version, "bin");
        }
    }

    protected override IEnumerable<string> CandidateFileNames(string name) { yield return name; }

    public override bool IsExecutable(string path)
    {
        if (!File.Exists(path)) return false;
        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    public override bool Notify(string title, string body) => NotifyCore(title, body);
    protected abstract bool NotifyCore(string title, string body);
}

[SupportedOSPlatform("macos")]
public sealed class MacPlatformService(AppPaths paths) : UnixPlatformServiceBase(paths)
{
    public override OsKind Os => OsKind.MacOS;
    public override string ShortcutModifier => "Cmd";
    public override string FileManagerName => "Finder";

    public override IEnumerable<string> KnownToolLocations(string toolId) => toolId switch
    {
        "ollama" => ["/Applications/Ollama.app/Contents/Resources/ollama", "/usr/local/bin/ollama", "/opt/homebrew/bin/ollama"],
        "claude-code" => ["~/.claude/local/claude", "~/.local/bin/claude"],
        "vscode" => ["/Applications/Visual Studio Code.app", "~/Applications/Visual Studio Code.app"],
        "cursor" => ["/Applications/Cursor.app", "~/Applications/Cursor.app"],
        "windsurf" => ["/Applications/Windsurf.app"],
        "antigravity-ide" => ["/Applications/Antigravity.app"],
        "zed" => ["/Applications/Zed.app"],
        "jetbrains" => ["/Applications/IntelliJ IDEA.app", "/Applications/PyCharm.app", "/Applications/Rider.app", "/Applications/WebStorm.app"],
        _ => [],
    };

    public override void OpenPath(string path) => StartDetached("/usr/bin/open", path);
    protected override void OpenUrlCore(string url) => StartDetached("/usr/bin/open", url);
    public override void RevealInFileManager(string path) => StartDetached("/usr/bin/open", "-R", path);
    public override bool OpenTerminal(string directory) => StartDetached("/usr/bin/open", "-a", "Terminal", directory);

    public override bool RunInTerminal(string fileName, IReadOnlyList<string> arguments, string directory)
    {
        // Terminal.app only accepts a command line; every part is shell-quoted,
        // then escaped for the AppleScript string literal.
        var command = "cd " + ShellQuote(directory) + " && " + string.Join(' ', new[] { fileName }.Concat(arguments).Select(ShellQuote));
        var literal = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return StartDetached("/usr/bin/osascript", "-e", "tell application \"Terminal\" to activate", "-e", $"tell application \"Terminal\" to do script \"{literal}\"");
    }

    // Title and body are passed as script arguments, never spliced into AppleScript source.
    protected override bool NotifyCore(string title, string body) => StartDetached("/usr/bin/osascript",
        "-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run", title, body);

    private static string LaunchAgentPath => Path.Combine(Home, "Library", "LaunchAgents", "com.agex.desktop.plist");
    public override bool IsStartWithSystemEnabled() => File.Exists(LaunchAgentPath);

    public override void SetStartWithSystem(bool enabled, string executablePath)
    {
        if (!enabled) { if (File.Exists(LaunchAgentPath)) File.Delete(LaunchAgentPath); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentPath)!);
        var escaped = System.Security.SecurityElement.Escape(executablePath);
        File.WriteAllText(LaunchAgentPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>com.agex.desktop</string>
              <key>ProgramArguments</key><array><string>{escaped}</string><string>--minimized</string></array>
              <key>RunAtLoad</key><true/>
            </dict>
            </plist>
            """);
    }

    protected override ISecureStore CreateSecureStore()
    {
        const string service = "AGEX";
        const string tool = "/usr/bin/security";
        // "security -i" reads commands from stdin, so the secret never appears
        // in the process list.
        static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return new CommandKeyringStore("macOS Keychain", Paths.Secrets,
            set: (key, value) => RunHelper(tool, ["-i"], $"add-generic-password -U -s {Quote(service)} -a {Quote(key)} -w {Quote(value)}\n") is not null,
            get: key => RunHelper(tool, ["find-generic-password", "-s", service, "-a", key, "-w"])?.TrimEnd('\n'),
            delete: key => RunHelper(tool, ["delete-generic-password", "-s", service, "-a", key]) is not null);
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxPlatformService(AppPaths paths) : UnixPlatformServiceBase(paths)
{
    public override OsKind Os => OsKind.Linux;
    public override string ShortcutModifier => "Ctrl";
    public override string FileManagerName => "file manager";

    protected override IEnumerable<string> ExtraSearchDirectories() => base.ExtraSearchDirectories().Append("/snap/bin");

    public override IEnumerable<string> KnownToolLocations(string toolId) => toolId switch
    {
        "vscode" => ["/usr/bin/code", "/usr/share/code/code", "/snap/bin/code"],
        "cursor" => ["/usr/bin/cursor", "/opt/Cursor/cursor"],
        "zed" => ["~/.local/bin/zed"],
        "ollama" => ["/usr/local/bin/ollama", "/usr/bin/ollama"],
        "claude-code" => ["~/.claude/local/claude", "~/.local/bin/claude"],
        _ => [],
    };

    private string? Opener => FindExecutable("xdg-open");
    public override void OpenPath(string path) { if (Opener is { } opener) StartDetached(opener, path); }
    protected override void OpenUrlCore(string url) { if (Opener is { } opener) StartDetached(opener, url); }
    public override void RevealInFileManager(string path) => OpenPath(Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path);

    public override bool OpenTerminal(string directory)
    {
        if (FindExecutable("x-terminal-emulator") is { } generic) return StartDetachedIn(generic, directory);
        if (FindExecutable("gnome-terminal") is { } gnome) return StartDetached(gnome, "--working-directory=" + directory);
        if (FindExecutable("konsole") is { } konsole) return StartDetached(konsole, "--workdir", directory);
        if (FindExecutable("xterm") is { } xterm) return StartDetachedIn(xterm, directory);
        return false;
    }

    public override bool RunInTerminal(string fileName, IReadOnlyList<string> arguments, string directory)
    {
        string[] command = [fileName, .. arguments];
        if (FindExecutable("gnome-terminal") is { } gnome) return StartDetachedIn(gnome, directory, ["--", .. command]);
        if (FindExecutable("konsole") is { } konsole) return StartDetachedIn(konsole, directory, ["-e", .. command]);
        if (FindExecutable("x-terminal-emulator") is { } generic) return StartDetachedIn(generic, directory, ["-e", .. command]);
        if (FindExecutable("xterm") is { } xterm) return StartDetachedIn(xterm, directory, ["-e", .. command]);
        return false;
    }

    private static bool StartDetachedIn(string fileName, string directory, IReadOnlyList<string>? arguments = null)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(fileName) { UseShellExecute = false, WorkingDirectory = directory };
            foreach (var argument in arguments ?? []) psi.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(psi);
            return process is not null;
        }
        catch (Exception) { return false; }
    }

    protected override bool NotifyCore(string title, string body) =>
        FindExecutable("notify-send") is { } tool && StartDetached(tool, "--app-name=AGEX", title, body);

    private static string AutostartPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(Home, ".config"), "autostart", "agex.desktop");

    public override bool IsStartWithSystemEnabled() => File.Exists(AutostartPath);

    public override void SetStartWithSystem(bool enabled, string executablePath)
    {
        if (!enabled) { if (File.Exists(AutostartPath)) File.Delete(AutostartPath); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AutostartPath)!);
        var exec = "\"" + executablePath.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\" --minimized";
        File.WriteAllText(AutostartPath, $"[Desktop Entry]\nType=Application\nName=AGEX\nExec={exec}\nX-GNOME-Autostart-enabled=true\n", new UTF8Encoding(false));
    }

    protected override ISecureStore CreateSecureStore()
    {
        var tool = FindExecutable("secret-tool");
        if (tool is null) return new PlainFileSecureStore(Paths.Secrets);
        return new CommandKeyringStore("Secret Service (secret-tool)", Paths.Secrets,
            set: (key, value) => RunHelper(tool, ["store", "--label=AGEX " + key, "service", "agex", "account", key], value) is not null,
            get: key => RunHelper(tool, ["lookup", "service", "agex", "account", key]),
            delete: key => RunHelper(tool, ["clear", "service", "agex", "account", key]) is not null);
    }
}
