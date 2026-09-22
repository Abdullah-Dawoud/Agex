[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Join-Path $env:TEMP ("codex-memory-isolation-smoke-" + $PID)

try {
    New-Item -ItemType Directory -Path (Join-Path $root "Project-A\docs") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root "Project-B\docs") -Force | Out-Null

    Set-Content -LiteralPath (Join-Path $root "Project-A\docs\DECISIONS.md") -Value "# Project A`nProvider: Alpha`nVersion: v1`nVersion: v2 supersedes v1" -Encoding utf8
    Set-Content -LiteralPath (Join-Path $root "Project-B\docs\DECISIONS.md") -Value "# Project B`nProvider: Beta`nVersion: v1" -Encoding utf8

    $aText = Get-Content -Raw (Join-Path $root "Project-A\docs\DECISIONS.md")
    $bText = Get-Content -Raw (Join-Path $root "Project-B\docs\DECISIONS.md")
    if ($aText -notmatch "Provider: Alpha" -or $aText -match "Provider: Beta") { throw "Project A scope failed" }
    if ($bText -notmatch "Provider: Beta" -or $bText -match "Provider: Alpha") { throw "Project B scope failed" }
    if ($aText -notmatch "v2 supersedes v1") { throw "Version supersession failed" }

    [pscustomobject]@{
        status = "PASS"
        scope = "Project A and Project B explicit project paths"
        supersession = "v2 supersedes v1 recorded in Project A"
        boundary = "No external memory engine used"
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
