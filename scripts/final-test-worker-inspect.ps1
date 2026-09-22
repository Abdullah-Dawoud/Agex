[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkerRoot,
    [Parameter(Mandatory)][string]$SessionId,
    [Parameter(Mandatory)][string]$WorkId
)
$ErrorActionPreference = "SilentlyContinue"
$common = Join-Path $PSScriptRoot "dawoud-common.ps1"
if (Test-Path -LiteralPath $common -PathType Leaf) { . $common }
if (-not (Test-Path -LiteralPath $WorkerRoot -PathType Container)) { exit 0 }
foreach ($file in @(Get-ChildItem -LiteralPath $WorkerRoot -Filter status.json -Recurse -File)) {
    try {
        $state = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ([string]$state.session_id -eq $SessionId -and [string]$state.work_id -eq $WorkId) {
            $state | Add-Member -MemberType NoteProperty -Name state_path -Value $file.FullName -Force
            $stderrPath = Join-Path $file.DirectoryName "agy.stderr.log"
            $stderrText = if (Test-Path -LiteralPath $stderrPath -PathType Leaf) { (Get-Content -LiteralPath $stderrPath -Raw).Trim() } else { "" }
            $state | Add-Member -MemberType NoteProperty -Name stderr_text -Value $stderrText -Force
            $cliPath = [string]$state.agy_cli_log_path
            $cliTail = if ($cliPath -and (Test-Path -LiteralPath $cliPath -PathType Leaf)) { (Get-Content -LiteralPath $cliPath -Tail 20 | Out-String).Trim() } else { "" }
            if (Get-Command Protect-DawoudTelemetryText -ErrorAction SilentlyContinue) { $cliTail = Protect-DawoudTelemetryText -Text $cliTail }
            $state | Add-Member -MemberType NoteProperty -Name agy_log_tail -Value $cliTail -Force
            $startupPath = [string]$state.startup_diagnostic_path
            $startupTrace = if ($startupPath -and (Test-Path -LiteralPath $startupPath -PathType Leaf)) { Get-Content -LiteralPath $startupPath -Raw } else { "" }
            $state | Add-Member -MemberType NoteProperty -Name startup_trace -Value $startupTrace -Force
            $state | ConvertTo-Json -Compress -Depth 12
            exit 0
        }
    } catch { }
}
