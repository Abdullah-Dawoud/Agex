# DAWOUD Live Dashboard

DAWOUD keeps one terminal frontend for Codex, Antigravity, and Auto leaders.

`dawoud-ui.ps1` owns session state and rendering only. Backend routing, AGY stream handling, `DAWOUD_AGY_RESULT_V1`, worker lifecycle, and final-test paths remain outside renderer logic.

Live state uses observable process handles, dispatch stdout, timestamps, bounded in-memory events, and a best-effort filesystem watcher. Reports and telemetry remain audit/result storage, not synchronous live IPC.

UI failure is isolated. Renderer exceptions fall back to one-line status output. Event history is capped at 160 entries. Command text passes through existing telemetry redaction before display. Hidden reasoning and token usage remain undisclosed when unavailable.

Focused UI checks:

```powershell
.\scripts\dawoud-ui.tests.ps1
```
