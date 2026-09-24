using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Projects;

/// <summary>
/// Git-aware safety: status, diffs, and snapshots that never touch the user's
/// working tree, index or branches. Snapshots are commits under
/// <c>refs/agex/snapshots/</c>, built with a temporary index file.
/// </summary>
public sealed class GitService(IPlatformService platform, ProcessRunner runner)
{
    public string? GitPath => platform.FindExecutable("git");

    public bool IsRepository(string project) =>
        GitPath is not null && (Directory.Exists(Path.Combine(project, ".git")) || File.Exists(Path.Combine(project, ".git")));

    private async Task<ProcessResult> GitAsync(string project, IEnumerable<string> arguments, IDictionary<string, string?>? extraEnvironment = null, CancellationToken cancellationToken = default)
    {
        var environment = platform.ChildEnvironment();
        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GIT_OPTIONAL_LOCKS"] = "0";
        if (extraEnvironment is not null) foreach (var (key, value) in extraEnvironment) environment[key] = value;
        return await runner.RunAsync(new ProcessRequest
        {
            FileName = GitPath ?? throw new InvalidOperationException("Git is not installed."),
            Arguments = ["-C", project, .. arguments],
            WorkingDirectory = project, Environment = environment, Timeout = TimeSpan.FromSeconds(60), Label = "git",
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> StatusAsync(string project, CancellationToken cancellationToken = default)
    {
        if (!IsRepository(project)) return [];
        var result = await GitAsync(project, ["status", "--porcelain=v1", "--untracked-files=all"], cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(500).ToList() : [];
    }

    public async Task<string> DiffAsync(string project, string relativePath, string? baseRef = null, CancellationToken cancellationToken = default)
    {
        if (!IsRepository(project) || !ProjectScanner.IsInside(project, relativePath)) return "";
        var args = new List<string> { "diff", "--no-color", "--no-ext-diff" };
        if (baseRef is not null) args.Add(baseRef);
        args.AddRange(["--", relativePath]);
        var result = await GitAsync(project, args, cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = result.Stdout;
        if (text.Length == 0 && baseRef is null)
        {
            // Untracked file: show it as an addition.
            var full = Path.Combine(project, relativePath);
            if (File.Exists(full) && new FileInfo(full).Length < 256 * 1024)
                text = "new file\n" + string.Join('\n', File.ReadLines(full).Take(400).Select(line => "+" + line));
        }
        return text.Length > 200_000 ? text[..200_000] + "\n... (diff shortened)" : text;
    }

    /// <summary>
    /// Records every file (tracked and untracked, respecting .gitignore) in a
    /// commit object under refs/agex/snapshots/&lt;name&gt;. The working tree,
    /// the index and HEAD are unchanged.
    /// </summary>
    public async Task<string?> SnapshotAsync(string project, string name, CancellationToken cancellationToken = default)
    {
        if (!IsRepository(project)) return null;
        var tempIndex = Path.Combine(platform.Paths.Temp.EnsureTempDirectory(), $"agex-index-{Guid.NewGuid():N}");
        try
        {
            var env = new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = tempIndex };
            var head = await GitAsync(project, ["rev-parse", "--verify", "-q", "HEAD"], cancellationToken: cancellationToken).ConfigureAwait(false);
            if (head.Succeeded) await GitAsync(project, ["read-tree", "HEAD"], env, cancellationToken).ConfigureAwait(false);
            var add = await GitAsync(project, ["add", "-A", "--", "."], env, cancellationToken).ConfigureAwait(false);
            if (!add.Succeeded) return null;
            var tree = await GitAsync(project, ["write-tree"], env, cancellationToken).ConfigureAwait(false);
            if (!tree.Succeeded) return null;
            var commitArgs = new List<string> { "-c", "user.name=AGEX", "-c", "user.email=agex@localhost", "commit-tree", tree.Stdout.Trim(), "-m", $"AGEX snapshot before {name}" };
            if (head.Succeeded) commitArgs.AddRange(["-p", head.Stdout.Trim()]);
            var commit = await GitAsync(project, commitArgs, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!commit.Succeeded) return null;
            var reference = "refs/agex/snapshots/" + name;
            var update = await GitAsync(project, ["update-ref", reference, commit.Stdout.Trim()], cancellationToken: cancellationToken).ConfigureAwait(false);
            return update.Succeeded ? reference : null;
        }
        finally
        {
            try { File.Delete(tempIndex); } catch (IOException) { }
        }
    }

    /// <summary>Files that differ between a snapshot and the working tree.</summary>
    public async Task<IReadOnlyList<string>> ChangedSinceAsync(string project, string snapshotRef, CancellationToken cancellationToken = default)
    {
        var tempIndex = Path.Combine(platform.Paths.Temp.EnsureTempDirectory(), $"agex-index-{Guid.NewGuid():N}");
        try
        {
            var env = new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = tempIndex };
            await GitAsync(project, ["read-tree", snapshotRef], env, cancellationToken).ConfigureAwait(false);
            await GitAsync(project, ["add", "-A", "--", "."], env, cancellationToken).ConfigureAwait(false);
            var diff = await GitAsync(project, ["diff", "--cached", "--name-status", snapshotRef], env, cancellationToken).ConfigureAwait(false);
            return diff.Succeeded ? diff.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList() : [];
        }
        finally
        {
            try { File.Delete(tempIndex); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Restores files changed since the snapshot to their snapshot content and
    /// removes files the snapshot did not have. Callers must confirm with the user first.
    /// </summary>
    public async Task<bool> RestoreAsync(string project, string snapshotRef, CancellationToken cancellationToken = default)
    {
        if (!snapshotRef.StartsWith("refs/agex/snapshots/", StringComparison.Ordinal)) return false;
        var changed = await ChangedSinceAsync(project, snapshotRef, cancellationToken).ConfigureAwait(false);
        var ok = true;
        foreach (var line in changed)
        {
            var parts = line.Split('\t');
            if (parts.Length < 2 || !ProjectScanner.IsInside(project, parts[^1])) continue;
            var path = parts[^1];
            if (parts[0].StartsWith('A'))
            {
                // Added after the snapshot: remove it.
                var full = Path.Combine(project, path);
                try { if (File.Exists(full)) File.Delete(full); } catch (IOException) { ok = false; }
            }
            else
            {
                var restore = await GitAsync(project, ["restore", "--source", snapshotRef, "--worktree", "--", path], cancellationToken: cancellationToken).ConfigureAwait(false);
                ok &= restore.Succeeded;
            }
        }
        return ok;
    }
}

internal static class TempDirectoryExtensions
{
    public static string EnsureTempDirectory(this string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
