[CmdletBinding()]
param(
    [switch]$Interactive,
    [string[]]$Modes,
    [string]$Project,
    [string]$Leader,
    [int]$CodexShare = -1,
    [int]$AntigravityShare = -1,
    [string]$CodexModel,
    [string]$CodexEffort,
    [string]$AntigravityModel,
    [string]$AntigravityEffort,
    [switch]$NoConfirm,
    [Parameter(ValueFromRemainingArguments)][string[]]$CodexArguments
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$resolver = Join-Path $PSScriptRoot "resolve-codex.ps1"
$codexPath = try { [string](& $resolver 2>$null | Select-Object -First 1) } catch { "" }
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE ".codex" }
$env:CODEX_HOME = $codexHome
$defaultProject = (Get-Location).Path
$codexConfigPath = Join-Path $codexHome "config.toml"
$commonPath = Join-Path $PSScriptRoot "agex-common.ps1"
. $commonPath
Initialize-AgexStorage
$telemetryRoot = Get-AgexPath Telemetry
$registryPath = Get-AgexPath Projects
$settingsPath = Get-AgexPath Settings
$script:preferences = Get-AgexPreferences -Path $settingsPath
$script:sessionId = "session-" + ([guid]::NewGuid().ToString("N"))
$script:codexModels = @()
$script:antigravityModels = @()
$script:codexEfforts = @()
$script:antigravityEfforts = @()
$script:registry = $null
if ($Leader -and @("Codex", "Antigravity", "Auto") -notcontains $Leader) { throw "Leader must be Codex, Antigravity, or Auto." }

function Get-ProjectMetadata {
    param([Parameter(Mandatory)][string]$Path)
    $markers = [System.Collections.Generic.List[string]]::new()
    foreach ($marker in @("package.json", "pyproject.toml", "go.mod", "Cargo.toml")) {
        if (Test-Path -LiteralPath (Join-Path $Path $marker) -PathType Leaf) { [void]$markers.Add($marker) }
    }
    foreach ($pattern in @("*.sln", "*.csproj")) {
        foreach ($file in @(Get-ChildItem -LiteralPath $Path -File -Filter $pattern -ErrorAction SilentlyContinue)) { [void]$markers.Add($file.Name) }
    }
    $instructions = @(Get-ChildItem -LiteralPath $Path -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @("AGENTS.md", "CLAUDE.md", "README.md") } | Select-Object -ExpandProperty Name)
    [pscustomobject]@{
        git = (Test-Path -LiteralPath (Join-Path $Path ".git"))
        markers = @($markers | Sort-Object -Unique)
        instructions = @($instructions | Sort-Object -Unique)
    }
}

function Get-DefaultProjectName {
    param([Parameter(Mandatory)][string]$Path)
    $leaf = Split-Path -Leaf $Path.TrimEnd("\", "/")
    if ([string]::IsNullOrWhiteSpace($leaf)) { return $Path }
    $leaf
}

function New-ProjectEntry {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Path)
    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) { throw "Project directory not found: $resolved" }
    $metadata = Get-ProjectMetadata -Path $resolved
    [pscustomobject]@{
        name = $Name.Trim()
        path = $resolved
        git = [bool]$metadata.git
        markers = @($metadata.markers)
        instructions = @($metadata.instructions)
        added_at = (Get-Date).ToUniversalTime().ToString("o")
    }
}

function Save-Registry {
    if (-not $script:registry) { return }
    New-Item -ItemType Directory -Path $codexHome -Force | Out-Null
    $script:registry | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $registryPath -Encoding utf8
}

function Add-RegistryEntry {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Path)
    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $existing = @($script:registry.projects | Where-Object { $_.path -ieq $resolved }) | Select-Object -First 1
    if ($existing) { return $existing }
    $entry = New-ProjectEntry -Name $Name -Path $resolved
    [void]$script:registry.projects.Add($entry)
    $entry
}

function Get-TrustedProjectPaths {
    $configPath = Join-Path $codexHome "config.toml"
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) { return @() }
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(Get-Content -LiteralPath $configPath -ErrorAction SilentlyContinue)) {
        if ($line -match "^\[projects\.'(?<single>[^']+)'\]$" -or $line -match '^\[projects\.\"(?<double>[^\"]+)\"\]$') {
            $candidate = if ($Matches.single) { $Matches.single } else { $Matches.double }
            if ((Test-Path -LiteralPath $candidate -PathType Container -ErrorAction SilentlyContinue) -and -not $paths.Contains($candidate)) { [void]$paths.Add($candidate) }
        }
    }
    @($paths)
}

function Ensure-Registry {
    if ($script:registry) { return }
    $changed = $false
    if (Test-Path -LiteralPath $registryPath -PathType Leaf) {
        try { $loaded = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json } catch { throw "Workbench project registry is invalid: $registryPath" }
        $projects = [System.Collections.Generic.List[object]]::new()
        foreach ($item in @($loaded.projects)) {
            if (-not $item.path) { continue }
            $exists = Test-Path -LiteralPath $item.path -PathType Container
            $metadata = if ($exists) { Get-ProjectMetadata -Path $item.path } else { $item }
            $storedPath = if ($exists) { (Resolve-Path -LiteralPath $item.path).Path } else { [string]$item.path }
            [void]$projects.Add([pscustomobject]@{ name = [string]$item.name; path = $storedPath; git = [bool]$metadata.git; markers = @($metadata.markers); instructions = @($metadata.instructions); added_at = [string]$item.added_at })
        }
        $script:registry = [pscustomobject]@{ version = 1; projects = $projects }
    } else {
        $script:registry = [pscustomobject]@{ version = 1; projects = [System.Collections.Generic.List[object]]::new() }
        $changed = $true
    }
    foreach ($path in @(Get-TrustedProjectPaths)) {
        $resolved = (Resolve-Path -LiteralPath $path).Path
        if (-not (@($script:registry.projects | Where-Object { $_.path -ieq $resolved }).Count)) {
            [void](Add-RegistryEntry -Name (Get-DefaultProjectName -Path $path) -Path $path)
            $changed = $true
        }
    }
    $resolvedRoot = (Resolve-Path -LiteralPath $root).Path
    if (-not (@($script:registry.projects | Where-Object { $_.path -ieq $resolvedRoot }).Count)) {
        [void](Add-RegistryEntry -Name (Get-DefaultProjectName -Path $root) -Path $root)
        $changed = $true
    }
    if ($changed) { Save-Registry }
}

function Refresh-AgexModels {
    $script:codexModels = @(Get-CodexModelCatalog -CodexConfigPath $codexConfigPath)
    $script:antigravityModels = @(Get-AntigravityModelCatalog)
    $script:codexEfforts = @($script:codexModels | ForEach-Object { $_.Efforts } | Where-Object { $_ } | Sort-Object -Unique)
    $script:antigravityEfforts = @(Get-AntigravityEfforts)
}

function Get-CatalogModel {
    param([object[]]$Catalog, [string]$Requested, [string]$Fallback)
    if ($Requested -and @($Catalog | Where-Object Id -eq $Requested).Count) { return [string]$Requested }
    if ($Requested -and $Catalog.Count -gt 0) { Write-Host "Saved model '$Requested' no longer available. Selecting current available model." -ForegroundColor Yellow }
    if ($Fallback -and @($Catalog | Where-Object Id -eq $Fallback).Count) { return [string]$Fallback }
    $default = @($Catalog | Where-Object IsDefault | Select-Object -First 1)
    if ($default) { return [string]$default[0].Id }
    if ($Catalog.Count -gt 0) { return [string]$Catalog[0].Id }
    if ($Requested) { return [string]$Requested }
    $Fallback
}

