function Resolve-AgeXPowerShellHost {
    [CmdletBinding()]
    param(
        [switch]$RequirePowerShell7,
        [string]$PwshPath,
        [string]$WindowsPowerShellPath,
        [string]$CurrentHostPath
    )
    if ([string]::IsNullOrWhiteSpace($PwshPath)) {
        $pwsh = Get-Command pwsh.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($pwsh) { $PwshPath = [string]$pwsh.Source }
    }
    if ([string]::IsNullOrWhiteSpace($WindowsPowerShellPath)) {
        $WindowsPowerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    }
    if ([string]::IsNullOrWhiteSpace($CurrentHostPath)) {
        try { $CurrentHostPath = (Get-Process -Id $PID -ErrorAction Stop).Path } catch { }
    }
    if ($RequirePowerShell7) {
        if ($PwshPath -and (Test-Path -LiteralPath $PwshPath -PathType Leaf)) { return [pscustomobject]@{ Path=(Resolve-Path -LiteralPath $PwshPath).Path; Name='pwsh'; PowerShell7=$true } }
        throw 'TOOL_UNAVAILABLE: PowerShell 7 is required but pwsh.exe is unavailable.'
    }
    if ($WindowsPowerShellPath -and (Test-Path -LiteralPath $WindowsPowerShellPath -PathType Leaf)) { return [pscustomobject]@{ Path=(Resolve-Path -LiteralPath $WindowsPowerShellPath).Path; Name='powershell.exe'; PowerShell7=$false } }
    if ($PwshPath -and (Test-Path -LiteralPath $PwshPath -PathType Leaf)) { return [pscustomobject]@{ Path=(Resolve-Path -LiteralPath $PwshPath).Path; Name='pwsh'; PowerShell7=$true } }
    if ($CurrentHostPath -and (Test-Path -LiteralPath $CurrentHostPath -PathType Leaf)) { return [pscustomobject]@{ Path=(Resolve-Path -LiteralPath $CurrentHostPath).Path; Name='current-host'; PowerShell7=($PSVersionTable.PSVersion.Major -ge 7) } }
    throw 'TOOL_UNAVAILABLE: No compatible PowerShell host is available.'
}

function Get-AgeXAcceptanceDeadlineSeconds {
    param([ValidateRange(1, 3600)][int]$WorkerDispatchTimeoutSeconds = 780, [ValidateRange(1, 3600)][int]$ReconciliationGraceSeconds = 420)
    $WorkerDispatchTimeoutSeconds + $ReconciliationGraceSeconds
}

function Get-AgeXPowerShellInvocation {
    param([Parameter(Mandatory)][string]$HostPath, [Parameter(Mandatory)][string]$ScriptPath)
    "& '{0}' -NoProfile -ExecutionPolicy Bypass -File '{1}'" -f $HostPath.Replace("'", "''"), $ScriptPath.Replace("'", "''")
}

function Test-AgeXAcceptanceFixtureEncoding {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if (-not $bytes.Length -or @($bytes | Where-Object { $_ -gt 0x7f }).Count) { return $false }
    $source = [Text.Encoding]::ASCII.GetString($bytes)
    $source.Contains('AGEX_ACCEPTANCE_VERIFY_V1') -and $source.Contains('0x00E9') -and $source.Contains('VERIFICATION: PASS')
}

function New-AgeXAcceptanceVerifyFixture {
    param([Parameter(Mandatory)][string]$Project)
    $path = Join-Path $Project 'verify.ps1'
    $source = @'
# AGEX_ACCEPTANCE_VERIFY_V1
$ErrorActionPreference = 'Stop'
$implementation = Join-Path $PSScriptRoot 'Convert-Names.ps1'
if (-not (Test-Path -LiteralPath $implementation -PathType Leaf)) { throw 'IMPLEMENTATION_MISSING: Convert-Names.ps1' }
. $implementation
$unicode = ([char]0x004A).ToString() + ([char]0x006F).ToString() + ([char]0x0073).ToString() + ([char]0x00E9).ToString()
$unicodeDuplicate = $unicode.ToUpperInvariant()
$input = @('  Alice  ', 'bob', 'ALICE', ' ', 'BOB', $unicode, $unicodeDuplicate)
$expected = @('Alice', 'bob', $unicode)
if (Get-Command Convert-Names -CommandType Function -ErrorAction SilentlyContinue) { $reported = @(Convert-Names -Names $input) } else { $reported = @(& $implementation -Names $input) }
$actualValues = [System.Collections.Generic.List[string]]::new()
foreach ($item in $reported) { if ($item -is [array]) { foreach ($value in $item) { [void]$actualValues.Add([string]$value) } } else { [void]$actualValues.Add([string]$item) } }
$actual = $actualValues.ToArray()
if ($actual.Count -ne $expected.Count) { throw ('VERIFICATION_FAILED: expected {0} values, got {1}' -f $expected.Count, $actual.Count) }
for ($index = 0; $index -lt $expected.Count; $index++) { if ($actual[$index] -cne $expected[$index]) { throw ('VERIFICATION_FAILED: output mismatch at {0}' -f $index) } }
if ([int][char]$unicode[3] -ne 0x00E9) { throw 'FIXTURE_ENCODING_INVALID: Unicode code point mismatch.' }
Write-Output 'VERIFICATION: PASS'
'@
    [IO.File]::WriteAllText($path, $source, [Text.UTF8Encoding]::new($false))
    if (-not (Test-AgeXAcceptanceFixtureEncoding -Path $path)) { throw 'FIXTURE_ENCODING_INVALID: Generated verify.ps1 is not ASCII-safe code-point fixture.' }
    $path
}

function Test-AgeXAcceptanceReadmeFixture {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 4 -or $bytes[0] -ne 0xef -or $bytes[1] -ne 0xbb -or $bytes[2] -ne 0xbf) { return $false }
    $body = $bytes[3..($bytes.Length - 1)]
    if (@($body | Where-Object { $_ -gt 0x7f }).Count) { return $false }
    $source = [Text.Encoding]::ASCII.GetString($body)
    $source.Contains('AGEX_ACCEPTANCE_README_V1') -and $source.Contains('U+00E9') -and $source.Contains('Error Expectations')
}

function New-AgeXAcceptanceReadmeFixture {
    param([Parameter(Mandatory)][string]$Project)
    $path = Join-Path $Project 'README.md'
    $source = @'
# Convert-Names

<!-- AGEX_ACCEPTANCE_README_V1 -->

Dependency-free PowerShell utility that trims names, discards blanks, and keeps
first spelling and order while deduplicating case-insensitively.

## Usage

```powershell
.\Convert-Names.ps1 -Names '  Alice  ', 'bob', 'ALICE', ' ', 'BOB'
```

## Unicode behavior

`verify.ps1` tests the Unicode case-variant path with code point U+00E9 and its
invariant uppercase variant. This documentation intentionally uses ASCII-safe
examples so Windows PowerShell readers cannot misdecode a raw UTF-8 example.

## Error Expectations

Blank values are ignored. The `Names` parameter accepts a string array.
'@
    [IO.File]::WriteAllText($path, $source, [Text.UTF8Encoding]::new($true))
    if (-not (Test-AgeXAcceptanceReadmeFixture -Path $path)) { throw 'README_ENCODING_INVALID: Generated README.md failed UTF-8 BOM and content validation.' }
    $path
}
