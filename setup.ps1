# Environment tools for this workstation setup (Codex mode profiles, doctor,
# project initialisation, skill sync). These are separate from AGEX itself;
# the AGEX product is the "agex" command and the desktop app (see README.md).
[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet("menu", "doctor", "init-project", "status", "updates", "codex", "caveman", "coworker", "orchestrator", "mode-profiles", "skills", "skills-sync")][string]$Command = "menu",
    [switch]$DryRun,
    [switch]$CreateDocs,
    [switch]$Json,
    [Parameter(ValueFromRemainingArguments)][string[]]$Arguments
)

$ErrorActionPreference = "Stop"
$scripts = Join-Path $PSScriptRoot "scripts"
$codexResolver = Join-Path $scripts "resolve-codex.ps1"
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE ".codex" }

function Invoke-Codex {
    param([string[]]$CodexArguments = @())
    $codexPath = (& $codexResolver | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($codexPath)) { throw "Codex CLI resolver returned no executable path." }
    $env:CODEX_HOME = $codexHome
    & $codexPath @CodexArguments
    exit $LASTEXITCODE
}

switch ($Command) {
    "menu" {
        # Opens AGEX when it is installed; otherwise explains how to get it.
        $agex = Get-Command agex -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($agex) { & $agex.Source @Arguments; exit $LASTEXITCODE }
        Write-Output "AGEX is not installed. Install it (see docs/INSTALL.md) or build it from source (see docs/DEVELOPMENT.md)."
        exit 1
    }
    "doctor" { if ($Json) { & (Join-Path $scripts "doctor.ps1") -Json } else { & (Join-Path $scripts "doctor.ps1") } }
    "init-project" { & (Join-Path $scripts "init-project.ps1") @Arguments -DryRun:$DryRun -CreateDocs:$CreateDocs }
    "status" { & (Join-Path $scripts "doctor.ps1") -Json | ConvertFrom-Json | Group-Object Status | Select-Object Name, Count | Format-Table -AutoSize }
    "updates" { if ($Json) { & (Join-Path $scripts "update-check.ps1") -Json } else { & (Join-Path $scripts "update-check.ps1") } }
    "codex" { Invoke-Codex -CodexArguments $Arguments }
    "caveman" { Invoke-Codex -CodexArguments (@("--profile", "caveman", "--config", "mcp_servers.context7.enabled=false", "--config", "mcp_servers.playwright.enabled=false") + $Arguments) }
    "coworker" { Invoke-Codex -CodexArguments (@("--profile", "coworker") + $Arguments) }
    "orchestrator" { Invoke-Codex -CodexArguments (@("--profile", "orchestrator") + $Arguments) }
    "mode-profiles" { & (Join-Path $scripts "mode-profiles.ps1") -Mode Install }
    "skills" { & (Join-Path $scripts "skills-sync.ps1") -Command list }
    "skills-sync" { & (Join-Path $scripts "skills-sync.ps1") -Command sync }
}
