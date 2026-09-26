using Agex.Core.Agents;
using Agex.Core.Platform;
using Agex.Core.Skills;
using Agex.Core.Teams;

namespace Agex.Core.Connections;

/// <summary>How AGEX can work with a tool. Never claims more control than it has.</summary>
public enum ConnectionMethod { Direct, Mcp, Cli, OpenProjectOnly, FilesOnly, NotControllable }

public enum ConnectionState { Connected, AvailableToConnect, InstalledNotConnected, NotInstalled, SignInRequired, DependencyMissing, Unsupported }

/// <summary>The next step a card offers. The desktop app turns each kind into a button.</summary>
public enum ConnectionActionKind
{
    Connect, Download, SignIn, AddKey, OpenProject, UseAsEditor, LearnMore, Configure, Test, Disconnect, Enable,
    SearchRegistry, OpenAgents, UseTeam, InstallDependency, ConnectBridge,
}

public sealed record ConnectionAction(ConnectionActionKind Kind, string Label, string Argument = "");

public sealed record ConnectionItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public ConnectionMethod Method { get; init; }
    public ConnectionState State { get; init; }
    /// <summary>One sentence: what the connection does, or what is missing.</summary>
    public string Detail { get; init; } = "";
    public string Cost { get; init; } = "";
    public string FreeAlternative { get; init; } = "";
    public IReadOnlyList<ConnectionAction> Actions { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];

    public static string StateText(ConnectionState state) => state switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.AvailableToConnect => "Available to connect",
        ConnectionState.InstalledNotConnected => "Installed, not connected",
        ConnectionState.NotInstalled => "Not installed",
        ConnectionState.SignInRequired => "Sign-in required",
        ConnectionState.DependencyMissing => "Needs another program",
        _ => "Not supported yet",
    };

    public static string MethodText(ConnectionMethod method) => method switch
    {
        ConnectionMethod.Direct => "Direct integration",
        ConnectionMethod.Mcp => "MCP integration",
        ConnectionMethod.Cli => "Command-line integration",
        ConnectionMethod.OpenProjectOnly => "Opens your project (no control)",
        ConnectionMethod.FilesOnly => "Works with its files (no live control)",
        _ => "No reviewed connection yet",
    };
}

/// <summary>A tool, program or service AGEX knows how to connect (or knows it cannot).</summary>
public sealed record ConnectionDefinition(
    string Id, string Name, string Category, ConnectionMethod Method, string How,
    string ProgramId = "", string EditorId = "", string[]? SkillIds = null, string Homepage = "", string FreeAlternative = "",
    string RegistryQuery = "", string[]? Keywords = null, string Cost = "");

