using System.IO.Compression;

namespace Agex.Core.Skills;

/// <summary>
/// Extracts untrusted zip files. Rejects absolute paths, drive letters, ".."
/// segments, symbolic links, device names, too many entries and archives that
/// expand beyond the size limit (zip bombs). Nothing is written outside the
/// target folder.
/// </summary>
public static class SafeArchive
{
    public const int MaxEntries = 400;
    public const long MaxTotalBytes = 20 * 1024 * 1024;
    public const long MaxEntryBytes = 5 * 1024 * 1024;
    private const int UnixSymlinkType = 0xA000;

    public static void Extract(string zipPath, string targetDirectory)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException($"The package has more than {MaxEntries} files.");
        var root = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(root);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var relative = SafeRelativePath(entry.FullName) ?? throw new InvalidDataException($"The package contains an unsafe path: {entry.FullName}");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == UnixSymlinkType) throw new InvalidDataException($"The package contains a symbolic link: {entry.FullName}");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            if (entry.Length > MaxEntryBytes) throw new InvalidDataException($"{entry.FullName} is larger than {MaxEntryBytes / 1024 / 1024} MB.");
            total += entry.Length;
            if (total > MaxTotalBytes) throw new InvalidDataException($"The package expands beyond {MaxTotalBytes / 1024 / 1024} MB.");
            var destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException($"The package tries to write outside its folder: {entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // Copy with a hard limit: the size in the header can lie.
            using var input = entry.Open();
            using var output = File.Create(destination);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;
                if (written > MaxEntryBytes) throw new InvalidDataException($"{entry.FullName} expands beyond its declared size.");
                output.Write(buffer, 0, read);
            }
        }
    }

    private static readonly string[] ReservedNames = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3"];

    /// <summary>Returns a normalized relative path, or null when the path is unsafe.</summary>
    public static string? SafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 260) return null;
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Contains('\0')) return null;
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        foreach (var part in parts)
        {
            if (part is "." or ".." || part.Trim() != part || part.EndsWith('.')) return null;
            if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(part).ToUpperInvariant())) return null;
            if (part.IndexOfAny(Path.GetInvalidFileNameChars().Concat(['<', '>', '|', '"', '*', '?']).ToArray()) >= 0) return null;
        }
        return string.Join(Path.DirectorySeparatorChar, parts);
    }
}
