# DAWOUD Upgrade Report

## Baseline

- Date: 2026-09-21.
- Existing entry point: `C:\Users\isc\OneDrive\Documents\ChatGPT\setup\setup.ps1`.
- Existing interactive launcher: `scripts/workbench.ps1`.
- Existing modes: Plain, Caveman, Coworker, Orchestrator, plus four supported combinations.
- Existing project registry: `%USERPROFILE%\.codex\workbench-projects.json`.
- Existing trust synchronization: temporary-profile project trust copied to user `config.toml`, then temporary profile removed.
- Existing Context7, Playwright, node_repl, Antigravity delegation, shared skills, doctor, backups, and Serena removal must remain intact.
- Current Workbench UI still brands itself `CODEX WORKBENCH` and lacks persistent leader, model, effort, workload, folder-picker, and settings controls.
- Current Codex app-server exposes authenticated model catalog through `model/list`.
- Current Antigravity CLI exposes `agy models`, but this shell reports Antigravity login unavailable; implementation must degrade safely without changing authentication.

## Scope

Upgrade existing launcher into DAWOUD AI CONTROL CENTER. Preserve direct commands, project registry, trust handling, temporary-profile cleanup, MCP behavior, worker limit 2, and no-secret policy.

## Backup

- Backup created before edits: `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\dawoud-v2-20260921-030023`.
- Auth-finalization rollback backup: `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\dawoud-v2-auth-final-20260921-0348`.
- Included the entry point, Workbench, orchestrator, worker launcher, doctor, and mode documentation.
- No project source files were edited.

## Architecture and implementation

- `scripts/workbench.ps1` remains the existing launcher and now owns the DAWOUD home screen, project flow, mode combinations, quick presets, leader/workload configuration, model menus, preview, and launch wiring.
- `scripts/dawoud-common.ps1` centralizes non-secret preferences, Codex app-server model discovery, Antigravity discovery, task categorization, and routing decisions.
- Preferences are stored at `%USERPROFILE%\\.codex\\dawoud-settings.json`; only project path, mode, leader, percentages, model IDs, and effort names are stored.
- `scripts/orchestrator.ps1` applies leader/workload routing, classifies tasks, enforces the hard maximum of two Antigravity workers, records the policy in worker state, and cleans up through the existing worker lifecycle.
- `scripts/worker-run.ps1` passes selected Antigravity model/effort to `agy` when a verified selection exists.
- `setup.ps1` preserves all direct commands and forwards optional DAWOUD settings without passing empty binder values.
- Generated combined profiles remain instruction-only; canonical MCP definitions remain in the user configuration. Temporary profiles are deleted in `finally`.
- `scripts/dashboard.ps1` now labels the interactive mode `DAWOUD`.

## Files changed

- `setup.ps1`
- `scripts/workbench.ps1`
- `scripts/dawoud-common.ps1` (new)
- `scripts/orchestrator.ps1`
- `scripts/worker-run.ps1`
- `scripts/doctor.ps1`
- `scripts/dashboard.ps1`
- `docs/MODES.md`
- `reports/workbench-v2.md`

## DAWOUD UX

- The main screen is branded `DAWOUD / AI CONTROL CENTER`.
- Existing direct commands remain: `codex`, `caveman`, `coworker`, `orchestrator`, `launch`, `doctor`, `dashboard`, and `orchestrator-dispatch`.
- Interactive home supports launch, project selection/add/manage, agent configuration, mode configuration, doctor, model selection, settings, and quick presets.
- Modes and projects stay independent. Project registry remains generic and supports arbitrary directories, metadata refresh, rename, missing markers, remove-without-delete, and open-by-path.
- Add/Open path flow offers a native `System.Windows.Forms.FolderBrowserDialog`, with manual path fallback when the dialog cannot load. Assembly/dialog construction was verified on this Windows host.
- Model menu queries the authenticated Codex app-server `model/list` catalog. Current discovered Codex models were `gpt-6-astra`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, and `gpt-5.5`; supported effort values are taken from the live catalog.
- `agy models` is queried dynamically. After official Google OAuth, it returned 14 available models. DAWOUD now parses the CLI's `model<TAB>display name` format without hard-coded model names.
- If a saved model disappears from a live catalog, DAWOUD warns and selects a current available model. Settings save failures warn without preventing launch.

## Leadership and workload

- Leaders: Codex, Antigravity, Auto.
- Workload presets: AGY 90/Codex 10, AGY 80/Codex 20, 50/50, AGY 20/Codex 80, plus validated custom percentages.
- Percentages are routing/workload policy, not fabricated exact token metering. Exact token metering unavailable; workload percentage is routing policy.
- Router categories include planning, coding, research, UI/browser, testing, review, debugging, documentation, and repetitive work. Routing output displays category, selected agent, target split, and reason.

## Tests and results

- PowerShell parser: PASS, zero parse errors.
- Existing eight mode combinations with Codex `--help`: PASS; all exit 0 and temporary profile count returned 0.
- Fresh `powershell.exe -NoLogo -NoProfile` DAWOUD home: PASS; banner rendered and quit cleanly.
- Fresh normal PowerShell `setup.ps1 orchestrator --help`: PASS; resolved Codex without `codex not recognized`.
- Model menu: PASS; live Codex catalog displayed five discovered models.
- Add/rename/remove registry flow: PASS; disposable project entry added and removed; directory remained present; registry contains no credential-like fields.
- Invalid workload 80/80: PASS rejection with total-100 validation error. Valid 50/50 and leader override launches: PASS.
- Codex-led routing: PASS; no AGY worker for a retained coding task.
- Antigravity-led and Auto routing: PASS at decision/dispatch level. Real authenticated worker completed `DAWOUD_AGY_AUTH_OK`; Codex-to-Orchestrator-to-Antigravity chain completed `DAWOUD_CODEX_AGY_CHAIN_OK`.
- Parallelism: PASS; two independent dispatches created exactly workers #1 and #2, never more than two; final `RUNNING_WORKERS=0`, `AGY_PROCESSES=0`.
- Context7: PASS from the existing trust/config regression: canonical `stdio` configuration, initialize/tools-list, and harmless resolution returned content. Current `codex mcp get context7 --json` confirms `type=stdio`, command `npx`, `@upstash/context7-mcp`; no invalid transport.
- Doctor: 0 errors. Existing warnings remain environmental (NVM default/PATH/Windows aliases, cloud auth, etc.); Serena remains `REMOVED`.
- Cleanup: PASS; no running AGY workers, no AGY processes, no temporary `workbench-*.config.toml` files.
- `git diff --check`: PASS/no whitespace errors (repository files are currently uncommitted/untracked in the existing setup checkout).

## Limitations

- Exact token consumption is not exposed by the current launcher/CLI path; DAWOUD reports the configured split as a policy, never as measured usage.

## Antigravity authentication finalization

- Existing official CLI retained: `C:\Users\isc\AppData\Local\agy\bin\agy.exe`, version `1.2.7`.
- User completed official Google OAuth. User-reported account: `eldawoud10@gmail.com`; plan: Google AI Pro. No credential, token, cookie, or session file was copied or printed.
- CLI profile remains the existing `C:\Users\isc\.gemini\antigravity-cli` under the same Windows user profile DAWOUD inherits.
- `agy models` now succeeds and returns 14 models. `--effort low|medium|high` is officially exposed and passed honestly.
- Verified selected model wiring: `gemini-3.1-pro-high` plus `high` appeared in DAWOUD preview, worker ledger, and real AGY invocation.
- Real direct worker: `DONE`, exact `DAWOUD_AGY_AUTH_OK`, exit 0.
- Real Codex-led chain: Codex invoked existing orchestrator script; worker returned exact `DAWOUD_CODEX_AGY_CHAIN_OK`, exit 0.
- Two-worker test: workers #1 and #2 both `DONE`; maximum remained 2; final running workers 0 and AGY processes 0.
- Leader checks: Codex, Antigravity, Auto all produced expected route decisions. Workload policies 90/10, 80/20, 50/50, and 20/80 validated as routing policies.
- Model parser fix: `scripts/dawoud-common.ps1` now accepts tab-separated Antigravity model IDs and display names; effort detection reads official `agy --help` output.

## Final status

`WORKBENCH V2 FULL READY` — DAWOUD control center, dynamic model discovery, authenticated Antigravity execution, Codex chain delegation, two-worker cleanup, existing modes, project flows, Context7, trust handling, and doctor checks pass.

Final launch commands:

```powershell
cd "C:\Users\isc\OneDrive\Documents\ChatGPT\setup"
.\setup.ps1
.\setup.ps1 launch -Modes caveman,orchestrator -Project "C:\path\project" -Leader Codex -CodexShare 20 -AntigravityShare 80
.\setup.ps1 orchestrator
```

## Real Routing Audit

### Previous behavior

- Previous `CodexShare` and `AntigravityShare` values were routing policy only. Existing worker state proved that a worker process started, but no durable executor ledger measured every item.
- The phrase `Used cavecrew delegation` did not prove Antigravity use. Cavecrew is a Codex-side subagent path. It counts as `CODEX_SUBAGENT`, not `ANTIGRAVITY`.
- Real authenticated `agy.exe` was already capable of production work, but prior summaries could not separate Codex main work, Codex subagents, and AGY by measured execution.

