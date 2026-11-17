param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
$source=Join-Path $RepoRoot 'UI/Class/NozzleZCalibrationDraft.cs'
if (!(Test-Path $source)) { throw 'Missing production calibration draft model' }
Add-Type -Path $source
$draft=New-Object UI.NozzleZCalibrationDraft
function Values($a,$b,$c) { return ,([Nullable[double][]]@($a,$b,$c)) }
try { $draft.Apply(0,(Values 50 $null 49)); throw 'Accepted incomplete first calibration' } catch { if ($_.Exception.Message -eq 'Accepted incomplete first calibration') { throw } }
if ($draft.Arms[0].Baseline.HasValue) { throw 'Failed transaction changed baseline' }
$draft.Apply(0,(Values 50 50.3 49.8))
if ([Math]::Abs($draft.Offset(0,1)-0.3) -gt 0.00001) { throw 'Wrong compensation sign' }
$draft.Apply(0,(Values 50.4 $null $null))
if ($draft.Arms[0].Baseline -ne 50 -or [Math]::Abs($draft.Offset(0,0)-0.4) -gt 0.00001) { throw 'N1 remeasurement changed fixed reference' }
$draft.Apply(1,(Values 70 69.9 70.2))
if ($draft.Arms[0].Baseline -ne 50 -or $draft.Arms[1].Baseline -ne 70) { throw 'Arms not independent' }
try { $draft.Apply(0,(Values 99 ([double]::NaN) $null)); throw 'Accepted NaN' } catch { if ($_.Exception.Message -eq 'Accepted NaN') { throw } }
if ($draft.Arms[0].Contact[0] -ne 50.4) { throw 'Invalid input partially committed' }
$path=Join-Path ([IO.Path]::GetTempPath()) ('nozzle-z-draft-'+[guid]::NewGuid()+'.xml')
try {
    $draft.Arms[0].Points[2].X = 12.5
    $draft.Arms[0].Points[2].Approach = 20
    $draft.Arms[0].Points[2].End = 25
    $copy = $draft.Copy()
    $copy.Arms[0].Points[2].X = 99
    if ($draft.Arms[0].Points[2].X -ne 12.5) { throw 'Point copy is aliased' }
    $copy.Arms[0].Points[2].End = 19
    try { $copy.Validate(); throw 'Accepted reversed probe range' } catch { if ($_.Exception.Message -eq 'Accepted reversed probe range') { throw } }
    $draft.Save($path)
    $loaded=[UI.NozzleZCalibrationDraft]::Load($path)
    if ($loaded.Arms[0].Points[2].X -ne 12.5 -or $loaded.Arms[1].Points[2].X -ne $null) { throw 'Point roundtrip or arm isolation failed' }
    if ($loaded.Arms[0].Contact[0] -ne 50.4 -or $loaded.Arms[1].Baseline -ne 70) { throw 'Roundtrip mismatch' }
    $loaded.Apply(1,(Values $null $null 70.6)); $loaded.Save($path)
    if ([Math]::Abs(([UI.NozzleZCalibrationDraft]::Load($path)).Offset(1,2)-0.6) -gt 0.00001) { throw 'Replace failed' }
    if (!(Test-Path ($path+'.bak'))) { throw 'Missing backup' }
} finally { Remove-Item -LiteralPath $path,($path+'.bak') -ErrorAction SilentlyContinue }
# Existing review drafts predate the optional point settings; loading must remain compatible.
$legacy=Join-Path ([IO.Path]::GetTempPath()) ('nozzle-z-legacy-'+[guid]::NewGuid()+'.xml')
try {
    $draft.Save($legacy)
    [xml]$xml=Get-Content -LiteralPath $legacy -Raw
    foreach($arm in $xml.NozzleZCalibrationDraft.Arms.ArmDraft) { [void]$arm.RemoveChild($arm.Points) }
    $xml.Save($legacy)
    $loaded=[UI.NozzleZCalibrationDraft]::Load($legacy)
    if ($loaded.Arms[0].Points.Length -ne 3 -or $loaded.Arms[0].Baseline -ne 50) { throw 'Legacy draft migration failed' }
} finally { Remove-Item -LiteralPath $legacy -ErrorAction SilentlyContinue }
'PASS: point isolation, legacy load, fixed baseline, N1 remeasurement, independent arms, validation atomicity, save/reload and backup.'
