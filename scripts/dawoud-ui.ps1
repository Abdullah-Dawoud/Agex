# AGEX observability only. It owns terminal state and rendering, never execution.

function Assert-DawoudUiStateSchema {
    param([Parameter(Mandatory)]$State)
    $required = @('AcceptanceStage','AssignmentCount','UiUpdates','UiUpdateClock','UiAppliedSequence','LastUiPublicationAt','StageTimes','TimingEvents','LastEvidenceAt','Tasks','Agents','Chat','Status','GoalStatus')
    $actual = @($State.PSObject.Properties.Name)
    $missing = @($required | Where-Object { $_ -notin $actual })
    if ($missing.Count) { throw "AGEX session state schema missing: $($missing -join ', ')" }
    $State
}

function New-DawoudUiState {
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
        GoalStatus = "PARTIAL"
        Chat = [System.Collections.Generic.List[object]]::new()
        Tasks = [System.Collections.Generic.List[object]]::new()
        UiUpdates = [System.Collections.Concurrent.ConcurrentQueue[object]]::new()
        UiUpdateClock = [hashtable]::Synchronized(@{ Value = 0 })
        UiAppliedSequence = 0L
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
    Assert-DawoudUiStateSchema -State $state
}

function Get-DawoudUiGlyph {
    param([Parameter(Mandatory)][string]$Status, [bool]$Unicode = $true)
    if ($Unicode) {
        $codePoint = switch ($Status) {
            "RUNNING" { 0x25CF }
            "STARTING" { 0x25D0 }
            "WAITING" { 0x25D0 }
            "RETRYING" { 0x21BB }
            "DONE" { 0x2713 }
            "WARNING" { 0x25B2 }
            "FAILED" { 0x2715 }
            "CANCELLED" { 0x25A0 }
            default { 0x25CB }
        }
        return [string][char]$codePoint
    }
    switch ($Status) {
        "RUNNING" { return ">" }
        "STARTING" { return ">" }
        "WAITING" { return "~" }
        "RETRYING" { return "R" }
        "DONE" { return "+" }
        "WARNING" { return "!" }
        "FAILED" { return "X" }
        "CANCELLED" { return "-" }
        default { return "o" }
    }
}

function Get-DawoudUiColor {
    param([string]$Status)
    switch ($Status) {
        "RUNNING" { "Green" }
        "STARTING" { "Yellow" }
        "WAITING" { "Yellow" }
        "RETRYING" { "Yellow" }
        "DONE" { "Green" }
        "WARNING" { "Yellow" }
        "FAILED" { "Red" }
        "CANCELLED" { "DarkGray" }
        default { "DarkGray" }
    }
}

function Format-DawoudUiDuration {
    param([datetime]$Start, [datetime]$End)
    if (-not $Start -or $Start -eq [datetime]::MinValue) { return "00:00" }
    $finish = if ($End -and $End -ne [datetime]::MinValue) { $End } else { Get-Date }
    $seconds = [math]::Max(0, [int]($finish - $Start).TotalSeconds)
    "{0:00}:{1:00}" -f [math]::Floor($seconds / 60), ($seconds % 60)
}

function Limit-DawoudUiText {
    param([AllowEmptyString()][string]$Text, [int]$Width)
    if ($Width -le 0) { return "" }
    $value = if ($null -eq $Text) { "" } else { ($Text -replace "\s+", " ").Trim() }
    if ($value.Length -le $Width) { return $value }
    if ($Width -le 3) { return $value.Substring(0, $Width) }
    $value.Substring(0, $Width - 3) + "..."
}

function Add-DawoudUiEvent {
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
        Message = Limit-DawoudUiText -Text (Protect-DawoudTelemetryText -Text $Message) -Width 500
        Status = $Status
        TaskId = $TaskId
    }
    if ($State.Events.Count -ge $State.MaxEvents) { $State.Events.RemoveAt(0) }
    [void]$State.Events.Add($item)
    $State.LastEventAt = $item.At
    $item
}

function Add-DawoudUiTask {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)]$Task)
    [void]$State.Tasks.Add($Task)
    Publish-DawoudUiUpdate -State $State -Kind "TASK" -Value $Task
}

function Publish-DawoudUiUpdate {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Kind, [Parameter(Mandatory)]$Value)
    [System.Threading.Monitor]::Enter($State.UiUpdateClock.SyncRoot)
    try {
        $State.UiUpdateClock.Value++; $sequence = [long]$State.UiUpdateClock.Value
        $snapshot = [pscustomobject]@{}; foreach ($property in $Value.PSObject.Properties) { $snapshot | Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value }
        if ($Kind -eq "TASK") { $snapshot | Add-Member -NotePropertyName UpdatedAt -NotePropertyValue $(if ($Value.UpdatedAt) { $Value.UpdatedAt } else { Get-Date }) -Force }
        while ($State.UiUpdates.Count -ge $State.MaxUiUpdates) { $discard=$null; [void]$State.UiUpdates.TryDequeue([ref]$discard) }
        $State.UiUpdates.Enqueue([pscustomobject]@{ Sequence=$sequence; Kind=$Kind; Value=$snapshot; At=Get-Date })
    } finally { [System.Threading.Monitor]::Exit($State.UiUpdateClock.SyncRoot) }
}

function Set-DawoudUiStage {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Stage)
    if ($State.AcceptanceStage -eq $Stage) { return }
    $State.AcceptanceStage = $Stage
    $item = [pscustomobject]@{ Stage=$Stage; At=Get-Date }
    [void]$State.StageTimes.Add($item)
    Publish-DawoudUiUpdate -State $State -Kind "STAGE" -Value $item
}

function Receive-DawoudUiUpdates {
    param([Parameter(Mandatory)]$State)
    $update = $null
    while ($State.UiUpdates.TryDequeue([ref]$update)) {
        if ($update.Sequence -le $State.UiAppliedSequence) { continue }
        $State.UiAppliedSequence = $update.Sequence
        $State.LastUiPublicationAt = $update.At
        if ($update.Kind -eq "TASK") {
            $current = Find-DawoudUiTask -State $State -TaskId ([string]$update.Value.Id)
            if (-not $current) { [void]$State.Tasks.Add($update.Value) }
            elseif (-not $current.UpdatedAt -or $update.Value.UpdatedAt -ge $current.UpdatedAt) { $current.Status=$update.Value.Status; $current.Agent=$update.Value.Agent; if($current.PSObject.Properties.Name -contains 'UpdatedAt'){$current.UpdatedAt=$update.Value.UpdatedAt}else{$current | Add-Member -NotePropertyName UpdatedAt -NotePropertyValue $update.Value.UpdatedAt} }
        } elseif ($update.Kind -eq "AGENT") {
            $State.Agents[$update.Value.Name] = $update.Value
            if ($State.Status -eq "RUNNING" -and $update.Value.Status -in @("RUNNING","STARTING","WAITING","QUEUED","RECONCILING")) { $State.CurrentAction = $update.Value }
        } elseif ($update.Kind -eq "STAGE") { $State.AcceptanceStage = $update.Value.Stage }
        elseif ($update.Kind -eq "COUNT") { $State.AssignmentCount = [int]$update.Value }
    }
}

