# Leader protocol and conservative scheduler. Execution remains in the existing backends.
function Get-DawoudLeaderPlanAudit {
    param([Parameter(Mandatory)][string]$Text)
    $json = $Text.Trim() -replace '^```(?:json)?\s*', '' -replace '\s*```$', ''
    $audit = [pscustomobject]@{ At=Get-Date; RawTaskCount=0; RawTaskIds=@(); DuplicateIds=@(); MissingIds=@(); InvalidDependencies=@(); SelfDependencies=@(); UnknownDependencyTargets=@(); Tasks=@(); ParseError='' }
    try {
        $plan = $json | ConvertFrom-Json -ErrorAction Stop; $tasks = @($plan.tasks); $audit.RawTaskCount = $tasks.Count
        $audit.Tasks = @($tasks | ForEach-Object { [pscustomobject]@{ Id=[string]$_.id; Title=[string]$_.title; Objective=[string]$_.objective; Executor=[string]$_.executor; Dependencies=@($_.dependencies); Parent=[string]$_.parent; VerificationIntent=[string]$_.verification } })
        $audit.RawTaskIds = @($audit.Tasks | ForEach-Object Id)
        $audit.MissingIds = @($audit.RawTaskIds | Where-Object { [string]::IsNullOrWhiteSpace($_) })
        $audit.DuplicateIds = @($audit.RawTaskIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    } catch { $audit.ParseError = [string]$_.Exception.Message }
    $audit
}

function Save-DawoudLeaderPlanAudit {
    param($State, [Parameter(Mandatory)]$Audit)
    if (-not $State) { return }
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try { if ($State.LeaderPlanAudit.Count -ge 12) { $State.LeaderPlanAudit.RemoveAt(0) }; [void]$State.LeaderPlanAudit.Add($Audit) }
    finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
}

function Get-DawoudLeaderPlanAuditSummary {
    param($State)
    $audit = @(Get-DawoudUiCollectionSnapshot -State $State -Collection LeaderPlanAudit | Select-Object -Last 1)
    if (-not $audit.Count) { return 'Raw leader plan audit unavailable.' }
    $item = $audit[0]
    $ids = @($item.RawTaskIds | ForEach-Object { if ([string]::IsNullOrWhiteSpace($_)) { '<missing>' } else { [string]$_ } } | Select-Object -First 12) -join ', '
    $duplicates = @($item.DuplicateIds | Select-Object -First 12) -join ', '
    $missing = @($item.MissingIds).Count
    "Raw tasks $($item.RawTaskCount); IDs: $ids; duplicates: $duplicates; missing IDs: $missing."
}

function New-DawoudCanonicalTaskId {
    param([Parameter(Mandatory)]$UsedIds, [Parameter(Mandatory)][ref]$NextNumber)
    do { $candidate = 'task-{0:d4}' -f $NextNumber.Value; $NextNumber.Value++ } while ($UsedIds.Contains($candidate))
    [void]$UsedIds.Add($candidate); $candidate
}

function ConvertTo-DawoudReferenceAlias {
    param([string]$Reference)
    if ([string]::IsNullOrWhiteSpace($Reference)) { return '' }
    (($Reference.Trim().ToLowerInvariant() -replace '[\s_-]+','-') -replace '^-|-$','')
}

function Add-DawoudPlanReference {
    param($References,$Ambiguous,[string]$Reference,[string]$CanonicalId)
    if ([string]::IsNullOrWhiteSpace($Reference)) { return }
    if ($References.ContainsKey($Reference) -and $References[$Reference] -ne $CanonicalId) { [void]$Ambiguous.Add($Reference); return }
    $References[$Reference]=$CanonicalId
}

function Get-DawoudPlanCycle {
    param([Parameter(Mandatory)][object[]]$Tasks)
    $byId=@{}; foreach ($task in $Tasks) { $byId[[string]$task.id]=$task }
    $marks=@{}; $path=[System.Collections.Generic.List[string]]::new(); $visit=$null
    $visit={ param([string]$Id)
        $marks[$Id]=1; [void]$path.Add($Id)
        foreach($dependency in @($byId[$Id].dependencies)) {
            $target=[string]$dependency
            if($marks[$target] -eq 1) { $start=$path.IndexOf($target); return ((@($path.GetRange($start,$path.Count-$start))+$target) -join ' -> ') }
            if($marks[$target] -ne 2) { $cycle=& $visit $target; if($cycle){return $cycle} }
        }
        $path.RemoveAt($path.Count-1);$marks[$Id]=2;''
    }
    foreach($task in $Tasks) { if($marks[[string]$task.id] -ne 2) { $cycle=& $visit ([string]$task.id);if($cycle){return $cycle} } };''
}

function ConvertFrom-DawoudPlan {
    param([string]$Text, [object[]]$Existing = @(), $AuditState = $null)
    $audit=Get-DawoudLeaderPlanAudit -Text $Text; Save-DawoudLeaderPlanAudit -State $AuditState -Audit $audit
    if($audit.ParseError){throw "Invalid leader JSON: $($audit.ParseError)"}
    $json=$Text.Trim() -replace '^```(?:json)?\s*','' -replace '\s*```$','';$plan=$json|ConvertFrom-Json -ErrorAction Stop
    if($plan.goal_status -notin @('CONTINUE','COMPLETE','BLOCKED')){throw 'Invalid leader goal_status.'}
    $usedIds=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$references=[hashtable]::new([StringComparer]::OrdinalIgnoreCase);$ambiguous=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$normalizedReferences=[hashtable]::new([StringComparer]::OrdinalIgnoreCase);$normalizedAmbiguous=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$nextNumber=1
    foreach($item in $Existing){$id=[string]$item.Id;if([string]::IsNullOrWhiteSpace($id)-or -not $usedIds.Add($id)){throw 'Existing graph has missing or duplicate canonical task id.'};foreach($reference in @($id,[string]$item.OriginalId,[string]$item.Summary,[string]$item.Task,@($item.Aliases))){Add-DawoudPlanReference $references $ambiguous $reference $id;Add-DawoudPlanReference $normalizedReferences $normalizedAmbiguous (ConvertTo-DawoudReferenceAlias $reference) $id};if($id -match '^task-(\d+)$'){$nextNumber=[math]::Max($nextNumber,([int]$Matches[1]+1))}}
    $canonical=[System.Collections.Generic.List[object]]::new()
    foreach($item in @($plan.tasks)) {
        if(-not $item.objective -or $item.executor -notin @('Codex','Antigravity')){throw 'Invalid task objective or executor.'}
        $canonicalId=New-DawoudCanonicalTaskId -UsedIds $usedIds -NextNumber ([ref]$nextNumber)
        $aliases=@([string]$item.id,[string]$item.title,[string]$item.objective,[string]$item.parent,[string]$item.reference,[string]$item.semantic_label | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
        $normalized=[pscustomobject]@{id=$canonicalId;original_id=[string]$item.id;title=[string]$item.title;objective=[string]$item.objective;aliases=$aliases;executor=[string]$item.executor;dependencies=@();affected_files=@($item.affected_files);repair_for=@()};[void]$canonical.Add($normalized)
        foreach($reference in @($canonicalId)+$aliases){Add-DawoudPlanReference $references $ambiguous $reference $canonicalId;Add-DawoudPlanReference $normalizedReferences $normalizedAmbiguous (ConvertTo-DawoudReferenceAlias $reference) $canonicalId}
    }
    for($index=0;$index -lt $canonical.Count;$index++) {
        $source=@($plan.tasks)[$index];$task=$canonical[$index];$dependencies=[System.Collections.Generic.List[string]]::new();$seen=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $rawDependencies=if($null -eq $source.dependencies){@()}else{@($source.dependencies)}
        foreach($rawDependency in $rawDependencies){$reference=[string]$rawDependency;if([string]::IsNullOrWhiteSpace($reference)){$audit.InvalidDependencies += "$($task.id): empty dependency";throw "Invalid dependency on $($task.id): empty reference."};$normalizedReference=ConvertTo-DawoudReferenceAlias $reference;if($ambiguous.Contains($reference)-or $normalizedAmbiguous.Contains($normalizedReference)){$audit.InvalidDependencies += "$($task.id): ambiguous $reference";throw "Ambiguous dependency '$reference' on $($task.id)."};if($references.ContainsKey($reference)){$dependency=[string]$references[$reference]}elseif($normalizedReferences.ContainsKey($normalizedReference)){$dependency=[string]$normalizedReferences[$normalizedReference]}else{$audit.UnknownDependencyTargets += "$($task.id): $reference";throw "Unknown dependency '$reference' on $($task.id)."};if($dependency -eq $task.id){$audit.SelfDependencies += $task.id;throw "Self dependency on $($task.id)."};if(-not $seen.Add($dependency)){$audit.InvalidDependencies += "$($task.id): duplicate $dependency";throw "Duplicate dependency '$reference' on $($task.id)."};[void]$dependencies.Add($dependency)}
        $task.dependencies=$dependencies.ToArray();$repairs=[System.Collections.Generic.List[string]]::new()
        $rawRepairs=if($null -eq $source.repair_for){@()}else{@($source.repair_for)}
        foreach($rawRepair in $rawRepairs){$reference=[string]$rawRepair;$normalizedReference=ConvertTo-DawoudReferenceAlias $reference;if($ambiguous.Contains($reference)-or $normalizedAmbiguous.Contains($normalizedReference)){throw "AMBIGUOUS TARGET '$reference' for repair target on $($task.id)."};if($references.ContainsKey($reference)){$repairTarget=[string]$references[$reference]}elseif($normalizedReferences.ContainsKey($normalizedReference)){$repairTarget=[string]$normalizedReferences[$normalizedReference]}else{throw "UNKNOWN TARGET '$reference' for repair target on $($task.id)."};[void]$repairs.Add($repairTarget)};$task.repair_for=@($repairs|Select-Object -Unique)
    }
    $cycle=Get-DawoudPlanCycle -Tasks @($Existing+$canonical.ToArray());if($cycle){$audit.InvalidDependencies += "cycle: $cycle";throw "Dependency cycle: $cycle"}
    $plan.tasks=$canonical.ToArray()
    if($plan.goal_status -eq 'CONTINUE'){foreach($failedTask in @($Existing|Where-Object Status -in @('FAILED','REPAIR REQUIRED'))){if(-not @($plan.tasks|Where-Object{$failedTask.Id -in @($_.repair_for)}).Count){throw "Plan omitted executable repair_for link for failed task $($failedTask.Id)."}}}
    if($plan.goal_status -eq 'COMPLETE' -and (-not $plan.verification -or @($plan.tasks).Count -gt 0)){throw 'Completion requires verification and no new tasks.'};$plan
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

function New-DawoudExecutorPrompt {
    param(
        [Parameter(Mandatory)][string]$RootGoal,
        [Parameter(Mandatory)]$Entry,
        [Parameter(Mandatory)][string]$Workspace,
        [string]$Mail,
        [string]$Context
    )
    $ownedPaths = @($Entry.AffectedFiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', '
    @"
AUTHORITATIVE EXECUTOR WORKSPACE: $Workspace
Use only this exact workspace for every file read, write, and command. Never use AGY scratch or a default workspace. For each owned relative path, use this workspace as its base. Before final response, inspect each claimed artifact under this workspace and report only observed results.
MESSAGES TO YOU: $Mail
ORIGINAL USER GOAL: $RootGoal
ASSIGNMENT: $($Entry.Task)
OWNED PATHS: $ownedPaths
PRIOR RESULTS: $Context
Stay within assigned file ownership. Return ONLY JSON with result (actual changes, checks and blockers) and optional messages array: {`"result`":`"...`",`"messages`": [{`"to`":`"Codex|Antigravity`",`"type`":`"QUESTION|ANSWER|REQUEST|RESULT|BLOCKER|HANDOFF|REVIEW`",`"content`":`"short useful message`"}]}. AGEX will deliver messages and return answers in dedicated communication turns.
"@
}

function Invoke-DawoudGoalGraph {
    param([string]$RootGoal, [string]$WorkId)
    # This bounds reconciliation rounds, never the number of planned tasks.
    $script:ui.GoalStatus = 'PARTIAL'
    Set-DawoudUiStage -State $script:ui -Stage 'Planning'
    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
    try { $script:ui.Chat.Clear() } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
    Update-DawoudChanges -State $script:ui
    $script:mailboxTurns = 0
    $reason = 'Reconciliation round budget exhausted.'
    for ($round = 0; $round -lt 6; $round++) {
        if ($script:cancellationSignal.Requested) { break }
        $rootPath=[IO.Path]::GetFullPath($Project).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
        $projectFiles=@(Get-ChildItem -LiteralPath $Project -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' } | Select-Object -First 300 | ForEach-Object { [pscustomobject]@{path=$_.FullName.Substring($rootPath.Length);size=$_.Length;modified_utc=$_.LastWriteTimeUtc.ToString('o')} })
        $gitStatus=if(Test-Path -LiteralPath (Join-Path $Project '.git')){@(& git -C $Project status --short --untracked-files=all 2>$null | Select-Object -First 300)}else{@()}
        $script:ui.VerifiedProjectState=[pscustomobject]@{files=$projectFiles;git_status=$gitStatus}
        $evidence = @(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Tasks | Select-Object Id,Summary,Status,Result,Verification) | ConvertTo-Json -Depth 8 -Compress
        $messages = @(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Chat) | ConvertTo-Json -Depth 5 -Compress
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
        try { $plan = ConvertFrom-DawoudPlan -Text $script:lastExecutorResult -Existing @(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Tasks) -AuditState $script:ui }
        catch {
            $failedIds=@(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -in @('FAILED','REPAIR REQUIRED') | ForEach-Object Id)
            $repair = $leaderPrompt + [Environment]::NewLine + "Your previous plan was invalid: $($_.Exception.Message). Repair it once. Every task in $($failedIds -join ', ') needs a new executable task whose repair_for includes its exact ID. The failed task must not remain falsely complete. Previous output:" + [Environment]::NewLine + $script:lastExecutorResult
            if (-not (Invoke-DawoudGraphExecutor -Agent $script:ResolvedLeader -Prompt $repair -WorkId "$WorkId-plan-repair-$round")) { $reason='Plan repair execution failed.'; break }
            try { $plan = ConvertFrom-DawoudPlan -Text $script:lastExecutorResult -Existing @(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Tasks) -AuditState $script:ui }
            catch { $reason="Invalid leader plan after repair: $($_.Exception.Message)`n$(Get-DawoudLeaderPlanAuditSummary -State $script:ui)"; break }
        }
        $reason = [string]$plan.reason
        if ($plan.goal_status -eq 'COMPLETE') {
            if (@(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -notin @('DONE','REPAIRED')).Count -gt 0) { $reason = 'Leader claimed completion with unresolved or unrepaired tasks.'; break }
            $script:ui.GoalStatus = 'COMPLETE'
            $script:ui.Result = "GOAL: COMPLETE`n$reason`nVERIFICATION: $($plan.verification)"
            return
        }
        if ($plan.goal_status -eq 'BLOCKED') { break }
        if (@($plan.tasks).Count -eq 0) { $reason = 'Leader returned no actionable work and no verified completion.'; break }
        foreach ($item in @($plan.tasks)) {
            Add-DawoudUiTask -State $script:ui -Task ([pscustomobject]@{
                Id=[string]$item.id; OriginalId=[string]$item.original_id; Aliases=@($item.aliases); Summary=[string]$item.title; Task=[string]$item.objective; Agent=[string]$item.executor
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
        foreach ($entry in @(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -in @('QUEUED','STARTING','BLOCKED'))) {
            [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
            try { $entry.Status='CANCELLED' } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
        }
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
            if (@(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Chat).Count -ge 48) { break }
            if ($message.to -notin @('Codex','Antigravity') -or $message.type -notin @('QUESTION','ANSWER','REQUEST','RESULT','BLOCKER','HANDOFF','REVIEW') -or -not $message.content) { continue }
            $content=Protect-DawoudTelemetryText -Text ([string]$message.content)
            if ($content.Length -gt 2000) { $content=$content.Substring(0,2000) }
            if (@(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Chat | Where-Object { $_.from -eq $Entry.Agent -and $_.to -eq $message.to -and $_.content -eq $content }).Count) { continue }
            $chatMessage=[pscustomobject]@{message_id=[guid]::NewGuid().ToString('N'); timestamp=(Get-Date).ToUniversalTime().ToString('o'); from=$Entry.Agent; to=$message.to; task_id=$Entry.Id; type=$message.type; content=$content; status='QUEUED'}
            [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
            try { [void]$script:ui.Chat.Add($chatMessage) } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
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
    foreach ($message in @(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Chat | Where-Object status -eq 'QUEUED')) {
        if ($script:cancellationSignal.Requested) { break }
        if ($script:mailboxTurns -ge 8) { [System.Threading.Monitor]::Enter($script:ui.CollectionSync); try { $message.status='FAILED' } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }; continue }
        $script:mailboxTurns++
        $prompt="AGEX operational message. Original goal: $RootGoal`nMessage id: $($message.message_id); from: $($message.from); task: $($message.task_id); type: $($message.type)`n$($message.content)`nInspect relevant project state if needed. Respond concisely with an explicit answer or blocker. Do not edit files in this communication turn. Do not generate further requests."
        if (-not (Invoke-DawoudGraphExecutor -Agent $message.to -Prompt $prompt -WorkId "$WorkId-mail-$($message.message_id)")) { [System.Threading.Monitor]::Enter($script:ui.CollectionSync); try { $message.status='FAILED' } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }; continue }
        [System.Threading.Monitor]::Enter($script:ui.CollectionSync); try { $message.status='DELIVERED' } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
        if ($message.type -in @('ANSWER','RESULT','BLOCKER')) { continue }
        $answer = Protect-DawoudTelemetryText -Text $script:lastExecutorResult
        if ($answer.Length -gt 2000) { $answer=$answer.Substring(0,2000) }
        $response=[pscustomobject]@{message_id=[guid]::NewGuid().ToString('N'); timestamp=(Get-Date).ToUniversalTime().ToString('o'); from=$message.to; to=$message.from; task_id=$message.task_id; type='ANSWER'; content=$answer; status='QUEUED'}
        [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
        try { [void]$script:ui.Chat.Add($response); $message.status='ANSWERED' } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
        if ($script:cancellationSignal.Requested) { break }
        $ack="AGEX answer to your message $($message.message_id). Response id: $($response.message_id). Original goal: $RootGoal`nTask: $($message.task_id). From $($message.to): $answer`nAcknowledge this answer and state any remaining gap. Do not edit files or generate further messages."
        $responseStatus=if (Invoke-DawoudGraphExecutor -Agent $response.to -Prompt $ack -WorkId "$WorkId-answer-$($response.message_id)") { 'ANSWERED' } else { 'FAILED' }
        [System.Threading.Monitor]::Enter($script:ui.CollectionSync); try { $response.status=$responseStatus } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
    }
}

function Invoke-DawoudReadyTasks {
    param([string]$RootGoal,[string]$WorkId)
    $running=[System.Collections.Generic.List[object]]::new()
    try {
        while (-not $script:cancellationSignal.Requested) {
            foreach ($job in @($running.ToArray())) {
                $workerSnapshot=New-DawoudUiRenderSnapshot -State $job.Ui
                foreach ($agent in @($workerSnapshot.Agents.Values)) {
                    $key=$agent.Name
                    $display="$($job.Entry.Id)/$key"
                    $agent.Name=$display
                    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                    try { $script:ui.Agents[$display]=$agent } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                    Publish-DawoudUiUpdate -State $script:ui -Kind 'AGENT' -Value $agent
                    if ($agent.PID -gt 0 -and $agent.Status -eq 'RUNNING') { Set-DawoudUiTaskState -TaskId $job.Entry.Id -Status 'RUNNING' -Agent $job.Entry.Agent }
                }
                if (-not $job.Async.IsCompleted) { continue }
                try { [void]$job.PowerShell.EndInvoke($job.Async) } catch { $job.Result.Success=$false; $job.Result.Output=$_.Exception.Message }
                if ($job.Result.Success) {
                    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                    try { foreach ($message in $job.Inbox) { $message.status='DELIVERED' } }
                    finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                }
                [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                try { $job.Entry.Result=[string]$job.Result.Output; $job.Entry.End=Get-Date }
                finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                Set-DawoudUiTaskState -TaskId $job.Entry.Id -Status 'VERIFYING' -Agent $job.Entry.Agent
                Set-DawoudUiStage -State $script:ui -Stage 'Verifying'
                $verificationStarted=Get-Date
                $verification=Get-DawoudTaskVerification -Entry $job.Entry
                $script:ui.TimingEvents.Enqueue([pscustomobject]@{Kind='VERIFY';WorkId=$job.Entry.Id;Started=$verificationStarted;Ended=Get-Date;Seconds=[math]::Round(((Get-Date)-$verificationStarted).TotalSeconds,2)})
                [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                try {
                    $job.Entry.Verification=$verification
                    $job.Entry.VerifiedResult=$verification
                    $job.Entry.Status=if ($job.Result.Success -and $verification.Pass) { 'DONE' } else { 'REPAIR REQUIRED' }
                    if ($job.Entry.Status -eq 'REPAIR REQUIRED') { $job.Entry.Error=$verification.Reason }
                } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                Set-DawoudUiTaskState -TaskId $job.Entry.Id -Status $job.Entry.Status -Agent $job.Entry.Agent -ErrorText $job.Entry.Error
                foreach ($repairedId in @($job.Entry.RepairFor)) {
                    $prior=Find-DawoudUiTask -State $script:ui -TaskId ([string]$repairedId)
                    if ($prior -and $job.Entry.Status -eq 'DONE') {
                        [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                        try { $prior.Status='REPAIRED';$prior.Verification="Repair verified by $($job.Entry.Id)." }
                        finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                    }
                }
                Add-DawoudOperationalMessages -Entry $job.Entry
                Update-DawoudChanges -State $script:ui
                $job.PowerShell.Dispose(); $job.Runspace.Dispose(); [void]$running.Remove($job)
            }
            $pending=@(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -eq 'QUEUED')
            if (-not $pending.Count -and -not $running.Count) { break }
            $started=$false
            foreach ($entry in $pending) {
                if ($running.Count -ge $MaxWorkers -or $script:cancellationSignal.Requested) { break }
                if (-not (Test-DawoudTaskDependenciesSatisfied -Entry $entry)) { continue }
                if ($entry.Agent -eq 'Codex' -and @($running | Where-Object { $_.Entry.Agent -eq 'Codex' }).Count) { continue }
                $conflict=$false
                foreach ($job in $running) { if (Test-DawoudPathConflict $entry $job.Entry) { $conflict=$true; break } }
                if ($conflict) { continue }
                $context=@(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -eq 'DONE' | Select-Object Id,Result) | ConvertTo-Json -Depth 5 -Compress
                $inbox=@(Get-DawoudUiCollectionSnapshot -State $script:ui -Collection Chat | Where-Object { $_.to -eq $entry.Agent -and $_.status -eq 'QUEUED' })
                $mail=$inbox | ConvertTo-Json -Depth 5 -Compress
                $prompt=New-DawoudExecutorPrompt -RootGoal $RootGoal -Entry $entry -Workspace $Project -Mail $mail -Context $context
                $childUi=New-DawoudUiState -Project $Project -SessionId $SessionId
                $childUi.UiThreadId=-1; $childUi.WorkId=$WorkId
                $result=[hashtable]::Synchronized(@{Success=$false;Output='';Completed=$false})
                $rs=[RunspaceFactory]::CreateRunspace(); $rs.Open()
                $ps=[PowerShell]::Create(); $ps.Runspace=$rs
                [void]$ps.AddCommand((Join-Path $PSScriptRoot 'dawoud-primary.ps1'))
                $parameters=@{Project=$Project;CodexPath=$CodexPath;SessionId=$SessionId;ConfiguredLeader=$ConfiguredLeader;CodexShare=$CodexShare;AntigravityShare=$AntigravityShare;CodexModel=$CodexModel;CodexEffort=$CodexEffort;AntigravityModel=$AntigravityModel;AntigravityEffort=$AntigravityEffort;AgyPath=$AgyPath;ExecutorPrompt=$prompt;AssignedExecutor=$entry.Agent;ExecutorWorkId="$WorkId-$($entry.Id)";SharedUiState=$childUi;ExecutorResult=$result;CancellationSignal=$script:cancellationSignal}
                foreach ($key in $parameters.Keys) { [void]$ps.AddParameter($key,$parameters[$key]) }
                [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                try { $entry.Attempt++; $entry.Started=Get-Date } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                Set-DawoudUiTaskState -TaskId $entry.Id -Status 'STARTING' -Agent $entry.Agent
                [System.Threading.Monitor]::Enter($script:ui.UiUpdateClock.SyncRoot)
                try { $script:ui.AssignmentCount = [int]$script:ui.AssignmentCount + 1; $assignmentCount=[int]$script:ui.AssignmentCount }
                finally { [System.Threading.Monitor]::Exit($script:ui.UiUpdateClock.SyncRoot) }
                Publish-DawoudUiUpdate -State $script:ui -Kind 'COUNT' -Value $assignmentCount
                $async=$ps.BeginInvoke()
                [void]$running.Add([pscustomobject]@{Entry=$entry;Inbox=$inbox;Ui=$childUi;Result=$result;Runspace=$rs;PowerShell=$ps;Async=$async})
                $started=$true
            }
            if (-not $running.Count -and -not $started -and $pending.Count) {
                foreach ($entry in $pending) {
                    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                    try { $entry.Status='BLOCKED';$entry.Error='Unresolved dependency or cycle.' }
                    finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                }
                break
            }
            Refresh-DawoudUi
            Start-Sleep -Milliseconds 100
        }
    } finally {
        foreach ($job in $running) {
            foreach ($agent in @(Get-DawoudUiCollectionSnapshot -State $job.Ui -Collection Agents | Where-Object Status -notin @('DONE','FAILED','CANCELLED','IDLE'))) {
                $rootPid=if ($agent.DispatchPID -gt 0) { $agent.DispatchPID } else { $agent.PID }
                if ($rootPid -gt 0) { & taskkill.exe /PID ([string]$rootPid) /T /F 2>$null | Out-Null }
            }
            try { $job.PowerShell.Stop() } catch { }
            $job.PowerShell.Dispose();$job.Runspace.Dispose()
            [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
            try { $job.Entry.Status=if ($script:cancellationSignal.Requested) { 'CANCELLED' } else { 'FAILED' } }
            finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
        }
    }
}
