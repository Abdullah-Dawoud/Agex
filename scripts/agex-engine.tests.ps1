# Targeted regression tests for the AGEX execution engine, fallback, outcome
# accounting, process runner, desktop protocol, CLI, installer and terminal frame. Uses fake Codex/AGY
# executables generated in a temp folder; never calls real agents.
[CmdletBinding()]
param([switch]$SkipSlow, [string]$FakesOnly)
$ErrorActionPreference = "Stop"
$scripts = $PSScriptRoot
. (Join-Path $scripts "agex-common.ps1")
. (Join-Path $scripts "agex-ui.ps1")
. (Join-Path $scripts "agex-graph.ps1")

$script:results = [System.Collections.Generic.List[object]]::new()
function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    $started = Get-Date
    try { & $Body; [void]$script:results.Add([pscustomobject]@{ Name = $Name; Pass = $true; Detail = ""; Seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1) }) }
    catch { [void]$script:results.Add([pscustomobject]@{ Name = $Name; Pass = $false; Detail = $_.Exception.Message; Seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1) }) }
}
function Assert-True { param($Condition, [string]$Message) if (-not $Condition) { throw $Message } }

$shell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
$work = if ($FakesOnly) { $FakesOnly } else { Join-Path $env:TEMP ("agex-engine-tests-" + [guid]::NewGuid().ToString("N")) }
if (-not $FakesOnly) { $env:AGEX_HOME = Join-Path $work "agex-home" }   # never touch real AGEX data
$fakes = Join-Path $work "fakes"
$project = Join-Path $work "project"
New-Item -ItemType Directory -Path $fakes, $project -Force | Out-Null

# ------------------------------------------------------------ fake agents
$fakeCodex = @'
$Rest = @(([string]$env:FAKE_ARGS) -split ' ')
$ErrorActionPreference = 'Stop'
$mode = [string]$env:FAKE_CODEX_MODE
if ($Rest -contains '--version') { if ($mode -eq 'noversion') { [Console]::Error.WriteLine('codex: broken install'); exit 3 }; 'codex-cli 0.0.0-fake'; exit 0 }
$stdin = [Console]::OpenStandardInput(); $ms = New-Object IO.MemoryStream; $stdin.CopyTo($ms)
try { $text = [Text.UTF8Encoding]::new($false, $true).GetString($ms.ToArray()) } catch { [Console]::Error.WriteLine('Failed to read prompt from stdin: input is not valid UTF-8'); exit 1 }
$kind = if ($text -match 'You are selected leader') { 'leader' } else { 'task' }
Add-Content -LiteralPath $env:FAKE_CALLS -Value ("codex|{0}|{1}|{2}" -f $kind, (Get-Date).ToString('o'), $PID)
if ($mode -eq 'fail' -or ($mode -eq 'failleader' -and $kind -eq 'leader')) { [Console]::Error.WriteLine('Error: simulated Codex crash'); exit 1 }
if ($mode -eq 'hang') { Start-Sleep -Seconds 120 }
if ($kind -eq 'leader') {
    $roundFile = Join-Path $env:FAKE_STATE 'rounds.txt'
    $round = if (Test-Path $roundFile) { [int](Get-Content $roundFile) } else { 0 }
    Set-Content -LiteralPath $roundFile -Value ($round + 1)
    $plan = [string]$env:FAKE_PLAN
    if ($plan -eq 'complete' -or ($round -ge 1 -and $plan -eq 'agy2')) { '{"goal_status":"COMPLETE","reason":"Answer: the project has 3 files. ' + [char]0x00E9 + '","verification":"Listed files.","tasks":[]}'; exit 0 }
    if ($plan -eq 'partial' -and $round -eq 0) { '{"goal_status":"CONTINUE","reason":"Two reads.","tasks":[{"id":"a","title":"Inspect A","objective":"Inspect A","executor":"Codex"},{"id":"b","title":"Inspect B","objective":"FAIL-B inspect","executor":"Codex"}]}'; exit 0 }
    if ($plan -eq 'partial') { '{"goal_status":"BLOCKED","reason":"Task B cannot be completed.","tasks":[]}'; exit 0 }
    if ($plan -eq 'agy2') { '{"goal_status":"CONTINUE","reason":"Two parallel writes.","tasks":[{"id":"w1","title":"Write one","objective":"Write one.txt","executor":"Antigravity","affected_files":["one.txt"]},{"id":"w2","title":"Write two","objective":"Write two.txt","executor":"Antigravity","affected_files":["two.txt"]}]}'; exit 0 }
    '{"goal_status":"BLOCKED","reason":"No plan configured.","tasks":[]}'; exit 0
}
if ($text -match 'ASSIGNMENT: FAIL') { [Console]::Error.WriteLine('Error: simulated task failure'); exit 1 }
'{"result":"Codex inspected the files."}'
exit 0
'@
$fakeAgy = @'
$Rest = @($args)
$ErrorActionPreference = 'Stop'
$mode = [string]$env:FAKE_AGY_MODE
if ($Rest -contains '--version') { if ($mode -eq 'noversion') { [Console]::Error.WriteLine('agy: not installed correctly'); exit 3 }; 'agy 0.0.0-fake'; exit 0 }
if ($Rest -contains 'models') { if ($mode -eq 'noversion') { exit 3 }; 'fake-model  Fake Model'; exit 0 }
if ($mode -eq 'fail') { [Console]::Error.WriteLine('simulated agy failure'); exit 1 }
$line = [Console]::In.ReadLine()
$prompt = [string](($line | ConvertFrom-Json).message.content)
$kind = if ($prompt -match 'You are selected leader') { 'leader' } else { 'task' }
$start = Get-Date
Add-Content -LiteralPath $env:FAKE_CALLS -Value ("agy|{0}|{1}|{2}|start" -f $kind, $start.ToString('o'), $PID)
'{"event":"start"}'
if ($kind -eq 'leader') {
    $response = '{"goal_status":"COMPLETE","reason":"Antigravity answered.","verification":"Checked.","tasks":[]}'
} else {
    $workspace = [regex]::Match($prompt, 'AUTHORITATIVE EXECUTOR WORKSPACE: (.+)').Groups[1].Value.Trim()
    $owned = [regex]::Match($prompt, 'OWNED PATHS: (.+)').Groups[1].Value.Trim()
    foreach ($item in ($owned -split ',\s*' | Where-Object { $_ })) { Set-Content -LiteralPath (Join-Path $workspace $item) -Value 'written by fake agy' }
    Start-Sleep -Milliseconds 2500
    $response = '{"result":"Created ' + $owned + '"}'
}
Add-Content -LiteralPath $env:FAKE_CALLS -Value ("agy|{0}|{1}|{2}|end" -f $kind, (Get-Date).ToString('o'), $PID)
(@{ event = 'result'; result = @{ response = $response } } | ConvertTo-Json -Compress -Depth 5)
while ($null -ne [Console]::In.ReadLine()) { }
exit 0
'@
$codexScript = Join-Path $fakes "fake-codex.ps1"
$codexPath = Join-Path $fakes "codex.cmd"
$agyScript = Join-Path $fakes "fake-agy.ps1"
$agyPath = Join-Path $fakes "agy.cmd"
[IO.File]::WriteAllText($codexScript, $fakeCodex, [Text.UTF8Encoding]::new($true))
# The .cmd wrapper passes arguments through FAKE_ARGS: powershell -File cannot take a lone "-" argument.
[IO.File]::WriteAllText($codexPath, "@echo off`r`nset `"FAKE_ARGS=%*`"`r`n`"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`" -NoProfile -ExecutionPolicy Bypass -File `"%~dp0fake-codex.ps1`"`r`nexit /b %errorlevel%`r`n", [Text.Encoding]::ASCII)
[IO.File]::WriteAllText($agyScript, $fakeAgy, [Text.UTF8Encoding]::new($true))
[IO.File]::WriteAllText($agyPath, "@echo off`r`n`"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`" -NoProfile -ExecutionPolicy Bypass -File `"%~dp0fake-agy.ps1`" %*`r`nexit /b %errorlevel%`r`n", [Text.Encoding]::ASCII)
Set-Content -LiteralPath (Join-Path $project "readme.txt") -Value "fixture"
if ($FakesOnly) { Write-Output "Fakes written to $fakes"; return }

function Get-FakeProcesses {
    @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($fakes) })
}

