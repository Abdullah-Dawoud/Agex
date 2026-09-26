# Release acceptance tests

Run this checklist on a clean Windows test account with no source checkout required for the tester. Record version, Windows build, AntiGravity version, Revit versions, AutoCAD versions, result, and evidence.

## Fresh install

- [ ] Start with a clean supported Windows account.
- [ ] Install AntiGravity.
- [ ] Install at least one supported Revit or AutoCAD version.
- [ ] Download only `AutodeskAIBridge-Setup.exe` from the release.
- [ ] Double-click installer.
- [ ] Confirm no Visual Studio, .NET SDK, MSBuild, WiX, Inno Setup, Python, Node.js, Git, Autodesk SDK, or PowerShell module is requested.
- [ ] Confirm installer does not close Revit or AutoCAD.
- [ ] Confirm missing-product case still installs Host and Manager.
- [ ] Confirm first-run page reports detected products and AntiGravity status.

PASS requires one installer and normal Windows interaction only.

## AntiGravity

- [ ] Open AntiGravity normally.
- [ ] Ask: `Check Autodesk AI Bridge health.`
- [ ] Confirm tools are available without importing JSON or starting a terminal.
- [ ] Confirm Host starts as an MCP stdio child process.
- [ ] Confirm existing unrelated MCP servers still work.
- [ ] Confirm no secret appears in logs, diagnostics, or configuration output.

## Revit

For each supported installed version:

- [ ] Open sample RVT.
- [ ] Ask: `Which Revit document is open?`
- [ ] Create `AI_TEST_LEVEL` at `4500 mm`.
- [ ] Rename it.
- [ ] Delete it.
- [ ] Create a wall.
- [ ] Query elements.
- [ ] Write a parameter.
- [ ] Run a dry-run edit and confirm model remains unchanged.
- [ ] Undo a confirmed edit.
- [ ] Save a copy.
- [ ] Confirm actual Revit model state and transaction history.

PASS requires real Revit changes, correct rollback, and no manual `.addin` or DLL handling.

## AutoCAD

For each supported installed version:

- [ ] Open test DWG.
- [ ] Ask: `List the layers.`
- [ ] Create layer `AI_TEST`.
- [ ] Create a polyline.
- [ ] Move it.
- [ ] Copy it.
- [ ] Delete the copy.
- [ ] Save a copy.
- [ ] Confirm drawing, layer table, entity handles, and saved file are correct.

PASS requires no `NETLOAD` and no manual bundle copy.

## Install while Autodesk is running

- [ ] Open Revit and AutoCAD with unsaved test documents.
- [ ] Run installer or upgrade.
- [ ] Confirm applications remain open and unsaved work remains intact.
- [ ] Confirm installer clearly says to restart affected application.
- [ ] Restart application and verify plug-in.

## Repair

- [ ] In designated test environment, remove one Bridge-owned plug-in file.
- [ ] Open Bridge Manager.
- [ ] Select **Repair Integration**.
- [ ] Confirm Revit manifest, AutoCAD bundle, Host settings, and AntiGravity entry are restored.
- [ ] Confirm unrelated MCP servers and files remain unchanged.

## Upgrade

- [ ] Install older version.
- [ ] Add unrelated AntiGravity MCP server and local Bridge settings.
- [ ] Install newer version.
- [ ] Confirm application binaries and plug-ins update.
- [ ] Confirm unrelated AntiGravity configuration survives.
- [ ] Confirm compatible local settings and logs survive.
- [ ] Confirm new version appears in Windows installed applications.

## Uninstall

- [ ] Uninstall Bridge.
- [ ] Choose **Keep logs and settings**.
- [ ] Confirm Host, Manager, shortcuts, Revit manifests, AutoCAD bundle, and Bridge MCP entry are removed.
- [ ] Confirm Revit, AutoCAD, AntiGravity, unrelated MCP servers, and Autodesk files remain.
- [ ] Repeat uninstall in a separate environment and choose remove-all-data.
- [ ] Confirm only Bridge local data is removed.

## Security and release artifacts

- [ ] Confirm no generic OS command MCP tool exists.
- [ ] Confirm pipe authentication rejects incorrect secret.
- [ ] Confirm oversized request is rejected.
- [ ] Confirm `SHA256SUMS.txt` matches `AutodeskAIBridge-Setup.exe`.
- [ ] Confirm release includes `release-notes.md`.
- [ ] Confirm package contains no Autodesk-owned API DLL.
- [ ] Confirm published Host and Manager are self-contained `win-x64` binaries.

Release is production-ready only when all required tests pass and evidence is attached to the release record.
