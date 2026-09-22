# DAWOUD shared preferences, model discovery, and routing helpers.
# Stores only non-secret launcher preferences.

function Get-SafeId {
    param([string]$Value)
    $safe = [string]$Value -replace "[^A-Za-z0-9_.-]", "_"
    if ([string]::IsNullOrWhiteSpace($safe)) { return "unknown" }
    if ($safe.Length -gt 100) { return $safe.Substring(0, 100) }
    $safe
}

function Get-DawoudRuntimeIdentity {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $groups = @()
    foreach ($sid in @($identity.Groups)) {
        try { $groups += $sid.Translate([Security.Principal.NTAccount]).Value } catch { }
    }
    $restricted = ([string]$identity.Name -match '(?i)CodexSandboxOffline|CodexSandboxUsers') -or (@($groups) -match '(?i)CodexSandboxOffline|CodexSandboxUsers')
    [pscustomobject]@{
        Name = [string]$identity.Name
        Restricted = [bool]$restricted
        Groups = @($groups)
    }
}

function Write-DawoudRuntimeIdentityNotice {
    $runtime = Get-DawoudRuntimeIdentity
    Write-Host ("DAWOUD process identity: {0}" -f $runtime.Name) -ForegroundColor DarkGray
    if ($runtime.Restricted) {
        Write-Host "WARNING:" -ForegroundColor Yellow
        Write-Host "DAWOUD is running inside Codex's restricted sandbox." -ForegroundColor Yellow
        Write-Host "Executor integration results may be invalid." -ForegroundColor Yellow
        Write-Host "Run DAWOUD from a normal user PowerShell for production validation." -ForegroundColor Yellow
    }
    $runtime
}

function Get-DawoudPreferences {
    param([Parameter(Mandatory)][string]$Path)
    $defaults = [ordered]@{
        version = 2
        last_project = ""
        last_modes = @()
        leader = "Codex"
        codex_share = 20
        antigravity_share = 80
        codex_model = ""
        codex_effort = ""
        antigravity_model = ""
        antigravity_effort = ""
    }
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        try {
            $saved = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
            foreach ($key in @($defaults.Keys)) {
                if ($null -ne $saved.PSObject.Properties[$key]) { $defaults[$key] = $saved.$key }
            }
        } catch {
            Write-Host "DAWOUD settings unreadable. Defaults loaded." -ForegroundColor Yellow
        }
    }
    $defaults.last_modes = @($defaults.last_modes | ForEach-Object { [string]$_ })
    $defaults.leader = switch ([string]$defaults.leader) { "Antigravity" { "Antigravity" } "Auto" { "Auto" } default { "Codex" } }
    try { $defaults.codex_share = [int]$defaults.codex_share } catch { $defaults.codex_share = 20 }
    try { $defaults.antigravity_share = [int]$defaults.antigravity_share } catch { $defaults.antigravity_share = 80 }
    if ($defaults.codex_share -lt 0 -or $defaults.codex_share -gt 100 -or $defaults.antigravity_share -lt 0 -or $defaults.antigravity_share -gt 100 -or ($defaults.codex_share + $defaults.antigravity_share -ne 100)) {
        $defaults.codex_share = 20
        $defaults.antigravity_share = 80
    }
    [pscustomobject]$defaults
}

function Save-DawoudPreferences {
    param([Parameter(Mandatory)]$Preferences, [Parameter(Mandatory)][string]$Path)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $safe = [ordered]@{
        version = 2
        last_project = [string]$Preferences.last_project
        last_modes = @($Preferences.last_modes)
        leader = [string]$Preferences.leader
        codex_share = [int]$Preferences.codex_share
        antigravity_share = [int]$Preferences.antigravity_share
        codex_model = [string]$Preferences.codex_model
        codex_effort = [string]$Preferences.codex_effort
        antigravity_model = [string]$Preferences.antigravity_model
        antigravity_effort = [string]$Preferences.antigravity_effort
    }
    $safe | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-ConfigModel {
    param([Parameter(Mandatory)][string]$ConfigPath)
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { return "" }
    $match = Select-String -LiteralPath $ConfigPath -Pattern '^\s*model\s*=\s*["''](?<model>[^"'']+)["'']' | Select-Object -First 1
    if ($match) { return [string]$match.Matches[0].Groups['model'].Value }
    ""
}

function Receive-DawoudWebSocketJson {
    param([Parameter(Mandatory)]$Socket, [Parameter(Mandatory)]$CancellationToken)
    $buffer = New-Object byte[] 65536
    $stream = [System.IO.MemoryStream]::new()
    do {
        $receive = $Socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $CancellationToken).GetAwaiter().GetResult()
        $stream.Write($buffer, 0, $receive.Count)
    } while (-not $receive.EndOfMessage)
    [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
}

function Send-DawoudWebSocketJson {
    param([Parameter(Mandatory)]$Socket, [Parameter(Mandatory)]$Payload, [Parameter(Mandatory)]$CancellationToken)
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Payload | ConvertTo-Json -Compress -Depth 12))
    $Socket.SendAsync([ArraySegment[byte]]::new($bytes), [Net.WebSockets.WebSocketMessageType]::Text, $true, $CancellationToken).GetAwaiter().GetResult() | Out-Null
}

