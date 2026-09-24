# AGEX observability only. It owns terminal state and rendering, never execution.
. (Join-Path $PSScriptRoot 'agex-process.ps1')
. (Join-Path $PSScriptRoot 'agex-session.ps1')

function Assert-AgexUiStateSchema {
    param([Parameter(Mandatory)]$State)
    $required = @('AcceptanceStage','AssignmentCount','UiUpdates','UiUpdateClock','UiAppliedSequence','LastUiPublicationAt','StageTimes','TimingEvents','LastEvidenceAt','Tasks','Agents','Chat','LeaderPlanAudit','CollectionSync','Status','GoalStatus')
    $actual = @($State.PSObject.Properties.Name)
    $missing = @($required | Where-Object { $_ -notin $actual })
    if ($missing.Count) { throw "AGEX session state schema missing: $($missing -join ', ')" }
    $State
}

function New-AgexUiState {
    param(
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$SessionId,
        [string]$ConfiguredLeader,
        [string]$ResolvedLeader,
        [int]$CodexShare,
        [int]$AntigravityShare,
        [string]$CodexModel,
        [string]$AntigravityModel
    )
    $state = [pscustomobject]@{
        Project = $Project
        ProjectName = Split-Path -Leaf $Project
        SessionId = $SessionId
        SessionStart = Get-Date
        ConfiguredLeader = $ConfiguredLeader
        ResolvedLeader = $ResolvedLeader
        CodexShare = $CodexShare
        AntigravityShare = $AntigravityShare
        CodexModel = if ($CodexModel) { $CodexModel } else { "default" }
        AntigravityModel = if ($AntigravityModel) { $AntigravityModel } else { "default" }
        Status = "IDLE"
        WorkId = ""
        Prompt = ""
        GoalStatus = "IDLE"
        Outcome = $null
        RequestStarted = [datetime]::MinValue
        Chat = [System.Collections.Generic.List[object]]::new()
        Messages = [System.Collections.Generic.List[object]]::new()
        MessageSeq = 0
        EventSeq = 0
        Tasks = [System.Collections.Generic.List[object]]::new()
        LeaderPlanAudit = [System.Collections.Generic.List[object]]::new()
        CollectionSync = [object]::new()
        UiUpdates = [System.Collections.Concurrent.ConcurrentQueue[object]]::new()
        UiUpdateClock = [hashtable]::Synchronized(@{ Value = 0 })
        UiAppliedSequence = 0
        LastUiPublicationAt = Get-Date
        MaxUiUpdates = 512
        AcceptanceStage = "Planning"
        StageTimes = [System.Collections.Generic.List[object]]::new()
        TimingEvents = [System.Collections.Concurrent.ConcurrentQueue[object]]::new()
        AssignmentCount = 0
        Events = [System.Collections.Generic.List[object]]::new()
        Agents = [ordered]@{}
        Files = [ordered]@{}
        GitChanges = $null
        DiffLines = @()
        VerifiedProjectState = $null
        CurrentAction = $null
        CurrentCommand = $null
        LastEventAt = [datetime]::MinValue
        Result = ""
        Summary = $null
        CancellationProcessesCleaned = "NOT APPLICABLE"
        CancellationOwnedPids = @()
        View = "NORMAL"
        Unicode = [bool]([Console]::OutputEncoding.CodePage -eq 65001)
        RenderTop = -1
        RenderRows = 0
        LastWindowWidth = 0
        LastWindowHeight = 0
        LastRenderAt = [datetime]::MinValue
        FileQueue = [System.Collections.Concurrent.ConcurrentQueue[object]]::new()
        LastEvidenceAt = [datetime]::MinValue
        FileWatcher = $null
        FileEventSource = ""
        LastFilePump = [datetime]::MinValue
        LastRenderedFingerprint = ""
        FailedRender = $false
        FailedRenderReason = ""
        UiThreadId = [Threading.Thread]::CurrentThread.ManagedThreadId
        EditorRender = $null
        EditorRestore = $null
        EditorCursorRow = -1
        EditorCursorCol = -1
        MaxEvents = 160
        MaxFiles = 200
        MaxFileEvents = 256
    }
    Assert-AgexUiStateSchema -State $state
}

function Limit-AgexUiText {
    param([AllowEmptyString()][string]$Text, [int]$Width)
    if ($Width -le 0) { return "" }
    $value = if ($null -eq $Text) { "" } else { ($Text -replace "\s+", " ").Trim() }
    if ($value.Length -le $Width) { return $value }
    if ($Width -le 3) { return $value.Substring(0, $Width) }
    $value.Substring(0, $Width - 3) + "..."
}

function Copy-AgexUiRecord {
    param([Parameter(Mandatory)]$Record)
    if ($Record -isnot [pscustomobject]) { return $Record }
    $copy = [pscustomobject]@{}
    foreach ($property in $Record.PSObject.Properties) { $copy | Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value | Out-Null }
    $copy
}

function Get-AgexUiCollectionSnapshot {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][ValidateSet('Tasks','Agents','Chat','Events','Files','StageTimes','TimingEvents','LeaderPlanAudit','Messages')][string]$Collection
    )
    if ($State.IsUiRenderSnapshot) {
        switch ($Collection) {
            'Tasks' { return @($State.Tasks) }
            'Agents' { return @($State.Agents.Values) }
            'Chat' { return @($State.Chat) }
            'Events' { return @($State.Events) }
            'Files' { return @($State.Files.Values) }
            'StageTimes' { return @($State.StageTimes) }
            'TimingEvents' { return @($State.TimingEvents) }
            'LeaderPlanAudit' { return @($State.LeaderPlanAudit) }
            'Messages' { return @($State.Messages) }
        }
    }
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try {
        switch ($Collection) {
            'Tasks' { $items = $State.Tasks.ToArray() }
            'Agents' { $items = @($State.Agents.Values) }
            'Chat' { $items = $State.Chat.ToArray() }
            'Events' { $items = $State.Events.ToArray() }
            'Files' { $items = @($State.Files.Values) }
            'StageTimes' { $items = $State.StageTimes.ToArray() }
            'TimingEvents' { $items = $State.TimingEvents.ToArray() }
            'LeaderPlanAudit' { $items = $State.LeaderPlanAudit.ToArray() }
            'Messages' { $items = $State.Messages.ToArray() }
        }
        $items
    } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
}

function Get-AgexUiAgentByName {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Name)
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try { if ($State.Agents.Contains($Name)) { return $State.Agents[$Name] } }
    finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
}

function New-AgexUiRenderSnapshot {
    param([Parameter(Mandatory)]$State)
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try {
        $properties = [ordered]@{}
        foreach ($property in $State.PSObject.Properties) { $properties[$property.Name] = $property.Value }
        foreach ($collection in @('Tasks','Chat','Events','StageTimes','TimingEvents','Messages')) {
            switch ($collection) {
                'Messages' { $source = $State.Messages.ToArray() }
                'Tasks' { $source = $State.Tasks.ToArray() }
                'Chat' { $source = $State.Chat.ToArray() }
                'Events' { $source = $State.Events.ToArray() }
                'StageTimes' { $source = $State.StageTimes.ToArray() }
                'TimingEvents' { $source = $State.TimingEvents.ToArray() }
            }
            $copies = [System.Collections.Generic.List[object]]::new()
            foreach ($record in $source) {
                if ($null -eq $record) { continue }
                [void]$copies.Add((Copy-AgexUiRecord -Record $record))
            }
            $properties[$collection] = $copies.ToArray()
        }
        $agents = [ordered]@{}
        foreach ($agent in $State.Agents.Values) { $agents[$agent.Name] = Copy-AgexUiRecord -Record $agent }
        $properties.Agents = $agents
        $files = [ordered]@{}
        foreach ($file in $State.Files.Values) { $files[$file.Path] = Copy-AgexUiRecord -Record $file }
        $properties.Files = $files
        $properties.IsUiRenderSnapshot = $true
        [pscustomobject]$properties
    } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
}

function Add-AgexUiEvent {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Kind,
        [Parameter(Mandatory)][string]$Message,
        [string]$Status = "",
        [string]$TaskId = ""
    )
    $item = [pscustomobject]@{
        At = Get-Date
        Source = $Source
        Kind = $Kind
        Message = Limit-AgexUiText -Text (Protect-AgexTelemetryText -Text $Message) -Width 500
        Status = $Status
        TaskId = $TaskId
    }
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try {
        if ($State.PSObject.Properties['EventSeq']) { $State.EventSeq = [int]$State.EventSeq + 1; $item | Add-Member -NotePropertyName Seq -NotePropertyValue ([int]$State.EventSeq) }
        if ($State.Events.Count -ge $State.MaxEvents) { $State.Events.RemoveAt(0) }
        [void]$State.Events.Add($item)
    } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
    $State.LastEventAt = $item.At
    $item
}