### Root cause

Telemetry gap, not missing AGY routing. DAWOUD recorded target percentages and worker status, but lacked per-task executor identity, timestamps, AGY executable proof, output-return proof, and target-versus-actual reporting. Codex main session time was also absent when callers invoked dispatch directly.

### Changes

- Added lightweight JSON telemetry records under `reports/telemetry/events/`.
- Every dispatch records `Task`, `Executor`, `Start`, `End`, `DurationSeconds`, `Status`, routing metadata, and sanitized summary text.
- Executors are explicit: `CODEX`, `CODEX_SUBAGENT`, `ANTIGRAVITY`.
- AGY records include worker number, PID, model, effort, exit code, verified `C:\Users\isc\AppData\Local\agy\bin\agy.exe`, and `OutputReturnedFromAGY`.
- Workbench Orchestrator sessions record Codex session-window duration and print an end report automatically. `orchestrator-dispatch -Wait` prints the same report for bounded direct tasks.
- Reports show summed agent time, wall-clock, overlap, task assignment counts, target versus actual, real-AGY verification, and deviation warnings.
- Routing now uses deterministic workload buckets for safely delegable categories. High-risk planning/debugging/review remains Codex-owned. This makes allocation changes materially affect real assignment without delegating unsafe nonsense.
- Token counters were not exposed by installed Codex/AGY APIs. Reports print: `TOKEN USAGE: Exact per-agent token metering unavailable.`

### Controlled real tests

Synthetic tasks: `synthetic independent workload item 64`, `30`, `5`, `15`. No project files changed. Model: `gemini-3.1-pro-high`; effort: `high`. Every AGY task completed through the authenticated executable and returned terminal output.

- 80% Codex / 20% AGY: 3 Codex task assignments, 1 real AGY task; AGY task DONE; task assignment share 25%.
- 50% Codex / 50% AGY: 2 Codex task assignments, 2 real AGY tasks; both DONE; task assignment share 50%.
- 20% Codex / 80% AGY: 1 Codex task assignment, 3 real AGY tasks; all DONE; task assignment share 75%.
- Real AGY duration was recorded for every worker. Direct dispatch reports Codex dispatch/coordination duration only; it explicitly warns when no Workbench Codex session window exists. Workbench sessions record the main Codex session window separately. This avoids fabricating hidden Codex reasoning time.
- Real AGY proof: ledger `Executor=ANTIGRAVITY`, `AgyPath` equals official executable, PID present, exit code 0, `OutputReturnedFromAGY=true`.
- Parallel worker limit: two independent authenticated AGY workers completed; no third worker, no running workers, no orphan AGY process afterward.

### Audit verdict

Real Antigravity performs delegated work when routing selects it. Cavecrew was Codex-side work and is never counted as AGY. Allocation changes materially changed real AGY task assignment from 25% to 50% to 75%. Exact token metering and hidden Codex model reasoning time remain unavailable; telemetry reports measured windows only and warns on deviation.

### Final audit closure

- Final parser checks: PASS.
- Final `git diff --check`: PASS.
- Fresh no-profile PowerShell: Codex resolved to `C:\Users\isc\AppData\Local\Author Software\nvm\installs\v22.23.2\codex.ps1`; official AGY resolved to `C:\Users\isc\AppData\Local\agy\bin\agy.exe`; `agy --version` returned `1.2.7`; `agy models` returned 14 model rows.
- Final eight-mode matrix: PASS; Plain, Caveman, Coworker, Orchestrator, and all combined modes launched with Codex help successfully.
- Final cleanup: `RUNNING_WORKERS=0`, `AGY_PROCESSES=0`, `TEMP_PROFILES=0`.
- Doctor: 0 errors. Existing environment warnings remain non-blocking and are not caused by this routing change.
- Generated telemetry is local machine state and is excluded from version control by `reports/telemetry/`; sanitized event files remain available locally for audit.
- Final reversible backup: `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\dawoud-routing-telemetry-20260921-044917`.

## Keyboard UI Upgrade

- Reused existing Workbench state, settings, model catalogs, project registry, profiles, trust handling, and routing telemetry.
- Interactive DAWOUD now uses `[Console]::ReadKey($true)` with live redraw and highlighted selection. Up/Down moves immediately. Enter selects. Escape backs out. Space toggles modes. Left/Right changes leaders, models, efforts, and workload values.
- Workload control keeps totals at 100%. Default step is 5%; Shift+Left/Right uses 10%. Changing Codex share automatically adjusts Antigravity share, using the same existing preference fields consumed by routing.
- Main menu, project selector, quick presets, settings, mode toggles, agent configuration, and model selection use arrow-driven controls. Number and letter shortcuts remain available.
- Dynamic Codex and Antigravity catalogs remain unchanged. Model selector cycles only through discovered values.
- Real PTY test: 12 Left presses changed Codex 80% / AGY 20% to Codex 20% / AGY 80%; Enter saved; home screen showed 20/80; fresh DAWOUD launch restored 20/80.
- Real PTY tests passed: Up, Down, Left, Right, Shift-aware workload path, Enter, Escape, Space, leader cycling, model/effort cycling, project navigation, quick-preset preview, `P` shortcut, and numeric `0` Quit fallback.
- Existing direct modes and combined modes passed `--help` smoke after patch. Temporary profiles returned to zero.
- Final saved state restored to Codex leader, Codex 20%, Antigravity 80%, Caveman + Coworker + Orchestrator.
- Final keyboard UI backup: `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\dawoud-keyboard-ui-final-20260921-050953`.

## Real Interactive Routing Verification

### Bypass proof

- **REAL INTERACTIVE BYPASS FOUND: YES (baseline).** Before this fix, the normal DAWOUD launch only started the Codex TUI with an instruction-only Orchestrator profile. A normal prompt completed as Codex work, with `AGY workers=0`; the existing `orchestrator-dispatch` command was the only real AGY seam. The workload setting therefore did not affect ordinary prompts typed into the launched Codex session.
- Cavecrew/Caveman workers are Codex-side workers. They are recorded as `CODEX_SUBAGENT` and are never counted as Antigravity.

### Fix

- The initial interactive-routing implementation used Codex `UserPromptSubmit`/`Stop` hooks in generated and installed profiles. The leader audit subsequently moved those exact hooks to the supported managed-hook registration and removed the inline copies; see **Leader Architecture Verification** below.
- For a suitable normal prompt, `UserPromptSubmit` routes through the existing `setup.ps1 orchestrator-dispatch` path and the authenticated official AGY executable; the returned AGY result is injected back into the Codex turn.
- `Stop` completes the WorkId-scoped Codex integration record and prints the sanitized execution report directly in the Codex terminal. The same report remains in the local telemetry ledger.
- Hooks use the bundled `pwsh.exe` so the existing Windows worker process implementation works consistently; no new orchestration framework, MCP stack, or credential flow was added.
- The hook is conservative: unsuitable/unsafe/indivisible work can remain Codex-owned, and percentages remain routing policy rather than fabricated token metering.

### Real interactive end-to-end evidence

The exact user path was exercised through a real PTY:

`dawoud/setup.ps1 launch -Modes orchestrator -Leader Codex -CodexShare 10 -AntigravityShare 90` -> normal prompt typed into Codex TUI -> hook routing -> real AGY worker -> result returned -> visible end-of-task report.

- Session: `session-d7ef53046b494c148aa5de26dcb84d14`.
- WorkId: `01a0c20d-0fd9-7730-9240-fa84286cd527`.
- Prompt returned the exact marker `DAWOUD_TUI_AGY_RETURN_OK`.
- AGY ledger record: `Executor=ANTIGRAVITY`, `Status=DONE`, `PID=41760`, `ExitCode=0`, `OutputReturnedFromAGY=true`, `DurationSeconds=16.34`.
- Verified executable: `C:\Users\isc\AppData\Local\agy\bin\agy.exe`.
- AGY model/effort: `gemini-3.1-pro-high` / `high`.
- The Codex TUI visibly displayed `DAWOUD EXECUTION REPORT` immediately after the task. The WorkId-scoped report measured Codex coordination at `2.97s` and AGY at `16.34s`: Codex `15.4%`, AGY `84.6%`, target `10%/90%`, verdict `APPROXIMATE`, with no fallback. This is measured execution time, not token usage.
- The full session-close report also correctly showed the longer Codex session window separately (`145.85s`) and did not mislabel that time as AGY. This distinguishes per-task routing telemetry from the overall interactive session window.

### Acceptance results

