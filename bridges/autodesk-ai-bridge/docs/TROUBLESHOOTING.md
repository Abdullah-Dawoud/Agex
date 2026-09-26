# Troubleshooting

## Bridge Manager says AntiGravity is not detected

Close AntiGravity, open Bridge Manager, and select **Repair Integration**. Repair scans supported `%APPDATA%\AntiGravity*` locations and preserves unrelated configuration. Reopen AntiGravity after repair.

## Revit integration is not active

Restart Revit. Plug-in loads only in supported Revit versions and after Revit starts. Bridge Manager can recreate the `.addin` manifest through **Repair Integration**.

Do not use `NETLOAD`, browse for a DLL, or copy files manually.

## AutoCAD integration is not active

Restart AutoCAD. Bundle installs in per-user Autodesk `ApplicationPlugins` location and loads on AutoCAD startup. Run **Repair Integration** if bundle was removed.

Do not use `NETLOAD`.

## AntiGravity shows no tools

1. Close and reopen AntiGravity.
2. Open Bridge Manager.
3. Run **Diagnostics**.
4. Select **Repair Integration**.
5. Open Autodesk product and a test project or drawing.
6. Ask AntiGravity: `Check Autodesk AI Bridge health.`

Host starts through MCP stdio. Users do not need a terminal.

## Autodesk application was open during install

Installer does not close or modify running documents. Restart affected Autodesk application after installation.

## No supported Autodesk product found

This is not an installation failure. Host and Manager remain installed. Install supported product later and run **Repair Integration**.

## Logs and diagnostics

Bridge logs are local under `%LOCALAPPDATA%\AutodeskAIBridge\Logs`. Use **Copy Diagnostics** to share status. Diagnostics exclude secrets.

## Safety

Use copies of RVT and DWG files during testing. Confirm destructive changes in AntiGravity. Do not test `save_as` over important files without an explicit backup.
