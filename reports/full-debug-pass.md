# AGEX full debug pass

Date: 2026-09-24. Branch: `codex-live-agent-dashboard`.

## Summary

The screenshot failure (`GOAL: PARTIAL`, `Leader execution failed.`, `SUMMARY 0/0 done; 0 failed; assignments 1`, `Command details unavailable from executor.`) had four separate causes. All four are fixed. The interactive terminal was rebuilt around one renderer with diff-based updates and a fixed input box.

## Root causes

### 1. "Leader execution failed" (the real failure)

Session telemetry for the screenshot run (`task-82f59731...`, 2026-09-23 23:50 UTC) shows Codex exited in 0.55 s with:

> Failed to read prompt from stdin: input is not valid UTF-8 (invalid byte at offset 5596)

`Invoke-CodexTask` wrote the prompt with `Process.StandardInput.Write`. On Windows PowerShell 5.1 that writer uses the console input code page (OEM, e.g. 437/1256), not UTF-8. Any non-ASCII character in the prompt (user text, project file names in the evidence block) corrupted the stream. The AGY path (`Invoke-AgexAgyStream`) had the same defect with `StandardInput.WriteLine`. Stdout was also decoded with the console code page.

A second encoding defect was found by the new tests: .NET Framework writes the console encoding's preamble into the child's stdin when the process starts. On a UTF-8 console every prompt started with a BOM.

Fix: all executor launches now go through `Invoke-AgexProcess` (`scripts/agex-process.ps1`), which writes UTF-8 bytes to the raw stdin stream, decodes stdout/stderr as UTF-8, and switches a BOM-bearing input encoding to the same code page without a preamble. The AGY stream writer now writes bytes directly.

### 2. Incorrect PARTIAL state

`Invoke-AgexGoalGraph` initialised `GoalStatus = 'PARTIAL'` (and `New-AgexUiState` did the same) and only changed it on COMPLETE or CANCELLED. Any early exit (leader failure, invalid plan, BLOCKED) left PARTIAL. `Complete-AgexUiSession` also mapped "not complete, not failed" to PARTIAL.

Fix: one function, `Complete-AgexRequestOutcome`, decides the status:

- `COMPLETE` / `COMPLETE_WITH_FALLBACK`: leader verified the goal (with a recovered fallback for the latter).
- `PARTIAL`: at least one task done AND at least one failed or cancelled. Nothing else.
- `FAILED`: work started, no task succeeded.
- `START_FAILED`: no agent could produce a plan; no tasks exist.
- `UNVERIFIED`: tasks finished without failure but the leader never confirmed.
- `CANCELLED`.

Every task ends in a terminal state (`DONE`, `FAILED`, `REPAIR REQUIRED`, `CANCELLED`); queued or blocked leftovers are closed with an explicit reason.

### 3. 0/0 task accounting with "assignments 1"

`TotalAssignments` was `max(AssignmentCount, tasks + internal)`, where "internal" = telemetry TASK records minus planned tasks. The failed leader call wrote one TASK telemetry record, so a request with zero tasks showed "assignments 1". The UI printed this next to "0/0 done" with no explanation.

Fix: the summary reports real tasks (total, done, failed, cancelled) and agent runs separately ("Codex runs 1 | Antigravity runs 1"). A request that never got a plan is `START_FAILED` with the reason shown.

### 4. "Command details unavailable from executor"

This text was hard-coded in `Get-AgexUiLines`: printed on every compact final screen and for every `DISPATCH` command, regardless of what was known. The launcher did know the command.

Fix: the hard-coded lines are removed. `Invoke-AgexProcess` records, before start, the executable, launched file (npm shims are unwrapped to `node codex.js`), sanitized arguments, working directory, stdin size, PID, start/end time, exit code, stdout/stderr tails, timeout/cancel flags, exception type and fallback eligibility. `:details` shows the last runs.

### 5. Terminal flicker / blanking

