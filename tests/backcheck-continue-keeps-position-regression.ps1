$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot '..\UI\Class\UpDownLoad.cs'
$source = Get-Content -Raw $sourcePath

$continuePattern = '(?s)if\s*\(result\s*==\s*DialogResult\.OK\)\s*\{\s*VAR\.msg\.AddMsg\(Msg\.EM_MSGTYPE\.SYS,.*?\);\s*return result;\s*\}'
$continueMatch = [regex]::Match($source, $continuePattern)
if (-not $continueMatch.Success) {
    throw 'Continue running does not return before the safety-position recovery.'
}

$zupIndex = $source.IndexOf('recoveryRes = ud.Zup(ref recoveryQuit);')
if ($zupIndex -lt 0 -or $continueMatch.Index -gt $zupIndex) {
    throw 'Continue running still enters the Z-up and safety-position recovery.'
}

if ($source -notmatch 'StopBackCheckAndReturnHome\(ref bquit, null, "上相机回检异常"\)') {
    throw 'The back-check stop-to-safety branch is missing.'
}

Write-Host 'PASS: Back-check continue keeps position and stop still returns to safety.'