public static class ConnectionCatalog
{
    /// <summary>
    /// Curated list. Connections through catalog skills use AGEX-reviewed MCP
    /// servers; services without an official or reviewed server say so and offer
    /// a search of the public MCP Registry instead of pretending.
    /// </summary>
    public static IReadOnlyList<ConnectionDefinition> All { get; } =
    [
        // Architecture & engineering
        new("revit", "Autodesk Revit", "Architecture & engineering", ConnectionMethod.Mcp, "Agents read and edit models through the Autodesk AI Bridge, after you confirm each edit.", ProgramId: "revit", SkillIds: [AutodeskBridge.SkillId], Homepage: "https://www.autodesk.com/products/revit/", Keywords: ["bim", "model"], Cost: "Paid (Autodesk)"),
        new("autocad", "Autodesk AutoCAD", "Architecture & engineering", ConnectionMethod.Mcp, "Agents work with drawings through the Autodesk AI Bridge, or write AutoLISP and scripts you run.", ProgramId: "autocad", SkillIds: [AutodeskBridge.SkillId], Homepage: "https://www.autodesk.com/products/autocad/", Keywords: ["cad", "dwg"], Cost: "Paid (Autodesk)"),
        new("navisworks", "Autodesk Navisworks", "Architecture & engineering", ConnectionMethod.FilesOnly, "No direct control. Export clash reports (XML, HTML or CSV) and attach them: agents summarise, group and assign the issues.", ProgramId: "navisworks", Homepage: "https://www.autodesk.com/products/navisworks/", Keywords: ["clash", "coordination", "bim"], Cost: "Paid (Autodesk)"),
        new("bluebeam", "Bluebeam Revu", "Architecture & engineering", ConnectionMethod.FilesOnly, "No direct control. Agents read and create the PDFs you mark up in Revu, and turn markup summaries (CSV) into issue lists.", ProgramId: "bluebeam", SkillIds: ["pdf-documents"], Homepage: "https://www.bluebeam.com/", FreeAlternative: "PDF Documents skill", Keywords: ["pdf", "markup", "drawings"], Cost: "Paid"),
        new("sketchup", "Trimble SketchUp", "Design & 3D", ConnectionMethod.NotControllable, "No reviewed connection yet. Agents can write SketchUp Ruby scripts that you run in SketchUp.", ProgramId: "sketchup", Homepage: "https://www.sketchup.com/", RegistryQuery: "sketchup", Keywords: ["3d", "massing"], Cost: "Paid"),
        new("blender", "Blender", "Design & 3D", ConnectionMethod.NotControllable, "No reviewed connection yet. Agents can write Blender Python scripts that you run in Blender.", ProgramId: "blender", Homepage: "https://www.blender.org/", RegistryQuery: "blender", Keywords: ["3d", "render"], Cost: "Free (open source)"),
        new("figma", "Figma", "Design & 3D", ConnectionMethod.Mcp, "Agents read your Figma designs (layout and styles) with a Figma access token. Read only.", ProgramId: "figma", SkillIds: ["figma-context"], Homepage: "https://www.figma.com/", Keywords: ["design", "ui"], Cost: "Free tier"),
        // Browsers
        new("chrome", "Google Chrome", "Browsers", ConnectionMethod.Mcp, "Agents browse, click and fill forms in a separate browser window (Playwright), and inspect pages (Chrome DevTools).", ProgramId: "chrome", SkillIds: ["playwright-mcp", "chrome-devtools-mcp"], Homepage: "https://www.google.com/chrome/", Keywords: ["browser", "web", "forms"], Cost: "Free"),
        new("edge", "Microsoft Edge", "Browsers", ConnectionMethod.Mcp, "Agents browse and fill forms in a separate browser window through Playwright.", ProgramId: "edge", SkillIds: ["playwright-mcp"], Homepage: "https://www.microsoft.com/edge", Keywords: ["browser", "web"], Cost: "Free"),
        // Office & documents
        new("pdf", "PDF documents", "Office & documents", ConnectionMethod.FilesOnly, "Agents read, fill and create PDF files (drawings, reports, forms).", SkillIds: ["pdf-documents"], Keywords: ["pdf", "drawing", "report"], Cost: "Free"),
        new("word", "Microsoft Word", "Office & documents", ConnectionMethod.FilesOnly, "Agents read and write .docx files; you open them in Word. AGEX does not control Word.", ProgramId: "word", Homepage: "https://www.microsoft.com/microsoft-365/word", FreeAlternative: "LibreOffice Writer", Keywords: ["docx", "documents"], Cost: "Paid (Microsoft 365)"),
        new("excel", "Microsoft Excel", "Office & documents", ConnectionMethod.FilesOnly, "Agents read and write .xlsx and .csv files (schedules, quantities, reports). AGEX does not control Excel.", ProgramId: "excel", Homepage: "https://www.microsoft.com/microsoft-365/excel", FreeAlternative: "LibreOffice Calc", Keywords: ["xlsx", "spreadsheet", "schedule"], Cost: "Paid (Microsoft 365)"),
        new("powerpoint", "Microsoft PowerPoint", "Office & documents", ConnectionMethod.FilesOnly, "Agents read and write .pptx files. AGEX does not control PowerPoint.", ProgramId: "powerpoint", Homepage: "https://www.microsoft.com/microsoft-365/powerpoint", FreeAlternative: "LibreOffice Impress", Keywords: ["pptx", "slides"], Cost: "Paid (Microsoft 365)"),
        new("libreoffice", "LibreOffice", "Office & documents", ConnectionMethod.FilesOnly, "Free office suite that opens the documents, spreadsheets and slides agents create.", ProgramId: "libreoffice", Homepage: "https://www.libreoffice.org/", Keywords: ["office", "docx", "xlsx"], Cost: "Free (open source)"),
        new("obsidian", "Obsidian", "Office & documents", ConnectionMethod.OpenProjectOnly, "An Obsidian vault is a folder of Markdown notes: open it as a project and agents work on the notes.", ProgramId: "obsidian", Homepage: "https://obsidian.md/", Keywords: ["notes", "markdown", "vault"], Cost: "Free"),
        new("notion", "Notion", "Web services", ConnectionMethod.Mcp, "Agents read and update the Notion pages you share with an integration.", SkillIds: ["notion-mcp"], Homepage: "https://www.notion.so/", Keywords: ["notes", "wiki", "docs"], Cost: "Free tier"),
        // Developer tools
        new("git", "Git", "Developer tools", ConnectionMethod.Cli, "Snapshots and undo for every request that changes files; agents use it for commits and diffs.", ProgramId: "git", Homepage: "https://git-scm.com/downloads", Keywords: ["version control"], Cost: "Free (open source)"),
        new("gh", "GitHub CLI", "Developer tools", ConnectionMethod.Cli, "Agents open pull requests, read issues and check CI with your GitHub sign-in.", ProgramId: "gh", Homepage: "https://cli.github.com/", Keywords: ["github", "pull request"], Cost: "Free"),
        new("github", "GitHub", "Web services", ConnectionMethod.Mcp, "Issues, pull requests, reviews and Actions through GitHub's official MCP server.", SkillIds: ["github-mcp"], Homepage: "https://github.com/github/github-mcp-server", Keywords: ["git", "issues", "pr"], Cost: "Free"),
        new("docker", "Docker", "Developer tools", ConnectionMethod.Cli, "Agents build and run containers with the docker command when commands are allowed.", ProgramId: "docker", Homepage: "https://docs.docker.com/get-started/get-docker/", Keywords: ["containers"], Cost: "Free for personal use"),
        new("node", "Node.js", "Developer tools", ConnectionMethod.Cli, "Needed by many MCP tools (npx) and JavaScript projects.", ProgramId: "node", Homepage: "https://nodejs.org/en/download", Cost: "Free (open source)"),
        new("uv", "uv (Python tools)", "Developer tools", ConnectionMethod.Cli, "Needed by MCP tools published for Python (uvx).", ProgramId: "uv", Homepage: "https://docs.astral.sh/uv/", Cost: "Free (open source)"),
        new("python", "Python", "Developer tools", ConnectionMethod.Cli, "Agents run Python for data analysis and scripts.", ProgramId: "python", Homepage: "https://www.python.org/downloads/", Cost: "Free (open source)"),
        new("ffmpeg", "ffmpeg", "Developer tools", ConnectionMethod.Cli, "Lets AGEX take still frames from attached videos.", ProgramId: "ffmpeg", Homepage: "https://ffmpeg.org/download.html", Cost: "Free (open source)"),
        // Web search and research
        new("web-search", "Web search", "Search & research", ConnectionMethod.Mcp, "Agents search the web and read pages. Exa and Fetch need no account.", SkillIds: ["exa-search", "web-fetch", "brave-search", "tavily-search"], Keywords: ["search", "research", "seo"], Cost: "Free"),
        new("docs", "Developer documentation", "Search & research", ConnectionMethod.Mcp, "Current library and platform docs (Context7, Microsoft Learn, AWS, Cloudflare, DeepWiki).", SkillIds: ["context7", "microsoft-learn", "aws-docs", "cloudflare-docs", "deepwiki"], Keywords: ["documentation"], Cost: "Free"),
        new("firecrawl", "Firecrawl", "Search & research", ConnectionMethod.Mcp, "Turns whole websites into clean text for research and SEO audits.", SkillIds: ["firecrawl"], Homepage: "https://www.firecrawl.dev/", FreeAlternative: "Web search (Fetch)", Keywords: ["scrape", "seo"], Cost: "Free tier"),
        // Work tools
        new("linear", "Linear", "Web services", ConnectionMethod.Mcp, "Find, create and update issues and projects.", SkillIds: ["linear-mcp"], Homepage: "https://linear.app/", Keywords: ["issues", "tickets"], Cost: "Free tier"),
        new("sentry", "Sentry", "Web services", ConnectionMethod.Mcp, "Production errors and traces for debugging.", SkillIds: ["sentry-mcp"], Homepage: "https://sentry.io/", Keywords: ["errors", "monitoring"], Cost: "Free tier"),
        new("supabase", "Supabase", "Databases & cloud", ConnectionMethod.Mcp, "Inspect your Supabase databases (read-only).", SkillIds: ["supabase-mcp"], Homepage: "https://supabase.com/", Keywords: ["database", "postgres", "sql"], Cost: "Free tier"),
        new("vercel", "Vercel", "Databases & cloud", ConnectionMethod.Cli, "Deploy web apps with the Vercel command line and your Vercel sign-in.", SkillIds: ["vercel-deploy"], Homepage: "https://vercel.com/", Keywords: ["deploy", "hosting"], Cost: "Free tier"),
        new("netlify", "Netlify", "Databases & cloud", ConnectionMethod.Cli, "Deploy web apps with the Netlify command line and your Netlify sign-in.", SkillIds: ["netlify-deploy"], Homepage: "https://www.netlify.com/", Keywords: ["deploy", "hosting"], Cost: "Free tier"),
        new("desktop-control", "Windows desktop control", "Automation", ConnectionMethod.Mcp, "Agents see the screen and click and type in Windows programs (Windows-MCP). High risk: asks before every use.", SkillIds: ["windows-mcp"], Keywords: ["computer use", "automation", "rpa"], Cost: "Free (open source)"),
        new("local-model", "Local models (Ollama)", "Local models", ConnectionMethod.Direct, "Private models on this computer: free, no account, nothing leaves the computer.", Homepage: "https://ollama.com/download", Keywords: ["private", "offline", "free"], Cost: "Free (local)"),
        // Services without an official or reviewed MCP server (honest: registry search only)
        new("slack", "Slack", "Web services", ConnectionMethod.NotControllable, "Slack has no official MCP server that AGEX has reviewed. Community servers are listed in the public MCP Registry.", RegistryQuery: "slack", Homepage: "https://slack.com/", Keywords: ["chat", "messages"]),
        new("google-drive", "Google Drive", "Web services", ConnectionMethod.NotControllable, "No official Google Drive MCP server yet. Community servers need your own Google Cloud setup. Sync the folder to this computer and open it as a project instead.", RegistryQuery: "google drive", Homepage: "https://www.google.com/drive/download/", FreeAlternative: "Google Drive for desktop (sync a folder, then open it as a project)", Keywords: ["files", "docs", "sheets"]),
        new("google-calendar", "Google Calendar", "Web services", ConnectionMethod.NotControllable, "No official Google Calendar MCP server yet. Community servers are in the public MCP Registry.", RegistryQuery: "google calendar", Keywords: ["calendar", "meetings"]),
        new("gmail", "Gmail / email", "Web services", ConnectionMethod.NotControllable, "No official email MCP server yet. Sending email always needs your approval; community servers are in the public MCP Registry.", RegistryQuery: "gmail", Keywords: ["email", "mail"]),
        new("google-analytics", "Google Analytics", "Web services", ConnectionMethod.NotControllable, "Google's official Analytics MCP server is read-only but needs your own Google Cloud OAuth client and the gcloud tool, so AGEX does not set it up automatically.", Homepage: "https://github.com/googleanalytics/google-analytics-mcp", FreeAlternative: "Export a report as CSV and attach it", Keywords: ["analytics", "traffic", "marketing"]),
    ];

