# AGEX installer (per user, no administrator rights).
#
# One-command install:
#   irm https://github.com/Abdullah-Dawoud/Ai-COGY/releases/latest/download/agex-install.ps1 -OutFile "$env:TEMP\agex-install.ps1"; powershell -ExecutionPolicy Bypass -File "$env:TEMP\agex-install.ps1"
#
# What it does:
#   1. Checks Windows, PowerShell and .NET Framework 4.8.
#   2. Downloads the AGEX release package and SHA256SUMS.txt from GitHub Releases.
#   3. Verifies the package SHA-256 against SHA256SUMS.txt. Stops on mismatch.
#   4. Installs to %LOCALAPPDATA%\Programs\AGEX (replacing only AGEX files).
#   5. Adds the "agex" command to your user PATH and a Start Menu shortcut.
#   6. Starts AGEX.
# It never installs, changes or removes Codex, Antigravity or any other tool.
[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$Repository = "Abdullah-Dawoud/Ai-COGY",
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\AGEX"),
    [string]$Package,          # local package zip (offline install / testing)
    [string]$Checksums,        # local SHA256SUMS.txt for -Package
    [switch]$NoLaunch,
    [switch]$NoShortcut,
    [switch]$NoPath
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step { param([string]$Text) Write-Host "  $Text" }
function Stop-Install { param([string]$Text) Write-Host "AGEX was not installed: $Text" -ForegroundColor Red; exit 1 }

Write-Host "AGEX AI CONTROL CENTER - installer" -ForegroundColor Cyan

# 1. System checks
if ([Environment]::OSVersion.Platform -ne "Win32NT") { Stop-Install "AGEX runs on Windows only." }
$os = [Environment]::OSVersion.Version
if ($os.Major -lt 10) { Stop-Install "Windows 10 or newer is required." }
$arch = if ([Environment]::Is64BitOperatingSystem) { if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64" -or $env:PROCESSOR_ARCHITEW6432 -eq "ARM64") { "arm64" } else { "x64" } } else { "x86" }
$release = 0
try { $release = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop).Release } catch { }
if ($release -lt 528040) { Stop-Install ".NET Framework 4.8 is required (it ships with current Windows 10 and 11; install it from Windows Update)." }
Write-Step ("Windows {0}.{1} ({2}), PowerShell {3}, .NET Framework 4.8 - OK" -f $os.Major, $os.Build, $arch, $PSVersionTable.PSVersion)

$work = Join-Path $env:TEMP ("agex-install-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    # 2. Obtain the package
    if ($Package) {
        if (-not (Test-Path -LiteralPath $Package -PathType Leaf)) { Stop-Install "Package not found: $Package" }
        $zip = (Resolve-Path -LiteralPath $Package).Path
        $sums = if ($Checksums) { $Checksums } else { Join-Path (Split-Path -Parent $zip) "SHA256SUMS.txt" }
        if (-not (Test-Path -LiteralPath $sums -PathType Leaf)) { Stop-Install "SHA256SUMS.txt not found next to the package." }
        $source = "local:" + (Split-Path -Leaf $zip)
        Write-Step "Using local package $(Split-Path -Leaf $zip)"
    } else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $api = if ($Version -eq "latest") { "https://api.github.com/repos/$Repository/releases/latest" } else { "https://api.github.com/repos/$Repository/releases/tags/v$($Version.TrimStart('v'))" }
        try { $info = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "AGEX-Installer" } -TimeoutSec 30 }
        catch { Stop-Install "Could not find an AGEX release at github.com/$Repository ($($_.Exception.Message))." }
        $asset = @($info.assets | Where-Object { $_.name -match '^agex-.*-win\.zip$' } | Select-Object -First 1)
        $sumAsset = @($info.assets | Where-Object { $_.name -eq "SHA256SUMS.txt" } | Select-Object -First 1)
        if (-not $asset.Count -or -not $sumAsset.Count) { Stop-Install "The release $($info.tag_name) has no AGEX package or checksum file." }
        $zip = Join-Path $work $asset[0].name
        $sums = Join-Path $work "SHA256SUMS.txt"
        Write-Step "Downloading AGEX $($info.tag_name)..."
        Invoke-WebRequest -Uri $asset[0].browser_download_url -OutFile $zip -UseBasicParsing
        Invoke-WebRequest -Uri $sumAsset[0].browser_download_url -OutFile $sums -UseBasicParsing
        $source = "github:$Repository@$($info.tag_name)"
    }

    # 3. Verify integrity
    $name = Split-Path -Leaf $zip
    $expected = @(Get-Content -LiteralPath $sums | Where-Object { $_ -match ('\s\*?' + [regex]::Escape($name) + '$') } | ForEach-Object { ($_ -split '\s+')[0] }) | Select-Object -First 1
    if (-not $expected) { Stop-Install "$name is not listed in SHA256SUMS.txt." }
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if ($actual -ne $expected.ToUpperInvariant()) { Stop-Install "Checksum mismatch for $name. The download may be damaged or altered." }
    Write-Step "Checksum verified (SHA-256 $($actual.Substring(0, 16))...)"

    # 4. Install
    $staging = Join-Path $work "package"
    Expand-Archive -LiteralPath $zip -DestinationPath $staging -Force
    if (-not (Test-Path -LiteralPath (Join-Path $staging "agex.ps1"))) { Stop-Install "The package does not contain agex.ps1." }
    Get-Process -Name "AGEX" -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { Write-Step "Closing the running AGEX app..."; $_.CloseMainWindow() | Out-Null; if (-not $_.WaitForExit(8000)) { $_.Kill() } }
    if (Test-Path -LiteralPath $InstallDir) {
        $marker = Join-Path $InstallDir "install.json"
        if (-not (Test-Path -LiteralPath $marker) -and @(Get-ChildItem -LiteralPath $InstallDir -Force).Count) { Stop-Install "$InstallDir exists and was not created by AGEX. Choose another folder with -InstallDir." }
        Get-ChildItem -LiteralPath $InstallDir -Force | Remove-Item -Recurse -Force
    }
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Copy-Item -Path (Join-Path $staging "*") -Destination $InstallDir -Recurse -Force
    $version = ([IO.File]::ReadAllText((Join-Path $InstallDir "VERSION"))).Trim()
    @{ product = "AGEX"; version = $version; repository = $Repository; source = $source; installed_at = (Get-Date).ToUniversalTime().ToString("o"); sha256 = $actual } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallDir "install.json") -Encoding UTF8
    Write-Step "Installed AGEX $version to $InstallDir"

    # 5. Command and shortcut
    if (-not $NoPath) {
        # Retire launcher shims from older builds that would shadow the new command.
        $dataRoot = if ($env:AGEX_HOME) { $env:AGEX_HOME } else { Join-Path $env:LOCALAPPDATA "AGEX" }
        foreach ($name in @("agex.cmd", ("da" + "woud.cmd"))) {
            $old = Join-Path $env:LOCALAPPDATA ("Microsoft\WindowsApps\" + $name)
            if ((Test-Path -LiteralPath $old -PathType Leaf) -and ([IO.File]::ReadAllText($old) -match '(?i)powershell\.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "[^"]*\\(setup|agex)\.ps1" %\*' -or [IO.File]::ReadAllText($old) -match '(?i)renamed to AGEX\. Use: agex')) {
                $backup = Join-Path $dataRoot "backup\legacy-shims"; New-Item -ItemType Directory -Path $backup -Force | Out-Null
                Copy-Item -LiteralPath $old -Destination (Join-Path $backup $name) -Force
                Remove-Item -LiteralPath $old -Force
                Write-Step "Retired old launcher $old (backup kept)"
            }
        }
        $bin = Join-Path $InstallDir "bin"
        $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
        if (@($userPath -split ';' | ForEach-Object { $_.TrimEnd('\') }) -notcontains $bin) {
            [Environment]::SetEnvironmentVariable("Path", $(if ($userPath) { $userPath.TrimEnd(';') + ';' + $bin } else { $bin }), "User")
            Write-Step "Added the agex command (open a new terminal to use it)"
        }
        $env:Path = $env:Path.TrimEnd(';') + ';' + $bin
    }
    $exe = Join-Path $InstallDir "AGEX.exe"
    if (-not $NoShortcut -and (Test-Path -LiteralPath $exe)) {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Programs")) "AGEX.lnk"))
        $link.TargetPath = $exe; $link.WorkingDirectory = $InstallDir; $link.Description = "AGEX AI CONTROL CENTER"; $link.IconLocation = "$exe,0"
        $link.Save()
        Write-Step "Start Menu shortcut created"
    }

    Write-Host "AGEX is installed." -ForegroundColor Green
    # 6. Launch
    if (-not $NoLaunch -and (Test-Path -LiteralPath $exe)) { Start-Process -FilePath $exe }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
