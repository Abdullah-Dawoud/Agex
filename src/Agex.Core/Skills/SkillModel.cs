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

/// <summary>How a skill connects to an account.</summary>
public enum SkillAuthType
{
    None,
    /// <summary>A key or token the user pastes once; kept in the OS secure store and passed as an environment variable or bearer token.</summary>
    ApiKey,
    /// <summary>The skill uses a command-line tool that the user signs in to with that tool's own login command.</summary>
    CliLogin,
}

/// <summary>A read-only request that proves a key works (for example GitHub's /user). The key goes only to this https host.</summary>
public sealed class SkillAuthTest
{
    public string Url { get; set; } = "";
    public string Header { get; set; } = "Authorization";
    /// <summary>Prefix before the key, e.g. "Bearer ". Empty = the key alone.</summary>
    public string Scheme { get; set; } = "Bearer ";
    public Dictionary<string, string> ExtraHeaders { get; set; } = new();
    /// <summary>Top-level JSON field shown as the connected identity (e.g. "login"). Empty = none.</summary>
    public string IdentityField { get; set; } = "";
}

public sealed class SkillAuth
{
    public SkillAuthType Type { get; set; } = SkillAuthType.None;
    /// <summary>Secret name (an entry of the MCP secret_env list) for ApiKey.</summary>
    public string Secret { get; set; } = "";
    /// <summary>Plain-language name of what the user provides, e.g. "GitHub personal access token".</summary>
    public string Label { get; set; } = "";
    /// <summary>The provider's page where the user creates the key or reads the sign-in steps (https).</summary>
    public string SetupUrl { get; set; } = "";
    /// <summary>For CliLogin: the tool (one of required_tools) and its login arguments.</summary>
    public string LoginTool { get; set; } = "";
    public List<string> LoginArgs { get; set; } = [];
    public string Note { get; set; } = "";
    public SkillAuthTest? Test { get; set; }
}

/// <summary>A named selection of catalog skills (installing a pack installs each skill once).</summary>
public sealed class SkillPack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Skills { get; set; } = [];
}

/// <summary>What stands between a skill and its use, in the order the user must fix it.</summary>
public enum SkillReadiness { Ready, NotInstalled, PlatformUnsupported, AgentIncompatible, DependencyMissing, AccountRequired, Disabled, Broken }

public sealed record ToolRequirement(string Id, string Label, string InstallUrl);

public sealed record SkillState(SkillReadiness Readiness, string Detail, IReadOnlyList<ToolRequirement> MissingTools);

public enum SkillRisk { Low, Medium, High }

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
    public SkillAuth? Auth { get; set; }
    /// <summary>Date of the pinned commit or package release (yyyy-MM-dd).</summary>
    public string LastUpdated { get; set; } = "";
    /// <summary>Catalog tiers such as "popular" or "advanced".</summary>
    public List<string> Tags { get; set; } = [];
    public string RiskNote { get; set; } = "";
    /// <summary>"free", "free-tier" or "paid" from the service's own pricing page; empty = derived (see <see cref="SkillCosts.Of"/>).</summary>
    public string Cost { get; set; } = "";

    public bool RequiresAccount => Auth?.Type is SkillAuthType.ApiKey or SkillAuthType.CliLogin;
}

public sealed class SkillCatalog
{
    public string Format { get; set; } = "";
    public int FormatVersion { get; set; }
    public string Updated { get; set; } = "";
    public string Source { get; set; } = "";
    public List<SkillManifest> Skills { get; set; } = [];
    public List<SkillPack> Packs { get; set; } = [];
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

public enum SkillCost { Local, Free, FreeTier, Paid }

public static class SkillCosts
{
    /// <summary>
    /// Instruction skills and tools that run on this computer without the network are Local;
    /// other open tools are Free unless the catalog records a free tier or a price.
    /// </summary>
    public static SkillCost Of(SkillManifest skill) => skill.Cost switch
    {
        "paid" => SkillCost.Paid,
        "free-tier" => SkillCost.FreeTier,
        "free" => SkillCost.Free,
        _ when skill.Kind == SkillKind.Instructions => SkillCost.Local,
        _ when skill.Mcp?.Transport == "stdio" && !skill.Permissions.Contains(SkillPermission.Network) => SkillCost.Local,
        _ => SkillCost.Free,
    };

    public static string Label(SkillCost cost) => cost switch
    {
        SkillCost.Local => "LOCAL",
        SkillCost.FreeTier => "FREE TIER",
        SkillCost.Paid => "PAID",
        _ => "FREE",
    };

