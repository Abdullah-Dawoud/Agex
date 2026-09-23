# Codex Modes

Four independent experiences use native Codex profile overlays. CODEX WORKBENCH composes capabilities at launch and selects any project.

| Mode | Launch | Scope | Disabled by default |
|---|---|---|---|
| Plain Codex | `agex codex` | Base Codex config; ordinary coding | No mode-added tools or delegation |
| Caveman | `agex caveman` | `caveman` profile; normal coding plus user skill | Context7 and Playwright MCP |
| Coworker | `agex coworker` | General worker routing | Unrelated tools remain task-scoped |
| Orchestrator | `agex orchestrator` | Codex boss; optional Antigravity worker | Unrelated tools remain task-scoped |

## AGEX AI CONTROL CENTER

Run `agex` for interactive AGEX mode, project selection, model selection, leader selection, and workload policy. Run `agex launch -Modes caveman,orchestrator -Project "C:\path\to\project"` for fast launch.

Modes and projects stay independent. Combined modes generate one temporary profile from existing profile instructions, remove it after Codex exits, and enable Coworker MCP only when Coworker is selected. Plain mode uses base Codex only.

AGEX saves project labels, paths, and non-secret metadata in `%USERPROFILE%\.codex\workbench-projects.json`. It retains `%USERPROFILE%\.codex\dawoud-settings.json` as the existing preferences filename for compatibility. Add, rename, remove, or open a path once from project menus. Remove only changes registry; it never deletes project files. Registries contain no credentials, tokens, passwords, or file contents.

## Native boundary

Codex supports named profile files at `$CODEX_HOME/<name>.config.toml` and selects them with `codex --profile <name>`. Base `config.toml` remains unchanged. Profiles override only mode instructions and Context7/Playwright MCP enablement.

Codex reads `AGENTS.md` by scope and loads standard `SKILL.md` directories. Antigravity uses same open skill format and discovers workspace skills under `.agents/skills`; its CLI global skill path is `~/.gemini/antigravity-cli/skills`. AGEX passes selected Codex model/effort to Codex and selected Antigravity model/effort to each worker when supported.

## Orchestrator boundary

`scripts/orchestrator.ps1` starts one finite `agy` headless run per routed dispatch. It stores a status ledger under `reports/workers/<run-id>/status.json` plus a lightweight executor ledger under `reports/telemetry/events/`. Each telemetry record distinguishes `CODEX`, `CODEX_SUBAGENT`, and `ANTIGRAVITY`, records timestamps, duration, status, worker PID/model/effort, verified `agy.exe` path, and whether AGY returned output. It caps running workers at 2, exits each child after one task, and never runs a permanent daemon. Worker tasks are read-only by default; delegated writes require explicit isolated worktree design by the parent Codex task. Exact per-agent token metering unavailable; workload percentage is routing policy.

Commands:

```powershell
agex orchestrator-dispatch -WorkerCommand dispatch -Task "Map the fixture server and report entry points. Do not edit files."
agex orchestrator-dispatch -WorkerCommand status
agex orchestrator-dispatch -WorkerCommand cleanup
```

Use `-Wait` when caller needs terminal state plus automatic `AGEX EXECUTION REPORT`. AGEX Workbench records Codex session window time; direct dispatch records dispatch/coordination time and states this measurement limit. Cavecrew and other Codex subagents remain `CODEX_SUBAGENT`, never `ANTIGRAVITY`.

Antigravity CLI authentication is user-owned. Run official `agy` login once in an interactive session when required. Never copy browser tokens or credentials into this repository.

## Rollback

Remove `caveman.config.toml`, `coworker.config.toml`, and `orchestrator.config.toml` from `$CODEX_HOME`, or restore them from the profile backup reported by `scripts/mode-profiles.ps1`. Base Codex config backup remains under `%LOCALAPPDATA%\AI-Developer-Setup\backups`.
