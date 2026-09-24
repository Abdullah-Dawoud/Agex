$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "agex-common.ps1")
. (Join-Path $PSScriptRoot "agex-ui.ps1")

function Assert-AgexUi {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "UI TEST FAIL: $Message" }
}

function Get-FrameText {
    param($State, [int]$Width = 100, [int]$Height = 30)
    $frame = Get-AgexScreenFrame -State (New-AgexUiRenderSnapshot -State $State) -Runtime (New-AgexRuntime) -Editor (New-AgexEditor) -View @{ Name = "NORMAL"; Lines = @(); Scroll = 0 } -Width $Width -Height $Height
    [pscustomobject]@{ Frame = $frame; Text = (($frame.Rows | ForEach-Object { $_.Text }) -join "`n") }
}

# State schema and update ingestion
$state = New-AgexUiState -Project (Get-Location).Path -SessionId "ui-test" -ConfiguredLeader "Auto" -ResolvedLeader "Antigravity" -CodexShare 10 -AntigravityShare 90 -CodexModel "codex-test" -AntigravityModel "agy-test"
Assert-AgexUi ([bool](Assert-AgexUiStateSchema -State $state)) "state must include all required fields"
Assert-AgexUi ($state.GoalStatus -eq "IDLE" -and $null -eq $state.Outcome) "a new session must not start as PARTIAL"
$live = New-AgexUiState -Project (Get-Location).Path -SessionId "live-bridge-test"
Add-AgexUiTask -State $live -Task ([pscustomobject]@{ Id = "first"; Summary = "First task"; Agent = "Antigravity"; Status = "QUEUED"; Started = [datetime]::MinValue; End = [datetime]::MinValue; UpdatedAt = Get-Date })
Receive-AgexUiUpdates -State $live
Assert-AgexUi ($live.Tasks.Count -eq 1) "UI thread must ingest a new task"

# Updates cross a real runspace boundary
$bridge = New-AgexUiState -Project (Get-Location).Path -SessionId "runspace-bridge-test"
$bridgeTask = [pscustomobject]@{ Id = "runspace-task"; Summary = "Published by worker runspace"; Agent = "Codex"; Status = "RUNNING"; UpdatedAt = Get-Date; Started = Get-Date; End = [datetime]::MinValue }
$workerRunspace = [RunspaceFactory]::CreateRunspace(); $workerRunspace.Open()
$workerPowerShell = [PowerShell]::Create(); $workerPowerShell.Runspace = $workerRunspace
[void]$workerPowerShell.AddScript('param($state,$task) $state.UiUpdates.Enqueue([pscustomobject]@{Sequence=1;Kind="TASK";Value=$task;At=Get-Date})')
[void]$workerPowerShell.AddArgument($bridge); [void]$workerPowerShell.AddArgument($bridgeTask)
[void]$workerPowerShell.Invoke(); $workerPowerShell.Dispose(); $workerRunspace.Dispose()
Receive-AgexUiUpdates -State $bridge
Assert-AgexUi ($bridge.Tasks.Count -eq 1 -and $bridge.Tasks[0].Id -eq "runspace-task") "task update must cross a runspace boundary"
$bridge.Status = "RUNNING"
Assert-AgexUi ((Get-FrameText -State $bridge).Text -match "Codex: Published by worker runspace") "a running task must be shown as the current work"

