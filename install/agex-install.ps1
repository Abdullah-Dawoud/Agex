# AGEX installer for Windows (per user, no administrator rights).
#
# One-command install (PowerShell):
#   irm https://raw.githubusercontent.com/Abdullah-Dawoud/Agex/main/install/agex-install.ps1 -OutFile "$env:TEMP\agex-install.ps1"; powershell -ExecutionPolicy Bypass -File "$env:TEMP\agex-install.ps1"
#
# What it does:
#   1. Checks Windows 10/11 and the processor (x64 or ARM64).
#   2. Downloads the AGEX package for this processor and SHA256SUMS.txt from GitHub Releases.
#   3. Verifies the package's SHA-256. Stops on any mismatch.
#   4. Installs to %LOCALAPPDATA%\Programs\AGEX, replacing only AGEX's own files.
#   5. Adds the "agex" command to your user PATH and an "AGEX" Start Menu shortcut.
#   6. Starts AGEX.
# It never installs, changes or removes Codex, Antigravity, other agents or your projects.
#
# Other uses:
#   -Uninstall [-Purge]      remove AGEX (and with -Purge also its settings and history)
#   -Package <zip> -ExpectedSha256 <hash>   install a package that was already downloaded and verified
[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$Repository = "Abdullah-Dawoud/Agex",
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\AGEX"),
    [string]$Package,
    [string]$Checksums,
    [string]$ExpectedSha256,
    [int]$WaitForExit = 0,
    [switch]$NoLaunch,
    [switch]$NoShortcut,
    [switch]$NoPath,
    [switch]$Uninstall,
    [switch]$Purge
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step { param([string]$Text) Write-Host "  $Text" }
function Stop-Install { param([string]$Text) Write-Host "AGEX: $Text" -ForegroundColor Red; exit 1 }
$marker = Join-Path $InstallDir "install.json"
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "AGEX.lnk"
$dataRoot = if ($env:AGEX_HOME) { $env:AGEX_HOME } else { Join-Path $env:LOCALAPPDATA "AGEX" }

function Stop-RunningAgex {
    foreach ($name in "AgexDesktop", "agex") {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) -and $_.Id -ne $PID } | ForEach-Object {
            Write-Step "Closing $($_.ProcessName)..."
            if ($_.MainWindowHandle -ne 0) { [void]$_.CloseMainWindow() }
            if (-not $_.WaitForExit(8000)) { $_.Kill() }
        }
    }
}

function Set-UserPath {
    param([string]$Add, [string[]]$Remove = @())
    $current = [Environment]::GetEnvironmentVariable("Path", "User")
    $parts = @($current -split ';' | Where-Object { $_ })
    $trimmed = { param($p) $p.TrimEnd('\') }
    $parts = @($parts | Where-Object { $entry = & $trimmed $_; -not ($Remove | Where-Object { (& $trimmed $_) -ieq $entry }) })
    if ($Add -and -not ($parts | Where-Object { (& $trimmed $_) -ieq (& $trimmed $Add) })) { $parts += $Add }
    $new = $parts -join ';'
    if ($new -ne $current) { [Environment]::SetEnvironmentVariable("Path", $new, "User") }
}

# ------------------------------------------------------------------ uninstall
if ($Uninstall) {
    Write-Host "AGEX AI CONTROL CENTER - uninstall" -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $marker)) { Stop-Install "No AGEX installation found in $InstallDir." }
    Set-Location $env:TEMP
    Stop-RunningAgex
    Set-UserPath -Remove @($InstallDir, (Join-Path $InstallDir "bin"))
    if (Test-Path -LiteralPath $shortcutPath) {
        $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
        if ($link.TargetPath -and $link.TargetPath.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $shortcutPath -Force; Write-Step "Removed the Start Menu shortcut" }
    }
    $run = Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "AGEX" -ErrorAction SilentlyContinue
    if ($run -and $run.AGEX -like "*$InstallDir*") { Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "AGEX"; Write-Step "Removed start-with-Windows" }
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
    Write-Step "Removed $InstallDir"
    if ($Purge) {
        if (Test-Path -LiteralPath $dataRoot) { Remove-Item -LiteralPath $dataRoot -Recurse -Force; Write-Step "Removed AGEX settings and history ($dataRoot)" }
    }
    else { Write-Step "Your AGEX settings and history were kept in $dataRoot (use -Purge to remove them)." }
    Write-Host "AGEX was removed. Your agents and projects were not touched." -ForegroundColor Green
    exit 0
}

