[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet("status", "dispatch", "cleanup")][string]$Command = "status",
    [string]$Task,
    [string]$WorkingDirectory = (Get-Location).Path,
    [int]$MaxWorkers = 2,
    [string]$Leader,
    [int]$CodexShare = -1,
    [int]$AntigravityShare = -1,
    [string]$AntigravityModel,
    [string]$AntigravityEffort,
    [ValidateSet("CODEX", "CODEX_SUBAGENT")][string]$Executor = "CODEX",
    [string]$SessionId,
    [string]$WorkId,
    [int]$HarnessPid = 0,
    [string]$StartupDiagnosticPath,
    [ValidateRange(1, 1800)][int]$WaitTimeoutSeconds = 900,
    [switch]$Wait
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$runRoot = Join-Path $root "reports\workers"
$telemetryRoot = Join-Path $root "reports\telemetry"
$workerScript = Join-Path $PSScriptRoot "worker-run.ps1"
. (Join-Path $PSScriptRoot "dawoud-common.ps1")
$SessionId = if ($SessionId) { $SessionId } elseif ($env:DAWOUD_SESSION_ID) { $env:DAWOUD_SESSION_ID } else { "session-" + ([guid]::NewGuid().ToString("N")) }
$settingsPath = Join-Path $env:USERPROFILE ".codex\dawoud-settings.json"
$preferences = Get-DawoudPreferences -Path $settingsPath
if (-not $Leader) { $Leader = if ($env:DAWOUD_LEADER) { $env:DAWOUD_LEADER } else { $preferences.leader } }
if (@("Codex", "Antigravity", "Auto") -notcontains $Leader) { throw "Leader must be Codex, Antigravity, or Auto." }
if ($CodexShare -lt 0) { $CodexShare = if ($env:DAWOUD_CODEX_SHARE) { [int]$env:DAWOUD_CODEX_SHARE } else { [int]$preferences.codex_share } }
if ($AntigravityShare -lt 0) { $AntigravityShare = if ($env:DAWOUD_ANTIGRAVITY_SHARE) { [int]$env:DAWOUD_ANTIGRAVITY_SHARE } else { [int]$preferences.antigravity_share } }
if (-not $AntigravityModel) { $AntigravityModel = if ($env:DAWOUD_ANTIGRAVITY_MODEL) { $env:DAWOUD_ANTIGRAVITY_MODEL } else { $preferences.antigravity_model } }
if (-not $AntigravityEffort) { $AntigravityEffort = if ($env:DAWOUD_ANTIGRAVITY_EFFORT) { $env:DAWOUD_ANTIGRAVITY_EFFORT } else { $preferences.antigravity_effort } }
if ($CodexShare -lt 0 -or $AntigravityShare -lt 0 -or $CodexShare + $AntigravityShare -ne 100) { throw "Codex and Antigravity workload percentages must total 100." }
if ($MaxWorkers -lt 1 -or $MaxWorkers -gt 2) { throw "Maximum concurrent Antigravity workers is 2." }
$agyPath = Resolve-AgyExecutable

function Write-DawoudRoutingWarning {
    param([Parameter(Mandatory)][string]$Reason)
    Write-Output "DAWOUD ROUTING WARNING"
    Write-Output "Antigravity delegation expected but unavailable."
    Write-Output "Reason: $Reason"
}

function Enter-DawoudWorkerSlotLock {
    $mutex = [Threading.Mutex]::new($false, "Local\DAWOUD-Agy-Worker-Slots")
    if (-not $mutex.WaitOne(5000)) { $mutex.Dispose(); throw "worker-slot lock timeout after 5 seconds" }
    $mutex
}

function Exit-DawoudWorkerSlotLock {
    param($Mutex)
    if ($Mutex) { try { $Mutex.ReleaseMutex() } catch { } finally { $Mutex.Dispose() } }
}

