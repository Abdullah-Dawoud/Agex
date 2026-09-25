# Team and connection research (AGEX 2.2)

Date: 2026-09-25. This note records where the team definitions and the connection catalog come from, so later changes can be checked against sources rather than guesses.

## Method

- Tasks and software for each profession come from O*NET OnLine (U.S. Department of Labor), the "Tasks" and "Technology Skills" sections of each occupation. AGEX uses them to decide what a team does, which files it starts from and which programs matter.
- MCP servers were checked in the official MCP Registry (`registry.modelcontextprotocol.io/v0/servers`), on npm and PyPI, and on each project's own page.
- A tool is only listed as connectable when AGEX can actually start it. Where no official or reviewed server exists, AGEX says so and offers a search of the registry, labelled "not reviewed".

## Professions (O*NET)

| Team | O*NET occupation | What the source shows | How the team uses it |
| --- | --- | --- | --- |
| Architecture & BIM | 17-1011.00 Architects | Tasks: scale drawings with CAD, contract documents, client reviews, construction observation, integrating engineering elements. Technology: Autodesk Revit and SketchUp Pro (in demand), AutoCAD Civil 3D, MicroStation, Rhino, Microsoft Office, Adobe Creative Cloud and InDesign, Microsoft Project, Excel. | Input files: PDF drawing sets, .rvt, .dwg, IFC exports, schedules, site photos. Programs: Revit and AutoCAD (through the Autodesk AI Bridge), Navisworks, Bluebeam and SketchUp (files only), Excel or LibreOffice. Sensitive: changing a model, issuing drawings, sending documents. |
| Marketing & Growth | 13-1161.00 Market Research Analysts and Marketing Specialists | Tasks: reports with graphics, competitor intelligence on pricing and distribution, customer and demographic analysis, surveys, measuring campaign effectiveness. Technology: Google Analytics, Google Ads, HubSpot, WordPress, Salesforce, Tableau, Excel, Canva, Adobe. | Web search, Firecrawl and browser for competitor and SEO work; analytics as CSV exports (the official Google Analytics MCP server needs a Google Cloud OAuth client, so AGEX does not set it up automatically). Sensitive: publishing, spending ad budget, emailing customers. |
| Document Office / Computer Operator / Job Search | 43-6014.00 Secretaries and Administrative Assistants | Tasks: databases, word processing, filing systems, email flow, scheduling, meeting notes and correspondence. Technology: Microsoft Office, Google Workspace, Slack, Teams, Zoom, Adobe Acrobat, SharePoint, Salesforce. | Office files (read and written without controlling the programs), PDF, browser forms with confirmation before submit, email only with approval. Slack, Google Drive, Calendar and Gmail have no official MCP server: AGEX shows community options as unreviewed. |
| Data Analyst | 15-2051.01 Business Intelligence Analysts | Tasks: standard and custom reports, dashboards, trend analysis, specifications for reports, collecting data from public and purchased sources. Technology: Power BI, Tableau, Looker, Alteryx, SQL (PostgreSQL, Snowflake, T-SQL), SAS, SPSS, Google Analytics, Excel. | Python and notebooks, CSV/Excel exports, Supabase (read-only) for databases. Paid BI tools are listed as tools the team works alongside, not replaced. |

Software Builder, Security Review, DevOps & Release, Research Lab and Local Private AI were already built around the tools their work needs (Git, GitHub, CI logs, security scanners, web research, local models). This pass added input files, sensitive actions and paid alternatives to each.

## Teams considered and not added

Real Estate Analysis, Content Studio, Startup Launch, Customer Support and E-commerce Ops were considered. Their key systems (listing services, help desks such as Zendesk, stores such as Shopify) have no official or reviewed MCP server that AGEX can start today. As teams they would only restate Research Lab, Marketing & Growth or Document Office. They were left out rather than shipped as shallow copies.

## MCP ecosystem findings

- The official MCP Registry is live and searchable, but publishing is open and search is not ranked by quality. Searches for "slack", "gmail", "google drive" and "analytics" return many community and commercial wrappers and no vendor-official server.
- Figma's official remote server (`com.figma.mcp/mcp`) uses OAuth, which AGEX cannot perform for agents yet. The community Framelink server (GLips/Figma-Context-MCP, MIT, about 15.9k stars) reads designs with a personal access token and was added to the catalog as Community.
- Google Analytics' official server (googleanalytics/google-analytics-mcp, Apache-2.0, read-only) needs Application Default Credentials created with the user's own OAuth client. It is listed with that explanation and a CSV-export alternative.
- On the test computer AGEX found MCP servers in Claude Code (`~/.claude.json`), Cursor (`~/.cursor/mcp.json`) and Codex (`~/.codex/config.toml`). Two of the Codex servers (context7, playwright) match reviewed catalog entries, so AGEX offers the pinned reviewed version instead of copying the configuration.

## Sources

- https://www.onetonline.org/link/summary/17-1011.00
- https://www.onetonline.org/link/summary/13-1161.00
- https://www.onetonline.org/link/summary/43-6014.00
- https://www.onetonline.org/link/summary/15-2051.01
- https://registry.modelcontextprotocol.io/v0/servers
- https://github.com/GLips/Figma-Context-MCP
- https://github.com/googleanalytics/google-analytics-mcp
