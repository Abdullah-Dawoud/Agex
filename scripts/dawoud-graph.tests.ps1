$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/dawoud-common.ps1"
. "$PSScriptRoot/dawoud-ui.ps1"
. "$PSScriptRoot/dawoud-graph.ps1"
function Assert-Graph($Condition, $Message) { if (-not $Condition) { throw $Message } }
$script:ui = New-DawoudUiState -Project $PSScriptRoot -SessionId 'graph-component'
$script:cancellationSignal = @{Requested=$false}
$script:ResolvedLeader = 'Codex'
$script:calls = [System.Collections.Generic.List[string]]::new()
$script:round = 0
function Set-DawoudUiTaskState { param($TaskId,$Status,$Agent); [void](Set-DawoudUiTask -State $script:ui -TaskId $TaskId -Status $Status -Agent $Agent) }
function Invoke-DawoudGraphExecutor {
    param($Agent,$Prompt,$WorkId)
    Assert-Graph ($Prompt.Contains('IMMUTABLE SENTINEL')) 'Root goal lost'
    [void]$script:calls.Add($WorkId)
    if ($WorkId -match 'leader') {
        $script:round++
        if ($script:round -eq 1) {
            $script:lastExecutorResult='{"goal_status":"CONTINUE","reason":"Separate implementation and dependent review","tasks":[{"id":"review","title":"Review","objective":"Review output","executor":"Codex","dependencies":["build"]},{"id":"build","title":"Build","objective":"Build output","executor":"Antigravity","dependencies":[]}]}'
        } elseif ($script:round -eq 2) {
            $script:lastExecutorResult='{"goal_status":"CONTINUE","reason":"Verification found missing case","tasks":[{"id":"repair","title":"Repair","objective":"Repair missing case","executor":"Antigravity","dependencies":["review"]}]}'
        } else { $script:lastExecutorResult='{"goal_status":"COMPLETE","reason":"Goal verified","verification":"Mock verification evidence","tasks":[]}' }
    } else {
        if ($WorkId -match 'review') { Assert-Graph ($Prompt.Contains('Inspect output')) 'Agent message not delivered' }
        if ($WorkId -match 'repair') { Assert-Graph ($Prompt.Contains('Review found missing case')) 'Reverse message not delivered' }
        $script:lastExecutorResult = if ($Agent -eq 'Codex') { '{"result":"component result","messages":[{"to":"Antigravity","type":"REQUEST","content":"Review found missing case"}]}' } else { '{"result":"component result","messages":[{"to":"Codex","type":"REVIEW","content":"Inspect output"}]}' }
    }
    return $true
}
Invoke-DawoudGoalGraph -RootGoal 'IMMUTABLE SENTINEL' -WorkId 'test'
Assert-Graph ($script:ui.GoalStatus -eq 'COMPLETE') 'Verified completion missing'
Assert-Graph ($script:calls.IndexOf('test-build') -lt $script:calls.IndexOf('test-review')) 'Dependency ordering violated'
Assert-Graph ($script:calls.Contains('test-repair')) 'Follow-up not executed'
Assert-Graph ($script:ui.Chat.Count -eq 2) 'Duplicate message suppression failed'
$rejected=$false
try { ConvertFrom-DawoudPlan '{"goal_status":"COMPLETE","tasks":[]}' } catch { $rejected=$true }
Assert-Graph $rejected 'Unverified completion accepted'
$rejected=$false
try { ConvertFrom-DawoudPlan '{"goal_status":"CONTINUE","tasks":[{"id":"x","objective":"x","executor":"Codex","dependencies":["missing"]}]}' } catch { $rejected=$true }
Assert-Graph $rejected 'Unknown dependency accepted'
$many = @(1..25 | ForEach-Object { @{id="item-$_";objective='Inspect';executor='Codex';dependencies=@()} })
$plan = ConvertFrom-DawoudPlan (@{goal_status='CONTINUE';tasks=$many} | ConvertTo-Json -Depth 5)
Assert-Graph (@($plan.tasks).Count -eq $many.Count) 'Planner truncates tasks'
$root = "  entire prompt`n" + ('x' * 20000)
Assert-Graph ((Get-DawoudTaskSlices $root).Task -ceq $root) 'Compatibility helper loses prompt'
$script:ui = New-DawoudUiState -Project $PSScriptRoot -SessionId 'cancel-component'
$script:cancellationSignal.Requested=$true
$before=$script:calls.Count
Invoke-DawoudGoalGraph -RootGoal 'IMMUTABLE SENTINEL' -WorkId 'cancel'
Assert-Graph ($script:calls.Count -eq $before) 'Executor started after cancellation'
Assert-Graph ($script:ui.GoalStatus -eq 'CANCELLED') 'Cancellation lost'
'DAWOUD GRAPH: COMPONENT PASS (mock executors, not real-agent acceptance)'
