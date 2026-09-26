using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;
using Agex.Core.Updates;

namespace Agex.Core.Connections;

/// <summary>A download whose SHA-256 did not match the published checksum. Nothing from it was installed.</summary>
public sealed class ChecksumMismatchException(string file, string expected, string actual)
    : Exception($"{file} does not match its published checksum (expected {expected[..12]}..., got {actual[..12]}...). Nothing was installed.");

/// <summary>An installation step failed; everything it changed was put back.</summary>
public sealed class InstallFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Downloads a file (https or a local file) and checks its SHA-256 when one is known.</summary>
public sealed class ArtifactDownloader(HttpClient http)
{
    public async Task<string> DownloadAsync(Uri source, string target, string? expectedSha256, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (source.IsFile) File.Copy(source.LocalPath, target, overwrite: true);
        else
        {
            if (source.Scheme != Uri.UriSchemeHttps) throw new InstallFailedException("Only https downloads are allowed.");
            using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InstallFailedException($"The download failed (HTTP {(int)response.StatusCode}) from {source.Host}.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = File.Create(target);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        if (expectedSha256 is { Length: > 0 })
        {
            var actual = await UpdateService.HashFileAsync(target, cancellationToken).ConfigureAwait(false);
            if (!actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new ChecksumMismatchException(Path.GetFileName(target), expectedSha256, actual);
            }
        }
        return target;
    }

    public async Task<string> TextAsync(Uri source, CancellationToken cancellationToken) =>
        source.IsFile ? await File.ReadAllTextAsync(source.LocalPath, cancellationToken).ConfigureAwait(false) : await http.GetStringAsync(source, cancellationToken).ConfigureAwait(false);

    /// <summary>Extracts a .zip or .tar.gz into a folder, refusing entries that would land outside it.</summary>
    public static void Extract(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        string Target(string name)
        {
            var full = Path.GetFullPath(Path.Combine(destination, name.Replace('\\', '/').TrimStart('/')));
            if (!full.StartsWith(root, StringComparison.Ordinal)) throw new InstallFailedException($"The archive contains a path outside its folder ({name}).");
            return full;
        }
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                var path = Target(entry.FullName);
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
            return;
        }
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } item)
        {
            var path = Target(item.Name);
            if (item.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
            if (item.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue; // links are not followed
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            item.ExtractToFile(path, overwrite: true);
        }
    }
}

/// <summary>
/// Records what an installation changed so it can be undone: new files and
/// folders are removed, replaced ones are restored from a backup.
/// </summary>
public sealed class InstallJournal : IDisposable
{
    private readonly List<(string Target, string? Backup)> _changes = [];
    private readonly string _backupRoot;
    private bool _committed;

    public InstallJournal(string backupRoot)
    {
        _backupRoot = backupRoot;
        Directory.CreateDirectory(backupRoot);
    }

    /// <summary>Moves an existing file or folder aside before it is replaced.</summary>
    public void Prepare(string target)
    {
        string? backup = null;
        if (File.Exists(target) || Directory.Exists(target))
        {
            backup = Path.Combine(_backupRoot, Guid.NewGuid().ToString("N"));
            if (Directory.Exists(target)) Directory.Move(target, backup); else File.Move(target, backup);
        }
        _changes.Add((target, backup));
    }