function Add-AgexUiTask {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)]$Task)
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try { [void]$State.Tasks.Add($Task) } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
    Publish-AgexUiUpdate -State $State -Kind "TASK" -Value $Task
}

function Add-AgexUiUpdateWarning {
    param(
        [Parameter(Mandatory)]$State,
        [string]$Sequence = "?",
        [string]$Kind = "?",
        [string]$Field = "Update",
        [string]$Expected = "valid update",
        [AllowEmptyString()][string]$RuntimeType = "null",
        [AllowEmptyString()][string]$Shape = "null"
    )
    $message = "Rejected UI update seq $Sequence kind $Kind; $Field expected $Expected, got $RuntimeType ($Shape)."
    [void](Add-AgexUiEvent -State $State -Source "UI" -Kind "UPDATE REJECTED" -Message $message -Status "WARNING")
}

function Write-AgexActivityLines {
    # Plain-text progress for redirected output (scripts, CI, acceptance):
    # prints AGEX activity events that were not printed yet.
    param([Parameter(Mandatory)]$State, [ref]$LastSeq)
    foreach ($item in @(Get-AgexUiCollectionSnapshot -State $State -Collection Events | Where-Object { ($_.Source -eq 'AGEX' -or $_.Kind -eq 'SESSION') -and [int]$_.Seq -gt $LastSeq.Value })) {
        $LastSeq.Value = [int]$item.Seq
        Write-Output ("{0} {1,-8} {2}" -f $item.At.ToString("HH:mm:ss"), $item.Kind, $item.Message)
    }
}

function Invoke-AgexUiObserverSafely {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Action, [Parameter(Mandatory)][scriptblock]$Operation)
    try { & $Operation; return $true }
    catch {
        $reason = Protect-AgexTelemetryText -Text ([string]$_.Exception.Message)
        if ($reason.Length -gt 180) { $reason = $reason.Substring(0,180) }
        try { [void](Add-AgexUiEvent -State $State -Source 'UI' -Kind 'UI STATE WARNING' -Message ("$Action failed; execution continues. $reason") -Status 'WARNING') } catch { }
        return $false
    }
}

function Publish-AgexUiUpdate {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Kind, [Parameter(Mandatory)]$Value)
    [System.Threading.Monitor]::Enter($State.UiUpdateClock.SyncRoot)
    try {
        $State.UiUpdateClock.Value = [int]$State.UiUpdateClock.Value + 1
        $sequence = [int]$State.UiUpdateClock.Value
        if ($Kind -eq "COUNT") {
            if ($Value -isnot [int]) {
                $runtimeType = if ($null -eq $Value) { "null" } else { $Value.GetType().FullName }
                $shape = if ($Value -is [pscustomobject]) { (@($Value.PSObject.Properties.Name | Select-Object -First 4) -join ",") } else { "scalar" }
                Add-AgexUiUpdateWarning -State $State -Sequence $sequence -Kind $Kind -Field "Value" -Expected "Int32" -RuntimeType $runtimeType -Shape $shape
                return
            }
            $snapshot = [int]$Value
        } else {
            $snapshot = [pscustomobject]@{}
            foreach ($property in $Value.PSObject.Properties) { $snapshot | Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value }
        }
        if ($Kind -eq "TASK") { $snapshot | Add-Member -NotePropertyName UpdatedAt -NotePropertyValue $(if ($Value.UpdatedAt) { $Value.UpdatedAt } else { Get-Date }) -Force }
        while ($State.UiUpdates.Count -ge $State.MaxUiUpdates) { $discard=$null; [void]$State.UiUpdates.TryDequeue([ref]$discard) }
        $State.UiUpdates.Enqueue([pscustomobject]@{ Sequence=[int]$sequence; Kind=[string]$Kind; Value=$snapshot; At=[datetime](Get-Date) })
    } finally { [System.Threading.Monitor]::Exit($State.UiUpdateClock.SyncRoot) }
}

function Set-AgexUiStage {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Stage)
    if ($State.AcceptanceStage -eq $Stage) { return }
    $State.AcceptanceStage = $Stage
    $item = [pscustomobject]@{ Stage=$Stage; At=Get-Date }
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try { [void]$State.StageTimes.Add($item) } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
    Publish-AgexUiUpdate -State $State -Kind "STAGE" -Value $item
}

function Receive-AgexUiUpdates {
    param([Parameter(Mandatory)]$State)
    $update = $null
    while ($State.UiUpdates.TryDequeue([ref]$update)) {
        if ($null -eq $update -or $update -isnot [pscustomobject]) {
            $runtimeType = if ($null -eq $update) { "null" } else { $update.GetType().FullName }
            Add-AgexUiUpdateWarning -State $State -Field "Envelope" -Expected "PSCustomObject" -RuntimeType $runtimeType
            continue
        }
        $sequenceProperty = $update.PSObject.Properties["Sequence"]
        if (-not $sequenceProperty -or $sequenceProperty.Value -isnot [int]) {
            $sequenceType = if (-not $sequenceProperty -or $null -eq $sequenceProperty.Value) { "null" } else { $sequenceProperty.Value.GetType().FullName }
            Add-AgexUiUpdateWarning -State $State -Kind ([string]$update.Kind) -Field "Sequence" -Expected "Int32" -RuntimeType $sequenceType
            continue
        }
        $sequence = [int]$sequenceProperty.Value
        if ($sequence -le $State.UiAppliedSequence) { continue }
        $State.UiAppliedSequence = $sequence
        $kindProperty = $update.PSObject.Properties["Kind"]
        $valueProperty = $update.PSObject.Properties["Value"]
        $atProperty = $update.PSObject.Properties["At"]
        $kind = if ($kindProperty) { [string]$kindProperty.Value } else { "?" }
        if (-not $atProperty -or $atProperty.Value -isnot [datetime]) {
            $atType = if (-not $atProperty -or $null -eq $atProperty.Value) { "null" } else { $atProperty.Value.GetType().FullName }
            Add-AgexUiUpdateWarning -State $State -Sequence $sequence -Kind $kind -Field "At" -Expected "DateTime" -RuntimeType $atType
            continue
        }
        $State.LastUiPublicationAt = [datetime]$atProperty.Value
        $value = if ($valueProperty) { $valueProperty.Value } else { $null }
        $invalidField = ""
        $expected = ""
        switch ($kind) {
            "TASK" {
                if ($value -isnot [pscustomobject] -or -not $value.PSObject.Properties["Id"] -or $value.Id -isnot [string] -or -not $value.PSObject.Properties["Status"] -or $value.Status -isnot [string]) { $invalidField="Value"; $expected="task object with string Id and Status" }
            }
            "AGENT" {
                if ($value -isnot [pscustomobject] -or -not $value.PSObject.Properties["Name"] -or $value.Name -isnot [string] -or -not $value.PSObject.Properties["Status"] -or $value.Status -isnot [string]) { $invalidField="Value"; $expected="agent object with string Name and Status" }
            }
            "STAGE" {
                if ($value -isnot [pscustomobject] -or -not $value.PSObject.Properties["Stage"] -or $value.Stage -isnot [string]) { $invalidField="Value"; $expected="stage object with string Stage" }
            }
            "COUNT" { if ($value -isnot [int]) { $invalidField="Value"; $expected="Int32" } }
            default { $invalidField="Kind"; $expected="TASK, AGENT, STAGE, or COUNT" }
        }
        if ($invalidField) {
            $runtimeType = if ($null -eq $value) { "null" } else { $value.GetType().FullName }
            $shape = if ($value -is [pscustomobject]) { (@($value.PSObject.Properties.Name | Select-Object -First 4) -join ",") } elseif ($null -eq $value) { "null" } else { "scalar" }
            Add-AgexUiUpdateWarning -State $State -Sequence $sequence -Kind $kind -Field $invalidField -Expected $expected -RuntimeType $runtimeType -Shape $shape
            continue
        }
        if ($update.Kind -eq "TASK") {
            [System.Threading.Monitor]::Enter($State.CollectionSync)
            try {
                $current = $null
                foreach ($task in $State.Tasks) { if ($task.Id -eq [string]$value.Id) { $current=$task; break } }
                if (-not $current) { [void]$State.Tasks.Add($value) }
                elseif (-not $current.UpdatedAt -or $value.UpdatedAt -ge $current.UpdatedAt) { $current.Status=$value.Status; $current.Agent=$value.Agent; if($current.PSObject.Properties.Name -contains 'UpdatedAt'){$current.UpdatedAt=$value.UpdatedAt}else{$current | Add-Member -NotePropertyName UpdatedAt -NotePropertyValue $value.UpdatedAt} }
            } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
        } elseif ($update.Kind -eq "AGENT") {
            [System.Threading.Monitor]::Enter($State.CollectionSync)
            try { $State.Agents[$value.Name] = $value } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
            if ($State.Status -eq "RUNNING" -and $value.Status -in @("RUNNING","STARTING","WAITING","QUEUED","RECONCILING")) { $State.CurrentAction = $value }
        } elseif ($update.Kind -eq "STAGE") { $State.AcceptanceStage = $value.Stage }
        elseif ($update.Kind -eq "COUNT") {
            [System.Threading.Monitor]::Enter($State.UiUpdateClock.SyncRoot)
            try { if ([int]$value -gt [int]$State.AssignmentCount) { $State.AssignmentCount = [int]$value } }
            finally { [System.Threading.Monitor]::Exit($State.UiUpdateClock.SyncRoot) }
        }
    }
}