function Invoke-LineSession {
    # Runs the real frontend in line mode (redirected stdin/stdout) as a separate process.
    param([string]$Prompt, [hashtable]$Env, [string]$Leader = "Codex", [int]$TimeoutSeconds = 150)
    $state = Join-Path $work ("state-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    $calls = Join-Path $state "calls.log"
    Set-Content -LiteralPath $calls -Value ""
    $saved = @{}
    $all = @{ FAKE_STATE = $state; FAKE_CALLS = $calls; FAKE_CODEX_MODE = ""; FAKE_AGY_MODE = ""; FAKE_PLAN = "complete"; AGEX_AGY_PATH = $agyPath }
    foreach ($key in $Env.Keys) { $all[$key] = $Env[$key] }
    foreach ($key in $all.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key, [string]$all[$key]) }
    try {
        $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $scripts "agex-primary.ps1"), "-Project", $project, "-CodexPath", $codexPath, "-AgyPath", $agyPath, "-SessionId", ("test-" + [guid]::NewGuid().ToString("N")), "-ConfiguredLeader", $Leader, "-CodexShare", "10", "-AntigravityShare", "90")
        $run = Invoke-AgexProcess -FilePath $shell -Arguments $arguments -WorkingDirectory $project -StdinText ($Prompt + "`n") -TimeoutSeconds $TimeoutSeconds -Label "line-session"
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) } }
    [pscustomobject]@{ Run = $run; Output = [string]$run.Stdout; Calls = @(Get-Content -LiteralPath $calls | Where-Object { $_ }); State = $state }
}

