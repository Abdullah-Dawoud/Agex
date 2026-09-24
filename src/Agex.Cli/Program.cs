using System.Diagnostics;
using System.Text;
using Agex.Core;
using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Core.Sessions;
using Agex.Core.Skills;

namespace Agex.Cli;

/// <summary>
/// The agex command. With no arguments it opens the desktop app; everything
/// else works in a terminal and uses the same engine and data as the app.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        var command = args.Length == 0 ? "open" : args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        try
        {
            return command switch
            {
                "open" => OpenDesktop(rest),
                "--safe-mode" => OpenDesktop(["--safe-mode"]),
                "run" => await RunAsync(rest),
                "--cli" or "cli" or "chat" => await InteractiveAsync(rest),
                "doctor" => await DoctorAsync(),
                "agents" => await AgentsAsync(),
                "project" => Project(rest),
                "sessions" => Sessions(rest),
                "skills" => await SkillsAsync(rest),
                "update" => await UpdateAsync(rest),
                "repair" => Repair(),
                "settings" => SettingsCommand(rest),
                "backup" => Backup(),
                "uninstall" => Uninstall(rest),
                "version" or "--version" or "-v" => Print($"AGEX {AgexInfo.Version} ({Agex.Core.Platform.PlatformFactory.Create().RuntimeId})"),
                "help" or "--help" or "-h" or "/?" => Help(),
                _ => Unknown(command),
            };
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled."); return 130; }
        catch (Exception ex) when (ex is InvalidOperationException or SkillException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("agex: " + ex.Message);
            return 1;
        }
    }

    private static AgexCore? _core;
    private static AgexCore Core()
    {
        if (_core is not null) return _core;
        _core = new AgexCore();
        _core.Start();
        foreach (var notice in _core.StartupNotices) Console.Error.WriteLine("note: " + notice);
        return _core;
    }

    private static int Print(string text) { Console.WriteLine(text); return 0; }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"agex: unknown command '{command}'. Run 'agex help'.");
        return 2;
    }

    private static int Help() => Print($"""
        AGEX {AgexInfo.Version} - AGEX AI CONTROL CENTER

        Usage:
          agex                         Open the desktop app
          agex --safe-mode             Open the desktop app without skills
          agex run "<request>"         Run one request in this terminal
               [--project <folder>] [--team <id>] [--agents codex,antigravity] [--yes]
          agex --cli                   Type requests one after another in this terminal
          agex doctor                  Check AGEX, agents, skills and storage
          agex agents                  List agents and tools found on this computer
          agex project [<folder>]      Show or change the current project
          agex sessions [list|show <id>|search <text>|export <id> <file>|delete <id>]
          agex skills [list|catalog|install <id>|remove <id>|enable <id>|disable <id>|update]
          agex update [--check]        Check for and install AGEX updates
          agex repair                  Fix AGEX's own files and settings
          agex settings export <file> | import <file>
          agex backup                  Back up settings, projects and the skill list
          agex uninstall               Remove AGEX (keeps agents and your projects)
          agex version
        """);

    // --------------------------------------------------------- desktop

    private static int OpenDesktop(string[] args)
    {
        var directory = AppContext.BaseDirectory;
        var name = OperatingSystem.IsWindows() ? "AgexDesktop.exe" : "AgexDesktop";
        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("The AGEX desktop app is not installed next to this command. Use 'agex --cli' for the terminal, or reinstall AGEX.");
            return 1;
        }
        var psi = new ProcessStartInfo(path) { UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi);
        return 0;
    }

    // ------------------------------------------------------------ run

    private sealed class ConsoleHost(bool assumeYes) : IEngineHost
    {
        public Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken)
        {
            Console.WriteLine();
            Console.WriteLine($"? {request.Title} {request.Detail}");
            if (assumeYes) { Console.WriteLine("  --yes given: allowed."); return Task.FromResult(ApprovalDecision.Allow); }
            if (Console.IsInputRedirected) { Console.WriteLine("  No terminal to ask; not allowed. Use --yes to allow."); return Task.FromResult(ApprovalDecision.Deny); }
            Console.Write("  Allow? [y/N] ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            return Task.FromResult(answer is "y" or "yes" ? ApprovalDecision.Allow : ApprovalDecision.Deny);
        }

        public Task<string?> AskUserAsync(PendingQuestion question, CancellationToken cancellationToken)
        {
            Console.WriteLine();
            Console.WriteLine($"? {question.From} asks: {question.Question}");
            if (Console.IsInputRedirected) return Task.FromResult<string?>(null);
            Console.Write("  Your answer (empty to stop): ");
            return Task.FromResult<string?>(Console.ReadLine());
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var flags = new[] { "--project", "--team", "--agents" };
        var words = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (flags.Contains(args[index], StringComparer.OrdinalIgnoreCase)) { index++; continue; }
            if (args[index] is "--yes" or "-y") continue;
            words.Add(args[index]);
        }
        var request = string.Join(' ', words).Trim();
        if (request.Length == 0 && Console.IsInputRedirected) request = (await Console.In.ReadToEndAsync()).Trim();
        if (request.Length == 0) { Console.Error.WriteLine("agex run: give the request in quotes, for example: agex run \"add a README\""); return 2; }
        return await ExecuteAsync(request, Option(args, "--project"), Option(args, "--team"), Option(args, "--agents"), args.Contains("--yes") || args.Contains("-y"));
    }

    private static async Task<int> ExecuteAsync(string request, string? projectOption, string? team, string? agents, bool yes)
    {
        var core = Core();
        var projectPath = Path.GetFullPath(projectOption ?? (core.Settings.LastProject.Length > 0 ? core.Settings.LastProject : Directory.GetCurrentDirectory()));
        if (!Directory.Exists(projectPath)) { Console.Error.WriteLine($"Project folder not found: {projectPath}"); return 1; }
        var profile = core.SettingsStore.LoadProject(projectPath);
        var members = core.BuildMembers(profile, team, agents?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (members.Count == 0) { Console.Error.WriteLine("No agent is ready. Run 'agex agents' to see what AGEX found."); return 1; }
        var destinations = AgexCore.CloudDestinations(members);
        if (destinations.Count > 0) Console.WriteLine($"Note: your request and the files agents read go to {string.Join(", ", destinations)}.");
        var active = core.Skills.ForRequest(profile.Skills);
        var engine = core.CreateRequest(projectPath, request, new ConsoleHost(yes), members, profile, active.Instructions, active.McpServers);
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Console.WriteLine("\nCancelling (agents are being stopped)..."); cancel.Cancel(); };
        engine.TimelineAdded += entry => Console.WriteLine($"{entry.At.ToLocalTime():HH:mm:ss}  {Glyph(entry.Kind)} {entry.Text}");
        engine.MessageAdded += message =>
        {
            if (message.Type is MessageType.Question && message.To != "User") Console.WriteLine($"          {message.From} asks {message.To}: {Short(message.Text)}");
        };
        Console.WriteLine($"AGEX {AgexInfo.Version}: {members.Count} agent(s) in {profile.Name}. Leader: {engine.Session.Leader}. Ctrl+C cancels.");
        var session = await engine.RunAsync(cancel.Token);
        Console.WriteLine();
        Console.WriteLine($"RESULT: {SessionStatusText.Code(session.Status)} - {session.Outcome?.Headline}");
        if (session.Outcome is { } outcome)
        {
            if (outcome.Reason.Length > 0) Console.WriteLine(outcome.Reason);
            if (outcome.Verification.Length > 0) Console.WriteLine("Checked: " + outcome.Verification);
            foreach (var line in outcome.WhatHappened) Console.WriteLine("- " + line);
            if (outcome.PrimaryFailure.Length > 0) Console.WriteLine("Problem: " + outcome.PrimaryFailure);
        }
        foreach (var change in session.Changes.Take(30)) Console.WriteLine($"  {change.Kind,-8} {change.Path}");
        Console.WriteLine($"Session {session.Id} saved. Open the desktop app to see the Agent Room.");
        return session.Status switch
        {
            SessionStatus.Complete or SessionStatus.CompleteWithFallback => 0,
            SessionStatus.Cancelled => 130,
            SessionStatus.Partial or SessionStatus.Unverified => 3,
            _ => 1,
        };
    }

    private static string Glyph(TimelineKind kind) => kind switch
    {
        TimelineKind.Done => "[ok]",
        TimelineKind.Failed => "[x] ",
        TimelineKind.Fallback or TimelineKind.Warning => "[!] ",
        TimelineKind.Input => "[?] ",
        TimelineKind.Approval => "[?] ",
        TimelineKind.Start => "[>] ",
        _ => "    ",
    };

    private static string Short(string text) { var line = text.ReplaceLineEndings(" "); return line.Length > 160 ? line[..157] + "..." : line; }

    private static async Task<int> InteractiveAsync(string[] args)
    {
        var core = Core();
        Console.WriteLine($"AGEX {AgexInfo.Version} terminal mode. Type a request and press Enter. Commands: :project <folder>, :agents, :quit");
        while (true)
        {
            var project = core.Settings.LastProject.Length > 0 ? Path.GetFileName(core.Settings.LastProject.TrimEnd('/', '\\')) : "(no project)";
            Console.Write($"{project}> ");
            var line = Console.ReadLine();
            if (line is null || line.Trim() is ":quit" or ":q" or "exit") return 0;
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith(":project", StringComparison.Ordinal)) { Project(line.Split(' ', 2).Skip(1).ToArray()); continue; }
            if (line == ":agents") { await AgentsAsync(); continue; }
            if (line.StartsWith(':')) { Console.WriteLine("Commands: :project <folder>, :agents, :quit"); continue; }
            await ExecuteAsync(line, null, null, null, yes: false);
        }
    }

    // ----------------------------------------------------- inspection

    private static async Task<DiscoveryResult> ScanAsync()
    {
        var core = Core();
        Console.Error.WriteLine("Scanning (known locations only; agents get a short version check)...");
        return await core.Discovery.FullScanAsync(null, CancellationToken.None);
    }

    private static async Task<int> AgentsAsync()
    {
        var scan = await ScanAsync();
        foreach (var group in scan.Items.GroupBy(item => item.Kind switch { DiscoveredKind.Agent or DiscoveredKind.LocalRuntime => "Agents", DiscoveredKind.Ide => "Editors", _ => "Tools and integrations" }))
        {
            Console.WriteLine(group.Key);
            foreach (var item in group.OrderByDescending(item => item.HasAdapter))
                Console.WriteLine($"  {item.Name,-24} {AgentStatusText.Code(item.Status),-22} {item.Version,-14} {Short(item.Detail)}");
        }
        return 0;
    }

    private static async Task<int> DoctorAsync()
    {
        var core = Core();
        var scan = await ScanAsync();
        Console.WriteLine(core.DiagnosticsReport(scan));
        var ready = scan.Items.Count(item => item.HasAdapter && item.Status == AgentStatus.Supported);
        Console.WriteLine(ready > 0 ? $"OK: {ready} agent(s) ready." : "PROBLEM: no agent is ready. Install and sign in to Codex, Antigravity, Claude Code, Gemini CLI or start Ollama.");
        return ready > 0 ? 0 : 1;
    }

    private static int Project(string[] args)
    {
        var core = Core();
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            return Print(core.Settings.LastProject.Length > 0 ? core.Settings.LastProject : "No project selected. Use: agex project <folder>");
        var path = Path.GetFullPath(args[0].Trim('"'));
        if (!Directory.Exists(path)) { Console.Error.WriteLine($"Folder not found: {path}"); return 1; }
        var profile = core.SettingsStore.LoadProject(path);
        profile.LastOpened = DateTimeOffset.UtcNow;
        core.SettingsStore.SaveProject(profile);
        core.SettingsStore.AddRecentProject(core.Settings, path);
        core.SaveSettings(core.Settings);
        var state = core.SettingsStore.LoadState();
        state.Project = path;
        core.SettingsStore.SaveState(state);
        return Print($"Project: {path}");
    }

    private static int Sessions(string[] args)
    {
        var core = Core();
        var action = args.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        switch (action)
        {
            case "list":
                foreach (var session in core.Sessions.List().Take(50))
                    Console.WriteLine($"{session.Id}  {SessionStatusText.Code(session.Status),-22} {session.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}  {Short(session.Title)}");
                return 0;
            case "show" when args.Length > 1:
                var shown = core.Sessions.Load(args[1]);
                if (shown is null) { Console.Error.WriteLine("Session not found."); return 1; }
                Console.WriteLine(SessionStore.ExportMarkdown(shown));
                return 0;
            case "search" when args.Length > 1:
                foreach (var hit in core.Sessions.Search(string.Join(' ', args.Skip(1))))
                    Console.WriteLine($"{hit.SessionId}  {hit.Where}: {hit.Snippet}");
                return 0;
            case "export" when args.Length > 2:
                var exported = core.Sessions.Load(args[1]);
                if (exported is null) { Console.Error.WriteLine("Session not found."); return 1; }
                File.WriteAllText(args[2], SessionStore.ExportMarkdown(exported), new UTF8Encoding(false));
                return Print($"Exported to {args[2]}");
            case "delete" when args.Length > 1:
                return core.Sessions.Delete(args[1]) ? Print("Deleted.") : Print("Session not found.");
            default:
                Console.Error.WriteLine("Usage: agex sessions [list|show <id>|search <text>|export <id> <file>|delete <id>]");
                return 2;
        }
    }

    private static async Task<int> SkillsAsync(string[] args)
    {
        var core = Core();
        var action = args.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        switch (action)
        {
            case "list":
                var installed = core.Skills.Installed();
                if (installed.Count == 0) return Print("No skills installed. See 'agex skills catalog'.");
                foreach (var skill in installed)
                    Console.WriteLine($"{skill.Id,-30} {(skill.Enabled ? "on " : "off")} {skill.Manifest.Version,-10} {SkillText.Trust(skill.Manifest.Trust)} {skill.DisabledReason}");
                return 0;
            case "catalog":
                foreach (var skill in core.Skills.Catalog().Skills)
                    Console.WriteLine($"{skill.Id,-30} {(skill.Recommended ? "*" : " ")} {skill.Name} - {skill.Author} ({skill.License})");
                return Print("* = recommended. Install with: agex skills install <id>");
            case "install" when args.Length > 1:
                var manifest = core.Skills.Catalog().Skills.FirstOrDefault(skill => skill.Id == args[1]);
                if (manifest is null) { Console.Error.WriteLine("No catalog skill with that id."); return 1; }
                Console.WriteLine($"{manifest.Name} by {manifest.Author} ({manifest.License}). It may: {string.Join(", ", manifest.Permissions.Select(SkillText.Permission))}.");
                var result = await core.Skills.InstallAsync(manifest, null, new Progress<InstallProgress>(step => Console.Error.WriteLine("  " + step.Step)), CancellationToken.None);
                return Print($"Installed {result.Manifest.Name}. Every file matched its pinned checksum.");
            case "remove" when args.Length > 1: core.Skills.Remove(args[1]); return Print("Removed.");
            case "enable" when args.Length > 1: core.Skills.SetEnabled(args[1], true); return Print("Enabled.");
            case "disable" when args.Length > 1: core.Skills.SetEnabled(args[1], false); return Print("Disabled.");
            case "update":
                return Print($"{await core.Skills.UpdateAllAsync(null, CancellationToken.None)} skill(s) updated.");
            default:
                Console.Error.WriteLine("Usage: agex skills [list|catalog|install <id>|remove <id>|enable <id>|disable <id>|update]");
                return 2;
        }
    }

    // ------------------------------------------------------ maintenance

    private static async Task<int> UpdateAsync(string[] args)
    {
        var core = Core();
        var info = await core.Updates.CheckAsync(CancellationToken.None);
        Console.WriteLine(info.Message);
        if (!info.Available || args.Contains("--check")) return 0;
        Console.Write($"Download and install AGEX {info.LatestVersion}? [y/N] ");
        if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("y" or "yes")) return 0;
        var downloaded = await core.Updates.DownloadAsync(info, null, CancellationToken.None);
        Console.WriteLine(downloaded.SignatureVerified ? "Signature and checksum verified." : "Checksum verified.");
        return core.Updates.StartInstaller(downloaded) ? Print("The installer is running; AGEX restarts when it finishes.") : Print("The installer is missing; download the release manually.");
    }

    private static int Repair()
    {
        foreach (var action in Core().Repair()) Console.WriteLine($"[{action.Status,-6}] {action.Area}: {action.Detail}");
        return 0;
    }

    private static int SettingsCommand(string[] args)
    {
        var core = Core();
        if (args.Length == 2 && args[0] == "export")
        {
            core.SettingsStore.ExportTo(args[1], includeProjects: true, core.Skills.Installed().Select(skill => skill.Id));
            return Print($"Exported to {args[1]} (no secrets included).");
        }
        if (args.Length == 2 && args[0] == "import")
        {
            var skills = core.SettingsStore.Import(args[1], includeProjects: true);
            Console.WriteLine("Imported. Your previous settings were backed up first.");
            if (skills.Count > 0) Console.WriteLine("Skills listed in the export: " + string.Join(", ", skills) + " (install with 'agex skills install <id>').");
            return 0;
        }
        Console.Error.WriteLine("Usage: agex settings export <file> | import <file>");
        return 2;
    }

    private static int Backup()
    {
        var folder = Core().SettingsStore.Backup("manual");
        return folder is null ? Print("Backup failed; see 'agex doctor'.") : Print($"Backup saved to {folder}");
    }

    private static int Uninstall(string[] args)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "install", OperatingSystem.IsWindows() ? "agex-install.ps1" : "agex-install.sh");
        if (!File.Exists(script)) { Console.Error.WriteLine("This copy of AGEX was not installed by the AGEX installer (running from source?). Delete its folder to remove it."); return 1; }
        if (!args.Contains("--yes"))
        {
            Console.Write("Remove AGEX from this computer? Agents, projects and (unless you add --purge) your AGEX data stay. [y/N] ");
            if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("y" or "yes")) return 0;
        }
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")) { ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Uninstall" } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { script, "--uninstall" } };
        if (args.Contains("--purge")) psi.ArgumentList.Add(OperatingSystem.IsWindows() ? "-Purge" : "--purge");
        psi.UseShellExecute = false;
        using var process = Process.Start(psi);
        return 0;
    }
}
