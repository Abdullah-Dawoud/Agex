[CmdletBinding()]
param([int]$DeadlineSeconds = 1200)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/agex-common.ps1"
. "$PSScriptRoot/agex-acceptance-fixture.ps1"
Write-Host "AGEX ACCEPTANCE: PREPARING" -ForegroundColor Cyan
$codex=& "$PSScriptRoot/resolve-codex.ps1"
$fixture=Join-Path (Split-Path $PSScriptRoot -Parent) ('.tmp/agex-real-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
& git -C $fixture init --quiet
$verificationHost = Resolve-AgeXPowerShellHost
$verifyFixture = New-AgeXAcceptanceVerifyFixture -Project $fixture
if (-not (Test-AgeXAcceptanceFixtureEncoding -Path $verifyFixture)) { throw 'FIXTURE_ENCODING_INVALID: verify.ps1 failed post-generation validation.' }
$readmeFixture = New-AgeXAcceptanceReadmeFixture -Project $fixture
if (-not (Test-AgeXAcceptanceReadmeFixture -Path $readmeFixture)) { throw 'README_ENCODING_INVALID: README.md failed post-generation validation.' }
$verificationCommand = Get-AgeXPowerShellInvocation -HostPath $verificationHost.Path -ScriptPath '.\verify.ps1'
. "$PSScriptRoot/agex-primary.ps1" -Project $fixture -CodexPath $codex -SessionId ('real-'+[guid]::NewGuid().ToString('N')) -ConfiguredLeader Codex -DefinitionsOnly
[void](Assert-AgexUiStateSchema -State $script:ui)
$script:ui.AcceptanceStage='STARTUP READY'
Write-Host "AGEX ACCEPTANCE: STARTUP READY | session state schema valid | verification host $($verificationHost.Path)" -ForegroundColor Green
$runtime=Get-AgexRuntimeIdentity
if ($runtime.Restricted) {
    Write-Output "NORMAL_USER_REQUIRED"
    Write-Output "Current identity: $($runtime.Name)"
    Write-Output 'Startup and session state initialized; executor launch skipped.'
    Write-Output 'Run agex acceptance from normal user PowerShell.'
    $tempRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) '.tmp')).TrimEnd('\')+'\'
    $fixturePath=[IO.Path]::GetFullPath($fixture)
    if ($fixturePath.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $fixturePath -Leaf) -like 'agex-real-*') { Remove-Item -LiteralPath $fixturePath -Recurse -Force -ErrorAction SilentlyContinue }
    exit 42
}
$goal=@"
Build a small dependency-free PowerShell utility in this disposable Git project. Implement Convert-Names.ps1 to accept a string array, trim names, discard blanks and deduplicate case-insensitively preserving first spelling and order. AGEX generated README.md as UTF-8 BOM, ASCII-safe acceptance documentation with examples and error expectations. Preserve its AGEX_ACCEPTANCE_README_V1 marker and do not replace it with raw non-ASCII examples. AGEX also generated verify.ps1 as an encoding-safe acceptance fixture. Do not replace its test data. Execute it after implementation with this verified command: $verificationCommand. PowerShell 7 required: NO. Do not attempt pwsh unless AGEX identifies it as the verified host. If a required tool is unavailable, report BLOCKER TOOL_UNAVAILABLE immediately. The fixture performs a real Unicode case-variant test through code-point construction and must print VERIFICATION: PASS. Codex can independently review the proposed behavior while Antigravity implements files. Exchange a useful explicit review question and answer between Codex and Antigravity through AGEX messages. Select your own semantic task boundaries, dependencies and file ownership. Keep this bounded small fixture; use installed PowerShell, no downloads. Reconcile original requirements against actual files and verification. Repair omissions if found. Do not claim completion from successful worker exits alone.
"@
$script:queuedPrompts=[System.Collections.Generic.Queue[string]]::new()
$script:ui.AcceptanceStage='PREPARING'
$script:ui.Status='RUNNING'
[void](Start-AgexTaskExecution -Prompt $goal)
$printed = 0
$deadline=(Get-Date).AddSeconds($DeadlineSeconds)
$out=Join-Path $fixture 'acceptance-evidence.json'
Write-Output "FIXTURE: $fixture"
try {
    while ($script:activeExecution -and (Get-Date) -lt $deadline) {
        [void](Complete-AgexTaskExecution)
        [void](Invoke-AgexUiObserverSafely -State $script:ui -Action 'Update ingestion' -Operation { Receive-AgexUiUpdates -State $script:ui })
        $now=Get-Date
        Write-AgexActivityLines -State $script:ui -LastSeq ([ref]$printed)
        if ($script:ui.LastEvidenceAt -eq [datetime]::MinValue -or ($now-$script:ui.LastEvidenceAt).TotalSeconds -ge 2) {
            [void](Invoke-AgexUiObserverSafely -State $script:ui -Action 'Evidence snapshot' -Operation {
                $snapshot=New-AgexUiRenderSnapshot -State $script:ui
                $planAudit=@(Get-AgexUiCollectionSnapshot -State $script:ui -Collection LeaderPlanAudit)
                $evidence=@{goal=$snapshot.GoalStatus;stage=$snapshot.AcceptanceStage;tasks=@($snapshot.Tasks);agents=@($snapshot.Agents.Values);messages=@($snapshot.Chat);files=@($snapshot.Files.Values);plan_audit=$planAudit;assignments=$snapshot.AssignmentCount}
                Write-AgeXAcceptanceEvidence -Path $out -Evidence $evidence
                $script:ui.LastEvidenceAt=$now
            })
        }
        Start-Sleep -Milliseconds 250
    }
    if ($script:activeExecution) { Request-AgexTaskCancellation }
    $snapshot=New-AgexUiRenderSnapshot -State $script:ui
    $planAudit=@(Get-AgexUiCollectionSnapshot -State $script:ui -Collection LeaderPlanAudit)
    $evidence=@{goal=$snapshot.GoalStatus;result=$snapshot.Result;tasks=@($snapshot.Tasks);agents=@($snapshot.Agents.Values);messages=@($snapshot.Chat);files=@($snapshot.Files.Values);plan_audit=$planAudit;cleanup=$snapshot.CancellationProcessesCleaned}
    Write-AgeXAcceptanceEvidence -Path $out -Evidence $evidence
    Write-Output "EVIDENCE: $out"
    Write-Output $script:ui.Result
} finally {
    if ($script:activeExecution) { Request-AgexTaskCancellation }
    $tempRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) '.tmp')).TrimEnd('\')+'\'
    $fixturePath=[IO.Path]::GetFullPath($fixture)
    if ($fixturePath.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $fixturePath -Leaf) -like 'agex-real-*') {
        if ($script:ui.GoalStatus -like 'COMPLETE*') {
            Remove-Item -LiteralPath $fixturePath -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            Write-Output "FIXTURE RETAINED: $fixturePath"
        }
    }
}
