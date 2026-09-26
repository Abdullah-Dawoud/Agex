# Changelog

## 2.3.0 (prepared, not released)

### Added

- **Fast chat and modes**: every message is classified first (no model call). Greetings and small talk get one short reply with no project scan, no plan and no skills; questions get one read-only answer that reads only the files it needs; "plan ..." writes a plan without changing anything; work requests go to the team as before. The composer has a mode button: **Auto** (default), **Ask**, **Plan**, **Build**. A plan's answer has **Build this plan**.
- **Home as a chat**: a conversation list (New chat, project switcher, recent conversations of this project or all projects, reopen); a message sent while a conversation is open continues it, and earlier turns stay on screen. Answers show headings, lists, bold and code. Chat, answers and plans show a typing line instead of a task board.
- **Capability routing**: before dispatch AGEX works out what the request needs (read or change the project, commands, a browser, pages on this computer, computer control, files outside the project, the internet, other programs, connected tools) and which agents and tools really have it. Browser work goes to an agent with a built-in browser or to Codex/Claude Code with the Browser (Playwright) tool; the leader is told not to do such work while planning. A missing capability is named with its one-click fix (Enable browser, Enable computer control, Allow commands, Allow file changes, Connect required tool, Turn it on, Open Agents).
- **Local web**: when a request needs a page on this computer, AGEX serves the project over `http://127.0.0.1:<port>` (loopback only, GET/HEAD, files inside the project only, never hidden files such as `.env`), so pages that use JavaScript modules work; the web preview uses the same server for project HTML files. Your own computer (localhost) is told apart from other private-network addresses.
- **Approval modes**: Ask every time (per task), Smart approvals (sensitive actions, changes that cannot be undone, and taking over the mouse and keyboard), Trust this session (until AGEX restarts). Payments, sending messages, deleting significant data, account and security changes, publishing and changing secrets always ask. Approval dialogs offer "Trust this session".
- **Permissions** in plain words (Settings > Approvals and permissions, and the Approvals button in Home): read files, write project files, run commands, browser, computer control, network, MCP tools, external communication, destructive actions.
- **Model settings follow each agent**: the Agents page shows only settings the agent accepts: reasoning effort for Codex (per model when Codex reports it) and Antigravity (low, medium, high, max from `agy --help`); temperature and context window for Ollama; model only for Claude Code, Gemini CLI and OpenCode. Saved settings an agent cannot use are not sent.
- **Account usage**: Codex shows its plan and 5-hour and weekly usage (percent used, reset time, source, when checked) through `codex app-server`; no model quota is used and no account details are read. Other agents say "Usage not reported by this agent"; Ollama says local models have no quota.
- **Usage per request and history**: each answer shows "This request: N in · M out" with per-agent input, cached, output and reasoning tokens and any cost an agent reported (AGEX keeps no price list). Sessions shows totals for Today, This week, This project or all sessions.
- **One-click MCP connections**: Connect on a reviewed MCP tool shows source, what runs, permissions, network use, account needs and which agents use it, then checks prerequisites, installs, stores the key, hands it to Codex and Claude Code, and tests it with the MCP handshake (initialize and tools/list; no tool is called). The Autodesk bridge has the same step list (programs found, bridge found, connect, test) and says plainly that the bridge itself has no public installer.
- `agex run --mode auto|ask|plan|build`.

### Changed

- Skills on Auto are sent only when they can matter for the request (for example no PDF or notebook skill for a game fix); skills you choose are always sent. Browser and computer tools are handed out by capability routing only.
- Leaders that can read files get a short file map (150 paths, no dates; 80 in Save tokens) instead of 300 entries with dates. Maximum quality and text-only leaders keep the full list.
- Codex gets network access inside its workspace-write sandbox only when the request needs the internet or a page on this computer. Antigravity's terminal sandbox is left off only when you allowed commands and network for such a request.
- "Allow agents to run commands" moved into the permissions (settings schema 5; your choice is kept).

### Fixed

- MCP tools handed to Codex were refused inside `codex exec` ("MCP tool call requires approval, but approval policy is never"). AGEX now approves the tools of the servers it passes (after its own approvals), so the browser and other tools work.
- A web page opened from `file://` could not load its JavaScript modules, so local games and apps failed to start for agents and in the preview.
- The Browser tool saved screenshots and page snapshots into the project folder; they now go to AGEX's temporary folder.
- Chat, questions and plans no longer scan and snapshot the whole project.