    public static string Explanation(SkillCost cost) => cost switch
    {
        SkillCost.Local => "Free; runs on this computer and needs no online service.",
        SkillCost.FreeTier => "The service has a free plan with limits; heavier use is paid.",
        SkillCost.Paid => "The service is paid.",
        _ => "Free to use.",
    };
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
        SkillTrust.Curated => "AGEX Curated",
        SkillTrust.Verified => "Official",
        SkillTrust.Community => "Community",
        SkillTrust.Local => "Local",
        _ => trust.ToString(),
    };

    public static string TrustExplanation(SkillTrust trust) => trust switch
    {
        SkillTrust.Verified => "Published by the company behind the tool or service it connects to.",
        SkillTrust.Curated => "An independent project that AGEX reviewed, pinned to an exact version and recommends.",
        SkillTrust.Community => "A community contribution. Pinned and checked, but reviewed less deeply: allow its permissions with care.",
        _ => "Added by you on this computer. Not reviewed by AGEX.",
    };

    /// <summary>Risk from what the skill may do: running code or acting on accounts is higher.</summary>
    public static SkillRisk Risk(SkillManifest manifest)
    {
        var permissions = manifest.Permissions;
        if (permissions.Contains(SkillPermission.Github) || manifest.RequiresAccount && permissions.Contains(SkillPermission.RunCommands)
            || permissions.Contains(SkillPermission.RunCommands) && permissions.Contains(SkillPermission.WriteFiles) && manifest.Trust is SkillTrust.Community or SkillTrust.Local)
            return SkillRisk.High;
        if (permissions.Any(IsHighRisk) || manifest.RequiresAccount) return SkillRisk.Medium;
        return SkillRisk.Low;
    }

    public static string Readiness(SkillReadiness readiness) => readiness switch
    {
        SkillReadiness.Ready => "Ready",
        SkillReadiness.NotInstalled => "Not installed",
        SkillReadiness.PlatformUnsupported => "Not available on this system",
        SkillReadiness.AgentIncompatible => "No enabled agent can use it",
        SkillReadiness.DependencyMissing => "Dependency missing",
        SkillReadiness.AccountRequired => "Account required",
        SkillReadiness.Disabled => "Off",
        SkillReadiness.Broken => "Needs attention",
        _ => readiness.ToString(),
    };
}

/// <summary>A named skill selection for the composer (for example "Deep research").</summary>
public sealed class SkillProfile
{
    public string Name { get; set; } = "";
    public List<string> SkillIds { get; set; } = [];
    /// <summary>Built-in profiles ship with AGEX; the user's own can be deleted.</summary>
    public bool BuiltIn { get; set; }
}

public static class SkillProfiles
{
    /// <summary>Starting profiles. Only installed skills are used when a profile is picked.</summary>
    public static IReadOnlyList<SkillProfile> BuiltIn { get; } =
    [
        new() { BuiltIn = true, Name = "Quick coding", SkillIds = ["test-driven-development", "systematic-debugging", "verification-before-completion", "requesting-code-review"] },
        new() { BuiltIn = true, Name = "Deep research", SkillIds = ["web-fetch", "exa-search", "context7", "deepwiki", "microsoft-learn", "pdf-documents"] },
        new() { BuiltIn = true, Name = "Save tokens", SkillIds = ["caveman", "caveman-review", "repomix", "serena"] },
        new() { BuiltIn = true, Name = "Architecture review", SkillIds = ["pdf-documents", "web-fetch", "exa-search", "autodesk-ai-bridge"] },
        new() { BuiltIn = true, Name = "Marketing research", SkillIds = ["web-fetch", "exa-search", "playwright-mcp", "firecrawl", "frontend-design"] },
        new() { BuiltIn = true, Name = "Local only", SkillIds = ["test-driven-development", "systematic-debugging", "writing-plans", "caveman"] },
    ];

    /// <summary>Built-in profiles first, then the user's own (a user profile with the same name replaces the built-in one).</summary>
    public static IReadOnlyList<SkillProfile> All(IEnumerable<SkillProfile> user)
    {
        var mine = user.ToList();
        return BuiltIn.Where(profile => mine.All(own => !own.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))).Concat(mine).ToList();
    }

    /// <summary>Skill id to the agents it is assigned to, from the per-agent settings.</summary>
    public static Dictionary<string, IReadOnlySet<string>> AgentsBySkill(IReadOnlyDictionary<string, List<string>> agentSkills)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (agent, skills) in agentSkills)
            foreach (var skill in skills)
                (result.TryGetValue(skill, out var set) ? set : result[skill] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(agent);
        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value);
    }
}
