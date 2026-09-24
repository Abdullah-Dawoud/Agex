[CmdletBinding()]
param(
    [string]$Project,
    [string]$CodexPath,
    [string]$AgyPath,
    [string]$SessionId,
    [ValidateSet("Codex", "Antigravity", "Auto")][string]$ConfiguredLeader = "Codex",
    [int]$CodexShare = -1,
    [int]$AntigravityShare = -1,
    [string]$CodexModel,
    [string]$CodexEffort,
    [string]$AntigravityModel,
    [string]$AntigravityEffort,
    [ValidateSet("read-only", "workspace-write")][string]$CodexSandbox = "read-only",
    [switch]$SkipPrecheck,
    [switch]$Serve,
    [Parameter(DontShow)][string]$ExecutePrompt,
    [Parameter(DontShow)][string]$RequestWorkId,
    [Parameter(DontShow)][object]$SharedUiState,
    [ValidateRange(1, 2)][int]$MaxWorkers = 2,
    [Parameter(DontShow)][switch]$DefinitionsOnly,
    [Parameter(DontShow)][string]$ExecutorPrompt,
    [Parameter(DontShow)][string]$AssignedExecutor,
    [Parameter(DontShow)][string]$ExecutorWorkId,
    [Parameter(DontShow)][object]$ExecutorResult,
    [Parameter(DontShow)][bool]$ExecutorNeedsWrite = $false,
    [Parameter(DontShow)][object]$CancellationSignal,
    [Parameter(DontShow)][object]$AgexRuntime
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$orchestratorScript = Join-Path $PSScriptRoot "orchestrator.ps1"
. (Join-Path $PSScriptRoot "agex-common.ps1")
Initialize-AgexStorage
$telemetryRoot = Get-AgexPath Telemetry
$settingsPath = Get-AgexPath Settings
. (Join-Path $PSScriptRoot "agex-ui.ps1")
. (Join-Path $PSScriptRoot "agex-graph.ps1")
$script:isBackgroundExecution = [bool]($ExecutePrompt -or $ExecutorPrompt)

trap {
    # Background runspaces rethrow so the owner records the failure; they never
    # write to the shared console directly.
    if ($script:isBackgroundExecution) { break }
    if ($script:agexServe) { try { Send-AgexServeEvent -Event @{ type = "fatal"; message = (Protect-AgexTelemetryText -Text ([string]$_.Exception.Message)) } } catch { }; exit 1 }
    try { Restore-AgexConsole } catch { }
    [Console]::Error.WriteLine((Protect-AgexTelemetryText -Text ([string]$_.Exception.Message)))
    exit 1
}

function Remove-AgexOldLogs {
    # Log rotation: keep the newest 60 session logs.
    try { Get-ChildItem -LiteralPath (Get-AgexPath Logs) -Filter "*.jsonl" -File | Sort-Object LastWriteTime -Descending | Select-Object -Skip 60 | Remove-Item -Force -ErrorAction SilentlyContinue } catch { }
}

function Set-AgexEnabledAgentPolicy {
    # Agents the user turned off are unavailable for routing this session.
    foreach ($adapter in Get-AgexAdapters) {
        $enabled = @($script:preferences.enabled_agents) -contains $adapter.Id
        $current = $script:runtime.Health[$adapter.Name]
        if (-not $enabled) { Set-AgexAgentHealth -Runtime $script:runtime -Agent $adapter.Name -Healthy $false -Reason "Turned off in AGEX Agents settings." -Permanent }
        elseif ($current -and -not $current.Healthy -and $current.Reason -like "Turned off*") { $script:runtime.Health.Remove($adapter.Name) }
    }
}

# Settings fill in anything the caller did not pass explicitly.
$script:preferences = Get-AgexPreferences
if (-not $SessionId) { $SessionId = "session-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 6) }
if (-not $PSBoundParameters.ContainsKey("ConfiguredLeader")) { $ConfiguredLeader = [string]$script:preferences.leader }
if ($CodexShare -lt 0 -or $AntigravityShare -lt 0) { $shares = Get-AgexStrategyShares -Strategy $script:preferences.strategy -CodexShare $script:preferences.codex_share; $CodexShare = $shares.Codex; $AntigravityShare = $shares.Antigravity }
if (-not $PSBoundParameters.ContainsKey("CodexModel") -and $script:preferences.codex_model) { $CodexModel = [string]$script:preferences.codex_model }
if (-not $PSBoundParameters.ContainsKey("CodexEffort") -and $script:preferences.codex_effort) { $CodexEffort = [string]$script:preferences.codex_effort }
if (-not $PSBoundParameters.ContainsKey("AntigravityModel") -and $script:preferences.antigravity_model) { $AntigravityModel = [string]$script:preferences.antigravity_model }
if (-not $PSBoundParameters.ContainsKey("AntigravityEffort") -and $script:preferences.antigravity_effort) { $AntigravityEffort = [string]$script:preferences.antigravity_effort }
if (-not $PSBoundParameters.ContainsKey("CodexSandbox")) { $CodexSandbox = [string]$script:preferences.codex_task_sandbox }
if (-not $Project) { $Project = if ($script:preferences.last_project -and (Test-Path -LiteralPath $script:preferences.last_project -PathType Container)) { [string]$script:preferences.last_project } else { $scratch = Join-Path $env:USERPROFILE "AGEX-Workspace"; if (-not (Test-Path -LiteralPath $scratch)) { New-Item -ItemType Directory -Path $scratch -Force | Out-Null }; $scratch } }
if (-not $CodexPath) { $CodexPath = Resolve-AgexCodexExecutable }
if (-not $AgyPath) { $AgyPath = Resolve-AgyExecutable }
if ($env:AGEX_CODEX_TIMEOUT_SECONDS -eq $null -and $script:preferences.codex_timeout_seconds) { $env:AGEX_CODEX_TIMEOUT_SECONDS = [string]$script:preferences.codex_timeout_seconds }
if ($env:AGEX_AGY_TIMEOUT_SECONDS -eq $null -and $script:preferences.antigravity_timeout_seconds) { $env:AGEX_AGY_TIMEOUT_SECONDS = [string]$script:preferences.antigravity_timeout_seconds }

if (-not (Test-Path -LiteralPath $Project -PathType Container)) { throw "Project folder was not found: $Project. Choose another folder." }
if ($CodexShare + $AntigravityShare -ne 100) { throw "Codex and Antigravity workload percentages must total 100." }

$script:runtime = if ($AgexRuntime) { $AgexRuntime } else { New-AgexRuntime -SessionId $SessionId -LogPath (Join-Path (Get-AgexPath Logs) ("{0}.jsonl" -f $SessionId)) -CodexSandbox $CodexSandbox }
if (-not $AgexRuntime) { Remove-AgexOldLogs }
$script:history = [System.Collections.Generic.List[object]]::new()
$script:lastAgyPath = $AgyPath
$script:ResolvedLeader = Resolve-AgexLeader -ConfiguredLeader $ConfiguredLeader -CodexShare $CodexShare -AntigravityShare $AntigravityShare
$script:ActiveLeader = $script:ResolvedLeader
$script:ui = New-AgexUiState -Project $Project -SessionId $SessionId -ConfiguredLeader $ConfiguredLeader -ResolvedLeader $script:ResolvedLeader -CodexShare $CodexShare -AntigravityShare $AntigravityShare -CodexModel $CodexModel -AntigravityModel $AntigravityModel
if ($SharedUiState) { [void](Assert-AgexUiStateSchema -State $SharedUiState); $script:ui = $SharedUiState }
$script:cancellationSignal = if ($CancellationSignal) { $CancellationSignal } else { [hashtable]::Synchronized(@{ Requested = $false }) }


function Set-AgexUiTaskState {
    param([string]$TaskId, [string]$Status, [string]$Agent, [string]$Reason = "", [string]$ErrorText = "")
    if ($script:ui) { [void](Set-AgexUiTask -State $script:ui -TaskId $TaskId -Status $Status -Agent $Agent -Reason $Reason -ErrorText $ErrorText) }
}

function Get-AgexSanitizedCommand {
    param([string]$Text)
    $safe = Protect-AgexTelemetryText -Text $Text
    $safe = $safe -replace '(?i)(-Task\s+)("[^"]*"|\S+)', '$1<task>'
    if ($safe.Length -gt 220) { $safe = $safe.Substring(0, 220) + "..." }
    $safe
}

# ------------------------------------------------------------------ helpers

function New-AgexTelemetrySafe {
    # Telemetry is secondary: a failed write is logged, never fatal.
    param([Parameter(Mandatory)][hashtable]$Arguments)
    try { New-AgexTelemetryRecord @Arguments }
    catch { Write-AgexLog -Runtime $script:runtime -Event 'telemetry_warning' -Data @{ error = $_.Exception.Message }; $null }
}

function Complete-AgexTelemetrySafe {
    param($Record, [Parameter(Mandatory)][hashtable]$Arguments)
    if (-not $Record) { return }
    try { Complete-AgexTelemetryRecord -Path $Record.Path @Arguments }
    catch { Write-AgexLog -Runtime $script:runtime -Event 'telemetry_warning' -Data @{ error = $_.Exception.Message } }
}

function Get-AgexUiTaskId {
    param([string]$WorkId)
    if ($script:ui.WorkId -and $WorkId.StartsWith($script:ui.WorkId + "-")) { return $WorkId.Substring($script:ui.WorkId.Length + 1) }
    $WorkId
}

function Get-AgexTimeoutSeconds {
    param([string]$Name, [int]$Default)
    $value = 0
    if ([int]::TryParse([string][Environment]::GetEnvironmentVariable($Name), [ref]$value) -and $value -ge 30) { return $value }
    $Default
}

function Get-AgexErrorLine {
    # Most useful single line from an executor's error output.
    param([AllowEmptyString()][string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return "" }
    $lines = @($Text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -notmatch '^(WARNING: failed to clean up stale arg0|-{3,}|={3,})' })
    $best = @($lines | Where-Object { $_ -match '(?i)error|failed|invalid|denied|not found|unauthori|not logged|exceed|refused|timeout' } | Select-Object -Last 1)
    $line = if ($best.Count) { $best[0] } elseif ($lines.Count) { $lines[-1] } else { "" }
    $line = Protect-AgexTelemetryText -Text $line
    $line
}

function Get-AgexFailureReason {
    param([Parameter(Mandatory)][string]$Agent, [Parameter(Mandatory)]$Record)
    $detail = Get-AgexErrorLine -Text $(if ($Record.Stderr) { $Record.Stderr } else { $Record.Stdout })
    $suffix = if ($detail) { " $detail" } else { "" }
    switch ($Record.Outcome) {
        "START_FAILED" { "$Agent could not be started. $($Record.ExceptionMessage)" }
        "TIMED_OUT" { "$Agent did not finish in time and was stopped." }
        "CANCELLED" { "Cancelled by user." }
        "ERROR" { "$Agent run failed: $($Record.ExceptionMessage)" }
        "EXIT_NONZERO" { "$Agent stopped with exit code $($Record.ExitCode).$suffix" }
        default { "$Agent finished without returning a result.$suffix" }
    }
}

function Set-AgexExecutorOutcome {
    param([string]$Agent, [bool]$Success, [string]$Outcome, [string]$Reason, [bool]$FallbackEligible, $ExitCode)
    $script:lastExecutorOutcome = [pscustomobject]@{ Agent = $Agent; Success = $Success; Outcome = $Outcome; Reason = $Reason; FallbackEligible = $FallbackEligible; ExitCode = $ExitCode }
}

# ---------------------------------------------------------------- executors

function Invoke-AgyTask {
    param([Parameter(Mandatory)][string]$Prompt, [Parameter(Mandatory)][string]$WorkId, [Parameter(Mandatory)]$Route)
    $resolved = if ($AgyPath -and (Test-Path -LiteralPath $AgyPath -PathType Leaf)) { (Resolve-Path -LiteralPath $AgyPath).Path } else { Resolve-AgyExecutable }
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        $reason = "Antigravity CLI (agy.exe) was not found."
        Set-AgexAgentHealth -Runtime $script:runtime -Agent Antigravity -Healthy $false -Reason $reason -Permanent
        Set-AgexExecutorOutcome -Agent Antigravity -Success $false -Outcome 'START_FAILED' -Reason $reason -FallbackEligible $true -ExitCode $null
        return $false
    }
    $script:lastAgyPath = $resolved
    $uiTaskId = Get-AgexUiTaskId -WorkId $WorkId
    $record = New-AgexTelemetrySafe -Arguments @{ TelemetryRoot = $telemetryRoot; SessionId = $SessionId; Task = $Prompt; Executor = 'ANTIGRAVITY'; Category = $Route.Category; CodexShare = $CodexShare; AntigravityShare = $AntigravityShare; Leader = $ConfiguredLeader; ResolvedLeader = $script:ActiveLeader; RouteReason = $Route.Reason; Model = $AntigravityModel; Effort = $AntigravityEffort; Worker = "AGEX AGY EXECUTOR"; AgyPath = $resolved; WorkId = $WorkId; SelectedProjectPath = $Project; ExecutorWorkingDirectory = $Project; AgyAvailable = $true; AgySelected = $true; CodexAvailable = (Test-Path -LiteralPath $CodexPath -PathType Leaf); CodexSelected = $false; RecordKind = 'TASK' }
    $dispatchTaskPath = Join-Path $env:TEMP ("agex-task-" + [guid]::NewGuid().ToString("N") + ".txt")
    $startupPath = Join-Path $env:TEMP ("agex-interactive-{0}.startup.log" -f ([guid]::NewGuid().ToString("N")))
    $shell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    $agyNumber = 1 + @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Agents | Where-Object Executor -eq "ANTIGRAVITY").Count
    $agyCapture = @{ Contract = ""; AgentName = "ANTIGRAVITY #$agyNumber"; ProbeReason = "" }
    try {
        [IO.File]::WriteAllText($dispatchTaskPath, $Prompt, [Text.UTF8Encoding]::new($false))
        $dispatchArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $orchestratorScript, "dispatch", "-TaskPath", $dispatchTaskPath, "-AssignedAgent", "Antigravity", "-WorkingDirectory", $Project, "-Leader", $script:ActiveLeader, "-CodexShare", $CodexShare, "-AntigravityShare", $AntigravityShare, "-Executor", "CODEX", "-SessionId", $SessionId, "-WorkId", $WorkId, "-Wait", "-WaitTimeoutSeconds", "780", "-HarnessPid", ([string]$PID), "-StartupDiagnosticPath", $startupPath)
        if ($AntigravityModel) { $dispatchArgs += @("-AntigravityModel", $AntigravityModel) }
        if ($AntigravityEffort) { $dispatchArgs += @("-AntigravityEffort", $AntigravityEffort) }
        $display = "powershell -File orchestrator.ps1 dispatch -AssignedAgent Antigravity -Wait (runs $resolved --input-format stream-json --output-format stream-json" + $(if ($AntigravityModel) { " --model $AntigravityModel" } else { "" }) + ")"
        [void](Start-AgexUiAgent -State $script:ui -Name $agyCapture.AgentName -Executor "ANTIGRAVITY" -TaskId $uiTaskId -TaskText $Prompt -Model $AntigravityModel -Command $display -WorkingDirectory $Project -DiagnosticPath $startupPath)
        $run = Invoke-AgexProcess -FilePath $shell -Arguments $dispatchArgs -WorkingDirectory $root -TimeoutSeconds (Get-AgexTimeoutSeconds -Name 'AGEX_AGY_TIMEOUT_SECONDS' -Default 840) -CancellationSignal $script:cancellationSignal -Runtime $script:runtime -Label "Antigravity $uiTaskId" -Executor 'Antigravity' -TaskId $uiTaskId -RequestId $WorkId `
            -OnStarted { param($dispatchPid) [void](Update-AgexUiAgent -State $script:ui -Name $agyCapture.AgentName -Status "STARTING" -Action "Starting Antigravity" -ProcessId $dispatchPid -EventKind "START" -Message ("Dispatch PID {0}" -f $dispatchPid)); $dispatchAgent = Get-AgexUiAgentByName -State $script:ui -Name $agyCapture.AgentName; if ($dispatchAgent) { $dispatchAgent.DispatchPID = $dispatchPid } } `
            -OnStdoutLine { param($agyLine)
                if ($agyLine -match '^AGEX_AGY_RESULT_V1::') { $agyCapture.Contract = $agyLine }
                elseif ($agyLine -match '^Started\s+(.+?)\s+PID\s+(\d+)') { [void](Update-AgexUiAgent -State $script:ui -Name $agyCapture.AgentName -Status "RUNNING" -Action "Antigravity is working" -ProcessId ([int]$Matches[2]) -EventKind "START" -Message ("Worker PID {0}" -f $Matches[2])) }
                elseif ($agyLine -match '^Reason:\s*(.+)$') { $agyCapture.ProbeReason = $Matches[1] }
            } `
            -OnTick { [void](Update-AgexUiDiagnostic -State $script:ui -Name $agyCapture.AgentName) }
        $state = $null
        $contractMatch = [regex]::Match($agyCapture.Contract, '^AGEX_AGY_RESULT_V1::(.+)$')
        if ($contractMatch.Success) { try { $state = $contractMatch.Groups[1].Value | ConvertFrom-Json } catch { $state = $null } }
        $response = if ($state -and $state.FinalResponse) { [string]$state.FinalResponse } else { "" }
        $workerAgent = Get-AgexUiAgentByName -State $script:ui -Name $agyCapture.AgentName
        if ($state -and $state.ActualPid -gt 0 -and $workerAgent) { [System.Threading.Monitor]::Enter($script:ui.CollectionSync); try { $workerAgent.PID = [int]$state.ActualPid; $workerAgent.ActualPID = [int]$state.ActualPid } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) } }
        if ($run.Outcome -eq "CANCELLED" -or $script:cancellationSignal.Requested) {
            [void](Complete-AgexUiAgent -State $script:ui -Name $agyCapture.AgentName -Status "CANCELLED" -Message "Cancelled by user." -ExitCode 130)
            Complete-AgexTelemetrySafe -Record $record -Arguments @{ Status = 'CANCELLED'; ExitCode = 130; Summary = "User cancelled the active AGEX task."; AgyPath = $resolved; OutputReturnedFromAGY = $false }
            Set-AgexExecutorOutcome -Agent Antigravity -Success $false -Outcome 'CANCELLED' -Reason 'Cancelled by user.' -FallbackEligible $false -ExitCode 130
            return $false
        }
        $ok = $run.Outcome -eq "OK" -and $state -and $state.Success -eq $true -and $state.FinalResultEvent -eq $true -and $state.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($response)
        if ($ok) {
            [void](Complete-AgexUiAgent -State $script:ui -Name $agyCapture.AgentName -Status "DONE" -Message "Antigravity result received" -ExitCode 0 -StreamEvents $(if ($state.StreamEvents) { [int]$state.StreamEvents } else { 0 }))
            $script:lastExecutorResult = $response
            Complete-AgexTelemetrySafe -Record $record -Arguments @{ Status = 'DONE'; ExitCode = 0; Summary = $response; WorkerPid = ([int]$state.ActualPid); AgyPath = $resolved; OutputReturnedFromAGY = $true }
            Register-AgexAgentResult -Runtime $script:runtime -Agent Antigravity -Success $true
            Set-AgexExecutorOutcome -Agent Antigravity -Success $true -Outcome 'OK' -Reason '' -FallbackEligible $false -ExitCode 0
            return $true
        }
        $authFailure = ($state -and [string]$state.Error -match '(?i)AUTH') -or ($run.Stdout -match '(?i)not logged into Antigravity|EXECUTOR_AUTH_REQUIRED')
        $unavailable = $run.ExitCode -eq 2 -or $run.Outcome -in @("START_FAILED", "ERROR")
        $reason = if ($run.Outcome -in @("START_FAILED", "TIMED_OUT", "ERROR")) { Get-AgexFailureReason -Agent 'Antigravity' -Record $run }
            elseif ($authFailure) { "Antigravity is not signed in. Open Antigravity, sign in, then type :agents to check again." }
            elseif ($run.ExitCode -eq 2) { "Antigravity is not available: $(if ($agyCapture.ProbeReason) { $agyCapture.ProbeReason } else { Get-AgexErrorLine -Text $run.Stdout })" }
            elseif ($state -and $state.Error) { "Antigravity failed: $(Protect-AgexTelemetryText -Text ([string]$state.Error))" }
            elseif ($run.Stderr) { "Antigravity failed: $(Get-AgexErrorLine -Text $run.Stderr)" }
            else { "Antigravity stopped with exit code $($run.ExitCode) and returned no result." }
        $noWorkDone = (-not $state) -or ([int]$state.ActualPid -le 0) -or ([int]$state.StreamEvents -eq 0 -and [string]$state.TimeoutReason -ne 'TOTAL')
        $eligible = $unavailable -or $authFailure -or ($run.Outcome -ne "TIMED_OUT" -and $noWorkDone)
        if ($run.Outcome -ne "TIMED_OUT") { Register-AgexAgentResult -Runtime $script:runtime -Agent Antigravity -Success $false -Reason $reason -Immediate:($unavailable -or $authFailure) -Path $resolved }
        $exitCode = if ($null -ne $run.ExitCode -and $run.ExitCode -ne 0) { [int]$run.ExitCode } else { 1 }
        [void](Complete-AgexUiAgent -State $script:ui -Name $agyCapture.AgentName -Status "FAILED" -Message $reason -ExitCode $exitCode -Timeout $(if ($run.TimedOut) { "TOTAL" } elseif ($state -and $state.TimeoutReason) { [string]$state.TimeoutReason } else { "NONE" }))
        Complete-AgexTelemetrySafe -Record $record -Arguments @{ Status = 'ERROR'; ExitCode = $exitCode; Summary = $reason; AgyPath = $resolved; OutputReturnedFromAGY = $false }
        Set-AgexExecutorOutcome -Agent Antigravity -Success $false -Outcome $(if ($run.Outcome -eq 'OK') { 'NO_RESULT' } else { $run.Outcome }) -Reason $reason -FallbackEligible $eligible -ExitCode $exitCode
        return $false
    } catch {
        $reason = "Antigravity run failed: $(Protect-AgexTelemetryText -Text $_.Exception.Message)"
        [void](Complete-AgexUiAgent -State $script:ui -Name $agyCapture.AgentName -Status "FAILED" -Message $reason -ExitCode 1)
        Complete-AgexTelemetrySafe -Record $record -Arguments @{ Status = 'ERROR'; ExitCode = 1; Summary = $reason; AgyPath = $resolved; OutputReturnedFromAGY = $false }
        Set-AgexExecutorOutcome -Agent Antigravity -Success $false -Outcome 'ERROR' -Reason $reason -FallbackEligible $true -ExitCode 1
        return $false
    } finally {
        if (Test-Path -LiteralPath $dispatchTaskPath) { Remove-Item -LiteralPath $dispatchTaskPath -Force -ErrorAction SilentlyContinue }
    }
}

function Get-AgexCodexArguments {
    $sandbox = if ($script:runtime -and $script:runtime.CodexSandbox -eq "workspace-write") { "workspace-write" } else { "read-only" }
    # --skip-git-repo-check: the project folder is chosen by the user and may
    # not be a trusted Git repository; without it Codex refuses to start.
    $list = @("exec", "--ephemeral", "--skip-git-repo-check", "-C", $Project, "--sandbox", $sandbox)
    if ($CodexModel) { $list += @("--model", $CodexModel) }
    if ($CodexEffort) { $list += @("--config", "model_reasoning_effort=$CodexEffort") }
    $list + @("-")
}

function Invoke-CodexTask {
    param([Parameter(Mandatory)][string]$Prompt, [Parameter(Mandatory)][string]$WorkId, [Parameter(Mandatory)]$Route)
    $uiTaskId = Get-AgexUiTaskId -WorkId $WorkId
    $record = New-AgexTelemetrySafe -Arguments @{ TelemetryRoot = $telemetryRoot; SessionId = $SessionId; Task = $Prompt; Executor = 'CODEX'; Category = $Route.Category; CodexShare = $CodexShare; AntigravityShare = $AntigravityShare; Leader = $ConfiguredLeader; ResolvedLeader = $script:ActiveLeader; RouteReason = $Route.Reason; Model = $CodexModel; Effort = $CodexEffort; WorkId = $WorkId; SelectedProjectPath = $Project; ExecutorWorkingDirectory = $Project; AgyAvailable = [bool]$script:lastAgyPath; AgySelected = $false; CodexAvailable = $true; CodexSelected = $true; RecordKind = 'TASK' }
    $arguments = Get-AgexCodexArguments
    $display = (@("codex") + @($arguments | ForEach-Object { Protect-AgexCommandArgument -Value $_ })) -join " "
    try {
        [void](Start-AgexUiAgent -State $script:ui -Name "CODEX" -Executor "CODEX" -TaskId $uiTaskId -TaskText $Prompt -Model $CodexModel -Command $display -WorkingDirectory $Project)
        $run = Invoke-AgexProcess -FilePath $CodexPath -Arguments $arguments -WorkingDirectory $Project -StdinText $Prompt -TimeoutSeconds (Get-AgexTimeoutSeconds -Name 'AGEX_CODEX_TIMEOUT_SECONDS' -Default 900) -CancellationSignal $script:cancellationSignal -Runtime $script:runtime -Label "Codex $uiTaskId" -Executor 'Codex' -TaskId $uiTaskId -RequestId $WorkId `
            -OnStarted { param($codexPid) [void](Update-AgexUiAgent -State $script:ui -Name "CODEX" -Status "RUNNING" -Action "Codex is working" -ProcessId $codexPid -EventKind "START" -Message ("Codex PID {0}" -f $codexPid)) }
        $output = ([string]$run.Stdout).Trim()
        if ($run.Outcome -eq "CANCELLED" -or $script:cancellationSignal.Requested) {
            [void](Complete-AgexUiAgent -State $script:ui -Name "CODEX" -Status "CANCELLED" -Message "Cancelled by user." -ExitCode 130)
            Complete-AgexTelemetrySafe -Record $record -Arguments @{ Status = 'CANCELLED'; ExitCode = 130; Summary = "User cancelled the active AGEX task."; WorkerPid = $run.ProcessId; OutputReturnedFromAGY = $false }
            Set-AgexExecutorOutcome -Agent Codex -Success $false -Outcome 'CANCELLED' -Reason 'Cancelled by user.' -FallbackEligible $false -ExitCode 130
            return $false
        }
        if ($run.Outcome -eq "OK" -and $output) {
            [void](Complete-AgexUiAgent -State $script:ui -Name "CODEX" -Status "DONE" -Message "Codex result received" -ExitCode 0)
            $script:lastExecutorResult = $output
            Complete-AgexTelemetrySafe -Record $record -Arguments @{ Status = 'DONE'; ExitCode = 0; Summary = $output; WorkerPid = $run.ProcessId; OutputReturnedFromAGY = $false }
            Register-AgexAgentResult -Runtime $script:runtime -Agent Codex -Success $true
            Set-AgexExecutorOutcome -Agent Codex -Success $true -Outcome 'OK' -Reason '' -FallbackEligible $false -ExitCode 0
            return $true
        }
        $reason = Get-AgexFailureReason -Agent 'Codex' -Record $run
        $exitCode = if ($run.TimedOut) { 124 } elseif ($null -ne $run.ExitCode -and $run.ExitCode -ne 0) { [int]$run.ExitCode } else { 1 }
        # Start-level failures repeat for every call: stop using Codex for a cooldown.
        if ($run.Outcome -ne "TIMED_OUT") { Register-AgexAgentResult -Runtime $script:runtime -Agent Codex -Success $false -Reason $reason -Immediate:($run.Outcome -in @("START_FAILED", "ERROR")) -Path $CodexPath }
        [void](Complete-AgexUiAgent -State $script:ui -Name "CODEX" -Status "FAILED" -Message $reason -ExitCode $exitCode -Timeout $(if ($run.TimedOut) { "TOTAL" } else { "" }))
        Complete-AgexTelemetrySafe -Record $record -Arguments @{ Status = 'ERROR'; ExitCode = $exitCode; Summary = $reason; WorkerPid = $run.ProcessId; OutputReturnedFromAGY = $false }
        Set-AgexExecutorOutcome -Agent Codex -Success $false -Outcome $(if ($run.Outcome -eq 'OK') { 'NO_RESULT' } else { $run.Outcome }) -Reason $reason -FallbackEligible ($run.FallbackEligible -or $run.Outcome -eq 'OK') -ExitCode $exitCode
        return $false
    } catch {
        $reason = "Codex run failed: $(Protect-AgexTelemetryText -Text $_.Exception.Message)"
        if (Get-AgexUiAgentByName -State $script:ui -Name "CODEX") { [void](Complete-AgexUiAgent -State $script:ui -Name "CODEX" -Status "FAILED" -Message $reason -ExitCode 1) }
        Complete-AgexTelemetrySafe -Record $record -Arguments @{ Status = 'ERROR'; ExitCode = 1; Summary = $reason; OutputReturnedFromAGY = $false }
        Set-AgexExecutorOutcome -Agent Codex -Success $false -Outcome 'ERROR' -Reason $reason -FallbackEligible $true -ExitCode 1
        return $false
    }
}

