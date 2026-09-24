# Troubleshooting

Start with **Settings → Diagnostics** (or `agex doctor`). It lists AGEX's version, system, every agent with its status and reason, skills, storage and update mode. **Copy diagnostics** gives you text with secrets and your user name removed, safe to share.

**Repair AGEX** (or `agex repair`) fixes what AGEX owns: missing folders, damaged settings (kept aside, defaults used), the session index, broken skills, leftover update folders, caches and agent cooldowns. It never installs, changes or removes agents.

| Problem | What to do |
| --- | --- |
| An agent shows **Not installed** but you installed it | Press **Scan again** in Agents. AGEX looks at your PATH (and your login shell's PATH on macOS/Linux) and known install folders. If the agent lives elsewhere, set `AGEX_CODEX_PATH`, `AGEX_ANTIGRAVITY_PATH`, `AGEX_CLAUDE_CODE_PATH`, `AGEX_GEMINI_CLI_PATH` or `AGEX_OLLAMA_PATH` to its full path. |
| **Sign-in required** | Open that agent once in its own terminal or app and sign in, then press **Check again**. |
| **Not working** | The reason is shown (for example the version check failed). Run the agent's `--version` yourself to see its error. |
| **Paused after errors** | AGEX stopped using the agent for 5 minutes after it failed to start or failed twice. Press **Try again** after fixing the cause. |
| Ollama shows **Not working** | Start the Ollama app and download a model (`ollama pull qwen2.5-coder:7b`). AGEX never starts Ollama for you. |
| The request stops with **Needs your answer** | An agent asked a question. Answer on Home, or press **Skip** to stop. |
| Tasks end as **Skipped** | You chose **Don't allow** for file changes. Retry and allow, or trust the project. |
| **Needs repair** / **Partly complete** | AGEX found that a file an agent claimed to change does not match the folder. The leader usually repairs it; the reason is in the timeline. |
| Non-English text is garbled in an agent's answer (Windows) | Windows PowerShell 5.1 reads UTF-8 files without a BOM in the old code page. AGEX tells agents to use `-Encoding UTF8`; if an agent ignores it, ask again or add the instruction to the project's instructions. |
| A skill was **disabled because it failed during startup** | Its files are missing or were changed after install. Remove and install it again, or re-enable it after restoring the files. |
| AGEX misbehaves after installing a skill | Start in **safe mode** (`agex --safe-mode`), remove the skill, restart normally. |
| "Could not reach the skill source" / update check fails | You are offline or GitHub is unreachable. AGEX keeps working; try later. |
| Checksum mismatch during install or update | The download was damaged or altered. Nothing was installed. Download again; if it repeats, report it. |
| macOS: "cannot check AGEX for malicious software" | See [INSTALL.md](INSTALL.md#macos-first-launch-of-an-unsigned-build). |
| Windows: SmartScreen warning | See [INSTALL.md](INSTALL.md#windows-smartscreen). |
| `agex` is not found in a new terminal | Windows: open a new terminal after installing. macOS/Linux: add `~/.local/bin` to your PATH. |
| A request was **Interrupted** | AGEX closed while it ran. Nothing was restarted. Check the changed files in Sessions, then **Retry** or **Continue**. |
| Settings look reset | AGEX found a damaged settings file, kept it as `settings.json.damaged-<time>` and used defaults. Backups are under `backups` in the data folder. |

Logs: `%LOCALAPPDATA%\AGEX\logs` (Windows), `~/Library/Logs/AGEX` (macOS), `~/.local/state/agex/logs` (Linux). One JSON line per event, secrets redacted.
