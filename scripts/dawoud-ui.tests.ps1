$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dawoud-common.ps1")
. (Join-Path $PSScriptRoot "dawoud-ui.ps1")

function Assert-DawoudUi {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "UI TEST FAIL: $Message" }
}

$state = New-DawoudUiState -Project (Get-Location).Path -SessionId "ui-test" -ConfiguredLeader "Auto" -ResolvedLeader "Antigravity" -CodexShare 10 -AntigravityShare 90 -CodexModel "codex-test" -AntigravityModel "agy-test"
Assert-DawoudUi ([bool](Assert-DawoudUiStateSchema -State $state)) "authoritative UI state must include all acceptance and live-update fields"
Assert-DawoudUi ($state.AcceptanceStage -eq 'Planning' -and $state.LastEvidenceAt -eq [datetime]::MinValue) "acceptance fields must exist at state construction"
Assert-DawoudUi ((Get-DawoudUiGlyph -Status WARNING -Unicode:$false) -eq '!') "ASCII glyph fallback must render warning status"
$live = New-DawoudUiState -Project (Get-Location).Path -SessionId "live-bridge-test"
Add-DawoudUiTask -State $live -Task ([pscustomobject]@{ Id="first"; Summary="First task"; Agent="Antigravity"; Status="QUEUED"; Started=[datetime]::MinValue; End=[datetime]::MinValue; UpdatedAt=Get-Date })
Receive-DawoudUiUpdates -State $live
Assert-DawoudUi ($live.Tasks.Count -eq 1) "UI thread must ingest newly created graph task"
$bridge = New-DawoudUiState -Project (Get-Location).Path -SessionId "runspace-bridge-test"
$bridgeTask=[pscustomobject]@{Id="runspace-task";Summary="Published by worker runspace";Agent="Codex";Status="RUNNING";UpdatedAt=Get-Date;Started=Get-Date;End=[datetime]::MinValue}
$workerRunspace=[RunspaceFactory]::CreateRunspace();$workerRunspace.Open()
$workerPowerShell=[PowerShell]::Create();$workerPowerShell.Runspace=$workerRunspace
[void]$workerPowerShell.AddScript('param($state,$task) $state.UiUpdates.Enqueue([pscustomobject]@{Sequence=1L;Kind="TASK";Value=$task;At=Get-Date})')
[void]$workerPowerShell.AddArgument($bridge);[void]$workerPowerShell.AddArgument($bridgeTask)
[void]$workerPowerShell.Invoke();$workerPowerShell.Dispose();$workerRunspace.Dispose()
Receive-DawoudUiUpdates -State $bridge
Assert-DawoudUi ($bridge.Tasks.Count -eq 1 -and $bridge.Tasks[0].Id -eq "runspace-task") "task update must cross an actual PowerShell runspace boundary"
$bridgeLines=@(Get-DawoudUiLines -State $bridge -Width 100 -Height 30)
Assert-DawoudUi (($bridgeLines -join "`n") -match "CODEX" -and ($bridgeLines -join "`n") -match "RUNNING" -and ($bridgeLines -join "`n") -notmatch "CODEX.*IDLE") "active task without executor detail must not render its executor IDLE"
[void](Start-DawoudUiAgent -State $live -Name "first/ANTIGRAVITY" -Executor "ANTIGRAVITY" -TaskId "first" -TaskText "First task" -Model "agy-test")
[void](Update-DawoudUiAgent -State $live -Name "first/ANTIGRAVITY" -Status "RUNNING" -Action "Running worker" -ProcessId 8123)
Publish-DawoudUiUpdate -State $live -Kind "AGENT" -Value $live.Agents["first/ANTIGRAVITY"]
Set-DawoudUiTask -State $live -TaskId "first" -Status "RUNNING" -Agent "Antigravity" | Out-Null
Receive-DawoudUiUpdates -State $live
$liveLines = @(Get-DawoudUiLines -State $live -Width 100 -Height 30)
Assert-DawoudUi (($liveLines -join "`n") -match "first/ANTIGRAVITY.*RUNNING") "active assignment must appear as RUNNING"
Assert-DawoudUi (($liveLines -join "`n") -notmatch "ANTIGRAVITY.*IDLE") "active executor must not appear IDLE"
Set-DawoudUiTask -State $live -TaskId "first" -Status "DONE" -Agent "Antigravity" | Out-Null
Add-DawoudUiTask -State $live -Task ([pscustomobject]@{ Id="follow-up"; Summary="Follow-up"; Agent="Codex"; Status="QUEUED"; Started=[datetime]::MinValue; End=[datetime]::MinValue; UpdatedAt=Get-Date })
  $staleTask = [pscustomobject]@{ Id="first"; Summary="First task"; Agent="Antigravity"; Status="RUNNING"; UpdatedAt=(Get-Date).AddMinutes(-1) }
  $live.UiUpdates.Enqueue([pscustomobject]@{ Sequence=1L; Kind="TASK"; Value=$staleTask; At=Get-Date })
