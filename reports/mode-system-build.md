# Mode System Build Report

Status: final verification complete; bounded real Antigravity delegation and parallel-worker acceptance passed
Started: 2026-09-20

## Baseline

- Repository: `C:\Users\isc\OneDrive\Documents\ChatGPT\setup`
- Git: repository has no commits; working tree contains untracked setup files.
- Existing entry points: `setup.ps1`, `scripts/doctor.ps1`, `scripts/dashboard.ps1`, backup/restore scripts.
- Existing docs: architecture, MCP, worker, skills, recovery, benchmarks, and Serena-removal reports.
- Existing fixture: `fixtures/ui-test` with screenshots and a local server.
- Existing worker evidence: `reports/worker-tests` contains DOCX, XLSX, WebM, PNG, and JSON outputs.
- Initial safety constraint: preserve current Codex, Git, GitHub CLI, Context7, Playwright, node_repl, browser/computer-use, document/PDF/spreadsheet workflows, setup, doctor, dashboard, backup/restore, and projects.
- Serena requirement: remain removed; search before any change.

## Change log

| Time | Change | Validation |
|---|---|---|
| 2026-09-20 | Created this report before implementation. | File creation succeeded. |
| 2026-09-20 | Audited repository, Codex config, MCP entries, installed skills, Antigravity state, tools, and prior reports. | Read-only inspection completed. |
| 2026-09-20 | Added native Codex mode profile templates, launcher commands, worker ledger, skill audit/sync, doctor checks, and dashboard modes. | PowerShell parse passed; profile `codex --profile ... --help` passed. |
| 2026-09-20 | Backed up base Codex config and installed additive mode profiles. | Backup created outside repository; profiles present and doctor reports READY. |
| 2026-09-20 | Synced 21 compatible standard skills to Antigravity CLI and desktop global skill paths. | Existing targets absent; sync completed without overwriting existing content. |
| 2026-09-20 | Re-ran existing browser worker fixture. | E2E and screenshot/video capture passed; bundled Node path required because NVM shim has no active version. |
| 2026-09-21 | Installed official Antigravity CLI v1.2.7 and fixed worker path resolution plus concurrent stream handling. | Auth probe and bounded delegation passed; no orphan worker remained. |
| 2026-09-21 | Ran two bounded workers concurrently. | Both returned success; max observed worker number was 2; zero orphan workers. |

## Pending sections

## Audit

- Base Codex config: `C:\Users\isc\.codex\config.toml` exists. It retains current model, plugins, `node_repl`, Context7, and Playwright MCP entries.
- Global Codex `AGENTS.md` exists but is empty. Repository `AGENTS.md` is active and contains the setup safety rules.
- User skills: 21 `SKILL.md` folders under `C:\Users\isc\.agents\skills`.
- Codex skill directory: only system skills under `C:\Users\isc\.codex\skills`.
- Antigravity desktop: installed at `C:\Users\isc\AppData\Local\Programs\Antigravity\Antigravity.exe`; GUI state exists under `C:\Users\isc\.gemini`.
- Antigravity CLI: `agy` not found on PATH or at `%LOCALAPPDATA%\agy\bin\agy.exe` before implementation.
- Serena: no active Serena command or config was added. Historical Serena text remains only in prior reports/docs as removal evidence.
- Current external process snapshot contained many pre-existing Codex/Node processes. No process was changed during audit.

## Architecture decision

Use native Codex profile overlays plus one PowerShell launcher. Codex official docs support `$CODEX_HOME/<profile>.config.toml` and `codex --profile <name>`. Base `config.toml` stays unchanged. Plain `codex` remains default.

- Plain: base configuration only.
- Caveman: profile injects automatic Caveman behavior and disables Context7/Playwright MCP.
- Coworker: profile adds worker-routing instructions and keeps existing worker MCP available.
- Orchestrator: profile keeps Codex as boss and routes finite worker dispatch through local scripts.
- Worker lifecycle: one `agy` headless process per dispatch, maximum two running, status ledger under `reports/workers`, no daemon.
- Visibility: existing generated HTML dashboard now has Modes and AI Workers sections. It shows operational metadata only, not private reasoning.

## Official documentation reviewed

