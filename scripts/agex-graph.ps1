# Leader protocol and conservative scheduler. Execution remains in the existing backends.
. (Join-Path $PSScriptRoot 'agex-process.ps1')
. (Join-Path $PSScriptRoot 'agex-adapters.ps1')
. (Join-Path $PSScriptRoot 'agex-session.ps1')
function Get-AgexLeaderPlanAudit {
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

function Save-AgexLeaderPlanAudit {
    param($State, [Parameter(Mandatory)]$Audit)
    if (-not $State) { return }
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try { if ($State.LeaderPlanAudit.Count -ge 12) { $State.LeaderPlanAudit.RemoveAt(0) }; [void]$State.LeaderPlanAudit.Add($Audit) }
    finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
}

function Get-AgexLeaderPlanAuditSummary {
    param($State)
    $audit = @(Get-AgexUiCollectionSnapshot -State $State -Collection LeaderPlanAudit | Select-Object -Last 1)
    if (-not $audit.Count) { return 'Raw leader plan audit unavailable.' }
    $item = $audit[0]
    $ids = @($item.RawTaskIds | ForEach-Object { if ([string]::IsNullOrWhiteSpace($_)) { '<missing>' } else { [string]$_ } } | Select-Object -First 12) -join ', '
    $duplicates = @($item.DuplicateIds | Select-Object -First 12) -join ', '
    $missing = @($item.MissingIds).Count
    "Raw tasks $($item.RawTaskCount); IDs: $ids; duplicates: $duplicates; missing IDs: $missing."
}

function New-AgexCanonicalTaskId {
    param([Parameter(Mandatory)]$UsedIds, [Parameter(Mandatory)][ref]$NextNumber)
    do { $candidate = 'task-{0:d4}' -f $NextNumber.Value; $NextNumber.Value++ } while ($UsedIds.Contains($candidate))
    [void]$UsedIds.Add($candidate); $candidate
}

function ConvertTo-AgexReferenceAlias {
    param([string]$Reference)
    if ([string]::IsNullOrWhiteSpace($Reference)) { return '' }
    (($Reference.Trim().ToLowerInvariant() -replace '[\s_-]+','-') -replace '^-|-$','')
}

function Add-AgexPlanReference {
    param($References,$Ambiguous,[string]$Reference,[string]$CanonicalId)
    if ([string]::IsNullOrWhiteSpace($Reference)) { return }
    if ($References.ContainsKey($Reference) -and $References[$Reference] -ne $CanonicalId) { [void]$Ambiguous.Add($Reference); return }
    $References[$Reference]=$CanonicalId
}

function Get-AgexPlanCycle {
    # A plan may legitimately contain no tasks (e.g. COMPLETE on a question).
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tasks)
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

function ConvertFrom-AgexPlan {
    param([string]$Text, [object[]]$Existing = @(), $AuditState = $null)
    $audit=Get-AgexLeaderPlanAudit -Text $Text; Save-AgexLeaderPlanAudit -State $AuditState -Audit $audit
    if($audit.ParseError){throw "Invalid leader JSON: $($audit.ParseError)"}
    $json=$Text.Trim() -replace '^```(?:json)?\s*','' -replace '\s*```$','';$plan=$json|ConvertFrom-Json -ErrorAction Stop
    if($plan.goal_status -notin @('CONTINUE','COMPLETE','BLOCKED')){throw 'Invalid leader goal_status.'}
    $usedIds=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$references=[hashtable]::new([StringComparer]::OrdinalIgnoreCase);$ambiguous=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$normalizedReferences=[hashtable]::new([StringComparer]::OrdinalIgnoreCase);$normalizedAmbiguous=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$nextNumber=1
    foreach($item in $Existing){$id=[string]$item.Id;if([string]::IsNullOrWhiteSpace($id)-or -not $usedIds.Add($id)){throw 'Existing graph has missing or duplicate canonical task id.'};foreach($reference in @($id,[string]$item.OriginalId,[string]$item.Summary,[string]$item.Task,@($item.Aliases))){Add-AgexPlanReference $references $ambiguous $reference $id;Add-AgexPlanReference $normalizedReferences $normalizedAmbiguous (ConvertTo-AgexReferenceAlias $reference) $id};if($id -match '^task-(\d+)$'){$nextNumber=[math]::Max($nextNumber,([int]$Matches[1]+1))}}
    $canonical=[System.Collections.Generic.List[object]]::new()
    foreach($item in @($plan.tasks)) {
        if(-not $item.objective -or $item.executor -notin @('Codex','Antigravity')){throw 'Invalid task objective or executor.'}
        $canonicalId=New-AgexCanonicalTaskId -UsedIds $usedIds -NextNumber ([ref]$nextNumber)
        $aliases=@([string]$item.id,[string]$item.title,[string]$item.objective,[string]$item.parent,[string]$item.reference,[string]$item.semantic_label | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
        $normalized=[pscustomobject]@{id=$canonicalId;original_id=[string]$item.id;title=[string]$item.title;objective=[string]$item.objective;aliases=$aliases;executor=[string]$item.executor;dependencies=@();affected_files=@($item.affected_files);repair_for=@()};[void]$canonical.Add($normalized)
        foreach($reference in @($canonicalId)+$aliases){Add-AgexPlanReference $references $ambiguous $reference $canonicalId;Add-AgexPlanReference $normalizedReferences $normalizedAmbiguous (ConvertTo-AgexReferenceAlias $reference) $canonicalId}
    }
    for($index=0;$index -lt $canonical.Count;$index++) {
        $source=@($plan.tasks)[$index];$task=$canonical[$index];$dependencies=[System.Collections.Generic.List[string]]::new();$seen=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $rawDependencies=if($null -eq $source.dependencies){@()}else{@($source.dependencies)}
        foreach($rawDependency in $rawDependencies){$reference=[string]$rawDependency;if([string]::IsNullOrWhiteSpace($reference)){$audit.InvalidDependencies += "$($task.id): empty dependency";throw "Invalid dependency on $($task.id): empty reference."};$normalizedReference=ConvertTo-AgexReferenceAlias $reference;if($ambiguous.Contains($reference)-or $normalizedAmbiguous.Contains($normalizedReference)){$audit.InvalidDependencies += "$($task.id): ambiguous $reference";throw "Ambiguous dependency '$reference' on $($task.id)."};if($references.ContainsKey($reference)){$dependency=[string]$references[$reference]}elseif($normalizedReferences.ContainsKey($normalizedReference)){$dependency=[string]$normalizedReferences[$normalizedReference]}else{$audit.UnknownDependencyTargets += "$($task.id): $reference";throw "Unknown dependency '$reference' on $($task.id)."};if($dependency -eq $task.id){$audit.SelfDependencies += $task.id;throw "Self dependency on $($task.id)."};if(-not $seen.Add($dependency)){$audit.InvalidDependencies += "$($task.id): duplicate $dependency";throw "Duplicate dependency '$reference' on $($task.id)."};[void]$dependencies.Add($dependency)}
        $task.dependencies=$dependencies.ToArray();$repairs=[System.Collections.Generic.List[string]]::new()
        $rawRepairs=if($null -eq $source.repair_for){@()}else{@($source.repair_for)}
        foreach($rawRepair in $rawRepairs){$reference=[string]$rawRepair;$normalizedReference=ConvertTo-AgexReferenceAlias $reference;if($ambiguous.Contains($reference)-or $normalizedAmbiguous.Contains($normalizedReference)){throw "AMBIGUOUS TARGET '$reference' for repair target on $($task.id)."};if($references.ContainsKey($reference)){$repairTarget=[string]$references[$reference]}elseif($normalizedReferences.ContainsKey($normalizedReference)){$repairTarget=[string]$normalizedReferences[$normalizedReference]}else{throw "UNKNOWN TARGET '$reference' for repair target on $($task.id)."};[void]$repairs.Add($repairTarget)};$task.repair_for=@($repairs|Select-Object -Unique)
    }
    $cycle=Get-AgexPlanCycle -Tasks @($Existing+$canonical.ToArray());if($cycle){$audit.InvalidDependencies += "cycle: $cycle";throw "Dependency cycle: $cycle"}
    $plan.tasks=$canonical.ToArray()
    if($plan.goal_status -eq 'CONTINUE'){foreach($failedTask in @($Existing|Where-Object Status -in @('FAILED','REPAIR REQUIRED'))){if(-not @($plan.tasks|Where-Object{$failedTask.Id -in @($_.repair_for)}).Count){throw "Plan omitted executable repair_for link for failed task $($failedTask.Id)."}}}
    if($plan.goal_status -eq 'COMPLETE' -and (-not $plan.verification -or @($plan.tasks).Count -gt 0)){throw 'Completion requires verification and no new tasks.'};$plan
}

function Invoke-AgexGraphExecutor {
    param([string]$Agent, [string]$Prompt, [string]$WorkId)
    if ($script:cancellationSignal.Requested) { return $false }
    $script:lastExecutorResult = ''
    $script:lastExecutorOutcome = $null
    $route = [pscustomobject]@{ Agent = $Agent; Category = 'engineering'; Reason = 'Selected leader assignment' }
    $started=Get-Date
    try {
        # Execution is bound to the adapter's fixed engine function.
        $adapter = Get-AgexAdapter -Id $Agent
        if (-not $adapter) { throw "No AGEX adapter for agent '$Agent'." }
        & $adapter.Executor -Prompt $Prompt -WorkId $WorkId -Route $route
    } finally {
        if ($script:ui -and $script:ui.TimingEvents) {
            $kind=if($WorkId -match '-leader-|plan-repair'){ 'LEADER' } elseif($WorkId -match '-mail-|answer-'){ 'MAILBOX' } else { 'EXECUTOR' }
            $script:ui.TimingEvents.Enqueue([pscustomobject]@{Kind=$kind;WorkId=$WorkId;Started=$started;Ended=Get-Date;Seconds=[math]::Round(((Get-Date)-$started).TotalSeconds,2)})
        }
    }
}

function Add-AgexActivity {
    # User-facing activity line. Kind drives the glyph: PASS, FAIL, START, WARNING, INFO, FALLBACK.
    param([Parameter(Mandatory)][string]$Message, [string]$Kind = 'INFO', [string]$TaskId = '')
    if (-not $script:ui) { return }
    $status = switch ($Kind) { 'PASS' { 'DONE' } 'FAIL' { 'FAILED' } 'WARNING' { 'WARNING' } 'FALLBACK' { 'WARNING' } 'START' { 'RUNNING' } default { '' } }
    [void](Add-AgexUiEvent -State $script:ui -Source 'AGEX' -Kind $Kind -Message $Message -Status $status -TaskId $TaskId)
    if ($Kind -in @('FALLBACK', 'FAIL')) { Add-AgexMessage -State $script:ui -From 'AGEX' -To 'User' -Type SYSTEM -Text $Message -TaskId $TaskId }
    Write-AgexLog -Runtime $script:runtime -Event 'activity' -Data @{ kind = $Kind; message = $Message; task = $TaskId; request = [string]$script:ui.WorkId }
}

function Get-AgexRequestId {
    if ($script:ui -and $script:ui.WorkId) { return [string]$script:ui.WorkId }
    ''
}

function Add-AgexRequestRecord {
    param([ValidateSet('Fallbacks','Failures')][string]$Kind, [Parameter(Mandatory)]$Record)
    if (-not $script:runtime) { return }
    $list = $script:runtime[$Kind]
    if ($null -eq $list) { return }
    $Record | Add-Member -NotePropertyName RequestId -NotePropertyValue (Get-AgexRequestId) -Force
    [void]$list.Add($Record)
    while ($list.Count -gt 200) { $list.RemoveAt(0) }
}

function Get-AgexRequestRecords {
    param([ValidateSet('Fallbacks','Failures')][string]$Kind, [string]$RequestId)
    if (-not $script:runtime -or $null -eq $script:runtime[$Kind] -or -not $RequestId) { return @() }
    @($script:runtime[$Kind].ToArray() | Where-Object { $_.RequestId -eq $RequestId -or ([string]$_.RequestId).StartsWith($RequestId + '-') })
}

function Get-AgexAvailabilityText {
    $codex = Get-AgexAgentHealth -Runtime $script:runtime -Agent Codex
    $agy = Get-AgexAgentHealth -Runtime $script:runtime -Agent Antigravity
    $codexWrites = Test-AgexAgentCanWrite -Agent Codex
    $lines = @()
    $lines += if (-not $codex.Healthy) { 'Codex: UNAVAILABLE this session. Do not assign tasks to Codex.' } elseif ($codexWrites) { 'Codex: available and can edit files in the project.' } else { 'Codex: available, read-only (can inspect, cannot edit files).' }
    $lines += if ($agy.Healthy) { 'Antigravity: available and can edit files.' } else { 'Antigravity: UNAVAILABLE this session. Do not assign tasks to Antigravity.' }
    if (-not $agy.Healthy -and -not $codexWrites) { $lines += 'No agent can edit files right now. Plan read-only analysis, or return BLOCKED explaining that file edits need Antigravity.' }
    $lines -join ' '
}

function Test-AgexAgentCanWrite {
    # Capability-aware: WRITE_FILES plus the adapter's write policy.
    param([string]$Agent)
    $adapter = Get-AgexAdapter -Id $Agent
    if (-not $adapter -or $adapter.Capabilities -notcontains 'WRITE_FILES') { return $false }
    if ($adapter.WritePolicy -eq 'always') { return $true }
    [bool]($script:runtime -and $script:runtime.CodexSandbox -eq 'workspace-write')
}

function Invoke-AgexAgentCall {
    # Runs one executor call. When the executor fails before doing meaningful
    # work and the other agent is healthy, retry once with the other agent.
    # Never bounces more than MaxAutoFallbacks times per call.
    param(
        [Parameter(Mandatory)][string]$Agent,
        [Parameter(Mandatory)][string]$Prompt,
        [Parameter(Mandatory)][string]$WorkId,
        [string]$Purpose = 'task',
        [switch]$AllowFallback,
        [bool]$NeedsWrite = $false
    )
    $script:lastCallAgent = $Agent
    $maxFallbacks = if ($script:runtime -and $null -ne $script:runtime.MaxAutoFallbacks) { [int]$script:runtime.MaxAutoFallbacks } else { 1 }
    $fallbacksUsed = 0
    $health = Get-AgexAgentHealth -Runtime $script:runtime -Agent $Agent
    if (-not $health.Healthy) {
        $other = Get-AgexOtherAgent -Agent $Agent
        $otherHealth = Get-AgexAgentHealth -Runtime $script:runtime -Agent $other
        if ($AllowFallback -and $maxFallbacks -gt 0 -and $otherHealth.Healthy -and (-not $NeedsWrite -or (Test-AgexAgentCanWrite -Agent $other))) {
            Add-AgexActivity -Kind 'FALLBACK' -Message ("{0} is unavailable. Using {1} for {2}." -f $Agent, $other, $Purpose)
            Add-AgexRequestRecord -Kind Fallbacks -Record ([pscustomobject]@{ From = $Agent; To = $other; Purpose = $Purpose; WorkId = $WorkId; Reason = $health.Reason; At = Get-Date; Recovered = $false; Preemptive = $true })
            $Agent = $other; $fallbacksUsed++
        } else {
            $script:lastExecutorResult = ''
            $script:lastExecutorOutcome = [pscustomobject]@{ Agent = $Agent; Success = $false; Outcome = 'UNAVAILABLE'; Reason = ("{0} is unavailable: {1}" -f $Agent, $health.Reason); FallbackEligible = $false; ExitCode = $null }
            Add-AgexRequestRecord -Kind Failures -Record ([pscustomobject]@{ Agent = $Agent; Purpose = $Purpose; WorkId = $WorkId; Reason = $script:lastExecutorOutcome.Reason; At = Get-Date })
            return $false
        }
    }
    while ($true) {
        $script:lastCallAgent = $Agent
        if (Invoke-AgexGraphExecutor -Agent $Agent -Prompt $Prompt -WorkId $WorkId) {
            foreach ($item in @(Get-AgexRequestRecords -Kind Fallbacks -RequestId (Get-AgexRequestId) | Where-Object { $_.WorkId -eq $WorkId -and $_.To -eq $Agent })) { $item.Recovered = $true }
            if ($fallbacksUsed -gt 0) { Add-AgexActivity -Kind 'PASS' -Message ("Recovered using {0}." -f $Agent) }
            return $true
        }
        $failure = $script:lastExecutorOutcome
        $reason = if ($failure -and $failure.Reason) { [string]$failure.Reason } else { "$Agent returned no result." }
        Add-AgexRequestRecord -Kind Failures -Record ([pscustomobject]@{ Agent = $Agent; Purpose = $Purpose; WorkId = $WorkId; Reason = $reason; At = Get-Date })
        if ($script:cancellationSignal.Requested -or -not $AllowFallback -or $fallbacksUsed -ge $maxFallbacks) { return $false }
        if (-not $failure -or -not $failure.FallbackEligible) { return $false }
        $other = Get-AgexOtherAgent -Agent $Agent
        $otherHealth = Get-AgexAgentHealth -Runtime $script:runtime -Agent $other
        if (-not $otherHealth.Healthy) { Add-AgexActivity -Kind 'FAIL' -Message ("{0} could not run {1}. {2} is also unavailable." -f $Agent, $Purpose, $other); return $false }
        if ($NeedsWrite -and -not (Test-AgexAgentCanWrite -Agent $other)) { Add-AgexActivity -Kind 'FAIL' -Message ("{0} could not run {1}. {2} is read-only and cannot make the file changes." -f $Agent, $Purpose, $other); return $false }
        Add-AgexActivity -Kind 'FALLBACK' -Message ("{0} could not start {1}. Trying {2} automatically..." -f $Agent, $Purpose, $other)
        if ($Purpose -match 'planning|review|plan repair' -and $script:ui) { $script:ui.ResolvedLeader = $other }
        Add-AgexRequestRecord -Kind Fallbacks -Record ([pscustomobject]@{ From = $Agent; To = $other; Purpose = $Purpose; WorkId = $WorkId; Reason = $reason; At = Get-Date; Recovered = $false; Preemptive = $false })
        $Agent = $other
        $fallbacksUsed++
    }
}

function Get-AgexExecutorResultText {
    # Executors answer with {"result": "...", "messages": [...]}. Return the
    # human-readable result, or the raw text when it is not that contract.
    param([AllowEmptyString()][string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    $json = $Text.Trim() -replace '^```(?:json)?\s*', '' -replace '\s*```$', ''
    try { $parsed = $json | ConvertFrom-Json -ErrorAction Stop; if ($parsed.result) { return [string]$parsed.result } } catch { }
    $match = [regex]::Match($Text, '"result"\s*:\s*"((?:[^"\\]|\\.)*)"')
    if ($match.Success) { try { return ('"' + $match.Groups[1].Value + '"' | ConvertFrom-Json) } catch { } }
    $Text.Trim()
}

function Complete-AgexRequestOutcome {
    # Single place that decides the final request status.
    # COMPLETE / COMPLETE_WITH_FALLBACK: leader verified completion.
    # PARTIAL: at least one task done AND at least one failed or cancelled.
    # FAILED: no task succeeded (or the leader gave up) after work began.
    # START_FAILED: no agent could produce a plan; no work started.
    # UNVERIFIED: tasks finished without failure but the leader never confirmed.
    param([string]$Reason, [string]$LeaderStatus = '', [bool]$LeaderEverSucceeded = $false, [string]$Verification = '')
    $cancelled = [bool]$script:cancellationSignal.Requested
    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
    try {
        foreach ($entry in @($script:ui.Tasks | Where-Object Status -in @('QUEUED','STARTING','RUNNING','WAITING','VERIFYING','BLOCKED'))) {
            if ($cancelled) { $entry.Status = 'CANCELLED'; if (-not $entry.Error) { $entry.Error = 'Cancelled by user.' } }
            elseif ($entry.Status -eq 'BLOCKED') { $entry.Status = 'FAILED'; $entry.Error = 'Did not run: a task it depends on did not finish.' }
            else { $entry.Status = 'FAILED'; if (-not $entry.Error) { $entry.Error = 'Did not run before the request ended.' } }
            if ($entry.PSObject.Properties['End']) { $entry.End = Get-Date }
        }
        $tasks = @($script:ui.Tasks.ToArray())
    } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
    $done = @($tasks | Where-Object Status -in @('DONE','REPAIRED')).Count
    $failed = @($tasks | Where-Object Status -in @('FAILED','REPAIR REQUIRED')).Count
    $cancelledTasks = @($tasks | Where-Object Status -eq 'CANCELLED').Count
    $requestId = Get-AgexRequestId
    $fallbacks = @(Get-AgexRequestRecords -Kind Fallbacks -RequestId $requestId)
    $recovered = @($fallbacks | Where-Object Recovered)
    $failures = @(Get-AgexRequestRecords -Kind Failures -RequestId $requestId)
    $status = if ($cancelled) { 'CANCELLED' }
        elseif ($LeaderStatus -eq 'COMPLETE') { if ($recovered.Count) { 'COMPLETE_WITH_FALLBACK' } else { 'COMPLETE' } }
        elseif (-not $LeaderEverSucceeded -and $tasks.Count -eq 0) { 'START_FAILED' }
        elseif ($done -gt 0 -and ($failed + $cancelledTasks) -gt 0) { 'PARTIAL' }
        elseif ($done -eq 0) { 'FAILED' }
        else { 'UNVERIFIED' }
    $headline = switch ($status) {
        'COMPLETE' { 'Request completed.' }
        'COMPLETE_WITH_FALLBACK' { 'Request completed (recovered with another agent).' }
        'PARTIAL' { "Request partly completed: $done of $($tasks.Count) tasks done." }
        'FAILED' { 'Request could not be completed.' }
        'START_FAILED' { 'Request could not start.' }
        'CANCELLED' { 'Request cancelled.' }
        default { 'Tasks finished, but the leader did not confirm the goal.' }
    }
    $whatHappened = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $fallbacks) {
        $outcomeText = if ($item.Recovered) { 'recovered successfully' } else { 'did not recover' }
        [void]$whatHappened.Add(("{0} could not run {1}; switched to {2} automatically ({3})." -f $item.From, $item.Purpose, $item.To, $outcomeText))
    }
    $primaryFailure = @($failures | Select-Object -First 1)
    $executors = [ordered]@{ Codex = 0; Antigravity = 0 }
    if ($script:runtime -and $script:runtime.Executions) {
        foreach ($execution in @($script:runtime.Executions.ToArray() | Where-Object { $_.RequestId -and ($_.RequestId -eq $requestId -or ([string]$_.RequestId).StartsWith($requestId + '-')) })) {
            if ($executors.Contains([string]$execution.Executor)) { $executors[[string]$execution.Executor]++ }
        }
    }
    $results = [System.Collections.Generic.List[string]]::new()
    foreach ($task in @($tasks | Where-Object Status -in @('DONE','REPAIRED'))) {
        $text = Get-AgexExecutorResultText -Text ([string]$task.Result)
        if ($text) { [void]$results.Add(("[{0}] {1}" -f $task.Id, $text)) }
    }
    $started = if ($script:ui.PSObject.Properties['RequestStarted']) { $script:ui.RequestStarted } else { [datetime]::MinValue }
    $outcome = [pscustomobject]@{
        Status = $status; Headline = $headline; Reason = [string]$Reason; Verification = [string]$Verification
        Tasks = $tasks.Count; Done = $done; Failed = $failed; Cancelled = $cancelledTasks
        Executors = [pscustomobject]$executors; Fallbacks = @($fallbacks); Failures = @($failures)
        PrimaryFailure = if ($primaryFailure.Count) { [string]$primaryFailure[0].Reason } else { '' }
        WhatHappened = @($whatHappened); TaskResults = @($results)
        DurationSeconds = if ($started -and $started -ne [datetime]::MinValue) { [math]::Round(((Get-Date) - $started).TotalSeconds, 1) } else { 0 }
    }
    $script:ui | Add-Member -NotePropertyName Outcome -NotePropertyValue $outcome -Force
    $script:ui.GoalStatus = $status
    $text = [System.Collections.Generic.List[string]]::new()
    [void]$text.Add("GOAL: $status")
    [void]$text.Add($headline)
    if ($Reason) { [void]$text.Add($Reason) }
    if ($Verification) { [void]$text.Add("VERIFICATION: $Verification") }
    foreach ($line in $whatHappened) { [void]$text.Add($line) }
    if ($status -in @('FAILED','START_FAILED','PARTIAL') -and $outcome.PrimaryFailure -and ([string]$Reason).IndexOf($outcome.PrimaryFailure) -lt 0) { [void]$text.Add("Failure: $($outcome.PrimaryFailure)") }
    foreach ($line in $results) { [void]$text.Add($line) }
    $script:ui.Result = $text -join "`n"
    Write-AgexLog -Runtime $script:runtime -Event 'request_outcome' -Data @{ request = $requestId; status = $status; tasks = $tasks.Count; done = $done; failed = $failed; cancelled = $cancelledTasks; fallbacks = $fallbacks.Count; reason = [string]$Reason }
}

function New-AgexExecutorPrompt {
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

function Invoke-AgexGoalGraph {
    param([string]$RootGoal, [string]$WorkId)
    # This bounds reconciliation rounds, never the number of planned tasks.
    $script:ui.GoalStatus = 'RUNNING'
    if (-not $script:ui.PSObject.Properties['RequestStarted'] -or $script:ui.RequestStarted -eq [datetime]::MinValue) { $script:ui | Add-Member -NotePropertyName RequestStarted -NotePropertyValue (Get-Date) -Force }
    Set-AgexUiStage -State $script:ui -Stage 'Planning'
    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
    try { $script:ui.Chat.Clear() } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
    Update-AgexChanges -State $script:ui
    $script:mailboxTurns = 0
    if (-not $script:ActiveLeader) { $script:ActiveLeader = $script:ResolvedLeader }
    $leaderHealth = Get-AgexAgentHealth -Runtime $script:runtime -Agent $script:ActiveLeader
    if (-not $leaderHealth.Healthy) {
        $other = Get-AgexOtherAgent -Agent $script:ActiveLeader
        if ((Get-AgexAgentHealth -Runtime $script:runtime -Agent $other).Healthy) {
            Add-AgexActivity -Kind 'FALLBACK' -Message ("{0} is unavailable. {1} leads this request." -f $script:ActiveLeader, $other)
            Add-AgexRequestRecord -Kind Fallbacks -Record ([pscustomobject]@{ From = $script:ActiveLeader; To = $other; Purpose = 'planning'; WorkId = "$WorkId-leader-0"; Reason = $leaderHealth.Reason; At = Get-Date; Recovered = $false; Preemptive = $true })
            $script:ActiveLeader = $other
        }
    }
    $script:ui.ResolvedLeader = $script:ActiveLeader
    $reason = 'Reconciliation round budget exhausted.'
    $leaderStatus = ''
    $leaderEverSucceeded = $false
    $verification = ''
    for ($round = 0; $round -lt 6; $round++) {
        if ($script:cancellationSignal.Requested) { break }
        $rootPath=[IO.Path]::GetFullPath($Project).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
        $projectFiles=@(Get-ChildItem -LiteralPath $Project -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' } | Select-Object -First 300 | ForEach-Object { [pscustomobject]@{path=$_.FullName.Substring($rootPath.Length);size=$_.Length;modified_utc=$_.LastWriteTimeUtc.ToString('o')} })
        $gitStatus=if(Test-Path -LiteralPath (Join-Path $Project '.git')){@(& git -C $Project status --short --untracked-files=all 2>$null | Select-Object -First 300)}else{@()}
        $script:ui.VerifiedProjectState=[pscustomobject]@{files=$projectFiles;git_status=$gitStatus}
        $evidence = @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks | Select-Object Id,Summary,Status,Result,Verification) | ConvertTo-Json -Depth 8 -Compress
        $messages = @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Chat) | ConvertTo-Json -Depth 5 -Compress
        $availability = Get-AgexAvailabilityText
        $leaderPrompt = @"
You are selected leader. Reconcile immutable user goal with independently collected filesystem and Git evidence below. Agent claims are untrusted. A task with status FAILED or REPAIR REQUIRED is not complete. Create and execute repair tasks; each repair task must set repair_for to failed task ids. COMPLETE requires every requirement verified and every failed task repaired. No task count target. AGENT AVAILABILITY: $availability Return ONLY JSON: {"goal_status":"CONTINUE|COMPLETE|BLOCKED","reason":"operational explanation; for a question, the answer itself","verification":"observed evidence required for COMPLETE","tasks":[{"id":"unique-id","title":"title","objective":"assignment","executor":"Codex|Antigravity","dependencies":[],"affected_files":[],"repair_for":[]}]}. Invalid plan gets one repair attempt. Six reconciliation rounds maximum; no task count limit.
ORIGINAL USER GOAL:
$RootGoal
INDEPENDENT CURRENT PROJECT EVIDENCE:
$($script:ui.VerifiedProjectState | ConvertTo-Json -Depth 5 -Compress)
TASK RESULTS:
$evidence
OPERATIONAL MESSAGES:
$messages
"@
        $purpose = if ($round -eq 0) { 'planning' } else { 'the progress review' }
        if ($round -eq 0) { Add-AgexActivity -Kind 'START' -Message ("{0} is planning the request..." -f $script:ActiveLeader) }
        else { Add-AgexActivity -Kind 'START' -Message ("{0} is reviewing results (round {1})..." -f $script:ActiveLeader, ($round + 1)) }
        if (-not (Invoke-AgexAgentCall -Agent $script:ActiveLeader -Prompt $leaderPrompt -WorkId "$WorkId-leader-$round" -Purpose $purpose -AllowFallback)) {
            if ($script:cancellationSignal.Requested) { $reason = 'Cancelled by user.'; break }
            $failureText = if ($script:lastExecutorOutcome -and $script:lastExecutorOutcome.Reason) { [string]$script:lastExecutorOutcome.Reason } else { 'The leader agent returned no result.' }
            $reason = if ($round -eq 0) { "No agent could plan this request. $failureText" } else { "The leader could not review the results. $failureText" }
            Add-AgexActivity -Kind 'FAIL' -Message $reason
            break
        }
        if ($script:lastCallAgent -and $script:lastCallAgent -ne $script:ActiveLeader) { $script:ActiveLeader = $script:lastCallAgent; $script:ui.ResolvedLeader = $script:ActiveLeader }
        $leaderEverSucceeded = $true
        try { $plan = ConvertFrom-AgexPlan -Text $script:lastExecutorResult -Existing @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks) -AuditState $script:ui }
        catch {
            Add-AgexActivity -Kind 'WARNING' -Message 'Leader plan was not valid. Asking the leader to fix it...'
            $failedIds=@(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -in @('FAILED','REPAIR REQUIRED') | ForEach-Object Id)
            $repair = $leaderPrompt + [Environment]::NewLine + "Your previous plan was invalid: $($_.Exception.Message). Repair it once. Every task in $($failedIds -join ', ') needs a new executable task whose repair_for includes its exact ID. The failed task must not remain falsely complete. Previous output:" + [Environment]::NewLine + $script:lastExecutorResult
            if (-not (Invoke-AgexAgentCall -Agent $script:ActiveLeader -Prompt $repair -WorkId "$WorkId-plan-repair-$round" -Purpose 'plan repair')) {
                $reason = "The leader could not repair its plan. $(if ($script:lastExecutorOutcome) { $script:lastExecutorOutcome.Reason })"
                Add-AgexActivity -Kind 'FAIL' -Message $reason
                break
            }
            try { $plan = ConvertFrom-AgexPlan -Text $script:lastExecutorResult -Existing @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks) -AuditState $script:ui }
            catch { $reason="The leader returned an invalid plan twice: $($_.Exception.Message)`n$(Get-AgexLeaderPlanAuditSummary -State $script:ui)"; Add-AgexActivity -Kind 'FAIL' -Message 'Leader plan still invalid after one repair.'; break }
        }
        $reason = [string]$plan.reason
        $leaderStatus = [string]$plan.goal_status
        if ($plan.goal_status -eq 'COMPLETE') {
            if (@(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -notin @('DONE','REPAIRED')).Count -gt 0) { $reason = 'Leader claimed completion with unresolved or unrepaired tasks.'; $leaderStatus = 'REJECTED'; Add-AgexActivity -Kind 'WARNING' -Message $reason; break }
            $verification = [string]$plan.verification
            Add-AgexMessage -State $script:ui -From $script:ActiveLeader -To 'User' -Type RESULT -Text ($reason + $(if ($verification) { "`n`nVerification: $verification" } else { '' }))
            Add-AgexActivity -Kind 'PASS' -Message 'Leader verified the goal is complete.'
            Complete-AgexRequestOutcome -Reason $reason -LeaderStatus 'COMPLETE' -LeaderEverSucceeded $true -Verification $verification
            return
        }
        if ($plan.goal_status -eq 'BLOCKED') { Add-AgexMessage -State $script:ui -From $script:ActiveLeader -To 'User' -Type STATUS -Text ("Blocked: " + $reason); Add-AgexActivity -Kind 'FAIL' -Message ("Leader reported the goal is blocked: {0}" -f $reason); break }
        if (@($plan.tasks).Count -eq 0) { $reason = 'Leader returned no actionable work and no verified completion.'; Add-AgexActivity -Kind 'WARNING' -Message $reason; break }
        foreach ($item in @($plan.tasks)) {
            Add-AgexUiTask -State $script:ui -Task ([pscustomobject]@{
                Id=[string]$item.id; OriginalId=[string]$item.original_id; Aliases=@($item.aliases); Summary=[string]$item.title; Task=[string]$item.objective; Agent=[string]$item.executor
                Status='QUEUED'; Dependencies=@($item.dependencies); AffectedFiles=@($item.affected_files); RepairFor=@($item.repair_for)
                Started=[datetime]::MinValue; End=[datetime]::MinValue; CreatedAt=Get-Date
                Reason='Leader assignment'; Error=''; Result=''; Verification=''; VerifiedResult=$null; Attempt=0
            })
        }
        if ($reason) { Add-AgexMessage -State $script:ui -From $script:ActiveLeader -To 'AGEX' -Type STATUS -Text $reason }
        foreach ($item in @($plan.tasks)) {
            $assignmentType = if (@($item.repair_for).Count) { 'REVISION_REQUEST' } else { 'ASSIGNMENT' }
            $assignmentText = if ($item.title -and $item.title -ne $item.objective) { "$($item.title)`n$($item.objective)" } else { [string]$item.objective }
            Add-AgexMessage -State $script:ui -From $script:ActiveLeader -To ([string]$item.executor) -Type $assignmentType -Text $assignmentText -TaskId ([string]$item.id)
        }
        $taskWord = if (@($plan.tasks).Count -eq 1) { 'task' } else { 'tasks' }
        Add-AgexActivity -Kind 'PASS' -Message ("Plan ready: {0} {1}." -f @($plan.tasks).Count, $taskWord)
        Set-AgexUiStage -State $script:ui -Stage 'Graph created'
        Set-AgexUiStage -State $script:ui -Stage 'Scheduling'
        Set-AgexUiStage -State $script:ui -Stage 'Executing'
        [void](Add-AgexUiEvent -State $script:ui -Source 'LEADER' -Kind 'PLAN' -Message $reason -Status 'RUNNING')
        Invoke-AgexReadyTasks -RootGoal $RootGoal -WorkId $WorkId
        Set-AgexUiStage -State $script:ui -Stage 'Reconciling'
        Invoke-AgexMailbox -RootGoal $RootGoal -WorkId $WorkId
    }
    Complete-AgexRequestOutcome -Reason $reason -LeaderStatus $leaderStatus -LeaderEverSucceeded $leaderEverSucceeded -Verification $verification
}

function Test-AgexPathConflict {
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

function Test-AgexTaskDependenciesSatisfied {
    param($Entry)
    foreach ($dependency in @($Entry.Dependencies)) {
        $prior=Find-AgexUiTask -State $script:ui -TaskId ([string]$dependency)
        if (-not $prior) { return $false }
        if ($prior.Status -in @('DONE','REPAIRED')) { continue }
        # A repair is allowed to run against the failed node it repairs; making
        # that failed node a hard prerequisite otherwise deadlocks the repair.
        if ($prior.Status -eq 'REPAIR REQUIRED' -and $dependency -in @($Entry.RepairFor)) { continue }
        return $false
    }
    return $true
}

function Get-AgexOperationalReplyObjects {
    param([string]$Text)
    $items=[System.Collections.Generic.List[object]]::new();$depth=0;$start=-1;$quoted=$false;$escaped=$false
    for($index=0;$index -lt $Text.Length;$index++){
        $char=$Text[$index]
        if($quoted){if($escaped){$escaped=$false}elseif($char -eq [char]92){$escaped=$true}elseif($char -eq '"'){$quoted=$false};continue}
        if($char -eq '"'){$quoted=$true;continue}
        if($char -eq '{'){if($depth -eq 0){$start=$index};$depth++;continue}
        if($char -eq '}'){$depth--;if($depth -eq 0 -and $start -ge 0){try{$candidate=$Text.Substring($start,$index-$start+1)|ConvertFrom-Json -ErrorAction Stop;if($candidate.messages){[void]$items.Add($candidate)}}catch{};$start=-1}}
    }
    $items.ToArray()
}

function Add-AgexOperationalMessages {
    param($Entry)
    try { foreach($reply in @(Get-AgexOperationalReplyObjects -Text ([string]$Entry.Result))) {
        foreach ($message in @($reply.messages)) {
            if (@(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Chat).Count -ge 48) { break }
            if ($message.to -notin @('Codex','Antigravity') -or $message.to -eq $Entry.Agent -or $message.type -notin @('QUESTION','ANSWER','REQUEST','RESULT','BLOCKER','HANDOFF','REVIEW') -or -not $message.content) { continue }
            $authoritativeBody=Protect-AgexAuthoritativeText -Text ([string]$message.content)
            $previewBody=Protect-AgexTelemetryText -Text $authoritativeBody
            if (@(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Chat | Where-Object { $_.from -eq $Entry.Agent -and $_.to -eq $message.to -and $_.AuthoritativeBody -eq $authoritativeBody }).Count) { continue }
            $messageId=[guid]::NewGuid().ToString('N');$createdAt=(Get-Date).ToUniversalTime().ToString('o')
            $chatMessage=[pscustomobject]@{MessageId=$messageId;From=$Entry.Agent;To=$message.to;TaskId=$Entry.Id;Type=$message.type;AuthoritativeBody=$authoritativeBody;PreviewBody=$previewBody;CreatedAt=$createdAt;DeliveredAt='';Status='QUEUED';message_id=$messageId;timestamp=$createdAt;task_id=$Entry.Id;content=$previewBody}
            [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
            try { [void]$script:ui.Chat.Add($chatMessage) } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
            Add-AgexMessage -State $script:ui -From $Entry.Agent -To ([string]$message.to) -Type (ConvertTo-AgexMessageType -Type ([string]$message.type)) -Text $authoritativeBody -TaskId $Entry.Id
        }
    } } catch { }
}

function Test-AgexAcceptanceReadmeEvidence {
    param([Parameter(Mandatory)][string]$Path)
    $verifyPath=Join-Path $Project 'verify.ps1'
    if (-not (Test-Path -LiteralPath $verifyPath -PathType Leaf) -or -not ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($verifyPath)).Contains('AGEX_ACCEPTANCE_VERIFY_V1'))) { return $null }
    $bytes=[IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 4 -or $bytes[0] -ne 0xef -or $bytes[1] -ne 0xbb -or $bytes[2] -ne 0xbf) { return 'README_ENCODING_INVALID: expected UTF-8 BOM.' }
    $body=$bytes[3..($bytes.Length-1)]
    if (@($body | Where-Object { $_ -gt 0x7f }).Count) { return 'README_ENCODING_INVALID: acceptance README contains non-ASCII bytes.' }
    $text=[Text.Encoding]::ASCII.GetString($body)
    if ($text -notmatch 'AGEX_ACCEPTANCE_README_V1' -or $text -notmatch 'U\+00E9' -or $text -notmatch 'Error Expectations') { return 'README_CONTENT_INVALID: acceptance README marker or required content missing.' }
    $null
}

function Get-AgexTaskVerification {
    param($Entry)
    $evidence=[System.Collections.Generic.List[object]]::new()
    $claims=[System.Collections.Generic.List[string]]::new()
    foreach ($path in @($Entry.AffectedFiles)) { if ($path) { [void]$claims.Add([string]$path) } }
    $text=[string]$Entry.Result
    $deleted=@{}
    foreach ($match in [regex]::Matches($text,'(?im)\b(?:deleted|removed)\s+(?:file\s+)?["]?([A-Za-z0-9_.-]+(?:[/\\][A-Za-z0-9_.-]+)*\.[A-Za-z0-9]{1,12})')) { $deleted[$match.Groups[1].Value.Trim()]=$true }
    foreach ($match in [regex]::Matches($text,'(?im)\b(?:created|modified|updated|deleted|removed|wrote|saved)\s+(?:file\s+)?["]?([A-Za-z0-9_.-]+(?:[/\\][A-Za-z0-9_.-]+)*\.[A-Za-z0-9]{1,12})')) {
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
            $contentError=''
            if ($exists) { $item=Get-Item -LiteralPath $absolute; $size=$item.Length;$modified=$item.LastWriteTimeUtc.ToString('o');$hash=(Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash; if ([IO.Path]::GetFileName($absolute) -ieq 'README.md') { $contentError=Test-AgexAcceptanceReadmeEvidence -Path $absolute } }
            $verifiedExists=$exists -and [string]::IsNullOrEmpty($contentError)
            [void]$evidence.Add([pscustomobject]@{claimed_path=$claimed;absolute_path=$absolute;exists=$verifiedExists;expected_exists=$shouldExist;size=$size;sha256=$hash;modified_utc=$modified;git_state=$gitState;content_error=$contentError})
        } catch { [void]$evidence.Add([pscustomobject]@{claimed_path=$claimed;exists=$false;error=$_.Exception.Message}) }
    }
    if ($claims.Count -and @($evidence | Where-Object { $_.exists -ne $_.expected_exists }).Count) { return [pscustomobject]@{Pass=$false;Claimed=@($claims);Evidence=@($evidence);Reason='One or more claimed file changes do not match project state.'} }
    [pscustomobject]@{Pass=$true;Claimed=@($claims);Evidence=@($evidence);Reason=if($claims.Count){'All declared and stated output files verified.'}else{'No file output claimed; executor result recorded.'}}
}

function Set-AgexTaskVerificationState {
    param($Entry,$Verification,[bool]$ExecutionSuccess,[datetime]$EvidenceAt=(Get-Date))
    if (-not $Entry.PSObject.Properties['VerificationHistory']) { $Entry | Add-Member -NotePropertyName VerificationHistory -NotePropertyValue ([System.Collections.Generic.List[object]]::new()) }
    [void]$Entry.VerificationHistory.Add([pscustomobject]@{At=$EvidenceAt;Pass=[bool]$Verification.Pass;Evidence=$Verification.Evidence;Reason=$Verification.Reason})
    $last=if($Entry.PSObject.Properties['LastVerificationAt']){[datetime]$Entry.LastVerificationAt}else{[datetime]::MinValue}
    if($last -gt $EvidenceAt){return $false}
    $Entry | Add-Member -NotePropertyName LastEvidenceAt -NotePropertyValue $EvidenceAt -Force
    $Entry | Add-Member -NotePropertyName LastVerificationAt -NotePropertyValue $EvidenceAt -Force
    $Entry | Add-Member -NotePropertyName Verification -NotePropertyValue $Verification -Force
    $Entry | Add-Member -NotePropertyName VerifiedResult -NotePropertyValue $Verification -Force
    if($ExecutionSuccess -and $Verification.Pass){$Entry.Status='DONE';$Entry.Error='';$Entry | Add-Member -NotePropertyName VerificationStatus -NotePropertyValue 'PASS' -Force;$Entry | Add-Member -NotePropertyName CurrentOutcome -NotePropertyValue 'DONE' -Force}elseif(-not $ExecutionSuccess){$Entry.Status='FAILED';if(-not $Entry.Error){$Entry.Error='The agent did not return a result.'};$Entry | Add-Member -NotePropertyName VerificationStatus -NotePropertyValue 'NOT RUN' -Force;$Entry | Add-Member -NotePropertyName CurrentOutcome -NotePropertyValue 'FAILED' -Force}else{$Entry.Status='REPAIR REQUIRED';$Entry.Error=$Verification.Reason;$Entry | Add-Member -NotePropertyName VerificationStatus -NotePropertyValue 'FAIL' -Force;$Entry | Add-Member -NotePropertyName CurrentOutcome -NotePropertyValue 'REPAIR REQUIRED' -Force}
    $true
}

function Unlock-AgexDependentTasks {
    param([string]$TaskId)
    $unlocked=[System.Collections.Generic.List[object]]::new()
    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
    try { foreach($entry in @($script:ui.Tasks | Where-Object { $_.Status -eq 'BLOCKED' -and $TaskId -in @($_.Dependencies) })){$entry.Status='QUEUED';$entry.Error='';$entry.Reason="Dependency $TaskId verified";[void]$unlocked.Add($entry)} }
    finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
    $unlocked.ToArray()
}

function Invoke-AgexMailbox {
    param([string]$RootGoal,[string]$WorkId)
    foreach ($message in @(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Chat | Where-Object status -eq 'QUEUED')) {
        if ($script:cancellationSignal.Requested) { break }
        if ($script:mailboxTurns -ge 8) { [System.Threading.Monitor]::Enter($script:ui.CollectionSync); try { $message.status='FAILED' } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }; continue }
        $script:mailboxTurns++
        $messageBody=if($message.PSObject.Properties['AuthoritativeBody']){[string]$message.AuthoritativeBody}else{[string]$message.content}
        $prompt="AGEX operational message. Original goal: $RootGoal`nMessage id: $($message.message_id); from: $($message.from); task: $($message.task_id); type: $($message.type)`n$messageBody`nInspect relevant project state if needed. Respond concisely with an explicit answer or blocker. Do not edit files in this communication turn. Do not generate further requests."
        if (-not (Invoke-AgexAgentCall -Agent $message.to -Prompt $prompt -WorkId "$WorkId-mail-$($message.message_id)" -Purpose "a message")) { [System.Threading.Monitor]::Enter($script:ui.CollectionSync); try { $message.status='FAILED' } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }; continue }
        [System.Threading.Monitor]::Enter($script:ui.CollectionSync); try { $message.status='DELIVERED';$message.Status='DELIVERED';$message.DeliveredAt=(Get-Date).ToUniversalTime().ToString('o') } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
        if ($message.type -in @('ANSWER','RESULT','BLOCKER')) { continue }
        $answer = Protect-AgexAuthoritativeText -Text $script:lastExecutorResult
        $previewAnswer=Protect-AgexTelemetryText -Text $answer
        $responseId=[guid]::NewGuid().ToString('N');$responseAt=(Get-Date).ToUniversalTime().ToString('o')
        $response=[pscustomobject]@{MessageId=$responseId;From=$message.to;To=$message.from;TaskId=$message.task_id;Type='ANSWER';AuthoritativeBody=$answer;PreviewBody=$previewAnswer;CreatedAt=$responseAt;DeliveredAt='';Status='QUEUED';message_id=$responseId;timestamp=$responseAt;task_id=$message.task_id;content=$previewAnswer}
        [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
        try { [void]$script:ui.Chat.Add($response); $message.status='ANSWERED' } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
        Add-AgexMessage -State $script:ui -From ([string]$message.to) -To ([string]$message.from) -Type ANSWER -Text $answer -TaskId ([string]$message.task_id)
        if ($script:cancellationSignal.Requested) { break }
        $ack="AGEX answer to your message $($message.message_id). Response id: $($response.message_id). Original goal: $RootGoal`nTask: $($message.task_id). From $($message.to): $answer`nAcknowledge this answer and state any remaining gap. Do not edit files or generate further messages."
        $responseStatus=if (Invoke-AgexAgentCall -Agent $response.to -Prompt $ack -WorkId "$WorkId-answer-$($response.message_id)" -Purpose "a message") { 'ANSWERED' } else { 'FAILED' }
        [System.Threading.Monitor]::Enter($script:ui.CollectionSync); try { $response.status=$responseStatus;$response.Status=$responseStatus;if($responseStatus -eq 'ANSWERED'){$response.DeliveredAt=(Get-Date).ToUniversalTime().ToString('o')} } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
    }
}

