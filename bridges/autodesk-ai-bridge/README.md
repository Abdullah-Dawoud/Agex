# Autodesk AI Bridge

Use Revit and AutoCAD directly from AntiGravity.

Autodesk AI Bridge runs locally. It adds Autodesk tools to AntiGravity's normal chat experience. It does not create a separate AI chat application.

## Installation

AGEX installs the bridge for you:

1. In AGEX, open **Connections** and press **Install & Connect** on Revit or AutoCAD.
2. Read what will be installed, then press **Install**.
3. Open Revit or AutoCAD and press **Test again** to see it connected.

AGEX downloads the prebuilt package (`autodesk-ai-bridge-win-x64.zip`) from the AGEX release, checks it against the release's `SHA256SUMS.txt`, and installs it for the current user only, with no administrator rights and nothing to compile:

- the host in `%LOCALAPPDATA%\AutodeskAIBridge\Host`;
- the Revit add-in for each Revit 2025, 2026 or 2027 found, registered in `%APPDATA%\Autodesk\Revit\Addins\<year>`;
- the AutoCAD plug-in (AutoCAD 2025 and later) in `%APPDATA%\Autodesk\ApplicationPlugins\AutodeskAIBridge.bundle`.

A failed installation is rolled back. **Uninstall** in the same AGEX window removes all of it. Revit 2024 and AutoCAD 2024 or earlier are not in the prebuilt package.

To build the package yourself (developers only): `./package.ps1 -Output <folder>`. The add-ins compile against Autodesk's reference packages; no Autodesk assembly is shipped.

## What you can ask

### Revit

- “Tell me what levels are in this model.”
- “Create a level named Level 03 at 7200 mm.”
- “Find all walls on Level 02.”
- “Create these internal walls using Generic - 200mm.”
- “Create a sheet and place the floor plan on it.”

### AutoCAD

- “List the layers in this drawing.”
- “Create an ELECTRICAL layer.”
- “Draw a polyline through these points.”
- “Move these entities 2 meters to the right.”
- “Save a copy as SitePlan-AI.dwg.”

For model edits, review AntiGravity's proposed action before confirming it. Use copies of important projects while testing.

## If no Autodesk product is installed

The installer still installs the Host and Manager. It reports:

> No supported Revit or AutoCAD installation was detected. Autodesk integrations can be repaired later.

Install Revit or AutoCAD later, then open Bridge Manager and select **Repair Integration**.

## Bridge Manager

Bridge Manager is a small status and repair utility. AntiGravity remains the primary user interface.

Bridge Manager provides integration status, **Run Diagnostics**, **Repair Integration**, **Open Logs**, and **Copy Diagnostics**. It does not require developer knowledge.

## Support and privacy

- [Installation](docs/INSTALLATION.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Privacy](docs/PRIVACY.md)
- [Security](SECURITY.md)
- [Release acceptance tests](docs/RELEASE_ACCEPTANCE_TESTS.md)

The Bridge communicates through local authenticated named pipes. The Host starts automatically when AntiGravity starts the configured MCP entry. No port, IP address, pipe name, token, DLL path, `NETLOAD`, PowerShell command, or developer runtime is required for normal use.

## Project status

The source MVP and productization architecture are prepared. A public release is not production-ready until a compiled installer passes the real Revit, AutoCAD, AntiGravity, fresh-install, upgrade, repair, and uninstall tests. See [release readiness](reports/release-readiness.md).

## For developers and release builders

Developer requirements are intentionally separate from end-user installation. See [Development](docs/DEVELOPMENT.md), [Architecture](docs/ARCHITECTURE.md), and [installer source](installer/README.md).
