using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Agex.Core.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformService : PlatformServiceBase
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "AGEX";

    public WindowsPlatformService(AppPaths paths) : base(paths) { }

    public override OsKind Os => OsKind.Windows;
    public override string ShortcutModifier => "Ctrl";
    public override string FileManagerName => "File Explorer";

    protected override IReadOnlyList<string> BuildSearchPath()
    {
        var list = new List<string>();
        AddPathEntries(list, Environment.GetEnvironmentVariable("PATH"));
        // A desktop app keeps the PATH it was started with. Read the saved user
        // and machine PATH so tools installed afterwards are found.
        AddPathEntries(list, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));
        AddPathEntries(list, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine));
        foreach (var extra in ExtraSearchDirectories()) AddPathEntry(list, extra);
        return list;
    }

    protected override IEnumerable<string> ExtraSearchDirectories()
    {
        yield return "%APPDATA%\\npm";
        yield return "%USERPROFILE%\\.local\\bin";
        yield return "%NVM_SYMLINK%";
        yield return "%LOCALAPPDATA%\\agy\\bin";
        yield return "%LOCALAPPDATA%\\Programs\\Ollama";
    }

    private static readonly string[] PreferredExtensions = [".exe", ".cmd", ".bat", ".com"];

    protected override IEnumerable<string> CandidateFileNames(string name)
    {
        if (Path.HasExtension(name)) { yield return name; yield break; }
        foreach (var extension in PreferredExtensions) yield return name + extension;
    }

    public override bool IsExecutable(string path)
    {
        if (!File.Exists(path)) return false;
        var extension = Path.GetExtension(path);
        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        return pathExt.Any(item => string.Equals(item, extension, StringComparison.OrdinalIgnoreCase));
    }

    public override IEnumerable<string> KnownToolLocations(string toolId) => toolId switch
    {
        "codex" => ["%LOCALAPPDATA%\\OpenAI\\Codex\\bin\\*\\codex.exe", "%LOCALAPPDATA%\\OpenAI\\Codex\\bin\\codex.exe", "%APPDATA%\\npm\\codex.cmd"],
        "antigravity" => ["%LOCALAPPDATA%\\agy\\bin\\agy.exe"],
        "claude-code" => ["%USERPROFILE%\\.local\\bin\\claude.exe", "%APPDATA%\\npm\\claude.cmd"],
        "gemini-cli" => ["%APPDATA%\\npm\\gemini.cmd"],
        "copilot-cli" => ["%APPDATA%\\npm\\copilot.cmd"],
        "opencode" => ["%APPDATA%\\npm\\opencode.cmd", "%USERPROFILE%\\.opencode\\bin\\opencode.exe"],
        "qwen-code" => ["%APPDATA%\\npm\\qwen.cmd"],
        "aider" => ["%USERPROFILE%\\.local\\bin\\aider.exe"],
        "cursor-agent" => ["%LOCALAPPDATA%\\cursor-agent\\cursor-agent.cmd"],
        "ollama" => ["%LOCALAPPDATA%\\Programs\\Ollama\\ollama.exe"],
        "vscode" => ["%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe", "%ProgramFiles%\\Microsoft VS Code\\Code.exe"],
        "cursor" => ["%LOCALAPPDATA%\\Programs\\cursor\\Cursor.exe"],
        "windsurf" => ["%LOCALAPPDATA%\\Programs\\Windsurf\\Windsurf.exe"],
        "antigravity-ide" => ["%LOCALAPPDATA%\\Programs\\Antigravity\\Antigravity.exe", "%LOCALAPPDATA%\\Programs\\Antigravity IDE\\Antigravity.exe"],
        "visual-studio" => ["%ProgramFiles%\\Microsoft Visual Studio\\*\\*\\Common7\\IDE\\devenv.exe"],
        "jetbrains" => ["%ProgramFiles%\\JetBrains\\*\\bin\\idea64.exe", "%ProgramFiles%\\JetBrains\\*\\bin\\pycharm64.exe", "%ProgramFiles%\\JetBrains\\*\\bin\\rider64.exe", "%LOCALAPPDATA%\\Programs\\*\\bin\\idea64.exe"],
        "zed" => ["%LOCALAPPDATA%\\Programs\\Zed\\Zed.exe"],
        _ => [],
    };

    public override void OpenPath(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    protected override void OpenUrlCore(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    public override void RevealInFileManager(string path)
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (File.Exists(path)) StartDetached(explorer, "/select," + path);
        else StartDetached(explorer, path);
    }

    public override bool OpenTerminal(string directory)
    {
        var terminal = FindExecutable("wt");
        if (terminal is not null) return StartDetached(terminal, "-d", directory);
        try
        {
            var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            Process.Start(new ProcessStartInfo(shell) { WorkingDirectory = directory, UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception) { return false; }
    }

    // Native Windows toasts need an app identity registered with the shell;
    // the desktop app shows in-app notifications and flashes the taskbar instead.
    public override bool Notify(string title, string body) => false;

    public override bool IsStartWithSystemEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    public override void SetStartWithSystem(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(RunValue, $"\"{executablePath}\" --minimized");
        else if (key.GetValue(RunValue) is not null) key.DeleteValue(RunValue);
    }

    protected override ISecureStore CreateSecureStore() => new DpapiSecureStore(Paths.Secrets);
}

/// <summary>Secrets encrypted with Windows DPAPI for the current user.</summary>
[SupportedOSPlatform("windows")]
internal sealed class DpapiSecureStore(string root) : FileBackedSecureStore(root)
{
    private static readonly byte[] Entropy = "AGEX secure store v1"u8.ToArray();
    public override string Mechanism => "Windows DPAPI (current user)";
    public override bool IsOsProtected => true;
    protected override byte[] Protect(byte[] plain) => Dpapi.Protect(plain, Entropy);
    protected override byte[]? Unprotect(byte[] cipher) => Dpapi.Unprotect(cipher, Entropy);
}

[SupportedOSPlatform("windows")]
internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    private const int UiForbidden = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Protect(byte[] plain, byte[] entropy) =>
        Transform(plain, entropy, protect: true) ?? throw new InvalidOperationException("DPAPI could not protect the value.");

    public static byte[]? Unprotect(byte[] cipher, byte[] entropy) => Transform(cipher, entropy, protect: false);

    private static byte[]? Transform(byte[] data, byte[] entropy, bool protect)
    {
        var input = Pin(data, out var inputHandle);
        var extra = Pin(entropy, out var entropyHandle);
        try
        {
            var ok = protect
                ? CryptProtectData(ref input, "AGEX", ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!ok) return null;
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { inputHandle.Free(); entropyHandle.Free(); }
    }

    private static DataBlob Pin(byte[] data, out GCHandle handle)
    {
        handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        return new DataBlob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
    }
}
