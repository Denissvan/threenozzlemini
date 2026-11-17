param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

$upDownLoad = Get-Content -LiteralPath (Join-Path $RepoRoot 'UI\Class\UpDownLoad.cs') -Raw -Encoding UTF8

Assert-Condition ($upDownLoad.Contains('private bool HasPendingTrayPlacement()')) 'Held OK or NG material must be recognized as pending tray placement.'
Assert-Condition ($upDownLoad.Contains('private bool IsTrayPlacementWaiting()')) 'Tray-change waits must be distinguishable from an ordinary module-2 wait.'
Assert-Condition ($upDownLoad.Contains('Proc = EM_STA.PLACETRAY;')) 'A restarted UDL flow must return held discharge material to PLACETRAY.'

$placeStart = $upDownLoad.IndexOf('public EM_RES PlaceMdToTray(ref bool bquit, WS ws)')
Assert-Condition ($placeStart -ge 0) 'PlaceMdToTray must exist.'
$placeBlock = $upDownLoad.Substring($placeStart, [Math]::Min(6000, $upDownLoad.Length - $placeStart))
Assert-Condition ($placeBlock.Contains('料盘放料被中断，保留吸头持料状态')) 'Interrupted tray placement must be recorded explicitly.'
Assert-Condition ($placeBlock.Contains('return EM_RES.QUIT;')) 'Interrupted tray placement must not be reported as successful.'

$pickStart = $upDownLoad.IndexOf('if (Proc == EM_STA.PICKTRAY && !bquit)')
Assert-Condition ($pickStart -ge 0) 'PICKTRAY flow must exist.'
$pickBlock = $upDownLoad.Substring($pickStart, [Math]::Min(5000, $upDownLoad.Length - $pickStart))
Assert-Condition ($pickBlock.Contains('HasPendingTrayPlacement()')) 'PICKTRAY must redirect held discharge material before it can reach PLACEWS.'

Write-Host 'PASS: interrupted tray placement resumes at the destination tray and cannot be routed back into the workstation.'
