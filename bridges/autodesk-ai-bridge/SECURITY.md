# Security policy

## Scope

Autodesk AI Bridge is a local Windows application. It exposes only named Revit and AutoCAD operations through MCP. It does not expose generic shell, PowerShell, C#, reflection, or arbitrary process execution tools.

The Host and Autodesk plug-ins authenticate over a per-user named pipe using a generated secret stored under the user's local application data. Secrets must not appear in logs, diagnostics copied to the clipboard, release notes, or bug reports.

## Report a vulnerability

Do not open a public issue for an undisclosed security problem. Contact maintainers privately through the GitHub security advisory mechanism when enabled, or the private maintainer contact listed on the repository profile.

Include a minimal reproduction, affected version, impact, and mitigation if known. Do not include project files containing confidential model data.

## Release security checks

Before release, verify pipe access control, per-user file permissions, AntiGravity configuration preservation, path validation, request-size limits, save-as behavior, and absence of generic OS execution tools.
