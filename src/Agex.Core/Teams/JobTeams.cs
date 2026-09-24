using Agex.Core.Agents;
using Agex.Core.Platform;
using Agex.Core.Skills;

namespace Agex.Core.Teams;

/// <summary>What agents may do when a team works. Teams recommend a level; the user decides.</summary>
public enum ApprovalLevel { ReadOnly, ProjectWrite, BrowserActions, ComputerControl, ExternalCommunication, Sensitive }

public enum RequirementKind { Skill, Program, Integration, LocalModel, EditingAgent }

public enum RequirementLevel { Required, Recommended, Optional }

public enum RequirementState { Ready, Missing, NeedsAccount, NeedsDependency, NotOnThisSystem, NotInstalled }

public sealed record TeamRequirement(RequirementKind Kind, string Id, string Label, RequirementLevel Level, string Why, string FreeAlternative = "", string PaidOption = "");

/// <summary>A job-based team: what it accomplishes and what it needs. Agents are chosen automatically.</summary>
public sealed record JobTeam
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> TypicalTasks { get; init; } = [];
    public IReadOnlyList<string> Outputs { get; init; } = [];
    public IReadOnlyList<TeamRequirement> Requirements { get; init; } = [];
    public ApprovalLevel Approval { get; init; } = ApprovalLevel.ProjectWrite;
    public EfficiencyHint Efficiency { get; init; } = EfficiencyHint.None;
    /// <summary>Extra rules for agents, added to every prompt while the team is in use.</summary>
    public string Workflow { get; init; } = "";
}

public enum EfficiencyHint { None, LocalFirst }

public sealed record RequirementStatus(TeamRequirement Requirement, RequirementState State, string Detail, string? ActionUrl);

public sealed record ProgramInfo(string Id, string Name, string DownloadUrl, string? Path);

/// <summary>Finds desktop programs a team can use (by known install locations only; nothing is started).</summary>
public sealed class ProgramDetector(IPlatformService platform)
{
    public ProgramInfo Find(string id)
    {
        var (name, url) = Describe(id);
        return new ProgramInfo(id, name, url, Locate(id));
    }

    public static (string Name, string Url) Describe(string id) => id switch
    {
        "revit" => ("Autodesk Revit", "https://www.autodesk.com/products/revit/"),
        "autocad" => ("Autodesk AutoCAD", "https://www.autodesk.com/products/autocad/"),
        "excel" => ("Microsoft Excel", "https://www.microsoft.com/microsoft-365/excel"),
        "word" => ("Microsoft Word", "https://www.microsoft.com/microsoft-365/word"),
        "powerpoint" => ("Microsoft PowerPoint", "https://www.microsoft.com/microsoft-365/powerpoint"),
        "libreoffice" => ("LibreOffice (free)", "https://www.libreoffice.org/download/"),
        "chrome" => ("Google Chrome", "https://www.google.com/chrome/"),
        "figma" => ("Figma desktop", "https://www.figma.com/downloads/"),
        "python" => ("Python", "https://www.python.org/downloads/"),
        "node" => ("Node.js", "https://nodejs.org/en/download"),
        "git" => ("Git", "https://git-scm.com/downloads"),
        "ffmpeg" => ("ffmpeg (video frames)", "https://ffmpeg.org/download.html"),
        "semgrep" => ("Semgrep", "https://semgrep.dev/docs/getting-started/quickstart"),
        _ => (id, ""),
    };

