# Final Build Audit

Date: 2026-09-19

## Ready

- Codex, Git, uv, Caveman skills, Serena, existing `node_repl`.
- Host Node/NVM: v22.23.2, npm/npx 10.9.8.
- Read-only doctor with JSON output.
- Allowlisted Codex backup/restore.
- Safe idempotent project initializer with dry-run.
- Local HTML dashboard generated from doctor JSON.
- Read-only update inventory.
- Daily workflow, visual workflow, cost, security, GitHub, OmniRoute, recovery documentation.
- Context7 local MCP and Playwright MCP configured additively after backup.

## Limited

- Serena: Python fixture worked; PowerShell backend cannot provision PSScriptAnalyzer under current permissions.
- Doctor: Codex sandbox cannot see host NVM registry; host verification passed outside sandbox.
- Browser: bundled browser/computer-use remains available; Playwright MCP passed the reusable localhost fixture benchmark.
- Dashboard: local report generated; it is not a long-running service.

## Deferred / excluded

- Context7: hosted OAuth/API-key modes; local credential-free MCP retained with limitations.
- Playwright MCP: hosted/remote browser use; local MCP retained with limitations.
- GitHub CLI: plain Git retained; authentication not attempted.
- OmniRoute: no router/provider/credentials configured.
- Persistent memory: Cavemem removed after project-isolation failure; no replacement retained.

## Verification

- Project initializer: dry-run, creation, and second-run idempotence passed on disposable Git fixture.
- Dashboard/status/update wrapper commands passed.
- PowerShell scripts parsed.
- Codex CLI help passed.
- MCP preservation passed: `node_repl` and `serena` present; `cavemem` absent.
- MCP additive configuration passed: `node_repl`, `serena`, `context7`, and `playwright` present.
- Context7 v4.1.1 initialize/tools-list and real version-sensitive Playwright MCP documentation query passed.
- Playwright localhost fixture passed navigation, visible/accessibility snapshot, form fill, click, DOM evaluation, console detection, screenshot, desktop 1440x900, and mobile 390x844.
- Deliberate fixture failure was detected (`intentional fixture error`), corrected, and the corrected save behavior passed.
- Cavemem package and test state removed.
- Disposable initializer fixture was deleted after inspection; reusable browser fixture remains at `fixtures/ui-test/`.
- Secret scan and text hygiene passed.
- Final doctor snapshot: 15 READY, 10 WARNING, 4 OPTIONAL, 0 ERROR. Host-only NVM registry visibility remains a sandbox limitation; user-provided normal PowerShell verification is accepted.

## Incomplete acceptance items

All requested final-build acceptance items are complete. Remaining limitations are documented: Serena PowerShell backend provisioning, sandbox-only NVM visibility, and no hosted Context7 credentials.