function Get-CodexModelCatalog {
    param([Parameter(Mandatory)][string]$CodexConfigPath)
    $socket = $null
    $cts = [Threading.CancellationTokenSource]::new(2500)
    try {
        $socket = [Net.WebSockets.ClientWebSocket]::new()
        $socket.ConnectAsync([Uri]"ws://127.0.0.1:10106", $cts.Token).GetAwaiter().GetResult() | Out-Null
        Send-DawoudWebSocketJson -Socket $socket -CancellationToken $cts.Token -Payload @{ jsonrpc = "2.0"; id = 1; method = "initialize"; params = @{ clientInfo = @{ name = "dawoud"; version = "1" }; capabilities = @{} } }
        $request = $null
        for ($i = 0; $i -lt 12; $i++) {
            $message = Receive-DawoudWebSocketJson -Socket $socket -CancellationToken $cts.Token
            if ($message.id -eq 1) {
                Send-DawoudWebSocketJson -Socket $socket -CancellationToken $cts.Token -Payload @{ jsonrpc = "2.0"; id = 2; method = "model/list"; params = @{ includeHidden = $false; limit = 100 } }
            }
            if ($message.id -eq 2) { $request = $message.result; break }
        }
        $models = [System.Collections.Generic.List[object]]::new()
        foreach ($item in @($request.data)) {
            if ($item.hidden) { continue }
            $efforts = @($item.supportedReasoningEfforts | ForEach-Object {
                if ($_ -is [string] -and $_ -match 'reasoningEffort=([^;}]*)') { $Matches[1].Trim() }
                elseif ($_.reasoningEffort) { [string]$_.reasoningEffort }
                elseif ($_ -is [string]) { [string]$_ }
            } | Where-Object { $_ })
            [void]$models.Add([pscustomobject]@{ Id = [string]$item.id; Name = if ($item.displayName) { [string]$item.displayName } else { [string]$item.id }; Efforts = $efforts; DefaultEffort = [string]$item.defaultReasoningEffort; IsDefault = [bool]$item.isDefault })
        }
        if ($models.Count -gt 0) { return @($models) }
    } catch { }
    finally {
        if ($socket) { $socket.Dispose() }
        $cts.Dispose()
    }
    $fallback = Get-ConfigModel -ConfigPath $CodexConfigPath
    if ($fallback) { return @([pscustomobject]@{ Id = $fallback; Name = $fallback; Efforts = @(); DefaultEffort = ""; IsDefault = $true }) }
    @()
}

function Resolve-AgyExecutable {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($env:USERPROFILE) { [void]$candidates.Add((Join-Path $env:USERPROFILE "AppData\Local\agy\bin\agy.exe")) }
    if ($env:LOCALAPPDATA) { [void]$candidates.Add((Join-Path $env:LOCALAPPDATA "agy\bin\agy.exe")) }
    $tool = Get-Command agy -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($tool -and $tool.Source) { [void]$candidates.Add([string]$tool.Source) }
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    ""
}

