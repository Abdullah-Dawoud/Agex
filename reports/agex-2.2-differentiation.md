# AGEX 2.2 — differentiation report

Date: 2026-09-25. Version: 2.2.0, published as a pre-release after acceptance. The v2.1.0 release and tag are unchanged.
Checked on the new Debug build on Windows 10 (display at 125% scaling) with isolated data folders. The installed AGEX in `%LOCALAPPDATA%\Programs\AGEX` was not touched.

**CHAT-FIRST UI**
Done. Home is now a conversation:
- With no request yet: "What do you want to do?" with team quick starts. A chosen team shows its readiness, "Your tools" with the next action for each, and example requests to click.
- After sending: your request as a bubble, progress, the answer with "Changes: N files +A -R", and the result actions (Continue, Retry, New conversation).
- The composer sits at the bottom in two compact rows: text, Attach, Team, Skills, Efficiency, and Agents shown as avatars.
- Timeline and tasks fold into a "Steps and tasks" section. Technical detail lives in the side panel.

Checked at 1366 x 768 (125%), 1600 x 900 and 1920 x 1080, in dark, light and high contrast.

**HOME SKILL SELECTOR**
Done. The Skills button shows "Skills: Auto (n)" or "n chosen" and opens a picker:
- Auto, or Choose for this request (switched-off skills can be used once);
- profiles: 6 built-in, plus your own saved selections;
- Pin to team;
- chips with remove, and "Back to Auto".

Tips can add suggested skills to the request.

**PER-AGENT SKILLS**
Done, under Advanced in the picker. A skill ticked for some agents goes only to them; a skill ticked for none goes to every compatible agent (the default, Auto). Enforced in the engine (`RequestEngine.SkillFor`) for both instruction skills and MCP servers, with tests.

**CONNECTIONS HUB**
Done: a new Connections page and a Connections tab in the side panel.
- Covers 42 connections on the test computer: agents' tools, programs, editors, web services and MCP servers.
- Each has one state: Connected (or "Ready (works with its files)" / "Ready (opens projects)" when AGEX does not control the program), Available to connect, Installed but not connected, Not installed, Sign-in required, Needs another program, or Not supported yet.
- Each has its next action: Connect, Official download, Sign in, Add key, Test, Settings, Disconnect, Turn on, Open project, Use as preferred editor, Learn how to connect, Find community servers, Enable a compatible agent.
- Filters: All, Connected, Needs attention, Available, On this computer.
- The active team's recommended connections come first, on the page, in the panel and on Home.
- Add connection searches what AGEX knows; for anything else it searches the public MCP Registry on request.

**MCP DISCOVERY**
Done.

Existing configurations are read from:
- Claude Code (user and per-project);
- Claude Desktop;
- Codex (`config.toml`, including quoted names and multi-line arrays);
- Gemini CLI;
- Antigravity;
- VS Code (`mcp.json` and `settings.json`, with comments);
- Cursor;
- Windsurf;
- OpenCode;
- the project's `.mcp.json`, `.vscode`, `.cursor` and `.gemini` files.

On the test computer AGEX found 5 servers: GitKraken in Claude Code, Zapier in Cursor, and three in Codex. For each found server:
- Actions are Import into AGEX, Use as-is and View details. Setting values are shown by name only, and are copied into the system key store only if you tick "Copy its settings" when importing.
- If the server matches a reviewed catalog entry (context7, playwright), the first button is "Use reviewed version".

The public MCP Registry search is live. Results are labelled "not reviewed" and show exactly what will run. Only npm, PyPI and https servers are accepted, package names are checked, and servers start with "ask each time".

**PROGRAM CONNECTIONS**
Detected on this computer: AutoCAD, Excel, VS Code, Cursor, Antigravity IDE, Git, GitHub CLI, Docker, Node.js, uv, Python, Chrome, Edge. AutoCAD shows "Needs another program" (the Autodesk AI Bridge is not installed).

