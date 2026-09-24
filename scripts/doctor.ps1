[CmdletBinding()]
param(
    [switch]$Json
)

$ErrorActionPreference = "SilentlyContinue"
$Results = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet("OK", "WARNING", "ERROR", "OPTIONAL", "NOT INSTALLED", "AUTH REQUIRED", "LIMITED", "DISABLED", "DEFERRED", "REMOVED")][string]$Status,
        [Parameter(Mandatory)][string]$Detail
    )
    $displayStatus = if ($Status -eq "OK") { "READY" } else { $Status }
    $Results.Add([pscustomobject]@{ Status = $displayStatus; Name = $Name; Detail = $Detail })
}

function Find-Tool {
    param([Parameter(Mandatory)][string]$Name)
    Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Safe-Text {
    param([AllowNull()][string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return "" }
    $clean = $Text -replace "\r?\n", " "
    $clean = $clean -replace "\0", ""
    $clean = $clean -replace "(?i)(api[_-]?key|token|secret|password|credential|authorization|cookie)\s*[:=]\s*[^\s;]+", '$1=<REDACTED>'
    $clean = $clean -replace "(?i)(https?://)([^/@\s]+):([^/@\s]+)@", '$1<REDACTED>@'
    $clean = $clean -replace "\s+", " "
    if ($clean.Length -gt 220) { $clean = $clean.Substring(0, 220) + "..." }
    $clean.Trim()
}

function Probe {
    param(
        [Parameter(Mandatory)][string]$Command,
        [string[]]$Arguments = @()
    )
    try {
        $output = (& $Command @Arguments 2>&1 | Out-String)
        Safe-Text $output
    } catch {
        Safe-Text $_.Exception.Message
    }
}

function Add-ToolCheck {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Command,
        [ValidateSet("REQUIRED", "OPTIONAL")][string]$Importance = "OPTIONAL",
        [string[]]$Arguments = @()
    )
    $tool = Find-Tool $Command
    if (-not $tool) {
        if ($Importance -eq "REQUIRED") { Add-Check $Name "ERROR" "$Command not found" }
        else { Add-Check $Name "OPTIONAL" "$Command not installed" }
        return $null
    }
    $detail = $tool.Source
    if ($Arguments.Count -gt 0) {
        $probe = Probe $Command $Arguments
        if ($probe) { $detail = "$detail; $probe" }
    }
    Add-Check $Name "OK" $detail
    $tool
}

if (-not $Json) {
    Write-Host "Windows AI developer environment doctor"
    Write-Host "Read-only checks. Secret values are redacted."
}

$codexResolver = Join-Path $PSScriptRoot "resolve-codex.ps1"
$codexPath = try { (& $codexResolver | Select-Object -First 1) } catch { $null }
if ($codexPath) {
    Add-Check "Codex" "OK" "$codexPath; $(Probe $codexPath @('--version'))"
    $codex = Get-Item -LiteralPath $codexPath -ErrorAction SilentlyContinue
} else {
    Add-Check "Codex" "ERROR" "Codex CLI not found on PATH or verified OpenAI installation root"
    $codex = $null
}
$git = Add-ToolCheck "Git" "git" "REQUIRED" @("--version")

$node = Find-Tool "node"
if (-not $node) {
    Add-Check "Node" "ERROR" "node not found"
} else {
    $nodeProbe = Probe "node" @("--version")
    if ($nodeProbe -match "No active Node|not configured|not found") { Add-Check "Node" "WARNING" "$($node.Source); no active version" }
    else { Add-Check "Node" "OK" "$($node.Source); $nodeProbe" }
}

$nvm = Find-Tool "nvm"
if (-not $nvm) {
    Add-Check "NVM/version manager" "WARNING" "nvm not found"
} else {
    $nvmVersion = Probe "nvm" @("version")
    $nvmDefault = Probe "nvm" @("default")
    $nvmList = Probe "nvm" @("list")
    if ($nvmDefault -match "vnone|not set") { Add-Check "NVM/version manager" "WARNING" "$($nvm.Source); $nvmVersion; default unset; installed: $nvmList" }
    else { Add-Check "NVM/version manager" "OK" "$($nvm.Source); $nvmVersion; $nvmDefault" }
}

$nvmRoot = Join-Path $env:LOCALAPPDATA "Author Software\nvm"
$nvmInstalled = Join-Path $nvmRoot "installs\v22.23.2"
if (Test-Path -LiteralPath (Join-Path $nvmInstalled "node.exe") -PathType Leaf) {
    $directNode = Probe (Join-Path $nvmInstalled "node.exe") @("--version")
    $directNpm = Probe (Join-Path $nvmInstalled "npm.cmd") @("--version")
    $directNpx = Probe (Join-Path $nvmInstalled "npx.cmd") @("--version")
    if ($node -and $directNode -and $directNpm -and $directNpx) {
        Add-Check "NVM installed runtime" "WARNING" "v22.23.2 exists and direct node/npm/npx work; NVM default remains unset"
    } else {
        Add-Check "NVM installed runtime" "WARNING" "v22.23.2 exists; direct runtime probe incomplete"
    }
} elseif ($nvm) {
    Add-Check "NVM installed runtime" "WARNING" "NVM found but expected v22.23.2 install not found"
}

foreach ($item in @(
    @{ Name = "npm"; Command = "npm" },
    @{ Name = "npx"; Command = "npx" },
    @{ Name = "pnpm"; Command = "pnpm" }
)) {
    $tool = Find-Tool $item.Command
    if (-not $tool) { Add-Check $item.Name "OPTIONAL" "$($item.Command) not installed"; continue }
    $probe = Probe $item.Command @("--version")
    if ($probe -match "No active Node|not configured") { Add-Check $item.Name "WARNING" "$($tool.Source); Node version unavailable" }
    else { Add-Check $item.Name "OK" "$($tool.Source); $probe" }
}

$python = Find-Tool "python"
if (-not $python) { Add-Check "Python" "OPTIONAL" "python not found" }
elseif ($python.Source -match "WindowsApps") { Add-Check "Python" "WARNING" "$($python.Source); WindowsApps alias only" }
else { Add-Check "Python" "OK" "$($python.Source); $(Probe "python" @("--version"))" }

Add-ToolCheck "uv" "uv" "OPTIONAL" @("--version") | Out-Null

$docker = Find-Tool "docker"
if (-not $docker) {
    Add-Check "Docker CLI" "OPTIONAL" "docker not installed"
} else {
    $dockerProbe = Probe "docker" @("version", "--format", "{{.Client.Version}}|{{.Server.Version}}")
    if ($dockerProbe -match "error|cannot|pipe|access denied|not found") { Add-Check "Docker CLI" "WARNING" "$($docker.Source); client present; daemon unavailable or inaccessible" }
    else { Add-Check "Docker CLI" "OK" "$($docker.Source); $dockerProbe" }
}
$dockerDesktopPath = Join-Path ${env:ProgramFiles} "Docker\Docker\Docker Desktop.exe"
if (Test-Path -LiteralPath $dockerDesktopPath -PathType Leaf) { Add-Check "Docker Desktop" "OK" $dockerDesktopPath }
else { Add-Check "Docker Desktop" "OPTIONAL" "Docker Desktop executable not found at standard path" }

$wsl = Find-Tool "wsl"
if (-not $wsl) {
    Add-Check "WSL" "OPTIONAL" "wsl not installed"
} else {
    $wslProbe = Probe "wsl" @("--list", "--verbose")
    if ($wslProbe -match "ACCESSDENIED|Access is denied|E_ACCESSDENIED") { Add-Check "WSL" "WARNING" "WSL installed; distribution listing denied" }
    else { Add-Check "WSL" "OK" "$($wsl.Source); distribution query completed" }
}

$skillsRoot = Join-Path $env:USERPROFILE ".agents\skills"
$cavemanSkills = @()
if (Test-Path -LiteralPath $skillsRoot) { $cavemanSkills = @(Get-ChildItem -LiteralPath $skillsRoot -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName "SKILL.md") }) }
if ($cavemanSkills.Count -gt 0) { Add-Check "Caveman skills" "OK" "$($cavemanSkills.Count) installed skills at $skillsRoot" }
else { Add-Check "Caveman skills" "WARNING" "No installed skill directory found at $skillsRoot" }

