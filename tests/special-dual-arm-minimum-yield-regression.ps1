param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Assert-SourceContains {
    param(
        [string]$Path,
        [string]$Text,
        [string]$Message
    )

    & rg --fixed-strings --quiet -- $Text $Path
    if ($LASTEXITCODE -ne 0) {
        throw $Message
    }
}

$frSysSafety = Join-Path $RepoRoot 'UI\FormView\FrSys.DualArmSafetyDistance.cs'
$frSys = Join-Path $RepoRoot 'UI\FormView\FrSys.cs'
$mt = Join-Path $RepoRoot 'UI\Class\MT.cs'
$upDownLoad = Join-Path $RepoRoot 'UI\Class\UpDownLoad.cs'

Assert-SourceContains $frSysSafety 'private NumericUpDown nudUdSafeDistance;' 'Dual-arm safety distance must use an independent NumericUpDown.'
Assert-SourceContains $frSysSafety 'private void ApplyDualArmSafetyDistanceControls()' 'Dual-arm settings must be explicitly synchronized before save.'
Assert-SourceContains $frSys 'ApplyDualArmSafetyDistanceControls();' 'FrSys.GetData must synchronize dual-arm controls.'
Assert-SourceContains $mt 'private sealed class DualArmYieldRequest' 'Special avoidance must retain a cooperative yield request.'
Assert-SourceContains $mt 'private static int QueueDualArmYieldRequest' 'Reservation conflicts must queue a yield request.'
Assert-SourceContains $mt 'public static bool TryGetDualArmYieldRequest' 'Only the yielding arm owner may retrieve a pending request.'
Assert-SourceContains $upDownLoad 'private EM_RES ProcessPendingSpecialDualArmYield' 'The owning UpDownLoad thread must process its pending yield.'
Assert-SourceContains $upDownLoad 'res = ProcessPendingSpecialDualArmYield(ref bquit);' 'Yield handling must run before the next business step.'

$yieldBlock = (& rg -n -A 40 --fixed-strings 'private EM_RES ProcessPendingSpecialDualArmYield(ref bool bquit)' $upDownLoad) -join "`n"
if ([string]::IsNullOrWhiteSpace($yieldBlock)) {
    throw 'Unable to locate the special avoidance yield method.'
}
if ($yieldBlock -match 'GoZero\(') {
    throw 'Special avoidance yield must not call GoZero.'
}
if ($yieldBlock -match 'COM\.UDLoad[12]') {
    throw 'Special avoidance yield must not command the other arm.'
}
if ($yieldBlock -notmatch 'res = Zup\(ref bquit\);') {
    throw 'Special avoidance yield must raise Z before the X retreat.'
}

Write-Host 'PASS: special dual-arm avoidance has independent configuration persistence and cooperative minimum-yield ownership.'