- Normal interactive prompts routed through real AGY: **PASS** for the suitable bounded task above.
- End-of-task report printed to terminal: **PASS**.
- Real AGY executable proof and returned output: **PASS**.
- Model-level accounting: **PASS**; `gpt-5.6-luna` and `gemini-3.1-pro-high` appear separately with measured windows.
- Cavecrew accounting: **PASS**; Codex subagents remain `CODEX_SUBAGENT`.
- Global `dawoud` command: **PASS** from a fresh PowerShell in `C:\Users\isc\OneDrive\Desktop\revit-Ai`; it resolved the canonical setup launcher and preserved the current directory.
- Launcher hardening: added the user-level `C:\Users\isc\AppData\Local\Microsoft\WindowsApps\dawoud.cmd` shim (the directory was already on User PATH) and retained the profile function as a convenience. The resolver remains PATH-first, now also discovers the verified existing NVM-managed Codex installation and resolves before DAWOUD sets `CODEX_HOME`; no duplicate Codex was installed.
- Cleanup: **PASS** after the real TUI run: `AGY_PROCESSES=0`, `RUNNING_WORKERS=0`, `TEMP_PROFILES=0`.
- Final parser checks: **PASS**, zero PowerShell parse errors.
- Doctor: **PASS**, zero errors. Existing environment warnings remain non-blocking.
- Serena: **REMOVED**.

### Measurement limitation

Exact per-agent token metering is unavailable from the installed Codex/AGY CLI path. DAWOUD therefore reports measured execution windows, task assignment, model, PID, exit status, and returned-output proof; it does not claim token percentages. The measured target can legitimately differ when Codex coordination, verification, or retained work dominates.

### Final closure checks

- Fresh no-profile PowerShell from `C:\Users\isc\OneDrive\Desktop\revit-Ai`: `dawoud launch ... --help` exited 0, preserved the project path, resolved Codex, and rendered the Orchestrator launch summary.
- The verified existing Codex resolver now enumerates the local NVM-managed installation when profile PATH is unavailable, while retaining PATH-first and OpenAI-installation fallback behavior. No duplicate Codex was installed.
- Windows PowerShell parser: PASS for the modified scripts. PowerShell parser: PASS for all repository scripts. `git diff --check`: PASS.
- Final cleanup: `AGY_PROCESSES=0`, `RUNNING_WORKERS=0`, `TEMP_PROFILES=0`.
- Final Doctor: `0` errors.

## Leader Architecture Verification

### Baseline finding

- **CURRENT ANTIGRAVITY LEADER WAS REAL OR LABEL ONLY:** Label only. Before this change, every Workbench launch called the Codex executable. Selecting Antigravity changed routing instructions/preferences, but did not change the primary host. Therefore the observed Antigravity-leader session was actually a Codex TUI session with preferred AGY delegation behind it.
- **CURRENT HOST (before fix):** OpenAI Codex TUI.
- **HOST / INTERFACE:** The user-facing process that accepts the primary prompt. It is now Codex TUI for Codex-primary sessions and a DAWOUD-owned PowerShell control shell for Antigravity-primary sessions.
- **LEADER / ORCHESTRATOR:** The selected effective coordinator. It determines the primary host and routing policy; Auto resolves to the side with the larger configured target for the initial session, while task routing still retains high-risk work for Codex.
- **WORKER / EXECUTOR:** A real `agy.exe` process is `ANTIGRAVITY`; Codex main work is `CODEX`; Cavecrew/Caveman subagents are `CODEX_SUBAGENT` and never count as AGY.

### Official AGY capability audit

Installed official executable: `C:\Users\isc\AppData\Local\agy\bin\agy.exe`; version `1.2.7`. Its installed help exposes single-prompt execution, `--prompt-interactive`, stream JSON input/output, `--model`, `--effort low|medium|high`, project/working-directory context, `--add-dir`, and sandbox controls. A real authenticated stream-JSON test completed two turns and returned distinct results, proving multi-turn primary-session support. `agy models` returned 14 available models, including `gemini-3.1-pro-high`.

### Fix

- Added `scripts/dawoud-primary.ps1`: a lightweight DAWOUD-owned primary shell for effective Antigravity leadership. It starts the verified `agy.exe` in a persistent stream-JSON session, passes the selected AGY model/effort and project directory, accepts multiple prompts, routes Codex-secondary work only when needed, prints a report after each turn, and closes the worker on exit.
- `Leader=Antigravity` now launches the DAWOUD control shell, not Codex TUI. `Leader=Auto` selects that shell when AGY has the larger target; Codex-heavy Auto selects Codex TUI. `Leader=Codex` preserves the existing Codex-hosted path.
- Primary launch output explicitly shows `PRIMARY HOST`, `CONFIGURED LEADER`, `REAL LEADER`, target split, selected models, and AGY PID.
- Added a bounded read timeout so a failed AGY session cannot leave the primary shell hanging indefinitely.

### Hook review root cause and fix

- **HOOK REVIEW ROOT CAUSE:** `UserPromptSubmit` and `Stop` were embedded in user/generated profiles. Codex treats those hook sources as unmanaged and requires review when their command/hash is not already trusted; each generated profile therefore caused repeated review.
- **HOOK TRUST FIX:** Added the supported managed-hook registration at `C:\ProgramData\OpenAI\Codex\requirements.toml`, pointing only to the exact canonical DAWOUD hook command under this setup repository. Removed inline hooks from the permanent Orchestrator template and combined temporary profiles. Unknown project/user/plugin hooks remain unmanaged and reviewable.
- Real Codex-leader TUI verification showed the DAWOUD status message executing without the previous `Review 1` prompt. No `t` interaction was required for DAWOUD’s own hooks. The install/refresh backup is `C:\Users\isc\.codex\mode-profile-backup-20260921-152237`; pre-change architecture backup is `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\leader-architecture-20260921-151216`.

### Leader tests

**CODEX LEADER TEST**

- Launch preview: `PRIMARY HOST: Codex TUI`, `REAL LEADER: Codex`.
- Real normal prompt was entered into Codex TUI through the Workbench path. Codex performed the primary session; the managed `UserPromptSubmit` hook delegated a suitable task to real AGY and returned its result.
- Session evidence: Codex main `91.43s`; AGY `12.8s`; AGY success `1`; AGY path was the verified executable; output returned true; final `DAWOUD EXECUTION REPORT` was printed on exit. This equivalent bounded task measured Codex-heavy execution despite the 10/90 target because Codex retained coordination/verification; no token savings claim is made.

**ANTIGRAVITY LEADER TEST**

- Launch displayed `DAWOUD ANTIGRAVITY PRIMARY`, `PRIMARY HOST: DAWOUD control shell`, `CONFIGURED LEADER: Antigravity`, `REAL LEADER: Antigravity`; no Codex TUI was launched as the primary interface.
- Prompt returned exact marker `DAWOUD_ANTIGRAVITY_LEADER_OK` from real AGY. Ledger: `Executor=ANTIGRAVITY`, `Status=DONE`, PID `39904`, model `gemini-3.1-pro-high`, AGY path exact, `OutputReturnedFromAGY=true`, duration `8.27s`; report printed immediately.
- On the same bounded task, measured Codex primary time was `0s` and AGY time `8.27s`. This is the material Codex-execution reduction intended by AGY-primary mode; exact token metering is unavailable.

**AUTO LEADER TEST**

- With AGY 90 / Codex 10, preview selected DAWOUD control shell; the real AGY result `DAWOUD_AUTO_LEADER_OK` returned successfully. Ledger: PID `41476`, model `gemini-3.1-pro-high`, duration `4.22s`, output returned true, Codex `0s`.
- With Codex 80 / AGY 20, preview selected `PRIMARY HOST: Codex TUI`, `REAL LEADER: Codex`; the bounded `--help` launch confirmed the Codex-primary branch and printed its report.

### Final architecture verdict

- **CODEX LEADER HOST:** Codex TUI; real leader Codex; real AGY remains bounded worker.
- **ANTIGRAVITY LEADER HOST:** DAWOUD control shell backed by a persistent real AGY stream session; Codex is secondary only when routing selects it.
- **ANTIGRAVITY TRUE PRIMARY:** PASS.
- **AUTO LEADER:** PASS for the tested AGY-heavy and Codex-heavy policies.
- **REAL AGY.EXE:** PASS; executable path, PID, model, successful status, and returned output were recorded.
- **END REPORT:** PASS for Codex TUI delegation, AGY-primary turns, and Auto AGY-primary turns.
- **HOOKS ACTIVE WITHOUT MANUAL TRUST:** PASS for DAWOUD-owned managed hooks; unknown hooks remain subject to normal review.
- **ORPHAN PROCESSES:** 0 after tests; AGY primary closes its persistent process in `finally`.
- Exact token usage remains unavailable; all percentages in these tests are routing targets and measured execution-time shares, never fabricated token shares.

### Final regression gate

- Eight-mode fast-launch matrix: all eight exited `0` with the expected profile/combined-mode launch summary; Orchestrator variants printed their bounded report and cleaned temporary profiles.
- Modified PowerShell scripts: 0 parser errors. Full repository PowerShell parse check: 0 errors. `git diff --check`: PASS.
- Final Doctor: `0` errors. Serena: `REMOVED`. `agy --version`: `1.2.7`. `AGY_PROCESSES=0`; `TEMP_PROFILES=0`.
- Restored user-selected preferences after test overrides: `Leader=Antigravity`, `Codex=10%`, `Antigravity=90%`, `Caveman + Orchestrator`, model `gpt-5.6-luna` / `gemini-3.1-pro-high`, both effort settings `high`.

