[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$BackupFile
)

$ErrorActionPreference = "Stop"
$userRoot = $env:USERPROFILE
$allowed = @(
    [IO.Path]::GetFullPath((Join-Path $userRoot ".codex\config.toml")),
    [IO.Path]::GetFullPath((Join-Path $userRoot ".codex\AGENTS.md")),
    [IO.Path]::GetFullPath((Join-Path $userRoot ".gitconfig"))
)
$target = [IO.Path]::GetFullPath($Path)
if ($allowed -notcontains $target) { throw "Path not allowlisted: $target" }
if (-not (Test-Path -LiteralPath $BackupFile -PathType Leaf)) { throw "Backup file not found: $BackupFile" }

if ($PSCmdlet.ShouldProcess($target, "Restore allowlisted configuration from backup")) {
    Copy-Item -LiteralPath $BackupFile -Destination $target -Force
    Write-Output "Restored allowlisted configuration: $target"
}