function Copy-AgexChildEvents {
    # Child executor runspaces keep their own UI state; surface their events in
    # the session activity so the single renderer can show them.
    param([Parameter(Mandatory)]$Job)
    try {
        foreach ($item in @(Get-AgexUiCollectionSnapshot -State $Job.Ui -Collection Events | Where-Object { $_.At -gt $Job.LastEventAt })) {
            $Job.LastEventAt = $item.At
            if ($item.Source -eq 'AGEX') { [void](Add-AgexUiEvent -State $script:ui -Source 'AGEX' -Kind $item.Kind -Message $item.Message -Status $item.Status -TaskId $Job.Entry.Id) }
            else { [void](Add-AgexUiEvent -State $script:ui -Source ("{0}/{1}" -f $Job.Entry.Id, $item.Source) -Kind $item.Kind -Message $item.Message -Status $item.Status -TaskId $Job.Entry.Id) }
        }
    } catch { }
}

function Resolve-AgexTaskAgent {
    # Before launching, make sure the assigned agent is usable. Reassign once to
    # the other agent when it can do the work; otherwise fail the task clearly
    # instead of launching an executor that is known to be down.
    param([Parameter(Mandatory)]$Entry)
    $health = Get-AgexAgentHealth -Runtime $script:runtime -Agent $Entry.Agent
    if ($health.Healthy) { return $true }
    $other = Get-AgexOtherAgent -Agent $Entry.Agent
    $needsWrite = [bool](@($Entry.AffectedFiles | Where-Object { $_ }).Count)
    if ((Get-AgexAgentHealth -Runtime $script:runtime -Agent $other).Healthy -and (-not $needsWrite -or (Test-AgexAgentCanWrite -Agent $other))) {
        Add-AgexActivity -Kind 'FALLBACK' -TaskId $Entry.Id -Message ("{0} is unavailable. {1} runs {2}." -f $Entry.Agent, $other, $Entry.Id)
        Add-AgexRequestRecord -Kind Fallbacks -Record ([pscustomobject]@{ From = $Entry.Agent; To = $other; Purpose = $Entry.Id; WorkId = "$($script:ui.WorkId)-$($Entry.Id)"; Reason = $health.Reason; At = Get-Date; Recovered = $false; Preemptive = $true })
        [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
        try { $Entry.Reason = "Reassigned from $($Entry.Agent): $($health.Reason)"; $Entry.Agent = $other } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
        return $true
    }
    $why = if ($needsWrite -and -not (Test-AgexAgentCanWrite -Agent $other)) { "$($Entry.Agent) is unavailable and $other is read-only." } else { "$($Entry.Agent) is unavailable: $($health.Reason)" }
    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
    try { $Entry.Status = 'FAILED'; $Entry.Error = $why; $Entry.End = Get-Date } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
    Set-AgexUiTaskState -TaskId $Entry.Id -Status 'FAILED' -Agent $Entry.Agent -ErrorText $why
    Add-AgexActivity -Kind 'FAIL' -TaskId $Entry.Id -Message ("Not started: {0}. {1}" -f $(if ($Entry.Summary) { $Entry.Summary } else { $Entry.Id }), $why)
    Add-AgexRequestRecord -Kind Failures -Record ([pscustomobject]@{ Agent = $Entry.Agent; Purpose = $Entry.Id; WorkId = "$($script:ui.WorkId)-$($Entry.Id)"; Reason = $why; At = Get-Date })
    $false
}

function Invoke-AgexReadyTasks {
    param([string]$RootGoal,[string]$WorkId)
    $running=[System.Collections.Generic.List[object]]::new()
    try {
        while (-not $script:cancellationSignal.Requested) {
            foreach ($job in @($running.ToArray())) {
                $workerSnapshot=New-AgexUiRenderSnapshot -State $job.Ui
                foreach ($agent in @($workerSnapshot.Agents.Values)) {
                    $key=$agent.Name
                    $display="$($job.Entry.Id)/$key"
                    $agent.Name=$display
                    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                    try { $script:ui.Agents[$display]=$agent } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                    Publish-AgexUiUpdate -State $script:ui -Kind 'AGENT' -Value $agent
                    if ($agent.PID -gt 0 -and $agent.Status -eq 'RUNNING') { Set-AgexUiTaskState -TaskId $job.Entry.Id -Status 'RUNNING' -Agent $job.Entry.Agent }
                }
                Copy-AgexChildEvents -Job $job
                if (-not $job.Async.IsCompleted) { continue }
                try { [void]$job.PowerShell.EndInvoke($job.Async) } catch { $job.Result.Success=$false; $job.Result.Reason=Protect-AgexTelemetryText -Text $_.Exception.Message }
                # Copy the final agent state once more: the snapshot above may predate it.
                foreach ($agent in @((New-AgexUiRenderSnapshot -State $job.Ui).Agents.Values)) {
                    $display="$($job.Entry.Id)/$($agent.Name)"; $agent.Name=$display
                    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                    try { $script:ui.Agents[$display]=$agent } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                }
                Copy-AgexChildEvents -Job $job
                if ($job.Result.Success) {
                    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                    try { foreach ($message in $job.Inbox) { $message.status='DELIVERED' } }
                    finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                }
                [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                try {
                    $job.Entry.Result=[string]$job.Result.Output; $job.Entry.End=Get-Date
                    if (-not $job.Result.Success) { $job.Entry.Error = if ($job.Result.Reason) { [string]$job.Result.Reason } else { 'The agent did not return a result.' } }
                    if ($job.Result.Agent -and $job.Result.Agent -ne $job.Entry.Agent) { $job.Entry.Reason = "Ran on $($job.Result.Agent) after $($job.Entry.Agent) failed"; $job.Entry.Agent = [string]$job.Result.Agent }
                }
                finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                Set-AgexUiTaskState -TaskId $job.Entry.Id -Status 'VERIFYING' -Agent $job.Entry.Agent
                Set-AgexUiStage -State $script:ui -Stage 'Verifying'
                $verificationStarted=Get-Date
                $verification=Get-AgexTaskVerification -Entry $job.Entry
                $script:ui.TimingEvents.Enqueue([pscustomobject]@{Kind='VERIFY';WorkId=$job.Entry.Id;Started=$verificationStarted;Ended=Get-Date;Seconds=[math]::Round(((Get-Date)-$verificationStarted).TotalSeconds,2)})
                [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                try { [void](Set-AgexTaskVerificationState -Entry $job.Entry -Verification $verification -ExecutionSuccess $job.Result.Success -EvidenceAt (Get-Date)) }
                finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                Set-AgexUiTaskState -TaskId $job.Entry.Id -Status $job.Entry.Status -Agent $job.Entry.Agent -ErrorText $job.Entry.Error
                $taskLabel = if ($job.Entry.Summary) { $job.Entry.Summary } else { $job.Entry.Id }
                switch ($job.Entry.Status) {
                    'DONE' { Add-AgexMessage -State $script:ui -From $job.Entry.Agent -To $script:ActiveLeader -Type RESULT -Text (Get-AgexExecutorResultText -Text ([string]$job.Entry.Result)) -TaskId $job.Entry.Id;  Add-AgexActivity -Kind 'PASS' -TaskId $job.Entry.Id -Message ("{0} finished: {1}" -f $job.Entry.Agent, $taskLabel) }
                    'FAILED' { Add-AgexMessage -State $script:ui -From 'AGEX' -To $script:ActiveLeader -Type SYSTEM -Text ("{0} failed: {1}" -f $job.Entry.Id, $job.Entry.Error) -TaskId $job.Entry.Id;  Add-AgexActivity -Kind 'FAIL' -TaskId $job.Entry.Id -Message ("{0} failed: {1}. {2}" -f $job.Entry.Agent, $taskLabel, $job.Entry.Error) }
                    default { Add-AgexMessage -State $script:ui -From $job.Entry.Agent -To $script:ActiveLeader -Type RESULT -Text (Get-AgexExecutorResultText -Text ([string]$job.Entry.Result)) -TaskId $job.Entry.Id; Add-AgexMessage -State $script:ui -From 'AGEX' -To $script:ActiveLeader -Type SYSTEM -Text ("AGEX could not verify {0}: {1}" -f $job.Entry.Id, $job.Entry.Error) -TaskId $job.Entry.Id; Add-AgexActivity -Kind 'WARNING' -TaskId $job.Entry.Id -Message ("{0} needs repair. {1}" -f $taskLabel, $job.Entry.Error) }
                }
                if ($job.Entry.Status -eq 'DONE') { foreach($unblocked in @(Unlock-AgexDependentTasks -TaskId $job.Entry.Id)){Set-AgexUiTaskState -TaskId $unblocked.Id -Status 'QUEUED' -Agent $unblocked.Agent} }
                foreach ($repairedId in @($job.Entry.RepairFor)) {
                    $prior=Find-AgexUiTask -State $script:ui -TaskId ([string]$repairedId)
                    if ($prior -and $job.Entry.Status -eq 'DONE') {
                        [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                        try { [void](Set-AgexTaskVerificationState -Entry $prior -Verification $verification -ExecutionSuccess $true -EvidenceAt (Get-Date)) }
                        finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                        Set-AgexUiTaskState -TaskId $prior.Id -Status $prior.Status -Agent $prior.Agent -ErrorText $prior.Error
                        foreach($unblocked in @(Unlock-AgexDependentTasks -TaskId $prior.Id)){Set-AgexUiTaskState -TaskId $unblocked.Id -Status 'QUEUED' -Agent $unblocked.Agent}
                    }
                }
                Add-AgexOperationalMessages -Entry $job.Entry
                Update-AgexChanges -State $script:ui
                $job.PowerShell.Dispose(); $job.Runspace.Dispose(); [void]$running.Remove($job)
            }
            $pending=@(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -eq 'QUEUED')
            if (-not $pending.Count -and -not $running.Count) { break }
            $started=$false
            foreach ($entry in $pending) {
                if ($running.Count -ge $MaxWorkers -or $script:cancellationSignal.Requested) { break }
                if (-not (Test-AgexTaskDependenciesSatisfied -Entry $entry)) { continue }
                if (-not (Resolve-AgexTaskAgent -Entry $entry)) { continue }
                if ($entry.Agent -eq 'Codex' -and @($running | Where-Object { $_.Entry.Agent -eq 'Codex' }).Count) { continue }
                $conflict=$false
                foreach ($job in $running) { if (Test-AgexPathConflict $entry $job.Entry) { $conflict=$true; break } }
                if ($conflict) { continue }
                $context=@(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Tasks | Where-Object Status -eq 'DONE' | Select-Object Id,Result) | ConvertTo-Json -Depth 5 -Compress
                $inbox=@(Get-AgexUiCollectionSnapshot -State $script:ui -Collection Chat | Where-Object { $_.to -eq $entry.Agent -and $_.status -eq 'QUEUED' })
                $mail=$inbox | ConvertTo-Json -Depth 5 -Compress
                $prompt=New-AgexExecutorPrompt -RootGoal $RootGoal -Entry $entry -Workspace $Project -Mail $mail -Context $context
                $childUi=New-AgexUiState -Project $Project -SessionId $SessionId
                $childUi.UiThreadId=-1; $childUi.WorkId=$WorkId
                $result=[hashtable]::Synchronized(@{Success=$false;Output='';Completed=$false;Reason='';Agent=''})
                $rs=[RunspaceFactory]::CreateRunspace(); $rs.Open()
                $ps=[PowerShell]::Create(); $ps.Runspace=$rs
                [void]$ps.AddCommand((Join-Path $PSScriptRoot 'agex-primary.ps1'))
                $parameters=@{Project=$Project;CodexPath=$CodexPath;SessionId=$SessionId;ConfiguredLeader=$ConfiguredLeader;CodexShare=$CodexShare;AntigravityShare=$AntigravityShare;CodexModel=$CodexModel;CodexEffort=$CodexEffort;AntigravityModel=$AntigravityModel;AntigravityEffort=$AntigravityEffort;AgyPath=$AgyPath;ExecutorPrompt=$prompt;AssignedExecutor=$entry.Agent;ExecutorWorkId="$WorkId-$($entry.Id)";SharedUiState=$childUi;ExecutorResult=$result;CancellationSignal=$script:cancellationSignal;AgexRuntime=$script:runtime;ExecutorNeedsWrite=[bool](@($entry.AffectedFiles | Where-Object { $_ }).Count)}
                foreach ($key in $parameters.Keys) { [void]$ps.AddParameter($key,$parameters[$key]) }
                [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                try { $entry.Attempt++; $entry.Started=Get-Date } finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                Set-AgexUiTaskState -TaskId $entry.Id -Status 'STARTING' -Agent $entry.Agent
                Add-AgexActivity -Kind 'START' -TaskId $entry.Id -Message ("{0} started: {1}" -f $entry.Agent, $(if ($entry.Summary) { $entry.Summary } else { $entry.Id }))
                [System.Threading.Monitor]::Enter($script:ui.UiUpdateClock.SyncRoot)
                try { $script:ui.AssignmentCount = [int]$script:ui.AssignmentCount + 1; $assignmentCount=[int]$script:ui.AssignmentCount }
                finally { [System.Threading.Monitor]::Exit($script:ui.UiUpdateClock.SyncRoot) }
                Publish-AgexUiUpdate -State $script:ui -Kind 'COUNT' -Value $assignmentCount
                $async=$ps.BeginInvoke()
                [void]$running.Add([pscustomobject]@{Entry=$entry;Inbox=$inbox;Ui=$childUi;Result=$result;Runspace=$rs;PowerShell=$ps;Async=$async;LastEventAt=[datetime]::MinValue})
                $started=$true
            }
            if (-not $running.Count -and -not $started -and $pending.Count) {
                foreach ($entry in $pending) {
                    [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
                    try { if ($entry.Status -eq 'QUEUED') { $entry.Status='BLOCKED'; if (-not $entry.Error) { $entry.Error='Unresolved dependency or cycle.' } } }
                    finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
                }
                break
            }
            Start-Sleep -Milliseconds 100
        }
    } finally {
        foreach ($job in $running) {
            foreach ($agent in @(Get-AgexUiCollectionSnapshot -State $job.Ui -Collection Agents | Where-Object Status -notin @('DONE','FAILED','CANCELLED','IDLE'))) {
                $rootPid=if ($agent.DispatchPID -gt 0) { $agent.DispatchPID } else { $agent.PID }
                if ($rootPid -gt 0) { & taskkill.exe /PID ([string]$rootPid) /T /F 2>$null | Out-Null }
            }
            try { $job.PowerShell.Stop() } catch { }
            $job.PowerShell.Dispose();$job.Runspace.Dispose()
            [System.Threading.Monitor]::Enter($script:ui.CollectionSync)
            try { $job.Entry.Status=if ($script:cancellationSignal.Requested) { 'CANCELLED' } else { 'FAILED' }; if (-not $job.Entry.Error) { $job.Entry.Error = if ($script:cancellationSignal.Requested) { 'Cancelled by user.' } else { 'Stopped: scheduler ended unexpectedly.' } } }
            finally { [System.Threading.Monitor]::Exit($script:ui.CollectionSync) }
        }
    }
}
