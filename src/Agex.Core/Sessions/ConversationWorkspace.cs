namespace Agex.Core.Sessions;

/// <summary>Private writable folder for a conversation without a user project.</summary>
public static class ConversationWorkspace
{
    public static string GetOrCreate(string dataRoot, Session? continueFrom = null)
    {
        var root = Path.GetFullPath(Path.Combine(dataRoot, "workspaces"));
        var path = Path.Combine(root, Guid.NewGuid().ToString("N"));
        if (continueFrom is { Projectless: true, Project.Length: > 0 })
        {
            var previous = Path.GetFullPath(continueFrom.Project);
            var relative = Path.GetRelativePath(root, previous);
            if (!Path.IsPathRooted(relative) && relative.Length > 0 && relative != "."
                && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                path = previous;
        }
        Directory.CreateDirectory(path);
        return path;
    }
}
