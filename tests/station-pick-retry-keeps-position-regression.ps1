$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot '..\UI\Class\UpDownLoad.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8

$gateCalls = [regex]::Matches($source, 'DisplayWarningWithDualArmRecoveryGate\(')
if ($gateCalls.Count -lt 4) {
    throw 'Expected the recovery-gate definition and its three warning call sites.'
}

$gateIndex = $gateCalls[$gateCalls.Count - 1].Index
$retrySegment = $source.Substring($gateIndex, [Math]::Min(2600, $source.Length - $gateIndex))
if ($retrySegment -notmatch 'UpDownLoadAbnormal,\s*"[^"]+",\s*true,\s*out recoveryRes') {
    throw 'Station pick retry must explicitly request position retention from the recovery gate.'
}

if ($retrySegment -notmatch 'else if\s*\(DialogResult\.Abort\s*==\s*logres\)\s*\{\s*goto PICK;') {
    throw 'Station pick retry must keep the recovery gate, retain its current position, and jump directly back to PICK.'
}

Write-Host 'PASS: station pick retry keeps position and retries directly.'
