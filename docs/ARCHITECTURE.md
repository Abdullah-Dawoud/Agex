# Architecture

AGEX has one execution engine and two front ends.

```
AGEX.exe (WPF desktop)        agex --cli (terminal)
        |  JSON lines                |  in-process
        v                            v
scripts/agex-primary.ps1  (engine host: request lifecycle, commands, serve mode)
   |-- agex-graph.ps1      leader loop, planning, task scheduler, fallback, outcome
   |-- agex-adapters.ps1   adapter registry, capabilities, allowlisted discovery
   |-- agex-session.ps1    collaboration messages, session history
   |-- agex-process.ps1    process runner, owned-process registry, agent health, logs
   |-- agex-ui.ps1         session state store, terminal renderer, editor model
   |-- agex-common.ps1     settings, storage, migration, telemetry
   `-- agex-maintenance.ps1 doctor, repair, update, uninstall
orchestrator.ps1 -> worker-run.ps1 -> agy   (supervised Antigravity worker)
```

## Engine (PowerShell)

- **Request lifecycle**: a request runs in a background runspace so the front end never blocks. The leader returns a JSON plan (`CONTINUE` with tasks, `COMPLETE`, or `BLOCKED`). Tasks run with dependencies and file-ownership conflict checks, up to two Antigravity workers in parallel. After each round the leader reconciles the goal against independently collected file and Git evidence (at most six rounds).
- **Outcome**: one function decides the final status (see USAGE). Every task ends in a terminal state.
- **Fallback**: `Invoke-AgexAgentCall` retries once with the other agent when an agent fails before doing work; never more than one automatic fallback per call.
- **Health**: start and sign-in failures pause an agent for the session (5-minute cooldown); other failures need two in a row.
- **Process runner**: every agent launch goes through `Invoke-AgexProcess`: UTF-8 stdin without BOM, concurrent stdout/stderr reading, timeouts, cancellation, process-tree kill of owned processes only, and a sanitized record of the command, exit code and output tails.
- **Messages**: `Add-AgexMessage` records only explicit content (user request, leader assignments and summaries, agent results, mailbox messages the agents sent, AGEX system events). Types: ASSIGNMENT, RESULT, QUESTION, ANSWER, REVIEW, REVISION_REQUEST, STATUS, SYSTEM.
- **Sessions**: saved to `%LOCALAPPDATA%\AGEX\sessions\<id>.json` (request, agents, tasks, messages, events, agent runs, changes, outcome); bounded by `max_sessions`.

## Desktop protocol

`agex-primary.ps1 -Serve` reads commands on stdin and writes events on stdout, one JSON object per line.

Commands: `scan`, `start {prompt}`, `cancel`, `retry`, `project.set {path}`, `settings.get`, `settings.set {settings}`, `sessions.list`, `session.get {id}`, `changes`, `diff {path}`, `details`, `agents.test {id}`, `repair`, `update.check`, `shutdown`.

Events: `hello`, `state` (full snapshot, sent only when it changes, at most 4 per second), `event` (activity, with sequence numbers), `message` (collaboration messages), plus one reply per command, `error`, `bye`.

The engine reads stdin only when `PeekNamedPipe` reports data. A pending synchronous read on the stdin pipe would block handle inheritance for every agent process the engine starts.

## Desktop app (C#, WPF, .NET Framework 4.8)

Built with the `csc.exe` that ships with Windows (`tools/build-desktop.ps1`), so neither users nor contributors need an SDK. The UI is built in code. It keeps the latest engine state and redraws only the affected views. Engine events are read on a background thread and dispatched to the UI thread. If the engine exits, the app restarts it (twice at most, then Diagnostics offers Restart engine). UI exceptions are logged and contained.

## Terminal front end

Diff-based full-screen renderer (only changed rows are rewritten), fixed input box, bracketed paste, confirmation for cancel and quit, console state restored on exit.

## Storage

`%LOCALAPPDATA%\AGEX` (or `AGEX_HOME`). Nothing is written to the repository or to agent configuration folders.
