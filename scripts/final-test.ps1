[CmdletBinding()]
param(
    [string]$Project,
    [switch]$AgyOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$orchestratorScript = Join-Path $PSScriptRoot "orchestrator.ps1"
$commonPath = Join-Path $PSScriptRoot "dawoud-common.ps1"
. $commonPath
$codexHome = if (-not [string]::IsNullOrWhiteSpace($env:CODEX_HOME)) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE ".codex" }
$preferences = Get-DawoudPreferences -Path (Join-Path $codexHome "dawoud-settings.json")

if ([string]::IsNullOrWhiteSpace($Project)) {
    $codexHome = if (-not [string]::IsNullOrWhiteSpace($env:CODEX_HOME)) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE ".codex" }
    $settingsPath = Join-Path $codexHome "dawoud-settings.json"
    $registryPath = Join-Path $codexHome "workbench-projects.json"
    $candidates = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        try {
            $settings = Get-DawoudPreferences -Path $settingsPath
            if (-not [string]::IsNullOrWhiteSpace([string]$settings.last_project)) { [void]$candidates.Add([string]$settings.last_project) }
        } catch { }
    }
    if (Test-Path -LiteralPath $registryPath -PathType Leaf) {
        try {
            $registry = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json
            foreach ($entry in @($registry.projects)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$entry.path)) { [void]$candidates.Add([string]$entry.path) }
            }
        } catch { }
    }
    foreach ($candidate in @($candidates)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Container)) {
            $Project = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }
}

$runtime = Get-DawoudRuntimeIdentity
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logRoot = Join-Path $root ("reports\final-test\{0}" -f $stamp)
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

