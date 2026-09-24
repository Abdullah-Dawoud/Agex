using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Agents;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Skills;

public sealed class SkillException(string message) : Exception(message);

public sealed record InstallProgress(string Step, int Done, int Total);

public sealed record CompatibilityReport(bool Installable, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings);

/// <summary>
/// Installs, verifies, updates and removes skills. AGEX itself never executes
/// skill content: instruction skills are offered to agents as SKILL.md files,
/// and MCP skills are handed to agents that support MCP. Every download is
/// checked against a pinned SHA-256 before it is installed.
/// </summary>
public sealed partial class SkillManager
{
    public const string StartupFailurePrefix = "This skill was disabled because it failed during startup";
    private const long MaxTotalBytes = 20 * 1024 * 1024;
    private const int MaxFiles = 400;
    private static readonly HttpClient Http = CreateHttp();
    private readonly IPlatformService _platform;
    private readonly AgexLog? _log;
    private readonly object _lock = new();

    public SkillManager(IPlatformService platform, AgexLog? log = null, bool safeMode = false)
    {
        _platform = platform;
        _log = log;
        SafeMode = safeMode;
    }

    /// <summary>In safe mode no installed skill is offered to agents.</summary>
    public bool SafeMode { get; }
    private string Root => _platform.Paths.Skills;
    private string RegistryPath => Path.Combine(Root, "installed.json");
    private string CachedCatalogPath => Path.Combine(_platform.Paths.CacheRoot, "skills-catalog.json");

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 3, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AGEX/{AgexInfo.Version}");
        return client;
    }

    // ----------------------------------------------------------- catalog

    /// <summary>The built-in curated catalog (always available offline).</summary>
    public static SkillCatalog BuiltInCatalog()
    {
        using var stream = typeof(SkillManager).Assembly.GetManifestResourceStream("Agex.Core.Skills.skills-catalog.json")
            ?? throw new InvalidOperationException("The built-in skill catalog is missing.");
        return JsonSerializer.Deserialize<SkillCatalog>(stream, Json.Options) ?? new SkillCatalog();
    }

    /// <summary>The newest valid catalog: a verified download from an AGEX release, or the built-in one.</summary>
    public SkillCatalog Catalog()
    {
        var builtIn = BuiltInCatalog();
        try
        {
            if (Json.ReadFile<SkillCatalog>(CachedCatalogPath) is { Format: "agex-skill-catalog" } cached && string.CompareOrdinal(cached.Updated, builtIn.Updated) > 0 && cached.Skills.All(skill => ValidateManifest(skill).Count == 0))
                return cached;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { _log?.Error("skill_catalog_cache_invalid", ex); }
        return builtIn;
    }

    /// <summary>Stores a catalog downloaded with an AGEX release after its checksum was verified by the caller.</summary>
    public void AcceptCatalog(string verifiedFile)
    {
        var catalog = JsonSerializer.Deserialize<SkillCatalog>(File.ReadAllText(verifiedFile), Json.Options) ?? throw new SkillException("The catalog is empty.");
        if (catalog.Format != "agex-skill-catalog" || catalog.Skills.Any(skill => ValidateManifest(skill).Count > 0)) throw new SkillException("The downloaded catalog is not valid.");
        Directory.CreateDirectory(Path.GetDirectoryName(CachedCatalogPath)!);
        File.Copy(verifiedFile, CachedCatalogPath, overwrite: true);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+/[A-Za-z0-9_.\-]+$")]
    private static partial Regex RepositoryPattern();

    /// <summary>Checks a manifest before anything is downloaded. Returns the problems found.</summary>
    public static List<string> ValidateManifest(SkillManifest manifest)
    {
        var problems = new List<string>();
        if (!IdPattern().IsMatch(manifest.Id)) problems.Add("The skill id is not valid.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80) problems.Add("The skill name is missing or too long.");
        if (manifest.Kind == SkillKind.Instructions && manifest.Trust is SkillTrust.Curated or SkillTrust.Verified)
        {
            var source = manifest.Source;
            if (source is null) { problems.Add("The skill has no source."); return problems; }
            if (!RepositoryPattern().IsMatch(source.Repository)) problems.Add("The source repository is not valid.");
            if (!CommitPattern().IsMatch(source.Commit)) problems.Add("The source is not pinned to a commit.");
            if (source.BasePath.Contains("..") || source.BasePath.StartsWith('/')) problems.Add("The source path is not valid.");
            if (source.Files.Count == 0 || source.Files.Count > MaxFiles) problems.Add("The skill has no files or too many files.");
            if (!source.Files.Any(file => file.Path == "SKILL.md")) problems.Add("The skill has no SKILL.md.");
            if (source.Files.Sum(file => file.Size) + source.ExtraFiles.Sum(file => file.Size) > MaxTotalBytes) problems.Add("The skill is too large.");
            foreach (var file in source.Files)
            {
                if (SafeArchive.SafeRelativePath(file.Path) is null) problems.Add($"Unsafe file path: {file.Path}");
                if (!Sha256Pattern().IsMatch(file.Sha256)) problems.Add($"Missing checksum for {file.Path}");
            }
            foreach (var extra in source.ExtraFiles)
            {
                if (SafeArchive.SafeRelativePath(extra.From) is null || SafeArchive.SafeRelativePath(extra.To) is null) problems.Add($"Unsafe file path: {extra.From}");
                if (!Sha256Pattern().IsMatch(extra.Sha256)) problems.Add($"Missing checksum for {extra.From}");
            }
        }
        if (manifest.Kind == SkillKind.Mcp)
        {
            var mcp = manifest.Mcp;
            if (mcp is null) problems.Add("The MCP skill has no server definition.");
            else if (mcp.Transport == "http")
            {
                if (!Uri.TryCreate(mcp.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps) problems.Add("Hosted MCP servers must use https.");
            }
            else if (mcp.Transport == "stdio")
            {
                if (string.IsNullOrWhiteSpace(mcp.Command) || mcp.Command.IndexOfAny(['&', '|', ';', '<', '>', '`', '$', '\n']) >= 0) problems.Add("The MCP command is not valid.");
            }
            else problems.Add("Unknown MCP transport.");
            if (mcp is not null && mcp.SecretEnv.Any(name => !Regex.IsMatch(name, "^[A-Z][A-Z0-9_]{1,63}$"))) problems.Add("A secret name is not valid.");
        }
        return problems;
    }

    public CompatibilityReport CheckCompatibility(SkillManifest manifest, IEnumerable<string> enabledAgents)
    {
        var problems = ValidateManifest(manifest);
        var warnings = new List<string>();
        var os = _platform.Os switch { OsKind.Windows => "windows", OsKind.MacOS => "macos", _ => "linux" };
        if (manifest.SupportedPlatforms.Count > 0 && !manifest.SupportedPlatforms.Contains(os)) problems.Add($"This skill does not support {os}.");
        if (manifest.MinAgexVersion.Length > 0 && AgexInfo.CompareVersions(AgexInfo.Version, manifest.MinAgexVersion) < 0)
            problems.Add($"This skill needs AGEX {manifest.MinAgexVersion} or newer (you have {AgexInfo.Version}).");
        if (manifest.TestedAgexVersion.Length > 0 && AgexInfo.CompareVersions(AgexInfo.Version, manifest.TestedAgexVersion) > 0)
            warnings.Add($"This skill was tested up to AGEX {manifest.TestedAgexVersion}.");
        var enabled = enabledAgents.ToList();
        if (manifest.SupportedAgents.Count > 0 && !manifest.SupportedAgents.Intersect(enabled, StringComparer.OrdinalIgnoreCase).Any())
            warnings.Add("None of your enabled agents supports this skill: " + string.Join(", ", manifest.SupportedAgents) + ".");
        foreach (var tool in manifest.RequiredTools.Where(tool => !string.IsNullOrWhiteSpace(tool)))
        {
            var command = tool switch { "node" => "npx", "uv" => "uvx", "python" => _platform.Os == OsKind.Windows ? "python" : "python3", _ => tool };
            if (_platform.FindExecutable(command) is null) warnings.Add($"Needs {ToolLabel(tool)}, which was not found on this computer.");
        }
        return new CompatibilityReport(problems.Count == 0, problems, warnings);
    }

    private static string ToolLabel(string tool) => tool switch
    {
        "node" => "Node.js (npx)",
        "uv" => "uv (uvx)",
        "python" => "Python",
        "gh" => "GitHub CLI (gh)",
        _ => tool,
    };

    // --------------------------------------------------------- installed

    public List<InstalledSkill> Installed()
    {
        lock (_lock)
        {
            try { return File.Exists(RegistryPath) ? JsonSerializer.Deserialize<List<InstalledSkill>>(File.ReadAllText(RegistryPath), Json.Options) ?? [] : []; }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _log?.Error("skill_registry_damaged", ex);
                return [];
            }
        }
    }

    private void SaveInstalled(List<InstalledSkill> skills)
    {
        lock (_lock) { Json.WriteFile(RegistryPath, skills.OrderBy(skill => skill.Id).ToList()); }
    }

    private void Upsert(InstalledSkill skill)
    {
        lock (_lock)
        {
            var list = Installed();
            list.RemoveAll(item => item.Id == skill.Id);
            list.Add(skill);
            SaveInstalled(list);
        }
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (_lock)
        {
            var list = Installed();
            var skill = list.FirstOrDefault(item => item.Id == id) ?? throw new SkillException($"Skill '{id}' is not installed.");
            skill.Enabled = enabled;
            if (enabled) skill.DisabledReason = "";
            SaveInstalled(list);
        }
    }

    public void SetPermission(string id, SkillPermission permission, PermissionChoice choice)
    {
        lock (_lock)
        {
            var list = Installed();
            var skill = list.FirstOrDefault(item => item.Id == id) ?? throw new SkillException($"Skill '{id}' is not installed.");
            skill.PermissionChoices[permission] = choice;
            SaveInstalled(list);
        }
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            var list = Installed();
            var skill = list.FirstOrDefault(item => item.Id == id);
            if (skill is null) return;
            var folder = Path.Combine(Root, id);
            if (IdPattern().IsMatch(id) && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            foreach (var secret in skill.Manifest.Mcp?.SecretEnv ?? []) _platform.SecureStore.Delete(SecretKey(id, secret));
            list.Remove(skill);
            SaveInstalled(list);
            _log?.Write("skill_removed", new { id });
        }
    }

    public static string SecretKey(string skillId, string name) => $"skill:{skillId}:{name}";

    // ----------------------------------------------------------- install

    /// <summary>
    /// Installs a catalog skill: validate, check compatibility, download every
    /// file from the pinned commit, verify each SHA-256, then register and
    /// enable. Nothing is installed if any step fails.
    /// </summary>
    public async Task<InstalledSkill> InstallAsync(SkillManifest manifest, IDictionary<SkillPermission, PermissionChoice>? choices, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new InstallProgress("Checking the skill", 0, 1));
        var problems = ValidateManifest(manifest);
        if (problems.Count > 0) throw new SkillException(string.Join(" ", problems));
        var compatibility = CheckCompatibility(manifest, manifest.SupportedAgents);
        if (!compatibility.Installable) throw new SkillException(string.Join(" ", compatibility.Problems));

        var installed = new InstalledSkill
        {
            Id = manifest.Id, Manifest = manifest, Enabled = true,
            PermissionChoices = manifest.Permissions.ToDictionary(permission => permission, permission => choices is not null && choices.TryGetValue(permission, out var choice) ? choice : PermissionChoice.AlwaysAllow),
        };
        if (manifest.Kind == SkillKind.Instructions)
        {
            var source = manifest.Source!;
            var staging = Path.Combine(_platform.Paths.Temp, "skill-" + Guid.NewGuid().ToString("N"));
            try
            {
                var total = source.Files.Count + source.ExtraFiles.Count;
                var done = 0;
                foreach (var file in source.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new InstallProgress($"Downloading {file.Path}", done, total));
                    var url = RawUrl(source.Repository, source.Commit, source.BasePath.TrimEnd('/') + "/" + file.Path);
                    await DownloadVerifiedAsync(url, Path.Combine(staging, SafeArchive.SafeRelativePath(file.Path)!), file.Sha256, file.Size, cancellationToken).ConfigureAwait(false);
                    installed.FileHashes[file.Path] = file.Sha256;
                    done++;
                }
                foreach (var extra in source.ExtraFiles)
                {
                    progress?.Report(new InstallProgress($"Downloading {extra.From}", done, total));
                    await DownloadVerifiedAsync(RawUrl(source.Repository, source.Commit, extra.From), Path.Combine(staging, SafeArchive.SafeRelativePath(extra.To)!), extra.Sha256, extra.Size, cancellationToken).ConfigureAwait(false);
                    installed.FileHashes[extra.To.Replace('\\', '/')] = extra.Sha256;
                    done++;
                }
                progress?.Report(new InstallProgress("Installing", total, total));
                installed.Folder = Promote(staging, manifest.Id);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }
        Upsert(installed);
        _log?.Write("skill_installed", new { id = manifest.Id, version = manifest.Version, trust = manifest.Trust.ToString() });
        progress?.Report(new InstallProgress("Installed", 1, 1));
        return installed;
    }

    private static string RawUrl(string repository, string commit, string path) =>
        $"https://raw.githubusercontent.com/{repository}/{commit}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";

    private static async Task DownloadVerifiedAsync(string url, string destination, string expectedSha256, long expectedSize, CancellationToken cancellationToken)
    {
        byte[] data;
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) throw new SkillException("The download was redirected to an insecure address.");
            if (!response.IsSuccessStatusCode) throw new SkillException($"Download failed ({(int)response.StatusCode}) for {Path.GetFileName(destination)}.");
            if (response.Content.Headers.ContentLength > Math.Max(expectedSize, 1) * 2 + 1024) throw new SkillException("The download is larger than expected.");
            data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) { throw new SkillException("Could not reach the skill source. Check your internet connection. (" + ex.Message + ")"); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new SkillException("The download timed out."); }
        var actual = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new SkillException($"{Path.GetFileName(destination)} does not match its published checksum. The skill was not installed.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves a verified staging folder into place, replacing an older version atomically where possible.</summary>
    private string Promote(string staging, string id)
    {
        Directory.CreateDirectory(Root);
        var target = Path.Combine(Root, id);
        var old = target + ".old-" + Guid.NewGuid().ToString("N")[..6];
        if (Directory.Exists(target)) Directory.Move(target, old);
        try { Directory.Move(staging, target); }
        catch (IOException)
        {
            // Different volume: copy instead.
            CopyDirectory(staging, target);
        }
        if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
        return target;
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }

    // ---------------------------------------------------------- updates

    public IReadOnlyList<(InstalledSkill Installed, SkillManifest Available)> AvailableUpdates()
    {
        var catalog = Catalog().Skills.ToDictionary(skill => skill.Id);
        return Installed()
            .Where(skill => skill.Manifest.Trust is SkillTrust.Curated or SkillTrust.Verified && catalog.ContainsKey(skill.Id))
            .Select(skill => (skill, catalog[skill.Id]))
            .Where(pair => pair.Item2.Version != pair.skill.Manifest.Version || pair.Item2.Source?.Commit != pair.skill.Manifest.Source?.Commit)
            .ToList();
    }

    public async Task<int> UpdateAllAsync(IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var (installed, available) in AvailableUpdates())
        {
            await InstallAsync(available, installed.PermissionChoices, progress, cancellationToken).ConfigureAwait(false);
            if (!installed.Enabled) SetEnabled(available.Id, false);
            count++;
        }
        return count;
    }

    // ------------------------------------------------------ custom skills

    [GeneratedRegex(@"^---\s*\n(?<front>[\s\S]*?)\n---", RegexOptions.Multiline)]
    private static partial Regex FrontMatter();

    /// <summary>Reads name and description from SKILL.md front matter (the Agent Skills format).</summary>
    public static (string Name, string Description) ReadSkillMd(string text)
    {
        var match = FrontMatter().Match(text.Replace("\r\n", "\n"));
        if (!match.Success) throw new SkillException("SKILL.md must start with front matter (--- name: ... description: ... ---).");
        string Field(string key)
        {
            var line = Regex.Match(match.Groups["front"].Value, $@"^{key}\s*:\s*(?<value>.+)$", RegexOptions.Multiline);
            return line.Success ? line.Groups["value"].Value.Trim().Trim('"', '\'') : "";
        }
        var name = Field("name");
        var description = Field("description");
        if (name.Length == 0 || description.Length == 0) throw new SkillException("SKILL.md front matter needs both 'name' and 'description'.");
        return (name, description.Length > 400 ? description[..400] : description);
    }

    /// <summary>Installs a skill from a local folder (copied, never linked).</summary>
    public InstalledSkill AddFromFolder(string folder)
    {
        var root = Path.GetFullPath(folder);
        var skillMd = Path.Combine(root, "SKILL.md");
        if (!File.Exists(skillMd)) throw new SkillException("The folder has no SKILL.md file.");
        var (name, description) = ReadSkillMd(File.ReadAllText(skillMd));
        var files = new List<string>();
        long total = 0;
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            if (entry.LinkTarget is not null) throw new SkillException($"The folder contains a link ({entry.Name}); links are not allowed in skills.");
            if (entry is not FileInfo file) continue;
            total += file.Length;
            files.Add(file.FullName);
        }
        if (files.Count > MaxFiles || total > MaxTotalBytes) throw new SkillException("The skill folder is too large (limit 400 files, 20 MB).");
        var staging = Path.Combine(_platform.Paths.Temp, "skill-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(root, file);
                if (SafeArchive.SafeRelativePath(relative) is null) throw new SkillException($"Unsafe file name: {relative}");
                var destination = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            return RegisterLocal(staging, name, description, SkillTrust.Local, "Local folder: " + Redactor.RedactPaths(root));
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    /// <summary>Installs a skill package (.zip) with safe extraction.</summary>
    public InstalledSkill ImportPackage(string zipPath)
    {
        if (new FileInfo(zipPath).Length > MaxTotalBytes) throw new SkillException("The package is larger than 20 MB.");
        var staging = Path.Combine(_platform.Paths.Temp, "skill-" + Guid.NewGuid().ToString("N"));
        try
        {
            try { SafeArchive.Extract(zipPath, staging); }
            catch (InvalidDataException ex) { throw new SkillException("The package was rejected: " + ex.Message); }
            var root = staging;
            if (!File.Exists(Path.Combine(root, "SKILL.md")) && Directory.GetFiles(root).Length == 0 && Directory.GetDirectories(root) is [var single]) root = single;
            var skillMd = Path.Combine(root, "SKILL.md");
            if (!File.Exists(skillMd)) throw new SkillException("The package has no SKILL.md at its top level.");
            var (name, description) = ReadSkillMd(File.ReadAllText(skillMd));
            return RegisterLocal(root, name, description, SkillTrust.Local, "Package: " + Path.GetFileName(zipPath));
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    [GeneratedRegex(@"^https://github\.com/(?<owner>[A-Za-z0-9_.\-]+)/(?<repo>[A-Za-z0-9_.\-]+)/tree/(?<ref>[^/]+)/(?<path>.+?)/?$")]
    private static partial Regex GitHubTreeUrl();

    /// <summary>
    /// Installs a community skill from a GitHub folder URL. The reference is
    /// resolved to a commit and every file is hashed at install time, so later
    /// changes upstream are detected. The files are not reviewed by AGEX.
    /// </summary>
    public async Task<InstalledSkill> AddFromGitHubAsync(string url, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var match = GitHubTreeUrl().Match(url.Trim());
        if (!match.Success) throw new SkillException("Use a GitHub folder link like https://github.com/owner/repo/tree/main/skills/my-skill");
        var repository = match.Groups["owner"].Value + "/" + match.Groups["repo"].Value;
        var basePath = Uri.UnescapeDataString(match.Groups["path"].Value).Trim('/');
        if (basePath.Contains("..")) throw new SkillException("The link contains an unsafe path.");
        progress?.Report(new InstallProgress("Resolving the version", 0, 1));
        var commit = await GetJsonStringAsync($"https://api.github.com/repos/{repository}/commits/{Uri.EscapeDataString(match.Groups["ref"].Value)}", "sha", cancellationToken).ConfigureAwait(false);
        if (!CommitPattern().IsMatch(commit)) throw new SkillException("Could not resolve that link to a commit.");
        using var tree = await GetJsonAsync($"https://api.github.com/repos/{repository}/git/trees/{commit}?recursive=1", cancellationToken).ConfigureAwait(false);
        var files = tree.RootElement.GetProperty("tree").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "blob" && item.GetProperty("path").GetString()!.StartsWith(basePath + "/", StringComparison.Ordinal))
            .Select(item => (Path: item.GetProperty("path").GetString()!, Size: item.TryGetProperty("size", out var size) ? size.GetInt64() : 0, Mode: item.GetProperty("mode").GetString()))
            .ToList();
        if (files.Any(file => file.Mode == "120000")) throw new SkillException("The skill contains symbolic links, which are not allowed.");
        if (!files.Any(file => file.Path == basePath + "/SKILL.md")) throw new SkillException("That folder has no SKILL.md file.");
        if (files.Count > MaxFiles || files.Sum(file => file.Size) > MaxTotalBytes) throw new SkillException("The skill is too large (limit 400 files, 20 MB).");
        var staging = Path.Combine(_platform.Paths.Temp, "skill-" + Guid.NewGuid().ToString("N"));
        try
        {
            var done = 0;
            foreach (var file in files)
            {
                var relative = file.Path[(basePath.Length + 1)..];
                var safe = SafeArchive.SafeRelativePath(relative) ?? throw new SkillException($"Unsafe file name: {relative}");
                progress?.Report(new InstallProgress($"Downloading {relative}", done++, files.Count));
                using var response = await Http.GetAsync(RawUrl(repository, commit, file.Path), cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new SkillException($"Download failed for {relative}.");
                var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (data.Length > 5 * 1024 * 1024) throw new SkillException($"{relative} is too large.");
                var destination = Path.Combine(staging, safe);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllBytesAsync(destination, data, cancellationToken).ConfigureAwait(false);
            }
            var (name, description) = ReadSkillMd(File.ReadAllText(Path.Combine(staging, "SKILL.md")));
            var skill = RegisterLocal(staging, name, description, SkillTrust.Community, $"https://github.com/{repository}/tree/{commit}/{basePath}");
            skill.Manifest.Source = new SkillSource { Repository = repository, Commit = commit, BasePath = basePath };
            skill.Manifest.Version = commit[..7];
            Upsert(skill);
            return skill;
        }
        catch (HttpRequestException ex) { throw new SkillException("Could not reach GitHub: " + ex.Message); }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new SkillException("GitHub refused the request (rate limit). Try again later.");
        if (!response.IsSuccessStatusCode) throw new SkillException($"GitHub answered {(int)response.StatusCode} for that link.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<string> GetJsonStringAsync(string url, string property, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        return document.RootElement.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";
    }

    private InstalledSkill RegisterLocal(string sourceFolder, string name, string description, SkillTrust trust, string origin)
    {
        var id = MakeId(name);
        var hasScripts = Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories)
            .Any(file => Path.GetExtension(file).ToLowerInvariant() is ".py" or ".sh" or ".js" or ".ts" or ".ps1" or ".bat" or ".cmd" or ".mjs" or ".cjs");
        var manifest = new SkillManifest
        {
            Id = id, Name = name, Kind = SkillKind.Instructions, Description = description, Author = trust == SkillTrust.Local ? "You" : "Unknown (community)",
            Version = DateTime.UtcNow.ToString("yyyyMMdd"), Homepage = origin.StartsWith("https://", StringComparison.Ordinal) ? origin : "", Trust = trust,
            Categories = ["Custom"], Permissions = hasScripts ? [SkillPermission.ReadFiles, SkillPermission.RunCommands] : [SkillPermission.ReadFiles],
            SupportedAgents = ["codex", "claude-code", "antigravity", "gemini-cli"], ReleaseNotes = origin,
            CompatibilityNote = hasScripts ? "Contains scripts that an agent may run. AGEX has not reviewed them." : "",
        };
        var installed = new InstalledSkill
        {
            Id = id, Manifest = manifest, Enabled = true,
            PermissionChoices = manifest.Permissions.ToDictionary(permission => permission, permission => hasScripts && permission == SkillPermission.RunCommands ? PermissionChoice.AskEachTime : PermissionChoice.AlwaysAllow),
        };
        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
            installed.FileHashes[Path.GetRelativePath(sourceFolder, file).Replace('\\', '/')] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        var staging = Path.Combine(_platform.Paths.Temp, "skill-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(sourceFolder, staging);
        installed.Folder = Promote(staging, id);
        Upsert(installed);
        _log?.Write("skill_installed", new { id, trust = trust.ToString(), origin = Redactor.RedactPaths(origin) });
        return installed;
    }

    /// <summary>Registers a user-defined MCP server (local command). Secrets are stored in the OS secure store.</summary>
    public InstalledSkill AddMcpServer(string name, string command, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> secrets)
    {
        var manifest = new SkillManifest
        {
            Id = MakeId(name), Name = name, Kind = SkillKind.Mcp, Description = $"Custom MCP server: {command} {string.Join(' ', arguments)}".Trim(),
            Author = "You", Version = "custom", Trust = SkillTrust.Local, Categories = ["Custom"],
            Permissions = [SkillPermission.RunCommands, SkillPermission.Mcp, SkillPermission.Network], SupportedAgents = ["codex", "claude-code"],
            Mcp = new McpSpec { Transport = "stdio", Command = command, Args = arguments.ToList(), SecretEnv = secrets.Keys.ToList() },
            CompatibilityNote = "AGEX has not reviewed this server. It runs with your user account whenever an agent uses it.",
        };
        var problems = ValidateManifest(manifest);
        if (problems.Count > 0) throw new SkillException(string.Join(" ", problems));
        foreach (var (key, value) in secrets) _platform.SecureStore.Set(SecretKey(manifest.Id, key), value);
        var installed = new InstalledSkill
        {
            Id = manifest.Id, Manifest = manifest, Enabled = true,
            PermissionChoices = manifest.Permissions.ToDictionary(permission => permission, _ => PermissionChoice.AskEachTime),
        };
        Upsert(installed);
        return installed;
    }

    private string MakeId(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length < 2) slug = "skill";
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        var id = "custom-" + slug;
        var existing = Installed().Select(skill => skill.Id).ToHashSet();
        var candidate = id;
        for (var index = 2; existing.Contains(candidate); index++) candidate = $"{id}-{index}";
        return candidate;
    }

    // --------------------------------------------------- startup checks

    /// <summary>
    /// Validates every installed skill. A skill whose files are missing,
    /// changed or unreadable is disabled with a clear reason; one broken skill
    /// never stops AGEX. Returns the ids that were disabled.
    /// </summary>
    public IReadOnlyList<string> ValidateInstalled()
    {
        var disabled = new List<string>();
        lock (_lock)
        {
            var list = Installed();
            foreach (var skill in list.Where(skill => skill.Enabled))
            {
                string? problem = null;
                try
                {
                    if (skill.Manifest.Kind == SkillKind.Instructions)
                    {
                        var folder = Path.Combine(Root, skill.Id);
                        if (!Directory.Exists(folder)) problem = "its files are missing";
                        else if (!File.Exists(Path.Combine(folder, "SKILL.md"))) problem = "SKILL.md is missing";
                        else
                        {
                            ReadSkillMd(File.ReadAllText(Path.Combine(folder, "SKILL.md")));
                            foreach (var (relative, hash) in skill.FileHashes)
                            {
                                var path = Path.Combine(folder, SafeArchive.SafeRelativePath(relative) ?? throw new SkillException("unsafe path"));
                                if (!File.Exists(path)) { problem = $"{relative} is missing"; break; }
                                if (!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), hash, StringComparison.OrdinalIgnoreCase)) { problem = $"{relative} was changed after installation"; break; }
                            }
                        }
                    }
                    else if (ValidateManifest(skill.Manifest) is { Count: > 0 } problems) problem = string.Join(" ", problems);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SkillException) { problem = ex.Message; }
                if (problem is null) continue;
                skill.Enabled = false;
                skill.DisabledReason = $"{StartupFailurePrefix}: {problem}.";
                disabled.Add(skill.Id);
                _log?.Write("skill_disabled", new { id = skill.Id, reason = problem });
            }
            if (disabled.Count > 0) SaveInstalled(list);
        }
        return disabled;
    }

    // ---------------------------------------------- use during requests

    public sealed record ActiveSkills(IReadOnlyList<SkillContext> Instructions, IReadOnlyList<McpServerSpec> McpServers, IReadOnlyList<(InstalledSkill Skill, IReadOnlyList<SkillPermission> Ask)> NeedApproval);

    /// <summary>
    /// Skills for one request: enabled, healthy, allowed on this project, with
    /// no denied permission. Skills with "ask each time" permissions are
    /// returned separately so the caller can ask the user.
    /// </summary>
    public ActiveSkills ForRequest(IReadOnlyCollection<string>? projectSkillIds)
    {
        if (SafeMode) return new ActiveSkills([], [], []);
        var instructions = new List<SkillContext>();
        var servers = new List<McpServerSpec>();
        var ask = new List<(InstalledSkill, IReadOnlyList<SkillPermission>)>();
        foreach (var skill in Installed().Where(skill => skill.Enabled && skill.DisabledReason.Length == 0))
        {
            if (projectSkillIds is not null && !projectSkillIds.Contains(skill.Id)) continue;
            if (skill.PermissionChoices.Values.Any(choice => choice == PermissionChoice.Deny)) continue;
            var askList = skill.PermissionChoices.Where(pair => pair.Value == PermissionChoice.AskEachTime).Select(pair => pair.Key).ToList();
            if (askList.Count > 0) { ask.Add((skill, askList)); continue; }
            Add(skill, instructions, servers);
        }
        return new ActiveSkills(instructions, servers, ask);
    }

    /// <summary>Adds a skill the user approved for this request.</summary>
    public void AddApproved(InstalledSkill skill, List<SkillContext> instructions, List<McpServerSpec> servers) => Add(skill, instructions, servers);

    private void Add(InstalledSkill skill, List<SkillContext> instructions, List<McpServerSpec> servers)
    {
        if (skill.Manifest.Kind == SkillKind.Instructions)
        {
            instructions.Add(new SkillContext(skill.Id, skill.Manifest.Name, skill.Manifest.Description, Path.Combine(Root, skill.Id), null));
            return;
        }
        var mcp = skill.Manifest.Mcp!;
        var secrets = new Dictionary<string, string>();
        foreach (var name in mcp.SecretEnv)
            if (_platform.SecureStore.Get(SecretKey(skill.Id, name)) is { Length: > 0 } value) secrets[name] = value;
        var key = Regex.Replace(skill.Id, "[^A-Za-z0-9_]", "_");
        if (mcp.Transport == "http")
        {
            if (mcp.BearerSecret.Length > 0 && !secrets.ContainsKey(mcp.BearerSecret)) return; // token not set yet
            servers.Add(new McpServerSpec(key, "", [], secrets, mcp.Url, mcp.BearerSecret.Length > 0 ? mcp.BearerSecret : null));
            return;
        }
        var command = _platform.FindExecutable(mcp.Command) ?? mcp.Command;
        servers.Add(new McpServerSpec(key, command, mcp.Args, secrets));
    }

    /// <summary>True when an MCP skill still needs a secret (for example a GitHub token) before it can be used.</summary>
    public bool MissingSecret(InstalledSkill skill) =>
        skill.Manifest.Mcp?.SecretEnv.Any(name => string.IsNullOrEmpty(_platform.SecureStore.Get(SecretKey(skill.Id, name)))) == true;

    public void SetSecret(string skillId, string name, string value) => _platform.SecureStore.Set(SecretKey(skillId, name), value);
}
