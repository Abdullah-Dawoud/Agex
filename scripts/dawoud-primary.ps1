[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string]$CodexPath,
    [string]$AgyPath,
    [Parameter(Mandatory)][string]$SessionId,
    [ValidateSet("Codex", "Antigravity", "Auto")][string]$ConfiguredLeader = "Antigravity",
    [int]$CodexShare = 10,
    [int]$AntigravityShare = 90,
    [string]$CodexModel,
    [string]$CodexEffort,
    [string]$AntigravityModel,
    [string]$AntigravityEffort,
    [Parameter(DontShow)][string]$ExecutePrompt,
    [Parameter(DontShow)][object]$SharedUiState,
    [ValidateRange(1, 2)][int]$MaxWorkers = 2,
    [Parameter(DontShow)][switch]$DefinitionsOnly,
    [Parameter(DontShow)][string]$ExecutorPrompt,
    [Parameter(DontShow)][string]$AssignedExecutor,
    [Parameter(DontShow)][string]$ExecutorWorkId,
    [Parameter(DontShow)][object]$ExecutorResult,
    [Parameter(DontShow)][object]$CancellationSignal
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$orchestratorScript = Join-Path $PSScriptRoot "orchestrator.ps1"
$telemetryRoot = Join-Path $root "reports\telemetry"
. (Join-Path $PSScriptRoot "dawoud-common.ps1")
. (Join-Path $PSScriptRoot "dawoud-ui.ps1")
. (Join-Path $PSScriptRoot "dawoud-graph.ps1")

trap {
    [Console]::Error.WriteLine((Protect-DawoudTelemetryText -Text ([string]$_.Exception.Message)))
    exit 1
}

if (-not (Test-Path -LiteralPath $Project -PathType Container)) { throw "Project directory not found: $Project" }
if (-not (Test-Path -LiteralPath $CodexPath -PathType Leaf)) { throw "Codex executable not found: $CodexPath" }
if ($CodexShare + $AntigravityShare -ne 100) { throw "Codex and Antigravity workload percentages must total 100." }

$script:history = [System.Collections.Generic.List[object]]::new()
$script:lastAgyPath = $AgyPath
$script:ResolvedLeader = Resolve-DawoudLeader -ConfiguredLeader $ConfiguredLeader -CodexShare $CodexShare -AntigravityShare $AntigravityShare
$script:ui = New-DawoudUiState -Project $Project -SessionId $SessionId -ConfiguredLeader $ConfiguredLeader -ResolvedLeader $script:ResolvedLeader -CodexShare $CodexShare -AntigravityShare $AntigravityShare -CodexModel $CodexModel -AntigravityModel $AntigravityModel
if ($SharedUiState) { [void](Assert-DawoudUiStateSchema -State $SharedUiState); $script:ui = $SharedUiState }
$script:cancellationSignal = if ($CancellationSignal) { $CancellationSignal } else { [hashtable]::Synchronized(@{ Requested = $false }) }

function Refresh-DawoudUi {
    param([switch]$Force)
    if ($script:ui) { Write-DawoudDashboard -State $script:ui -Force:$Force }
}

function Set-DawoudUiTaskState {
    param([string]$TaskId, [string]$Status, [string]$Agent, [string]$Reason = "", [string]$ErrorText = "")
    if ($script:ui) { [void](Set-DawoudUiTask -State $script:ui -TaskId $TaskId -Status $Status -Agent $Agent -Reason $Reason -ErrorText $ErrorText); Refresh-DawoudUi }
}

function Get-DawoudSanitizedCommand {
    param([string]$Text)
    $safe = Protect-DawoudTelemetryText -Text $Text
    $safe = $safe -replace '(?i)(-Task\s+)("[^"]*"|\S+)', '$1<task>'
    if ($safe.Length -gt 220) { $safe = $safe.Substring(0, 220) + "..." }
    $safe
}

function Add-ProcessArguments {
    param([Parameter(Mandatory)]$StartInfo, [Parameter(Mandatory)][AllowEmptyString()][string[]]$Arguments)
    if ($StartInfo.PSObject.Properties.Name -contains "ArgumentList" -and $null -ne $StartInfo.ArgumentList) {
        foreach ($argument in $Arguments) { [void]$StartInfo.ArgumentList.Add([string]$argument) }
        return
    }
    $StartInfo.Arguments = (($Arguments | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join ' ')
}

function Write-DawoudHeader {
    if ($script:ui) {
        $script:ui.Project = $Project
        $script:ui.ProjectName = Split-Path -Leaf $Project
        $script:ui.ConfiguredLeader = $ConfiguredLeader
        $script:ui.ResolvedLeader = $script:ResolvedLeader
        $script:ui.CodexShare = $CodexShare
        $script:ui.AntigravityShare = $AntigravityShare
        $script:ui.CodexModel = if ($CodexModel) { $CodexModel } else { "default" }
        $script:ui.AntigravityModel = if ($AntigravityModel) { $AntigravityModel } else { "default" }
        if ($script:ui.RenderTop -lt 0 -and -not [Console]::IsOutputRedirected) {
            if (-not $script:identityNoticeShown) { $script:identityNoticeShown = $true; [void](Write-DawoudRuntimeIdentityNotice) }
            $script:ui.RenderTop = [Console]::CursorTop
        }
        Refresh-DawoudUi -Force
        return
    }
    if (-not [Console]::IsOutputRedirected) { try { Clear-Host } catch {} }
    $consoleWidth = 80
    try { $consoleWidth = [Console]::WindowWidth } catch {}
    $unicode = ([Console]::OutputEncoding.CodePage -eq 65001)
    $tl = if ($unicode) { [string][char]0x256D } else { "+" }
    $tr = if ($unicode) { [string][char]0x256E } else { "+" }
    $bl = if ($unicode) { [string][char]0x2570 } else { "+" }
    $br = if ($unicode) { [string][char]0x256F } else { "+" }
    $h = if ($unicode) { [string][char]0x2500 } else { "-" }
    $v = if ($unicode) { [string][char]0x2502 } else { "|" }
    if ($consoleWidth -lt 68) {
        Write-Host "AGEX AI CONTROL CENTER" -ForegroundColor Cyan
        Write-Host ("Project: {0}" -f (Split-Path -Leaf $Project))
        Write-Host ("Leader: {0} -> {1}; AGY {2}% / Codex {3}%" -f $ConfiguredLeader, $script:ResolvedLeader, $AntigravityShare, $CodexShare)
        if (-not $script:identityNoticeShown) { $script:identityNoticeShown = $true; [void](Write-DawoudRuntimeIdentityNotice) }
        Write-Host "Write request below. Enter=new line; Ctrl+Enter/:send=send; Ctrl+U=clear; Ctrl+C=cancel." -ForegroundColor DarkGray
        Write-Host ":help = Commands" -ForegroundColor DarkGray
        return
    }
    $rule = -join (1..62 | ForEach-Object { $h })
    $bar = "$tl$rule$tr"
    Write-Host $bar -ForegroundColor DarkCyan
    Write-Host ("$v AGEX  AI CONTROL CENTER".PadRight(63) + $v) -ForegroundColor Cyan
    Write-Host ("$v$rule$v") -ForegroundColor DarkCyan
    Write-Host (("$v Project   {0}" -f (Split-Path -Leaf $Project)).PadRight(63) + $v)
    Write-Host (("$v Leader    {0} -> {1}" -f $ConfiguredLeader, $script:ResolvedLeader).PadRight(63) + $v)
    $agyBars = [math]::Max(0, [math]::Min(20, [math]::Round($AntigravityShare / 5)))
    $codexBars = 20 - $agyBars
    $filled = if ($agyBars -gt 0) { -join (1..$agyBars | ForEach-Object { [string][char]0x2588 }) } else { "" }
    $empty = if ($codexBars -gt 0) { -join (1..$codexBars | ForEach-Object { [string][char]0x2591 }) } else { "" }
    $workload = ("$v Workload  AGY {0}% {1}{2} Codex {3}%" -f $AntigravityShare, $filled, $empty, $CodexShare)
    if (-not $unicode) { $workload = ("$v Workload  AGY {0}% [{1}] Codex {2}%" -f $AntigravityShare, ("#" * $agyBars + "." * $codexBars), $CodexShare) }
    Write-Host (($workload.PadRight(63)) + $v)
    Write-Host (("$v Models    AGY: {0} | Codex: {1}" -f $(if ($AntigravityModel) { $AntigravityModel } else { "default" }), $(if ($CodexModel) { $CodexModel } else { "default" })).PadRight(63) + $v)
    Write-Host (("$v Modes     Caveman + Orchestrator").PadRight(63) + $v)
    Write-Host "$bl$rule$br" -ForegroundColor DarkCyan
    if (-not $script:identityNoticeShown) {
        $script:identityNoticeShown = $true
        [void](Write-DawoudRuntimeIdentityNotice)
    }
    Write-Host ""
    Write-Host "Write request below." -ForegroundColor DarkGray
    Write-Host "Enter = new line    Ctrl+Enter or :send = Send    Ctrl+U = Clear    Ctrl+C = Cancel" -ForegroundColor DarkGray
    Write-Host ":help = Commands" -ForegroundColor DarkGray
    Write-Host ""
}

function Write-DawoudCompactSummary {
    param([Parameter(Mandatory)][string]$WorkId)
    if ($script:ui -and $script:ui.WorkId -eq $WorkId) {
        return
    }
    $records = @(Get-DawoudTelemetryRecords -TelemetryRoot $telemetryRoot -SessionId $SessionId | Where-Object { $_.WorkId -like "$WorkId-*" -and $_.End })
    $codex = @($records | Where-Object Executor -eq "CODEX")
    $agy = @($records | Where-Object Executor -eq "ANTIGRAVITY")
    $codexSeconds = [math]::Round((@($codex | Measure-Object DurationSeconds -Sum).Sum), 2)
    $agySeconds = [math]::Round((@($agy | Measure-Object DurationSeconds -Sum).Sum), 2)
    $agyDone = @($agy | Where-Object Status -eq "DONE").Count
    $agyFailed = @($agy | Where-Object Status -ne "DONE").Count
    Write-Host ""
    Write-Host "----------------------------------------" -ForegroundColor DarkCyan
    Write-Host "AGEX EXECUTION" -ForegroundColor Cyan
    Write-Host ("Leader: {0}" -f $ConfiguredLeader)
    Write-Host ("Codex: {0} task(s) | {1}s" -f $codex.Count, $codexSeconds)
    Write-Host ("AGY: {0} task(s) | {1}s | {2} success | {3} failure" -f $agy.Count, $agySeconds, $agyDone, $agyFailed)
    Write-Host ("AGY model: {0}" -f $(if ($AntigravityModel) { $AntigravityModel } else { "default" }))
    Write-Host ("Status: {0}" -f $(if ($agyFailed -gt 0 -or @($records | Where-Object Status -eq "ERROR").Count -gt 0) { "PARTIAL/ERROR" } else { "COMPLETE" }))
    Write-Host "----------------------------------------" -ForegroundColor DarkCyan
}

function Get-WorkerState {
    param([Parameter(Mandatory)][string]$WorkId)
    $workerRoot = Join-Path $root "reports\workers"
    @(Get-ChildItem -LiteralPath $workerRoot -Filter status.json -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object { try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } catch {} } |
        Where-Object { $_.session_id -eq $SessionId -and $_.work_id -eq $WorkId } |
        Sort-Object started_at -Descending | Select-Object -First 1)
}

