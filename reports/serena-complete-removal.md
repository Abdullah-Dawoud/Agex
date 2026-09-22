# Serena Complete Removal

Date: 2026-09-20

## Root cause

One global `%USERPROFILE%\\.codex\\config.toml` MCP registration launched:

`uv tool run --from serena-agent serena start-mcp-server --context=codex --project-from-cwd`

Codex MCP sessions multiplied that process tree. Serena also had dashboard auto-open enabled before the earlier suppression change, so each launch could create browser/window noise. Startup Run keys, scheduled tasks, PowerShell profiles, PATH, and environment-variable checks found no independent Serena persistence.

Initial incident inventory: 166 Serena-related process rows, including 43 Serena executable rows and 151 MCP-tree command rows; estimated working set was about 1082.9 MB.

## Completed changes

- Backed up Codex config and Serena state before cleanup:
  `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\serena-removal-20260920-224545`
- Removed `[mcp_servers.serena]` from the user Codex config.
- Stopped the exact Serena/uv MCP process trees; later exact process checks returned zero active Serena rows.
- Removed `%USERPROFILE%\\.serena` and this repository's `.serena` directory.
- Removed Serena setup/update branches and changed doctor/dashboard state to `REMOVED / NOT REQUIRED`.
- Updated active architecture, workflow, security, recovery, MCP, stack, tool, and troubleshooting docs so setup does not reinstall Serena.
- Preserved Codex, Git, Context7, Playwright, `node_repl`, browser/computer-use, Caveman, document/PDF/spreadsheet, GitHub, and email workflows.

## Verification

- Doctor: exit 0; MCP servers reported `node_repl`, `context7`, and `playwright`; Serena reported `REMOVED`.
- PowerShell parse: `setup.ps1`, `scripts/init-project.ps1`, and `scripts/update-check.ps1` each had zero parse errors.
- Dashboard generation path executed through the updated doctor.
- `git diff --check`: passed.
- Exact Serena process check after cleanup: `0`.
- No Serena MCP block remains in Codex config.
- Global and project `.serena` directories are absent.

## Final verification

- The stale uv environment and all three user shims (`serena.exe`, `serena-agent.exe`, `serena-hooks.exe`) were removed after stopping the two orphaned Python workers holding the wrapper.
- `uv tool list` reports no Serena tool.
- `serena` no longer resolves on PATH.
- Exact process count remains zero after cleanup and after doctor/dashboard use.
- Stability samples at approximately 20s, 40s, and 60s: `0`, `0`, `0` Serena rows.

## Verdict

SERENA COMPLETELY REMOVED — STABLE
