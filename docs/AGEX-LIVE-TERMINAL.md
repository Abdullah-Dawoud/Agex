# AGEX Live Terminal

AGEX keeps one terminal frontend for Codex, Antigravity, and Auto leaders.

`dawoud-ui.ps1` is a retained internal filename. It owns one session state and one renderer. `DAWOUD_AGY_RESULT_V1`, `DAWOUD_*` process variables, telemetry paths, and backend function names remain internal compatibility identifiers.

The acceptance harness and interactive terminal both consume ordered, bounded runspace updates on their UI thread. The renderer shows observed task, assignment, executor, verification, Git, mailbox, and acceptance-stage state.

The acceptance harness and interactive terminal consume ordered, bounded runspace updates on the UI thread. The renderer shows observed task, assignment, executor, verification, Git, mailbox, and acceptance-stage state. Live state uses observable process handles, dispatch output, timestamps, bounded events, and a best-effort filesystem watcher.

UI failure is isolated. Renderer exceptions fall back to one-line status output. Event history is capped at 160 entries. Command text passes through existing telemetry redaction before display. Hidden reasoning and token usage remain undisclosed when unavailable.

Focused UI checks:

```powershell
.\scripts\dawoud-ui.tests.ps1
```