- OpenAI Codex CLI: https://developers.openai.com/codex/cli/
- OpenAI profile configuration: https://learn.chatgpt.com/docs/config-file/config-advanced
- OpenAI configuration reference: https://learn.chatgpt.com/docs/config-file/config-reference
- OpenAI AGENTS.md discovery: https://developers.openai.com/codex/guides/agents-md/
- OpenAI skills: https://developers.openai.com/codex/skills/
- Google Antigravity overview: https://developers.googleblog.com/build-with-google-antigravity-our-new-agentic-development-platform/
- Google Antigravity download: https://www.antigravity.google/download
- Google Antigravity CLI headless mode: https://www.antigravity.google/docs/cli/headless/
- Google Antigravity CLI agents: https://www.antigravity.google/docs/cli/commands/agents/
- Google Antigravity skills: https://www.antigravity.google/docs/skills

Relevant findings: official `agy -p` headless mode supports `json` and `stream-json`, cached credentials, `--sandbox`, model/agent selection, and clean exit. Official skill format is standard `SKILL.md`; Antigravity CLI global path is `~/.gemini/antigravity-cli/skills`; Antigravity desktop global path is `~/.gemini/config/skills`.

## Backups

- Base Codex config backup: `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\20260920-233143\config.toml`.
- Mode profile replacement backup: `C:\Users\isc\.codex\mode-profile-backup-20260920-233143`.
- Antigravity skill targets did not exist before sync; no pre-existing skill content needed backup.
- No auth, cookies, session databases, or credential files were copied.

Restore base Codex config with `.\scripts\restore-config.ps1 -Path "$env:USERPROFILE\.codex\config.toml" -BackupFile "C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\20260920-233143\config.toml"`.

## Implementation

- `setup.ps1` commands: `codex`, `caveman`, `coworker`, `orchestrator`, `mode-profiles`, `skills`, `skills-sync`, and `orchestrator-dispatch`.
- Profile templates: `config/codex/profiles/*.config.toml`.
- Worker scripts: `scripts/orchestrator.ps1` and `scripts/worker-run.ps1`.
- Skill audit/sync: `scripts/skills-sync.ps1`.
- Doctor checks: mode profiles, skill sync, Antigravity CLI, Orchestrator mode, existing MCP, and Serena REMOVED.
- Dashboard: Modes and AI Workers sections added to existing `reports/dashboard.html` generator.
- Docs: `docs/MODES.md`, daily workflow, README, and reuse decisions updated.

## Skills compatibility matrix

| Source | Codex | Antigravity CLI | Antigravity desktop | Decision |
|---|---:|---:|---:|---|
| `C:\Users\isc\.agents\skills\<skill>\SKILL.md` | READY | READY after mirror | READY after mirror | Codex user skills are canonical. |
| Standard frontmatter and instructions | READY | READY | READY | Official common format. |
| Skills requiring Codex-only app tools | READY | NOT COPIED by policy when detected | NOT COPIED by policy when detected | Keep Codex-only. |

Current heuristic audit classified 21 installed skills as standard-compatible. Sync preserves source and creates target backups on future drift.

## GitHub reuse decisions

Research covered `InonB2/multi-agent-orchestration`, `andyyaro/orkestra`, `Augani/agent-orchestrator`, and `Sora-bluesky/antigravity-orchestra`. All are rejected for installation: extra framework/process/trust surface exceeds current requirement. Useful ideas retained: adapter boundary, finite task receipts, isolated worktrees, explicit permissions, and lifecycle status.

See `docs/REUSE-DECISIONS.md` for repository, purpose, maintenance signal, license/security notes, and USE/ADAPT/REJECT decisions.

## Installs

Installed: official Antigravity CLI v1.2.7 at `C:\Users\isc\AppData\Local\agy\bin\agy.exe`; additive native Codex profiles and safe skill mirrors.

Rejected/deferred: no third-party orchestrator was installed. Official CLI installation used the inspected Windows installer from `https://www.antigravity.google/download`; no unofficial installer, browser-token extraction, or auth bypass was used.

## Tests and benchmark