Set-DawoudUiStage -State $live -Stage "Reconciling"
Receive-DawoudUiUpdates -State $live
Assert-DawoudUi (($live.Tasks | Where-Object Id -eq "first").Status -eq "DONE") "stale task event must not overwrite newer terminal state"
$liveLines = @(Get-DawoudUiLines -State $live -Width 100 -Height 30)
Assert-DawoudUi ($live.Tasks.Count -eq 2 -and ($liveLines -join "`n") -match "follow-up") "follow-up graph task must be visible live"
Assert-DawoudUi (($liveLines -join "`n") -match "Tasks 1/2 done") "task completion counter must update from observed states"
Assert-DawoudUi (($liveLines -join "`n") -match "ACCEPTANCE: Reconciling") "acceptance stage must be visible"
Set-DawoudUiStage -State $live -Stage "Executing"
$live.Agents["first/ANTIGRAVITY"].LastEvent = (Get-Date).AddSeconds(-15)
Update-DawoudUiHealth -State $live
Receive-DawoudUiUpdates -State $live
$liveLines = @(Get-DawoudUiLines -State $live -Width 100 -Height 30)
Assert-DawoudUi (($liveLines -join "`n") -match "WAITING") "silent live executor must show WAITING"
Assert-DawoudUi (($liveLines -join "`n") -notmatch "ANTIGRAVITY.*IDLE") "silent active executor must not show IDLE"
Set-DawoudUiTask -State $live -TaskId "follow-up" -Status "WAITING" -Agent "Codex" | Out-Null
Receive-DawoudUiUpdates -State $live
Assert-DawoudUi (($live.Tasks | Where-Object Id -eq "follow-up").Status -eq "WAITING") "waiting task state must survive UI ingestion"
Add-DawoudUiTask -State $state -Task ([pscustomobject]@{ Id = "slice-1"; Summary = "Inspect files"; Agent = "ANTIGRAVITY"; Status = "QUEUED"; Started = [datetime]::MinValue; End = [datetime]::MinValue })
Add-DawoudUiTask -State $state -Task ([pscustomobject]@{ Id = "slice-2"; Summary = "Run tests"; Agent = "CODEX"; Status = "QUEUED"; Started = [datetime]::MinValue; End = [datetime]::MinValue })
[void](Start-DawoudUiAgent -State $state -Name "ANTIGRAVITY #1" -Executor "ANTIGRAVITY" -TaskId "slice-1" -TaskText "Inspect files" -Model "agy-test" -Command "agy command" -WorkingDirectory $state.Project)
[void](Start-DawoudUiAgent -State $state -Name "ANTIGRAVITY #2" -Executor "ANTIGRAVITY" -TaskId "slice-2" -TaskText "Run tests" -Model "agy-test" -Command "agy command 2" -WorkingDirectory $state.Project)
[void](Start-DawoudUiAgent -State $state -Name "CODEX" -Executor "CODEX" -TaskId "slice-2" -TaskText "Run tests" -Model "codex-test" -Command "codex exec --sandbox read-only" -WorkingDirectory $state.Project)
[void](Update-DawoudUiAgent -State $state -Name "ANTIGRAVITY #1" -Status "RUNNING" -Action "Editing cleanup.ps1" -ProcessId 7036 -File "scripts/cleanup.ps1" -EventKind "EDIT" -Message "Editing cleanup.ps1")
[void](Update-DawoudUiAgent -State $state -Name "ANTIGRAVITY #2" -Status "WAITING" -Action "Running tests" -ProcessId 9104 -EventKind "COMMAND" -Message "Running parser")
[void](Complete-DawoudUiAgent -State $state -Name "CODEX" -Status "DONE" -Message "Verification complete" -ExitCode 0)
for ($i = 0; $i -lt 200; $i++) { [void](Add-DawoudUiEvent -State $state -Source "TEST" -Kind "ACTION" -Message ("bounded event {0} {1}" -f $i, ("x" * 400))) }
Set-DawoudUiFile -State $state -Path "reports/result.txt" -Action "+"
$normal = @(Get-DawoudUiLines -State $state -Width 100 -Height 80)
$narrow = @(Get-DawoudUiLines -State $state -Width 48 -Height 24)
$micro = @(Get-DawoudUiLines -State $state -Width 48 -Height 12)
Assert-DawoudUi ($state.Events.Count -eq 160) "event history must stay bounded"
Assert-DawoudUi (($normal -join "`n") -match "ANTIGRAVITY #1") "normal view must show first worker"
Assert-DawoudUi (($normal -join "`n") -match "PID 7036") "normal view must show PID"
Assert-DawoudUi (($normal -join "`n") -match "CURRENT ACTION") "normal view must show current action"
Assert-DawoudUi (($normal -join "`n") -match "reports/result.txt") "normal view must show file activity"
Assert-DawoudUi (($normal -join "`n") -match "CODEX") "normal view must show Codex"
Assert-DawoudUi (($normal -join "`n") -match "ANTIGRAVITY #2") "normal view must show two AGY workers"
Assert-DawoudUi (($normal | ForEach-Object Length | Where-Object { $_ -ne 100 }).Count -eq 0) "wide frame rows must have exact width"
Assert-DawoudUi (($narrow | ForEach-Object Length | Where-Object { $_ -gt 48 }).Count -eq 0) "narrow view must fit width"
Assert-DawoudUi (($micro | ForEach-Object Length | Where-Object { $_ -gt 48 }).Count -eq 0) "micro view must fit width"
Assert-DawoudUi ($micro.Count -le 12) "micro view must fit terminal height"
Assert-DawoudUi (($normal | ForEach-Object Length | Where-Object { $_ -gt 100 }).Count -eq 0) "wide view must fit width"
$state.Unicode = $false
$ascii = @(Get-DawoudUiLines -State $state -Width 80 -Height 24)
Assert-DawoudUi ((($ascii -join "") -match '[^\x00-\x7F]') -eq $false) "ASCII mode must not emit Unicode frame/status glyphs"
$state.Unicode = $true
$commonSource = Get-Content (Join-Path $PSScriptRoot "dawoud-common.ps1") -Raw
Assert-DawoudUi ($commonSource -match 'AGY total task timeout after \$TotalTimeoutSeconds seconds') "hard total timeout must remain enforced"
Assert-DawoudUi ($commonSource -notmatch 'AGY idle timeout after|AGY startup timeout after') "stream silence must not terminate a live AGY process"
$primarySource = Get-Content (Join-Path $PSScriptRoot "dawoud-primary.ps1") -Raw
$assertCancellation = $primarySource -match 'CancellationSignal\.Requested\s*=\s*\$true' -and $primarySource -match 'taskkill\.exe.*?/T\s+/F' -and $primarySource -match 'function Get-DawoudOwnedProcessTreeIds' -and $primarySource -match 'function Get-DawoudActiveExecutionRootPids' -and $primarySource -match 'Status -notin @\("DONE", "FAILED", "CANCELLED", "IDLE"\)' -and $primarySource -match 'CancellationProcessesCleaned\s*=\s*if' -and $primarySource -match 'if \(\$input\.Type -eq "CANCEL"\)\s*\{\s*if \(\$script:activeExecution\) \{ Request-DawoudTaskCancellation \}'
Assert-DawoudUi $assertCancellation "Ctrl+C during active execution must signal cancellation and terminate only recorded process roots"
$uiSource = Get-Content (Join-Path $PSScriptRoot "dawoud-ui.ps1") -Raw
Assert-DawoudUi ($uiSource -match 'Status -in @\("DONE", "FAILED", "CANCELLED"\)' -and $uiSource -match '\$Status -eq "CANCELLED"\) \{ "CANCELLED"') "CANCELLED must remain distinct from FAILED"
$cancelState = New-DawoudUiState -Project $state.Project -SessionId "cancel-test" -ConfiguredLeader "Codex" -ResolvedLeader "Codex" -CodexShare 100 -AntigravityShare 0
Add-DawoudUiTask -State $cancelState -Task ([pscustomobject]@{ Id = "slice-cancel"; Summary = "Cancel"; Agent = "CODEX"; Status = "RUNNING"; Started = Get-Date; End = [datetime]::MinValue })
[void](Start-DawoudUiAgent -State $cancelState -Name "CODEX" -Executor "CODEX" -TaskId "slice-cancel" -TaskText "Cancel" -Model "codex-test" -Command "codex exec")
[void](Complete-DawoudUiAgent -State $cancelState -Name "CODEX" -Status "CANCELLED" -Message "user cancel" -ExitCode 130)
[void](Set-DawoudUiTask -State $cancelState -TaskId "slice-cancel" -Status "CANCELLED" -Agent "CODEX")
Assert-DawoudUi ($cancelState.Agents["CODEX"].Status -eq "CANCELLED" -and $cancelState.CurrentCommand.Status -eq "CANCELLED" -and $cancelState.Events[-1].Kind -eq "CANCEL") "cancelled executor state must not render as command/test failure"
Complete-DawoudUiSession -State $cancelState
Assert-DawoudUi (-not $cancelState.FailedRender) ("completed terminal render failed: {0}" -f $cancelState.FailedRenderReason)
Assert-DawoudUi ($cancelState.Status -eq "CANCELLED") "cancelled session must remain CANCELLED instead of becoming DONE"
$launcherSource = Get-Content (Join-Path $PSScriptRoot "workbench.ps1") -Raw
Assert-DawoudUi ([regex]::Matches($primarySource, 'function Read-DawoudDraft').Count -eq 1) "one input editor implementation must own the draft"
Assert-DawoudUi ($launcherSource -match 'function Remove-LaunchPreview' -and $launcherSource -match 'Remove-LaunchPreview\s*\r?\n\s*# AGEX owns one permanent frontend') "launcher rows must be removed before dashboard handoff"
Assert-DawoudUi ($launcherSource -notmatch 'function Remove-LaunchPreview\s*\{[^}]*Clear-Host') "dashboard handoff must not use Clear-Host"
Assert-DawoudUi ($uiSource -match 'TotalMilliseconds -ge 1000') "dashboard timer refresh must stay bounded to one update per second"
$finalTestSource = Get-Content (Join-Path $PSScriptRoot "final-test.ps1") -Raw
Assert-DawoudUi ($finalTestSource.Contains('if ($missingMilestone) { $timedOut = $true; $timeoutReason = $missingMilestone; break }') -and $finalTestSource.Contains('$startupDeadline = [datetime]::MaxValue')) "completed startup milestones must not trigger the startup timeout"
$state.View = "DETAILS"
$details = @(Get-DawoudUiLines -State $state -Width 100 -Height 80)
Assert-DawoudUi (($details -join "`n") -match "stream_events") "details view must expose executor diagnostics"
Assert-DawoudUi (($details -join "`n") -match "latest=") "details view must expose latest observable event"

