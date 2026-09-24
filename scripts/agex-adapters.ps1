# AGEX agent adapters and system discovery.
#
# An adapter is the only way AGEX talks to an agent. Each adapter declares its
# identity, capabilities and how to find its executable. Execution is bound to
# a fixed engine function per adapter (never an arbitrary command).
#
# Discovery is allowlisted: it checks known install locations and PATH entries
# for known tool names. It never crawls the disk and never runs a program that
# is not a supported adapter. Detected-only tools are reported honestly as
# "Detected - not integrated".

$script:AgexCapabilities = @("READ_FILES", "WRITE_FILES", "RUN_COMMANDS", "WEB_RESEARCH", "BROWSER", "CODE_REVIEW", "TESTING", "PLANNING", "DEBUGGING", "MCP", "IMAGE", "DOCUMENTS")

function Get-AgexAdapters {
    # Supported adapters. To add one, see docs/DEVELOPMENT.md.
    @(
        [pscustomobject]@{
            Id = "codex"; Name = "Codex"; DisplayName = "Codex"; Provider = "OpenAI"; Kind = "Agent"; Support = "SUPPORTED"
            Description = "Coding, review, planning"
            Capabilities = @("READ_FILES", "WRITE_FILES", "RUN_COMMANDS", "CODE_REVIEW", "TESTING", "PLANNING", "DEBUGGING", "MCP")
            WritePolicy = "sandbox"   # file edits only when the user allows workspace-write
            SupportsStreaming = $false; SupportsTools = $true; SupportsMcp = $true; SupportsSubagents = $false
            Resolver = "Resolve-AgexCodexExecutable"; Executor = "Invoke-CodexTask"; CloudService = $true
        },
        [pscustomobject]@{
            Id = "antigravity"; Name = "Antigravity"; DisplayName = "Antigravity"; Provider = "Google"; Kind = "Agent"; Support = "SUPPORTED"
            Description = "Coding, browsing, implementation"
            Capabilities = @("READ_FILES", "WRITE_FILES", "RUN_COMMANDS", "WEB_RESEARCH", "BROWSER", "CODE_REVIEW", "TESTING", "PLANNING", "DEBUGGING", "IMAGE", "DOCUMENTS")
            WritePolicy = "always"
            SupportsStreaming = $true; SupportsTools = $true; SupportsMcp = $false; SupportsSubagents = $false
            Resolver = "Resolve-AgyExecutable"; Executor = "Invoke-AgyTask"; CloudService = $true
        }
    )
}

function Get-AgexAdapter {
    param([Parameter(Mandatory)][string]$Id)
    $key = $Id.ToLowerInvariant()
    @(Get-AgexAdapters | Where-Object { $_.Id -eq $key -or $_.Name.ToLowerInvariant() -eq $key }) | Select-Object -First 1
}

function Resolve-AgexCodexExecutable {
    # Same search order as resolve-codex.ps1, without running the binary.
    if ($env:AGEX_CODEX_PATH -and (Test-Path -LiteralPath $env:AGEX_CODEX_PATH -PathType Leaf)) { return (Resolve-Path -LiteralPath $env:AGEX_CODEX_PATH).Path }
    $onPath = @(Get-Command codex -CommandType Application -ErrorAction SilentlyContinue | Where-Object { $_.Source -match '\.(exe|cmd)$' } | Select-Object -First 1)
    if ($onPath.Count) { return [string]$onPath[0].Source }
    $installRoot = Join-Path $env:LOCALAPPDATA "OpenAI\Codex\bin"
    if (Test-Path -LiteralPath $installRoot -PathType Container) {
        $exe = @(Get-ChildItem -LiteralPath $installRoot -Filter "codex.exe" -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
        if ($exe.Count) { return $exe[0].FullName }
    }
    foreach ($root in @((Join-Path $env:APPDATA "npm"), (Join-Path $env:LOCALAPPDATA "Author Software\nvm\installs"), (Join-Path $env:APPDATA "nvm"))) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        $direct = Join-Path $root "codex.cmd"
        if (Test-Path -LiteralPath $direct -PathType Leaf) { return $direct }
        $nested = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName "codex.cmd" } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1)
        if ($nested.Count) { return $nested[0] }
    }
    ""
}

function Resolve-AgexAdapterExecutable {
    param([Parameter(Mandatory)]$Adapter)
    try { [string](& $Adapter.Resolver) } catch { "" }
}