function Find-AgexUiTask {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$TaskId)
    @(Get-AgexUiCollectionSnapshot -State $State -Collection Tasks | Where-Object Id -eq $TaskId | Select-Object -First 1)
}

function Set-AgexUiTask {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$TaskId, [string]$Status, [string]$Agent, [string]$Reason = "", [string]$ErrorText = "")
    $task = $null
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try {
        foreach ($candidate in $State.Tasks) { if ($candidate.Id -eq $TaskId) { $task=$candidate; break } }
        if ($task) {
            if ($Status) { $task.Status = $Status }
            if ($Agent) { $task.Agent = $Agent }
            if ($Reason) { $task.Reason = $Reason }
            if ($ErrorText) { $task.Error = $ErrorText }
            if (($Status -eq "STARTING" -or $Status -eq "RUNNING") -and $task.Started -eq [datetime]::MinValue) { $task.Started = Get-Date }
            if ($Status -in @("DONE", "FAILED", "CANCELLED")) { $task.End = Get-Date }
            if ($task.PSObject.Properties.Name -contains "UpdatedAt") { $task.UpdatedAt = Get-Date } else { $task | Add-Member -NotePropertyName UpdatedAt -NotePropertyValue (Get-Date) }
        }
    } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
    if ($task) { Publish-AgexUiUpdate -State $State -Kind "TASK" -Value $task }
    $task
}

function Start-AgexUiAgent {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Executor,
        [string]$TaskId,
        [string]$TaskText,
        [string]$Model,
        [string]$Command,
        [string]$WorkingDirectory,
        [string]$DiagnosticPath
    )
    $now = Get-Date
    $agent = [pscustomobject]@{
        Name = $Name; Executor = $Executor; Status = "STARTING"; TaskId = $TaskId
        Task = Limit-AgexUiText -Text $TaskText -Width 160; Action = "Starting executor"
        File = ""; PID = 0; Started = $now; End = [datetime]::MinValue
        LastEvent = $now; LastMonitorAt = $now; Model = if ($Model) { $Model } else { "default" }
        Command = $Command; Cwd = $WorkingDirectory; ExitCode = $null; Retry = ""
        StreamEvents = 0; Timeout = "NONE"; ActualPID = 0; DispatchPID = 0; DiagnosticPath = $DiagnosticPath; DiagnosticMarks = @{}
        Health = "OK"; FailureReason = ""
    }
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try { $State.Agents[$Name] = $agent } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
    Publish-AgexUiUpdate -State $State -Kind "AGENT" -Value $agent
    $State.Status = "RUNNING"
    $State.CurrentAction = $agent
    if ($Command) { $State.CurrentCommand = [pscustomobject]@{ Kind = "DISPATCH"; Text = $Command; Status = "RUNNING"; ExitCode = $null; Started = $now; End = [datetime]::MinValue } }
    [void](Add-AgexUiEvent -State $State -Source $Name -Kind "START" -Message ("Task {0}" -f $TaskId) -Status "STARTING" -TaskId $TaskId)
    $agent
}

function Update-AgexUiAgent {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$Name,
        [string]$Status,
        [string]$Action,
        [int]$ProcessId = 0,
        [string]$File,
        [string]$TaskId,
        [string]$EventKind = "ACTION",
        [string]$Message = "",
        [string]$CommandStatus,
        [int]$ExitCode = -999,
        [int]$StreamEvents = -1,
        [string]$Timeout = ""
    )
    $now = Get-Date
    $waitEvent = $null
    $markTaskRunning = $false
    $agent = $null
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try {
        if (-not $State.Agents.Contains($Name)) { return }
        $agent = $State.Agents[$Name]
        if ($EventKind -ne "MONITOR" -and $agent.Health -in @("WAITING", "WARNING") -and $agent.LastEvent -gt [datetime]::MinValue) {
            $waitEvent=[pscustomobject]@{Kind='AGENT_WAIT';WorkId=$agent.TaskId;Started=$agent.LastEvent;Ended=$now;Seconds=[math]::Round(($now-$agent.LastEvent).TotalSeconds,2)}
        }
        if ($Status) { $agent.Status = $Status }
        if ($Status -eq "RUNNING" -and $EventKind -ne "MONITOR") { $agent.Health = "OK" }
        if ($Action) { $agent.Action = $Action }
        if ($ProcessId -gt 0) { $agent.PID = $ProcessId }
        $markTaskRunning = ($Status -eq "RUNNING" -and $agent.PID -gt 0 -and $agent.TaskId)
        if ($File) { $agent.File = $File; $State.Files[$File] = [pscustomobject]@{ Path = $File; Action = "M"; At = $now; Active = $true } }
        if ($TaskId) { $agent.TaskId = $TaskId }
        if ($EventKind -eq "MONITOR") { $agent.LastMonitorAt = $now } else { $agent.LastEvent = $now }
        if ($ExitCode -ne -999) { $agent.ExitCode = $ExitCode }
        if ($StreamEvents -ge 0) { $agent.StreamEvents = $StreamEvents }
        if ($Timeout) { $agent.Timeout = $Timeout }
        $State.CurrentAction = $agent
        if ($CommandStatus -and $State.CurrentCommand) {
            $State.CurrentCommand.Status = $CommandStatus
            if ($ExitCode -ne -999) { $State.CurrentCommand.ExitCode = $ExitCode }
            if ($CommandStatus -ne "RUNNING") { $State.CurrentCommand.End = $now }
        }
        $agentSnapshot=Copy-AgexUiRecord -Record $agent
    } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
    if ($waitEvent) { $State.TimingEvents.Enqueue($waitEvent) }
    if ($markTaskRunning) { [void](Set-AgexUiTask -State $State -TaskId $agentSnapshot.TaskId -Status "RUNNING" -Agent $agentSnapshot.Executor) }
    if ($Message) { [void](Add-AgexUiEvent -State $State -Source $Name -Kind $EventKind -Message $Message -Status $agentSnapshot.Status -TaskId $agentSnapshot.TaskId) }
    Publish-AgexUiUpdate -State $State -Kind "AGENT" -Value $agentSnapshot
    $agent
}

function Update-AgexUiDiagnostic {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Name)
    $agent = Get-AgexUiAgentByName -State $State -Name $Name
    if (-not $agent) { return }
    if ([string]::IsNullOrWhiteSpace($agent.DiagnosticPath) -or -not (Test-Path -LiteralPath $agent.DiagnosticPath -PathType Leaf)) { return }
    try { $text = [IO.File]::ReadAllText($agent.DiagnosticPath) } catch { return }
    foreach ($mark in @("AGY_PROCESS_STARTED", "STDIN_WRITTEN", "STDIN_FLUSHED", "FIRST_STDOUT_EVENT")) {
        $newMark=$false
        [System.Threading.Monitor]::Enter($State.CollectionSync)
        try { if ($text -match [regex]::Escape($mark) -and -not $agent.DiagnosticMarks.ContainsKey($mark)) { $agent.DiagnosticMarks[$mark] = $true; $newMark=$true } }
        finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
        if ($newMark) {
            $kind = switch ($mark) { "AGY_PROCESS_STARTED" { "START" } "FIRST_STDOUT_EVENT" { "ACTION" } default { "INPUT" } }
            $message = switch ($mark) { "AGY_PROCESS_STARTED" { "AGY process started" } "STDIN_WRITTEN" { "AGY input written" } "STDIN_FLUSHED" { "AGY input flushed" } default { "First AGY stream event" } }
            [void](Add-AgexUiEvent -State $State -Source $Name -Kind $kind -Message $message -Status "RUNNING" -TaskId $agent.TaskId)
            [System.Threading.Monitor]::Enter($State.CollectionSync)
            try { $agent.Action = $message; $agent.LastEvent = Get-Date; $agentSnapshot=Copy-AgexUiRecord -Record $agent }
            finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
            Publish-AgexUiUpdate -State $State -Kind "AGENT" -Value $agentSnapshot
        }
    }
    if ($text -match '(?m)^AGY_PID=(\d+)' -and $agent.PID -ne [int]$Matches[1]) {
        [System.Threading.Monitor]::Enter($State.CollectionSync)
        try { $agent.PID = [int]$Matches[1]; $agent.ActualPID = $agent.PID; $agentSnapshot=Copy-AgexUiRecord -Record $agent }
        finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
        [void](Add-AgexUiEvent -State $State -Source $Name -Kind "START" -Message ("Actual AGY PID {0}" -f $agent.PID) -Status "RUNNING" -TaskId $agent.TaskId)
        Publish-AgexUiUpdate -State $State -Kind "AGENT" -Value $agentSnapshot
    }
}

