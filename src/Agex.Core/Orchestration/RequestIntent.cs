using System.Text.RegularExpressions;

namespace Agex.Core.Orchestration;

/// <summary>The mode chosen in the composer. Auto lets AGEX classify each request.</summary>
public enum ChatMode { Auto, Ask, Plan, Build }

/// <summary>How AGEX handles a request.</summary>
public enum RequestKind
{
    /// <summary>Greeting or small talk: one short reply, no project context.</summary>
    Chat,
    /// <summary>A question: one read-only answer, reading only the files needed.</summary>
    Question,
    /// <summary>A plan: analysis and a written plan, no changes.</summary>
    Plan,
    /// <summary>Work: the leader plans, the team executes and AGEX verifies.</summary>
    Build,
}

/// <summary>What a request needs from agents and tools.</summary>
[Flags]
public enum NeededCapability
{
    None = 0,
    ReadProject = 1 << 0,
    EditProject = 1 << 1,
    RunShell = 1 << 2,
    Browser = 1 << 3,
    /// <summary>A page or server on this computer (localhost, 127.0.0.1, a local HTML file).</summary>
    LocalWeb = 1 << 4,
    ComputerControl = 1 << 5,
    OutsideFiles = 1 << 6,
    ExternalNetwork = 1 << 7,
    /// <summary>A program such as Revit, AutoCAD or Excel.</summary>
    AppControl = 1 << 8,
    Mcp = 1 << 9,
    ExternalCommunication = 1 << 10,
    Destructive = 1 << 11,
}

/// <summary>AGEX's reading of one request: how to handle it and which capabilities it needs.</summary>
public sealed record RequestIntent(RequestKind Kind, NeededCapability Needs, string Why)
{
    /// <summary>Capabilities that help but are not required (for example computer control when a browser tool can also play a web game).</summary>
    public NeededCapability Prefers { get; init; }
    /// <summary>Sensitive actions the request mentions, in plain words. These always need confirmation.</summary>
    public IReadOnlyList<string> Sensitive { get; init; } = [];
    /// <summary>Web addresses in the request, with where they point.</summary>
    public IReadOnlyList<(Uri Uri, TargetKind Kind)> Targets { get; init; } = [];

    public bool Has(NeededCapability capability) => (Needs & capability) == capability;
    public bool Wants(NeededCapability capability) => Has(capability) || (Prefers & capability) == capability;

    public static string KindLabel(RequestKind kind) => kind switch
    {
        RequestKind.Chat => "Chat",
        RequestKind.Question => "Ask",
        RequestKind.Plan => "Plan",
        _ => "Build",
    };

    public static string CapabilityText(NeededCapability capability) => capability switch
    {
        NeededCapability.ReadProject => "read the project",
        NeededCapability.EditProject => "change project files",
        NeededCapability.RunShell => "run commands",
        NeededCapability.Browser => "use a browser",
        NeededCapability.LocalWeb => "open pages on this computer",
        NeededCapability.ComputerControl => "control the mouse and keyboard",
        NeededCapability.OutsideFiles => "use files outside the project",
        NeededCapability.ExternalNetwork => "use the internet",
        NeededCapability.AppControl => "work with another program",
        NeededCapability.Mcp => "use connected tools",
        NeededCapability.ExternalCommunication => "send messages outside this computer",
        NeededCapability.Destructive => "delete or overwrite data",
        _ => capability.ToString(),
    };

    public static IEnumerable<NeededCapability> Each(NeededCapability flags) =>
        Enum.GetValues<NeededCapability>().Where(value => value != NeededCapability.None && (flags & value) == value);
}

/// <summary>Where a web address points. The user's own computer is treated differently from other private networks.</summary>
public enum TargetKind { Loopback, LocalFile, PrivateNetwork, Public }