function Write-TestLine {
    param([string]$Text)
    Write-Host $Text
    try { Add-Content -LiteralPath (Join-Path $logRoot "summary.log") -Value $Text -Encoding utf8 -ErrorAction Stop } catch { }
}

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [string]$InputText,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$Name,
        [string]$StartupPath,
        [int]$StartupTimeoutSeconds = 0,
        [switch]$PreserveOutput
    )
    $stdoutPath = Join-Path $logRoot "$Name.stdout.log"
    $stderrPath = Join-Path $logRoot "$Name.stderr.log"
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FileName
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($psi.PSObject.Properties.Name -contains "ArgumentList" -and $null -ne $psi.ArgumentList) {
        foreach ($arg in $Arguments) { [void]$psi.ArgumentList.Add([string]$arg) }
    } else {
        $psi.Arguments = (($Arguments | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join ' ')
    }
    $process = [Diagnostics.Process]::new()
    $started = Get-Date
    $processId = 0
    $pipeTimeout = $false
    $timeoutReason = "NONE"
    $startupTrace = ""
    try {
        $process.StartInfo = $psi
        [void]$process.Start()
        $processId = $process.Id
        Write-Host "$Name PID: $processId"
        if ($null -ne $InputText) {
            $process.StandardInput.Write($InputText)
        }
        $process.StandardInput.Close()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timedOut = $false
        if ($StartupPath -and $StartupTimeoutSeconds -gt 0) {
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            $startupDeadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
            while (-not $process.HasExited) {
                if (Test-Path -LiteralPath $StartupPath -PathType Leaf) { try { $startupTrace = [IO.File]::ReadAllText($StartupPath) } catch { } }
                if ((Get-Date) -ge $startupDeadline) {
                    $timedOut = $true
                    $timeoutReason = if ($startupTrace -notmatch "WORKER_STARTED") { "STARTUP_MILESTONE_WORKER_STARTED" } elseif ($startupTrace -notmatch "AGY_PROCESS_STARTED") { "STARTUP_MILESTONE_AGY_PROCESS_STARTED" } elseif ($startupTrace -notmatch "STDIN_WRITTEN") { "STARTUP_MILESTONE_STDIN_WRITTEN" } elseif ($startupTrace -notmatch "STDIN_FLUSHED") { "STARTUP_MILESTONE_STDIN_FLUSHED" } else { "STARTUP_MILESTONE_COMPLETE" }
                    break
                }
                if ((Get-Date) -ge $deadline) { $timedOut = $true; $timeoutReason = "HARNESS_TOTAL"; break }
                Start-Sleep -Milliseconds 250
            }
            if (Test-Path -LiteralPath $StartupPath -PathType Leaf) { try { $startupTrace = [IO.File]::ReadAllText($StartupPath) } catch { } }
            if (-not $timedOut) {
                if ($startupTrace -notmatch "WORKER_STARTED") { $timeoutReason = "STARTUP_MILESTONE_WORKER_STARTED" }
                elseif ($startupTrace -notmatch "AGY_PROCESS_STARTED") { $timeoutReason = "STARTUP_MILESTONE_AGY_PROCESS_STARTED" }
                elseif ($startupTrace -notmatch "STDIN_WRITTEN") { $timeoutReason = "STARTUP_MILESTONE_STDIN_WRITTEN" }
                elseif ($startupTrace -notmatch "STDIN_FLUSHED") { $timeoutReason = "STARTUP_MILESTONE_STDIN_FLUSHED" }
            }
        } else {
            $finished = $process.WaitForExit($TimeoutSeconds * 1000)
            $timedOut = -not $finished
            if ($timedOut) { $timeoutReason = "HARNESS_TOTAL" }
        }
        if ($timedOut) {
            try { & taskkill.exe /PID ([string]$processId) /T /F 2>$null | Out-Null } catch { }
            try { $process.StandardOutput.BaseStream.Close() } catch { }
            try { $process.StandardError.BaseStream.Close() } catch { }
            [void]$process.WaitForExit(5000)
        }
        if (-not $stdoutTask.Wait(5000)) { $pipeTimeout = $true; $stdout = "" } else { $stdout = $stdoutTask.Result }
        if (-not $stderrTask.Wait(5000)) { $pipeTimeout = $true; $stderr = "" } else { $stderr = $stderrTask.Result }
        $safeStdout = Protect-DawoudTelemetryText -Text ([string]$stdout)
        $safeStderr = Protect-DawoudTelemetryText -Text ([string]$stderr)
        Set-Content -LiteralPath $stdoutPath -Value $safeStdout -Encoding utf8
        Set-Content -LiteralPath $stderrPath -Value $safeStderr -Encoding utf8
        [pscustomobject]@{
            Name = $Name
            Pid = $processId
            ExitCode = if ($timedOut) { 124 } else { $process.ExitCode }
            TimedOut = $timedOut
            PipeTimeout = $pipeTimeout
            DurationSeconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 2)
            Output = if ($PreserveOutput) { [string]$stdout } else { [string]$safeStdout }
            Error = [string]$safeStderr
            TimeoutReason = $timeoutReason
            StartupTrace = $startupTrace
        }
    } catch {
        $detail = Protect-DawoudTelemetryText -Text $_.Exception.Message
        Set-Content -LiteralPath $stderrPath -Value $detail -Encoding utf8
        [pscustomobject]@{ Name = $Name; Pid = $processId; ExitCode = 1; TimedOut = $false; PipeTimeout = $false; DurationSeconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 2); Output = ""; Error = $detail; TimeoutReason = "START_EXCEPTION"; StartupTrace = $startupTrace }
    } finally {
        $process.Dispose()
    }
}

