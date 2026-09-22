# Serena Popup Fix

Date: 2026-09-20

## Root cause

Serena global configuration had:

```yaml
web_dashboard: true
web_dashboard_open_on_launch: true
gui_log_window: false
```

Serena documentation states that `web_dashboard_open_on_launch` opens the dashboard when Serena starts. Codex uses one stdio Serena MCP launch per active MCP session, so repeated sessions multiplied dashboard openings. `gui_log_window` was already disabled. Codex had one Serena MCP registration; no duplicate registration or startup task was found.

## Fix

Changed only:

```yaml
web_dashboard_open_on_launch: false
```

Dashboard remains enabled for manual access. Serena remains installed. Codex MCP configuration remains unchanged. Backup created before edit:

`C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\20260920-200203\serena_config.yml`

## Evidence

- Before: 20 Serena process trees, each from the same Codex MCP command; no unrelated startup task matched Serena.
- Before: 1 Serena MCP entry in `%USERPROFILE%\.codex\config.toml`.
- Before: no Serena dashboard tab present in inspected Brave or Codex in-app browser state.
- Current config: dashboard enabled, automatic launch disabled, GUI log window disabled.
- After cleanup: one Codex-owned Serena MCP wrapper remained. Isolated smoke wrapper was removed. Three 5-second samples stayed at one process tree.
- Browser check: no Serena or port-24282 dashboard tab appeared.
- Serena check: active project, language server status, symbol overview, and pattern search returned successfully.
- No source code changed.

## Verification

Restart Codex or reload MCP to apply the global configuration. Then run harmless Serena operations: project activation, file listing, symbol overview, and code lookup. Watch process count and browser tabs during startup. Expected result: no automatic dashboard tab, no native log window, and one Serena process tree per active MCP session.