# --------------------------------------------------------- runner tests
Test-Case "runner: UTF-8 stdin reaches child intact (root cause of 'Leader execution failed')" {
    $text = "Arabic " + [char]0x0645 + [char]0x0631 + [char]0x062D + [char]0x0628 + [char]0x0627 + " e" + [char]0x0301 + " " + [char]0x00E9 + " " + [char]0x2192 + " done"
    $run = Invoke-AgexProcess -FilePath $shell -Arguments @("-NoProfile", "-Command", '$s=[Console]::OpenStandardInput();$m=New-Object IO.MemoryStream;$s.CopyTo($m);[Console]::Out.Write([BitConverter]::ToString($m.ToArray()))') -StdinText $text -TimeoutSeconds 30
    Assert-True ($run.Outcome -eq "OK") "outcome $($run.Outcome) $($run.Summary)"
    $expected = [BitConverter]::ToString([Text.UTF8Encoding]::new($false).GetBytes($text))
    Assert-True ($run.Stdout.Trim() -eq $expected) "child received different bytes: $($run.Stdout.Trim())"
}
Test-Case "runner: large stdout and stderr together do not deadlock" {
    $run = Invoke-AgexProcess -FilePath $shell -Arguments @("-NoProfile", "-Command", '$e=[Console]::Error;$o=[Console]::Out;for($i=0;$i -lt 4000;$i++){$e.WriteLine(("e"*60)+$i);$o.WriteLine(("o"*60)+$i)}') -TimeoutSeconds 60
    Assert-True ($run.Outcome -eq "OK") "outcome $($run.Outcome)"
    Assert-True (($run.Stdout -split "`n").Count -ge 4000 -and ($run.Stderr -split "`n").Count -ge 4000) "output truncated"
}
Test-Case "runner: missing executable is START_FAILED with command details kept" {
    $run = Invoke-AgexProcess -FilePath (Join-Path $work "missing.exe") -Arguments @("exec", "--model", "m1") -TimeoutSeconds 5
    Assert-True ($run.Outcome -eq "START_FAILED" -and $run.FallbackEligible) "outcome $($run.Outcome)"
    Assert-True ($run.CommandLine -match 'missing\.exe exec --model m1' -and $run.ExceptionType) "command details lost: $($run.CommandLine)"
}
Test-Case "runner: nonzero exit keeps exit code and stderr" {
    $run = Invoke-AgexProcess -FilePath $shell -Arguments @("-NoProfile", "-Command", '[Console]::Error.WriteLine("boom happened"); exit 7') -TimeoutSeconds 30
    Assert-True ($run.Outcome -eq "EXIT_NONZERO" -and $run.ExitCode -eq 7 -and $run.StderrTail -match 'boom happened' -and $run.FallbackEligible) "got $($run.Outcome) $($run.ExitCode)"
}
Test-Case "runner: timeout stops the whole process tree" {
    $marker = "agex-timeout-" + [guid]::NewGuid().ToString("N")
    $run = Invoke-AgexProcess -FilePath $shell -Arguments @("-NoProfile", "-Command", "Start-Process -NoNewWindow -FilePath '$shell' -ArgumentList '-NoProfile','-Command','Start-Sleep 60 # $marker'; Start-Sleep 60") -TimeoutSeconds 3
    Start-Sleep -Milliseconds 500
    $left = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($marker) })
    Assert-True ($run.Outcome -eq "TIMED_OUT" -and -not $run.FallbackEligible) "outcome $($run.Outcome)"
    Assert-True ($left.Count -eq 0) "orphan processes left: $($left.Count)"
}
Test-Case "runner: cancellation stops the child promptly" {
    $signal = [hashtable]::Synchronized(@{ Requested = $false })
    $runtime = New-AgexRuntime -SessionId "cancel"
    $job = [powershell]::Create()
    [void]$job.AddScript({ param($s) Start-Sleep -Seconds 2; $s.Requested = $true }).AddArgument($signal)
    $async = $job.BeginInvoke()
    $started = Get-Date
    $run = Invoke-AgexProcess -FilePath $shell -Arguments @("-NoProfile", "-Command", "Start-Sleep 60") -TimeoutSeconds 60 -CancellationSignal $signal -Runtime $runtime
    [void]$job.EndInvoke($async); $job.Dispose()
    Assert-True ($run.Outcome -eq "CANCELLED" -and ((Get-Date) - $started).TotalSeconds -lt 15) "outcome $($run.Outcome)"
    Assert-True ($runtime.OwnedProcesses.Count -eq 0 -and -not (Get-Process -Id $run.ProcessId -ErrorAction SilentlyContinue)) "process still owned or alive"
}
Test-Case "runner: argument quoting survives spaces, quotes and trailing backslash" {
    $values = @('C:\Path With Space\', 'say "hi"', 'plain', '')
    $echo = Join-Path $work "echo-args.ps1"; Set-Content -LiteralPath $echo -Value '$args | ForEach-Object { "[" + $_ + "]" }'
    $run = Invoke-AgexProcess -FilePath $shell -Arguments (@("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $echo) + $values) -TimeoutSeconds 30
    Assert-True ($run.Stdout -match [regex]::Escape('[C:\Path With Space\]') -and $run.Stdout -match [regex]::Escape('[say "hi"]') -and $run.Stdout -match '\[plain\]') "quoting broke: $($run.Stdout)"
}
Test-Case "runner: npm .cmd shim is launched directly through node" {
    $shimDir = Join-Path $work "shim"; $js = Join-Path $shimDir "node_modules\@openai\codex\bin"
    New-Item -ItemType Directory -Path $js -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $shimDir "codex.cmd") -Value "@echo off"; Set-Content -LiteralPath (Join-Path $js "codex.js") -Value "//"
    Set-Content -LiteralPath (Join-Path $shimDir "node.exe") -Value ""
    $target = Resolve-AgexLaunchTarget -FilePath (Join-Path $shimDir "codex.cmd")
    Assert-True ($target.Kind -eq "node-shim" -and $target.FileName -like "*node.exe" -and $target.PrefixArguments[0] -like "*codex.js") "shim not unwrapped: $($target.Kind)"
}

# ----------------------------------------------------- session / fallback
Test-Case "session: leader succeeds -> COMPLETE" {
    $session = Invoke-LineSession -Prompt "How many files?" -Env @{ FAKE_PLAN = "complete" }
    Assert-True ($session.Output -match 'STATUS: COMPLETE' -and $session.Output -notmatch 'PARTIAL') "output: $($session.Output)"
    Assert-True ($session.Output -match 'Answer: the project has 3 files') "answer missing"
}
Test-Case "session: leader crashes, Antigravity healthy -> COMPLETE_WITH_FALLBACK (screenshot case)" {
    $session = Invoke-LineSession -Prompt "Explain the project" -Env @{ FAKE_CODEX_MODE = "failleader"; FAKE_PLAN = "complete" }
    Assert-True ($session.Output -match 'STATUS: COMPLETE_WITH_FALLBACK') "output: $($session.Output)"
    Assert-True ($session.Output -match 'switched to Antigravity automatically \(recovered successfully\)') "fallback not explained"
    Assert-True (@($session.Calls | Where-Object { $_ -like 'codex|leader*' }).Count -eq 1 -and @($session.Calls | Where-Object { $_ -like 'agy|leader*start' }).Count -eq 1) "unexpected calls: $($session.Calls -join '; ')"
}
Test-Case "session: both agents fail -> START_FAILED, never PARTIAL or 0/0" {
    $session = Invoke-LineSession -Prompt "Explain the project" -Env @{ FAKE_CODEX_MODE = "fail"; FAKE_AGY_MODE = "fail" }
    Assert-True ($session.Output -match 'STATUS: START_FAILED' -and $session.Output -notmatch 'PARTIAL') "output: $($session.Output)"
    Assert-True ($session.Output -match 'exit code 1' -and $session.Output -match 'Tasks: 0 \| Done: 0 \| Failed: 0') "reason or counters missing: $($session.Output)"
    Assert-True (@($session.Calls | Where-Object { $_ -like 'codex|*' }).Count -eq 1) "Codex retried more than once (bounce)"
}
Test-Case "session: Antigravity unavailable -> Codex only, request still runs" {
    $session = Invoke-LineSession -Prompt "Explain" -Leader "Antigravity" -Env @{ FAKE_AGY_MODE = "noversion"; FAKE_PLAN = "complete" }
    Assert-True ($session.Output -match 'STATUS: COMPLETE') "output: $($session.Output)"
    Assert-True (@($session.Calls | Where-Object { $_ -like 'agy|*' }).Count -eq 0) "unavailable AGY was still launched"
}
Test-Case "session: Codex unavailable -> Antigravity leads" {
    $session = Invoke-LineSession -Prompt "Explain" -Leader "Codex" -Env @{ FAKE_CODEX_MODE = "noversion"; FAKE_PLAN = "complete" }
    Assert-True ($session.Output -match 'STATUS: COMPLETE') "output: $($session.Output)"
    Assert-True (@($session.Calls | Where-Object { $_ -like 'codex|*' }).Count -eq 0) "unavailable Codex was still launched"
}
Test-Case "session: one task succeeds, one fails -> PARTIAL with correct counters" {
    $session = Invoke-LineSession -Prompt "Inspect A and B" -Env @{ FAKE_PLAN = "partial"; FAKE_AGY_MODE = "noversion" }
    Assert-True ($session.Output -match 'STATUS: PARTIAL' -and $session.Output -match 'Tasks: 2 \| Done: 1 \| Failed: 1') "output: $($session.Output)"
}
if (-not $SkipSlow) {
    Test-Case "session: two Antigravity workers run in parallel and both finish" {
        $session = Invoke-LineSession -Prompt "Write two files" -Env @{ FAKE_PLAN = "agy2" } -TimeoutSeconds 240
        Assert-True ($session.Output -match 'STATUS: COMPLETE' -and $session.Output -match 'Tasks: 2 \| Done: 2 \| Failed: 0') "output: $($session.Output)"
        $spans = @($session.Calls | Where-Object { $_ -like 'agy|task|*' } | ForEach-Object { $p = $_ -split '\|'; [pscustomobject]@{ Pid = $p[3]; At = [datetime]$p[2]; Edge = $p[4] } })
        $starts = @($spans | Where-Object Edge -eq 'start' | Sort-Object At); $ends = @($spans | Where-Object Edge -eq 'end' | Sort-Object At)
        Assert-True ($starts.Count -eq 2 -and $starts[1].At -lt $ends[0].At) "workers did not overlap: $($session.Calls -join '; ')"
        Assert-True ((Test-Path (Join-Path $project 'one.txt')) -and (Test-Path (Join-Path $project 'two.txt'))) "worker output missing"
    }
}
Test-Case "session: no fake agent processes remain after sessions exit" {
    Start-Sleep -Seconds 1
    $left = @(Get-FakeProcesses)
    Assert-True ($left.Count -eq 0) ("orphans: " + (($left | ForEach-Object { "$($_.ProcessId) $($_.Name)" }) -join ', '))
}

# ------------------------------------------------ in-process cancellation
Test-Case "cancel: Ctrl+C path stops running request and its processes" {
    $env:FAKE_CODEX_MODE = "hang"; $env:FAKE_STATE = $work; $env:FAKE_CALLS = Join-Path $work "cancel-calls.log"; $env:AGEX_AGY_PATH = $agyPath; $env:FAKE_AGY_MODE = "noversion"
    try {
        . (Join-Path $scripts "agex-primary.ps1") -Project $project -CodexPath $codexPath -AgyPath $agyPath -SessionId "cancel-test" -ConfiguredLeader Codex -DefinitionsOnly
        $script:queuedPrompts = [System.Collections.Generic.Queue[string]]::new()
        [void](Invoke-AgexPrecheck)
        [void](Start-AgexTaskExecution -Prompt "hang please")
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline -and @($script:runtime.OwnedProcesses.Keys).Count -eq 0) { Start-Sleep -Milliseconds 100 }
        Assert-True (@($script:runtime.OwnedProcesses.Keys).Count -gt 0) "Codex never started"
        Request-AgexTaskCancellation
        $deadline = (Get-Date).AddSeconds(20)
        while ($script:activeExecution -and (Get-Date) -lt $deadline) { Pump-AgexTaskExecution; Start-Sleep -Milliseconds 100 }
        Assert-True (-not $script:activeExecution) "execution did not finish after cancel"
        Assert-True ($script:ui.Status -eq "CANCELLED" -and $script:ui.Outcome.Status -eq "CANCELLED" -and $script:ui.CancellationProcessesCleaned -eq "YES") "status $($script:ui.Status) cleaned $($script:ui.CancellationProcessesCleaned)"
        Start-Sleep -Milliseconds 500
        Assert-True (@(Get-FakeProcesses).Count -eq 0) "fake Codex still running after cancel"
    } finally { $env:FAKE_CODEX_MODE = ""; $env:FAKE_AGY_MODE = "" }
}


# ------------------------------------------------------ desktop protocol
function Invoke-ServeSession {
    # Drives the engine the same way the desktop app does: JSON lines on stdin/stdout.
    param([object[]]$Commands, [scriptblock]$Until, [hashtable]$Env = @{}, [int]$TimeoutSeconds = 90)
    $state = Join-Path $work ("serve-" + [guid]::NewGuid().ToString("N")); New-Item -ItemType Directory -Path $state -Force | Out-Null
    $all = @{ FAKE_STATE = $state; FAKE_CALLS = (Join-Path $state "calls.log"); FAKE_CODEX_MODE = ""; FAKE_AGY_MODE = ""; FAKE_PLAN = "complete"; AGEX_AGY_PATH = $agyPath; AGEX_CODEX_PATH = $codexPath }
    foreach ($key in $Env.Keys) { $all[$key] = $Env[$key] }
    $saved = @{}; foreach ($key in $all.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key, [string]$all[$key]) }
    try {
        $psi = [Diagnostics.ProcessStartInfo]::new($shell, "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $scripts 'agex-primary.ps1')`" -Serve -Project `"$project`"")
        $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
        $psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
        $p = [Diagnostics.Process]::Start($psi)
        $in = [IO.StreamWriter]::new($p.StandardInput.BaseStream, [Text.UTF8Encoding]::new($false)); $in.AutoFlush = $true
        $errTask = $p.StandardError.ReadToEndAsync()
        foreach ($c in $Commands) { $in.WriteLine(($c | ConvertTo-Json -Compress -Depth 6)) }
        $events = [System.Collections.Generic.List[object]]::new()
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $read = $p.StandardOutput.ReadLineAsync()
        $done = $false
        while ((Get-Date) -lt $deadline) {
            if (-not $read.Wait(200)) { continue }
            if ($null -eq $read.Result) { break }
            $evt = $read.Result | ConvertFrom-Json
            $events.Add($evt)
            $read = $p.StandardOutput.ReadLineAsync()
            if (-not $done -and (& $Until $evt $events)) { $done = $true; $in.WriteLine('{"cmd":"sessions.list","reqId":"s1"}'); $in.WriteLine('{"cmd":"shutdown"}') }
        }
        $exited = $p.WaitForExit(15000)
        if (-not $exited) { $p.Kill() }
        [pscustomobject]@{ Events = $events; Exited = $exited; ExitCode = $(if ($exited) { $p.ExitCode } else { -1 }); Stderr = $errTask.Result; Finished = $done }
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) } }
}

