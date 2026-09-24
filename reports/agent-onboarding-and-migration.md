# Agent onboarding, model discovery, skills expansion and name migration

Date: 2026-09-24. Base: `main` at `c7dd65a` (v2.0.0 pre-release plus CI commits). Test machine: Windows 10 x64 with Codex 0.155.1, Antigravity 1.2.10, Claude Code 2.0.14 (not signed in), Gemini CLI 0.9.0 (not signed in) and Ollama 0.32.14.

## Summary

| Item | Result |
| --- | --- |
| AGENT INSTALL UX | **READY** for Codex, Claude Code and Gemini CLI (one-click install from the official npm package after a confirmation dialog); **manual with official instructions** for Antigravity and Ollama, whose vendors publish script/app installers only. Install was not run for real in this pass (all agents were already installed); the plan, confirmation and "Node.js missing" paths are tested. |
| AGENT SIGN-IN UX | **READY**: Sign in opens the agent's own sign-in in a terminal; AGEX re-checks sign-in automatically where the check is quota-free, and offers Check sign-in otherwise. Real sign-in flows were not executed in this pass (no new accounts were signed in). |
| MODEL DISCOVERY | **PARTIAL by nature**: Codex, Antigravity and Ollama report real model lists; Claude Code and Gemini CLI have no listing command and honestly report "not offered". |
| MODEL PICKER | **READY**: Auto plus the reported models; custom ID under Advanced; disappeared model falls back to Auto with a notice. |
| SUPPORTED INSTALLABLE AGENTS | Codex (`@openai/codex`), Claude Code (`@anthropic-ai/claude-code`), Gemini CLI (`@google/gemini-cli`) |
| SUPPORTED LOGIN FLOWS | Codex (`codex login`), Antigravity (`agy` browser sign-in), Claude Code (`claude` browser sign-in / `/login`), Gemini CLI (`gemini` → Login with Google). Ollama needs none. |
| MODEL DISCOVERY AGENTS | Codex (`codex debug models`), Antigravity (`agy models`), Ollama (`/api/tags`) |
| LOCAL FOLDER | `setup` (unchanged; see below) |
| GITHUB ORIGIN | `https://github.com/Abdullah-Dawoud/Agex.git` |
| ACTIVE AI-COGY REFERENCES | **0** |
| MIGRATION-ONLY REFERENCES | **2** (`src/Agex.Core/AgexInfo.cs` legacy-name list; `tests/Agex.Tests/OnboardingTests.cs`, which assembles the old name to check that no active file contains it) |
| HISTORICAL REFERENCES | **2** in `reports/release-2.0.0.md`, plus the mentions in this report. Outside the repository: the existing v2.0.0 install's `install.json` (migrated automatically by the next AGEX version). |
| TESTS | 80 passed, 0 failed (66 before; 14 new). Online test: all 36 catalog instruction skills downloaded and every file hash verified. |
| COMMIT | see the final message (`feat: improve agent onboarding, model discovery and AGEX migration`) |

## Real checks (no model quota used)

| Check | Result |
| --- | --- |
| `agex agents` | Codex INSTALLED_READY ("Logged in using ChatGPT"), Antigravity INSTALLED_READY, Claude Code INSTALLED_AUTH_REQUIRED, Gemini CLI INSTALLED_AUTH_REQUIRED, Ollama INSTALLED_READY (local) |
| Codex models | 7 models from `codex debug models` with context windows and reasoning levels (hidden models excluded) |
| Antigravity models | 14 models from `agy models` |
| Ollama models | 5 local and 2 cloud models, cloud ones marked |
| Claude Code / Gemini CLI models | "has no command that lists its models" (not guessed) |
| Claude / Gemini sign-in | Sign-in required — matches the earlier real runs that failed with "not signed in" |
| Desktop Agents page | Cards show state, version, sign-in, selected model, model count and only applicable actions |
| Desktop Skills page | Search, filters, sort, packs, details view and readiness render with 53 entries |

## Name migration

