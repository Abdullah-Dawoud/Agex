namespace Agex.Core.Sessions;

/// <summary>What the desktop workspace panel can show beside the chat.</summary>
public enum WorkspaceSurface { Activity, Changes, Browser, Terminal, Preview, Files, Connections }

/// <summary>A surface that has something in it, with its count ("Changes 3").</summary>
public sealed record SurfaceCount(WorkspaceSurface Surface, int Count);

/// <summary>
/// The workspace panel offers a surface only when the current work produced
/// something for it: changed files, browser pages, commands, a preview, files.
/// Activity and Connections are always one click away but never pushed forward.
/// </summary>
public static class WorkspaceSurfaces
{
    public static IReadOnlyList<SurfaceCount> Visible(Session? session, bool running, int browserPages, int commands, bool previewOpen, int files)
    {
        var list = new List<SurfaceCount>();
        var changes = session?.Changes.Count ?? 0;
        if (changes > 0 || running && session?.Mode == "build") list.Add(new(WorkspaceSurface.Changes, changes));
        if (browserPages > 0) list.Add(new(WorkspaceSurface.Browser, browserPages));
        if (commands > 0) list.Add(new(WorkspaceSurface.Terminal, commands));
        if (previewOpen) list.Add(new(WorkspaceSurface.Preview, 0));
        if (files > 0) list.Add(new(WorkspaceSurface.Files, 0));
        return list;
    }
}

/// <summary>Shows a command as the user would type it: without "Running" and the shell wrapper agents add.</summary>
public static class CommandText
{
    private static readonly System.Text.RegularExpressions.Regex Wrapper = new(
        @"^(?:""?[^""]*?(?:powershell|pwsh|cmd|bash|sh|zsh)(?:\.exe)?""?\s+(?:-NoProfile\s+)?(?:-Command|/c|-lc|-c)\s+)(?<inner>.+)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

    public static string Display(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("Running ", StringComparison.Ordinal)) text = text[8..].Trim();
        if (Wrapper.Match(text) is { Success: true } match) text = match.Groups["inner"].Value.Trim();
        if (text.Length >= 2 && (text[0] == '\'' && text[^1] == '\'' || text[0] == '"' && text[^1] == '"')) text = text[1..^1];
        return text.Replace("\\\\", "\\");
    }

    /// <summary>A short tab title: the program and its first argument ("git log").</summary>
    public static string Title(string raw)
    {
        var parts = Display(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "Command" : string.Join(' ', parts.Take(parts.Length > 1 && !parts[1].StartsWith('-') ? 2 : 1));
    }
}