function Invoke-DawoudAgyStream {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AgyPath,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Prompt,
        [string]$Model,
        [string]$Effort,
        [string]$CliLogPath,
        [string]$StdinPath,
        [string]$RawStdoutPath,
        [string]$RawStderrPath,
        [string]$EventLogPath,
        [int]$StartupTimeoutSeconds = 45,
        [int]$IdleTimeoutSeconds = 180,
        [int]$TotalTimeoutSeconds = 720,
        [scriptblock]$OnMilestone
    )

    $args = @(
        "--log-file", $CliLogPath,
        "--input-format", "stream-json",
        "--output-format", "stream-json",
        "--sandbox",
        "--dangerously-skip-permissions",
        "--print-timeout", "10m"
    )
    if ($Model) { $args += @("--model", $Model) }
    if ($Effort) { $args += @("--effort", $Effort) }
    $payload = ([ordered]@{
        message = ([ordered]@{ content = $Prompt })
        event = "user"
    } | ConvertTo-Json -Compress -Depth 8)
    $payloadBytes = [Text.Encoding]::UTF8.GetBytes($payload + "`n")
    $proc = [Diagnostics.Process]::new()
    $started = Get-Date
    $stdoutLines = [System.Collections.Generic.List[string]]::new()
    $stderrLines = [System.Collections.Generic.List[string]]::new()
    $readTask = $null
    $stderrTask = $null
    $result = $null
    $failureReason = ""
    $timeoutReason = "NONE"
    $failedStage = ""
    $currentStage = ""
    $exceptionType = ""
    $exceptionMessage = ""
    $firstStdout = "NONE"
    $firstStderr = "NONE"
    $eventCount = 0
    $finalResult = $false
    $stdinWritten = $false
    $processStarted = $false

    $milestone = {
        param([string]$Name)
        if ($OnMilestone) { & $OnMilestone $Name $(if ($proc) { $proc.Id } else { 0 }) }
    }
    $writeTransportFile = {
        param([string]$Path, [byte[]]$Bytes)
        if ([string]::IsNullOrWhiteSpace($Path)) { return }
        $parent = Split-Path -Parent $Path
        if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        [IO.File]::WriteAllBytes($Path, $Bytes)
    }
    try {
        $currentStage = "RESOLVE_AGY"
        if (-not (Test-Path -LiteralPath $AgyPath -PathType Leaf)) { throw "AGY executable not found: $AgyPath" }
        if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) { throw "AGY working directory not found: $WorkingDirectory" }
        if ($CliLogPath) {
            $cliParent = Split-Path -Parent $CliLogPath
            if ($cliParent) { New-Item -ItemType Directory -Path $cliParent -Force | Out-Null }
        }
        $currentStage = "CREATE_PROCESS_START_INFO"
        & $milestone "CREATE_PROCESS_START_INFO"
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $AgyPath
        $psi.WorkingDirectory = $WorkingDirectory
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        if ($psi.PSObject.Properties.Name -contains "ArgumentList" -and $null -ne $psi.ArgumentList) {
            foreach ($arg in $args) { [void]$psi.ArgumentList.Add([string]$arg) }
        } else {
            $psi.Arguments = (($args | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join ' ')
        }
        $proc.StartInfo = $psi
        $currentStage = "START_PROCESS"
        & $milestone "START_PROCESS"
        [void]$proc.Start()
        $processStarted = $true
        $currentStage = "START_STDOUT_READER"
        & $milestone "AGY_PROCESS_STARTED"

        $currentStage = "START_STDOUT_READER"
        & $milestone "START_STDOUT_READER"
        $readTask = $proc.StandardOutput.ReadLineAsync()
        $currentStage = "START_STDERR_READER"
        & $milestone "START_STDERR_READER"
        $stderrTask = $proc.StandardError.ReadToEndAsync()

        $currentStage = "WRITE_STDIN"
        & $milestone "WRITE_STDIN"
        & $writeTransportFile $StdinPath $payloadBytes
        $proc.StandardInput.WriteLine($payload)
        $stdinWritten = $true
        & $milestone "STDIN_WRITTEN"
        $currentStage = "FLUSH_STDIN"
        & $milestone "FLUSH_STDIN"
        $proc.StandardInput.Flush()
        & $milestone "STDIN_FLUSHED"
        $currentStage = "WAIT_FIRST_EVENT"

        $deadline = (Get-Date).AddSeconds($TotalTimeoutSeconds)
        $startupDeadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
        $lastEventAt = Get-Date
        while ($null -eq $result) {
            $now = Get-Date
            if ($now -ge $deadline) { $timeoutReason = "TOTAL"; $failureReason = "AGY total task timeout after $TotalTimeoutSeconds seconds."; break }
            if ($eventCount -eq 0 -and $now -ge $startupDeadline) { $timeoutReason = "STARTUP"; $failureReason = "AGY startup timeout after $StartupTimeoutSeconds seconds without a stream event."; break }
            if ($eventCount -gt 0 -and ($now - $lastEventAt).TotalSeconds -ge $IdleTimeoutSeconds) { $timeoutReason = "IDLE"; $failureReason = "AGY idle timeout after $IdleTimeoutSeconds seconds without a stream event."; break }
            if (-not $readTask.Wait(1000)) {
                if ($proc.HasExited) { break }
                continue
            }
            $line = $readTask.Result
            if ($null -eq $line) { break }
            if ($firstStdout -eq "NONE") { $firstStdout = Protect-DawoudTelemetryText -Text $line; & $milestone "FIRST_STDOUT_EVENT" }
            [void]$stdoutLines.Add((Protect-DawoudTelemetryText -Text $line))
            if ($RawStdoutPath) { Add-Content -LiteralPath $RawStdoutPath -Value (Protect-DawoudTelemetryText -Text $line) -Encoding utf8 }
            if ([string]::IsNullOrWhiteSpace($line)) { $readTask = $proc.StandardOutput.ReadLineAsync(); continue }
            try {
                $event = $line | ConvertFrom-Json
                $eventCount++
                $lastEventAt = Get-Date
                if ($EventLogPath) { Add-Content -LiteralPath $EventLogPath -Value ("{0} event={1}" -f (Get-Date).ToUniversalTime().ToString("o"), [string]$event.event) -Encoding utf8 }
                if ($event.event -eq "result") { $result = $event.result; $finalResult = $true; & $milestone "FINAL_RESULT_EVENT" }
                elseif ($event.event -eq "error" -and $event.error) { $failureReason = Protect-DawoudTelemetryText -Text ([string]$event.error) }
            } catch {
                if ($EventLogPath) { Add-Content -LiteralPath $EventLogPath -Value ("{0} event=NON_JSON" -f (Get-Date).ToUniversalTime().ToString("o")) -Encoding utf8 }
            }
            if ($null -eq $result) { $readTask = $proc.StandardOutput.ReadLineAsync() }
        }

        if ($null -eq $result -and -not $proc.HasExited) {
            if ($timeoutReason -eq "NONE") { $timeoutReason = "PROCESS_EXIT_WITHOUT_RESULT"; $failureReason = "AGY exited without a final result event." }
            try { $proc.StandardInput.Close() } catch { }
            try { $proc.Kill() } catch { }
            try { [void]$proc.WaitForExit(5000) } catch { }
        } elseif ($null -ne $result -and -not $proc.HasExited) {
            $proc.StandardInput.Close()
            if (-not $proc.WaitForExit(5000)) {
                $timeoutReason = "SHUTDOWN"
                $failureReason = "AGY returned a final result but did not exit after stdin close."
                try { $proc.Kill(); [void]$proc.WaitForExit(5000) } catch { }
            }
        }
        if ($stderrTask -and $stderrTask.Wait(5000)) {
            $stderr = [string]$stderrTask.Result
        } else {
            if ($timeoutReason -eq "NONE") { $timeoutReason = "PIPE" }
            if (-not $failureReason) { $failureReason = "AGY stderr pipe did not close within 5 seconds." }
            try { if ($proc.Id) { & taskkill.exe /PID ([string]$proc.Id) /T /F 2>$null | Out-Null } } catch { }
            $stderr = ""
        }
        if ($stderr.Trim()) { [void]$stderrLines.Add((Protect-DawoudTelemetryText -Text $stderr.Trim())) }
        if ($RawStderrPath) { Set-Content -LiteralPath $RawStderrPath -Value ($stderrLines -join "`n") -Encoding utf8 }
        if ($proc.HasExited -and $proc.ExitCode -ne 0 -and -not $failureReason) { $failureReason = "AGY exited with code $($proc.ExitCode)." }
    } catch {
        $failedStage = if ($failedStage) { $failedStage } elseif ($currentStage) { $currentStage } else { "LAUNCH_SEQUENCE" }
        $exceptionType = $_.Exception.GetType().FullName
        $exceptionMessage = Protect-DawoudTelemetryText -Text $_.Exception.Message
        $failureReason = $exceptionMessage
    } finally {
        if ($processStarted -and -not $proc.HasExited) { try { & taskkill.exe /PID ([string]$proc.Id) /T /F 2>$null | Out-Null } catch { } }
    }
    $exitCode = if ($proc.HasExited) { $proc.ExitCode } else { 124 }
    $finalResponse = if ($result -and $result.response) { [string]$result.response } else { "" }
    if (-not $finalResult -and $exitCode -eq 0) {
        $failedStage = "WAIT_FINAL_RESULT"
        if (-not $failureReason) { $failureReason = "Process exited without a final AGY result event." }
    }
    $success = $processStarted -and $stdinWritten -and $eventCount -gt 0 -and $finalResult -and -not [string]::IsNullOrWhiteSpace($finalResponse) -and $exitCode -eq 0 -and $timeoutReason -eq "NONE"
    [pscustomobject]@{
        Contract = "DAWOUD_AGY_RESULT_V1"; Success = $success; ActualExe = $AgyPath; Arguments = @($args); ActualPid = if ($processStarted) { $proc.Id } else { 0 }; ProcessStarted = $processStarted
        StdinWritten = $stdinWritten; StdinBytes = $payloadBytes.Length; FirstStdoutEvent = $firstStdout; FirstRawStdout = $firstStdout; FirstStderrEvent = if ($stderrLines.Count) { $stderrLines[0] } else { "NONE" }
        StreamEvents = $eventCount; FinalResultEvent = $finalResult; FinalResponse = $finalResponse; Result = $result; ExitCode = $exitCode; TimeoutReason = $timeoutReason
        FailedStage = $failedStage; ExceptionType = $exceptionType; Error = $failureReason; ExceptionMessage = $exceptionMessage
        DurationSeconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 2); Stdout = ($stdoutLines -join "`n"); Stderr = ($stderrLines -join "`n")
    }
}

