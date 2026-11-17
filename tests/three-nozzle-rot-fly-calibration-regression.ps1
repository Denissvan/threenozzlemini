$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content (Join-Path $root 'UI/Compment/RotAndFly.cs') -Raw
if($source -match 'bool\[\] bxtmd = new bool\[2\]') { throw '飞拍仍把两片治具和两嘴数量混用' }
if($source -match 'XtMd\s*=|cy_zk.SetOff\(\);\s*Thread.Sleep') { throw '标定不允许伪造或清除真实物料身份/关闭真空' }
if($source -notmatch 'calibrationMove:') { throw '旋转中心未显式绑定物理U轴' }
$partial=Get-Content (Join-Path $root 'UI/Compment/RotAndFly.Calibration.cs') -Raw
foreach($pattern in @('SlotIndex','DisableHcmp','finally','bUpdate','bOK','double.IsNaN','BuildFlySamples','CaptureFlyPass')) {
 if($partial -notmatch [regex]::Escape($pattern)) {throw "缺少必要飞拍校验: $pattern"}
}
Write-Output 'PASS: 独立嘴任务映射、身份不变与结果门禁源码回归'
