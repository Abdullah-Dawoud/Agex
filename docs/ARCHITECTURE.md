# Architecture

AGEX 2 is one .NET 10 code base with three programs:

```text
src/Agex.Core      platform-independent engine (class library)
src/Agex.Desktop   desktop app (Avalonia UI)         -> AgexDesktop(.exe)
src/Agex.Cli       terminal command                   -> agex(.exe)
tests/             xUnit tests + a fake agent that speaks every agent protocol
```

The desktop app and the CLI use the same engine in-process and the same data folder, so a request started in one appears in the other's history.

## Why .NET 10 + Avalonia

AGEX 1 was Windows-only (WPF on .NET Framework 4.8 with a PowerShell engine). Cross-platform needed a new UI stack and an engine that does not depend on Windows PowerShell. Options considered:

| Option | Why not chosen / chosen |
| --- | --- |
| Electron / Tauri (web UI) | Most comparable tools use Electron. Rejected to keep the footprint smaller and the engine in one language; Tauri would have split the engine (Rust/TS) from the existing .NET logic. |
| .NET MAUI | No Linux desktop support; macOS runs through Mac Catalyst. |
| Uno Platform | Viable; Avalonia has the larger desktop-first community and simpler code-only UI. |
| **Avalonia 12 (chosen)** | Mature cross-platform desktop UI for .NET (MIT); Windows, macOS and Linux from one code base; Skia rendering (consistent look, high-DPI/Retina aware); native accessibility bridges (UI Automation on Windows, NSAccessibility on macOS, AT-SPI on Linux); native macOS menu and Dock behaviour; self-contained packages with no runtime to install. |

.NET 10 is the current LTS release (supported until November 2028).

## Engine modules (Agex.Core)

| Namespace | Role |
| --- | --- |
| `Platform` | `IPlatformService` with `WindowsPlatformService`, `MacPlatformService`, `LinuxPlatformService`: data/log/cache folders, PATH discovery (Windows registry PATH; login-shell PATH on macOS/Linux), executable lookup (PATHEXT vs execute bit), known install locations, opening files/URLs/folders/terminals, notifications, start-at-login, and `ISecureStore` (DPAPI, Keychain via `security -i`, Secret Service via `secret-tool`, clearly-labelled file fallback). Nothing else in Core checks the OS, except two prompt hints that exist only because of Windows PowerShell's encoding. |
| `Runtime` | `ProcessRunner` — the only place AGEX starts programs: exact argument lists (no shell), UTF-8 stdin without BOM, stdout/stderr read concurrently, capped capture, timeouts, cancellation, whole-process-tree kill, owned-process tracking, sanitized command lines; npm `.cmd` shims unwrapped to `node script.js`, other batch files refused if an argument contains cmd.exe metacharacters. `AgexLog` (JSON lines, secrets redacted, 30 files kept). `Redactor`. |
| `Agents` | `IAgentAdapter` and the five adapters, `AgentRegistry` (detection cache, health with cooldown), `Discovery` (allowlisted scan, progressive health checks), `ToolLocator`, MCP config summary. |
| `Orchestration` | `RequestClassifier` and `RequestIntent` (mode and needed capabilities), `CapabilityRouting` and `ApprovalRules` (who can do it, what is missing, when to ask), `SkillRelevance` (Auto skills that matter), `RequestEngine` (direct route for chat, questions and plans; plan → approve → schedule → verify → deliver messages → review for work), `LeaderPlanParser` (canonical task ids, dependency resolution by id/title/alias, cycle detection, repair links), `ExecutorReply`, `Router` (presets, leader choice, local-only filter), `IEngineHost` (approval and questions, implemented by the desktop app and the CLI). |
| `Sessions` | Session model (messages, timeline, tasks, runs, artifacts, changes, usage, questions, snapshot), `SessionStore` (one JSON file per session + index, search, retention, crash recovery that respects sessions still owned by another AGEX process), Markdown export. |
| `Skills` | Catalog (embedded, curated), `SkillManager` (validate, check compatibility, download with SHA-256, install, update, remove, custom skills, MCP servers, startup validation), `SafeArchive`. |
| `Settings` | Versioned settings with step-by-step migrations and backups, per-project profiles, crash-recovery state, export/import. |
| `Projects` | Ignore-aware project listing and change detection; `GitService` snapshots under `refs/agex/snapshots/` built with a temporary index (working tree, index and branches untouched), diffs, restore. |
| `Updates` | GitHub release lookup for this OS/processor, download, SHA-256 and optional ECDSA signature check, handing over to the bundled installer. |
| `AgexCore` | Composition root used by both programs; startup checks, team building, diagnostics, repair. |

## A request

```text
User request
  -> RequestClassifier (rules, no model call): Chat | Question | Plan | Build, and the capabilities it needs
  -> CapabilityRouting: which agents and tools have them (built-in browser, Playwright/Windows-MCP tools),
                        what is missing and its fix; LocalWebServer on 127.0.0.1 when a local page is needed
  -> RequestEngine
       Chat / Question / Plan -> one read-only run with a small prompt (no project scan, no plan protocol)
       Build:
       leader prompt (goal, project folder, file list, git status, team abilities, routing guidance,
                      earlier results, agent messages, user answers)
       -> leader agent -> JSON plan (CONTINUE | COMPLETE | BLOCKED | NEEDS_INPUT)
            invalid plan -> one repair attempt
       NEEDS_INPUT -> IEngineHost.AskUserAsync -> answer added to the context
       CONTINUE -> tasks with dependencies and owned files
            scheduler: dependencies satisfied, agent healthy and able, per-agent concurrency,
                       no overlapping files, approval before the first write (+ Git snapshot)
            executor prompt -> agent -> {"result", "messages"}
            verification of claimed file changes against the folder
            agent-to-agent questions delivered in short read-only turns (max 8)
       -> leader reviews (max 6 rounds) -> outcome
  -> events (messages, timeline, tasks, agent states) -> UI / terminal
  -> session saved after every step
```

Text-only teams (Ollama only) take a direct-answer path instead of planning.

**Nothing the UI shows is invented.** Messages come from what agents returned (their result text and their explicit `messages` array) or from what AGEX did (assignments, notices, verification). Model reasoning is never requested, read or displayed: Codex `reasoning` items are ignored, Ollama `thinking` fields and `<think>` blocks are removed.

## Desktop app

Code-built views (no XAML except the design system in `App.axaml`): `MainWindow` (navigation, top bar, team panel, dialogs, toasts, command palette, shortcuts, native macOS menu, single instance via named mutex + pipe), pages under `Pages/`, `Workspace` as the controller between engine events and views. Theme tokens are resources with separate Light, Dark and High-contrast dictionaries; text size scales the type ramp. Long Agent Room conversations use a virtualized list; a session keeps at most 5,000 messages (oldest tool events go first).

## Data

See [INSTALL.md](INSTALL.md#where-agex-keeps-data). Everything is JSON, written atomically. Settings contain no machine identity and no secrets, so they can be exported and, later, synced.

## Extension points

- New agent: implement `IAgentAdapter` (or derive from `CliAgentAdapter`) and register it in `AgentRegistry.CreateDefault`.
- New skill: a SKILL.md folder, a package, or a catalog entry.
- New platform behaviour: `IPlatformService`.
- New settings field: add it with a default; add a migration step when a field changes meaning.

Details: [DEVELOPMENT.md](DEVELOPMENT.md).