    /// <summary>Connections that matter for each job team, most important first.</summary>
    public static IReadOnlyDictionary<string, string[]> ForTeam { get; } = new Dictionary<string, string[]>
    {
        ["software-builder"] = ["git", "github", "gh", "chrome", "docs", "docker", "node", "local-model"],
        ["research-lab"] = ["web-search", "pdf", "chrome", "docs", "notion", "firecrawl"],
        ["architecture-bim"] = ["revit", "autocad", "pdf", "excel", "web-search", "chrome", "navisworks", "bluebeam", "sketchup", "libreoffice"],
        ["marketing-growth"] = ["web-search", "chrome", "firecrawl", "notion", "figma", "google-analytics", "excel"],
        ["computer-operator"] = ["chrome", "edge", "desktop-control", "pdf", "gmail"],
        ["job-search"] = ["chrome", "web-search", "pdf", "word", "gmail"],
        ["document-office"] = ["pdf", "word", "excel", "powerpoint", "libreoffice", "notion", "obsidian"],
        ["data-analyst"] = ["python", "excel", "supabase", "pdf", "local-model"],
        ["security-review"] = ["git", "github", "gh", "docs"],
        ["devops-release"] = ["git", "gh", "github", "docker", "vercel", "netlify", "sentry"],
        ["local-private"] = ["local-model", "obsidian", "git"],
    };

