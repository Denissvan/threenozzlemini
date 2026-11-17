$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$mtPath = Join-Path $root 'UI\Class\MT.cs'
$upDownLoadPath = Join-Path $root 'UI\Class\UpDownLoad.cs'
$mt = Get-Content -LiteralPath $mtPath -Raw -Encoding Default
$upDownLoad = Get-Content -LiteralPath $upDownLoadPath -Raw -Encoding Default

foreach ($required in @(
    'private const int DualArmAvoidanceWaitTimeoutMs = 30000;',
    'Monitor.Wait(dualArmXReservationLock',
    'Monitor.PulseAll(dualArmXReservationLock)')) {
    if ($mt -notlike "*$required*") {
        throw "Special dual-arm avoidance must provide bounded wait-and-wake behavior: $required"
    }
}

$acquireStart = $mt.IndexOf('private static EM_RES AcquireDualArmXReservation')
$releaseStart = $mt.IndexOf('private static void ReleaseDualArmXReservation', $acquireStart)
if ($acquireStart -lt 0 -or $releaseStart -le $acquireStart) {
    throw 'Unable to locate the dual-arm reservation acquire/release pair.'
}

$acquireBlock = $mt.Substring($acquireStart, $releaseStart - $acquireStart)
if ($acquireBlock -match 'return EM_RES\.ABORT') {
    throw 'A normal dual-arm avoidance conflict must wait, not return the generic ABORT result.'
}

if ($acquireBlock -notmatch 'return EM_RES\.TIMEOUT') {
    throw 'A dual-arm avoidance wait must become a fault only after its bounded timeout.'
}

if ($mt -match 'dualArmAvoidanceYield|ConsumeDualArmAvoidanceYield') {
    throw 'The obsolete ABORT-based special-avoidance yield signal must be removed.'
}

if ($upDownLoad -match 'retryPickTrayAfterSafeYield|res == EM_RES\.ABORT && PT_SET\.bSpecialDualArmAvoidance') {
    throw 'A normal special-avoidance conflict must not force a return to the safety position.'
}

Write-Host 'PASS: special dual-arm avoidance waits for release, resumes without ABORT, and only times out as a real fault.'
