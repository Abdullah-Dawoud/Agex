# Builds AGEX.exe (WPF, .NET Framework 4.8) with the C# compiler that ships
# with Windows. No SDK, Visual Studio or NuGet is needed.
[CmdletBinding()]
param([string]$Output)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $root "AGEX.exe" }
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path (Join-Path $framework "csc.exe"))) { $framework = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319" }
$csc = Join-Path $framework "csc.exe"
if (-not (Test-Path $csc)) { throw ".NET Framework 4.x C# compiler not found at $csc" }
$wpf = Join-Path $framework "WPF"
$references = @(
    (Join-Path $wpf "PresentationFramework.dll"), (Join-Path $wpf "PresentationCore.dll"), (Join-Path $wpf "WindowsBase.dll"),
    (Join-Path $framework "System.Xaml.dll"), (Join-Path $framework "System.Web.Extensions.dll"), (Join-Path $framework "System.Windows.Forms.dll"),
    (Join-Path $framework "System.dll"), (Join-Path $framework "System.Core.dll"), (Join-Path $framework "System.Xml.dll")
)
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root "desktop") -Filter "*.cs" | ForEach-Object FullName)
$version = ([IO.File]::ReadAllText((Join-Path $root "VERSION"))).Trim()
$info = Join-Path $env:TEMP ("agex-assemblyinfo-" + [guid]::NewGuid().ToString("N") + ".cs")
[IO.File]::WriteAllText($info, @"
[assembly: System.Reflection.AssemblyTitle("AGEX AI CONTROL CENTER")]
[assembly: System.Reflection.AssemblyProduct("AGEX")]
[assembly: System.Reflection.AssemblyVersion("$($version -replace '[^0-9.].*$','').0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
"@)
$arguments = @("/nologo", "/target:winexe", "/optimize+", "/platform:anycpu", "/out:$Output") + @($references | ForEach-Object { "/reference:$_" })
$icon = Join-Path $root "desktop\agex.ico"
if (Test-Path $icon) { $arguments += "/win32icon:$icon" }
$arguments += $sources + @($info)
& $csc @arguments
$code = $LASTEXITCODE
Remove-Item -LiteralPath $info -Force -ErrorAction SilentlyContinue
if ($code -ne 0) { throw "Desktop build failed (csc exit code $code)." }
Write-Output "Built $Output"
