[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$StatePath,
    [Parameter(Mandatory)][string]$TaskPath,
    [Parameter(Mandatory)][string]$WorkingDirectory,
    [Parameter(Mandatory)][string]$AgyPath,
    [string]$TelemetryPath,
    [string]$AntigravityModel,
    [string]$AntigravityEffort,
    [int]$ConcurrentAgyCount = 1,
    [int]$OrchestratorPid = 0,
    [int]$HarnessPid = 0,
    [string]$StartupDiagnosticPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dawoud-common.ps1")
$started = Get-Date
$state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
$task = Get-Content -LiteralPath $TaskPath -Raw
$script:runDir = Split-Path -Parent $StatePath
$script:workerPid = 0
$script:processStarted = $false
$script:eventCount = 0
$script:lastEvent = ""
$script:finalResponseEvent = $false
$script:outputFromAgy = $false
$script:timeoutState = "NONE"
$script:stdoutLength = 0
$script:stderrLength = 0
$script:firstEventAt = $null
$script:firstStdoutLine = ""
$script:failedStage = ""
$script:exceptionType = ""
$script:exceptionMessage = ""
$script:startupTimeoutSeconds = 45
$script:idleTimeoutSeconds = 180
$script:totalTimeoutSeconds = 720
$script:startupDiagnosticPath = if ($StartupDiagnosticPath) { $StartupDiagnosticPath } else { Join-Path $script:runDir "agy.startup.diagnostic.log" }
$script:transportRoot = Join-Path $env:TEMP ("dawoud-agy-worker-" + (Get-SafeId -Value (Split-Path -Leaf $script:runDir)))
New-Item -ItemType Directory -Path $script:transportRoot -Force | Out-Null
$script:stdinPath = Join-Path $script:transportRoot "agy.stdin.ndjson"
$script:rawStdoutPath = Join-Path $script:transportRoot "agy.stdout.raw.log"
$script:rawStderrPath = Join-Path $script:transportRoot "agy.stderr.raw.log"
$script:eventLogPath = Join-Path $script:transportRoot "agy.events.log"
$script:cliLogPath = Join-Path $script:transportRoot "agy.cli.log"
$script:stderrLogPath = Join-Path $script:transportRoot "agy.stderr.log"

function Write-StartupDiagnostic {
    param([Parameter(Mandatory)][string]$Line)
    try {
        $parent = Split-Path -Parent $script:startupDiagnosticPath
        if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        Add-Content -LiteralPath $script:startupDiagnosticPath -Value $Line -Encoding utf8 -ErrorAction Stop
    } catch { }
}

function Write-ProcessTreeDiagnostic {
    param([int[]]$Roots)
    try {
        $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop)
        $ids = [System.Collections.Generic.HashSet[int]]::new()
        foreach ($rootPid in @($Roots | Where-Object { $_ -gt 0 })) { [void]$ids.Add([int]$rootPid) }
        $changed = $true
        while ($changed) {
            $changed = $false
            foreach ($item in $processes) { if ($ids.Contains([int]$item.ParentProcessId) -and $ids.Add([int]$item.ProcessId)) { $changed = $true } }
        }
        $stamp = (Get-Date).ToUniversalTime().ToString("o")
        foreach ($item in @($processes | Where-Object { $ids.Contains([int]$_.ProcessId) } | Sort-Object ProcessId)) {
            Write-StartupDiagnostic ("PROCESS_TREE timestamp={0}; pid={1}; ppid={2}; executable={3}; command_line={4}; creation_time={5}" -f $stamp, $item.ProcessId, $item.ParentProcessId, (Protect-DawoudTelemetryText -Text ([string]$item.ExecutablePath)), (Protect-DawoudTelemetryText -Text ([string]$item.CommandLine)), $item.CreationDate)
        }
    } catch { Write-StartupDiagnostic ("PROCESS_TREE_ERROR {0}" -f (Protect-DawoudTelemetryText -Text $_.Exception.Message)) }
}

