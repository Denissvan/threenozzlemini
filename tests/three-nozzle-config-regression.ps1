param([string]$RepoRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$production = Get-Content -LiteralPath (Join-Path $RepoRoot 'UI/Class/ThreeNozzleHardwareConfig.cs') -Raw
# Approved wiring contract: all four legacy nozzles must leave the old card.
$motion = Get-Content -LiteralPath (Join-Path $RepoRoot 'UI/Class/MT.cs') -Raw
foreach ($row in @(@(1,1,0,0),@(1,2,2,1),@(2,1,6,3),@(2,2,8,4))) {
    foreach ($signal in @(@('OUT','ZK',$row[2]),@('OUT','PZK',($row[2]+1)),@('IN','ZK',$row[3]))) {
        $field = 'GPIO_{0}_UDL{1}_{2}_N{3}' -f $signal[0],$row[0],$signal[1],$row[1]
        $expected = '{0} = new GPIO({1}, CARD_ZMC432_8,' -f $field,$signal[2]
        if (!$motion.Contains($expected)) { throw "Approved six-nozzle wiring mismatch: $field" }
    }
}
if (!$motion.Contains('"192.168.0.30", 18, 12, 12,')) { throw 'New card must expose the twelve listed inputs/outputs' }

Add-Type -TypeDefinition ($production + @'
namespace System.Windows.Forms { public static class Application { public static string StartupPath; } }
namespace MotionCtrl {
 public class CARD { public enum TYPE { MOTION, IO, OTHER }; public TYPE type=TYPE.MOTION; public int id; public int input_num=32,output_num=32; public System.Collections.Generic.List<GPIO> GPIOList=new System.Collections.Generic.List<GPIO>(); }
 public class GPIO { public enum IO_DIR { IN, OUT }; public enum IO_TYPE { NULL, MT_CARD, IO_CARD }; public enum IO_STA { OUT_OFF, IN_OFF }; public CARD card; public ushort num; public int m_id; public IO_DIR dir; public IO_TYPE type; public GPIO(ushort n,CARD c,IO_DIR d,IO_TYPE t,string s,string e,IO_STA v){num=n;card=c;dir=d;type=t;} }
 public class Cylinder { public GPIO io_out1, io_sen_on; public Cylinder(GPIO o,GPIO i){io_out1=o;io_sen_on=i;} }
}
namespace UI { public static class MT { public static System.Collections.Generic.List<MotionCtrl.CARD> CardList=new System.Collections.Generic.List<MotionCtrl.CARD>{ new MotionCtrl.CARD{id=3},new MotionCtrl.CARD{id=8,input_num=12,output_num=12} }; } }
namespace UI {
 public static class NozzleZCalibrationMotion { public static bool IsBusy; }
 public static class NozzleZCalibration { public static bool Missing; public static int Calls; public static double ToAxisZ(int arm,int slot,double z,int direction){Calls++; if(Missing)throw new System.InvalidOperationException("Missing official calibration");return direction*z;} }
 public class XT {public int SlotIndex;public int ZApproachDirection=1;}
 public class Arm {public System.Collections.Generic.List<XT> list_xt=new System.Collections.Generic.List<XT>{new XT{SlotIndex=0},new XT{SlotIndex=1},new XT{SlotIndex=2}};}
 public static class COM {public static Arm[] List_UDLoad={new Arm(),new Arm()};}
}
namespace ConfigTests {
public static class Regression {
 static void Check(bool condition,string message) { if(!condition)throw new System.Exception(message); }
 public static void Run(string root) {
  foreach(string mode in new[]{"valid","missing","duplicate","occupied","range","oldcard","unconfirmed","nan","duplicate-key"}) {
   string dir=System.IO.Path.Combine(root,mode); System.IO.Directory.CreateDirectory(dir); System.Windows.Forms.Application.StartupPath=dir;
   typeof(UI.ThreeNozzleHardwareConfig).GetField("initialized",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).SetValue(null,false);
   foreach(var card in UI.MT.CardList)card.GPIOList.Clear();
   if(mode=="occupied")UI.MT.CardList[1].GPIOList.Add(new MotionCtrl.GPIO(4,UI.MT.CardList[1],MotionCtrl.GPIO.IO_DIR.OUT,MotionCtrl.GPIO.IO_TYPE.MT_CARD,"existing","existing",MotionCtrl.GPIO.IO_STA.OUT_OFF));
   string data="[Machine]\nCalibrationConfirmed="+(mode=="unconfirmed"?"false":"true")+"\n";
   for(int arm=1;arm<=2;arm++) { data+="[Arm"+arm+"]\nSafeTrayMaxY="+(mode=="nan"?"NaN":"10")+"\n"; int n=0; int[] points=arm==1?new[]{4,5,2}:new[]{10,11,5};
    foreach(string key in new[]{"Vacuum","VacuumBreak","VacuumSense"})data+=key+"Card="+(mode=="oldcard"?3:8)+"\n"+key+"Point="+(mode=="duplicate"?0:mode=="range"?12:points[n++])+"\n";
   }
   if(mode=="duplicate-key")data+="VacuumCard=3\n";
   if(mode!="missing")System.IO.File.WriteAllText(UI.ThreeNozzleHardwareConfig.ConfigurationPath,data);
   var stable=UI.ThreeNozzleHardwareConfig.Arm1VacuumBreak;
   UI.ThreeNozzleHardwareConfig.Initialize(); string err; bool ok=UI.ThreeNozzleHardwareConfig.ValidateForAutomatic(out err);
   Check(ok==(mode=="valid"),"Unexpected automatic gate "+mode+": "+err);
   if(mode=="valid") {
    Check(UI.NozzleZCalibration.Calls==6,"Automatic start must validate all six official nozzle calibrations");
    UI.NozzleZCalibrationMotion.IsBusy=true;Check(!UI.ThreeNozzleHardwareConfig.ValidateForAutomatic(out err),"Calibration lease must block automatic start");UI.NozzleZCalibrationMotion.IsBusy=false;
    UI.NozzleZCalibration.Missing=true;Check(!UI.ThreeNozzleHardwareConfig.ValidateForAutomatic(out err),"Missing official calibration must block automatic start");UI.NozzleZCalibration.Missing=false;
   }
   bool wired=mode=="valid"||mode=="nan"||mode=="unconfirmed";
   Check(UI.ThreeNozzleHardwareConfig.IsConfigured==wired,"Unexpected wiring result "+mode);
   int count=wired?6:mode=="occupied"?1:0;
   Check(UI.MT.CardList[1].GPIOList.Count==count,"Partial binding "+mode);
   if(wired) {
    Check(UI.ThreeNozzleHardwareConfig.Arm1Vacuum.io_out1.num==4 && UI.ThreeNozzleHardwareConfig.Arm1VacuumBreak.num==5 && UI.ThreeNozzleHardwareConfig.Arm1Vacuum.io_sen_on.num==2,"Arm1 N3 address");
    Check(UI.ThreeNozzleHardwareConfig.Arm2Vacuum.io_out1.num==10 && UI.ThreeNozzleHardwareConfig.Arm2VacuumBreak.num==11 && UI.ThreeNozzleHardwareConfig.Arm2Vacuum.io_sen_on.num==5,"Arm2 N3 address");
    Check(UI.MT.CardList[0].GPIOList.Count==0,"Old card unexpectedly registered");
   }
   UI.ThreeNozzleHardwareConfig.Initialize();
   Check(UI.MT.CardList[1].GPIOList.Count==count,"Repeated registration "+mode);
   Check(object.ReferenceEquals(stable,UI.ThreeNozzleHardwareConfig.Arm1VacuumBreak),"Reference replaced");
  }
 }
}
}
'@)
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('three-nozzle-config-' + [Guid]::NewGuid().ToString('N'))
try { [ConfigTests.Regression]::Run($tempRoot) }
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($tempRoot)
    $allowedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolvedRoot.StartsWith($allowedParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedRoot) -notmatch '^three-nozzle-config-[a-f0-9]{32}$') { throw 'Unsafe temporary cleanup path' }
    if (Test-Path -LiteralPath $resolvedRoot) { Remove-Item -LiteralPath $resolvedRoot -Recurse -Force }
}
Write-Host 'PASS: production configuration valid/missing/duplicate/occupied/out-of-range/old-card/calibration/safe-Y/duplicate-key cases.'
