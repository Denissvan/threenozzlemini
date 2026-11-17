$ErrorActionPreference = 'Stop'

$dualArmSource = (& rg -n -A 95 --fixed-strings 'private void InitDualArmSafetyDistanceControls()' 'UI/FormView/FrSys.DualArmSafetyDistance.cs') -join "`n"
$designerSource = (& rg -n --fixed-strings 'this.tp_setparm_1.AutoScrollMinSize = new System.Drawing.Size(1098, 1050);' 'UI/FormView/FrSys.Designer.cs') -join "`n"

if ($dualArmSource -notmatch 'Location = new Point\(435, 895\);') {
    throw 'The dual-arm safety group must use the new scroll row.'
}

if ($dualArmSource -notmatch 'groupBoxDualArmSafetyDistance\.BringToFront\(\);') {
    throw 'The dual-arm safety group must explicitly be brought to the front.'
}

if ([string]::IsNullOrWhiteSpace($designerSource)) {
    throw 'Parameter 1 scroll range must include the dual-arm safety group.'
}

Write-Host 'PASS: special avoidance controls are visible in the Parameter 1 scroll area.'