function Read-States {
    if (-not (Test-Path -LiteralPath $runRoot -PathType Container)) { return @() }
    @(Get-ChildItem -LiteralPath $runRoot -Filter status.json -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $state = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
            if ($state.status -eq "RUNNING" -and $state.worker_pid -and -not (Get-Process -Id $state.worker_pid -ErrorAction SilentlyContinue)) {
                $state.status = "ERROR"
                $state.exit_code = 1
                $state.finished_at = (Get-Date).ToUniversalTime().ToString("o")
                $state.error = "Worker process exited before writing a terminal state."
                $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $_.FullName -Encoding utf8
            }
            $state
        } catch { }
    })
}

if ($Command -eq "status") {
    $states = Read-States
    if ($states.Count -eq 0) { Write-Output "No worker runs."; exit 0 }
    Write-Output ("LEADER: {0}`tTARGET: Codex {1}% / AGY {2}%" -f $Leader, $CodexShare, $AntigravityShare)
    foreach ($state in $states | Sort-Object started_at -Descending) {
        $elapsed = if ($state.status -eq "RUNNING") { [math]::Round(((Get-Date).ToUniversalTime() - [datetime]$state.started_at).TotalSeconds, 0) } else { $state.elapsed_seconds }
        Write-Output ("{0}`t{1}`t{2}s`tPID {3}`t{4}" -f $state.worker, $state.status, $elapsed, $state.worker_pid, $state.task)
    }
    $report = Get-DawoudExecutionReport -TelemetryRoot $telemetryRoot -SessionId $SessionId -WorkId $WorkId -CodexShare $CodexShare -AntigravityShare $AntigravityShare
    if ($report) { Write-Output ""; Write-Output $report.Text }
    exit 0
}