function Invoke-CodexSmoke {
    param([string]$Name, [string]$Prompt)
    $codexPath = (& (Join-Path $PSScriptRoot "resolve-codex.ps1") | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($codexPath) -or -not (Test-Path -LiteralPath $codexPath -PathType Leaf)) {
        return [pscustomobject]@{ Name = $Name; ExitCode = 1; TimedOut = $false; Output = ""; Error = "Codex executable was not resolved."; Pid = 0; DurationSeconds = 0 }
    }
    $args = @("exec", "--ephemeral", "-C", $Project, "--sandbox", "read-only")
    if (-not (Test-Path -LiteralPath (Join-Path $Project ".git") -PathType Container)) { $args += "--skip-git-repo-check" }
    if ($preferences.codex_model) { $args += @("--model", [string]$preferences.codex_model) }
    if ($preferences.codex_effort) { $args += @("--config", "model_reasoning_effort=$([string]$preferences.codex_effort)") }
    $args += "-"
    Invoke-BoundedProcess -FileName $codexPath -Arguments $args -WorkingDirectory $Project -InputText $Prompt -TimeoutSeconds 90 -Name $Name
}

function Invoke-AgyToCodexSmoke {
    $stageStart = Get-Date
    Write-TestLine "AGY -> CODEX START"
    $route = $null
    $prompt = $null
    for ($i = 1; $i -le 40 -and -not $route; $i++) {
        $candidate = "Return exactly one short sentence for AGY-led reverse routing smoke $i."
        $candidateRoute = Get-DawoudRouteDecision -Task $candidate -Leader "Antigravity" -CodexShare 90 -AntigravityShare 10
        if ($candidateRoute.Agent -eq "Codex") { $route = $candidateRoute; $prompt = $candidate }
    }
    if (-not $route) {
        Write-TestLine "AGY -> CODEX FAIL"
        Write-TestLine "FAILED_STAGE: ROUTE_SELECTION"
        Write-TestLine "ERROR: Antigravity-led 90/10 route did not select Codex within bounded candidates."
        return [pscustomobject]@{ Pass = $false; Pid = 0; WorkerPid = 0; ExitCode = 1; Output = ""; DurationSeconds = [math]::Round(((Get-Date) - $stageStart).TotalSeconds, 2) }
    }
    Write-TestLine ("AGY-side route: Leader=Antigravity; resolved executor={0}; reason={1}" -f $route.Agent, $route.Reason)
    $result = Invoke-CodexSmoke -Name "agy-to-codex" -Prompt $prompt
    $pass = $result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Output)
    Write-TestLine ("AGY -> CODEX {0}" -f $(if ($pass) { "PASS" } else { "FAIL" }))
    Write-TestLine ("AGY -> CODEX DETAILS: codex_pid={0}; model={1}; cwd={2}; exit_code={3}; elapsed={4}s; output={5}" -f $result.Pid, $(if ($preferences.codex_model) { [string]$preferences.codex_model } else { "selected-default" }), $Project, $result.ExitCode, $result.DurationSeconds, $(if ($pass) { Protect-DawoudTelemetryText -Text $result.Output.Trim() } else { "NONE" }))
    $result | Add-Member -NotePropertyName Pass -NotePropertyValue $pass -Force
    $result | Add-Member -NotePropertyName WorkerPid -NotePropertyValue 0 -Force
    $result
}

