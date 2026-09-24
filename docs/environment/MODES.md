# Codex Modes

Four independent experiences use native Codex profile overlays.

| Mode | Launch | Scope | Disabled by default |
|---|---|---|---|
| Plain Codex | `.\setup.ps1 codex` | Base Codex config; ordinary coding | No mode-added tools or delegation |
| Caveman | `.\setup.ps1 caveman` | `caveman` profile; normal coding plus user skill | Context7 and Playwright MCP |
| Coworker | `.\setup.ps1 coworker` | General worker routing | Unrelated tools remain task-scoped |
| Orchestrator | `.\setup.ps1 orchestrator` | Codex boss profile | Unrelated tools remain task-scoped |

Install or refresh the profiles with `.\setup.ps1 mode-profiles`.

## Multi-agent work

Coordinating several agents (Codex, Antigravity, Claude Code, Gemini CLI, Ollama) is done by AGEX, not by these profiles. AGEX 2 replaced the earlier PowerShell workbench, orchestrator dispatch and worker scripts; see the main [README](../../README.md) and [USAGE](../USAGE.md). AGEX keeps its own settings and project list in its data folder, never in the Codex home folder.

## Native boundary

Codex supports named profile files at `$CODEX_HOME/<name>.config.toml` and selects them with `codex --profile <name>`. Base `config.toml` remains unchanged. Profiles override only mode instructions and Context7/Playwright MCP enablement.

Codex reads `AGENTS.md` by scope and loads standard `SKILL.md` directories. Antigravity uses the same open skill format and discovers workspace skills under `.agents/skills`; its CLI global skill path is `~/.gemini/antigravity-cli/skills`.

Antigravity CLI authentication is user-owned. Run the official `agy` login once in an interactive session when required. Never copy browser tokens or credentials into this repository.

## Rollback

Remove `caveman.config.toml`, `coworker.config.toml`, and `orchestrator.config.toml` from `$CODEX_HOME`, or restore them from the profile backup reported by `scripts/mode-profiles.ps1`. Base Codex config backup remains under `%LOCALAPPDATA%\AI-Developer-Setup\backups`.
