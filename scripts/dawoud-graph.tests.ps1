$ErrorActionPreference='Stop'
. "$PSScriptRoot/dawoud-common.ps1"
. "$PSScriptRoot/dawoud-ui.ps1"
. "$PSScriptRoot/dawoud-graph.ps1"
. "$PSScriptRoot/agex-acceptance-fixture.ps1"
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
$evidenceRoot=Join-Path $env:TEMP ('agex-evidence-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $evidenceRoot -Force|Out-Null
try {
    $Project=$evidenceRoot;[IO.File]::WriteAllText((Join-Path $Project 'Convert-Names.ps1'),'output');[IO.File]::WriteAllText((Join-Path $Project 'README.md'),'docs')
    $proseClaim=Get-DawoudTaskVerification ([pscustomobject]@{AffectedFiles=@('Convert-Names.ps1','README.md');Result='Created Convert-Names.ps1 and README.md. Convert-Names.ps1 was verified.'})
    Assert-Graph ($proseClaim.Pass -and $proseClaim.Claimed.Count -eq 2) 'Prose file list must not create a composite path claim'
    [IO.File]::WriteAllText((Join-Path $Project 'verify.ps1'),'# AGEX_ACCEPTANCE_VERIFY_V1',[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $Project 'README.md'), "# Convert-Names`n<!-- AGEX_ACCEPTANCE_README_V1 -->`nU+00E9`n## Error Expectations", [Text.UTF8Encoding]::new($true))
    $readmeEvidence=Get-DawoudTaskVerification ([pscustomobject]@{AffectedFiles=@('README.md');Result='Updated README.md'})
    Assert-Graph ($readmeEvidence.Pass -and -not $readmeEvidence.Evidence[0].content_error) 'UTF-8 BOM acceptance README must verify'
    [IO.File]::WriteAllText((Join-Path $Project 'README.md'), "# broken`nJos$([char]0x00C3)$([char]0x00A9)", [Text.UTF8Encoding]::new($false))
    $badReadmeEvidence=Get-DawoudTaskVerification ([pscustomobject]@{AffectedFiles=@('README.md');Result='Updated README.md'})
    Assert-Graph (-not $badReadmeEvidence.Pass -and $badReadmeEvidence.Evidence[0].content_error -match 'README_') 'Mojibaked acceptance README must fail verification'
} finally { Remove-Item -LiteralPath $evidenceRoot -Recurse -Force -ErrorAction SilentlyContinue;$Project=$PSScriptRoot }
$stateEntry=[pscustomobject]@{Id='task-state';Status='REPAIR REQUIRED';Error='old claim';VerificationHistory=[System.Collections.Generic.List[object]]::new()}
$newPass=[pscustomobject]@{Pass=$true;Evidence=@([pscustomobject]@{exists=$true});Reason='verified'};$oldFail=[pscustomobject]@{Pass=$false;Evidence=@();Reason='old claim'}
[void](Set-DawoudTaskVerificationState -Entry $stateEntry -Verification $newPass -ExecutionSuccess $true -EvidenceAt (Get-Date))
[void](Set-DawoudTaskVerificationState -Entry $stateEntry -Verification $oldFail -ExecutionSuccess $true -EvidenceAt (Get-Date).AddMinutes(-1))
Assert-Graph ($stateEntry.Status -eq 'DONE' -and $stateEntry.VerificationStatus -eq 'PASS' -and $stateEntry.VerificationHistory.Count -eq 2) 'New verified evidence must supersede stale failure while retaining history'
$script:ui=New-DawoudUiState -Project $Project -SessionId 'unblock-test';$prerequisite=[pscustomobject]@{Id='task-0001';Status='DONE';Dependencies=@()};$dependent=[pscustomobject]@{Id='task-0002';Status='BLOCKED';Dependencies=@('task-0001');Error='Unresolved dependency or cycle.';Reason=''};$downstream=[pscustomobject]@{Id='task-0003';Status='BLOCKED';Dependencies=@('task-0002');Error='Unresolved dependency or cycle.';Reason=''};Add-DawoudUiTask -State $script:ui -Task $prerequisite;Add-DawoudUiTask -State $script:ui -Task $dependent;Add-DawoudUiTask -State $script:ui -Task $downstream
$unlocked=@(Unlock-DawoudDependentTasks -TaskId 'task-0001')
Assert-Graph ($unlocked.Count -eq 1 -and $dependent.Status -eq 'QUEUED' -and (Test-DawoudTaskDependenciesSatisfied -Entry $dependent)) 'Verified repair must unblock dependent task'
$dependent.Status='DONE';$unlocked=@(Unlock-DawoudDependentTasks -TaskId 'task-0002')
Assert-Graph ($unlocked.Count -eq 1 -and $downstream.Status -eq 'QUEUED' -and (Test-DawoudTaskDependenciesSatisfied -Entry $downstream)) 'Completed review must unblock downstream verification'
$script:ui=New-DawoudUiState -Project $Project -SessionId 'mail-test'
$script:cancellationSignal=@{Requested=$false};$script:mailboxTurns=0
$script:recipients=[System.Collections.Generic.List[object]]::new()
function Invoke-DawoudGraphExecutor { param($Agent,$Prompt,$WorkId); [void]$script:recipients.Add([pscustomobject]@{Agent=$Agent;Prompt=$Prompt;WorkId=$WorkId});$script:lastExecutorResult=$(if($script:testAnswer){$script:testAnswer}else{'Explicit answer'});$true }
Add-DawoudOperationalMessages -Entry ([pscustomobject]@{Id='review';Agent='Codex';Result='{"messages":[{"to":"Antigravity","type":"QUESTION","content":"What changed?"}]}'})
Invoke-DawoudMailbox -RootGoal 'Inspect' -WorkId 'mail-test'
Assert-Graph (($script:recipients.Agent -join ',') -eq 'Antigravity,Codex') 'Mailbox failed bidirectional delivery'
Assert-Graph ($script:recipients[0].Prompt.Contains($script:ui.Chat[0].message_id) -and $script:recipients[1].Prompt.Contains($script:ui.Chat[1].message_id)) 'Message identity did not reach both recipient executions'
Assert-Graph ($script:ui.Chat[0].status -eq 'ANSWERED' -and $script:ui.Chat[1].status -eq 'ANSWERED') 'Mailbox responses were not consumed/answered'
Assert-Graph ($script:ui.Chat[1].AuthoritativeBody -eq 'Explicit answer' -and $script:ui.Chat[1].DeliveredAt) 'Mailbox answer must retain authoritative content and delivery time'
$script:ui.Chat.Clear();$script:recipients.Clear();$script:testAnswer=('Jos' + [char]0x00E9 + '; M' + [char]0x00FC + 'ller; ' + [char]0x0130 + 'stanbul; na' + [char]0x00EF + 've; r' + [char]0x00E9 + 'sum' + [char]0x00E9 + '; ' + [char]0x2019 + ' ' + [char]0x2013 + ' ' + [char]0x2192 + ' ' + ('x' * 320))
$longMessageJson=([pscustomobject]@{messages=@([pscustomobject]@{to='Antigravity';type='QUESTION';content=$script:testAnswer})}|ConvertTo-Json -Compress)
Add-DawoudOperationalMessages -Entry ([pscustomobject]@{Id='unicode-review';Agent='Codex';Result=$longMessageJson})
$longMessage=$script:ui.Chat[0]
Assert-Graph ($longMessage.AuthoritativeBody -ceq $script:testAnswer -and $longMessage.PreviewBody.Length -lt $longMessage.AuthoritativeBody.Length) 'Mailbox must separate full Unicode body from bounded preview'
Invoke-DawoudMailbox -RootGoal 'Inspect' -WorkId 'mail-unicode-test'
Assert-Graph ($script:recipients[0].Prompt.Contains($script:testAnswer) -and $script:ui.Chat[1].AuthoritativeBody -ceq $script:testAnswer) 'Mailbox delivery and answer must preserve full Unicode body'
$evidencePath=Join-Path $env:TEMP ('agex-message-evidence-'+[guid]::NewGuid().ToString('N')+'.json')
try { Write-AgeXAcceptanceEvidence -Path $evidencePath -Evidence @{messages=@($script:ui.Chat[0])};$roundTrip=(Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8|ConvertFrom-Json).messages[0].AuthoritativeBody;Assert-Graph ($roundTrip -ceq $script:testAnswer) 'Evidence JSON must preserve exact Unicode message body' } finally { Remove-Item -LiteralPath $evidencePath -Force -ErrorAction SilentlyContinue }
$script:ui.Chat.Clear();$duplicateReply='{"messages":[{"to":"Antigravity","type":"REVIEW","content":"Review actual artifact"}]}'
Add-DawoudOperationalMessages -Entry ([pscustomobject]@{Id='review';Agent='Codex';Result=($duplicateReply+$duplicateReply)})
Assert-Graph ($script:ui.Chat.Count -eq 1 -and $script:ui.Chat[0].status -eq 'QUEUED') 'Concatenated executor JSON must retain one operational review message'
Add-DawoudOperationalMessages -Entry ([pscustomobject]@{Id='review';Agent='Codex';Result='{"messages":[{"to":"Codex","type":"ANSWER","content":"self message"}]}'})
Assert-Graph ($script:ui.Chat.Count -eq 1) 'Self-directed executor messages must not trigger mailbox work'
$prior=[pscustomobject]@{Id='broken';Status='REPAIR REQUIRED'}
$repair=[pscustomobject]@{Id='fix';Dependencies=@('broken');RepairFor=@('broken')}
$script:ui.Tasks.Clear();Add-DawoudUiTask -State $script:ui -Task $prior
Assert-Graph (Test-DawoudTaskDependenciesSatisfied -Entry $repair) 'Repair task deadlocks behind the failed task it repairs'
$unrelated=[pscustomobject]@{Id='other';Status='REPAIR REQUIRED'}
$repair.RepairFor=@()
Assert-Graph (-not (Test-DawoudTaskDependenciesSatisfied -Entry $repair)) 'Unrelated failed prerequisite incorrectly satisfied'
$script:ui=New-DawoudUiState -Project $Project -SessionId 'final-reconcile-test';$script:cancellationSignal=@{Requested=$false};$script:ResolvedLeader='Codex'
$repaired=[pscustomobject]@{Id='task-0005';Summary='Repair documentation';Task='Restore README';Agent='Antigravity';Status='DONE';Dependencies=@();AffectedFiles=@('README.md');RepairFor=@('task-0004');Result='README bytes verified';Verification='PASS';VerifiedResult=$null;Attempt=1;Started=[datetime]::MinValue;End=[datetime]::MinValue;CreatedAt=(Get-Date);Reason='';Error=''}
Add-DawoudUiTask -State $script:ui -Task $repaired
$reconciliationMessage=[pscustomobject]@{MessageId='unicode-evidence';From='Antigravity';To='Codex';TaskId='task-0005';Type='ANSWER';AuthoritativeBody=$script:testAnswer;PreviewBody=(Protect-DawoudTelemetryText -Text $script:testAnswer);CreatedAt=(Get-Date).ToUniversalTime().ToString('o');DeliveredAt='';Status='ANSWERED';message_id='unicode-evidence';task_id='task-0005';content=(Protect-DawoudTelemetryText -Text $script:testAnswer)};[void]$script:ui.Chat.Add($reconciliationMessage)
$reconciliationPayload=@(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Chat)|ConvertTo-Json -Depth 5 -Compress
Assert-Graph ($reconciliationPayload.Contains($script:testAnswer)) 'Reconciliation payload must contain full authoritative message body'
function Invoke-DawoudGraphExecutor { param($Agent,$Prompt,$WorkId);$script:leaderPrompt=$Prompt;$script:lastExecutorResult='{"goal_status":"COMPLETE","reason":"All repaired evidence verified.","verification":"README bytes and utility verification passed.","tasks":[]}' -replace '\\','';$true }
Invoke-DawoudGoalGraph -RootGoal 'Acceptance fixture' -WorkId 'final-reconcile-test'
Assert-Graph ($script:ui.GoalStatus -eq 'COMPLETE' -and $script:ui.Result -match 'All repaired evidence verified') 'Repaired task must permit final reconciliation COMPLETE'
'AGEX SCHEDULER/MAILBOX CHECKS: COMPONENT PASS'
