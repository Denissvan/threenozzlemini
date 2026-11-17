param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
Add-Type -Path @((Join-Path $RepoRoot 'UI/Class/NozzleZCalibrationDraft.cs'),(Join-Path $RepoRoot 'UI/Class/NozzleZCalibration.cs'),(Join-Path $RepoRoot 'UI/Class/NozzleZProbe.cs'),(Join-Path $RepoRoot 'tests/ZCalibrationRegression.cs'))
[ZCalibrationRegression]::Run()
'PASS: fixed reference, direction/teach inverse, three samples, spread atomicity, probe bounds/cancel/initial vacuum, persistence.'