# Malformed and stale updates are rejected without stopping ingestion
$contract = New-AgexUiState -Project (Get-Location).Path -SessionId "contract"
Publish-AgexUiUpdate -State $contract -Kind 'COUNT' -Value ([int]4)
$contract.UiUpdates.Enqueue([pscustomobject]@{ Sequence = [int]5; Kind = 'COUNT'; Value = [pscustomobject]@{}; At = Get-Date })
$contract.UiUpdateClock.Value = [int]5
Publish-AgexUiUpdate -State $contract -Kind 'COUNT' -Value ([int]6)
$failed = $false
try { Receive-AgexUiUpdates -State $contract } catch { $failed = $true }
Assert-AgexUi (-not $failed -and $contract.AssignmentCount -eq 6 -and $contract.UiAppliedSequence -eq 6) "malformed update must not abort ingestion"
Assert-AgexUi (@($contract.Events | Where-Object { $_.Kind -eq 'UPDATE REJECTED' }).Count -eq 1) "rejected update must leave one warning"
$contract.UiUpdates.Enqueue([pscustomobject]@{ Sequence = [int]5; Kind = 'COUNT'; Value = [int]99; At = Get-Date })
Receive-AgexUiUpdates -State $contract
Assert-AgexUi ($contract.AssignmentCount -eq 6) "stale update must not overwrite newer state"

# Observer failures are contained
$fallback = New-AgexUiState -Project (Get-Location).Path -SessionId 'fallback'
$fallback.Status = 'RUNNING'
$ok = Invoke-AgexUiObserverSafely -State $fallback -Action 'test render' -Operation { throw 'controlled renderer failure' }
Assert-AgexUi (-not $ok -and $fallback.Status -eq 'RUNNING') "observer failure must not change execution state"

# Concurrent writers from a runspace while the UI thread reads and renders
$stress = New-AgexUiState -Project (Get-Location).Path -SessionId 'stress'
$stress.Status = 'RUNNING'
$stressCode = @'
param($state,$iterations,$uiPath)
. $uiPath
[void](Start-AgexUiAgent -State $state -Name 'stress-agent' -Executor 'CODEX' -TaskId 'stress-0' -TaskText 'Concurrency stress' -Model 'codex-test')
for($i=0;$i -lt $iterations;$i++) {
  $id='stress-'+$i
  Add-AgexUiTask -State $state -Task ([pscustomobject]@{Id=$id;Summary='Concurrent task';Agent='Codex';Status='QUEUED';Started=[datetime]::MinValue;End=[datetime]::MinValue;UpdatedAt=Get-Date}) | Out-Null
  Set-AgexUiTask -State $state -TaskId $id -Status 'RUNNING' -Agent 'Codex' | Out-Null
  Add-AgexMessage -State $state -From 'Codex' -To 'AGEX' -Type STATUS -Text "update $i"
  [System.Threading.Monitor]::Enter($state.UiUpdateClock.SyncRoot)
  try { $state.AssignmentCount=[int]$state.AssignmentCount+1; $count=[int]$state.AssignmentCount } finally { [System.Threading.Monitor]::Exit($state.UiUpdateClock.SyncRoot) }
  Publish-AgexUiUpdate -State $state -Kind 'COUNT' -Value $count
}
'@
$rs = [RunspaceFactory]::CreateRunspace(); $rs.Open()
$ps = [PowerShell]::Create(); $ps.Runspace = $rs
[void]$ps.AddScript($stressCode).AddArgument($stress).AddArgument(200).AddArgument((Join-Path $PSScriptRoot 'agex-ui.ps1'))
$async = $ps.BeginInvoke()
$errors = [System.Collections.Generic.List[string]]::new()
while (-not $async.IsCompleted) {
    try { [void](Get-FrameText -State $stress); Receive-AgexUiUpdates -State $stress } catch { [void]$errors.Add($_.Exception.Message) }
}
try { [void]$ps.EndInvoke($async) } catch { [void]$errors.Add($_.Exception.Message) }
$ps.Dispose(); $rs.Dispose()
Receive-AgexUiUpdates -State $stress
Assert-AgexUi ($errors.Count -eq 0) ("concurrent render raised: {0}" -f ($errors -join '; '))
Assert-AgexUi ($stress.Tasks.Count -eq 200 -and $stress.AssignmentCount -eq 200 -and $stress.Messages.Count -eq 200) "concurrent updates must stay complete"
Assert-AgexUi (@($stress.Messages | ForEach-Object Seq | Sort-Object -Unique).Count -eq 200) "message sequence numbers must be unique"

