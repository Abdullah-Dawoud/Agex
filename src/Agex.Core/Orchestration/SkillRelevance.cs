using System.Text.RegularExpressions;

namespace Agex.Core.Orchestration;

/// <summary>
/// With Skills on Auto, AGEX sends a skill only when it can matter for the
/// request: a PDF skill for PDFs, security skills for security work, web
/// research for questions about the web. Skills the user chose for the request
/// are always sent. Skills AGEX has no rule for are always sent.
/// </summary>
public static class SkillRelevance
{
    private static readonly Dictionary<string, Regex> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf-documents"] = Words(@"pdf|pdfs"),
        ["jupyter-notebook"] = Words(@"jupyter|notebooks?|ipynb"),
        ["security-best-practices"] = Words(@"secur\w*|vulnerab\w*|auth\w*|login|password|token|xss|csrf|injection|encrypt\w*|permissions?"),
        ["security-threat-model"] = Words(@"threat|secur\w*|attack\w*|risk"),
        ["security-ownership-map"] = Words(@"secur\w*|ownership|owners"),
        ["differential-review"] = Words(@"secur\w*|diff|review|pull request|pr"),
        ["semgrep-scan"] = Words(@"semgrep|static analysis|secur\w*|scan"),
        ["codeql-setup"] = Words(@"codeql|code scanning"),
        ["gh-fix-ci"] = Words(@"ci|github actions?|workflow|pipeline|checks?"),
        ["gh-address-comments"] = Words(@"pr|pull request|review comments?|comments"),
        ["vercel-deploy"] = Words(@"vercel|deploy\w*"),
        ["netlify-deploy"] = Words(@"netlify|deploy\w*"),
        ["cloudflare-deploy"] = Words(@"cloudflare|workers?|deploy\w*"),
        ["render-deploy"] = Words(@"render\.com|render|deploy\w*"),
        ["aspnet-core"] = Words(@"asp\.?net|\.net|c#|csharp|blazor|razor"),
        ["winui-app"] = Words(@"winui|windows app|xaml"),
        ["modern-python"] = Words(@"python|pip|uv|poetry|pyproject"),
        ["mcp-builder"] = Words(@"mcp"),
        ["skill-creator"] = Words(@"skills?"),
        ["finishing-a-development-branch"] = Words(@"branch|merge|pull request|pr"),
        ["using-git-worktrees"] = Words(@"worktrees?|branch"),
        ["caveman-commit"] = Words(@"commit\w*"),
        ["frontend-design"] = Words(@"ui|ux|design|css|layout|style|page|website|landing|frontend|polish\w*|beautiful|modern|look"),
        ["webapp-testing"] = Words(@"web ?app|browser|page|ui|e2e|end-to-end|game|localhost"),
        ["playwright-cli"] = Words(@"browser|page|e2e|playwright|game|localhost|click"),
        ["figma-context"] = Words(@"figma|design"),
        ["notion-mcp"] = Words(@"notion"),
        ["linear-mcp"] = Words(@"linear|issues?|tickets?"),
        ["sentry-mcp"] = Words(@"sentry|errors?|crash\w*|exceptions?"),
        ["supabase-mcp"] = Words(@"supabase|database|db|postgres\w*|sql|tables?"),
        ["github-mcp"] = Words(@"github|issues?|pull requests?|prs?|actions"),
    };

    /// <summary>Web research servers: useful only when the request needs the internet.</summary>
    private static readonly HashSet<string> WebResearch = new(StringComparer.OrdinalIgnoreCase) { "web-fetch", "exa-search", "brave-search", "tavily-search", "firecrawl" };

    private static Regex Words(string pattern) => new($@"(?<![\w])(?:{pattern})(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="context">Request text plus attachment names, lower-cased or not.</param>
    public static bool IsRelevant(string skillId, RequestIntent intent, string context)
    {
        if (skillId.Length == 0) return true;
        if (CapabilityRouting.KnownTools.ContainsKey(skillId)) return false; // handed out by capability routing only
        if (WebResearch.Contains(skillId)) return intent.Wants(NeededCapability.ExternalNetwork);
        if (intent.Kind == RequestKind.Chat) return false;
        return !Rules.TryGetValue(skillId, out var rule) || rule.IsMatch(context);
    }
}