$longPath = "src/" + ("nested/" * 80) + ("very-long-file-name" * 8) + ".cs"
Set-DawoudUiFile -State $state -Path $longPath -Action M | Out-Null
for ($i = 0; $i -lt 240; $i++) { Set-DawoudUiFile -State $state -Path ("bounded/{0}.txt" -f $i) -Action M | Out-Null }
Assert-DawoudUi ($state.Files.Count -le 200) "file activity must stay bounded"
$longState = New-DawoudUiState -Project $state.Project -SessionId "long-text" -ConfiguredLeader "Auto" -ResolvedLeader "Antigravity" -CodexShare 10 -AntigravityShare 90 -CodexModel ("codex-" * 40) -AntigravityModel ("agy-" * 40)
Add-DawoudUiTask -State $longState -Task ([pscustomobject]@{ Id = "slice-long"; Summary = "Long test"; Agent = "ANTIGRAVITY"; Status = "RUNNING"; Started = Get-Date; End = [datetime]::MinValue })
[void](Start-DawoudUiAgent -State $longState -Name "ANTIGRAVITY #1" -Executor "ANTIGRAVITY" -TaskId "slice-long" -TaskText ("task " * 150) -Model "agy-test" -Command ("verification " * 100) -WorkingDirectory $longPath)
[void](Update-DawoudUiAgent -State $longState -Name "ANTIGRAVITY #1" -Status "RUNNING" -Action ("editing " * 100) -ProcessId 8123 -File $longPath)
$longLines = @(Get-DawoudUiLines -State $longState -Width 60 -Height 26)
Assert-DawoudUi (($longLines | ForEach-Object Length | Where-Object { $_ -gt 60 }).Count -eq 0) "long labels and paths must be clipped to frame"