function Get-DawoudAgyAvailability {
    param([string]$RequestedModel)
    $path = Resolve-AgyExecutable
    $result = [ordered]@{
        Available = $false
        Path = $path
        Version = ""
        ModelAvailable = $false
        Reason = ""
        VersionExitCode = $null
        ModelsExitCode = $null
    }
    if ([string]::IsNullOrWhiteSpace($path)) {
        $result.Reason = "canonical agy.exe not found at $env:USERPROFILE\AppData\Local\agy\bin\agy.exe and no PATH executable resolved"
        return [pscustomobject]$result
    }
    try {
        $versionOutput = (& $path --version 2>&1 | Out-String).Trim()
        $result.VersionExitCode = $LASTEXITCODE
        $result.Version = Protect-DawoudTelemetryText -Text $versionOutput
        if ($result.VersionExitCode -ne 0) { $result.Reason = "agy.exe --version failed with exit code $($result.VersionExitCode): $($result.Version)"; return [pscustomobject]$result }
    } catch {
        $result.Reason = "agy.exe could not start for --version: $($_.Exception.Message)"
        return [pscustomobject]$result
    }
    try {
        $probeLog = Join-Path $env:TEMP ("dawoud-agy-probe-" + $PID + ".log")
        $modelsOutput = (& $path --log-file $probeLog models 2>&1 | Out-String).Trim()
        $result.ModelsExitCode = $LASTEXITCODE
        if ($result.ModelsExitCode -ne 0) {
            $result.Reason = "agy.exe models failed with exit code $($result.ModelsExitCode): $(Protect-DawoudTelemetryText -Text $modelsOutput)"
            return [pscustomobject]$result
        }
        $result.ModelAvailable = [string]::IsNullOrWhiteSpace($RequestedModel) -or ($modelsOutput -match [regex]::Escape($RequestedModel))
        if (-not $result.ModelAvailable) {
            $result.Reason = "selected AGY model '$RequestedModel' was not returned by agy models"
            return [pscustomobject]$result
        }
        $result.Available = $true
        return [pscustomobject]$result
    } catch {
        $result.Reason = "agy.exe models could not verify authentication/profile state: $($_.Exception.Message)"
        return [pscustomobject]$result
    }
}

function Get-AntigravityModelCatalog {
    $agy = Resolve-AgyExecutable
    if (-not $agy) { return @() }
    try {
        $probeLog = Join-Path $env:TEMP ("dawoud-agy-catalog-" + $PID + ".log")
        $raw = (& $agy --log-file $probeLog models 2>$null | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($raw)) { return @() }
        $models = [System.Collections.Generic.List[object]]::new()
        foreach ($line in ($raw -split "`r?`n")) {
            $clean = ($line -replace "\x1b\[[0-9;]*[A-Za-z]", "").Trim()
            if (-not $clean -or $clean -match '^(ERROR|WARNING|INFO|E\d{8}|W\d{8}|I\d{8}|Fetching|Please|Usage|Available|Auth|Error)') { continue }
            if ($clean -match '^(?<id>[A-Za-z0-9][A-Za-z0-9_.:/-]{2,})(?:\s+|`t+)(?<name>.+)$') {
                [void]$models.Add([pscustomobject]@{ Id = $Matches.id; Name = $Matches.name.Trim(); Efforts = @("low", "medium", "high"); DefaultEffort = "medium" })
            }
        }
        @($models | Sort-Object Id -Unique)
    } catch { @() }
}

function Get-AntigravityEfforts {
    $agy = Resolve-AgyExecutable
    if (-not $agy) { return @() }
    try {
        $help = (& $agy --help 2>&1 | Out-String)
        if ($help -match '--effort') { return @("low", "medium", "high") }
    } catch { }
    @()
}

function Get-DawoudTaskCategory {
    param([Parameter(Mandatory)][string]$Task)
    $text = $Task.ToLowerInvariant()
    if ($text -match 'architecture|plan|design|break down|strategy') { return "PLANNING" }
    if ($text -match 'debug|failure|bug|crash|regression|root cause') { return "DEBUGGING" }
    if ($text -match 'browser|ui|frontend|screenshot|click|visual') { return "UI/BROWSER" }
    if ($text -match 'test|verify|benchmark|lint|doctor') { return "TESTING" }
    if ($text -match 'review|audit|inspect|check') { return "REVIEW" }
    if ($text -match 'research|look up|documentation|docs') { return "RESEARCH" }
    if ($text -match 'repeat|bulk|rename|format|migrate|mechanical') { return "REPETITIVE WORK" }
    if ($text -match 'document|readme|report') { return "DOCUMENTATION" }
    "CODING"
}

function Resolve-DawoudLeader {
    param(
        [ValidateSet("Codex", "Antigravity", "Auto")][string]$ConfiguredLeader = "Codex",
        [int]$CodexShare = 20,
        [int]$AntigravityShare = 80
    )
    if ($ConfiguredLeader -ne "Auto") { return [string]$ConfiguredLeader }
    if ($AntigravityShare -gt $CodexShare) { return "Antigravity" }
    "Codex"
}

