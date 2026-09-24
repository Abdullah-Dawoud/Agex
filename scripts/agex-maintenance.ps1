# AGEX installation maintenance: doctor, repair, update, uninstall.
# Only AGEX-owned files and settings are touched. Third-party agents (Codex,
# Antigravity, IDEs) are detected, never installed or removed.

$script:AgexRepository = "Abdullah-Dawoud/Ai-COGY"

function Get-AgexInstallRoot { Split-Path -Parent $PSScriptRoot }

function Get-AgexInstallInfo {
    $root = Get-AgexInstallRoot
    $file = Join-Path $root "install.json"
    $info = [ordered]@{ Root = $root; Installed = $false; Version = (Get-AgexVersion); Repository = $script:AgexRepository; InstalledAt = ""; Source = "" }
    if (Test-Path -LiteralPath $file -PathType Leaf) {
        try {
            $data = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
            $info.Installed = $true
            if ($data.repository) { $info.Repository = [string]$data.repository }
            $info.InstalledAt = [string]$data.installed_at
            $info.Source = [string]$data.source
        } catch { }
    }
    [pscustomobject]$info
}

function Get-AgexShortcutPath { Join-Path ([Environment]::GetFolderPath("Programs")) "AGEX.lnk" }

function Test-AgexUserPath {
    param([Parameter(Mandatory)][string]$Directory)
    $current = [Environment]::GetEnvironmentVariable("Path", "User")
    @($current -split ';' | Where-Object { $_ } | ForEach-Object { $_.TrimEnd('\') }) -contains $Directory.TrimEnd('\')
}

function Add-AgexUserPath {
    param([Parameter(Mandatory)][string]$Directory)
    if (Test-AgexUserPath -Directory $Directory) { return $false }
    $current = [Environment]::GetEnvironmentVariable("Path", "User")
    $value = if ([string]::IsNullOrWhiteSpace($current)) { $Directory } else { $current.TrimEnd(';') + ';' + $Directory }
    [Environment]::SetEnvironmentVariable("Path", $value, "User")
    $true
}

function Remove-AgexUserPath {
    param([Parameter(Mandatory)][string]$Directory)
    $current = [Environment]::GetEnvironmentVariable("Path", "User")
    if (-not $current) { return }
    $kept = @($current -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ne $Directory.TrimEnd('\') })
    [Environment]::SetEnvironmentVariable("Path", ($kept -join ';'), "User")
}

function New-AgexShortcut {
    param([Parameter(Mandatory)][string]$Target, [string]$Path = (Get-AgexShortcutPath))
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $Target
    $link.WorkingDirectory = Split-Path -Parent $Target
    $link.Description = "AGEX AI CONTROL CENTER"
    $link.IconLocation = "$Target,0"
    $link.Save()
}

function Remove-AgexLegacyShims {
    # Older builds copied launcher shims into WindowsApps, where they shadow the
    # installed agex command. Retire only files whose content is one of those
    # launchers (PowerShell -File ...\setup.ps1 or ...\agex.ps1); keep a backup.
    param([string]$BackupRoot)
    $removed = @()
    $folder = Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps"
    foreach ($name in @("agex.cmd", ("da" + "woud.cmd"))) {
        $file = Join-Path $folder $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        $text = [IO.File]::ReadAllText($file)
        if ($text -notmatch '(?i)powershell\.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "[^"]*\\(setup|agex)\.ps1" %\*' -and $text -notmatch '(?i)renamed to AGEX\. Use: agex') { continue }
        if ($BackupRoot) { New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null; Copy-Item -LiteralPath $file -Destination (Join-Path $BackupRoot $name) -Force }
        Remove-Item -LiteralPath $file -Force
        $removed += $file
    }
    $removed
}

function Invoke-AgexDoctor {
    # Read-only health report for the AGEX installation.
    param([switch]$SkipAgents)
    $checks = [System.Collections.Generic.List[object]]::new()
    $add = { param($Name, $Status, $Detail, $Fix) [void]$checks.Add([pscustomobject]@{ Check = $Name; Status = $Status; Detail = $Detail; Fix = $Fix }) }
    $info = Get-AgexInstallInfo
    & $add "AGEX version" "OK" ("{0} at {1}{2}" -f $info.Version, $info.Root, $(if ($info.Installed) { "" } else { " (source checkout)" })) ""
    $missing = @("agex.ps1", "scripts\agex-primary.ps1", "scripts\agex-process.ps1", "scripts\agex-graph.ps1", "scripts\agex-ui.ps1", "scripts\agex-common.ps1", "scripts\agex-adapters.ps1", "scripts\agex-session.ps1", "scripts\orchestrator.ps1", "scripts\worker-run.ps1", "bin\agex.cmd") | Where-Object { -not (Test-Path -LiteralPath (Join-Path $info.Root $_) -PathType Leaf) }
    & $add "Engine files" $(if ($missing.Count) { "FAIL" } else { "OK" }) $(if ($missing.Count) { "Missing: " + ($missing -join ", ") } else { "All present." }) $(if ($missing.Count) { "Reinstall AGEX (agex update or the install command)." } else { "" })
    $desktop = Join-Path $info.Root "AGEX.exe"
    & $add "Desktop app" $(if (Test-Path -LiteralPath $desktop -PathType Leaf) { "OK" } else { "WARN" }) $(if (Test-Path -LiteralPath $desktop -PathType Leaf) { $desktop } else { "AGEX.exe not built. Terminal mode still works." }) $(if (Test-Path -LiteralPath $desktop -PathType Leaf) { "" } else { "Run tools\build-desktop.ps1 or install a release." })
    $release = 0
    try { $release = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop).Release } catch { }
    & $add ".NET Framework 4.8" $(if ($release -ge 528040) { "OK" } else { "FAIL" }) ("Release key {0}" -f $release) $(if ($release -ge 528040) { "" } else { "Windows Update provides .NET Framework 4.8." })
    & $add "PowerShell" "OK" ("Windows PowerShell {0}" -f $PSVersionTable.PSVersion) ""
    try { $data = Get-AgexDataRoot; $probe = Join-Path $data ".write-test"; [IO.File]::WriteAllText($probe, "ok"); Remove-Item -LiteralPath $probe -Force; & $add "Data folder" "OK" $data "" } catch { & $add "Data folder" "FAIL" $_.Exception.Message "Check permissions on %LOCALAPPDATA%\AGEX." }
    $settingsFile = Get-AgexPath Settings
    $settingsOk = $true
    if (Test-Path -LiteralPath $settingsFile) { try { Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json | Out-Null } catch { $settingsOk = $false } }
    & $add "Settings" $(if ($settingsOk) { "OK" } else { "FAIL" }) $settingsFile $(if ($settingsOk) { "" } else { "agex repair resets unreadable settings (a backup is kept)." })
    $bin = Join-Path $info.Root "bin"
    $onPath = Test-AgexUserPath -Directory $bin
    $resolved = @(Get-Command agex -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($info.Installed -and $resolved.Count -and -not $resolved[0].Source.StartsWith($info.Root, [StringComparison]::OrdinalIgnoreCase)) { & $add "agex command target" "WARN" ("'agex' currently runs {0}" -f $resolved[0].Source) "agex repair retires old AGEX shims; remove other copies yourself." }
    & $add "agex command" $(if ($onPath) { "OK" } elseif ($info.Installed) { "WARN" } else { "INFO" }) $(if ($onPath) { "$bin is on your PATH." } else { "$bin is not on your user PATH." }) $(if ($onPath) { "" } elseif ($info.Installed) { "agex repair adds it." } else { "Install AGEX (docs/INSTALL.md) to register the agex command." })
    $shortcut = Get-AgexShortcutPath
    & $add "Start Menu shortcut" $(if (Test-Path -LiteralPath $shortcut) { "OK" } elseif ($info.Installed) { "WARN" } else { "INFO" }) $shortcut $(if (Test-Path -LiteralPath $shortcut) { "" } elseif ($info.Installed) { "agex repair creates it." } else { "The installer creates it." })
    if (-not $SkipAgents) {
        $scan = Invoke-AgexDiscovery -Runtime (New-AgexRuntime) -EnabledAgents @((Get-AgexPreferences).enabled_agents)
        foreach ($agent in @($scan.Agents | Where-Object Status -eq "SUPPORTED")) { & $add ("Agent: " + $agent.Name) $(if ($agent.Ready) { "OK" } else { "WARN" }) $(if ($agent.Ready) { "Ready {0}" -f $agent.Version } else { $agent.Reason }) $(if ($agent.Ready) { "" } else { "Install or sign in to $($agent.Name); AGEX does not install agents." }) }
        foreach ($agent in @($scan.Agents | Where-Object Status -eq "UNAVAILABLE" | Where-Object Integration -eq "Supported")) { & $add ("Agent: " + $agent.Name) "WARN" $agent.Reason "Install $($agent.Name) to use it with AGEX." }
        & $add "Detected tools" "INFO" ("{0} supported, {1} detected but not integrated" -f $scan.Summary.Supported, $scan.Summary.Detected) ""
    }
    @($checks)
}

function Invoke-AgexRepair {
    # Fixes only AGEX-owned state. Returns the doctor results after repair.
    param([switch]$Fix)
    $info = Get-AgexInstallInfo
    $actions = [System.Collections.Generic.List[object]]::new()
    $settingsFile = Get-AgexPath Settings
    if (Test-Path -LiteralPath $settingsFile) {
        try { Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json | Out-Null }
        catch {
            $backup = "$settingsFile.broken-" + (Get-Date -Format "yyyyMMddHHmmss")
            Move-Item -LiteralPath $settingsFile -Destination $backup -Force
            Save-AgexPreferences -Preferences (Get-AgexPreferences)
            [void]$actions.Add([pscustomobject]@{ Check = "Repair: settings"; Status = "FIXED"; Detail = "Unreadable settings reset. Backup: $backup"; Fix = "" })
        }
    }
    if ($info.Installed) {
        foreach ($shim in @(Remove-AgexLegacyShims -BackupRoot (Join-Path (Get-AgexDataRoot) "backup\legacy-shims"))) { [void]$actions.Add([pscustomobject]@{ Check = "Repair: old command shim"; Status = "FIXED"; Detail = "Removed $shim (backup in AGEX data folder)."; Fix = "" }) }
    }
    if ($info.Installed -or $Fix) {
        $bin = Join-Path $info.Root "bin"
        if ($info.Installed -and (Add-AgexUserPath -Directory $bin)) { [void]$actions.Add([pscustomobject]@{ Check = "Repair: agex command"; Status = "FIXED"; Detail = "Added $bin to your user PATH. Open a new terminal."; Fix = "" }) }
        $desktop = Join-Path $info.Root "AGEX.exe"
        if ($info.Installed -and (Test-Path -LiteralPath $desktop -PathType Leaf) -and -not (Test-Path -LiteralPath (Get-AgexShortcutPath))) { New-AgexShortcut -Target $desktop; [void]$actions.Add([pscustomobject]@{ Check = "Repair: shortcut"; Status = "FIXED"; Detail = "Start Menu shortcut created."; Fix = "" }) }
    }
    @($actions) + @(Invoke-AgexDoctor)
}

function Get-AgexUpdateInfo {
    param([string]$Repository)
    $info = Get-AgexInstallInfo
    $repo = if ($Repository) { $Repository } else { $info.Repository }
    $result = [ordered]@{ Current = $info.Version; Latest = ""; Available = $false; Repository = $repo; Url = ""; Message = "" }
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "AGEX" } -TimeoutSec 15
        $latest = ([string]$release.tag_name).TrimStart('v')
        $result.Latest = $latest
        $result.Url = [string]$release.html_url
        $result.Available = ([version]($latest -replace '[^0-9.].*$', '')) -gt ([version]($info.Version -replace '[^0-9.].*$', ''))
        $result.Message = if ($result.Available) { "AGEX $latest is available." } else { "AGEX is up to date." }
    } catch {
        $status = $null
        try { $status = [int]$_.Exception.Response.StatusCode } catch { }
        $result.Message = if ($status -eq 404) { "No AGEX release is published yet for $repo." } else { "Could not check for updates: $($_.Exception.Message)" }
    }
    [pscustomobject]$result
}

function Invoke-AgexUpdate {
    # Downloads the release installer, verifies it against the release
    # checksum list, then runs it. The installer verifies the package too.
    param([string]$Repository)
    $update = Get-AgexUpdateInfo -Repository $Repository
    if (-not $update.Available) { return $update.Message }
    $work = Join-Path $env:TEMP ("agex-update-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $base = "https://github.com/$($update.Repository)/releases/download/v$($update.Latest)"
    Invoke-WebRequest -Uri "$base/SHA256SUMS.txt" -OutFile (Join-Path $work "SHA256SUMS.txt") -UseBasicParsing
    Invoke-WebRequest -Uri "$base/agex-install.ps1" -OutFile (Join-Path $work "agex-install.ps1") -UseBasicParsing
    $expected = @(Get-Content (Join-Path $work "SHA256SUMS.txt") | Where-Object { $_ -match '\sagex-install\.ps1$' } | ForEach-Object { ($_ -split '\s+')[0] }) | Select-Object -First 1
    $actual = (Get-FileHash -LiteralPath (Join-Path $work "agex-install.ps1") -Algorithm SHA256).Hash
    if (-not $expected -or $expected -ne $actual) { throw "Installer checksum did not match the release. Update stopped." }
    & (Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe") -NoProfile -ExecutionPolicy Bypass -File (Join-Path $work "agex-install.ps1") -Version $update.Latest -Repository $update.Repository
    "Updated to AGEX $($update.Latest)."
}

function Invoke-AgexUninstall {
    # Removes AGEX-owned files only. Agents and IDEs are never touched.
    param([switch]$PurgeData)
    $info = Get-AgexInstallInfo
    if (-not $info.Installed) { throw "This copy of AGEX was not installed by the AGEX installer ($($info.Root)). Delete the folder yourself if you no longer need it." }
    Remove-AgexUserPath -Directory (Join-Path $info.Root "bin")
    $shortcut = Get-AgexShortcutPath
    $exe = Join-Path $info.Root "AGEX.exe"
    if (Test-Path -LiteralPath $shortcut) {
        # Remove the shortcut only when it points at this installation.
        $target = try { (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath } catch { "" }
        if ($target -and $target -ieq $exe) { Remove-Item -LiteralPath $shortcut -Force }
    }
    $run = (Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "AGEX" -ErrorAction SilentlyContinue).AGEX
    if ($run -and $run -like "*$exe*") { Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "AGEX" -ErrorAction SilentlyContinue }
    if ($PurgeData) { Remove-Item -LiteralPath (Get-AgexDataRoot) -Recurse -Force -ErrorAction SilentlyContinue }
    # The install folder is removed by a detached process after this one exits.
    $cmd = "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '{0}' -Recurse -Force -ErrorAction SilentlyContinue" -f $info.Root.Replace("'", "''")
    Start-Process -FilePath (Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe") -ArgumentList @("-NoProfile", "-WindowStyle", "Hidden", "-Command", $cmd) -WindowStyle Hidden
    "AGEX was removed. Codex, Antigravity and other tools were not changed." + $(if ($PurgeData) { " AGEX data was deleted." } else { " Your AGEX data is kept in $(Get-AgexDataRoot)." })
}

function Set-AgexStartWithWindows {
    param([bool]$Enabled)
    $key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $desktop = Join-Path (Get-AgexInstallRoot) "AGEX.exe"
    if ($Enabled -and (Test-Path -LiteralPath $desktop)) { Set-ItemProperty -Path $key -Name "AGEX" -Value ('"{0}" --minimized' -f $desktop) }
    else { Remove-ItemProperty -Path $key -Name "AGEX" -ErrorAction SilentlyContinue }
}