function Get-ModelEffort {
    param([object[]]$Catalog, [string]$Model, [string]$Requested, [string]$Fallback)
    $entry = @($Catalog | Where-Object Id -eq $Model | Select-Object -First 1)
    $available = if ($entry) { @($entry[0].Efforts) } else { @() }
    if ($Requested -and ($available.Count -eq 0 -or $available -contains $Requested)) { return [string]$Requested }
    if ($Requested -and $available.Count -gt 0) { Write-Host "Saved effort '$Requested' not supported by '$Model'. Selecting supported effort." -ForegroundColor Yellow }
    if ($Fallback -and ($available.Count -eq 0 -or $available -contains $Fallback)) { return [string]$Fallback }
    if ($entry -and $entry[0].DefaultEffort) { return [string]$entry[0].DefaultEffort }
    if ($available.Count -gt 0) { return [string]$available[0] }
    ""
}

function Apply-AgexOverrides {
    if ($Leader) { $script:preferences.leader = $Leader }
    if ($CodexShare -ge 0) { $script:preferences.codex_share = $CodexShare }
    if ($AntigravityShare -ge 0) { $script:preferences.antigravity_share = $AntigravityShare }
    if ($CodexModel) { $script:preferences.codex_model = $CodexModel }
    if ($CodexEffort) { $script:preferences.codex_effort = $CodexEffort }
    if ($AntigravityModel) { $script:preferences.antigravity_model = $AntigravityModel }
    if ($AntigravityEffort) { $script:preferences.antigravity_effort = $AntigravityEffort }
    if ($script:preferences.codex_share -lt 0 -or $script:preferences.codex_share -gt 100) { $script:preferences.codex_share = 20 }
    if ($script:preferences.antigravity_share -lt 0 -or $script:preferences.antigravity_share -gt 100) { $script:preferences.antigravity_share = 80 }
    if ($script:preferences.codex_share + $script:preferences.antigravity_share -ne 100) { throw "Codex and Antigravity workload percentages must total 100." }
    $codexFallback = Get-ConfigModel -ConfigPath $codexConfigPath
    $script:preferences.codex_model = Get-CatalogModel -Catalog $script:codexModels -Requested ([string]$script:preferences.codex_model) -Fallback $codexFallback
    $script:preferences.codex_effort = Get-ModelEffort -Catalog $script:codexModels -Model ([string]$script:preferences.codex_model) -Requested ([string]$script:preferences.codex_effort) -Fallback ""
    $script:preferences.antigravity_model = Get-CatalogModel -Catalog $script:antigravityModels -Requested ([string]$script:preferences.antigravity_model) -Fallback ""
    $script:preferences.antigravity_effort = Get-ModelEffort -Catalog $script:antigravityModels -Model ([string]$script:preferences.antigravity_model) -Requested ([string]$script:preferences.antigravity_effort) -Fallback ""
}

function Save-AgexSession {
    param([string[]]$Selected, [Parameter(Mandatory)]$ProjectEntry)
    $script:preferences.last_modes = @($Selected)
    Add-AgexRecentProject -Preferences $script:preferences -ProjectPath ([string]$ProjectEntry.path)
    try {
        Save-AgexPreferences -Preferences $script:preferences -Path $settingsPath
    } catch {
        Write-Host "AGEX settings could not be saved: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "Launch continues; preferences will remain unchanged until the settings path is writable." -ForegroundColor Yellow
    }
}

function Get-FolderFromPicker {
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $dialog = [System.Windows.Forms.FolderBrowserDialog]::new()
        $dialog.Description = "Select project folder for AGEX"
        $dialog.UseDescriptionForTitle = $true
        $result = $dialog.ShowDialog()
        if ($result -eq [System.Windows.Forms.DialogResult]::OK) { return $dialog.SelectedPath }
    } catch {
        Write-Host "Native folder picker unavailable. Use manual path input." -ForegroundColor Yellow
    }
    ""
}

function Read-ProjectPathInput {
    $choice = Invoke-AgexListMenu -Title "PROJECT PATH" -Items @("Browse for folder", "Paste/type path") -Shortcuts @{ B = "Back" }
    if ($choice.Action -eq "Back") { return "" }
    if ($choice.Index -eq 0) { return (Get-FolderFromPicker) }
    if (-not [Console]::IsInputRedirected) { return (Read-Host "Project folder/path").Trim('"') }
    ""
}

function Parse-Modes {
    param([string[]]$Value)
    if (-not $Value -or [string]::IsNullOrWhiteSpace(($Value -join ""))) { return }
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($token in (($Value | ForEach-Object { $_ -split "[,;+\s]+" }) | Where-Object { $_ })) {
        $mode = switch ($token.ToLowerInvariant()) {
            "plain" { $null }
            "caveman" { "Caveman" }
            "coworker" { "Coworker" }
            "orchestrator" { "Orchestrator" }
            default { throw "Unknown mode '$token'. Use Plain, Caveman, Coworker, or Orchestrator." }
        }
        if ($mode -and -not $result.Contains($mode)) { [void]$result.Add($mode) }
    }
    return $result.ToArray()
}

function Get-ModeLabel {
    param([string[]]$Selected)
    if (-not $Selected -or $Selected.Count -eq 0) { return "Plain Codex" }
    ($Selected -join " + ")
}

function Clear-AgexScreen {
    # Menus redraw in place: the screen is cleared once, later frames overwrite
    # the same rows. Clearing on every key press made the terminal blink.
    if ([Console]::IsOutputRedirected) { return }
    try {
        if ($script:menuFrameOpen -and [Console]::CursorTop -lt [Console]::WindowHeight) {
            $script:menuBottom = [math]::Max([int]$script:menuBottom, [Console]::CursorTop + 1)
            [Console]::SetCursorPosition(0, 0)
            return
        }
        [Console]::Clear()
    } catch { Clear-Host }
    $script:menuFrameOpen = $true
    $script:menuBottom = 0
}

function Complete-AgexMenuFrame {
    # Blank rows left over from a taller previous frame, then wait for input.
    if ([Console]::IsOutputRedirected) { return }
    try {
        $top = [Console]::CursorTop
        $width = [math]::Max(1, [Console]::WindowWidth - 1)
        for ($row = $top; $row -lt [int]$script:menuBottom -and $row -lt [Console]::BufferHeight; $row++) { [Console]::SetCursorPosition(0, $row); [Console]::Write(" " * $width) }
        $script:menuBottom = $top
        [Console]::SetCursorPosition(0, $top)
    } catch { }
}

function Write-Host {
    # Pads each menu line to the window width so in-place redraws never leave
    # stale characters from a longer previous line.
    param([Parameter(Position = 0, ValueFromPipeline)][object]$Object, [ConsoleColor]$ForegroundColor, [ConsoleColor]$BackgroundColor, [switch]$NoNewline)
    $text = [string]$Object
    if (-not $NoNewline -and -not [Console]::IsOutputRedirected) {
        try { $width = [Console]::WindowWidth - 1; if ($text.Length -lt $width -and $text -notmatch "`n") { $text = $text.PadRight($width) } } catch { }
    }
    $forward = @{ Object = $text; NoNewline = $NoNewline }
    if ($PSBoundParameters.ContainsKey("ForegroundColor")) { $forward.ForegroundColor = $ForegroundColor }
    if ($PSBoundParameters.ContainsKey("BackgroundColor")) { $forward.BackgroundColor = $BackgroundColor }
    Microsoft.PowerShell.Utility\Write-Host @forward
}

