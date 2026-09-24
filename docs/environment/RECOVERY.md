# Recovery

## Codex configuration

Back up allowlisted config before edits:

```powershell
.\scripts\backup-config.ps1 -Path "$env:USERPROFILE\.codex\config.toml"
```

Restore only explicit backup after review:

```powershell
.\scripts\restore-config.ps1 -Path "$env:USERPROFILE\.codex\config.toml" -BackupFile <backup-file>
```

Validate with `codex --help` and inspect MCP entries. Do not back up auth, session, queue, log, or memory databases.

Latest known backup: `%USERPROFILE%\AppData\Local\AI-Developer-Setup\backups\20260920-024012`. It contains `config.toml` and `.gitconfig` only. Keep this path outside Git.

Serena removal backup: `%USERPROFILE%\AppData\Local\AI-Developer-Setup\backups\serena-removal-20260920-224545`. Do not restore it during normal recovery.

## Project setup

Initializer never overwrites existing files. Remove only files created by a reviewed initializer run. Git diff provides recovery for tracked project changes.

## Memory

No external memory engine retained. Codex internal memory is host-managed and excluded from setup backups.

## Integrations

Remove additive MCP entries with `codex mcp remove <name>` after recording current config. Uninstall package-managed tools through their official uninstall command. Do not delete broad user-profile directories.

GitHub CLI 2.101.0 is user-scope installed. GitHub connector authentication is separate from CLI login. Keep repository writes explicit.