# --------------------------------------------------------- request lifecycle

function Invoke-AgexTask {
    param([Parameter(Mandatory)][string]$Prompt)
    if (-not $script:ui.WorkId) { $script:ui.WorkId = if ($RequestWorkId) { $RequestWorkId } else { "ui-" + ([guid]::NewGuid().ToString("N")) } }
    $workId = $script:ui.WorkId
    $script:ui.Prompt = $Prompt
    $script:ui.Status = "RUNNING"
    Write-AgexLog -Runtime $script:runtime -Event 'request_start' -Data @{ request = $workId; leader = $script:ActiveLeader; configured_leader = $ConfiguredLeader; prompt_chars = $Prompt.Length; project = $Project }
    Add-AgexMessage -State $script:ui -From 'User' -To 'AGEX' -Type ASSIGNMENT -Text $Prompt
    Save-AgexSessionRecord -State $script:ui -Runtime $script:runtime -MaxSessions ([int]$script:preferences.max_sessions)
    Start-AgexUiFileWatch -State $script:ui
    try { Invoke-AgexGoalGraph -RootGoal $Prompt -WorkId $workId }
    finally {
        Complete-AgexUiSession -State $script:ui
        Save-AgexSessionRecord -State $script:ui -Runtime $script:runtime -MaxSessions ([int]$script:preferences.max_sessions)
    }
}