function Get-AgexNumberFromKey {
    param([Parameter(Mandatory)][ConsoleKeyInfo]$Key)
    switch ($Key.Key) {
        ([ConsoleKey]::D0) { return 0 }
        ([ConsoleKey]::D1) { return 1 }
        ([ConsoleKey]::D2) { return 2 }
        ([ConsoleKey]::D3) { return 3 }
        ([ConsoleKey]::D4) { return 4 }
        ([ConsoleKey]::D5) { return 5 }
        ([ConsoleKey]::D6) { return 6 }
        ([ConsoleKey]::D7) { return 7 }
        ([ConsoleKey]::D8) { return 8 }
        ([ConsoleKey]::D9) { return 9 }
        ([ConsoleKey]::NumPad0) { return 0 }
        ([ConsoleKey]::NumPad1) { return 1 }
        ([ConsoleKey]::NumPad2) { return 2 }
        ([ConsoleKey]::NumPad3) { return 3 }
        ([ConsoleKey]::NumPad4) { return 4 }
        ([ConsoleKey]::NumPad5) { return 5 }
        ([ConsoleKey]::NumPad6) { return 6 }
        ([ConsoleKey]::NumPad7) { return 7 }
        ([ConsoleKey]::NumPad8) { return 8 }
        ([ConsoleKey]::NumPad9) { return 9 }
        default { return -1 }
    }
}

function Invoke-AgexListMenu {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string[]]$Items,
        [int]$StartIndex = 0,
        [scriptblock]$Preview,
        [hashtable]$Shortcuts = @{}
    )
    if (-not $Items.Count) { return [pscustomobject]@{ Action = "Back"; Index = -1; Key = "" } }
    $index = [math]::Max(0, [math]::Min($StartIndex, $Items.Count - 1))
    $oldCursor = [Console]::CursorVisible
    try {
        [Console]::CursorVisible = $false
        while ($true) {
            Clear-AgexScreen
            Write-Host ""
            Write-Host $Title -ForegroundColor Cyan
            Write-Host ""
            for ($i = 0; $i -lt $Items.Count; $i++) {
                $prefix = if ($i -eq $index) { ">" } else { " " }
                if ($i -eq $index) { Write-Host "$prefix $($Items[$i])" -ForegroundColor Cyan } else { Write-Host "$prefix $($Items[$i])" }
            }
            if ($Preview) {
                Write-Host ""
                foreach ($line in @(& $Preview $index)) { if ($null -ne $line) { Write-Host ([string]$line) -ForegroundColor DarkGray } }
            }
            Write-Host ""
            Write-Host "Up/Down Navigate   Enter Select   Esc Back" -ForegroundColor DarkGray
            Write-Host "Number shortcuts remain available." -ForegroundColor DarkGray
            Complete-AgexMenuFrame
            $key = [Console]::ReadKey($true)
            switch ($key.Key) {
                ([ConsoleKey]::UpArrow) { $index = ($index - 1 + $Items.Count) % $Items.Count; continue }
                ([ConsoleKey]::DownArrow) { $index = ($index + 1) % $Items.Count; continue }
                ([ConsoleKey]::Enter) { return [pscustomobject]@{ Action = "Select"; Index = $index; Key = "Enter" } }
                ([ConsoleKey]::Escape) { return [pscustomobject]@{ Action = "Back"; Index = $index; Key = "Escape" } }
                default { }
            }
            $number = Get-AgexNumberFromKey -Key $key
            if ($number -gt 0) {
                $shortcutIndex = $number - 1
                if ($shortcutIndex -lt $Items.Count) { return [pscustomobject]@{ Action = "Select"; Index = $shortcutIndex; Key = "Number" } }
            } elseif ($number -eq 0 -and $Items.Count -ge 10) {
                return [pscustomobject]@{ Action = "Select"; Index = 9; Key = "Number" }
            }
            $keyName = $key.Key.ToString().ToUpperInvariant()
            if ($Shortcuts.ContainsKey($keyName)) { return [pscustomobject]@{ Action = [string]$Shortcuts[$keyName]; Index = $index; Key = $keyName } }
        }
    } finally {
        [Console]::CursorVisible = $oldCursor
    }
}

function Read-AgexAnyKey {
    Write-Host ""
    Write-Host "Press any key to return." -ForegroundColor DarkGray
    $oldCursor = [Console]::CursorVisible
    try { [Console]::CursorVisible = $false; [void][Console]::ReadKey($true) } finally { [Console]::CursorVisible = $oldCursor }
}

function Get-AgexBar {
    param([Parameter(Mandatory)][int]$Percent)
    $width = 20
    $filled = [int][math]::Round(($Percent / 100) * $width)
    if ($filled -lt 0) { $filled = 0 }
    if ($filled -gt $width) { $filled = $width }
    ([string][char]0x2588 * $filled) + ([string][char]0x2591 * ($width - $filled))
}

function Move-AgexValue {
    param([object[]]$Values, [string]$Current, [int]$Direction)
    if (-not $Values.Count) { return $Current }
    $index = -1
    for ($i = 0; $i -lt $Values.Count; $i++) { if ([string]$Values[$i] -eq $Current) { $index = $i; break } }
    if ($index -lt 0) { $index = if ($Direction -ge 0) { 0 } else { $Values.Count - 1 } } else { $index = ($index + $Direction) % $Values.Count; if ($index -lt 0) { $index += $Values.Count } }
    [string]$Values[$index]
}

function Read-ModeMenu {
    param([string[]]$Initial = @())
    $modes = @("Caveman", "Coworker", "Orchestrator")
    $selected = [System.Collections.Generic.List[string]]::new()
    foreach ($mode in @($Initial)) { if ($modes -contains $mode) { [void]$selected.Add($mode) } }
    $index = 0
    $oldCursor = [Console]::CursorVisible
    try {
        [Console]::CursorVisible = $false
        while ($true) {
            Clear-AgexScreen
            Write-Host "AGEX / MODES" -ForegroundColor Cyan
            Write-Host ""
            for ($i = 0; $i -lt $modes.Count; $i++) {
                $mark = if ($selected.Contains($modes[$i])) { "ON " } else { "OFF" }
                $prefix = if ($i -eq $index) { ">" } else { " " }
                $line = "$prefix $($modes[$i].PadRight(14)) [$mark]"
                if ($i -eq $index) { Write-Host $line -ForegroundColor Cyan } else { Write-Host $line }
            }
            Write-Host ""
            Write-Host "Up/Down Select   Space Toggle   Enter Save   Esc Cancel" -ForegroundColor DarkGray
            Complete-AgexMenuFrame
            $key = [Console]::ReadKey($true)
            switch ($key.Key) {
                ([ConsoleKey]::UpArrow) { $index = ($index - 1 + $modes.Count) % $modes.Count; continue }
                ([ConsoleKey]::DownArrow) { $index = ($index + 1) % $modes.Count; continue }
                ([ConsoleKey]::Spacebar) { if ($selected.Contains($modes[$index])) { [void]$selected.Remove($modes[$index]) } else { [void]$selected.Add($modes[$index]) }; continue }
                ([ConsoleKey]::Enter) { return ,([string[]]$selected.ToArray()) }
                ([ConsoleKey]::Escape) { return $null }
                default { }
            }
            $number = Get-AgexNumberFromKey -Key $key
            if ($number -ge 1 -and $number -le $modes.Count) {
                $index = $number - 1
                if ($selected.Contains($modes[$index])) { [void]$selected.Remove($modes[$index]) } else { [void]$selected.Add($modes[$index]) }
            }
        }
    } finally { [Console]::CursorVisible = $oldCursor }
}