function Invoke-AgyTask {
    param([Parameter(Mandatory)][string]$Prompt, [Parameter(Mandatory)][string]$WorkId, [Parameter(Mandatory)]$Route)
    $resolved = if ($AgyPath -and (Test-Path -LiteralPath $AgyPath -PathType Leaf)) { (Resolve-Path -LiteralPath $AgyPath).Path } else { Resolve-AgyExecutable }
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        Write-Host "AGEX ROUTING FAILURE" -ForegroundColor Red
        Write-Host "Antigravity delegation expected but unavailable." -ForegroundColor Red
        Write-Host "Reason: canonical agy.exe not resolved." -ForegroundColor Red
        Write-Host "AGY attempted: NO" -ForegroundColor Red
        Write-Host "Fallback performed: NO" -ForegroundColor Red
        return $false
    }
    $script:lastAgyPath = $resolved
    $dispatchTaskPath = Join-Path $env:TEMP ("dawoud-task-" + [guid]::NewGuid().ToString("N") + ".txt")
    $record = New-DawoudTelemetryRecord -TelemetryRoot $telemetryRoot -SessionId $SessionId -Task $Prompt -Executor ANTIGRAVITY -Category $Route.Category -CodexShare $CodexShare -AntigravityShare $AntigravityShare -Leader $ConfiguredLeader -ResolvedLeader $script:ResolvedLeader -RouteReason $Route.Reason -Model $AntigravityModel -Effort $AntigravityEffort -Worker "AGEX AGY EXECUTOR" -AgyPath $resolved -WorkId $WorkId -SelectedProjectPath $Project -ExecutorWorkingDirectory $Project -AgyAvailable $true -AgySelected $true -CodexAvailable (Test-Path -LiteralPath $CodexPath -PathType Leaf) -CodexSelected $false -RecordKind TASK
    try {
        $shell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
        $startupPath = Join-Path $env:TEMP ("dawoud-interactive-{0}.startup.log" -f ([guid]::NewGuid().ToString("N")))
        [IO.File]::WriteAllText($dispatchTaskPath, $Prompt, [Text.UTF8Encoding]::new($false))
        $dispatchArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $orchestratorScript, "dispatch", "-TaskPath", $dispatchTaskPath, "-AssignedAgent", "Antigravity", "-WorkingDirectory", $Project, "-Leader", $script:ResolvedLeader, "-CodexShare", $CodexShare, "-AntigravityShare", $AntigravityShare, "-Executor", "CODEX", "-SessionId", $SessionId, "-WorkId", $WorkId, "-Wait", "-WaitTimeoutSeconds", "780", "-HarnessPid", ([string]$PID), "-StartupDiagnosticPath", $startupPath, "-AntigravityModel", $AntigravityModel, "-AntigravityEffort", $AntigravityEffort)
        $agyNumber = 1 + @($script:ui.Agents.Values | Where-Object Executor -eq "ANTIGRAVITY").Count
        $agentName = "ANTIGRAVITY #$agyNumber"
        $uiTaskId = if ($script:ui.WorkId -and $WorkId.StartsWith($script:ui.WorkId + "-")) { $WorkId.Substring($script:ui.WorkId.Length + 1) } else { $WorkId }
        $dispatchDisplay = Get-DawoudSanitizedCommand -Text ("powershell -NoProfile -File {0} dispatch -Wait" -f $orchestratorScript)
        [void](Start-DawoudUiAgent -State $script:ui -Name $agentName -Executor "ANTIGRAVITY" -TaskId $uiTaskId -TaskText $Prompt -Model $AntigravityModel -Command $dispatchDisplay -WorkingDirectory $Project -DiagnosticPath $startupPath)
        [void](Update-DawoudUiAgent -State $script:ui -Name $agentName -Status "STARTING" -Action "Starting AGY worker" -EventKind "START" -Message "Dispatch process started")
        Refresh-DawoudUi -Force
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $shell
        $psi.WorkingDirectory = $root
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardInput = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        Add-ProcessArguments -StartInfo $psi -Arguments $dispatchArgs
        $dispatch = [Diagnostics.Process]::new()
        $dispatch.StartInfo = $psi
        [void]$dispatch.Start()
        [void](Update-DawoudUiAgent -State $script:ui -Name $agentName -Status "STARTING" -Action "Waiting for executor event" -ProcessId $dispatch.Id -EventKind "START" -Message ("Dispatch PID {0}" -f $dispatch.Id))
        if ($script:ui.Agents.Contains($agentName)) { $script:ui.Agents[$agentName].DispatchPID = $dispatch.Id }
        $stdoutLines = [System.Collections.Generic.List[string]]::new()
        $contractLine = ""
        $stdoutReader = $dispatch.StandardOutput
        $readTask = $stdoutReader.ReadLineAsync()
        $stdoutDone = $false
        $stderrTask = $dispatch.StandardError.ReadToEndAsync()
        while (-not $dispatch.HasExited -or -not $stdoutDone) {
            if ($readTask.IsCompleted) {
                $line = $readTask.Result
                if ($null -ne $line) {
                    if ($line -match '^DAWOUD_AGY_RESULT_V1::') { $contractLine = $line }
                    [void]$stdoutLines.Add((Protect-DawoudTelemetryText -Text $line))
                    if ($line -match '^Started\s+(.+?)\s+PID\s+(\d+)') {
                        $workerPid = [int]$Matches[2]
                        [void](Update-DawoudUiAgent -State $script:ui -Name $agentName -Status "RUNNING" -Action "Running Antigravity worker" -ProcessId $workerPid -EventKind "START" -Message ("Worker PID {0}" -f $workerPid))
                    } elseif ($line -match '^Status:') {
                        [void](Update-DawoudUiAgent -State $script:ui -Name $agentName -Status "RUNNING" -Action "Monitoring worker" -EventKind "MONITOR" -Message "Worker monitor active")
                    } elseif ($line -match '^(LEADER|ROUTE|ROUTE_REASON):') {
                        [void](Update-DawoudUiAgent -State $script:ui -Name $agentName -Action "Routing confirmed" -EventKind "ROUTE" -Message $line)
                    }
                    $readTask = $stdoutReader.ReadLineAsync()
                } else { $stdoutDone = $true }
            }
            [void](Update-DawoudUiDiagnostic -State $script:ui -Name $agentName)
            Refresh-DawoudUi
            Start-Sleep -Milliseconds 250
        }
        $dispatch.WaitForExit()
        $dispatchExit = $dispatch.ExitCode
        if ($stderrTask.IsCompleted) { $stderrText = Protect-DawoudTelemetryText -Text ([string]$stderrTask.Result) } else { $stderrText = "" }
        $dispatchOutput = ($stdoutLines -join "`n")
        $dispatch.Dispose()
        if ($script:cancellationSignal.Requested) {
            [void](Complete-DawoudUiAgent -State $script:ui -Name $agentName -Status "CANCELLED" -Message "User cancelled the active AGEX task." -ExitCode 130)
            Complete-DawoudTelemetryRecord -Path $record.Path -Status CANCELLED -ExitCode 130 -Summary "User cancelled the active AGEX task." -AgyPath $resolved -OutputReturnedFromAGY $false
            return $false
        }
        $state = $null
        $contractMatch = [regex]::Match($contractLine, '^DAWOUD_AGY_RESULT_V1::(.+)$')
        if ($contractMatch.Success) { try { $state = $contractMatch.Groups[1].Value | ConvertFrom-Json } catch { $state = $null } }
        $response = if ($state -and $state.FinalResponse) { [string]$state.FinalResponse } else { "" }
        if ($state -and $state.ActualPid -gt 0 -and $script:ui.Agents.Contains($agentName)) { $script:ui.Agents[$agentName].PID = [int]$state.ActualPid; $script:ui.Agents[$agentName].ActualPID = [int]$state.ActualPid }
        $ok = $dispatchExit -eq 0 -and $state -and $state.Success -eq $true -and $state.FinalResultEvent -eq $true -and $state.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($response)
        if ($ok) {
            [void](Complete-DawoudUiAgent -State $script:ui -Name $agentName -Status "DONE" -Message "Antigravity result received" -ExitCode 0 -StreamEvents $(if ($state.StreamEvents) { [int]$state.StreamEvents } else { 0 }))
            $script:lastExecutorResult = $response
            $script:ui.Result = Protect-DawoudTelemetryText -Text $response
            $script:ui.Status = "RUNNING"
            Complete-DawoudTelemetryRecord -Path $record.Path -Status DONE -ExitCode 0 -Summary $response -WorkerPid ([int]$state.ActualPid) -AgyPath $resolved -OutputReturnedFromAGY $true
            Refresh-DawoudUi -Force
            return $true
        }
        $reason = if ($state -and $state.Error) { [string]$state.Error } elseif ($stderrText) { $stderrText } elseif ($dispatchOutput) { (Protect-DawoudTelemetryText -Text $dispatchOutput.Trim()) } else { "orchestrator dispatch exit code $dispatchExit; no result contract returned" }
        [void](Complete-DawoudUiAgent -State $script:ui -Name $agentName -Status "FAILED" -Message $reason -ExitCode $(if ($dispatchExit) { $dispatchExit } else { 1 }) -Timeout $(if ($state -and $state.TimeoutReason) { [string]$state.TimeoutReason } else { "NONE" }))
        Complete-DawoudTelemetryRecord -Path $record.Path -Status ERROR -ExitCode $(if ($dispatchExit) { $dispatchExit } else { 1 }) -Summary $reason -AgyPath $resolved -OutputReturnedFromAGY $false
        Write-Host "AGEX ROUTING FAILURE" -ForegroundColor Red
        Write-Host "Antigravity delegation expected but unavailable." -ForegroundColor Red
        Write-Host ("Reason: {0}" -f $reason) -ForegroundColor Red
        Write-Host "AGY attempted: YES" -ForegroundColor Red
        Write-Host "Fallback performed: NO" -ForegroundColor Red
        return $false
    } catch {
        if ($script:ui -and $agentName) { [void](Complete-DawoudUiAgent -State $script:ui -Name $agentName -Status "FAILED" -Message $_.Exception.Message -ExitCode 1) }
        Complete-DawoudTelemetryRecord -Path $record.Path -Status ERROR -ExitCode 1 -Summary $_.Exception.Message -AgyPath $resolved -OutputReturnedFromAGY $false
        Write-Host "AGEX ROUTING FAILURE" -ForegroundColor Red
        Write-Host ("Reason: {0}" -f (Protect-DawoudTelemetryText -Text $_.Exception.Message)) -ForegroundColor Red
        Write-Host "AGY attempted: YES" -ForegroundColor Red
        Write-Host "Fallback performed: NO" -ForegroundColor Red
        return $false
    } finally {
        if (Test-Path -LiteralPath $dispatchTaskPath) { Remove-Item -LiteralPath $dispatchTaskPath -Force -ErrorAction SilentlyContinue }
    }
}

