param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
Add-Type -Path @((Join-Path $RepoRoot 'tests/ZCalibrationMotionHarness.cs'),(Join-Path $RepoRoot 'UI/Class/NozzleZCalibrationDraft.cs'),(Join-Path $RepoRoot 'UI/Class/NozzleZCalibration.cs'),(Join-Path $RepoRoot 'UI/Class/NozzleZProbe.cs'),(Join-Path $RepoRoot 'UI/Class/NozzleZCalibrationMotion.cs'))
[ZCalibrationMotionHarness]::Run()
'PASS: real calibration motion adapter; loaded preflight preservation, IO fault stop, positioning speed restore, in-flight cancellation.'