Test-Case "desktop protocol: scan, request, live messages, session history, clean shutdown" {
    $run = Invoke-ServeSession -Commands @(@{ cmd = "scan"; reqId = "1" }, @{ cmd = "start"; prompt = ("How many files? " + [char]0x00E9 + [char]0x0645); reqId = "2" }) -Until { param($e) $e.type -eq "state" -and $e.outcome -and $e.outcome.status -like "COMPLETE*" }
    Assert-True ($run.Finished -and $run.Exited -and $run.ExitCode -eq 0) "serve did not finish cleanly: exit $($run.ExitCode) $($run.Stderr)"
    $scan = @($run.Events | Where-Object type -eq "scan")[0].scan
    Assert-True ($scan.Summary.Supported -eq 2 -and @($scan.Agents | Where-Object { $_.Status -eq "DETECTED" -and $_.Ready }).Count -eq 0) "scan must separate supported from detected-only tools"
    $messages = @($run.Events | Where-Object type -eq "message" | ForEach-Object { $_.message })
    Assert-True (@($messages | Where-Object { $_.From -eq "User" -and $_.Type -eq "ASSIGNMENT" -and $_.Text -match ([char]0x0645) }).Count -eq 1) "user request message missing or not UTF-8 intact"
    Assert-True (@($messages | Where-Object { $_.From -eq "Codex" -and $_.To -eq "User" -and $_.Type -eq "RESULT" }).Count -eq 1) "leader result message missing"
    $sessions = @($run.Events | Where-Object type -eq "sessions")[0].items
    Assert-True (@($sessions).Count -ge 1 -and @($sessions)[0].Status -like "COMPLETE*") "session history not saved"
    Assert-True (@($run.Events | Where-Object type -eq "bye").Count -eq 1) "engine did not say bye"
}
Test-Case "desktop protocol: settings round trip and disabled agent is not used" {
    $run = Invoke-ServeSession -Commands @(@{ cmd = "settings.set"; reqId = "1"; settings = @{ enabled_agents = @("antigravity"); leader = "Codex" } }, @{ cmd = "start"; prompt = "Explain"; reqId = "2" }) -Until { param($e) $e.type -eq "state" -and $e.outcome }
    $calls = @(Get-ChildItem -LiteralPath $work -Filter calls.log -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content)
    Assert-True ($run.Finished) "request did not finish"
    Assert-True (@($calls | Where-Object { $_ -like "codex|*" }).Count -eq 0) "Codex was used although it is turned off"
    $settings = @($run.Events | Where-Object type -eq "settings")[0].settings
    Assert-True (@($settings.enabled_agents) -join "," -eq "antigravity") "settings not saved"
    $prefs = Get-AgexPreferences; $prefs.enabled_agents = @("codex", "antigravity"); Save-AgexPreferences -Preferences $prefs
}
Test-Case "routing: file-writing task never goes to read-only Codex" {
    function Set-AgexUiTaskState { param($TaskId, $Status, $Agent, $Reason, $ErrorText) }
    $script:runtime = New-AgexRuntime -SessionId "cap"
    $script:ui = New-AgexUiState -Project $project -SessionId "cap"
    Set-AgexAgentHealth -Runtime $script:runtime -Agent Antigravity -Healthy $false -Reason "down"
    $entry = [pscustomobject]@{ Id = "task-0001"; Summary = "Edit"; Agent = "Antigravity"; Status = "QUEUED"; AffectedFiles = @("a.txt"); Error = ""; Reason = ""; End = [datetime]::MinValue }
    Add-AgexUiTask -State $script:ui -Task $entry
    Assert-True (-not (Resolve-AgexTaskAgent -Entry $entry)) "write task was reassigned to read-only Codex"
    Assert-True ($entry.Status -eq "FAILED" -and $entry.Error -match "read-only") "write task must fail with a clear reason, got $($entry.Status): $($entry.Error)"
    $read = [pscustomobject]@{ Id = "task-0002"; Summary = "Read"; Agent = "Antigravity"; Status = "QUEUED"; AffectedFiles = @(); Error = ""; Reason = ""; End = [datetime]::MinValue }
    Add-AgexUiTask -State $script:ui -Task $read
    Assert-True ((Resolve-AgexTaskAgent -Entry $read) -and $read.Agent -eq "Codex") "read-only task should move to Codex"
    $script:runtime.CodexSandbox = "workspace-write"
    Assert-True (Test-AgexAgentCanWrite -Agent Codex) "workspace-write policy must allow Codex edits"
}
Test-Case "storage: custom AGEX_HOME is isolated and never imports other settings" {
    Initialize-AgexStorage
    $marker = Get-Content -LiteralPath (Join-Path (Get-AgexDataRoot) "migration.json") -Raw
    Assert-True ((Get-AgexDataRoot) -like "$work*" -and $marker -match "custom AGEX_HOME") "tests must not use or migrate real AGEX data"
    Assert-True ((Get-AgexPreferences).version -eq 3) "settings schema version"
}
Test-Case "cli: agex version, agents and doctor" {
    $env:AGEX_AGY_PATH = $agyPath; $env:AGEX_CODEX_PATH = $codexPath; $env:FAKE_CODEX_MODE = ""; $env:FAKE_AGY_MODE = ""
    $agex = Join-Path (Split-Path -Parent $scripts) "bin\agex.cmd"
    $version = (& $agex version | Out-String).Trim()
    Assert-True ($version -eq ([IO.File]::ReadAllText((Join-Path (Split-Path -Parent $scripts) "VERSION"))).Trim()) "version mismatch: $version"
    $agents = & $agex agents 2>&1 | Out-String
    Assert-True ($LASTEXITCODE -eq 0 -and $agents -match "2 supported" -and $agents -match "Supported, ready") "agents output: $agents"
    $doctor = & $agex doctor 2>&1 | Out-String
    Assert-True ($LASTEXITCODE -eq 0 -and $doctor -match "Engine files\s+All present") "doctor output: $doctor"
}
if (-not $SkipSlow) {
    Test-Case "installer: verified local package install, tamper refusal, uninstall" {
        $dist = Join-Path $work "dist"; $target = Join-Path $work "installed"
        & (Join-Path (Split-Path -Parent $scripts) "tools\build-release.ps1") -OutputDir $dist | Out-Null
        $zip = @(Get-ChildItem -LiteralPath $dist -Filter "agex-*-win.zip")[0].FullName
        $out = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dist "agex-install.ps1") -Package $zip -InstallDir $target -NoLaunch -NoPath -NoShortcut 2>&1 | Out-String
        Assert-True ($LASTEXITCODE -eq 0 -and (Test-Path (Join-Path $target "AGEX.exe")) -and (Test-Path (Join-Path $target "install.json")) -and $out -match "Checksum verified") "install failed: $out"
        $installedVersion = (& $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $target "agex.ps1") version | Out-String).Trim()
        Assert-True ($installedVersion -match '^\d+\.\d+') "installed agex does not run: $installedVersion"
        $bad = Join-Path $work "bad"; New-Item -ItemType Directory -Path $bad -Force | Out-Null
        Copy-Item $zip (Join-Path $bad (Split-Path -Leaf $zip)); Copy-Item (Join-Path $dist "SHA256SUMS.txt") $bad
        [IO.File]::AppendAllText((Join-Path $bad (Split-Path -Leaf $zip)), "tampered")
        $tamper = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dist "agex-install.ps1") -Package (Join-Path $bad (Split-Path -Leaf $zip)) -InstallDir (Join-Path $work "installed2") -NoLaunch -NoPath -NoShortcut 2>&1 | Out-String
        Assert-True ($LASTEXITCODE -ne 0 -and $tamper -match "Checksum mismatch" -and -not (Test-Path (Join-Path $work "installed2\agex.ps1"))) "tampered package was not refused: $tamper"
        $un = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $target "agex.ps1") uninstall 2>&1 | Out-String
        Start-Sleep -Seconds 5
        Assert-True ($un -match "AGEX was removed" -and -not (Test-Path (Join-Path $target "agex.ps1"))) "uninstall failed: $un"
    }
}