public static class LocalTargets
{
    public static TargetKind Classify(Uri uri)
    {
        if (uri.IsFile) return TargetKind.LocalFile;
        var host = uri.IdnHost.Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return TargetKind.Loopback;
        if (System.Net.IPAddress.TryParse(host, out var address))
        {
            if (System.Net.IPAddress.IsLoopback(address)) return TargetKind.Loopback;
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal ? TargetKind.PrivateNetwork : TargetKind.Public;
            var bytes = address.GetAddressBytes();
            var isPrivate = bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 100 && bytes[1] is >= 64 and <= 127 || bytes[0] == 0;
            return isPrivate ? TargetKind.PrivateNetwork : TargetKind.Public;
        }
        // Single-label names (for example "nas" or "router") resolve inside the local network.
        return host.Contains('.') && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            ? TargetKind.Public : TargetKind.PrivateNetwork;
    }
}

/// <summary>
/// Lightweight, rule-based request classification. It decides between a fast
/// reply, a read-only answer, a plan and full team work, and which capabilities
/// the work needs. It never calls a model, so it costs nothing and is instant.
/// </summary>
public static partial class RequestClassifier
{
    [GeneratedRegex(@"^(hi|hii+|hello|hey|heya|hola|salam|salaam|marhaba|yo|sup|good (morning|afternoon|evening|night)|thanks?( you)?( so much| a lot)?|thank u|thx|ty|ok(ay)?|cool|nice|great|awesome|perfect|bye|goodbye|see you|how are (you|u)|who are (you|u)|what are (you|u)|what can (you|u) do|can (you|u) help( me)?|could you help( me)?|help|what model (are|do) (you|u)( using| use)?|which model (are|do) (you|u)( using| use)?|what('s| is) your (model|name))\b[\s!.?,]*(agex|there|again|everyone|all)?[\s!.?,]*$", RegexOptions.IgnoreCase)]
    private static partial Regex SmallTalk();

