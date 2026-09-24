# Builds the AGEX release: dist\agex-<version>-win.zip, dist\agex-install.ps1
# and dist\SHA256SUMS.txt. Uses only tools that ship with Windows.
[CmdletBinding()]
param([string]$OutputDir)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root "dist" }
$version = ([IO.File]::ReadAllText((Join-Path $root "VERSION"))).Trim()
$stage = Join-Path $env:TEMP ("agex-release-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage, (Join-Path $stage "scripts"), (Join-Path $stage "bin"), (Join-Path $stage "docs"), (Join-Path $stage "install") -Force | Out-Null
try {
    & (Join-Path $PSScriptRoot "build-desktop.ps1") -Output (Join-Path $stage "AGEX.exe") | Out-Null
    $runtime = @("agex-common.ps1", "agex-process.ps1", "agex-adapters.ps1", "agex-session.ps1", "agex-graph.ps1", "agex-ui.ps1", "agex-primary.ps1", "agex-maintenance.ps1", "orchestrator.ps1", "worker-run.ps1", "resolve-codex.ps1", "workbench.ps1")
    foreach ($file in $runtime) { Copy-Item -LiteralPath (Join-Path $root "scripts\$file") -Destination (Join-Path $stage "scripts\$file") }
    Copy-Item -LiteralPath (Join-Path $root "bin\agex.cmd") -Destination (Join-Path $stage "bin\agex.cmd")
    Copy-Item -LiteralPath (Join-Path $root "install\agex-install.ps1") -Destination (Join-Path $stage "install\agex-install.ps1")
    foreach ($file in @("agex.ps1", "VERSION", "LICENSE", "README.md", "CHANGELOG.md", "SECURITY.md", "THIRD_PARTY_NOTICES.md")) {
        $path = Join-Path $root $file
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $stage $file) }
    }
    foreach ($doc in @("INSTALL.md", "USAGE.md", "AGENTS.md", "TROUBLESHOOTING.md", "SECURITY.md", "ARCHITECTURE.md")) {
        $path = Join-Path $root "docs\$doc"
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $stage "docs\$doc") }
    }
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    $zip = Join-Path $OutputDir ("agex-{0}-win.zip" -f $version)
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
    $installer = Join-Path $OutputDir "agex-install.ps1"
    Copy-Item -LiteralPath (Join-Path $root "install\agex-install.ps1") -Destination $installer -Force
    $lines = foreach ($file in @($zip, $installer)) { "{0}  {1}" -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash, (Split-Path -Leaf $file) }
    [IO.File]::WriteAllLines((Join-Path $OutputDir "SHA256SUMS.txt"), [string[]]$lines, [Text.UTF8Encoding]::new($false))
    Write-Output "Release $version"
    Get-ChildItem -LiteralPath $OutputDir | ForEach-Object { Write-Output ("  {0} ({1:N0} KB)" -f $_.Name, ($_.Length / 1KB)) }
} finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
