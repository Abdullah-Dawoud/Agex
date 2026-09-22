$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dawoud-common.ps1")
. (Join-Path $PSScriptRoot "dawoud-ui.ps1")

function Assert-DawoudUi {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "UI TEST FAIL: $Message" }
}

$state = New-DawoudUiState -Project (Get-Location).Path -SessionId "ui-test" -ConfiguredLeader "Auto" -ResolvedLeader "Antigravity" -CodexShare 10 -AntigravityShare 90 -CodexModel "codex-test" -AntigravityModel "agy-test"
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
Assert-DawoudUi ($normal[0].StartsWith("┌") -and $normal[0].EndsWith("┐")) "dashboard must have a stable top frame"
Assert-DawoudUi ($normal[-1].StartsWith("└") -and $normal[-1].EndsWith("┘")) "dashboard must have a stable bottom frame"
Assert-DawoudUi (($normal | ForEach-Object Length | Where-Object { $_ -ne 100 }).Count -eq 0) "wide frame rows must have exact width"
Assert-DawoudUi (($narrow | ForEach-Object Length | Where-Object { $_ -gt 48 }).Count -eq 0) "narrow view must fit width"
Assert-DawoudUi (($micro | ForEach-Object Length | Where-Object { $_ -gt 48 }).Count -eq 0) "micro view must fit width"
Assert-DawoudUi ($micro.Count -le 12) "micro view must fit terminal height"
Assert-DawoudUi (($normal | ForEach-Object Length | Where-Object { $_ -gt 100 }).Count -eq 0) "wide view must fit width"
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
Complete-DawoudUiSession -State $state
Assert-DawoudUi ($state.CurrentAction -eq $null) "completed session must expose idle current action"
Assert-DawoudUi ($state.Status -eq "DONE") "completed session must expose DONE status"
"DAWOUD UI TESTS: PASS"