Assert-DawoudUi ((Protect-DawoudUiCommand -Text "tool --token topsecret") -notmatch "topsecret") "sensitive command values must be sanitized"
Assert-DawoudUi (-not (Get-Command Update-DawoudUiStreamLog -ErrorAction SilentlyContinue)) "dashboard must not read AGY local stream logs"
$longState.Status = "DONE"
$longState.Result = "Acceptance response line one`nAcceptance response line two"
$longState.Summary = [pscustomobject]@{ UserSlices = 1; InternalTasks = 1; TotalAssignments = 2; Completed = 1; Failed = 0 }
$longState.Tasks[0].Status = "DONE"
$smallFinal = @(Get-DawoudUiLines -State $longState -Width 60 -Height 17)
Assert-DawoudUi (($smallFinal -join "`n") -match "RESULT") "compact completion must preserve separate result section"
Assert-DawoudUi (($smallFinal -join "`n") -match "Acceptance response line one") "compact completion must show actual result text"
Assert-DawoudUi (($smallFinal -join "`n") -match "SUMMARY") "compact completion must preserve execution summary"
$state.GoalStatus = "COMPLETE"
Complete-DawoudUiSession -State $state
Assert-DawoudUi (-not $state.FailedRender) ("completed terminal render failed: {0}" -f $state.FailedRenderReason)
Assert-DawoudUi ($state.CurrentAction -eq $null) "completed session must expose idle current action"
Assert-DawoudUi ($state.Status -eq "DONE") "completed session must expose DONE status"
"AGEX UI TESTS: PASS"

Assert-DawoudUi (-not $normal[0].StartsWith([string][char]0x250C)) "dashboard must not be one bordered box"
