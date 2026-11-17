$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$upDownLoad = Get-Content (Join-Path $repoRoot "UI\Class\UpDownLoad.cs") -Raw
$myfunc = Get-Content (Join-Path $repoRoot "UI\Class\myfunc.cs") -Raw

function Require([bool]$condition, [string]$message)
{
    if (!$condition)
    {
        throw $message
    }
}

$hallStart = $upDownLoad.IndexOf("if (PT_SET.HallEn && !WS.Demo)")
$feedReady = $upDownLoad.IndexOf("ws.FeedStatus = WS.EM_STA.REDAYFORTEST;", $hallStart)
Require ($hallStart -ge 0 -and $feedReady -gt $hallStart) "UpDownLoad Hall flow must set REDAYFORTEST after the Hall stage."

$hallBlock = $upDownLoad.Substring($hallStart, $feedReady - $hallStart)
$expectedSequence = "res\s*=\s*ws\.RunHallStage\(ref VAR\.gsys_set\.bquit, WS\.Demo\);\s*if \(res != EM_RES\.OK\) break;\s*res\s*=\s*ws\.SetupForTest\(ref VAR\.gsys_set\.bquit\);\s*if \(res != EM_RES\.OK\) break;"
Require ($hallBlock -match $expectedSequence) "UpDownLoad must flip to the test position immediately after the Hall stage and before REDAYFORTEST."

$legacyStart = $myfunc.IndexOf("res = workstation.RunHallStage")
$legacyEnd = $myfunc.IndexOf("workstation.FeedStatus = WS.EM_STA.REDAYFORTEST;", $legacyStart)
Require ($legacyStart -ge 0 -and $legacyEnd -gt $legacyStart) "Main test Hall flow block was not found."
$legacyBlock = $myfunc.Substring($legacyStart, $legacyEnd - $legacyStart)
Require ($legacyBlock -notmatch "workstation\.SetupForTest") "The non-updownload Hall branch must not carry the immediate flip workaround."

Write-Output "PASS: the updownload Hall flow flips immediately without waiting for all workstation test tasks."