function Find-DawoudUiTask {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$TaskId)
    @($State.Tasks | Where-Object Id -eq $TaskId | Select-Object -First 1)
}

function Get-DawoudUiVisibleTasks {
    param([Parameter(Mandatory)]$State, [int]$Limit = 6)
    $selected = [System.Collections.Generic.List[object]]::new()
    foreach ($task in @($State.Tasks | Where-Object Status -in @("STARTING", "RUNNING", "WAITING", "WARNING", "RETRYING", "VERIFYING", "REPAIR REQUIRED", "BLOCKED", "FAILED"))) { [void]$selected.Add($task) }
    foreach ($task in @($State.Tasks | Where-Object Status -eq "QUEUED")) { if ($selected.Count -lt $Limit) { [void]$selected.Add($task) } }
    foreach ($task in @($State.Tasks | Where-Object Status -in @("DONE", "CANCELLED") | Select-Object -Last $Limit)) { if ($selected.Count -lt $Limit) { [void]$selected.Add($task) } }
    @($selected | Select-Object -First $Limit)
}

function Set-DawoudUiTask {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$TaskId, [string]$Status, [string]$Agent, [string]$Reason = "", [string]$ErrorText = "")
    $task = Find-DawoudUiTask -State $State -TaskId $TaskId
    if ($task) {
        if ($Status) { $task.Status = $Status }
        if ($Agent) { $task.Agent = $Agent }
        if ($Reason) { $task.Reason = $Reason }
        if ($ErrorText) { $task.Error = $ErrorText }
        if (($Status -eq "STARTING" -or $Status -eq "RUNNING") -and $task.Started -eq [datetime]::MinValue) { $task.Started = Get-Date }
        if ($Status -in @("DONE", "FAILED", "CANCELLED")) { $task.End = Get-Date }
        if ($task.PSObject.Properties.Name -contains "UpdatedAt") { $task.UpdatedAt = Get-Date } else { $task | Add-Member -NotePropertyName UpdatedAt -NotePropertyValue (Get-Date) }
        Publish-DawoudUiUpdate -State $State -Kind "TASK" -Value $task
    }
    $task
}

function Start-DawoudUiAgent {
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
        Task = Limit-DawoudUiText -Text $TaskText -Width 160; Action = "Starting executor"
        File = ""; PID = 0; Started = $now; End = [datetime]::MinValue
        LastEvent = $now; LastMonitorAt = $now; Model = if ($Model) { $Model } else { "default" }
        Command = $Command; Cwd = $WorkingDirectory; ExitCode = $null; Retry = ""
        StreamEvents = 0; Timeout = "NONE"; ActualPID = 0; DispatchPID = 0; DiagnosticPath = $DiagnosticPath; DiagnosticMarks = @{}
        Health = "OK"; FailureReason = ""
    }
    $State.Agents[$Name] = $agent
    Publish-DawoudUiUpdate -State $State -Kind "AGENT" -Value $agent
    $State.Status = "RUNNING"
    $State.CurrentAction = $agent
    if ($Command) { $State.CurrentCommand = [pscustomobject]@{ Kind = "DISPATCH"; Text = $Command; Status = "RUNNING"; ExitCode = $null; Started = $now; End = [datetime]::MinValue } }
    [void](Add-DawoudUiEvent -State $State -Source $Name -Kind "START" -Message ("Task {0}" -f $TaskId) -Status "STARTING" -TaskId $TaskId)
    $agent
}

function Update-DawoudUiAgent {
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
    if (-not $State.Agents.Contains($Name)) { return }
    $agent = $State.Agents[$Name]
    $now = Get-Date
    if ($EventKind -ne "MONITOR" -and $agent.Health -in @("WAITING", "WARNING") -and $agent.LastEvent -gt [datetime]::MinValue) {
        $State.TimingEvents.Enqueue([pscustomobject]@{Kind='AGENT_WAIT';WorkId=$agent.TaskId;Started=$agent.LastEvent;Ended=$now;Seconds=[math]::Round(($now-$agent.LastEvent).TotalSeconds,2)})
    }
    if ($Status) { $agent.Status = $Status }
    if ($Status -eq "RUNNING" -and $EventKind -ne "MONITOR") { $agent.Health = "OK" }
    if ($Action) { $agent.Action = $Action }
    if ($ProcessId -gt 0) { $agent.PID = $ProcessId }
    if ($Status -eq "RUNNING" -and $agent.PID -gt 0 -and $agent.TaskId) {
        [void](Set-DawoudUiTask -State $State -TaskId $agent.TaskId -Status "RUNNING" -Agent $agent.Executor)
    }
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
    if ($Message) { [void](Add-DawoudUiEvent -State $State -Source $Name -Kind $EventKind -Message $Message -Status $agent.Status -TaskId $agent.TaskId) }
    Publish-DawoudUiUpdate -State $State -Kind "AGENT" -Value $agent
    $agent
}

function Update-DawoudUiDiagnostic {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Name)
    if (-not $State.Agents.Contains($Name)) { return }
    $agent = $State.Agents[$Name]
    if ([string]::IsNullOrWhiteSpace($agent.DiagnosticPath) -or -not (Test-Path -LiteralPath $agent.DiagnosticPath -PathType Leaf)) { return }
    try { $text = [IO.File]::ReadAllText($agent.DiagnosticPath) } catch { return }
    foreach ($mark in @("AGY_PROCESS_STARTED", "STDIN_WRITTEN", "STDIN_FLUSHED", "FIRST_STDOUT_EVENT")) {
        if ($text -match [regex]::Escape($mark) -and -not $agent.DiagnosticMarks.ContainsKey($mark)) {
            $agent.DiagnosticMarks[$mark] = $true
            $kind = switch ($mark) { "AGY_PROCESS_STARTED" { "START" } "FIRST_STDOUT_EVENT" { "ACTION" } default { "INPUT" } }
            $message = switch ($mark) { "AGY_PROCESS_STARTED" { "AGY process started" } "STDIN_WRITTEN" { "AGY input written" } "STDIN_FLUSHED" { "AGY input flushed" } default { "First AGY stream event" } }
            [void](Add-DawoudUiEvent -State $State -Source $Name -Kind $kind -Message $message -Status "RUNNING" -TaskId $agent.TaskId)
            $agent.Action = $message
            $agent.LastEvent = Get-Date
        }
    }
    if ($text -match '(?m)^AGY_PID=(\d+)' -and $agent.PID -ne [int]$Matches[1]) {
        $agent.PID = [int]$Matches[1]
        $agent.ActualPID = $agent.PID
        [void](Add-DawoudUiEvent -State $State -Source $Name -Kind "START" -Message ("Actual AGY PID {0}" -f $agent.PID) -Status "RUNNING" -TaskId $agent.TaskId)
    }
}

