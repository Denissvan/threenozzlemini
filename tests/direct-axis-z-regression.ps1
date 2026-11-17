param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
Add-Type -Path @((Join-Path $RepoRoot 'UI/Class/NozzleZCalibrationDraft.cs'),(Join-Path $RepoRoot 'UI/Class/NozzleZCalibration.cs'))
$failures = New-Object 'System.Collections.Generic.List[string]'
$checks = 0
function Check([string]$Name, [scriptblock]$Body) {
    $script:checks++
    try { & $Body; Write-Output "PASS: $Name" }
    catch { $script:failures.Add("$Name : $_"); Write-Output "FAIL: $Name : $_" }
}
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('mini-direct-z-' + [Guid]::NewGuid().ToString('N'))
$previous = [Environment]::CurrentDirectory
[IO.Directory]::CreateDirectory((Join-Path $scratch 'work')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $scratch 'syscfg')) | Out-Null
try {
    [Environment]::CurrentDirectory = Join-Path $scratch 'work'
    Check 'Six nozzles use signed axis Z without a calibration file' {
        foreach($arm in 0..1) { foreach($slot in 0..2) { foreach($dir in @(-1,1)) {
            foreach($z in @(-20.5,0,20.5)) {
                if([UI.NozzleZCalibration]::ToAxisZ($arm,$slot,$z,$dir) -ne $z) { throw 'Target sign/value changed' }
                if([UI.NozzleZCalibration]::ToProcessZ($arm,$slot,$z,$dir) -ne $z) { throw 'Teach sign/value changed' }
            }
        }}}
    }
    [IO.File]::WriteAllText([UI.NozzleZCalibration]::ConfigurationPath, 'invalid legacy measurement file')
    Check 'Old measurement file cannot block or modify motion coordinates' {
        if([UI.NozzleZCalibration]::ToAxisZ(1,2,-25,-1) -ne -25) { throw 'Legacy file affected target' }
    }
    Check 'Invalid coordinates are still rejected' {
        foreach($z in @([double]::NaN,[double]::PositiveInfinity,[double]::NegativeInfinity)) {
            $rejected=$false
            try { [UI.NozzleZCalibration]::ToAxisZ(0,0,$z,-1) | Out-Null } catch { $rejected=$true }
            if(!$rejected) { throw 'Non-finite coordinate accepted' }
        }
    }
    Check 'Clearance retracts toward zero for either down direction' {
        if([UI.NozzleZCalibration]::RetractAxisZ(-20,-1,5) -ne -15) { throw 'Negative Z clearance incorrect' }
        if([UI.NozzleZCalibration]::RetractAxisZ(20,1,5) -ne 15) { throw 'Positive Z clearance incorrect' }
    }
    Check 'Place-check numeric field loads negative recipe Z' {
        $source = [IO.File]::ReadAllText((Join-Path $RepoRoot 'UI/Compment/XtOfsAdj.Designer.cs'))
        $block = [regex]::Match($source, '(?s)// nub_Zhigh\s*//(.*?)// lbl_zhigh').Groups[1].Value
        if(!$block) { throw 'Numeric control source not found' }
        $uiSource = @"
public class SignedZControlHarness {
    public System.Windows.Forms.NumericUpDown nub_Zhigh = new System.Windows.Forms.NumericUpDown();
    public SignedZControlHarness() { $block }
    [System.STAThread] public static int Main() {
        var h = new SignedZControlHarness();
        try {
            h.nub_Zhigh.Value = -20;
            return h.nub_Zhigh.Value == -20 ? 0 : 1;
        } catch (System.Exception ex) {
            System.Console.WriteLine(ex.Message); return 1;
        } finally { h.nub_Zhigh.Dispose(); }
    }
}
"@
        $uiPath = Join-Path $scratch 'SignedZControlHarness.cs'
        $uiExe = Join-Path $scratch 'SignedZControlHarness.exe'
        [IO.File]::WriteAllText($uiPath,$uiSource)
        & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$uiExe" $uiPath
        if($LASTEXITCODE -ne 0) { throw 'UI harness compilation failed' }
        & $uiExe
        if($LASTEXITCODE -ne 0) { throw 'Negative Z load failed in the actual WinForms control' }
    }
} finally { [Environment]::CurrentDirectory=$previous }
if($failures.Count) { throw "$($failures.Count)/$checks checks failed: $($failures -join '; ')" }
"PASS: $checks direct-axis Z checks. Scratch: $scratch"