function Read-WorkloadMenu {
    $items = @("Save Codex usage       AGY 90 / Codex 10", "AGY Heavy              AGY 80 / Codex 20", "Balanced               AGY 50 / Codex 50", "Codex Heavy            AGY 20 / Codex 80", "Custom")
    $choice = Invoke-AgexListMenu -Title "WORKLOAD TARGET" -Items $items -Shortcuts @{ B = "Back" }
    if ($choice.Action -eq "Back") { return }
    switch ($choice.Index) {
        0 { $script:preferences.codex_share = 10; $script:preferences.antigravity_share = 90 }
        1 { $script:preferences.codex_share = 20; $script:preferences.antigravity_share = 80 }
        2 { $script:preferences.codex_share = 50; $script:preferences.antigravity_share = 50 }
        3 { $script:preferences.codex_share = 80; $script:preferences.antigravity_share = 20 }
        4 {
            if ([Console]::IsInputRedirected) { return }
            $raw = Read-Host "Codex target percentage (0-100)"
            $codex = 0
            if ([int]::TryParse($raw, [ref]$codex) -and $codex -ge 0 -and $codex -le 100) { $script:preferences.codex_share = $codex; $script:preferences.antigravity_share = 100 - $codex } else { Write-Host "Enter whole number 0-100." -ForegroundColor Yellow; Read-AgexAnyKey }
        }
    }
}

function Read-AgentConfiguration {
    $leaders = @("Codex", "Antigravity", "Auto")
    $leaderIndex = [array]::IndexOf($leaders, [string]$script:preferences.leader); if ($leaderIndex -lt 0) { $leaderIndex = 0 }
    $codexShare = [int]$script:preferences.codex_share
    $antigravityShare = [int]$script:preferences.antigravity_share
    $field = 0
    $oldCursor = [Console]::CursorVisible
    try {
        [Console]::CursorVisible = $false
        while ($true) {
            Clear-AgexScreen
            Write-Host "AGENT CONFIGURATION" -ForegroundColor Cyan
            Write-Host ""
            $leaderPrefix = if ($field -eq 0) { ">" } else { " " }
            $leaderLine = "$leaderPrefix Leader: < $($leaders[$leaderIndex]) >"
            $codexLine = "  Codex:       $($codexShare.ToString().PadLeft(3))%  $(Get-AgexBar -Percent $codexShare)"
            $agyLine = "  Antigravity: $($antigravityShare.ToString().PadLeft(3))%  $(Get-AgexBar -Percent $antigravityShare)"
            if ($field -eq 0) { Write-Host $leaderLine -ForegroundColor Cyan } else { Write-Host $leaderLine }
            Write-Host ""
            Write-Host "Workload"
            if ($field -eq 1) { Write-Host "> $($codexLine.Substring(2))" -ForegroundColor Cyan } else { Write-Host $codexLine }
            if ($field -eq 2) { Write-Host "> $($agyLine.Substring(2))" -ForegroundColor Cyan } else { Write-Host $agyLine }
            Write-Host ""
            Write-Host "Target: Codex $codexShare% | Antigravity $antigravityShare%"
            Write-Host ""
            Write-Host "Up/Down Select   Left/Right Change   Shift+Left/Right Step 10%" -ForegroundColor DarkGray
            Write-Host "Enter Save   Esc Cancel   Number shortcuts: 1 Codex, 2 Antigravity, 3 Auto" -ForegroundColor DarkGray
            Complete-AgexMenuFrame
            $key = [Console]::ReadKey($true)
            switch ($key.Key) {
                ([ConsoleKey]::UpArrow) { $field = ($field - 1 + 3) % 3; continue }
                ([ConsoleKey]::DownArrow) { $field = ($field + 1) % 3; continue }
                ([ConsoleKey]::Enter) { $script:preferences.leader = $leaders[$leaderIndex]; $script:preferences.codex_share = $codexShare; $script:preferences.antigravity_share = $antigravityShare; return }
                ([ConsoleKey]::Escape) { return }
                default { }
            }
            $number = Get-AgexNumberFromKey -Key $key
            if ($number -ge 1 -and $number -le 3) { $leaderIndex = $number - 1; $field = 0; continue }
            $direction = if ($key.Key -eq [ConsoleKey]::LeftArrow) { -1 } elseif ($key.Key -eq [ConsoleKey]::RightArrow) { 1 } else { 0 }
            if ($direction -eq 0) { continue }
            if ($field -eq 0) { $leaderIndex = ($leaderIndex + $direction + $leaders.Count) % $leaders.Count; continue }
            $step = if (($key.Modifiers -band [ConsoleModifiers]::Shift) -ne 0) { 10 } else { 5 }
            $delta = $direction * $step
            if ($field -eq 1) { $codexShare = [math]::Max(0, [math]::Min(100, $codexShare + $delta)); $antigravityShare = 100 - $codexShare }
            if ($field -eq 2) { $antigravityShare = [math]::Max(0, [math]::Min(100, $antigravityShare + $delta)); $codexShare = 100 - $antigravityShare }
        }
    } finally { [Console]::CursorVisible = $oldCursor }
}

function Select-ModelEntry {
    param([object[]]$Catalog, [string]$Current, [string]$AgentName)
    if (-not $Catalog.Count) {
        Write-Host "$AgentName model catalog unavailable. Keep authentication unchanged; use CLI defaults." -ForegroundColor Yellow
        [void](Read-Host "Press Enter")
        return $Current
    }
    for ($i = 0; $i -lt $Catalog.Count; $i++) { Write-Host "[$($i + 1)] $($Catalog[$i].Name) [$($Catalog[$i].Id)]" }
    Write-Host "[B] Back"
    $choice = (Read-Host "Select $AgentName model").Trim().ToUpperInvariant()
    if ($choice -eq "B") { return $Current }
    if ($choice -match '^\d+$' -and [int]$choice -ge 1 -and [int]$choice -le $Catalog.Count) { return [string]$Catalog[[int]$choice - 1].Id }
    Write-Host "Invalid selection." -ForegroundColor Yellow
    $Current
}

function Select-EffortEntry {
    param([string[]]$Efforts, [string]$Current, [string]$AgentName)
    if (-not $Efforts.Count) { Write-Host "$AgentName effort catalog unavailable." -ForegroundColor Yellow; [void](Read-Host "Press Enter"); return $Current }
    for ($i = 0; $i -lt $Efforts.Count; $i++) { Write-Host "[$($i + 1)] $($Efforts[$i])" }
    Write-Host "[B] Back"
    $choice = (Read-Host "Select $AgentName effort").Trim().ToUpperInvariant()
    if ($choice -eq "B") { return $Current }
    if ($choice -match '^\d+$' -and [int]$choice -ge 1 -and [int]$choice -le $Efforts.Count) { return [string]$Efforts[[int]$choice - 1] }
    Write-Host "Invalid selection." -ForegroundColor Yellow
    $Current
}