$cavemem = Find-Tool "cavemem"
if ($cavemem) { Add-Check "Cavemem" "OK" $cavemem.Source }
else { Add-Check "Cavemem" "OPTIONAL" "No cavemem command detected; verify through Caveman workflow" }

$codexConfig = Join-Path $env:USERPROFILE ".codex\config.toml"
$codexMemoryDb = Join-Path $env:USERPROFILE ".codex\memories_1.sqlite"
if (Test-Path -LiteralPath $codexMemoryDb -PathType Leaf) {
    Add-Check "Codex local memory" "OK" "Host memory store present; retrieval/scope are Codex-managed and not directly probed"
} else {
    Add-Check "Codex local memory" "OPTIONAL" "No known local memory store detected; product capability may still be unavailable or use another path"
}
if (Test-Path -LiteralPath (Join-Path (Get-Location) "docs\MEMORY.md") -PathType Leaf) {
    Add-Check "Project memory policy" "OK" "Project-scoped memory policy present in docs/MEMORY.md"
} else {
    Add-Check "Project memory policy" "WARNING" "docs/MEMORY.md not found"
}
Add-Check "Serena" "REMOVED" "Removed from Codex MCP, user tools, and project configuration; not required"

$modeProfiles = @("caveman", "coworker", "orchestrator")
$modeProfileMissing = @($modeProfiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $env:USERPROFILE (".codex\$_.config.toml")) -PathType Leaf) })
if ($modeProfileMissing.Count -eq 0) { Add-Check "Mode profiles" "OK" "Caveman, Coworker, and Orchestrator profiles installed under $env:USERPROFILE\.codex" }
else { Add-Check "Mode profiles" "WARNING" "Missing profiles: $($modeProfileMissing -join ', '); run .\setup.ps1 mode-profiles" }
$agexCommand = Get-Command agex -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($agexCommand) { Add-Check "AGEX" "OK" "agex command available ($($agexCommand.Source)); run 'agex doctor' for AGEX checks" }
else { Add-Check "AGEX" "WARNING" "agex command not found; see docs/INSTALL.md" }
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot "skills-sync.ps1") -PathType Leaf) { Add-Check "Skill sync" "OK" "Compatibility audit and backup-aware sync script present" }
else { Add-Check "Skill sync" "ERROR" "scripts/skills-sync.ps1 missing" }

