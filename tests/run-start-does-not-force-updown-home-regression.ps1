$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$runFlowPath = Join-Path $root 'UI\Class\myfunc.cs'
$runFlow = Get-Content -LiteralPath $runFlowPath -Raw -Encoding Default

if ($runFlow -match 'RecoverUpDownLoadBeforeRun') {
    throw 'Starting a new run must not force both up/down arms to GoZero because a prior flow returned ERR or QUIT.'
}

if ($runFlow -match '上下料异常退出后，启动前回安全位') {
    throw 'The generic start-before-safety-home branch must be removed.'
}

Write-Host 'PASS: a new run does not force both up/down arms to X=0 after a generic abnormal exit.'