    private string? Locate(string id)
    {
        switch (id)
        {
            case "python": return platform.FindExecutable(platform.Os == OsKind.Windows ? "python" : "python3");
            case "node": return platform.FindExecutable("node");
            case "git" or "ffmpeg" or "semgrep": return platform.FindExecutable(id);
        }
        return platform.Os switch
        {
            OsKind.Windows => LocateWindows(id),
            OsKind.MacOS => id switch
            {
                "excel" => Existing("/Applications/Microsoft Excel.app"),
                "word" => Existing("/Applications/Microsoft Word.app"),
                "powerpoint" => Existing("/Applications/Microsoft PowerPoint.app"),
                "libreoffice" => Existing("/Applications/LibreOffice.app"),
                "chrome" => Existing("/Applications/Google Chrome.app"),
                "figma" => Existing("/Applications/Figma.app"),
                "autocad" => Directory.Exists("/Applications") ? Directory.GetDirectories("/Applications", "Autodesk*").SelectMany(dir => Directory.GetDirectories(dir, "AutoCAD*")).FirstOrDefault() : null,
                _ => null,
            },
            _ => id switch
            {
                "chrome" => platform.FindExecutable("google-chrome") ?? platform.FindExecutable("chromium"),
                "libreoffice" => platform.FindExecutable("libreoffice") ?? platform.FindExecutable("soffice"),
                _ => null,
            },
        };
    }

    private static string? Existing(string path) => Directory.Exists(path) || File.Exists(path) ? path : null;

    private static string? LocateWindows(string id)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? Versioned(string folderPattern, string exe)
        {
            var autodesk = Path.Combine(programFiles, "Autodesk");
            return Directory.Exists(autodesk)
                ? Directory.GetDirectories(autodesk, folderPattern).OrderByDescending(dir => dir, StringComparer.Ordinal).Select(dir => Path.Combine(dir, exe)).FirstOrDefault(File.Exists)
                : null;
        }
        return id switch
        {
            "revit" => Versioned("Revit 20*", "Revit.exe"),
            "autocad" => Versioned("AutoCAD 20*", "acad.exe"),
            "excel" => AppPath("excel.exe"),
            "word" => AppPath("winword.exe"),
            "powerpoint" => AppPath("powerpnt.exe"),
            "libreoffice" => Existing(Path.Combine(programFiles, "LibreOffice", "program", "soffice.exe")),
            "chrome" => AppPath("chrome.exe") ?? Existing(Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe")) ?? Existing(Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe")),
            "figma" => Existing(Path.Combine(local, "Figma", "Figma.exe")),
            _ => null,
        };
    }

    /// <summary>Windows "App Paths" registration, which Office and Chrome use.</summary>
    private static string? AppPath(string exe)
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
        }
        return null;
    }
}

/// <summary>
/// The Autodesk AI Bridge (a separate project by the same author): a local MCP
/// server that talks to Revit and AutoCAD plug-ins over authenticated named
/// pipes. AGEX only uses it when its Host is installed.
/// </summary>
public static class AutodeskBridge
{
    public const string SkillId = "autodesk-ai-bridge";

    public static string? HostPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutodeskAIBridge", "Host", "AutodeskAIBridge.Host.exe");
        return File.Exists(path) ? path : null;
    }
}

public static class JobTeamCatalog
{
    private static TeamRequirement Skill(string id, string label, RequirementLevel level, string why, string free = "", string paid = "") => new(RequirementKind.Skill, id, label, level, why, free, paid);
    private static TeamRequirement Program(string id, RequirementLevel level, string why, string free = "") => new(RequirementKind.Program, id, ProgramDetector.Describe(id).Name, level, why, free);

