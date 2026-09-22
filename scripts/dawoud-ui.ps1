# DAWOUD observability only. It owns terminal state and rendering, never execution.

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
    [pscustomobject]@{
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
        Tasks = [System.Collections.Generic.List[object]]::new()
        Events = [System.Collections.Generic.List[object]]::new()
        Agents = [ordered]@{}
        Files = [ordered]@{}
        CurrentAction = $null
        CurrentCommand = $null
        LastEventAt = [datetime]::MinValue
        Result = ""
        Summary = $null
        View = "NORMAL"
        Unicode = [bool]($env:WT_SESSION -or [Console]::OutputEncoding.CodePage -eq 65001)
        RenderTop = -1
        RenderRows = 0
        LastRenderAt = [datetime]::MinValue
        FileQueue = [System.Collections.Concurrent.ConcurrentQueue[object]]::new()
        FileWatcher = $null
        FileEventSource = ""
        LastFilePump = [datetime]::MinValue
        LastRenderedFingerprint = ""
        FailedRender = $false
    }
}

function Get-DawoudUiGlyph {
    param([Parameter(Mandatory)][string]$Status, [bool]$Unicode = $true)
    if (-not $Unicode) {
        switch ($Status) {
            "RUNNING" { return ">" }
            "STARTING" { return ">" }
            "WAITING" { return "~" }
            "RETRYING" { return "R" }
            "DONE" { return "V" }
            "WARNING" { return "!" }
            "FAILED" { return "X" }
            "CANCELLED" { return "-" }
            default { return "o" }
        }
    }
    switch ($Status) {
        "RUNNING" { "●" }
        "STARTING" { "◐" }
        "WAITING" { "◐" }
        "RETRYING" { "↻" }
        "DONE" { "✓" }
        "WARNING" { "▲" }
        "FAILED" { "✕" }
        "CANCELLED" { "■" }
        default { "○" }
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
    if ($State.Events.Count -ge 160) { $State.Events.RemoveAt(0) }
    [void]$State.Events.Add($item)
    $State.LastEventAt = $item.At
    $item
}

function Add-DawoudUiTask {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)]$Task)
    [void]$State.Tasks.Add($Task)
}

function Find-DawoudUiTask {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$TaskId)
    @($State.Tasks | Where-Object Id -eq $TaskId | Select-Object -First 1)
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
        if ($Status -eq "DONE" -or $Status -eq "FAILED") { $task.End = Get-Date }
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
        [string]$WorkingDirectory
    )
    $now = Get-Date
    $agent = [pscustomobject]@{
        Name = $Name; Executor = $Executor; Status = "STARTING"; TaskId = $TaskId
        Task = Limit-DawoudUiText -Text $TaskText -Width 160; Action = "Starting executor"
        File = ""; PID = 0; DispatchPID = 0; Started = $now; End = [datetime]::MinValue
        LastEvent = $now; Model = if ($Model) { $Model } else { "default" }
        Command = $Command; Cwd = $WorkingDirectory; ExitCode = $null; Retry = ""
        StreamEvents = 0; Timeout = "NONE"
        Health = "OK"
    }
    $State.Agents[$Name] = $agent
    $State.Status = "RUNNING"
    $State.CurrentAction = $agent
    if ($Command) { $State.CurrentCommand = [pscustomobject]@{ Text = $Command; Status = "RUNNING"; ExitCode = $null; Started = $now; End = [datetime]::MinValue } }
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
    if ($Status) { $agent.Status = $Status }
    if ($Status -eq "RUNNING" -and $EventKind -ne "MONITOR") { $agent.Health = "OK" }
    if ($Action) { $agent.Action = $Action }
    if ($ProcessId -gt 0) { $agent.PID = $ProcessId }
    if ($File) { $agent.File = $File; $State.Files[$File] = [pscustomobject]@{ Path = $File; Action = "M"; At = $now; Active = $true } }
    if ($TaskId) { $agent.TaskId = $TaskId }
    $agent.LastEvent = $now
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
    $agent
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
    if ($StreamEvents -ge 0) { $agent.StreamEvents = $StreamEvents }
    if ($Timeout) { $agent.Timeout = $Timeout }
    $State.CurrentAction = $agent
    if ($State.CurrentCommand) {
        $State.CurrentCommand.Status = if ($Status -eq "DONE") { "PASS" } else { "FAIL" }
        $State.CurrentCommand.ExitCode = $ExitCode
        $State.CurrentCommand.End = $agent.End
    }
    $kind = if ($Status -eq "DONE") { "PASS" } else { "FAIL" }
    $finalMessage = if ($Message) { $Message } else { $agent.Action }
    [void](Add-DawoudUiEvent -State $State -Source $Name -Kind $kind -Message $finalMessage -Status $Status -TaskId $agent.TaskId)
    $agent
}

