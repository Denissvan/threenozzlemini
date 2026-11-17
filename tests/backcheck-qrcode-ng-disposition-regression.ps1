$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot '..\UI\Class\UpDownLoad.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8

$start = $source.IndexOf('if (upcam.curTask.ResData.BarCode != null && upcam.curTask.ResData.BarCode.Length > 1')
$end = $source.IndexOf('bAlm = false;', $start)
if ($start -lt 0 -or $end -lt $start) {
    throw 'The pre-unload barcode mismatch handling block was not found.'
}

$barcodeBlock = $source.Substring($start, $end - $start)
if ($barcodeBlock -notmatch 'if\s*\(md\.res\s*==\s*0\)\s*md\.res\s*=\s*2222;') {
    throw 'A barcode mismatch must mark an originally OK module as NG code 2222.'
}

if ($barcodeBlock -notmatch 'st_warn\.abort_txt\s*=\s*"\u4e0d\u7528";') {
    throw 'The barcode mismatch dialog still exposes a second confirmation path.'
}

if ($barcodeBlock -notmatch '(?s)DisplayWarningWithDualArmRecoveryGate\s*\(.*?false,\s*out recoveryRes\)') {
    throw 'The barcode mismatch dialog must use the position-preserving recovery-gate path.'
}

$helperStart = $source.IndexOf('private DialogResult DisplayWarningWithDualArmRecoveryGate(')
$helperEnd = $source.IndexOf('private DialogResult DisplayWarningWithDualArmPause(', $helperStart)
if ($helperStart -lt 0 -or $helperEnd -lt $helperStart) {
    throw 'The dual-arm recovery-gate helper was not found.'
}

$helper = $source.Substring($helperStart, $helperEnd - $helperStart)
$continueMatch = [regex]::Match($helper, '(?s)if\s*\(result\s*==\s*DialogResult\.OK\)\s*\{.*?return result;\s*\}')
$zupIndex = $helper.IndexOf('recoveryRes = ud.Zup(ref recoveryQuit);')
if (-not $continueMatch.Success -or $zupIndex -lt 0 -or $continueMatch.Index -gt $zupIndex) {
    throw 'Barcode mismatch confirmation must return before the two-arm safety recovery.'
}

if ($source -notmatch 'if\s*\(traybox\.IsNgTray\s*&&\s*xt\.XtMd\.res\s*<\s*1\)\s*continue;') {
    throw 'NG modules must remain routed to the NG tray during tray placement.'
}

Write-Host 'PASS: Barcode mismatch confirmation preserves position and continues normal NG unloading.'