function Get-DawoudRouteDecision {
    param(
        [Parameter(Mandatory)][string]$Task,
        [string]$Leader = "Codex",
        [int]$CodexShare = 20,
        [int]$AntigravityShare = 80,
        [string]$TelemetryRoot,
        [string]$SessionId
    )
    $category = Get-DawoudTaskCategory -Task $Task
    $reason = ""
    $agent = "Codex"
    $independentSlice = $Task -match "DAWOUD independent work slice"
    $highRisk = @("PLANNING", "REVIEW") -contains $category
    $delegable = @("RESEARCH", "UI/BROWSER", "TESTING", "DOCUMENTATION", "REPETITIVE WORK", "CODING") -contains $category
    if ($category -eq "DEBUGGING" -and $independentSlice) { $delegable = $true }
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($Task))
        $bucket = [BitConverter]::ToUInt32($bytes, 0) % 100
    } finally { $hash.Dispose() }
    if ($highRisk) { $agent = "Codex"; $reason = "Codex retains high-risk $category work." }
    elseif (-not $delegable) { $agent = "Codex"; $reason = "Task category $category is not safely delegable by default." }
    elseif ($Leader -eq "Auto" -and $category -eq "UI/BROWSER" -and $AntigravityShare -gt 0) { $agent = "Antigravity"; $reason = "Auto routes tool-heavy UI/BROWSER work to Antigravity." }
    elseif ($TelemetryRoot -and $SessionId -and $AntigravityShare -gt $CodexShare) {
        $budget = Get-DawoudWorkloadBudget -TelemetryRoot $TelemetryRoot -SessionId $SessionId -CodexShare $CodexShare -AntigravityShare $AntigravityShare
        if ($budget.AntigravityAssigned -lt $budget.AntigravityTargetCount) { $agent = "Antigravity"; $reason = "Running workload budget is below AGY target ($($budget.AntigravityAssigned)/$($budget.AntigravityTargetCount) assigned slices)." }
        elseif ($Leader -eq "Antigravity" -and $bucket -lt [math]::Max(50, $AntigravityShare)) { $agent = "Antigravity"; $reason = "Antigravity leadership plus workload policy selected bucket $bucket." }
        elseif ($bucket -lt $AntigravityShare) { $agent = "Antigravity"; $reason = "Workload policy selected bucket $bucket below AGY target $AntigravityShare." }
        else { $agent = "Codex"; $reason = "Running workload budget reached AGY target; Codex retains this slice." }
    }
    elseif ($Leader -eq "Antigravity" -and $bucket -lt [math]::Max(50, $AntigravityShare)) { $agent = "Antigravity"; $reason = "Antigravity leadership plus workload policy selected bucket $bucket." }
    elseif ($bucket -lt $AntigravityShare) { $agent = "Antigravity"; $reason = "Workload policy selected bucket $bucket below AGY target $AntigravityShare." }
    elseif ($CodexShare -ge 70) { $agent = "Codex"; $reason = "Workload policy selected bucket $bucket for Codex-heavy target." }
    else { $agent = "Codex"; $reason = "Workload policy retained task for Codex at bucket $bucket." }
    [pscustomobject]@{ Agent = $agent; Category = $category; Reason = $reason; CodexShare = $CodexShare; AntigravityShare = $AntigravityShare; Bucket = $bucket; Delegable = $delegable }
}

function Get-DawoudTaskSlices {
    param([Parameter(Mandatory)][string]$Task, [int]$MaxSlices = 8)
    $clean = ($Task -replace "\r", "").Trim()
    $lines = @($clean -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $marked = @($lines | Where-Object { $_ -match '^(?:[-*•]|(?:slice\s+)?\d+[:.)]|#{1,6}\s+)' })
    if ($clean.Length -lt 800 -and $marked.Count -lt 2) { return @([pscustomobject]@{ Id = "slice-1"; Summary = "whole task"; Task = $clean; Decomposed = $false }) }
    $chunks = [System.Collections.Generic.List[string]]::new()
    $inlineNumbered = @([regex]::Split($clean, '(?i)(?=(?:\bslice\s+\d+|(?<!slice )\b\d+)[:.)]\s+)') | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -ge 80 })
    if ($inlineNumbered.Count -ge 3 -and $inlineNumbered[0] -notmatch '^(?i)(?:slice\s+\d+|\d+)[:.)]\s+') {
        $prefix = $inlineNumbered[0]
        $inlineNumbered = @($inlineNumbered | Select-Object -Skip 1)
        $inlineNumbered[0] = "$prefix`n$($inlineNumbered[0])"
    }
    if ($inlineNumbered.Count -ge 2) {
        foreach ($chunk in $inlineNumbered | Select-Object -First $MaxSlices) { [void]$chunks.Add($chunk) }
    } elseif ($marked.Count -ge 2) {
        $current = [System.Text.StringBuilder]::new()
        foreach ($line in $lines) {
            if ($line -match '^(?:[-*•]|(?:slice\s+)?\d+[:.)]|#{1,6}\s+)' -and $current.Length -gt 0) { [void]$chunks.Add($current.ToString().Trim()); [void]$current.Clear() }
            [void]$current.AppendLine($line)
        }
        if ($current.Length -gt 0) { [void]$chunks.Add($current.ToString().Trim()) }
    } else {
        foreach ($paragraph in @($clean -split "`n\s*`n" | Where-Object { $_.Trim() })) { [void]$chunks.Add($paragraph.Trim()) }
    }
    $usable = @($chunks | Where-Object { $_.Length -ge 80 } | Select-Object -First $MaxSlices)
    if ($usable.Count -lt 2) { return @([pscustomobject]@{ Id = "slice-1"; Summary = "whole task; no safe independent boundary"; Task = $clean; Decomposed = $false }) }
    $index = 0
    @($usable | ForEach-Object {
        $index++
        $summary = (($_ -replace "\s+", " ").Trim())
        if ($summary.Length -gt 140) { $summary = $summary.Substring(0, 140) + "..." }
        [pscustomobject]@{
            Id = "slice-$index"
            Summary = $summary
            Task = "DAWOUD independent work slice $index of $($usable.Count). Work only on this slice. Return concrete result, files changed, tests run, and blockers.`n`n$_"
            Decomposed = $true
        }
    })
}

