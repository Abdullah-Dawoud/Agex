# Skills research

Date: 2026-09-24. Purpose: decide which skills AGEX offers in its built-in catalog, based on what exists, what is maintained, what the licence allows, what is safe and what AGEX's agents can actually use.

## Ecosystems

| Ecosystem | What it is | Fit for AGEX |
| --- | --- | --- |
| **Agent Skills (`SKILL.md`)** | Open format (spec at agentskills.io, `agentskills/agentskills`, Apache-2.0; ~25.6k stars) for folders of instructions, references and scripts. Supported by Claude Code, Codex, Gemini CLI, Cursor, VS Code/Copilot, OpenCode, Goose and ~40 products listed on agentskills.io. | **Primary format.** Works with every AGEX agent that can read files. Content is text; scripts only run if an agent decides to run them, inside its sandbox. |
| **MCP servers** | Tool servers speaking the Model Context Protocol; official registry at registry.modelcontextprotocol.io; reference servers in `modelcontextprotocol/servers` (~90.6k stars). | **Secondary.** AGEX can pass MCP servers per request to Codex (`-c mcp_servers…`) and Claude Code (`--mcp-config`); Antigravity and Gemini CLI only read their own settings, so MCP skills are limited to two agents. Servers run code, so only pinned, well-known servers are curated. |
| Agent-specific plugin formats (Codex plugins, Claude plugins, Copilot instructions) | Vendor formats | Not used: AGEX needs agent-neutral skills. |
| Prompt collections (`github/awesome-copilot`, `wshobson/agents`) | Large collections, varying format and quality | Not curated wholesale; individual items could be added later after review. |

Popularity evidence below is what the public sources report on the research date: GitHub stars (repository-wide, not per skill) and npm monthly downloads (per package). No number was estimated. PyPI does not publish reliable download counts through its main API, so none is shown.

## Candidates by category