if ($Command -eq "cleanup") {
    foreach ($state in (Read-States | Where-Object status -eq "RUNNING")) {
        if ($state.worker_pid -and (Get-Process -Id $state.worker_pid -ErrorAction SilentlyContinue)) {
            Stop-Process -Id $state.worker_pid -Force -ErrorAction SilentlyContinue
            Write-Output "Stopped worker PID $($state.worker_pid)"
        }
    }
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Task)) { throw "dispatch requires -Task" }
if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) { throw "Working directory not found: $WorkingDirectory" }
$resolvedLeader = Resolve-DawoudLeader -ConfiguredLeader $Leader -CodexShare $CodexShare -AntigravityShare $AntigravityShare
$route = Get-DawoudRouteDecision -Task $Task -Leader $resolvedLeader -CodexShare $CodexShare -AntigravityShare $AntigravityShare -TelemetryRoot $telemetryRoot -SessionId $SessionId
$runtimeIdentity = Get-DawoudRuntimeIdentity
Write-Output ("DAWOUD PROCESS IDENTITY: {0}" -f $runtimeIdentity.Name)
Write-Output ("LEADER: {0}; CATEGORY: {1}; ROUTE: {2}; TARGET: Codex {3}% / AGY {4}%" -f $Leader, $route.Category, $route.Agent, $CodexShare, $AntigravityShare)
Write-Output ("ROUTE_REASON: {0}" -f $route.Reason)
$coordination = New-DawoudTelemetryRecord -TelemetryRoot $telemetryRoot -SessionId $SessionId -Task $Task -Executor $Executor -Category $route.Category -CodexShare $CodexShare -AntigravityShare $AntigravityShare -Leader $Leader -ResolvedLeader $resolvedLeader -RouteReason $route.Reason -WorkId $WorkId -SelectedProjectPath $WorkingDirectory -ExecutorWorkingDirectory $WorkingDirectory -AgyAvailable ([bool](Resolve-AgyExecutable)) -AgySelected ($route.Agent -eq "Antigravity") -CodexAvailable $true -CodexSelected ($route.Agent -eq "Codex") -RecordKind COORDINATION
if ($route.Agent -eq "Codex") {
    Write-Output "No Antigravity worker started. Codex retains this task."
    Complete-DawoudTelemetryRecord -Path $coordination.Path -Status DONE -Summary "Codex retained task."
    $report = Get-DawoudExecutionReport -TelemetryRoot $telemetryRoot -SessionId $SessionId -WorkId $WorkId -CodexShare $CodexShare -AntigravityShare $AntigravityShare
    if ($report) { Write-Output ""; Write-Output $report.Text }
    exit 0
}
Set-DawoudTelemetryRecord -Path $coordination.Path -Fields @{ RecordKind = "COORDINATION" }
$agyProbe = Get-DawoudAgyAvailability -RequestedModel $AntigravityModel
if (-not $agyProbe.Available) {
    # Bounded recovery: resolve canonical path again and verify CLI/profile/model once more.
    $agyPath = Resolve-AgyExecutable
    $agyProbe = Get-DawoudAgyAvailability -RequestedModel $AntigravityModel
}
if (-not $agyProbe.Available) {
    $reason = [string]$agyProbe.Reason
    if ([string]::IsNullOrWhiteSpace($reason)) { $reason = "AGY runtime verification returned unavailable without a reason" }
    $versionOk = $false
    if ($agyPath -and (Test-Path -LiteralPath $agyPath -PathType Leaf)) {
        try {
            & $agyPath --version 2>$null | Out-Null
            $versionOk = ($LASTEXITCODE -eq 0)
        } catch { $versionOk = $false }
    }
    if ($versionOk -and $reason -match "models|Fetching available models|model") {
        Write-Output "DAWOUD ROUTING WARNING"
        Write-Output "Antigravity model discovery probe unavailable; canonical AGY executable passed version probe."
        Write-Output "Reason: $reason"
        Write-Output "Recovery: proceed with one bounded real worker; worker result is authoritative."
        Set-DawoudTelemetryRecord -Path $coordination.Path -Fields @{ AgyPath = $agyPath; AgyAvailable = $false; FallbackEvents = @("AGY model discovery probe unavailable; real worker verification attempted: $reason") }
        $agyProbe.Available = $true
    } else {
        Write-DawoudRoutingWarning -Reason $reason
        Set-DawoudTelemetryRecord -Path $coordination.Path -Fields @{ FallbackEvents = @("AGY unavailable: $reason"); AgyPath = $agyPath; AgyAvailable = $false }
        Complete-DawoudTelemetryRecord -Path $coordination.Path -Status ERROR -ExitCode 2 -Summary $reason -AgyPath $agyPath
        $report = Get-DawoudExecutionReport -TelemetryRoot $telemetryRoot -SessionId $SessionId -WorkId $WorkId -CodexShare $CodexShare -AntigravityShare $AntigravityShare
        if ($report) { Write-Output ""; Write-Output $report.Text }
        exit 2
    }
}
if ([string]::IsNullOrWhiteSpace($agyPath) -or -not (Test-Path -LiteralPath $agyPath -PathType Leaf)) { throw "Antigravity CLI path could not be resolved after successful verification." }
Write-Output ("AGY EXECUTOR IDENTITY (worker): {0}" -f $runtimeIdentity.Name)

