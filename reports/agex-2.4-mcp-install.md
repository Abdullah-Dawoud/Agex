# AGEX 2.4 — one-click MCP install report

Date: 2026-09-27. Version: 2.4.0, published as a pre-release. The v2.2.0 and v2.3.0 tags and releases are unchanged.

## Real flows (Windows 10, Debug build, isolated data folder)

Node.js and uv on the system were hidden with `AGEX_HIDE_TOOLS=node,uv`, so every runtime below was installed by AGEX.

| Flow | Type | Result |
| --- | --- | --- |
| Install Node.js from the Google Chrome card | missing dependency | Node.js LTS from nodejs.org, checksum verified, into `<data>\runtimes\node` |
| Browser (Playwright MCP) | npm, browser | `@playwright/mcp@0.0.82` installed; 25 tools; Ready |
| Install uv from the Windows desktop control card, then continue | missing dependency | uv from its GitHub release, checksum verified; the connection wizard opened next |
| Windows-MCP | PyPI with uv | `windows-mcp==0.8.5` installed; 20 tools; Ready (the 2.3 pywin32 file lock no longer occurs with copy mode) |
| Web search (Exa) | hosted | 2 tools; Ready |
| Library Docs (Context7) | npm | `@upstash/context7-mcp@4.1.1` installed; 2 tools; Ready |
| Update on Context7 (record set to 4.1.0) | update | 4.1.1 installed again; Ready |
| Firecrawl | npm, needs a key | installed; stopped at "Firecrawl API key is still needed" with Add key |
| Autodesk AI Bridge from a locally built package | bridge | host, AutoCAD bundle and settings installed; MCP handshake with 99 tools; AutoCAD 2027 shown as Installed, not connected |
| Uninstall, then Install from the published v2.4.0 asset | bridge | everything removed; reinstalled from `autodesk-ai-bridge-win-x64.zip`, checked against `SHA256SUMS.txt`; 99 tools |

Found and fixed during these runs:
- a dialog opened by the code that runs when another dialog closes could not be closed (the wizard's next step after Install dependency);
- npm install scripts could start a different Node.js from PATH (Firecrawl's `tldjs` postinstall).

## Not verified live

- Revit or AutoCAD connected to the bridge: Revit is not installed on the test machine, and AutoCAD 2027 exits at start with "Unhandled Delayload AcJsCoreStub.crx" also when the bridge plug-in is removed.

## Tests

207 tests pass (new: downloads and checksums, archive path escape, install journal rollback, pinned package parsing, missing runtime with rollback, Install dependency state, managed launch and Update, Disconnect with package removal and reconnect, bridge detection, bridge install and uninstall, bridge checksum failure, bridge package without host, bridge rollback, connected-program parsing). The bridge's own 16 tests pass and run in CI with the package build.
