# AGEX central process runner and session runtime.
# Every external executor launch (Codex, AGY dispatch, availability probes) goes
# through Invoke-AgexProcess so that command details, exit codes, stdout/stderr,
# timeouts, cancellation and owned-process cleanup are handled in one place.

function New-AgexRuntime {
    param([string]$SessionId = "", [string]$LogPath = "", [string]$CodexSandbox = "read-only", [int]$MaxAutoFallbacks = 1)
    [hashtable]::Synchronized(@{
        SessionId = $SessionId
        Sync = [object]::new()
        OwnedProcesses = [hashtable]::Synchronized(@{})
        Executions = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
        Fallbacks = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
        Failures = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
        Health = [hashtable]::Synchronized(@{})
        LogPath = $LogPath
        LogSync = [object]::new()
        LogWarnings = 0
        CodexSandbox = $CodexSandbox
        MaxAutoFallbacks = $MaxAutoFallbacks
        UnhealthyCooldownSeconds = 300
    })
}

function Write-AgexLog {
    # Diagnostic log. Never throws: logging must not break execution.
    param($Runtime, [Parameter(Mandatory)][string]$Event, [hashtable]$Data = @{})
    if (-not $Runtime -or [string]::IsNullOrWhiteSpace([string]$Runtime.LogPath)) { return }
    try {
        $record = [ordered]@{ at = (Get-Date).ToUniversalTime().ToString("o"); session = [string]$Runtime.SessionId; event = $Event }
        foreach ($key in $Data.Keys) {
            $value = $Data[$key]
            if ($value -is [string]) { $value = Protect-AgexAuthoritativeText -Text $value; if ($value.Length -gt 4000) { $value = $value.Substring(0, 4000) + "..." } }
            $record[$key] = $value
        }
        $line = ($record | ConvertTo-Json -Compress -Depth 6) + [Environment]::NewLine
        [System.Threading.Monitor]::Enter($Runtime.LogSync)
        try {
            $parent = Split-Path -Parent $Runtime.LogPath
            if ($parent -and -not (Test-Path -LiteralPath $parent -PathType Container)) { [void][IO.Directory]::CreateDirectory($parent) }
            [IO.File]::AppendAllText($Runtime.LogPath, $line, [Text.UTF8Encoding]::new($false))
        } finally { [System.Threading.Monitor]::Exit($Runtime.LogSync) }
    } catch { try { $Runtime.LogWarnings = [int]$Runtime.LogWarnings + 1 } catch { } }
}