function Reset-AgexRequestState {
    param([Parameter(Mandatory)][string]$WorkId)
    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
    try { $script:ui.Tasks.Clear(); $script:ui.Events.Clear(); $script:ui.Agents.Clear(); $script:ui.Files.Clear(); $script:ui.Chat.Clear(); $script:ui.Messages.Clear() }
    finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
    $script:ui.WorkId = $WorkId
    $script:ui.Status = "RUNNING"
    $script:ui.GoalStatus = "RUNNING"
    $script:ui.Result = ""
    $script:ui.Summary = $null
    $script:ui.Outcome = $null
    $script:ui.RequestStarted = Get-Date
    $script:ui.Prompt = ""
    $script:ui.GitChanges = $null
    $script:ui.AcceptanceStage = "Planning"
    $script:ui.CurrentAction = $null
    $script:ui.CurrentCommand = $null
    $script:ui.LastRenderedFingerprint = ""
    $script:ui.CancellationProcessesCleaned = "NOT APPLICABLE"
    $script:ui.CancellationOwnedPids = @()
}

function Start-AgexTaskExecution {
    param([Parameter(Mandatory)][string]$Prompt, [string]$LeaderOverride)
    $leader = if ($LeaderOverride) { $LeaderOverride } else { $ConfiguredLeader }
    $workId = "ui-" + ([guid]::NewGuid().ToString("N"))
    Reset-AgexRequestState -WorkId $workId
    $script:ui.ConfiguredLeader = $leader
    $script:ui.ResolvedLeader = Resolve-AgexLeader -ConfiguredLeader $leader -CodexShare $CodexShare -AntigravityShare $AntigravityShare
    $script:lastPrompt = $Prompt
    $script:lastLeaderOverride = $LeaderOverride
    $runspace = [RunspaceFactory]::CreateRunspace()
    $powerShell = [PowerShell]::Create()
    try {
        $runspace.Open()
        $powerShell.Runspace = $runspace
        [void]$powerShell.AddCommand($PSCommandPath)
        $parameters = @{ Project = $Project; MaxWorkers = $MaxWorkers; CodexPath = $CodexPath; AgyPath = $AgyPath; SessionId = $SessionId; ConfiguredLeader = $leader; CodexShare = $CodexShare; AntigravityShare = $AntigravityShare; CodexModel = $CodexModel; CodexEffort = $CodexEffort; AntigravityModel = $AntigravityModel; AntigravityEffort = $AntigravityEffort; ExecutePrompt = $Prompt; RequestWorkId = $workId; SharedUiState = $script:ui; AgexRuntime = $script:runtime }
        foreach ($key in $parameters.Keys) { [void]$powerShell.AddParameter($key, $parameters[$key]) }
        $signal = [hashtable]::Synchronized(@{ Requested = $false })
        [void]$powerShell.AddParameter("CancellationSignal", $signal)
        $async = $powerShell.BeginInvoke()
        $script:activeExecution = [pscustomobject]@{ PowerShell = $powerShell; Runspace = $runspace; Async = $async; Prompt = $Prompt; Started = Get-Date; CancellationSignal = $signal; CancelCount = 0; CancelRequestedAt = $null; StopIssued = $false; WorkId = $workId }
        Add-AgexActivity -Kind 'INFO' -Message ("Request sent. Leader: {0}." -f $script:ui.ResolvedLeader)
        return $true
    } catch {
        try { $powerShell.Dispose() } catch { }
        try { $runspace.Dispose() } catch { }
        $message = Protect-AgexTelemetryText -Text $_.Exception.Message
        Add-AgexActivity -Kind 'FAIL' -Message "AGEX could not start the request: $message"
        $script:ui.Status = "START_FAILED"; $script:ui.GoalStatus = "START_FAILED"
        $script:ui.Outcome = [pscustomobject]@{ Status = 'START_FAILED'; Headline = 'Request could not start.'; Reason = $message; Verification = ''; Tasks = 0; Done = 0; Failed = 0; Cancelled = 0; Executors = [pscustomobject]@{ Codex = 0; Antigravity = 0 }; Fallbacks = @(); Failures = @(); PrimaryFailure = $message; WhatHappened = @(); TaskResults = @(); DurationSeconds = 0 }
        return $false
    }
}

function Complete-AgexTaskExecution {
    if (-not $script:activeExecution -or -not $script:activeExecution.Async.IsCompleted) { return $false }
    $execution = $script:activeExecution
    $script:activeExecution = $null
    $cancelled = [bool]$execution.CancellationSignal.Requested
    try {
        [void]$execution.PowerShell.EndInvoke($execution.Async)
    } catch {
        if (-not $cancelled) {
            $message = Protect-AgexTelemetryText -Text $_.Exception.Message
            Add-AgexActivity -Kind 'FAIL' -Message "Internal error: $message"
            Write-AgexLog -Runtime $script:runtime -Event 'execution_error' -Data @{ request = $execution.WorkId; error = $_.Exception.ToString() }
            if (-not $script:ui.Outcome) {
                $script:ui.Outcome = [pscustomobject]@{ Status = 'FAILED'; Headline = 'Request failed because of an internal error.'; Reason = $message; Verification = ''; Tasks = $script:ui.Tasks.Count; Done = @($script:ui.Tasks | Where-Object Status -eq 'DONE').Count; Failed = @($script:ui.Tasks | Where-Object Status -in @('FAILED','REPAIR REQUIRED')).Count; Cancelled = 0; Executors = [pscustomobject]@{ Codex = 0; Antigravity = 0 }; Fallbacks = @(); Failures = @(); PrimaryFailure = $message; WhatHappened = @(); TaskResults = @(); DurationSeconds = [math]::Round(((Get-Date) - $execution.Started).TotalSeconds, 1) }
                $script:ui.GoalStatus = 'FAILED'; $script:ui.Status = 'FAILED'; $script:ui.Result = "GOAL: FAILED`n$message"
            }
        }
    } finally {
        try { $execution.PowerShell.Dispose() } catch { }
        try { $execution.Runspace.Dispose() } catch { }
    }
    if ($cancelled) {
        foreach ($task in @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -in @("QUEUED", "STARTING", "RUNNING", "WAITING", "VERIFYING", "BLOCKED"))) { [void](Set-AgexUiTask -State $script:ui -TaskId $task.Id -Status "CANCELLED" -Agent $task.Agent) }
        foreach ($agent in @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Agents | Where-Object Status -notin @("DONE", "FAILED", "CANCELLED", "IDLE"))) { [void](Complete-AgexUiAgent -State $script:ui -Name $agent.Name -Status "CANCELLED" -Message "Cancelled by user." -ExitCode 130) }
        [void](Stop-AgexOwnedProcesses -Runtime $script:runtime)
        $remaining = @($script:ui.CancellationOwnedPids | Where-Object { Get-Process -Id ([int]$_) -ErrorAction SilentlyContinue })
        $script:ui.CancellationProcessesCleaned = if ($remaining.Count) { "NO" } else { "YES" }
        $script:ui.Status = "CANCELLED"; $script:ui.GoalStatus = "CANCELLED"; $script:ui.CurrentAction = $null
        if (-not $script:ui.Outcome -or $script:ui.Outcome.Status -ne 'CANCELLED') {
            $tasks = @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks)
            $script:ui.Outcome = [pscustomobject]@{ Status = 'CANCELLED'; Headline = 'Request cancelled.'; Reason = "Stopped by you. Agent processes cleaned up: $($script:ui.CancellationProcessesCleaned)."; Verification = ''; Tasks = $tasks.Count; Done = @($tasks | Where-Object Status -eq 'DONE').Count; Failed = @($tasks | Where-Object Status -in @('FAILED','REPAIR REQUIRED')).Count; Cancelled = @($tasks | Where-Object Status -eq 'CANCELLED').Count; Executors = [pscustomobject]@{ Codex = 0; Antigravity = 0 }; Fallbacks = @(); Failures = @(); PrimaryFailure = ''; WhatHappened = @(); TaskResults = @(); DurationSeconds = [math]::Round(((Get-Date) - $execution.Started).TotalSeconds, 1) }
            $script:ui.Result = "GOAL: CANCELLED`nRequest cancelled."
        }
        Add-AgexActivity -Kind 'INFO' -Message ("Request cancelled. Agent processes cleaned up: {0}." -f $script:ui.CancellationProcessesCleaned)
    } elseif ($script:ui.Status -in @('RUNNING', 'CANCELLING')) {
        $script:ui.Status = if ($script:ui.GoalStatus -and $script:ui.GoalStatus -ne 'RUNNING') { $script:ui.GoalStatus } else { 'FAILED' }
    }
    Write-AgexLog -Runtime $script:runtime -Event 'request_end' -Data @{ request = $execution.WorkId; status = [string]$script:ui.Status; duration_s = [math]::Round(((Get-Date) - $execution.Started).TotalSeconds, 1) }
    if (-not $script:ui.Prompt) { $script:ui.Prompt = $execution.Prompt }
    Save-AgexSessionRecord -State $script:ui -Runtime $script:runtime -MaxSessions ([int]$script:preferences.max_sessions)
    if ($script:history.Count -ge 50) { $script:history.RemoveAt(0) }
    [void]$script:history.Add([pscustomobject]@{ Role = "AGEX"; Text = ("{0}: {1}" -f $script:ui.Status, $(if ($script:ui.Outcome) { $script:ui.Outcome.Headline } else { '' })) })
    $true
}

function Pump-AgexTaskExecution {
    $execution = $script:activeExecution
    if ($execution -and -not $execution.Async.IsCompleted -and $execution.CancelRequestedAt -and -not $execution.StopIssued -and ((Get-Date) - $execution.CancelRequestedAt).TotalSeconds -ge 8) {
        # Processes are already killed; stop the pipeline if it has not unwound.
        $execution.StopIssued = $true
        try { [void]$execution.PowerShell.BeginStop($null, $null) } catch { }
    }
    [void](Complete-AgexTaskExecution)
    if (-not $script:activeExecution -and $script:queuedPrompts -and $script:queuedPrompts.Count -gt 0) {
        $next = $script:queuedPrompts.Dequeue()
        [void](Start-AgexTaskExecution -Prompt $next)
    }
}