$slotMutex = Enter-DawoudWorkerSlotLock
try {
    $running = @(Read-States | Where-Object status -eq "RUNNING")
    if ($running.Count -ge $MaxWorkers) { throw "Worker limit reached: $MaxWorkers" }

    $runId = "agy-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + ([guid]::NewGuid().ToString("N").Substring(0, 6))
    $runDir = Join-Path $runRoot $runId
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    $taskPath = Join-Path $runDir "task.txt"
    $statePath = Join-Path $runDir "status.json"
    Set-Content -LiteralPath $taskPath -Value $Task -Encoding utf8
    $agyTelemetry = New-DawoudTelemetryRecord -TelemetryRoot $telemetryRoot -SessionId $SessionId -Task $Task -Executor ANTIGRAVITY -Category $route.Category -CodexShare $CodexShare -AntigravityShare $AntigravityShare -Leader $Leader -ResolvedLeader $resolvedLeader -RouteReason $route.Reason -Model $AntigravityModel -Effort $AntigravityEffort -Worker ("ANTIGRAVITY #$($running.Count + 1)") -AgyPath $agyPath -WorkId $WorkId -SelectedProjectPath $WorkingDirectory -ExecutorWorkingDirectory $WorkingDirectory -AgyAvailable $true -AgySelected $true -CodexAvailable $true -CodexSelected $false -RecordKind TASK
    Set-DawoudTelemetryRecord -Path $agyTelemetry.Path -Fields @{ AgyAvailable = $true; AgyVersion = $agyProbe.Version; InvocationPath = $agyPath }
    $state = [ordered]@{
    run_id = $runId
    worker = "ANTIGRAVITY #$($running.Count + 1)"
    status = "RUNNING"
    task = ($Task -replace "\s+", " ").Trim().Substring(0, [math]::Min(180, (($Task -replace "\s+", " ").Trim().Length)))
    working_directory = $WorkingDirectory
    started_at = (Get-Date).ToUniversalTime().ToString("o")
    finished_at = ""
    elapsed_seconds = 0
    worker_pid = 0
    exit_code = $null
    result_summary = ""
    error = ""
    leader = $Leader
    category = $route.Category
    route = $route.Agent
    route_reason = $route.Reason
    codex_share = $CodexShare
    antigravity_share = $AntigravityShare
    antigravity_model = $AntigravityModel
    antigravity_effort = $AntigravityEffort
    telemetry_id = $agyTelemetry.Id
    telemetry_path = $agyTelemetry.Path
    session_id = $SessionId
    work_id = $WorkId
    }
    $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding utf8

    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $workerScript, "-StatePath", $statePath, "-TaskPath", $taskPath, "-WorkingDirectory", $WorkingDirectory, "-AgyPath", $agyPath, "-TelemetryPath", $agyTelemetry.Path, "-OrchestratorPid", ([string]$PID))
    if ($HarnessPid -gt 0) { $args += @("-HarnessPid", ([string]$HarnessPid)) }
    if ($StartupDiagnosticPath) { $args += @("-StartupDiagnosticPath", $StartupDiagnosticPath) }
    if ($AntigravityModel) { $args += @("-AntigravityModel", $AntigravityModel) }
    if ($AntigravityEffort) { $args += @("-AntigravityEffort", $AntigravityEffort) }
    $args += @("-ConcurrentAgyCount", [string]($running.Count + 1))
    # Worker must run under stable Windows PowerShell host. Do not inherit a
    # wrapper/cmd path from the caller; that path caused Start-Process argument
    # dictionary collisions in the production UI.
    $shellPath = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    if ([string]::IsNullOrWhiteSpace($shellPath)) { $shellPath = "pwsh.exe" }
    $stdoutLog = Join-Path $runDir "worker.stdout.log"
    $stderrLog = Join-Path $runDir "worker.stderr.log"
    $argumentString = ($args | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join ' '
    $child = $null
    try {
        # Codex desktop can pass both PATH and Path. Normalize process PATH before
        # Start-Process builds child environment dictionary.
        $pathValue = [Environment]::GetEnvironmentVariable("PATH", "Process")
        Remove-Item Env:Path -ErrorAction SilentlyContinue
        $env:Path = $pathValue
        $child = Start-Process -FilePath $shellPath -ArgumentList $argumentString -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    } catch {
        $launchError = Protect-DawoudTelemetryText -Text ("line {0}: {1}" -f $_.InvocationInfo.ScriptLineNumber, $_.Exception.Message)
        $state.status = "ERROR"
        $state.finished_at = (Get-Date).ToUniversalTime().ToString("o")
        $state.exit_code = 1
        $state.error = "AGY worker launch failed: $launchError"
        $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding utf8
        Complete-DawoudTelemetryRecord -Path $agyTelemetry.Path -Status ERROR -ExitCode 1 -Summary $state.error -AgyPath $agyPath -OutputReturnedFromAGY $false
        throw $state.error
    }
    $state.worker_pid = $child.Id
    $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding utf8
    Complete-DawoudTelemetryRecord -Path $coordination.Path -Status DONE -Summary "Codex dispatched real Antigravity worker." -WorkerPid $PID
    Set-DawoudTelemetryRecord -Path $agyTelemetry.Path -Fields @{ PID = 0; AgyPath = $agyPath; AgyAvailable = $true }
    Write-Output "Started $($state.worker) PID $($child.Id)"
    Write-Output "Status: .\setup.ps1 orchestrator-dispatch -Command status"
    Write-Output "Result folder: $runDir"
} finally {
    Exit-DawoudWorkerSlotLock -Mutex $slotMutex
}
if ($Wait) {
    $waitDeadline = (Get-Date).AddSeconds($WaitTimeoutSeconds)
    $finalState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    do {
        if ((Get-Date) -ge $waitDeadline) {
            $timeoutReason = "orchestrator wait exceeded hard deadline of $WaitTimeoutSeconds seconds"
            try {
                if ($finalState.worker_pid -and (Get-Process -Id $finalState.worker_pid -ErrorAction SilentlyContinue)) {
                    & taskkill.exe /PID ([string]$finalState.worker_pid) /T /F 2>$null | Out-Null
                }
            } catch { }
            $finalState.status = "ERROR"
            $finalState.exit_code = 124
            $finalState.finished_at = (Get-Date).ToUniversalTime().ToString("o")
            $finalState.error = $timeoutReason
            $finalState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8
            try {
                if ($finalState.telemetry_path) {
                    Set-DawoudTelemetryRecord -Path ([string]$finalState.telemetry_path) -Fields @{ TimeoutState = "ORCHESTRATOR_WAIT"; FailureReason = $timeoutReason }
                    Complete-DawoudTelemetryRecord -Path ([string]$finalState.telemetry_path) -Status ERROR -ExitCode 124 -Summary $timeoutReason
                }
            } catch { }
            Write-Output "DAWOUD STATUS WARNING"
            Write-Output "Reason: $timeoutReason"
            exit 124
        }
        Start-Sleep -Seconds 1
        $finalState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    } while ($finalState.status -eq "RUNNING")
    $agyResultContract = [ordered]@{
        Contract = if ($finalState.contract) { [string]$finalState.contract } else { "DAWOUD_AGY_RESULT_V1" }
        Success = [bool]$finalState.success
        ActualExe = [string]$finalState.actual_exe
        ActualPid = if ($finalState.actual_pid) { [int]$finalState.actual_pid } elseif ($finalState.agy_pid) { [int]$finalState.agy_pid } else { 0 }
        StdinWritten = [bool]$finalState.stdin_written
        FirstStdoutEvent = [string]$finalState.first_stdout_event
        FirstRawStdout = [string]$finalState.first_raw_stdout
        StreamEvents = if ($null -ne $finalState.stream_events) { [int]$finalState.stream_events } else { 0 }
        FinalResultEvent = [bool]$finalState.final_result_event
        FinalResponse = [string]$finalState.final_response
        ExitCode = if ($null -ne $finalState.exit_code) { [int]$finalState.exit_code } else { 1 }
        TimeoutReason = if ($finalState.timeout_reason) { [string]$finalState.timeout_reason } else { "NONE" }
        FailedStage = [string]$finalState.failed_stage
        ExceptionType = [string]$finalState.exception_type
        Error = [string]$finalState.error
    }
    Write-Output ("DAWOUD_AGY_RESULT_V1::" + ($agyResultContract | ConvertTo-Json -Compress -Depth 8))
    $report = Get-DawoudExecutionReport -TelemetryRoot $telemetryRoot -SessionId $SessionId -WorkId $WorkId -CodexShare $CodexShare -AntigravityShare $AntigravityShare
    if ($report) { Write-Output ""; Write-Output $report.Text }
    if ($finalState.status -ne "DONE") { exit 1 }
}
