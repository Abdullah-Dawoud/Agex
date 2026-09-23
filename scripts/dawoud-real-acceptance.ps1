[CmdletBinding()]
param([int]$DeadlineSeconds = 240)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/dawoud-common.ps1"
Write-Host "AGEX ACCEPTANCE: PREPARING" -ForegroundColor Cyan
$codex=& "$PSScriptRoot/resolve-codex.ps1"
$fixture=Join-Path (Split-Path $PSScriptRoot -Parent) ('.tmp/dawoud-real-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
& git -C $fixture init --quiet
. "$PSScriptRoot/dawoud-primary.ps1" -Project $fixture -CodexPath $codex -SessionId ('real-'+[guid]::NewGuid().ToString('N')) -ConfiguredLeader Codex -DefinitionsOnly
[void](Assert-DawoudUiStateSchema -State $script:ui)
$script:ui.AcceptanceStage='STARTUP READY'
Write-Host "AGEX ACCEPTANCE: STARTUP READY | session state schema valid" -ForegroundColor Green
$runtime=Get-DawoudRuntimeIdentity
if ($runtime.Restricted) {
    Write-Output "NORMAL_USER_REQUIRED"
    Write-Output "Current identity: $($runtime.Name)"
    Write-Output 'Startup and session state initialized; executor launch skipped.'
    Write-Output 'Run agex acceptance from normal user PowerShell.'
    $tempRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) '.tmp')).TrimEnd('\')+'\'
    $fixturePath=[IO.Path]::GetFullPath($fixture)
    if ($fixturePath.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $fixturePath -Leaf) -like 'dawoud-real-*') { Remove-Item -LiteralPath $fixturePath -Recurse -Force -ErrorAction SilentlyContinue }
    exit 42
}
$goal=@'
Build a small dependency-free PowerShell utility in this disposable Git project. Implement Convert-Names.ps1 to accept a string array, trim names, discard blanks and deduplicate case-insensitively preserving first spelling and order. Independently provide README.md with examples and error expectations. Add verify.ps1 after implementation, execute it against whitespace, mixed case duplicates and Unicode, and inspect actual output. Codex can independently review the proposed behavior while Antigravity implements files. Exchange a useful explicit review question and answer between Codex and Antigravity through AGEX messages. Select your own semantic task boundaries, dependencies and file ownership. Keep this bounded small fixture; use installed PowerShell, no downloads. Reconcile the original requirements against actual files and verification. Repair omissions if found. Do not claim completion from successful worker exits alone.
'@
$script:queuedPrompts=[System.Collections.Generic.Queue[string]]::new()
$script:ui.AcceptanceStage='PREPARING'
$script:ui.Status='RUNNING'
[void](Start-DawoudTaskExecution -Prompt $goal)
[void](Write-DawoudDashboard -State $script:ui -Force)
$deadline=(Get-Date).AddSeconds($DeadlineSeconds)
$out=Join-Path $fixture 'acceptance-evidence.json'
Write-Output "FIXTURE: $fixture"
try {
    while ($script:activeExecution -and (Get-Date) -lt $deadline) {
        [void](Complete-DawoudTaskExecution)
        Receive-DawoudUiUpdates -State $script:ui
        $now=Get-Date
        [void](Write-DawoudDashboard -State $script:ui)
        if ($script:ui.LastEvidenceAt -eq [datetime]::MinValue -or ($now-$script:ui.LastEvidenceAt).TotalSeconds -ge 2) {
            $evidence=@{goal=$script:ui.GoalStatus;stage=$script:ui.AcceptanceStage;tasks=@($script:ui.Tasks);agents=@($script:ui.Agents.Values);messages=@($script:ui.Chat);files=@($script:ui.Files.Values);assignments=$script:ui.AssignmentCount}
            $evidence | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $out -Encoding utf8
            $script:ui.LastEvidenceAt=$now
        }
        Start-Sleep -Milliseconds 250
    }
    if ($script:activeExecution) { Request-DawoudTaskCancellation }
    $evidence=@{goal=$script:ui.GoalStatus;result=$script:ui.Result;tasks=@($script:ui.Tasks);agents=@($script:ui.Agents.Values);messages=@($script:ui.Chat);files=@($script:ui.Files.Values);cleanup=$script:ui.CancellationProcessesCleaned}
    $evidence | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $out -Encoding utf8
    Write-Output "EVIDENCE: $out"
    Write-Output $script:ui.Result
} finally {
    if ($script:activeExecution) { Request-DawoudTaskCancellation }
    $tempRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) '.tmp')).TrimEnd('\')+'\'
    $fixturePath=[IO.Path]::GetFullPath($fixture)
    if ($fixturePath.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $fixturePath -Leaf) -like 'dawoud-real-*') {
        Remove-Item -LiteralPath $fixturePath -Recurse -Force -ErrorAction SilentlyContinue
    }
}
