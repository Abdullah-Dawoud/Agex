[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$Output = '',
    [switch]$SkipTests
)

# Builds the prebuilt Autodesk AI Bridge package that AGEX installs for users:
#   host/                         AutodeskAIBridge.Host.exe (self-contained, win-x64)
#   revit/2025|2026|2027/         Revit add-in for each Revit version
#   autocad/AutodeskAIBridge.bundle/  AutoCAD 2025 and later (ApplicationPlugins bundle)
#   bridge.json                   version and supported products
# The add-ins compile against Autodesk's API reference packages (AutoCAD.NET,
# Nice3point.Revit.Api.*) at build time only. No Autodesk assembly is shipped.

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
if (-not $Output) { $Output = Join-Path $repo 'artifacts' }
$work = Join-Path ([IO.Path]::GetTempPath()) ("agex-bridge-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$package = Join-Path $work 'package'
New-Item -ItemType Directory -Force -Path $package, $Output | Out-Null
$revitVersions = @('2025', '2026', '2027')

function Invoke-Dotnet([string[]]$arguments) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments[0]) failed with exit code $LASTEXITCODE" }
}

if (-not $SkipTests) {
    $env:DOTNET_ROLL_FORWARD = 'Major'
    Invoke-Dotnet @('run', '--project', (Join-Path $repo 'tests/AutodeskAIBridge.Tests'), '-c', 'Release')
    Remove-Item Env:DOTNET_ROLL_FORWARD
}

Invoke-Dotnet @('publish', (Join-Path $repo 'src/AutodeskAIBridge.Host/AutodeskAIBridge.Host.csproj'), '-c', 'Release', '-r', 'win-x64',
    '-p:PublishSelfContained=true', "-p:Version=$Version", '-o', (Join-Path $package 'host'))

foreach ($revit in $revitVersions) {
    Invoke-Dotnet @('build', (Join-Path $repo 'src/AutodeskAIBridge.Revit/AutodeskAIBridge.Revit.csproj'), '-c', 'Release',
        '-p:UseAutodeskReferencePackages=true', "-p:RevitVersion=$revit", "-p:Version=$Version", '-o', (Join-Path $work "revit-$revit"))
    $target = Join-Path $package "revit/$revit"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem (Join-Path $work "revit-$revit") -Filter 'AutodeskAIBridge.*.dll' | Copy-Item -Destination $target
}

Invoke-Dotnet @('build', (Join-Path $repo 'src/AutodeskAIBridge.AutoCAD/AutodeskAIBridge.AutoCAD.csproj'), '-c', 'Release',
    '-p:UseAutodeskReferencePackages=true', '-p:AutoCADSeries=2025', "-p:Version=$Version", '-o', (Join-Path $work 'autocad'))
$bundle = Join-Path $package 'autocad/AutodeskAIBridge.bundle'
New-Item -ItemType Directory -Force -Path (Join-Path $bundle 'Contents') | Out-Null
Get-ChildItem (Join-Path $work 'autocad') -Filter 'AutodeskAIBridge.*.dll' | Copy-Item -Destination (Join-Path $bundle 'Contents')
Copy-Item (Join-Path $repo 'src/AutodeskAIBridge.AutoCAD/AutodeskAIBridge.bundle/PackageContents.xml') $bundle

# Autodesk's own assemblies must never be in the package.
$blocked = @('RevitAPI.dll', 'RevitAPIUI.dll', 'AcCoreMgd.dll', 'AcDbMgd.dll', 'AcMgd.dll')
$found = Get-ChildItem $package -Recurse -File | Where-Object { $blocked -contains $_.Name }
if ($found) { throw "The package contains Autodesk assemblies: $($found.Name -join ', ')" }

[ordered]@{
    name = 'Autodesk AI Bridge'
    version = $Version
    protocol = '1'
    host = 'host/AutodeskAIBridge.Host.exe'
    revit = $revitVersions
    autocad = @{ minimum = '2025'; series = 'R25.0-R26.0' }
    license = 'MIT'
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $package 'bridge.json') -Encoding utf8
Copy-Item (Join-Path $repo 'LICENSE') $package

Get-ChildItem (Join-Path $package 'host') -Recurse -Include '*.pdb', '*.xml' | Remove-Item
$zip = Join-Path $Output 'autodesk-ai-bridge-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip }
# Release builds run in PowerShell 7, where ZipFile writes '/' separators. AGEX accepts both.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($package, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $work -Recurse -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"Built $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB, SHA-256 $hash)"
