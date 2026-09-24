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

## Rejected for policy reasons

- `vercel-labs/agent-skills` (31,491 stars): no licence in the repository — cannot be redistributed.
- `anthropics/skills` document skills and `openai/skills` Figma skills: proprietary terms.
- `oraios/serena`, `getsentry/sentry-mcp`: licence reported as NOASSERTION by GitHub on the research date; not checked further.
- Anything that needs an account or API key as a default recommendation.

## Result

AGEX 2.0 ships **19 curated skills**: 14 instruction skills (pinned to commits `5bf4e78` of obra/superpowers, `49f948f` of openai/skills, `34040c9` of anthropics/skills; every file SHA-256-checked) and 5 tools (Playwright MCP 0.0.82, Chrome DevTools MCP 1.10.1, Context7 MCP 4.1.1, Fetch MCP 2026.8.18, GitHub hosted MCP). Onboarding recommends six, all unticked by default: Test-Driven Development, Code Review, Browser (Playwright MCP), Library Docs (Context7), Web Research (Fetch), Git & GitHub. Every instruction skill was downloaded and hash-verified from GitHub during testing (`AGEX_ONLINE_TESTS=1`).

Limits worth stating: star counts are repository-wide, so they show the source's reach, not a single skill's quality. MCP skills reach only Codex and Claude Code. The catalog does not update itself between AGEX releases.
