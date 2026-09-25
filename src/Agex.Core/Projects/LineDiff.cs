using System.Security.Cryptography;
using System.Text;
using Agex.Core.Sessions;

namespace Agex.Core.Projects;

public enum DiffLineKind { Same, Added, Removed }

public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldNumber, int? NewNumber);

/// <summary>Line diff (Myers' O(ND) algorithm) for change counts and the side-by-side view.</summary>
public static class LineDiff
{
    /// <summary>Edit scripts longer than this are not computed; the caller shows "too many changes".</summary>
    public const int MaxEdits = 4000;

    public static string[] SplitLines(string text)
    {
        if (text.Length == 0) return [];
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    /// <summary>The full line-by-line comparison, or null when the files differ too much to compare cheaply.</summary>
    public static IReadOnlyList<DiffLine>? Compute(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        // Trim the common start and end first: most edits are small.
        var start = 0;
        while (start < a.Count && start < b.Count && a[start] == b[start]) start++;
        var endA = a.Count; var endB = b.Count;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1]) { endA--; endB--; }
        var middle = Myers(a, b, start, endA, start, endB);
        if (middle is null) return null;
        var result = new List<DiffLine>(a.Count + b.Count);
        for (var index = 0; index < start; index++) result.Add(new DiffLine(DiffLineKind.Same, a[index], index + 1, index + 1));
        result.AddRange(middle);
        for (int i = endA, j = endB; i < a.Count; i++, j++) result.Add(new DiffLine(DiffLineKind.Same, a[i], i + 1, j + 1));
        return result;
    }

    /// <summary>Lines added and removed; (b.Count, a.Count) when the files are too different to compare.</summary>
    public static (int Added, int Removed) Count(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var diff = Compute(a, b);
        if (diff is null) return (b.Count, a.Count);
        return (diff.Count(line => line.Kind == DiffLineKind.Added), diff.Count(line => line.Kind == DiffLineKind.Removed));
    }

    private static List<DiffLine>? Myers(IReadOnlyList<string> a, IReadOnlyList<string> b, int a0, int a1, int b0, int b1)
    {
        int n = a1 - a0, m = b1 - b0, max = n + m;
        var result = new List<DiffLine>();
        if (n == 0) { for (var j = b0; j < b1; j++) result.Add(new DiffLine(DiffLineKind.Added, b[j], null, j + 1)); return result; }
        if (m == 0) { for (var i = a0; i < a1; i++) result.Add(new DiffLine(DiffLineKind.Removed, a[i], i + 1, null)); return result; }
        var limit = Math.Min(max, MaxEdits);
        var offset = limit + 1;
        var v = new int[2 * limit + 3];
        var trace = new List<int[]>();
        var found = false;
        for (var d = 0; d <= limit && !found; d++)
        {
            trace.Add((int[])v.Clone());
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[a0 + x] == b[b0 + y]) { x++; y++; }
                v[offset + k] = x;
                if (x >= n && y >= m) { found = true; break; }
            }
        }
        if (!found) return null;
        // Walk the trace backwards to recover the edit script.
        var edits = new List<DiffLine>();
        int cx = n, cy = m;
        for (var d = trace.Count - 1; d >= 0; d--)
        {
            var vd = trace[d];
            var k = cx - cy;
            var prevK = k == -d || (k != d && vd[offset + k - 1] < vd[offset + k + 1]) ? k + 1 : k - 1;
            var prevX = d == 0 ? 0 : vd[offset + prevK];
            var prevY = prevX - prevK;
            while (cx > prevX && cy > prevY) { edits.Add(new DiffLine(DiffLineKind.Same, a[a0 + cx - 1], a0 + cx, b0 + cy)); cx--; cy--; }
            if (d == 0) break;
            if (cx == prevX) edits.Add(new DiffLine(DiffLineKind.Added, b[b0 + cy - 1], null, b0 + cy));
            else edits.Add(new DiffLine(DiffLineKind.Removed, a[a0 + cx - 1], a0 + cx, null));
            cx = prevX; cy = prevY;
        }
        while (cx > 0 && cy > 0) { edits.Add(new DiffLine(DiffLineKind.Same, a[a0 + cx - 1], a0 + cx, b0 + cy)); cx--; cy--; }
        edits.Reverse();
        return edits;
    }
}

/// <summary>
/// Text of the project's files when a request starts, kept in memory (never
/// written to disk) so AGEX can count added and removed lines and show
/// side-by-side diffs afterwards, with or without Git. Bounded: text files up
/// to 512 KB, 40 MB in total.
/// </summary>
public sealed class TextBaseline
{
    public const long MaxFileBytes = 512 * 1024;
    public const long MaxTotalBytes = 40L * 1024 * 1024;

    private readonly Dictionary<string, string> _text;
    private readonly Dictionary<string, string> _hashes;

    private TextBaseline(string root, Dictionary<string, string> text, Dictionary<string, string> hashes) { Root = root; _text = text; _hashes = hashes; }

    public string Root { get; }

    public static TextBaseline Capture(string root, IEnumerable<ProjectFile> files)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var text = new Dictionary<string, string>(comparer);
        var hashes = new Dictionary<string, string>(comparer);
        long total = 0;
        foreach (var file in files)
        {
            if (file.Size > MaxFileBytes || total + file.Size > MaxTotalBytes) continue;
            if (ReadText(Path.Combine(root, file.RelativePath)) is not { } content) continue;
            text[file.RelativePath] = content;
            hashes[file.RelativePath] = Hash(content);
            total += file.Size;
        }
        return new TextBaseline(root, text, hashes);
    }

    public string? Before(string relativePath) => _text.GetValueOrDefault(relativePath);

    /// <summary>Reads a file as text, or null for binary files (a NUL byte in the first 8 KB) and unreadable files.</summary>
    public static string? ReadText(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8192)).IndexOf((byte)0) >= 0) return null;
            return new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Adds line counts to the changes, and turns a delete plus an add of the same content into a rename.</summary>
    public List<FileChange> Describe(List<FileChange> changes)
    {
        var deleted = changes.Where(change => change.Kind == "deleted").ToList();
        foreach (var change in changes.Where(change => change.Kind == "added").ToList())
        {
            var now = ReadText(Path.Combine(Root, change.Path));
            if (now is null) continue;
            var hash = Hash(now);
            var from = deleted.FirstOrDefault(item => _hashes.GetValueOrDefault(item.Path) == hash);
            if (from is null) continue;
            change.Kind = "renamed";
            change.OldPath = from.Path;
            deleted.Remove(from);
            changes.Remove(from);
        }
        foreach (var change in changes)
        {
            var before = change.Kind is "added" ? "" : change.Kind == "renamed" ? Before(change.OldPath) : Before(change.Path);
            var after = change.Kind == "deleted" ? "" : ReadText(Path.Combine(Root, change.Path));
            if (before is null || after is null) continue; // binary or too large when the request started
            (change.Added, change.Removed) = LineDiff.Count(LineDiff.SplitLines(before), LineDiff.SplitLines(after));
        }
        return changes;
    }
}