function Read-ModelsMenu {
    $fields = @("Codex Model", "Codex Effort", "Antigravity Model", "Antigravity Effort")
    $codexModel = [string]$script:preferences.codex_model
    $codexEffort = [string]$script:preferences.codex_effort
    $antigravityModel = [string]$script:preferences.antigravity_model
    $antigravityEffort = [string]$script:preferences.antigravity_effort
    $field = 0
    $oldCursor = [Console]::CursorVisible
    try {
        [Console]::CursorVisible = $false
        while ($true) {
            Clear-AgexScreen
            Write-Host "MODELS" -ForegroundColor Cyan
            Write-Host ""
            $values = @($codexModel, $(if ($codexEffort) { $codexEffort } else { "CLI default" }), $(if ($antigravityModel) { $antigravityModel } else { "CLI default/unavailable" }), $(if ($antigravityEffort) { $antigravityEffort } else { "CLI default" }))
            for ($i = 0; $i -lt $fields.Count; $i++) {
                $prefix = if ($i -eq $field) { ">" } else { " " }
                $line = "$prefix $($fields[$i].PadRight(22)) $($values[$i])"
                if ($i -eq $field) { Write-Host $line -ForegroundColor Cyan } else { Write-Host $line }
            }
            Write-Host ""
            Write-Host "Up/Down Select   Left/Right Change   Enter Save   Esc Cancel" -ForegroundColor DarkGray
            Write-Host "R Refresh catalogs   Number shortcuts select field" -ForegroundColor DarkGray
            Complete-AgexMenuFrame
            $key = [Console]::ReadKey($true)
            switch ($key.Key) {
                ([ConsoleKey]::UpArrow) { $field = ($field - 1 + $fields.Count) % $fields.Count; continue }
                ([ConsoleKey]::DownArrow) { $field = ($field + 1) % $fields.Count; continue }
                ([ConsoleKey]::Enter) { $script:preferences.codex_model = $codexModel; $script:preferences.codex_effort = $codexEffort; $script:preferences.antigravity_model = $antigravityModel; $script:preferences.antigravity_effort = $antigravityEffort; return }
                ([ConsoleKey]::Escape) { return }
                ([ConsoleKey]::R) { Refresh-AgexModels; $script:codexEfforts = @($script:codexModels | ForEach-Object { $_.Efforts } | Where-Object { $_ } | Sort-Object -Unique); $script:antigravityEfforts = @(Get-AntigravityEfforts); continue }
                default { }
            }
            $number = Get-AgexNumberFromKey -Key $key
            if ($number -ge 1 -and $number -le $fields.Count) { $field = $number - 1; continue }
            $direction = if ($key.Key -eq [ConsoleKey]::LeftArrow) { -1 } elseif ($key.Key -eq [ConsoleKey]::RightArrow) { 1 } else { 0 }
            if ($direction -eq 0) { continue }
            switch ($field) {
                0 { $codexModel = Move-AgexValue -Values @($script:codexModels | ForEach-Object { $_.Id }) -Current $codexModel -Direction $direction }
                1 { $codexEffort = Move-AgexValue -Values $script:codexEfforts -Current $codexEffort -Direction $direction }
                2 { $antigravityModel = Move-AgexValue -Values @($script:antigravityModels | ForEach-Object { $_.Id }) -Current $antigravityModel -Direction $direction }
                3 { $antigravityEffort = Move-AgexValue -Values $script:antigravityEfforts -Current $antigravityEffort -Direction $direction }
            }
        }
    } finally { [Console]::CursorVisible = $oldCursor }
}

function Read-QuickPreset {
    $items = @("Normal Codex", "Caveman", "Coworker", "Codex-led Team", "Antigravity-led Team", "Balanced Team", "Custom")
    $choice = Invoke-AgexListMenu -Title "QUICK PRESETS" -Items $items -Preview {
        param($index)
        switch ($index) {
            0 { @("Normal Codex", "Leader: Codex", "Codex: 100%", "AGY: 0%", "Caveman: OFF", "Orchestrator: OFF") }
            1 { @("Caveman", "Leader: Codex", "Codex: 100%", "AGY: 0%", "Caveman: ON", "Orchestrator: OFF") }
            2 { @("Coworker", "Leader: Codex", "Codex: 100%", "AGY: 0%", "Caveman: OFF", "Orchestrator: OFF") }
            3 { @("Codex-led Team", "Leader: Codex", "Codex: 40%", "AGY: 60%", "Caveman: OFF", "Orchestrator: ON") }
            4 { @("Antigravity-led Team", "Leader: Antigravity", "Codex: 20%", "AGY: 80%", "Caveman: OFF", "Orchestrator: ON") }
            5 { @("Balanced Team", "Leader: Auto", "Codex: 50%", "AGY: 50%", "Caveman: OFF", "Orchestrator: ON") }
            6 { @("Custom", "Keep or change modes, leader, workload") }
        }
    } -Shortcuts @{ B = "Back" }
    if ($choice.Action -eq "Back") { return }
    switch ($choice.Index) {
        0 { $script:sessionModes = @(); $script:preferences.leader = "Codex"; $script:preferences.codex_share = 100; $script:preferences.antigravity_share = 0 }
        1 { $script:sessionModes = @("Caveman"); $script:preferences.leader = "Codex"; $script:preferences.codex_share = 100; $script:preferences.antigravity_share = 0 }
        2 { $script:sessionModes = @("Coworker"); $script:preferences.leader = "Codex"; $script:preferences.codex_share = 100; $script:preferences.antigravity_share = 0 }
        3 { $script:sessionModes = @("Orchestrator"); $script:preferences.leader = "Codex"; $script:preferences.codex_share = 40; $script:preferences.antigravity_share = 60 }
        4 { $script:sessionModes = @("Orchestrator"); $script:preferences.leader = "Antigravity"; $script:preferences.codex_share = 20; $script:preferences.antigravity_share = 80 }
        5 { $script:sessionModes = @("Orchestrator"); $script:preferences.leader = "Auto"; $script:preferences.codex_share = 50; $script:preferences.antigravity_share = 50 }
        6 {
            $newModes = Read-ModeMenu -Initial $script:sessionModes
            if ($null -eq $newModes) { return }
            $script:sessionModes = @($newModes)
            if ($script:sessionModes -contains "Orchestrator") { Read-AgentConfiguration }
        }
    }
}

function Show-ProjectMetadata {
    param([Parameter(Mandatory)]$Entry)
    $git = if ($Entry.git) { "Git repository" } else { "Not detected as Git repository" }
    $markers = if (@($Entry.markers).Count) { @($Entry.markers) -join ", " } else { "none" }
    $instructions = if (@($Entry.instructions).Count) { @($Entry.instructions) -join ", " } else { "none" }
    Write-Host "  Name: $($Entry.name)"
    Write-Host "  Path: $($Entry.path)"
    Write-Host "  Info: $git; markers: $markers; instructions: $instructions"
}

function Add-ProjectInteractive {
    Ensure-Registry
    Write-Host "ADD PROJECT"
    $rawPath = Read-ProjectPathInput
    $rawPath = ([string]$rawPath).Trim('"')
    if ([string]::IsNullOrWhiteSpace($rawPath)) { return }
    try { $resolved = (Resolve-Path -LiteralPath ([Environment]::ExpandEnvironmentVariables($rawPath)) -ErrorAction Stop).Path } catch { Write-Host "Directory not found." -ForegroundColor Yellow; return }
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) { Write-Host "Directory not found." -ForegroundColor Yellow; return }
    $existing = @($script:registry.projects | Where-Object { $_.path -ieq $resolved }) | Select-Object -First 1
    if ($existing) { Write-Host "Project already saved as '$($existing.name)'." -ForegroundColor Yellow; return }
    $defaultName = Get-DefaultProjectName -Path $resolved
    $name = (Read-Host "Friendly name [$defaultName]").Trim()
    if ([string]::IsNullOrWhiteSpace($name)) { $name = $defaultName }
    $entry = New-ProjectEntry -Name $name -Path $resolved
    Write-Host "Detected project:" -ForegroundColor Cyan
    Show-ProjectMetadata -Entry $entry
    if ((Read-Host "Save project? [Y/N]").Trim().ToUpperInvariant() -eq "Y") {
        [void]$script:registry.projects.Add($entry)
        Save-Registry
        Write-Host "Saved."
        return $entry
    }
}