    [GeneratedRegex(@"^(?:please\s+|pls\s+|now\s+|ok(?:ay)?,?\s+|can you\s+|could you\s+|would you\s+|will you\s+|go ahead and\s+|i want you to\s+|i need you to\s+|let'?s\s+)*(implement|build|create|add|fix|repair|change|update|upgrade|refactor|rewrite|write|make|delete|remove|rename|move|install|deploy|run|test|open|play|launch|start|click|type|fill|migrate|generate|convert|set ?up|configure|improve|optimi[sz]e|clean ?up|redesign|translate|debug|continue|finish|apply|do it|go|proceed|transform|replace|edit|modify|use)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Imperative();

    [GeneratedRegex(@"^(?:please\s+|can you\s+|could you\s+)?(?:make|create|write|draft|give me|prepare|propose|outline)?\s*(?:a\s+|an\s+|the\s+)?(?:step[- ]by[- ]step\s+)?(plan|roadmap|strategy|approach|design proposal)\b|^(?:please\s+)?plan\b|\bhow (should|would|could) (i|we|you) (go about|approach|implement|build|add|structure|design)\b|\b(plan|outline|propose) (how|the steps|an approach)\b|\bwithout (changing|editing|touching) (anything|any files|the code)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlanRequest();

    [GeneratedRegex(@"^(what|why|how|where|which|who|whom|whose|when|is|are|was|were|does|do|did|has|have|should|would|could|can|explain|describe|summari[sz]e|tell me|show me|list|compare|what's|whats|define|walk me through|help me understand|review)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionStart();

    [GeneratedRegex(@"\b(implement|build|create|add|fix|change|update|refactor|rewrite|write|make|delete|remove|rename|install|deploy|migrate|generate|convert|set ?up|improve|redesign|edit|modify|replace)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChangeVerb();

    [GeneratedRegex(@"\b(bugs?|fix(es|ing)?|repair|broken|crash(es)?|error)\b.*\b(fix|repair|solve|correct)\b|\bfix (it|them|this|that|the|any|all|bugs?)\b|\bif (you|u) find (any )?(bugs?|issues?|problems?)", RegexOptions.IgnoreCase)]
    private static partial Regex FixRequest();

    [GeneratedRegex(@"\b(run|test|tests|npm|pnpm|yarn|pip|dotnet|cargo|compile|install|start the server|dev server|serve|execute|script|terminal|command|shell)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShellWords();

    [GeneratedRegex(@"\b(not (working|starting|loading|opening|responding)|doesn'?t (work|start|load|open)|does not (work|start|load|open)|won'?t (work|start|load|open)|can'?t (press|click|start|open|play)|broken|something('s| is)? wrong|there is a bug|crash(es|ing)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BugReport();

    /// <summary>Words that ask for a real browser by themselves.</summary>
    [GeneratedRegex(@"\b(browser|localhost|127\.0\.0\.1|urls?|in (chrome|edge|brave|firefox|safari)|open (it|the (game|app|site|page|website|web ?app))|(play|try|test) (it|the game) (in|on) (the|a) (browser|page)|play (it|the game)|as a (normal )?user|like a user)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BrowserWords();

    /// <summary>Things that live in a browser; they need one only when the request also asks to use them.</summary>
    [GeneratedRegex(@"\b(game|web ?page|website|site|web ?app|page|html|app|ui)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WebThings();

    [GeneratedRegex(@"\b(play|click|press|tap|type into|fill (in|out)|scroll|drag|interact|as a (normal )?user|like a user|use it|try it|try to play|test (it|the ui|the game|the app) (manually|by hand|like a user))\b", RegexOptions.IgnoreCase)]
    private static partial Regex InteractWords();

    [GeneratedRegex(@"\b(mouse|keyboard|my desktop|the desktop|my screen|on screen|the screen|my computer|the computer|taskbar|start menu|control panel|file explorer|notepad|visible apps?|screenshots?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ComputerWords();

    [GeneratedRegex(@"\b(revit|autocad|excel|word document|powerpoint|outlook|sketchup|blender|photoshop|figma|navisworks|bluebeam|notion|slack|teams app)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AppWords();

    [GeneratedRegex(@"\b(local|locally|localhost|127\.0\.0\.1|my (game|app|site|page|project)|the (game|app|site|page)|this (game|app|site|page)|index\.html|dev server|file:///)", RegexOptions.IgnoreCase)]
    private static partial Regex LocalWords();

    [GeneratedRegex(@"\b(search the web|google|online|internet|latest|current version|news|research|documentation|docs for|look up|download)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkWords();

    [GeneratedRegex(@"\b(https?|file)://[^\s""'<>)\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?<![\w/])(?:[A-Za-z]:\\|\\\\|~/|/(?:home|Users|etc|var|opt|mnt)/)[^\s""'<>|]*")]
    private static partial Regex AbsolutePath();

    private static readonly (string Label, NeededCapability Capability, Regex Pattern)[] SensitivePatterns =
    [
        ("Paying or buying something", NeededCapability.ExternalCommunication, new Regex(@"\b(pay|payment|purchase|buy|checkout|check out|order|subscribe|credit card|invoice|transfer (money|funds))\b", RegexOptions.IgnoreCase)),
        ("Sending messages or emails to other people", NeededCapability.ExternalCommunication, new Regex(@"\b(send|reply|forward|post|tweet|dm|message|email|e-mail|text)\b.{0,40}\b(email|e-mail|mail|message|to (him|her|them|my|the|[A-Z][a-z]+)|slack|teams|whatsapp|linkedin|twitter|x\.com|customers?|clients?|everyone)\b", RegexOptions.IgnoreCase)),
        ("Deleting a lot of data", NeededCapability.Destructive, new Regex(@"\b(delete|remove|wipe|erase|drop|purge|destroy)\b.{0,30}\b(all|every|everything|database|table|folder|directory|repo(sitory)?|branch|history|backups?|account)\b|\brm -rf\b|\bdrop table\b|\bformat (the )?(disk|drive)\b", RegexOptions.IgnoreCase)),
        ("Changing accounts or security settings", NeededCapability.Destructive, new Regex(@"\b(change|reset|disable|turn off)\b.{0,30}\b(password|2fa|two-factor|mfa|security settings?|account settings?|firewall|antivirus|permissions)\b", RegexOptions.IgnoreCase)),
        ("Publishing or submitting something that cannot be taken back", NeededCapability.ExternalCommunication, new Regex(@"\b(publish|release|deploy to prod(uction)?|go live|submit (the )?(form|application|order|pull request)|push to (main|master|prod)|merge (it|the pr|to main))\b", RegexOptions.IgnoreCase)),
        ("Changing secrets or credentials", NeededCapability.Destructive, new Regex(@"\b(api key|secret|token|credentials?|private key|\.env)\b.{0,30}\b(change|rotate|replace|update|set|add|remove|delete|commit)\b|\b(change|rotate|replace|update|set|add|remove|delete|commit)\b.{0,30}\b(api key|secret|token|credentials?|private key|\.env)\b", RegexOptions.IgnoreCase)),
    ];

    /// <summary>Classifies a request for the given composer mode.</summary>
    /// <param name="project">The active project, used to tell project paths from paths outside it.</param>
    public static RequestIntent Classify(string request, ChatMode mode = ChatMode.Auto, string? project = null, bool hasAttachments = false)
    {
        var text = (request ?? "").Trim();
        var needs = Capabilities(text, project, out var prefers, out var targets);
        var sensitive = SensitivePatterns.Where(item => item.Pattern.IsMatch(text)).Select(item => item.Label).ToList();
        foreach (var item in SensitivePatterns.Where(item => item.Pattern.IsMatch(text))) needs |= item.Capability;
        var smallTalk = !hasAttachments && text.Length <= 80 && SmallTalk().IsMatch(text);

        RequestKind kind;
        string why;
        switch (mode)
        {
            case ChatMode.Ask:
                kind = smallTalk ? RequestKind.Chat : RequestKind.Question;
                why = "Ask mode: answer only, no changes.";
                needs &= ~(NeededCapability.EditProject | NeededCapability.Destructive | NeededCapability.ExternalCommunication);
                break;
            case ChatMode.Plan:
                kind = RequestKind.Plan;
                why = "Plan mode: analyse and write a plan, no changes.";
                needs &= ~(NeededCapability.EditProject | NeededCapability.Destructive | NeededCapability.ExternalCommunication | NeededCapability.ComputerControl);
                break;
            case ChatMode.Build:
                kind = RequestKind.Build;
                why = "Build mode.";
                break;
            default:
                (kind, why) = Auto(text, smallTalk, needs, hasAttachments);
                break;
        }
        if (kind is RequestKind.Question or RequestKind.Plan or RequestKind.Build) needs |= NeededCapability.ReadProject;
        if (kind == RequestKind.Chat) needs = NeededCapability.None;
        if (kind != RequestKind.Build) needs &= ~(NeededCapability.EditProject | NeededCapability.RunShell | NeededCapability.ComputerControl | NeededCapability.Destructive | NeededCapability.ExternalCommunication);
        return new RequestIntent(kind, needs, why)
        {
            Prefers = kind == RequestKind.Build ? prefers & ~needs : NeededCapability.None,
            Sensitive = kind == RequestKind.Build ? sensitive : [],
            Targets = targets,
        };
    }

    private static (RequestKind, string) Auto(string text, bool smallTalk, NeededCapability needs, bool hasAttachments)
    {
        if (text.Length == 0) return (RequestKind.Chat, "Empty request.");
        if (smallTalk) return (RequestKind.Chat, "Greeting or small talk.");
        if (PlanRequest().IsMatch(text)) return (RequestKind.Plan, "Asks for a plan.");
        var acts = (needs & (NeededCapability.EditProject | NeededCapability.RunShell | NeededCapability.ComputerControl | NeededCapability.Destructive | NeededCapability.ExternalCommunication)) != 0
            || (needs & NeededCapability.Browser) != 0 && InteractWords().IsMatch(text);
        if (Imperative().IsMatch(text) && acts) return (RequestKind.Build, "Asks for work to be done.");
        if (QuestionStart().IsMatch(text) || text.EndsWith('?'))
        {
            // "Can you fix the login?" is a request for work phrased as a question.
            var polite = Regex.IsMatch(text, @"^(can|could|would|will) (you|u)\b", RegexOptions.IgnoreCase);
            var fix = FixRequest().IsMatch(text) || BugReport().IsMatch(text);
            return acts && (polite || fix) ? (RequestKind.Build, "Asks for work to be done.") : (RequestKind.Question, "A question: answered read-only.");
        }
        if (acts || Imperative().IsMatch(text)) return (RequestKind.Build, "Asks for work to be done.");
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words <= 3 && !hasAttachments) return (RequestKind.Chat, "Short message.");
        return (RequestKind.Question, "No change requested: answered read-only.");
    }

    private static NeededCapability Capabilities(string text, string? project, out NeededCapability prefers, out List<(Uri, TargetKind)> targets)
    {
        var needs = NeededCapability.None;
        prefers = NeededCapability.None;
        targets = [];
        if (ChangeVerb().IsMatch(text) || FixRequest().IsMatch(text) || BugReport().IsMatch(text)) needs |= NeededCapability.EditProject;
        if (ShellWords().IsMatch(text)) needs |= NeededCapability.RunShell;
        var browser = BrowserWords().IsMatch(text);
        var interact = InteractWords().IsMatch(text);
        foreach (Match match in UrlPattern().Matches(text))
        {
            if (!Uri.TryCreate(match.Value.TrimEnd('.', ',', ';'), UriKind.Absolute, out var uri)) continue;
            var kind = LocalTargets.Classify(uri);
            targets.Add((uri, kind));
            browser = true;
            needs |= kind switch
            {
                TargetKind.Loopback or TargetKind.LocalFile => NeededCapability.LocalWeb,
                _ => NeededCapability.ExternalNetwork,
            };
        }
        if (browser || interact && WebThings().IsMatch(text))
        {
            needs |= NeededCapability.Browser;
            if (LocalWords().IsMatch(text) || targets.Count == 0 && !NetworkWords().IsMatch(text)) needs |= NeededCapability.LocalWeb;
        }
        if (ComputerWords().IsMatch(text))
        {
            // Mouse and keyboard in a web page work through a browser tool; the real computer tool is preferred when available.
            if ((needs & NeededCapability.Browser) != 0) prefers |= NeededCapability.ComputerControl;
            else needs |= NeededCapability.ComputerControl;
        }
        else if (interact && (needs & NeededCapability.Browser) == 0) prefers |= NeededCapability.ComputerControl;
        if (AppWords().IsMatch(text)) needs |= NeededCapability.AppControl | NeededCapability.Mcp;
        if (NetworkWords().IsMatch(text)) needs |= NeededCapability.ExternalNetwork;
        if ((needs & NeededCapability.LocalWeb) != 0 && (needs & NeededCapability.EditProject) == 0 && Regex.IsMatch(text, @"\b(start|run|serve)\b", RegexOptions.IgnoreCase)) needs |= NeededCapability.RunShell;
        foreach (Match match in AbsolutePath().Matches(text))
        {
            var path = match.Value.TrimEnd('.', ',', ';', ')');
            if (project is null || !IsInside(project, path)) needs |= NeededCapability.OutsideFiles;
        }
        return needs;
    }

    private static bool IsInside(string project, string path)
    {
        try
        {
            if (path.StartsWith("~/", StringComparison.Ordinal)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
            var root = Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return (full + Path.DirectorySeparatorChar).StartsWith(root, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
