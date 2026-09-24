# AGEX finalization

Date: 2026-09-24. Version: 1.0.0.

| Item | Result |
| --- | --- |
| PRODUCT NAME | AGEX (AGEX AI CONTROL CENTER) |
| LEGACY DAWOUD REFERENCES | 0 product references. 3 migration-only references remain on purpose: the old settings filename in `Initialize-AgexStorage` (`scripts/agex-common.ps1`), the retirement of old launcher shims (`scripts/agex-maintenance.ps1`, `install/agex-install.ps1`), and the migration note in `docs/INSTALL.md`. The GitHub owner name in release URLs is the account name, not the product. |
| DESKTOP APP | READY |
| CLI | READY |
| AGENT DISCOVERY | READY |
| SUPPORTED AGENTS | Codex CLI, Antigravity CLI |
| DETECTED BUT UNSUPPORTED (this machine) | Claude Code CLI, Gemini CLI, OpenCode, Ollama; IDEs: Visual Studio Code, Cursor, Antigravity IDE |
| COLLABORATION VIEW | READY (Agent Room feed and graph) |
| SESSION HISTORY | READY |
| ONE-COMMAND INSTALL | READY BY SOURCE: installer, release build and release workflow are finished and tested with a local package; no GitHub release is published yet (needs a `v1.0.0` tag push by the owner). |
| REAL CODEX | PASS |
| REAL ANTIGRAVITY | PASS |

## What was built

- **Desktop app** (`desktop/*.cs`, WPF on .NET Framework 4.8, compiled with the in-box `csc.exe`). Pages: Home (request, live progress, Result, Tasks, Changes with diffs, Activity), Agent Room (live messages with filters by agent and task, expand, copy, open, follow live; orchestration graph), Agents (enable, test connection, leader, strategy, models, Codex write permission; detected tools, IDEs, integrations), Projects, Sessions, Settings (General, Agents and routing, Projects, Appearance with dark theme, Advanced, Privacy), Diagnostics (engine, logs, repair, what ran), and first-run scan and setup. A right panel shows each agent's live state. Single instance; the engine restarts if it dies; UI errors are contained.
- **One engine** for desktop and terminal (`scripts/agex-primary.ps1`). The desktop drives it through a JSON-lines protocol (`-Serve`).
- **Adapters and discovery** (`scripts/agex-adapters.ps1`): Codex and Antigravity adapters with capabilities and write policy. Allowlisted discovery never executes unsupported tools.
- **Capability-aware routing**: enabled and health checks per task; file-writing tasks go only to agents allowed to write; the leader is told which agents are available.
- **Collaboration protocol and sessions** (`scripts/agex-session.ps1`): typed messages built only from explicit output; sessions saved with tasks, messages, events, runs and changes.
- **Storage** in `%LOCALAPPDATA%\AGEX`, with one-time migration of the old settings and project list; a custom `AGEX_HOME` is isolated.
- **CLI** (`agex.ps1`, `bin/agex.cmd`): `agex` (desktop), `--cli`, `doctor`, `agents`, `project`, `update`, `repair`, `uninstall`, `version`.
- **Install, update, repair, uninstall** (`install/agex-install.ps1`, `scripts/agex-maintenance.ps1`, `tools/build-release.ps1`, `.github/workflows/release.yml`): per-user install, SHA-256 verified, PATH and Start Menu registration, retirement of old launcher shims (with backup), uninstall of AGEX-owned files only.
- **Repository cleanup**: all product names now AGEX; files renamed `scripts/agex-*.ps1`; the old dashboard renderer, dead launcher code and the dead Codex prompt hook (and its installer) removed; machine-specific audit reports and ACL dumps removed; the old environment docs moved to `docs/environment` with paths sanitized; README, INSTALL, USAGE, AGENTS, ARCHITECTURE, TROUBLESHOOTING, SECURITY, DEVELOPMENT, LICENSE, SECURITY policy, CHANGELOG, CONTRIBUTING and THIRD_PARTY_NOTICES added or rewritten.

## Bugs found and fixed in this pass

- The desktop engine hung as soon as an agent was launched: a pending synchronous read on the stdin pipe blocked handle inheritance. Fixed by reading stdin only when `PeekNamedPipe` reports data.
- The Antigravity result travelled through `orchestrator.ps1` stdout in the console code page, so `✓` became `�`. The leader then believed a correct file was wrong and ran 2 unneeded repair rounds (found in the real Antigravity run). The result contract is now ASCII-escaped JSON.
- .NET wrote a BOM ahead of the first protocol line (also fixed at the engine side).
- A finished worker could remain "Running" in the side panel.
- Old launcher shims in `WindowsApps` shadowed the installed `agex` command.
- A custom `AGEX_HOME` imported real settings. During testing this let a fake agent write two files into a real project; both were removed right away, and isolation is now enforced.
- Plus layout fixes found through screenshots (right panel threshold, wrapping, clipped headlines).

## Tests

- `scripts/agex-engine.tests.ps1`: 27 passed, 0 failed. Covers the process runner (UTF-8, deadlock, timeout, cancel, quoting, shim), sessions (success, fallback, both fail, unavailable agents, partial, 2 parallel workers, no orphans), cancel, the desktop protocol (scan, request, messages, history, shutdown; settings with a disabled agent), capability routing, storage isolation, the CLI (version, agents, doctor), the installer (verified install, tampered package refused, uninstall), the terminal frame and the editor.
- `scripts/agex-ui.tests.ps1`: PASS. `scripts/agex-graph.tests.ps1`: PASS. `scripts/agex-acceptance-fixture.tests.ps1`: PASS.
- Desktop acceptance, driven through UI Automation with screenshots: launch, first-run scan, agent selection, project, send request, live Agent Room, graph, task list, result, changes, cancel, retry, repair, settings, dark theme, resize, restart with session history, clean close (no engine or agent processes left).
- Real agents, in a disposable folder with an isolated profile:
  1. Codex leader, direct question on a file with Arabic and accented text: COMPLETE, text intact both ways, zero-task answer accepted.
  2. Codex made unavailable, then an Antigravity file task: COMPLETE_WITH_FALLBACK, file correct, no leftover processes.
  3. Antigravity direct question returning `✓`: COMPLETE, symbol intact after the fix.
  4. Desktop app, Codex leader, Antigravity writes a file, Codex verifies: COMPLETE, live collaboration visible in the Agent Room.
- Installed from the release package on this machine: `agex` resolves to the install, `agex doctor` is all OK with real agents ready, and the installed desktop launches and closes cleanly.

## Known issues

- No GitHub release exists yet, so the one-command URL works only after the owner pushes tag `v1.0.0` (the workflow then builds and publishes).
- The Antigravity path still goes through `orchestrator.ps1` and `worker-run.ps1` (the existing, validated worker chain) with its own stream reader. The engine supervises the chain through the central runner.
- Each Antigravity dispatch runs `agy models` as an availability probe, which adds a few seconds per call.
- `C:\ProgramData\OpenAI\Codex\requirements.toml` on this machine still lists the old managed Codex hook. That hook was already inert, because it calls `pwsh.exe`, which is not installed. Removing it is a system change outside the repository; delete the file (or its hook sections) if Codex reports hook errors.
- The PowerShell profile on this machine still defines a `dawoud` function that calls the source checkout's `setup.ps1` (which now opens AGEX). It is user configuration outside the repository and was left unchanged.
- The license is MIT with "AGEX contributors" as holder. The owner should confirm or change it before publishing.
- Terminal and desktop count wide CJK characters as one column.

## Commit

Recorded in git history: see `git log --oneline -1`.