function Set-DiagnosticState {
    param([string]$FailureReason = "")
    $runtimeIdentity = Get-DawoudRuntimeIdentity
    foreach ($pair in @{
        agy_executable = $AgyPath
        agy_arguments = @("--log-file <temp-worker-log>", "--input-format stream-json", "--output-format stream-json", "--sandbox", "--dangerously-skip-permissions", "--print-timeout 10m", "--model $AntigravityModel", "--effort $AntigravityEffort")
        input_mode = "stdin NDJSON; one user event; stdin flushed and held until result"
        output_mode = "stdout NDJSON stream-json"
        working_directory = $WorkingDirectory
        model = $AntigravityModel
        effort = $AntigravityEffort
        prompt_chars = $task.Length
        prompt_utf8_bytes = [Text.Encoding]::UTF8.GetByteCount($task)
        concurrent_agy = $ConcurrentAgyCount
        process_started = $script:processStarted
        stream_events = $script:eventCount
        last_valid_event = $script:lastEvent
        final_response_event = $script:finalResponseEvent
        timeout_state = $script:timeoutState
        stdout_chars = $script:stdoutLength
        stderr_chars = $script:stderrLength
        first_event_at = if ($script:firstEventAt) { $script:firstEventAt.ToUniversalTime().ToString("o") } else { "" }
        dawoud_process_identity = $runtimeIdentity.Name
        agy_executor_identity = $runtimeIdentity.Name
        agy_parent_pid = $PID
        agy_pid = $script:workerPid
        agy_cli_log_path = $script:cliLogPath
        agy_stdin_path = $script:stdinPath
        startup_diagnostic_path = $script:startupDiagnosticPath
        first_stdout_line = $script:firstStdoutLine
        failed_stage = $script:failedStage
        exception_type = $script:exceptionType
        exception_message = $script:exceptionMessage
        failure_reason = (Protect-DawoudTelemetryText -Text $FailureReason)
    }.GetEnumerator()) { $state | Add-Member -MemberType NoteProperty -Name $pair.Key -Value $pair.Value -Force }
}

function Save-State {
    param([string]$Status, [int]$ExitCode, [string]$Summary, [string]$ErrorText = "")
    Set-DiagnosticState -FailureReason $ErrorText
    $now = Get-Date
    foreach ($pair in @{
        status = $Status
        exit_code = $ExitCode
        finished_at = $now.ToUniversalTime().ToString("o")
        elapsed_seconds = [math]::Round(($now - $started).TotalSeconds, 2)
        result_summary = (Protect-DawoudTelemetryText -Text $Summary)
        error = (Protect-DawoudTelemetryText -Text $ErrorText)
    }.GetEnumerator()) { $state | Add-Member -MemberType NoteProperty -Name $pair.Key -Value $pair.Value -Force }
    $state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $StatePath -Encoding utf8
    if ($TelemetryPath) {
        try {
            Set-DawoudTelemetryRecord -Path $TelemetryPath -Fields @{ AgyPath = $AgyPath; InvocationArguments = $state.agy_arguments; InputMode = $state.input_mode; OutputMode = $state.output_mode; WorkingDirectory = $WorkingDirectory; SelectedProjectPath = $WorkingDirectory; ExecutorWorkingDirectory = $WorkingDirectory; PromptChars = $state.prompt_chars; PromptUtf8Bytes = $state.prompt_utf8_bytes; ConcurrentAGY = $ConcurrentAgyCount; ProcessStarted = $state.process_started; StreamEvents = $state.stream_events; LastValidEvent = $state.last_valid_event; FinalResponseEvent = $state.final_response_event; TimeoutState = $state.timeout_state; StdoutChars = $state.stdout_chars; StderrChars = $state.stderr_chars; FailureReason = $state.failure_reason }
            Complete-DawoudTelemetryRecord -Path $TelemetryPath -Status $Status -ExitCode $ExitCode -Summary $Summary -WorkerPid $script:workerPid -AgyPath $AgyPath -OutputReturnedFromAGY $script:outputFromAgy
        } catch {
            $state | Add-Member -MemberType NoteProperty -Name telemetry_write_error -Value (Protect-DawoudTelemetryText -Text $_.Exception.Message) -Force
            $state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $StatePath -Encoding utf8
        }
    }
}

