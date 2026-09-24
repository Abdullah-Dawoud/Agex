namespace Agex.Core.Platform;

public static class PlatformFactory
{
    /// <summary>
    /// Creates the service for the running operating system. <c>AGEX_HOME</c>
    /// (or <paramref name="homeOverride"/>) puts all data, logs and cache in one
    /// isolated folder, which tests and portable installs use.
    /// </summary>
    public static IPlatformService Create(string? homeOverride = null)
    {
        var paths = ResolvePaths(homeOverride ?? Environment.GetEnvironmentVariable("AGEX_HOME"));
        if (OperatingSystem.IsWindows()) return new WindowsPlatformService(paths);
        if (OperatingSystem.IsMacOS()) return new MacPlatformService(paths);
        if (OperatingSystem.IsLinux()) return new LinuxPlatformService(paths);
        throw new PlatformNotSupportedException("AGEX supports Windows, macOS and Linux.");
    }

    public static AppPaths ResolvePaths(string? home)
    {
        if (!string.IsNullOrWhiteSpace(home))
        {
            var root = Path.GetFullPath(home);
            return new AppPaths(root, Path.Combine(root, "logs"), Path.Combine(root, "cache"));
        }
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.Combine(local, "AGEX");
            return new AppPaths(root, Path.Combine(root, "logs"), Path.Combine(root, "cache"));
        }
        if (OperatingSystem.IsMacOS())
        {
            var library = Path.Combine(user, "Library");
            return new AppPaths(Path.Combine(library, "Application Support", "AGEX"), Path.Combine(library, "Logs", "AGEX"), Path.Combine(library, "Caches", "AGEX"));
        }
        static string Xdg(string variable, string fallback) =>
            Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value && Path.IsPathRooted(value) ? value : fallback;
        return new AppPaths(
            Path.Combine(Xdg("XDG_DATA_HOME", Path.Combine(user, ".local", "share")), "agex"),
            Path.Combine(Xdg("XDG_STATE_HOME", Path.Combine(user, ".local", "state")), "agex", "logs"),
            Path.Combine(Xdg("XDG_CACHE_HOME", Path.Combine(user, ".cache")), "agex"));
    }
}