function Invoke-CodexTask {
    param([Parameter(Mandatory)][string]$Prompt, [Parameter(Mandatory)][string]$WorkId, [Parameter(Mandatory)]$Route)
    $record = New-DawoudTelemetryRecord -TelemetryRoot $telemetryRoot -SessionId $SessionId -Task $Prompt -Executor CODEX -Category $Route.Category -CodexShare $CodexShare -AntigravityShare $AntigravityShare -Leader $ConfiguredLeader -ResolvedLeader $script:ResolvedLeader -RouteReason $Route.Reason -Model $CodexModel -Effort $CodexEffort -WorkId $WorkId -SelectedProjectPath $Project -ExecutorWorkingDirectory $Project -AgyAvailable ([bool](Resolve-AgyExecutable)) -AgySelected $false -CodexAvailable $true -CodexSelected $true -RecordKind TASK
    $process = $null
    try {
        $codexIdentity = Get-DawoudRuntimeIdentity
        Write-Host ("CODEX EXECUTOR IDENTITY: {0}" -f $codexIdentity.Name) -ForegroundColor DarkGray
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $CodexPath
        $psi.WorkingDirectory = $Project
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $args = @("exec", "--ephemeral", "-C", $Project, "--sandbox", "read-only")
        if (-not (Test-Path -LiteralPath (Join-Path $Project ".git") -PathType Container)) { $args += "--skip-git-repo-check" }
        if ($CodexModel) { $args += @("--model", $CodexModel) }
        if ($CodexEffort) { $args += @("--config", "model_reasoning_effort=$CodexEffort") }
        $args += "-"
        Add-ProcessArguments -StartInfo $psi -Arguments $args
        $codexCommand = Get-DawoudSanitizedCommand -Text ((@("codex exec --ephemeral", "-C", $Project, "--sandbox read-only") + $(if ($CodexModel) { @("--model", $CodexModel) } else { @() }) + $(if ($CodexEffort) { @("--config model_reasoning_effort=$CodexEffort") } else { @() }) + @("-")) -join " ")
        $uiTaskId = if ($script:ui.WorkId -and $WorkId.StartsWith($script:ui.WorkId + "-")) { $WorkId.Substring($script:ui.WorkId.Length + 1) } else { $WorkId }
        [void](Start-DawoudUiAgent -State $script:ui -Name "CODEX" -Executor "CODEX" -TaskId $uiTaskId -TaskText $Prompt -Model $CodexModel -Command $codexCommand -WorkingDirectory $Project)
        # Backend uses `codex exec`, not interactive Codex TUI hooks. Do not touch
        # ProcessStartInfo.EnvironmentVariables here: Windows PowerShell can expose
        # that collection as null for a .cmd-backed executable.
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $psi
        [void]$process.Start()
        [void](Update-DawoudUiAgent -State $script:ui -Name "CODEX" -Status "RUNNING" -Action "Waiting for Codex final response" -ProcessId $process.Id -EventKind "START" -Message ("Codex PID {0}" -f $process.Id))
        Refresh-DawoudUi -Force
        $process.StandardInput.Write($Prompt)
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $deadline = (Get-Date).AddSeconds(180)
        $finished = $false
        while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
            Refresh-DawoudUi
            Start-Sleep -Milliseconds 250
        }
        $finished = $process.HasExited
        if ($script:cancellationSignal.Requested) {
            [void](Complete-DawoudUiAgent -State $script:ui -Name "CODEX" -Status "CANCELLED" -Message "User cancelled the active AGEX task." -ExitCode 130)
            Complete-DawoudTelemetryRecord -Path $record.Path -Status CANCELLED -ExitCode 130 -Summary "User cancelled the active AGEX task." -WorkerPid $process.Id -OutputReturnedFromAGY $false
            return $false
        }
        if (-not $finished) {
            try { & taskkill.exe /PID ([string]$process.Id) /T /F 2>$null | Out-Null } catch {}
            Complete-DawoudTelemetryRecord -Path $record.Path -Status ERROR -ExitCode 124 -Summary "Codex backend exceeded 180 second deadline." -WorkerPid $process.Id -OutputReturnedFromAGY $false
            [void](Complete-DawoudUiAgent -State $script:ui -Name "CODEX" -Status "FAILED" -Message "Codex backend exceeded 180 second deadline." -ExitCode 124 -Timeout "TOTAL")
            Write-Host "AGEX ROUTING FAILURE" -ForegroundColor Red
            Write-Host "Reason: Codex backend exceeded 180 second deadline." -ForegroundColor Red
            Write-Host "Fallback performed: NO" -ForegroundColor Red
            return $false
        }
        $process.WaitForExit()
        $output = $stdout.Result.Trim()
        $errorText = $stderr.Result.Trim()
        $ok = $process.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($output)
        if (-not $ok) {
            $reason = if ($errorText) { Protect-DawoudTelemetryText -Text $errorText } else { "Codex backend exited with code $($process.ExitCode) without final response." }
            [void](Complete-DawoudUiAgent -State $script:ui -Name "CODEX" -Status "FAILED" -Message $reason -ExitCode $process.ExitCode)
            Complete-DawoudTelemetryRecord -Path $record.Path -Status ERROR -ExitCode $process.ExitCode -Summary $reason -WorkerPid $process.Id -OutputReturnedFromAGY $false
            Write-Host "AGEX ROUTING FAILURE" -ForegroundColor Red
            Write-Host ("Reason: {0}" -f $reason) -ForegroundColor Red
            Write-Host "Fallback performed: NO" -ForegroundColor Red
            return $false
        }
        [void](Complete-DawoudUiAgent -State $script:ui -Name "CODEX" -Status "DONE" -Message "Codex result received" -ExitCode 0)
        $script:lastExecutorResult = $output
        $script:ui.Result = Protect-DawoudTelemetryText -Text $output
        Complete-DawoudTelemetryRecord -Path $record.Path -Status DONE -ExitCode 0 -Summary $output -WorkerPid $process.Id -OutputReturnedFromAGY $false
        Refresh-DawoudUi -Force
        return $true
    } catch {
        $detail = "line $($_.InvocationInfo.ScriptLineNumber): $($_.Exception.Message)"
        if ($script:ui -and $script:ui.Agents.Contains("CODEX")) { [void](Complete-DawoudUiAgent -State $script:ui -Name "CODEX" -Status "FAILED" -Message $detail -ExitCode 1) }
        Complete-DawoudTelemetryRecord -Path $record.Path -Status ERROR -ExitCode 1 -Summary $detail -WorkerPid $(if ($process) { $process.Id } else { 0 }) -OutputReturnedFromAGY $false
        Write-Host "AGEX ROUTING FAILURE" -ForegroundColor Red
        Write-Host ("Reason: {0}" -f (Protect-DawoudTelemetryText -Text $detail)) -ForegroundColor Red
        Write-Host "Fallback performed: NO" -ForegroundColor Red
        return $false
    } finally {
        if ($process) { $process.Dispose() }
    }
}

