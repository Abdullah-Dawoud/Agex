using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Platform;
using Agex.Core.Runtime;
using Agex.Core.Updates;

namespace Agex.Core.Connections;

/// <summary>An installed Autodesk program AGEX found (for example Revit 2026).</summary>
public sealed record AutodeskProduct(string Product, string Year, string InstallPath)
{
    public string Name => (Product == "revit" ? "Revit " : "AutoCAD ") + Year;
    /// <summary>The prebuilt bridge covers Revit 2025-2027 and AutoCAD 2025 and later (the .NET 8+ generation).</summary>
    public bool Supported => int.TryParse(Year, out var year) && (Product == "revit" ? year is >= 2025 and <= 2027 : year >= 2025);
}

public sealed record AutodeskProductStatus(AutodeskProduct Product, bool Installed, bool? Connected, string Detail)
{
    public bool Running { get; init; }
    public bool? DocumentAvailable { get; init; }
}

public sealed record AutodeskBridgeStatus(bool HostInstalled, string Version, IReadOnlyList<AutodeskProductStatus> Products);

/// <summary>Where the bridge package comes from, and the SHA-256 it must have.</summary>
public sealed record BridgePackageSource(Uri Package, string? Sha256, string Description);

/// <summary>
/// Installs the Autodesk AI Bridge for the current user: the prebuilt package
/// (published with each AGEX release, checked against its SHA256SUMS.txt) is
/// unpacked into %LOCALAPPDATA%\AutodeskAIBridge, the Revit add-in is
/// registered for each supported Revit year found, and the AutoCAD bundle goes
/// to %APPDATA%\Autodesk\ApplicationPlugins. No administrator rights are
/// needed and nothing is compiled on the user's computer. Every change is
/// journaled and rolled back if a step fails.
/// </summary>
public sealed class AutodeskBridgeInstaller
{
    public const string PackageName = "autodesk-ai-bridge-win-x64.zip";
    public const string RevitAddInId = "7B9A8F2C-4D4C-4DB0-A65C-2CBF44B41938";

    private readonly IPlatformService _platform;
    private readonly ArtifactDownloader _downloader;
    private readonly AgexLog? _log;
    private readonly IReadOnlyList<string> _programRoots;
    private readonly string _localAppData;
    private readonly string _appData;
    private readonly Func<AutodeskProduct, bool> _isRunning;

