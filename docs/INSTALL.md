# Installing AGEX

## Requirements

- Windows 10 or 11 (x64 or ARM64).
- Windows PowerShell 5.1 and .NET Framework 4.8. Both ship with current Windows.
- At least one supported agent, installed and signed in: [Codex CLI](https://github.com/openai/codex) or Antigravity CLI (`agy`). AGEX never installs agents for you.

No administrator rights, Git, Python, Node, SDK or Visual Studio are needed.

## One command

```powershell
irm https://github.com/Abdullah-Dawoud/Ai-COGY/releases/latest/download/agex-install.ps1 -OutFile "$env:TEMP\agex-install.ps1"; powershell -ExecutionPolicy Bypass -File "$env:TEMP\agex-install.ps1"
```

The command downloads the installer to a file first (it does not pipe remote code straight into PowerShell). The installer then:

1. Checks Windows, PowerShell and .NET Framework 4.8.
2. Downloads `agex-<version>-win.zip` and `SHA256SUMS.txt` from the GitHub release.
3. Verifies the SHA-256 checksum of the package and stops if it does not match.
4. Installs to `%LOCALAPPDATA%\Programs\AGEX` (per user).
5. Adds the `agex` command to your user PATH and creates a Start Menu shortcut.
6. Starts AGEX.

Installer options: `-Version 1.2.0`, `-InstallDir <folder>`, `-NoLaunch`, `-NoShortcut`, `-NoPath`, and `-Package <zip>` for an offline install from a downloaded package (with `SHA256SUMS.txt` next to it).

## First start

AGEX scans your computer once and shows what it found: supported agents, other agent tools it can see but does not drive yet, IDEs, Git and MCP configuration. Choose the agents to use, the leader, a strategy, and a project folder. You can change all of this later.

## Update, repair, uninstall

```powershell
agex update      # installs the newest release after verifying its checksum
agex repair      # re-registers the agex command and shortcut, fixes unreadable settings, re-detects agents
agex uninstall   # removes AGEX; keeps your data (add --purge-data to delete it)
```

The desktop app offers the same under Settings (check for updates) and Diagnostics (repair). Uninstall never touches Codex, Antigravity, IDEs or other tools.

## Where AGEX keeps data

`%LOCALAPPDATA%\AGEX`: `settings.json`, `projects.json`, `sessions\` (history), `logs\` (sanitized, rotated), `telemetry\`, `workers\`, `scan.json`. Set `AGEX_HOME` to use another folder (a custom `AGEX_HOME` is fully isolated).

## Migration from the old name

Earlier builds of this tool stored settings as `%USERPROFILE%\.codex\dawoud-settings.json` and projects as `%USERPROFILE%\.codex\workbench-projects.json`. On first start AGEX copies them once into `%LOCALAPPDATA%\AGEX` and never writes the old files again. You can delete the old files afterwards. If your PowerShell profile defines a `dawoud` function from those builds, you can remove it; use `agex` instead.

## Building from source

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-desktop.ps1   # builds AGEX.exe with the C# compiler that ships with Windows
powershell -ExecutionPolicy Bypass -File tools\build-release.ps1   # dist\agex-<version>-win.zip, agex-install.ps1, SHA256SUMS.txt
```

Pushing a tag `v<version>` (matching `VERSION`) runs `.github/workflows/release.yml`, which tests, builds and publishes the release.
