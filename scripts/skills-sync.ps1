[CmdletBinding()]
param(
    [ValidateSet("list", "sync")][string]$Command = "list",
    [string]$SourceRoot = (Join-Path $env:USERPROFILE ".agents\skills"),
    [string]$TargetRoot = (Join-Path $env:USERPROFILE ".gemini\antigravity-cli\skills")
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) { throw "Skill source missing: $SourceRoot" }
$items = @(Get-ChildItem -LiteralPath $SourceRoot -Directory | ForEach-Object {
    $skillFile = Join-Path $_.FullName "SKILL.md"
    if (-not (Test-Path -LiteralPath $skillFile -PathType Leaf)) { return }
    $head = ((Get-Content -LiteralPath $skillFile -Raw) -split "\r?\n" | Select-Object -First 30) -join "`n"
    $name = if ($head -match "(?m)^name:\s*(.+)$") { $Matches[1].Trim() } else { $_.Name }
    $description = if ($head -match "(?ms)^description:\s*(.+?)(?:\r?\n---|\r?\n\r?\n)") { ($Matches[1] -replace "\s+", " ").Trim() } else { "" }
    $codexOnly = $head -match "mcp__|functions\.|codex-app-tools|ChatGPT desktop"
    [pscustomobject]@{ Folder = $_.Name; Name = $name; Description = $description; Classification = $(if ($codexOnly) { "CODEX-ONLY" } else { "SHARED" }); Path = $skillFile }
})

if ($Command -eq "list") {
    $items | Sort-Object Name | ForEach-Object { "{0}`t{1}`t{2}" -f $_.Name, $_.Classification, $_.Description }
    exit 0
}

New-Item -ItemType Directory -Path $TargetRoot -Force | Out-Null
$backupRoot = Join-Path $TargetRoot ("backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
foreach ($item in $items | Where-Object Classification -eq "SHARED") {
    $target = Join-Path $TargetRoot $item.Folder
    if (Test-Path -LiteralPath $target) {
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        Copy-Item -LiteralPath $target -Destination (Join-Path $backupRoot $item.Folder) -Recurse -Force
        $old = Get-FileHash -LiteralPath (Join-Path $target "SKILL.md") -Algorithm SHA256
        $new = Get-FileHash -LiteralPath $item.Path -Algorithm SHA256
        if ($old.Hash -ne $new.Hash) { Write-Output "Updated $($item.Name); old copy backed up." }
    }
    Copy-Item -LiteralPath (Split-Path -Parent $item.Path) -Destination $TargetRoot -Recurse -Force
}
Write-Output "Synced $(@($items | Where-Object Classification -eq 'SHARED').Count) compatible skills to $TargetRoot"
Write-Output "Codex-only skills not copied: $(@($items | Where-Object Classification -eq 'CODEX-ONLY').Count)"
if (Test-Path -LiteralPath $backupRoot) { Write-Output "Backup: $backupRoot" }
