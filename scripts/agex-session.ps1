# AGEX session model: collaboration messages and local session history.
#
# Messages are only ever created from explicit, observable content: the user's
# request, leader plans and assignments, agent results, operational messages the
# agents chose to send, review/repair requests, and AGEX system events. AGEX
# never invents agent speech and never extracts hidden reasoning.

$script:AgexMessageTypes = @("ASSIGNMENT", "RESULT", "QUESTION", "ANSWER", "REVIEW", "REVISION_REQUEST", "STATUS", "SYSTEM")

function Add-AgexMessage {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$From,
        [Parameter(Mandatory)][string]$To,
        [Parameter(Mandatory)][ValidateSet("ASSIGNMENT", "RESULT", "QUESTION", "ANSWER", "REVIEW", "REVISION_REQUEST", "STATUS", "SYSTEM")][string]$Type,
        [AllowEmptyString()][string]$Text,
        [string]$TaskId = ""
    )
    if (-not $State -or -not $State.PSObject.Properties["Messages"]) { return }
    if ([string]::IsNullOrWhiteSpace($Text)) { return }
    $clean = Protect-AgexAuthoritativeText -Text $Text
    if ($clean.Length -gt 20000) { $clean = $clean.Substring(0, 20000) + "`n[truncated]" }
    [System.Threading.Monitor]::Enter($State.CollectionSync)
    try {
        $State.MessageSeq = [int]$State.MessageSeq + 1
        $message = [pscustomobject]@{
            Seq = [int]$State.MessageSeq
            MessageId = "msg-" + [guid]::NewGuid().ToString("N").Substring(0, 12)
            SessionId = [string]$State.WorkId
            From = $From; To = $To; TaskId = $TaskId; Type = $Type
            Text = $clean
            Timestamp = (Get-Date).ToUniversalTime().ToString("o")
        }
        [void]$State.Messages.Add($message)
        while ($State.Messages.Count -gt 2000) { $State.Messages.RemoveAt(0) }
    } finally { [System.Threading.Monitor]::Exit($State.CollectionSync) }
}

function ConvertTo-AgexMessageType {
    # Maps the executor mailbox vocabulary onto the AGEX message types.
    param([string]$Type)
    switch ($Type) {
        "QUESTION" { "QUESTION" } "ANSWER" { "ANSWER" } "REVIEW" { "REVIEW" } "RESULT" { "RESULT" }
        "REQUEST" { "REVISION_REQUEST" } "HANDOFF" { "ASSIGNMENT" } default { "STATUS" }
    }
}

function Get-AgexSessionFolder { Get-AgexPath Sessions }

function Get-AgexTaskRecord {
    param($Task)
    [pscustomobject]@{
        TaskId = [string]$Task.Id; Title = [string]$Task.Summary; Description = [string]$Task.Task
        AssignedAgent = [string]$Task.Agent; State = [string]$Task.Status
        Dependencies = @($Task.Dependencies); Error = [string]$Task.Error
        Result = (Get-AgexExecutorResultText -Text ([string]$Task.Result))
        CreatedAt = $(if ($Task.CreatedAt -and $Task.CreatedAt -ne [datetime]::MinValue) { ([datetime]$Task.CreatedAt).ToUniversalTime().ToString("o") } else { "" })
        StartedAt = $(if ($Task.Started -and $Task.Started -ne [datetime]::MinValue) { ([datetime]$Task.Started).ToUniversalTime().ToString("o") } else { "" })
        CompletedAt = $(if ($Task.End -and $Task.End -ne [datetime]::MinValue) { ([datetime]$Task.End).ToUniversalTime().ToString("o") } else { "" })
    }
}