## 2.2.0

### Added

- **Connections** page: agents' tools, programs on this computer, web services and MCP servers in one place, each with one clear state (Connected, Available to connect, Installed but not connected, Not installed, Sign-in required, Needs another program, Not supported yet) and its next step (Connect, Official download, Sign in, Add key, Test, Settings, Disconnect, Open project, Use as preferred editor, Learn more). Filters for connected, needs attention, available and on this computer.
- **Found existing MCP connections**: servers already set up in Claude Code, Claude Desktop, Codex, Gemini CLI, Antigravity, VS Code, Cursor, Windsurf, OpenCode and project files are listed with Import into AGEX, Use as-is and View details. Setting values stay in the source file unless the user imports them, and servers that match a reviewed catalog entry offer that pinned version instead.
- **Add connection**: search programs and services; for anything without a reviewed connection, search the public MCP Registry. Results are labelled "not reviewed", show exactly what will run, ask for required settings, and start with "ask each time".
- Program detection for Revit, AutoCAD, Navisworks, Bluebeam Revu, SketchUp, Blender, Figma, Chrome, Edge, Word, Excel, PowerPoint, LibreOffice, Obsidian, Docker, Git, GitHub CLI (with its own sign-in), Node.js, uv, Python and ffmpeg, each with an honest connection method (MCP, command line, files only, open project, or none yet).
- **Chat-first Home**: the conversation fills the page (your request, progress, the answer and a change summary); the composer sits at the bottom with compact Team, Skills, Efficiency and Agents menus; steps and tasks fold away. With no conversation, Home asks "What do you want to do?" and shows the chosen team's tools, readiness and example requests.
- **Skills per request**: Auto or your own choice for the next request, skill chips, profiles (Quick coding, Deep research, Save tokens, Architecture review, Marketing research, Local only, and your own), pin skills to a team, and (advanced) assign skills to particular agents. "Use now" on ready skills.
- **Changes** in the workspace panel: every created, modified, deleted or renamed file with lines added and removed, unified or side-by-side diffs (also for projects without Git), and a "Changes: N files +A -R" summary on each result. **Files** tab with a project tree that highlights changed files. **Connections** tab with the active team's recommended connections.
- **Smart tips** above the composer: connect a detected program the team needs, add the PDF skill for a PDF attachment, web research for research questions, code review and tests for code changes, Save tokens for large projects, Local-first when a local model is ready. At most two, each can be dismissed for good.
- Teams: each team lists the files it starts from, actions that always need your confirmation (also added to its brief) and paid tools it works alongside, based on O*NET task and technology data (reports/team-research.md). Setup reads READY / TO SET UP / OPTIONAL, with programs and services, "Set up recommended" and "Use free alternative".
- Catalog: Figma Designs (Framelink), read-only, with a Figma token (57 entries).

### Fixed

- Connecting the Autodesk AI Bridge created the skill under a different id, so its connection never showed as connected.
- Dialogs keep their buttons visible on short windows (1366 x 768 at 125%).

## 2.1.0

### Added

