# Troubleshooting

## Run diagnostics

```powershell
.\scripts\doctor.ps1
```

Doctor is read-only. Capture output only after checking it contains no sensitive values.

## Node reports no active version

Audit NVM first:

```powershell
nvm version
nvm list
nvm default
nvm env
```

Select only an already-installed version after confirming the version manager has permission to update its shim. Do not install another Node distribution. If registry or ACL access is denied, stop and resolve that permission with the user.

## Docker CLI works but daemon fails

Check Docker Desktop state and rerun the doctor. Do not weaken permissions. A missing `docker_engine` pipe means daemon health is not proven; it does not justify ACL changes.

## Serena dashboard or window spam

Serena is removed. Do not restore its MCP entry, user runtime, project directory, or dashboard configuration. Removal backup: `C:\Users\isc\AppData\Local\AI-Developer-Setup\backups\serena-removal-20260920-224545`.

## Context7 deferred

Context7 requires a working Node/npm path for CLI or local MCP setup, or an approved hosted OAuth/API-key path. Do not add credentials to Codex config or enable remote documentation retrieval until the Node blocker, network behavior, query scope, and rollback plan are documented.

## WSL access denied

Run `wsl --version` and `wsl --list --verbose` from a normal user PowerShell. Do not install a distribution or alter security policy during setup foundation work. Windows-native tools remain preferred unless a real dependency needs WSL.

## Backup and restore

Use explicit allowlisted paths only:

```powershell
.\scripts\backup-config.ps1 -Path "$env:USERPROFILE\.codex\config.toml" -WhatIf
.\scripts\backup-config.ps1 -Path "$env:USERPROFILE\.codex\config.toml"
.\scripts\restore-config.ps1 -Path "$env:USERPROFILE\.codex\config.toml" -BackupFile "C:\path\to\backup"
```

Backups stay outside this repository. Restore has high confirmation impact.