function Get-AgexOwnedProcessTreeIds {
    param([int[]]$RootProcessIds)
    $items = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)
    $ids = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($rootPid in @($RootProcessIds | Where-Object { $_ -gt 0 })) { [void]$ids.Add([int]$rootPid) }
    $changed = $true
    while ($changed) {
        $changed = $false
        foreach ($item in $items) {
            if ($ids.Contains([int]$item.ParentProcessId) -and $ids.Add([int]$item.ProcessId)) { $changed = $true }
        }
    }
    @($ids | Sort-Object)
}

function Get-AgexActiveExecutionRootPids {
    @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Agents | Where-Object { $_.Status -notin @("DONE", "FAILED", "CANCELLED", "IDLE") } | ForEach-Object {
        if ($_.DispatchPID -gt 0) { [int]$_.DispatchPID }
        elseif ($_.PID -gt 0) { [int]$_.PID }
    } | Sort-Object -Unique)
}

function Request-AgexTaskCancellation {
    # Non-blocking: signal, then stop only process trees this session started.
    if (-not $script:activeExecution) { return }
    $execution = $script:activeExecution
    $execution.CancelCount++
    $execution.CancellationSignal.Requested = $true
    if (-not $execution.CancelRequestedAt) { $execution.CancelRequestedAt = Get-Date }
    $script:ui.Status = "CANCELLING"
    Add-AgexActivity -Kind 'WARNING' -Message 'Cancelling the current request...'
    $owned = [System.Collections.Generic.List[int]]::new()
    foreach ($entry in @($script:runtime.OwnedProcesses.Values)) { if ($entry) { [void]$owned.Add([int]$entry.Pid) } }
    foreach ($rootPid in @(Get-AgexActiveExecutionRootPids)) { if (-not $owned.Contains([int]$rootPid)) { [void]$owned.Add([int]$rootPid) } }
    foreach ($ownedPid in $owned) { Stop-AgexProcessTree -ProcessId $ownedPid }
    foreach ($key in @($script:runtime.OwnedProcesses.Keys)) { $script:runtime.OwnedProcesses.Remove($key) }
    $script:ui.CancellationOwnedPids = @($owned)
    Write-AgexLog -Runtime $script:runtime -Event 'cancel' -Data @{ request = $execution.WorkId; owned_pids = (@($owned) -join ',') }
}

function Stop-AgexSessionWork {
    # Used on quit: cancel, wait a bounded time, then kill anything still owned.
    if ($script:activeExecution) {
        Request-AgexTaskCancellation
        $deadline = (Get-Date).AddSeconds(6)
        while ($script:activeExecution -and -not $script:activeExecution.Async.IsCompleted -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if ($script:activeExecution -and -not $script:activeExecution.Async.IsCompleted) { try { [void]$script:activeExecution.PowerShell.BeginStop($null, $null) } catch { } }
        if ($script:activeExecution -and $script:activeExecution.Async.IsCompleted) { [void](Complete-AgexTaskExecution) }
        elseif ($script:activeExecution) { try { $script:activeExecution.Runspace.Dispose() } catch { }; $script:activeExecution = $null }
    }
    [void](Stop-AgexOwnedProcesses -Runtime $script:runtime)
    try { Stop-AgexUiFileWatch -State $script:ui } catch { }
}

# ----------------------------------------------------------- agent precheck

function Invoke-AgexPrecheck {
    # Bounded health check of enabled adapters ("--version", no network).
    param([switch]$Quiet)
    $results = [ordered]@{}
    foreach ($adapter in Get-AgexAdapters) {
        if (@($script:preferences.enabled_agents) -notcontains $adapter.Id) { continue }
        $exe = if ($adapter.Id -eq 'codex') { $CodexPath } elseif ($adapter.Id -eq 'antigravity') { if ($AgyPath -and (Test-Path -LiteralPath $AgyPath -PathType Leaf)) { $AgyPath } else { Resolve-AgyExecutable } } else { Resolve-AgexAdapterExecutable -Adapter $adapter }
        if ($adapter.Id -eq 'antigravity' -and $exe) { $script:lastAgyPath = $exe }
        $results[$adapter.Name] = Test-AgexAgentPrecheck -Runtime $script:runtime -Agent $adapter.Name -ExecutablePath $exe -WorkingDirectory $Project
    }
    Set-AgexEnabledAgentPolicy
    foreach ($adapter in Get-AgexAdapters) { if (-not $results.Contains($adapter.Name)) { $results[$adapter.Name] = Get-AgexAgentHealth -Runtime $script:runtime -Agent $adapter.Name } }
    if (-not $Quiet) {
        foreach ($name in @($results.Keys)) {
            $health = $results[$name]
            if ($health.Healthy) { Add-AgexActivity -Kind 'PASS' -Message "$name ready." } else { Add-AgexActivity -Kind 'WARNING' -Message ("{0} unavailable: {1}" -f $name, $health.Reason) }
        }
        $ready = @($results.Values | Where-Object Healthy)
        if (-not $ready.Count) { Add-AgexActivity -Kind 'FAIL' -Message 'No agent is available. Fix or enable an agent, then check again.' }
        elseif ($ready.Count -lt $results.Count) { Add-AgexActivity -Kind 'WARNING' -Message ("Session will use {0} only." -f (($ready | ForEach-Object Agent) -join ' and ')) }
    }
    [pscustomobject]$results
}

function Test-AgexCommandLine {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)
    $Value -match '^:(help|h|\?|paste|send|cancel|quit|q|exit|clear|details|agents|tasks|files|changes|diff|chat|log|result|retry|codex|agy|antigravity|leader|workload|model|project|status|report|history)(\s.*)?$'
}

# ------------------------------------------------------------- commands

function Get-AgexHelpLines {
    @(
        "Type your request in the box at the bottom, then press Ctrl+Enter to send."
        "Enter adds a new line. You can also type :send on its own line, or press Ctrl+S."
        ""
        "Commands (type one and press Enter):"
        "  :help            Show this help"
        "  :retry           Run the last request again"
        "  :codex  :agy     Run the last request again with Codex or Antigravity as leader"
        "  :details         What ran: commands, exit codes, errors"
        "  :log [N]         Recent activity (default 40 lines)"
        "  :result          Full result of the last request"
        "  :agents          Agent status; checks Codex and Antigravity again"
        "  :project [path]  Show or change the project folder"
        "  :clear           Clear the result and activity"
        "  :cancel          Cancel the running request (same as Ctrl+C)"
        "  :quit            Exit AGEX"
        ""
        "Keys: Esc back   PgUp/PgDn scroll   Ctrl+U clear draft   Ctrl+C cancel"
        "More: :leader Codex|Antigravity|Auto   :workload CODEX AGY   :model   :changes   :diff   :chat   :report   :history"
    )
}

function Get-AgexDetailsLines {
    $lines = [System.Collections.Generic.List[string]]::new()
    $snapshot = New-AgexUiRenderSnapshot -State $script:ui
    $outcome = $snapshot.Outcome
    [void]$lines.Add("REQUEST")
    if ($outcome) {
        [void]$lines.Add(("  Status: {0} - {1}" -f $outcome.Status, $outcome.Headline))
        if ($outcome.Reason) { [void]$lines.Add("  Reason: $($outcome.Reason)") }
        [void]$lines.Add(("  Tasks: {0} | done {1} | failed {2} | cancelled {3} | duration {4}s" -f $outcome.Tasks, $outcome.Done, $outcome.Failed, $outcome.Cancelled, $outcome.DurationSeconds))
        [void]$lines.Add(("  Agent runs: Codex {0} | Antigravity {1}" -f $outcome.Executors.Codex, $outcome.Executors.Antigravity))
        foreach ($item in @($outcome.Fallbacks)) { [void]$lines.Add(("  Fallback: {0} -> {1} for {2} ({3}). Why: {4}" -f $item.From, $item.To, $item.Purpose, $(if ($item.Recovered) { 'recovered' } else { 'not recovered' }), $item.Reason)) }
        foreach ($item in @($outcome.Failures)) { [void]$lines.Add(("  Failure: {0} {1}: {2}" -f $item.Agent, $item.Purpose, $item.Reason)) }
    } elseif ($snapshot.WorkId) { [void]$lines.Add("  Status: $($snapshot.Status) (running)") }
    else { [void]$lines.Add("  No request yet.") }
    [void]$lines.Add(("  Leader: configured {0}, active {1}" -f $snapshot.ConfiguredLeader, $snapshot.ResolvedLeader))
    [void]$lines.Add("")
    [void]$lines.Add("TASKS")
    $tasks = @($snapshot.Tasks)
    if (-not $tasks.Count) { [void]$lines.Add("  None") }
    foreach ($task in $tasks) {
        [void]$lines.Add(("  {0} {1} [{2}] {3}" -f $task.Id, $task.Status, $task.Agent, $task.Summary))
        if ($task.Error) { [void]$lines.Add("      $($task.Error)") }
    }
    [void]$lines.Add("")
    [void]$lines.Add("AGENT RUNS (newest first)")
    $executions = @($script:runtime.Executions.ToArray() | Where-Object { $_.Label -notmatch 'precheck' } | Select-Object -Last 8)
    [array]::Reverse($executions)
    if (-not $executions.Count) { [void]$lines.Add("  None yet.") }
    foreach ($run in $executions) {
        [void]$lines.Add(("  {0} | {1} | {2} | exit {3} | {4}s | {5}" -f $run.StartedAt.ToString("HH:mm:ss"), $run.Label, $run.Outcome, $(if ($null -eq $run.ExitCode) { "-" } else { $run.ExitCode }), $run.DurationSeconds, $run.Summary))
        [void]$lines.Add("      Executable: $($run.Executable)$(if ($run.LaunchedFile -and $run.LaunchedFile -ne $run.Executable) { " (launched via $($run.LaunchedFile))" })")
        [void]$lines.Add("      Command: $($run.CommandLine)")
        [void]$lines.Add(("      Folder: {0} | PID {1} | timed out: {2} | cancelled: {3} | input {4} chars" -f $run.WorkingDirectory, $run.ProcessId, $run.TimedOut, $run.Cancelled, $run.StdinChars))
        if ($run.ExceptionType) { [void]$lines.Add("      Exception: $($run.ExceptionType): $($run.ExceptionMessage)") }
        if ($run.StderrTail) { foreach ($tail in @(($run.StderrTail -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -Last 4)) { [void]$lines.Add("      stderr: $tail") } }
    }
    [void]$lines.Add("")
    [void]$lines.Add("AGENTS")
    foreach ($name in @("Codex", "Antigravity")) {
        $health = Get-AgexAgentHealth -Runtime $script:runtime -Agent $name
        $state = if (-not $health.Checked) { "not checked" } elseif ($health.Healthy) { "ready" } else { "unavailable" }
        [void]$lines.Add(("  {0}: {1}. {2}" -f $name, $state, $health.Reason))
        if ($health.Path) { [void]$lines.Add("      Path: $($health.Path)") }
    }
    [void]$lines.Add("")
    [void]$lines.Add("ROUTING")
    [void]$lines.Add(("  Workload target: Antigravity {0}% / Codex {1}%" -f $AntigravityShare, $CodexShare))
    [void]$lines.Add(("  Models: Codex {0} ({1}) | Antigravity {2} ({3})" -f $(if ($CodexModel) { $CodexModel } else { "default" }), $(if ($CodexEffort) { $CodexEffort } else { "default" }), $(if ($AntigravityModel) { $AntigravityModel } else { "default" }), $(if ($AntigravityEffort) { $AntigravityEffort } else { "default" })))
    [void]$lines.Add("  Codex sandbox: $($script:runtime.CodexSandbox)")
    [void]$lines.Add("")
    [void]$lines.Add("SESSION")
    [void]$lines.Add("  Session: $SessionId")
    [void]$lines.Add("  Project: $Project")
    [void]$lines.Add("  Log file: $($script:runtime.LogPath)")
    $identity = Get-AgexRuntimeIdentity
    [void]$lines.Add("  Windows user: $($identity.Name)$(if ($identity.Restricted) { ' (restricted Codex sandbox: results may be invalid)' })")
    @($lines)
}

function Get-AgexLogLines {
    param([int]$Count = 40)
    $events = @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Events | Select-Object -Last $Count)
    if (-not $events.Count) { return @("No activity yet.") }
    @($events | ForEach-Object { "{0} {1,-18} {2,-9} {3}" -f $_.At.ToString("HH:mm:ss"), (Limit-AgexUiText -Text $_.Source -Width 18), (Limit-AgexUiText -Text $_.Kind -Width 9), $_.Message })
}

function Get-AgexAgentsLines {
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @("Codex", "Antigravity")) {
        $health = Get-AgexAgentHealth -Runtime $script:runtime -Agent $name
        $mark = if ($health.Healthy) { "ready" } else { "unavailable" }
        [void]$lines.Add(("{0,-12} {1}" -f $name, $mark))
        if ($health.Reason) { [void]$lines.Add("             $($health.Reason)") }
        if (-not $health.Healthy -and $health.Until -ne [datetime]::MaxValue -and $health.Until -ne [datetime]::MinValue) { [void]$lines.Add(("             Next automatic retry after {0}" -f $health.Until.ToString("HH:mm"))) }
    }
    [void]$lines.Add("")
    [void]$lines.Add(("Leader: {0}   Workload: Antigravity {1}% / Codex {2}%" -f $ConfiguredLeader, $AntigravityShare, $CodexShare))
    [void]$lines.Add("Codex file access: $(if ($script:runtime.CodexSandbox -eq 'workspace-write') { 'can edit project files' } else { 'read-only' })")
    [void]$lines.Add("")
    [void]$lines.Add("Checked again just now. Use :agy or :codex to run the last request with that leader.")
    @($lines)
}