- PowerShell parser: PASS for all repository `.ps1` files.
- `codex --profile caveman --help`: PASS.
- `codex --profile coworker --help`: PASS.
- `codex --profile orchestrator --help`: PASS.
- `.\setup.ps1 skills`: PASS; reports installed skill inventory.
- `.\setup.ps1 mode-profiles`: PASS; base config backup plus three profiles.
- `.\setup.ps1 doctor -Json`: PASS; 0 ERROR. Mode profiles READY, Skill sync READY, Serena REMOVED. Antigravity CLI NOT INSTALLED; Orchestrator AUTH REQUIRED.
- `.\setup.ps1 dashboard`: PASS; existing HTML dashboard regenerated.
- Plain live Codex prompt benchmark: PASS with read-only network-approved run; returned `MODE_OK`.
- Caveman live benchmark: PASS; returned `CAVEMAN_OK` and first response stated Caveman skill active, then read the skill and README.
- Coworker live benchmark: PASS; returned `COWORKER_OK` and used native file read only.
- Orchestrator profile live benchmark: PASS; returned `ORCH_OK` with delegation disabled for synthetic prompt.
- Initial sandbox-only live benchmark: blocked by socket error 10013. Network-approved rerun passed. Non-blocking existing skill metadata warnings reported invalid relative icon paths; no mode failure.
- Coworker browser fixture: PASS. `scripts/end-to-end-worker-smoke.mjs` passed navigation, fill, save, deliberate failure detection, recovery, and final save. Screenshot/video capture passed with bundled Playwright.
- Orchestrator real Antigravity task: BLOCKED; `agy` not installed.

## Resource and rollback checks

- No permanent service or daemon created.
- Orchestrator refuses dispatch when `agy` is absent and reports exit code 2.
- Running-worker cap is 2.
- `cleanup` stops only PIDs recorded in worker status ledger.
- No worker ledger exists after blocked dispatch; `agy` CLI process count is 0. Existing Antigravity IDE process group (16 pre-existing GUI processes) was observed and left unchanged.
- Failed local Codex stdin benchmark left three test processes; exact test PIDs were stopped and final recent-test process check passed.
- Profile rollback is documented above. Skill mirrors can be removed from `C:\Users\isc\.gemini\antigravity-cli\skills` and `C:\Users\isc\.gemini\config\skills` after backing up those exact folders.

## Final usage

```powershell
.\setup.ps1 codex
.\setup.ps1 caveman
.\setup.ps1 coworker
.\setup.ps1 orchestrator
.\setup.ps1 skills
.\setup.ps1 skills-sync
.\setup.ps1 doctor
.\setup.ps1 dashboard
```

For orchestration after `agy` is installed and authenticated:

```powershell
.\setup.ps1 orchestrator-dispatch -WorkerCommand dispatch -Task "Map independent subsystems. Do not edit files."
.\setup.ps1 orchestrator-dispatch -WorkerCommand status
```

## Antigravity CLI Installation

- Official Windows installer fetched from `https://antigravity.google/cli/install.ps1`, inspected, and executed.
- Installed binary: `C:\Users\isc\AppData\Local\agy\bin\agy.exe`.
- Version: `1.2.7`.
- The installer did not add the bin directory to the current/user PATH. The orchestrator resolves the official per-user binary directly, so no PATH mutation was required.

## Authentication Verification

- Safe headless probe: `agy -p 'Return exactly AUTH_PROBE_OK...' --output-format json --sandbox`.
- Result: `status=SUCCESS`, response `AUTH_PROBE_OK`, one turn.
- Cached authentication is valid. No login, MFA, CAPTCHA, token copy, or credential file was touched.

## Real Delegation Test

- Route: Codex setup entry point -> `scripts/orchestrator.ps1` -> `scripts/worker-run.ps1` -> official `agy`.
- Task was deliberately bounded to prevent repository disclosure: do not read/inspect/modify/transmit workspace files; return `WORKER_SAFE_OK`.
- Result: `DONE`, exit code `0`, result `WORKER_SAFE_OK`, elapsed about 6 seconds.
- Fixes made during verification: fallback `FileInfo.FullName` resolution; concurrent stdout/stderr draining; stale-PID ledger reconciliation; child output logs.

## Parallel Worker Test

- Two independent bounded tasks dispatched back-to-back.
- Worker results: `PARALLEL_A_OK` and `PARALLEL_B_OK`.
- Ledger labels: `ANTIGRAVITY #1` and `ANTIGRAVITY #2`; observed concurrency stayed within the configured cap of 2.
- Both finished `DONE`, exit code `0`; orphan count `0`.

