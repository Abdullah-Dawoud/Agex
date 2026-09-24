# Security and privacy

## What AGEX runs

AGEX has no generic "run any program" feature. It starts only:

- the agent executables of supported adapters (Codex CLI, Antigravity CLI), found through fixed known locations, with arguments AGEX builds itself;
- its own scripts (engine, supervised Antigravity worker) with Windows PowerShell;
- `git` (read-only status and diff in the project folder) and `taskkill` (to stop processes AGEX started).

Discovery of other tools only checks for files; it never executes them. Every started process is registered; cancel, quit and time-outs stop only those process trees. Working directories are the chosen project folder or AGEX's own folders.

Agents act in the project folder you choose. Codex runs read-only unless you allow edits. Antigravity runs with its own permissions in that folder; review the Changes tab before you rely on its edits.

## What AGEX stores

In `%LOCALAPPDATA%\AGEX` only: settings, project list, session history (your requests, agent messages and results, task list, changed-file names), sanitized and rotated logs, run telemetry, and the last scan. Logs do not contain full prompts. Values that look like tokens, passwords, API keys, cookies or bearer credentials are redacted in logs, messages and command records. AGEX never reads or copies agent credentials.

AGEX has no server, account or telemetry upload. Codex and Antigravity are cloud services: what you ask them goes to OpenAI or Google under their terms.

## Installation and updates

The installer and `agex update` download from GitHub Releases of this repository and verify SHA-256 checksums from the release's `SHA256SUMS.txt` before installing. The install is per user and needs no administrator rights. Uninstall removes AGEX files, its PATH entry, shortcut and autostart entry only.

## Reporting a vulnerability

See [../SECURITY.md](../SECURITY.md).