function Invoke-AgySmoke {
    param([string]$Name, [string]$Leader, [int]$CodexShare, [int]$AntigravityShare, [string]$Prompt)
    $agy = Resolve-AgyExecutable
    if ([string]::IsNullOrWhiteSpace($agy) -or -not (Test-Path -LiteralPath $agy -PathType Leaf)) {
        return [pscustomobject]@{ Name = $Name; ExitCode = 1; TimedOut = $false; Output = ""; Error = "Canonical agy.exe was not resolved."; Pid = 0; DurationSeconds = 0 }
    }
    $session = "final-test-" + ([guid]::NewGuid().ToString("N"))
    $work = $Name + "-" + ([guid]::NewGuid().ToString("N").Substring(0, 8))
    $startupPath = Join-Path $env:TEMP ("dawoud-{0}-{1}.startup.log" -f $Name, ([guid]::NewGuid().ToString("N")))
    $stageStart = Get-Date
    $stageLabel = switch ($Name) { "agy-backend" { "AGY BACKEND" } "codex-to-agy" { "CODEX -> AGY" } default { $Name.ToUpperInvariant() } }
    Write-TestLine "$stageLabel START"
    Write-TestLine "$stageLabel START TIME: $($stageStart.ToUniversalTime().ToString('o'))"
    $args = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $orchestratorScript,
        "dispatch", "-Task", $Prompt,
        "-WorkingDirectory", $Project, "-Leader", $Leader,
        "-CodexShare", ([string]$CodexShare), "-AntigravityShare", ([string]$AntigravityShare),
        "-Executor", "CODEX", "-SessionId", $session, "-WorkId", $work,
        "-Wait", "-WaitTimeoutSeconds", "90", "-HarnessPid", ([string]$PID), "-StartupDiagnosticPath", $startupPath
    )
    $processResult = Invoke-BoundedProcess -FileName (Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe") -Arguments $args -WorkingDirectory $root -TimeoutSeconds 120 -Name $Name -StartupPath $startupPath -StartupTimeoutSeconds 20 -PreserveOutput
    $state = $null
    $contractMatch = [regex]::Match([string]$processResult.Output, '(?m)^DAWOUD_AGY_RESULT_V1::(.+)$')
    if ($contractMatch.Success) { try { $state = $contractMatch.Groups[1].Value | ConvertFrom-Json } catch { $state = $null } }
    $workerStderr = if ($processResult.Error) { [string]$processResult.Error } else { "" }
    $firstEvent = if ($state) { [string]$state.FirstStdoutEvent } else { "NONE" }
    $streamEvents = if ($state) { [string]$state.StreamEvents } else { "0" }
    $finalEvent = if ($state) { [string]$state.FinalResultEvent } else { "FALSE" }
    $timeoutReason = if ($state -and $state.TimeoutReason) { [string]$state.TimeoutReason } elseif ($processResult.TimeoutReason -and $processResult.TimeoutReason -ne "NONE") { [string]$processResult.TimeoutReason } elseif ($processResult.PipeTimeout) { "PIPE_READ" } else { "NONE" }
    $exitCode = if ($state -and $null -ne $state.ExitCode) { [int]$state.ExitCode } else { [int]$processResult.ExitCode }
    $workerPid = if ($state -and $state.ActualPid) { [int]$state.ActualPid } else { [int]$processResult.Pid }
    $actualExe = if ($state -and $state.ActualExe) { [string]$state.ActualExe } else { "NONE" }
    $parentPid = 0
    $firstRawStdout = if ($state -and $state.FirstRawStdout) { [string]$state.FirstRawStdout } else { "NONE" }
    $failedStage = if ($state -and $state.FailedStage) { [string]$state.FailedStage } else { if ($state) { "WAIT_RESULT_CONTRACT" } else { "RESULT_CONTRACT" } }
    $exceptionType = if ($state -and $state.ExceptionType) { [string]$state.ExceptionType } else { "NONE" }
    $exceptionMessage = if ($state -and $state.Error) { [string]$state.Error } else { if ($state) { "NONE" } else { "Orchestrator did not return DAWOUD_AGY_RESULT contract." } }
    $finalResponse = if ($state) { [string]$state.FinalResponse } else { "" }
    $success = $state -and [bool]$state.Success -and $actualExe -eq (Resolve-AgyExecutable) -and $workerPid -gt 0 -and [bool]$state.StdinWritten -and ([int]$streamEvents -gt 0) -and [bool]$state.FinalResultEvent -and -not [string]::IsNullOrWhiteSpace($finalResponse) -and $exitCode -eq 0 -and $timeoutReason -eq "NONE"
    $diagnosticText = $workerStderr
    if ([string]::IsNullOrWhiteSpace($diagnosticText)) { $safeStderr = "NONE" } else { $safeStderr = Protect-DawoudTelemetryText -Text $diagnosticText; if ($safeStderr.Length -gt 240) { $safeStderr = $safeStderr.Substring(0, 240) } }
    Write-TestLine ("{0} {1}" -f $stageLabel, $(if ($success) { "PASS" } else { "FAIL" }))
    if (-not $success) {
        Write-TestLine "FAILED_STAGE: $failedStage"
        Write-TestLine "EXCEPTION_TYPE: $exceptionType"
        Write-TestLine "ERROR: $exceptionMessage"
        Write-TestLine "ACTUAL_EXE: $actualExe"
        Write-TestLine "ACTUAL_PID: $workerPid"
        Write-TestLine "STDIN_WRITTEN: $(if ($state -and $state.StdinWritten) { 'YES' } else { 'NO' })"
    }
    Write-TestLine "FINAL_RESPONSE: $(if ([string]::IsNullOrWhiteSpace($finalResponse)) { 'NONE' } else { Protect-DawoudTelemetryText -Text $finalResponse })"
    if ($state) { Write-TestLine ("AGY EXECUTOR: pid={0}; model={1}; cwd={2}" -f $workerPid, $(if ($Leader -eq "Antigravity" -or $AntigravityShare -gt 0) { if ($preferences.antigravity_model) { [string]$preferences.antigravity_model } else { "selected-default" } } else { "selected-default" }), $Project) }
    Write-TestLine ("{0} DETAILS: exe={1}; pid={2}; parent_pid={3}; args={4}; start={5}; first_stdout_event={6}; first_raw_stdout={7}; stream_events={8}; stderr_or_log={9}; final_result_event={10}; exit_code={11}; timeout_reason={12}; elapsed={13}s" -f $stageLabel, $actualExe, $workerPid, $parentPid, $(if ($state) { "DAWOUD_AGY_RESULT_V1" } else { "NONE" }), $stageStart.ToUniversalTime().ToString('o'), $firstEvent, $firstRawStdout, $streamEvents, $safeStderr, $finalEvent, $exitCode, $timeoutReason, $processResult.DurationSeconds)
    $processResult | Add-Member -NotePropertyName Pass -NotePropertyValue $success -Force
    $processResult | Add-Member -NotePropertyName WorkerPid -NotePropertyValue $workerPid -Force
    $processResult | Add-Member -NotePropertyName SessionId -NotePropertyValue $session -Force
    $processResult | Add-Member -NotePropertyName WorkId -NotePropertyValue $work -Force
    $processResult
}