## Shared Skills Verification

- Canonical source: `C:\Users\isc\.agents\skills`.
- Mirrored targets: `C:\Users\isc\.gemini\antigravity-cli\skills` and `C:\Users\isc\.gemini\config\skills`.
- 21 skills checked; all source/CLI/desktop `SKILL.md` SHA-256 hashes match, including `caveman`.

## Regression Test

- PowerShell parse: PASS, zero parser errors.
- `codex --profile caveman --help`: PASS.
- `codex --profile coworker --help`: PASS.
- `codex --profile orchestrator --help`: PASS.
- `setup.ps1 mode-profiles`: PASS with fresh backup `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\20260921-000218`.
- `setup.ps1 doctor -Json`: PASS, zero errors; Codex/Git READY, Serena REMOVED, Mode profiles READY, Skill sync READY, Antigravity CLI READY, Orchestrator mode READY.
- `setup.ps1 dashboard`: PASS; dashboard regenerated.
- Existing Codex live mode probes and browser worker fixture remain PASS from the prior verification phase.
- `git diff --check`: PASS.

## Process / Resource Check

- Orchestrator status: no `RUNNING` ledger entries.
- Exact official `agy.exe` process count after tests: `0`.
- No permanent service or daemon created.
- Existing Antigravity desktop processes were not modified.

## Remaining Limitations

- The security gate rejected an unbounded task that would have sent repository contents to the external Antigravity service. This is correct behavior. Delegation is operational for explicitly bounded/authorized tasks; worker prompts must constrain sensitive-file disclosure.
- Antigravity CLI bin is resolved directly because its installer did not update PATH.
- Skill classification remains heuristic; hashes and standard `SKILL.md` compatibility are verified.

## Daily Usage

```powershell
.\setup.ps1 caveman
.\setup.ps1 coworker
.\setup.ps1 orchestrator
.\setup.ps1 orchestrator-dispatch -WorkerCommand dispatch -Task "Bounded task; explicitly list allowed files and prohibit secrets."
.\setup.ps1 orchestrator-dispatch -WorkerCommand status
```

## Final Verdict

FULL MODE SYSTEM READY

Native Codex modes, shared skill mirrors, official Antigravity CLI, cached authentication, bounded real delegation, two-worker concurrency, lifecycle cleanup, rollback backups, doctor checks, dashboard, and regression gates are operational.

## Codex Launcher Repair

### Root Cause

`setup.ps1` called bare `codex`. Codex Desktop injects its hashed CLI directory into its own process PATH, but normal PowerShell receives neither that directory nor a User PATH entry. Fresh normal PowerShell therefore reported: `The term 'codex' is not recognized...`.

### Codex Executable Discovered

- Verified executable: `C:\Users\isc\AppData\Local\OpenAI\Codex\bin\247581e40ee272fb\codex.exe`.
- Probe: `codex-cli 0.155.0-alpha.9.2`.
- No duplicate Codex installation added.

### PATH State

- Current Codex desktop process PATH contained temporary runtime entries plus hashed Codex directories.
- User PATH contained only `C:\Users\isc\AppData\Local\Microsoft\WindowsApps`.
- Machine PATH contained no OpenAI Codex directory.
- Launcher now checks PATH first, then verifies executable candidates under existing `%LOCALAPPDATA%\OpenAI\Codex\bin`.

### Files Changed

- `setup.ps1`: all four mode launchers use resolved executable path.
- `scripts/resolve-codex.ps1`: PATH-first, verified-install fallback, `--version` validation.
- `scripts/doctor.ps1`: reports verified Codex fallback when PATH is absent.
- `scripts/update-check.ps1`: uses same resolver.
- This report: root cause, evidence, tests, commands.

### Tests Performed

- Resolver found verified executable and version successfully.
- Current shell: `codex`, `caveman`, `coworker`, `orchestrator` with `--help`: all exit `0`.
- Fresh normal Windows PowerShell with clean Machine+User PATH: all four launch commands with `--help`: exit `0`; no `codex not recognized` error.
- `setup.ps1 doctor -Json`: Codex READY, reports verified executable; zero errors.
- `agy --version`: `1.2.7`.
- Real bounded worker smoke through existing orchestrator: `DONE`, exit `0`, result `CODEX_AGY_SMOKE_OK`.
- Worker status terminal; `agy` orphan process: `False`.
- PowerShell parse: zero errors; `git diff --check`: PASS.