function Protect-DawoudUiCommand {
    param([AllowEmptyString()][string]$Text)
    $safe = Protect-DawoudTelemetryText -Text $Text
    $safe = $safe -replace '(?i)(--?(?:password|passwd|token|api[-_]?key|secret)(?:=|\s+))("[^"]*"|\S+)', '$1<redacted>'
    Limit-DawoudUiText -Text $safe -Width 180
}

function Complete-DawoudUiAgent {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Name, [ValidateSet("DONE", "FAILED", "CANCELLED")][string]$Status, [string]$Message = "", [int]$ExitCode = 0, [int]$StreamEvents = -1, [string]$Timeout = "")
    if (-not $State.Agents.Contains($Name)) { return }
    $agent = $State.Agents[$Name]
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
    $kind = if ($Status -eq "DONE") { "PASS" } elseif ($Status -eq "CANCELLED") { "CANCEL" } else { "FAIL" }
    $finalMessage = if ($Message) { $Message } else { $agent.Action }
    [void](Add-DawoudUiEvent -State $State -Source $Name -Kind $kind -Message $finalMessage -Status $Status -TaskId $agent.TaskId)
    $agent
}

function Set-DawoudUiFile {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Path, [ValidateSet("R", "M", "A", "D", "+", "-")][string]$Action = "M", [bool]$Active = $true)
    $clean = Protect-DawoudTelemetryText -Text $Path
    if (-not $State.Files.Contains($clean) -and $State.Files.Count -ge $State.MaxFiles) {
        $oldest = @($State.Files.Values | Sort-Object At | Select-Object -First 1)
        if ($oldest.Count) { [void]$State.Files.Remove([string]$oldest[0].Path) }
    }
    $State.Files[$clean] = [pscustomobject]@{ Path = $clean; Action = $Action; At = Get-Date; Active = $Active }
    $source = if ($State.CurrentAction) { $State.CurrentAction.Name } else { "FILES" }
    $taskId = if ($State.CurrentAction) { $State.CurrentAction.TaskId } else { "" }
    [void](Add-DawoudUiEvent -State $State -Source $source -Kind "FILE" -Message ("{0} {1}" -f $Action, $clean) -Status "RUNNING" -TaskId $taskId)
}

function Start-DawoudUiFileWatch {
    param([Parameter(Mandatory)]$State)
    try {
        if (-not (Test-Path -LiteralPath $State.Project -PathType Container)) { return }
        $watcher = New-Object System.IO.FileSystemWatcher
        $watcher.Path = $State.Project
        $watcher.IncludeSubdirectories = $true
        $watcher.InternalBufferSize = 16384
        $watcher.NotifyFilter = [IO.NotifyFilters]::FileName -bor [IO.NotifyFilters]::LastWrite -bor [IO.NotifyFilters]::Size -bor [IO.NotifyFilters]::CreationTime
        $watcher.EnableRaisingEvents = $true
        $source = "dawoud-ui-" + ([guid]::NewGuid().ToString("N"))
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
        [void](Add-DawoudUiEvent -State $State -Source "FILES" -Kind "WARNING" -Message "File activity watcher unavailable")
    }
}

function Pump-DawoudUiFileWatch {
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
        Set-DawoudUiFile -State $State -Path $relative -Action $action -Active $true
    }
}

