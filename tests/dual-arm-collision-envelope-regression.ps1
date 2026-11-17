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

$myType = Get-Content -LiteralPath (Join-Path $RepoRoot 'MotionCtrl\MyType.cs') -Raw -Encoding UTF8
$mt = Get-Content -LiteralPath (Join-Path $RepoRoot 'UI\Class\MT.cs') -Raw -Encoding UTF8
$upDownLoad = Get-Content -LiteralPath (Join-Path $RepoRoot 'UI\Class\UpDownLoad.cs') -Raw -Encoding UTF8

Assert-Condition ($myType.Contains('DefaultUdMaxCombinedExtent')) 'A calibrated dual-arm combined-extension limit must have a safe default.'
Assert-Condition ($myType.Contains('UD_MAX_COMBINED_EXTENT')) 'The calibrated dual-arm combined-extension limit must persist with product settings.'
Assert-Condition ($mt.Contains('EffectiveDualArmCombinedExtentLimit')) 'Normal and special avoidance must share one effective combined-extension limit.'
Assert-Condition ($mt.Contains('Math.Min(PT_SET.UdMaxCombinedExtent')) 'The mechanical combined-extension limit must cap the vision-calibrated limit.'

$moveStart = $mt.IndexOf('private static EM_RES MoveCore(')
Assert-Condition ($moveStart -ge 0) 'The guarded axis dispatch implementation is missing.'
$moveBlock = $mt.Substring($moveStart, [Math]::Min(5000, $mt.Length - $moveStart))
Assert-Condition ($moveBlock.Contains('if (dualArmId >= 0)')) 'Every UDL X move must reserve the shared collision envelope.'
Assert-Condition (-not $moveBlock.Contains('PT_SET.bSpecialDualArmAvoidance && dualArmId >= 0')) 'The collision envelope must not depend on the special-avoidance checkbox.'

Assert-Condition ($upDownLoad.Contains('EffectiveDualArmCombinedExtentLimit')) 'Module-2 wait logic must use the same collision envelope as axis movement.'

Write-Host 'PASS: dual-arm collision envelope is independent from vision calibration and active for normal and special avoidance.'
