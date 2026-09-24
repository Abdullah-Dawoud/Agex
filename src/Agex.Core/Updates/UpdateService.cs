using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Updates;

public sealed record ReleaseAsset(string Name, Uri Url, long Size);

public sealed record UpdateInfo(bool Available, string CurrentVersion, string LatestVersion, string Notes, ReleaseAsset? Package, ReleaseAsset? Checksums, ReleaseAsset? Signature, string Message);

public sealed record DownloadedUpdate(string PackagePath, string Version, bool SignatureVerified)
{
    public string Sha256 { get; init; } = "";
}

/// <summary>
/// Finds, downloads and verifies AGEX releases from GitHub Releases. The
/// package for this operating system and processor is chosen by name; it must
/// match SHA256SUMS.txt, and when the release is signed the checksum file's
/// ECDSA signature must match the public key built into AGEX. Nothing is run
/// until verification passes.
/// </summary>
public sealed class UpdateService(IPlatformService platform, AgexLog? log = null, HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? CreateHttp();

    /// <summary>
    /// PEM public key (ECDSA P-256) used to verify SHA256SUMS.txt.sig. Empty
    /// until the project owner creates a release signing key (see docs/DEVELOPMENT.md);
    /// until then releases are verified by checksum only and the UI says so.
    /// </summary>
    public const string ReleasePublicKeyPem = "";

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AGEX/{AgexInfo.Version}");
        return client;
    }

    /// <summary>Release asset name for this platform, for example "agex-2.0.0-osx-arm64.zip".</summary>
    public static string PackageName(string version, string runtimeId) =>
        $"agex-{version}-{runtimeId}.{(runtimeId.StartsWith("linux", StringComparison.Ordinal) ? "tar.gz" : "zip")}";

    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken)
    {
        var current = AgexInfo.Version;
        JsonDocument release;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{AgexInfo.Repository}/releases?per_page=10");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateInfo(false, current, current, "", null, null, null, "No AGEX release has been published yet.");
            if (!response.IsSuccessStatusCode) return new UpdateInfo(false, current, current, "", null, null, null, $"Update server answered {(int)response.StatusCode}. Try again later.");
            release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new UpdateInfo(false, current, current, "", null, null, null, "You appear to be offline. AGEX keeps working; check for updates later.");
        }
        using (release)
        {
            // The list is newest first and includes pre-releases, so pre-release builds are offered too.
            if (release.RootElement.ValueKind != JsonValueKind.Array) return new UpdateInfo(false, current, current, "", null, null, null, "The update server sent an unexpected answer. Try again later.");
            var published = release.RootElement.EnumerateArray().Where(item => !(item.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True)).ToList();
            if (published.Count == 0) return new UpdateInfo(false, current, current, "", null, null, null, "No AGEX release has been published yet.");
            var root = published[0];
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var latest = tag.TrimStart('v', 'V');
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var assets = root.TryGetProperty("assets", out var a) ? a.EnumerateArray().Select(asset => new ReleaseAsset(
                asset.GetProperty("name").GetString() ?? "", new Uri(asset.GetProperty("browser_download_url").GetString() ?? "https://invalid/"), asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0)).ToList() : [];
            var package = assets.FirstOrDefault(asset => asset.Name == PackageName(latest, platform.RuntimeId));
            var sums = assets.FirstOrDefault(asset => asset.Name == "SHA256SUMS.txt");
            var signature = assets.FirstOrDefault(asset => asset.Name == "SHA256SUMS.txt.sig");
            var newer = latest.Length > 0 && AgexInfo.CompareVersions(latest, current) > 0;
            var message = !newer ? $"AGEX {current} is up to date."
                : package is null ? $"AGEX {latest} is available, but not for {platform.RuntimeId} yet."
                : sums is null ? $"AGEX {latest} is available, but it has no checksum file, so AGEX will not install it."
                : $"AGEX {latest} is available.";
            return new UpdateInfo(newer && package is not null && sums is not null, current, latest.Length > 0 ? latest : current, notes, package, sums, signature, message);
        }
    }

    /// <summary>Downloads the package and verifies it. Throws when any check fails; the file is deleted in that case.</summary>
    public async Task<DownloadedUpdate> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!info.Available || info.Package is null || info.Checksums is null) throw new InvalidOperationException("No verified update is available.");
        var folder = Path.Combine(platform.Paths.Updates, info.LatestVersion);
        Directory.CreateDirectory(folder);
        var sumsText = await _http.GetStringAsync(info.Checksums.Url, cancellationToken).ConfigureAwait(false);
        var signatureVerified = false;
        if (ReleasePublicKeyPem.Length > 0)
        {
            if (info.Signature is null) throw new InvalidDataException("This release is not signed, but this AGEX build requires signed releases.");
            var signature = await _http.GetByteArrayAsync(info.Signature.Url, cancellationToken).ConfigureAwait(false);
            if (!VerifySignature(Encoding.UTF8.GetBytes(sumsText), signature, ReleasePublicKeyPem)) throw new InvalidDataException("The release signature is not valid. The update was not installed.");
            signatureVerified = true;
        }
        var expected = ExpectedHash(sumsText, info.Package.Name) ?? throw new InvalidDataException($"{info.Package.Name} is not listed in SHA256SUMS.txt.");
        var target = Path.Combine(folder, info.Package.Name);
        var partial = target + ".part";
        try
        {
            using (var response = await _http.GetAsync(info.Package.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? info.Package.Size;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = File.Create(partial);
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                    if (total > 0) progress?.Report((double)written / total);
                }
            }
            var actual = await HashFileAsync(partial, cancellationToken).ConfigureAwait(false);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download does not match the published checksum. It may be damaged or altered, so it was deleted.");
            File.Move(partial, target, overwrite: true);
            log?.Write("update_downloaded", new { version = info.LatestVersion, signed = signatureVerified });
            return new DownloadedUpdate(target, info.LatestVersion, signatureVerified) { Sha256 = actual };
        }
        catch
        {
            try { File.Delete(partial); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>
    /// Starts the installer that shipped with this AGEX installation on the
    /// verified package. The installer checks the hash again, waits for AGEX to
    /// close, replaces only AGEX's own files and starts the new version.
    /// </summary>
    public bool StartInstaller(DownloadedUpdate update)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var psi = platform.Os == OsKind.Windows
            ? new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            : new System.Diagnostics.ProcessStartInfo("/bin/sh");
        var script = Path.Combine(baseDirectory, "install", platform.Os == OsKind.Windows ? "agex-install.ps1" : "agex-install.sh");
        if (!File.Exists(script)) return false;
        if (platform.Os == OsKind.Windows)
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Package", update.PackagePath, "-ExpectedSha256", update.Sha256, "-WaitForExit", Environment.ProcessId.ToString() })
                psi.ArgumentList.Add(arg);
        else
            foreach (var arg in new[] { script, "--package", update.PackagePath, "--sha256", update.Sha256, "--wait-pid", Environment.ProcessId.ToString() })
                psi.ArgumentList.Add(arg);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        using var process = System.Diagnostics.Process.Start(psi);
        log?.Write("update_installer_started", new { version = update.Version });
        return process is not null;
    }

    public static string? ExpectedHash(string sumsText, string fileName)
    {
        foreach (var line in sumsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[])[' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart(' ', '\t', '*') == fileName && parts[0].Length == 64) return parts[0].ToLowerInvariant();
        }
        return null;
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    public static bool VerifySignature(byte[] data, byte[] signature, string publicKeyPem)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException) { return false; }
    }
}
