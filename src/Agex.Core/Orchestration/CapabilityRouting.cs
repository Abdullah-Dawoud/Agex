using Agex.Core.Agents;
using Agex.Core.Settings;

namespace Agex.Core.Orchestration;

/// <summary>Actions AGEX may have to ask about.</summary>
public enum ActionKind { ReadFiles, WriteFiles, RunCommands, Browser, ComputerControl, Network, McpTool, ExternalCommunication, Destructive }

/// <summary>When AGEX asks before an action, for each approval mode.</summary>
public static class ApprovalRules
{
    /// <summary>Always confirmed, in every mode: sending things outside this computer and deleting or overwriting data.</summary>
    public static bool IsSensitive(ActionKind action) => action is ActionKind.ExternalCommunication or ActionKind.Destructive;

    /// <param name="canUndo">True when AGEX saves a snapshot first, so file changes can be undone.</param>
    public static bool MustAsk(ApprovalMode mode, ActionKind action, bool projectTrusted, bool canUndo)
    {
        if (IsSensitive(action)) return true;
        return mode switch
        {
            ApprovalMode.AskEveryTime => action != ActionKind.ReadFiles,
            // Smart: changes that cannot be undone in a project you have not trusted, and taking over the real mouse and keyboard.
            ApprovalMode.Smart => action == ActionKind.WriteFiles && !projectTrusted && !canUndo || action == ActionKind.ComputerControl,
            _ => false,
        };
    }

    public static string ModeLabel(ApprovalMode mode) => mode switch
    {
        ApprovalMode.AskEveryTime => "Ask every time",
        ApprovalMode.TrustSession => "Trust this session",
        _ => "Smart approvals",
    };

    public static string ModeDescription(ApprovalMode mode) => mode switch
    {
        ApprovalMode.AskEveryTime => "AGEX asks before every task that changes files, runs commands, uses the browser or controls the computer.",
        ApprovalMode.TrustSession => "Agents may use everything you turned on, without asking, until AGEX restarts. Payments, sending messages, deleting data, publishing and changing passwords or keys still ask.",
        _ => "AGEX asks only for sensitive actions, for changes it cannot undo, and before taking over the mouse and keyboard.",
    };
}

/// <summary>A browser or computer-control tool (an MCP server from an installed skill) that AGEX can hand to agents.</summary>
public sealed record ToolServer(string SkillId, string Name, ToolServerKind Kind, bool Enabled, McpServerSpec? Spec)
{
    /// <summary>The user set this tool to "ask each time" under Skills: AGEX asks before its first use in a request (except in Trust this session).</summary>
    public bool AskFirst { get; init; }
}

public enum ToolServerKind { Browser, Computer }

/// <summary>How the user can fix a missing capability or a failure.</summary>
public enum RecoveryKind { Retry, TryAnotherAgent, EnableBrowser, EnableComputerControl, EnableCommands, AllowFileChanges, EnableNetwork, EnableMcp, ConnectTool, TurnOnTool, OpenAgents }

public sealed record MissingCapability(NeededCapability Need, string Title, string Detail, RecoveryKind Fix, string Argument = "", bool Blocking = true)
{
    public string FixLabel => Fix switch
    {
        RecoveryKind.EnableBrowser => "Enable browser",
        RecoveryKind.EnableComputerControl => "Enable computer control",
        RecoveryKind.EnableCommands => "Allow commands",
        RecoveryKind.AllowFileChanges => "Allow file changes",
        RecoveryKind.EnableNetwork => "Allow network",
        RecoveryKind.EnableMcp => "Allow connected tools",
        RecoveryKind.ConnectTool => "Connect required tool",
        RecoveryKind.TurnOnTool => "Turn it on",
        RecoveryKind.OpenAgents => "Open Agents",
        RecoveryKind.TryAnotherAgent => "Try another tool",
        _ => "Retry",
    };
}

/// <summary>Which agents and tools can do what a request needs, and what is missing.</summary>
public sealed record CapabilityPlan
{
    public IReadOnlyList<MissingCapability> Missing { get; init; } = [];
    /// <summary>Agents that can open a real browser (built in, or through a browser tool AGEX passes to them).</summary>
    public IReadOnlyList<string> BrowserAgents { get; init; } = [];
    public IReadOnlyList<string> ComputerAgents { get; init; } = [];
    /// <summary>Tool servers to hand to agents for this request.</summary>
    public IReadOnlyList<ToolServer> Tools { get; init; } = [];
    public bool AllowNetwork { get; init; }
    public IEnumerable<MissingCapability> Blocking => Missing.Where(item => item.Blocking);
}

