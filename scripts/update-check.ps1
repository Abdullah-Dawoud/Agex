[CmdletBinding()]
param([switch]$Json)

$ErrorActionPreference = "SilentlyContinue"
$items = @()
function Add-Item($Name, $Installed, $Source, $Risk) {
    $script:items += [pscustomobject]@{ Name = $Name; Installed = $Installed; OfficialSource = $Source; Risk = $Risk; Action = "Review manually; no automatic update" }
}

$codexResolver = Join-Path $PSScriptRoot "resolve-codex.ps1"
$codexPath = try { (& $codexResolver | Select-Object -First 1) } catch { $null }
if ($codexPath) { Add-Item "Codex" $codexPath "Codex app/official distribution" "HIGH" }
$node = Get-Command node -ErrorAction SilentlyContinue
if ($node) { Add-Item "Node" (& node --version) "https://nodejs.org/" "HIGH" }
$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) { Add-Item "Git" ((& git --version) -join " ") "https://git-scm.com/" "MEDIUM" }
Add-Item "Context7" "deferred" "https://context7.com/docs" "MEDIUM"
Add-Item "Playwright MCP" "deferred" "https://playwright.dev/docs/getting-started-mcp" "MEDIUM"
Add-Item "OmniRoute" "deferred" "https://github.com/ourines/omniroute" "HIGH"

if ($Json) { $items | ConvertTo-Json -Depth 3 } else { $items | Format-Table -AutoSize | Out-String | Write-Host }