    public static ConnectionDefinition? Get(string id) => All.FirstOrDefault(item => item.Id == id);
}

/// <summary>Builds the current state of every connection, with the next action for each.</summary>
public sealed class ConnectionService(IPlatformService platform, SkillManager skills, AgentRegistry registry, Func<IReadOnlyList<string>> enabledAgents, Func<string> preferredEditor)
{
    private readonly ProgramDetector _programs = new(platform);

    public McpConfigScanner Scanner { get; } = new(platform);

    /// <summary>
    /// All connections, with the active team's recommended ones first.
    /// <paramref name="editorLocations"/> maps discovered editor ids to their path (from the scan).
    /// <paramref name="ghSignedIn"/> is the result of "gh auth status" when known.
    /// </summary>
    public IReadOnlyList<ConnectionItem> Build(IReadOnlyDictionary<string, string> editorLocations, bool? ghSignedIn = null, bool ollamaReady = false)
    {
        var catalog = skills.Catalog().Skills.ToDictionary(skill => skill.Id);
        var installed = skills.Installed().ToDictionary(skill => skill.Id);
        var agents = enabledAgents();
        var items = new List<ConnectionItem>();
        foreach (var definition in ConnectionCatalog.All)
            items.Add(Describe(definition, catalog, installed, agents, ghSignedIn, ollamaReady));
        foreach (var (id, path) in editorLocations)
            items.Add(Editor(id, path));
        // Custom and imported MCP servers the user added.
        var covered = ConnectionCatalog.All.SelectMany(item => item.SkillIds ?? []).ToHashSet();
        foreach (var skill in installed.Values.Where(skill => skill.Manifest.Kind == SkillKind.Mcp && !covered.Contains(skill.Id)))
            items.Add(FromSkill(skill.Id, skill.Manifest.Name, "Your MCP servers", skill.Manifest.Description, skill.Manifest, skill, agents, "", []));
        return items;
    }