- The draft editor cleared every one of its rows with spaces and then redrew them on each key press and on each dashboard refresh (visible blink).
- The dashboard repainted every line at least once per second (`timerDue`), moving the visible cursor across the screen each time.
- Menus called `[Console]::Clear()` on every arrow key.
- The dashboard and the editor were positioned independently (`RenderTop`); when the dashboard grew it overwrote the editor, and `Write-Host` output (help, warnings, paste status) scrolled the buffer under both.
- The background execution runspace's `trap` wrote to `[Console]::Error` directly.

Fix: one renderer (`Get-AgexScreenFrame` + `Write-AgexScreen`) owned by the UI thread. The whole screen is a fixed layout (status top, content middle, input box and hint line bottom). Each frame is compared row by row with the previous frame and only changed rows are rewritten; the screen is cleared only on a real resize or Ctrl+L. Background work only publishes state. Menus redraw in place. Measured in a real console session: typing and status updates rewrite only the affected rows; 0 render warnings.

### 6. Draft/input instability

The editor was a separate closure-based renderer whose position depended on the dashboard height and on scroll position; it was recreated per prompt, so commands and output reset the view. Fix: persistent editor model (`New-AgexEditor` and friends), fixed-height input box with internal scrolling, cursor restored after every frame. A test proves status updates do not change input rows or cursor position.

### Other defects fixed

- **Leader plan with zero tasks rejected.** `Get-AgexPlanCycle` had a mandatory `[object[]]` parameter, so `COMPLETE` with `"tasks": []` on the first round (every direct answer) threw "Cannot bind argument ... empty array" and became "invalid plan". Fixed with `[AllowEmptyCollection()]`.
- **No fallback.** Added `Invoke-AgexAgentCall`: when an agent fails before meaningful work (start failure, crash with no output, unavailable, not signed in) and the other agent is healthy, retry once with it. Never more than `MaxAutoFallbacks` (1). Codex→Antigravity always; Antigravity→Codex only when the task does not need file writes or Codex is allowed to write.
- **No availability precheck; AGY failures retried every call.** Startup runs `--version` on both CLIs (bounded, no network). Health is tracked per session: start/auth failures mark an agent unavailable immediately, other failures after two in a row; 5-minute cooldown; `:agents`, `:codex`, `:agy` retry now. The leader prompt states which agents are available, so a 90% AGY target is not pretended when AGY is down.
- **Codex refused non-Git folders** ("Not inside a trusted directory"): `--skip-git-repo-check` is now always passed.
- **Codex 180 s hard deadline** cut long planning; now 900 s (configurable).
- **Cancellation blocked the UI up to 6 s** (CIM process scans + sleep loop). Now non-blocking: signal, kill owned process trees from the runner's registry, pipeline stop after 8 s if still running.
- **Quit while running was refused.** Now asks "Cancel it and quit?", cancels, waits up to 6 s, kills owned processes, restores console state (cursor, colors, Ctrl+C mode, encoding, bracketed paste).
- **Line mode (redirected stdin) looped forever** printing "Task still running" at EOF. Rewritten to run each request to completion and print a plain summary.
- **Telemetry write failures threw** into execution. Executor paths now use safe wrappers; failures are logged, never fatal.
- **Child task events were invisible** (children used private UI state). Their events are copied into the session activity.
- **Workbench bar glyphs** were raw UTF-8 in a BOM-less file (mojibake on PowerShell 5.1).

## Files changed