function Save-AgexLastProject {
    try {
        $preferences = Get-AgexPreferences -Path $settingsPath
        $preferences.last_project = $Project
        Save-AgexPreferences -Preferences $preferences -Path $settingsPath
    } catch { Write-AgexLog -Runtime $script:runtime -Event 'settings_warning' -Data @{ error = $_.Exception.Message } }
}

function Invoke-AgexCommand {
    # Returns @{ View; ViewTitle; Lines; Message; MessageColor; Quit; Run; RunLeader; ConfirmQuit }
    param([Parameter(Mandatory)][string]$Command)
    $result = @{ View = $null; ViewTitle = ""; Lines = @(); Message = ""; MessageColor = "Gray"; Quit = $false; Run = $null; RunLeader = $null; ClearDraft = $true }
    $trimmed = $Command.Trim()
    $parts = @($trimmed -split '\s+')
    $name = $parts[0].ToLowerInvariant()
    $argText = if ($trimmed.Length -gt $parts[0].Length) { $trimmed.Substring($parts[0].Length).Trim().Trim('"') } else { "" }
    $running = [bool]$script:activeExecution
    switch -Regex ($name) {
        '^:(help|h|\?)$' { $result.View = "HELP"; $result.ViewTitle = "Help"; $result.Lines = Get-AgexHelpLines }
        '^:paste$' { $result.Message = "Paste your text, then press Ctrl+Enter (or type :send on its own line) to send." }
        '^:(quit|q|exit)$' { $result.Quit = $true }
        '^:cancel$' { if ($running) { Request-AgexTaskCancellation; $result.Message = "Cancelling the current request..." } else { $result.Message = "Nothing is running. Draft cleared." } }
        '^:send$' { $result.Message = "Type your request first, then :send on the next line." }
        '^:clear$' {
            if ($running) { $result.Message = "A request is running. Use :cancel first."; $result.MessageColor = "Yellow"; break }
            [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
            try { $script:ui.Events.Clear(); $script:ui.Tasks.Clear(); $script:ui.Agents.Clear() } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
            $script:ui.Outcome = $null; $script:ui.Result = ""; $script:ui.Status = "IDLE"; $script:ui.WorkId = ""
            $result.View = "NORMAL"; $result.Message = "Cleared."
        }
        '^:details$' { $result.View = "DETAILS"; $result.ViewTitle = "Details"; $result.Lines = Get-AgexDetailsLines }
        '^:log$' {
            $count = 40; $parsed = 0
            if ($argText -and [int]::TryParse($argText, [ref]$parsed) -and $parsed -gt 0) { $count = [math]::Min(500, $parsed) }
            $result.View = "LOG"; $result.ViewTitle = "Activity log (last $count)"; $result.Lines = Get-AgexLogLines -Count $count
        }
        '^:result$' {
            $text = if ($script:ui.Result) { [string]$script:ui.Result } else { "No result yet." }
            $result.View = "RESULT"; $result.ViewTitle = "Result"; $result.Lines = @($text -split "`r?`n")
        }
        '^:agents$' {
            if ($running) { $result.View = "AGENTS"; $result.ViewTitle = "Agents"; $result.Lines = Get-AgexAgentsLines; break }
            [void](Invoke-AgexPrecheck)
            $result.View = "AGENTS"; $result.ViewTitle = "Agents"; $result.Lines = Get-AgexAgentsLines
        }
        '^:(retry|codex|agy|antigravity)$' {
            if ($running) { $result.Message = "A request is already running. Wait, or use :cancel."; $result.MessageColor = "Yellow"; break }
            if (-not $script:lastPrompt) { $result.Message = "Nothing to retry yet. Type a request first."; $result.MessageColor = "Yellow"; break }
            $leader = switch ($name) { ':codex' { 'Codex' } ':retry' { $script:lastLeaderOverride } default { 'Antigravity' } }
            if ($leader) {
                # An explicit choice clears the cooldown for that agent.
                $health = Get-AgexAgentHealth -Runtime $script:runtime -Agent $leader
                if (-not $health.Healthy -and $health.Until -ne [datetime]::MaxValue) { Set-AgexAgentHealth -Runtime $script:runtime -Agent $leader -Healthy $true -Reason 'Retry requested by user.' }
            } else {
                foreach ($agent in @('Codex', 'Antigravity')) { $health = Get-AgexAgentHealth -Runtime $script:runtime -Agent $agent; if (-not $health.Healthy -and $health.Until -ne [datetime]::MaxValue) { Set-AgexAgentHealth -Runtime $script:runtime -Agent $agent -Healthy $true -Reason 'Retry requested by user.' } }
            }
            $result.Run = $script:lastPrompt; $result.RunLeader = $leader; $result.View = "NORMAL"
        }
        '^:leader$' {
            if ($parts.Count -lt 2 -or @("Codex", "Antigravity", "Auto") -notcontains $parts[1]) { $result.Message = "Use :leader Codex, :leader Antigravity or :leader Auto"; $result.MessageColor = "Yellow"; break }
            $script:ConfiguredLeader = (Get-Culture).TextInfo.ToTitleCase($parts[1].ToLowerInvariant())
            $script:ResolvedLeader = Resolve-AgexLeader -ConfiguredLeader $script:ConfiguredLeader -CodexShare $script:CodexShare -AntigravityShare $script:AntigravityShare
            $script:ActiveLeader = $script:ResolvedLeader
            $script:ui.ConfiguredLeader = $script:ConfiguredLeader; $script:ui.ResolvedLeader = $script:ResolvedLeader
            $result.Message = "Leader set to $($script:ConfiguredLeader)."
        }
        '^:workload$' {
            $c = 0; $a = 0
            if ($parts.Count -ge 3 -and [int]::TryParse($parts[1], [ref]$c) -and [int]::TryParse($parts[2], [ref]$a) -and $c -ge 0 -and $a -ge 0 -and $c + $a -eq 100) {
                $script:CodexShare = $c; $script:AntigravityShare = $a
                $script:ui.CodexShare = $c; $script:ui.AntigravityShare = $a
                $script:ResolvedLeader = Resolve-AgexLeader -ConfiguredLeader $ConfiguredLeader -CodexShare $c -AntigravityShare $a
                $result.Message = "Workload set: Antigravity $a% / Codex $c%."
            } else { $result.Message = "Use :workload CODEX_PERCENT AGY_PERCENT (they must add up to 100)."; $result.MessageColor = "Yellow" }
        }
        '^:model$' {
            if ($parts.Count -eq 1) { $result.Message = "Codex: $(if ($CodexModel) { $CodexModel } else { 'default' }) / $(if ($CodexEffort) { $CodexEffort } else { 'default' })   Antigravity: $(if ($AntigravityModel) { $AntigravityModel } else { 'default' }) / $(if ($AntigravityEffort) { $AntigravityEffort } else { 'default' })" }
            elseif ($parts.Count -ge 4 -and $parts[1].ToLowerInvariant() -eq "codex") { $script:CodexModel = $parts[2]; $script:CodexEffort = $parts[3]; $script:ui.CodexModel = $parts[2]; $result.Message = "Codex model set." }
            elseif ($parts.Count -ge 4 -and @("agy", "antigravity") -contains $parts[1].ToLowerInvariant()) { $script:AntigravityModel = $parts[2]; $script:AntigravityEffort = $parts[3]; $script:ui.AntigravityModel = $parts[2]; $result.Message = "Antigravity model set." }
            else { $result.Message = "Use :model codex MODEL EFFORT or :model agy MODEL EFFORT"; $result.MessageColor = "Yellow" }
        }
        '^:project$' {
            if (-not $argText) { $result.Message = "Project: $Project   (change with :project C:\path\to\folder)"; break }
            if ($running) { $result.Message = "A request is running. Change the project after it finishes."; $result.MessageColor = "Yellow"; break }
            $candidate = [Environment]::ExpandEnvironmentVariables($argText)
            if (Test-Path -LiteralPath $candidate -PathType Container) {
                $script:Project = (Resolve-Path -LiteralPath $candidate).Path
                $script:ui.Project = $script:Project; $script:ui.ProjectName = Split-Path -Leaf $script:Project
                Save-AgexLastProject
                $result.Message = "Project changed to $($script:ui.ProjectName)."; $result.MessageColor = "Green"
            } else { $result.Message = "Project folder was not found. Choose another folder."; $result.MessageColor = "Yellow" }
        }
        '^:status$' { $result.View = "AGENTS"; $result.ViewTitle = "Status"; $result.Lines = Get-AgexAgentsLines }
        '^:report$' {
            $report = Get-AgexExecutionReport -TelemetryRoot $telemetryRoot -SessionId $SessionId -CodexShare $CodexShare -AntigravityShare $AntigravityShare
            $result.View = "INFO"; $result.ViewTitle = "Execution report"; $result.Lines = if ($report) { @($report.Text -split "`n") } else { @("No completed execution yet.") }
        }
        '^:history$' {
            $result.View = "INFO"; $result.ViewTitle = "History"
            $result.Lines = if ($script:history.Count) { @($script:history | ForEach-Object { "{0}: {1}" -f $_.Role, $_.Text }) } else { @("No history yet.") }
        }
        '^:(changes|files)$' {
            Update-AgexChanges -State $script:ui
            $changes = if ($null -ne $script:ui.GitChanges) { @($script:ui.GitChanges) } else { @($script:ui.Files.Values) }
            $result.View = "INFO"; $result.ViewTitle = "Changes"; $result.Lines = if ($changes.Count) { @($changes | ForEach-Object { "{0} {1} {2}" -f $_.Action, $_.Path, $_.Lines }) } else { @("No project changes observed.") }
        }
        '^:diff$' {
            Update-AgexChanges -State $script:ui -IncludeDiff
            $result.View = "INFO"; $result.ViewTitle = "Diff (excerpt)"; $result.Lines = @($script:ui.DiffLines)
        }
        '^:chat$' {
            $chat = @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Chat)
            $result.View = "INFO"; $result.ViewTitle = "Agent messages"; $result.Lines = if ($chat.Count) { @($chat | ForEach-Object { "{0} -> {1} {2} [{3}]: {4}" -f $_.From, $_.To, $_.Type, $_.Status, $_.PreviewBody }) } else { @("No messages between agents.") }
        }
        '^:tasks$' { $result.View = "DETAILS"; $result.ViewTitle = "Details"; $result.Lines = Get-AgexDetailsLines }
        default { $result.Message = "Unknown command $name. Type :help."; $result.MessageColor = "Yellow" }
    }
    $result
}

# ------------------------------------------------------ interactive session

function Save-AgexConsoleState {
    $state = @{}
    try { $state.CursorVisible = [Console]::CursorVisible } catch { $state.CursorVisible = $true }
    try { $state.Foreground = [Console]::ForegroundColor } catch { }
    try { $state.Background = [Console]::BackgroundColor } catch { }
    try { $state.TreatControlC = [Console]::TreatControlCAsInput } catch { }
    try { $state.OutputEncoding = [Console]::OutputEncoding } catch { }
    $state
}

function Restore-AgexConsole {
    $saved = $script:consoleState
    if (-not $saved) { return }
    try { [Console]::Write(([char]27) + "[?2004l") } catch { }
    try { if ($null -ne $saved.Foreground) { [Console]::ForegroundColor = $saved.Foreground } } catch { }
    try { if ($null -ne $saved.Background) { [Console]::BackgroundColor = $saved.Background } } catch { }
    try { if ($null -ne $saved.TreatControlC) { [Console]::TreatControlCAsInput = $saved.TreatControlC } } catch { }
    try { if ($saved.OutputEncoding) { [Console]::OutputEncoding = $saved.OutputEncoding } } catch { }
    try { [Console]::CursorVisible = $true } catch { }
    $script:consoleState = $null
}

function Set-AgexMessage {
    param([string]$Text, [string]$Color = "Gray", [int]$Seconds = 8)
    $script:screenMessage = @{ Text = $Text; Color = $Color; Until = (Get-Date).AddSeconds($Seconds) }
}

function Open-AgexView {
    param([string]$View, [string]$Title, [string[]]$Lines)
    $script:view = @{ Name = $View; Title = $Title; Lines = @($Lines); Scroll = 0 }
}

function Submit-AgexPrompt {
    param([Parameter(Mandatory)][string]$Prompt, [string]$Leader)
    if ($script:activeExecution) {
        if ($script:queuedPrompts.Count -ge 8) { Set-AgexMessage -Text "Queue is full (8). Wait for the current request." -Color Yellow; return }
        $script:queuedPrompts.Enqueue($Prompt)
        Set-AgexMessage -Text ("Queued. It will start after the current request ({0} waiting)." -f $script:queuedPrompts.Count) -Color Cyan
        return
    }
    if ($script:history.Count -ge 50) { $script:history.RemoveAt(0) }
    [void]$script:history.Add([pscustomobject]@{ Role = "USER"; Text = $Prompt })
    $script:view = @{ Name = "NORMAL"; Title = ""; Lines = @(); Scroll = 0 }
    if (Start-AgexTaskExecution -Prompt $Prompt -LeaderOverride $Leader) { Set-AgexMessage -Text "Sent. You can keep typing; Ctrl+C cancels." -Color Cyan -Seconds 5 }
}