function ConvertTo-AgexArgumentString {
    # Quote arguments using CommandLineToArgvW rules so paths ending in "\" and
    # embedded quotes survive the trip to the child process.
    param([AllowEmptyCollection()][string[]]$Arguments = @())
    $parts = foreach ($argument in @($Arguments)) {
        $value = [string]$argument
        if ($value.Length -gt 0 -and $value -notmatch '[\s"]') { $value; continue }
        $builder = [System.Text.StringBuilder]::new()
        [void]$builder.Append('"')
        $slashes = 0
        foreach ($char in $value.ToCharArray()) {
            if ($char -eq '\') { $slashes++; continue }
            if ($char -eq '"') { [void]$builder.Append('\' * ($slashes * 2 + 1)); [void]$builder.Append('"'); $slashes = 0; continue }
            if ($slashes) { [void]$builder.Append('\' * $slashes); $slashes = 0 }
            [void]$builder.Append($char)
        }
        if ($slashes) { [void]$builder.Append('\' * ($slashes * 2)) }
        [void]$builder.Append('"')
        $builder.ToString()
    }
    @($parts) -join ' '
}

function Protect-AgexCommandArgument {
    param([AllowEmptyString()][string]$Value)
    $safe = Protect-AgexAuthoritativeText -Text $Value
    $safe = $safe -replace '(?i)^(--?(?:password|passwd|token|api[-_]?key|secret)(?:=))(.+)$', '$1<redacted>'
    if ($safe.Length -gt 160) { $safe = $safe.Substring(0, 157) + "..." }
    $safe
}

function Resolve-AgexLaunchTarget {
    # Resolve what is actually started. npm .cmd shims are unwrapped to
    # node + script so exit codes, stdin and process-tree ownership are direct.
    param([Parameter(Mandatory)][string]$FilePath)
    $extension = [IO.Path]::GetExtension($FilePath).ToLowerInvariant()
    if ($extension -eq ".ps1") {
        $shell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
        return [pscustomobject]@{ FileName = $shell; PrefixArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $FilePath); Kind = "powershell-script" }
    }
    if ($extension -in @(".cmd", ".bat")) {
        $directory = Split-Path -Parent $FilePath
        $name = [IO.Path]::GetFileNameWithoutExtension($FilePath)
        $script = Join-Path $directory ("node_modules\@openai\{0}\bin\{0}.js" -f $name)
        if (Test-Path -LiteralPath $script -PathType Leaf) {
            $node = Join-Path $directory "node.exe"
            if (-not (Test-Path -LiteralPath $node -PathType Leaf)) {
                $nodeCommand = Get-Command node -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                $node = if ($nodeCommand) { [string]$nodeCommand.Source } else { "" }
            }
            if ($node) { return [pscustomobject]@{ FileName = $node; PrefixArguments = @($script); Kind = "node-shim" } }
        }
    }
    [pscustomobject]@{ FileName = $FilePath; PrefixArguments = @(); Kind = "direct" }
}

function Register-AgexOwnedProcess {
    param($Runtime, [int]$ProcessId, [string]$Label)
    if ($Runtime -and $ProcessId -gt 0) { $Runtime.OwnedProcesses[[string]$ProcessId] = [pscustomobject]@{ Pid = $ProcessId; Label = $Label; Started = Get-Date } }
}

function Unregister-AgexOwnedProcess {
    param($Runtime, [int]$ProcessId)
    if ($Runtime -and $ProcessId -gt 0) { $Runtime.OwnedProcesses.Remove([string]$ProcessId) }
}

function Stop-AgexProcessTree {
    # Kill only a PID this session started, plus its descendants.
    param([Parameter(Mandatory)][int]$ProcessId)
    if ($ProcessId -le 0) { return }
    try {
        $taskkill = Join-Path $env:SystemRoot "System32\taskkill.exe"
        & $taskkill /PID ([string]$ProcessId) /T /F 2>$null | Out-Null
    } catch { }
}

function Stop-AgexOwnedProcesses {
    param($Runtime)
    if (-not $Runtime) { return @() }
    $stopped = [System.Collections.Generic.List[int]]::new()
    foreach ($key in @($Runtime.OwnedProcesses.Keys)) {
        $entry = $Runtime.OwnedProcesses[$key]
        if (-not $entry) { continue }
        Stop-AgexProcessTree -ProcessId ([int]$entry.Pid)
        [void]$stopped.Add([int]$entry.Pid)
        $Runtime.OwnedProcesses.Remove($key)
    }
    @($stopped)
}

function Add-AgexExecutionRecord {
    param($Runtime, [Parameter(Mandatory)]$Record)
    if (-not $Runtime) { return }
    try {
        # Keep tails only; full output stays with the caller.
        $copy = [pscustomobject]@{}
        foreach ($property in $Record.PSObject.Properties) { if ($property.Name -notin @("Stdout", "Stderr")) { $copy | Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value } }
        [void]$Runtime.Executions.Add($copy)
        while ($Runtime.Executions.Count -gt 60) { $Runtime.Executions.RemoveAt(0) }
    } catch { }
}

function Get-AgexTextTail {
    param([AllowEmptyString()][string]$Text, [int]$MaxChars = 1200)
    if ([string]::IsNullOrEmpty($Text)) { return "" }
    $clean = Protect-AgexAuthoritativeText -Text $Text
    if ($clean.Length -le $MaxChars) { return $clean }
    "..." + $clean.Substring($clean.Length - $MaxChars)
}

function Invoke-AgexProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [AllowEmptyCollection()][string[]]$Arguments = @(),
        [string]$WorkingDirectory = (Get-Location).Path,
        [AllowEmptyString()][string]$StdinText,
        [int]$TimeoutSeconds = 600,
        $CancellationSignal,
        $Runtime,
        [string]$Label = "process",
        [string]$Executor = "",
        [string]$TaskId = "",
        [string]$RequestId = "",
        [scriptblock]$OnStdoutLine,
        [scriptblock]$OnStarted,
        [scriptblock]$OnTick,
        [int]$MaxCaptureChars = 4194304
    )
    $sanitizedArguments = @($Arguments | ForEach-Object { Protect-AgexCommandArgument -Value ([string]$_) })
    $record = [pscustomobject]@{
        Label = $Label; Executor = $Executor; TaskId = $TaskId; RequestId = $RequestId
        Executable = $FilePath; LaunchedFile = ""; LaunchKind = ""
        Arguments = $sanitizedArguments
        CommandLine = (@((Split-Path -Leaf $FilePath)) + $sanitizedArguments) -join ' '
        WorkingDirectory = $WorkingDirectory
        StdinChars = if ($null -eq $StdinText) { 0 } else { $StdinText.Length }
        StartedAt = Get-Date; EndedAt = [datetime]::MinValue; DurationSeconds = 0
        ProcessId = 0; ExitCode = $null
        Stdout = ""; Stderr = ""; StdoutTail = ""; StderrTail = ""
        TimedOut = $false; Cancelled = $false; StartFailed = $false
        ExceptionType = ""; ExceptionMessage = ""
        Outcome = "PENDING"; Summary = ""; FallbackEligible = $false
    }
    $process = $null
    $stdout = [System.Text.StringBuilder]::new()
    $stderr = [System.Text.StringBuilder]::new()
    try {
        if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) { throw [IO.FileNotFoundException]::new("Executable not found: $FilePath") }
        if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) { throw [IO.DirectoryNotFoundException]::new("Working directory not found: $WorkingDirectory") }
        $target = Resolve-AgexLaunchTarget -FilePath $FilePath
        $record.LaunchedFile = $target.FileName
        $record.LaunchKind = $target.Kind
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $target.FileName
        $psi.Arguments = ConvertTo-AgexArgumentString -Arguments (@($target.PrefixArguments) + @($Arguments))
        $psi.WorkingDirectory = $WorkingDirectory
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        # Windows PowerShell 5.1 otherwise decodes/encodes pipes with the console
        # code page, which corrupts non-ASCII prompts ("input is not valid UTF-8").
        $psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
        $psi.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
        # .NET Framework writes the console input encoding's preamble into the
        # child's stdin at start. A UTF-8 console with BOM would prefix every
        # prompt with EF BB BF, so use the same code page without a preamble.
        try { if ([Console]::InputEncoding.GetPreamble().Length -gt 0) { [Console]::InputEncoding = [Text.UTF8Encoding]::new($false) } } catch { }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $psi
        $record.StartedAt = Get-Date
        try { [void]$process.Start() }
        catch { $record.StartFailed = $true; throw }
        $record.ProcessId = $process.Id
        Register-AgexOwnedProcess -Runtime $Runtime -ProcessId $process.Id -Label $Label
        if ($OnStarted) { try { & $OnStarted $process.Id } catch { } }

        # Read stdout and stderr concurrently; never block on one pipe while
        # the child fills the other.
        $stdoutTask = $process.StandardOutput.ReadLineAsync()
        $stderrTask = $process.StandardError.ReadLineAsync()
        $stdinTask = $null
        $stdinStream = $process.StandardInput.BaseStream
        if (-not [string]::IsNullOrEmpty($StdinText)) {
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($StdinText)
            $stdinTask = $stdinStream.WriteAsync($bytes, 0, $bytes.Length)
        } else { try { $process.StandardInput.Close() } catch { } }
        $deadline = if ($TimeoutSeconds -gt 0) { (Get-Date).AddSeconds($TimeoutSeconds) } else { [datetime]::MaxValue }
        $stdoutOpen = $true; $stderrOpen = $true
        $nextTick = Get-Date
        while ($stdoutOpen -or $stderrOpen) {
            if ($OnTick -and (Get-Date) -ge $nextTick) { try { & $OnTick } catch { }; $nextTick = (Get-Date).AddSeconds(1) }
            if ($stdinTask -and $stdinTask.IsCompleted) {
                try { $stdinStream.Flush(); $process.StandardInput.Close() } catch { }
                $stdinTask = $null
            }
            if ($CancellationSignal -and $CancellationSignal.Requested) { $record.Cancelled = $true; break }
            if ((Get-Date) -ge $deadline) { $record.TimedOut = $true; break }
            $pending = @()
            if ($stdoutOpen) { $pending += $stdoutTask }
            if ($stderrOpen) { $pending += $stderrTask }
            [void][Threading.Tasks.Task]::WaitAny([Threading.Tasks.Task[]]$pending, 200)
            if ($stdoutOpen -and $stdoutTask.IsCompleted) {
                $line = if ($stdoutTask.IsFaulted) { $null } else { $stdoutTask.Result }
                if ($null -eq $line) { $stdoutOpen = $false }
                else {
                    if ($stdout.Length -lt $MaxCaptureChars) { [void]$stdout.AppendLine($line) }
                    if ($OnStdoutLine) { try { & $OnStdoutLine $line } catch { } }
                    $stdoutTask = $process.StandardOutput.ReadLineAsync()
                }
            }
            if ($stderrOpen -and $stderrTask.IsCompleted) {
                $line = if ($stderrTask.IsFaulted) { $null } else { $stderrTask.Result }
                if ($null -eq $line) { $stderrOpen = $false }
                else {
                    if ($stderr.Length -lt $MaxCaptureChars) { [void]$stderr.AppendLine($line) }
                    $stderrTask = $process.StandardError.ReadLineAsync()
                }
            }
        }
        if ($record.TimedOut -or $record.Cancelled) {
            Stop-AgexProcessTree -ProcessId $process.Id
            [void]$process.WaitForExit(5000)
        } elseif (-not $process.WaitForExit(10000)) {
            # Pipes closed but the process is still alive: treat as hung.
            $record.TimedOut = $true
            Stop-AgexProcessTree -ProcessId $process.Id
            [void]$process.WaitForExit(5000)
        } else { $process.WaitForExit() }
        if ($process.HasExited) { $record.ExitCode = $process.ExitCode }
    } catch {
        $record.ExceptionType = $_.Exception.GetType().FullName
        $record.ExceptionMessage = Protect-AgexAuthoritativeText -Text $_.Exception.Message
        if (-not $process -or $record.ProcessId -eq 0) { $record.StartFailed = $true }
        if ($process -and $record.ProcessId -gt 0) { try { if (-not $process.HasExited) { Stop-AgexProcessTree -ProcessId $process.Id } } catch { } }
    } finally {
        if ($record.ProcessId -gt 0) { Unregister-AgexOwnedProcess -Runtime $Runtime -ProcessId $record.ProcessId }
        if ($process) { try { $process.Dispose() } catch { } }
    }
    $record.EndedAt = Get-Date
    $record.DurationSeconds = [math]::Round(($record.EndedAt - $record.StartedAt).TotalSeconds, 2)
    $record.Stdout = $stdout.ToString().TrimEnd()
    $record.Stderr = $stderr.ToString().TrimEnd()
    $record.StdoutTail = Get-AgexTextTail -Text $record.Stdout
    $record.StderrTail = Get-AgexTextTail -Text $record.Stderr
    $record.Outcome = if ($record.Cancelled) { "CANCELLED" }
        elseif ($record.StartFailed) { "START_FAILED" }
        elseif ($record.TimedOut) { "TIMED_OUT" }
        elseif ($record.ExceptionType) { "ERROR" }
        elseif ($record.ExitCode -ne 0) { "EXIT_NONZERO" }
        else { "OK" }
    $record.Summary = switch ($record.Outcome) {
        "OK" { "Exited normally." }
        "CANCELLED" { "Cancelled by user." }
        "START_FAILED" { "Could not start: $($record.ExceptionMessage)" }
        "TIMED_OUT" { "No result within $TimeoutSeconds seconds; process stopped." }
        "ERROR" { "$($record.ExceptionType): $($record.ExceptionMessage)" }
        default { "Exited with code $($record.ExitCode)." }
    }
    # Fallback is only safe when the executor never produced meaningful work.
    $record.FallbackEligible = $record.Outcome -in @("START_FAILED", "ERROR") -or ($record.Outcome -eq "EXIT_NONZERO" -and [string]::IsNullOrWhiteSpace($record.Stdout))
    Add-AgexExecutionRecord -Runtime $Runtime -Record $record
    Write-AgexLog -Runtime $Runtime -Event "process_exit" -Data @{ label = $Label; executor = $Executor; task = $TaskId; command = $record.CommandLine; cwd = $WorkingDirectory; pid = $record.ProcessId; duration_s = $record.DurationSeconds; exit = $record.ExitCode; outcome = $record.Outcome; error = $record.ExceptionMessage; stderr_tail = (Get-AgexTextTail -Text $record.Stderr -MaxChars 600) }
    $record
}

# ---------------------------------------------------------------- agent health

function Get-AgexAgentHealth {
    param($Runtime, [Parameter(Mandatory)][ValidateSet("Codex", "Antigravity")][string]$Agent)
    if (-not $Runtime) { return [pscustomobject]@{ Agent = $Agent; Healthy = $true; Checked = $false; Reason = ""; Until = [datetime]::MinValue; Path = "" } }
    $entry = $Runtime.Health[$Agent]
    if (-not $entry) { return [pscustomobject]@{ Agent = $Agent; Healthy = $true; Checked = $false; Reason = ""; Until = [datetime]::MinValue; Path = "" } }
    if (-not $entry.Healthy -and $entry.Until -ne [datetime]::MaxValue -and (Get-Date) -ge $entry.Until) {
        # Cooldown elapsed: allow one new attempt.
        return [pscustomobject]@{ Agent = $Agent; Healthy = $true; Checked = $entry.Checked; Reason = "Retry allowed after cooldown."; Until = [datetime]::MinValue; Path = $entry.Path }
    }
    $entry
}

function Set-AgexAgentHealth {
    param($Runtime, [Parameter(Mandatory)][ValidateSet("Codex", "Antigravity")][string]$Agent, [bool]$Healthy, [string]$Reason = "", [string]$Path = "", [switch]$Permanent)
    if (-not $Runtime) { return }
    $until = if ($Healthy) { [datetime]::MinValue } elseif ($Permanent) { [datetime]::MaxValue } else { (Get-Date).AddSeconds([int]$Runtime.UnhealthyCooldownSeconds) }
    $previous = $Runtime.Health[$Agent]
    if (-not $Path -and $previous) { $Path = $previous.Path }
    $Runtime.Health[$Agent] = [pscustomobject]@{ Agent = $Agent; Healthy = $Healthy; Checked = $true; Reason = $Reason; Until = $until; Path = $Path; At = Get-Date }
    Write-AgexLog -Runtime $Runtime -Event "agent_health" -Data @{ executor = $Agent; healthy = $Healthy; reason = $Reason }
}

function Register-AgexAgentResult {
    # Environment-level failures (cannot start, not signed in) mark the agent
    # unhealthy at once. Other failures need two in a row, so one bad task does
    # not take an agent out of the session.
    param($Runtime, [Parameter(Mandatory)][ValidateSet("Codex", "Antigravity")][string]$Agent, [bool]$Success, [string]$Reason = "", [switch]$Immediate, [string]$Path = "")
    if (-not $Runtime) { return }
    if (-not $Runtime.ContainsKey("FailureStreak")) { $Runtime.FailureStreak = [hashtable]::Synchronized(@{}) }
    if ($Success) {
        $Runtime.FailureStreak[$Agent] = 0
        $current = $Runtime.Health[$Agent]
        if ($current -and -not $current.Healthy) { Set-AgexAgentHealth -Runtime $Runtime -Agent $Agent -Healthy $true -Reason "Worked again." }
        return
    }
    $streak = [int]$Runtime.FailureStreak[$Agent] + 1
    $Runtime.FailureStreak[$Agent] = $streak
    if ($Immediate -or $streak -ge 2) { Set-AgexAgentHealth -Runtime $Runtime -Agent $Agent -Healthy $false -Reason $Reason -Path $Path }
}

function Get-AgexOtherAgent {
    param([Parameter(Mandatory)][string]$Agent)
    if ($Agent -eq "Codex") { "Antigravity" } else { "Codex" }
}

function Test-AgexAgentPrecheck {
    # Lightweight availability check: executable exists and answers --version.
    # No network calls, no model listing; bounded to a few seconds.
    param($Runtime, [Parameter(Mandatory)][ValidateSet("Codex", "Antigravity")][string]$Agent, [string]$ExecutablePath, [string]$WorkingDirectory = $env:TEMP)
    if ([string]::IsNullOrWhiteSpace($ExecutablePath) -or -not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        $reason = if ($Agent -eq "Codex") { "Codex CLI was not found." } else { "Antigravity CLI (agy.exe) was not found." }
        Set-AgexAgentHealth -Runtime $Runtime -Agent $Agent -Healthy $false -Reason $reason -Permanent
        return (Get-AgexAgentHealth -Runtime $Runtime -Agent $Agent)
    }
    if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) { $WorkingDirectory = $env:TEMP }
    $probe = Invoke-AgexProcess -FilePath $ExecutablePath -Arguments @("--version") -WorkingDirectory $WorkingDirectory -TimeoutSeconds 20 -Runtime $Runtime -Label "$Agent precheck" -Executor $Agent
    if ($probe.Outcome -eq "OK") {
        Set-AgexAgentHealth -Runtime $Runtime -Agent $Agent -Healthy $true -Reason (Get-AgexTextTail -Text $probe.Stdout -MaxChars 80) -Path $ExecutablePath
    } else {
        $detail = if ($probe.StderrTail) { $probe.StderrTail } else { $probe.Summary }
        Set-AgexAgentHealth -Runtime $Runtime -Agent $Agent -Healthy $false -Reason ("{0} did not respond to --version: {1}" -f $Agent, $detail) -Path $ExecutablePath
    }
    Get-AgexAgentHealth -Runtime $Runtime -Agent $Agent
}