function Remove-ProjectInteractive {
    Ensure-Registry
    if ($script:registry.projects.Count -eq 0) { Write-Host "No saved projects."; return }
    for ($i = 0; $i -lt $script:registry.projects.Count; $i++) { Write-Host "[$($i + 1)] $($script:registry.projects[$i].name) - $($script:registry.projects[$i].path)" }
    $choice = Read-Host "Remove number, or B"
    if ($choice -notmatch "^\d+$") { return }
    $index = [int]$choice - 1
    if ($index -lt 0 -or $index -ge $script:registry.projects.Count) { return }
    $entry = $script:registry.projects[$index]
    if ((Read-Host "Remove '$($entry.name)' from Workbench list? [Y/N]").Trim().ToUpperInvariant() -eq "Y") {
        $script:registry.projects.RemoveAt($index)
        Save-Registry
        Write-Host "Removed registry entry. Directory unchanged."
    }
}

function Edit-ProjectInteractive {
    Ensure-Registry
    if ($script:registry.projects.Count -eq 0) { Write-Host "No saved projects."; return }
    for ($i = 0; $i -lt $script:registry.projects.Count; $i++) { Write-Host "[$($i + 1)] $($script:registry.projects[$i].name)" }
    $choice = Read-Host "Edit number, or B"
    if ($choice -notmatch "^\d+$") { return }
    $index = [int]$choice - 1
    if ($index -lt 0 -or $index -ge $script:registry.projects.Count) { return }
    $entry = $script:registry.projects[$index]
    $name = (Read-Host "New friendly name [$($entry.name)]").Trim()
    if (-not [string]::IsNullOrWhiteSpace($name)) { $entry.name = $name; Save-Registry; Write-Host "Renamed." }
}

function Refresh-ProjectInteractive {
    Ensure-Registry
    if ($script:registry.projects.Count -eq 0) { Write-Host "No saved projects."; return }
    for ($i = 0; $i -lt $script:registry.projects.Count; $i++) { Write-Host "[$($i + 1)] $($script:registry.projects[$i].name)" }
    $choice = Read-Host "Refresh number, or B"
    if ($choice -notmatch '^\d+$') { return }
    $index = [int]$choice - 1
    if ($index -lt 0 -or $index -ge $script:registry.projects.Count) { return }
    $old = $script:registry.projects[$index]
    if (Test-Path -LiteralPath $old.path -PathType Container) {
        $metadata = Get-ProjectMetadata -Path $old.path
        $old.git = [bool]$metadata.git
        $old.markers = @($metadata.markers)
        $old.instructions = @($metadata.instructions)
        Save-Registry
        Write-Host "Metadata refreshed."
    } else { Write-Host "MISSING: $($old.path)" -ForegroundColor Yellow }
}

function Open-ProjectByPath {
    Write-Host "OPEN PROJECT BY PATH"
    $rawPath = ([string](Read-ProjectPathInput)).Trim('"')
    try {
        $resolved = (Resolve-Path -LiteralPath ([Environment]::ExpandEnvironmentVariables($rawPath)) -ErrorAction Stop).Path
        if (-not (Test-Path -LiteralPath $resolved -PathType Container)) { throw "not directory" }
        $metadata = Get-ProjectMetadata -Path $resolved
        return [pscustomobject]@{ name = Get-DefaultProjectName -Path $resolved; path = $resolved; git = $metadata.git; markers = $metadata.markers; instructions = $metadata.instructions; saved = $false }
    } catch { Write-Host "Directory not found." -ForegroundColor Yellow; return $null }
}

function Read-ProjectMenu {
    Ensure-Registry
    while ($true) {
        $items = [System.Collections.Generic.List[string]]::new()
        [void]$items.Add("Current Directory - $defaultProject")
        foreach ($entry in @($script:registry.projects)) {
            $state = if (Test-Path -LiteralPath $entry.path -PathType Container) { "" } else { " [MISSING]" }
            [void]$items.Add("$($entry.name)$state")
        }
        $choice = Invoke-AgexListMenu -Title "PROJECTS" -Items $items.ToArray() -Shortcuts @{ A = "Add"; R = "Remove"; E = "Edit"; F = "Refresh"; O = "Open"; B = "Back" }
        switch ($choice.Action) {
            "Back" { return $null }
            "Add" { [void](Add-ProjectInteractive); continue }
            "Remove" { Remove-ProjectInteractive; continue }
            "Edit" { Edit-ProjectInteractive; continue }
            "Refresh" { Refresh-ProjectInteractive; continue }
            "Open" { $opened = Open-ProjectByPath; if ($opened) { return $opened }; continue }
            "Select" {
                if ($choice.Index -eq 0) { return [pscustomobject]@{ name = "Current Directory"; path = $defaultProject; saved = $false } }
                $entry = $script:registry.projects[$choice.Index - 1]
                if (Test-Path -LiteralPath $entry.path -PathType Container) { return $entry }
                Write-Host "MISSING: $($entry.path). Remove or edit entry." -ForegroundColor Yellow
                Read-AgexAnyKey
            }
        }
    }
}

function Resolve-ProjectPath {
    param([string]$Requested)
    if ([string]::IsNullOrWhiteSpace($Requested)) { return [pscustomobject]@{ name = "Current Directory"; path = $defaultProject; saved = $false } }
    $expanded = [Environment]::ExpandEnvironmentVariables($Requested)
    if (Test-Path -LiteralPath $expanded -PathType Container) { return [pscustomobject]@{ name = Get-DefaultProjectName -Path $expanded; path = (Resolve-Path -LiteralPath $expanded).Path; saved = $false } }
    Ensure-Registry
    $entry = @($script:registry.projects | Where-Object { $_.name -ieq $Requested }) | Select-Object -First 1
    if ($entry -and (Test-Path -LiteralPath $entry.path -PathType Container)) { return $entry }
    throw "Project path or saved project not found: $Requested"
}

function Get-InitialProject {
    Ensure-Registry
    if ($script:preferences.last_project -and (Test-Path -LiteralPath $script:preferences.last_project -PathType Container)) {
        $saved = @($script:registry.projects | Where-Object { $_.path -ieq $script:preferences.last_project } | Select-Object -First 1)
        if ($saved) { return $saved }
        return [pscustomobject]@{ name = Get-DefaultProjectName -Path $script:preferences.last_project; path = (Resolve-Path -LiteralPath $script:preferences.last_project).Path; saved = $false }
    }
    [pscustomobject]@{ name = "Current Directory"; path = $defaultProject; saved = $false }
}