try {
    Write-StartupDiagnostic "WORKER_STARTED"
    Write-StartupDiagnostic ("WORKER_PID={0}" -f $PID)
    Write-StartupDiagnostic ("AGY_PATH={0}" -f (Protect-DawoudTelemetryText -Text $AgyPath))
    $argsPreview = @("--log-file <temp-worker-log>", "--input-format stream-json", "--output-format stream-json", "--sandbox", "--dangerously-skip-permissions", "--print-timeout 10m", "--model $AntigravityModel", "--effort $AntigravityEffort")
    Write-StartupDiagnostic ("AGY_ARGUMENTS_SANITIZED={0}" -f (Protect-DawoudTelemetryText -Text ($argsPreview -join " ")))
    Write-StartupDiagnostic ("CWD={0}" -f (Protect-DawoudTelemetryText -Text $WorkingDirectory))
    Write-StartupDiagnostic ("ORCHESTRATOR_PID={0};HARNESS_PID={1}" -f $OrchestratorPid, $HarnessPid)
    $milestone = {
        param([string]$Name, [int]$ProcessId)
        Write-StartupDiagnostic $Name
        if ($Name -eq "AGY_PROCESS_STARTED") { Write-StartupDiagnostic ("AGY_PID={0}" -f $ProcessId); Write-ProcessTreeDiagnostic -Roots @($HarnessPid, $OrchestratorPid, $PID, $ProcessId) }
        if ($Name -eq "FIRST_STDOUT_EVENT") { $script:firstEventAt = Get-Date }
    }
    $stream = Invoke-DawoudAgyStream -AgyPath $AgyPath -WorkingDirectory $WorkingDirectory -Prompt $task -Model $AntigravityModel -Effort $AntigravityEffort -CliLogPath $script:cliLogPath -StdinPath $script:stdinPath -RawStdoutPath $script:rawStdoutPath -RawStderrPath $script:rawStderrPath -EventLogPath $script:eventLogPath -StartupTimeoutSeconds $script:startupTimeoutSeconds -IdleTimeoutSeconds $script:idleTimeoutSeconds -TotalTimeoutSeconds $script:totalTimeoutSeconds -OnMilestone $milestone
    $script:processStarted = [bool]$stream.ProcessStarted
    $script:workerPid = [int]$stream.ActualPid
    $state.worker_pid = $script:workerPid
    $script:eventCount = [int]$stream.StreamEvents
    $script:lastEvent = if ($stream.FinalResultEvent) { "result" } else { "" }
    $script:finalResponseEvent = [bool]$stream.FinalResultEvent
    $script:outputFromAgy = [bool]$stream.Success
    $script:firstStdoutLine = [string]$stream.FirstStdoutEvent
    $script:stdoutLength = ([string]$stream.Stdout).Length
    $script:stderrLength = ([string]$stream.Stderr).Length
    $script:timeoutState = [string]$stream.TimeoutReason
    $script:failedStage = [string]$stream.FailedStage
    $script:exceptionType = [string]$stream.ExceptionType
    $script:exceptionMessage = [string]$stream.ExceptionMessage
    foreach ($pair in @{
        contract = $stream.Contract
        success = [bool]$stream.Success
        actual_exe = $stream.ActualExe
        actual_pid = [int]$stream.ActualPid
        stdin_written = [bool]$stream.StdinWritten
        first_stdout_event = $stream.FirstStdoutEvent
        first_raw_stdout = $stream.FirstRawStdout
        stream_events = [int]$stream.StreamEvents
        final_result_event = [bool]$stream.FinalResultEvent
        final_response = $stream.FinalResponse
        exit_code = [int]$stream.ExitCode
        timeout_reason = $stream.TimeoutReason
        error = $stream.Error
    }.GetEnumerator()) { $state | Add-Member -MemberType NoteProperty -Name $pair.Key -Value $pair.Value -Force }
    if ($stream.FinalResult) { $script:firstEventAt = Get-Date }
    $failureReason = [string]$stream.Error
    $summary = if ($stream.FinalResponse) { [string]$stream.FinalResponse } else { "No final response returned." }
    if ($summary.Length -gt 4000) { $summary = $summary.Substring(0, 4000) + "..." }
    $success = [bool]$stream.Success
    $exitCode = if ($success) { 0 } elseif ($stream.ExitCode -ne 0) { $stream.ExitCode } else { 1 }
    if ($stream.Stderr) { Set-Content -LiteralPath $script:stderrLogPath -Value (Protect-DawoudTelemetryText -Text $stream.Stderr) -Encoding utf8 }
    if ($stream.ExceptionType) { $failureReason = "FAILED_STAGE=$($stream.FailedStage); EXCEPTION_TYPE=$($stream.ExceptionType); ERROR=$($stream.ExceptionMessage)" }
    if (-not $failureReason -and -not $success) { $failureReason = "Antigravity returned no final response event payload." }
    Save-State -Status $(if ($success) { "DONE" } else { "ERROR" }) -ExitCode $exitCode -Summary $summary -ErrorText $failureReason
    exit $exitCode
} catch {
    $message = $_.Exception.Message
    Save-State -Status "ERROR" -ExitCode 1 -Summary "Worker failed before final response." -ErrorText $message
    exit 1
}