function Get-AgexFileVersion {
    # Version from file metadata or an adjacent npm package.json; never executes the file.
    param([string]$Path)
    try {
        if ($Path -match '\.exe$') {
            $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
            if ($info.ProductVersion) { return ($info.ProductVersion -split '\s')[0] }
            if ($info.FileVersion) { return $info.FileVersion }
        }
        if ($Path -match '\.cmd$') {
            $name = [IO.Path]::GetFileNameWithoutExtension($Path)
            $dir = Split-Path -Parent $Path
            $packages = @{ codex = "@openai\codex"; claude = "@anthropic-ai\claude-code"; gemini = "@google\gemini-cli"; copilot = "@github\copilot"; qwen = "@qwen-code\qwen-code"; opencode = "opencode-ai" }
            $candidates = @()
            if ($packages.ContainsKey($name)) { $candidates += Join-Path $dir ("node_modules\{0}\package.json" -f $packages[$name]) }
            $candidates += Join-Path $dir "node_modules\$name\package.json"
            foreach ($pkg in $candidates) {
                if (Test-Path -LiteralPath $pkg -PathType Leaf) { return [string](Get-Content -LiteralPath $pkg -Raw | ConvertFrom-Json).version }
            }
        }
    } catch { }
    ""
}

function Find-AgexToolPath {
    # Known file locations first, then PATH. Returns the first existing file.
    param([string[]]$Candidates = @(), [string[]]$CommandNames = @())
    foreach ($candidate in $Candidates) {
        if (-not $candidate) { continue }
        $expanded = [Environment]::ExpandEnvironmentVariables($candidate)
        if ($expanded -match '[*?]') {
            $match = @(Get-ChildItem -Path $expanded -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
            if ($match.Count) { return $match[0].FullName }
        } elseif (Test-Path -LiteralPath $expanded -PathType Leaf) { return $expanded }
    }
    foreach ($name in $CommandNames) {
        $command = @(Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1)
        if ($command.Count) { return [string]$command[0].Source }
    }
    ""
}

function Get-AgexDiscoveryRules {
    # Allowlist of known tools that AGEX can recognise but does not drive.
    @(
        [pscustomobject]@{ Id = "claude-code"; Name = "Claude Code CLI"; Kind = "Agent"; Provider = "Anthropic"; Candidates = @("%USERPROFILE%\.local\bin\claude.exe", "%APPDATA%\npm\claude.cmd"); Commands = @("claude") }
        [pscustomobject]@{ Id = "gemini-cli"; Name = "Gemini CLI"; Kind = "Agent"; Provider = "Google"; Candidates = @("%APPDATA%\npm\gemini.cmd"); Commands = @("gemini") }
        [pscustomobject]@{ Id = "copilot-cli"; Name = "GitHub Copilot CLI"; Kind = "Agent"; Provider = "GitHub"; Candidates = @("%APPDATA%\npm\copilot.cmd"); Commands = @("copilot") }
        [pscustomobject]@{ Id = "cursor-agent"; Name = "Cursor Agent CLI"; Kind = "Agent"; Provider = "Cursor"; Candidates = @("%LOCALAPPDATA%\cursor-agent\cursor-agent.cmd"); Commands = @("cursor-agent") }
        [pscustomobject]@{ Id = "aider"; Name = "Aider"; Kind = "Agent"; Provider = "Aider"; Candidates = @("%USERPROFILE%\.local\bin\aider.exe"); Commands = @("aider") }
        [pscustomobject]@{ Id = "opencode"; Name = "OpenCode"; Kind = "Agent"; Provider = "OpenCode"; Candidates = @("%APPDATA%\npm\opencode.cmd"); Commands = @("opencode") }
        [pscustomobject]@{ Id = "qwen-code"; Name = "Qwen Code"; Kind = "Agent"; Provider = "Alibaba"; Candidates = @("%APPDATA%\npm\qwen.cmd"); Commands = @("qwen") }
        [pscustomobject]@{ Id = "ollama"; Name = "Ollama (local models)"; Kind = "Runtime"; Provider = "Ollama"; Candidates = @("%LOCALAPPDATA%\Programs\Ollama\ollama.exe"); Commands = @("ollama") }
        [pscustomobject]@{ Id = "vscode"; Name = "Visual Studio Code"; Kind = "IDE"; Provider = "Microsoft"; Candidates = @("%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe", "%ProgramFiles%\Microsoft VS Code\Code.exe"); Commands = @() }
        [pscustomobject]@{ Id = "cursor"; Name = "Cursor"; Kind = "IDE"; Provider = "Cursor"; Candidates = @("%LOCALAPPDATA%\Programs\cursor\Cursor.exe"); Commands = @() }
        [pscustomobject]@{ Id = "windsurf"; Name = "Windsurf"; Kind = "IDE"; Provider = "Codeium"; Candidates = @("%LOCALAPPDATA%\Programs\Windsurf\Windsurf.exe"); Commands = @() }
        [pscustomobject]@{ Id = "antigravity-ide"; Name = "Antigravity IDE"; Kind = "IDE"; Provider = "Google"; Candidates = @("%LOCALAPPDATA%\Programs\Antigravity\Antigravity.exe", "%LOCALAPPDATA%\Programs\Antigravity IDE\Antigravity.exe"); Commands = @() }
        [pscustomobject]@{ Id = "visual-studio"; Name = "Visual Studio"; Kind = "IDE"; Provider = "Microsoft"; Candidates = @("%ProgramFiles%\Microsoft Visual Studio\*\*\Common7\IDE\devenv.exe"); Commands = @() }
        [pscustomobject]@{ Id = "jetbrains"; Name = "JetBrains IDE"; Kind = "IDE"; Provider = "JetBrains"; Candidates = @("%ProgramFiles%\JetBrains\*\bin\*64.exe", "%LOCALAPPDATA%\Programs\*\bin\idea64.exe", "%LOCALAPPDATA%\Programs\*\bin\pycharm64.exe", "%LOCALAPPDATA%\Programs\*\bin\rider64.exe"); Commands = @() }
        [pscustomobject]@{ Id = "zed"; Name = "Zed"; Kind = "IDE"; Provider = "Zed"; Candidates = @("%LOCALAPPDATA%\Programs\Zed\Zed.exe"); Commands = @() }
    )
}

function Get-AgexMcpSummary {
    # Reads known MCP configuration files (read-only) and returns server names only.
    $servers = [System.Collections.Generic.List[string]]::new()
    $sources = [System.Collections.Generic.List[string]]::new()
    $codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE ".codex" }
    $codexConfig = Join-Path $codexHome "config.toml"
    if (Test-Path -LiteralPath $codexConfig -PathType Leaf) {
        foreach ($line in @(Get-Content -LiteralPath $codexConfig -ErrorAction SilentlyContinue)) { if ($line -match '^\s*\[mcp_servers\.([A-Za-z0-9_.-]+)\]') { [void]$servers.Add($Matches[1]); if (-not $sources.Contains("Codex")) { [void]$sources.Add("Codex") } } }
    }
    foreach ($file in @(@{ Path = (Join-Path $env:APPDATA "Claude\claude_desktop_config.json"); Key = "mcpServers"; Source = "Claude Desktop" }, @{ Path = (Join-Path $env:APPDATA "Code\User\mcp.json"); Key = "servers"; Source = "VS Code" }, @{ Path = (Join-Path $env:USERPROFILE ".cursor\mcp.json"); Key = "mcpServers"; Source = "Cursor" })) {
        if (-not (Test-Path -LiteralPath $file.Path -PathType Leaf)) { continue }
        try {
            $json = Get-Content -LiteralPath $file.Path -Raw | ConvertFrom-Json
            $section = $json.($file.Key)
            if ($section) { foreach ($name in $section.PSObject.Properties.Name) { [void]$servers.Add([string]$name) }; [void]$sources.Add($file.Source) }
        } catch { }
    }
    [pscustomobject]@{ Servers = @($servers | Select-Object -Unique); Sources = @($sources) }
}