function Invoke-DawoudTask {
    param([Parameter(Mandatory)][string]$Prompt)
    $workId = "ui-" + ([guid]::NewGuid().ToString("N"))
    $script:ui.WorkId = $workId
    $script:ui.Prompt = $Prompt
    $script:ui.Status = "RUNNING"
    $script:ui.Result = ""
    $script:ui.Summary = $null
    $script:ui.CancellationProcessesCleaned = "NOT APPLICABLE"
    $script:ui.CancellationOwnedPids = @()
    $script:ui.Tasks.Clear(); $script:ui.Events.Clear(); $script:ui.Agents.Clear(); $script:ui.Files.Clear()
    $script:ui.CurrentAction = $null; $script:ui.CurrentCommand = $null; $script:ui.LastRenderedFingerprint = ""
    Start-DawoudUiFileWatch -State $script:ui
    Invoke-DawoudGoalGraph -RootGoal $Prompt -WorkId $workId
    Complete-DawoudUiSession -State $script:ui
    Write-DawoudCompactSummary -WorkId $workId
}

function Start-DawoudTaskExecution {
    param([Parameter(Mandatory)][string]$Prompt)
    $runspace = [RunspaceFactory]::CreateRunspace()
    $powerShell = [PowerShell]::Create()
    try {
        $runspace.Open()
        $powerShell.Runspace = $runspace
        [void]$powerShell.AddCommand($PSCommandPath)
        [void]$powerShell.AddParameter("Project", $Project)
        [void]$powerShell.AddParameter("MaxWorkers", $MaxWorkers)
        [void]$powerShell.AddParameter("CodexPath", $CodexPath)
        [void]$powerShell.AddParameter("AgyPath", $AgyPath)
        [void]$powerShell.AddParameter("SessionId", $SessionId)
        [void]$powerShell.AddParameter("ConfiguredLeader", $ConfiguredLeader)
        [void]$powerShell.AddParameter("CodexShare", $CodexShare)
        [void]$powerShell.AddParameter("AntigravityShare", $AntigravityShare)
        [void]$powerShell.AddParameter("CodexModel", $CodexModel)
        [void]$powerShell.AddParameter("CodexEffort", $CodexEffort)
        [void]$powerShell.AddParameter("AntigravityModel", $AntigravityModel)
        [void]$powerShell.AddParameter("AntigravityEffort", $AntigravityEffort)
        [void]$powerShell.AddParameter("ExecutePrompt", $Prompt)
        [void]$powerShell.AddParameter("SharedUiState", $script:ui)
        $signal = [hashtable]::Synchronized(@{ Requested = $false })
        [void]$powerShell.AddParameter("CancellationSignal", $signal)
        $async = $powerShell.BeginInvoke()
        $script:activeExecution = [pscustomobject]@{ PowerShell = $powerShell; Runspace = $runspace; Async = $async; Prompt = $Prompt; Started = Get-Date; CancellationSignal = $signal; CancelCount = 0 }
        $script:ui.Status = "RUNNING"
        Refresh-DawoudUi -Force
        return $true
    } catch {
        try { $powerShell.Dispose() } catch { }
        try { $runspace.Dispose() } catch { }
        [void](Add-DawoudUiEvent -State $script:ui -Source "AGEX" -Kind "FAIL" -Message (Protect-DawoudTelemetryText -Text $_.Exception.Message) -Status "FAILED")
        $script:ui.Status = "FAILED"
        Refresh-DawoudUi -Force
        return $false
    }
}

function Complete-DawoudTaskExecution {
    if (-not $script:activeExecution -or -not $script:activeExecution.Async.IsCompleted) { return $false }
    $execution = $script:activeExecution
    $script:activeExecution = $null
    try {
        [void]$execution.PowerShell.EndInvoke($execution.Async)
        $errors = @($execution.PowerShell.Streams.Error)
        if ($errors.Count -gt 0 -and $script:ui.Status -ne "FAILED") {
            [void](Add-DawoudUiEvent -State $script:ui -Source "AGEX" -Kind "WARNING" -Message (Protect-DawoudTelemetryText -Text $errors[-1].ToString()) -Status "WARNING")
        }
    } catch {
        if ($execution.CancellationSignal -and $execution.CancellationSignal.Requested) {
            foreach ($task in @($script:ui.Tasks | Where-Object Status -in @("QUEUED", "STARTING", "RUNNING", "WAITING"))) {
                [void](Set-DawoudUiTask -State $script:ui -TaskId $task.Id -Status "CANCELLED" -Agent $task.Agent)
            }
            $script:ui.Status = "CANCELLED"
            $script:ui.CurrentAction = $null
            [void](Add-DawoudUiEvent -State $script:ui -Source "AGEX" -Kind "CANCEL" -Message "Request cancelled; executor process tree stopped." -Status "CANCELLED")
        } else {
            $script:ui.Status = "FAILED"
            $script:ui.CurrentAction = $null
            [void](Add-DawoudUiEvent -State $script:ui -Source "AGEX" -Kind "FAIL" -Message (Protect-DawoudTelemetryText -Text $_.Exception.Message) -Status "FAILED")
        }
    } finally {
        try { $execution.PowerShell.Dispose() } catch { }
        try { $execution.Runspace.Dispose() } catch { }
        Refresh-DawoudUi -Force
    }
    $true
}

function Pump-DawoudTaskExecution {
    [void](Complete-DawoudTaskExecution)
    if (-not $script:activeExecution -and $script:queuedPrompts.Count -gt 0) {
        $next = $script:queuedPrompts.Dequeue()
        [void](Start-DawoudTaskExecution -Prompt $next)
    }
}