### Final Launch Commands

```powershell
cd "C:\Users\isc\OneDrive\Documents\ChatGPT\setup"
.\setup.ps1 codex
.\setup.ps1 caveman
.\setup.ps1 coworker
.\setup.ps1 orchestrator
```

Direct Codex noninteractive orchestration smoke with workspace-write was blocked by host safety policy because it could permit arbitrary repository edits. Bounded direct orchestrator delegation passed; no repository disclosure occurred.

Fresh normal PowerShell launch without `--help` also resolved Codex and opened Orchestrator TUI. TUI then showed Codex sign-in screen. Existing `auth.json` is present, but fresh normal CLI session did not accept it. No credentials were read or changed. Complete interactive acceptance requires one user-owned Codex login.

## CODEX WORKBENCH UX

Implemented additive interactive launcher in `scripts/workbench.ps1`.

- `.\setup.ps1` opens Workbench menu.
- Direct commands remain unchanged: `codex`, `caveman`, `coworker`, `orchestrator`.
- Fast combinations: `.\setup.ps1 launch -Modes caveman,orchestrator -Project C:\path\to\project`.
- Plain mode uses base Codex only.
- Single modes use existing native profiles.
- Combined modes compose existing profile instructions into one temporary profile, then delete it after Codex exits.
- Coworker MCP is enabled only when Coworker is selected in a generated combined profile.
- Least-tools-first routing remains explicit.
- Orchestrator cap remains 2 workers; no daemon added.

## Project Registry

- Registry: `%USERPROFILE%\.codex\workbench-projects.json`.
- Stores friendly name, absolute path, Git detection, top-level project markers, and local instruction filenames.
- Registry contains no credentials, tokens, passwords, secrets, or project file contents.
- Add validates directory, detects metadata, asks for friendly name, then persists entry.
- Remove deletes only registry entry; project directory remains untouched.
- Edit renames registry entry.
- Open Project by Path launches once without saving.
- Existing Codex trusted project paths are discovered generically from `config.toml`; no EMMARA-specific launcher logic exists.

## Workbench Verification

- Workbench menu opened in interactive terminal.
- Preset menu paths tested: Plain, Caveman, Coworker, Orchestrator, all four combined presets.
- Custom toggle tested: Caveman + Orchestrator.
- Project selector tested: Current Directory, saved entries, Add Project, metadata detection, persistent save, Remove Project, Open Project by Path, Back.
- Temporary test project entry removed; directory was not modified.
- All eight fast-launch combinations passed `--help` with exit code `0`.
- Fresh normal Windows PowerShell with clean Machine+User PATH passed combined launch; exit code `0`.
- Temporary combined profile count after tests: `0`.
- Existing direct commands passed `--help` with exit code `0`.
- Doctor: Mode launcher READY, Combined modes READY, Antigravity READY, Serena REMOVED, zero errors.
- Dashboard regenerated with WORKBENCH row.
- Existing bounded Antigravity smoke remains PASS; worker status terminal; no orphan `agy` process.
- PowerShell parse: zero errors.

## HOW TO USE MY CODEX WORKBENCH

1. Open menu: `cd C:\Users\isc\OneDrive\Documents\ChatGPT\setup; .\setup.ps1`.
2. Choose Plain for minimal coding, Caveman for compressed communication, Coworker for browser/artifact work, Orchestrator for Antigravity delegation.
3. Choose a combined preset or Custom Combination when multiple capabilities are needed.
4. Choose Current Directory, any saved project, Add Project, or Open Project by Path.
5. Review READY TO LAUNCH capabilities. Confirm `Y`.
6. Return to Plain with menu `[1] Plain Codex` or direct `.\setup.ps1 codex`.
## Context7 Trust-Write Regression

### Root cause

- Installed PATH-first CLI: `C:\Users\isc\AppData\Local\Author Software\nvm\installs\v22.23.2\codex.cmd`, `codex-cli 0.155.1`.
- Desktop-linked binary remains separate and was not reinstalled: `C:\Users\isc\AppData\Local\OpenAI\Codex\bin\247581e40ee272fb\codex.exe`, `0.155.0-alpha.9.2`.
- Base Context7 is now canonical and valid:

```toml
[mcp_servers.context7]
command = "npx"
args = ["-y", "@upstash/context7-mcp"]
```

- The actual Mode 6 effective configuration had a second, stale definition in the generated profile:

```toml
[mcp_servers.context7]
enabled = false

[mcp_servers.playwright]
enabled = false
```

- That partial profile table was accepted by CLI profile merging but was invalid when Desktop/TUI `config/batchWrite` validated the active profile as a standalone MCP definition. It had no `command`, `url`, or valid transport, producing `invalid transport` in `mcp_servers.context7`.
- The stale reproduction file was `C:\Users\isc\.codex\workbench-d5449b4d6f1c4e949efce593c36c1fa0.config.toml`; it was archived and removed. No project-local `.codex/config.toml` or project source was involved.

### Why the previous test missed it

- The earlier base-only change corrected the user config but left the generated combined profile layer unchanged.
- Earlier checks used `--help`, profile parsing, and direct MCP initialize/tool calls. They did not perform the real TUI trust flow or inspect the temporary profile that `config/batchWrite` was validating.
- The installed app-server schema confirms `config/batchWrite` edits the active config layer; a direct canonical user-config batch write returned `OK`.

### Fix

- `config/codex/profiles/caveman.config.toml`, `coworker.config.toml`, and `orchestrator.config.toml` no longer define partial MCP tables.
- `scripts/workbench.ps1` now makes generated combined profiles instruction-only and applies `enabled` changes as runtime `--config` overrides, leaving the base MCP definition authoritative.
- The launcher now captures Codex trust entries written to a temporary profile, backs up the user config, synchronizes only `[projects.<path>] trust_level = "trusted"` into `C:\Users\isc\.codex\config.toml`, then deletes the temporary profile. It does not bypass or disable the trust prompt.
- Backup paths: `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\20260921-015109`, `20260921-021311`, `20260921-0215-profile-fix`, `20260921-021858`, `workbench-trust-write\20260921-024019`, and the repair backup `workbench-trust-write\20260921-024019-repair`.
- No Codex reinstall, duplicate MCP stack, Serena restoration, Playwright removal, or project-source edit occurred.

### Mode 6 + revit-Ai trust-write regression

- Real command: `.\setup.ps1 launch -Modes caveman,orchestrator -Project "C:\Users\isc\OneDrive\Desktop\revit-Ai"` from a fresh PowerShell process.
- Trust prompt: selected `1. Yes, continue`.
- Codex entered the project TUI successfully; no `invalid transport`, `configValidationError`, or `config/batchWrite failed` appeared.
- On exit, launcher reported persistence to `C:\Users\isc\.codex\config.toml`; the profile was removed. A fresh relaunch entered the same project without repeating the trust prompt.
- Codex config remained parseable, with zero `transport =` keys and exactly one revit-Ai trust section.

### Regression results

- Workbench Plain, Caveman, Coworker, Orchestrator, Caveman + Coworker, Caveman + Orchestrator, Coworker + Orchestrator, and Caveman + Coworker + Orchestrator: all exit `0`; temporary profile count `0`.
- `revit-Ai` and `batman` project-path launches passed; neither project source was modified. `batman` pre-existing dirty state was preserved.
- Context7 smoke: initialize succeeded; `tools/list` returned `resolve-library-id` and `query-docs`; harmless PowerShell resolution returned non-empty content.
- Bounded Antigravity smoke: `CONTEXT7_FIX_AGY_OK`, exit `0`; worker `DONE`; running workers `0`; `agy.exe` processes `0`.
- Serena check: REMOVED. PowerShell parser: `0` errors. `git diff --check`: PASS. Repository doctor: `0` errors; Context7, Playwright, node_repl, modes, Workbench, Antigravity, and Serena-removed checks READY/REMOVED.

### Final launch commands

```powershell
cd "C:\Users\isc\OneDrive\Documents\ChatGPT\setup"
.\setup.ps1 orchestrator
.\setup.ps1 launch -Modes caveman,orchestrator -Project "C:\Users\isc\OneDrive\Desktop\revit-Ai"
```
