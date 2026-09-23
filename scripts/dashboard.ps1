[CmdletBinding()]
param(
    [switch]$Open,
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "reports\dashboard.html")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$doctor = & (Join-Path $PSScriptRoot "doctor.ps1") -Json | ConvertFrom-Json
$workerNames = @("Browser worker", "Computer Use", "Screenshots", "Documents", "PDFs", "Spreadsheets", "Browser demo video", "Research")
$integrationNames = @("GitHub workflow", "Email worker", "Cloud files")
$knowledgeNames = @("AGENTS", "Project docs", "Git history", "Persistent memory status")
$routingNames = @("Direct Codex routing", "OmniRoute routing")
$categorizedNames = $workerNames + $integrationNames + $knowledgeNames + $routingNames
$developerRows = foreach ($item in $doctor | Where-Object { $categorizedNames -notcontains $_.Name }) {
    $class = $item.Status.ToLowerInvariant().Replace(" ", "-")
    "<tr><td>$([System.Net.WebUtility]::HtmlEncode($item.Name))</td><td class='$class'>$([System.Net.WebUtility]::HtmlEncode($item.Status))</td><td>$([System.Net.WebUtility]::HtmlEncode($item.Detail))</td></tr>"
}
$workerRows = foreach ($item in $doctor | Where-Object { $workerNames -contains $_.Name }) {
    $class = $item.Status.ToLowerInvariant().Replace(" ", "-")
    "<tr><td>$([System.Net.WebUtility]::HtmlEncode($item.Name))</td><td class='$class'>$([System.Net.WebUtility]::HtmlEncode($item.Status))</td><td>$([System.Net.WebUtility]::HtmlEncode($item.Detail))</td></tr>"
}
$integrationRows = foreach ($item in $doctor | Where-Object { $integrationNames -contains $_.Name }) {
    $class = $item.Status.ToLowerInvariant().Replace(" ", "-")
    "<tr><td>$([System.Net.WebUtility]::HtmlEncode($item.Name))</td><td class='$class'>$([System.Net.WebUtility]::HtmlEncode($item.Status))</td><td>$([System.Net.WebUtility]::HtmlEncode($item.Detail))</td></tr>"
}
$knowledgeRows = foreach ($item in $doctor | Where-Object { $knowledgeNames -contains $_.Name }) {
    $class = $item.Status.ToLowerInvariant().Replace(" ", "-")
    "<tr><td>$([System.Net.WebUtility]::HtmlEncode($item.Name))</td><td class='$class'>$([System.Net.WebUtility]::HtmlEncode($item.Status))</td><td>$([System.Net.WebUtility]::HtmlEncode($item.Detail))</td></tr>"
}
$routingRows = foreach ($item in $doctor | Where-Object { $routingNames -contains $_.Name }) {
    $class = $item.Status.ToLowerInvariant().Replace(" ", "-")
    "<tr><td>$([System.Net.WebUtility]::HtmlEncode($item.Name))</td><td class='$class'>$([System.Net.WebUtility]::HtmlEncode($item.Status))</td><td>$([System.Net.WebUtility]::HtmlEncode($item.Detail))</td></tr>"
}
$modeRows = @(
    @{ Name = "AGEX"; Purpose = "Interactive AI control center and project/mode launcher"; Tools = "Plain, Caveman, Coworker, Orchestrator, combinations"; Launch = "agex"; Status = (($doctor | Where-Object Name -eq "Mode launcher").Status) },
    @{ Name = "PLAIN"; Purpose = "Ordinary coding with base Codex configuration"; Tools = "Files, terminal, Git, tests"; Launch = ".\setup.ps1 codex"; Status = "READY" },
    @{ Name = "CAVEMAN"; Purpose = "Normal coding with Caveman communication active"; Tools = "Files, terminal, Git, tests"; Launch = ".\setup.ps1 caveman"; Status = (($doctor | Where-Object Name -eq "Mode profiles").Status) },
    @{ Name = "COWORKER"; Purpose = "General computer work with task-scoped worker tools"; Tools = "Browser, Computer Use, Playwright, documents, PDFs, spreadsheets"; Launch = ".\setup.ps1 coworker"; Status = (($doctor | Where-Object Name -eq "Mode profiles").Status) },
    @{ Name = "ORCHESTRATOR"; Purpose = "Codex boss with optional Antigravity worker"; Tools = "Codex, agy headless worker, status ledger"; Launch = ".\setup.ps1 orchestrator"; Status = (($doctor | Where-Object Name -eq "Orchestrator mode").Status) }
)
$modeHtml = foreach ($mode in $modeRows) {
    $class = ([string]$mode.Status).ToLowerInvariant().Replace(" ", "-")
    "<tr><td>$($mode.Name)</td><td class='$class'>$([System.Net.WebUtility]::HtmlEncode([string]$mode.Status))</td><td>$([System.Net.WebUtility]::HtmlEncode($mode.Purpose))</td><td>$([System.Net.WebUtility]::HtmlEncode($mode.Tools))</td><td><code>$([System.Net.WebUtility]::HtmlEncode($mode.Launch))</code></td></tr>"
}
$workerStates = @()
$workerStateRoot = Join-Path $root "reports\workers"
if (Test-Path -LiteralPath $workerStateRoot -PathType Container) {
    $workerStates = @(Get-ChildItem -LiteralPath $workerStateRoot -Filter status.json -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } catch { } } | Sort-Object started_at -Descending | Select-Object -First 8)
}
$workerHtml = if ($workerStates.Count -eq 0) { "<p>No worker runs.</p>" } else { foreach ($state in $workerStates) { "<p><strong>$([System.Net.WebUtility]::HtmlEncode($state.worker))</strong> — $([System.Net.WebUtility]::HtmlEncode($state.status)) — $([System.Net.WebUtility]::HtmlEncode([string]$state.task))</p>" } }
$generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$html = @"
<!doctype html><html><head><meta charset="utf-8"><title>AI Developer Setup</title>
<style>body{font:15px system-ui;margin:2rem;color:#17202a;background:#f7f9fb}main{max-width:1100px;margin:auto;background:white;padding:2rem;border-radius:12px;box-shadow:0 2px 12px #0001}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:.55rem;border-bottom:1px solid #e6e9ed}.ready{color:#087f23}.warning{color:#9a6700}.optional,.not-installed{color:#57606a}.error{color:#cf222e}</style></head>
<body><main><h1>AI Developer Setup</h1><p>Generated $generated. Read-only doctor data. No secrets or memory contents shown.</p>
<h2>Modes</h2><table><thead><tr><th>Mode</th><th>Status</th><th>Purpose</th><th>Main tools</th><th>Launch</th></tr></thead><tbody>$($modeHtml -join "`n")</tbody></table>
<h2>AI Workers</h2>$workerHtml
<h2>Developer</h2><table><thead><tr><th>Capability</th><th>Status</th><th>Detail</th></tr></thead><tbody>$($developerRows -join "`n")</tbody></table>
<h2>Worker</h2><table><thead><tr><th>Capability</th><th>Status</th><th>Detail</th></tr></thead><tbody>$($workerRows -join "`n")</tbody></table>
<h2>Integrations</h2><table><thead><tr><th>Capability</th><th>Status</th><th>Detail</th></tr></thead><tbody>$($integrationRows -join "`n")</tbody></table>
<h2>Knowledge</h2><table><thead><tr><th>Capability</th><th>Status</th><th>Detail</th></tr></thead><tbody>$($knowledgeRows -join "`n")</tbody></table>
<h2>Routing</h2><table><thead><tr><th>Capability</th><th>Status</th><th>Detail</th></tr></thead><tbody>$($routingRows -join "`n")</tbody></table>
<h2>Boundaries</h2><p>Global capability: Codex, Git, browser/computer use, scripts. Project knowledge: AGENTS.md, docs, source, tests. Serena: removed / not required. Persistent memory: none retained.</p>
</main></body></html>
"@
Set-Content -LiteralPath $OutputPath -Value $html -Encoding utf8
Write-Output "Dashboard written: $OutputPath"
if ($Open) { Start-Process $OutputPath }
