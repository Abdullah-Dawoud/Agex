using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Connections;

/// <summary>How a reviewed MCP server is started once AGEX has installed it.</summary>
public sealed record ManagedLaunch(string Command, IReadOnlyList<string> Arguments);

/// <summary>An MCP package installed by AGEX: the pinned version and how to start it.</summary>
public sealed record ManagedInstall(string SkillId, string Kind, string Package, string Version, string Command, IReadOnlyList<string> Arguments, DateTimeOffset InstalledAt);

/// <summary>The pinned package behind an npx or uvx command line from the reviewed catalog.</summary>
public sealed record PackageRef(string Kind, string Name, string Version, string Executable, IReadOnlyList<string> RestArguments)
{
    public string Spec => Kind == "npm" ? $"{Name}@{Version}" : $"{Name}=={Version}";

    /// <summary>Reads "npx -y @scope/name@1.2.3 --flag" or "uvx [--from pkg==1.2.3 cmd | pkg==1.2.3] args". Null when it is not a pinned package.</summary>
    public static PackageRef? Parse(string command, IReadOnlyList<string> args)
    {
        var tool = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        if (tool == "npx")
        {
            var rest = args.SkipWhile(arg => arg is "-y" or "--yes").ToList();
            if (rest.Count == 0) return null;
            var spec = rest[0];
            var at = spec.LastIndexOf('@');
            if (at <= 0) return null; // unpinned or a bare scope
            var name = spec[..at];
            var version = spec[(at + 1)..];
            if (!IsVersion(version) || !IsNpmName(name)) return null;
            return new PackageRef("npm", name, version, "", rest.Skip(1).ToList());
        }
        if (tool == "uvx")
        {
            var list = args.ToList();
            string spec, executable;
            List<string> rest;
            if (list.Count >= 3 && list[0] == "--from") { spec = list[1]; executable = list[2]; rest = list.Skip(3).ToList(); }
            else if (list.Count >= 1) { spec = list[0]; executable = ""; rest = list.Skip(1).ToList(); }
            else return null;
            var parts = spec.Split("==");
            if (parts.Length != 2 || !IsVersion(parts[1]) || !IsPythonName(parts[0])) return null;
            return new PackageRef("pypi", parts[0], parts[1], executable.Length > 0 ? executable : parts[0], rest);
        }
        return null;
    }

    private static bool IsVersion(string value) => value.Length > 0 && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '+');
    private static bool IsNpmName(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"^(@[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*$");
    private static bool IsPythonName(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9._-]*$");
}

/// <summary>
/// Installs reviewed MCP packages into AGEX's own folder (one folder per skill
/// and version): npm packages with npm, PyPI packages into their own virtual
/// environment with uv. Each install happens in a staging folder that replaces
/// the old version only when it succeeded, so a failure leaves the previous
/// version (or nothing) behind. npm and uv verify package integrity themselves.
/// </summary>
public sealed class ManagedPackages(IPlatformService platform, ProcessRunner runner, AgexLog? log = null)
{
    public string Root => Path.Combine(platform.Paths.DataRoot, "tools");

    private string RecordPath(string skillId) => Path.Combine(Root, skillId, "installed.json");

