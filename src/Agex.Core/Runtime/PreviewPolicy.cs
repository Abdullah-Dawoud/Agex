namespace Agex.Core.Runtime;

/// <summary>What the embedded web preview may load: files inside the previewed folder, and http(s) servers on this computer.</summary>
public static class PreviewPolicy
{
    public static bool IsAllowed(Uri uri, string? root)
    {
        if (uri.Scheme == "about") return uri.AbsoluteUri == "about:blank";
        if (uri.IsFile)
        {
            if (root is null) return false;
            var path = Path.GetFullPath(uri.LocalPath);
            var folder = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(folder, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
        return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && (uri.IsLoopback || uri.Host == "localhost");
    }
}
