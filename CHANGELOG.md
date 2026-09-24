# Changelog

## 2.1.0 (prepared, not released)

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