    public static IReadOnlyList<JobTeam> All { get; } =
    [
        new()
        {
            Id = "software-builder", Name = "Software Builder",
            Summary = "Plans, builds, tests and reviews software in your project, with undo for every change.",
            TypicalTasks = ["Add a feature", "Fix a bug with a test", "Review a change", "Explain unfamiliar code"],
            Outputs = ["Code changes with tests", "Review notes", "Pull request text"],
            Requirements =
            [
                new(RequirementKind.EditingAgent, "editing-agent", "An agent that can edit files", RequirementLevel.Required, "Code changes need an agent with write access (Codex, Antigravity, Claude Code, Gemini CLI or OpenCode).", "OpenCode with its free models"),
                Program("git", RequirementLevel.Required, "Snapshots and undo for every change."),
                Skill("writing-plans", "Writing Plans", RequirementLevel.Recommended, "Turns requests into step-by-step plans."),
                Skill("test-driven-development", "Test-Driven Development", RequirementLevel.Recommended, "Proves changes with tests."),
                Skill("systematic-debugging", "Systematic Debugging", RequirementLevel.Recommended, "Finds root causes before fixing."),
                Skill("requesting-code-review", "Code Review", RequirementLevel.Recommended, "Structured review of finished work."),
                Skill("context7", "Library Docs (Context7)", RequirementLevel.Optional, "Current library documentation."),
                Skill("github-mcp", "Git & GitHub", RequirementLevel.Optional, "Issues and pull requests.", "Free GitHub account"),
                Skill("repomix", "Repository Packer (Repomix)", RequirementLevel.Optional, "Compact repository summaries to save tokens on big projects."),
            ],
            Workflow = "Plan first, keep changes small, add or update tests, and check your work before saying it is done.",
        },
        new()
        {
            Id = "research-lab", Name = "Research Lab",
            Summary = "Researches a question on the web and in documents, and writes a sourced report.",
            TypicalTasks = ["Compare options", "Summarise documents", "Find current documentation", "Market or technology scan"],
            Outputs = ["Report with sources (Markdown)", "Comparison table (CSV)"],
            Approval = ApprovalLevel.ProjectWrite,
            Requirements =
            [
                Skill("web-fetch", "Web Research (Fetch)", RequirementLevel.Required, "Reads web pages.", "Free"),
                Skill("exa-search", "Web Search (Exa)", RequirementLevel.Recommended, "Searches the web without an account.", "Free (rate-limited)", "Tavily or Brave Search with an API key"),
                Skill("pdf-documents", "PDF Documents", RequirementLevel.Recommended, "Reads and writes PDFs."),
                Skill("deepwiki", "Repository Wiki (DeepWiki)", RequirementLevel.Optional, "Questions about public GitHub projects."),
                Skill("microsoft-learn", "Microsoft Learn Docs", RequirementLevel.Optional, "Official Microsoft documentation."),
            ],
            Workflow = "Cite a source for every factual claim. Say clearly when something could not be verified. Write the report into a file in the project.",
        },
        new()
        {
            Id = "architecture-bim", Name = "Architecture & BIM",
            Summary = "Architectural research, drawing and model reviews, quantities, schedules and project reports, with Revit and AutoCAD when connected.",
            TypicalTasks = ["Analyse a floor plan image or PDF", "BIM model QA checklist", "Quantity and area schedules", "Regulation and code research", "Project report", "AutoLISP or script for a repetitive AutoCAD task"],
            Outputs = ["Reports (Markdown/PDF)", "Schedules (CSV/Excel)", "Review issue lists", "AutoCAD scripts", "Revit/AutoCAD edits through the bridge (after you confirm)"],
            Approval = ApprovalLevel.ProjectWrite,
            Requirements =
            [
                Skill("pdf-documents", "PDF Documents", RequirementLevel.Required, "Reads drawing sets and writes reports."),
                Skill("web-fetch", "Web Research (Fetch)", RequirementLevel.Recommended, "Reads regulations and product data."),
                Skill("exa-search", "Web Search (Exa)", RequirementLevel.Recommended, "Finds codes, standards and references.", "Free (rate-limited)"),
                Program("autocad", RequirementLevel.Optional, "Drawings and scripts."),
                Program("revit", RequirementLevel.Optional, "BIM models."),
                new(RequirementKind.Integration, AutodeskBridge.SkillId, "Autodesk AI Bridge (Revit/AutoCAD connection)", RequirementLevel.Optional, "Lets agents read and edit Revit and AutoCAD models through named tools, after you confirm each edit."),
                Program("excel", RequirementLevel.Optional, "Opens schedules and quantity tables.", "LibreOffice (free) opens the same CSV/XLSX files"),
            ],
            Workflow = "Work on copies of drawings and models. Never change a Revit or AutoCAD model without asking the user first. State units and assumptions in every quantity or area result.",
        },
        new()
        {
            Id = "marketing-growth", Name = "Marketing & Growth",
            Summary = "Competitor research, campaign plans, ad and social copy, SEO reviews and content calendars.",
            TypicalTasks = ["Competitor overview", "Campaign plan", "Ad copy variations", "Social posts for a month", "SEO review of a landing page", "Keyword ideas"],
            Outputs = ["Campaign plan (Markdown)", "Content calendar (CSV)", "Ad and social copy", "SEO audit"],
            Approval = ApprovalLevel.BrowserActions,
            Requirements =
            [
                Skill("web-fetch", "Web Research (Fetch)", RequirementLevel.Required, "Reads competitor and landing pages.", "Free"),
                Skill("exa-search", "Web Search (Exa)", RequirementLevel.Recommended, "Finds competitors and trends.", "Free (rate-limited)", "Tavily or Brave Search"),
                Skill("playwright-mcp", "Browser (Playwright MCP)", RequirementLevel.Recommended, "Checks landing pages in a real browser."),
                Skill("firecrawl", "Web Scraping (Firecrawl)", RequirementLevel.Optional, "Extracts whole sites into clean text.", "", "Firecrawl API key (free credits)"),
                Skill("frontend-design", "Frontend Design", RequirementLevel.Optional, "Landing-page design guidance."),
                Skill("notion-mcp", "Notion", RequirementLevel.Optional, "Publishes plans to Notion.", "Free Notion plan"),
                Program("figma", RequirementLevel.Optional, "Design files (open them yourself; AGEX has no Figma connection)."),
            ],
            Workflow = "Never publish, post or send anything; prepare it for the user to review. Mark every number that is an estimate.",
        },
        new()
        {
            Id = "computer-operator", Name = "Computer Operator",
            Summary = "Does multi-step browser work for you: collecting information, filling forms and downloading files, stopping before anything risky.",
            TypicalTasks = ["Collect data from several websites", "Fill a web form (you confirm before submit)", "Download and organise files", "Repeat a browser workflow"],
            Outputs = ["Collected data (CSV)", "Filled forms waiting for your confirmation", "Organised files"],
            Approval = ApprovalLevel.Sensitive,
            Requirements =
            [
                Skill("playwright-mcp", "Browser (Playwright MCP)", RequirementLevel.Required, "Controls a real browser for the agent."),
                Skill("chrome-devtools-mcp", "Chrome DevTools", RequirementLevel.Optional, "Inspects pages when something goes wrong."),
                Skill("windows-mcp", "Windows Computer Use (Windows-MCP)", RequirementLevel.Optional, "Operates Windows programs (clicks, typing, screenshots) when the browser is not enough. Every use asks you first."),
                Program("chrome", RequirementLevel.Recommended, "The browser Chrome DevTools controls."),
            ],
            Workflow = "Before any action that submits a form, sends a message, makes a payment, deletes files, uploads a document or changes an account, stop and ask the user (NEEDS_INPUT or a QUESTION to User) with exactly what will happen. Never type passwords, card numbers or identity numbers.",
        },
        new()
        {
            Id = "job-search", Name = "Job Search & Applications",
            Summary = "Finds and scores openings, tailors your CV and cover letters, tracks applications and prepares interviews. Never applies without your approval.",
            TypicalTasks = ["Find openings that match my CV", "Score jobs against my profile", "Tailor my CV for this job", "Write a cover letter", "Track my applications", "Prepare interview notes"],
            Outputs = ["Tailored CV and cover letter", "Application tracker (CSV)", "Interview notes"],
            Approval = ApprovalLevel.ExternalCommunication,
            Requirements =
            [
                Skill("web-fetch", "Web Research (Fetch)", RequirementLevel.Required, "Reads job postings."),
                Skill("exa-search", "Web Search (Exa)", RequirementLevel.Recommended, "Finds openings.", "Free (rate-limited)"),
                Skill("pdf-documents", "PDF Documents", RequirementLevel.Recommended, "Reads and writes CVs as PDF."),
                Skill("playwright-mcp", "Browser (Playwright MCP)", RequirementLevel.Optional, "Fills application forms (you confirm before submit)."),
                Program("word", RequirementLevel.Optional, "Opens the tailored CV.", "LibreOffice (free)"),
            ],
            Workflow = "Never submit an application, send an email or message, or accept terms without the user's explicit approval for that exact action. Attach the user's CV only when they ask. Keep a tracker file up to date.",
        },
        new()
        {
            Id = "document-office", Name = "Document Office",
            Summary = "Reads, writes and converts documents: reports, letters, spreadsheets and presentations.",
            TypicalTasks = ["Summarise these files", "Write a report from my notes", "Turn this table into a chart", "Draft a letter"],
            Outputs = ["Markdown, PDF, Word and Excel files", "Summaries"],
            Requirements =
            [
                Skill("pdf-documents", "PDF Documents", RequirementLevel.Required, "Reads and creates PDFs."),
                Skill("jupyter-notebook", "Jupyter Notebooks", RequirementLevel.Optional, "Tables and charts."),
                Program("python", RequirementLevel.Recommended, "Most document tools use Python."),
                Program("excel", RequirementLevel.Optional, "Opens spreadsheets.", "LibreOffice (free)"),
                Program("word", RequirementLevel.Optional, "Opens documents.", "LibreOffice (free)"),
            ],
            Workflow = "Keep the user's original files unchanged; write results as new files.",
        },
        new()
        {
            Id = "data-analyst", Name = "Data Analyst",
            Summary = "Cleans data, analyses it and explains the results with charts.",
            TypicalTasks = ["Explore this CSV", "Find trends", "Build a chart", "Check data quality"],
            Outputs = ["Notebook", "Charts", "Clean data (CSV)", "Findings summary"],
            Requirements =
            [
                Skill("jupyter-notebook", "Jupyter Notebooks", RequirementLevel.Recommended, "Reproducible analysis."),
                Program("python", RequirementLevel.Required, "Runs the analysis."),
                Program("excel", RequirementLevel.Optional, "Opens results.", "LibreOffice (free)"),
            ],
            Workflow = "Never change the original data file; write cleaned data to a new file and explain each cleaning step.",
        },
        new()
        {
            Id = "security-review", Name = "Security Review",
            Summary = "Reviews code for security problems and writes findings, without changing anything.",
            TypicalTasks = ["Security review of this project", "Threat model", "Review this change for security", "Check dependencies"],
            Outputs = ["Findings by severity", "Threat model"],
            Approval = ApprovalLevel.ReadOnly,
            Requirements =
            [
                Skill("security-best-practices", "Security Best Practices", RequirementLevel.Required, "Language-specific review guidance."),
                Skill("security-threat-model", "Threat Model", RequirementLevel.Recommended, "Structured threat modelling."),
                Skill("differential-review", "Security Diff Review", RequirementLevel.Recommended, "Reviews changes for risk."),
                Skill("semgrep-scan", "Semgrep Static Analysis", RequirementLevel.Optional, "Automated scanning."),
                Program("semgrep", RequirementLevel.Optional, "Runs Semgrep scans."),
            ],
            Workflow = "Do not change any file. Report each finding with location, impact and a suggested fix.",
        },
        new()
        {
            Id = "devops-release", Name = "DevOps & Release",
            Summary = "Fixes CI, prepares releases and deploys web apps to the service you choose.",
            TypicalTasks = ["Why is CI failing?", "Prepare a release", "Deploy this site", "Look at production errors"],
            Outputs = ["CI fixes", "Release notes", "Deployment links"],
            Approval = ApprovalLevel.ExternalCommunication,
            Requirements =
            [
                new(RequirementKind.EditingAgent, "editing-agent", "An agent that can edit files", RequirementLevel.Required, "Fixes need write access."),
                Skill("github-mcp", "Git & GitHub", RequirementLevel.Recommended, "Pull requests and Actions.", "Free GitHub account"),
                Skill("gh-fix-ci", "Fix GitHub CI", RequirementLevel.Recommended, "Reads failing checks."),
                Skill("vercel-deploy", "Deploy to Vercel", RequirementLevel.Optional, "Deploys to Vercel.", "Free Vercel hobby plan"),
                Skill("netlify-deploy", "Deploy to Netlify", RequirementLevel.Optional, "Deploys to Netlify."),
                Skill("sentry-mcp", "Sentry", RequirementLevel.Optional, "Production errors."),
            ],
            Workflow = "Ask the user before deploying, publishing a release or changing anything outside the project folder.",
        },
        new()
        {
            Id = "local-private", Name = "Local Private AI",
            Summary = "Everything stays on this computer: local models only, no cloud agent, no network skills.",
            TypicalTasks = ["Private notes and summaries", "Explain code without sending it anywhere", "Draft text offline"],
            Outputs = ["Answers and drafts that never leave this computer"],
            Approval = ApprovalLevel.ReadOnly,
            Efficiency = EfficiencyHint.LocalFirst,
            Requirements =
            [
                new(RequirementKind.LocalModel, "ollama", "A local model (Ollama)", RequirementLevel.Required, "Runs the model on this computer.", "Free"),
                Skill("caveman", "Caveman (short answers)", RequirementLevel.Optional, "Shorter answers from small local models."),
            ],
            Workflow = "Use only local agents. Do not use the web.",
        },
    ];

