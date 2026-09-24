using System.Diagnostics;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>
/// Opens a project or file in a code editor the scan found. AGEX only launches
/// the editor with the path as its single argument; it does not control it.
/// Only the known editors from the scan are supported (no arbitrary programs).
/// </summary>
public static class Editors
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { "vscode", "cursor", "windsurf", "antigravity-ide", "zed", "visual-studio", "jetbrains" };
    private static readonly string[] WindowsExecutables = ["Code.exe", "Cursor.exe", "Windsurf.exe", "Antigravity.exe", "Zed.exe"];

    public static bool IsEditor(string id) => Known.Contains(id);

    /// <summary>The program to start for an editor found at <paramref name="location"/>, or null when AGEX cannot start it safely.</summary>
    public static string? Launchable(OsKind os, string location)
    {
        if (location.Length == 0) return null;
        if (os == OsKind.MacOS && location.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return Directory.Exists(location) ? location : null;
        if (os != OsKind.Windows) return File.Exists(location) ? location : null;
        if (location.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return File.Exists(location) ? location : null;
        // A command-line shim (code.cmd): the editor's own .exe sits a few folders above it.
        var folder = Path.GetDirectoryName(location);
        for (var level = 0; level < 4 && folder is not null; level++, folder = Path.GetDirectoryName(folder))
            foreach (var name in WindowsExecutables)
                if (File.Exists(Path.Combine(folder, name))) return Path.Combine(folder, name);
        return null;
    }

    public static bool Open(IPlatformService platform, string editorId, string location, string target, AgexLog? log = null)
    {
        if (!IsEditor(editorId) || Launchable(platform.Os, location) is not { } program || !(Directory.Exists(target) || File.Exists(target))) return false;
        try
        {
            var start = platform.Os == OsKind.MacOS && program.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-a", program, target } }
                : new ProcessStartInfo(program) { ArgumentList = { target } };
            start.UseShellExecute = false;
            start.WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target) ?? "";
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log?.Error("editor_open_failed", ex, new { editor = editorId });
            return false;
        }
    }
}