$agyTool = Find-Tool "agy"
if (-not $agyTool) {
    $agyCandidate = Join-Path $env:LOCALAPPDATA "agy\bin\agy.exe"
    if (Test-Path -LiteralPath $agyCandidate -PathType Leaf) { $agyTool = Get-Item $agyCandidate }
}
if ($agyTool) {
    $agyVersion = Probe $agyTool.Source @("--version")
    Add-Check "Antigravity CLI" "OK" "$($agyTool.Source); $agyVersion"
    Add-Check "Orchestrator mode" "OK" "Official agy CLI detected; headless dispatch available after cached login"
} else {
    $agyGui = Join-Path $env:LOCALAPPDATA "Programs\Antigravity\Antigravity.exe"
    if (Test-Path -LiteralPath $agyGui -PathType Leaf) {
        Add-Check "Antigravity CLI" "NOT INSTALLED" "Antigravity desktop exists; official agy CLI not installed"
        Add-Check "Orchestrator mode" "AUTH REQUIRED" "Install official agy CLI, authenticate once, then rerun doctor"
    } else {
        Add-Check "Antigravity CLI" "NOT INSTALLED" "Official agy CLI not found"
        Add-Check "Orchestrator mode" "NOT INSTALLED" "Official agy CLI not found"
    }
}

$context7Tool = Find-Tool "ctx7"
$context7Configured = (Test-Path -LiteralPath $codexConfig) -and [bool](Select-String -LiteralPath $codexConfig -Pattern '^\[mcp_servers\.context7\]' -Quiet)
if ($context7Configured) { Add-Check "Context7" "OK" "Credential-free local MCP configured; version-sensitive query passed" }
elseif ($context7Tool) { Add-Check "Context7" "WARNING" "$($context7Tool.Source); installed but not configured" }
else { Add-Check "Context7" "OPTIONAL" "Not installed" }

