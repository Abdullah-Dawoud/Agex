# Installing AGEX

AGEX installs for your user only. It needs no administrator rights, no .NET, Python, Node or other developer tools: everything it needs is inside the package.

## Requirements

| System | Supported |
| --- | --- |
| Windows | Windows 10 (1809 or newer) or Windows 11, x64 or ARM64 |
| macOS | macOS 14 Sonoma or newer, Apple Silicon or Intel |
| Linux | x64 or ARM64 with a desktop (X11 or Wayland); Ubuntu 22.04+, Debian 12+, Fedora 42+ are .NET 10's supported list |

AGEX coordinates agents you install separately (Codex CLI, Antigravity CLI, Claude Code, Gemini CLI or Ollama). It works without any of them, but it cannot do work until at least one is installed and signed in.

## One-command install

> These commands download from the project's GitHub releases. No release has been published yet; until one is, use a package you built yourself (see "Manual install" below and [DEVELOPMENT.md](DEVELOPMENT.md#release-packages)).

Windows (PowerShell):

```powershell
irm https://github.com/Abdullah-Dawoud/Ai-COGY/releases/latest/download/agex-install.ps1 -OutFile "$env:TEMP\agex-install.ps1"; powershell -ExecutionPolicy Bypass -File "$env:TEMP\agex-install.ps1"
```

macOS and Linux (Terminal):

```sh
curl -fsSL https://github.com/Abdullah-Dawoud/Ai-COGY/releases/latest/download/agex-install.sh | sh
```

The installer:

1. Detects your system and processor and picks the matching package.
2. Downloads it and `SHA256SUMS.txt` from the GitHub release.
3. Checks the package's SHA-256. If it does not match, nothing is extracted or installed.
4. Installs AGEX:
   - Windows: `%LOCALAPPDATA%\Programs\AGEX`, adds `agex` to your user PATH and an **AGEX** Start Menu shortcut.
   - macOS: `~/Applications/AGEX.app`, links `agex` into `~/.local/bin`.
   - Linux: `~/.local/share/agex/app`, links `agex` into `~/.local/bin` and adds AGEX to your applications menu.
5. Starts AGEX.

If `~/.local/bin` is not on your PATH (macOS/Linux), the installer prints the one line to add.

## Manual install

Download from [Releases](https://github.com/Abdullah-Dawoud/Ai-COGY/releases):

- the package for your system (`agex-<version>-win-x64.zip`, `...-osx-arm64.zip` or `.dmg`, `...-linux-x64.tar.gz`, …),
- `SHA256SUMS.txt`,
- `agex-install.ps1` or `agex-install.sh`.

Then install that file offline:

```powershell
powershell -ExecutionPolicy Bypass -File .\agex-install.ps1 -Package .\agex-2.0.0-win-x64.zip
```

```sh
sh agex-install.sh --package ./agex-2.0.0-osx-arm64.zip
```

On macOS you can also open the `.dmg` and drag **AGEX** to Applications.

## macOS: first launch of an unsigned build

Official releases are signed and notarized only when the project has an Apple Developer ID configured (see [DEVELOPMENT.md](DEVELOPMENT.md#signing)). If a release is not notarized, macOS says it "cannot check AGEX for malicious software". To open it once:

1. In Finder, open **Applications** (or `~/Applications`).
2. Right-click (or Control-click) **AGEX** and choose **Open**, then **Open** again.

macOS remembers the choice. Only do this for a package whose checksum the installer verified.

## Windows: SmartScreen

Unsigned Windows builds can show "Windows protected your PC" the first time. Choose **More info** → **Run anyway**. Signed releases do not show this once the certificate has built reputation.

## Updating

- In the app: **Settings → Updates → Check now**, then **Download and install**. AGEX checks at most once a day when the automatic check is on.
- In a terminal: `agex update` (or `agex update --check`).

Updates come from the GitHub release. The package must match `SHA256SUMS.txt`, and when the release is signed, the checksum file's signature must match the key built into AGEX. AGEX then closes, replaces only its own files and starts again. Settings, sessions and skills are kept.

## Uninstalling

```sh
agex uninstall            # keeps your settings, sessions and skills
agex uninstall --purge    # also removes them
```

This removes AGEX, the `agex` command, the shortcut or menu entry and the start-at-login entry. It never touches your agents or projects.

## Where AGEX keeps data

| | Windows | macOS | Linux |
| --- | --- | --- | --- |
| Settings, projects, sessions, skills | `%LOCALAPPDATA%\AGEX` | `~/Library/Application Support/AGEX` | `~/.local/share/agex` |
| Logs | `%LOCALAPPDATA%\AGEX\logs` | `~/Library/Logs/AGEX` | `~/.local/state/agex/logs` |
| Cache | `%LOCALAPPDATA%\AGEX\cache` | `~/Library/Caches/AGEX` | `~/.cache/agex` |

Set `AGEX_HOME` to keep everything in one other folder (portable use, testing).

## Upgrading from AGEX 1.x (Windows)

AGEX 2 replaces the Windows-only 1.x app. The installer replaces the old files in the same folder and moves the PATH entry. On first start AGEX 2 migrates your 1.x settings (leader, models, efforts, workload shares, recent projects, project list) to the new format, after saving a copy under `backups\<date>-before-migration`. Codex stays read-only if it was read-only in 1.x (Agents → Codex → Options → Can change files).
