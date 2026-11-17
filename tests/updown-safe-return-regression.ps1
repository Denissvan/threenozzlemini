$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$upDownLoadPath = Join-Path $repoRoot 'UI\Class\UpDownLoad.cs'
$runFlowPath = Join-Path $repoRoot 'UI\Class\myfunc.cs'
$upDownLoad = Get-Content -LiteralPath $upDownLoadPath -Raw
$runFlow = Get-Content -LiteralPath $runFlowPath -Raw

function Get-Section
{
    param(
        [string]$source,
        [string]$start,
        [string]$end
    )

    $startIndex = $source.IndexOf($start)
    if ($startIndex -lt 0) { throw "Missing section start: $start" }
    $endIndex = $source.IndexOf($end, $startIndex + $start.Length)
    if ($endIndex -le $startIndex) { throw "Missing section end: $end" }
    return $source.Substring($startIndex, $endIndex - $startIndex)
}

$goZero = Get-Section $upDownLoad 'public EM_RES GoZero' '#endregion'
if ($goZero -notmatch 'bool\s+IsZero\s*=\s*false') { throw 'Automatic GoZero must default to the standby safe pose, not logical zero.' }
if ($goZero -notmatch 'IsZero\s*==\s*true\s*\?\s*0\s*:\s*list_xt\[1\]\.st_cap_pos\.y') { throw 'GoZero(false) must retain the established capture-Y safe pose.' }

$stopBackCheck = Get-Section $upDownLoad 'private EM_RES StopBackCheckAndReturnHome' 'private DialogResult DisplayWarningWithDualArmRecoveryGate'
if ($stopBackCheck -notmatch 'GoZero\(ref\s+homeQuit\s*,\s*false\)') { throw 'Alarm Stop must explicitly move to the standby safe pose.' }
if ($stopBackCheck -match 'GoZero\(ref\s+homeQuit\s*\)') { throw 'Alarm Stop must not rely on the logical-zero default.' }

$recoveryGate = Get-Section $upDownLoad 'private DialogResult DisplayWarningWithDualArmRecoveryGate' 'private DialogResult DisplayWarningWithDualArmPause'
$cancelIndex = $recoveryGate.IndexOf('if (result == DialogResult.Cancel)')
$motionIndex = $recoveryGate.IndexOf('recoveryRes = ud.Zup(ref recoveryQuit);')
$cancelLeavesBeforeMotion = ($cancelIndex -ge 0) -and ($motionIndex -gt $cancelIndex)
if (-not $cancelLeavesBeforeMotion) { throw 'Alarm Stop must leave the recovery gate before any additional arm motion.' }

$runUdTask = Get-Section $upDownLoad 'public void UdLoad()' '#endregion'
if ($runUdTask -notmatch 'MT\.BeQuitEn\(true\);') { throw 'Ordinary stop/error exits must keep the global cleanup token available.' }
if ($runUdTask -notmatch 'if\s*\(res\s*==\s*EM_RES\.MOVE_ERR\s*\|\|\s*res\s*==\s*EM_RES\.MOVE_TIMEOUT\s*\|\|\s*res\s*==\s*EM_RES\.MOVE_PARA_ERR\s*\)\s*\{\s*MT\.BeQuitEn\(true,\s*true\);') { throw 'Only hard axis-motion failures may cancel automatic safety movement globally.' }

$bareGoZeroCalls = [regex]::Matches($upDownLoad, 'GoZero\(ref\s+[^,\)\r\n]+\)')
if ($bareGoZeroCalls.Count -ne 0) { throw 'Every GoZero call must explicitly state whether it targets the safe pose or logical zero.' }
if ($upDownLoad -notmatch 'EM_RES\s+homeRes\s*=\s*GoZero\(ref\s+bquit\s*,\s*true\)') { throw 'The explicit operator Home-confirm action must remain a logical-zero action.' }
if ($runFlow -notmatch 'GoZero\(ref\s+VAR\.gsys_set\.bquit\s*,\s*false\)') { throw 'The total run cleanup must retain the established safe-pose return.' }

$jigScan = Get-Section $upDownLoad 'public EM_RES JigSan' 'private void RestoreRunSignalAfterWarning'
if ($jigScan -notmatch 'GoZero\(ref\s+bquit\s*,\s*false\)') { throw 'The automatic jig barcode completion path must return to the standby safe pose.' }
if ($jigScan -match 'MT\.ZupMove\(ref\s+bquit\s*,\s*ref\s+ax_x\s*,\s*0\s*,\s*ref\s+ax_y\s*,\s*0\s*\)') { throw 'The automatic jig barcode completion path must not return directly to logical zero.' }

Write-Host 'PASS: up/down automatic exit returns to the standby safe pose; logical zero is explicit.'
