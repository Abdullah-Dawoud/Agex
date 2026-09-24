namespace Agex.Core.Platform;

public enum OsKind { Windows, MacOS, Linux }

/// <summary>Per-platform folders. Every path AGEX writes lives below one of these.</summary>
public sealed record AppPaths(string DataRoot, string LogsRoot, string CacheRoot)
{
    public string Settings => Path.Combine(DataRoot, "settings.json");
    public string State => Path.Combine(DataRoot, "state.json");
    public string Projects => Path.Combine(DataRoot, "projects");
    public string Sessions => Path.Combine(DataRoot, "sessions");
    public string Skills => Path.Combine(DataRoot, "skills");
    public string Backups => Path.Combine(DataRoot, "backups");
    public string Secrets => Path.Combine(DataRoot, "secure");
    public string Scan => Path.Combine(CacheRoot, "scan.json");
    public string Updates => Path.Combine(CacheRoot, "updates");
    public string Temp => Path.Combine(CacheRoot, "tmp");
}

/// <summary>
/// Everything that differs between Windows, macOS and Linux. Core code never
/// checks the operating system directly; it asks this service.
/// </summary>
public interface IPlatformService
{
    OsKind Os { get; }
    /// <summary>"x64" or "arm64".</summary>
    string Architecture { get; }
    /// <summary>.NET runtime identifier for release assets, for example "osx-arm64".</summary>
    string RuntimeId { get; }
    string DisplayName { get; }
    AppPaths Paths { get; }
    /// <summary>Label of the primary shortcut modifier: "Ctrl" or "Cmd".</summary>
    string ShortcutModifier { get; }
    /// <summary>Name of the file manager, for labels such as "Show in Finder".</summary>
    string FileManagerName { get; }

    /// <summary>Directories searched for command-line tools, including the user's login shell PATH on macOS/Linux.</summary>
    IReadOnlyList<string> SearchPath { get; }
    /// <summary>Finds an executable by command name, honouring PATHEXT on Windows and the execute bit on Unix.</summary>
    string? FindExecutable(string name);
    bool IsExecutable(string path);
    /// <summary>Well-known extra install locations for a tool, before PATH is searched.</summary>
    IEnumerable<string> KnownToolLocations(string toolId);
    /// <summary>Environment for child processes (PATH repaired for GUI launches).</summary>
    IDictionary<string, string?> ChildEnvironment();

    void OpenPath(string path);
    void OpenUrl(Uri url);
    void RevealInFileManager(string path);
    bool OpenTerminal(string directory);
    /// <summary>
    /// Opens a visible terminal window that runs one program with fixed arguments
    /// (used for an agent's own sign-in command, so the agent owns the credentials).
    /// </summary>
    bool RunInTerminal(string fileName, IReadOnlyList<string> arguments, string directory);
    /// <summary>Shows an operating-system notification. Returns false when the platform has no supported mechanism.</summary>
    bool Notify(string title, string body);

    bool IsStartWithSystemEnabled();
    void SetStartWithSystem(bool enabled, string executablePath);

    ISecureStore SecureStore { get; }
}

/// <summary>Stores small secrets (API tokens) with the operating system's protection.</summary>
public interface ISecureStore
{
    /// <summary>Human-readable name of the mechanism, e.g. "Windows DPAPI" or "macOS Keychain".</summary>
    string Mechanism { get; }
    /// <summary>False when the fallback file store is in use (not encrypted by the OS).</summary>
    bool IsOsProtected { get; }
    bool Set(string key, string value);
    string? Get(string key);
    bool Delete(string key);
    IReadOnlyList<string> Keys();
}