function Get-DawoudOwnedProcessTreeIds {
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

function Get-DawoudActiveExecutionRootPids {
    @($script:ui.Agents.Values | Where-Object { $_.Status -notin @("DONE", "FAILED", "CANCELLED", "IDLE") } | ForEach-Object {
        if ($_.DispatchPID -gt 0) { [int]$_.DispatchPID }
        elseif ($_.PID -gt 0) { [int]$_.PID }
    } | Sort-Object -Unique)
}

function Request-DawoudTaskCancellation {
    if (-not $script:activeExecution) { return }
    $execution = $script:activeExecution
    $execution.CancelCount++
    $execution.CancellationSignal.Requested = $true
    $script:ui.Status = "CANCELLING"
    [void](Add-DawoudUiEvent -State $script:ui -Source "AGEX" -Kind "CANCEL" -Message "Cancelling active request; stopping task-owned process trees." -Status "CANCELLING")
    Refresh-DawoudUi -Force

    # The recorded executor/dispatch PIDs are the roots created by this task.
    # Terminate only those roots and descendants; never select processes by name.
    $ownedPidSet = [System.Collections.Generic.HashSet[int]]::new()
    $treeObserved = $true
    # Capture and stop task-owned roots repeatedly while the background runspace
    # unwinds, covering the STARTING race where a worker PID arrives late.
    $deadline = (Get-Date).AddSeconds(6)
    $killedRoots = [System.Collections.Generic.HashSet[int]]::new()
    do {
        $roots = @(Get-DawoudActiveExecutionRootPids)
        foreach ($rootPid in $roots) {
            if (-not $killedRoots.Add([int]$rootPid)) { continue }
            [void]$ownedPidSet.Add([int]$rootPid)
            try {
                foreach ($treePid in @(Get-DawoudOwnedProcessTreeIds -RootProcessIds @([int]$rootPid))) { [void]$ownedPidSet.Add([int]$treePid) }
            } catch { $treeObserved = $false }
            try { & (Join-Path $env:SystemRoot "System32\taskkill.exe") /PID ([string]$rootPid) /T /F 2>$null | Out-Null } catch { }
        }
        if (-not $script:activeExecution -or $script:activeExecution.Async.IsCompleted -or (Get-Date) -ge $deadline) { break }
        Start-Sleep -Milliseconds 100
    } while ($true)
    $ownedPids = @($ownedPidSet | Sort-Object)
    $script:ui.CancellationOwnedPids = @($ownedPids)
    # Process-tree termination unblocks the backend's existing pipe waits.
    $processesRemain = $false
    foreach ($ownedPid in $ownedPids) {
        if (Get-Process -Id ([int]$ownedPid) -ErrorAction SilentlyContinue) { $processesRemain = $true }
    }
    $completedWithoutProcess = ($ownedPids.Count -eq 0 -and $script:activeExecution -and $script:activeExecution.Async.IsCompleted)
    $script:ui.CancellationProcessesCleaned = if (-not $processesRemain -and (($treeObserved -and $ownedPids.Count -gt 0) -or $completedWithoutProcess)) { "YES" } elseif ($processesRemain) { "NO" } else { "UNVERIFIED" }
    $cleanupText = "Processes cleaned: $($script:ui.CancellationProcessesCleaned)"
    if ($script:activeExecution -and -not $script:activeExecution.Async.IsCompleted) {
        try { $execution.PowerShell.Stop() } catch { }
    }
    if ($script:activeExecution -and $script:activeExecution.Async.IsCompleted) { [void](Complete-DawoudTaskExecution) }
    if ($script:activeExecution) {
        foreach ($agent in @($script:ui.Agents.Values | Where-Object Status -notin @("DONE", "FAILED", "CANCELLED", "IDLE"))) {
            [void](Complete-DawoudUiAgent -State $script:ui -Name $agent.Name -Status "CANCELLED" -Message "User cancelled the active AGEX task." -ExitCode 130)
        }
        foreach ($task in @($script:ui.Tasks | Where-Object Status -in @("QUEUED", "STARTING", "RUNNING", "WAITING"))) {
            [void](Set-DawoudUiTask -State $script:ui -TaskId $task.Id -Status "CANCELLED" -Agent $task.Agent)
        }
        $script:ui.Status = "CANCELLED"
        $script:ui.CurrentAction = $null
        [void](Add-DawoudUiEvent -State $script:ui -Source "AGEX" -Kind "CANCEL" -Message ("Request cancelled. {0}; task-owned PIDs={1}" -f $cleanupText, $ownedPids.Count) -Status "CANCELLED")
        try { $execution.PowerShell.Dispose() } catch { }
        try { $execution.Runspace.Dispose() } catch { }
        $script:activeExecution = $null
        Refresh-DawoudUi -Force
    }
    if (-not $script:activeExecution) {
        [void](Add-DawoudUiEvent -State $script:ui -Source "AGEX" -Kind "CANCEL" -Message ("{0}; task-owned PIDs={1}" -f $cleanupText, $ownedPids.Count) -Status "CANCELLED")
        Refresh-DawoudUi -Force
    }
}

function Read-DawoudKeyWithDeadline {
    param([int]$TimeoutMilliseconds = 100)
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while (-not [Console]::KeyAvailable) {
        if ([DateTime]::UtcNow -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 5
    }
    [Console]::ReadKey($true)
}

function Test-DawoudCommandLine {
    param([Parameter(Mandatory)][string]$Value)
    $Value -match '^:(help|paste|send|cancel|quit|q|clear|details|agents|tasks|files|changes|diff|chat|log|leader\s+(Codex|Antigravity|Auto)|workload\s+\d+\s+\d+|model(?:\s+.*)?|project\s+.*|status|report|history)$'
}

function Read-DawoudDraft {
    # One bounded line editor. Backend receives only returned PROMPT text.
    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        $line = [Console]::In.ReadLine()
        if ($null -eq $line) { return [pscustomobject]@{ Type = "QUIT"; Value = "" } }
        if ($line -match '^:(help|paste|send|cancel|quit|q|clear|details|agents|tasks|files|changes|diff|chat|log|status|report|history)$') { return [pscustomobject]@{ Type = "COMMAND"; Value = $line } }
        return [pscustomobject]@{ Type = "PROMPT"; Value = $line }
    }
    $esc = [char]27
    $maxChars = 262144
    $pasteTimeoutMs = 15000
    $bodyHeight = 14
    $state = @{
        Lines = [System.Collections.Generic.List[System.Text.StringBuilder]]::new()
        Line = 0
        Column = 0
        PreferredColumn = -1
        SelectAll = $false
        Truncated = $false
        Unicode = [bool]([Console]::OutputEncoding.CodePage -eq 65001)
        RenderRows = 0
        RenderTop = 0
    }
    [void]$state.Lines.Add([System.Text.StringBuilder]::new())

    $recount = {
        $n = 0
        for ($i = 0; $i -lt $state.Lines.Count; $i++) {
            $n += $state.Lines[$i].Length
            if ($i -gt 0) { $n++ }
        }
        $state.CharCount = $n
    }
    & $recount
    $text = {
        $parts = [System.Collections.Generic.List[string]]::new()
        foreach ($line in $state.Lines) { [void]$parts.Add($line.ToString()) }
        $parts -join "`n"
    }
    $clear = {
        $state.Lines.Clear()
        [void]$state.Lines.Add([System.Text.StringBuilder]::new())
        $state.Line = 0
        $state.Column = 0
        $state.PreferredColumn = -1
        $state.SelectAll = $false
        $state.Truncated = $false
        & $recount
    }
    $replaceAllIfSelected = {
        if ($state.SelectAll) { & $clear }
    }
    $insert = {
        param([string]$Value)
        if ($null -eq $Value -or $Value.Length -eq 0) { return }
        & $replaceAllIfSelected
        & $recount
        $available = $maxChars - [int]$state.CharCount
        if ($available -le 0) { $state.Truncated = $true; return }
        $normalized = $Value.Replace("`r`n", "`n").Replace("`r", "`n")
        if ($normalized.Length -gt $available) {
            $normalized = $normalized.Substring(0, $available)
            $state.Truncated = $true
        }
        $lineIndex = $state.Line
        $current = $state.Lines[$lineIndex].ToString()
        $before = $current.Substring(0, $state.Column)
        $after = $current.Substring($state.Column)
        $parts = [System.Text.RegularExpressions.Regex]::Split($normalized, "`n")
        if ($parts.Count -eq 1) {
            $state.Lines[$lineIndex] = [System.Text.StringBuilder]::new($before + $parts[0] + $after)
            $state.Column += $parts[0].Length
        } else {
            $replacement = [System.Collections.Generic.List[System.Text.StringBuilder]]::new()
            [void]$replacement.Add([System.Text.StringBuilder]::new($before + $parts[0]))
            for ($i = 1; $i -lt $parts.Count - 1; $i++) { [void]$replacement.Add([System.Text.StringBuilder]::new($parts[$i])) }
            [void]$replacement.Add([System.Text.StringBuilder]::new($parts[$parts.Count - 1] + $after))
            $state.Lines.RemoveAt($lineIndex)
            for ($i = 0; $i -lt $replacement.Count; $i++) { $state.Lines.Insert($lineIndex + $i, $replacement[$i]) }
            $state.Line = $lineIndex + $replacement.Count - 1
            $state.Column = $parts[$parts.Count - 1].Length
        }
        $state.PreferredColumn = -1
        $state.SelectAll = $false
        & $recount
    }
    $backspace = {
        if ($state.SelectAll) { & $clear; return }
        if ($state.Column -gt 0) {
            [void]$state.Lines[$state.Line].Remove($state.Column - 1, 1)
            $state.Column--
        } elseif ($state.Line -gt 0) {
            $previous = $state.Line - 1
            $newColumn = $state.Lines[$previous].Length
            [void]$state.Lines[$previous].Append($state.Lines[$state.Line].ToString())
            $state.Lines.RemoveAt($state.Line)
            $state.Line = $previous
            $state.Column = $newColumn
        }
        $state.PreferredColumn = -1
        & $recount
    }
    $delete = {
        if ($state.SelectAll) { & $clear; return }
        if ($state.Column -lt $state.Lines[$state.Line].Length) {
            [void]$state.Lines[$state.Line].Remove($state.Column, 1)
        } elseif ($state.Line -lt $state.Lines.Count - 1) {
            [void]$state.Lines[$state.Line].Append($state.Lines[$state.Line + 1].ToString())
            $state.Lines.RemoveAt($state.Line + 1)
        }
        $state.PreferredColumn = -1
        & $recount
    }
    $moveHorizontal = {
        param([int]$Direction)
        $state.SelectAll = $false
        if ($Direction -lt 0) {
            if ($state.Column -gt 0) { $state.Column-- }
            elseif ($state.Line -gt 0) { $state.Line--; $state.Column = $state.Lines[$state.Line].Length }
        } else {
            if ($state.Column -lt $state.Lines[$state.Line].Length) { $state.Column++ }
            elseif ($state.Line -lt $state.Lines.Count - 1) { $state.Line++; $state.Column = 0 }
        }
        $state.PreferredColumn = -1
    }
    $moveWord = {
        param([int]$Direction)
        $state.SelectAll = $false
        $line = $state.Lines[$state.Line].ToString()
        if ($Direction -lt 0) {
            while ($state.Column -gt 0 -and [char]::IsWhiteSpace($line[$state.Column - 1])) { $state.Column-- }
            while ($state.Column -gt 0 -and -not [char]::IsWhiteSpace($line[$state.Column - 1])) { $state.Column-- }
        } else {
            while ($state.Column -lt $line.Length -and [char]::IsWhiteSpace($line[$state.Column])) { $state.Column++ }
            while ($state.Column -lt $line.Length -and -not [char]::IsWhiteSpace($line[$state.Column])) { $state.Column++ }
        }
        $state.PreferredColumn = -1
    }
    $moveVertical = {
        param([int]$Direction)
        $state.SelectAll = $false
        if ($state.PreferredColumn -lt 0) { $state.PreferredColumn = $state.Column }
        $target = $state.Line + $Direction
        if ($target -ge 0 -and $target -lt $state.Lines.Count) {
            $state.Line = $target
            $state.Column = [math]::Min($state.PreferredColumn, $state.Lines[$target].Length)
        }
    }
    $render = {
        $width = [math]::Max(8, [Console]::WindowWidth)
        $inner = [math]::Max(20, $width - 4)
        $visibleBottom = [Console]::WindowTop + [Console]::WindowHeight
        $maxBody = [math]::Max(2, $visibleBottom - $state.RenderTop - 3)
        $visibleHeight = [math]::Min($bodyHeight, $maxBody)
        $top = [math]::Max([Console]::WindowTop, [math]::Min($state.RenderTop, [Console]::WindowTop + [Console]::WindowHeight - $visibleHeight - 2))
        for ($row = 0; $row -lt $state.RenderRows; $row++) {
            $y = [math]::Min([Console]::BufferHeight - 1, $top + $row)
            [Console]::SetCursorPosition(0, $y)
            [Console]::Write((" " * [math]::Max(1, $width - 1)))
        }
        $segments = [System.Collections.Generic.List[object]]::new()
        $startLine = [math]::Max(0, $state.Line - 5)
        $endLine = [math]::Min($state.Lines.Count - 1, $state.Line + 5)
        for ($li = $startLine; $li -le $endLine; $li++) {
            $value = $state.Lines[$li].ToString()
            if ($value.Length -eq 0) { [void]$segments.Add([pscustomobject]@{ Logical = $li; Offset = 0; Value = "" }); continue }
            $firstChunk = 0
            $lastChunk = [math]::Ceiling($value.Length / [double]$inner) - 1
            if ($li -eq $state.Line) {
                $focus = [math]::Min($lastChunk, [math]::Floor($state.Column / [double]$inner))
                $firstChunk = [math]::Max(0, $focus - 4)
                $lastChunk = [math]::Min($lastChunk, $focus + 5)
            } elseif ($lastChunk -gt 0) { $lastChunk = 0 }
            for ($chunk = $firstChunk; $chunk -le $lastChunk; $chunk++) {
                $offset = $chunk * $inner
                $length = [math]::Min($inner, $value.Length - $offset)
                [void]$segments.Add([pscustomobject]@{ Logical = $li; Offset = $offset; Value = $value.Substring($offset, $length) })
            }
        }
        $currentSegment = 0
        for ($i = 0; $i -lt $segments.Count; $i++) {
            if ($segments[$i].Logical -eq $state.Line -and $state.Column -ge $segments[$i].Offset -and $state.Column -le ($segments[$i].Offset + $inner)) { $currentSegment = $i; break }
        }
        $first = [math]::Max(0, $currentSegment - 6)
        $last = [math]::Min($segments.Count - 1, $first + $visibleHeight - 1)
        $visible = @($segments[$first..$last])
        $lineCount = $state.Lines.Count
        $status = if ($state.SelectAll) { "ALL SELECTED" } elseif ($state.Truncated) { "LIMIT $maxChars" } else { "READY" }
        $tl = if ($state.Unicode) { [string][char]0x256D } else { "+" }
        $tr = if ($state.Unicode) { [string][char]0x256E } else { "+" }
        $bl = if ($state.Unicode) { [string][char]0x2570 } else { "+" }
        $br = if ($state.Unicode) { [string][char]0x256F } else { "+" }
        $h = if ($state.Unicode) { [string][char]0x2500 } else { "-" }
        $v = if ($state.Unicode) { [string][char]0x2502 } else { "|" }
        $topRule = -join (1..([math]::Max(1, $width - 10)) | ForEach-Object { $h })
        $blankRule = -join (1..([math]::Max(1, $width - 2)) | ForEach-Object { " " })
        $rows = [System.Collections.Generic.List[string]]::new()
        [void]$rows.Add("$tl$h Draft $topRule$tr")
        foreach ($segment in $visible) {
            $marker = if ($segment.Logical -eq $state.Line) { ">" } else { " " }
            $body = ("$v$marker " + $segment.Value).PadRight($width - 1)
            [void]$rows.Add($body.Substring(0, $width - 1) + $v)
        }
        while ($rows.Count -lt ($visibleHeight + 1)) { [void]$rows.Add("$v$blankRule$v") }
        $footer = ("{0}{1} {2} lines {3} {4} chars" -f $bl, $h, $lineCount, $(if ($state.Unicode) { [string][char]0x2022 } else { "-" }), $state.CharCount)
        if (-not $state.Unicode) { $footer = ("+-- {0} lines - {1} chars" -f $lineCount, $state.CharCount) }
        if ($footer.Length -gt $width - 12) { $footer = $footer.Substring(0, [math]::Max(1, $width - 12)) }
        [void]$rows.Add(($footer.PadRight($width - 12) + $status.PadLeft(10) + $br).PadRight($width - 1))
        for ($i = 0; $i -lt $rows.Count; $i++) {
            $y = [math]::Min([Console]::BufferHeight - 1, $top + $i)
            [Console]::SetCursorPosition(0, $y)
            [Console]::Write($rows[$i].PadRight($width - 1))
        }
        $cursorRow = 1
        for ($i = 0; $i -lt $visible.Count; $i++) { if ($visible[$i].Logical -eq $state.Line -and $state.Column -ge $visible[$i].Offset -and $state.Column -le ($visible[$i].Offset + $inner)) { $cursorRow = $i + 1; break } }
        $cursorCol = [math]::Min($width - 2, 3 + [math]::Max(0, $state.Column - $visible[$cursorRow - 1].Offset))
        [Console]::SetCursorPosition($cursorCol, [math]::Min([Console]::BufferHeight - 1, $top + $cursorRow))
        $state.RenderRows = $rows.Count
        if ($script:ui) { $script:ui.EditorCursorRow = [math]::Min([Console]::BufferHeight - 1, $top + $cursorRow); $script:ui.EditorCursorCol = $cursorCol }
    }
    $restoreEditorCursor = {
        if ($script:ui -and $script:ui.EditorCursorRow -ge 0 -and $script:ui.EditorCursorRow -lt [Console]::BufferHeight) {
            [Console]::SetCursorPosition([math]::Min($script:ui.EditorCursorCol, [Console]::BufferWidth - 1), $script:ui.EditorCursorRow)
        }
    }
    $maybeRender = {
        $queued = $false
        try { $queued = [Console]::KeyAvailable } catch {}
        if (-not $queued) { & $render }
    }
    $readPaste = {
        param([hashtable]$EditorState)
        $prefix = [System.Text.StringBuilder]::new()
        foreach ($expected in @('[', '2', '0', '0', '~')) {
            $next = Read-DawoudKeyWithDeadline -TimeoutMilliseconds 150
            if ($null -eq $next) { return $false }
            [void]$prefix.Append($next.KeyChar)
        }
        if ($prefix.ToString() -ne '[200~') { return $false }
        $end = "$esc[201~"
        $payload = [System.Text.StringBuilder]::new()
        $tail = [System.Text.StringBuilder]::new()
        $deadline = [DateTime]::UtcNow.AddMilliseconds($pasteTimeoutMs)
        $closed = $false
        Write-Host "PASTING..."
        $feedbackAt = [DateTime]::UtcNow
        $pasteLines = 1
        while ([DateTime]::UtcNow -lt $deadline) {
            $next = Read-DawoudKeyWithDeadline -TimeoutMilliseconds 100
            if ($null -eq $next) { continue }
            if (([DateTime]::UtcNow - $feedbackAt).TotalMilliseconds -ge 500) {
                [Console]::Write(("`rPASTING... Lines: {0} Size: {1:N1} Kchars   " -f $pasteLines, ($payload.Length / 1024)))
                $feedbackAt = [DateTime]::UtcNow
            }
            if ($next.KeyChar -eq "`n") { $pasteLines++ }
            [void]$tail.Append($next.KeyChar)
            while ($tail.Length -gt $end.Length) {
                if ($payload.Length -lt $maxChars) { [void]$payload.Append($tail[0]) } else { $EditorState.Truncated = $true }
                [void]$tail.Remove(0, 1)
            }
            if ($tail.ToString() -eq $end) { $closed = $true; break }
        }
        if (-not $closed) { $EditorState.PasteTimedOut = $true; return $false }
        & $insert $payload.ToString()
        Write-Host ("`rPASTE READY. Received {0} lines, {1:N1} Kchars. Explicit send required." -f $pasteLines, ($payload.Length / 1024))
        $true
    }
    $oldTreatControlC = [Console]::TreatControlCAsInput
    $bracketedPasteEnabled = $false
    $result = $null
    try {
        [Console]::TreatControlCAsInput = $true
        [Console]::Write("$esc[?2004h")
        $bracketedPasteEnabled = $true
        if ($script:ui) { $script:ui.EditorRender = $render; $script:ui.EditorRestore = $restoreEditorCursor }
        Write-Host ""
        $state.RenderTop = [Console]::CursorTop
        & $render
        while ($true) {
            $key = Read-DawoudKeyWithDeadline -TimeoutMilliseconds 150
            if ($null -eq $key) {
                Pump-DawoudTaskExecution
                Refresh-DawoudUi
                continue
            }
            if ($null -eq $key) { $result = [pscustomobject]@{ Type = "QUIT"; Value = "" }; break }
            $ctrl = (($key.Modifiers -band [ConsoleModifiers]::Control) -ne 0)
            $shift = (($key.Modifiers -band [ConsoleModifiers]::Shift) -ne 0)
            if ($ctrl -and $key.Key -eq [ConsoleKey]::C) { $result = [pscustomobject]@{ Type = "CANCEL"; Value = "" }; break }
            if ($ctrl -and $key.Key -eq [ConsoleKey]::L) { Write-DawoudHeader; & $maybeRender; continue }
            if ($ctrl -and $key.Key -eq [ConsoleKey]::A) { $state.SelectAll = $true; $state.Line = 0; $state.Column = 0; & $maybeRender; continue }
            if ($ctrl -and $key.Key -eq [ConsoleKey]::U) { & $clear; & $maybeRender; continue }
            if ($ctrl -and $key.Key -eq [ConsoleKey]::K) {
                if ($state.SelectAll) { & $clear } else {
                    [void]$state.Lines[$state.Line].Remove($state.Column, $state.Lines[$state.Line].Length - $state.Column)
                    while ($state.Lines.Count -gt $state.Line + 1) { $state.Lines.RemoveAt($state.Lines.Count - 1) }
                    & $recount
                }
                & $maybeRender
                continue
            }
            if ($key.Key -eq [ConsoleKey]::Escape) {
                $pasteResult = & $readPaste $state
                if ($pasteResult) { & $maybeRender; continue }
                if ($state.PasteTimedOut) { $state.PasteTimedOut = $false; Write-Host "Paste cancelled: end marker not received within $pasteTimeoutMs ms." -ForegroundColor Yellow }
                else { $result = [pscustomobject]@{ Type = "CANCEL"; Value = "" }; break }
            }
            if ($key.Key -eq [ConsoleKey]::Backspace) {
                if ($ctrl) { & $moveWord -Direction -1; & $backspace } else { & $backspace }
                & $maybeRender; continue
            }
            if ($key.Key -eq [ConsoleKey]::Delete) { & $delete; & $maybeRender; continue }
            if ($key.Key -eq [ConsoleKey]::LeftArrow) { if ($ctrl) { & $moveWord -Direction -1 } else { & $moveHorizontal -Direction -1 }; & $maybeRender; continue }
            if ($key.Key -eq [ConsoleKey]::RightArrow) { if ($ctrl) { & $moveWord -Direction 1 } else { & $moveHorizontal -Direction 1 }; & $maybeRender; continue }
            if ($key.Key -eq [ConsoleKey]::UpArrow) { & $moveVertical -Direction -1; & $maybeRender; continue }
            if ($key.Key -eq [ConsoleKey]::DownArrow) { & $moveVertical -Direction 1; & $maybeRender; continue }
            if ($key.Key -eq [ConsoleKey]::Home) { $state.SelectAll = $false; $state.Column = 0; if ($ctrl) { $state.Line = 0 }; $state.PreferredColumn = -1; & $maybeRender; continue }
            if ($key.Key -eq [ConsoleKey]::End) { $state.SelectAll = $false; $state.Column = $state.Lines[$state.Line].Length; if ($ctrl) { $state.Line = $state.Lines.Count - 1; $state.Column = $state.Lines[$state.Line].Length }; $state.PreferredColumn = -1; & $maybeRender; continue }
            if ($key.Key -eq [ConsoleKey]::Enter) {
                $current = & $text
                if ($ctrl) {
                    if ($state.Truncated) { Write-Host "Draft exceeds the 262144 character limit. Shorten it before sending." -ForegroundColor Yellow; continue }
                    if (-not [string]::IsNullOrWhiteSpace($current)) { $result = [pscustomobject]@{ Type = "PROMPT"; Value = $current }; break }
                    continue
                }
                if ($current -eq ":send" -or $current.EndsWith("`n:send")) {
                    if ($state.Truncated) { Write-Host "Draft exceeds the 262144 character limit. Shorten it before sending." -ForegroundColor Yellow; continue }
                    $value = if ($current -eq ":send") { "" } else { $current.Substring(0, $current.Length - 5) }
                    $result = [pscustomobject]@{ Type = "PROMPT"; Value = $value.TrimEnd("`n") }; break
                }
                if ($current -eq ":cancel" -or $current.EndsWith("`n:cancel")) { $result = [pscustomobject]@{ Type = "CANCEL"; Value = "" }; break }
                if ($current.Length -gt 0 -and $current -notmatch "`n" -and (Test-DawoudCommandLine -Value $current)) { $result = [pscustomobject]@{ Type = "COMMAND"; Value = $current }; break }
                & $insert "`n"; & $maybeRender; continue
            }
            if ($key.KeyChar -ne [char]0) { & $insert ([string]$key.KeyChar); & $maybeRender }
        }
    } finally {
        if ($script:ui) { $script:ui.EditorRender = $null; $script:ui.EditorRestore = $null; $script:ui.EditorCursorRow = -1; $script:ui.EditorCursorCol = -1 }
        if ($bracketedPasteEnabled) { [Console]::Write("$esc[?2004l") }
        [Console]::TreatControlCAsInput = $oldTreatControlC
        $width = [math]::Max(8, [Console]::WindowWidth)
        $top = [math]::Min([Console]::BufferHeight - 1, $state.RenderTop)
        for ($row = 0; $row -lt $state.RenderRows; $row++) {
            $y = [math]::Min([Console]::BufferHeight - 1, $top + $row)
            [Console]::SetCursorPosition(0, $y)
            [Console]::Write((" " * [math]::Max(1, $width - 1)))
        }
        [Console]::SetCursorPosition(0, [math]::Min([Console]::BufferHeight - 1, $top))
        Write-Host ""
        if ($result -and $result.Type -eq "CANCEL") { Write-Host "Draft cancelled." -ForegroundColor DarkGray }
    }
    if ($null -eq $result) { return [pscustomobject]@{ Type = "QUIT"; Value = "" } }
    $result
}

function Show-DawoudHelp {
    Write-Host "KEYS"
    Write-Host "  Enter        new line       Ctrl+Enter   send"
    Write-Host "  Arrows       move cursor     Home/End     line start/end"
    Write-Host "  Ctrl+Arrows  word move       Ctrl+A       select all"
    Write-Host "  Ctrl+U       clear draft     Ctrl+K       clear to end"
    Write-Host "  Ctrl+L       redraw dashboard"
    Write-Host "  Backspace/Delete work across lines; Esc/Ctrl+C cancel draft"
    Write-Host "COMMANDS"
    Write-Host ":send       submit current draft"
    Write-Host ":cancel     discard current draft"
    Write-Host ":paste      show multiline paste instructions"
    Write-Host ":leader X   change Codex, Antigravity, or Auto without leaving UI"
    Write-Host ":workload X Y  set Codex X% and AGY Y%"
    Write-Host ":model      show or set selected models"
    Write-Host ":project P  change selected project without leaving UI"
    Write-Host ":status     show compact agent/backend status"
    Write-Host ":report     show full execution report"
    Write-Host ":history    show conversation history"
    Write-Host ":clear      clear display"
    Write-Host ":details    expanded execution details"
    Write-Host ":agents     focus agent panel"
    Write-Host ":tasks      focus task view"
    Write-Host ":chat       operational messages"
    Write-Host ":changes    observed file changes"
    Write-Host ":diff       Git change statistics on demand"
    Write-Host ":files      focus file view"
    Write-Host ":log        show bounded event log"
    Write-Host ":quit       clean exit"
    Write-Host "Draft limit: 262144 characters. Paste timeout: 15 seconds."
}

function Invoke-DawoudCommand {
    param([Parameter(Mandatory)][string]$Command)
    $parts = @($Command.Trim() -split '\s+')
    switch ($parts[0].ToLowerInvariant()) {
        ":help" { Show-DawoudHelp }
        ":paste" { Write-Host "Paste mode: type/paste all lines. Type :send on its own line. No line executes before :send." -ForegroundColor DarkGray }
        ":quit" { return "QUIT" }
        ":q" { return "QUIT" }
        ":cancel" { Write-Host "Draft cancelled." -ForegroundColor DarkGray }
        ":clear" { if ($script:ui -and $script:ui.WorkId) { $script:ui.LastRenderedFingerprint = ""; Refresh-DawoudUi -Force } else { Write-DawoudHeader } }
        ":details" { if ($script:ui) { Set-DawoudUiView -State $script:ui -View "DETAILS" } }
        ":agents" { if ($script:ui) { Set-DawoudUiView -State $script:ui -View "AGENTS" } }
        ":changes" { if ($script:ui) { Update-DawoudChanges -State $script:ui; Set-DawoudUiView -State $script:ui -View "FILES" } }
        ":chat" { if ($script:ui) { Set-DawoudUiView -State $script:ui -View "CHAT" } }
        ":diff" { Update-DawoudChanges -State $script:ui -IncludeDiff; Set-DawoudUiView -State $script:ui -View "DIFF" }
        ":tasks" { if ($script:ui) { Set-DawoudUiView -State $script:ui -View "TASKS" } }
        ":files" { if ($script:ui) { Update-DawoudChanges -State $script:ui; Set-DawoudUiView -State $script:ui -View "FILES" } }
        ":log" { if ($script:ui) { Set-DawoudUiView -State $script:ui -View "LOG" } }
        ":leader" {
            if ($parts.Count -lt 2 -or @("Codex", "Antigravity", "Auto") -notcontains $parts[1]) { Write-Host "Use :leader Codex|Antigravity|Auto" -ForegroundColor Yellow }
            else { $script:ConfiguredLeader = $parts[1]; $script:ResolvedLeader = Resolve-DawoudLeader -ConfiguredLeader $script:ConfiguredLeader -CodexShare $CodexShare -AntigravityShare $AntigravityShare; Write-DawoudHeader }
        }
        ":workload" {
            $c = 0; $a = 0
            if ($parts.Count -ge 3 -and [int]::TryParse($parts[1], [ref]$c) -and [int]::TryParse($parts[2], [ref]$a) -and $c -ge 0 -and $a -ge 0 -and $c + $a -eq 100) { $script:CodexShare = $c; $script:AntigravityShare = $a; $script:ResolvedLeader = Resolve-DawoudLeader -ConfiguredLeader $ConfiguredLeader -CodexShare $script:CodexShare -AntigravityShare $script:AntigravityShare; Write-DawoudHeader }
            else { Write-Host "Use :workload CODEX_PERCENT AGY_PERCENT; total must equal 100." -ForegroundColor Yellow }
        }
        ":model" {
            if ($parts.Count -eq 1) { Write-Host "Codex: $CodexModel / $CodexEffort; AGY: $AntigravityModel / $AntigravityEffort" }
            elseif ($parts.Count -ge 4 -and $parts[1].ToLowerInvariant() -eq "codex") { $script:CodexModel = $parts[2]; $script:CodexEffort = $parts[3]; Write-DawoudHeader }
            elseif ($parts.Count -ge 4 -and @("agy", "antigravity") -contains $parts[1].ToLowerInvariant()) { $script:AntigravityModel = $parts[2]; $script:AntigravityEffort = $parts[3]; Write-DawoudHeader }
            else { Write-Host "Use :model codex|agy MODEL EFFORT" -ForegroundColor Yellow }
        }
        ":project" {
            if ($parts.Count -lt 2) { Write-Host "Use :project PATH" -ForegroundColor Yellow; break }
            $candidate = ($Command.Substring(8)).Trim().Trim('"')
            if (Test-Path -LiteralPath $candidate -PathType Container) { $script:Project = (Resolve-Path -LiteralPath $candidate).Path; Write-DawoudHeader }
            else { Write-Host "Project directory not found: $candidate" -ForegroundColor Yellow }
        }
        ":status" {
            $agy = if ($script:lastAgyPath -and (Test-Path -LiteralPath $script:lastAgyPath -PathType Leaf)) { "installed: YES; $script:lastAgyPath" } else { "installed: NO" }
            Write-Host "AGEX STATUS"
            Write-Host "Leader: $ConfiguredLeader | Project: $Project"
            Write-Host "Codex: selected backend | AGY: $agy"
            Write-Host "Models: Codex $CodexModel/$CodexEffort | AGY $AntigravityModel/$AntigravityEffort"
        }
        ":report" {
            $report = Get-DawoudExecutionReport -TelemetryRoot $telemetryRoot -SessionId $SessionId -CodexShare $CodexShare -AntigravityShare $AntigravityShare
            if ($report) { Write-Host $report.Text } else { Write-Host "No completed execution yet." }
        }
        ":history" {
            if ($script:history.Count -eq 0) { Write-Host "No conversation history." }
            else { foreach ($item in $script:history) { Write-Host ("{0}: {1}" -f $item.Role, $item.Text) } }
        }
        default { Write-Host "Unknown command. Use :help." -ForegroundColor Yellow }
    }
    "CONTINUE"
}

if ($DefinitionsOnly) { return }
if ($ExecutorPrompt) {
    try { $ExecutorResult.Success = [bool](Invoke-DawoudGraphExecutor -Agent $AssignedExecutor -Prompt $ExecutorPrompt -WorkId $ExecutorWorkId); $ExecutorResult.Output = $script:lastExecutorResult }
    finally { $ExecutorResult.Completed = $true }
    return
}

if (-not [string]::IsNullOrWhiteSpace($ExecutePrompt)) {
    Invoke-DawoudTask -Prompt $ExecutePrompt
    return
}

$script:activeExecution = $null
$script:queuedPrompts = [System.Collections.Generic.Queue[string]]::new()
Write-DawoudHeader
try {
    while ($true) {
        Pump-DawoudTaskExecution
        $input = Read-DawoudDraft
        Pump-DawoudTaskExecution
        if ($input.Type -eq "QUIT") {
            if ($script:activeExecution -or $script:queuedPrompts.Count -gt 0) { Write-Host "Task still running. Wait for completion before quitting." -ForegroundColor Yellow; continue }
            break
        }
        if ($input.Type -eq "COMMAND") {
            if ((Invoke-DawoudCommand -Command $input.Value) -eq "QUIT") {
                if ($script:activeExecution -or $script:queuedPrompts.Count -gt 0) { Write-Host "Task still running. Wait for completion before quitting." -ForegroundColor Yellow; continue }
                break
            }
            continue
        }
        if ($input.Type -eq "CANCEL") {
            if ($script:activeExecution) { Request-DawoudTaskCancellation }
            continue
        }
        if ($input.Type -eq "PROMPT" -and -not [string]::IsNullOrWhiteSpace($input.Value)) {
            if ($script:history.Count -ge 50) { $script:history.RemoveAt(0) }
            [void]$script:history.Add([pscustomobject]@{ Role = "USER"; Text = $input.Value })
            if ($script:activeExecution -or $script:queuedPrompts.Count -gt 0) {
                if ($script:queuedPrompts.Count -ge 8) { Write-Host "Prompt queue full (8). Wait for an active task." -ForegroundColor Yellow; continue }
                $script:queuedPrompts.Enqueue($input.Value)
                [void](Add-DawoudUiEvent -State $script:ui -Source "AGEX" -Kind "QUEUED" -Message ("Queued prompt {0}; waiting for active task" -f ($script:queuedPrompts.Count)) -Status "WAITING")
                Refresh-DawoudUi -Force
            } else { [void](Start-DawoudTaskExecution -Prompt $input.Value) }
        }
    }
} finally {
    if ($script:activeExecution -and -not $script:activeExecution.Async.IsCompleted) {
        try { $script:activeExecution.PowerShell.Stop() } catch { }
        try { $script:activeExecution.PowerShell.Dispose() } catch { }
        try { $script:activeExecution.Runspace.Dispose() } catch { }
    }
    $finalReport = Get-DawoudExecutionReport -TelemetryRoot $telemetryRoot -SessionId $SessionId -CodexShare $CodexShare -AntigravityShare $AntigravityShare
    if ($finalReport) { Write-Host ""; Write-Host $finalReport.Text }
}
exit 0