function Get-DawoudWorkloadBudget {
    param([Parameter(Mandatory)][string]$TelemetryRoot, [Parameter(Mandatory)][string]$SessionId, [int]$CodexShare, [int]$AntigravityShare)
    $records = @(Get-DawoudTelemetryRecords -TelemetryRoot $TelemetryRoot -SessionId $SessionId | Where-Object { $_.RecordKind -eq "TASK" })
    $assigned = $records.Count
    $agy = @($records | Where-Object Executor -eq "ANTIGRAVITY").Count
    $targetCount = if ($assigned -lt 1) { 1 } else { [math]::Ceiling(($assigned + 1) * ($AntigravityShare / 100)) }
    [pscustomobject]@{ TotalAssigned = $assigned; AntigravityAssigned = $agy; CodexAssigned = @($records | Where-Object Executor -in @("CODEX", "CODEX_SUBAGENT")).Count; AntigravityTargetCount = $targetCount; CodexShare = $CodexShare; AntigravityShare = $AntigravityShare }
}

function Protect-DawoudTelemetryText {
    param([AllowEmptyString()][string]$Text)
    if ($null -eq $Text) { return "" }
    $safe = $Text -replace '(?i)(api[_-]?key|token|password|secret|cookie|authorization)\s*[:=]\s*[^\s,;]+', '$1=<redacted>'
    $safe = $safe -replace '(?i)(bearer\s+)[^\s,;]+', '$1<redacted>'
    if ($safe.Length -gt 240) { $safe = $safe.Substring(0, 240) + "..." }
    $safe
}

function Write-DawoudTelemetryJson {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Json,
        [int]$Attempts = 4
    )
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $encoding = [Text.UTF8Encoding]::new($false)
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $tempPath = "$Path.$PID.$([guid]::NewGuid().ToString('N')).tmp"
        try {
            [IO.File]::WriteAllText($tempPath, $Json, $encoding)
            Move-Item -LiteralPath $tempPath -Destination $Path -Force -ErrorAction Stop
            return $true
        } catch {
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
            if ($attempt -lt $Attempts) { Start-Sleep -Milliseconds (150 * $attempt) }
        }
    }
    return $false
}

function New-DawoudTelemetryRecord {
    param(
        [Parameter(Mandatory)][string]$TelemetryRoot,
        [Parameter(Mandatory)][string]$SessionId,
        [Parameter(Mandatory)][string]$Task,
        [Parameter(Mandatory)][ValidateSet("CODEX", "CODEX_SUBAGENT", "ANTIGRAVITY")][string]$Executor,
        [string]$Category,
        [int]$CodexShare,
        [int]$AntigravityShare,
        [string]$Leader,
        [string]$RouteReason,
        [string]$Model,
        [string]$Effort,
        [string]$Worker,
        [string]$AgyPath,
        [string]$WorkId,
        [string]$SelectedProjectPath,
        [string]$ExecutorWorkingDirectory,
        [bool]$AgyAvailable = $false,
        [bool]$AgySelected = $false,
        [bool]$CodexAvailable = $false,
        [bool]$CodexSelected = $false,
        [string]$ResolvedLeader,
        [ValidateSet("TASK", "COORDINATION", "SESSION")][string]$RecordKind = "TASK"
    )
    $events = Join-Path $TelemetryRoot "events"
    New-Item -ItemType Directory -Path $events -Force | Out-Null
    $id = "task-" + ([guid]::NewGuid().ToString("N"))
    $path = Join-Path $events "$id.json"
    $now = [DateTimeOffset]::Now
    $record = [ordered]@{
        TaskId = $id
        SessionId = $SessionId
        Task = Protect-DawoudTelemetryText -Text $Task
        WorkId = $WorkId
        RecordKind = $RecordKind
        Executor = $Executor
        Category = $Category
        Start = $now.UtcDateTime.ToString("o")
        StartEpochMs = $now.ToUnixTimeMilliseconds()
        End = ""
        DurationSeconds = 0
        Status = "RUNNING"
        Worker = $Worker
        Model = $Model
        Effort = $Effort
        PID = 0
        ExitCode = $null
        AgyPath = $AgyPath
        OutputReturnedFromAGY = $false
        SelectedProjectPath = $SelectedProjectPath
        ExecutorWorkingDirectory = $ExecutorWorkingDirectory
        AgyAvailable = $AgyAvailable
        AgySelected = $AgySelected
        CodexAvailable = $CodexAvailable
        CodexSelected = $CodexSelected
        ResolvedLeader = $ResolvedLeader
        FallbackEvents = @()
        Leader = $Leader
        RouteReason = $RouteReason
        CodexShare = $CodexShare
        AntigravityShare = $AntigravityShare
    }
    $json = $record | ConvertTo-Json -Depth 8
    if (-not (Write-DawoudTelemetryJson -Path $path -Json $json)) {
        throw "Telemetry file write failed after bounded retries: $path"
    }
    [pscustomobject]@{ Id = $id; Path = $path; Record = [pscustomobject]$record }
}

function Set-DawoudTelemetryRecord {
    param([Parameter(Mandatory)][string]$Path, [hashtable]$Fields)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $record = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    foreach ($key in $Fields.Keys) { $record | Add-Member -MemberType NoteProperty -Name $key -Value $Fields[$key] -Force }
    $json = $record | ConvertTo-Json -Depth 8
    if (-not (Write-DawoudTelemetryJson -Path $Path -Json $json)) {
        throw "Telemetry file write failed after bounded retries: $Path"
    }
}

