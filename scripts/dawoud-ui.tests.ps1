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
Assert-DawoudUi ($state.Events.Count -eq 160) "event history must stay bounded"
Assert-DawoudUi (($normal -join "`n") -match "ANTIGRAVITY #1") "normal view must show first worker"
Assert-DawoudUi (($normal -join "`n") -match "PID 7036") "normal view must show PID"
Assert-DawoudUi (($normal -join "`n") -match "CURRENT ACTION") "normal view must show current action"
Assert-DawoudUi (($normal -join "`n") -match "reports/result.txt") "normal view must show file activity"
Assert-DawoudUi (($normal -join "`n") -match "CODEX") "normal view must show Codex"
Assert-DawoudUi (($narrow | ForEach-Object Length | Where-Object { $_ -gt 48 }).Count -eq 0) "narrow view must fit width"
Assert-DawoudUi (($normal | ForEach-Object Length | Where-Object { $_ -gt 100 }).Count -eq 0) "wide view must fit width"
"DAWOUD UI TESTS: PASS"
