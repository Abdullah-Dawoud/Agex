# Skills

A **skill** adds know-how or a tool that agents can use. It is separate from an agent adapter: an adapter connects AGEX to an agent; a skill gives agents something extra to work with.

AGEX supports two kinds:

| Kind | What it is | How agents get it |
| --- | --- | --- |
| **Instructions** | A folder in the open [Agent Skills](https://agentskills.io) format: `SKILL.md` (name, description, instructions) plus optional references and scripts. | AGEX lists enabled skills in the prompt (name, description, where `SKILL.md` is) and gives the agent read access to the skill folder. The agent reads it when relevant. Works with Codex, Antigravity, Claude Code and Gemini CLI. |
| **Tools (MCP)** | A Model Context Protocol server: a pinned local command (`npx …@version`, `uvx …==version`) or a hosted `https` endpoint. | Passed to Codex (`-c mcp_servers…`) and Claude Code (`--mcp-config`) for each request. Antigravity and Gemini CLI read MCP servers only from their own settings, so AGEX does not pass MCP skills to them. |

**AGEX never runs skill content itself.** Agents use skills inside their own permissions and sandboxes. A skill cannot widen what an agent may do.

## The catalog

The built-in catalog is small and curated (19 skills). Selection criteria, sources and the candidates that were rejected are in [reports/skills-research.md](../reports/skills-research.md). Every entry shows its author, licence, version, what it may do, which agents it works with, and — only where a trustworthy public number exists — a popularity figure with its source and date (for example npm monthly downloads of an MCP package, or GitHub stars of the repository it comes from).

Categories: Recommended, Developer, Testing, Debugging, Security, Git & GitHub, Web, Research, Documents, Data, Design, DevOps, Productivity.

## One-click install

**Install** does, in order:

1. Validates the catalog entry (id, safe file paths, pinned commit, a SHA-256 for every file, `https` for hosted servers, no shell characters in MCP commands).
2. Checks compatibility: operating system, minimum AGEX version, whether your enabled agents support it, and whether tools it needs (Node.js, uv, Python, `gh`) are installed — warnings, not blockers.
3. Shows the permissions the skill declares and lets you set each one to **Always allow**, **Ask each time** or **Don't allow**; asks for a token when the skill needs one.
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
| Curated by AGEX | Reviewed for AGEX's catalog, pinned to a commit, every file checksummed. |
| Official publisher | An MCP server from the vendor of the service or tool, pinned to a package version. |
| Community (not reviewed) | Added by you from a GitHub link. AGEX pins the commit and warns if files change, but has not reviewed it. |
| Your own (local) | Added from a folder, a `.zip` package or a custom MCP server. |

Skills that contain scripts start with "run commands" set to **Ask each time**.

## Updates

**Skills → Updates** lists curated skills whose catalog entry has a newer commit or version, with notes, and **Update all**. Updates are verified exactly like installs. Automatic updates are off by default. Newer catalogs arrive with AGEX updates.

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
