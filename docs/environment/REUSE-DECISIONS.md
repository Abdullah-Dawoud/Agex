# Reuse Decisions

Review date: 2026-09-20

| Candidate | Purpose | Decision | Reason |
|---|---|---|---|
| OpenAI Codex | Agent and local workflow | KEEP | Official existing capability. Apache-2.0. No source copied. |
| Microsoft Playwright | Browser QA, screenshots, video | KEEP | Already configured, active, Apache-2.0, Windows support. Proven on fixture. |
| GitHub CLI `cli/cli` | Issues, PRs, CI, releases | KEEP | Official, active, MIT, Windows support. Installed at user scope. Separate GitHub connector authenticated and read-only repository probe passed. |
| LibreOffice `LibreOffice/core` | DOCX/PDF render backend | OPTIONAL | Mature and free, but large installation and mixed copyleft licenses. Not installed for one QA gap. |
| OBS Studio | Whole-screen recording | REJECT | Active GPL-2.0+ project, but adds GUI capture permissions and duplicates browser-scoped Playwright video. |
| `python-docx` | DOCX authoring | KEEP | Existing bundled package, MIT. Used without copying source. |
| `openpyxl` | XLSX authoring and inspection | KEEP | Existing bundled package, MIT. Used without copying source. |
| Extra browser MCP/automation tools | General web control | REJECT | Duplicates Codex browser, Computer Use, and Playwright MCP. |
| Gmail connector | Read-only email worker | KEEP, READ-ONLY TESTED | Already installed. Harmless empty search passed. Draft/send remain explicit user-authorized actions. |
| GitHub connector | Account workflows | KEEP, READ-ONLY TESTED | Authenticated. Repository listing probe passed. Keep issue, PR, branch, and file writes explicit. |
| Google Drive connector | Account workflows | SUGGESTED, NOT CONNECTED | Official candidate found. No OAuth or remote read performed. |
| Cavemem or replacement memory service | Persistent worker memory | REJECT | Existing evidence shows project-isolation failure. Keep project docs as authority. |
| Google Antigravity CLI (`agy`) | Official finite external worker | USE WHEN INSTALLED | Official headless mode supports JSON/stream-JSON output, cached login, sandbox flag, model pinning, and clean exit. Install only from `antigravity.google`; no credential scraping. |
| Google Antigravity SDK | Programmatic custom agent runtime | DEFER | Official Python SDK exists, but adds dependency and API/auth surface. CLI meets current single-task worker need. |
| `InonB2/multi-agent-orchestration` | Multi-model dispatcher | REJECT | Early local framework; broad process/checkpoint surface exceeds need. Use adapter and receipt ideas only as design inspiration. |
| `andyyaro/orkestra` | Isolated multi-agent worktree runtime | REJECT | More infrastructure and an extra installation than current two-worker read-only requirement. |
| `Augani/agent-orchestrator` | Codex plugin for external CLI jobs | REJECT | Third-party plugin expands trust and tool surface. Reuse scoped permissions concept only. |
| `Sora-bluesky/antigravity-orchestra` | Codex plus Antigravity template | REJECT | Template quality and maintenance not established enough for core setup. |

## Reused or adapted work

Existing Codex browser/computer-use, Playwright MCP, local fixture, document skill, spreadsheet skill, bundled runtimes, doctor, dashboard, backup policy, and project instruction patterns were reused. New code is limited to task-local smoke builders and additive health/reporting surfaces. No third-party source was copied. No attribution file is required beyond retained upstream licenses and package metadata.

## Sources checked

- [`openai/codex`](https://github.com/openai/codex) — official Codex source and Apache-2.0 license.
- [`microsoft/playwright`](https://github.com/microsoft/playwright) — browser automation, MCP, screenshots, and video; Apache-2.0.
- [`cli/cli`](https://github.com/cli/cli) — official GitHub CLI; MIT.
- [`LibreOffice/core`](https://github.com/LibreOffice/core) — document suite and render backend; GPL/LGPL/MPL components.
- [`obsproject/obs-studio`](https://github.com/obsproject/obs-studio) — screen recording; GPL-2.0+.
- [`python-openxml/python-docx`](https://github.com/python-openxml/python-docx) — DOCX authoring; MIT.
- [`Google Antigravity CLI documentation`](https://www.antigravity.google/docs/cli/headless/) — official headless invocation and machine-readable output.
- [`Google Antigravity skills documentation`](https://www.antigravity.google/docs/skills) — compatible `SKILL.md` format and workspace/global skill paths.
- [`Google Antigravity download page`](https://www.antigravity.google/download) — current official CLI installer source and Windows support.

GitHub CLI install was approved as a narrow local addition. GitHub connector authentication completed outside setup; repository listing probe passed. Gmail remained connected and passed a harmless empty search. Google Drive connector remains suggested only. Existing capability won wherever it met acceptance.