function Invoke-AgexConfirmKey {
    param([Parameter(Mandatory)][ConsoleKeyInfo]$Key)
    $confirm = $script:pendingConfirm
    $script:pendingConfirm = $null
    $yes = $Key.Key -eq [ConsoleKey]::Y -or $Key.Key -eq [ConsoleKey]::Enter
    if (-not $yes) { Set-AgexMessage -Text "OK, nothing changed." -Seconds 3; return }
    switch ($confirm.Kind) {
        "cancel" { if ($script:activeExecution) { Request-AgexTaskCancellation; Set-AgexMessage -Text "Cancelling the current request..." -Color Yellow } }
        "quit" { $script:quitRequested = $true }
    }
}

function Invoke-AgexKey {
    param([Parameter(Mandatory)][ConsoleKeyInfo]$Key)
    $editor = $script:editor
    $ctrl = (($Key.Modifiers -band [ConsoleModifiers]::Control) -ne 0)
    if ($script:pendingConfirm) { Invoke-AgexConfirmKey -Key $Key; return }
    if ($ctrl -and $Key.Key -eq [ConsoleKey]::C) {
        if ($script:activeExecution) { $script:pendingConfirm = @{ Kind = "cancel"; Text = "Cancel the current request? Press Y for yes, N for no." } }
        elseif ((Get-AgexEditorCharCount -Editor $editor) -gt 0) { Clear-AgexEditor -Editor $editor; Set-AgexMessage -Text "Draft cleared. Press Ctrl+C again to quit." -Seconds 5 }
        else { $script:pendingConfirm = @{ Kind = "quit"; Text = "Quit AGEX? Press Y for yes, N for no." } }
        return
    }
    if ($Key.Key -eq [ConsoleKey]::Escape) {
        if (Read-AgexBracketedPaste -Editor $editor) { return }
        if ($script:view.Name -ne "NORMAL") { $script:view = @{ Name = "NORMAL"; Title = ""; Lines = @(); Scroll = 0 } }
        else { $script:screenMessage = $null }
        return
    }
    if ($Key.Key -eq [ConsoleKey]::PageUp) { if ($script:view.Name -ne "NORMAL") { $script:view.Scroll = [math]::Max(0, $script:view.Scroll - 10) }; return }
    if ($Key.Key -eq [ConsoleKey]::PageDown) { if ($script:view.Name -ne "NORMAL") { $script:view.Scroll += 10 }; return }
    if ($Key.Key -eq [ConsoleKey]::F1) { Open-AgexView -View "HELP" -Title "Help" -Lines (Get-AgexHelpLines); return }
    $send = ($Key.Key -eq [ConsoleKey]::Enter -and $ctrl) -or ($ctrl -and $Key.Key -eq [ConsoleKey]::S)
    if ($send) {
        $text = (Get-AgexEditorText -Editor $editor).Trim()
        if (-not $text) { return }
        if ($editor.Truncated) { Set-AgexMessage -Text "Draft is longer than 262144 characters. Shorten it before sending." -Color Yellow; return }
        if ($text -notmatch "`n" -and (Test-AgexCommandLine -Value $text)) { Invoke-AgexCommandText -Text $text; return }
        Clear-AgexEditor -Editor $editor
        Submit-AgexPrompt -Prompt $text
        return
    }
    if ($Key.Key -eq [ConsoleKey]::Enter) {
        $text = Get-AgexEditorText -Editor $editor
        $lastLine = $editor.Lines[$editor.Lines.Count - 1].Trim()
        if ($editor.Lines.Count -gt 1 -and $lastLine -eq ":send" -and $editor.Line -eq $editor.Lines.Count - 1) {
            $prompt = (($editor.Lines | Select-Object -First ($editor.Lines.Count - 1)) -join "`n").Trim()
            if ($prompt) { Clear-AgexEditor -Editor $editor; Submit-AgexPrompt -Prompt $prompt }
            return
        }
        if ($lastLine -eq ":cancel" -and $editor.Lines.Count -gt 1) { Clear-AgexEditor -Editor $editor; Set-AgexMessage -Text "Draft discarded." -Seconds 4; return }
        if ($editor.Lines.Count -eq 1 -and (Test-AgexCommandLine -Value $text.Trim())) { Invoke-AgexCommandText -Text $text.Trim(); return }
        Add-AgexEditorText -Editor $editor -Text "`n"
        return
    }
    if ($Key.Key -eq [ConsoleKey]::Backspace) { Remove-AgexEditorText -Editor $editor -Word:$ctrl; return }
    if ($Key.Key -eq [ConsoleKey]::Delete) { Remove-AgexEditorText -Editor $editor -Forward; return }
    if ($Key.Key -eq [ConsoleKey]::LeftArrow) { Move-AgexEditorCursor -Editor $editor -Direction $(if ($ctrl) { "WordLeft" } else { "Left" }); return }
    if ($Key.Key -eq [ConsoleKey]::RightArrow) { Move-AgexEditorCursor -Editor $editor -Direction $(if ($ctrl) { "WordRight" } else { "Right" }); return }
    if ($Key.Key -eq [ConsoleKey]::UpArrow) { Move-AgexEditorCursor -Editor $editor -Direction Up; return }
    if ($Key.Key -eq [ConsoleKey]::DownArrow) { Move-AgexEditorCursor -Editor $editor -Direction Down; return }
    if ($Key.Key -eq [ConsoleKey]::Home) { Move-AgexEditorCursor -Editor $editor -Direction $(if ($ctrl) { "Top" } else { "Home" }); return }
    if ($Key.Key -eq [ConsoleKey]::End) { Move-AgexEditorCursor -Editor $editor -Direction $(if ($ctrl) { "Bottom" } else { "End" }); return }
    if ($ctrl -and $Key.Key -eq [ConsoleKey]::U) { Clear-AgexEditor -Editor $editor; return }
    if ($ctrl -and $Key.Key -eq [ConsoleKey]::L) { $script:screen.ForceFull = $true; return }
    if ($ctrl -and $Key.Key -eq [ConsoleKey]::K) {
        $line = $editor.Lines[$editor.Line]
        $editor.Lines[$editor.Line] = $line.Substring(0, $editor.Column); $editor.Version++
        return
    }
    if ($ctrl) { return }
    if ($Key.KeyChar -ne [char]0 -and -not [char]::IsControl($Key.KeyChar)) { Add-AgexEditorText -Editor $editor -Text ([string]$Key.KeyChar) }
}

function Test-AgexKeyAvailable {
    try { [Console]::KeyAvailable } catch { $false }
}

function Read-AgexBracketedPaste {
    # Windows Terminal sends ESC [200~ ... ESC [201~ around pasted text.
    param([Parameter(Mandatory)]$Editor)
    $deadline = [DateTime]::UtcNow.AddMilliseconds(40)
    while (-not (Test-AgexKeyAvailable)) { if ([DateTime]::UtcNow -ge $deadline) { return $false }; Start-Sleep -Milliseconds 2 }
    $prefix = [System.Text.StringBuilder]::new()
    foreach ($expected in @('[', '2', '0', '0', '~')) {
        $wait = [DateTime]::UtcNow.AddMilliseconds(40)
        while (-not (Test-AgexKeyAvailable)) { if ([DateTime]::UtcNow -ge $wait) { break }; Start-Sleep -Milliseconds 2 }
        if (-not (Test-AgexKeyAvailable)) { break }
        $next = [Console]::ReadKey($true)
        [void]$prefix.Append($next.KeyChar)
        if ($next.KeyChar -ne $expected) { break }
    }
    if ($prefix.ToString() -ne '[200~') {
        # Not a paste: keep any plain characters that were typed after Esc.
        # Not a paste: keep the characters typed after Esc; Esc still acts.
        $plain = ($prefix.ToString() -replace '[^\p{L}\p{N}\p{P}\p{S} ]', '')
        if ($plain -and $plain -ne '[') { Add-AgexEditorText -Editor $Editor -Text $plain }
        return $false
    }
    $payload = [System.Text.StringBuilder]::new()
    $end = ([char]27) + '[201~'
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Test-AgexKeyAvailable)) { Start-Sleep -Milliseconds 2; continue }
        $next = [Console]::ReadKey($true)
        $char = if ($next.Key -eq [ConsoleKey]::Enter) { "`n" } else { [string]$next.KeyChar }
        [void]$payload.Append($char)
        if ($payload.Length -ge $end.Length -and $payload.ToString($payload.Length - $end.Length, $end.Length) -eq $end) { $payload.Length -= $end.Length; break }
    }
    Add-AgexEditorText -Editor $Editor -Text $payload.ToString()
    Set-AgexMessage -Text ("Pasted {0} characters. Press Ctrl+Enter to send." -f $payload.Length) -Seconds 5
    $true
}

function Invoke-AgexCommandText {
    param([Parameter(Mandatory)][string]$Text)
    $result = Invoke-AgexCommand -Command $Text
    if ($result.ClearDraft) { Clear-AgexEditor -Editor $script:editor }
    if ($result.View) {
        if ($result.View -eq "NORMAL") { $script:view = @{ Name = "NORMAL"; Title = ""; Lines = @(); Scroll = 0 } }
        else { Open-AgexView -View $result.View -Title $result.ViewTitle -Lines $result.Lines }
    }
    if ($result.Message) { Set-AgexMessage -Text $result.Message -Color $result.MessageColor }
    if ($result.Quit) {
        if ($script:activeExecution) { $script:pendingConfirm = @{ Kind = "quit"; Text = "A request is running. Cancel it and quit? Press Y for yes, N for no." } }
        else { $script:quitRequested = $true }
    }
    if ($result.Run) { Submit-AgexPrompt -Prompt $result.Run -Leader $result.RunLeader }
}

function Update-AgexScreen {
    param([switch]$Force)
    $width = [Console]::WindowWidth
    $height = [Console]::WindowHeight
    if ($width -ne $script:screen.Width -or $height -ne $script:screen.Height -or $script:screen.ForceFull) {
        # Only a real resize (or Ctrl+L) clears the terminal.
        $script:screen.Width = $width; $script:screen.Height = $height; $script:screen.ForceFull = $false
        $script:screen.Prev = @{}
        $script:screen.LastRowCount = 0
        try { [Console]::Clear() } catch { }
        if ($script:view.Name -eq "DETAILS") { $script:view.Lines = Get-AgexDetailsLines }
    }
    if ($script:screenMessage -and (Get-Date) -gt $script:screenMessage.Until) { $script:screenMessage = $null }
    Receive-AgexUiUpdates -State $script:ui
    Pump-AgexUiFileWatch -State $script:ui
    $snapshot = New-AgexUiRenderSnapshot -State $script:ui
    $frame = Get-AgexScreenFrame -State $snapshot -Runtime $script:runtime -Editor $script:editor -View $script:view -Message $script:screenMessage -Confirm $script:pendingConfirm -Width $width -Height $height
    Write-AgexScreen -Screen $script:screen -Frame $frame
}

function Start-AgexInteractive {
    $script:agexInteractive = $true
    $script:consoleState = Save-AgexConsoleState
    $script:editor = New-AgexEditor
    $script:view = @{ Name = "NORMAL"; Title = ""; Lines = @(); Scroll = 0 }
    $script:screen = New-AgexScreen
    $script:screenMessage = $null
    $script:pendingConfirm = $null
    $script:quitRequested = $false
    $renderFailures = 0
    try {
        try { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false) } catch { }
        $script:screen.Unicode = ([Console]::OutputEncoding.CodePage -eq 65001)
        $script:ui.Unicode = $script:screen.Unicode
        [Console]::TreatControlCAsInput = $true
        if (-not $SkipPrecheck) { Show-AgexStartupCheck }
        [Console]::Write(([char]27) + "[?2004h")
        Set-AgexMessage -Text "Type your request below. Ctrl+Enter sends. Type :help for help." -Seconds 12
        $lastRender = [datetime]::MinValue
        while (-not $script:quitRequested) {
            try { Pump-AgexTaskExecution } catch { Set-AgexMessage -Text ("Execution error: {0}" -f (Protect-AgexTelemetryText -Text $_.Exception.Message)) -Color Red }
            $handled = 0
            while ((Test-AgexKeyAvailable) -and $handled -lt 400 -and -not $script:quitRequested) {
                $key = [Console]::ReadKey($true)
                try { Invoke-AgexKey -Key $key } catch { Set-AgexMessage -Text ("Input error: {0}" -f $_.Exception.Message) -Color Red }
                $handled++
            }
            if ($script:quitRequested) { break }
            $now = Get-Date
            if ($handled -gt 0 -or ($now - $lastRender).TotalMilliseconds -ge 200) {
                try { Update-AgexScreen; $renderFailures = 0 }
                catch {
                    # A rendering failure must never stop execution; redraw fully next time.
                    $renderFailures++
                    $script:screen.ForceFull = $true
                    Write-AgexLog -Runtime $script:runtime -Event 'render_warning' -Data @{ error = $_.Exception.Message; count = $renderFailures }
                    if ($renderFailures -ge 20) { Start-Sleep -Milliseconds 500 }
                }
                $lastRender = $now
            }
            if ($handled -eq 0) { Start-Sleep -Milliseconds 25 }
        }
    } finally {
        Stop-AgexSessionWork
        Restore-AgexConsole
        try {
            [Console]::ForegroundColor = [ConsoleColor]::Gray
            [Console]::SetCursorPosition(0, [math]::Max(0, [Console]::WindowTop + [Console]::WindowHeight - 1))
            [Console]::WriteLine()
        } catch { }
        Write-Host "AGEX closed. Session log: $($script:runtime.LogPath)" -ForegroundColor DarkGray
        Write-AgexLog -Runtime $script:runtime -Event 'session_end' -Data @{}
    }
}

