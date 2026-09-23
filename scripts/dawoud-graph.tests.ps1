$ErrorActionPreference='Stop'
. "$PSScriptRoot/dawoud-common.ps1"
. "$PSScriptRoot/dawoud-ui.ps1"
. "$PSScriptRoot/dawoud-graph.ps1"
function Assert-Graph($condition,$message) { if (-not $condition) { throw $message } }
$Project=$PSScriptRoot
$script:ui=New-DawoudUiState -Project $Project -SessionId 'truth-test'
$workspacePrompt=New-DawoudExecutorPrompt -RootGoal 'Create artifact' -Entry ([pscustomobject]@{Task='Write proof';AffectedFiles=@('proof.txt')}) -Workspace 'C:\fixture workspace' -Mail '[]' -Context '[]'
Assert-Graph ($workspacePrompt -match [regex]::Escape('AUTHORITATIVE EXECUTOR WORKSPACE: C:\fixture workspace')) 'Executor prompt must name authoritative workspace'
Assert-Graph ($workspacePrompt -match 'Never use AGY scratch') 'Executor prompt must prohibit AGY scratch workspace'
$authLog=Join-Path $PSScriptRoot '.agy-auth-classification-test.log'
try {
    [IO.File]::WriteAllText($authLog,"You are not logged into Antigravity.`nOAuth: authenticated successfully as test@example.test`nPrint mode: silent auth succeeded`n")
    Assert-Graph (-not (Test-DawoudAgyAuthFailure -CliLogPath $authLog)) 'Successful silent auth must supersede early auth warning'
    Add-Content -LiteralPath $authLog -Value 'authentication required'
    Assert-Graph (Test-DawoudAgyAuthFailure -CliLogPath $authLog) 'Final auth failure must remain an executor blocker'
} finally { Remove-Item -LiteralPath $authLog -Force -ErrorAction SilentlyContinue }
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
$auditState=New-DawoudUiState -Project $Project -SessionId 'plan-audit-test'
$duplicatePlan='{"goal_status":"CONTINUE","tasks":[{"id":"shared","title":"Inspect source","objective":"Read source","executor":"Codex"},{"id":"shared","title":"Verify source","objective":"Verify source","executor":"Codex","dependencies":["Inspect source"]}]}'
$duplicate=ConvertFrom-DawoudPlan -Text $duplicatePlan -AuditState $auditState
Assert-Graph ((@($duplicate.tasks).id -join ',') -eq 'task-0001,task-0002') 'Duplicate leader ids must receive unique canonical ids'
Assert-Graph ($duplicate.tasks[1].dependencies -eq 'task-0001') 'Temporary title dependency must map to canonical id'
Assert-Graph ($auditState.LeaderPlanAudit[-1].DuplicateIds -eq 'shared') 'Raw duplicate id audit missing'
$missing=ConvertFrom-DawoudPlan -Text '{"goal_status":"CONTINUE","tasks":[{"title":"No id","objective":"Read","executor":"Codex"}]}'
Assert-Graph ($missing.tasks[0].id -eq 'task-0001') 'Missing leader id must receive canonical id'
$temporary=ConvertFrom-DawoudPlan -Text '{"goal_status":"CONTINUE","tasks":[{"id":"inspect","title":"Inspect","objective":"Read","executor":"Codex"},{"id":"verify","title":"Verify","objective":"Check","executor":"Codex","dependencies":["Inspect"]}]}'
Assert-Graph ($temporary.tasks[1].dependencies -eq 'task-0001') 'Temporary dependency label must map to canonical id'
$unknownRejected=$false;try{ConvertFrom-DawoudPlan -Text '{"goal_status":"CONTINUE","tasks":[{"title":"A","objective":"Read","executor":"Codex","dependencies":["missing"]}]}'|Out-Null}catch{$unknownRejected=$_.Exception.Message -match "Unknown dependency 'missing'"}
Assert-Graph $unknownRejected 'Unknown dependency must report exact target'
$selfRejected=$false;try{ConvertFrom-DawoudPlan -Text '{"goal_status":"CONTINUE","tasks":[{"id":"self","title":"Self","objective":"Read","executor":"Codex","dependencies":["self"]}]}'|Out-Null}catch{$selfRejected=$_.Exception.Message -match 'Self dependency on task-0001'}
Assert-Graph $selfRejected 'Self dependency must report canonical task'
$cycleRejected=$false;try{ConvertFrom-DawoudPlan -Text '{"goal_status":"CONTINUE","tasks":[{"id":"a","title":"A","objective":"Read A","executor":"Codex","dependencies":["b"]},{"id":"b","title":"B","objective":"Read B","executor":"Codex","dependencies":["a"]}]}'|Out-Null}catch{$cycleRejected=$_.Exception.Message -match 'Dependency cycle: task-0001 -> task-0002 -> task-0001'}
Assert-Graph $cycleRejected 'Cycle must report exact canonical path'
$existingRepair=[pscustomobject]@{Id='task-0001';Status='REPAIR REQUIRED';Dependencies=@()}
$followup=ConvertFrom-DawoudPlan -Existing @($existingRepair) -Text '{"goal_status":"CONTINUE","tasks":[{"id":"repair","title":"Repair","objective":"Fix","executor":"Codex","repair_for":["task-0001"]}]}'
Assert-Graph ($followup.tasks[0].id -eq 'task-0002' -and $followup.tasks[0].repair_for -eq 'task-0001') 'Follow-up repair must use canonical allocator and remap target'
$initialPlan=ConvertFrom-DawoudPlan -Text '{"goal_status":"CONTINUE","tasks":[{"id":"implement-utility-and-readme","title":"Implement utility and README","objective":"Create files","executor":"Antigravity"}]}'
$initialTask=$initialPlan.tasks[0]
$existingTask=[pscustomobject]@{Id=$initialTask.id;OriginalId=$initialTask.original_id;Aliases=@($initialTask.aliases);Summary=$initialTask.title;Task=$initialTask.objective;Status='DONE';Dependencies=@()}
$repairByOriginal=ConvertFrom-DawoudPlan -Existing @($existingTask) -Text '{"goal_status":"CONTINUE","tasks":[{"id":"repair-original","title":"Repair original","objective":"Fix files","executor":"Antigravity","repair_for":["implement-utility-and-readme"]}]}'
Assert-Graph ($repairByOriginal.tasks[0].repair_for -eq 'task-0001') 'Original model repair id must resolve to canonical task'
$repairByCanonical=ConvertFrom-DawoudPlan -Existing @($existingTask) -Text '{"goal_status":"CONTINUE","tasks":[{"id":"repair-canonical","title":"Repair canonical","objective":"Fix files","executor":"Antigravity","repair_for":["task-0001"]}]}'
Assert-Graph ($repairByCanonical.tasks[0].repair_for -eq 'task-0001') 'Canonical repair id must resolve directly'
$repairByTitle=ConvertFrom-DawoudPlan -Existing @($existingTask) -Text '{"goal_status":"CONTINUE","tasks":[{"id":"repair-title","title":"Repair title","objective":"Fix files","executor":"Antigravity","repair_for":["implement_utility_and_readme"]}]}'
Assert-Graph ($repairByTitle.tasks[0].repair_for -eq 'task-0001') 'Unique normalized title must resolve to canonical task'
$unknownRepair=$false;try{ConvertFrom-DawoudPlan -Existing @($existingTask) -Text '{"goal_status":"CONTINUE","tasks":[{"id":"repair-unknown","title":"Repair unknown","objective":"Fix","executor":"Antigravity","repair_for":["missing-target"]}]}'|Out-Null}catch{$unknownRepair=$_.Exception.Message -match "UNKNOWN TARGET 'missing-target'"}
Assert-Graph $unknownRepair 'Unknown repair target must report UNKNOWN TARGET'
$ambiguousExisting=@([pscustomobject]@{Id='task-0001';OriginalId='first';Aliases=@('shared-title');Summary='Shared title';Task='First';Status='DONE';Dependencies=@()},[pscustomobject]@{Id='task-0002';OriginalId='second';Aliases=@('shared title');Summary='Shared-title';Task='Second';Status='DONE';Dependencies=@()})
$ambiguousRepair=$false;try{ConvertFrom-DawoudPlan -Existing $ambiguousExisting -Text '{"goal_status":"CONTINUE","tasks":[{"id":"repair-ambiguous","title":"Repair ambiguous","objective":"Fix","executor":"Antigravity","repair_for":["shared_title"]}]}'|Out-Null}catch{$ambiguousRepair=$_.Exception.Message -match "AMBIGUOUS TARGET 'shared_title'"}
Assert-Graph $ambiguousRepair 'Ambiguous normalized repair target must report AMBIGUOUS TARGET'
$repairExisting=[pscustomobject]@{Id=$repairByOriginal.tasks[0].id;OriginalId=$repairByOriginal.tasks[0].original_id;Aliases=@($repairByOriginal.tasks[0].aliases);Summary=$repairByOriginal.tasks[0].title;Task=$repairByOriginal.tasks[0].objective;Status='DONE';Dependencies=@()}
$recursiveRepair=ConvertFrom-DawoudPlan -Existing @($existingTask,$repairExisting) -Text '{"goal_status":"CONTINUE","tasks":[{"id":"repair-repair","title":"Repair repair","objective":"Fix again","executor":"Antigravity","repair_for":["repair-original"]}]}'
Assert-Graph ($recursiveRepair.tasks[0].repair_for -eq 'task-0002') 'Repair alias must resolve recursively'
$existingStatusBefore=$existingTask.Status;$invalidRepair=$false;try{ConvertFrom-DawoudPlan -Existing @($existingTask) -Text '{"goal_status":"CONTINUE","tasks":[{"id":"bad-repair","title":"Bad repair","objective":"Fix","executor":"Antigravity","repair_for":["missing"]}]}'|Out-Null}catch{$invalidRepair=$true}
Assert-Graph ($invalidRepair -and $existingTask.Status -eq $existingStatusBefore) 'Invalid repair batch must preserve existing graph state'
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