- **Repository**: the GitHub repository `Ai-COGY` was renamed to `Agex`. The local `origin` now uses `https://github.com/Abdullah-Dawoud/Agex.git`; the updater, both installers, the README and install docs, the v2.0.0 release notes file and the CI install job use `Abdullah-Dawoud/Agex`. The canonical raw installer URLs and release API answer 200.
- **Install marker migration**: v2.0.0 installers wrote `repository: Abdullah-Dawoud/Ai-COGY` into `install.json`. At start, AGEX rewrites it once to the new name, keeps the old value in `migrated_from_repository`, writes through a temporary file, and never deletes data. Nothing reads that field for downloads, so already-installed 2.0.0 copies keep working through GitHub's redirect until they update.
- **Local folder**: the repository folder is `C:\Users\isc\OneDrive\Documents\ChatGPT\setup`. It was never named AI-COGY, so no rename was done (renaming the folder of the running session would also break it). No shortcut, recent-project entry or AGEX setting refers to an AI-COGY path; the Start Menu shortcut points to `%LOCALAPPDATA%\Programs\AGEX`.
- **Install and data paths**: `%LOCALAPPDATA%\Programs\AGEX`, `%LOCALAPPDATA%\AGEX`, `~/Library/Application Support/AGEX`, `~/.local/share/agex`. No code creates AI-COGY folders.

## Skills catalog

| Item | Result |
| --- | --- |
| CAVEMAN | **ADDED** (`caveman`, `caveman-commit`, `caveman-review`) |
| CAVEMAN SOURCE | https://github.com/JuliusBrussee/caveman (107,660 stars; the other same-named repositories are forks). Only the MIT `skills/` folder is used; the BSL-1.1 engine and proxy are not. |
| OMNIROUTE | **NOT APPROPRIATE as a skill** |
| OMNIROUTE SOURCE | https://github.com/diegosouzapw/OmniRoute (MIT, 69,814 stars). It is an OpenAI-compatible model gateway that would proxy all agent traffic through many third-party providers; that belongs in an optional per-agent custom endpoint setting (next), not in Skills. |
| TOTAL CURATED SKILLS | 53 catalog entries: 35 Official, 16 AGEX Curated, 2 Community |
| TOTAL COMMUNITY SKILLS | 2 (Codebase Onboarding Map, CodeQL Setup from GitHub Awesome Copilot) |
| TOTAL ACCOUNT-REQUIRED INTEGRATIONS | 14 (8 key/token, 6 tool sign-in) |
| LOGIN-READY INTEGRATIONS | Key or token with Add key / Disconnect: GitHub, Notion, Linear, Sentry, Supabase, Brave Search, Tavily, Firecrawl (Test connection for GitHub, Notion, Supabase). Tool sign-in: GitHub CLI (Fix GitHub CI, Address PR Comments), Vercel, Netlify, Cloudflare Wrangler, Render. |
| DEPENDENCY-AWARE INSTALL | **PARTIAL**: missing programs are detected (Node.js, uv, Python, gh, Git, Vercel/Render CLI, .NET, Semgrep, Chrome) and shown as "Dependency missing" with the official download page; AGEX does not install system-wide programs itself. |
| ONE-CLICK SKILL INSTALL | **READY**: install per skill and per pack, with permissions and optional key in one dialog. |

Acceptance covered by tests: catalog validity (every entry pinned, hashed, https, restricted sign-in commands), one broken entry skipped without hiding the rest, account required → key saved → ready → disconnected, platform / agent / dependency problems in the right order, sign-in command metadata cannot become an arbitrary command, missing-key MCP tools not given to agents. Existing tests keep covering corrupt packages, checksum mismatch, zip slip and tampered installs. The Skills page catches errors per card.

Not tested with real accounts: successful Connect/Test for the key-based services (no keys were available), the CLI sign-ins, and offline behaviour of the Skills page beyond the connection test's "could not be reached" path.

## Remaining (real limitations)

- Claude Code and Gemini CLI have no model-listing command; their pickers show Auto and a custom ID only.
- Sign-in detection for Claude Code and Gemini CLI relies on local markers, because neither has a status command; a revoked token can still look signed in until the first request.
- `codex debug models` is a debugging command and may change; AGEX then shows "could not read" and keeps the last list.
- One-click agent install needs Node.js/npm; on macOS/Linux with a system-wide Node.js, npm may need administrator rights, and AGEX then shows the official command instead.
- Hosted MCP servers that require OAuth (Cloudflare account, Atlassian, Slack) are not in the catalog yet.
- The signed remote catalog is designed but off until a release signing key exists.
- None of this was run on macOS or Linux.
