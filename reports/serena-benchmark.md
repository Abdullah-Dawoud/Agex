# Serena Benchmark

Date: 2026-09-19
Version: Serena Agent 1.7.0

## Method

Compared intended baseline `rg` plus focused reads against Serena project activation/indexing on:

- setup repository: PowerShell-heavy repository;
- disposable Python fixture: `orders.py` and `checkout.py`, with one cross-file function reference.

No user project files were edited. Fixture exists only under the disposable visualization workspace.

## Results

| Test | Baseline | Serena | Result |
|---|---|---|---|
| Locate `calculate_total` | `rg` can find definition and call | Python project indexed 7 symbols | Serena adds symbol model; baseline is faster for one known name |
| Cross-file relationship | Manual search and file reads | Python language server started through uv-managed Pyright | Serena path is better suited to definitions/references; direct MCP query was not completed |
| Setup PowerShell symbols | `rg` and file reads work | Health check failed while installing PSScriptAnalyzer due access denied | Serena unusable for PowerShell until dependency write issue is resolved |
| Project isolation | Separate working directories | Setup and fixture registered as separate Serena projects | Isolation observed; project config and caches are separate |

## Security and overhead

- Serena exposes edit and shell tools in its default Codex context; setup project is configured `read_only: true`.
- Serena creates project configuration and caches under `.serena`; cache/log files are ignored by Serena’s nested ignore file.
- Language servers and dependencies may be downloaded on first use.
- Serena has project memory features. Memory phase is not enabled by this task.
- Exact token savings and latency were not measured; no fabricated numbers reported.

## Decision

**KEEP WITH LIMITATIONS**.

Keep global uv installation and additive Codex MCP registration. Use project-local Serena configuration. Do not treat Serena as healthy for PowerShell until the PSScriptAnalyzer/PowerShell Editor Services dependency path is repaired without weakening ACLs. Repeat symbol/reference benchmark after that fix and complete a real MCP tool call before enabling write tools.