- Workspace panel on the right of Home and the Agent Room: Activity, Files, Preview, Diff and Computer tabs; resizable, can be hidden (Ctrl/Cmd+J) or opened in its own window; width and state are remembered.
- Attachments: Attach button, drag and drop, pasted screenshots, chips with type and size. Office text extraction, zip listings and video details/frames on this computer; a per-agent delivery plan and "These files may be sent to …" confirmation; unsupported binaries are refused.
- Job teams (Teams page): 11 purpose-based teams with typical tasks, outputs, approval level, required/recommended/optional tools, x/y readiness and a setup checklist (Install, Connect, Official download, Connect Revit/AutoCAD or Learn how, Skip, Set up everything for free no-account skills). The team's brief and approval rules go into every prompt; read-only teams never let agents write; Local Private AI uses local models only.
- OpenCode adapter (beta): `opencode run --format json`, read-only turns through its `plan` agent, attachments with `--file`, model list from `opencode models` with local and free models first, sign-in with `opencode auth login`.
- Model picker facts from the agents' own data (local/cloud, free, sees images, tools, reasoning, context); Ollama capabilities and context length from `/api/show`; a refresh button next to each picker.
- Routing & Providers: optional OpenAI-compatible endpoints for Codex (Ollama or LM Studio on this computer, OmniRoute, OpenRouter, custom), with cost, privacy and key labels, API keys in the system key store, and model listing.
- Efficiency modes (Maximum quality, Balanced, Save tokens, Local-first), shown in each request's timeline. Save tokens made the leader's first prompt 69% shorter on a 250-file project.
- Skills: cost labels (LOCAL, FREE, FREE TIER, PAID, API KEY REQUIRED) and a Cost filter; Repomix, Serena and Windows-MCP; Efficiency, Automation and Routing & Providers categories; Token Saver pack (56 entries).
- Other tools are actionable: editors get Open project and Use as preferred editor (and Open in <editor> in the workspace panel); agents without an adapter get Learn more and Request integration.
- Embedded web preview (system web engine) for HTML files and local dev servers in the workspace panel, limited to the page's folder and this computer; other links open in the browser only on request.
- Live Computer view: a screenshot of the main screen every 2 seconds while a request runs (Windows and macOS; kept in memory only, can be switched off), the window in front, and the latest reported action, next to Pause, Resume, Take control and Stop.
- `AGEX_HIGH_CONTRAST=1` previews the high-contrast theme without changing the system setting.

- Agent setup: every agent card shows one clear state (Ready, Sign-in required, Not working, Not installed, Not available on this system, Detected - not integrated) and only the actions that apply: Install, Install manually, Sign in, Check sign-in, Test connection, Refresh models, Retry detection.
- One-click install of Codex, Claude Code and Gemini CLI from their official npm packages after a confirmation that shows source, size, administrator rights and data destination; official instructions for Antigravity and Ollama.
- Sign in opens the agent's own sign-in in a terminal; AGEX re-checks sign-in without using model quota (`codex login status`, local markers for Claude Code and Gemini CLI, `agy models` on request).
- Model discovery from the agents themselves (`codex debug models`, `agy models`, Ollama `/api/tags`), cached for 12 hours, and a model picker with Auto; custom model IDs under Advanced. A saved model that disappears falls back to Auto with a notice.
- `agex models [agent] [--refresh]`; `agex agents` shows install and sign-in state.
- Skills catalog expanded from 19 to 53 reviewed entries (36 instruction skills, 17 MCP tools), including Caveman, more Superpowers, Trail of Bits security skills, deploy skills, and Brave, Tavily, Firecrawl, Exa, Notion, Linear, Sentry, Supabase, Microsoft Learn, AWS, Cloudflare and DeepWiki tools.
- Skills page: search, filters (category, tier, trust, installed, agent, account, system), sorting, details view, nine packs, readiness per skill (Ready, Account required, Dependency missing, …), Add key / Test connection / Disconnect for account-based tools, and the tool's own sign-in for CLI-based ones.
- docs/AGENT_INSTALLATION.md.

### Changed

- Trust labels: Official, AGEX Curated, Community, Local. Community skills start risky permissions at "Ask each time".
- The repository is now `Abdullah-Dawoud/Agex`: git remote, installers, updater and README use the new name; install markers written by 2.0.0 installers are migrated once.
- One broken catalog entry is skipped instead of hiding the whole catalog.

### Fixed

- Batch files in folders with spaces (for example an nvm folder under "Author Software") failed to start; cmd.exe now gets its own quoting, and npm shims that only start a native program are run directly.
- An MCP tool whose key was missing was still given to agents and failed inside them; it is now left out until connected.

## 2.0.0

AGEX is now a cross-platform desktop application (Windows, macOS, Linux) written in C# on .NET 10 with Avalonia. The Windows-only WPF app and PowerShell engine were replaced.

### Added