function Save-AgexSessionRecord {
    # Writes the session to %LOCALAPPDATA%\AGEX\sessions\<id>.json. Never fatal.
    param([Parameter(Mandatory)]$State, $Runtime, [int]$MaxSessions = 200)
    if (-not $State.WorkId) { return }
    try {
        $snapshot = New-AgexUiRenderSnapshot -State $State
        $runs = @()
        if ($Runtime -and $Runtime.Executions) {
            $runs = @($Runtime.Executions.ToArray() | Where-Object { $_.RequestId -and ([string]$_.RequestId).StartsWith([string]$State.WorkId) } | ForEach-Object {
                [pscustomobject]@{ Agent = $_.Executor; Label = $_.Label; Command = $_.CommandLine; Outcome = $_.Outcome; ExitCode = $_.ExitCode; DurationSeconds = $_.DurationSeconds; StartedAt = $_.StartedAt.ToUniversalTime().ToString("o"); Summary = $_.Summary }
            })
        }
        $changes = @()
        if ($null -ne $snapshot.GitChanges) { $changes = @($snapshot.GitChanges | ForEach-Object { [pscustomobject]@{ Action = $_.Action; Path = $_.Path; Lines = $_.Lines } }) }
        $outcome = $snapshot.Outcome
        $record = [ordered]@{
            SchemaVersion = 1
            SessionId = [string]$State.WorkId
            Project = [string]$State.Project
            ProjectName = [string]$State.ProjectName
            Request = [string]$State.Prompt
            Leader = [string]$State.ResolvedLeader
            ConfiguredLeader = [string]$State.ConfiguredLeader
            StartedAt = $(if ($State.RequestStarted -ne [datetime]::MinValue) { $State.RequestStarted.ToUniversalTime().ToString("o") } else { "" })
            SavedAt = (Get-Date).ToUniversalTime().ToString("o")
            Status = [string]$State.Status
            Outcome = $outcome
            Result = [string]$State.Result
            AgentsUsed = @($runs | ForEach-Object { $_.Agent } | Where-Object { $_ } | Select-Object -Unique)
            Tasks = @($snapshot.Tasks | ForEach-Object { Get-AgexTaskRecord -Task $_ })
            Messages = @($snapshot.Messages)
            Events = @($snapshot.Events | Where-Object { $_.Source -eq 'AGEX' -or $_.Kind -eq 'SESSION' } | ForEach-Object { [pscustomobject]@{ At = $_.At.ToUniversalTime().ToString("o"); Kind = $_.Kind; Message = $_.Message; TaskId = $_.TaskId } })
            AgentRuns = @($runs)
            Changes = @($changes)
        }
        $folder = Get-AgexSessionFolder
        $path = Join-Path $folder ("{0}.json" -f (Get-SafeId -Value $State.WorkId))
        $temp = "$path.$PID.tmp"
        [IO.File]::WriteAllText($temp, ($record | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temp -Destination $path -Force
        $all = @(Get-ChildItem -LiteralPath $folder -Filter "*.json" -File | Sort-Object LastWriteTime -Descending)
        if ($all.Count -gt $MaxSessions) { $all | Select-Object -Skip $MaxSessions | Remove-Item -Force -ErrorAction SilentlyContinue }
    } catch {
        if ($Runtime) { Write-AgexLog -Runtime $Runtime -Event 'session_save_warning' -Data @{ error = $_.Exception.Message } }
    }
}

function Get-AgexSessionList {
    param([int]$Limit = 100)
    $folder = Get-AgexSessionFolder
    @(Get-ChildItem -LiteralPath $folder -Filter "*.json" -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First $Limit | ForEach-Object {
        try {
            $data = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $firstLine = ([string]$data.Request -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
            [pscustomobject]@{ SessionId = $data.SessionId; StartedAt = $data.StartedAt; SavedAt = $data.SavedAt; Status = $data.Status; ProjectName = $data.ProjectName; Project = $data.Project; Request = [string]$firstLine; Tasks = @($data.Tasks).Count; Messages = @($data.Messages).Count; Leader = $data.Leader }
        } catch { }
    })
}

function Get-AgexSessionRecord {
    param([Parameter(Mandatory)][string]$SessionId)
    $path = Join-Path (Get-AgexSessionFolder) ("{0}.json" -f (Get-SafeId -Value $SessionId))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
}
