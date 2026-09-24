namespace Agex.Core.Skills;

/// <summary>Instructions = an Agent Skills (SKILL.md) folder. Mcp = an MCP server made available to agents.</summary>
public enum SkillKind { Instructions, Mcp }

/// <summary>Where a skill came from, from most to least trusted.</summary>
public enum SkillTrust { Curated, Verified, Community, Local }

public enum SkillPermission { ReadFiles, WriteFiles, Network, Browser, RunCommands, Git, Github, Mcp, Clipboard }

public enum PermissionChoice { AlwaysAllow, AskEachTime, Deny }

public sealed class CatalogFile
{
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

public sealed class CatalogExtraFile
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

public sealed class SkillSource
{
    public string Repository { get; set; } = "";
    public string Commit { get; set; } = "";
    public string BasePath { get; set; } = "";
    public List<CatalogFile> Files { get; set; } = [];
    public List<CatalogExtraFile> ExtraFiles { get; set; } = [];
}

public sealed class McpSpec
{
    /// <summary>"stdio" (a local command) or "http" (a hosted endpoint).</summary>
    public string Transport { get; set; } = "stdio";
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = [];
    public string Url { get; set; } = "";
    /// <summary>Secure-store key whose value is sent as a bearer token (http transport).</summary>
    public string BearerSecret { get; set; } = "";
    /// <summary>Secrets passed to the server as environment variables (names only here).</summary>
    public List<string> SecretEnv { get; set; } = [];
}

public sealed class Popularity
{
    public string Label { get; set; } = "";
    public long Value { get; set; }
    public string AsOf { get; set; } = "";
}

/// <summary>One skill as described by a catalog or a local manifest.</summary>
public sealed class SkillManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public SkillKind Kind { get; set; }
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string License { get; set; } = "";
    public string Homepage { get; set; } = "";
    public List<string> Categories { get; set; } = [];
    public bool Recommended { get; set; }
    public SkillTrust Trust { get; set; } = SkillTrust.Community;
    public List<string> Capabilities { get; set; } = [];
    public List<SkillPermission> Permissions { get; set; } = [];
    public List<string> SupportedAgents { get; set; } = [];
    public List<string> SupportedPlatforms { get; set; } = ["windows", "macos", "linux"];
    public List<string> RequiredTools { get; set; } = [];
    public string MinAgexVersion { get; set; } = "";
    public string TestedAgexVersion { get; set; } = "";
    public string CompatibilityNote { get; set; } = "";
    public Popularity? Popularity { get; set; }
    public string ReleaseNotes { get; set; } = "";
    public SkillSource? Source { get; set; }
    public McpSpec? Mcp { get; set; }
}

public sealed class SkillCatalog
{
    public string Format { get; set; } = "";
    public int FormatVersion { get; set; }
    public string Updated { get; set; } = "";
    public string Source { get; set; } = "";
    public List<SkillManifest> Skills { get; set; } = [];
}

/// <summary>An installed skill, as recorded in skills/installed.json.</summary>
public sealed class InstalledSkill
{
    public string Id { get; set; } = "";
    public SkillManifest Manifest { get; set; } = new();
    public bool Enabled { get; set; } = true;
    /// <summary>Why AGEX disabled the skill (failed validation at startup). Empty when healthy.</summary>
    public string DisabledReason { get; set; } = "";
    public string Folder { get; set; } = "";
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<SkillPermission, PermissionChoice> PermissionChoices { get; set; } = new();
    /// <summary>SHA-256 of every installed file, recorded at install time and checked at startup.</summary>
    public Dictionary<string, string> FileHashes { get; set; } = new();
}

public static class SkillText
{
    public static string Permission(SkillPermission permission) => permission switch
    {
        SkillPermission.ReadFiles => "Read project files",
        SkillPermission.WriteFiles => "Change project files",
        SkillPermission.Network => "Use the internet",
        SkillPermission.Browser => "Control a web browser",
        SkillPermission.RunCommands => "Run programs and commands",
        SkillPermission.Git => "Use Git",
        SkillPermission.Github => "Act on GitHub with your token",
        SkillPermission.Mcp => "Start an MCP tool server",
        SkillPermission.Clipboard => "Read the clipboard",
        _ => permission.ToString(),
    };

    public static bool IsHighRisk(SkillPermission permission) =>
        permission is SkillPermission.RunCommands or SkillPermission.Github or SkillPermission.Browser or SkillPermission.WriteFiles or SkillPermission.Mcp;

    public static string Trust(SkillTrust trust) => trust switch
    {
        SkillTrust.Curated => "Curated by AGEX",
        SkillTrust.Verified => "Official publisher",
        SkillTrust.Community => "Community (not reviewed)",
        SkillTrust.Local => "Your own (local)",
        _ => trust.ToString(),
    };
}
