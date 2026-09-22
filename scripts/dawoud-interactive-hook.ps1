[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$setupRoot = Split-Path -Parent $PSScriptRoot
$telemetryRoot = Join-Path $setupRoot "reports\telemetry"
$markerRoot = Join-Path $telemetryRoot "active"
$settingsPath = Join-Path $env:USERPROFILE ".codex\dawoud-settings.json"
. (Join-Path $PSScriptRoot "dawoud-common.ps1")

function Read-HookInput {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    try { $raw | ConvertFrom-Json } catch { return $null }
}

function Write-HookJson {
    param([hashtable]$Payload)
    $Payload | ConvertTo-Json -Compress -Depth 10
}

try {
$inputObject = Read-HookInput
if (-not $inputObject -or $env:DAWOUD_INTERACTIVE_ROUTING -ne "1") { exit 0 }
$sessionId = if ($env:DAWOUD_SESSION_ID) { [string]$env:DAWOUD_SESSION_ID } else { "session-" + ([guid]::NewGuid().ToString("N")) }
$workId = if ($inputObject.turn_id) { [string]$inputObject.turn_id } else { "turn-" + ([guid]::NewGuid().ToString("N")) }
$markerRoot = Join-Path $markerRoot (Get-SafeId -Value $sessionId)
New-Item -ItemType Directory -Path $markerRoot -Force | Out-Null
$markerPath = Join-Path $markerRoot ((Get-SafeId -Value $workId) + ".json")

if ($inputObject.hook_event_name -eq "UserPromptSubmit") {
    $prompt = [string]$inputObject.prompt
    if ([string]::IsNullOrWhiteSpace($prompt)) { exit 0 }
    $workingDirectory = if ($inputObject.cwd) { [string]$inputObject.cwd } else { (Get-Location).Path }
    $preferences = Get-DawoudPreferences -Path $settingsPath
    $leader = if ($env:DAWOUD_LEADER) { [string]$env:DAWOUD_LEADER } else { [string]$preferences.leader }
    $codexShare = if ($env:DAWOUD_CODEX_SHARE) { [int]$env:DAWOUD_CODEX_SHARE } else { [int]$preferences.codex_share }
    $agyShare = if ($env:DAWOUD_ANTIGRAVITY_SHARE) { [int]$env:DAWOUD_ANTIGRAVITY_SHARE } else { [int]$preferences.antigravity_share }
    $agyModel = if ($env:DAWOUD_ANTIGRAVITY_MODEL) { [string]$env:DAWOUD_ANTIGRAVITY_MODEL } else { [string]$preferences.antigravity_model }
    $agyEffort = if ($env:DAWOUD_ANTIGRAVITY_EFFORT) { [string]$env:DAWOUD_ANTIGRAVITY_EFFORT } else { [string]$preferences.antigravity_effort }
    $route = Get-DawoudRouteDecision -Task $prompt -Leader $leader -CodexShare $codexShare -AntigravityShare $agyShare -TelemetryRoot $telemetryRoot -SessionId $sessionId
    $record = $null
    $context = New-Object System.Collections.Generic.List[string]
    $slices = @(Get-DawoudTaskSlices -Task $prompt)
    [void]$context.Add("DAWOUD decomposed prompt into $($slices.Count) workload slice(s) before allocation.")
    foreach ($slice in $slices) {
        $sliceWorkId = "$workId-$($slice.Id)"
        $sliceRoute = Get-DawoudRouteDecision -Task $slice.Task -Leader $leader -CodexShare $codexShare -AntigravityShare $agyShare -TelemetryRoot $telemetryRoot -SessionId $sessionId
        if ($sliceRoute.Agent -eq "Antigravity") {
            $dispatchArgs = @("orchestrator-dispatch", "-WorkerCommand", "dispatch", "-Task", $slice.Task, "-WorkingDirectory", $workingDirectory, "-Leader", $leader, "-CodexShare", $codexShare, "-AntigravityShare", $agyShare, "-Executor", "CODEX", "-SessionId", $sessionId, "-WorkId", $sliceWorkId, "-Wait", "-WaitTimeoutSeconds", "540")
            if ($agyModel) { $dispatchArgs += @("-AntigravityModel", $agyModel) }
            if ($agyEffort) { $dispatchArgs += @("-AntigravityEffort", $agyEffort) }
            $pwsh = (Get-Command pwsh.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
            if ([string]::IsNullOrWhiteSpace($pwsh)) { $pwsh = "pwsh.exe" }
            # Invoke setup in child PowerShell. This preserves normal CLI parameter binding.
            $dispatchOutput = & $pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setupRoot "setup.ps1") @dispatchArgs 2>&1 | Out-String
            $dispatchExit = $LASTEXITCODE
            $state = @(Get-ChildItem -LiteralPath (Join-Path $setupRoot "reports\workers") -Filter status.json -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { try { Get-Content $_.FullName -Raw | ConvertFrom-Json } catch {} } | Where-Object { $_.session_id -eq $sessionId -and $_.work_id -eq $sliceWorkId } | Sort-Object started_at -Descending | Select-Object -First 1)
            $summary = if ($state -and $state.result_summary) { [string]$state.result_summary } else { "No Antigravity worker result returned." }
            if ($dispatchExit -eq 0 -and $state -and $state.status -eq "DONE") {
                [void]$context.Add("REAL AGY EXECUTION: slice $($slice.Id) completed through official agy.exe; model $agyModel; effort $agyEffort.")
                [void]$context.Add("Delegated worker result ($($slice.Id)): $summary")
                [void]$context.Add("Use this delegated result. Do not repeat same implementation work unless verification or correction needed.")
            } else {
                $retryWorkId = "$sliceWorkId-retry1"
                $retryArgs = @("orchestrator-dispatch", "-WorkerCommand", "dispatch", "-Task", $slice.Task, "-WorkingDirectory", $workingDirectory, "-Leader", $leader, "-CodexShare", $codexShare, "-AntigravityShare", $agyShare, "-Executor", "CODEX", "-SessionId", $sessionId, "-WorkId", $retryWorkId, "-Wait", "-WaitTimeoutSeconds", "540")
                if ($agyModel) { $retryArgs += @("-AntigravityModel", $agyModel) }
                if ($agyEffort) { $retryArgs += @("-AntigravityEffort", $agyEffort) }
                $retryOutput = & $pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setupRoot "setup.ps1") @retryArgs 2>&1 | Out-String
                $retryExit = $LASTEXITCODE
                $retryState = @(Get-ChildItem -LiteralPath (Join-Path $setupRoot "reports\workers") -Filter status.json -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { try { Get-Content $_.FullName -Raw | ConvertFrom-Json } catch {} } | Where-Object { $_.session_id -eq $sessionId -and $_.work_id -eq $retryWorkId } | Sort-Object started_at -Descending | Select-Object -First 1)
                if ($retryExit -eq 0 -and $retryState -and $retryState.status -eq "DONE") {
                    if ($retryState.telemetry_path) {
                        try { Set-DawoudTelemetryRecord -Path ([string]$retryState.telemetry_path) -Fields @{ RetryCount = 1 } } catch { }
                    }
                    [void]$context.Add("REAL AGY EXECUTION: slice $($slice.Id) succeeded on bounded retry 1 through official agy.exe.")
                    [void]$context.Add("Delegated worker result ($($slice.Id)): $([string]$retryState.result_summary)")
                } else {
                    $flat = ((($retryOutput + " " + $dispatchOutput) -replace '\s+', ' ').Trim())
                    $reasonMatch = [regex]::Match(($retryOutput + "`n" + $dispatchOutput), '(?s)Reason:\s*(.+?)(?:\r?\nDAWOUD EXECUTION REPORT|$)')
                    $reason = if ($retryState -and $retryState.error) { [string]$retryState.error } elseif ($state -and $state.error) { [string]$state.error } elseif ($reasonMatch.Success) { ($reasonMatch.Groups[1].Value -replace '\s+', ' ').Trim() } elseif ($flat) { $flat } else { "dispatch exited with code $dispatchExit; retry exited with code $retryExit; no diagnostic returned" }
                    if ($reason.Length -gt 300) { $reason = $reason.Substring(0, 300) + "..." }
                    [void]$context.Add("DAWOUD ROUTING WARNING")
                    [void]$context.Add("Antigravity delegation expected but unavailable.")
                    [void]$context.Add("Reason: $reason")
                    [void]$context.Add("TARGET DEVIATION: AGY slice $($slice.Id) failed after 1 bounded retry; Codex must retain only this failed slice and report failure.")
                }
            }
        } else {
            $retained = New-DawoudTelemetryRecord -TelemetryRoot $telemetryRoot -SessionId $sessionId -Task $slice.Task -Executor CODEX -Category $sliceRoute.Category -CodexShare $codexShare -AntigravityShare $agyShare -Leader $leader -RouteReason $sliceRoute.Reason -Model ($env:DAWOUD_CODEX_MODEL) -Effort ($env:DAWOUD_CODEX_EFFORT) -WorkId $sliceWorkId -RecordKind TASK
            Complete-DawoudTelemetryRecord -Path $retained.Path -Status DONE -Summary "Codex retained slice: $($sliceRoute.Reason)"
            [void]$context.Add("TARGET DEVIATION: slice $($slice.Id) retained for Codex. Reason: $($sliceRoute.Reason)")
        }
    }
    $integrationReason = if (@($slices | Where-Object { (Get-DawoudRouteDecision -Task $_.Task -Leader $leader -CodexShare $codexShare -AntigravityShare $agyShare).Agent -eq "Antigravity" }).Count -gt 0) { "Codex integration after real Antigravity delegation." } else { "Codex retained routed work." }
    $record = New-DawoudTelemetryRecord -TelemetryRoot $telemetryRoot -SessionId $sessionId -Task $prompt -Executor CODEX -Category $route.Category -CodexShare $codexShare -AntigravityShare $agyShare -Leader $leader -RouteReason $integrationReason -Model ($env:DAWOUD_CODEX_MODEL) -Effort ($env:DAWOUD_CODEX_EFFORT) -WorkId $workId -RecordKind TASK
    $marker = [ordered]@{ Path = $record.Path; WorkId = $workId; SessionId = $sessionId; WorkingDirectory = $workingDirectory; PromptLength = $prompt.Length; Route = if ($slices.Count -gt 1) { "Decomposed" } else { $route.Agent } }
    $marker | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $markerPath -Encoding utf8
    [void]$context.Add("Cavecrew or Codex subagents remain CODEX_SUBAGENT, never Antigravity.")
    Write-HookJson -Payload @{ hookSpecificOutput = @{ hookEventName = "UserPromptSubmit"; additionalContext = ($context -join "`n") } }
    exit 0
}

if ($inputObject.hook_event_name -eq "Stop") {
    $candidate = $null
    if ($inputObject.turn_id) { $candidate = Join-Path $markerRoot ((Get-SafeId -Value ([string]$inputObject.turn_id)) + ".json") }
    if (-not $candidate -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) { $candidate = Get-ChildItem -LiteralPath $markerRoot -Filter *.json -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Select-Object -ExpandProperty FullName }
    if (-not $candidate -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) { exit 0 }
    $marker = Get-Content -LiteralPath $candidate -Raw | ConvertFrom-Json
    if (Test-Path -LiteralPath $marker.Path -PathType Leaf) { Complete-DawoudTelemetryRecord -Path $marker.Path -Status DONE -Summary "Interactive Codex turn completed." }
    $preferences = Get-DawoudPreferences -Path $settingsPath
    $report = Get-DawoudExecutionReport -TelemetryRoot $telemetryRoot -SessionId ([string]$marker.SessionId) -WorkId ([string]$marker.WorkId) -CodexShare ([int]$preferences.codex_share) -AntigravityShare ([int]$preferences.antigravity_share)
    Remove-Item -LiteralPath $candidate -Force -ErrorAction SilentlyContinue
    if ($report) { Write-HookJson -Payload @{ systemMessage = $report.Text; continue = $true } }
    exit 0
}

exit 0
} catch {
    $hookName = if ($inputObject -and $inputObject.hook_event_name) { [string]$inputObject.hook_event_name } else { "Unknown" }
    $reason = Protect-DawoudTelemetryText -Text ([string]$_.Exception.Message)
    $failure = "DAWOUD ROUTING FAILURE`nHook: $hookName`nExit code: 0 (blocking hook response; internal failure 1)`nReason: $reason`nAGY attempted: $(if ($null -ne $dispatchExit) { 'YES' } else { 'NO' })`nFallback performed: NO"
    try { [Console]::Error.WriteLine($failure) } catch { }
    try {
        $failurePath = Join-Path $telemetryRoot ("hook-failure-" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + "-$PID.log")
        [IO.File]::WriteAllText($failurePath, $failure)
    } catch { }
    Write-HookJson -Payload @{ continue = $false; stopReason = $failure; systemMessage = $failure }
    exit 0
}