function Complete-AgexUiAgent {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Name, [ValidateSet("DONE", "FAILED", "CANCELLED")][string]$Status, [string]$Message = "", [int]$ExitCode = 0, [int]$StreamEvents = -1, [string]$Timeout = "")
    $agent = Get-AgexUiAgentByName -State $State -Name $Name
    if (-not $agent) { return }
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try {
        $agent.Status = $Status
        $agent.Health = "OK"
        $agent.End = Get-Date
        $agent.LastEvent = $agent.End
        $agent.Action = if ($Status -eq "DONE") { "Completed" } else { $Status }
        $agent.ExitCode = $ExitCode
        if ($Status -eq "FAILED") { $agent.FailureReason = $Message }
        if ($StreamEvents -ge 0) { $agent.StreamEvents = $StreamEvents }
        if ($Timeout) { $agent.Timeout = $Timeout }
        $State.CurrentAction = $agent
        if ($State.CurrentCommand) {
            $State.CurrentCommand.Status = if ($Status -eq "DONE") { "PASS" } elseif ($Status -eq "CANCELLED") { "CANCELLED" } else { "FAIL" }
            $State.CurrentCommand.ExitCode = $ExitCode
            $State.CurrentCommand.End = $agent.End
        }
        $agentSnapshot=Copy-AgexUiRecord -Record $agent
    } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
    $kind = if ($Status -eq "DONE") { "PASS" } elseif ($Status -eq "CANCELLED") { "CANCEL" } else { "FAIL" }
    $finalMessage = if ($Message) { $Message } else { $agentSnapshot.Action }
    [void](Add-AgexUiEvent -State $State -Source $Name -Kind $kind -Message $finalMessage -Status $Status -TaskId $agent.TaskId)
    $agentSnapshot
}

function Set-AgexUiFile {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Path, [ValidateSet("R", "M", "A", "D", "+", "-")][string]$Action = "M", [bool]$Active = $true)
    $clean = Protect-AgexTelemetryText -Text $Path
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try {
        if (-not $State.Files.Contains($clean) -and $State.Files.Count -ge $State.MaxFiles) {
            $oldest = @($State.Files.Values | Sort-Object At | Select-Object -First 1)
            if ($oldest.Count) { [void]$State.Files.Remove([string]$oldest[0].Path) }
        }
        $State.Files[$clean] = [pscustomobject]@{ Path = $clean; Action = $Action; At = Get-Date; Active = $Active }
    } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
    $source = if ($State.CurrentAction) { $State.CurrentAction.Name } else { "FILES" }
    $taskId = if ($State.CurrentAction) { $State.CurrentAction.TaskId } else { "" }
    [void](Add-AgexUiEvent -State $State -Source $source -Kind "FILE" -Message ("{0} {1}" -f $Action, $clean) -Status "RUNNING" -TaskId $taskId)
}

function Start-AgexUiFileWatch {
    param([Parameter(Mandatory)]$State)
    try {
        if (-not (Test-Path -LiteralPath $State.Project -PathType Container)) { return }
        $watcher = New-Object System.IO.FileSystemWatcher
        $watcher.Path = $State.Project
        $watcher.IncludeSubdirectories = $true
        $watcher.InternalBufferSize = 16384
        $watcher.NotifyFilter = [IO.NotifyFilters]::FileName -bor [IO.NotifyFilters]::LastWrite -bor [IO.NotifyFilters]::Size -bor [IO.NotifyFilters]::CreationTime
        $watcher.EnableRaisingEvents = $true
        $source = "agex-ui-" + ([guid]::NewGuid().ToString("N"))
        foreach ($eventName in @("Created", "Changed", "Deleted", "Renamed")) {
            Register-ObjectEvent -InputObject $watcher -EventName $eventName -SourceIdentifier ($source + "-" + $eventName) -MessageData $State.FileQueue -Action {
                try {
                    $queue = $event.MessageData
                    $args = $event.SourceEventArgs
                    $path = if ($args.FullPath) { [string]$args.FullPath } else { "" }
                    while ($queue.Count -ge 256) { $discard = $null; [void]$queue.TryDequeue([ref]$discard) }
                    $queue.Enqueue([pscustomobject]@{ Kind = $event.SourceEventArgs.ChangeType.ToString(); Path = $path; At = Get-Date })
                } catch { }
            } | Out-Null
        }
        $State.FileWatcher = $watcher
        $State.FileEventSource = $source
    } catch {
        $State.FileWatcher = $null
        [void](Add-AgexUiEvent -State $State -Source "FILES" -Kind "WARNING" -Message "File activity watcher unavailable")
    }
}