    /// <summary>The team's recommended connections, in the team's order.</summary>
    public static IReadOnlyList<ConnectionItem> ForTeam(IReadOnlyList<ConnectionItem> all, string? teamId)
    {
        if (teamId is null || !ConnectionCatalog.ForTeam.TryGetValue(teamId, out var ids)) return [];
        return ids.Select(id => all.FirstOrDefault(item => item.Id == id)).OfType<ConnectionItem>().ToList();
    }

    private ConnectionItem Describe(ConnectionDefinition definition, Dictionary<string, SkillManifest> catalog, Dictionary<string, InstalledSkill> installed, IReadOnlyList<string> agents, bool? ghSignedIn, bool ollamaReady)
    {
        var baseItem = new ConnectionItem
        {
            Id = definition.Id, Name = definition.Name, Category = definition.Category, Method = definition.Method, Detail = definition.How,
            Cost = definition.Cost, FreeAlternative = definition.FreeAlternative, Keywords = definition.Keywords ?? [],
        };
        if (definition.Id == "local-model")
            return baseItem with
            {
                State = ollamaReady ? ConnectionState.Connected : registry.LastDetection("ollama")?.Status is AgentStatus.Supported or AgentStatus.Available ? ConnectionState.InstalledNotConnected : ConnectionState.NotInstalled,
                Actions = ollamaReady ? [new(ConnectionActionKind.OpenAgents, "Choose models")] : registry.LastDetection("ollama")?.Status is AgentStatus.Supported or AgentStatus.Available
                    ? [new(ConnectionActionKind.OpenAgents, "Turn on in Agents")] : [new(ConnectionActionKind.Download, "Get Ollama", definition.Homepage)],
            };

        string? programPath = null;
        if (definition.ProgramId.Length > 0)
        {
            if (definition.ProgramId == "revit" && platform.Os != OsKind.Windows)
                return baseItem with { State = ConnectionState.Unsupported, Detail = "Revit runs on Windows only.", Actions = [new(ConnectionActionKind.LearnMore, "Learn more", definition.Homepage)] };
            programPath = _programs.Find(definition.ProgramId).Path;
            if (programPath is null)
            {
                var download = ProgramDetector.Describe(definition.ProgramId).Url;
                var actions = new List<ConnectionAction> { new(ConnectionActionKind.Download, "Official download", download.Length > 0 ? download : definition.Homepage) };
                if (definition.FreeAlternative.Length > 0 && definition.FreeAlternative.StartsWith("LibreOffice", StringComparison.Ordinal))
                    actions.Add(new(ConnectionActionKind.Download, "Get " + definition.FreeAlternative + " (free)", ProgramDetector.Describe("libreoffice").Url));
                return baseItem with { State = ConnectionState.NotInstalled, Detail = "Not found on this computer. " + definition.How, Actions = actions };
            }
        }

        switch (definition.Method)
        {
            case ConnectionMethod.Mcp when definition.SkillIds is { Length: > 0 } ids && ids[0] == AutodeskBridge.SkillId:
            {
                if (installed.ContainsKey(AutodeskBridge.SkillId))
                    return baseItem with { State = ConnectionState.Connected, Detail = "Connected through the Autodesk AI Bridge. Open the program with the bridge plug-in loaded before asking agents to use it.", Actions = [new(ConnectionActionKind.Disconnect, "Disconnect", AutodeskBridge.SkillId)] };
                return AutodeskBridge.HostPath() is null
                    ? baseItem with { State = ConnectionState.DependencyMissing, Detail = "Installed. To let agents work with it, install the Autodesk AI Bridge (not installed).", Actions = [new(ConnectionActionKind.LearnMore, "Connect step by step", "bridge")] }
                    : baseItem with { State = ConnectionState.AvailableToConnect, Detail = "The Autodesk AI Bridge is installed. Connect it so agents can use " + definition.Name + ".", Actions = [new(ConnectionActionKind.ConnectBridge, "Connect")] };
            }
            case ConnectionMethod.Mcp or ConnectionMethod.FilesOnly or ConnectionMethod.Cli when definition.SkillIds is { Length: > 0 } ids:
            {
                // The first skill that is ready wins; otherwise the first one that can be installed.
                var states = ids.Where(catalog.ContainsKey).Select(id => (Id: id, Manifest: catalog[id], Installed: installed.GetValueOrDefault(id))).ToList();
                if (states.Count == 0) return baseItem with { State = ConnectionState.Unsupported, Detail = "Not available in this AGEX version's catalog." };
                var ready = states.FirstOrDefault(state => state.Installed is not null && skills.State(state.Manifest, state.Installed, agents).Readiness == SkillReadiness.Ready);
                // Free-first: an option that needs no account comes before one that asks for a key.
                var noAccount = states.FirstOrDefault(state => !state.Manifest.RequiresAccount && skills.State(state.Manifest, state.Installed, agents).Readiness is SkillReadiness.NotInstalled or SkillReadiness.Disabled or SkillReadiness.DependencyMissing);
                var pick = ready.Manifest is not null ? ready
                    : noAccount.Manifest is not null ? noAccount
                    : states.FirstOrDefault(state => state.Installed is not null) is { Manifest: not null } some ? some : states[0];
                var item = FromSkill(definition.Id, definition.Name, definition.Category, definition.How, pick.Manifest, pick.Installed, agents, definition.FreeAlternative, definition.Keywords ?? []);
                return item with { Method = definition.Method, Cost = definition.Cost };
            }
            case ConnectionMethod.FilesOnly:
                return baseItem with
                {
                    State = ConnectionState.Connected,
                    Actions = [new(ConnectionActionKind.UseTeam, "Use Document Office team", "document-office")],
                };
            case ConnectionMethod.OpenProjectOnly:
                return baseItem with { State = ConnectionState.Connected, Actions = [new(ConnectionActionKind.OpenProject, "Open a folder as project")] };
            case ConnectionMethod.Cli when definition.Id == "gh":
                return ghSignedIn == false
                    ? baseItem with { State = ConnectionState.SignInRequired, Detail = "Installed. Sign in with GitHub so agents can use it.", Actions = [new(ConnectionActionKind.SignIn, "Sign in", "gh")] }
                    : baseItem with { State = ConnectionState.Connected, Detail = ghSignedIn == true ? "Signed in. " + definition.How : definition.How, Actions = [new(ConnectionActionKind.Test, "Check sign-in", "gh")] };
            case ConnectionMethod.Cli:
                return baseItem with { State = ConnectionState.Connected, Detail = "Found on this computer. " + definition.How };
            default:
            {
                var actions = new List<ConnectionAction>();
                if (definition.RegistryQuery.Length > 0) actions.Add(new(ConnectionActionKind.SearchRegistry, "Find community servers", definition.RegistryQuery));
                if (definition.Homepage.Length > 0) actions.Add(new(ConnectionActionKind.LearnMore, "Learn more", definition.Homepage));
                // No reviewed way to connect: say so, and still offer the next step (community servers, learn more).
                return baseItem with { State = programPath is null ? ConnectionState.Unsupported : ConnectionState.InstalledNotConnected, Actions = actions };
            }
        }
    }