function Invoke-FinalTestCleanup {
    param([object[]]$Stages)
    Write-TestLine "CLEANUP START"
    $cleanupPass = $true
    foreach ($stage in @($Stages)) {
        foreach ($pidValue in @($stage.Pid, $stage.WorkerPid)) {
            if ($pidValue -and $pidValue -gt 0 -and (Get-Process -Id $pidValue -ErrorAction SilentlyContinue)) {
                try { & taskkill.exe /PID ([string]$pidValue) /T /F 2>$null | Out-Null } catch { $cleanupPass = $false }
                if (Get-Process -Id $pidValue -ErrorAction SilentlyContinue) { $cleanupPass = $false }
            }
        }
    }
    Write-TestLine ("CLEANUP {0}" -f $(if ($cleanupPass) { "PASS" } else { "FAIL" }))
    $cleanupPass
}

Write-TestLine "DAWOUD FINAL-TEST"
Write-TestLine ("Project: {0}" -f $Project)
Write-TestLine ("DAWOUD process identity: {0}" -f $runtime.Name)
if ($runtime.Restricted) {
    Write-TestLine "FAIL: nested Codex sandbox identity detected. No ACL workaround will be attempted."
    Write-TestLine "Run this command from a normal user PowerShell. Logs: $logRoot"
    exit 2
}
if ([string]::IsNullOrWhiteSpace($Project)) {
    Write-TestLine "FAIL: no valid DAWOUD project found. Select a project in dawoud, or pass -Project <path>."
    exit 2
}
if (-not (Test-Path -LiteralPath $Project -PathType Container)) { Write-TestLine "FAIL: project directory not found: $Project"; exit 2 }

