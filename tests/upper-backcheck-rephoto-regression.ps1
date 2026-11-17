$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot '..\UI\Class\UpDownLoad.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8

if ($source -notmatch 'ERR_ALM\.EmErrItem\.UpDownLoadAbnormal,\s*".*",\s*true,\s*out recoveryRes') {
    throw 'The upper-camera backcheck retry must explicitly retain its current position.'
}

if (-not $source.Contains('if (keepPositionOnRetry && result == DialogResult.Abort)')) {
    throw 'The recovery gate must preserve position when the retry-photo button is selected.'
}

$enterGate = $source.IndexOf('recoveryRes = MT.EnterDualArmRecoveryGate')
$keepPosition = $source.IndexOf('if (keepPositionOnRetry && result == DialogResult.Abort)')
$zup = $source.IndexOf('foreach (UpDownLoad ud in COM.List_UDLoad)', $keepPosition)

if ($enterGate -lt 0 -or $keepPosition -lt 0 -or $zup -lt 0 -or $enterGate -ge $keepPosition -or $keepPosition -ge $zup) {
    throw 'The retry-photo branch must keep the recovery gate but skip both-arm safe return before retaking the photo.'
}

Write-Host 'PASS: upper-camera retry-photo keeps its original retake path without both-arm safe return.'
