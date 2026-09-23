# Windows AI Developer Setup

The interactive multi-agent product is **AGEX**. Run `agex` to open the launcher; use `agex acceptance` for bounded orchestration acceptance and `agex final-test` for environment checks.

This repository is the source of truth for a safe, reproducible Windows environment for Codex and other coding agents.

It contains documentation, reusable instruction templates, read-only health checks, and narrowly scoped backup helpers. It does not copy credentials, Codex auth/session state, local databases, caches, or machine secrets into Git.

## Current status

Core environment ready for daily development and general worker tasks with documented limits.

- Codex, Git, uv, Caveman skills, and core artifact tools are retained.
- Serena is removed from Codex MCP, user tools, and project configuration.
- Cavemem was tested and removed after project-isolation failure.
- Context7 and Playwright MCP are configured additively with credential-free/local defaults; both passed focused benchmarks.
- Worker routing covers browser, Computer Use, screenshots, bundled document/PDF/spreadsheet skills, and browser-scoped demo media.
- Gmail read-only worker passed. GitHub connector read-only repository probe passed. Cloud-file APIs remain `AUTH REQUIRED`.
- OmniRoute remains deferred and cannot replace normal Codex.
- Native mode profiles provide Plain Codex, Caveman, Coworker, and Orchestrator launches. Antigravity dispatch stays finite, capped at two workers, and requires official `agy` CLI.
- CODEX WORKBENCH provides interactive mode composition and generic project selection. Project registry stores paths and non-secret metadata only; it never deletes project files.

## Run doctor

From PowerShell:

```powershell
agex doctor
agex dashboard -Open
```

Doctor is read-only. It reports missing tools, version-manager problems, MCP names, and permission/connectivity issues without printing secret values.

## Repository map

- `docs/` — architecture, security, memory, MCP, workflows, costs, dashboard, troubleshooting, benchmarks, and tool decisions.
- `docs/WORKER.md` — outcome-oriented worker routing, safety boundaries, and prompt patterns.
- `docs/WORKER-TESTS.md` — disposable browser, document, spreadsheet, guide, and demo checks.
- `scripts/` — doctor and explicit allowlisted backup/restore helpers.
- `config/` — safe configuration guidance and examples. Machine-specific values stay outside Git.
- `templates/` — reusable project instructions.
- `reports/` — audit output and future redacted diagnostics.

## Safety rules

Inspect before changing. Preserve existing working configuration. Back up only explicit allowlisted files before intentional edits. Never commit credentials, tokens, auth files, cookies, connection strings, or generated machine state.

Normal `codex` must continue working independently. Major installations and important configuration changes require a separate approval after architecture review.

## Common commands

```powershell
agex status
agex updates
agex mode-profiles
agex codex
agex caveman
agex coworker
agex orchestrator
agex                 # CODEX WORKBENCH menu
agex launch -Modes caveman,orchestrator -Project C:\path\to\project
agex skills
agex skills-sync
agex orchestrator-dispatch -WorkerCommand status
agex init-project C:\path\to\project -DryRun
agex init-project C:\path\to\project
```

Read [docs/DAILY-WORKFLOW.md](docs/DAILY-WORKFLOW.md), [docs/VISUAL-DEVELOPMENT.md](docs/VISUAL-DEVELOPMENT.md), and [docs/SECURITY.md](docs/SECURITY.md) before changing integrations.