# -------------------------------------------------------------------- install
Write-Host "AGEX AI CONTROL CENTER - installer" -ForegroundColor Cyan
if ([Environment]::OSVersion.Platform -ne "Win32NT") { Stop-Install "This installer is for Windows. On macOS or Linux use agex-install.sh." }
$os = [Environment]::OSVersion.Version
if ($os.Major -lt 10) { Stop-Install "Windows 10 or newer is required." }
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64" -or $env:PROCESSOR_ARCHITEW6432 -eq "ARM64") { "arm64" } elseif ([Environment]::Is64BitOperatingSystem) { "x64" } else { "" }
if (-not $arch) { Stop-Install "AGEX needs 64-bit Windows (x64 or ARM64)." }
Write-Step ("Windows {0}.{1}, {2}" -f $os.Major, $os.Build, $arch)

if ($WaitForExit -gt 0) {
    $waiting = Get-Process -Id $WaitForExit -ErrorAction SilentlyContinue
    if ($waiting) { Write-Step "Waiting for AGEX to close..."; [void]$waiting.WaitForExit(60000) }
}

$work = Join-Path $env:TEMP ("agex-install-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    if ($Package) {
        if (-not (Test-Path -LiteralPath $Package -PathType Leaf)) { Stop-Install "Package not found: $Package" }
        $zip = (Resolve-Path -LiteralPath $Package).Path
        $source = "local:" + (Split-Path -Leaf $zip)
    }
    else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        # "latest" includes pre-releases: /releases lists the newest first.
        $api = if ($Version -eq "latest") { "https://api.github.com/repos/$Repository/releases?per_page=10" } else { "https://api.github.com/repos/$Repository/releases/tags/v$($Version.TrimStart('v'))" }
        # Assign first: Windows PowerShell 5.1 emits a JSON array as one object, and piping the variable enumerates it.
        try { $releases = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "AGEX-Installer" } -TimeoutSec 30; $info = $releases | Where-Object { -not $_.draft } | Select-Object -First 1 }
        catch { Stop-Install "Could not find an AGEX release at github.com/$Repository ($($_.Exception.Message))." }
        if (-not $info) { Stop-Install "No AGEX release has been published at github.com/$Repository yet." }
        $wanted = "agex-$($info.tag_name.TrimStart('v'))-win-$arch.zip"
        $asset = @($info.assets | Where-Object { $_.name -eq $wanted })
        $sumAsset = @($info.assets | Where-Object { $_.name -eq "SHA256SUMS.txt" })
        if (-not $asset.Count) { Stop-Install "The release $($info.tag_name) has no package for Windows $arch ($wanted)." }
        if (-not $sumAsset.Count) { Stop-Install "The release $($info.tag_name) has no checksum file, so it will not be installed." }
        $zip = Join-Path $work $wanted
        $Checksums = Join-Path $work "SHA256SUMS.txt"
        Write-Step "Downloading AGEX $($info.tag_name) for $arch..."
        Invoke-WebRequest -Uri $asset[0].browser_download_url -OutFile $zip -UseBasicParsing
        Invoke-WebRequest -Uri $sumAsset[0].browser_download_url -OutFile $Checksums -UseBasicParsing
        $source = "github:$Repository@$($info.tag_name)"
    }

    # Verify before anything is extracted or run.
    $name = Split-Path -Leaf $zip
    $expected = $ExpectedSha256
    if (-not $expected) {
        if (-not $Checksums) { $Checksums = Join-Path (Split-Path -Parent $zip) "SHA256SUMS.txt" }
        if (-not (Test-Path -LiteralPath $Checksums -PathType Leaf)) { Stop-Install "SHA256SUMS.txt not found, so the package cannot be verified." }
        $expected = @(Get-Content -LiteralPath $Checksums | Where-Object { $_ -match ('\s\*?' + [regex]::Escape($name) + '$') } | ForEach-Object { ($_ -split '\s+')[0] }) | Select-Object -First 1
        if (-not $expected) { Stop-Install "$name is not listed in SHA256SUMS.txt." }
    }
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if ($actual -ne $expected.ToUpperInvariant()) { Stop-Install "Checksum mismatch for $name. The download may be damaged or altered; nothing was installed." }
    Write-Step "Checksum verified (SHA-256 $($actual.Substring(0, 16))...)"

    $staging = Join-Path $work "package"
    Expand-Archive -LiteralPath $zip -DestinationPath $staging -Force
    if (-not (Test-Path -LiteralPath (Join-Path $staging "agex.exe"))) { Stop-Install "The package does not contain agex.exe." }

    Stop-RunningAgex
    if (Test-Path -LiteralPath $InstallDir) {
        if (-not (Test-Path -LiteralPath $marker) -and @(Get-ChildItem -LiteralPath $InstallDir -Force).Count) { Stop-Install "$InstallDir exists and was not created by AGEX. Choose another folder with -InstallDir." }
        Get-ChildItem -LiteralPath $InstallDir -Force | Remove-Item -Recurse -Force
    }
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Copy-Item -Path (Join-Path $staging "*") -Destination $InstallDir -Recurse -Force
    $installed = ([IO.File]::ReadAllText((Join-Path $InstallDir "VERSION"))).Trim()
    @{ product = "AGEX"; version = $installed; repository = $Repository; source = $source; runtime = "win-$arch"; installed_at = (Get-Date).ToUniversalTime().ToString("o"); sha256 = $actual } | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
    Write-Step "Installed AGEX $installed to $InstallDir"

    if (-not $NoPath) {
        # Older AGEX builds put a "bin" folder on PATH and a launcher in WindowsApps; retire both.
        Set-UserPath -Add $InstallDir -Remove @((Join-Path $InstallDir "bin"))
        $legacy = Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\agex.cmd"
        if ((Test-Path -LiteralPath $legacy -PathType Leaf) -and ([IO.File]::ReadAllText($legacy) -match '(?i)-File "[^"]*\\(setup|agex)\.ps1"')) {
            $backup = Join-Path $dataRoot "backup\legacy-shims"
            New-Item -ItemType Directory -Path $backup -Force | Out-Null
            Copy-Item -LiteralPath $legacy -Destination $backup -Force
            Remove-Item -LiteralPath $legacy -Force
            Write-Step "Retired an old AGEX launcher (backup kept)"
        }
        $env:Path = $env:Path.TrimEnd(';') + ';' + $InstallDir
        Write-Step "The agex command is ready (open a new terminal to use it)"
    }
    $desktop = Join-Path $InstallDir "AgexDesktop.exe"
    if (-not $NoShortcut -and (Test-Path -LiteralPath $desktop)) {
        $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
        $link.TargetPath = $desktop; $link.WorkingDirectory = $InstallDir; $link.Description = "AGEX AI CONTROL CENTER"; $link.IconLocation = "$desktop,0"
        $link.Save()
        Write-Step "Start Menu shortcut: AGEX"
    }
    Write-Host "AGEX is installed." -ForegroundColor Green
    if (-not $NoLaunch -and (Test-Path -LiteralPath $desktop)) { Start-Process -FilePath $desktop }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
