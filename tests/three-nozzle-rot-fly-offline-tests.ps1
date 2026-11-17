$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$out=Join-Path ([IO.Path]::GetTempPath()) ('RotAndFlyCalibration-'+[Guid]::NewGuid().ToString('N')+'.exe')
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "/out:$out" (Join-Path $root 'UI/Compment/RotAndFly.Calibration.cs') (Join-Path $PSScriptRoot 'RotAndFlyCalibrationHarness.cs')
if($LASTEXITCODE -ne 0){throw 'Offline calibration harness compile failed'}
try { & $out; if($LASTEXITCODE -ne 0){throw 'Offline calibration harness failed'} }
finally { Remove-Item -LiteralPath $out -ErrorAction SilentlyContinue }
