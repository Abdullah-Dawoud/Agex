$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/agex-acceptance-fixture.ps1"
function Assert-AgeXFixture($condition, $message) { if (-not $condition) { throw $message } }
$root = Join-Path $env:TEMP ('agex-fixture-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
try {
    $windowsHost = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $verificationHost = Resolve-AgeXPowerShellHost -PwshPath (Join-Path $root 'missing-pwsh.exe') -WindowsPowerShellPath $windowsHost -CurrentHostPath ''
    Assert-AgeXFixture ($verificationHost.Name -eq 'powershell.exe' -and $verificationHost.Path -eq (Resolve-Path -LiteralPath $windowsHost).Path) 'Windows PowerShell fallback failed'
    $verify = New-AgeXAcceptanceVerifyFixture -Project $root
    Assert-AgeXFixture (Test-AgeXAcceptanceFixtureEncoding -Path $verify) 'Generated fixture encoding invalid'
    $bytes = [IO.File]::ReadAllBytes($verify)
    Assert-AgeXFixture (-not @($bytes | Where-Object { $_ -gt 0x7f }).Count) 'Fixture contains raw Unicode source bytes'
    $implementation = @'
function Convert-Names {
    param([string[]]$Names)
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $Names) { if (-not [string]::IsNullOrWhiteSpace($name)) { $value = $name.Trim(); if ($seen.Add($value)) { [void]$result.Add($value) } } }
    ,$result.ToArray()
}
'@
    [IO.File]::WriteAllText((Join-Path $root 'Convert-Names.ps1'), $implementation, [Text.UTF8Encoding]::new($false))
    $output = @(& $verificationHost.Path -NoProfile -ExecutionPolicy Bypass -File $verify)
    Assert-AgeXFixture ($LASTEXITCODE -eq 0 -and $output -contains 'VERIFICATION: PASS') 'Portable Unicode verification failed'
    $invocation = Get-AgeXPowerShellInvocation -HostPath 'C:\Program Files\PowerShell\7\pwsh.exe' -ScriptPath 'C:\fixture path\verify.ps1'
    Assert-AgeXFixture ($invocation -eq "& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\fixture path\verify.ps1'") 'PowerShell invocation must preserve paths with spaces'
    $missingPwsh = $false
    try { Resolve-AgeXPowerShellHost -RequirePowerShell7 -PwshPath (Join-Path $root 'missing-pwsh.exe') -WindowsPowerShellPath $windowsHost -CurrentHostPath '' | Out-Null } catch { $missingPwsh = $_.Exception.Message -match 'TOOL_UNAVAILABLE' }
    Assert-AgeXFixture $missingPwsh 'Unavailable required pwsh must fail fast'
    Assert-AgeXFixture ((Get-AgeXAcceptanceDeadlineSeconds) -eq 1200) 'Acceptance deadline budget incorrect'
} finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
'AGEX ACCEPTANCE FIXTURE TESTS: PASS'