- Platform layer (`IPlatformService`) for Windows, macOS and Linux: data folders, program discovery (including the login-shell PATH on macOS/Linux), secure storage (DPAPI, Keychain, Secret Service), notifications, start with computer, opening files, folders and terminals.
- Desktop app: guided six-step first run, Home with timeline, Projects, Agents, Agent Room (filters, search, copy, expand, follow live), Sessions, Skills, Settings with Diagnostics; light, dark and high-contrast themes that follow the system, text size setting, keyboard shortcuts, command palette, macOS menu bar, safe mode, single instance.
- Agents: Claude Code, Gemini CLI and Ollama adapters (beta) besides Codex and Antigravity; status taxonomy (Ready, Not installed, Sign-in required, Not working, Not available on this system, Paused after errors, Unknown); teams and routing presets including Local only; privacy labels and data destinations; usage shown only when an agent reports it.
- Engine: leader plans with task dependencies and parallel work, one repair round, agent questions to the user, messages between agents, approval before file changes, Git snapshot and undo per request, independent verification of changed files, direct-answer mode for text-only teams.
- Skills: curated catalog of 19 skills (14 instruction skills pinned to commits with per-file SHA-256, 5 MCP servers), one-click install, custom skills from GitHub, zip or folder, permissions, trust levels, updates, compatibility checks, re-verification at start.
- Sessions: search, export, clone, continue, retry with another agent, retention settings, crash recovery.
- `agex` command: `run`, `doctor`, `agents`, `project`, `sessions`, `skills`, `update`, `repair`, `settings export|import`, `backup`, `uninstall`.
- Updates from GitHub Releases for the running platform and architecture, verified against `SHA256SUMS.txt` and, once a release key is published, an ECDSA signature.
- Installers for Windows (`agex-install.ps1`) and macOS/Linux (`agex-install.sh`); release workflow for Windows x64/ARM64, macOS arm64/x64 (zip and DMG) and Linux x64/ARM64; signing hooks that stay off without real certificates.
- Settings migrations from the 1.x format, with a backup.

### Fixed

- Codex was located inside the Codex desktop app instead of the CLI on PATH.
- The leader did not read project files and was not told the project folder; Antigravity searched the whole drive.
- Leader JSON followed by extra text was rejected.
- A second AGEX process marked a running session as interrupted.
- Non-English text written by agents through Windows PowerShell 5.1 was garbled.
- A crash in a page could close the app; double-clicking Send could start two requests.
- `agex version` migrated settings.
- Gemini CLI sign-in problems were shown as `"error": {` instead of a sign-in message.

### Removed

- WPF desktop app, PowerShell engine and worker scripts, and their tests and reports.
- Build output folders (`bin/`, `obj/`) are now ignored by Git.

## 1.0.0

First release as **AGEX**, a local desktop control center for multiple AI coding agents.

### Added

- Desktop app (WPF, .NET Framework 4.8): Home, Projects, Agents, Sessions, Agent Room (live collaboration feed and agent graph), Settings, Diagnostics, first-run scan and setup, light and dark themes.
- Agent adapters for Codex CLI and Antigravity CLI, with capability-aware routing (file edits only go to agents allowed to write).
- Allowlisted discovery of agents, IDEs, Git and MCP configuration, with honest "Supported / Detected, not integrated / Unavailable" states.
- Collaboration messages (assignment, result, question, answer, review, revision request, status, system) built only from explicit agent output and AGEX events.
- Session history with request, tasks, messages, changed files and result.
- One-command per-user installer with SHA-256 verification; `agex update`, `agex repair`, `agex uninstall`, `agex doctor`, `agex agents`, `agex project`; release workflow.
- Settings and data in `%LOCALAPPDATA%\AGEX`, with one-time migration from the previous settings location.

### Fixed

- Prompts with non-ASCII text failed ("input is not valid UTF-8"); agent stdin and stdout are now UTF-8 without BOM, and the Antigravity result contract is encoding-proof.
- Wrong PARTIAL status and "0/0 tasks, assignments 1" after a leader failure; outcomes are now COMPLETE, COMPLETE_WITH_FALLBACK, PARTIAL, FAILED, START_FAILED, UNVERIFIED or CANCELLED by precise rules.
- A leader answer without tasks was rejected as an invalid plan.
- No fallback when an agent failed to start; AGEX now retries once with the other agent and pauses failing agents.
- Terminal flicker, cursor jumps and lost drafts; single diff-based renderer.
- The desktop engine could hang when started agent processes inherited a stdin handle with a pending read.
- A finished worker could remain shown as running.

### Changed

- Product renamed to AGEX everywhere; the previous name is no longer used.
- Legacy dashboard renderer, dead launcher code and the unused Codex prompt hook were removed.
