# Changelog

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
