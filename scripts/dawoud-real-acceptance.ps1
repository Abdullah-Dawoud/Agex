[CmdletBinding()]
param([int]$DeadlineSeconds = 240)
$ErrorActionPreference='Stop'
$fixture=Join-Path (Split-Path $PSScriptRoot -Parent) ('.tmp/dawoud-real-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
& git -C $fixture init --quiet
$codex=& "$PSScriptRoot/resolve-codex.ps1"
. "$PSScriptRoot/dawoud-primary.ps1" -Project $fixture -CodexPath $codex -SessionId ('real-'+[guid]::NewGuid().ToString('N')) -ConfiguredLeader Codex -DefinitionsOnly
$goal=@'
Build a small dependency-free PowerShell utility in this disposable Git project. Implement Convert-Names.ps1 to accept a string array, trim names, discard blanks and deduplicate case-insensitively preserving first spelling and order. Independently provide README.md with examples and error expectations. Add verify.ps1 after implementation, execute it against whitespace, mixed case duplicates and Unicode, and inspect actual output. Codex can independently review the proposed behavior while Antigravity implements files. Exchange a useful explicit review question and answer between Codex and Antigravity through DAWOUD messages. Select your own semantic task boundaries, dependencies and file ownership. Keep this bounded small fixture; use installed PowerShell, no downloads. Reconcile the original requirements against actual files and verification. Repair omissions if found. Do not claim completion from successful worker exits alone.
'@
$script:queuedPrompts=[System.Collections.Generic.Queue[string]]::new()
[void](Start-DawoudTaskExecution -Prompt $goal)
$deadline=(Get-Date).AddSeconds($DeadlineSeconds)
$out=Join-Path $fixture 'acceptance-evidence.json'
Write-Output "FIXTURE: $fixture"
try {
    while ($script:activeExecution -and (Get-Date) -lt $deadline) {
        [void](Complete-DawoudTaskExecution)
        $evidence=@{goal=$script:ui.GoalStatus;result=$script:ui.Result;tasks=@($script:ui.Tasks);agents=@($script:ui.Agents.Values);messages=@($script:ui.Chat);files=@($script:ui.Files.Values)}
        $evidence | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $out -Encoding utf8
        Start-Sleep -Seconds 2
    }
    if ($script:activeExecution) { Request-DawoudTaskCancellation }
    $evidence=@{goal=$script:ui.GoalStatus;result=$script:ui.Result;tasks=@($script:ui.Tasks);agents=@($script:ui.Agents.Values);messages=@($script:ui.Chat);files=@($script:ui.Files.Values);cleanup=$script:ui.CancellationProcessesCleaned}
    $evidence | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $out -Encoding utf8
    Write-Output "EVIDENCE: $out"
    Write-Output $script:ui.Result
} finally {
    if ($script:activeExecution) { Request-DawoudTaskCancellation }
}