function Show-AgexHome {
    param([Parameter(Mandatory)]$ProjectEntry, [string[]]$Selected, [int]$MenuIndex = 0)
    $agyState = if (Resolve-AgyExecutable) { "READY" } else { "NOT FOUND" }
    Clear-AgexScreen
    Write-Host "====================================================" -ForegroundColor DarkCyan
    Write-Host "                    AGEX" -ForegroundColor Cyan
    Write-Host "             AI CONTROL CENTER" -ForegroundColor Cyan
    Write-Host "====================================================" -ForegroundColor DarkCyan
    Write-Host "PROJECT"
    Write-Host "  Current: $($ProjectEntry.name)"
    Write-Host "  Path:    $($ProjectEntry.path)"
    Write-Host "AGENTS"
    Write-Host "  Leader: $($script:preferences.leader)"
    Write-Host "  Workload: Codex $($script:preferences.codex_share)% | Antigravity $($script:preferences.antigravity_share)%"
    Write-Host "  Codex: $($script:preferences.codex_model) / $(if($script:preferences.codex_effort){$script:preferences.codex_effort}else{'CLI default'})"
    Write-Host "  Antigravity: $(if($script:preferences.antigravity_model){$script:preferences.antigravity_model}else{'unavailable/default'}) / $(if($script:preferences.antigravity_effort){$script:preferences.antigravity_effort}else{'CLI default'})"
    Write-Host "MODES"
    Write-Host "  Caveman: $(if($Selected -contains 'Caveman'){'ON'}else{'OFF'})"
    Write-Host "  Coworker: $(if($Selected -contains 'Coworker'){'ON'}else{'OFF'})"
    Write-Host "  Orchestrator: $(if($Selected -contains 'Orchestrator'){'ON'}else{'OFF'})"
    Write-Host "STATUS"
    Write-Host "  Codex: READY   Context7: READY   Antigravity: $agyState"
    Write-Host "----------------------------------------------------"
    $menu = @("Launch", "Select Project", "Add Project", "Manage Projects", "Configure Agents", "Configure Modes", "Models", "Status / Doctor", "Settings", "Quick Presets", "Quit")
    for ($i = 0; $i -lt $menu.Count; $i++) {
        $prefix = if ($i -eq $MenuIndex) { ">" } else { " " }
        $number = if ($i -eq 10) { "0" } else { [string]($i + 1) }
        $line = "$prefix [$number] $($menu[$i])"
        if ($i -eq $MenuIndex) { Write-Host $line -ForegroundColor Cyan } else { Write-Host $line }
    }
    Write-Host ""
    Write-Host "Up/Down Navigate   Enter Select   Esc Quit" -ForegroundColor DarkGray
    Write-Host "Number shortcuts remain available. P = Quick Presets, Q = Quit." -ForegroundColor DarkGray
    Write-Host "===================================================="
}

function Invoke-AgexHome {
    param([Parameter(Mandatory)]$ProjectEntry, [string[]]$Selected)
    $menu = @("Launch", "Select Project", "Add Project", "Manage Projects", "Configure Agents", "Configure Modes", "Models", "Status / Doctor", "Settings", "Quick Presets", "Quit")
    $index = 0
    $oldCursor = [Console]::CursorVisible
    try {
        [Console]::CursorVisible = $false
        while ($true) {
            Show-AgexHome -ProjectEntry $ProjectEntry -Selected $Selected -MenuIndex $index
            Complete-AgexMenuFrame
            $key = [Console]::ReadKey($true)
            switch ($key.Key) {
                ([ConsoleKey]::UpArrow) { $index = ($index - 1 + $menu.Count) % $menu.Count; continue }
                ([ConsoleKey]::DownArrow) { $index = ($index + 1) % $menu.Count; continue }
                ([ConsoleKey]::Enter) { return $menu[$index] }
                ([ConsoleKey]::Escape) { return "Quit" }
                ([ConsoleKey]::P) { return "Quick Presets" }
                ([ConsoleKey]::Q) { return "Quit" }
                default { }
            }
            $number = Get-AgexNumberFromKey -Key $key
            if ($number -ge 1 -and $number -le 9) { return $menu[$number - 1] }
            if ($number -eq 0) { return $menu[10] }
        }
    } finally { [Console]::CursorVisible = $oldCursor }
}

function Read-AgexSettingsMenu {
    $choice = Invoke-AgexListMenu -Title "AGEX SETTINGS" -Items @("Refresh model catalogs", "Reset launcher preferences") -Preview {
        param($index)
        @("Preferences: $settingsPath", "Project registry: $registryPath")
    } -Shortcuts @{ B = "Back" }
    if ($choice.Action -eq "Back") { return }
    if ($choice.Index -eq 0) { Refresh-AgexModels; Apply-AgexOverrides; Write-Host "Model catalogs refreshed." -ForegroundColor Cyan; Read-AgexAnyKey; return }
    if ($choice.Index -eq 1) {
        $script:preferences = Get-AgexPreferences -Path (Join-Path $env:TEMP "agex-defaults-missing.json")
        Save-AgexPreferences -Preferences $script:preferences -Path $settingsPath
        Write-Host "Launcher preferences reset. Authentication untouched."
        Read-AgexAnyKey
    }
}

function Get-AgexScratchProject {
    $path = Join-Path $env:USERPROFILE "AGEX-Workspace"
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
    [pscustomobject]@{ name = "No project (AGEX-Workspace)"; path = (Resolve-Path -LiteralPath $path).Path; saved = $false }
}

function Invoke-AgexStart {
    # Simple start for everyday use: continue, open another folder, or start
    # without a project. Everything else lives under Settings.
    $notice = ""
    $last = [string]$script:preferences.last_project
    $current = $null
    if ($last -and (Test-Path -LiteralPath $last -PathType Container)) { $current = Get-InitialProject }
    elseif ($last) { $notice = "Project folder was not found: $last. Choose another folder." }
    $index = 0
    $oldCursor = [Console]::CursorVisible
    try {
        [Console]::CursorVisible = $false
        while ($true) {
            $items = [System.Collections.Generic.List[object]]::new()
            if ($current) { [void]$items.Add(@{ Key = "continue"; Label = "Continue with $($current.name)" }) }
            [void]$items.Add(@{ Key = "open"; Label = "Open another project" })
            [void]$items.Add(@{ Key = "scratch"; Label = "Start without a project" })
            [void]$items.Add(@{ Key = "settings"; Label = "Settings (leader, workload, models, modes)" })
            [void]$items.Add(@{ Key = "quit"; Label = "Quit" })
            $index = [math]::Max(0, [math]::Min($index, $items.Count - 1))
            Clear-AgexScreen
            Write-Host "AGEX AI CONTROL CENTER" -ForegroundColor Cyan
            Write-Host ""
            if ($current) { Write-Host ("Project: {0}" -f $current.path) -ForegroundColor DarkGray } else { Write-Host "No project selected." -ForegroundColor DarkGray }
            $agy = if (Resolve-AgyExecutable) { "found" } else { "not found" }
            Write-Host ("Agents: Codex found, Antigravity {0}. Leader {1}, workload AGY {2}% / Codex {3}%." -f $agy, $script:preferences.leader, $script:preferences.antigravity_share, $script:preferences.codex_share) -ForegroundColor DarkGray
            if ($notice) { Write-Host $notice -ForegroundColor Yellow } else { Write-Host "" }
            Write-Host ""
            for ($i = 0; $i -lt $items.Count; $i++) {
                $number = if ($items[$i].Key -eq "quit") { "0" } else { [string]($i + 1) }
                $line = "{0} {1}  {2}" -f $(if ($i -eq $index) { ">" } else { " " }), $number, $items[$i].Label
                if ($i -eq $index) { Write-Host $line -ForegroundColor Cyan } else { Write-Host $line }
            }
            Write-Host ""
            Write-Host "Enter select   Up/Down move   Number shortcut   Esc quit" -ForegroundColor DarkGray
            Complete-AgexMenuFrame
            $key = [Console]::ReadKey($true)
            $choice = $null
            switch ($key.Key) {
                ([ConsoleKey]::UpArrow) { $index = ($index - 1 + $items.Count) % $items.Count; continue }
                ([ConsoleKey]::DownArrow) { $index = ($index + 1) % $items.Count; continue }
                ([ConsoleKey]::Enter) { $choice = $items[$index].Key }
                ([ConsoleKey]::Escape) { $choice = "quit" }
                ([ConsoleKey]::Q) { $choice = "quit" }
                default { }
            }
            if (-not $choice) {
                $number = Get-AgexNumberFromKey -Key $key
                if ($number -eq 0) { $choice = "quit" } elseif ($number -ge 1 -and $number -le $items.Count) { $choice = $items[$number - 1].Key }
            }
            switch ($choice) {
                "continue" { return $current }
                "open" { $script:menuFrameOpen = $false; $chosen = Read-ProjectMenu; $script:menuFrameOpen = $false; if ($chosen) { if (Test-Path -LiteralPath $chosen.path -PathType Container) { return $chosen }; $notice = "Project folder was not found. Choose another folder." } }
                "scratch" { return (Get-AgexScratchProject) }
                "settings" { $script:menuFrameOpen = $false; $fromSettings = Invoke-AgexSettingsLoop -ProjectEntry $(if ($current) { $current } else { Get-AgexScratchProject }); $script:menuFrameOpen = $false; if ($fromSettings) { return $fromSettings } }
                "quit" { return $null }
            }
        }
    } finally { [Console]::CursorVisible = $oldCursor }
}

