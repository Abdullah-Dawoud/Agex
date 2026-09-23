# Leader protocol and conservative scheduler. Execution remains in the existing backends.
function ConvertFrom-DawoudPlan {
    param([string]$Text, [object[]]$Existing = @())
    $json = $Text.Trim() -replace '^```(?:json)?\s*', '' -replace '\s*```$', ''
    $plan = $json | ConvertFrom-Json -ErrorAction Stop
    if ($plan.goal_status -notin @('CONTINUE', 'COMPLETE', 'BLOCKED')) { throw 'Invalid leader goal_status.' }
    $ids = @{}
    foreach ($item in $Existing) { $ids[$item.Id] = $true }
    foreach ($item in @($plan.tasks)) {
        if (-not $item.id -or $ids.ContainsKey([string]$item.id) -or $item.id -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Missing or duplicate task id.' }
        if (-not $item.objective -or $item.executor -notin @('Codex','Antigravity')) { throw 'Invalid task objective or executor.' }
        $ids[[string]$item.id] = $true
    }
    foreach ($item in @($plan.tasks)) {
        foreach ($dependency in @($item.dependencies)) {
            if (-not $ids.ContainsKey([string]$dependency) -or $dependency -eq $item.id) { throw 'Invalid task dependency.' }
        }
    }
    if ($plan.goal_status -eq 'CONTINUE') {
        $unrepaired=@($Existing | Where-Object Status -in @('FAILED','REPAIR REQUIRED'))
        foreach ($failedTask in $unrepaired) {
            if (-not @($plan.tasks | Where-Object { $failedTask.Id -in @($_.repair_for) }).Count) {
                throw "Plan omitted executable repair_for link for failed task $($failedTask.Id)."
            }
        }
    }
    if ($plan.goal_status -eq 'COMPLETE' -and (-not $plan.verification -or @($plan.tasks).Count -gt 0)) { throw 'Completion requires verification and no new tasks.' }
    $plan
}

function Invoke-DawoudGraphExecutor {
    param([string]$Agent, [string]$Prompt, [string]$WorkId)
    if ($script:cancellationSignal.Requested) { return $false }
    $script:lastExecutorResult = ''
    $route = [pscustomobject]@{ Agent = $Agent; Category = 'engineering'; Reason = 'Selected leader assignment' }
    $started=Get-Date
    try {
        if ($Agent -eq 'Antigravity') { return (Invoke-AgyTask -Prompt $Prompt -WorkId $WorkId -Route $route) }
        Invoke-CodexTask -Prompt $Prompt -WorkId $WorkId -Route $route
    } finally {
        if ($script:ui -and $script:ui.TimingEvents) {
            $kind=if($WorkId -match '-leader-|plan-repair'){ 'LEADER' } elseif($WorkId -match '-mail-|answer-'){ 'MAILBOX' } else { 'EXECUTOR' }
            $script:ui.TimingEvents.Enqueue([pscustomobject]@{Kind=$kind;WorkId=$WorkId;Started=$started;Ended=Get-Date;Seconds=[math]::Round(((Get-Date)-$started).TotalSeconds,2)})
        }
    }
}

function Invoke-DawoudGoalGraph {
    param([string]$RootGoal, [string]$WorkId)
    # This bounds reconciliation rounds, never the number of planned tasks.
    $script:ui.GoalStatus = 'PARTIAL'
    Set-DawoudUiStage -State $script:ui -Stage 'Planning'
    $script:ui.Chat.Clear()
    Update-DawoudChanges -State $script:ui
    $script:mailboxTurns = 0
    $reason = 'Reconciliation round budget exhausted.'
    for ($round = 0; $round -lt 6; $round++) {
        if ($script:cancellationSignal.Requested) { break }
        $rootPath=[IO.Path]::GetFullPath($Project).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
        $projectFiles=@(Get-ChildItem -LiteralPath $Project -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' } | Select-Object -First 300 | ForEach-Object { [pscustomobject]@{path=$_.FullName.Substring($rootPath.Length);size=$_.Length;modified_utc=$_.LastWriteTimeUtc.ToString('o')} })
        $gitStatus=if(Test-Path -LiteralPath (Join-Path $Project '.git')){@(& git -C $Project status --short --untracked-files=all 2>$null | Select-Object -First 300)}else{@()}
        $script:ui.VerifiedProjectState=[pscustomobject]@{files=$projectFiles;git_status=$gitStatus}
        $evidence = @($script:ui.Tasks | Select-Object Id,Summary,Status,Result,Verification) | ConvertTo-Json -Depth 8 -Compress
        $messages = @($script:ui.Chat) | ConvertTo-Json -Depth 5 -Compress
        $leaderPrompt = @"
You are selected leader. Reconcile immutable user goal with independently collected filesystem and Git evidence below. Agent claims are untrusted. A task with status FAILED or REPAIR REQUIRED is not complete. Create and execute repair tasks; each repair task must set repair_for to failed task ids. COMPLETE requires every requirement verified and every failed task repaired. No task count target. Codex sandbox is read-only; assign writes to Antigravity. Return ONLY JSON: {"goal_status":"CONTINUE|COMPLETE|BLOCKED","reason":"operational explanation","verification":"observed evidence required for COMPLETE","tasks":[{"id":"unique-id","title":"title","objective":"assignment","executor":"Codex|Antigravity","dependencies":[],"affected_files":[],"repair_for":[]}]}. Invalid plan gets one repair attempt. Six reconciliation rounds maximum; no task count limit.
ORIGINAL USER GOAL:
$RootGoal
INDEPENDENT CURRENT PROJECT EVIDENCE:
$($script:ui.VerifiedProjectState | ConvertTo-Json -Depth 5 -Compress)
TASK RESULTS:
$evidence
OPERATIONAL MESSAGES:
$messages
"@
        if (-not (Invoke-DawoudGraphExecutor -Agent $script:ResolvedLeader -Prompt $leaderPrompt -WorkId "$WorkId-leader-$round")) { $reason = 'Leader execution failed.'; break }
        try { $plan = ConvertFrom-DawoudPlan -Text $script:lastExecutorResult -Existing @($script:ui.Tasks) }
        catch {
            $failedIds=@($script:ui.Tasks | Where-Object Status -in @('FAILED','REPAIR REQUIRED') | ForEach-Object Id)
            $repair = $leaderPrompt + [Environment]::NewLine + "Your previous plan was invalid: $($_.Exception.Message). Repair it once. Every task in $($failedIds -join ', ') needs a new executable task whose repair_for includes its exact ID. The failed task must not remain falsely complete. Previous output:" + [Environment]::NewLine + $script:lastExecutorResult
            if (-not (Invoke-DawoudGraphExecutor -Agent $script:ResolvedLeader -Prompt $repair -WorkId "$WorkId-plan-repair-$round")) { $reason='Plan repair execution failed.'; break }
            try { $plan = ConvertFrom-DawoudPlan -Text $script:lastExecutorResult -Existing @($script:ui.Tasks) }
            catch { $reason="Invalid leader plan after repair: $($_.Exception.Message)"; break }
        }
        $reason = [string]$plan.reason
        if ($plan.goal_status -eq 'COMPLETE') {
            if (@($script:ui.Tasks | Where-Object Status -notin @('DONE','REPAIRED')).Count -gt 0) { $reason = 'Leader claimed completion with unresolved or unrepaired tasks.'; break }
            $script:ui.GoalStatus = 'COMPLETE'
            $script:ui.Result = "GOAL: COMPLETE`n$reason`nVERIFICATION: $($plan.verification)"
            return
        }
        if ($plan.goal_status -eq 'BLOCKED') { break }
        if (@($plan.tasks).Count -eq 0) { $reason = 'Leader returned no actionable work and no verified completion.'; break }
        foreach ($item in @($plan.tasks)) {
            Add-DawoudUiTask -State $script:ui -Task ([pscustomobject]@{
                Id=[string]$item.id; Summary=[string]$item.title; Task=[string]$item.objective; Agent=[string]$item.executor
                Status='QUEUED'; Dependencies=@($item.dependencies); AffectedFiles=@($item.affected_files); RepairFor=@($item.repair_for)
                Started=[datetime]::MinValue; End=[datetime]::MinValue; CreatedAt=Get-Date
                Reason='Leader assignment'; Error=''; Result=''; Verification=''; VerifiedResult=$null; Attempt=0
            })
        }
        Set-DawoudUiStage -State $script:ui -Stage 'Graph created'
        Set-DawoudUiStage -State $script:ui -Stage 'Scheduling'
        Set-DawoudUiStage -State $script:ui -Stage 'Executing'
        [void](Add-DawoudUiEvent -State $script:ui -Source 'LEADER' -Kind 'PLAN' -Message $reason -Status 'RUNNING')
        Invoke-DawoudReadyTasks -RootGoal $RootGoal -WorkId $WorkId
        Set-DawoudUiStage -State $script:ui -Stage 'Reconciling'
        Invoke-DawoudMailbox -RootGoal $RootGoal -WorkId $WorkId

    }
    if ($script:cancellationSignal.Requested) {
        foreach ($entry in @($script:ui.Tasks | Where-Object Status -in @('QUEUED','STARTING','BLOCKED'))) { $entry.Status='CANCELLED' }
        $script:ui.GoalStatus='CANCELLED'
    }
    $script:ui.Result = "GOAL: $($script:ui.GoalStatus)`n$reason"
}

function Test-DawoudPathConflict {
    param($Left, $Right)
    if (-not @($Left.AffectedFiles).Count -or -not @($Right.AffectedFiles).Count) { return $true }
    foreach ($a in $Left.AffectedFiles) {
        foreach ($b in $Right.AffectedFiles) {
            try {
                if ($a -match '[*?]' -or $b -match '[*?]') { return $true }
                $x=[IO.Path]::GetFullPath((Join-Path $Project $a)).TrimEnd('\','/').ToLowerInvariant()
                $y=[IO.Path]::GetFullPath((Join-Path $Project $b)).TrimEnd('\','/').ToLowerInvariant()
                if ($x -eq $y -or $x.StartsWith($y+'\') -or $y.StartsWith($x+'\')) { return $true }
            } catch { return $true }
        }
    }
    $false
}

function Test-DawoudTaskDependenciesSatisfied {
    param($Entry)
    foreach ($dependency in @($Entry.Dependencies)) {
        $prior=Find-DawoudUiTask -State $script:ui -TaskId ([string]$dependency)
        if (-not $prior) { return $false }
        if ($prior.Status -in @('DONE','REPAIRED')) { continue }
        # A repair is allowed to run against the failed node it repairs; making
        # that failed node a hard prerequisite otherwise deadlocks the repair.
        if ($prior.Status -eq 'REPAIR REQUIRED' -and $dependency -in @($Entry.RepairFor)) { continue }
        return $false
    }
    return $true
}

function Add-DawoudOperationalMessages {
    param($Entry)
    try {
        $reply = ($Entry.Result.Trim() -replace '^```(?:json)?\s*','' -replace '\s*```$','') | ConvertFrom-Json -ErrorAction Stop
        foreach ($message in @($reply.messages)) {
            if ($script:ui.Chat.Count -ge 48) { break }
            if ($message.to -notin @('Codex','Antigravity') -or $message.type -notin @('QUESTION','ANSWER','REQUEST','RESULT','BLOCKER','HANDOFF','REVIEW') -or -not $message.content) { continue }
            $content=Protect-DawoudTelemetryText -Text ([string]$message.content)
            if ($content.Length -gt 2000) { $content=$content.Substring(0,2000) }
            if (@($script:ui.Chat | Where-Object { $_.from -eq $Entry.Agent -and $_.to -eq $message.to -and $_.content -eq $content }).Count) { continue }
            [void]$script:ui.Chat.Add([pscustomobject]@{message_id=[guid]::NewGuid().ToString('N'); timestamp=(Get-Date).ToUniversalTime().ToString('o'); from=$Entry.Agent; to=$message.to; task_id=$Entry.Id; type=$message.type; content=$content; status='QUEUED'})
        }
    } catch { }
}

function Get-DawoudTaskVerification {
    param($Entry)
    $evidence=[System.Collections.Generic.List[object]]::new()
    $claims=[System.Collections.Generic.List[string]]::new()
    foreach ($path in @($Entry.AffectedFiles)) { if ($path) { [void]$claims.Add([string]$path) } }
    $text=[string]$Entry.Result
    $deleted=@{}
    foreach ($match in [regex]::Matches($text,'(?im)\b(?:deleted|removed)\s+(?:file\s+)?["]?([A-Za-z0-9_. -]+(?:[/\\][A-Za-z0-9_. -]+)*\.[A-Za-z0-9]{1,12})')) { $deleted[$match.Groups[1].Value.Trim()]=$true }
    foreach ($match in [regex]::Matches($text,'(?im)\b(?:created|modified|updated|deleted|removed|wrote|saved)\s+(?:file\s+)?["]?([A-Za-z0-9_. -]+(?:[/\\][A-Za-z0-9_. -]+)*\.[A-Za-z0-9]{1,12})')) {
        if (-not $claims.Contains($match.Groups[1].Value.Trim())) { [void]$claims.Add($match.Groups[1].Value.Trim()) }
    }
    foreach ($claimed in $claims) {
        try {
            $base=[IO.Path]::GetFullPath($Project).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
            $absolute=[IO.Path]::GetFullPath((Join-Path $Project $claimed))
            if (-not $absolute.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) { throw 'Claimed path escapes project.' }
            $exists=Test-Path -LiteralPath $absolute -PathType Leaf
            $shouldExist=-not $deleted.ContainsKey($claimed)
            $gitState=''
            if (Test-Path -LiteralPath (Join-Path $Project '.git')) { $gitState=(@(& git -C $Project status --short --untracked-files=all -- $claimed 2>$null) -join '; ') }
            $hash='';$size=0;$modified=''
            if ($exists) { $item=Get-Item -LiteralPath $absolute; $size=$item.Length;$modified=$item.LastWriteTimeUtc.ToString('o');$hash=(Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash }
            [void]$evidence.Add([pscustomobject]@{claimed_path=$claimed;absolute_path=$absolute;exists=$exists;expected_exists=$shouldExist;size=$size;sha256=$hash;modified_utc=$modified;git_state=$gitState})
        } catch { [void]$evidence.Add([pscustomobject]@{claimed_path=$claimed;exists=$false;error=$_.Exception.Message}) }
    }
    if ($claims.Count -and @($evidence | Where-Object { $_.exists -ne $_.expected_exists }).Count) { return [pscustomobject]@{Pass=$false;Claimed=@($claims);Evidence=@($evidence);Reason='One or more claimed file changes do not match project state.'} }
    [pscustomobject]@{Pass=$true;Claimed=@($claims);Evidence=@($evidence);Reason=if($claims.Count){'All declared and stated output files verified.'}else{'No file output claimed; executor result recorded.'}}
}

function Invoke-DawoudMailbox {
    param([string]$RootGoal,[string]$WorkId)
    foreach ($message in @($script:ui.Chat | Where-Object status -eq 'QUEUED')) {
        if ($script:cancellationSignal.Requested) { break }
        if ($script:mailboxTurns -ge 8) { $message.status='FAILED'; continue }
        $script:mailboxTurns++
        $prompt="AGEX operational message. Original goal: $RootGoal`nMessage id: $($message.message_id); from: $($message.from); task: $($message.task_id); type: $($message.type)`n$($message.content)`nInspect relevant project state if needed. Respond concisely with an explicit answer or blocker. Do not edit files in this communication turn. Do not generate further requests."
        if (-not (Invoke-DawoudGraphExecutor -Agent $message.to -Prompt $prompt -WorkId "$WorkId-mail-$($message.message_id)")) { $message.status='FAILED'; continue }
        $message.status='DELIVERED'
        if ($message.type -in @('ANSWER','RESULT','BLOCKER')) { continue }
        $answer = Protect-DawoudTelemetryText -Text $script:lastExecutorResult
        if ($answer.Length -gt 2000) { $answer=$answer.Substring(0,2000) }
        $response=[pscustomobject]@{message_id=[guid]::NewGuid().ToString('N'); timestamp=(Get-Date).ToUniversalTime().ToString('o'); from=$message.to; to=$message.from; task_id=$message.task_id; type='ANSWER'; content=$answer; status='QUEUED'}
        [void]$script:ui.Chat.Add($response)
        $message.status='ANSWERED'
        if ($script:cancellationSignal.Requested) { break }
        $ack="AGEX answer to your message $($message.message_id). Response id: $($response.message_id). Original goal: $RootGoal`nTask: $($message.task_id). From $($message.to): $answer`nAcknowledge this answer and state any remaining gap. Do not edit files or generate further messages."
        if (Invoke-DawoudGraphExecutor -Agent $response.to -Prompt $ack -WorkId "$WorkId-answer-$($response.message_id)") { $response.status='ANSWERED' } else { $response.status='FAILED' }
    }
}

function Invoke-DawoudReadyTasks {
    param([string]$RootGoal,[string]$WorkId)
    $running=[System.Collections.Generic.List[object]]::new()
    try {
        while (-not $script:cancellationSignal.Requested) {
            foreach ($job in @($running.ToArray())) {
                foreach ($key in @($job.Ui.Agents.Keys)) {
                    $agent=$job.Ui.Agents[$key]
                    $display="$($job.Entry.Id)/$key"
                    $agent.Name=$display
                    $script:ui.Agents[$display]=$agent
                    Publish-DawoudUiUpdate -State $script:ui -Kind 'AGENT' -Value $agent
                    if ($agent.PID -gt 0 -and $agent.Status -eq 'RUNNING') { Set-DawoudUiTaskState -TaskId $job.Entry.Id -Status 'RUNNING' -Agent $job.Entry.Agent }
                }
                if (-not $job.Async.IsCompleted) { continue }
                try { [void]$job.PowerShell.EndInvoke($job.Async) } catch { $job.Result.Success=$false; $job.Result.Output=$_.Exception.Message }
                if ($job.Result.Success) { foreach ($message in $job.Inbox) { $message.status='DELIVERED' } }
                $job.Entry.Result=[string]$job.Result.Output
                $job.Entry.End=Get-Date
                Set-DawoudUiTaskState -TaskId $job.Entry.Id -Status 'VERIFYING' -Agent $job.Entry.Agent
                Set-DawoudUiStage -State $script:ui -Stage 'Verifying'
                $verificationStarted=Get-Date
                $verification=Get-DawoudTaskVerification -Entry $job.Entry
                $script:ui.TimingEvents.Enqueue([pscustomobject]@{Kind='VERIFY';WorkId=$job.Entry.Id;Started=$verificationStarted;Ended=Get-Date;Seconds=[math]::Round(((Get-Date)-$verificationStarted).TotalSeconds,2)})
                $job.Entry.Verification=$verification
                $job.Entry.VerifiedResult=$verification
                $job.Entry.Status=if ($job.Result.Success -and $verification.Pass) { 'DONE' } else { 'REPAIR REQUIRED' }
                Set-DawoudUiTaskState -TaskId $job.Entry.Id -Status $job.Entry.Status -Agent $job.Entry.Agent -ErrorText $job.Entry.Error
                if ($job.Entry.Status -eq 'REPAIR REQUIRED') { $job.Entry.Error=$verification.Reason }
                foreach ($repairedId in @($job.Entry.RepairFor)) {
                    $prior=Find-DawoudUiTask -State $script:ui -TaskId ([string]$repairedId)
                    if ($prior -and $job.Entry.Status -eq 'DONE') { $prior.Status='REPAIRED';$prior.Verification="Repair verified by $($job.Entry.Id)." }
                }
                Add-DawoudOperationalMessages -Entry $job.Entry
                Update-DawoudChanges -State $script:ui
                $job.PowerShell.Dispose(); $job.Runspace.Dispose(); [void]$running.Remove($job)
            }
            $pending=@($script:ui.Tasks | Where-Object Status -eq 'QUEUED')
            if (-not $pending.Count -and -not $running.Count) { break }
            $started=$false
            foreach ($entry in $pending) {
                if ($running.Count -ge $MaxWorkers -or $script:cancellationSignal.Requested) { break }
                if (-not (Test-DawoudTaskDependenciesSatisfied -Entry $entry)) { continue }
                if ($entry.Agent -eq 'Codex' -and @($running | Where-Object { $_.Entry.Agent -eq 'Codex' }).Count) { continue }
                $conflict=$false
                foreach ($job in $running) { if (Test-DawoudPathConflict $entry $job.Entry) { $conflict=$true; break } }
                if ($conflict) { continue }
                $context=@($script:ui.Tasks | Where-Object Status -eq 'DONE' | Select-Object Id,Result) | ConvertTo-Json -Depth 5 -Compress
                $inbox=@($script:ui.Chat | Where-Object { $_.to -eq $entry.Agent -and $_.status -eq 'QUEUED' })
                $mail=$inbox | ConvertTo-Json -Depth 5 -Compress
                $prompt="MESSAGES TO YOU: $mail`nORIGINAL USER GOAL: $RootGoal`nASSIGNMENT: $($entry.Task)`nOWNED PATHS: $($entry.AffectedFiles -join ', ')`nPRIOR RESULTS: $context`nStay within assigned file ownership. Return ONLY JSON with result (actual changes, checks and blockers) and optional messages array: {`"result`":`"...`",`"messages`": [{`"to`":`"Codex|Antigravity`",`"type`":`"QUESTION|ANSWER|REQUEST|RESULT|BLOCKER|HANDOFF|REVIEW`",`"content`":`"short useful message`"}]}. AGEX will deliver messages and return answers in dedicated communication turns."
                $childUi=New-DawoudUiState -Project $Project -SessionId $SessionId
                $childUi.UiThreadId=-1; $childUi.WorkId=$WorkId
                $result=[hashtable]::Synchronized(@{Success=$false;Output='';Completed=$false})
                $rs=[RunspaceFactory]::CreateRunspace(); $rs.Open()
                $ps=[PowerShell]::Create(); $ps.Runspace=$rs
                [void]$ps.AddCommand((Join-Path $PSScriptRoot 'dawoud-primary.ps1'))
                $parameters=@{Project=$Project;CodexPath=$CodexPath;SessionId=$SessionId;ConfiguredLeader=$ConfiguredLeader;CodexShare=$CodexShare;AntigravityShare=$AntigravityShare;CodexModel=$CodexModel;CodexEffort=$CodexEffort;AntigravityModel=$AntigravityModel;AntigravityEffort=$AntigravityEffort;AgyPath=$AgyPath;ExecutorPrompt=$prompt;AssignedExecutor=$entry.Agent;ExecutorWorkId="$WorkId-$($entry.Id)";SharedUiState=$childUi;ExecutorResult=$result;CancellationSignal=$script:cancellationSignal}
                foreach ($key in $parameters.Keys) { [void]$ps.AddParameter($key,$parameters[$key]) }
                $entry.Attempt++; $entry.Started=Get-Date; Set-DawoudUiTaskState -TaskId $entry.Id -Status 'STARTING' -Agent $entry.Agent
                [System.Threading.Monitor]::Enter($script:ui.UiUpdateClock.SyncRoot)
                try { $script:ui.AssignmentCount = [int]$script:ui.AssignmentCount + 1; $assignmentCount=[int]$script:ui.AssignmentCount }
                finally { [System.Threading.Monitor]::Exit($script:ui.UiUpdateClock.SyncRoot) }
                Publish-DawoudUiUpdate -State $script:ui -Kind 'COUNT' -Value $assignmentCount
                $async=$ps.BeginInvoke()
                [void]$running.Add([pscustomobject]@{Entry=$entry;Inbox=$inbox;Ui=$childUi;Result=$result;Runspace=$rs;PowerShell=$ps;Async=$async})
                $started=$true
            }
            if (-not $running.Count -and -not $started -and $pending.Count) { foreach ($entry in $pending) { $entry.Status='BLOCKED';$entry.Error='Unresolved dependency or cycle.' }; break }
            Refresh-DawoudUi
            Start-Sleep -Milliseconds 100
        }
    } finally {
        foreach ($job in $running) {
            foreach ($agent in @($job.Ui.Agents.Values | Where-Object Status -notin @('DONE','FAILED','CANCELLED','IDLE'))) {
                $rootPid=if ($agent.DispatchPID -gt 0) { $agent.DispatchPID } else { $agent.PID }
                if ($rootPid -gt 0) { & taskkill.exe /PID ([string]$rootPid) /T /F 2>$null | Out-Null }
            }
            try { $job.PowerShell.Stop() } catch { }
            $job.PowerShell.Dispose();$job.Runspace.Dispose()
            $job.Entry.Status=if ($script:cancellationSignal.Requested) { 'CANCELLED' } else { 'FAILED' }
        }
    }
}