# ------------------------------------------------------ frame and input
Test-Case "frame: every size fits, input stays at the bottom, compact when small" {
    $state = New-AgexUiState -Project $project -SessionId "frame" -ConfiguredLeader Codex -ResolvedLeader Codex -CodexShare 10 -AntigravityShare 90
    $runtime = New-AgexRuntime -SessionId "frame"
    Set-AgexAgentHealth -Runtime $runtime -Agent Codex -Healthy $true; Set-AgexAgentHealth -Runtime $runtime -Agent Antigravity -Healthy $false -Reason "missing"
    $editor = New-AgexEditor
    Add-AgexEditorText -Editor $editor -Text ("x" * 500 + "`nsecond line")
    foreach ($size in @(@(120, 40), @(80, 24), @(60, 20), @(45, 14), @(30, 10))) {
        $frame = Get-AgexScreenFrame -State (New-AgexUiRenderSnapshot -State $state) -Runtime $runtime -Editor $editor -View @{ Name = "NORMAL"; Lines = @(); Scroll = 0 } -Width $size[0] -Height $size[1]
        Assert-True ($frame.Rows.Count -eq [math]::Max(6, $size[1] - 1)) "row count $($frame.Rows.Count) for $($size -join 'x')"
        Assert-True ($frame.CursorRow -lt $frame.Rows.Count -and $frame.CursorRow -ge $frame.Rows.Count - 6) "cursor not in input area for $($size -join 'x')"
        Assert-True ($frame.CursorCol -lt $size[0]) "cursor beyond width"
    }
    $small = Get-AgexScreenFrame -State (New-AgexUiRenderSnapshot -State $state) -Runtime $runtime -Editor $editor -View @{ Name = "NORMAL"; Lines = @(); Scroll = 0 } -Width 45 -Height 14
    Assert-True ($small.Rows[0].Text -like "AGEX |*") "small window must use compact header"
}
Test-Case "frame: status updates while typing do not change the input rows" {
    $state = New-AgexUiState -Project $project -SessionId "typing" -ConfiguredLeader Codex -ResolvedLeader Codex -CodexShare 10 -AntigravityShare 90
    $runtime = New-AgexRuntime -SessionId "typing"
    $editor = New-AgexEditor; Add-AgexEditorText -Editor $editor -Text "half typed request"
    $view = @{ Name = "NORMAL"; Lines = @(); Scroll = 0 }
    $before = Get-AgexScreenFrame -State (New-AgexUiRenderSnapshot -State $state) -Runtime $runtime -Editor $editor -View $view -Width 100 -Height 30
    $state.Status = "RUNNING"; $state.RequestStarted = Get-Date
    for ($i = 0; $i -lt 30; $i++) { [void](Add-AgexUiEvent -State $state -Source "AGEX" -Kind "START" -Message ("worker update $i " + ("long " * 40))) }
    $after = Get-AgexScreenFrame -State (New-AgexUiRenderSnapshot -State $state) -Runtime $runtime -Editor $editor -View $view -Width 100 -Height 30
    $n = $before.Rows.Count
    for ($i = $n - 6; $i -lt $n - 1; $i++) { Assert-True ($before.Rows[$i].Text -eq $after.Rows[$i].Text) "input row $i changed during status update" }
    Assert-True ($before.CursorRow -eq $after.CursorRow -and $before.CursorCol -eq $after.CursorCol) "cursor moved during status update"
    Assert-True ((Get-AgexEditorText -Editor $editor) -eq "half typed request") "draft lost"
}
Test-Case "frame: outcome shows reason, fallback and next steps; long result wraps" {
    $state = New-AgexUiState -Project $project -SessionId "outcome" -ConfiguredLeader Codex -ResolvedLeader Codex -CodexShare 10 -AntigravityShare 90
    $state.Status = "START_FAILED"
    $state.Outcome = [pscustomobject]@{ Status = "START_FAILED"; Headline = "Request could not start."; Reason = "No agent could plan this request. Codex stopped with exit code 1. " + ("detail " * 60); Verification = ""; Tasks = 0; Done = 0; Failed = 0; Cancelled = 0; Executors = [pscustomobject]@{ Codex = 1; Antigravity = 1 }; Fallbacks = @(); Failures = @(); PrimaryFailure = ""; WhatHappened = @("Codex could not run planning; switched to Antigravity automatically (did not recover)."); TaskResults = @(); DurationSeconds = 12 }
    $frame = Get-AgexScreenFrame -State (New-AgexUiRenderSnapshot -State $state) -Runtime (New-AgexRuntime) -Editor (New-AgexEditor) -View @{ Name = "NORMAL"; Lines = @(); Scroll = 0 } -Width 90 -Height 32
    $text = ($frame.Rows | ForEach-Object { $_.Text }) -join "`n"
    Assert-True ($text -match 'Request could not start' -and $text -match 'exit code 1' -and $text -match 'What AGEX did' -and $text -match ':retry' -and $text -notmatch 'PARTIAL' -and $text -notmatch 'Command details unavailable') "outcome text: $text"
}
Test-Case "editor: typing, newline, word delete, send and commands" {
    . (Join-Path $scripts "agex-primary.ps1") -Project $project -CodexPath $codexPath -SessionId "keys" -ConfiguredLeader Codex -DefinitionsOnly -SkipPrecheck
    $script:editor = New-AgexEditor; $script:view = @{ Name = "NORMAL"; Title = ""; Lines = @(); Scroll = 0 }; $script:screen = New-AgexScreen; $script:pendingConfirm = $null; $script:quitRequested = $false
    $script:queuedPrompts = [System.Collections.Generic.Queue[string]]::new()
    $script:sent = @()
    function Submit-AgexPrompt { param($Prompt, $Leader) $script:sent += $Prompt }
    $key = { param([char]$c, [ConsoleKey]$k, [bool]$ctrl = $false) [ConsoleKeyInfo]::new($c, $k, $false, $false, $ctrl) }
    foreach ($c in "hello world".ToCharArray()) { Invoke-AgexKey -Key (& $key $c ([ConsoleKey]::A)) }
    Invoke-AgexKey -Key (& $key ([char]13) ([ConsoleKey]::Enter))
    foreach ($c in "line two".ToCharArray()) { Invoke-AgexKey -Key (& $key $c ([ConsoleKey]::A)) }
    Invoke-AgexKey -Key (& $key ([char]8) ([ConsoleKey]::Backspace) $true)
    Assert-True ((Get-AgexEditorText -Editor $script:editor) -eq "hello world`nline ") "editor text: $(Get-AgexEditorText -Editor $script:editor)"
    Invoke-AgexKey -Key (& $key ([char]10) ([ConsoleKey]::Enter) $true)
    Assert-True ($script:sent.Count -eq 1 -and $script:sent[0] -eq "hello world`nline" -and (Get-AgexEditorText -Editor $script:editor) -eq "") "Ctrl+Enter did not send"
    foreach ($c in ":log 5".ToCharArray()) { Invoke-AgexKey -Key (& $key $c ([ConsoleKey]::A)) }
    Invoke-AgexKey -Key (& $key ([char]13) ([ConsoleKey]::Enter))
    Assert-True ($script:view.Name -eq "LOG" -and $script:view.Title -match 'last 5') "log view not opened"
    foreach ($c in ":details".ToCharArray()) { Invoke-AgexKey -Key (& $key $c ([ConsoleKey]::A)) }
    Invoke-AgexKey -Key (& $key ([char]13) ([ConsoleKey]::Enter))
    Assert-True ($script:view.Name -eq "DETAILS" -and (($script:view.Lines -join "`n") -match 'AGENT RUNS' -and ($script:view.Lines -join "`n") -match 'Log file')) "details view incomplete"
    foreach ($c in "keep this draft".ToCharArray()) { Invoke-AgexKey -Key (& $key $c ([ConsoleKey]::A)) }
    Invoke-AgexKey -Key (& $key ([char]27) ([ConsoleKey]::Escape))
    Assert-True ($script:view.Name -eq "NORMAL" -and (Get-AgexEditorText -Editor $script:editor) -eq "keep this draft") "Esc lost the draft or view"
    Invoke-AgexKey -Key (& $key ([char]3) ([ConsoleKey]::C) $true)
    Assert-True ((Get-AgexEditorText -Editor $script:editor) -eq "" -and -not $script:quitRequested) "first Ctrl+C should clear the draft only"
    Invoke-AgexKey -Key (& $key ([char]3) ([ConsoleKey]::C) $true)
    Assert-True ($script:pendingConfirm.Kind -eq "quit") "second Ctrl+C should ask to quit"
    Invoke-AgexKey -Key (& $key 'y' ([ConsoleKey]::Y))
    Assert-True ($script:quitRequested) ":quit confirmation did not quit"
}

# ---------------------------------------------------------------- report
try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue } catch { }
$failed = @($script:results | Where-Object { -not $_.Pass })
foreach ($item in $script:results) { Write-Output ("{0} {1} ({2}s){3}" -f $(if ($item.Pass) { "PASS" } else { "FAIL" }), $item.Name, $item.Seconds, $(if ($item.Pass) { "" } else { " :: " + $item.Detail })) }
Write-Output ("AGEX ENGINE TESTS: {0} passed, {1} failed" -f ($script:results.Count - $failed.Count), $failed.Count)
if ($failed.Count) { exit 1 }
