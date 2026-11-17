param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$upDownLoad = Join-Path $RepoRoot 'UI\Class\UpDownLoad.cs'
$source = Get-Content -Raw -LiteralPath $upDownLoad

if ($source.Contains('lock (placetray)')) {
    throw 'A waiting placer must not hold the shared placetray lock while the other arm needs to process a yield request.'
}

$tryEnter = 'Monitor.TryEnter(placetray, 100, ref placeTrayLockTaken);'
$tryEnterIndex = $source.IndexOf($tryEnter, [System.StringComparison]::Ordinal)
if ($tryEnterIndex -lt 0) {
    throw 'Placetray access must use a bounded lock attempt so a yielding arm can re-enter its priority loop.'
}

$unavailableIndex = $source.IndexOf('if (!placeTrayLockTaken)', $tryEnterIndex, [System.StringComparison]::Ordinal)
$continueIndex = $source.IndexOf('continue;', $unavailableIndex, [System.StringComparison]::Ordinal)
$finallyIndex = $source.IndexOf('finally', $tryEnterIndex, [System.StringComparison]::Ordinal)
$exitIndex = $source.IndexOf('Monitor.Exit(placetray);', $finallyIndex, [System.StringComparison]::Ordinal)

if ($unavailableIndex -lt 0 -or $continueIndex -lt $unavailableIndex) {
    throw 'When placetray is busy, the arm must return to the loop before entering the placement step.'
}
if ($finallyIndex -lt 0 -or $exitIndex -lt $finallyIndex) {
    throw 'The bounded placetray lock must still be released reliably.'
}

$yieldCallIndex = $source.IndexOf('res = ProcessPendingSpecialDualArmYield(ref bquit);', [System.StringComparison]::Ordinal)
if ($yieldCallIndex -lt 0 -or $yieldCallIndex -gt $tryEnterIndex) {
    throw 'Pending yield handling must run before each bounded placetray attempt.'
}

Write-Host 'PASS: a busy placetray lock cannot prevent the yielding arm from returning to special-avoidance processing.'