| File | Change |
| --- | --- |
| `scripts/agex-process.ps1` (new) | Central process runner, session runtime, owned-process registry, agent health, diagnostic log |
| `scripts/agex-primary.ps1` | Executors on the runner; request lifecycle; cancellation; commands; interactive loop; line mode |
| `scripts/agex-graph.ps1` | Leader fallback, health-aware scheduling, outcome decision, child event bridge, empty-plan fix |
| `scripts/agex-ui.ps1` | New screen renderer and editor model; accurate session summary; removed hard-coded "details unavailable" |
| `scripts/agex-common.ps1` | UTF-8 AGY stdin; `AGEX_AGY_PATH`; `codex_task_sandbox` preference |
| `scripts/workbench.ps1` | Simple start screen; in-place menu redraw; lazy model catalogs; passes Codex sandbox |
| `scripts/agex-real-acceptance.ps1` | Accepts `COMPLETE_WITH_FALLBACK` |
| `scripts/agex-engine.tests.ps1` (new) | 21 targeted regression tests with fake Codex/AGY |
| `scripts/agex-ui.tests.ps1` | Source assertions updated for the new cancellation/editor design |
| `bin/agex.cmd` | Direct launcher (no rename notice) |
| `README.md`, `docs/USAGE.md` (new), `docs/TROUBLESHOOTING.md` | User documentation |

## Tests run

`scripts/agex-engine.tests.ps1`: 21 passed, 0 failed. Real child processes; fake agents only.

| Required case | Covered by |
| --- | --- |
| Leader succeeds | session: leader succeeds -> COMPLETE |
| Leader start fails | runner: missing executable is START_FAILED |
| Leader exits nonzero | session: leader crashes -> COMPLETE_WITH_FALLBACK (screenshot case) |
| AGY unavailable | session: Antigravity unavailable -> Codex only |
| Codex unavailable | session: Codex unavailable -> Antigravity leads |
| Fallback succeeds | screenshot case |
| Both unavailable | session: both agents fail -> START_FAILED, never PARTIAL or 0/0; Codex called exactly once |
| Worker succeeds / fails | session: one task succeeds, one fails -> PARTIAL, counters 2/1/1 |
| Ctrl+C | cancel: stops request and processes; editor test: Ctrl+C flow; real console smoke |
| Terminal resize | frame: every size fits; real console shrink 100x30 -> 50x14 |
| Typing during status updates | frame: status updates do not change input rows or cursor |
| Long output | runner: 4000+4000 lines stdout/stderr, no deadlock; frame: long result wraps |
| Log view / details view | editor test (`:log 5`, `:details`); real console smoke |
| Clean quit | editor test; real console `:quit` during a running request -> exit 0 |
| 2 workers | session: two Antigravity workers run in parallel (overlap verified) |
| No orphans | runner timeout tree kill; session orphan check; real console quit: 0 agent processes left |

Existing suites: `agex-ui.tests.ps1` PASS, `agex-graph.tests.ps1` PASS, `agex-acceptance-fixture.tests.ps1` PASS.

Real-console smoke: the interactive app was driven in a real conhost window through `WriteConsoleInput` / `ReadConsoleOutput` (throwaway driver, not committed): start check, typing, Ctrl+Enter, automatic Codex -> Antigravity recovery, `:details`, `:log 5`, Esc, resize, Ctrl+C cancel with confirmation, `:quit` during a run, launcher start screen.

## Known limitations

- Real Codex and real Antigravity were not called in this pass (no quota spent). The UTF-8 fix addresses the exact error in the screenshot run's telemetry; confirm with one real request.
- AGY still runs through `orchestrator.ps1 dispatch` -> `worker-run.ps1` (existing, validated chain). The dispatch process is started and supervised by the central runner; the inner worker keeps its own stream reader.
- Codex is read-only by default, so with Antigravity down AGEX can analyse but not edit files unless `codex_task_sandbox` is set to `workspace-write` (deliberate security default).
- If AGEX is killed from outside (window closed, Task Manager), processes it started are not cleaned up; a normal `:quit` or Ctrl+C is required for cleanup.
- Wide characters (CJK, some emoji) count as one column; very long lines of such text may be clipped early.
- The legacy dashboard (`Get-AgexUiLines` / `Write-AgexDashboard`) is kept only for redirected output and the acceptance script.

## Remaining issues

- Run `agex acceptance` from a normal PowerShell to re-validate the full real-agent path.
- `Invoke-AgexAgyStream` could move onto `Invoke-AgexProcess` in a later pass to remove the last duplicate stream reader.