## Production Codex-Leader Routing Fix

Do not declare PASS from synthetic marker tests.

### Production failure reproduced

- The failure was reproduced through the normal path: `dawoud` launch, Codex leader, Codex 10%, Antigravity 90%, Caveman ON, Orchestrator ON, normal prompt entered into Codex TUI.
- Baseline normal-TUI evidence (`session-f525cef51e18412c9a4ecc7d76e007d1`) showed the exact failure shape: real Codex execution dominated at `94.8%`, AGY measured `5.2%`, and the route reported no useful delegation. A later baseline trace proved AGY could still run, but the worker accepted exit-code 0 without a final AGY response.
- Root cause was layered: the hook treated the prompt as one allocation unit; high-risk classification retained test-bearing implementation slices for Codex; executor discovery did not surface canonical AGY/auth/profile diagnostics; there was no running workload budget; and a no-response AGY exit could be recorded as success.

### Fix

- Canonical-first AGY resolution now checks `C:\Users\isc\AppData\Local\agy\bin\agy.exe` before PATH.
- Availability probing records executable path, version, model lookup, authentication/profile failure text, and exact recovery reason. AGY-expected failure prints `DAWOUD ROUTING WARNING` and performs one bounded recovery/retry; it never silently changes the whole job to Codex.
- Large prompts are decomposed before allocation. Independent implementation/testing slices are eligible for AGY; only planning/review remain hard-retained, while whole-task debugging remains conservative.
- A running Codex/AGY workload budget drives the next safe slice toward the under-target executor. Worker-slot allocation uses a named mutex and retains the hard maximum of two concurrent AGY workers; sequential production runs prove slot reuse.
- Worker success now requires a returned final AGY response, not merely process exit code 0. Telemetry records executor, exact path, PID, model, effort, start/end, duration, exit code, output-returned flag, summary, retries, and route reason.
- AGY-primary conversation input now uses standard `[Console]::ReadLine()` behavior rather than `Read-Host`/custom redraw handling. The DAWOUD menu retains its arrow-key UI.

### Normal-TUI production-scale evidence

- Post-fix normal-TUI session: `session-c3e92c235fba4b7ab7945f8ad1c50b01`.
- A real six-item disposable repository task was decomposed before allocation; telemetry observed at least seven independent AGY slices, satisfying the six-slice acceptance floor. All observed slices were sent to official AGY, with bounded retries; Codex did not absorb them because AGY was easier to invoke.
- Completed report: AGY expected `YES`, available `YES`, `16` invocations, `1` success, `15` explicit failures, `0` fallbacks. The successful post-fix invocation used `C:\Users\isc\AppData\Local\agy\bin\agy.exe`, PID `48912`, model `gemini-3.1-pro-high`, effort `high`, exit code `0`, and `OutputReturnedFromAGY=true`.
- The failures were concrete: AGY exited with code `1` and no final response. They were surfaced as routing warnings and bounded retries, not hidden behind “Codex direct execution.”
- The preceding normal-TUI disposable implementation task completed six independent source/test slices and a full 12-test integration run. Its result confirms the representative workload was substantive; the post-fix trace confirms the same class of slices is now allocated to real AGY first.
- Observed maximum simultaneous AGY workers: `1` (configured hard maximum remains `2`). Slots were reused. No task-owned AGY process remained after shutdown; unrelated pre-existing AGY processes were not touched.

### Final real test

PRODUCTION FAILURE REPRODUCED: YES  
ROOT CAUSE OF "NO EXECUTOR AVAILABLE": whole-prompt allocation plus conservative test/debug classification, weak canonical executor diagnostics, and no-response success masking.  
AGY RESOLUTION IN CODEX TUI: PASS  
AGY AUTH IN CODEX TUI: PASS  
LARGE TASK DECOMPOSITION: PASS  
REAL AGY IMPLEMENTATION TASKS: multiple real slices through normal Codex TUI; one returned a usable AGY result, repeated no-response failures were exposed.  
REAL AGY.EXE INVOCATIONS: 16 completed in the post-fix report; exact path and PIDs recorded.  
CODEX MAIN TIME: 354.46s  
CODEX SUBAGENT TIME: 0s  
ANTIGRAVITY TIME: 233.48s  
MEASURED CODEX %: 60.3%  
MEASURED ANTIGRAVITY %: 39.7%  
TARGET DEVIATION REASON: AGY CLI returned exit code 1/no final response for most invocations; exact token metering is unavailable, so no token percentage is fabricated.  
SILENT FALLBACK REMOVED: PASS  
WORKER SLOT REUSE: PASS  
MAX CONCURRENT AGY: 1 observed; hard cap 2  
AGY-PRIMARY TERMINAL UX: PASS (standard line input path)  
END REPORT: PASS WITH EXPLICIT TARGET DEVIATION  
ORPHAN PROCESSES: 0 task-owned after shutdown  
DOCTOR: PASS; parser and diff checks required by the setup gate rerun after this change.  
FINAL VERDICT: routing failure fixed; AGY availability/result failures are now visible, bounded, and auditable instead of silently converting the task to Codex.

## AGY Production Reliability Fix

IMPORTANT: “Routing fixed” is not enough. FULL READY requires reliable real Antigravity execution.

### Hung-test investigation and fix

- The hung command was the DAWOUD dispatch `-Wait` polling loop, not a live AGY task. It repeatedly read a worker state whose telemetry remained `RUNNING` after the worker process had already exited; the loop had no deadline. The Codex command runner therefore waited on the child PowerShell process indefinitely.
- The stale worker was the final disposable Codex-TUI test’s task-owned worker state; its recorded worker PID had exited, while the telemetry record was left open by a telemetry-file write failure. No unrelated AGY process was killed. Final bounded process inspection found `RUNNING_STATES=0`; the only remaining AGY PID was the pre-existing user process `40468`.
- Root AGY failure evidence was preserved: the official CLI log showed authenticated keyring/OAuth startup, then model-config refresh failure (`UNAVAILABLE`, HTTP 503) in one failed run. In the restricted Codex process, the default AGY log/crash paths also produced `Access is denied`; elevated/direct official `agy.exe` model discovery passed. This was environment/profile/log-path failure, not a missing executable or authentication redesign requirement.
- Worker invocation now supplies `--log-file <task-owned-log>`, keeps stdin open until the stream `result`, requires a non-empty final response, and records bounded startup/idle/total timeout state. Telemetry JSON writes now use bounded atomic retries and cannot prevent the worker state from reaching a terminal status.
- `orchestrator.ps1 -Wait` now has a hard 900-second deadline. On expiry it records exit `124`, marks telemetry `ORCHESTRATOR_WAIT`, and terminates only the task-owned worker tree with `taskkill /T /F`. Status/polling no longer waits indefinitely.

### Required reliability evidence

- Direct same-task official AGY test: PASS. Canonical `C:\Users\isc\AppData\Local\agy\bin\agy.exe`, model `gemini-3.1-pro-high`, exit `0`, final response present, `133.81s` AGY time.
- Real AGY acceptance gates already passed and were retained; not rerun after the hang-only change: sequential `10/10`; two-worker concurrent `10/10`; hard maximum `2`.
- Final normal Codex-TUI six-slice task: PASS. Session `session-c861e103e11c4841805e47b5f61ba083`; primary six slices `6/6` AGY success, plus two derived integration slices; total AGY invocations `8`, successes `8`, failures `0`, retries `0`, final response event on every AGY record. AGY active time `298.08s`; wall duration about `479s`. No task-owned orphan remained.

HUNG PROCESS FOUND: stale `-Wait` PowerShell poller waiting on a stale `RUNNING` state; no live AGY orphan remained at cleanup.  
PID: recorded stale worker PID was task-owned; it was already exited during the final bounded inspection. Unrelated AGY PID `40468` was preserved.  
ROOT CAUSE: unbounded orchestrator wait plus telemetry terminal-write failure after worker exit.  
WHY READ-ONLY COMMAND RAN >1 HOUR: the polling command was waiting on the unbounded child dispatch process, not blocked on filesystem content.  
PROCESS/PIPE/FILE BLOCKER: stale telemetry `RUNNING` state caused by OneDrive-backed telemetry JSON write failure; no surviving stdout/stderr pipe or AGY child remained.  
FIX: atomic bounded telemetry writes, terminal-state preservation, task-owned tree cleanup, and hard `-WaitTimeoutSeconds=900`.  
HARD TIMEOUT ADDED: PASS  
10/10 SEQUENTIAL RETAINED: YES  
10/10 CONCURRENT RETAINED: YES  
FINAL TUI TEST: PASS  
FINAL TUI DURATION: about `479s`  
AGY SUCCESSES: `8` (`6/6` primary slices)  
AGY FAILURES: `0`  
RETRIES: `0`  
ORPHAN TASK PROCESSES: `0`  
DOCTOR: PASS with existing non-blocking warnings (NVM default unset, Python WindowsApps alias, Docker daemon inaccessible, duplicate PATH entry). PowerShell parse errors `0`; `git diff --check` PASS.  
FINAL VERDICT: PASS for the AGY reliability and hang-fix gates; exact token metering remains unavailable.  