function Set-DawoudUiFile {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Path, [ValidateSet("R", "M", "+", "-")][string]$Action = "M", [bool]$Active = $true)
    $clean = Protect-DawoudTelemetryText -Text $Path
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
        $watcher.NotifyFilter = [IO.NotifyFilters]::FileName -bor [IO.NotifyFilters]::LastWrite -bor [IO.NotifyFilters]::Size -bor [IO.NotifyFilters]::CreationTime
        $watcher.EnableRaisingEvents = $true
        $source = "dawoud-ui-" + ([guid]::NewGuid().ToString("N"))
        foreach ($eventName in @("Created", "Changed", "Deleted", "Renamed")) {
            Register-ObjectEvent -InputObject $watcher -EventName $eventName -SourceIdentifier ($source + "-" + $eventName) -MessageData $State.FileQueue -Action {
                try {
                    $queue = $event.MessageData
                    $args = $event.SourceEventArgs
                    $path = if ($args.FullPath) { [string]$args.FullPath } else { "" }
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
        $action = switch ($item.Kind) { "Created" { "+" } "Deleted" { "-" } default { "M" } }
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
    $Width = [math]::Max(42, $Width)
    $Height = [math]::Max(12, $Height)
    $rule = if ($State.Unicode) { "─" } else { "-" }
    $v = if ($State.Unicode) { "│" } else { "|" }
    $lines = [System.Collections.Generic.List[string]]::new()
    $sessionTime = Format-DawoudUiDuration -Start $State.SessionStart
    $statusGlyph = Get-DawoudUiGlyph -Status $State.Status -Unicode $State.Unicode
    [void]$lines.Add(("DAWOUD AI CONTROL CENTER  |  {0}  |  SESSION {1}  |  {2} {3}" -f $State.ProjectName, $sessionTime, $statusGlyph, $State.Status))
    [void]$lines.Add(("Leader {0} -> {1}   Target AGY {2}% / Codex {3}%   Models AGY {4} | Codex {5}" -f $State.ConfiguredLeader, $State.ResolvedLeader, $State.AntigravityShare, $State.CodexShare, $State.AntigravityModel, $State.CodexModel))
    [void]$lines.Add(($rule * $Width))

    $eventCount = [math]::Min(7, $State.Events.Count)
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
    if ($State.Agents.Count -eq 0) { [void]$agents.Add("  ○ CODEX       IDLE"); [void]$agents.Add("  ○ ANTIGRAVITY IDLE") }
    else {
        foreach ($agent in @($State.Agents.Values)) {
            $glyph = Get-DawoudUiGlyph -Status $agent.Status -Unicode $State.Unicode
            [void]$agents.Add(("  {0} {1}" -f $glyph, $agent.Name))
            [void]$agents.Add(("    {0}  {1}" -f $agent.Status, (Format-DawoudUiDuration -Start $agent.Started -End $agent.End)))
            if ($agent.TaskId) { [void]$agents.Add(("    Slice {0}" -f (Limit-DawoudUiText -Text $agent.TaskId -Width 28))) }
            if ($agent.Action) { [void]$agents.Add(("    {0}" -f (Limit-DawoudUiText -Text $agent.Action -Width 30))) }
            if ($agent.PID -gt 0) { [void]$agents.Add(("    PID {0}" -f $agent.PID)) }
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
    $taskLines = @($State.Tasks | Select-Object -Last 6)
    if ($taskLines.Count -eq 0) { [void]$lines.Add("  No task slices") }
    else {
        foreach ($task in $taskLines) {
            $glyph = Get-DawoudUiGlyph -Status $task.Status -Unicode $State.Unicode
            [void]$lines.Add(("  {0} {1,-10} {2,-12} {3}" -f $glyph, $task.Id, $(if ($task.Agent) { $task.Agent } else { "queued" }), (Limit-DawoudUiText -Text $task.Summary -Width ([math]::Max(10, $Width - 42)))))
        }
        $done = @($State.Tasks | Where-Object Status -eq "DONE").Count
        $started = @($State.Tasks | Where-Object Status -in @("RUNNING", "STARTING", "DONE", "FAILED", "CANCELLED")).Count
        [void]$lines.Add(("  Task completion: {0}/{1} completed; {2}/{1} started" -f $done, $State.Tasks.Count, $started))
    }

    [void]$lines.Add("FILES THIS TASK")
    $files = @($State.Files.Values | Sort-Object At -Descending | Select-Object -First 5)
    if ($files.Count -eq 0) { [void]$lines.Add("  No observable file changes") }
    else { foreach ($file in $files) { [void]$lines.Add(("  {0} {1}" -f $file.Action, (Limit-DawoudUiText -Text $file.Path -Width ([math]::Max(8, $Width - 6))))) } }

    if ($State.CurrentCommand) {
        [void]$lines.Add("COMMAND")
        [void]$lines.Add(("  {0} {1}" -f $State.CurrentCommand.Status, (Limit-DawoudUiText -Text $State.CurrentCommand.Text -Width ([math]::Max(10, $Width - 4)))))
    }
    if ($State.View -eq "DETAILS") {
        [void]$lines.Add("DETAILS")
        foreach ($agent in @($State.Agents.Values)) { [void]$lines.Add(("  {0}: model={1}; cwd={2}; exit={3}; retries={4}; stream_events={5}; timeout={6}" -f $agent.Name, $agent.Model, (Limit-DawoudUiText -Text $agent.Cwd -Width 28), $(if ($null -eq $agent.ExitCode) { "not exposed" } else { $agent.ExitCode }), $(if ($agent.Retry) { $agent.Retry } else { "0" }), $agent.StreamEvents, $agent.Timeout)) }
    }
    if ($State.View -eq "LOG") {
        [void]$lines.Add("EVENT LOG")
        foreach ($e in @($State.Events | Select-Object -Last 12)) { [void]$lines.Add(("  {0} {1} {2} {3}" -f $e.At.ToString("o"), $e.Source, $e.Kind, $e.Message)) }
    }
    if ($State.Summary) {
        [void]$lines.Add("EXECUTION SUMMARY")
        [void]$lines.Add(("  User task slices {0}  |  Internal/derived tasks {1}  |  Total executor assignments {2}" -f $State.Summary.UserSlices, $State.Summary.InternalTasks, $State.Summary.TotalAssignments))
        [void]$lines.Add(("  Completed {0}  |  Failed {1}  |  Total {2}" -f $State.Summary.Completed, $State.Summary.Failed, (Format-DawoudUiDuration -Start $State.SessionStart)))
    }
    if ($State.Result) {
        [void]$lines.Add("RESULT")
        foreach ($line in @($State.Result -split "`r?`n" | Select-Object -First 5)) { [void]$lines.Add(("  {0}" -f (Limit-DawoudUiText -Text $line -Width ([math]::Max(10, $Width - 2))))) }
    }
    [void]$lines.Add(("Keys: :details :agents :tasks :files :log :help   Ctrl+L redraw   Ctrl+U clear   Ctrl+C cancel   Ctrl+Enter send"))
    if (($State.Status -eq "DONE" -or $State.Status -eq "FAILED") -and $State.Result) {
        $finalLines = [System.Collections.Generic.List[string]]::new()
        foreach ($line in @($lines | Select-Object -First 3)) { [void]$finalLines.Add($line) }
        [void]$finalLines.Add(($rule * $Width))
        [void]$finalLines.Add("TASKS")
        foreach ($task in @($State.Tasks | Select-Object -Last 6)) {
            $glyph = Get-DawoudUiGlyph -Status $task.Status -Unicode $State.Unicode
            [void]$finalLines.Add(("  {0} {1,-10} {2,-12} {3}" -f $glyph, $task.Id, $(if ($task.Agent) { $task.Agent } else { "queued" }), (Limit-DawoudUiText -Text $task.Summary -Width ([math]::Max(10, $Width - 42)))))
        }
        if ($State.Summary) {
            [void]$finalLines.Add("EXECUTION SUMMARY")
            [void]$finalLines.Add(("  User slices {0}  |  Internal/derived {1}  |  Total assignments {2}" -f $State.Summary.UserSlices, $State.Summary.InternalTasks, $State.Summary.TotalAssignments))
            [void]$finalLines.Add(("  Completed {0}  |  Failed {1}  |  Total {2}" -f $State.Summary.Completed, $State.Summary.Failed, (Format-DawoudUiDuration -Start $State.SessionStart)))
        }
        [void]$finalLines.Add("RESULT")
        foreach ($line in @($State.Result -split "`r?`n" | Select-Object -First 4)) { [void]$finalLines.Add(("  {0}" -f $line)) }
        [void]$finalLines.Add("TOKEN USAGE: Exact per-agent token metering unavailable.")
        [void]$finalLines.Add("Keys: :details :log :help   Ctrl+L redraw   Ctrl+U clear   Ctrl+C cancel")
        $lines = $finalLines
    }
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) { $lines[$lineIndex] = Limit-DawoudUiText -Text $lines[$lineIndex] -Width $Width }
    if ($lines.Count -gt $Height) {
        $trimmed = [System.Collections.Generic.List[string]]::new()
        foreach ($line in @($lines | Select-Object -First ($Height - 1))) { [void]$trimmed.Add($line) }
        [void]$trimmed.Add("... dashboard truncated to terminal height")
        $lines = $trimmed
    }
    @($lines)
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
            $agent.Action = if ($health -eq "WARNING") { "No executor event for 45s" } elseif ($health -eq "WAITING") { "No executor event for 12s" } else { "Executor event received" }
            [void](Add-DawoudUiEvent -State $State -Source $agent.Name -Kind $health -Message $agent.Action -Status $health -TaskId $agent.TaskId)
        }
    }
}

function Write-DawoudDashboard {
    param([Parameter(Mandatory)]$State, [switch]$Force)
    try {
        Pump-DawoudUiFileWatch -State $State
        Update-DawoudUiHealth -State $State
        $width = 100; $height = 34
        try { $width = [Console]::WindowWidth; $height = [Console]::WindowHeight } catch { }
        $lines = @(Get-DawoudUiLines -State $State -Width $width -Height $height)
        $fingerprint = [string]::Join("`n", $lines)
        $timerDue = ((Get-Date) - $State.LastRenderAt).TotalMilliseconds -ge 500
        if (-not $Force -and -not $timerDue -and $fingerprint -eq $State.LastRenderedFingerprint) { return }
        if ([Console]::IsOutputRedirected) { $redirectedAction = if ($State.CurrentAction) { $State.CurrentAction.Action } else { "idle" }; Write-Host ("DAWOUD {0}: {1}" -f $State.Status, $redirectedAction); return }
        if ($State.RenderTop -lt 0) { $State.RenderTop = [Console]::CursorTop }
        $rows = [math]::Max($State.RenderRows, $lines.Count)
        for ($i = 0; $i -lt $rows; $i++) {
            $y = [math]::Min([Console]::BufferHeight - 1, $State.RenderTop + $i)
            [Console]::SetCursorPosition(0, $y)
            $line = if ($i -lt $lines.Count) { Limit-DawoudUiText -Text $lines[$i] -Width ([math]::Max(1, $width - 1)) } else { "" }
            $color = if ($line -match "FAILED|FAIL|✕") { "Red" } elseif ($line -match "DONE|PASS|✓") { "Green" } elseif ($line -match "WAIT|START|RETRY|WARNING|▲|◐") { "Yellow" } elseif ($i -eq 0 -or $line -match "CURRENT ACTION|TASKS|FILES|AGENTS|LIVE ACTIVITY|RESULT|DETAILS|EVENT LOG|COMMAND|EXECUTION SUMMARY") { "Cyan" } else { "Gray" }
            Write-Host $line.PadRight([math]::Max(1, $width - 1)) -ForegroundColor $color -NoNewline
        }
        [Console]::SetCursorPosition(0, [math]::Min([Console]::BufferHeight - 1, $State.RenderTop + $lines.Count))
        $State.RenderRows = $lines.Count
        $State.LastRenderAt = Get-Date
        $State.LastRenderedFingerprint = $fingerprint
    } catch {
        $State.FailedRender = $true
        try { Write-Host ("DAWOUD status: {0}; dashboard unavailable; execution continues." -f $State.Status) -ForegroundColor Yellow } catch { }
    }
}

function Set-DawoudUiView {
    param([Parameter(Mandatory)]$State, [ValidateSet("NORMAL", "DETAILS", "AGENTS", "TASKS", "FILES", "LOG")][string]$View)
    $State.View = if ($View -in @("AGENTS", "TASKS", "FILES")) { "NORMAL" } else { $View }
    $State.LastRenderedFingerprint = ""
    Write-DawoudDashboard -State $State -Force
}

function Complete-DawoudUiSession {
    param([Parameter(Mandatory)]$State)
    Stop-DawoudUiFileWatch -State $State
    Pump-DawoudUiFileWatch -State $State
    $done = @($State.Tasks | Where-Object Status -eq "DONE").Count
    $failed = @($State.Tasks | Where-Object Status -eq "FAILED").Count
    $taskRecords = @()
    try { $taskRecords = @(Get-DawoudTelemetryRecords -TelemetryRoot $script:telemetryRoot -SessionId $State.SessionId | Where-Object { $_.WorkId -like "$($State.WorkId)-*" -and $_.RecordKind -eq "TASK" }) } catch { }
    $internal = [math]::Max(0, $taskRecords.Count - $State.Tasks.Count)
    $State.Summary = [pscustomobject]@{ UserSlices = $State.Tasks.Count; InternalTasks = $internal; TotalAssignments = $State.Tasks.Count + $internal; Completed = $done; Failed = $failed }
    $State.Status = if ($failed -gt 0) { "FAILED" } else { "DONE" }
    [void](Add-DawoudUiEvent -State $State -Source "DAWOUD" -Kind "SESSION" -Message ("Completed {0}/{1} user task slices" -f $done, $State.Tasks.Count) -Status $State.Status)
    Write-DawoudDashboard -State $State -Force
    if ($State.Result) {
        Write-Host ""
        Write-Host "RESULT" -ForegroundColor Cyan
        Write-Host (Protect-DawoudTelemetryText -Text $State.Result)
        if ($State.Summary) {
            Write-Host "EXECUTION SUMMARY" -ForegroundColor Cyan
            Write-Host ("User slices: {0} | Internal/derived tasks: {1} | Total executor assignments: {2}" -f $State.Summary.UserSlices, $State.Summary.InternalTasks, $State.Summary.TotalAssignments)
            Write-Host ("Completed: {0} | Failed: {1}" -f $State.Summary.Completed, $State.Summary.Failed)
        }
    }
}
