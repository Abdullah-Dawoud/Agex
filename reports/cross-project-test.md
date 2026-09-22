# Cross-Project Test

Date: 2026-09-19

## Scope

Tested global Serena installation and project-local context using:

- setup repository;
- disposable Python fixture repository under the visualization workspace.

No user project source was edited.

## Results

- Global Serena runtime is available through uv `serena-agent 1.7.0`.
- Codex global MCP entry starts Serena with current-working-directory project selection.
- Setup generated its own `.serena/project.yml`; fixture generated a separate `.serena/project.yml` and separate cache.
- Fixture used Python language-server backend and indexed 7 symbols successfully.
- Setup used PowerShell backend but failed dependency provisioning because PSScriptAnalyzer write access was denied under `C:\Users\isc\.serena`.
- No project-specific source or memory was copied between setup and fixture.

## Isolation result

PASS for global availability and project separation. LIMITATION: setup PowerShell backend is not working. Cross-project test must be repeated after resolving the PowerShell dependency path. No ACL weakening is approved.