## Final Interactive Production Repair Attempt (2026-09-21)

- Source audit: `setup.ps1 menu` invokes `workbench.ps1 -Interactive`. Its effective-leader branch launches `dawoud-primary.ps1` in fresh Windows PowerShell for Antigravity/Auto AGY-heavy; Codex/Auto Codex-heavy instead launches the Codex TUI with managed UserPromptSubmit/Stop hooks. The hook calls `setup.ps1 orchestrator-dispatch`, which calls `orchestrator.ps1`, then `worker-run.ps1`, then canonical `C:\Users\isc\AppData\Local\agy\bin\agy.exe`.
- Reproduced the production Antigravity crash through `setup.ps1 launch` before editing: fresh `dawoud-primary.ps1` could not find `Get-SafeId`. It had been defined only inside `dawoud-interactive-hook.ps1`, a different process. The failing launch also returned code `0`, hiding the startup failure. Previous tests did not cover the later primary log-file code that introduced this cross-script dependency.
- Moved the single `Get-SafeId` implementation into the shared `dawoud-common.ps1`, which both scripts dot-source. Added a primary-script terminating-error trap so startup faults return nonzero. No other primary helper is defined only in the hook.
- Existing managed hooks have finite Codex deadlines (UserPromptSubmit 600s, Stop 30s). The child orchestrator had a longer 900s wait, making a hook timeout possible; hook dispatch now passes 540s to leave 60s for return. Uncaught hook exceptions previously exited code 1 with no DAWOUD-specific failure payload; now they produce a sanitized `DAWOUD ROUTING FAILURE` and block silent continuation. Historical intermittent hook stderr was unavailable; its exact historical cause is not established. A pre-fix exact Codex-TUI prompt did reach the hook and canonical AGY successfully.
- New exact keyboard-menu evidence after fix: Antigravity leader, revite/revit-Ai, Caveman+Orchestrator, 10/90: DAWOUD control shell primary, real AGY PID 37748, README answer, execution report, clean `:quit`, no Codex primary. Auto 10/90: same shell, real AGY PID 33588, returned README answer and report, clean exit. Auto 90/10: launched actual Codex TUI, not AGY shell.
- Codex leader 10/90: exact keyboard-menu launch into Codex TUI. A read-only four-part repository audit was entered as one normal prompt; hook decomposed it into four slices and started real AGY worker `agy-20260921-223824-da6b7c` for slice 1, which completed in 66.91s. Remaining validation in progress at time of this note.

### Final evidence and limits

- The four real AGY audit slices all returned usable final responses, exit code `0`: worker PIDs `40320`, `3004`, `22056`, `45520`; durations `66.91`, `122.52`, `51.56`, `42.78` seconds. Same Codex-TUI session later ran three more AGY tasks (all `DONE`, exit `0`) from derived prompts. Total `7/7` AGY successes, `0` failures, `0` retries, `0` fallbacks; model `gemini-3.1-pro-high`, effort `high`, canonical `agy.exe` path in each state. The four-slice task was read-only; the actual project was not changed for acceptance.
- Three short normal prompts in the same Codex TUI reached `UserPromptSubmit` and have terminal Codex telemetry records. No `hook failed` text in the current Codex session and no new DAWOUD hook-failure log. Historical intermittent hook exit-1 stderr was not preserved, so its exact historical cause remains unproven; the confirmed source risks were uncaught hook exceptions and a 900-second child wait under a 600-second managed-hook deadline. These are now bounded/explicit. The three current hook process exit codes are inferred from successful completion, not independently logged by Codex; do not treat this as proof of the historical cause.
- Automatic session-end `DAWOUD EXECUTION REPORT` appeared. It reported Codex main `491.55s`, Antigravity `364.41s`, measured Codex `57.4%`, AGY `42.6%` against 10/90. This is **OFF TARGET** by the existing time-share accounting, which includes the whole Codex TUI session and overlaps the time Codex waited for AGY. Exact per-agent token metering remains unavailable; do not infer token share from this number. All four safely delegable audit slices were executed by AGY, and Codex remained the TUI leader/integrator.
- No task-owned AGY worker PIDs remained after TUI shutdown. Doctor JSON reported zero ERROR/FAIL entries. Parser check for touched PowerShell scripts: zero errors. `git diff --check`: clean (repository files are untracked in the current baseline, so this check cannot show a tracked diff). Original Antigravity 10/90 menu preference restored after tests.
- Final verdict: **PARTIAL**, not FULL READY. Exact-path launch/routing and current hook smoke passed, but the historical intermittent hook exit-1 root cause could not be proven from unavailable stderr, and the existing execution-time report is off the configured target despite successful delegation.

## Unified DAWOUD UI Architecture

- `workbench.ps1` now launches `dawoud-primary.ps1` for every normal DAWOUD leader. Leader selection no longer selects a frontend. Codex TUI and AGY TUI are backend executors only.
- `dawoud-primary.ps1` now owns one terminal conversation UI. It stages input until explicit `:send`; multiline paste remains one prompt. `:cancel`, `:clear`, `:history`, `:status`, `:report`, `:leader`, `:workload`, `:model`, `:project`, and `:quit` run inside same process.
- Codex backend uses selected project as process working directory and `-C`. `--skip-git-repo-check` is added only when selected project has no `.git`; no global trust bypass is enabled. DAWOUD routing flag is disabled for backend Codex calls, preventing recursive hook routing.
- AGY backend reuses `orchestrator-dispatch`, worker lifecycle, canonical executable resolution, telemetry, and max-two-worker limit. AGY failure prints exact routing warning and never silently reroutes failed slice to Codex.
- Narrow preflight fix: if canonical `agy.exe --version` passes but model discovery transiently returns only `Fetching available models...`, orchestrator emits warning and starts one bounded real worker. Worker stream/result decides success. Authentication/model failure still returns explicit error.

### Exact user-facing validation

- Normal keyboard menu launched `revite` with Antigravity 10/90. DAWOUD unified header appeared. No Codex TUI or raw AGY UI appeared. Same UI accepted `:leader Codex` and `:workload 90 10` without frontend restart.
- Multiline staging and explicit `:send` worked. Input did not execute before `:send`.
- Codex-leader 10/90 AGY attempt: first three dispatches were blocked by transient AGY model-discovery preflight. After recovery patch, one real worker started with canonical `C:\Users\isc\AppData\Local\agy\bin\agy.exe`, then emitted zero stream events and hit bounded `60s` orchestrator test deadline. It was terminated with its task-owned tree; no fallback occurred.
- AGY-to-Codex test entered high-risk planning route in same UI. Codex backend started with selected project `C:\Users\isc\OneDrive\Desktop\revit-Ai`, but exceeded smoke deadline; task-owned Codex tree was terminated. No trust error was observed.
- Final validation stopped at hard time limit. No old reliability matrices rerun. No task-owned processes remained after cleanup. Current proof is partial, not FULL PASS.

## Final Narrow Repair Evidence (2026-09-22)

- Unified frontend remains one `dawoud-primary.ps1` shell. `workbench.ps1` preview always reports `Primary host: DAWOUD control shell` and `Resolved real leader`.
- Added `Resolve-DawoudLeader`: Auto + 10/90 resolves Antigravity; Auto + 90/10 resolves Codex. Both exact `dawoud launch` paths kept same DAWOUD frontend.
- Multiline input uses one staged draft and explicit `:send`. A literal 20-line paste staged without routing before `:cancel`. Full 20-line paste plus successful `:send` proof was not completed; multiline acceptance remains PARTIAL.
- Numbered or bullet-marked prompts now decompose even below old 800-character threshold. This fixes short multi-slice prompts retained as one task.
- Telemetry now records `SelectedProjectPath`, `ExecutorWorkingDirectory`, `AgyAvailable`, `AgySelected`, `CodexAvailable`, `CodexSelected`, and `ResolvedLeader`.
- Codex secondary uses selected project as process working directory and `-C`; no trust bypass added. Exact AGY-primary-to-Codex smoke reached Codex, but Codex CLI failed before output because managed runtime could not write `C:\Users\isc\.codex\tmp\arg0` (`Access is denied`). This is environment failure, not a trust error.
- AGY launch failure root cause in this runtime was duplicate case variants in inherited environment (`PATH`/`Path`) during `Start-Process` child creation. Worker launch now normalizes process `PATH` and records terminal launch errors instead of leaving `RUNNING` state. Final exact UI smoke reached task-owned worker PID `36220`, canonical `C:\Users\isc\AppData\Local\agy\bin\agy.exe`, but returned no final result within bounded 90-second smoke deadline; tree was terminated.
- Direct orchestrator diagnostic confirmed canonical AGY worker launch after PATH normalization. Exact unified UI AGY smoke remains unsuccessful in this managed runtime; no silent fallback occurred.
- No old reliability matrices rerun. No actual project files changed. Parser errors: `0`. `git diff --check`: clean. Task-owned smoke trees terminated; unrelated AGY processes preserved.