function Stop-DawoudUiFileWatch {
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

function Get-DawoudUiLines {
    param([Parameter(Mandatory)]$State, [int]$Width = 100, [int]$Height = 34)
    $Width = [math]::Max(24, $Width)
    $Height = [math]::Max(12, $Height)
    $rule = if ($State.Unicode) { [string][char]0x2500 } else { "-" }
    $v = if ($State.Unicode) { [string][char]0x2502 } else { "|" }
    $lines = [System.Collections.Generic.List[string]]::new()
    $sessionTime = Format-DawoudUiDuration -Start $State.SessionStart
    $statusGlyph = Get-DawoudUiGlyph -Status $State.Status -Unicode $State.Unicode
    [void]$lines.Add(("AGEX AI CONTROL CENTER  |  {0}  |  SESSION {1}  |  {2} {3}" -f $State.ProjectName, $sessionTime, $statusGlyph, $State.Status))
    [void]$lines.Add(("Leader {0} -> {1}   Target AGY {2}% / Codex {3}%   Models AGY {4} | Codex {5}" -f $State.ConfiguredLeader, $State.ResolvedLeader, $State.AntigravityShare, $State.CodexShare, $State.AntigravityModel, $State.CodexModel))
    [void]$lines.Add(("Goal {0} | Agent messages {1} (:chat)" -f $State.GoalStatus, $State.Chat.Count))
    [void]$lines.Add(($rule * $Width))
    [void]$lines.Add(("ACCEPTANCE: {0} | Tasks {1}/{2} done | Assignments {3}" -f $State.AcceptanceStage, @($State.Tasks | Where-Object Status -eq 'DONE').Count, $State.Tasks.Count, $State.AssignmentCount))

    $compact = $Height -lt 30 -and $State.View -eq "NORMAL"
    $eventCount = [math]::Min($(if ($compact) { 4 } else { 7 }), $State.Events.Count)
    $activity = [System.Collections.Generic.List[string]]::new()
    [void]$activity.Add("LIVE ACTIVITY")
    if ($eventCount -eq 0) { [void]$activity.Add("  Waiting for observable events") }
    else {
        $start = [math]::Max(0, $State.Events.Count - $eventCount)
        for ($i = $start; $i -lt $State.Events.Count; $i++) {
            $e = $State.Events[$i]
            $time = $e.At.ToString("HH:mm:ss")
            [void]$activity.Add(("{0} {1,-14} {2,-9} {3}" -f $time, (Limit-DawoudUiText -Text $e.Source -Width 14), (Limit-DawoudUiText -Text $e.Kind -Width 9), (Limit-DawoudUiText -Text $e.Message -Width ([math]::Max(8, [int]($Width * .43))))))
        }
    }

    $agents = [System.Collections.Generic.List[string]]::new()
    [void]$agents.Add("AGENTS")
    $displayAgents = [System.Collections.Generic.List[object]]::new()
    foreach ($agent in @($State.Agents.Values)) { [void]$displayAgents.Add($agent) }
    foreach ($executor in @(@{Name='CODEX';TaskAgent='Codex';Model=$State.CodexModel},@{Name='ANTIGRAVITY';TaskAgent='Antigravity';Model=$State.AntigravityModel})) {
        if (@($State.Agents.Values | Where-Object Executor -eq $executor.Name).Count) { continue }
        $assigned = @($State.Tasks | Where-Object { $_.Agent -eq $executor.TaskAgent -and $_.Status -in @('STARTING','RUNNING','WAITING','QUEUED','VERIFYING') } | Select-Object -Last 1)
        if ($assigned.Count) {
            $status = if ($assigned[0].Status -eq 'QUEUED') { 'QUEUED' } elseif ($assigned[0].Status -eq 'STARTING') { 'STARTING' } else { $assigned[0].Status }
            [void]$displayAgents.Add([pscustomobject]@{Name=$executor.Name;Executor=$executor.Name;Model=$executor.Model;Status=$status;Health=$status;TaskId=$assigned[0].Id;Action=$assigned[0].Summary;PID=0;Started=$assigned[0].Started;End=$assigned[0].End;LastEvent=[datetime]::MinValue})
        } else { [void]$displayAgents.Add([pscustomobject]@{Name=$executor.Name;Executor=$executor.Name;Model=$executor.Model;Status='IDLE';Health='IDLE';TaskId='';Action='IDLE';PID=0;Started=[datetime]::MinValue;End=[datetime]::MinValue;LastEvent=[datetime]::MinValue}) }
    }
    foreach ($agent in @($displayAgents)) {
        $displayStatus = if ($agent.Status -eq "RUNNING" -and $agent.Health -in @("WAITING", "WARNING")) { $agent.Health } else { $agent.Status }
        $glyph = Get-DawoudUiGlyph -Status $displayStatus -Unicode $State.Unicode
        if ($compact) {
            $slice = if ($agent.TaskId) { " Task $($agent.TaskId)" } else { "" }
            $pidText = if ($agent.PID -gt 0) { " PID $($agent.PID)" } else { "" }
            [void]$agents.Add(("  {0} {1} {2}{3}{4}" -f $glyph, $agent.Name, $displayStatus, $slice, $pidText))
            [void]$agents.Add(("    {0}" -f (Limit-DawoudUiText -Text $agent.Action -Width 34)))
        } else {
            [void]$agents.Add(("  {0} {1}" -f $glyph, $agent.Name))
            [void]$agents.Add(("    {0}  {1}" -f $displayStatus, (Format-DawoudUiDuration -Start $agent.Started -End $agent.End)))
            if ($agent.TaskId) { [void]$agents.Add(("    Slice {0}" -f (Limit-DawoudUiText -Text $agent.TaskId -Width 28))) }
            if ($agent.Action) { [void]$agents.Add(("    {0}" -f (Limit-DawoudUiText -Text $agent.Action -Width 30))) }
            if ($agent.PID -gt 0) { [void]$agents.Add(("    PID {0}" -f $agent.PID)) }
            if ($State.View -eq "DETAILS" -and $agent.DispatchPID -gt 0) { [void]$agents.Add(("    Dispatch PID {0}" -f $agent.DispatchPID)) }
        }
    }
    if ($Width -ge 92) {
        $left = [math]::Floor($Width * .64); $right = $Width - $left - 3
        $max = [math]::Max($activity.Count, $agents.Count)
        for ($i = 0; $i -lt $max; $i++) {
            $l = if ($i -lt $activity.Count) { Limit-DawoudUiText -Text $activity[$i] -Width $left } else { "" }
            $r = if ($i -lt $agents.Count) { Limit-DawoudUiText -Text $agents[$i] -Width $right } else { "" }
            $combined = ($l.PadRight([int]$left) + "  " + $v + " " + $r.PadRight([int]$right))
            [void]$lines.Add((Limit-DawoudUiText -Text $combined -Width $Width))
        }
    } else {
        foreach ($line in $activity) { [void]$lines.Add((Limit-DawoudUiText -Text $line -Width $Width)) }
        foreach ($line in $agents) { [void]$lines.Add((Limit-DawoudUiText -Text $line -Width $Width)) }
    }

    [void]$lines.Add(($rule * $Width))
    [void]$lines.Add("CURRENT ACTION")
    $action = $State.CurrentAction
    if ($action) {
        [void]$lines.Add(("Agent {0}  Task {1}  Status {2}  Action {3}" -f $action.Name, (Limit-DawoudUiText -Text $action.TaskId -Width 18), $action.Status, (Limit-DawoudUiText -Text $action.Action -Width 30)))
        [void]$lines.Add(("PID {0}  File {1}  Elapsed {2}  Last event {3}" -f $(if ($action.PID) { $action.PID } else { "not exposed" }), $(if ($action.File) { $action.File } else { "not exposed" }), (Format-DawoudUiDuration -Start $action.Started -End $action.End), (Format-DawoudUiDuration -Start $action.LastEvent)))
    } else { [void]$lines.Add("  Waiting for executor event") }

    [void]$lines.Add("TASKS")
    $taskLines = @(Get-DawoudUiVisibleTasks -State $State -Limit $(if ($compact) { 4 } else { 6 }))
    if ($taskLines.Count -eq 0) { [void]$lines.Add("  No planned tasks") }
    else {
        foreach ($task in $taskLines) {
            $glyph = Get-DawoudUiGlyph -Status $task.Status -Unicode $State.Unicode
            [void]$lines.Add(("  {0} {1,-10} {2,-12} {3}" -f $glyph, $task.Id, $(if ($task.Agent) { $task.Agent } else { "queued" }), (Limit-DawoudUiText -Text $task.Summary -Width ([math]::Max(10, $Width - 42)))))
        }
        $done = @($State.Tasks | Where-Object Status -eq "DONE").Count
        $started = @($State.Tasks | Where-Object Status -in @("RUNNING", "STARTING", "DONE", "FAILED", "CANCELLED")).Count
        [void]$lines.Add(("  Task completion: {0}/{1} completed; {2}/{1} started" -f $done, $State.Tasks.Count, $started))
    }

    if ($State.Chat.Count -gt 0 -and $State.View -eq "NORMAL") {
        [void]$lines.Add("AGENT CHAT")
        foreach ($message in @($State.Chat | Select-Object -Last 2)) { [void]$lines.Add(("  {0} {1} -> {2} {3} [{4}] {5}" -f ([string]$message.timestamp).Substring(11,5), $message.from, $message.to, $message.type, $message.status, (Limit-DawoudUiText -Text $message.content -Width ([math]::Max(10,$Width-40))))) }
    }
    [void]$lines.Add("CHANGES")
    $changes = if ($null -ne $State.GitChanges) { @($State.GitChanges) } else { @($State.Files.Values) }
    $files = @($changes | Select-Object -First $(if ($compact) { 3 } else { 5 }))
    if ($files.Count -eq 0) { [void]$lines.Add("  No repository changes observed.") }
    else { foreach ($file in $files) { [void]$lines.Add(("  {0} {1} {2}" -f $file.Action, (Limit-DawoudUiText -Text $file.Path -Width ([math]::Max(8, $Width - 18))), $file.Lines)) } }

    if ($State.CurrentCommand) {
        [void]$lines.Add("DISPATCH COMMAND")
        [void]$lines.Add(("  {0} {1}" -f $State.CurrentCommand.Status, (Limit-DawoudUiText -Text $State.CurrentCommand.Text -Width ([math]::Max(10, $Width - 4)))))
        if ($State.CurrentCommand.Kind -eq "DISPATCH") { [void]$lines.Add("  Command details unavailable from executor") }
    }
    if ($State.View -eq "DETAILS") {
        [void]$lines.Add("DETAILS")
        foreach ($agent in @($State.Agents.Values)) {
            [void]$lines.Add(("  {0}: model={1}; executor={2}; cwd={3}" -f $agent.Name, $agent.Model, $agent.Executor, (Limit-DawoudUiText -Text $agent.Cwd -Width 28)))
            [void]$lines.Add(("    pid={0}; dispatch_pid={1}; exit={2}; retries={3}; stream_events={4}; timeout={5}; last_event={6}" -f $(if ($agent.PID) { $agent.PID } else { "not exposed" }), $(if ($agent.DispatchPID) { $agent.DispatchPID } else { "not exposed" }), $(if ($null -eq $agent.ExitCode) { "not exposed" } else { $agent.ExitCode }), $(if ($agent.Retry) { $agent.Retry } else { "0" }), $agent.StreamEvents, $agent.Timeout, $(if ($agent.LastEvent -ne [datetime]::MinValue) { $agent.LastEvent.ToString("HH:mm:ss") } else { "not exposed" })))
            [void]$lines.Add("    latest=executor event detail not exposed")
            if ($agent.FailureReason) { [void]$lines.Add(("    failure={0}" -f (Limit-DawoudUiText -Text $agent.FailureReason -Width 150))) }
        }
    }
    if ($State.View -eq "LOG") {
        [void]$lines.Add("EVENT LOG")
        foreach ($e in @($State.Events | Select-Object -Last 12)) { [void]$lines.Add(("  {0} {1} {2} {3}" -f $e.At.ToString("o"), $e.Source, $e.Kind, $e.Message)) }
    }
    if ($State.Summary) {
        [void]$lines.Add("EXECUTION SUMMARY")
        [void]$lines.Add(("  Planned tasks {0}  |  Internal/derived tasks {1}  |  Total executor assignments {2}" -f $State.Summary.UserSlices, $State.Summary.InternalTasks, $State.Summary.TotalAssignments))
        [void]$lines.Add(("  Completed {0}  |  Failed {1}  |  Total {2}" -f $State.Summary.Completed, $State.Summary.Failed, (Format-DawoudUiDuration -Start $State.SessionStart)))
    }
    if ($State.Result -and [Threading.Thread]::CurrentThread.ManagedThreadId -eq $State.UiThreadId) {
        [void]$lines.Add("RESULT")
        foreach ($line in @($State.Result -split "`r?`n" | Select-Object -First 5)) { [void]$lines.Add(("  {0}" -f (Limit-DawoudUiText -Text $line -Width ([math]::Max(10, $Width - 2))))) }
    }
    [void]$lines.Add(("Keys: :details :agents :tasks :changes :chat :diff :log :help   Ctrl+L redraw   Ctrl+U clear   Ctrl+C cancel   Ctrl+Enter send"))
    if (($State.Status -in @("DONE", "FAILED", "PARTIAL", "CANCELLED")) -and $State.Result -and $State.View -eq "NORMAL") {
        $finalLines = [System.Collections.Generic.List[string]]::new()
        if ($Height -lt 30) {
            foreach ($line in @($lines | Select-Object -First 2)) { [void]$finalLines.Add($line) }
            [void]$finalLines.Add(($rule * $Width))
            [void]$finalLines.Add("RESULT")
            foreach ($line in @($State.Result -split "`r?`n" | Select-Object -First 2)) { [void]$finalLines.Add((Limit-DawoudUiText -Text $line -Width ([math]::Max(10, $Width - 4)))) }
            if ($State.Summary) { [void]$finalLines.Add(("SUMMARY {0}/{1} done; {2} failed; assignments {3}" -f $State.Summary.Completed, $State.Summary.UserSlices, $State.Summary.Failed, $State.Summary.TotalAssignments)) }
            foreach ($task in @($State.Tasks | Select-Object -Last 2)) { $taskText = if ($task.Error) { $task.Error } else { $task.Summary }; [void]$finalLines.Add(("TASK {0} {1}: {2}" -f (Get-DawoudUiGlyph -Status $task.Status -Unicode $State.Unicode), $task.Id, $taskText)) }
            [void]$finalLines.Add("Command details unavailable from executor")
            [void]$finalLines.Add("TOKEN USAGE: Exact per-agent metering unavailable.")
            [void]$finalLines.Add("Keys :details :log :help")
            $lines = $finalLines
        } else {
        foreach ($line in @($lines | Select-Object -First 3)) { [void]$finalLines.Add($line) }
        [void]$finalLines.Add(($rule * $Width))
        [void]$finalLines.Add("TASKS")
        foreach ($task in @($State.Tasks | Select-Object -Last 6)) {
            $glyph = Get-DawoudUiGlyph -Status $task.Status -Unicode $State.Unicode
            [void]$finalLines.Add(("  {0} {1,-10} {2,-12} {3}" -f $glyph, $task.Id, $(if ($task.Agent) { $task.Agent } else { "queued" }), (Limit-DawoudUiText -Text $task.Summary -Width ([math]::Max(10, $Width - 42)))))
        }
        if ($State.Summary) {
            [void]$finalLines.Add("EXECUTION SUMMARY")
            [void]$finalLines.Add(("  Planned tasks {0}  |  Internal/derived {1}  |  Total assignments {2}" -f $State.Summary.UserSlices, $State.Summary.InternalTasks, $State.Summary.TotalAssignments))
            [void]$finalLines.Add(("  Completed {0}  |  Failed {1}  |  Total {2}" -f $State.Summary.Completed, $State.Summary.Failed, (Format-DawoudUiDuration -Start $State.SessionStart)))
        }
        [void]$finalLines.Add("RESULT")
        foreach ($line in @($State.Result -split "`r?`n" | Select-Object -First 4)) { [void]$finalLines.Add(("  {0}" -f $line)) }
        [void]$finalLines.Add("TOKEN USAGE: Exact per-agent token metering unavailable.")
        [void]$finalLines.Add("Keys: :details :log :help   Ctrl+L redraw   Ctrl+U clear   Ctrl+C cancel")
        $lines = $finalLines
        }
    }
    if ($compact -and -not ($State.Status -in @("DONE", "FAILED", "PARTIAL", "CANCELLED") -and $State.Result -and $State.View -eq "NORMAL")) {
        $compactLines = [System.Collections.Generic.List[string]]::new()
        [void]$compactLines.Add($lines[0]); [void]$compactLines.Add($lines[1]); [void]$compactLines.Add($rule * $Width)
        [void]$compactLines.Add("AGENTS")
        foreach ($agent in @($displayAgents)) {
            $health = if ($agent.Status -eq "RUNNING" -and $agent.Health -in @("WAITING", "WARNING")) { $agent.Health } else { $agent.Status }
            $glyph = Get-DawoudUiGlyph -Status $health -Unicode $State.Unicode
            $slice = if ($agent.TaskId) { " $($agent.TaskId)" } else { "" }
            $pidText = if ($agent.PID -gt 0) { " PID $($agent.PID)" } else { "" }
            $elapsed = if ($agent.Started -ne [datetime]::MinValue) { " " + (Format-DawoudUiDuration -Start $agent.Started -End $agent.End) } else { "" }
            [void]$compactLines.Add(("  {0} {1} {2}{3}{4}{5}" -f $glyph, $agent.Name, $health, $slice, $elapsed, $pidText))
        }
        [void]$compactLines.Add("CURRENT ACTION")
        if ($State.CurrentAction) {
            [void]$compactLines.Add(("  {0} {1}: {2}" -f $State.CurrentAction.Name, $State.CurrentAction.TaskId, $State.CurrentAction.Action))
            [void]$compactLines.Add(("  File {0} | elapsed {1} | PID {2}" -f $(if ($State.CurrentAction.File) { $State.CurrentAction.File } else { "not exposed" }), (Format-DawoudUiDuration -Start $State.CurrentAction.Started -End $State.CurrentAction.End), $(if ($State.CurrentAction.PID) { $State.CurrentAction.PID } else { "not exposed" })))
        } else { [void]$compactLines.Add("  IDLE") }
        [void]$compactLines.Add("TASKS")
        $compactTaskCount = if ($Height -le 24) { 2 } else { 3 }
        foreach ($task in @(Get-DawoudUiVisibleTasks -State $State -Limit $compactTaskCount)) { [void]$compactLines.Add(("  {0} {1} {2}: {3}" -f (Get-DawoudUiGlyph -Status $task.Status -Unicode $State.Unicode), $task.Id, $(if ($task.Agent) { $task.Agent } else { "QUEUED" }), $task.Summary)) }
        [void]$compactLines.Add("LIVE ACTIVITY")
        $compactEventCount = if ($Height -le 24) { 2 } else { 3 }
        foreach ($e in @($State.Events | Select-Object -Last $compactEventCount)) { [void]$compactLines.Add(("  {0} {1} {2}" -f $e.At.ToString("HH:mm:ss"), $e.Source, $e.Message)) }
        [void]$compactLines.Add("FILE ACTIVITY")
        $compactFileCount = if ($Height -le 24) { 1 } else { 2 }
        $compactFiles = @($State.Files.Values | Sort-Object At -Descending | Select-Object -First $compactFileCount)
        if ($compactFiles.Count -eq 0) { [void]$compactLines.Add("  No repository changes observed.") }
        else { foreach ($file in $compactFiles) { [void]$compactLines.Add(("  {0} {1}" -f $file.Action, $file.Path)) } }
        if ($State.CurrentCommand) {
            [void]$compactLines.Add(("DISPATCH {0}: {1}" -f $State.CurrentCommand.Status, $State.CurrentCommand.Text))
            if ($State.CurrentCommand.Kind -eq "DISPATCH") { [void]$compactLines.Add("Command details unavailable from executor") }
        }
        [void]$compactLines.Add("Keys: :details :send :clear :cancel :help")
        if ($Height -lt 16) {
            $micro = [System.Collections.Generic.List[string]]::new()
            [void]$micro.Add($lines[0])
            foreach ($agent in @($displayAgents)) {
                $health = if ($agent.Status -eq "RUNNING" -and $agent.Health -in @("WAITING", "WARNING")) { $agent.Health } else { $agent.Status }
                $glyph = Get-DawoudUiGlyph -Status $health -Unicode $State.Unicode
                $elapsed = if ($agent.Started -ne [datetime]::MinValue) { Format-DawoudUiDuration -Start $agent.Started -End $agent.End } else { "--:--" }
                [void]$micro.Add(("{0} {1} {2} {3} {4}" -f $glyph, $agent.Name, $health, $elapsed, $(if ($agent.PID) { "PID $($agent.PID)" } else { "" })))
            }
            if ($State.CurrentAction) { [void]$micro.Add(("CURRENT {0} {1}: {2} | {3}" -f $State.CurrentAction.Name, $State.CurrentAction.TaskId, $State.CurrentAction.Action, $(if ($State.CurrentAction.File) { $State.CurrentAction.File } else { "file not exposed" }))) }
            else { [void]$micro.Add("CURRENT IDLE") }
            $microTask = @(Get-DawoudUiVisibleTasks -State $State -Limit 1)
            if ($microTask.Count) { [void]$micro.Add(("TASK {0} {1}: {2}" -f (Get-DawoudUiGlyph -Status $microTask[0].Status -Unicode $State.Unicode), $microTask[0].Id, $microTask[0].Summary)) } else { [void]$micro.Add("TASK idle") }
            $lastEvent = @($State.Events | Select-Object -Last 1)
            $eventText = if ($lastEvent.Count) { "EVENT $($lastEvent[0].Source): $($lastEvent[0].Message)" } else { "EVENT waiting" }
            [void]$micro.Add($eventText)
            $microFile = @($State.Files.Values | Sort-Object At -Descending | Select-Object -First 1)
            $fileText = if ($microFile.Count) { "FILE $($microFile[0].Action) $($microFile[0].Path)" } else { "FILE not observed" }
            [void]$micro.Add($fileText)
            $commandText = if ($State.CurrentCommand) { "DISPATCH $($State.CurrentCommand.Status): $($State.CurrentCommand.Text)" } else { "DISPATCH not exposed" }
            [void]$micro.Add($commandText)
            if ($State.CurrentCommand.Kind -eq "DISPATCH") { [void]$micro.Add("Command details unavailable from executor") }
            [void]$micro.Add("Keys :details :send :clear :cancel :help")
            $compactLines = $micro
        }
        $lines = $compactLines
    }
    $innerWidth = [math]::Max(1, $Width - 2)
    $contentHeight = [math]::Max(1, $Height - 2)
    if ($lines.Count -gt $contentHeight) {
        $trimmed = [System.Collections.Generic.List[string]]::new()
        foreach ($line in @($lines | Select-Object -First ($contentHeight - 1))) { [void]$trimmed.Add($line) }
        [void]$trimmed.Add("... dashboard truncated to terminal height")
        $lines = $trimmed
    }
    if ($State.View -eq "FILES") {
        $changes = if ($null -ne $State.GitChanges) { @($State.GitChanges) } else { @($State.Files.Values) }
        $lines = @("CHANGES") + @($changes | Select-Object -First ($Height - 2) | ForEach-Object { "$($_.Action) $($_.Path) $($_.Lines)" })
        if ($changes.Count -eq 0) { $lines += "No repository changes observed." }
    }
    if ($State.View -eq "DIFF") {
        $lines = @('DIFF (bounded excerpt)') + @($State.DiffLines | Select-Object -First ($Height - 2))
    }
    if ($State.View -eq "CHAT") {
        $lines = @("AGENT CHAT") + @($State.Chat | Select-Object -Last ($Height - 2) | ForEach-Object { "$($_.timestamp) $($_.from) -> $($_.to) $($_.type) $($_.status) $($_.task_id): $($_.content)" })
        if ($State.Chat.Count -eq 0) { $lines += "No operational messages exchanged." }
    }
    @($lines | ForEach-Object { Fit-DawoudUiLine -Text $_ -Width $Width })
}

function Fit-DawoudUiLine {
    param([AllowEmptyString()][string]$Text, [int]$Width)
    $width = [math]::Max(1, $Width)
    $value = if ($null -eq $Text) { "" } else { [string]$Text }
    if ($value.Length -gt $width) {
        if ($width -le 3) { return $value.Substring(0, $width) }
        return $value.Substring(0, $width - 3) + "..."
    }
    $value.PadRight($width)
}

function Update-DawoudUiHealth {
    param([Parameter(Mandatory)]$State)
    $now = Get-Date
    foreach ($agent in @($State.Agents.Values)) {
        if ($agent.Status -notin @("RUNNING", "WAITING", "WARNING")) { continue }
        $age = ($now - $agent.LastEvent).TotalSeconds
        $health = if ($age -ge 45) { "WARNING" } elseif ($age -ge 12) { "WAITING" } else { "RUNNING" }
        if ($health -ne $agent.Health) {
            $agent.Health = $health
            $agent.Status = $health
            $agent.Action = if ($health -eq "WARNING") { "No executor event for 45s+; state unverified" } elseif ($health -eq "WAITING") { "No executor event for 12s+; waiting" } else { "Executor event received" }
            Publish-DawoudUiUpdate -State $State -Kind "AGENT" -Value $agent
            [void](Add-DawoudUiEvent -State $State -Source $agent.Name -Kind $health -Message $agent.Action -Status $health -TaskId $agent.TaskId)
        }
    }
}

function Write-DawoudDashboard {
    param([Parameter(Mandatory)]$State, [switch]$Force)
    if ([Threading.Thread]::CurrentThread.ManagedThreadId -ne $State.UiThreadId) { return }
    try {
        Receive-DawoudUiUpdates -State $State
        Pump-DawoudUiFileWatch -State $State
        Update-DawoudUiHealth -State $State
        $warningExists = @($State.Events | Where-Object Kind -eq 'UI STATE WARNING').Count -gt 0
        if ($State.Status -eq 'RUNNING' -and ((Get-Date)-$State.LastUiPublicationAt).TotalSeconds -ge 15 -and -not $warningExists) {
            [void](Add-DawoudUiEvent -State $State -Source 'AGEX' -Kind 'WARNING' -Message 'UI STATE WARNING: Background execution state pending; execution continues.' -Status 'WARNING')
        }
        $width = 99; $height = 22
        try { $width = [math]::Max(24, [Console]::WindowWidth - 1); $height = [math]::Max(12, [math]::Min(22, [Console]::WindowHeight - 12)) } catch { }
        $lines = @(Get-DawoudUiLines -State $State -Width $width -Height $height)
        $resized = $State.LastWindowWidth -gt 0 -and ($width -ne $State.LastWindowWidth -or $height -ne $State.LastWindowHeight)
        if ($resized) {
            $oldTop = $State.RenderTop
            for ($i = 0; $i -lt $State.RenderRows; $i++) {
                $y = $oldTop + $i
                if ($y -ge 0 -and $y -lt [Console]::BufferHeight) { [Console]::SetCursorPosition(0, $y); [Console]::Write(" ".PadRight([math]::Max(1, [Console]::BufferWidth - 1))) }
            }
            $State.RenderTop = [math]::Max([Console]::WindowTop, 0)
            $State.LastRenderedFingerprint = ""
        }
        $fingerprint = [string]::Join("`n", $lines)
        $timerDue = ((Get-Date) - $State.LastRenderAt).TotalMilliseconds -ge 1000
        if (-not $Force -and -not $timerDue -and $fingerprint -eq $State.LastRenderedFingerprint) { return }
        if ([Console]::IsOutputRedirected) {
            $active=@($State.Agents.Values | Where-Object Status -in @('STARTING','RUNNING','WAITING','WARNING','QUEUED')) | ForEach-Object { "$($_.Executor) $($_.Status) $($_.TaskId) PID $($_.PID)" }
            $tasks="Tasks $(@($State.Tasks | Where-Object Status -eq 'DONE').Count)/$($State.Tasks.Count) done"
            $assignments="Assignments $($State.AssignmentCount)"
            $detail=if($active.Count){$active -join '; '}else{@(Get-DawoudUiVisibleTasks -State $State -Limit 3 | ForEach-Object { "$($_.Status) $($_.Id)" }) -join '; '}
            if (@($State.Events | Where-Object Kind -eq 'UI STATE WARNING').Count) { $detail='UI STATE WARNING: Background execution state pending; execution continues. ' + $detail }
            Write-Host ("AGEX {0} | {1} | {2} | {3} | {4}" -f $State.Status,$State.AcceptanceStage,$tasks,$assignments,$detail)
            $State.LastRenderAt=Get-Date;$State.LastRenderedFingerprint=$fingerprint
            return
        }
        if ($State.RenderTop -lt 0) { $State.RenderTop = [Console]::CursorTop }
        $rows = [math]::Max($State.RenderRows, $lines.Count)
        for ($i = 0; $i -lt $rows; $i++) {
            $y = [math]::Min([Console]::BufferHeight - 1, $State.RenderTop + $i)
            [Console]::SetCursorPosition(0, $y)
            $line = if ($i -lt $lines.Count) { Fit-DawoudUiLine -Text $lines[$i] -Width $width } else { "".PadRight($width) }
            $color = if ($line -match "FAILED|FAIL") { "Red" } elseif ($line -match "DONE|PASS") { "Green" } elseif ($line -match "WAIT|START|RETRY|WARNING") { "Yellow" } elseif ($i -eq 0 -or $line -match "CURRENT ACTION|TASKS|FILES|AGENTS|LIVE ACTIVITY|RESULT|DETAILS|EVENT LOG|COMMAND|EXECUTION SUMMARY") { "Cyan" } else { "Gray" }
            Write-Host $line.PadRight([math]::Max(1, $width - 1)) -ForegroundColor $color -NoNewline
        }
        [Console]::SetCursorPosition(0, [math]::Min([Console]::BufferHeight - 1, $State.RenderTop + $lines.Count))
        $State.RenderRows = $lines.Count
        $State.LastWindowWidth = $width
        $State.LastWindowHeight = $height
        $State.LastRenderAt = Get-Date
        $State.LastRenderedFingerprint = $fingerprint
        if ($State.EditorRender) {
            if ($resized) { try { & $State.EditorRender } catch { } }
            elseif ($State.EditorRestore) { try { & $State.EditorRestore } catch { } }
        }
    } catch {
        $State.FailedRender = $true
        $State.FailedRenderReason = Protect-DawoudTelemetryText -Text $_.Exception.Message
        try { Write-Host ("AGEX status: {0}; terminal view unavailable; execution continues." -f $State.Status) -ForegroundColor Yellow } catch { }
    }
}

function Set-DawoudUiView {
    param([Parameter(Mandatory)]$State, [ValidateSet("NORMAL", "DETAILS", "AGENTS", "TASKS", "FILES", "DIFF", "CHAT", "LOG")][string]$View)
    $State.View = if ($View -in @("AGENTS", "TASKS")) { "NORMAL" } else { $View }
    $State.LastRenderedFingerprint = ""
    Write-DawoudDashboard -State $State -Force
}

function Complete-DawoudUiSession {
    param([Parameter(Mandatory)]$State)
    Stop-DawoudUiFileWatch -State $State
    Pump-DawoudUiFileWatch -State $State
    Update-DawoudChanges -State $State
    foreach($agent in @($State.Agents.Values | Where-Object Health -in @('WAITING','WARNING'))) {
        if($agent.LastEvent -gt [datetime]::MinValue) { $State.TimingEvents.Enqueue([pscustomobject]@{Kind='AGENT_WAIT';WorkId=$agent.TaskId;Started=$agent.LastEvent;Ended=Get-Date;Seconds=[math]::Round(((Get-Date)-$agent.LastEvent).TotalSeconds,2)}) }
    }
    $done = @($State.Tasks | Where-Object Status -eq "DONE").Count
    $failed = @($State.Tasks | Where-Object Status -eq "FAILED").Count
    $cancelled = @($State.Tasks | Where-Object Status -eq "CANCELLED").Count
    $taskRecords = @()
    try { $taskRecords = @(Get-DawoudTelemetryRecords -TelemetryRoot $script:telemetryRoot -SessionId $State.SessionId | Where-Object { $_.WorkId -like "$($State.WorkId)-*" -and $_.RecordKind -eq "TASK" }) } catch { }
    $internal = [math]::Max(0, $taskRecords.Count - $State.Tasks.Count)
    $running = @($State.Tasks | Where-Object Status -in @("RUNNING", "STARTING", "VERIFYING")).Count
    $pending = @($State.Tasks | Where-Object Status -in @("QUEUED", "WAITING", "BLOCKED")).Count
    $changedFiles = @($State.Files.Values | ForEach-Object Path) + @($State.GitChanges | ForEach-Object Path)
    $State.Summary = [pscustomobject]@{ UserSlices = $State.Tasks.Count; InternalTasks = $internal; TotalAssignments = [math]::Max($State.AssignmentCount, $State.Tasks.Count + $internal); Completed = $done; Failed = $failed; Running=$running; Pending=$pending; Verification=if($running){"IN PROGRESS"}elseif($failed){"FAILED / REPAIR REQUIRED"}else{"RECORDED"}; ChangedFiles=@($changedFiles | Where-Object { $_ } | Sort-Object -Unique) }
    if ($State.StageTimes.Count) {
        $stageRows = @($State.StageTimes)
        for ($i=0; $i -lt $stageRows.Count; $i++) {
            $endAt = if ($i + 1 -lt $stageRows.Count) { $stageRows[$i+1].At } else { Get-Date }
            $stageRows[$i] | Add-Member -NotePropertyName DurationSeconds -NotePropertyValue ([math]::Round(($endAt - $stageRows[$i].At).TotalSeconds,2)) -Force
        }
    }
    $stageTiming = (@($State.StageTimes | ForEach-Object { "{0}={1}s" -f $_.Stage, $_.DurationSeconds }) -join "; ")
    $State.Summary | Add-Member -NotePropertyName StageTiming -NotePropertyValue $stageTiming -Force
    $timing = @($State.TimingEvents.ToArray())
    $State.Summary | Add-Member -NotePropertyName TimingBreakdown -NotePropertyValue ([pscustomobject]@{
        LeaderPlanningSeconds=[math]::Round([double](@($timing | Where-Object Kind -eq 'LEADER' | Measure-Object Seconds -Sum).Sum),2)
        TaskExecutionSeconds=[math]::Round([double](@($State.Tasks | ForEach-Object { if($_.End -gt $_.Started -and $_.Started -gt [datetime]::MinValue) { ($_.End-$_.Started).TotalSeconds } } | Measure-Object -Sum).Sum),2)
        VerificationSeconds=[math]::Round([double](@($timing | Where-Object Kind -eq 'VERIFY' | Measure-Object Seconds -Sum).Sum),2)
        AgentWaitingSeconds=[math]::Round([double](@($timing | Where-Object Kind -eq 'AGENT_WAIT' | Measure-Object Seconds -Sum).Sum),2)
        MailboxWaitSeconds=[math]::Round([double](@($timing | Where-Object Kind -eq 'MAILBOX' | Measure-Object Seconds -Sum).Sum),2)
        StageSeconds=$stageTiming
    }) -Force
    $State.Status = if ($cancelled -gt 0 -or $State.GoalStatus -eq "CANCELLED") { "CANCELLED" } elseif ($failed -gt 0) { "FAILED" } elseif ($State.GoalStatus -eq "COMPLETE") { "DONE" } else { "PARTIAL" }
    $State.CurrentAction = $null
    $State.CurrentCommand = $null
    $sessionMessage = if ($cancelled -gt 0) { "Cancelled request; completed $done/$($State.Tasks.Count) planned tasks" } else { "Completed $done/$($State.Tasks.Count) planned tasks" }
    [void](Add-DawoudUiEvent -State $State -Source "AGEX" -Kind "SESSION" -Message $sessionMessage -Status $State.Status)
    Write-DawoudDashboard -State $State -Force
    if ($State.Result) {
        Write-Host ""
        Write-Host "RESULT" -ForegroundColor Cyan
        Write-Host (Protect-DawoudTelemetryText -Text $State.Result)
        if ($State.Summary) {
            Write-Host "EXECUTION SUMMARY" -ForegroundColor Cyan
            Write-Host ("Planned tasks: {0} | Internal/derived tasks: {1} | Total executor assignments: {2}" -f $State.Summary.UserSlices, $State.Summary.InternalTasks, $State.Summary.TotalAssignments)
            Write-Host ("Completed: {0} | Failed: {1}" -f $State.Summary.Completed, $State.Summary.Failed)
            Write-Host ("Running: {0} | Pending: {1} | Verification: {2}" -f $State.Summary.Running, $State.Summary.Pending, $State.Summary.Verification)
            if ($State.Summary.StageTiming) { Write-Host ("Stage timing: {0}" -f $State.Summary.StageTiming) }
            Write-Host ("Leader planning {0}s | Task execution {1}s | Agent waiting {2}s | Verification {3}s | Mailbox waits {4}s" -f $State.Summary.TimingBreakdown.LeaderPlanningSeconds,$State.Summary.TimingBreakdown.TaskExecutionSeconds,$State.Summary.TimingBreakdown.AgentWaitingSeconds,$State.Summary.TimingBreakdown.VerificationSeconds,$State.Summary.TimingBreakdown.MailboxWaitSeconds)
            if ($State.Summary.ChangedFiles.Count) { Write-Host ("Changed files: {0}" -f ($State.Summary.ChangedFiles -join ', ')) } else { Write-Host "Changed files: none observed" }
            foreach ($task in @($State.Tasks | Where-Object { $_.Status -in @('FAILED','REPAIR REQUIRED','BLOCKED') })) { Write-Host ("Blocker {0}: {1}" -f $task.Id, $(if($task.Error){$task.Error}else{$task.Status})) }
        }
    }
}

function Update-DawoudChanges {
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
