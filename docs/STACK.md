# Proposed Stack

Status: final-build baseline after Phase 5

## Design rule

Use smallest useful tool set. Prefer existing Codex capabilities, local source code, and simple CLI tools before adding MCP servers or background services.

Normal `codex` remains primary path. Future alternate routing must be opt-in and independent.

## Classification

| Tool or layer | Status | Decision |
|---|---|---|
| Codex | REQUIRED | Existing agent and orchestration layer. Preserve current installation and configuration. |
| Git | REQUIRED | Existing source history, branches, diffs, rollback, and local audit trail. |
| AGENTS.md | REQUIRED | Scoped project instructions. Global rules plus project-specific templates. |
| PowerShell doctor | REQUIRED | Reproducible Windows health checks without exposing secrets. |
| Caveman skills | RECOMMENDED | Already installed. Use for token-aware communication, review, compression, and workflow discipline. Do not reinstall. |
| Cavemem | REMOVE | 0.2.1 benchmark passed recall but failed hard project-isolation and update-precedence gates. |
| Codex bundled browser/computer-use | RECOMMENDED | Existing browser control covers many UI and browser-testing needs. Start here. |
| General worker workflows | KEEP | Reusable routing for research, QA, user guides, product demos, job preparation, documents, spreadsheets, and combined developer/worker tasks. |
| Playwright MCP | KEEP WITH LIMITATIONS | Local MCP configured; focused localhost UI benchmark passed. |
| Serena | REMOVED | Removed from Codex MCP, user runtime, and project configuration. Not required. |
| Context7 | KEEP WITH LIMITATIONS | Credential-free local MCP configured; focused version-sensitive query passed; no always-on injection. |
| GitHub integration | KEEP, AUTH REQUIRED | Official `gh` 2.101.0 installed. Run user-owned login before remote work; plain Git remains local path. |
| Docker | OPTIONAL | Use for isolated services or reproducible databases only when a concrete project requires it. Existing CLI is not proof that daemon works. |
| Graphiti | OPTIONAL, deferred | Temporal graph is attractive only after Cavemem benchmark shows missing entity/history relationships worth Docker plus graph infrastructure. |
| OpenMemory/Mem0 | RESEARCH-ONLY | Useful ecosystem and MCP surface, but adds vector/service/credential choices; no demonstrated need yet. |
| OmniRoute | DEFERRED | Last phase. Normal Codex must remain independently usable. |
| Extra MCP servers | REJECTED by default | Every server adds permissions, context/tool-selection overhead, maintenance, and possible credentials. Add per problem, not by catalog. |
| Duplicate Node/browser/container installations | REJECTED | Repair and reuse existing installations first. |

## Layered architecture

1. Source of truth: current repository files and tests.
2. Project instructions: scoped `AGENTS.md` files.
3. Human documentation: architecture, security, database, decisions, integrations.
4. Search and code inspection: `rg`, Codex, Git, and focused tests.
5. Documentation retrieval: Context7 only for current, version-specific library questions.
6. Persistent memory: none retained. Project docs and decision records supplement source; they never replace source inspection.
7. Advanced graph memory: Graphiti only with measured need.
8. Verification: Git, tests, bundled browser control, and Playwright where repeatable browser tests justify it.
9. Services: Docker only for a concrete isolated dependency.
10. Worker artifacts: use bundled document/PDF/spreadsheet runtimes; use browser-scoped screenshots/video; keep external connectors optional.

## Retrieval policy

| Task | First context source | Escalation |
|---|---|---|
| Instructions | Scoped `AGENTS.md` | Parent/global instructions |
| Architecture | Project docs and current code | Git history, then memory |
| Code location | `rg` and file inspection | Focused tests and Git history |
| Library API | Installed lockfile/version plus official docs | Context7 candidate |
| Previous decision | Decision docs | Verified persistent memory |
| UI behavior | Existing browser control | Playwright regression workflow |
| Change history | Git log/diff | GitHub integration |
| Local service | Direct CLI/status check | Docker only when needed |

## Security boundaries

- Never commit credentials, auth files, cookies, tokens, connection strings, or machine state.
- Back up explicit configuration files before edits. Keep backups outside tracked source or encrypt them.
- Do not broaden Codex trust, sandbox, MCP, Docker, browser, GitHub, or database permissions automatically.
- Treat authentication, authorization/RLS, payments, financial logic, migrations, secrets, production configuration, and destructive operations as high-risk tasks.
- For high-risk work, inspect current source and schema, run stronger validation, and do not rely on memory alone.
- Keep MCP configuration small. Record purpose, permissions, credentials, maintenance status, and disable path for every server.

## Phase order

1. Audit and architecture: complete.
2. Core foundation: complete.
3. Code intelligence: Serena removed; Codex and `rg` remain the default.
4. Memory: Cavemem removed after isolation failure; no replacement retained.
5. Browser verification: bundled browser retained; Playwright MCP deferred.
6. Documentation and reusable setup tooling: implemented locally.
7. Routing: OmniRoute deferred after source ambiguity and no provider credentials; direct Codex remains default.

## Current blockers

- Codex sandbox cannot see host NVM registry state; host normal PowerShell verification passed.
- Python runtime and Docker daemon need verification.
- Context7 and Playwright MCP are configured additively and benchmarked locally.
- Worker smoke tests produced synthetic DOCX, XLSX, PNG, and WebM artifacts. Automated DOCX visual render remains limited by missing LibreOffice `soffice.exe`.

## Phase 3 evidence

- Serena Agent was removed after MCP process and dashboard-window multiplication.
- Context7 uses local stdio without credentials; hosted/API-key modes remain disabled.

## Final-build boundary

No further phase starts automatically. GitHub CLI login, cloud connector OAuth, OmniRoute, and replacement memory require separate review.
