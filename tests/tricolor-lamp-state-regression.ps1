$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$sysInfo = Get-Content -LiteralPath (Join-Path $root 'MotionCtrl\MSG.cs') -Raw -Encoding UTF8
$main = Get-Content -LiteralPath (Join-Path $root 'UI\FormView\FrMain.cs') -Raw -Encoding UTF8
$runFlow = Get-Content -LiteralPath (Join-Path $root 'UI\Class\myfunc.cs') -Raw -Encoding UTF8

foreach ($required in @(
    'public enum EM_NORMAL_LAMP',
    'public void SetNormalLamp(EM_NORMAL_LAMP normalLamp)',
    'normalLamp == EM_NORMAL_LAMP.RUN ? out_alm_green : out_alm_yellow',
    'ApplyLampOutput(io, bflash)')) {
    if ($sysInfo -notlike "*$required*") {
        throw "SYS_INF must centrally apply normal lamp state: $required"
    }
}

if ($main -notlike '*VAR.sys_inf.SetNormalLamp(normalLamp);*') {
    throw 'The main timer must publish standby, running, and tray-wait lamp state through SYS_INF.'
}

if ($main -match 'MT\.GPIO_OUT_ALM_YELLOW\.SetOn\(\);//待料黄灯') {
    throw 'The main timer must not directly turn on the yellow lamp for tray wait.'
}

if ($runFlow -match '(?m)^\s*MT\.GPIO_OUT_ALM_GREEN\.SetOn\(\);\s*$') {
    throw 'The run thread must not directly write the green lamp after SYS_INF owns normal lamp output.'
}

Write-Host 'PASS: normal tri-color lamp state is centralized and tray wait cannot leave green and yellow on together.'
