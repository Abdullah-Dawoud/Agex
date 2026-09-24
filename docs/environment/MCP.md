# MCP Policy

MCP is an optional integration boundary, not a default tool catalog.

## Review questions

For every server, record:

- problem solved;
- Codex capability or CLI alternative;
- overlap with installed tools;
- tool count and context overhead;
- required credentials;
- filesystem, shell, browser, network, and database permissions;
- maintenance and official source;
- enable/disable method;
- rollback path.

## Current state

Codex retains the Codex-managed `node_repl` MCP, Context7, and Playwright MCP entries, plus bundled browser/computer-use capabilities. Serena was removed. GitHub MCP, memory MCPs, Graphiti, and OmniRoute are not enabled. GitHub CLI is separate and installed locally; it is not authenticated.

Do not edit the existing Codex MCP configuration during Phase 2. Future changes require an explicit backup, additive diff, focused test, and confirmation that plain `codex` still works.

## Phase 3 entries

### Serena (removed)

- Status: REMOVED / NOT REQUIRED.
- Removed from the user Codex MCP configuration, user runtime/config, and this project.
- Do not add a replacement registration or automatic installer.

### Context7

- Purpose: current, version-specific library documentation.
- Scope: hybrid. Global CLI/MCP capability; project dependency/version determines query.
- Credentials: hosted use may use OAuth or API key; no credential configured.
- Network: hosted requests leave machine with documentation query and library identifier.
- Launch: `npx -y @upstash/context7-mcp` (Codex stdio `command` plus separate `args`).
- Credentials: none; local stdio mode. Optional hosted/API-key modes remain disabled.
- Verification: Context7 v4.1.1 initialized and answered a version-sensitive Playwright MCP query.
- Disable: `codex mcp remove context7`.

### Playwright MCP

- Purpose: repeatable browser navigation, accessibility snapshots, interaction, screenshots, viewport changes, and console inspection.
- Launch: `cmd /c npx -y @playwright/mcp@latest`.
- Credentials: none. Browser control is task-scoped; no hosted service or paid account.
- Verification: localhost fixture passed desktop/mobile, DOM/accessibility, form/click, screenshot, and deliberate console-error detection/correction checks.
- Disable: `codex mcp remove playwright`.

## Target policy

Keep always-on MCP small. Prefer enabling specialized servers only for tasks that need them. Never put credentials in this repository or generated examples.

## Memory MCP boundary

No memory MCP server is enabled. Codex local memory is a host capability, not a repository MCP entry. Cavemem 0.2.1 was tested with an additive MCP entry, then removed after project-isolation failure: its search schema accepts query/limit but no project namespace or cwd filter. Graphiti and Mem0/OpenMemory remain research-only because they introduce service, database, embedding, or credential boundaries.