Legend for the recommendation column: **Catalog** (in AGEX 2.0's catalog, recommended ones marked ★), **Next** (reasonable, not yet), **No** (rejected, reason given).

### Coding and planning

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Writing Plans | `obra/superpowers` skills/writing-plans | Turn a request into a step-by-step plan | MIT (repo) | Active (pushed 2026-09-22) | Repo 290,751 stars | Text only | All four CLI agents | Catalog |
| Verify Before Done | `obra/superpowers` verification-before-completion | Require evidence before claiming completion | MIT | Active | Repo 290,751 stars | Text only | All four | Catalog (matches AGEX's own verification) |
| Brainstorming | `obra/superpowers` brainstorming | Explore requirements before building | MIT | Active | Repo 290,751 stars | Bundles a local web server script | All four | No: its "must use before any creative work" trigger fights AGEX's leader, and it starts a server |
| Frontend Design | `anthropics/skills` frontend-design | Deliberate UI design guidance | Apache-2.0 (per skill) | Active (2026-09-22) | Repo 177,839 stars | Text only | All four | Catalog |
| MCP Builder / Skill Creator | `anthropics/skills` | Build MCP servers / skills | Apache-2.0 | Active | Repo 177,839 stars | Scripts | All four | Next (niche) |

### Git and GitHub

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Git & GitHub (GitHub MCP, hosted) | `github/github-mcp-server`, endpoint `api.githubcopilot.com/mcp/` | Issues, PRs, reviews, Actions | MIT | Active (2026-09-22), official | Repo 33,156 stars | Acts with the user's token: stored in OS keychain, passed by env var; https only | Codex, Claude Code | Catalog ★ |
| Fix GitHub CI | `openai/skills` .curated/gh-fix-ci | Diagnose failing PR checks | Apache-2.0 (per skill) | Active (2026-09-08) | Repo 27,593 stars | Uses `gh` + a Python script | All four | Catalog |
| Address PR Comments | `openai/skills` .curated/gh-address-comments | Work through PR review comments | Apache-2.0 | Active | Repo 27,593 stars | Uses `gh` + Python | All four | Catalog |
| Git MCP (`mcp-server-git`) | modelcontextprotocol/servers | Git operations | MIT | Active | Repo 90,569 stars | Local | Codex, Claude | No: every AGEX agent already runs `git` itself |

### Web and research

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Web Research (Fetch MCP) | modelcontextprotocol/servers `mcp-server-fetch==2026.8.18` | Read a web page as text | MIT | Active (release 2026-08-18) | Repo 90,569 stars | Can reach local-network URLs (warned on the card) | Codex, Claude | Catalog ★ |
| Library Docs (Context7 MCP) | `upstash/context7`, `@upstash/context7-mcp@4.1.1` | Current library documentation | MIT | Active (2026-09-24) | 3,451,044 npm downloads last month; repo 62,370 stars | Sends library names to the Context7 service (disclosed) | Codex, Claude | Catalog ★ |
| Brave Search / Exa / Firecrawl MCP | vendors | Web search | MIT | Active | 1.5k / 5.0k / 7.5k stars | Need paid/free API keys and accounts | Codex, Claude | Next: useful, but an account requirement is a poor default for ordinary users |

### Browser

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Browser (Playwright MCP) | `microsoft/playwright-mcp`, `@playwright/mcp@0.0.82` | Drive a real browser | Apache-2.0 | Active (2026-09-18) | 23,800,747 npm downloads last month; repo 37,521 stars | Controls a browser; runs Node | Codex, Claude | Catalog ★ |
| Chrome DevTools MCP | `ChromeDevTools/chrome-devtools-mcp@1.10.1` | Console, network, performance | Apache-2.0 | Active (2026-09-23), official | 7,881,886 npm downloads last month; repo 52,533 stars | Controls Chrome | Codex, Claude | Catalog |
| Playwright CLI skill | `openai/skills` .curated/playwright | Browser automation via CLI (agent-neutral) | Apache-2.0 | Active | Repo 27,593 stars | Runs `npx playwright` | All four | Catalog (covers Antigravity and Gemini, which cannot use MCP skills) |

### Documents

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| PDF Documents | `openai/skills` .curated/pdf | Read, create, check PDFs | Apache-2.0 | Active | Repo 27,593 stars | Python libraries | All four | Catalog |
| Anthropic docx / pdf / pptx / xlsx | `anthropics/skills` | Office documents | **Proprietary** ("All rights reserved", use governed by Anthropic terms) | Active | Repo 177,839 stars | Many scripts | — | **No: licence does not allow redistribution through a third-party catalog** |
| MarkItDown MCP | `microsoft/markitdown` (`markitdown-mcp` 0.0.1a7) | Convert documents to Markdown | MIT | Active | Repo 186,668 stars | Local | Codex, Claude | Next: the MCP package is still alpha |

### Testing and debugging

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Test-Driven Development | `obra/superpowers` | Red-green-refactor discipline | MIT | Active | Repo 290,751 stars | Text | All four | Catalog ★ |
| Systematic Debugging | `obra/superpowers` | Root cause before fix | MIT | Active | Repo 290,751 stars | Includes a small shell helper | All four | Catalog |
| Code Review | `obra/superpowers` requesting-code-review | Structured review by severity | MIT | Active | Repo 290,751 stars | Text | All four (best with sub-agents) | Catalog ★ |
| Web App Testing | `anthropics/skills` webapp-testing | Test local web apps with Playwright (Python) | Apache-2.0 | Active | Repo 177,839 stars | Python scripts | All four | Catalog |

### Security

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Security Best Practices | `openai/skills` | Language-specific security review | Apache-2.0 | Active | Repo 27,593 stars | Text | All four | Catalog |
| Threat Model | `openai/skills` security-threat-model | Repository threat model | Apache-2.0 | Active | Repo 27,593 stars | Text | All four | Catalog |
| Trail of Bits skills | `trailofbits/skills` | Security auditing workflows | CC-BY-SA-4.0 | Active (2026-09-23) | 7,221 stars | Text + tools | All four | Next: strong source; share-alike terms and specialist content need a closer review |

### DevOps

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Fix GitHub CI | (above) | CI failures | Apache-2.0 | Active | — | — | All four | Catalog |
| Docker MCP Gateway | `docker/mcp-gateway` | Run MCP servers in containers | MIT | Active | 1,581 stars | Needs Docker | Codex, Claude | Later |
| AWS MCP servers | `awslabs/mcp` | AWS services | Apache-2.0 | Active | 9,726 stars | Cloud credentials | Codex, Claude | Later: provider-specific |
| Deploy skills (Netlify, Vercel, Cloudflare, Render) | `openai/skills` | Deploy to one provider | Apache-2.0 / MIT | Active | — | Provider tokens | All four | Next: provider-specific |

### Database

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Supabase MCP | `supabase-community/supabase-mcp` | Supabase projects | Apache-2.0 | Active | 2,919 stars | Account token, can change data | Codex, Claude | Later: one provider, destructive potential |
| GenAI Toolbox | `googleapis/genai-toolbox` | Databases for agents | Apache-2.0 | Active | 16,484 stars | Database credentials | Codex, Claude | Later: needs a configuration UI |

AGEX 2.0 ships **no database skill**: every serious option needs credentials and can change data, which needs a better permission experience than "one click".

### Data

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Jupyter Notebooks | `openai/skills` | Clean experiment/tutorial notebooks | Apache-2.0 | Active | Repo 27,593 stars | Python script | All four | Catalog |
| Anthropic xlsx | `anthropics/skills` | Spreadsheets | Proprietary | — | — | — | — | No (licence) |

### Productivity

| Name | Source | Purpose | Licence | Maintenance | Popularity evidence | Security | Agents | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Writing Plans, Verify Before Done | (above) | Planning and discipline | MIT | Active | — | Text | All four | Catalog |
| Notion / Linear / Figma skills | `openai/skills` | Tool-specific workflows | Notion: MIT-style; Figma: proprietary terms | Active | — | Accounts | All four | No for Figma (terms); Notion/Linear later |
| Memory / Sequential thinking MCP | modelcontextprotocol/servers | Scratch memory, step prompting | MIT | Active | — | Local | Codex, Claude | No: overlaps the agents' own features and AGEX sessions |

## Popular Community Additions (catalog expansion, 2026-09-24)

The first catalog (19 entries) was deliberately small. This second pass looked for widely used community skills and account-based integrations, with evidence gathered on 2026-09-24 from the GitHub API, the npm registry, PyPI, the packages' own source code (to confirm which environment variable carries each key) and the vendors' documentation. Stars are for the whole repository.

### Caveman and OmniRoute

| Name | Canonical source | Evidence | Licence | Last update | Purpose | Auth | Dependencies | Risk | AGEX fit | Decision |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **Caveman** | [JuliusBrussee/caveman](https://github.com/JuliusBrussee/caveman) | 107,660 stars; the many other `*/caveman` repositories with the same description are forks of it | `skills/` MIT; engine, proxy, rewriter, MCP and related runtime folders BSL-1.1 (LICENSING.md) | pushed 2026-09-24 | Makes agents answer in compressed text; its own README says the rules cost about 1,000-1,500 input tokens per turn | None | None (instruction skills) | Low: text only | Good: plain SKILL.md folders that every AGEX CLI agent can read | **ADDED**: `caveman`, `caveman-commit`, `caveman-review` from `skills/` (MIT) only. The BSL-licensed proxy and engine are not included. |
| **OmniRoute** | [diegosouzapw/OmniRoute](https://github.com/diegosouzapw/OmniRoute) | 69,814 stars; active (pushed 2026-09-24); several `*/omniroute` repositories with a different description are unrelated forks | MIT | 2026-09-24 | A local OpenAI-compatible AI gateway: one endpoint in front of hundreds of providers, with fallback, load balancing and token compression | Provider keys stored by OmniRoute | Its own server/desktop app | High for AGEX's model: every request (and code) is proxied through a third-party router to whichever providers it picks, which breaks AGEX's per-agent privacy labels | It is a model gateway for the agents, not a skill; AGEX already routes work between agents | **NOT APPROPRIATE as a skill.** Next: an optional "custom endpoint" setting per agent (Codex and Claude Code accept a base URL) so users who run OmniRoute can point an agent at it knowingly, with the privacy label changed to "via OmniRoute". |

### Other candidates

| Name | Source | Evidence | Licence | Updated | Auth | Dependencies | Risk | Decision |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Superpowers: Executing Plans, Receiving Code Review, Finish a Branch, Git Worktrees | obra/superpowers | 291,063 stars | MIT | 2026-09-18 | None | Git | Low-medium | **ADDED** |
| Brainstorming, Subagent-Driven Development, Dispatching Parallel Agents | obra/superpowers | as above | MIT | | None | | | REJECTED: they take over the whole session or assume Claude sub-agents, which conflicts with AGEX's leader |
| MCP Server Builder, Skill Creator | anthropics/skills | 177,925 stars | Apache-2.0 per skill | 2026-09-10 | None | Python | Medium (scripts) | **ADDED** (Advanced) |
| Web Artifacts Builder, Doc Co-authoring | anthropics/skills | | Apache-2.0 / no per-skill licence | | | | | REJECTED: claude.ai-specific / licence not stated per skill |
| Deploy to Vercel, Netlify, Cloudflare, Render | openai/skills (Vercel's is MIT by Vercel) | 27,608 stars | Apache-2.0 / MIT | 2026-06-23 | The tool's own CLI login | Vercel CLI / Node.js / Render CLI | Medium: deploys with your account | **ADDED** (Requires account) |
| ASP.NET Core, WinUI 3 Apps (Windows only), Security Ownership Map | openai/skills | as above | Apache-2.0 | 2026-06-23 | None | .NET SDK / Python, Git | Low-medium | **ADDED** |
| Linear, Notion, Sentry workflow skills | openai/skills | as above | Apache-2.0 / MIT (Notion) | | via MCP | | | NEXT: they are written for Codex's own connector names; the MCP tools below were added instead |
| Screenshot, OpenAI Docs, Speech, Transcribe | openai/skills | | Apache-2.0 | | OpenAI key / screen access | | | REJECTED for now: whole-screen capture is too sensitive for one-click; the others need an OpenAI API key or OpenAI's docs connector |
| Security Diff Review, Property-Based Testing, Modern Python Tooling, Semgrep Static Analysis | trailofbits/skills | 7,226 stars | CC-BY-SA-4.0 (downloaded from the source, not redistributed by AGEX) | 2026-09-21 | None | Git / uv / Semgrep | Low-medium | **ADDED** (AGEX Curated) |
| Codebase Onboarding Map, CodeQL Setup | github/awesome-copilot (423 community skills) | 39,352 stars | MIT | 2026-09-24 | None | Python | Medium: community contribution with a scan script | **ADDED** as the two **Community** entries, with a stronger warning |
| Brave Search MCP | brave/brave-search-mcp-server | 58,065 npm downloads/month | MIT | 2026-09-17 | API key (`BRAVE_API_KEY`) | Node.js | Medium | **ADDED** |
| Tavily MCP | tavily-ai/tavily-mcp | 77,144 npm downloads/month | MIT | 2026-08-05 | API key (`TAVILY_API_KEY`) | Node.js | Medium | **ADDED** |
| Firecrawl MCP | firecrawl/firecrawl-mcp-server | 126,701 npm downloads/month | MIT | 2026-09-23 | API key (`FIRECRAWL_API_KEY`) | Node.js | Medium | **ADDED** |
| Exa (hosted) | exa-labs/exa-mcp-server | 248,026 npm downloads/month | MIT | 2026-08-18 | None (anonymous, rate-limited) | None | Low | **ADDED** |
| Notion MCP | makenotion/notion-mcp-server | 634,404 npm downloads/month | MIT | 2026-09-20 | Integration token (`NOTION_TOKEN`), test via `/v1/users/me` | Node.js | Medium | **ADDED** |
| Linear (hosted) | mcp.linear.app | Linear docs: API key accepted as bearer header | Hosted service | 2026-09-24 | API key | None | Medium | **ADDED** |
| Sentry MCP | getsentry/sentry-mcp | 417,238 npm downloads/month | FSL-1.1-ALv2 (source-available, becomes Apache-2.0 after two years) | 2026-09-24 | Auth token (`SENTRY_ACCESS_TOKEN`) | Node.js | Medium | **ADDED** (licence shown on the card) |
| Supabase MCP | supabase/mcp | 406,869 npm downloads/month | Apache-2.0 | 2026-09-17 | Personal access token (`SUPABASE_ACCESS_TOKEN`) | Node.js | High: database access, so AGEX starts it with `--read-only` | **ADDED** |
| Microsoft Learn Docs, Cloudflare Docs, DeepWiki (hosted) | Microsoft, Cloudflare, Cognition | endpoints answered an MCP `initialize` request on 2026-09-24 | Hosted services | 2026-09-24 | None | None | Low | **ADDED** |
| AWS Documentation MCP | awslabs/mcp | PyPI 1.2.1 | Apache-2.0 | 2026-09-08 | None | uv | Low | **ADDED** |
| Stripe MCP | stripe/ai | 56,858 npm downloads/month | MIT | | Secret key | Node.js | High | NEXT: its documented setup passes the secret key on the command line (`--api-key`), which AGEX never does |
| Azure MCP | @azure/mcp | 537,523 npm downloads/month | MIT | | Azure CLI login | Node.js | Medium | NEXT: only a beta version (3.0.0-beta.46) is published |
| Cloudflare account servers, Atlassian, Slack, Notion hosted | vendors | | | | OAuth only | | | NEXT: AGEX does not run OAuth flows for hosted MCP servers yet |
| Sequential Thinking, Memory, Git MCP | modelcontextprotocol/servers | 487,276 npm downloads/month (sequential thinking) | MIT | | None | | | REJECTED: duplicate what agents and AGEX sessions already do |
| MarkItDown MCP | microsoft/markitdown | | MIT | | None | uv | | NEXT: still alpha |
| Serena | oraios/serena | | NOASSERTION on GitHub | | | | | REJECTED: licence unclear |
| `vercel-labs/agent-skills` | | 31,491 stars | no licence | | | | | REJECTED: cannot be redistributed |

## Rejected for policy reasons

- `vercel-labs/agent-skills` (31,491 stars): no licence in the repository — cannot be redistributed.
- `anthropics/skills` document skills and `openai/skills` Figma skills: proprietary terms.
- `oraios/serena`: licence reported as NOASSERTION by GitHub on the research date. (`getsentry/sentry-mcp` was listed here in the first pass; its npm package states FSL-1.1-ALv2, and it was added in the second pass.)
- Anything that needs an account or API key as a default recommendation (account-based tools are in the catalog, but never pre-selected).

## Result

The catalog now has **53 entries**, each reviewed individually: **36 instruction skills** and **17 MCP tools**.

- Trust: 35 Official, 16 AGEX Curated, 2 Community.
- 14 need an account: 8 through an API key or token that AGEX stores in the system keychain (GitHub, Notion, Linear, Sentry, Supabase, Brave Search, Tavily, Firecrawl) and 6 through the tool's own CLI sign-in (GitHub CLI ×2, Vercel, Netlify, Cloudflare, Render).
- Instruction skills are pinned to commits `5bf4e78` (obra/superpowers), `2fd153c` (JuliusBrussee/caveman), `34040c9` (anthropics/skills), `49f948f` (openai/skills), `32e34f8` (trailofbits/skills) and `1f56440` (github/awesome-copilot), with a SHA-256 for every file. All 36 were downloaded from GitHub and verified file by file on 2026-09-24 (`AGEX_ONLINE_TESTS=1`).
- MCP packages are pinned to exact versions; hosted endpoints are official vendor endpoints over https.
- Nine packs group them; first-run offers only the six-skill starter pack, unticked.

Limits worth stating: star counts are repository-wide, so they show the source's reach, not a single skill's quality. MCP tools reach only Codex and Claude Code. Connection tests exist only where the provider has a free read-only "who am I" request (GitHub, Notion, Supabase); other keys are checked the first time an agent uses them. The catalog updates with AGEX releases; a signed remote catalog is prepared but off until release signing is configured.
