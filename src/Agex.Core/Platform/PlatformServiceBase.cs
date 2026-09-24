using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Agex.Core.Platform;

/// <summary>Shared logic for the three platform services.</summary>
public abstract class PlatformServiceBase : IPlatformService
{
    private IReadOnlyList<string>? _searchPath;
    private readonly object _pathLock = new();
    private ISecureStore? _secureStore;

    protected PlatformServiceBase(AppPaths paths) { Paths = paths; }

    public abstract OsKind Os { get; }
    public string Architecture => RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        var other => other.ToString().ToLowerInvariant(),
    };
    public string RuntimeId => (Os switch { OsKind.Windows => "win", OsKind.MacOS => "osx", _ => "linux" }) + "-" + Architecture;
    public virtual string DisplayName => $"{RuntimeInformation.OSDescription} ({Architecture})";
    public AppPaths Paths { get; }
    public abstract string ShortcutModifier { get; }
    public abstract string FileManagerName { get; }

    public IReadOnlyList<string> SearchPath
    {
        get
        {
            lock (_pathLock) { return _searchPath ??= BuildSearchPath(); }
        }
    }

    /// <summary>Forgets the cached PATH, for example after the user installs a tool.</summary>
    public void RefreshSearchPath() { lock (_pathLock) { _searchPath = null; } }

    protected virtual IReadOnlyList<string> BuildSearchPath()
    {
        var list = new List<string>();
        AddPathEntries(list, Environment.GetEnvironmentVariable("PATH"));
        foreach (var extra in ExtraSearchDirectories()) AddPathEntry(list, extra);
        return list;
    }

    protected abstract IEnumerable<string> ExtraSearchDirectories();

    protected static void AddPathEntries(List<string> list, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var part in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddPathEntry(list, part);
    }

    protected static void AddPathEntry(List<string> list, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        var expanded = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
        if (expanded.StartsWith("~/", StringComparison.Ordinal))
            expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), expanded[2..]);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!list.Any(existing => string.Equals(existing.TrimEnd('/', '\\'), expanded.TrimEnd('/', '\\'), comparison)))
            list.Add(expanded);
    }

    public virtual string? FindExecutable(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.IndexOfAny(['/', '\\']) >= 0) return IsExecutable(name) ? Path.GetFullPath(name) : null;
        foreach (var directory in SearchPath)
        {
            foreach (var candidate in CandidateFileNames(name))
            {
                string full;
                try { full = Path.Combine(directory, candidate); } catch (ArgumentException) { continue; }
                if (IsExecutable(full)) return full;
            }
        }
        return null;
    }

    protected abstract IEnumerable<string> CandidateFileNames(string name);
    public abstract bool IsExecutable(string path);
    public abstract IEnumerable<string> KnownToolLocations(string toolId);

    public IDictionary<string, string?> ChildEnvironment()
    {
        var env = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            env[(string)entry.Key] = entry.Value as string;
        env[OperatingSystem.IsWindows() ? "Path" : "PATH"] = string.Join(Path.PathSeparator, SearchPath);
        return env;
    }

    public abstract void OpenPath(string path);
    public abstract void RevealInFileManager(string path);
    public abstract bool OpenTerminal(string directory);
    public abstract bool RunInTerminal(string fileName, IReadOnlyList<string> arguments, string directory);

    /// <summary>Quotes one argument for a POSIX shell command line.</summary>
    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
    public abstract bool Notify(string title, string body);
    public abstract bool IsStartWithSystemEnabled();
    public abstract void SetStartWithSystem(bool enabled, string executablePath);
    protected abstract ISecureStore CreateSecureStore();
    public ISecureStore SecureStore => _secureStore ??= CreateSecureStore();

    public void OpenUrl(Uri url)
    {
        // Only web links. Agent output can contain arbitrary URIs (file:, custom
        // protocol handlers); those are never opened.
        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            throw new InvalidOperationException("Only http and https links can be opened.");
        OpenUrlCore(url.AbsoluteUri);
    }

    protected abstract void OpenUrlCore(string url);

    /// <summary>Starts a helper program with exact arguments and no shell.</summary>
    protected static bool StartDetached(string fileName, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            return process is not null;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Runs a short helper program and returns its stdout, or null on failure or timeout.</summary>
    internal static string? RunHelper(string fileName, IEnumerable<string> arguments, string? stdin = null, int timeoutMs = 5000)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var argument in arguments) psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (stdin is not null) process.StandardInput.Write(stdin);
            process.StandardInput.Close();
            if (!process.WaitForExit(timeoutMs)) { try { process.Kill(true); } catch { } return null; }
            return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
        }
        catch (Exception) { return null; }
    }
}