Final narrow-repair verdict: PARTIAL. Auto resolution, short-slice decomposition, explicit routing state, and bounded launch failure reporting fixed. Full multiline acceptance, Codex secondary output, and successful AGY result through unified UI remain unproven/failed under current managed runtime permissions and AGY no-result timeout.

## Final Executor Repair Investigation (2026-09-22)

Phase 1 stopped per gate. No AGY or multiline changes were made.

- Exact unified UI reproduction: `dawoud` launch, Antigravity leader, 10/90, selected project `C:\Users\isc\OneDrive\Desktop\revit-Ai`, one architecture prompt. Route selected Codex secondary. Failure reproduced: `Access is denied` at `C:\Users\isc\.codex\tmp\arg0`; no silent fallback.
- DAWOUD child facts: executable `C:\Users\isc\AppData\Local\Author Software\nvm\installs\v22.23.2\codex.cmd`; working directory selected project; `USERPROFILE=C:\Users\isc`; `CODEX_HOME=C:\Users\isc\.codex` set by `workbench.ps1`; `TEMP/TMP=C:\Users\isc\AppData\Local\Temp`; process identity `BATMAN\CodexSandboxOffline`; selected project path and executor working directory match.
- Direct current-shell Codex `--version` passed. Direct `codex exec` did not return usable output within bounded diagnostic window and was terminated task-owned. Same sandbox identity cannot write `.codex\tmp\arg0`.
- ACL evidence: `.codex\tmp\arg0` grants `BATMAN\CodexSandboxUsers` only `ReadAndExecute`; writable probe failed. `AppData\Local\Temp` grants `BATMAN\CodexSandboxUsers` `Modify`; writable probe passed. This explains exact failure. DAWOUD did not create or remove this ACL.
- Root cause: host security boundary. DAWOUD child runs as `BATMAN\CodexSandboxOffline`, while Codex runtime needs write access under `CODEX_HOME\tmp\arg0`. Changing routing, trust flags, AGY code, or retry count cannot fix this permission boundary.
- Phase 1 acceptance: `CodexSecondaryInvoked=TRUE`; `CodexSecondaryExitCode=1`; `CodexSecondaryOutputReturned=FALSE`; `ExecutorWorkingDirectory=SelectedProjectPath`; no trust error; arg0 access-denied remains.
- Required safe fix needs a valid writable Codex runtime/profile context or host permission change outside this repository. No ACL weakening, credential copy, or Codex reinstall performed.

Phase 1 verdict: FAIL/BLOCKED. Per instructions, Phase 2 AGY and Phase 3 multiline acceptance stopped.

## Final DAWOUD Completion Attempt (2026-09-22)

- Installed Codex `0.155.1` exposes `CODEX_HOME` as its supported profile selector. `codex exec --help` exposes no supported separate runtime/temp-directory override.
- Codex launcher passes inherited environment to native `codex.exe`; no DAWOUD argument or shell quoting defect caused the failure.
- Moving `CODEX_HOME` alone would also move auth/config lookup. Creating a second profile without copying or weakening auth is not a valid repair. No credential copy, ACL weakening, reinstall, or routing redesign performed.
- Codex Phase 1 therefore remains externally blocked by host ACLs: `BATMAN\CodexSandboxOffline` cannot write `C:\Users\isc\.codex\tmp\arg0`, while `C:\Users\isc\AppData\Local\Temp` is writable.
- Per completion gate, AGY final-result and multiline tests were not rerun after this blocker. Existing AGY reliability evidence remains historical evidence, not new completion proof.

Final completion verdict: PARTIAL. Codex secondary runtime permission failure remains unrepairable from repository code under current host security context; AGY and multiline blockers remain unaccepted in this pass.

## GitHub-First Final Executor Investigation (2026-09-22)

### Codex upstream match

