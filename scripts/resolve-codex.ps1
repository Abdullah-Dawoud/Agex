[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

function Test-CodexCandidate {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try {
        $probe = (& $Path --version 2>$null | Out-String).Trim()
        return ($LASTEXITCODE -eq 0 -and $probe -match "(?i)codex")
    } catch {
        return $false
    }
}

$onPath = @(Get-Command codex -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1)
if ($onPath -and (Test-CodexCandidate -Path $onPath.Source)) {
    Write-Output $onPath.Source
    exit 0
}

$installRoot = Join-Path $env:LOCALAPPDATA "OpenAI\Codex\bin"
$candidates = @()
if (Test-Path -LiteralPath $installRoot -PathType Container) {
    $candidates = @(Get-ChildItem -LiteralPath $installRoot -Filter "codex.exe" -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
}

foreach ($candidate in $candidates) {
    if (Test-CodexCandidate -Path $candidate.FullName) {
        Write-Output $candidate.FullName
        exit 0
    }
}

# Some Windows shells expose the NVM-managed Codex shim only after profile
# initialization. Discover the already-installed local copy explicitly so a
# fresh/no-profile launcher still resolves the same verified CLI without
# installing another distribution.
$nvmInstallRoot = Join-Path $env:LOCALAPPDATA "Author Software\nvm\installs"
if (Test-Path -LiteralPath $nvmInstallRoot -PathType Container) {
    $nvmCandidates = @(Get-ChildItem -LiteralPath $nvmInstallRoot -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("codex.exe", "codex.cmd", "codex.ps1") } |
        Sort-Object LastWriteTime -Descending)
    foreach ($candidate in $nvmCandidates) {
        if (Test-CodexCandidate -Path $candidate.FullName) {
            Write-Output $candidate.FullName
            exit 0
        }
    }
}

throw "Codex CLI not found on PATH or under verified OpenAI installation root: $installRoot"