function Complete-DawoudTelemetryRecord {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidateSet("DONE", "ERROR", "CANCELLED")][string]$Status,
        [int]$ExitCode = 0,
        [string]$Summary = "",
        [int]$WorkerPid = 0,
        [string]$AgyPath = "",
        [bool]$OutputReturnedFromAGY = $false
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $record = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $end = [DateTimeOffset]::Now
    $record.End = $end.UtcDateTime.ToString("o")
    $endEpochMs = $end.ToUnixTimeMilliseconds()
    $record | Add-Member -MemberType NoteProperty -Name EndEpochMs -Value $endEpochMs -Force
    if ($record.PSObject.Properties['StartEpochMs']) {
        $record.DurationSeconds = [math]::Round(($endEpochMs - [int64]$record.StartEpochMs) / 1000, 2)
    } else {
        $start = [DateTimeOffset]::Parse([string]$record.Start, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal)
        $record.DurationSeconds = [math]::Round(($end.ToUniversalTime() - $start.ToUniversalTime()).TotalSeconds, 2)
    }
    $record.Status = $Status
    $record.ExitCode = $ExitCode
    if ($WorkerPid) { $record.PID = $WorkerPid }
    if ($AgyPath) { $record.AgyPath = $AgyPath }
    $record.OutputReturnedFromAGY = $OutputReturnedFromAGY
    $record | Add-Member -MemberType NoteProperty -Name Summary -Value (Protect-DawoudTelemetryText -Text $Summary) -Force
    $json = $record | ConvertTo-Json -Depth 8
    if (-not (Write-DawoudTelemetryJson -Path $Path -Json $json)) {
        throw "Telemetry file write failed after bounded retries: $Path"
    }
}

function Get-DawoudTelemetryRecords {
    param([Parameter(Mandatory)][string]$TelemetryRoot, [string]$SessionId, [string]$WorkId)
    $events = Join-Path $TelemetryRoot "events"
    if (-not (Test-Path -LiteralPath $events -PathType Container)) { return @() }
    @(Get-ChildItem -LiteralPath $events -Filter "task-*.json" -File -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $record = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
            if ((-not $SessionId -or $record.SessionId -eq $SessionId) -and (-not $WorkId -or $record.WorkId -eq $WorkId)) { $record }
        } catch { }
    })
}