function Invoke-AgexDiscovery {
    # Structured scan of the machine. Supported adapters get a health check
    # (bounded "--version"); everything else is file presence only.
    param($Runtime, [string[]]$EnabledAgents = @("codex", "antigravity"), [string]$WorkingDirectory = $env:TEMP, [switch]$SkipHealth)
    $agents = [System.Collections.Generic.List[object]]::new()
    $ides = [System.Collections.Generic.List[object]]::new()
    $integrations = [System.Collections.Generic.List[object]]::new()
    foreach ($adapter in Get-AgexAdapters) {
        $path = Resolve-AgexAdapterExecutable -Adapter $adapter
        $version = if ($path) { Get-AgexFileVersion -Path $path } else { "" }
        $ready = $false; $reason = ""
        if (-not $path) { $reason = "$($adapter.Name) is not installed." }
        elseif ($SkipHealth) { $ready = $true }
        else {
            $health = Test-AgexAgentPrecheck -Runtime $Runtime -Agent $adapter.Name -ExecutablePath $path -WorkingDirectory $WorkingDirectory
            $ready = [bool]$health.Healthy; $reason = [string]$health.Reason
            if ($ready -and -not $version -and $reason -match '(\d+\.\d+[\w.\-]*)') { $version = $Matches[1] }
        }
        [void]$agents.Add([pscustomobject]@{
            Id = $adapter.Id; Name = $adapter.Name; Kind = "Agent"; Provider = $adapter.Provider; Description = $adapter.Description
            Status = if ($path) { "SUPPORTED" } else { "UNAVAILABLE" }
            Integration = "Supported"; Ready = $ready; Enabled = ($EnabledAgents -contains $adapter.Id)
            Version = $version; Location = $path; Reason = $(if ($ready) { "" } else { $reason })
            Capabilities = @($adapter.Capabilities); CloudService = $adapter.CloudService
            CanWriteNow = ($adapter.WritePolicy -eq "always" -or ($Runtime -and $Runtime.CodexSandbox -eq "workspace-write"))
        })
    }
    foreach ($rule in Get-AgexDiscoveryRules) {
        $path = Find-AgexToolPath -Candidates $rule.Candidates -CommandNames $rule.Commands
        if (-not $path) {
            if ($rule.Kind -eq "Agent") { [void]$agents.Add([pscustomobject]@{ Id = $rule.Id; Name = $rule.Name; Kind = $rule.Kind; Provider = $rule.Provider; Description = ""; Status = "UNAVAILABLE"; Integration = "Not integrated"; Ready = $false; Enabled = $false; Version = ""; Location = ""; Reason = "Not found."; Capabilities = @(); CloudService = $true }) }
            else { [void]$ides.Add([pscustomobject]@{ Id = $rule.Id; Name = $rule.Name; Kind = $rule.Kind; Status = "UNAVAILABLE"; Version = ""; Location = ""; Integration = "Not integrated" }) }
            continue
        }
        $item = [pscustomobject]@{ Id = $rule.Id; Name = $rule.Name; Kind = $rule.Kind; Provider = $rule.Provider; Description = ""; Status = "DETECTED"; Integration = "Detected - not integrated"; Ready = $false; Enabled = $false; Version = (Get-AgexFileVersion -Path $path); Location = $path; Reason = "AGEX has no adapter for this tool yet."; Capabilities = @(); CloudService = $true }
        if ($rule.Kind -in @("Agent", "Runtime")) { [void]$agents.Add($item) } else { [void]$ides.Add($item) }
    }
    $git = Find-AgexToolPath -CommandNames @("git")
    [void]$integrations.Add([pscustomobject]@{ Id = "git"; Name = "Git"; Status = $(if ($git) { "AVAILABLE" } else { "UNAVAILABLE" }); Detail = $(if ($git) { "Change tracking and diffs enabled." } else { "Install Git to see diffs of changed files." }) })
    $mcp = Get-AgexMcpSummary
    [void]$integrations.Add([pscustomobject]@{ Id = "mcp"; Name = "MCP servers"; Status = $(if ($mcp.Servers.Count) { "AVAILABLE" } else { "UNAVAILABLE" }); Detail = $(if ($mcp.Servers.Count) { "{0} configured ({1})" -f $mcp.Servers.Count, ($mcp.Sources -join ", ") } else { "None configured." }) })
    [void]$integrations.Add([pscustomobject]@{ Id = "workspace"; Name = "Local workspace access"; Status = "AVAILABLE"; Detail = "AGEX works in the project folder you choose." })
    $result = [pscustomobject]@{
        ScannedAt = (Get-Date).ToUniversalTime().ToString("o")
        Agents = @($agents); Ides = @($ides); Integrations = @($integrations)
        Summary = [pscustomobject]@{
            Supported = @($agents | Where-Object Status -eq "SUPPORTED").Count
            Ready = @($agents | Where-Object Ready).Count
            Detected = @($agents | Where-Object Status -eq "DETECTED").Count + @($ides | Where-Object Status -eq "DETECTED").Count
        }
    }
    try { [IO.File]::WriteAllText((Get-AgexPath Scan), ($result | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false)) } catch { }
    $result
}
