using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Agex.Core.Runtime;

/// <summary>
/// Serves one project folder over http://127.0.0.1 so agents, the browser tool
/// and the web preview open local HTML pages the way a web server would.
/// Pages opened from file:// cannot load JavaScript modules, which is why games
/// and apps built from plain HTML fail there. The server listens on the
/// loopback address only, answers GET and HEAD, serves only files inside the
/// folder, never lists folders and never serves hidden files (.env, .git).
/// </summary>
public sealed class LocalWebServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _root;
    private readonly Task _loop;

    private LocalWebServer(string root)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }
    public string Root => _root.TrimEnd(Path.DirectorySeparatorChar);
    public Uri BaseUri => new($"http://127.0.0.1:{Port}/");

    public static LocalWebServer Start(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        return new LocalWebServer(root);
    }

    /// <summary>The page to open first: index.html in the folder, or in a usual web folder inside it. Null when there is none.</summary>
    public static string? EntryPage(string root)
    {
        foreach (var candidate in new[] { "index.html", "index.htm", "public/index.html", "dist/index.html", "build/index.html", "www/index.html", "docs/index.html" })
            if (File.Exists(Path.Combine(root, candidate))) return candidate;
        return null;
    }

    /// <summary>The address of a file inside the served folder.</summary>
    public Uri UrlFor(string relativePath) =>
        new(BaseUri, string.Join('/', relativePath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString)));

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 10_000;
                var stream = client.GetStream();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var header = await ReadHeaderAsync(stream, timeout.Token).ConfigureAwait(false);
                if (header is null) return;
                var parts = header.Split('\n')[0].Trim().Split(' ');
                if (parts.Length < 2) { await WriteAsync(stream, 400, "Bad Request", "text/plain", "Bad request"u8.ToArray(), true).ConfigureAwait(false); return; }
                var method = parts[0];
                if (method is not ("GET" or "HEAD")) { await WriteAsync(stream, 405, "Method Not Allowed", "text/plain", "Only GET and HEAD are supported."u8.ToArray(), true).ConfigureAwait(false); return; }
                var file = Resolve(parts[1]);
                if (file is null) { await WriteAsync(stream, 404, "Not Found", "text/plain", "Not found"u8.ToArray(), method == "GET").ConfigureAwait(false); return; }
                var body = await File.ReadAllBytesAsync(file, timeout.Token).ConfigureAwait(false);
                await WriteAsync(stream, 200, "OK", ContentType(file), body, method == "GET").ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or UnauthorizedAccessException or ObjectDisposedException) { }
        }
    }

    /// <summary>Maps a request target to a file inside the folder, or null.</summary>
    internal string? Resolve(string target)
    {
        var path = target.Split('?', '#')[0];
        string decoded;
        try { decoded = Uri.UnescapeDataString(path); }
        catch (UriFormatException) { return null; }
        if (decoded.Contains('\0') || decoded.Contains(':')) return null;
        var segments = decoded.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Hidden files and folders (.env, .git, .ssh) are never served; neither is anything above the folder.
        if (segments.Any(segment => segment == ".." || segment.StartsWith('.'))) return null;
        string full;
        try { full = Path.GetFullPath(Path.Combine(_root, Path.Combine(segments))); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!(full + Path.DirectorySeparatorChar).StartsWith(_root, comparison)) return null;
        if (Directory.Exists(full)) full = Path.Combine(full, "index.html");
        return File.Exists(full) ? full : null;
    }

    private static async Task<string?> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal) || text.Contains("\n\n", StringComparison.Ordinal)) return text;
        }
        return total > 0 ? Encoding.ASCII.GetString(buffer, 0, total) : null;
    }

    private static async Task WriteAsync(NetworkStream stream, int code, string reason, string type, byte[] body, bool includeBody)
    {
        var head = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head)).ConfigureAwait(false);
        if (includeBody) await stream.WriteAsync(body).ConfigureAwait(false);
    }

    internal static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".map" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".wasm" => "application/wasm",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".wav" => "audio/wav",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".glb" => "model/gltf-binary",
        ".gltf" => "model/gltf+json",
        ".txt" or ".md" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _loop.ConfigureAwait(false); } catch (Exception) { }
        _stop.Dispose();
    }
}
