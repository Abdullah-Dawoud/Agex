$ErrorActionPreference='Stop'
. "$PSScriptRoot/dawoud-common.ps1"
. "$PSScriptRoot/dawoud-ui.ps1"
. "$PSScriptRoot/dawoud-graph.ps1"
function Assert-Graph($condition,$message) { if (-not $condition) { throw $message } }
$Project=$PSScriptRoot
$script:ui=New-DawoudUiState -Project $Project -SessionId 'truth-test'
$absent=Get-DawoudTaskVerification ([pscustomobject]@{AffectedFiles=@('never-created-artifact.txt');Result='Created never-created-artifact.txt'})
Assert-Graph (-not $absent.Pass -and -not $absent.Evidence[0].exists) 'False file claim must fail verification'
$verifiedPath=Join-Path $PSScriptRoot '.dawoud-verified-fixture.txt'
try {
    [IO.File]::WriteAllText($verifiedPath,'actual output')
    $present=Get-DawoudTaskVerification ([pscustomobject]@{AffectedFiles=@('.dawoud-verified-fixture.txt');Result='Created .dawoud-verified-fixture.txt'})
    Assert-Graph ($present.Pass -and $present.Evidence[0].sha256) 'Existing output must include filesystem evidence'
} finally { Remove-Item -LiteralPath $verifiedPath -Force -ErrorAction SilentlyContinue }
Assert-Graph (Test-DawoudPathConflict @{AffectedFiles=@('src')} @{AffectedFiles=@('src/a.ps1')}) 'Directory overlap missed'
Assert-Graph (Test-DawoudPathConflict @{AffectedFiles=@()} @{AffectedFiles=@('a.ps1')}) 'Unknown ownership must serialize'
Assert-Graph (-not (Test-DawoudPathConflict @{AffectedFiles=@('a.ps1')} @{AffectedFiles=@('b.ps1')})) 'Independent files conflict'
$rejected=$false
try { ConvertFrom-DawoudPlan '{"goal_status":"COMPLETE","tasks":[]}' } catch { $rejected=$true }
Assert-Graph $rejected 'Missing verification accepted'
$many=@(1..25 | ForEach-Object { @{id="item-$_";objective='Read';executor='Codex';dependencies=@()} })
$plan=ConvertFrom-DawoudPlan (@{goal_status='CONTINUE';tasks=$many} | ConvertTo-Json -Depth 5)
Assert-Graph (@($plan.tasks).Count -eq $many.Count) 'Task truncation'
$script:ui=New-DawoudUiState -Project $Project -SessionId 'mail-test'
$script:cancellationSignal=@{Requested=$false};$script:mailboxTurns=0
$script:recipients=[System.Collections.Generic.List[object]]::new()
function Invoke-DawoudGraphExecutor { param($Agent,$Prompt,$WorkId); [void]$script:recipients.Add([pscustomobject]@{Agent=$Agent;Prompt=$Prompt;WorkId=$WorkId});$script:lastExecutorResult='Explicit answer';$true }
Add-DawoudOperationalMessages -Entry ([pscustomobject]@{Id='review';Agent='Codex';Result='{"messages":[{"to":"Antigravity","type":"QUESTION","content":"What changed?"}]}'})
Invoke-DawoudMailbox -RootGoal 'Inspect' -WorkId 'mail-test'
Assert-Graph (($script:recipients.Agent -join ',') -eq 'Antigravity,Codex') 'Mailbox failed bidirectional delivery'
Assert-Graph ($script:recipients[0].Prompt.Contains($script:ui.Chat[0].message_id) -and $script:recipients[1].Prompt.Contains($script:ui.Chat[1].message_id)) 'Message identity did not reach both recipient executions'
Assert-Graph ($script:ui.Chat[0].status -eq 'ANSWERED' -and $script:ui.Chat[1].status -eq 'ANSWERED') 'Mailbox responses were not consumed/answered'
$prior=[pscustomobject]@{Id='broken';Status='REPAIR REQUIRED'}
$repair=[pscustomobject]@{Id='fix';Dependencies=@('broken');RepairFor=@('broken')}
$script:ui.Tasks.Clear();Add-DawoudUiTask -State $script:ui -Task $prior
Assert-Graph (Test-DawoudTaskDependenciesSatisfied -Entry $repair) 'Repair task deadlocks behind the failed task it repairs'
$unrelated=[pscustomobject]@{Id='other';Status='REPAIR REQUIRED'}
$repair.RepairFor=@()
Assert-Graph (-not (Test-DawoudTaskDependenciesSatisfied -Entry $repair)) 'Unrelated failed prerequisite incorrectly satisfied'
'AGEX SCHEDULER/MAILBOX CHECKS: COMPONENT PASS'