    public ManagedInstall? Installed(string skillId)
    {
        try { return Json.ReadFile<ManagedInstall>(RecordPath(skillId)) is { } record && File.Exists(record.Command) ? record : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Where the package would go, for the confirmation dialog.</summary>
    public string Location(string skillId) => Path.Combine(Root, skillId);

    public async Task<ManagedInstall> InstallAsync(string skillId, PackageRef package, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        // Installed in place (Python launchers record their own folder), with the previous copy of the
        // same version kept aside until the new one works. Other versions stay until the new one is ready.
        var folder = Path.Combine(Root, skillId);
        var target = Path.Combine(folder, package.Version);
        var aside = Directory.Exists(target) ? target + ".old-" + Guid.NewGuid().ToString("N")[..6] : null;
        if (aside is not null) Directory.Move(target, aside);
        Directory.CreateDirectory(target);
        try
        {
            var launch = package.Kind == "npm"
                ? await InstallNpmAsync(package, target, progress, cancellationToken).ConfigureAwait(false)
                : await InstallPythonAsync(package, target, progress, cancellationToken).ConfigureAwait(false);
            var record = new ManagedInstall(skillId, package.Kind, package.Name, package.Version, launch.Command, launch.Arguments, DateTimeOffset.UtcNow);
            Json.WriteFile(RecordPath(skillId), record);
            foreach (var old in Directory.GetDirectories(folder).Where(path => !path.Equals(target, StringComparison.OrdinalIgnoreCase)))
                try { Directory.Delete(old, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            log?.Write("mcp_package_installed", new { skill = skillId, package = package.Spec });
            return record;
        }
        catch
        {
            // Roll back: remove the half-installed folder and bring back what was there.
            try { if (Directory.Exists(target)) Directory.Delete(target, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            try { if (aside is not null && Directory.Exists(aside)) Directory.Move(aside, target); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    public bool Remove(string skillId)
    {
        var folder = Path.Combine(Root, skillId);
        if (!Directory.Exists(folder)) return false;
        try { Directory.Delete(folder, recursive: true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log?.Error("mcp_package_remove_failed", ex); return false; }
    }

    private async Task<ManagedLaunch> InstallNpmAsync(PackageRef package, string staging, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var npm = platform.FindExecutable("npm") ?? throw new InstallFailedException("Node.js (npm) is needed first.");
        var node = platform.FindExecutable("node") ?? throw new InstallFailedException("Node.js is needed first.");
        progress?.Report($"npm install {package.Spec}");
        File.WriteAllText(Path.Combine(staging, "package.json"), "{\"private\":true}");
        // Install scripts of dependencies start "node" themselves: they get the same Node.js as npm.
        await RunAsync(npm, ["install", package.Spec, "--no-audit", "--no-fund", "--omit=dev", "--prefix", staging], staging, $"Install {package.Spec}", cancellationToken, Path.GetDirectoryName(node)).ConfigureAwait(false);
        var manifest = Path.Combine(staging, "node_modules", package.Name.Replace('/', Path.DirectorySeparatorChar), "package.json");
        if (!File.Exists(manifest)) throw new InstallFailedException($"npm finished but {package.Name} is not in the install folder.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, cancellationToken).ConfigureAwait(false));
        var entry = BinEntry(document.RootElement, package.Name) ?? throw new InstallFailedException($"{package.Name} declares no program to start.");
        var script = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest)!, entry));
        if (!File.Exists(script)) throw new InstallFailedException($"{package.Name}'s program file is missing ({entry}).");
        // Started as "node script": no shell or .cmd wrapper is involved.
        return new ManagedLaunch(node, [script, .. package.RestArguments]);
    }

    /// <summary>The "bin" entry of package.json: the only one, or the one named after the package.</summary>
    internal static string? BinEntry(JsonElement manifest, string packageName)
    {
        if (!manifest.TryGetProperty("bin", out var bin)) return null;
        if (bin.ValueKind == JsonValueKind.String) return bin.GetString();
        if (bin.ValueKind != JsonValueKind.Object) return null;
        var entries = bin.EnumerateObject().ToList();
        var unscoped = packageName.Contains('/') ? packageName[(packageName.IndexOf('/') + 1)..] : packageName;
        return (entries.FirstOrDefault(item => item.Name == unscoped).Value.ValueKind == JsonValueKind.String ? entries.First(item => item.Name == unscoped) : entries.FirstOrDefault()).Value.GetString();
    }

    private async Task<ManagedLaunch> InstallPythonAsync(PackageRef package, string staging, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var uv = platform.FindExecutable("uv") ?? throw new InstallFailedException("uv is needed first.");
        var venv = Path.Combine(staging, "venv");
        progress?.Report("Creating a private Python environment (uv venv)");
        await RunAsync(uv, ["venv", venv, "--quiet"], staging, "uv venv", cancellationToken).ConfigureAwait(false);
        var python = platform.Os == OsKind.Windows ? Path.Combine(venv, "Scripts", "python.exe") : Path.Combine(venv, "bin", "python");
        progress?.Report($"uv pip install {package.Spec}");
        // Copies instead of hard links: a file held open by a virus scanner cannot break the install.
        await RunAsync(uv, ["pip", "install", "--python", python, "--link-mode", "copy", package.Spec], staging, $"Install {package.Spec}", cancellationToken).ConfigureAwait(false);
        var executable = platform.Os == OsKind.Windows ? Path.Combine(venv, "Scripts", package.Executable + ".exe") : Path.Combine(venv, "bin", package.Executable);
        if (!File.Exists(executable)) throw new InstallFailedException($"{package.Name} installed but its program '{package.Executable}' was not found.");
        return new ManagedLaunch(executable, package.RestArguments);
    }

    private async Task RunAsync(string file, IReadOnlyList<string> arguments, string directory, string label, CancellationToken cancellationToken, string? firstOnPath = null)
    {
        var environment = platform.ChildEnvironment();
        if (firstOnPath is { Length: > 0 })
        {
            var key = OperatingSystem.IsWindows() ? "Path" : "PATH";
            environment[key] = firstOnPath + Path.PathSeparator + (environment.TryGetValue(key, out var path) ? path : "");
        }
        var result = await runner.RunAsync(new ProcessRequest
        {
            FileName = file, Arguments = arguments, WorkingDirectory = directory, Timeout = TimeSpan.FromMinutes(10), Label = label,
            Environment = environment,
        }, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) return;
        var text = (result.Stderr + "\n" + result.Stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !item.Contains("complete log of this run", StringComparison.OrdinalIgnoreCase)).ToList();
        // The most telling line: npm's own message (not its log location or "command failed"), else the last line.
        var line = text.LastOrDefault(item => (item.Contains("error", StringComparison.OrdinalIgnoreCase) || item.Contains("ERR!", StringComparison.Ordinal))
            && !item.EndsWith("command failed", StringComparison.OrdinalIgnoreCase) && !item.Contains("error code", StringComparison.OrdinalIgnoreCase)
            && !item.Contains("error path", StringComparison.OrdinalIgnoreCase) && !item.Contains("error command", StringComparison.OrdinalIgnoreCase))
            ?? text.LastOrDefault() ?? result.ErrorMessage;
        throw new InstallFailedException($"{label} failed: {Redactor.Redact(line.Length > 400 ? line[..400] : line)}");
    }
}