    public void CopyDirectory(string source, string target)
    {
        Prepare(target);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    public void WriteFile(string target, string contents)
    {
        Prepare(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, contents, new System.Text.UTF8Encoding(false));
    }

    public void Commit() => _committed = true;

    /// <summary>Undoes every change, newest first.</summary>
    public void Rollback()
    {
        for (var index = _changes.Count - 1; index >= 0; index--)
        {
            var (target, backup) = _changes[index];
            try
            {
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                else if (File.Exists(target)) File.Delete(target);
                if (backup is null) continue;
                if (Directory.Exists(backup)) Directory.Move(backup, target); else if (File.Exists(backup)) File.Move(backup, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        _changes.Clear();
    }

    public void Dispose()
    {
        if (!_committed) Rollback();
        try { if (Directory.Exists(_backupRoot)) Directory.Delete(_backupRoot, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

public sealed record InstallStep(string Title, string Detail);

/// <summary>
/// Installs the runtimes MCP servers need (Node.js with npx, uv with uvx) from
/// their official downloads into AGEX's own folder for this user: no
/// administrator rights, no change to the system PATH, checksums verified.
/// Other tools (Git, Docker, Chrome, CLIs) are not installed automatically;
/// their official download page is opened instead.
/// </summary>
public sealed class DependencyInstaller(IPlatformService platform, ArtifactDownloader downloader, AgexLog? log = null)
{
    public static bool CanInstall(string toolId) => toolId is "node" or "uv";

    public string RuntimesRoot => Path.Combine(platform.Paths.DataRoot, "runtimes");

    /// <summary>What will be downloaded, from where, and where it goes (shown before anything runs).</summary>
    public string Describe(string toolId) => toolId switch
    {
        "node" => $"Node.js (current LTS) from nodejs.org, checked against nodejs.org's SHA256SUMS, into {Redactor.RedactPaths(Path.Combine(RuntimesRoot, "node"))}. For your user only; no administrator rights; your system PATH is not changed.",
        "uv" => $"uv from its official GitHub release (astral-sh/uv), checked against the published .sha256, into {Redactor.RedactPaths(Path.Combine(RuntimesRoot, "uv"))}. For your user only; no administrator rights.",
        _ => "",
    };

    public async Task<string> InstallAsync(string toolId, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var work = Path.Combine(platform.Paths.Temp, "runtime-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            var (url, sha, top) = toolId switch
            {
                "node" => await NodeSourceAsync(cancellationToken).ConfigureAwait(false),
                "uv" => await UvSourceAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new InstallFailedException($"AGEX does not install {toolId} itself."),
            };
            progress?.Report("Downloading " + url.Host + url.AbsolutePath);
            var archive = await downloader.DownloadAsync(url, Path.Combine(work, Path.GetFileName(url.AbsolutePath)), sha, cancellationToken).ConfigureAwait(false);
            progress?.Report("Checksum verified. Extracting...");
            var extracted = Path.Combine(work, "x");
            ArtifactDownloader.Extract(archive, extracted);
            var source = top.Length > 0 && Directory.Exists(Path.Combine(extracted, top)) ? Path.Combine(extracted, top) : extracted;
            var target = Path.Combine(RuntimesRoot, toolId);
            using (var journal = new InstallJournal(Path.Combine(work, "backup")))
            {
                journal.CopyDirectory(source, target);
                if (!OperatingSystem.IsWindows()) MarkExecutable(target);
                journal.Commit();
            }
            log?.Write("runtime_installed", new { tool = toolId, source = url.Host });
            return target;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task<(Uri, string, string)> NodeSourceAsync(CancellationToken cancellationToken)
    {
        using var index = JsonDocument.Parse(await downloader.TextAsync(new Uri("https://nodejs.org/dist/index.json"), cancellationToken).ConfigureAwait(false));
        var release = index.RootElement.EnumerateArray().First(item => item.TryGetProperty("lts", out var lts) && lts.ValueKind == JsonValueKind.String);
        var version = release.GetProperty("version").GetString()!;
        var (osName, extension) = platform.Os switch { OsKind.Windows => ("win", "zip"), OsKind.MacOS => ("darwin", "tar.gz"), _ => ("linux", "tar.gz") };
        var name = $"node-{version}-{osName}-{platform.Architecture}";
        var file = $"{name}.{extension}";
        var sums = await downloader.TextAsync(new Uri($"https://nodejs.org/dist/{version}/SHASUMS256.txt"), cancellationToken).ConfigureAwait(false);
        var sha = UpdateService.ExpectedHash(sums, file) ?? throw new InstallFailedException($"{file} is not listed in nodejs.org's checksums.");
        return (new Uri($"https://nodejs.org/dist/{version}/{file}"), sha, name);
    }

    private async Task<(Uri, string, string)> UvSourceAsync(CancellationToken cancellationToken)
    {
        var arch = platform.Architecture == "arm64" ? "aarch64" : "x86_64";
        var target = platform.Os switch
        {
            OsKind.Windows => $"{arch}-pc-windows-msvc",
            OsKind.MacOS => $"{arch}-apple-darwin",
            _ => $"{arch}-unknown-linux-gnu",
        };
        var file = $"uv-{target}.{(platform.Os == OsKind.Windows ? "zip" : "tar.gz")}";
        var baseUrl = "https://github.com/astral-sh/uv/releases/latest/download/";
        var sums = await downloader.TextAsync(new Uri(baseUrl + file + ".sha256"), cancellationToken).ConfigureAwait(false);
        var sha = sums.Split(' ', '\t', '\n')[0].Trim();
        if (sha.Length != 64) throw new InstallFailedException("uv's published checksum could not be read.");
        return (new Uri(baseUrl + file), sha, $"uv-{target}");
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void MarkExecutable(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(file => file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || Path.GetFileName(file) is "uv" or "uvx"))
            try { File.SetUnixFileMode(file, File.GetUnixFileMode(file) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
    }
}