function Invoke-AgexSettingsLoop {
    # The original full menu. Returns a project entry when the user launches from here.
    param([Parameter(Mandatory)]$ProjectEntry)
    while ($true) {
        $homeChoice = Invoke-AgexHome -ProjectEntry $ProjectEntry -Selected $script:sessionModes
        switch ($homeChoice) {
            "Launch" { return $ProjectEntry }
            "Select Project" { $chosen = Read-ProjectMenu; if ($chosen) { $ProjectEntry = $chosen } }
            "Add Project" { $added = Add-ProjectInteractive; if ($added) { $ProjectEntry = $added } }
            "Manage Projects" { $managed = Read-ProjectMenu; if ($managed) { $ProjectEntry = $managed } }
            "Configure Agents" { Read-AgentConfiguration; Apply-AgexOverrides }
            "Configure Modes" { $newModes = Read-ModeMenu -Initial $script:sessionModes; if ($null -ne $newModes) { $script:sessionModes = @($newModes) }; if ($script:sessionModes -contains "Orchestrator") { Read-AgentConfiguration; Apply-AgexOverrides } }
            "Models" { if (-not $script:codexModels.Count) { Refresh-AgexModels }; Read-ModelsMenu; Apply-AgexOverrides }
            "Status / Doctor" { $script:menuFrameOpen = $false; & (Join-Path $PSScriptRoot "doctor.ps1"); Read-AgexAnyKey; $script:menuFrameOpen = $false }
            "Settings" { Read-AgexSettingsMenu; Apply-AgexOverrides }
            "Quick Presets" { Read-QuickPreset; Apply-AgexOverrides }
            "Quit" { Save-AgexSession -Selected $script:sessionModes -ProjectEntry $ProjectEntry; return $null }
        }
        Save-AgexSession -Selected $script:sessionModes -ProjectEntry $ProjectEntry
    }
}

function Show-Preview {
    param([string[]]$Selected, [Parameter(Mandatory)]$ProjectEntry)
    $script:launchUiStartRow = -1
    if (-not [Console]::IsOutputRedirected) { try { $script:launchUiStartRow = [Console]::CursorTop } catch { } }
    Write-Host ""
    Write-Host "====================================================" -ForegroundColor DarkCyan
    Write-Host "                 READY TO LAUNCH" -ForegroundColor Cyan
    Write-Host "====================================================" -ForegroundColor DarkCyan
    [void](Write-AgexRuntimeIdentityNotice)
    Write-Host "Project: $($ProjectEntry.name)"
    Write-Host "Path: $($ProjectEntry.path)"
    Write-Host "Mode: $(Get-ModeLabel -Selected $Selected)"
    Write-Host "Leader: $($script:preferences.leader)"
    Write-Host "Target workload: Antigravity $($script:preferences.antigravity_share)% | Codex $($script:preferences.codex_share)%"
    $primary = Resolve-AgexLeader -ConfiguredLeader $script:preferences.leader -CodexShare $script:preferences.codex_share -AntigravityShare $script:preferences.antigravity_share
    Write-Host "Primary host: AGEX control shell"
    Write-Host "Resolved real leader: $primary"
    Write-Host "Models: Codex $($script:preferences.codex_model) / $(if($script:preferences.codex_effort){$script:preferences.codex_effort}else{'default'}); Antigravity $(if($script:preferences.antigravity_model){$script:preferences.antigravity_model}else{'default/unavailable'}) / $(if($script:preferences.antigravity_effort){$script:preferences.antigravity_effort}else{'default'})"
    Write-Host "Capabilities:"
    if (-not $Selected -or $Selected.Count -eq 0) { Write-Host "  EXTRA MODES: NONE" }
    if ($Selected -contains "Caveman") { Write-Host "  CAVEMAN: ACTIVE FOR SESSION" }
    if ($Selected -contains "Coworker") { Write-Host "  COWORKER TOOLS: ENABLED" }
    if ($Selected -contains "Orchestrator") { Write-Host "  ANTIGRAVITY: ENABLED"; Write-Host "  MAX WORKERS: 2" }
    Write-Host "  CONTEXT7: READY"
    Write-Host ""
}

function Remove-LaunchPreview {
    if ([Console]::IsOutputRedirected -or $script:launchUiStartRow -lt 0) { return }
    try {
        $first = [math]::Min($script:launchUiStartRow, [Console]::BufferHeight - 1)
        $last = [math]::Min([Console]::CursorTop, [Console]::BufferHeight - 1)
        for ($row = $first; $row -le $last; $row++) {
            [Console]::SetCursorPosition(0, $row)
            [Console]::Write((" " * [math]::Max(1, [Console]::BufferWidth - 1)))
        }
        [Console]::SetCursorPosition(0, $first)
    } catch { }
    $script:launchUiStartRow = -1
}

# Model catalogs need a local socket and an AGY network call; load them only
# when a model menu is opened so the normal start stays fast.
if ($CodexModel -or $CodexEffort -or $AntigravityModel -or $AntigravityEffort) { Refresh-AgexModels }
Apply-AgexOverrides
$script:sessionModes = @(Parse-Modes -Value $script:preferences.last_modes)
$projectEntry = $null

if ($Interactive) {
    $projectEntry = Invoke-AgexStart
    if (-not $projectEntry) { exit 0 }
    $selected = @($script:sessionModes)
} else {
    $selected = @(Parse-Modes -Value $Modes)
    $projectEntry = Resolve-ProjectPath -Requested $Project
    Show-Preview -Selected $selected -ProjectEntry $projectEntry
}

Apply-AgexOverrides
Save-AgexSession -Selected $selected -ProjectEntry $projectEntry
Remove-LaunchPreview

# AGEX owns one permanent frontend. Leader changes backend routing only.
$agyPath = Resolve-AgyExecutable
$primaryScript = Join-Path $PSScriptRoot "agex-primary.ps1"
$primaryArgs = @("-Project", $projectEntry.path, "-SessionId", $script:sessionId, "-ConfiguredLeader", $script:preferences.leader, "-CodexShare", ([string]$script:preferences.codex_share), "-AntigravityShare", ([string]$script:preferences.antigravity_share), "-CodexSandbox", $(if ([string]$script:preferences.codex_task_sandbox -eq "workspace-write") { "workspace-write" } else { "read-only" }))
# Windows PowerShell drops empty arguments on the command line: pass only values that are set.
foreach ($pair in @(@("-CodexPath", $codexPath), @("-AgyPath", $agyPath), @("-CodexModel", $script:preferences.codex_model), @("-CodexEffort", $script:preferences.codex_effort), @("-AntigravityModel", $script:preferences.antigravity_model), @("-AntigravityEffort", $script:preferences.antigravity_effort))) {
    if (-not [string]::IsNullOrWhiteSpace([string]$pair[1])) { $primaryArgs += @($pair[0], [string]$pair[1]) }
}
& (Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe") -NoProfile -ExecutionPolicy Bypass -File $primaryScript @primaryArgs
exit $LASTEXITCODE