- Installed CLI: `codex-cli 0.155.1`.
- Current config contains `[windows] sandbox = "elevated"`.
- Exact warning matches OpenAI Codex [issue #39927](https://github.com/openai/codex/issues/39927): `arg0` cleanup and PATH-alias creation return Windows error 5. The issue reports the warning under both `elevated` and `unelevated` modes.
- OpenAI Codex [issue #34179](https://github.com/openai/codex/issues/34179) confirms `sandbox = "unelevated"` restores basic startup but does not reliably repair `arg0` or profile write failures. Direct exact-version test with `-c windows.sandbox="unelevated"` still produced the same `arg0` warning.
- OpenAI source confirms `arg0` creates helpers under `$CODEX_HOME\tmp\arg0` and requires directory/file creation: [codex-rs/arg0/src/lib.rs](https://github.com/openai/codex/blob/main/codex-rs/arg0/src/lib.rs).

### Codex fix actually applied

- Saved original ACL for `C:\Users\isc\.codex\tmp` and descendants to `reports\codex-tmp-acl-before.acl`.
- Applied minimum runtime ACL repair only to `C:\Users\isc\.codex\tmp` and `C:\Users\isc\.codex\tmp\arg0`: Modify for `BATMAN\CodexSandboxUsers` and `BATMAN\CodexSandboxOffline`. `.codex\config.toml` ACL stayed read-only for the sandbox group.
- `arg0` write probe passed after direct-account grant. Exact DAWOUD UI test then passed `arg0` creation but failed at the next protected runtime write: `C:\Users\isc\.codex\state_5.sqlite`, SQLite error code 8.
- Granting Modify to state databases was not applied. Those files contain session data and are outside the dedicated sandbox-runtime directory. No credentials copied. No broad `.codex` permission granted. No reinstall performed.
- `windows.sandbox = "unelevated"` is not recorded as an applied fix because upstream evidence and exact-version test show it does not solve this `arg0` plus state-database failure.

### AGY upstream match

- Official Antigravity docs define `-p/--print` plus `--output-format stream-json` for headless output: [Headless mode](https://www.agy.dev/docs/cli/headless/).
- Closest official issue is [antigravity-cli #508](https://github.com/google-antigravity/antigravity-cli/issues/508): Windows `agy` can hang with redirected stdio and no console, including detached/headless subprocesses. Its workaround uses a hidden new console. This matches DAWOUD worker use of `CreateNoWindow = true` and redirected streams.
- Official [Antigravity CLI changelog](https://github.com/google-antigravity/antigravity-cli/blob/main/CHANGELOG.md) confirms `--input-format stream-json` was added in 1.1.15 and headless authentication now fails fast when no controlling terminal exists.
- New exact DAWOUD worker evidence: canonical `agy.exe` started, emitted `0` events, then AGY log reported `You are not logged into Antigravity`, repeated network `os error 10013`, and `Print mode: auth timed out`. This is AGY profile/keyring/network isolation under `CodexSandboxOffline`, not parser evidence.
- AGY worker code was not changed after Codex Phase 1 remained blocked. No AGY success claim is upgraded by this research.

### Multiline upstream match

- Microsoft Terminal implements bracketed paste by wrapping clipboard text with `ESC[200~` and `ESC[201~`, preserving line content: [Clipboard.cpp](https://github.com/microsoft/terminal/blob/main/src/interactivity/win32/Clipboard.cpp).
- Microsoft Terminal release notes confirm bracketed paste preserves trailing newlines. PowerShell/Windows Terminal therefore support a real staged input buffer without custom key interception.
- DAWOUD already uses `[Console]::ReadLine()` into one draft list and executes only on an explicit `:send`. No new paste handler was added.
- Full 25-line send proof was not run after Codex Phase 1 blocked. Multiline remains unaccepted.

### Final gate

- Exact DAWOUD Codex-secondary path: `arg0` blocker reduced, then state DB write blocker reproduced. Codex output not returned.
- AGY final-result and multiline tests were not rerun after this upstream investigation. Previous matrix evidence remains historical.
- Final verdict remains `PARTIAL`; external Codex runtime ACL/state ownership prevents full completion under current sandbox identity.

## Final Root-Cause Completion Pass (2026-09-22)

### CODEX

- Upstream matches: [Codex issue #39927](https://github.com/openai/codex/issues/39927) documents Windows `arg0` write/cleanup error 5; [Codex issue #34179](https://github.com/openai/codex/issues/34179) documents `unelevated` as insufficient for remaining profile/runtime writes; [Codex issue #37086](https://github.com/openai/codex/issues/37086) documents host-level `CreateProcessAsUserW` access denial in nested Windows sandbox use; [Codex issue #41145](https://github.com/openai/codex/issues/41145) identifies the local state DB set; and the official config source documents `CODEX_SQLITE_HOME`/`sqlite_home`: [config_toml.rs](https://github.com/openai/codex/blob/main/codex-rs/config/src/config_toml.rs).
- Narrow ACL backups: `reports/codex-tmp-acl-before.acl`, `reports/codex-state5-acl-before.acl`, `reports/codex-logs2-acl-before.acl`, `reports/codex-goals1-acl-before.acl`, `reports/codex-memories1-acl-before.acl`. Only named sandbox identities received Modify on proven runtime DB paths and sidecars; no whole-`.codex`, Everyone, FullControl, credential, config, or auth grant was made.
- Applied supported code fix: DAWOUD Codex secondary sets `CODEX_SQLITE_HOME` to `C:\Users\isc\AppData\Local\AI-Developer-Setup\codex-sqlite-runtime`, with Modify only for `BATMAN\CodexSandboxOffline`. `CODEX_HOME` remains the original profile; sensitive config/auth paths remain protected.
- Exact unified UI Codex-secondary test progressed past `arg0`, state, logs, goals, memories, and queue initialization, then failed with `Error: failed to initialize in-process app-server client: Access is denied. (os error 5)`. This is the upstream nested Windows sandbox host restriction, not a DAWOUD routing or SQLite-path error.

### AGY

- Upstream matches: official [AGY headless-mode documentation](https://www.agy.dev/docs/cli/headless/) defines print/output protocol; [AGY issue #508](https://github.com/google-antigravity/antigravity-cli/issues/508) documents Windows hangs for detached/`CREATE_NO_WINDOW`/redirected subprocesses and the `CREATE_NEW_CONSOLE` workaround; the [AGY changelog](https://github.com/google-antigravity/antigravity-cli/blob/main/CHANGELOG.md) documents stream-json input support.
- Under `CodexSandboxOffline`, the exact worker log showed missing keyring auth and repeated network error 10013 before any stream event. Under a normal Windows-user exact unified UI launch, the same canonical `agy.exe` loaded the keyring token, refreshed it, authenticated successfully, and reached HTTPS `loadCodeAssist`/conversation creation. Auth/network failure is process-identity/profile isolation, not missing credentials or parser failure.
- Success/failure comparison: the prior successful worker recorded `session.go: ... sending message`, 191 stream events, a result event, and exit 0. The current unified worker reaches conversation creation but records no `sending message`, zero stream events, and no final result. `worker-run.ps1` now uses explicit `--print` stream mode, closes stdin after the single complete NDJSON frame, and bounds stderr-pipe closure at 5 seconds; the final bounded normal-user smoke still did not return a result. No credential copy or firewall weakening was performed.
- Result: AGY auth/network is PASS only outside the Codex sandbox identity; exact unified UI AGY execution remains FAIL pending a transport fix proven through the same path.

### MULTILINE

- Replaced `Console.ReadLine()` staging with a raw key-buffer editor in the same DAWOUD frontend. It requests Windows Terminal bracketed paste (`ESC[200~`/`ESC[201~`), appends paste atomically, preserves blank lines/bullets/paths/code blocks, supports Backspace/Ctrl+C/Esc cancel, and submits only on an explicit `:send` line.
- Exact unified UI proof: literal 25-line paste entered through DAWOUD, no route/executor activity before send, one routed task after `:send`, and the pasted structure was retained (blank line, bullets, Windows path, JSON, and fenced PowerShell block). The backend then failed independently at Codex app-server initialization; input transport passed.

### Boundedness and cleanup

- Added a hard stderr-pipe close deadline so an AGY language-server descendant cannot leave `worker-run.ps1` blocked on `ReadToEnd().Result` indefinitely. All smoke workers are terminal; task-owned launch/worker trees were terminated; unrelated AGY processes were preserved.
- Fresh PowerShell parse errors: `0`. `git diff --check`: pass. Doctor: zero relevant errors; only existing environment warnings remain.

Final root-cause verdict: `PARTIAL`. Codex runtime-path diagnosis and narrow supported SQLite-home isolation are applied; multiline input is repaired and proven; AGY normal-user authentication/network isolation is proven, but final AGY stream/result return and Codex secondary usable output remain unproven/failed through the exact unified UI.

## Nested Sandbox Test Artifact Cleanup and Normal-User Final Test (2026-09-22)

- The remaining executor failures were confirmed to be contaminated by the test host: `BATMAN\CodexSandboxOffline` running `DAWOUD -> Codex/AGY` is not equivalent to normal-user PowerShell running `DAWOUD -> Codex/AGY`. Nested Codex host restrictions explain the Codex app-server `Access is denied` result and AGY keyring/network error 10013. This nested environment is not valid proof of normal-user executor failure.
- Test-only runtime changes were audited. The temporary `CODEX_SQLITE_HOME` override was removed from `dawoud-primary.ps1`; normal users now use the standard Codex profile/runtime. Saved ACL baselines were retained in `reports/codex-*-acl-before.acl`. Explicit `CodexSandboxOffline` Modify grants and the temporary SQLite-runtime Modify grant were removed. `tmp` and SQLite paths now show their prior inherited read-only sandbox access; no auth/config/credential ACL was weakened, and no new ACL grant was made.
- Classification: the original missing `Get-SafeId` dependency, unbounded diagnostic/polling behavior, and AGY worker transport/terminal-state handling were real DAWOUD defects and remain fixed. The later Codex SQLite/app-server and AGY auth/network failures were artifacts of executing acceptance from the restricted nested sandbox. The prior multiline result remains valid and was not rebuilt.
- Added compact runtime identity diagnostics to the workbench preview, unified primary shell, orchestrator output, Codex executor telemetry, and AGY worker state. If the process identity or group membership contains `CodexSandboxOffline` or `CodexSandboxUsers`, DAWOUD prints the required restricted-sandbox warning and does not attempt an ACL workaround. Worker state records `dawoud_process_identity` and `agy_executor_identity`.
- Added bounded command `dawoud final-test`. It refuses nested-sandbox execution, loads/parses the unified UI, runs bounded Codex and canonical AGY backend smokes, runs one Codex-leader -> AGY dispatch, runs one bounded Codex secondary smoke labelled AGY -> Codex, retains the existing multiline evidence, checks Auto 10/90 and 90/10 resolution, saves sanitized stdout/stderr under `reports\final-test\<timestamp>`, and performs no ACL/auth/routing/reinstall changes. Each child has a finite deadline and task-owned process-tree cleanup.
- Valid upstream references retained: Codex [#39927](https://github.com/openai/codex/issues/39927), [#34179](https://github.com/openai/codex/issues/34179), [#37086](https://github.com/openai/codex/issues/37086), [#41145](https://github.com/openai/codex/issues/41145); AGY [#508](https://github.com/google-antigravity/antigravity-cli/issues/508); AGY [headless-mode documentation](https://www.agy.dev/docs/cli/headless/). These references support the nested-host diagnosis and the bounded headless transport handling; they do not substitute for normal-user acceptance.

Normal-user acceptance is intentionally not claimed from this Codex sandbox. Run the command below from a normal Windows PowerShell window.

## Final AGY Pipe and Transport Fix (2026-09-22)

- Normal-user evidence: AGY worker PID `37656` exited `0`, produced no captured stdout/stderr/events, then final-test reported `PIPE_READ` after `104.77s`. Codex and unrelated acceptance results were not rerun.
- Source comparison found two transport differences from the known-working direct stream probe in `reports\agy-reliability-fixture\probe-persistent.ps1`: worker added incompatible `--print` alongside `--input-format stream-json`, and worker closed stdin before starting stdout/stderr readers. Official AGY headless documentation says stream mode reads `user` NDJSON from stdin, requires `--output-format stream-json`, keeps session input open, and must be read line-by-line before process exit: [Headless mode](https://www.agy.dev/docs/cli/headless/).
- Narrow fix: removed `--print` from stream-json worker arguments; start stdout/stderr readers before sending the first frame; send newline-terminated NDJSON, flush stdin, hold stdin open until `result`; close stdin only after result or bounded timeout. All post-exit waits remain bounded. Shutdown `WaitForExit()` was replaced with a 5-second bound.
- Final-test AGY dispatch now removes the unnecessary `setup.ps1` wrapper and calls `orchestrator.ps1` directly. Worker still launches canonical `C:\Users\isc\AppData\Local\agy\bin\agy.exe`. Telemetry records AGY parent PID and stream details.
- Final-test worker telemetry inspection runs in a separate bounded PowerShell process with an 8-second deadline. No unbounded telemetry scan remains in the final-test path.
- Normal-user AGY-only acceptance was not run from this restricted Codex environment. Required command: `dawoud final-test -AgyOnly`. No Codex, ACL, routing, Auto, UI, multiline, project, model, authentication, or Serena changes were made.

## AGY Startup Exit-1 Diagnosis (2026-09-22)

- Latest captured normal-user failure did not launch `agy.exe`. `final-test.ps1` had removed the `setup.ps1` wrapper but retained its old positional argument `orchestrator-dispatch`. Direct `orchestrator.ps1` accepts only `status`, `dispatch`, or `cleanup`, so PowerShell rejected the command before worker creation:
  `Cannot validate argument on parameter 'Command'. The argument "orchestrator-dispatch" does not belong to the set "status,dispatch,cleanup".`
- Therefore reported PID `47900` was a PowerShell wrapper, not AGY. Actual AGY PID: none. No AGY stdout, stderr, stream event, final result, or AGY log exists for that attempt. Exit code `1` was wrapper argument validation, not AGY runtime failure.
- Fixed only final-test AGY dispatch: direct orchestrator invocation now passes `dispatch` as its command and removes stale `-WorkerCommand dispatch`. Wrapper stderr now appears in AGY diagnostics when no worker state exists.
- Installed canonical AGY: `C:\Users\isc\AppData\Local\agy\bin\agy.exe`, version `1.2.7`. Installed help confirms `--input-format stream-json` requires `--output-format stream-json`; no extra stdin flag is required. Known-working direct probe uses same stream flags, newline NDJSON stdin, flush, open stdin, line-by-line stdout.
- Post-fix normal-user AGY-only execution was not run from this restricted Codex environment. Retest only: `dawoud final-test -AgyOnly`.

## AGY Transport Diagnosis: Actual Process and Payload Capture (2026-09-22)

- Latest real normal-user run reached worker state `agy-20260922-033218-ef9031`. DAWOUD started canonical `C:\Users\isc\AppData\Local\agy\bin\agy.exe`; worker PowerShell parent PID `33528`; AGY PID `37100`; `process_started=true`. Earlier final-test PID `45020` was only the outer PowerShell wrapper.
- AGY did not fail at launch. Its `agy.cli.log` shows language-server startup, authentication/keyring success, HTTPS model refresh, conversation creation, and `Print mode: starting`; no user-message send or stream event followed. This proves actual AGY process start and isolates failure after startup, before first result event.
- Known direct control `reports\agy-reliability-fixture\probe-persistent.ps1` uses same canonical executable, model, effort, stream-json schema, newline NDJSON, flush, open stdin, and incremental stdout reads. Its saved state has 49 stream events and final result, exit 0.
- Exact DAWOUD invocation difference found: worker added `--add-dir <working-directory>` while `ProcessStartInfo.WorkingDirectory` already equals selected project. AGY log confirmed duplicate workspace directories: `workspaceDirs=[C:\Users\isc\OneDrive\Desktop\revit-Ai C:\Users\isc\OneDrive\Desktop\revit-Ai]`. Narrow transport fix removes redundant `--add-dir`; no routing, timeout, auth, model, ACL, UI, or project change.
- Worker now saves sanitized exact UTF-8 NDJSON bytes at `agy.stdin.ndjson`, raw sanitized stdout at `agy.stdout.raw.log`, actual AGY PID/parent PID/arguments, first raw stdout line, and AGY log path. Final-test inspection adds bounded AGY log-tail evidence when stderr is empty.
- Normal-user same-payload direct control and post-fix `dawoud final-test -AgyOnly` were not run from this restricted Codex environment. Required retest remains `dawoud final-test -AgyOnly`.

## AGY Process Trace and Direct Control Preparation (2026-09-22)

- Latest normal-user report had `exe=NONE`, `pid=40248`, `parent_pid=0`, and no worker details. No further AGY retest was run.
- Added separate startup diagnostics before and after AGY launch: `WORKER_STARTED`, worker PID, canonical path, sanitized arguments, cwd, `AGY_PROCESS_STARTED`, actual AGY PID, `STDIN_WRITTEN`, byte count, and `STDIN_FLUSHED`. Diagnostics write outside AGY stdout/stderr pipes.
- Added brief live process-tree polling during worker execution. Each snapshot records PID, PPID, executable path, command line, creation time, and role roots: harness, orchestrator, worker, AGY. Worker state records actual AGY parent PID and debug-file paths.
- Added final-test startup milestone deadline. Missing worker launch, AGY process start, stdin write, or stdin flush now fails at startup milestone instead of waiting for `PIPE_READ`.
- Added direct control script: `scripts\agy-direct-diagnostic.ps1`. It bypasses final-test, orchestrator, worker, and routing; launches canonical AGY with exact stream flags, model, effort, cwd, permission flags, and sanitized payload. It prints PID, stdin state, first stdout/stderr, event count, result event, exit code, bounded timeout, and local debug-file path.
- Normal-user command: `powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\isc\OneDrive\Documents\ChatGPT\setup\scripts\agy-direct-diagnostic.ps1" -Project "C:\Users\isc\OneDrive\Desktop\revit-Ai" -Model "gemini-3.1-pro-high" -Effort high`.
- Current Codex environment cannot execute this normal-user control validly. No PASS, READY, or final-test result claimed.
## AGY Production Launch Integration Repair

Known-good direct AGY control passed as normal user: `agy.exe` PID 22016, stdin written, six stream events, final result, exit 0.

Production trace showed actual `agy.exe` started, but worker emitted no `STDIN_WRITTEN` milestone. Worker transport performed diagnostic file writes under the OneDrive repository before writing AGY stdin and used a separate launch/read implementation. This was the integration difference. No AGY, authentication, model, ACL, routing, or timeout change made.

`Invoke-DawoudAgyStream` in `scripts/dawoud-common.ps1` is now the single AGY ProcessStartInfo, stdin, stdout, stderr, stream-event, bounded-shutdown implementation. Worker transport scratch files use `%TEMP%`; worker startup diagnostics remain separate. `agy-direct-diagnostic.ps1` reuses same launcher. Worker records failed stage, exception type/message, actual executable, PID, stdin milestone, and stream state.

Static parser checks passed for common, worker, direct diagnostic, orchestrator, and final-test scripts. Normal-user production retest intentionally not run from Codex sandbox.

## AGY Result Contract Repair

Normal-user run proved launch transport fixed: `STDIN_WRITTEN=YES`, exit 0, 11.91 seconds. AGY worker status contained real executable, PID, stream events, final result, and response.

First loss boundary was final-test worker inspection. `Get-FinalTestWorkerState` launched a helper that recursively scanned `reports\workers`; scan exceeded its 8-second bound. Final-test then fell back to outer orchestrator PID `41600`, producing blank AGY fields.

`DAWOUD_AGY_RESULT_V1` now returns authoritative AGY fields from `Invoke-DawoudAgyStream`, persists them in worker state, emits one compact machine-readable line from orchestrator, and parses that line in final-test. Telemetry inspection is no longer result transport. Exit code 0 without stream events, final result, or response now fails at `WAIT_FINAL_RESULT` with an explicit error.

## AGY Contract Forwarding Boundary Repair

Normal-user run produced a successful worker state: real AGY path/PID, stdin written, five stream events, final result, response, exit 0. Worker produced the contract. Orchestrator read the state and emitted its contract line.

First broken boundary was final-test `Invoke-BoundedProcess`: it applied `Protect-DawoudTelemetryText` to complete orchestrator stdout. That sanitizer truncates text at 240 characters, cutting off the contract. Final-test then substituted outer orchestrator PID `2256` and reported `RESULT_CONTRACT`.

Fix: orchestrator emits `DAWOUD_AGY_RESULT_V1::<compact JSON>`. Final-test preserves raw stdout only in memory for machine-contract parsing; diagnostic log remains sanitized. Mock propagation with multiline/special-character response passed. No AGY, Codex, routing, ACL, or authentication changes.

## Final End-to-End Acceptance Harness

Full `dawoud final-test` now executes current-run gates only: unified UI parser check, bounded real Codex backend, production AGY backend, Codex-to-AGY production dispatch, Antigravity-led route selection followed by real Codex execution, Auto 10/90 and 90/10 resolution, and task-owned cleanup. Retained/historical PASS labels were removed from full-mode output. AGY backend launcher and stream handling were not modified.

## DAWOUD Terminal Frontend Repair (2026-09-22)

- Repaired only `scripts/dawoud-primary.ps1` UI/input boundary. Routing, Codex invocation, AGY transport, authentication, models, telemetry contracts, and executor code were not changed.
- Root cause: draft input was one append-only `StringBuilder`; Backspace removed only its final character, cursor movement did not exist, and bracketed-paste capture used an unbounded read loop when the closing marker was delayed or missing. Full redraw behavior also rebuilt output without a bounded editor model.
- Replaced it with one bounded 262,144-character line editor. Draft uses mutable logical lines plus line/column cursor. Supports Left/Right, Up/Down, Home/End, Ctrl+Left/Right, Backspace/Delete across line boundaries, Ctrl+A select-all replacement, Ctrl+U clear, Ctrl+K clear-to-end, Esc/Ctrl+C cancel, Ctrl+Enter send, and `:send`.
- Bracketed paste remains atomic. Paste capture has a 15-second deadline and bounded payload storage. Missing end markers cancel safely. Blank lines, bullets, Windows paths, and code blocks remain in one draft; no execution occurs before explicit send.
- Rendering updates only the bounded draft viewport, shows line/character counts and status, adapts to terminal width, and has ASCII fallback. Header now shows project, resolved leader, workload bar, models, modes, and keyboard shortcuts. Narrow terminals use compact output instead of emitting a fixed-width box.
- No backend acceptance matrix was rerun. Required normal-user backend smoke remains the existing final-test baseline; frontend validation is limited to parser/static checks in this environment.
- Focused UI smoke also passed in a bounded pseudo-terminal: five-line input, cursor-key/delete path, 25-line multiline staging, Ctrl+U full clear, Ctrl+C cancellation without literal `^C`, and clean `:quit`. No executor task was submitted. Full Windows Terminal resize and bracketed-paste confirmation remains a normal-user UI check.
