# Builds AGEX release packages.
#
#   pwsh tools/build-release.ps1                          # packages for this OS
#   pwsh tools/build-release.ps1 -Runtime win-x64,osx-arm64,linux-x64
#
# Output in dist/:
#   agex-<version>-win-x64.zip / win-arm64.zip      app folder (desktop + agex command)
#   agex-<version>-osx-arm64.zip / osx-x64.zip      AGEX.app bundle
#   agex-<version>-linux-x64.tar.gz / linux-arm64   app folder
#   agex-install.ps1, agex-install.sh               one-command installers
#   skills-catalog.json                             curated skill catalog
#   SHA256SUMS.txt                                  checksums of everything above
#
# Packages built on Windows are complete but unsigned; macOS packages built on
# Windows carry no code signature. Official releases are built, signed and
# notarized by .github/workflows/release.yml on each platform's own runner.
[CmdletBinding()]
param(
    [string[]]$Runtime,
    [string]$Configuration = "Release",
    [string]$Output,
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $Output) { $Output = Join-Path $root "dist" }
$version = ([IO.File]::ReadAllText((Join-Path $root "VERSION"))).Trim()
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } elseif (Get-Command dotnet -ErrorAction SilentlyContinue) { "dotnet" } else { Join-Path $HOME ".dotnet/dotnet" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
# Avalonia's build tooling otherwise sends anonymous build telemetry.
$env:AVALONIA_TELEMETRY_OPTOUT = "1"

$Runtime = @($Runtime | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if (-not $Runtime) {
    $arch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq "Arm64") { "arm64" } else { "x64" }
    $os = if ($IsMacOS) { "osx" } elseif ($IsLinux) { "linux" } else { "win" }
    $Runtime = @("$os-$arch")
}

New-Item -ItemType Directory -Path $Output -Force | Out-Null
$Output = (Resolve-Path $Output).Path
$staging = Join-Path $Output "staging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

if (-not $SkipTests) {
    Write-Host "Running tests..."
    & $dotnet test (Join-Path $root "tests/Agex.Tests") -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed; no package was built." }
}

function Copy-Common([string]$Target) {
    foreach ($file in "LICENSE", "THIRD_PARTY_NOTICES.md", "VERSION") { Copy-Item (Join-Path $root $file) $Target }
    $install = New-Item -ItemType Directory -Path (Join-Path $Target "install") -Force
    Copy-Item (Join-Path $root "install/agex-install.ps1") $install
    Copy-Item (Join-Path $root "install/agex-install.sh") $install
}

function Publish-App([string]$Rid, [string]$Target) {
    foreach ($project in "src/Agex.Desktop", "src/Agex.Cli") {
        & $dotnet publish (Join-Path $root $project) -c $Configuration -r $Rid --self-contained true -o $Target --nologo -v quiet -p:DebugType=none -p:GenerateDocumentationFile=false
        if ($LASTEXITCODE -ne 0) { throw "Publishing $project for $Rid failed." }
    }
}

$artifacts = [System.Collections.Generic.List[string]]::new()
foreach ($rid in $Runtime) {
    Write-Host "Building $rid..."
    $name = "agex-$version-$rid"
    if ($rid.StartsWith("osx")) {
        $bundle = Join-Path $staging "$rid/AGEX.app"
        $macos = Join-Path $bundle "Contents/MacOS"
        $resources = New-Item -ItemType Directory -Path (Join-Path $bundle "Contents/Resources") -Force
        Publish-App $rid $macos
        Copy-Common $macos
        Copy-Item (Join-Path $root "src/Agex.Desktop/Assets/agex.icns") $resources
        $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>AGEX</string>
  <key>CFBundleDisplayName</key><string>AGEX</string>
  <key>CFBundleIdentifier</key><string>com.agex.desktop</string>
  <key>CFBundleExecutable</key><string>AgexDesktop</string>
  <key>CFBundleIconFile</key><string>agex.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Copyright (c) AGEX contributors. MIT License.</string>
</dict>
</plist>
"@
        [IO.File]::WriteAllText((Join-Path $bundle "Contents/Info.plist"), $plist, [Text.UTF8Encoding]::new($false))
        $archive = Join-Path $Output "$name.zip"
        if (Test-Path $archive) { Remove-Item $archive }
        if ($IsMacOS) {
            # Apple Silicon only runs signed code: ad-hoc at least, Developer ID when configured.
            & sh (Join-Path $root "tools/sign-macos.sh") app $bundle
            if ($LASTEXITCODE -ne 0) { throw "Signing failed for $rid." }
            & ditto -c -k --sequesterRsrc --keepParent $bundle $archive
            $dmg = Join-Path $Output "$name.dmg"
            if (Test-Path $dmg) { Remove-Item $dmg }
            & hdiutil create -volname "AGEX" -srcfolder (Split-Path $bundle) -ov -format UDZO $dmg | Out-Null
            & sh (Join-Path $root "tools/sign-macos.sh") dmg $dmg
            $artifacts.Add($dmg)
        }
        else {
            # Unsigned: Apple Silicon will not run it until it is signed on a Mac (see docs/DEVELOPMENT.md).
            Compress-Archive -Path $bundle -DestinationPath $archive
        }
    }
    else {
        $app = Join-Path $staging "$rid/agex"
        Publish-App $rid $app
        Copy-Common $app
        if ($rid.StartsWith("linux")) {
            Copy-Item (Join-Path $root "src/Agex.Desktop/Assets/agex.png") $app
            $archive = Join-Path $Output "$name.tar.gz"
            if (Test-Path $archive) { Remove-Item $archive }
            & tar -czf $archive -C (Split-Path $app) (Split-Path $app -Leaf)
            if ($LASTEXITCODE -ne 0) { throw "tar failed for $rid." }
        }
        else {
            & (Join-Path $root "tools/sign-windows.ps1") -Folder $app
            $archive = Join-Path $Output "$name.zip"
            if (Test-Path $archive) { Remove-Item $archive }
            Compress-Archive -Path (Join-Path $app "*") -DestinationPath $archive
        }
    }
    $artifacts.Add($archive)
}

foreach ($file in "install/agex-install.ps1", "install/agex-install.sh", "src/Agex.Core/Skills/skills-catalog.json") {
    $target = Join-Path $Output (Split-Path $file -Leaf)
    Copy-Item (Join-Path $root $file) $target -Force
    $artifacts.Add($target)
}

# Checksums for every artifact in dist/ (existing packages from other runs included).
$lines = Get-ChildItem $Output -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" -and $_.Name -ne "SHA256SUMS.txt.sig" } | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllText((Join-Path $Output "SHA256SUMS.txt"), ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Remove-Item $staging -Recurse -Force
Write-Host "Packages in $Output"
Get-ChildItem $Output -File | ForEach-Object { "  {0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
