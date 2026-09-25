# Skills

A **skill** adds know-how or a tool that agents can use. It is separate from an agent adapter: an adapter connects AGEX to an agent; a skill gives agents something extra to work with.

AGEX supports two kinds:

| Kind | What it is | How agents get it |
| --- | --- | --- |
| **Instructions** | A folder in the open [Agent Skills](https://agentskills.io) format: `SKILL.md` (name, description, instructions) plus optional references and scripts. | AGEX lists enabled skills in the prompt (name, description, where `SKILL.md` is) and gives the agent read access to the skill folder. The agent reads it when relevant. Works with Codex, Antigravity, Claude Code and Gemini CLI. |
| **Tools (MCP)** | A Model Context Protocol server: a pinned local command (`npx …@version`, `uvx …==version`) or a hosted `https` endpoint. | Passed to Codex (`-c mcp_servers…`) and Claude Code (`--mcp-config`) for each request. Antigravity and Gemini CLI read MCP servers only from their own settings, so AGEX does not pass MCP skills to them. |

**AGEX never runs skill content itself.** Agents use skills inside their own permissions and sandboxes. A skill cannot widen what an agent may do.

## The catalog

The built-in catalog has 57 individually reviewed entries: 36 instruction skills and 21 tools (MCP servers). 15 entries need an account: 9 with an API key, 6 with the tool's own sign-in. Selection criteria, sources, and the candidates that were rejected are in [reports/skills-research.md](../reports/skills-research.md). Every entry shows its author, licence, version and date, trust level, what it may do, which agents and systems it works with, whether it needs an account or another program, a risk level, and — only where a trustworthy public number exists — a popularity figure with its source and date.

Every entry has a cost label: **LOCAL** (runs on this computer, needs no service), **FREE**, **FREE TIER** (a free plan with limits, from the service's own pricing page) or **PAID**, plus **API KEY REQUIRED** when it needs a key.

**Discover** has search, filters (category, tier, trust, installed, agent, account, cost, this system only) and sorting (recommended, popular, recently updated, name). **Details** shows everything above before you install.

Tiers: Recommended, Popular, Community, Advanced, Requires account, Requires local dependency.
Categories: Developer, Testing, Debugging, Security, Git & GitHub, Web, Research, Documents, Data, Design, DevOps, Productivity, Efficiency, Automation, Routing & Providers. Routers and model providers are connections, not skills: the Routing & Providers category points to the Agents page, where they are set up per agent.

Efficiency tools: **Repomix** (packs a repository into one compact file; its compress mode keeps only signatures, which the project says saves about 70% of tokens; AGEX has not measured this) and **Serena** (symbol-level navigation with language servers, so agents read single functions instead of whole files). **Windows-MCP** (Automation, Windows only, Community, high risk) lets agents operate the desktop; AGEX asks before every use.

**Packs** select several skills at once (Recommended starter pack, Developer Essentials, GitHub Workflow, Web App Builder, Research, Local / Private, DevOps, Security, Token Saver, Documents & Data). A skill already installed is never installed twice. First-run offers only the starter pack, unticked.

## One-click install

**Install** does, in order:

1. Validates the catalog entry (id, safe file paths, pinned commit, a SHA-256 for every file, `https` for hosted servers, no shell characters in MCP commands).
2. Checks compatibility: operating system, minimum AGEX version, whether your enabled agents support it, and whether programs it needs (Node.js, uv, Python, `gh`, a deploy CLI, Chrome…) are installed. A missing program shows **Dependency missing** with a button to the program's official download page; AGEX does not install system-wide programs for you.
3. Shows the permissions the skill declares and lets you set each one to **Always allow**, **Ask each time** or **Don't allow** (community skills start risky permissions at **Ask each time**); offers to add the account key now or later.
4. Downloads each file from the pinned commit on `raw.githubusercontent.com` and checks its SHA-256. One mismatch and nothing is installed.
5. Installs into the AGEX data folder (`skills/<id>`), registers and enables it.

No terminal, no copying folders.

## Permissions and trust

Permissions a skill can declare: read project files, change project files, use the internet, control a web browser, run programs and commands, use Git, act on GitHub with your token, start an MCP tool server, read the clipboard. Higher-risk ones are marked.

- **Always allow**: included in every request.
- **Ask each time**: AGEX asks when a request starts; skipped if you say no.
- **Don't allow**: never included.

AGEX enforces this by including or leaving out the skill. What the agent may do once it has the skill is enforced by the agent's own sandbox and the approval settings.

Trust levels, shown on every card:

| Trust | Meaning |
| --- | --- |
| Official | Published by the company behind the tool or service it connects to (OpenAI, Anthropic, Microsoft, GitHub, Notion, Supabase…). |
| AGEX Curated | An independent project reviewed for the catalog, pinned to a commit, every file checksummed (Superpowers, Caveman, Trail of Bits). |
| Community | A community contribution in the catalog (GitHub Awesome Copilot) or a skill you added from a GitHub link: pinned and checked, reviewed less deeply; shown with a warning, risky permissions start at **Ask each time**. |
| Local | Added by you from a folder, a `.zip` package or a custom MCP server. |

Skills that contain scripts start with "run commands" set to **Ask each time**.

## Accounts

Skills that work with an account say so on the card (**Requires account**) and stay visible before you connect them.

| How it connects | What AGEX does |
| --- | --- |
| API key or token (GitHub, Notion, Linear, Sentry, Supabase, Brave Search, Tavily, Firecrawl) | **Add key** opens a dialog with a link to the provider's page where you create the key. The key goes to the system keychain and reaches the tool only as an environment variable (or bearer header for hosted servers). **Test connection** makes one read-only request to the provider (GitHub, Notion, Supabase) and shows the account name when the provider returns one. **Disconnect** deletes the key. A tool whose key is missing is not given to agents. |
| The tool's own sign-in (GitHub CLI, Vercel, Netlify, Cloudflare Wrangler, Render) | **Sign in** opens a terminal that runs the tool's own login command (for example `vercel login`); the tool keeps the sign-in. The command is limited to the skill's declared program and plain words, so catalog data can never become an arbitrary command. |
| OAuth-only hosted servers | Not in the catalog yet: AGEX would need to run the provider's OAuth flow itself. |

AGEX never asks for passwords.

## Updates

**Skills → Updates** lists catalog skills whose entry has a newer commit or version, with notes, and **Update all**. Updates are verified exactly like installs. Automatic updates are off by default.

The catalog itself ships inside AGEX and updates with AGEX releases, so it works offline. A remotely updatable catalog is prepared but not switched on: it would be published as a release asset, covered by `SHA256SUMS.txt` and the release signature, and AGEX would still validate every entry (pinned commits, file hashes, https-only hosts, restricted sign-in commands) and skip broken entries one by one. It stays off until release signing is configured.

## Your own skills

**Skills → Add your own**:

- **From a GitHub folder link** (`https://github.com/owner/repo/tree/<branch>/path`): AGEX resolves the branch to a commit, downloads every file, refuses symbolic links and anything over 400 files or 20 MB, and records a hash of every file.
- **From a folder** on this computer: copied (never linked) into AGEX.
- **Import a package** (`.zip`): extracted safely — no absolute paths, no `..`, no links, no device names, 20 MB and 400-file limits, sizes enforced while extracting.
- **Add an MCP server**: name, command, arguments, and optionally one secret (stored in the system keychain). It asks before each use.

A `SKILL.md` needs front matter:

```markdown
---
name: my-skill
description: One sentence on when to use it.
---
# Instructions
...
```

## When a skill breaks

At every start AGEX checks each enabled skill: files present, `SKILL.md` readable, every file unchanged since install. A skill that fails is turned off with the message "This skill was disabled because it failed during startup: …". One broken skill never stops AGEX. **Safe mode** starts with all skills off.

## Secrets

Tokens (for example a GitHub token for the GitHub skill) are stored with the operating system: Windows DPAPI, macOS Keychain, Linux Secret Service (`secret-tool`). If Linux has no keyring, AGEX says so and uses a private file (`chmod 600`) that is not encrypted. Tokens reach MCP servers only through environment variables — never on a command line, in logs, diagnostics or exports.

## Creating skills for AGEX

See [DEVELOPMENT.md](DEVELOPMENT.md#creating-a-skill).
