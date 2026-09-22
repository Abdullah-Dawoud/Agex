[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Project,
    [string]$Model = "gemini-3.1-pro-high",
    [ValidateSet("low", "medium", "high")][string]$Effort = "high"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dawoud-common.ps1")
$agy = Resolve-AgyExecutable
if ([string]::IsNullOrWhiteSpace($agy) -or -not (Test-Path -LiteralPath $agy -PathType Leaf)) { throw "Canonical AGY executable not found." }
if (-not (Test-Path -LiteralPath $Project -PathType Container)) { throw "Project directory not found: $Project" }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$debugRoot = Join-Path $env:TEMP "dawoud-agy-direct-$stamp"
New-Item -ItemType Directory -Path $debugRoot -Force | Out-Null
$prompt = "Research nothing external. Return exactly one short sentence: DAWOUD AGY backend is available.`r`n"
$milestones = { param([string]$Name) }
$stream = Invoke-DawoudAgyStream -AgyPath $agy -WorkingDirectory $Project -Prompt $prompt -Model $Model -Effort $Effort -CliLogPath (Join-Path $debugRoot "agy.cli.log") -StdinPath (Join-Path $debugRoot "stdin.ndjson") -RawStdoutPath (Join-Path $debugRoot "stdout.raw.log") -RawStderrPath (Join-Path $debugRoot "stderr.raw.log") -EventLogPath (Join-Path $debugRoot "events.log") -StartupTimeoutSeconds 15 -IdleTimeoutSeconds 30 -TotalTimeoutSeconds 45 -OnMilestone $milestones
Write-Host "DIRECT AGY PID: $($stream.ActualPid)"
Write-Host "STDIN WRITTEN: $(if ($stream.StdinWritten) { 'YES' } else { 'NO' })"
Write-Host "STDIN BYTES: $($stream.StdinBytes)"
Write-Host "FIRST STDOUT LINE: $($stream.FirstStdoutEvent)"
Write-Host "FIRST STDERR LINE: $($stream.FirstStderrEvent)"
Write-Host "STREAM EVENT COUNT: $($stream.StreamEvents)"
Write-Host "FINAL RESULT EVENT: $($stream.FinalResultEvent)"
Write-Host "FINAL RESPONSE: $(if ([string]::IsNullOrWhiteSpace($stream.FinalResponse)) { 'NONE' } else { Protect-DawoudTelemetryText -Text $stream.FinalResponse })"
Write-Host "EXIT CODE: $($stream.ExitCode)"
Write-Host "TIMEOUT REASON: $($stream.TimeoutReason)"
if ($stream.FailedStage) { Write-Host "FAILED STAGE: $($stream.FailedStage)" }
if ($stream.ExceptionType) { Write-Host "EXCEPTION TYPE: $($stream.ExceptionType)" }
if ($stream.ExceptionMessage) { Write-Host "ERROR: $($stream.ExceptionMessage)" }
if ($stream.Stderr) { Write-Host "STDERR: $($stream.Stderr)" }
Write-Host "DEBUG FILES: $debugRoot"
