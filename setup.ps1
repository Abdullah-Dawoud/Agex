# Developer and environment tools (doctor, Codex mode profiles, acceptance,
# worker dispatch). The AGEX product entry point is agex.ps1.
[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet("menu", "launch", "doctor", "dashboard", "init-project", "status", "updates", "codex", "caveman", "coworker", "orchestrator", "mode-profiles", "skills", "skills-sync", "orchestrator-dispatch", "final-test", "acceptance", "executor-test")][string]$Command = "menu",
    [ValidateSet("status", "dispatch", "cleanup")][string]$WorkerCommand = "status",
    [string]$Task,
    [string]$WorkingDirectory,
    [string[]]$Modes,
    [string]$Project,
    [ValidateSet("Codex", "Antigravity", "Auto")][string]$Leader,
    [ValidateRange(0, 100)][int]$CodexShare = -1,
    [ValidateRange(0, 100)][int]$AntigravityShare = -1,
    [string]$CodexModel,
    [string]$CodexEffort,
    [string]$AntigravityModel,
    [string]$AntigravityEffort,
    [ValidateSet("CODEX", "CODEX_SUBAGENT")][string]$Executor = "CODEX",
    [string]$SessionId,
    [string]$WorkId,
    [ValidateRange(1, 1800)][int]$WaitTimeoutSeconds = 900,
    [switch]$Wait,
    [switch]$NoConfirm,
    [switch]$Open,
    [switch]$DryRun,
    [switch]$CreateDocs,
    [switch]$Json,
    [Alias("agy-only")][switch]$AgyOnly,
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
    "menu" { & (Join-Path $PSScriptRoot "agex.ps1") @Arguments; exit $LASTEXITCODE }
    "launch" { & (Join-Path $scripts "workbench.ps1") -Modes $Modes -Project $Project -Leader $Leader -CodexShare $CodexShare -AntigravityShare $AntigravityShare -CodexModel $CodexModel -CodexEffort $CodexEffort -AntigravityModel $AntigravityModel -AntigravityEffort $AntigravityEffort -NoConfirm:$NoConfirm -CodexArguments $Arguments }
    "doctor" { if ($Json) { & (Join-Path $scripts "doctor.ps1") -Json } else { & (Join-Path $scripts "doctor.ps1") } }
    "dashboard" { if ($Open) { & (Join-Path $scripts "dashboard.ps1") -Open } else { & (Join-Path $scripts "dashboard.ps1") } }
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
    "orchestrator-dispatch" {
        $orchestratorParams = @{}
        if ($Leader) { $orchestratorParams.Leader = $Leader }
        if ($CodexShare -ge 0) { $orchestratorParams.CodexShare = $CodexShare }
        if ($AntigravityShare -ge 0) { $orchestratorParams.AntigravityShare = $AntigravityShare }
        if ($AntigravityModel) { $orchestratorParams.AntigravityModel = $AntigravityModel }
        if ($AntigravityEffort) { $orchestratorParams.AntigravityEffort = $AntigravityEffort }
        if ($Executor) { $orchestratorParams.Executor = $Executor }
        if ($SessionId) { $orchestratorParams.SessionId = $SessionId }
        if ($WorkId) { $orchestratorParams.WorkId = $WorkId }
        if ($Wait) { $orchestratorParams.Wait = $true }
        $orchestratorParams.WaitTimeoutSeconds = $WaitTimeoutSeconds
        if ($Task) { $orchestratorParams.Task = $Task }
        if ($WorkingDirectory) { $orchestratorParams.WorkingDirectory = $WorkingDirectory }
        & (Join-Path $scripts "orchestrator.ps1") -Command $WorkerCommand @orchestratorParams
    }
    "final-test" {
        $finalTestParams = @{}
        if (-not [string]::IsNullOrWhiteSpace($Project)) { $finalTestParams.Project = $Project }
        if ($AgyOnly -or @($Arguments) -contains "--agy-only" -or @($Arguments) -contains "-AgyOnly") { $finalTestParams.AgyOnly = $true }
        & (Join-Path $scripts "final-test.ps1") @finalTestParams
        exit $LASTEXITCODE
    }
    "acceptance" {
        & (Join-Path $scripts "agex-real-acceptance.ps1") -DeadlineSeconds 1200
        exit $LASTEXITCODE
    }
    "executor-test" {
        if (@($Arguments).Count -ne 1 -or $Arguments[0] -ne 'agy') { throw 'Usage: agex executor-test agy' }
        . (Join-Path $scripts 'agex-common.ps1')
        $runtime = Get-AgexRuntimeIdentity
        if ($runtime.Restricted) {
            Write-Output 'NORMAL_USER_REQUIRED'
            Write-Output "Current identity: $($runtime.Name)"
            exit 42
        }
        $fixture = Join-Path $PSScriptRoot ('.tmp/agex-executor-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $fixture -Force | Out-Null
        & git -C $fixture init --quiet
        & (Join-Path $scripts 'agy-direct-diagnostic.ps1') -Project $fixture -WriteProbe
        Write-Output "FIXTURE RETAINED: $fixture"
        exit $LASTEXITCODE
    }
}
