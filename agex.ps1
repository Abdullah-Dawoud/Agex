# AGEX command line entry point.
#
#   agex               open the AGEX desktop app (terminal mode if the app is missing)
#   agex --cli         terminal control center
#   agex doctor        check the AGEX installation and agents
#   agex agents        list supported and detected agents
#   agex project [p]   show or set the default project folder
#   agex update        install the newest AGEX release (checksum verified)
#   agex repair        fix AGEX command, shortcut and settings; re-detect agents
#   agex uninstall     remove AGEX (keeps Codex, Antigravity and your data)
#   agex version       print the version
#
# Developer and environment tools from the original setup repository remain
# available (env-doctor, acceptance, final-test, orchestrator-dispatch, ...).
$ErrorActionPreference = "Stop"
$scripts = Join-Path $PSScriptRoot "scripts"
$command = if ($args.Count) { [string]$args[0] } else { "open" }
$rest = if ($args.Count -gt 1) { @($args[1..($args.Count - 1)]) } else { @() }

function Import-AgexCli {
    . (Join-Path $scripts "agex-common.ps1")
    . (Join-Path $scripts "agex-process.ps1")
    . (Join-Path $scripts "agex-adapters.ps1")
    . (Join-Path $scripts "agex-maintenance.ps1")
    Initialize-AgexStorage
}

function Write-AgexCheckTable {
    param($Rows)
    foreach ($row in $Rows) {
        $color = switch ($row.Status) { "OK" { "Green" } "FIXED" { "Green" } "WARN" { "Yellow" } "FAIL" { "Red" } default { "Gray" } }
        Write-Host ("{0,-6} {1,-24} {2}" -f $row.Status, $row.Check, $row.Detail) -ForegroundColor $color
        if ($row.Fix) { Write-Host ("       {0}" -f $row.Fix) -ForegroundColor DarkGray }
    }
}

switch -Regex ($command) {
    '^(open|desktop|app)$' {
        $desktop = Join-Path $PSScriptRoot "AGEX.exe"
        if (Test-Path -LiteralPath $desktop -PathType Leaf) { Start-Process -FilePath $desktop; exit 0 }
        Write-Host "The AGEX desktop app is not built in this folder; starting terminal mode." -ForegroundColor Yellow
        & (Join-Path $scripts "workbench.ps1") -Interactive
        exit $LASTEXITCODE
    }
    '^(--cli|cli|menu|-cli)$' { & (Join-Path $scripts "workbench.ps1") -Interactive; exit $LASTEXITCODE }
    '^(doctor)$' {
        . Import-AgexCli
        $rows = @(Invoke-AgexDoctor)
        Write-Host "AGEX DOCTOR" -ForegroundColor Cyan
        Write-AgexCheckTable -Rows $rows
        if (@($rows | Where-Object Status -eq "FAIL").Count) { exit 1 }
        exit 0
    }
    '^(agents)$' {
        . Import-AgexCli
        $prefs = Get-AgexPreferences
        $scan = Invoke-AgexDiscovery -Runtime (New-AgexRuntime) -EnabledAgents @($prefs.enabled_agents)
        Write-Host ("AGENTS  {0} supported, {1} ready, {2} detected but not integrated" -f $scan.Summary.Supported, $scan.Summary.Ready, $scan.Summary.Detected) -ForegroundColor Cyan
        foreach ($agent in @($scan.Agents | Where-Object { $_.Status -ne "UNAVAILABLE" -or $_.Integration -eq "Supported" })) {
            $state = if ($agent.Status -eq "SUPPORTED" -and $agent.Ready) { "Supported, ready" } elseif ($agent.Status -eq "SUPPORTED") { "Supported, not ready" } elseif ($agent.Status -eq "DETECTED") { "Detected, not integrated" } else { "Not installed" }
            $color = if ($agent.Ready) { "Green" } elseif ($agent.Status -eq "DETECTED") { "Gray" } else { "Yellow" }
            Write-Host ("  {0,-22} {1,-26} {2} {3}" -f $agent.Name, $state, $agent.Version, $(if ($agent.Status -eq "SUPPORTED" -and -not $agent.Enabled) { "(turned off)" } else { "" })) -ForegroundColor $color
            if (-not $agent.Ready -and $agent.Reason -and $agent.Status -ne "DETECTED") { Write-Host ("      {0}" -f $agent.Reason) -ForegroundColor DarkGray }
        }
        $ides = @($scan.Ides | Where-Object Status -eq "DETECTED")
        if ($ides.Count) { Write-Host "DEVELOPMENT ENVIRONMENTS (detected, not agents)" -ForegroundColor Cyan; foreach ($ide in $ides) { Write-Host ("  {0,-22} {1}" -f $ide.Name, $ide.Version) } }
        Write-Host "INTEGRATIONS" -ForegroundColor Cyan
        foreach ($item in $scan.Integrations) { Write-Host ("  {0,-22} {1}" -f $item.Name, $item.Detail) }
        exit 0
    }
    '^(project)$' {
        . Import-AgexCli
        $prefs = Get-AgexPreferences
        if ($rest.Count) {
            $path = [Environment]::ExpandEnvironmentVariables([string]$rest[0])
            if (-not (Test-Path -LiteralPath $path -PathType Container)) { Write-Host "Project folder was not found. Choose another folder." -ForegroundColor Yellow; exit 1 }
            Add-AgexRecentProject -Preferences $prefs -ProjectPath (Resolve-Path -LiteralPath $path).Path
            Save-AgexPreferences -Preferences $prefs
        }
        Write-Host ("Project: {0}" -f $(if ($prefs.last_project) { $prefs.last_project } else { "(none)" }))
        foreach ($recent in @($prefs.recent_projects | Select-Object -Skip 1)) { Write-Host "  recent: $recent" -ForegroundColor DarkGray }
        exit 0
    }
    '^(update)$' { . Import-AgexCli; Write-Host (Invoke-AgexUpdate); exit 0 }
    '^(update-check)$' { . Import-AgexCli; $u = Get-AgexUpdateInfo; Write-Host $u.Message; exit 0 }
    '^(repair)$' {
        . Import-AgexCli
        Write-Host "AGEX REPAIR" -ForegroundColor Cyan
        $rows = @(Invoke-AgexRepair -Fix)
        Write-AgexCheckTable -Rows $rows
        exit 0
    }
    '^(uninstall)$' { . Import-AgexCli; Write-Host (Invoke-AgexUninstall -PurgeData:(@($rest) -contains "--purge-data")); exit 0 }
    '^(version|--version|-v)$' { . Import-AgexCli; Write-Output (Get-AgexVersion); exit 0 }
    '^(help|--help|-h|/\?)$' {
        Get-Content -LiteralPath $PSCommandPath | Select-Object -Skip 2 -First 12 | ForEach-Object { $_.TrimStart('#').Substring([math]::Min(1, $_.TrimStart('#').Length)) }
        exit 0
    }
    '^(env-doctor)$' { & (Join-Path $PSScriptRoot "setup.ps1") doctor @rest; exit $LASTEXITCODE }
    default {
        $tools = Join-Path $PSScriptRoot "setup.ps1"
        if (Test-Path -LiteralPath $tools) { & $tools $command @rest; exit $LASTEXITCODE }
        Write-Host "Unknown command '$command'. Run 'agex help'." -ForegroundColor Yellow
        exit 2
    }
}
