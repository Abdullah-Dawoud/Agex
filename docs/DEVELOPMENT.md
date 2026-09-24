# Development

## Layout

| Path | What |
| --- | --- |
| `agex.ps1`, `bin/agex.cmd` | CLI entry point |
| `scripts/agex-*.ps1` | engine (see [ARCHITECTURE.md](ARCHITECTURE.md)) |
| `scripts/orchestrator.ps1`, `scripts/worker-run.ps1` | supervised Antigravity worker |
| `scripts/workbench.ps1` | terminal launcher menus |
| `desktop/*.cs` | WPF desktop app |
| `install/agex-install.ps1` | installer |
| `tools/build-desktop.ps1`, `tools/build-release.ps1` | builds |
| `scripts/*.tests.ps1` | tests |
| `setup.ps1`, other `scripts/*.ps1`, `docs/environment` | original developer-environment tools |

## Build and test

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-desktop.ps1
powershell -ExecutionPolicy Bypass -File scripts\agex-engine.tests.ps1      # engine, protocol, CLI, installer (fake agents)
powershell -ExecutionPolicy Bypass -File scripts\agex-ui.tests.ps1          # state store and renderer
powershell -ExecutionPolicy Bypass -File scripts\agex-graph.tests.ps1       # planner and mailbox
powershell -ExecutionPolicy Bypass -File scripts\agex-acceptance-fixture.tests.ps1
```

Tests generate fake Codex and Antigravity executables and use an isolated `AGEX_HOME`; they never call real agents or touch your data. `agex acceptance` runs a bounded real-agent acceptance in a disposable Git folder.

Code rules: Windows PowerShell 5.1 syntax; C# 5 for the desktop (the in-box compiler); no new runtime dependencies; every external process through `Invoke-AgexProcess`; nothing writes to the console from background runspaces; `.ps1` files with non-ASCII characters need a UTF-8 BOM (prefer `[char]0x2713` escapes).

## Adding an agent adapter

1. **Register it** in `Get-AgexAdapters` (`scripts/agex-adapters.ps1`):

   ```powershell
   [pscustomobject]@{
       Id = "myagent"; Name = "MyAgent"; DisplayName = "MyAgent"; Provider = "Vendor"; Kind = "Agent"; Support = "SUPPORTED"
       Description = "What it is good at"
       Capabilities = @("READ_FILES", "WRITE_FILES", "PLANNING")
       WritePolicy = "always"            # or "sandbox" if edits depend on a user setting
       SupportsStreaming = $false; SupportsTools = $true; SupportsMcp = $false; SupportsSubagents = $false
       Resolver = "Resolve-MyAgentExecutable"; Executor = "Invoke-MyAgentTask"; CloudService = $true
   }
   ```

2. **Resolver**: return the executable path from fixed, known locations (never search the whole disk). Remove the tool from `Get-AgexDiscoveryRules` if it was listed as detected-only.
3. **Executor** `Invoke-MyAgentTask -Prompt -WorkId -Route`: build fixed arguments, call `Invoke-AgexProcess` (pass `-Runtime $script:runtime -CancellationSignal $script:cancellationSignal -RequestId $WorkId -Label -Executor`), then:
   - on success set `$script:lastExecutorResult` to the agent's final text, call `Register-AgexAgentResult -Success $true` and `Set-AgexExecutorOutcome -Success $true`, and return `$true`;
   - on failure call `Register-AgexAgentResult -Success $false` (with `-Immediate` for start or sign-in failures), and call `Set-AgexExecutorOutcome` with a plain-language reason and `FallbackEligible = $true` only when the agent did no meaningful work. Then return `$false`.
   Publish progress with `Start-AgexUiAgent` / `Update-AgexUiAgent` / `Complete-AgexUiAgent`. Never write to the console.
4. **Names**: the planner and the leader prompt refer to agents by `Name`. Update `Get-AgexAvailabilityText` and the leader prompt's executor list (`"executor":"Codex|Antigravity"`) and `ConvertFrom-AgexPlan` validation so the leader may assign the new agent.
5. **Health**: `Invoke-AgexPrecheck` and discovery call `Test-AgexAgentPrecheck` (`--version`); adjust if the tool has another cheap, offline health command.
6. **Tests**: add a fake executable and cases to `scripts/agex-engine.tests.ps1` (start failure, success, fallback).

The desktop app needs no change: agents, capabilities and states come from the engine.
