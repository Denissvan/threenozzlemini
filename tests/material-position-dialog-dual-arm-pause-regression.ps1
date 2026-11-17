$ErrorActionPreference = 'Stop'

$upDownLoadPath = Join-Path $PSScriptRoot '..\UI\Class\UpDownLoad.cs'
$mtPath = Join-Path $PSScriptRoot '..\UI\Class\MT.cs'
$upDownLoad = Get-Content -LiteralPath $upDownLoadPath -Raw -Encoding UTF8
$mt = Get-Content -LiteralPath $mtPath -Raw -Encoding UTF8

if ($upDownLoad -notmatch 'private DialogResult DisplayWarningWithDualArmPause\(') {
    throw 'Material-position alarms must use a dedicated dual-arm pause wrapper.'
}

 $materialPauseCalls = [regex]::Matches(
    $upDownLoad,
    'DisplayWarningWithDualArmPause\(\s*fr_warn,\s*warn,\s*ERR_ALM\.EmErrItem\.MaterialPosErr,\s*".*",\s*out pauseRes\)')
if ($materialPauseCalls.Count -ne 2) {
    throw 'Both material-position dialog branches must enter the dual-arm pause wrapper.'
}

$materialCheckStart = $upDownLoad.IndexOf('private EM_RES CheckWsPutOkShow')
$materialCheckEnd = $upDownLoad.IndexOf('/// <summary>', $materialCheckStart)
$materialCheck = $upDownLoad.Substring($materialCheckStart, $materialCheckEnd - $materialCheckStart)
if ($materialCheck -match 'DialogResult logres = MT\.Display_frwarn') {
    throw 'Material-position dialog must not bypass the dual-arm pause wrapper.'
}

if ($mt -notmatch 'public static bool IsDualArmRecoveryGateActive\(\)') {
    throw 'The scheduler needs a thread-safe recovery-gate activity query.'
}

$waitStart = $upDownLoad.IndexOf('if (Proc != EM_STA.UPDOWNLOADEND')
$waitEnd = $upDownLoad.IndexOf('if (msg_outcnt > 600)', $waitStart)
$waitBlock = $upDownLoad.Substring($waitStart, $waitEnd - $waitStart)
$pauseIndex = $waitBlock.IndexOf('MT.IsDualArmRecoveryGateActive()')
$counterIndex = $waitBlock.IndexOf('msg_outcnt++')
if ($pauseIndex -lt 0 -or $counterIndex -lt 0 -or $pauseIndex -ge $counterIndex) {
    throw 'The module-2 wait counter must be frozen while a dual-arm recovery gate is active.'
}

Write-Host 'PASS: material-position dialogs pause both arms and freeze the paired-arm wait timeout.'
