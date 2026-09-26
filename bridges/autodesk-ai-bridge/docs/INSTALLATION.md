# Installation

## Normal users

Download `AutodeskAIBridge-Setup.exe` from GitHub Releases and run it. Do not download repository source or run `build.ps1`.

Installer runs per user under `%LOCALAPPDATA%\AutodeskAIBridge`. It detects supported Revit 2024-2026 and AutoCAD 2025-2027 installations, installs matching plug-ins, creates Revit manifests, installs AutoCAD bundle, creates local authenticated IPC settings, and merges one `autodesk-ai-bridge` entry into AntiGravity configuration.

AntiGravity merge process:

1. Find supported AntiGravity settings locations under `%APPDATA%`.
2. Read existing JSON.
3. Create timestamped backup beside file.
4. Preserve all unrelated MCP servers and settings.
5. Add or update only `autodesk-ai-bridge`.
6. Write through temporary file.
7. Parse and verify result.
8. Restore backup if verification fails.

Installer never asks users to enter a port, pipe name, token, path, or DLL location.

## After installation

1. Restart Revit or AutoCAD if it was already open.
2. Open project or drawing.
3. Open AntiGravity.
4. Ask: `Check Autodesk AI Bridge health.`

If a product was not installed during setup, install it later and select **Repair Integration** in Bridge Manager.

## Screenshots

Screenshots belong in `docs/images/`:

- `docs/images/installer.png`
- `docs/images/bridge-manager.png`
- `docs/images/antigravity-revit-example.png`

These are placeholders until captured from a real release build. Do not publish fabricated screenshots.

## Developer installation

Developer and release-builder requirements are in [Development](DEVELOPMENT.md). They are not end-user requirements.