function Show-AgexStartupCheck {
    $ok = if ($script:screen.Unicode) { [string][char]0x2713 } else { "OK" }
    $warn = if ($script:screen.Unicode) { [string][char]0x26A0 } else { "!" }
    try { [Console]::Clear() } catch { }
    Write-Host "AGEX AI CONTROL CENTER" -ForegroundColor Cyan
    Write-Host "Checking agents..."
    $check = Invoke-AgexPrecheck
    foreach ($name in @("Codex", "Antigravity")) {
        $health = $check.$name
        if ($health.Healthy) { Write-Host ("  {0,-12} {1} ready" -f $name, $ok) -ForegroundColor Green }
        else { Write-Host ("  {0,-12} {1} unavailable" -f $name, $warn) -ForegroundColor Yellow }
    }
    Write-Host ("  {0,-12} {1} {2}" -f "Project", $ok, (Split-Path -Leaf $Project)) -ForegroundColor Green
    if (-not $check.Antigravity.Healthy -and $check.Codex.Healthy) { Write-Host "AGEX can still run with Codex." -ForegroundColor Yellow }
    elseif ($check.Antigravity.Healthy -and -not $check.Codex.Healthy) { Write-Host "AGEX can still run with Antigravity." -ForegroundColor Yellow }
    elseif (-not $check.Antigravity.Healthy -and -not $check.Codex.Healthy) { Write-Host "No agent is available. Type :agents after fixing them." -ForegroundColor Red }
    Write-Host "Ready."
}

function Start-AgexLineMode {
    # Redirected input/output (scripts, CI): one request per line, plain text.
    while ($true) {
        $line = [Console]::In.ReadLine()
        if ($null -eq $line) { break }
        $line = $line.Trim()
        if (-not $line) { continue }
        if (Test-AgexCommandLine -Value $line) {
            $result = Invoke-AgexCommand -Command $line
            foreach ($item in @($result.Lines)) { Write-Output $item }
            if ($result.Message) { Write-Output $result.Message }
            if ($result.Quit) { break }
            if (-not $result.Run) { continue }
            $line = $result.Run
        }
        [void](Start-AgexTaskExecution -Prompt $line)
        $printed = 0
        while ($script:activeExecution) {
            Pump-AgexTaskExecution
            Write-AgexActivityLines -State $script:ui -LastSeq ([ref]$printed)
            Start-Sleep -Milliseconds 250
        }
        Pump-AgexTaskExecution
        Write-AgexActivityLines -State $script:ui -LastSeq ([ref]$printed)
        $outcome = $script:ui.Outcome
        if ($outcome) {
            Write-Output ("STATUS: {0}" -f $outcome.Status)
            Write-Output $outcome.Headline
            Write-Output ("Tasks: {0} | Done: {1} | Failed: {2} | Cancelled: {3}" -f $outcome.Tasks, $outcome.Done, $outcome.Failed, $outcome.Cancelled)
        }
        if ($script:ui.Result) { Write-Output $script:ui.Result }
    }
    Stop-AgexSessionWork
}

# ------------------------------------------------------------- serve mode
# The desktop app runs this engine as a child process. Protocol: one JSON
# object per line. Commands arrive on stdin; events go to stdout. Nothing else
# is ever written to stdout in this mode.

function Send-AgexServeEvent {
    param([Parameter(Mandatory)][hashtable]$Event)
    $json = ConvertTo-Json -InputObject $Event -Compress -Depth 10
    [System.Threading.Monitor]::Enter($script:serveOutSync)
    try { $script:serveOut.WriteLine($json); $script:serveOut.Flush() } finally { [System.Threading.Monitor]::Exit($script:serveOutSync) }
}

function ConvertTo-AgexIso {
    param($Value)
    if ($null -eq $Value) { return "" }
    if ($Value -is [datetime]) { if ($Value -eq [datetime]::MinValue) { return "" }; return $Value.ToUniversalTime().ToString("o") }
    [string]$Value
}

function Get-AgexServeAgents {
    @(Get-AgexAdapters | ForEach-Object {
        $health = Get-AgexAgentHealth -Runtime $script:runtime -Agent $_.Name
        [ordered]@{
            id = $_.Id; name = $_.Name; provider = $_.Provider
            enabled = (@($script:preferences.enabled_agents) -contains $_.Id)
            checked = [bool]$health.Checked; healthy = [bool]$health.Healthy; reason = [string]$health.Reason
            canWrite = [bool](Test-AgexAgentCanWrite -Agent $_.Name)
            capabilities = @($_.Capabilities)
        }
    })
}

function Get-AgexAgentActivity {
    # Per-agent live state for the side panel and the graph.
    $tasks = @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks)
    $agentsRaw = @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Agents)
    $out = [ordered]@{}
    foreach ($adapter in Get-AgexAdapters) {
        $mine = @($tasks | Where-Object Agent -eq $adapter.Name)
        $runningTasks = @($mine | Where-Object Status -in @('STARTING', 'RUNNING', 'VERIFYING'))
        # Nothing can be running once the request has ended, whatever the last snapshot said.
        $runningAgents = if ($script:activeExecution) { @($agentsRaw | Where-Object { ([string]$_.Executor) -eq $adapter.Name.ToUpperInvariant() -and $_.Status -in @('STARTING', 'RUNNING', 'WAITING', 'WARNING') }) } else { @() }
        $state = if ($runningTasks.Count -or $runningAgents.Count) { "running" }
            elseif (@($mine | Where-Object Status -in @('QUEUED', 'BLOCKED')).Count) { "waiting" }
            elseif (@($mine | Where-Object Status -in @('FAILED', 'REPAIR REQUIRED')).Count -and -not @($mine | Where-Object Status -eq 'DONE').Count) { "failed" }
            elseif (@($mine | Where-Object Status -eq 'DONE').Count -or @($agentsRaw | Where-Object { ([string]$_.Executor) -eq $adapter.Name.ToUpperInvariant() -and $_.Status -eq 'DONE' }).Count) { "done" }
            else { "idle" }
        $current = if ($runningTasks.Count) { [string]$runningTasks[0].Summary } elseif ($runningAgents.Count) { [string]$runningAgents[0].Action } else { "" }
        $out[$adapter.Name] = [ordered]@{ state = $state; current = $current; tasks = $mine.Count; done = @($mine | Where-Object Status -eq 'DONE').Count }
    }
    $out
}

function Get-AgexServeState {
    $snapshot = New-AgexUiRenderSnapshot -State $script:ui
    $outcome = $null
    if ($snapshot.Outcome) {
        $o = $snapshot.Outcome
        $outcome = [ordered]@{
            status = [string]$o.Status; headline = [string]$o.Headline; reason = [string]$o.Reason; verification = [string]$o.Verification
            tasks = [int]$o.Tasks; done = [int]$o.Done; failed = [int]$o.Failed; cancelled = [int]$o.Cancelled
            codexRuns = [int]$o.Executors.Codex; antigravityRuns = [int]$o.Executors.Antigravity
            primaryFailure = [string]$o.PrimaryFailure; whatHappened = @($o.WhatHappened); taskResults = @($o.TaskResults)
            durationSeconds = [double]$o.DurationSeconds
        }
    }
    $running = [bool]$script:activeExecution
    [ordered]@{
        type = "state"
        status = $(if ($snapshot.Status) { [string]$snapshot.Status } else { "IDLE" })
        running = $running
        stage = [string]$snapshot.AcceptanceStage
        project = [string]$snapshot.Project; projectName = [string]$snapshot.ProjectName
        leader = [string]$snapshot.ResolvedLeader; configuredLeader = [string]$snapshot.ConfiguredLeader
        codexShare = [int]$CodexShare; antigravityShare = [int]$AntigravityShare
        requestId = [string]$snapshot.WorkId; request = [string]$snapshot.Prompt
        requestStarted = (ConvertTo-AgexIso $snapshot.RequestStarted)
        elapsedSeconds = $(if ($running -and $snapshot.RequestStarted -ne [datetime]::MinValue) { [int]((Get-Date) - $snapshot.RequestStarted).TotalSeconds } else { 0 })
        current = (Get-AgexCurrentTaskText -State $snapshot)
        workers = @($snapshot.Tasks | Where-Object Status -in @('STARTING', 'RUNNING', 'VERIFYING', 'WAITING')).Count
        tasks = @($snapshot.Tasks | ForEach-Object { [ordered]@{ id = [string]$_.Id; title = [string]$_.Summary; description = [string]$_.Task; agent = [string]$_.Agent; status = [string]$_.Status; error = [string]$_.Error; dependencies = @($_.Dependencies | ForEach-Object { [string]$_ }); started = (ConvertTo-AgexIso $_.Started); ended = (ConvertTo-AgexIso $_.End) } })
        agents = @(Get-AgexServeAgents)
        agentActivity = (Get-AgexAgentActivity)
        outcome = $outcome
        result = [string]$snapshot.Result
        queued = $(if ($script:queuedPrompts) { $script:queuedPrompts.Count } else { 0 })
    }
}

function Get-AgexServeChanges {
    Update-AgexChanges -State $script:ui
    $items = [System.Collections.Generic.List[object]]::new()
    $git = $null -ne $script:ui.GitChanges
    if ($git) {
        $numstat = @{}
        foreach ($line in @(& git -C $Project diff --numstat HEAD 2>$null)) { $parts = $line -split "`t", 3; if ($parts.Count -eq 3) { $numstat[$parts[2]] = @($parts[0], $parts[1]) } }
        foreach ($change in @($script:ui.GitChanges)) {
            $path = [string]$change.Path
            $added = ""; $removed = ""
            if ($numstat.ContainsKey($path)) { $added = $numstat[$path][0]; $removed = $numstat[$path][1] }
            elseif ($change.Action -eq 'A') { try { $full = Join-Path $Project $path; if (Test-Path -LiteralPath $full -PathType Leaf) { $added = [string]@(Get-Content -LiteralPath $full -ErrorAction Stop).Count; $removed = "0" } } catch { } }
            [void]$items.Add([ordered]@{ action = [string]$change.Action; path = $path; added = $added; removed = $removed })
        }
    } else {
        foreach ($file in @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Files)) { [void]$items.Add([ordered]@{ action = [string]$file.Action; path = [string]$file.Path; added = ""; removed = "" }) }
    }
    [ordered]@{ type = "changes"; git = $git; project = $Project; items = @($items) }
}

