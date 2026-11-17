$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$source = Get-Content -Raw -Encoding UTF8 (Join-Path $repo 'UI/Class/TrayBox.cs')

function Require([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}

Require ($source -match 'res\s*=\s*!Demo\s*\?\s*cy_hd\.SetOn\(ref bquit, 2000\)\s*:\s*cy_hd\.SetOn\(\);') 'TrayOut must keep the clamp SetOn result.'
Require ($source -match 'clampFailed\s*=\s*true;\s*MT\.BeQuitEn\(true, true\);\s*return res;') 'TrayOut must stop the run when clamp confirmation fails.'
Require ($source -match 'if \(!clampFailed\)\s*\{\s*bool bq = false;\s*res = MT\.Move\(ref bq, ref ax_x, fd_safe_x\);') 'TrayOut must not move X in finally after clamp confirmation fails.'
Require ($source -match 'res\s*=\s*cy_hd\.SetOff\(ref bquit, 2000\);\s*if \(res != EM_RES\.OK\)\s*\{\s*MT\.BeQuitEn\(true, true\);\s*return res;') 'TrayIn must stop the run when clamp retract confirmation fails.'

Write-Output 'PASS: tray clamp confirmation failures stop the tray action before subsequent X/Z motion.'
