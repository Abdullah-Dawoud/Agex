using System.Diagnostics;
using System.Text.Json;
using Agex.Core.Platform;

namespace Agex.Core.Agents;

/// <summary>
/// Finds installed tools without running them: explicit override, known install
/// locations, then PATH. Versions come from file metadata or an adjacent npm
/// package.json.
/// </summary>
public static class ToolLocator
{
    public static string? Locate(IPlatformService platform, string toolId, IEnumerable<string> commandNames)
    {
        var overrideVariable = "AGEX_" + toolId.ToUpperInvariant().Replace('-', '_') + "_PATH";
        if (Environment.GetEnvironmentVariable(overrideVariable) is { Length: > 0 } explicitPath && File.Exists(explicitPath))
            return Path.GetFullPath(explicitPath);
        // The command the user runs in a terminal wins; known install folders are
        // the fallback (for example when a GUI launch has a shorter PATH).
        foreach (var name in commandNames)
        {
            if (platform.FindExecutable(name) is { } found) return found;
        }
        foreach (var pattern in platform.KnownToolLocations(toolId))
        {
            foreach (var match in Expand(pattern))
            {
                if (match.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? Directory.Exists(match) : File.Exists(match))
                    return match;
            }
        }
        return null;
    }

    /// <summary>Expands environment variables, "~" and "*" wildcards (one directory level per "*").</summary>
    public static IEnumerable<string> Expand(string pattern)
    {
        var expanded = Environment.ExpandEnvironmentVariables(pattern);
        if (expanded.Contains('%')) yield break; // an unset variable
        if (expanded.StartsWith("~/", StringComparison.Ordinal))
            expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), expanded[2..]);
        if (!expanded.Contains('*')) { yield return expanded; yield break; }
        var root = Path.GetPathRoot(expanded) ?? "";
        var parts = expanded[root.Length..].Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> current = [root];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            var last = index == parts.Length - 1;
            current = current.SelectMany(directory =>
            {
                if (!part.Contains('*')) return [Path.Combine(directory, part)];
                try
                {
                    if (!Directory.Exists(directory)) return [];
                    var entries = last ? Directory.GetFileSystemEntries(directory, part) : Directory.GetDirectories(directory, part);
                    return entries.OrderByDescending(entry => SafeWriteTime(entry)).AsEnumerable();
                }
                catch (Exception) { return []; }
            }).ToList();
        }
        foreach (var item in current) yield return item;
    }

    private static DateTime SafeWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); } catch (Exception) { return DateTime.MinValue; }
    }

    /// <summary>Version from metadata only; never executes the file.</summary>
    public static string ReadVersion(string path, string? npmPackage = null)
    {
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                var product = info.ProductVersion?.Split(' ', '+')[0];
                if (!string.IsNullOrWhiteSpace(product) && product != "0.0.0.0") return product;
            }
            if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                var plist = Path.Combine(path, "Contents", "Info.plist");
                if (File.Exists(plist))
                {
                    var text = File.ReadAllText(plist);
                    var marker = text.IndexOf("<key>CFBundleShortVersionString</key>", StringComparison.Ordinal);
                    if (marker >= 0)
                    {
                        var start = text.IndexOf("<string>", marker, StringComparison.Ordinal) + 8;
                        var end = text.IndexOf("</string>", start, StringComparison.Ordinal);
                        if (start > 7 && end > start) return text[start..end];
                    }
                }
            }
            var directory = Path.GetDirectoryName(ResolveLink(path)) ?? "";
            var candidates = new List<string>();
            if (npmPackage is not null)
            {
                candidates.Add(Path.Combine(directory, "node_modules", npmPackage, "package.json"));
                candidates.Add(Path.Combine(directory, "..", "lib", "node_modules", npmPackage, "package.json"));
                candidates.Add(Path.Combine(directory, "..", "package.json"));
            }
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(candidate));
                if (document.RootElement.TryGetProperty("name", out var name) && npmPackage is not null && name.GetString() != npmPackage) continue;
                if (document.RootElement.TryGetProperty("version", out var version)) return version.GetString() ?? "";
            }
        }
        catch (Exception) { }
        return "";
    }

    private static string ResolveLink(string path)
    {
        try { return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path; }
        catch (Exception) { return path; }
    }
}