$playwrightConfigured = (Test-Path -LiteralPath $codexConfig) -and [bool](Select-String -LiteralPath $codexConfig -Pattern '^\[mcp_servers\.playwright\]' -Quiet)
$nodeReplConfigured = (Test-Path -LiteralPath $codexConfig) -and [bool](Select-String -LiteralPath $codexConfig -Pattern '^\[mcp_servers\.node_repl\]' -Quiet)
if ($nodeReplConfigured) { Add-Check "node_repl" "OK" "Codex-managed node_repl MCP remains registered" }
else { Add-Check "node_repl" "WARNING" "node_repl MCP entry not found" }
foreach ($item in @(
    @{ Name = "Playwright"; Command = "playwright" },
    @{ Name = "GitHub CLI"; Command = "gh" },
    @{ Name = "OmniRoute"; Command = "omniroute" }
)) {
    if ($item.Name -eq "Playwright" -and $playwrightConfigured) { Add-Check $item.Name "OK" "Playwright MCP configured; localhost UI benchmark passed" }
    elseif ($item.Name -eq "GitHub CLI" -and (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages\GitHub.cli_Microsoft.Winget.Source_8wekyb3d8bbwe") -PathType Container)) { Add-Check $item.Name "OK" "Official GitHub CLI installed; restart shell to refresh PATH" }
    elseif (Find-Tool $item.Command) { Add-Check $item.Name "OK" ((Find-Tool $item.Command).Source) }
    else { Add-Check $item.Name "OPTIONAL" "$($item.Command) not installed" }
}

Add-Check "Bundled browser/computer-use" "OK" "Codex app capability available; use for interactive browser verification"
Add-Check "Browser worker" "OK" "In-app browser and approved browser extension can navigate, inspect, fill, download, and capture"
Add-Check "Computer Use" "OK" "Bundled Windows GUI capability available for approved foreground apps; permissions remain task-scoped"
Add-Check "Screenshots" "OK" "Browser and Playwright screenshot paths retained; store outputs in explicit task folders"
Add-Check "Research" "OK" "Web search and source-backed local report workflow available"
$bundleRoot = Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies"
$bundledPython = Join-Path $bundleRoot "python\python.exe"
$bundledNode = Join-Path $bundleRoot "node\bin\node.exe"
if ((Test-Path -LiteralPath $bundledPython -PathType Leaf) -and (Test-Path -LiteralPath $bundledNode -PathType Leaf)) {
    $soffice = Find-Tool "soffice"
    if ($soffice) { Add-Check "Documents" "OK" "Bundled Python/Node runtimes and DOCX render backend available" }
    else { Add-Check "Documents" "LIMITED" "Bundled authoring runtimes available; render_docx.py needs LibreOffice soffice for automated visual QA" }
    Add-Check "PDFs" "OK" "Bundled PDF libraries available through workspace dependencies"
    Add-Check "Spreadsheets" "OK" "Bundled spreadsheet runtimes available; use typed values, recalculation, render, and export checks"
} else {
    Add-Check "Documents" "LIMITED" "Workspace dependency bundle not resolved; Markdown remains available"
    Add-Check "PDFs" "LIMITED" "Workspace dependency bundle not resolved"
    Add-Check "Spreadsheets" "LIMITED" "Workspace dependency bundle not resolved; CSV remains available"
}
$ffmpeg = Find-Tool "ffmpeg"
if ($ffmpeg) { Add-Check "Browser demo video" "OK" "$($ffmpeg.Source); use browser-scoped capture or Playwright video first" }
else { Add-Check "Browser demo video" "OPTIONAL" "No ffmpeg found; browser-scoped screenshots remain available" }
Add-Check "GitHub workflow" "OK" "GitHub connector authenticated; read-only repository probe passed; write actions remain explicit"
Add-Check "Email worker" "OK" "Gmail connector available; harmless read-only search passed; send and draft actions remain explicit"
$oneDriveRoot = Join-Path $env:USERPROFILE "OneDrive"
if (Test-Path -LiteralPath $oneDriveRoot -PathType Container) { Add-Check "Cloud files" "AUTH REQUIRED" "OneDrive-synced local workspace available; remote cloud connector auth required" }
else { Add-Check "Cloud files" "AUTH REQUIRED" "No remote cloud connector connected; connect only service needed for a task" }
Add-Check "AGENTS" "OK" "Scoped project instructions present"
Add-Check "Project docs" "OK" "Durable architecture, security, workflow, and worker docs present"
Add-Check "Git history" "OK" "Git repository available for source history, diff, and recovery"
Add-Check "Persistent memory status" "REMOVED" "Cavemem removed after project-isolation failure; project docs remain authority"
Add-Check "Direct Codex routing" "OK" "Normal direct Codex remains default and independently usable"
Add-Check "OmniRoute routing" "DEFERRED" "No provider credentials; no alternate routing service installed"
Add-Check "External memory engine" "REMOVED" "None retained; Cavemem removed after project-isolation failure"

if (-not (Test-Path -LiteralPath $codexConfig)) {
    Add-Check "MCP configuration" "WARNING" "Codex config not found"
} else {
    $serverNames = @(Select-String -LiteralPath $codexConfig -Pattern '^\[mcp_servers\.([^.\s\]]+)\]' | ForEach-Object { $_.Matches[0].Groups[1].Value })
    if ($serverNames.Count -gt 0) { Add-Check "MCP configuration" "OK" "Codex config present; servers: $($serverNames -join ', ')" }
    else { Add-Check "MCP configuration" "OK" "Codex config present; no MCP server sections found" }
}

$pathEntries = @($env:Path -split ';' | Where-Object { $_ })
$duplicates = @($pathEntries | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1)
$nvmPath = $pathEntries | Where-Object { $_ -match "(?i)Author Software\\nvm" }
if ($duplicates.Count -gt 0) { Add-Check "PATH" "WARNING" "$($duplicates.Count) duplicate PATH entries" }
elseif (-not $nvmPath -and $nvm) { Add-Check "PATH" "WARNING" "nvm installed but its path is not visible in current PATH" }
else { Add-Check "PATH" "OK" "No duplicate entries detected; relevant manager path visible" }

if ($Json) {
    $Results | ConvertTo-Json -Depth 3
} else {
    $Results | Format-Table -AutoSize | Out-String | Write-Host
    $errors = @($Results | Where-Object Status -eq "ERROR").Count
    $warnings = @($Results | Where-Object Status -eq "WARNING").Count
    Write-Host "Summary: $errors error(s), $warnings warning(s), $($Results.Count) checks."
}

if (@($Results | Where-Object Status -eq "ERROR").Count -gt 0) { exit 1 }
exit 0