function Get-DawoudExecutionReport {
    param([Parameter(Mandatory)][string]$TelemetryRoot, [Parameter(Mandatory)][string]$SessionId, [int]$CodexShare, [int]$AntigravityShare, [string]$WorkId)
    $records = @(Get-DawoudTelemetryRecords -TelemetryRoot $TelemetryRoot -SessionId $SessionId -WorkId $WorkId)
    if (-not $records.Count) { return $null }
    $finished = @($records | Where-Object { $_.End })
    if (-not $finished.Count) { return $null }
    $sessionRecords = @($finished | Where-Object RecordKind -eq "SESSION")
    $codexTaskRecords = @($finished | Where-Object { $_.Executor -in @("CODEX", "CODEX_SUBAGENT") -and $_.RecordKind -ne "SESSION" })
    $codexTime = if ($sessionRecords.Count -gt 0) {
        [math]::Round((@($sessionRecords | Measure-Object DurationSeconds -Sum).Sum), 2)
    } else {
        [math]::Round((@($codexTaskRecords | Measure-Object DurationSeconds -Sum).Sum), 2)
    }
    $codexMainRecords = @($finished | Where-Object { $_.Executor -eq "CODEX" -and $_.RecordKind -ne "SESSION" })
    $codexSubagentRecords = @($finished | Where-Object Executor -eq "CODEX_SUBAGENT")
    $codexMainTime = [math]::Round((@($codexMainRecords | Measure-Object DurationSeconds -Sum).Sum), 2)
    if ($sessionRecords.Count -gt 0) { $codexMainTime = [math]::Round((@($sessionRecords | Measure-Object DurationSeconds -Sum).Sum), 2) }
    $codexSubagentTime = [math]::Round((@($codexSubagentRecords | Measure-Object DurationSeconds -Sum).Sum), 2)
    $agyTime = [math]::Round((@($finished | Where-Object Executor -eq "ANTIGRAVITY" | Measure-Object DurationSeconds -Sum).Sum), 2)
    $startMs = @($finished | ForEach-Object { if ($_.PSObject.Properties['StartEpochMs']) { [int64]$_.StartEpochMs } else { [DateTimeOffset]::Parse([string]$_.Start, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal).ToUnixTimeMilliseconds() } }) | Measure-Object -Minimum -Maximum
    $endMs = @($finished | ForEach-Object { if ($_.PSObject.Properties['EndEpochMs']) { [int64]$_.EndEpochMs } else { [DateTimeOffset]::Parse([string]$_.End, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal).ToUnixTimeMilliseconds() } }) | Measure-Object -Minimum -Maximum
    $wall = [math]::Round(($endMs.Maximum - $startMs.Minimum) / 1000, 2)
    if ($wall -lt 0) { $wall = 0 }
    $sum = $codexTime + $agyTime
    $agyPct = if ($sum -gt 0) { [math]::Round(($agyTime / $sum) * 100, 1) } else { 0 }
    $codexPct = if ($sum -gt 0) { [math]::Round(($codexTime / $sum) * 100, 1) } else { 0 }
    $overlap = [math]::Round([math]::Max(0, $sum - $wall), 2)
    $verdict = if ($sum -eq 0) { "NO MEASURED WORK" } elseif ([math]::Abs($agyPct - $AntigravityShare) -le 20) { "APPROXIMATE" } else { "OFF TARGET" }
    $agyDone = @($finished | Where-Object { $_.Executor -eq "ANTIGRAVITY" -and $_.Status -eq "DONE" }).Count
    $agyFailed = @($finished | Where-Object { $_.Executor -eq "ANTIGRAVITY" -and $_.Status -ne "DONE" }).Count
    $fallbackCount = @($finished | ForEach-Object { @($_.FallbackEvents).Count } | Measure-Object -Sum).Sum
    $tasks = @($finished | Where-Object { $_.RecordKind -eq "TASK" })
    $taskAgy = @($tasks | Where-Object Executor -eq "ANTIGRAVITY").Count
    $taskCodex = @($tasks | Where-Object Executor -in @("CODEX", "CODEX_SUBAGENT")).Count
    $taskTotal = $taskAgy + $taskCodex
    $taskAgyPct = if ($taskTotal -gt 0) { [math]::Round(($taskAgy / $taskTotal) * 100, 1) } else { 0 }
    $agyRecords = @($finished | Where-Object Executor -eq "ANTIGRAVITY")
    $agyAvailable = @($agyRecords | Where-Object { $_.AgyAvailable -eq $true -or $_.AgyPath }).Count -gt 0
    $explicitRetries = [int](@($finished | ForEach-Object { if ($_.RetryCount) { [int]$_.RetryCount } else { 0 } } | Measure-Object -Sum).Sum)
    $suffixRetries = @($agyRecords | Where-Object { [string]$_.WorkId -match '-retry\d+$' }).Count
    $retries = [math]::Max($explicitRetries, $suffixRetries)
    $deviationReason = ""
    if ($AntigravityShare -gt 0 -and $agyDone -eq 0) {
        $errorReason = @($finished | Where-Object { $_.AgyAvailable -eq $false -and $_.Summary } | Select-Object -First 1 | ForEach-Object { [string]$_.Summary })
        if ($errorReason) { $deviationReason = $errorReason }
        elseif (@($finished | Where-Object { $_.RouteReason -match "safety|high-risk|retained" }).Count -gt 0) { $deviationReason = "task retained for safety" }
        else { $deviationReason = "no suitable AGY slice was allocated" }
    }
    $text = [System.Collections.Generic.List[string]]::new()
    [void]$text.Add("DAWOUD EXECUTION REPORT")
    [void]$text.Add("Configured:")
    [void]$text.Add("Codex: $CodexShare%")
    [void]$text.Add("Antigravity: $AntigravityShare%")
    [void]$text.Add("Execution:")
    [void]$text.Add(("Codex main: {0}s | Codex subagents: {1}s | Antigravity: {2}s" -f $codexMainTime, $codexSubagentTime, $agyTime))
    [void]$text.Add(("Measured execution-time share: Codex {0}% | Antigravity {1}%" -f $codexPct, $agyPct))
    [void]$text.Add(("Wall-clock: total {0}s | AGY active {1}s | Codex active {2}s | overlap {3}s" -f $wall, $agyTime, $codexTime, $overlap))
    $codexMainCount = if ($sessionRecords.Count -gt 0) { $sessionRecords.Count } else { @($codexTaskRecords | Where-Object Executor -eq "CODEX").Count }
    [void]$text.Add(("Executions: Codex main {0} | Codex subagents {1} | AGY workers {2} | AGY successes {3} | AGY failures {4}" -f $codexMainCount, @($finished | Where-Object Executor -eq "CODEX_SUBAGENT").Count, @($finished | Where-Object Executor -eq "ANTIGRAVITY").Count, $agyDone, $agyFailed))
    [void]$text.Add("Assignments:")
    [void]$text.Add(("Codex: {0} | Codex subagents: {1} | AGY: {2} | AGY task share: {3}%" -f @($tasks | Where-Object Executor -eq "CODEX").Count, @($tasks | Where-Object Executor -eq "CODEX_SUBAGENT").Count, $taskAgy, $taskAgyPct))
    if ($sessionRecords.Count -eq 0) { [void]$text.Add("Measurement note: Codex main session window unavailable; CODEX time is dispatch/coordination time only.") }
    [void]$text.Add("AGY:")
    [void]$text.Add("Expected: $(if($AntigravityShare -gt 0){'YES'}else{'NO'})")
    [void]$text.Add("Available: $(if($agyAvailable){'YES'}else{'NO'})")
    [void]$text.Add(("Invocations: {0}" -f $agyRecords.Count))
    [void]$text.Add(("Successes: {0}" -f $agyDone))
    [void]$text.Add(("Failures: {0}" -f $agyFailed))
    [void]$text.Add(("Retries: {0}" -f $retries))
    [void]$text.Add(("Fallbacks: {0}" -f $fallbackCount))
    [void]$text.Add(("Target vs Actual: Codex {0}% -> {1}% | Antigravity {2}% -> {3}%" -f $CodexShare, $codexPct, $AntigravityShare, $agyPct))
    [void]$text.Add(("Antigravity verification: REAL AGY EXECUTION: {0}" -f $(if($agyDone -gt 0){"YES"}else{"NO"})))
    if ($agyDone -eq 0) { [void]$text.Add("WARNING $([char]0x2014) NO REAL ANTIGRAVITY EXECUTION OCCURRED.") }
    if ($verdict -eq "OFF TARGET") { [void]$text.Add(("WARNING: Configured Antigravity target was {0}%, but measured Antigravity execution share was {1}%." -f $AntigravityShare, $agyPct)) }
    if ($deviationReason) { [void]$text.Add("TARGET DEVIATION: $deviationReason") }
    [void]$text.Add("Routing verdict: $verdict")
    [void]$text.Add("TOKEN USAGE: Exact per-agent token metering unavailable.")
    [void]$text.Add("MODEL EXECUTION")
    $modelGroups = @($finished | Where-Object { $_.Model } | Group-Object Executor,Model | Sort-Object Name)
    foreach ($group in $modelGroups) {
        $sample = @($group.Group)
        $modelTime = [math]::Round((@($sample | Measure-Object DurationSeconds -Sum).Sum), 2)
        $modelTasks = $sample.Count
        [void]$text.Add(("{0} | model {1} | measured active time {2}s | tasks {3}" -f $sample[0].Executor, $sample[0].Model, $modelTime, $modelTasks))
    }
    [pscustomobject]@{ Text = ($text -join "`n"); Records = $records; CodexSeconds = $codexTime; AntigravitySeconds = $agyTime; WallSeconds = $wall; AntigravityPercent = $agyPct; Verdict = $verdict }
}