| Program or service | How AGEX connects |
| --- | --- |
| Revit, AutoCAD | MCP through the Autodesk AI Bridge, when it is installed. Otherwise "Learn how to connect". |
| Navisworks, Bluebeam Revu | Files only (clash reports, PDF markups). |
| SketchUp, Blender | No reviewed connection yet. Agents can write scripts that you run yourself. |
| Figma | MCP (Framelink, read only, with a token). |
| Chrome, Edge | MCP (Playwright, Chrome DevTools). |
| Word, Excel, PowerPoint, LibreOffice, PDF | Files only. |
| Obsidian | Open the vault as a project. |
| VS Code, Cursor, Windsurf, Antigravity IDE, Zed, Visual Studio, JetBrains | Open project, Use as preferred editor. |
| Git, Docker, Node.js, uv, Python, ffmpeg | Command line. |
| GitHub CLI | Command line, with its own `gh auth login`. |
| GitHub, Notion, Linear, Sentry, Supabase, Vercel, Netlify, web search, developer docs, Firecrawl | Reviewed catalog skills. |
| Slack, Google Drive, Google Calendar, Gmail | Not supported yet. Community servers can be found in the registry. |
| Google Analytics | Not set up automatically (needs the user's own Google Cloud OAuth client). The alternative is a CSV export. |
| Windows desktop control, local models | Windows-MCP and Ollama. |

**TEAMS IMPROVED**
All 11 teams improved: Software Builder, Research Lab, Architecture & BIM, Marketing & Growth, Computer Operator, Job Search & Applications, Document Office, Data Analyst, Security Review, DevOps & Release, and Local Private AI.
- Each team gained input files, sensitive actions (also added to its brief, so agents ask first) and the paid tools it works alongside.
- Setup reads READY / TO SET UP / OPTIONAL / PROGRAMS AND SERVICES, with "Set up recommended" and "Use free alternative".
- Architecture & BIM also covers Navisworks, Bluebeam and SketchUp.
- No new teams were added; the reasons are in the research note.

**TEAM RESEARCH**
See reports/team-research.md. Tasks and technology come from O*NET for Architects, Market Research Analysts and Marketing Specialists, Secretaries and Administrative Assistants, and Business Intelligence Analysts. MCP facts come from the official registry, npm, PyPI and each project's pages.

Real Estate, Content Studio, Startup Launch, Customer Support and E-commerce were considered and not added. Their core systems have no official or reviewed connection, so they would repeat existing teams.

**FREE-FIRST**
- When several tools connect a service, the one that needs no account comes first. For example, Web search offers Exa, which needs no key, before Brave, which needs a key (tested).
- Every connection shows its cost (Free, Free tier, Local, Paid).
- Paid programs show a free option where one exists, for example LibreOffice for Microsoft Office or the PDF skill for Bluebeam, with "Get … (free)".
- Tips suggest Local-first when Ollama is ready.

**FILE TREE**
Done: the side panel's Files tab.
- Lazy project tree that follows the project's ignore rules.
- Created and modified files are coloured with A/M marks; folders with changes are marked; deleted files are listed above the tree.
- Click previews the file, or opens its diff when it changed.

**CHANGE COUNTS**
Done. Every change records lines added and removed, and the status Added, Modified, Deleted or Renamed. A rename is found from identical content.

When a request starts, AGEX keeps the text of the project's files in memory (512 KB per file, 40 MB in total), so counts work without Git. The result shows "Changes: 3 files +2 -38", which opens the Changes tab.

Checked on a test run:
- guide.md: +1;
- app.py: +1 / -20;
- old-config.json: deleted, -18.

**DIFF VIEW**
Done. Unified and side-by-side views, with 3 lines of context and "..." for long unchanged stretches, and line numbers. Uses the in-memory baseline; falls back to Git for older sessions.

**SMART RECOMMENDATIONS**
Done. Rule-based and visible, at most two at a time, and a dismissed tip never returns. Tips cover:
- connecting a detected program the team needs;
- the PDF skill for a PDF attachment;
- web research for research wording;
- Code Review plus Test-Driven Development for code changes;
- Save tokens for projects of 3,000 files or more;
- Local-first when a local model is ready.

**CREATIVE IMPROVEMENTS**
- "Use reviewed version" for found MCP servers that match the catalog: no raw copy and no duplicate secrets.
- "Use now" on ready skills: the next step after installing.
- New conversation button and Ctrl/Cmd+N.
- Example requests on the team card fill the composer.
- Found servers show setting names only, never values, until you import.
- Dialogs keep their buttons visible on short windows.
- Fixed a 2.1 bug: the Autodesk bridge was saved under a different id and never showed as connected.

**TESTS**
130 of 130 pass, twice. There are 18 new ConnectionTests:
- line diff;
- baseline added, modified, deleted and renamed files with counts;
- JSON, JSONC and OpenCode configurations;
- Codex TOML;
- import reads secret values only on request;
- registry parsing and unsafe-package refusal;
- every connection has a next action;
- connection states for sign-in, connected and turned off;
- free-first ordering;
- team connections;
- bridge id;
- per-request skill selection;
- per-agent skills in the engine;
- profiles;
- team research fields in briefs;
- recommendations.

`git diff --check` is clean. Build: 0 warnings.

Visual checks covered:
- Home with and without a team;
- a running and finished request with changes;
- Changes, unified and side-by-side diff;
- the tree;
- the skill picker;
- Connections, Add connection and registry results;
- found MCP connections;
- team setup.

These were run at the three sizes, in dark, light and high contrast. Screenshots are in `%USERPROFILE%\AGEX-2.2-evidence\screenshots` (not committed).

**COMMIT**
The commit on branch `codex-live-agent-dashboard` containing this report. Not pushed, not published.

**REMAINING**
- OAuth-based hosted MCP servers (for example Figma's official server) need the agent's own sign-in flow; AGEX supports bearer tokens only.
- Slack, Google Drive, Google Calendar and Gmail have no official MCP server; only unreviewed community servers exist.
- A Terminal tab in the side panel was not added.
- Line counts for requests made before AGEX was restarted come from Git only.
- Checked on Windows only this pass. The code has no Windows-only parts except program detection paths, which include macOS and Linux locations.