    public static JobTeam? Get(string? id) => All.FirstOrDefault(team => team.Id == id);

    public static string ApprovalText(ApprovalLevel level) => level switch
    {
        ApprovalLevel.ReadOnly => "Read only: agents may read the project but not change it",
        ApprovalLevel.ProjectWrite => "Project write: agents may change files in the project (you approve, and can undo)",
        ApprovalLevel.BrowserActions => "Browser actions: agents may browse and read websites",
        ApprovalLevel.ComputerControl => "Computer control: agents may operate programs on this computer",
        ApprovalLevel.ExternalCommunication => "External communication: anything sent outside (forms, emails, messages) needs your confirmation",
        _ => "Sensitive actions: submitting, paying, deleting or changing accounts always needs your confirmation",
    };

    /// <summary>The team brief added to prompts: goal, workflow rules and the approval level as plain rules.</summary>
    public static string Brief(JobTeam team)
    {
        var rules = team.Approval switch
        {
            ApprovalLevel.ReadOnly => "Do not create, change or delete any file.",
            ApprovalLevel.ExternalCommunication or ApprovalLevel.Sensitive or ApprovalLevel.ComputerControl or ApprovalLevel.BrowserActions =>
                "Before any action that leaves this computer or cannot be undone (submitting a form, sending a message or email, making a payment, uploading a file, deleting files, changing an account), stop and ask the user first, describing exactly what will happen.",
            _ => "",
        };
        return $"Team: {team.Name}. Goal: {team.Summary}\n{team.Workflow}\n{rules}".Trim();
    }
}