function Pump-AgexUiFileWatch {
    param([Parameter(Mandatory)]$State)
    $item = $null
    while ($State.FileQueue.TryDequeue([ref]$item)) {
        $relative = $item.Path
        try {
            $base = ([IO.Path]::GetFullPath($State.Project)).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            $full = [IO.Path]::GetFullPath($item.Path)
            if ($full.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { $relative = $full.Substring($base.Length) }
        } catch { }
        if ($relative -eq ".") { continue }
        $action = switch ($item.Kind) { "Created" { "A" } "Deleted" { "D" } default { "M" } }
        Set-AgexUiFile -State $State -Path $relative -Action $action -Active $true
    }
}

function Stop-AgexUiFileWatch {
    param([Parameter(Mandatory)]$State)
    try {
        if ($State.FileEventSource) {
            foreach ($eventName in @("Created", "Changed", "Deleted", "Renamed")) {
                Unregister-Event -SourceIdentifier ($State.FileEventSource + "-" + $eventName) -ErrorAction SilentlyContinue
            }
        }
        if ($State.FileWatcher) { $State.FileWatcher.EnableRaisingEvents = $false; $State.FileWatcher.Dispose() }
    } catch { }
    $State.FileWatcher = $null
}

function Complete-AgexUiSession {
    param([Parameter(Mandatory)]$State)
    Stop-AgexUiFileWatch -State $State
    Pump-AgexUiFileWatch -State $State
    Update-AgexChanges -State $State
    $snapshot = New-AgexUiRenderSnapshot -State $State
    foreach($agent in @($snapshot.Agents.Values | Where-Object Health -in @('WAITING','WARNING'))) {
        if($agent.LastEvent -gt [datetime]::MinValue) { $State.TimingEvents.Enqueue([pscustomobject]@{Kind='AGENT_WAIT';WorkId=$agent.TaskId;Started=$agent.LastEvent;Ended=Get-Date;Seconds=[math]::Round(((Get-Date)-$agent.LastEvent).TotalSeconds,2)}) }
    }
    $tasks = @($snapshot.Tasks)
    $done = @($tasks | Where-Object Status -in @("DONE", "REPAIRED")).Count
    $failed = @($tasks | Where-Object Status -in @("FAILED", "REPAIR REQUIRED")).Count
    $cancelled = @($tasks | Where-Object Status -eq "CANCELLED").Count
    $running = @($tasks | Where-Object Status -in @("RUNNING", "STARTING", "VERIFYING")).Count
    $pending = @($tasks | Where-Object Status -in @("QUEUED", "WAITING", "BLOCKED")).Count
    $changedFiles = @($snapshot.Files.Values | ForEach-Object Path) + @($snapshot.GitChanges | ForEach-Object Path)
    $agentRuns = 0
    if ($script:runtime -and $script:runtime.Executions -and $State.WorkId) { $agentRuns = @($script:runtime.Executions.ToArray() | Where-Object { $_.RequestId -and ([string]$_.RequestId).StartsWith([string]$State.WorkId) }).Count }
    $State.Summary = [pscustomobject]@{ UserSlices = $tasks.Count; InternalTasks = 0; TotalAssignments = [math]::Max($agentRuns, [int]$snapshot.AssignmentCount); Completed = $done; Failed = $failed; Cancelled = $cancelled; Running = $running; Pending = $pending; Verification = if ($running) { "IN PROGRESS" } elseif ($failed) { "FAILED / REPAIR REQUIRED" } else { "RECORDED" }; ChangedFiles = @($changedFiles | Where-Object { $_ } | Sort-Object -Unique) }
    $stageRows = @($snapshot.StageTimes)
    for ($i = 0; $i -lt $stageRows.Count; $i++) {
        $endAt = if ($i + 1 -lt $stageRows.Count) { $stageRows[$i+1].At } else { Get-Date }
        $stageRows[$i] | Add-Member -NotePropertyName DurationSeconds -NotePropertyValue ([math]::Round(($endAt - $stageRows[$i].At).TotalSeconds,2)) -Force
    }
    $stageTiming = (@($stageRows | ForEach-Object { "{0}={1}s" -f $_.Stage, $_.DurationSeconds }) -join "; ")
    $State.Summary | Add-Member -NotePropertyName StageTiming -NotePropertyValue $stageTiming -Force
    $timing = @($State.TimingEvents.ToArray())
    $State.Summary | Add-Member -NotePropertyName TimingBreakdown -NotePropertyValue ([pscustomobject]@{
        LeaderPlanningSeconds=[math]::Round([double](@($timing | Where-Object Kind -eq 'LEADER' | Measure-Object Seconds -Sum).Sum),2)
        TaskExecutionSeconds=[math]::Round([double](@($tasks | ForEach-Object { if($_.End -gt $_.Started -and $_.Started -gt [datetime]::MinValue) { ($_.End-$_.Started).TotalSeconds } } | Measure-Object -Sum).Sum),2)
        VerificationSeconds=[math]::Round([double](@($timing | Where-Object Kind -eq 'VERIFY' | Measure-Object Seconds -Sum).Sum),2)
        AgentWaitingSeconds=[math]::Round([double](@($timing | Where-Object Kind -eq 'AGENT_WAIT' | Measure-Object Seconds -Sum).Sum),2)
        MailboxWaitSeconds=[math]::Round([double](@($timing | Where-Object Kind -eq 'MAILBOX' | Measure-Object Seconds -Sum).Sum),2)
        StageSeconds=$stageTiming
    }) -Force
    # The request outcome (Complete-AgexRequestOutcome) is authoritative. Without
    # one, derive a status from task counts; never report PARTIAL unless at least
    # one task succeeded and at least one failed or was cancelled.
    $State.Status = if ($State.Outcome -and $State.Outcome.Status) { [string]$State.Outcome.Status }
        elseif ($State.GoalStatus -eq "CANCELLED" -or ($cancelled -gt 0 -and $done -eq 0)) { "CANCELLED" }
        elseif ($State.GoalStatus -in @("COMPLETE", "COMPLETE_WITH_FALLBACK")) { [string]$State.GoalStatus }
        elseif ($done -gt 0 -and ($failed + $cancelled) -gt 0) { "PARTIAL" }
        elseif ($tasks.Count -eq 0) { if ($State.GoalStatus -in @("START_FAILED", "FAILED")) { [string]$State.GoalStatus } else { "START_FAILED" } }
        elseif ($done -eq 0) { "FAILED" }
        else { "UNVERIFIED" }
    $State.CurrentAction = $null
    $State.CurrentCommand = $null
    $sessionMessage = if ($tasks.Count -eq 0 -and $State.Status -like 'COMPLETE*') { "Finished: the leader answered directly." } elseif ($tasks.Count -eq 0) { "Finished: no tasks were started." } else { "Finished: {0} of {1} tasks done, {2} failed{3}." -f $done, $tasks.Count, $failed, $(if ($cancelled) { ", $cancelled cancelled" } else { "" }) }
    [void](Add-AgexUiEvent -State $State -Source "AGEX-SESSION" -Kind "SESSION" -Message $sessionMessage -Status $State.Status)
}

function Update-AgexChanges {
    param($State, [switch]$IncludeDiff)
    try {
        $inside = & git -C $State.Project rev-parse --is-inside-work-tree 2>$null
        if ($LASTEXITCODE -ne 0 -or $inside -ne 'true') { $State.GitChanges=$null; return }
        $status=@(& git -C $State.Project -c core.quotePath=false status --porcelain=v1 --untracked-files=normal 2>$null)
        if ($LASTEXITCODE -ne 0) { return }
        $stats=@{}
        foreach ($line in @(& git -C $State.Project diff --numstat 2>$null)) {
            $parts=$line -split "`t",3
            if ($parts.Count -eq 3) { $stats[$parts[2]]="+$($parts[0]) -$($parts[1])" }
        }
        $observed=@(foreach ($line in $status) {
            if ($line.Length -lt 4) { continue }
            $code=$line.Substring(0,2);$path=$line.Substring(3)
            $action=if ($code -match 'D') { 'D' } elseif ($code -match 'A|\?') { 'A' } else { 'M' }
            [pscustomobject]@{Action=$action;Path=$path;Lines=$stats[$path]}
        })
        $State.GitChanges=$observed
        if ($IncludeDiff) {
            $State.DiffLines=@(& git -C $State.Project --no-pager diff --no-ext-diff --no-color --unified=2 2>$null | Select-Object -First 120)
            $State.DiffLines+=@(& git -C $State.Project --no-pager diff --cached --no-ext-diff --no-color --unified=2 2>$null | Select-Object -First 40)
            if (-not $State.DiffLines.Count) { $State.DiffLines=@('No tracked diff. Added/untracked files appear in :changes.') }
        }
    } catch { }
}

# ------------------------------------------------------------ draft editor

function New-AgexEditor {
    $editor = @{ Lines = [System.Collections.Generic.List[string]]::new(); Line = 0; Column = 0; Preferred = -1; Scroll = 0; MaxChars = 262144; Truncated = $false; Version = 0 }
    [void]$editor.Lines.Add("")
    $editor
}

function Get-AgexEditorText { param([Parameter(Mandatory)]$Editor) $Editor.Lines -join "`n" }

function Get-AgexEditorCharCount {
    param([Parameter(Mandatory)]$Editor)
    $count = [math]::Max(0, $Editor.Lines.Count - 1)
    foreach ($line in $Editor.Lines) { $count += $line.Length }
    $count
}

function Clear-AgexEditor {
    param([Parameter(Mandatory)]$Editor)
    $Editor.Lines.Clear(); [void]$Editor.Lines.Add("")
    $Editor.Line = 0; $Editor.Column = 0; $Editor.Preferred = -1; $Editor.Scroll = 0; $Editor.Truncated = $false; $Editor.Version++
}

function Add-AgexEditorText {
    param([Parameter(Mandatory)]$Editor, [AllowEmptyString()][string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return }
    $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n").Replace("`t", "    ")
    $available = $Editor.MaxChars - (Get-AgexEditorCharCount -Editor $Editor)
    if ($available -le 0) { $Editor.Truncated = $true; return }
    if ($normalized.Length -gt $available) { $normalized = $normalized.Substring(0, $available); $Editor.Truncated = $true }
    $current = $Editor.Lines[$Editor.Line]
    $before = $current.Substring(0, $Editor.Column)
    $after = $current.Substring($Editor.Column)
    $parts = $normalized.Split([char]"`n")
    if ($parts.Count -eq 1) {
        $Editor.Lines[$Editor.Line] = $before + $parts[0] + $after
        $Editor.Column += $parts[0].Length
    } else {
        $Editor.Lines[$Editor.Line] = $before + $parts[0]
        for ($i = 1; $i -lt $parts.Count; $i++) {
            $value = if ($i -eq $parts.Count - 1) { $parts[$i] + $after } else { $parts[$i] }
            $Editor.Lines.Insert($Editor.Line + $i, $value)
        }
        $Editor.Line += $parts.Count - 1
        $Editor.Column = $parts[$parts.Count - 1].Length
    }
    $Editor.Preferred = -1; $Editor.Version++
}

function Remove-AgexEditorText {
    param([Parameter(Mandatory)]$Editor, [switch]$Forward, [switch]$Word)
    if ($Forward) {
        $line = $Editor.Lines[$Editor.Line]
        if ($Editor.Column -lt $line.Length) { $Editor.Lines[$Editor.Line] = $line.Remove($Editor.Column, 1) }
        elseif ($Editor.Line -lt $Editor.Lines.Count - 1) { $Editor.Lines[$Editor.Line] = $line + $Editor.Lines[$Editor.Line + 1]; $Editor.Lines.RemoveAt($Editor.Line + 1) }
    } else {
        if ($Editor.Column -gt 0) {
            $line = $Editor.Lines[$Editor.Line]
            $start = $Editor.Column - 1
            if ($Word) {
                while ($start -gt 0 -and [char]::IsWhiteSpace($line[$start])) { $start-- }
                while ($start -gt 0 -and -not [char]::IsWhiteSpace($line[$start - 1])) { $start-- }
            }
            $Editor.Lines[$Editor.Line] = $line.Remove($start, $Editor.Column - $start)
            $Editor.Column = $start
        } elseif ($Editor.Line -gt 0) {
            $previous = $Editor.Lines[$Editor.Line - 1]
            $Editor.Lines[$Editor.Line - 1] = $previous + $Editor.Lines[$Editor.Line]
            $Editor.Lines.RemoveAt($Editor.Line)
            $Editor.Line--
            $Editor.Column = $previous.Length
        }
    }
    $Editor.Preferred = -1; $Editor.Truncated = $false; $Editor.Version++
}

function Move-AgexEditorCursor {
    param([Parameter(Mandatory)]$Editor, [Parameter(Mandatory)][ValidateSet("Left", "Right", "Up", "Down", "Home", "End", "WordLeft", "WordRight", "Top", "Bottom")][string]$Direction)
    $line = $Editor.Lines[$Editor.Line]
    switch ($Direction) {
        "Left" { if ($Editor.Column -gt 0) { $Editor.Column-- } elseif ($Editor.Line -gt 0) { $Editor.Line--; $Editor.Column = $Editor.Lines[$Editor.Line].Length }; $Editor.Preferred = -1 }
        "Right" { if ($Editor.Column -lt $line.Length) { $Editor.Column++ } elseif ($Editor.Line -lt $Editor.Lines.Count - 1) { $Editor.Line++; $Editor.Column = 0 }; $Editor.Preferred = -1 }
        "Up" { if ($Editor.Preferred -lt 0) { $Editor.Preferred = $Editor.Column }; if ($Editor.Line -gt 0) { $Editor.Line--; $Editor.Column = [math]::Min($Editor.Preferred, $Editor.Lines[$Editor.Line].Length) } }
        "Down" { if ($Editor.Preferred -lt 0) { $Editor.Preferred = $Editor.Column }; if ($Editor.Line -lt $Editor.Lines.Count - 1) { $Editor.Line++; $Editor.Column = [math]::Min($Editor.Preferred, $Editor.Lines[$Editor.Line].Length) } }
        "Home" { $Editor.Column = 0; $Editor.Preferred = -1 }
        "End" { $Editor.Column = $line.Length; $Editor.Preferred = -1 }
        "Top" { $Editor.Line = 0; $Editor.Column = 0; $Editor.Preferred = -1 }
        "Bottom" { $Editor.Line = $Editor.Lines.Count - 1; $Editor.Column = $Editor.Lines[$Editor.Line].Length; $Editor.Preferred = -1 }
        "WordLeft" {
            while ($Editor.Column -gt 0 -and [char]::IsWhiteSpace($line[$Editor.Column - 1])) { $Editor.Column-- }
            while ($Editor.Column -gt 0 -and -not [char]::IsWhiteSpace($line[$Editor.Column - 1])) { $Editor.Column-- }
            $Editor.Preferred = -1
        }
        "WordRight" {
            while ($Editor.Column -lt $line.Length -and [char]::IsWhiteSpace($line[$Editor.Column])) { $Editor.Column++ }
            while ($Editor.Column -lt $line.Length -and -not [char]::IsWhiteSpace($line[$Editor.Column])) { $Editor.Column++ }
            $Editor.Preferred = -1
        }
    }
    $Editor.Version++
}

# ------------------------------------------------------------------ AGEX screen
# Single renderer for the interactive session. Only the UI thread calls
# Write-AgexScreen; background work publishes state and never writes to the
# console. Rows are diffed against the previous frame so only changed lines
# are rewritten: no clear-and-repaint, no blanking, no cursor jumps.

function New-AgexScreen {
    @{ Width = 0; Height = 0; Prev = @{}; ForceFull = $true; Unicode = $true; LastRowCount = 0 }
}

function Get-AgexGlyphs {
    param([bool]$Unicode = $true)
    if ($Unicode) {
        return @{ Ok = [string][char]0x2713; Fail = [string][char]0x2717; Run = [string][char]0x2192; Dot = [string][char]0x2022; Warn = [string][char]0x26A0; Rule = [string][char]0x2500; Full = [string][char]0x2588; Empty = [string][char]0x2591; Prompt = [string][char]0x203A; Live = [string][char]0x25CF; Ellipsis = [string][char]0x2026 }
    }
    @{ Ok = "+"; Fail = "x"; Run = ">"; Dot = "-"; Warn = "!"; Rule = "-"; Full = "#"; Empty = "."; Prompt = ">"; Live = "*"; Ellipsis = "..." }
}

function Split-AgexText {
    # Word-wrap text to a width. Keeps explicit line breaks; hard-breaks long words.
    param([AllowEmptyString()][string]$Text, [int]$Width)
    $Width = [math]::Max(4, $Width)
    $out = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Text) { $Text = "" }
    foreach ($paragraph in ($Text.Replace("`r", "") -split "`n")) {
        $line = ""
        foreach ($word in ($paragraph -split ' ')) {
            while ($word.Length -gt $Width) {
                if ($line) { [void]$out.Add($line); $line = "" }
                [void]$out.Add($word.Substring(0, $Width))
                $word = $word.Substring($Width)
            }
            if (-not $line) { $line = $word }
            elseif (($line.Length + 1 + $word.Length) -le $Width) { $line += " " + $word }
            else { [void]$out.Add($line); $line = $word }
        }
        [void]$out.Add($line)
    }
    @($out)
}

function Format-AgexFixedWidth {
    param([AllowEmptyString()][string]$Text, [int]$Width, [string]$Ellipsis = "...")
    $value = if ($null -eq $Text) { "" } else { ($Text -replace "[`r`n`t]", " ") }
    if ($value.Length -gt $Width) {
        if ($Width -le $Ellipsis.Length) { return $value.Substring(0, $Width) }
        return $value.Substring(0, $Width - $Ellipsis.Length) + $Ellipsis
    }
    $value.PadRight($Width)
}

function Get-AgexStatusInfo {
    param([string]$Status)
    switch ($Status) {
        "RUNNING" { return @{ Label = "Running"; Color = "Cyan"; Kind = "Run" } }
        "CANCELLING" { return @{ Label = "Cancelling"; Color = "Yellow"; Kind = "Warn" } }
        "COMPLETE" { return @{ Label = "Complete"; Color = "Green"; Kind = "Ok" } }
        "DONE" { return @{ Label = "Complete"; Color = "Green"; Kind = "Ok" } }
        "COMPLETE_WITH_FALLBACK" { return @{ Label = "Complete (recovered)"; Color = "Green"; Kind = "Ok" } }
        "PARTIAL" { return @{ Label = "Partly done"; Color = "Yellow"; Kind = "Warn" } }
        "UNVERIFIED" { return @{ Label = "Finished, not verified"; Color = "Yellow"; Kind = "Warn" } }
        "FAILED" { return @{ Label = "Failed"; Color = "Red"; Kind = "Fail" } }
        "START_FAILED" { return @{ Label = "Could not start"; Color = "Red"; Kind = "Fail" } }
        "CANCELLED" { return @{ Label = "Cancelled"; Color = "Yellow"; Kind = "Warn" } }
        default { return @{ Label = "Ready"; Color = "Gray"; Kind = "Dot" } }
    }
}

function Get-AgexEditorLayout {
    param([Parameter(Mandatory)]$Editor, [int]$Width, [int]$Rows, [bool]$Unicode = $true)
    $g = Get-AgexGlyphs -Unicode $Unicode
    $Width = [math]::Max(4, $Width)
    $Rows = [math]::Max(1, $Rows)
    $visual = [System.Collections.Generic.List[object]]::new()
    $cursorVisual = 0
    for ($i = 0; $i -lt $Editor.Lines.Count; $i++) {
        $line = [string]$Editor.Lines[$i]
        $chunks = if ($i -eq $Editor.Line) { [math]::Floor($line.Length / $Width) + 1 } else { [math]::Max(1, [math]::Ceiling($line.Length / $Width)) }
        for ($c = 0; $c -lt $chunks; $c++) {
            $offset = $c * $Width
            $length = [math]::Max(0, [math]::Min($Width, $line.Length - $offset))
            if ($i -eq $Editor.Line -and $Editor.Column -ge $offset -and $Editor.Column -lt ($offset + $Width)) { $cursorVisual = $visual.Count }
            [void]$visual.Add(@{ Logical = $i; Offset = $offset; Text = $(if ($length -gt 0) { $line.Substring($offset, $length) } else { "" }) })
        }
    }
    if ($cursorVisual -lt $Editor.Scroll) { $Editor.Scroll = $cursorVisual }
    if ($cursorVisual -ge $Editor.Scroll + $Rows) { $Editor.Scroll = $cursorVisual - $Rows + 1 }
    $Editor.Scroll = [math]::Max(0, [math]::Min($Editor.Scroll, [math]::Max(0, $visual.Count - $Rows)))
    $empty = ($Editor.Lines.Count -eq 1 -and $Editor.Lines[0].Length -eq 0)
    $out = [System.Collections.Generic.List[object]]::new()
    for ($r = 0; $r -lt $Rows; $r++) {
        $index = $Editor.Scroll + $r
        if ($empty -and $r -eq 0) { [void]$out.Add(@{ Text = "$($g.Prompt) Type your request here..."; Color = "DarkGray" }); continue }
        if ($index -lt $visual.Count) {
            $prefix = if ($index -eq 0) { "$($g.Prompt) " } else { "  " }
            [void]$out.Add(@{ Text = $prefix + $visual[$index].Text; Color = "White" })
        } else { [void]$out.Add(@{ Text = ""; Color = "Gray" }) }
    }
    $cursor = $visual[$cursorVisual]
    [pscustomobject]@{ Rows = @($out); CursorRow = $cursorVisual - $Editor.Scroll; CursorCol = 2 + ($Editor.Column - $cursor.Offset); VisualCount = $visual.Count; LineCount = $Editor.Lines.Count }
}

function Get-AgexActivityRows {
    param([Parameter(Mandatory)]$State, [int]$Count, [int]$Width, [hashtable]$Glyphs)
    $rows = [System.Collections.Generic.List[object]]::new()
    if ($Count -le 0) { return @() }
    $events = @($State.Events | Where-Object { $_.Source -eq 'AGEX' -or $_.Kind -in @('SESSION') } | Select-Object -Last $Count)
    foreach ($item in $events) {
        $glyph = $Glyphs.Dot; $color = "Gray"
        switch ($item.Kind) {
            "PASS" { $glyph = $Glyphs.Ok; $color = "Green" }
            "FAIL" { $glyph = $Glyphs.Fail; $color = "Red" }
            "START" { $glyph = $Glyphs.Run; $color = "Cyan" }
            "WARNING" { $glyph = $Glyphs.Warn; $color = "Yellow" }
            "FALLBACK" { $glyph = $Glyphs.Warn; $color = "Yellow" }
        }
        [void]$rows.Add(@{ Text = ("{0} {1} {2}" -f $item.At.ToString("HH:mm"), $glyph, $item.Message); Color = $color })
    }
    @($rows)
}

function Get-AgexOutcomeRows {
    param([Parameter(Mandatory)]$State, [int]$Width, [int]$MaxRows, [hashtable]$Glyphs)
    $outcome = $State.Outcome
    $rows = [System.Collections.Generic.List[object]]::new()
    if (-not $outcome) { return @() }
    $info = Get-AgexStatusInfo -Status $outcome.Status
    [void]$rows.Add(@{ Text = ("{0} {1}" -f $Glyphs[$info.Kind], $outcome.Headline); Color = $info.Color })
    $failing = $outcome.Status -in @('FAILED', 'START_FAILED', 'PARTIAL', 'UNVERIFIED')
    $body = [System.Collections.Generic.List[string]]::new()
    if ($outcome.Reason) { foreach ($line in (Split-AgexText -Text $outcome.Reason -Width ($Width - 2))) { [void]$body.Add($line) } }
    foreach ($line in @($outcome.WhatHappened)) { foreach ($wrapped in (Split-AgexText -Text ("What AGEX did: " + $line) -Width ($Width - 2))) { [void]$body.Add($wrapped) } }
    if ($failing -and $outcome.PrimaryFailure -and ([string]$outcome.Reason).IndexOf([string]$outcome.PrimaryFailure) -lt 0) { foreach ($wrapped in (Split-AgexText -Text ("Why: " + $outcome.PrimaryFailure) -Width ($Width - 2))) { [void]$body.Add($wrapped) } }
    foreach ($result in @($outcome.TaskResults)) { foreach ($wrapped in (Split-AgexText -Text $result -Width ($Width - 2))) { [void]$body.Add($wrapped) } }
    $minutes = [math]::Floor([double]$outcome.DurationSeconds / 60); $seconds = [int]([double]$outcome.DurationSeconds % 60)
    $stats = "Tasks {0} | Done {1} | Failed {2}" -f $outcome.Tasks, $outcome.Done, $outcome.Failed
    if ($outcome.Cancelled) { $stats += " | Cancelled $($outcome.Cancelled)" }
    if ($outcome.Executors) { $stats += " | Codex runs {0} | Antigravity runs {1}" -f $outcome.Executors.Codex, $outcome.Executors.Antigravity }
    $stats += " | {0}m {1:00}s" -f $minutes, $seconds
    $hint = if ($outcome.Status -in @('COMPLETE', 'COMPLETE_WITH_FALLBACK', 'DONE')) { "Full result: :result   What ran: :details" } elseif ($outcome.Status -eq 'CANCELLED') { "Run it again: :retry" } else { "Try: :retry   :agy   :codex   :details" }
    $bodyRoom = [math]::Max(0, $MaxRows - 3)
    $shown = 0
    foreach ($line in $body) {
        if ($shown -ge $bodyRoom) { break }
        if ($shown -eq $bodyRoom - 1 -and $body.Count -gt $bodyRoom) { [void]$rows.Add(@{ Text = "  ... more: type :result"; Color = "DarkGray" }); $shown++; break }
        [void]$rows.Add(@{ Text = "  " + $line; Color = "Gray" }); $shown++
    }
    [void]$rows.Add(@{ Text = "  " + $stats; Color = "DarkGray" })
    [void]$rows.Add(@{ Text = "  " + $hint; Color = $(if ($failing) { "Yellow" } else { "DarkGray" }) })
    @($rows)
}

function Get-AgexCurrentTaskText {
    param([Parameter(Mandatory)]$State)
    if ($State.Status -eq 'CANCELLING') { return "Cancelling the current request..." }
    $active = @($State.Tasks | Where-Object Status -in @('STARTING', 'RUNNING', 'VERIFYING', 'WAITING'))
    if ($active.Count) {
        $first = $active[0]
        $label = if ($first.Summary) { $first.Summary } else { $first.Id }
        $text = if ($first.Status -eq 'VERIFYING') { "Checking the result of: $label" } else { "$($first.Agent): $label" }
        if ($active.Count -gt 1) { $text += " (+$($active.Count - 1) more)" }
        return $text
    }
    if ($State.Status -eq 'RUNNING') {
        switch ($State.AcceptanceStage) {
            'Planning' { return "$($State.ResolvedLeader) is planning your request..." }
            'Reconciling' { return "$($State.ResolvedLeader) is reviewing the results..." }
            'Verifying' { return "Checking results..." }
            default { return "Working..." }
        }
    }
    "Waiting for your request."
}

function Get-AgexScreenFrame {
    param(
        [Parameter(Mandatory)]$State,
        $Runtime,
        [Parameter(Mandatory)]$Editor,
        [Parameter(Mandatory)]$View,
        $Message,
        $Confirm,
        [int]$Width = 100,
        [int]$Height = 30
    )
    $unicode = [bool]$State.Unicode
    $g = Get-AgexGlyphs -Unicode $unicode
    $W = [math]::Max(20, $Width) - 1
    $rowsAvailable = [math]::Max(6, $Height - 1)
    $compact = $Height -lt 18 -or $Width -lt 50
    $rows = [System.Collections.Generic.List[object]]::new()
    $status = if ($State.Status -in @('IDLE', '', $null)) { 'IDLE' } else { [string]$State.Status }
    $info = Get-AgexStatusInfo -Status $status
    $elapsed = ""
    if ($status -in @('RUNNING', 'CANCELLING') -and $State.RequestStarted -and $State.RequestStarted -ne [datetime]::MinValue) {
        $span = (Get-Date) - $State.RequestStarted
        $elapsed = " {0:00}:{1:00}" -f [math]::Floor($span.TotalMinutes), $span.Seconds
    }
    $statusText = "{0} {1}{2}" -f $(if ($status -in @('RUNNING', 'CANCELLING')) { $g.Live } else { $g[$info.Kind] }), $info.Label, $elapsed
    $codexHealth = Get-AgexAgentHealth -Runtime $Runtime -Agent Codex
    $agyHealth = Get-AgexAgentHealth -Runtime $Runtime -Agent Antigravity
    $mark = { param($health) if (-not $health.Checked) { "$($g.Dot) not checked" } elseif ($health.Healthy) { "$($g.Ok) ready" } else { "$($g.Warn) unavailable" } }
    $workers = @($State.Tasks | Where-Object Status -in @('STARTING', 'RUNNING', 'VERIFYING', 'WAITING')).Count
    $agentsColor = if ($codexHealth.Healthy -and $agyHealth.Healthy) { "Gray" } elseif ($codexHealth.Healthy -or $agyHealth.Healthy) { "Yellow" } else { "Red" }
    $rule = $g.Rule * $W
    if ($compact) {
        [void]$rows.Add(@{ Text = ("AGEX | {0} | {1}" -f $State.ProjectName, $statusText); Color = $info.Color })
        [void]$rows.Add(@{ Text = ("Codex {0}  Antigravity {1}  Workers {2}/2" -f (& $mark $codexHealth), (& $mark $agyHealth), $workers); Color = $agentsColor })
        [void]$rows.Add(@{ Text = $rule; Color = "DarkGray" })
    } else {
        [void]$rows.Add(@{ Text = "AGEX AI CONTROL CENTER"; Color = "Cyan" })
        [void]$rows.Add(@{ Text = ("Project: {0}    Status: {1}" -f $State.ProjectName, $statusText); Color = $info.Color })
        [void]$rows.Add(@{ Text = ("Leader: {0}    Workload: AGY {1}% / Codex {2}%" -f $State.ResolvedLeader, $State.AntigravityShare, $State.CodexShare); Color = "Gray" })
        [void]$rows.Add(@{ Text = ("Codex {0}    Antigravity {1}    Workers {2}/2" -f (& $mark $codexHealth), (& $mark $agyHealth), $workers); Color = $agentsColor })
        [void]$rows.Add(@{ Text = $rule; Color = "DarkGray" })
    }
    $editorRows = if ($compact) { 2 } else { 4 }
    $footerRows = 1 + $editorRows + 1
    $mainRows = [math]::Max(1, $rowsAvailable - $rows.Count - $footerRows)
    $panel = [System.Collections.Generic.List[object]]::new()
    if ($View.Name -and $View.Name -ne "NORMAL") {
        $wrapped = [System.Collections.Generic.List[string]]::new()
        foreach ($line in @($View.Lines)) { foreach ($piece in (Split-AgexText -Text ([string]$line) -Width $W)) { [void]$wrapped.Add($piece) } }
        $bodyRows = [math]::Max(1, $mainRows - 1)
        $maxScroll = [math]::Max(0, $wrapped.Count - $bodyRows)
        if ($View.Scroll -gt $maxScroll) { $View.Scroll = $maxScroll }
        $position = if ($wrapped.Count -gt $bodyRows) { "  lines {0}-{1} of {2}, PgUp/PgDn scroll" -f ($View.Scroll + 1), [math]::Min($wrapped.Count, $View.Scroll + $bodyRows), $wrapped.Count } else { "" }
        [void]$panel.Add(@{ Text = ("{0}  (Esc to go back){1}" -f $View.Title, $position); Color = "Cyan" })
        for ($i = $View.Scroll; $i -lt [math]::Min($wrapped.Count, $View.Scroll + $bodyRows); $i++) { [void]$panel.Add(@{ Text = $wrapped[$i]; Color = "Gray" }) }
    } else {
        $running = $status -in @('RUNNING', 'CANCELLING')
        if ($State.Outcome -and -not $running) {
            $outcomeBudget = [math]::Max(3, [math]::Min($mainRows - 2, [math]::Floor($mainRows * 0.7)))
            foreach ($row in (Get-AgexOutcomeRows -State $State -Width $W -MaxRows $outcomeBudget -Glyphs $g)) { [void]$panel.Add($row) }
        } else {
            [void]$panel.Add(@{ Text = "Now: " + (Get-AgexCurrentTaskText -State $State); Color = $(if ($running) { "Cyan" } else { "Gray" }) })
            $total = @($State.Tasks).Count
            if ($total -gt 0) {
                $done = @($State.Tasks | Where-Object Status -in @('DONE', 'REPAIRED')).Count
                $failed = @($State.Tasks | Where-Object Status -in @('FAILED', 'REPAIR REQUIRED')).Count
                $barWidth = [math]::Max(10, [math]::Min(30, $W - 40))
                $filled = [math]::Round($barWidth * ($done / [double]$total))
                $bar = ($g.Full * $filled) + ($g.Empty * ($barWidth - $filled))
                $progress = "Progress: {0} {1}/{2} tasks done" -f $bar, $done, $total
                if ($failed) { $progress += " ($failed failed)" }
                [void]$panel.Add(@{ Text = $progress; Color = $(if ($failed) { "Yellow" } else { "Cyan" }) })
            }
        }
        $remaining = $mainRows - $panel.Count
        if ($remaining -ge 3) {
            [void]$panel.Add(@{ Text = ""; Color = "Gray" })
            [void]$panel.Add(@{ Text = "Activity"; Color = "DarkGray" })
            $activity = @(Get-AgexActivityRows -State $State -Count ($remaining - 2) -Width $W -Glyphs $g)
            if (-not $activity.Count) { [void]$panel.Add(@{ Text = "  Nothing yet."; Color = "DarkGray" }) }
            foreach ($row in $activity) { [void]$panel.Add($row) }
        }
    }
    for ($i = 0; $i -lt $mainRows; $i++) {
        if ($i -lt $panel.Count) { [void]$rows.Add($panel[$i]) } else { [void]$rows.Add(@{ Text = ""; Color = "Gray" }) }
    }
    $layout = Get-AgexEditorLayout -Editor $Editor -Width ($W - 2) -Rows $editorRows -Unicode $unicode
    $label = " Your request "
    if ($layout.LineCount -gt 1 -or $layout.VisualCount -gt $editorRows) { $label = " Your request ({0} lines) " -f $layout.LineCount }
    [void]$rows.Add(@{ Text = ($g.Rule * 2) + $label + ($g.Rule * [math]::Max(0, $W - 2 - $label.Length)); Color = "DarkGray" })
    $editorTop = $rows.Count
    foreach ($row in $layout.Rows) { [void]$rows.Add($row) }
    if ($Confirm) { [void]$rows.Add(@{ Text = $Confirm.Text; Color = "Yellow" }) }
    elseif ($Message -and $Message.Text) { [void]$rows.Add(@{ Text = $Message.Text; Color = $Message.Color }) }
    else { [void]$rows.Add(@{ Text = "Enter new line | Ctrl+Enter send | :help | Ctrl+C cancel"; Color = "DarkGray" }) }
    [pscustomobject]@{ Rows = @($rows); CursorRow = $editorTop + $layout.CursorRow; CursorCol = [math]::Min($W - 1, $layout.CursorCol); Ellipsis = $g.Ellipsis }
}

function Write-AgexScreen {
    param([Parameter(Mandatory)]$Screen, [Parameter(Mandatory)]$Frame)
    $width = [math]::Max(1, $Screen.Width - 1)
    $count = $Frame.Rows.Count
    $count = [math]::Min($count, [Console]::BufferHeight)
    for ($i = 0; $i -lt $count; $i++) {
        $row = $Frame.Rows[$i]
        $text = Format-AgexFixedWidth -Text $row.Text -Width $width -Ellipsis $Frame.Ellipsis
        $key = "$($row.Color)|$text"
        if ($Screen.Prev.ContainsKey($i) -and $Screen.Prev[$i] -ceq $key) { continue }
        [Console]::SetCursorPosition(0, $i)
        try { [Console]::ForegroundColor = [ConsoleColor]$row.Color } catch { [Console]::ForegroundColor = [ConsoleColor]::Gray }
        [Console]::Write($text)
        $Screen.Prev[$i] = $key
    }
    $bufferRows = [Console]::BufferHeight
    for ($i = $count; $i -lt [math]::Min($Screen.LastRowCount, $bufferRows); $i++) {
        [Console]::SetCursorPosition(0, $i); [Console]::Write("".PadRight($width)); $Screen.Prev.Remove($i)
    }
    $Screen.LastRowCount = $count
    [Console]::ForegroundColor = [ConsoleColor]::Gray
    [Console]::SetCursorPosition([math]::Max(0, [math]::Min($width - 1, $Frame.CursorCol)), [math]::Max(0, [math]::Min($count - 1, $Frame.CursorRow)))
    if (-not [Console]::CursorVisible) { [Console]::CursorVisible = $true }
}