/// <summary>
/// Decides before dispatch which capabilities a request needs and which agents
/// and tools actually have them. A browser or computer task is never sent to an
/// agent that only has project file access.
/// </summary>
public static class CapabilityRouting
{
    /// <summary>Reviewed catalog skills that provide a real browser or computer control.</summary>
    public static readonly IReadOnlyDictionary<string, ToolServerKind> KnownTools = new Dictionary<string, ToolServerKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["playwright-mcp"] = ToolServerKind.Browser,
        ["chrome-devtools-mcp"] = ToolServerKind.Browser,
        ["windows-mcp"] = ToolServerKind.Computer,
    };

    public static bool HasNativeBrowser(TeamMember member) => member.Adapter.Capabilities.Contains(Capability.Browser);
    public static bool AcceptsMcp(TeamMember member) => member.Adapter.Capabilities.Contains(Capability.Mcp);

    public static CapabilityPlan Plan(RequestIntent intent, IReadOnlyList<TeamMember> members, PermissionSettings permissions, IReadOnlyList<ToolServer> tools, bool windows)
    {
        var missing = new List<MissingCapability>();
        var usable = tools.Where(tool => tool.Enabled && tool.Spec is not null && permissions.McpTools).ToList();
        var browserTools = usable.Where(tool => tool.Kind == ToolServerKind.Browser).ToList();
        var computerTools = usable.Where(tool => tool.Kind == ToolServerKind.Computer).ToList();
        var chosen = new List<ToolServer>();
        var browserAgents = new List<string>();
        var computerAgents = new List<string>();

        if (intent.Has(NeededCapability.EditProject) && intent.Kind == RequestKind.Build)
        {
            if (!permissions.WriteProject)
                missing.Add(new(NeededCapability.EditProject, "Changing files is turned off", "This request changes project files, but 'Write project files' is off in your permissions.", RecoveryKind.AllowFileChanges));
            else if (!members.Any(member => member.CanWrite))
                missing.Add(new(NeededCapability.EditProject, "No agent may change files", "None of the selected agents is allowed to edit files. Turn on 'Can change files' for one of them.", RecoveryKind.OpenAgents));
        }
        if (intent.Has(NeededCapability.RunShell) && intent.Kind == RequestKind.Build && !permissions.RunCommands)
            missing.Add(new(NeededCapability.RunShell, "Running commands is turned off", "This request needs to run commands (for example tests or a local server), but 'Run commands' is off.", RecoveryKind.EnableCommands, Blocking: false));

        if (intent.Wants(NeededCapability.Browser))
        {
            if (!permissions.Browser)
                missing.Add(new(NeededCapability.Browser, "Browser use is turned off", "This request needs a real browser (to open and use a page), but 'Browser' is off in your permissions.", RecoveryKind.EnableBrowser, Blocking: intent.Has(NeededCapability.Browser)));
            else
            {
                browserAgents.AddRange(members.Where(HasNativeBrowser).Select(member => member.Id));
                if (browserTools.Count > 0)
                {
                    var takers = members.Where(AcceptsMcp).ToList();
                    if (takers.Count > 0) { chosen.Add(browserTools[0]); browserAgents.AddRange(takers.Select(member => member.Id)); }
                }
                if (browserAgents.Count == 0)
                {
                    var installed = tools.FirstOrDefault(tool => tool.Kind == ToolServerKind.Browser);
                    var mcpTaker = members.Any(AcceptsMcp);
                    missing.Add(!permissions.McpTools && installed is not null
                        ? new(NeededCapability.Browser, "Connected tools are turned off", "A browser tool is installed, but 'MCP tools' is off in your permissions.", RecoveryKind.EnableMcp, Blocking: intent.Has(NeededCapability.Browser))
                        : installed is { Enabled: false }
                            ? new(NeededCapability.Browser, $"{installed.Name} is turned off", "Turn it on so agents can open and use pages in a real browser.", RecoveryKind.TurnOnTool, installed.SkillId, intent.Has(NeededCapability.Browser))
                            : !mcpTaker
                                ? new(NeededCapability.Browser, "No selected agent can use a browser", "Antigravity has a browser built in; Codex and Claude Code can use the Browser (Playwright) tool. Enable one of them.", RecoveryKind.OpenAgents, Blocking: intent.Has(NeededCapability.Browser))
                                : new(NeededCapability.Browser, "No browser tool is connected", "Connect Browser (Playwright MCP) so agents can open pages, click and type in a real browser.", RecoveryKind.ConnectTool, "playwright-mcp", intent.Has(NeededCapability.Browser)));
                }
            }
        }

        if (intent.Wants(NeededCapability.ComputerControl))
        {
            var required = intent.Has(NeededCapability.ComputerControl) && browserAgents.Count == 0;
            if (!windows)
            {
                if (intent.Has(NeededCapability.ComputerControl))
                    missing.Add(new(NeededCapability.ComputerControl, "Computer control is not available here", "AGEX's reviewed computer-control tool (Windows-MCP) works on Windows only.", RecoveryKind.TryAnotherAgent, Blocking: required));
            }
            else if (!permissions.ComputerControl)
                missing.Add(new(NeededCapability.ComputerControl, "Computer control is turned off", "Agents can use the mouse and keyboard only when 'Computer control' is on. You can take over or stop at any time.", RecoveryKind.EnableComputerControl, Blocking: required));
            else if (computerTools.Count > 0 && members.Any(AcceptsMcp))
            {
                chosen.Add(computerTools[0]);
                computerAgents.AddRange(members.Where(AcceptsMcp).Select(member => member.Id));
            }
            else
            {
                var installed = tools.FirstOrDefault(tool => tool.Kind == ToolServerKind.Computer);
                missing.Add(installed is { Enabled: false }
                    ? new(NeededCapability.ComputerControl, $"{installed.Name} is turned off", "Turn it on so agents can see the screen and use the mouse and keyboard.", RecoveryKind.TurnOnTool, installed.SkillId, required)
                    : !members.Any(AcceptsMcp)
                        ? new(NeededCapability.ComputerControl, "No selected agent can control the computer", "Codex and Claude Code can use Windows Computer Use. Enable one of them.", RecoveryKind.OpenAgents, Blocking: required)
                        : new(NeededCapability.ComputerControl, "No computer-control tool is connected", "Connect Windows Computer Use (Windows-MCP) so agents can see the screen, click and type.", RecoveryKind.ConnectTool, "windows-mcp", required));
            }
        }

        var network = permissions.Network && (intent.Wants(NeededCapability.ExternalNetwork) || intent.Wants(NeededCapability.LocalWeb) || intent.Wants(NeededCapability.Browser));
        if (intent.Has(NeededCapability.ExternalNetwork) && !permissions.Network)
            missing.Add(new(NeededCapability.ExternalNetwork, "Network use is turned off", "This request needs the internet, but 'Network' is off in your permissions.", RecoveryKind.EnableNetwork, Blocking: false));
        if (intent.Has(NeededCapability.Mcp) && !permissions.McpTools)
            missing.Add(new(NeededCapability.Mcp, "Connected tools are turned off", "This request mentions a program AGEX reaches through a connected tool, but 'MCP tools' is off.", RecoveryKind.EnableMcp, Blocking: false));

        return new CapabilityPlan
        {
            Missing = missing, BrowserAgents = browserAgents.Distinct().ToList(), ComputerAgents = computerAgents.Distinct().ToList(),
            Tools = chosen, AllowNetwork = network,
        };
    }

    /// <summary>One line per member for the leader: what it can actually do in this request.</summary>
    public static string Abilities(TeamMember member, CapabilityPlan plan, bool commands)
    {
        var parts = new List<string> { member.CanWrite ? "can read and edit project files" : member.CanReadFiles ? "can read project files but must not edit them" : "text only: cannot open or edit files; give it self-contained questions" };
        if (member.CanReadFiles && commands) parts.Add("can run commands");
        if (plan.BrowserAgents.Contains(member.Id)) parts.Add(HasNativeBrowser(member) ? "has a built-in browser" : "can open and use pages in a real browser (Playwright tool)");
        if (plan.ComputerAgents.Contains(member.Id)) parts.Add("can see the screen and use the mouse and keyboard (Windows Computer Use tool)");
        return string.Join("; ", parts);
    }
}