    private ConnectionItem FromSkill(string id, string name, string category, string how, SkillManifest manifest, InstalledSkill? installed, IReadOnlyList<string> agents, string free, IReadOnlyList<string> keywords)
    {
        var state = skills.State(manifest, installed, agents);
        var item = new ConnectionItem
        {
            Id = id, Name = name, Category = category, Method = ConnectionMethod.Mcp, Detail = how, FreeAlternative = free, Keywords = keywords,
            Cost = SkillCosts.Label(SkillCosts.Of(manifest)),
        };
        return state.Readiness switch
        {
            SkillReadiness.Ready => item with
            {
                State = ConnectionState.Connected, Detail = $"Connected with {manifest.Name}. {how}",
                Actions = manifest.Auth?.Test is not null && installed is not null
                    ? [new(ConnectionActionKind.Test, "Test", manifest.Id), new(ConnectionActionKind.Configure, "Settings", manifest.Id), new(ConnectionActionKind.Disconnect, "Disconnect", manifest.Id)]
                    : [new(ConnectionActionKind.Configure, "Settings", manifest.Id), new(ConnectionActionKind.Disconnect, "Disconnect", manifest.Id)],
            },
            SkillReadiness.AccountRequired => item with
            {
                State = ConnectionState.SignInRequired, Detail = state.Detail,
                Actions = [manifest.Auth?.Type == SkillAuthType.CliLogin ? new(ConnectionActionKind.Configure, "Sign in", manifest.Id) : new(ConnectionActionKind.AddKey, manifest.Auth?.Label is { Length: > 0 } label ? "Add " + label : "Add key", manifest.Id)],
            },
            SkillReadiness.DependencyMissing => item with
            {
                State = ConnectionState.DependencyMissing, Detail = state.Detail,
                Actions = state.MissingTools.Select(tool => new ConnectionAction(ConnectionActionKind.InstallDependency, "Get " + tool.Label, tool.InstallUrl)).Take(2).ToList(),
            },
            SkillReadiness.PlatformUnsupported => item with { State = ConnectionState.Unsupported, Detail = state.Detail, Actions = manifest.Homepage.Length > 0 ? [new(ConnectionActionKind.LearnMore, "Learn more", manifest.Homepage)] : [] },
            SkillReadiness.AgentIncompatible => item with { State = ConnectionState.InstalledNotConnected, Detail = state.Detail, Actions = [new(ConnectionActionKind.OpenAgents, "Enable a compatible agent")] },
            SkillReadiness.Disabled => item with { State = ConnectionState.InstalledNotConnected, Detail = "Installed but switched off.", Actions = [new(ConnectionActionKind.Enable, "Turn on", manifest.Id)] },
            SkillReadiness.Broken => item with { State = ConnectionState.DependencyMissing, Detail = state.Detail, Actions = [new(ConnectionActionKind.Connect, "Repair", manifest.Id)] },
            _ => item with { State = ConnectionState.AvailableToConnect, Detail = how + (manifest.RequiresAccount ? " Needs an account." : ""), Actions = [new(ConnectionActionKind.Connect, "Connect", manifest.Id)] },
        };
    }

    private ConnectionItem Editor(string id, string path)
    {
        var preferred = preferredEditor() == id;
        var name = id switch
        {
            "vscode" => "Visual Studio Code", "cursor" => "Cursor", "windsurf" => "Windsurf", "antigravity-ide" => "Antigravity IDE",
            "zed" => "Zed", "visual-studio" => "Visual Studio", "jetbrains" => "JetBrains IDE", _ => id,
        };
        return new ConnectionItem
        {
            Id = "editor-" + id, Name = name, Category = "Editors & IDEs", Method = ConnectionMethod.OpenProjectOnly,
            State = preferred ? ConnectionState.Connected : ConnectionState.InstalledNotConnected,
            Detail = preferred ? "Your preferred editor: files in AGEX open in it. AGEX does not control the editor." : "AGEX can open your project and changed files in it. AGEX does not control the editor.",
            Actions = preferred ? [new(ConnectionActionKind.OpenProject, "Open current project", id)] : [new(ConnectionActionKind.UseAsEditor, "Use as preferred editor", id), new(ConnectionActionKind.OpenProject, "Open current project", id)],
            Keywords = ["editor", "ide", "code"], Cost = "",
        };
    }
}