$uiFiles = @("dawoud-common.ps1", "dawoud-primary.ps1", "workbench.ps1", "worker-run.ps1", "orchestrator.ps1") | ForEach-Object { Join-Path $PSScriptRoot $_ }
$uiPass = $true
foreach ($file in $uiFiles) {
    $tokens = $null; $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors) | Out-Null
    if (@($errors).Count -gt 0) { $uiPass = $false; Write-TestLine "UNIFIED UI: FAIL ($file)" }
}
if ($uiPass) { Write-TestLine "UNIFIED UI: PASS" }

Write-TestLine "CODEX BACKEND START"
$codex = Invoke-CodexSmoke -Name "codex-backend" -Prompt "Return exactly one short sentence: DAWOUD Codex backend is available."
$codex | Add-Member -NotePropertyName Pass -NotePropertyValue ($codex.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($codex.Output)) -Force
Write-TestLine ("CODEX BACKEND: {0}" -f $(if ($codex.Pass) { "PASS" } else { "FAIL" }))
Write-TestLine ("CODEX DETAILS: pid={0}; model={1}; cwd={2}; exit_code={3}; elapsed={4}s; output={5}" -f $codex.Pid, $(if ($preferences.codex_model) { [string]$preferences.codex_model } else { "selected-default" }), $Project, $codex.ExitCode, $codex.DurationSeconds, $(if ($codex.Pass) { Protect-DawoudTelemetryText -Text $codex.Output.Trim() } else { Protect-DawoudTelemetryText -Text $codex.Error }))

$agy = Invoke-AgySmoke -Name "agy-backend" -Leader "Antigravity" -CodexShare 10 -AntigravityShare 90 -Prompt "Research nothing external. Return exactly one short sentence: DAWOUD AGY backend is available."

if ($AgyOnly) {
    $cleanupPass = Invoke-FinalTestCleanup -Stages @($agy)
    Write-TestLine ("FINAL-TEST AGY-ONLY: {0}" -f $(if ($agy.Pass -and $cleanupPass) { "PASS" } else { "FAIL" }))
    exit $(if ($agy.Pass -and $cleanupPass) { 0 } else { 1 })
}

$codexToAgy = Invoke-AgySmoke -Name "codex-to-agy" -Leader "Codex" -CodexShare 10 -AntigravityShare 90 -Prompt "Return exactly one short sentence: Codex coordinator delegated this bounded task to AGY."
$agyToCodex = Invoke-AgyToCodexSmoke

$autoAgy = Resolve-DawoudLeader -ConfiguredLeader Auto -CodexShare 10 -AntigravityShare 90
$autoCodex = Resolve-DawoudLeader -ConfiguredLeader Auto -CodexShare 90 -AntigravityShare 10
$autoAgyPass = $autoAgy -eq "Antigravity"
$autoCodexPass = $autoCodex -eq "Codex"
Write-TestLine ("AUTO 10/90: {0} -> {1}" -f $(if ($autoAgyPass) { "PASS" } else { "FAIL" }), $autoAgy)
Write-TestLine ("AUTO 90/10: {0} -> {1}" -f $(if ($autoCodexPass) { "PASS" } else { "FAIL" }), $autoCodex)

$cleanupPass = Invoke-FinalTestCleanup -Stages @($codex, $agy, $codexToAgy, $agyToCodex)
Write-TestLine "No ACL changes, authentication changes, routing changes, or reinstall actions were performed."
Write-TestLine "Detailed sanitized logs: $logRoot"

$allPass = $uiPass -and $codex.Pass -and $agy.Pass -and $codexToAgy.Pass -and $agyToCodex.Pass -and $cleanupPass -and $autoAgyPass -and $autoCodexPass
Write-TestLine ("FINAL-TEST: {0}" -f $(if ($allPass) { "PASS" } else { "FAIL/PARTIAL" }))
exit $(if ($allPass) { 0 } else { 1 })
