# GitHub Workflow

## Decision

Use plain Git for local status, branches, diffs, commits, and history. Codex can perform repository work through the normal workspace. Official GitHub CLI 2.101.0 is installed at user scope. GitHub connector is authenticated; read-only repository probe passed.

Official GitHub CLI supports Windows and can be installed with `winget install --id GitHub.cli --source winget`; authentication remains a user action. Do not install overlapping GitHub integrations automatically. Source: [GitHub CLI Windows installation](https://github.com/cli/cli/blob/trunk/docs/install_windows.md).

## Safe workflow

```powershell
git status --short
git diff --check
git diff
git log --oneline -n 10
```

For remote issues, pull requests, or reviews, use authenticated GitHub connector tools or restart the shell and run `gh auth login` for CLI workflows. Keep write actions explicit. Never copy tokens into setup files.
