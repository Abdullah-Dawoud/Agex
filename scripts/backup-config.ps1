[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
param(
    [Parameter(Mandatory)][string[]]$Path,
    [string]$DestinationRoot = (Join-Path $env:LOCALAPPDATA "AI-Developer-Setup\backups")
)

$ErrorActionPreference = "Stop"
$userRoot = $env:USERPROFILE
$allowed = @(
    [IO.Path]::GetFullPath((Join-Path $userRoot ".codex\config.toml")),
    [IO.Path]::GetFullPath((Join-Path $userRoot ".codex\AGENTS.md")),
    [IO.Path]::GetFullPath((Join-Path $userRoot ".gitconfig"))
)

foreach ($inputPath in $Path) {
    $fullPath = [IO.Path]::GetFullPath($inputPath)
    if ($allowed -notcontains $fullPath) { throw "Path not allowlisted: $fullPath" }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "File not found: $fullPath" }
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$targetDir = Join-Path $DestinationRoot $stamp
if ($PSCmdlet.ShouldProcess($targetDir, "Create explicit configuration backup")) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    foreach ($inputPath in $Path) {
        $fullPath = [IO.Path]::GetFullPath($inputPath)
        Copy-Item -LiteralPath $fullPath -Destination (Join-Path $targetDir ([IO.Path]::GetFileName($fullPath))) -Force
    }
    Write-Output "Backup created outside repository: $targetDir"
}
