$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$mtPath = Join-Path $root 'UI\Class\MT.cs'
$mt = Get-Content -LiteralPath $mtPath -Raw -Encoding Default

$manualConflictGuard = 'VAR.gsys_set.status != EM_SYS_STA.RUN || !PT_SET.bSpecialDualArmAvoidance'
$guardCount = ([regex]::Matches($mt, [regex]::Escape($manualConflictGuard))).Count
if ($guardCount -ne 2) {
    throw 'Both manual-positioning conflict branches must override special avoidance when the system is not running.'
}

foreach ($required in @(
    'ref COM.UDLoad2.ax_x, 0, ref COM.UDLoad2.ax_y, 0',
    'ref COM.UDLoad1.ax_x, 0, ref COM.UDLoad1.ax_y, 0')) {
    if ($mt -notlike "*$required*") {
        throw "Manual positioning must retain the historical opposite-arm logical-zero retreat: $required"
    }
}

$zupStart = $mt.IndexOf('public static EM_RES ZupMove(ref bool bquit, ref AXIS ax_x, double xpos, ref AXIS ax_y, double ypos, ref AXIS ax_z, double zpos, ref AXIS ax_r, double rpos')
$zupEnd = $mt.IndexOf('#endregion', $zupStart)
if ($zupStart -lt 0 -or $zupEnd -le $zupStart) {
    throw 'Unable to locate the shared up-Z positioning implementation.'
}

$zupBlock = $mt.Substring($zupStart, $zupEnd - $zupStart)
if ($zupBlock -match 'COM\.UDLoad[12]\.GoZero\(ref VAR\.gsys_set\.bquit, false\)') {
    throw 'Manual positioning must retain the historical logical-zero retreat instead of the standby-safe return.'
}

$moveStart = $mt.IndexOf('public static EM_RES Move(ref bool bquit, ref AXIS ax_x')
if ($moveStart -lt 0) {
    throw 'Unable to find the shared dual-arm move implementation.'
}

$moveBlock = $mt.Substring($moveStart)
if ($moveBlock -notlike '*if (dualArmId >= 0)*' -or
    $moveBlock -notlike '*AcquireDualArmXReservation*') {
    throw 'Every automatic UDL X move must reserve the shared collision envelope.'
}

Write-Host 'PASS: manual positioning returns the opposite arm to logical zero outside RUN while automatic moves reserve the shared collision envelope.'
