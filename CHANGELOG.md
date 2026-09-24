# Changelog

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
