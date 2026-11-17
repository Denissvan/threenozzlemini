param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$upDownLoad = Join-Path $RepoRoot 'UI\Class\UpDownLoad.cs'
$source = Get-Content -Raw -LiteralPath $upDownLoad
$yieldCall = 'res = ProcessPendingSpecialDualArmYield(ref bquit);'
$yieldCallIndex = $source.IndexOf($yieldCall, [System.StringComparison]::Ordinal)

if ($yieldCallIndex -lt 0) {
    throw 'The workflow must check pending special-avoidance yield requests before shared resource access.'
}

$workflowLocks = @(
    @{ Name = 'pickws'; Variable = 'pickWsLockTaken' },
    @{ Name = 'placetray'; Variable = 'placeTrayLockTaken' },
    @{ Name = 'picktray'; Variable = 'pickTrayLockTaken' },
    @{ Name = 'placews'; Variable = 'placeWsLockTaken' }
)

foreach ($workflowLock in $workflowLocks) {
    $name = $workflowLock.Name
    $variable = $workflowLock.Variable

    if ($source.Contains("lock ($name)")) {
        throw "The $name workflow lock must not block a yielding arm indefinitely."
    }

    $tryEnter = "Monitor.TryEnter($name, 100, ref $variable);"
    $tryEnterIndex = $source.IndexOf($tryEnter, [System.StringComparison]::Ordinal)
    if ($tryEnterIndex -lt 0) {
        throw "The $name workflow lock must use a bounded lock attempt."
    }

    $unavailableIndex = $source.IndexOf("if (!$variable)", $tryEnterIndex, [System.StringComparison]::Ordinal)
    $continueIndex = $source.IndexOf('continue;', $unavailableIndex, [System.StringComparison]::Ordinal)
    $finallyIndex = $source.IndexOf('finally', $tryEnterIndex, [System.StringComparison]::Ordinal)
    $exitIndex = $source.IndexOf("Monitor.Exit($name);", $finallyIndex, [System.StringComparison]::Ordinal)

    if ($unavailableIndex -lt 0 -or $continueIndex -lt $unavailableIndex) {
        throw "A busy $name lock must return the arm to the special-avoidance processing loop."
    }
    if ($finallyIndex -lt 0 -or $exitIndex -lt $finallyIndex) {
        throw "The $name workflow lock must be released reliably after successful acquisition."
    }
    if ($yieldCallIndex -gt $tryEnterIndex) {
        throw "Pending special-avoidance requests must be checked before the $name lock attempt."
    }
}

Write-Host 'PASS: every shared up/down workflow lock yields back to the special-avoidance priority loop when busy.'
