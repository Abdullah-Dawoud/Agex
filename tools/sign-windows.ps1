# Authenticode-signs the AGEX executables in a staged Windows app folder.
#
#   tools/sign-windows.ps1 -Folder <staged app folder>
#
# Needs WINDOWS_CERT_PFX_BASE64 (the code-signing certificate as base64 PFX) and
# WINDOWS_CERT_PASSWORD. Without them nothing is signed and the script says so;
# unsigned builds work but Windows SmartScreen warns on first start.
param([Parameter(Mandatory)][string]$Folder)
$ErrorActionPreference = "Stop"
if (-not $env:WINDOWS_CERT_PFX_BASE64) { Write-Host "Code signing skipped (no certificate configured)."; exit 0 }
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw "signtool.exe not found (install the Windows SDK)." }
$pfx = Join-Path $env:RUNNER_TEMP "agex-signing.pfx"
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:WINDOWS_CERT_PFX_BASE64))
try {
    foreach ($exe in "AgexDesktop.exe", "agex.exe") {
        & $signtool.FullName sign /fd SHA256 /f $pfx /p $env:WINDOWS_CERT_PASSWORD /tr http://timestamp.digicert.com /td SHA256 (Join-Path $Folder $exe)
        if ($LASTEXITCODE -ne 0) { throw "Signing $exe failed." }
    }
}
finally { Remove-Item $pfx -Force -ErrorAction SilentlyContinue }