# Bounded histories and cancelled state
for ($i = 0; $i -lt 200; $i++) { [void](Add-AgexUiEvent -State $state -Source "TEST" -Kind "ACTION" -Message ("bounded event {0} {1}" -f $i, ("x" * 400))) }
Assert-AgexUi ($state.Events.Count -eq 160) "event history must stay bounded"
for ($i = 0; $i -lt 240; $i++) { Set-AgexUiFile -State $state -Path ("bounded/{0}.txt" -f $i) -Action M | Out-Null }
Assert-AgexUi ($state.Files.Count -le 200) "file activity must stay bounded"
$cancel = New-AgexUiState -Project $state.Project -SessionId "cancel-test"
Add-AgexUiTask -State $cancel -Task ([pscustomobject]@{ Id = "slice-cancel"; Summary = "Cancel"; Agent = "CODEX"; Status = "RUNNING"; Started = Get-Date; End = [datetime]::MinValue })
[void](Start-AgexUiAgent -State $cancel -Name "CODEX" -Executor "CODEX" -TaskId "slice-cancel" -TaskText "Cancel" -Model "codex-test" -Command "codex exec")
[void](Complete-AgexUiAgent -State $cancel -Name "CODEX" -Status "CANCELLED" -Message "user cancel" -ExitCode 130)
[void](Set-AgexUiTask -State $cancel -TaskId "slice-cancel" -Status "CANCELLED" -Agent "CODEX")
Complete-AgexUiSession -State $cancel
Assert-AgexUi ($cancel.Status -eq "CANCELLED") "cancelled session must stay CANCELLED"
$done = New-AgexUiState -Project $state.Project -SessionId "done-test"
$done.GoalStatus = "COMPLETE"
Complete-AgexUiSession -State $done
Assert-AgexUi ($done.Status -eq "COMPLETE" -and $null -eq $done.CurrentAction) "completed session must expose COMPLETE"
$none = New-AgexUiState -Project $state.Project -SessionId "none-test"
Complete-AgexUiSession -State $none
Assert-AgexUi ($none.Status -eq "START_FAILED" -and @($none.Events | Where-Object Message -match 'no tasks were started').Count -eq 1) "zero tasks without an outcome is START_FAILED, never PARTIAL"

# Frame: ASCII fallback, long text clipped, collaboration messages never fabricated
$state.Unicode = $false
$ascii = Get-FrameText -State $state -Width 80 -Height 24
Assert-AgexUi (($ascii.Text -match '[^\x00-\x7F]') -eq $false) "ASCII mode must not emit Unicode glyphs"
$state.Unicode = $true
$long = New-AgexUiState -Project $state.Project -SessionId "long" -ConfiguredLeader Codex -ResolvedLeader Codex -CodexShare 10 -AntigravityShare 90
$long.Status = "RUNNING"; $long.RequestStarted = Get-Date
Add-AgexUiTask -State $long -Task ([pscustomobject]@{ Id = "slice-long"; Summary = ("very long title " * 40); Agent = "Antigravity"; Status = "RUNNING"; Started = Get-Date; End = [datetime]::MinValue })
$frame = (Get-FrameText -State $long -Width 60 -Height 26).Frame
Assert-AgexUi ($frame.Rows.Count -eq 25) "frame must fill the window height minus one row"
Add-AgexMessage -State $long -From 'Codex' -To 'Antigravity' -Type ASSIGNMENT -Text ""
Assert-AgexUi ($long.Messages.Count -eq 0) "empty agent output must not create a message"
$secret = "token=abc123secret"
Add-AgexMessage -State $long -From 'Codex' -To 'AGEX' -Type RESULT -Text "done $secret"
Assert-AgexUi ($long.Messages[0].Text -notmatch 'abc123secret') "messages must be sanitized"
"AGEX UI TESTS: PASS"
