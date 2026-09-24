[CmdletBinding()]
param(
    [ValidateSet("Install", "Status")][string]$Mode = "Install",
    [string]$CodexHome = (Join-Path $env:USERPROFILE ".codex")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$profileRoot = Join-Path $root "config\codex\profiles"
$names = @("caveman", "coworker", "orchestrator")
$backupScript = Join-Path $PSScriptRoot "backup-config.ps1"

if (-not (Test-Path -LiteralPath $CodexHome -PathType Container)) {
    throw "Codex home not found: $CodexHome"
}

if ($Mode -eq "Status") {
    foreach ($name in $names) {
        $target = Join-Path $CodexHome "$name.config.toml"
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            Write-Output "$name`tREADY`t$target"
        } else {
            Write-Output "$name`tNOT INSTALLED`t$target"
        }
    }
    exit 0
}

$config = Join-Path $CodexHome "config.toml"
if (Test-Path -LiteralPath $config -PathType Leaf) {
    $backup = & $backupScript -Path $config
    Write-Output $backup
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$installBackup = Join-Path $CodexHome "mode-profile-backup-$stamp"
New-Item -ItemType Directory -Path $installBackup -Force | Out-Null

foreach ($name in $names) {
    $source = Join-Path $profileRoot "$name.config.toml"
    $target = Join-Path $CodexHome "$name.config.toml"
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Profile template missing: $source" }
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        Copy-Item -LiteralPath $target -Destination (Join-Path $installBackup ([IO.Path]::GetFileName($target))) -Force
        $old = Get-FileHash -LiteralPath $target -Algorithm SHA256
        $new = Get-FileHash -LiteralPath $source -Algorithm SHA256
        if ($old.Hash -ne $new.Hash) { Write-Output "Profile changed; backup saved: $target" }
    }
    Copy-Item -LiteralPath $source -Destination $target -Force
}

Write-Output "Mode profiles installed: $CodexHome"
Write-Output "Rollback: restore files from $installBackup, then remove profile files if they were new."