/// <summary>Checks what a team needs against this computer, the installed skills and the agents.</summary>
public sealed class JobTeamService(IPlatformService platform, SkillManager skills, AgentRegistry registry, Func<IReadOnlyList<string>> enabledAgents)
{
    private readonly ProgramDetector _programs = new(platform);

    public IReadOnlyList<RequirementStatus> Check(JobTeam team)
    {
        var catalog = skills.Catalog();
        var installed = skills.Installed().ToDictionary(skill => skill.Id);
        var agents = enabledAgents();
        var list = new List<RequirementStatus>();
        foreach (var requirement in team.Requirements)
        {
            switch (requirement.Kind)
            {
                case RequirementKind.Skill:
                {
                    var manifest = catalog.Skills.FirstOrDefault(skill => skill.Id == requirement.Id);
                    if (manifest is null) { list.Add(new(requirement, RequirementState.Missing, "Not in this AGEX version's catalog.", null)); break; }
                    var state = skills.State(manifest, installed.GetValueOrDefault(manifest.Id), agents);
                    list.Add(state.Readiness switch
                    {
                        SkillReadiness.Ready => new(requirement, RequirementState.Ready, "Installed", null),
                        SkillReadiness.NotInstalled => new(requirement, RequirementState.NotInstalled, manifest.RequiresAccount ? "Not installed · needs an account" : "Not installed", null),
                        SkillReadiness.AccountRequired => new(requirement, RequirementState.NeedsAccount, state.Detail, manifest.Auth?.SetupUrl),
                        SkillReadiness.DependencyMissing => new(requirement, RequirementState.NeedsDependency, state.Detail, state.MissingTools.FirstOrDefault()?.InstallUrl),
                        SkillReadiness.PlatformUnsupported => new(requirement, RequirementState.NotOnThisSystem, state.Detail, null),
                        SkillReadiness.AgentIncompatible => new(requirement, RequirementState.Missing, state.Detail, null),
                        _ => new(requirement, RequirementState.Missing, state.Detail.Length > 0 ? state.Detail : "Turned off", null),
                    });
                    break;
                }
                case RequirementKind.Program:
                {
                    var program = _programs.Find(requirement.Id);
                    if (requirement.Id == "revit" && platform.Os != OsKind.Windows) { list.Add(new(requirement, RequirementState.NotOnThisSystem, "Revit runs on Windows only.", null)); break; }
                    list.Add(program.Path is not null
                        ? new(requirement, RequirementState.Ready, "Found on this computer", null)
                        : new(requirement, RequirementState.NotInstalled, "Not found on this computer", program.DownloadUrl.Length > 0 ? program.DownloadUrl : null));
                    break;
                }
                case RequirementKind.Integration when requirement.Id == AutodeskBridge.SkillId:
                {
                    if (platform.Os != OsKind.Windows) { list.Add(new(requirement, RequirementState.NotOnThisSystem, "Revit and the bridge run on Windows only.", null)); break; }
                    var connected = installed.ContainsKey(AutodeskBridge.SkillId);
                    list.Add(AutodeskBridge.HostPath() is null
                        ? new(requirement, RequirementState.NotInstalled, "The Autodesk AI Bridge is not installed. AGEX cannot control Revit or AutoCAD without it.", null)
                        : connected ? new(requirement, RequirementState.Ready, "Connected", null) : new(requirement, RequirementState.Missing, "Installed, not connected to AGEX yet", null));
                    break;
                }
                case RequirementKind.LocalModel:
                {
                    var detection = registry.LastDetection("ollama");
                    list.Add(detection?.Status == AgentStatus.Supported && agents.Contains("ollama")
                        ? new(requirement, RequirementState.Ready, "Ollama is ready", null)
                        : new(requirement, RequirementState.NotInstalled, detection?.Status == AgentStatus.Supported ? "Ollama is installed; turn it on in Agents." : "Install Ollama and download a model.", "https://ollama.com/download"));
                    break;
                }
                case RequirementKind.EditingAgent:
                {
                    var ready = agents.Any(id => registry.Get(id) is { CanWriteFiles: true } && registry.LastDetection(id)?.Status == AgentStatus.Supported);
                    list.Add(ready ? new(requirement, RequirementState.Ready, "Ready", null) : new(requirement, RequirementState.Missing, "Enable an agent that can edit files in Agents.", null));
                    break;
                }
                default:
                    list.Add(new(requirement, RequirementState.Missing, "", null));
                    break;
            }
        }
        return list;
    }

    public static (int Ready, int Total, int RequiredMissing) Progress(IReadOnlyList<RequirementStatus> statuses) =>
        (statuses.Count(item => item.State == RequirementState.Ready), statuses.Count(item => item.State != RequirementState.NotOnThisSystem),
         statuses.Count(item => item.Requirement.Level == RequirementLevel.Required && item.State != RequirementState.Ready));

    /// <summary>Connects the Autodesk AI Bridge as a local MCP tool (only when its Host is installed).</summary>
    public InstalledSkill? ConnectAutodeskBridge()
    {
        if (AutodeskBridge.HostPath() is not { } host) return null;
        var skill = skills.AddMcpServer("Autodesk AI Bridge", host, [], new Dictionary<string, string>());
        return skill;
    }
}