    /// <param name="programRoots">Folders that hold "Autodesk\Revit 2026" and similar (tests pass their own).</param>
    public AutodeskBridgeInstaller(IPlatformService platform, ArtifactDownloader downloader, AgexLog? log = null,
        IReadOnlyList<string>? programRoots = null, string? localAppData = null, string? appData = null,
        Func<AutodeskProduct, bool>? isRunning = null)
    {
        _platform = platform;
        _downloader = downloader;
        _log = log;
        _programRoots = programRoots ?? [Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)];
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _appData = appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _isRunning = isRunning ?? (product =>
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(product.Product == "revit" ? "Revit" : "acad");
                try { return processes.Length > 0; }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
        });
    }

    public string Root => Path.Combine(_localAppData, "AutodeskAIBridge");
    public string HostPath => Path.Combine(Root, "Host", "AutodeskAIBridge.Host.exe");
    private string RecordPath => Path.Combine(Root, "installed.json");
    public string RevitManifest(string year) => Path.Combine(_appData, "Autodesk", "Revit", "Addins", year, "AutodeskAIBridge.addin");
    public string RevitFolder(string year) => Path.Combine(Root, "Revit", year);
    public string AutoCadBundle => Path.Combine(_appData, "Autodesk", "ApplicationPlugins", "AutodeskAIBridge.bundle");

    /// <summary>Revit and AutoCAD installations found in the program folders.</summary>
    public IReadOnlyList<AutodeskProduct> Detect()
    {
        var found = new List<AutodeskProduct>();
        foreach (var root in _programRoots)
        {
            var autodesk = Path.Combine(root, "Autodesk");
            if (!Directory.Exists(autodesk)) continue;
            foreach (var folder in Directory.GetDirectories(autodesk))
            {
                var match = Regex.Match(Path.GetFileName(folder), @"^(Revit|AutoCAD)\s+(20\d\d)$", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var product = match.Groups[1].Value.ToLowerInvariant();
                if (!File.Exists(Path.Combine(folder, product == "revit" ? "Revit.exe" : "acad.exe"))) continue;
                found.Add(new AutodeskProduct(product, match.Groups[2].Value, folder));
            }
        }
        return found.OrderBy(item => item.Product).ThenBy(item => item.Year).ToList();
    }

    public string? InstalledVersion()
    {
        try { return File.Exists(RecordPath) && File.Exists(HostPath) ? JsonDocument.Parse(File.ReadAllText(RecordPath)).RootElement.GetProperty("version").GetString() : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException) { return null; }
    }

    /// <summary>Host and per-program state, without starting anything. <paramref name="connected"/> comes from a live test.</summary>
    public AutodeskBridgeStatus Status(IReadOnlySet<string>? connected = null, IReadOnlySet<string>? documents = null)
    {
        var products = Detect().Select(product =>
        {
            if (!product.Supported)
                return new AutodeskProductStatus(product, false, null, product.Product == "revit" && int.Parse(product.Year) > 2027
                    ? "Newer than this bridge version; update AGEX to get a bridge for it."
                    : "Not supported by the prebuilt bridge (Revit 2025-2027 and AutoCAD 2025 or later).");
            var installed = product.Product == "revit" ? File.Exists(RevitManifest(product.Year)) : File.Exists(Path.Combine(AutoCadBundle, "PackageContents.xml"));
            var running = _isRunning(product);
            bool? live = connected is null ? null : connected.Contains(product.Product);
            bool? document = documents is null ? null : documents.Contains(product.Product);
            var detail = !installed ? "AGEX integration not installed" : live == true && document == false ? "Connected; open a document to use it"
                : live == true ? "Connected" : !running ? $"Installed; open {product.Name}, then test again"
                : live == false ? "Application running; load the AGEX integration or restart the application, then test again" : "Application running; test the bridge connection";
            return new AutodeskProductStatus(product, installed, live, detail) { Running = running, DocumentAvailable = document };
        }).ToList();
        return new AutodeskBridgeStatus(File.Exists(HostPath), InstalledVersion() ?? "", products);
    }

    /// <summary>
    /// The package for this AGEX version from its GitHub release, checked against that release's
    /// SHA256SUMS.txt. AGEX_BRIDGE_PACKAGE (a file or https address) and AGEX_BRIDGE_SHA256 override it
    /// for testing a locally built package.
    /// </summary>
    public async Task<BridgePackageSource> ResolveSourceAsync(CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable("AGEX_BRIDGE_PACKAGE") is { Length: > 0 } custom)
        {
            var uri = File.Exists(custom) ? new Uri(Path.GetFullPath(custom)) : new Uri(custom);
            return new BridgePackageSource(uri, Environment.GetEnvironmentVariable("AGEX_BRIDGE_SHA256"), "Local test package");
        }
        var release = $"https://github.com/{AgexInfo.Repository}/releases/download/v{AgexInfo.Version}/";
        var sums = await _downloader.TextAsync(new Uri(release + "SHA256SUMS.txt"), cancellationToken).ConfigureAwait(false);
        var sha = UpdateService.ExpectedHash(sums, PackageName) ?? throw new InstallFailedException($"The AGEX {AgexInfo.Version} release has no Autodesk AI Bridge package.");
        return new BridgePackageSource(new Uri(release + PackageName), sha, $"AGEX {AgexInfo.Version} release on GitHub ({AgexInfo.Repository})");
    }

    /// <summary>Downloads, verifies and installs the bridge. Returns the installed version.</summary>
    public async Task<string> InstallAsync(BridgePackageSource source, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (_platform.Os != OsKind.Windows) throw new InstallFailedException("Revit and AutoCAD run on Windows only.");
        var work = Path.Combine(_platform.Paths.Temp, "bridge-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            progress?.Report("Downloading the bridge package");
            var archive = await _downloader.DownloadAsync(source.Package, Path.Combine(work, PackageName), source.Sha256, cancellationToken).ConfigureAwait(false);
            progress?.Report(source.Sha256 is null ? "Downloaded (no checksum given for this test package)" : "Checksum verified");
            var package = Path.Combine(work, "package");
            ArtifactDownloader.Extract(archive, package);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(package, "bridge.json"), cancellationToken).ConfigureAwait(false));
            var version = manifest.RootElement.GetProperty("version").GetString() ?? "unknown";
            if (!File.Exists(Path.Combine(package, "host", "AutodeskAIBridge.Host.exe"))) throw new InstallFailedException("The bridge package has no host program.");

            using var journal = new InstallJournal(Path.Combine(work, "backup"));
            try
            {
                progress?.Report("Installing the bridge host");
                journal.CopyDirectory(Path.Combine(package, "host"), Path.GetDirectoryName(HostPath)!);
                var products = Detect().Where(item => item.Supported).ToList();
                foreach (var product in products.Where(item => item.Product == "revit"))
                {
                    var addin = Path.Combine(package, "revit", product.Year);
                    if (!Directory.Exists(addin)) continue;
                    progress?.Report($"Registering the {product.Name} add-in");
                    journal.CopyDirectory(addin, RevitFolder(product.Year));
                    journal.WriteFile(RevitManifest(product.Year), AddInManifest(Path.Combine(RevitFolder(product.Year), "AutodeskAIBridge.Revit.dll")));
                }
                // One bundle serves every AutoCAD 2025 and later; AutoCAD loads it from the user's ApplicationPlugins folder.
                var bundle = Path.Combine(package, "autocad", "AutodeskAIBridge.bundle");
                if (products.Any(item => item.Product == "autocad") && Directory.Exists(bundle))
                {
                    progress?.Report("Registering the AutoCAD plug-in (" + string.Join(", ", products.Where(item => item.Product == "autocad").Select(item => item.Name)) + ")");
                    journal.CopyDirectory(bundle, AutoCadBundle);
                }
                EnsureRuntimeSettings(journal);
                journal.WriteFile(RecordPath, JsonSerializer.Serialize(new { version, installedAt = DateTimeOffset.UtcNow, source = source.Description }));
                journal.Commit();
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not InstallFailedException)
            {
                throw new InstallFailedException("Installing the bridge failed and every change was undone: " + ex.Message, ex);
            }
            _log?.Write("autodesk_bridge_installed", new { version });
            return version;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Removes the host, add-ins and bundle. The shared pipe settings go too.</summary>
    public void Uninstall()
    {
        foreach (var year in Directory.Exists(Path.Combine(_appData, "Autodesk", "Revit", "Addins")) ? Directory.GetDirectories(Path.Combine(_appData, "Autodesk", "Revit", "Addins")).Select(Path.GetFileName).OfType<string>() : [])
            TryDelete(RevitManifest(year));
        TryDelete(AutoCadBundle);
        TryDelete(Root);
        _log?.Write("autodesk_bridge_removed", new { });
    }

    /// <summary>The per-user pipe name and shared secret the host and plug-ins use (kept when they already exist).</summary>
    private void EnsureRuntimeSettings(InstallJournal journal)
    {
        var secret = Path.Combine(Root, "config", "ipc-secret");
        var pipe = Path.Combine(Root, "config", "ipc-pipe");
        if (!File.Exists(secret) || File.ReadAllText(secret).Trim().Length < 32)
            journal.WriteFile(secret, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + Environment.NewLine);
        if (!File.Exists(pipe) || File.ReadAllText(pipe).Trim().Length == 0)
        {
            var identity = Environment.UserDomainName + "\\" + Environment.UserName + "@" + Environment.MachineName;
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
            journal.WriteFile(pipe, "AutodeskAIBridge-" + suffix + Environment.NewLine);
        }
    }

    internal static string AddInManifest(string assembly) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <RevitAddIns>
          <AddIn Type="Application">
            <Name>Autodesk AI Bridge</Name>
            <Assembly>{System.Security.SecurityElement.Escape(assembly)}</Assembly>
            <AddInId>{RevitAddInId}</AddInId>
            <FullClassName>AutodeskAIBridge.Revit.RevitBridgeApplication</FullClassName>
            <VendorId>AutodeskAIBridge</VendorId>
            <VendorDescription>Autodesk AI Bridge (installed by AGEX)</VendorDescription>
          </AddIn>
        </RevitAddIns>
        """;

    /// <summary>Which products the running host reports as connected (from autodesk_list_instances).</summary>
    public static IReadOnlySet<string> ConnectedProducts(string? toolOutput)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(toolOutput)) return result;
        foreach (Match match in Regex.Matches(toolOutput, "\\\\?\"product\\\\?\"\\s*:\\s*\\\\?\"(revit|autocad)", RegexOptions.IgnoreCase)) result.Add(match.Groups[1].Value.ToLowerInvariant());
        return result;
    }

    /// <summary>Connected products that report an active or open document.</summary>
    public static IReadOnlySet<string> DocumentProducts(string? toolOutput)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(toolOutput)) return result;
        try
        {
            using var reply = JsonDocument.Parse(toolOutput);
            var body = reply.RootElement;
            if (body.ValueKind != JsonValueKind.Object) return result;
            if (body.TryGetProperty("result", out var rpc)) body = rpc;
            if (body.ValueKind != JsonValueKind.Object) return result;
            if (body.TryGetProperty("structuredContent", out var structured)) body = structured;
            if (body.ValueKind != JsonValueKind.Object) return result;
            if (body.TryGetProperty("data", out var data)) body = data;
            if (body.ValueKind != JsonValueKind.Object) return result;
            if (!body.TryGetProperty("instances", out var instances) || instances.ValueKind != JsonValueKind.Array) return result;
            foreach (var instance in instances.EnumerateArray())
            {
                if (!instance.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.String) continue;
                var name = product.GetString()?.ToLowerInvariant();
                if (name is not ("revit" or "autocad")) continue;
                if (instance.TryGetProperty("activeDocumentId", out var active) && active.ValueKind == JsonValueKind.String && active.GetString() is { Length: > 0 }
                    || instance.TryGetProperty("documents", out var documents) && documents.ValueKind == JsonValueKind.Array && documents.GetArrayLength() > 0)
                    result.Add(name);
            }
        }
        catch (JsonException) { }
        return result;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Error("autodesk_bridge_remove_failed", ex); }
    }
}
