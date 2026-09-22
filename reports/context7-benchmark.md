# Context7 Benchmark

Date: 2026-09-19

## Method

Official-source review compared three possible paths:

1. Codex knowledge plus current project dependency inspection.
2. Manual official-documentation lookup.
3. Context7 CLI/MCP retrieval.

Local Context7 MCP was configured additively with no credential or hosted account.

## Findings

- Context7 provides current, version-specific library docs through CLI plus skills or MCP.
- CLI path requires Node.js 18 or newer. Current machine has Node `v22.23.2` installed but no active NVM default, so npm/npx validation is blocked.
- Hosted MCP requires network access and sends documentation queries to Context7. API key is recommended for higher rate limits; OAuth is available for supported clients.
- Local MCP uses npm package `@upstash/context7-mcp`, which would add Node/npm startup and package supply-chain surface.
- Codex plugin path is documented by the official Context7 repository, but plugin installation and OAuth would add another integration surface.

## Decision

**NEEDS MORE TESTING**.

Result: Context7 MCP v4.1.1 initialized and answered a real version-sensitive query about current Playwright MCP prerequisites and Codex settings. Decision: KEEP WITH LIMITATIONS. Use local stdio on demand; avoid always-on injection and hosted credentials.
