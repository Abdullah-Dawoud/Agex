namespace Agex.Core.Projects;

public sealed record ProjectFile(string RelativePath, long Size, DateTime ModifiedUtc);

/// <summary>
/// Bounded, ignore-aware file listing. Never descends into dependency, build or
/// version-control folders, so large projects stay fast.
/// </summary>
public static class ProjectScanner
{
    public static readonly IReadOnlySet<string> DefaultIgnored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", "node_modules", "bin", "obj", "dist", "build", "out", "target", ".venv", "venv", "__pycache__",
        ".pytest_cache", ".mypy_cache", ".next", ".nuxt", ".gradle", ".idea", ".vs", ".cache", "coverage", "Pods", "DerivedData", ".terraform",
    };

    public static List<ProjectFile> List(string root, IEnumerable<string>? extraIgnored = null, int maxFiles = 5000, int maxDepth = 12)
    {
        var ignored = new HashSet<string>(DefaultIgnored, StringComparer.OrdinalIgnoreCase);
        if (extraIgnored is not null) foreach (var item in extraIgnored) if (!string.IsNullOrWhiteSpace(item)) ignored.Add(item.Trim().TrimEnd('/', '\\'));
        var result = new List<ProjectFile>();
        var fullRoot = Path.GetFullPath(root);
        var stack = new Stack<(string Directory, int Depth)>();
        stack.Push((fullRoot, 0));
        while (stack.Count > 0 && result.Count < maxFiles)
        {
            var (directory, depth) = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(directory).EnumerateFileSystemInfos(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if (result.Count >= maxFiles) break;
                // Symbolic links and junctions are listed but never followed.
                var isLink = entry.LinkTarget is not null;
                if (entry is DirectoryInfo dir)
                {
                    if (isLink || ignored.Contains(dir.Name) || depth >= maxDepth) continue;
                    var relativeDir = Path.GetRelativePath(fullRoot, dir.FullName);
                    if (ignored.Contains(relativeDir) || ignored.Contains(relativeDir.Replace('\\', '/'))) continue;
                    stack.Push((dir.FullName, depth + 1));
                }
                else if (entry is FileInfo file)
                {
                    try { result.Add(new ProjectFile(Path.GetRelativePath(fullRoot, file.FullName).Replace('\\', '/'), file.Length, file.LastWriteTimeUtc)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        return result;
    }

    /// <summary>Compares two listings: added, modified and deleted files.</summary>
    public static List<Sessions.FileChange> Diff(IReadOnlyList<ProjectFile> before, IReadOnlyList<ProjectFile> after)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var old = before.ToDictionary(file => file.RelativePath, comparer);
        var current = after.ToDictionary(file => file.RelativePath, comparer);
        var changes = new List<Sessions.FileChange>();
        foreach (var file in after)
        {
            if (!old.TryGetValue(file.RelativePath, out var previous)) changes.Add(new() { Path = file.RelativePath, Kind = "added" });
            else if (previous.Size != file.Size || previous.ModifiedUtc != file.ModifiedUtc) changes.Add(new() { Path = file.RelativePath, Kind = "modified" });
        }
        foreach (var file in before)
            if (!current.ContainsKey(file.RelativePath)) changes.Add(new() { Path = file.RelativePath, Kind = "deleted" });
        return changes.OrderBy(change => change.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True when <paramref name="candidate"/> resolves inside <paramref name="root"/> (no "..", no absolute escape).</summary>
    public static bool IsInside(string root, string candidate)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, candidate));
            return full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
