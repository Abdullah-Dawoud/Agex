[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][string]$Path,
    [switch]$DryRun,
    [switch]$CreateDocs
)

$ErrorActionPreference = "Stop"
$setupRoot = Split-Path -Parent $PSScriptRoot
$target = [IO.Path]::GetFullPath($Path)
if (-not (Test-Path -LiteralPath $target -PathType Container)) { throw "Project directory not found: $target" }

function Show-Action {
    param([string]$Action, [string]$File)
    if ($DryRun) { Write-Output "DRY-RUN ${Action}: $File" }
    else { Write-Output "${Action}: $File" }
}

function Ensure-File {
    param([string]$Destination, [string]$Source)
    if (Test-Path -LiteralPath $Destination) { Write-Output "KEEP: $Destination"; return }
    if ($DryRun) { Show-Action "CREATE" $Destination; return }
    Copy-Item -LiteralPath $Source -Destination $Destination
    Show-Action "CREATE" $Destination
}

$gitDir = Join-Path $target ".git"
if (-not (Test-Path -LiteralPath $gitDir -PathType Container)) { Write-Output "WARNING: target is not a Git repository: $target" }

Ensure-File (Join-Path $target "AGENTS.md") (Join-Path $setupRoot "templates\AGENTS.project.md")

if ($CreateDocs) {
    $docs = Join-Path $target "docs"
    if (-not (Test-Path -LiteralPath $docs)) {
        if ($DryRun) { Show-Action "CREATE DIRECTORY" $docs } else { New-Item -ItemType Directory -Path $docs | Out-Null; Show-Action "CREATE DIRECTORY" $docs }
    }
    foreach ($name in @("ARCHITECTURE.md", "SECURITY.md", "DECISIONS.md")) {
        $destination = Join-Path $docs $name
        if (Test-Path -LiteralPath $destination) { Write-Output "KEEP: $destination"; continue }
        if ($DryRun) { Show-Action "CREATE" $destination; continue }
        $title = [IO.Path]::GetFileNameWithoutExtension($name)
        Set-Content -LiteralPath $destination -Value "# $title`n`nAdd project-specific content. Do not copy setup-repository machine state here.`n" -Encoding utf8
        Show-Action "CREATE" $destination
    }
}

Write-Output "Project initialization complete: $target"
