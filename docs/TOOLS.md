# Tool Evaluation

Audit date: 2026-09-19

## Global versus project-local

| Capability | Global | Project-local | Classification |
|---|---|---|---|
| Codex, Git, Caveman, uv | Install and capability | Current project instructions | HYBRID |
| Serena runtime | Removed | No project or user runtime retained | REMOVED |
| Context7 | CLI/MCP capability and credentials | Dependency versions and query context | HYBRID |
| AGENTS.md and architecture docs | Reusable templates | Repository-specific files | PROJECT-LOCAL |
| Serena memory | Removed | No Serena memory namespace retained | REMOVED |

Global capability must not imply global project knowledge. Project knowledge stays in scoped instructions, source, tests, and docs; no Serena state is retained.

## Serena (removed)

Official sources:

- Repository: https://github.com/oraios/serena
- Installation: https://github.com/oraios/serena/blob/main/docs/02-usage/010_installation.md
- Codex client setup: https://oraios.github.io/serena/02-usage/030_clients.html#codex-cli-and-app
- Project workflow: https://oraios.github.io/serena/02-usage/040_workflow.html
- Security guidance: https://oraios.github.io/serena/02-usage/070_security.html

Serena was removed after repeated MCP process and dashboard-window multiplication. Do not reinstall it or recreate a `.serena` project configuration.

Historical result: Serena Agent `1.7.0` was installed through uv and registered as an additive global MCP entry. The registration, project configuration, and user runtime were subsequently removed.

Historical functional result:

- Python fixture: project creation and symbol indexing succeeded; 7 symbols indexed from `src/orders.py`; Python language server started through uv-managed Pyright.
- Setup repository: project auto-configuration succeeded, but PowerShell language-server health check failed because Serena could not write/install `PSScriptAnalyzer 1.25.0` under `C:\Users\isc\.serena`. No ACL workaround was attempted.

Security result: Serena exposed read, symbol, edit, shell, and project-memory tools and could start background language-server processes. It is no longer an approved setup dependency.

Decision: REMOVED. Use Codex, `rg`, Git, and focused tests for code inspection.

## Context7

Official sources:

- Repository: https://github.com/upstash/context7
- Client setup: https://github.com/upstash/context7/blob/master/docs/resources/all-clients.mdx
- Server README: https://github.com/upstash/context7/blob/master/packages/mcp/README.md
- Hosted endpoint: https://mcp.context7.com/mcp

Current official options:

- CLI plus skills: `npx ctx7 setup`; requires Node.js 18 or newer.
- MCP: hosted HTTP endpoint or local `@upstash/context7-mcp` server.
- Codex plugin: official repository documents `codex plugin marketplace add upstash/context7` followed by plugin installation and OAuth.

Security: hosted use sends documentation queries and library identifiers to Context7. A repository file is not inherently sent, but prompts must not include secrets or unnecessary source. API key is recommended for higher rate limits and private repositories. OAuth is available for remote MCP clients that support it. Local stdio mode requires Node/npm and may download packages.

Current result: local credential-free MCP configured (`@upstash/context7-mcp` v4.1.1). A real version-sensitive Playwright MCP query passed. Hosted OAuth/API-key modes remain disabled. Decision: KEEP WITH LIMITATIONS.

Bundled document, PDF, spreadsheet, and browser/computer-use skills are retained. Use workspace dependency paths for artifacts. `ffmpeg`, when present, is optional local post-processing for browser-scoped demo media.

Decision: NEEDS MORE TESTING. Prefer targeted use, not always-on documentation injection.

## Rejected or deferred alternatives

- Graphiti and other memory systems: deferred; Cavemem failed project isolation.
- OmniRoute: deferred; no provider credentials configured.
- Extra browser or code-search MCPs: no evidence they beat existing Codex capabilities.