function Get-AgexServeDiff {
    param([Parameter(Mandatory)][string]$Path)
    $base = [IO.Path]::GetFullPath($Project).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath((Join-Path $Project $Path))
    if (-not $full.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { return [ordered]@{ type = "diff"; path = $Path; text = "Path is outside the project." } }
    $text = ""
    if ($null -ne $script:ui.GitChanges -or (Test-Path -LiteralPath (Join-Path $Project ".git"))) {
        $text = (@(& git -C $Project --no-pager diff --no-color --no-ext-diff HEAD -- $Path 2>$null) -join "`n")
        if (-not $text -and (Test-Path -LiteralPath $full -PathType Leaf)) { $text = "New file`n" + ((@(Get-Content -LiteralPath $full -TotalCount 2000 -ErrorAction SilentlyContinue) | ForEach-Object { "+" + $_ }) -join "`n") }
    } elseif (Test-Path -LiteralPath $full -PathType Leaf) {
        $text = "No Git repository: showing current content.`n" + ((@(Get-Content -LiteralPath $full -TotalCount 2000 -ErrorAction SilentlyContinue)) -join "`n")
    }
    if (-not $text) { $text = "No changes to show (file deleted or unchanged)." }
    if ($text.Length -gt 400000) { $text = $text.Substring(0, 400000) + "`n[truncated]" }
    [ordered]@{ type = "diff"; path = $Path; text = $text }
}

function Set-AgexServeSettings {
    param([Parameter(Mandatory)]$Values)
    foreach ($property in $Values.PSObject.Properties) {
        if ($script:preferences.PSObject.Properties[$property.Name]) { $script:preferences.($property.Name) = $property.Value }
    }
    Save-AgexPreferences -Preferences $script:preferences
    $script:preferences = Get-AgexPreferences
    if ($Values.PSObject.Properties['start_with_windows']) { try { . (Join-Path $PSScriptRoot "agex-maintenance.ps1"); Set-AgexStartWithWindows -Enabled ([bool]$script:preferences.start_with_windows) } catch { } }
    Apply-AgexPreferences
}

function Apply-AgexPreferences {
    # Settings take effect for the next request; the running one is not changed.
    $script:ConfiguredLeader = [string]$script:preferences.leader
    $shares = Get-AgexStrategyShares -Strategy $script:preferences.strategy -CodexShare $script:preferences.codex_share
    $script:CodexShare = [int]$shares.Codex; $script:AntigravityShare = [int]$shares.Antigravity
    $script:CodexModel = [string]$script:preferences.codex_model; $script:CodexEffort = [string]$script:preferences.codex_effort
    $script:AntigravityModel = [string]$script:preferences.antigravity_model; $script:AntigravityEffort = [string]$script:preferences.antigravity_effort
    $script:runtime.CodexSandbox = [string]$script:preferences.codex_task_sandbox
    $env:AGEX_CODEX_TIMEOUT_SECONDS = [string]$script:preferences.codex_timeout_seconds
    $env:AGEX_AGY_TIMEOUT_SECONDS = [string]$script:preferences.antigravity_timeout_seconds
    $script:ResolvedLeader = Resolve-AgexLeader -ConfiguredLeader $script:ConfiguredLeader -CodexShare $script:CodexShare -AntigravityShare $script:AntigravityShare
    $script:ActiveLeader = $script:ResolvedLeader
    $script:ui.ConfiguredLeader = $script:ConfiguredLeader; $script:ui.ResolvedLeader = $script:ResolvedLeader
    $script:ui.CodexShare = $script:CodexShare; $script:ui.AntigravityShare = $script:AntigravityShare
    Set-AgexEnabledAgentPolicy
}

function Set-AgexServeProject {
    param([Parameter(Mandatory)][string]$Path)
    if ($script:activeExecution) { throw "A request is running. Change the project after it finishes." }
    $candidate = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { throw "Project folder was not found. Choose another folder." }
    $script:Project = (Resolve-Path -LiteralPath $candidate).Path
    $script:ui.Project = $script:Project; $script:ui.ProjectName = Split-Path -Leaf $script:Project
    Add-AgexRecentProject -Preferences $script:preferences -ProjectPath $script:Project
    Save-AgexPreferences -Preferences $script:preferences
}

function Invoke-AgexServeCommand {
    param([Parameter(Mandatory)]$Command)
    $name = [string]$Command.cmd
    $reply = @{ type = "ack"; cmd = $name; reqId = [string]$Command.reqId; ok = $true }
    try {
        switch ($name) {
            "hello" { $reply = @{ type = "hello"; reqId = [string]$Command.reqId; version = (Get-AgexVersion); sessionId = $SessionId; dataRoot = (Get-AgexDataRoot); logPath = [string]$script:runtime.LogPath; firstRun = -not [bool]$script:preferences.first_run_complete } }
            "scan" {
                $scan = Invoke-AgexDiscovery -Runtime $script:runtime -EnabledAgents @($script:preferences.enabled_agents) -WorkingDirectory $Project
                Set-AgexEnabledAgentPolicy
                $reply = @{ type = "scan"; reqId = [string]$Command.reqId; scan = $scan }
            }
            "agents.test" {
                $adapter = Get-AgexAdapter -Id ([string]$Command.id)
                if (-not $adapter) { throw "Unknown agent." }
                $exe = Resolve-AgexAdapterExecutable -Adapter $adapter
                $health = Test-AgexAgentPrecheck -Runtime $script:runtime -Agent $adapter.Name -ExecutablePath $exe -WorkingDirectory $Project
                Set-AgexEnabledAgentPolicy
                $reply = @{ type = "agentTest"; reqId = [string]$Command.reqId; id = $adapter.Id; healthy = [bool]$health.Healthy; reason = [string]$health.Reason }
            }
            "start" {
                if ($Command.project) { Set-AgexServeProject -Path ([string]$Command.project) }
                $prompt = [string]$Command.prompt
                if ([string]::IsNullOrWhiteSpace($prompt)) { throw "Type a request first." }
                if ($script:activeExecution) {
                    $script:queuedPrompts.Enqueue($prompt)
                    $reply.queued = $script:queuedPrompts.Count
                } else {
                    $leader = if ($Command.leader) { [string]$Command.leader } else { "" }
                    if ($leader) { $health = Get-AgexAgentHealth -Runtime $script:runtime -Agent $leader; if (-not $health.Healthy -and $health.Until -ne [datetime]::MaxValue) { Set-AgexAgentHealth -Runtime $script:runtime -Agent $leader -Healthy $true -Reason "Retry requested by user." } }
                    if (-not (Start-AgexTaskExecution -Prompt $prompt -LeaderOverride $leader)) { throw "AGEX could not start the request." }
                    $reply.requestId = [string]$script:ui.WorkId
                }
            }
            "retry" {
                if (-not $script:lastPrompt) { throw "Nothing to retry yet." }
                if ($script:activeExecution) { throw "A request is already running." }
                foreach ($adapter in Get-AgexAdapters) { $health = Get-AgexAgentHealth -Runtime $script:runtime -Agent $adapter.Name; if (-not $health.Healthy -and $health.Until -ne [datetime]::MaxValue) { Set-AgexAgentHealth -Runtime $script:runtime -Agent $adapter.Name -Healthy $true -Reason "Retry requested by user." } }
                $leader = if ($Command.leader) { [string]$Command.leader } else { [string]$script:lastLeaderOverride }
                [void](Start-AgexTaskExecution -Prompt $script:lastPrompt -LeaderOverride $leader)
            }
            "cancel" { Request-AgexTaskCancellation }
            "project.set" { Set-AgexServeProject -Path ([string]$Command.path) }
            "settings.get" { $reply = @{ type = "settings"; reqId = [string]$Command.reqId; settings = $script:preferences } }
            "settings.set" { Set-AgexServeSettings -Values $Command.settings; $reply = @{ type = "settings"; reqId = [string]$Command.reqId; settings = $script:preferences } }
            "sessions.list" { $reply = @{ type = "sessions"; reqId = [string]$Command.reqId; items = @(Get-AgexSessionList -Limit 200) } }
            "session.get" { $reply = @{ type = "session"; reqId = [string]$Command.reqId; session = (Get-AgexSessionRecord -SessionId ([string]$Command.id)) } }
            "changes" { $reply = Get-AgexServeChanges; $reply.reqId = [string]$Command.reqId }
            "diff" { $reply = Get-AgexServeDiff -Path ([string]$Command.path); $reply.reqId = [string]$Command.reqId }
            "details" { $reply = @{ type = "details"; reqId = [string]$Command.reqId; lines = @(Get-AgexDetailsLines) } }
            "repair" { . (Join-Path $PSScriptRoot "agex-maintenance.ps1"); $reply = @{ type = "repair"; reqId = [string]$Command.reqId; results = @(Invoke-AgexRepair -Fix) } }
            "update.check" { . (Join-Path $PSScriptRoot "agex-maintenance.ps1"); $reply = @{ type = "update"; reqId = [string]$Command.reqId; update = (Get-AgexUpdateInfo) } }
            "shutdown" { $script:serveStop = $true }
            default { throw "Unknown command: $name" }
        }
    } catch {
        $reply = @{ type = "error"; cmd = $name; reqId = [string]$Command.reqId; message = (Protect-AgexTelemetryText -Text $_.Exception.Message) }
    }
    Send-AgexServeEvent -Event $reply
}

function Initialize-AgexPipeReader {
    # A pending synchronous ReadFile on the stdin pipe blocks every other use of
    # that handle, including handle inheritance when the engine starts agent
    # processes. So stdin is only read when PeekNamedPipe says data is waiting.
    if (-not ("AgexPipe" -as [type])) {
        Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class AgexPipe {
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool PeekNamedPipe(IntPtr h, IntPtr buffer, uint size, IntPtr read, out uint available, IntPtr left);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(IntPtr h, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);
    public static int Available(IntPtr h) { uint a; if (!PeekNamedPipe(h, IntPtr.Zero, 0, IntPtr.Zero, out a, IntPtr.Zero)) return -1; return (int)a; }
}
"@
    }
    $script:pipeHandle = [AgexPipe]::GetStdHandle(-10)
    $script:pipeDecoder = [Text.UTF8Encoding]::new($false).GetDecoder()
    $script:pipeBuffer = [System.Text.StringBuilder]::new()
}

function Read-AgexPipeLines {
    # Returns complete lines that are available now; $null marks end of input.
    $available = [AgexPipe]::Available($script:pipeHandle)
    if ($available -lt 0) { return , @($null) }
    if ($available -eq 0) { return , @() }
    $bytes = New-Object byte[] ([math]::Min($available, 1048576))
    $read = [uint32]0
    if (-not [AgexPipe]::ReadFile($script:pipeHandle, $bytes, [uint32]$bytes.Length, [ref]$read, [IntPtr]::Zero) -or $read -eq 0) { return , @($null) }
    $chars = New-Object char[] ($script:pipeDecoder.GetCharCount($bytes, 0, [int]$read))
    [void]$script:pipeDecoder.GetChars($bytes, 0, [int]$read, $chars, 0)
    [void]$script:pipeBuffer.Append($chars)
    $text = $script:pipeBuffer.ToString()
    $cut = $text.LastIndexOf("`n")
    if ($cut -lt 0) { return , @() }
    [void]$script:pipeBuffer.Remove(0, $cut + 1)
    , @($text.Substring(0, $cut).Split("`n") | ForEach-Object { $_.TrimEnd("`r") })
}

function Start-AgexServe {
    $script:agexServe = $true
    $script:agexInteractive = $true
    # Keep stdout clean: host writes from engine code must not reach the protocol stream.
    function global:Write-Host { }
    $utf8 = [Text.UTF8Encoding]::new($false)
    $script:serveOutSync = [object]::new()
    $script:serveOut = [IO.StreamWriter]::new([Console]::OpenStandardOutput(), $utf8)
    $script:serveOut.AutoFlush = $false
    Initialize-AgexPipeReader
    $script:serveStop = $false
    $lastState = ""
    $lastEventSeq = 0
    $lastMessageSeq = 0
    $lastTick = [datetime]::MinValue
    Send-AgexServeEvent -Event @{ type = "hello"; version = (Get-AgexVersion); sessionId = $SessionId; dataRoot = (Get-AgexDataRoot); logPath = [string]$script:runtime.LogPath; firstRun = -not [bool]$script:preferences.first_run_complete }
    if (-not $SkipPrecheck) { try { [void](Invoke-AgexPrecheck) } catch { } }
    while (-not $script:serveStop) {
        $inputEnded = $false
        foreach ($line in (Read-AgexPipeLines)) {
            if ($null -eq $line) { $inputEnded = $true; break }
            $line = $line.TrimStart([char]0xFEFF)
            if (-not $line.Trim()) { continue }
            $command = $null
            try { $command = $line | ConvertFrom-Json } catch { Send-AgexServeEvent -Event @{ type = "error"; message = "Invalid command JSON." } }
            if ($command) { Invoke-AgexServeCommand -Command $command }
            if ($script:serveStop) { break }
        }
        if ($inputEnded) { break }
        try { Pump-AgexTaskExecution } catch { Send-AgexServeEvent -Event @{ type = "error"; message = (Protect-AgexTelemetryText -Text $_.Exception.Message) } }
        if (((Get-Date) - $lastTick).TotalMilliseconds -ge 250) {
            $lastTick = Get-Date
            try {
                Receive-AgexUiUpdates -State $script:ui
                Pump-AgexUiFileWatch -State $script:ui
                foreach ($item in @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Events | Where-Object { [int]$_.Seq -gt $lastEventSeq })) {
                    $lastEventSeq = [int]$item.Seq
                    Send-AgexServeEvent -Event @{ type = "event"; seq = [int]$item.Seq; at = (ConvertTo-AgexIso $item.At); source = [string]$item.Source; kind = [string]$item.Kind; message = [string]$item.Message; taskId = [string]$item.TaskId }
                }
                foreach ($item in @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Messages | Where-Object { [int]$_.Seq -gt $lastMessageSeq })) {
                    $lastMessageSeq = [int]$item.Seq
                    Send-AgexServeEvent -Event @{ type = "message"; message = $item }
                }
                $state = Get-AgexServeState
                $json = ConvertTo-Json -InputObject $state -Compress -Depth 10
                if ($json -ne $lastState) { $lastState = $json; [System.Threading.Monitor]::Enter($script:serveOutSync); try { $script:serveOut.WriteLine($json); $script:serveOut.Flush() } finally { [System.Threading.Monitor]::Exit($script:serveOutSync) } }
            } catch { Write-AgexLog -Runtime $script:runtime -Event 'serve_warning' -Data @{ error = $_.Exception.Message } }
        }
        Start-Sleep -Milliseconds 40
    }
    Stop-AgexSessionWork
    try { Send-AgexServeEvent -Event @{ type = "bye" } } catch { }
}

# ---------------------------------------------------------------- entry

if ($DefinitionsOnly) { return }
if ($ExecutorPrompt) {
    try {
        $ok = Invoke-AgexAgentCall -Agent $AssignedExecutor -Prompt $ExecutorPrompt -WorkId $ExecutorWorkId -Purpose (Get-AgexUiTaskId -WorkId $ExecutorWorkId) -AllowFallback -NeedsWrite $ExecutorNeedsWrite
        $ExecutorResult.Success = [bool]$ok
        $ExecutorResult.Output = $script:lastExecutorResult
        $ExecutorResult.Agent = $script:lastCallAgent
        if (-not $ok -and $script:lastExecutorOutcome) { $ExecutorResult.Reason = [string]$script:lastExecutorOutcome.Reason }
    } catch {
        $ExecutorResult.Success = $false
        $ExecutorResult.Reason = Protect-AgexTelemetryText -Text $_.Exception.Message
    } finally { $ExecutorResult.Completed = $true }
    return
}

if (-not [string]::IsNullOrWhiteSpace($ExecutePrompt)) {
    Invoke-AgexTask -Prompt $ExecutePrompt
    return
}

$script:activeExecution = $null
$script:queuedPrompts = [System.Collections.Generic.Queue[string]]::new()
Write-AgexLog -Runtime $script:runtime -Event 'session_start' -Data @{ project = $Project; leader = $ConfiguredLeader; codex_share = $CodexShare; antigravity_share = $AntigravityShare; codex = $CodexPath; agy = [string]$AgyPath }
if ($Serve) {
    Start-AgexServe
    exit 0
}
if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
    if (-not $SkipPrecheck) { [void](Invoke-AgexPrecheck) }
    Start-AgexLineMode
    exit 0
}
Start-AgexInteractive
exit 0
