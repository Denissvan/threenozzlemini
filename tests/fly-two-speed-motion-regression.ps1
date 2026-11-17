$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$plan = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/FlyTwoSpeedPlan.cs')
$move = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/UpDownLoad.ThreeNozzle.FlySpeed.cs')
$move = [regex]::Replace($move, '(?m)^using .*;\r?\n', '')
$axis = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'MotionCtrl/AXIS.cs')
$scopeStart = $axis.IndexOf('        [ThreadStatic] private static MoveProgressScope moveProgress;')
$scopeEnd = $axis.IndexOf('        public double ReadMotionSpeed()', $scopeStart)
$scope = $axis.Substring($scopeStart, $scopeEnd - $scopeStart)
$tickStart = $axis.IndexOf('                if (!pbquit && !VAR.gsys_set.bquit && moveProgress != null')
$tickEnd = $axis.IndexOf('                //time out', $tickStart)
$tick = $axis.Substring($tickStart, $tickEnd - $tickStart)
$stub = @'
namespace MotionCtrl {
 public enum EM_RES { OK,ERR,PARA_ERR,QUIT }
 public class CARD { public enum BRAND { LEADSHINE,OTHER }; public BRAND brand=BRAND.LEADSHINE; }
 public class VAR { public class Settings { public bool bquit; } public static Settings gsys_set=new Settings(); }
 public class AXIS {
  public CARD card=new CARD(); public double fcmd_pos,spd_work=800,spd_start=10,spd_stop=10,tacc=.1,tdec=.1,ts=.02;
  public double Current=800; public bool FailChange,FailRestore,WrongSpeed,Stopped; public EM_RES res;
  public System.Collections.Generic.List<double> Profiles=new System.Collections.Generic.List<double>(),Changes=new System.Collections.Generic.List<double>();
  public double ReadMotionSpeed(){return WrongSpeed?800:Current;}
  public EM_RES SetSpeed(double start,double high,double stop,double acc,double dec){Profiles.Add(high);Current=high;return FailRestore&&Profiles.Count>1?EM_RES.ERR:EM_RES.OK;}
  public EM_RES ChangSpeed(double speed){Changes.Add(speed);if(FailChange)return EM_RES.ERR;Current=speed;return EM_RES.OK;}
  public void Stop(){Stopped=true;}
  /*SCOPE*/
  public EM_RES Tick(bool pbquit){ /*TICK*/ return EM_RES.OK; }
 }
}
namespace UI {
 public static class ThreeNozzleHardwareConfig { public static bool FlyTwoSpeedEnabled; public static double[] Low={250,300}; public static double FlyLowSpeed(int arm){return Low[arm];} }
 public class Msg { public enum EM_MSGTYPE { DBG,ERR }; public void AddMsg(EM_MSGTYPE type,string text){} }
 public static class VAR { public static Msg msg=new Msg(); }
 public class Cam { public void FlyDiagnosticRecord(string text){} }
 public static class MT {
  public static bool QuitMove,ThrowMove,SkipSamples;
  public static MotionCtrl.EM_RES ZupMove(ref bool quit,ref MotionCtrl.AXIS axis,double end){
   if(ThrowMove)throw new System.InvalidOperationException("simulated exception");
   if(QuitMove){quit=true;return MotionCtrl.EM_RES.QUIT;}
   if(!SkipSamples){
    double step=end>axis.fcmd_pos?5:-5;
    for(double y=axis.fcmd_pos;step>0?y<end:y>end;y+=step){axis.fcmd_pos=y;var result=axis.Tick(quit);if(result!=MotionCtrl.EM_RES.OK)return result;}
   }
   axis.fcmd_pos=end;return MotionCtrl.EM_RES.OK;
  }
 }
 public partial class UpDownLoad {
  internal class ThreeNozzleFlyShot { public double TriggerY; }
  public MotionCtrl.AXIS ax_y=new MotionCtrl.AXIS();public int id;public string disc="test";
  static int checks;static void Check(bool ok,string text){checks++;if(!ok)throw new System.Exception(text);}
  static System.Collections.Generic.Dictionary<Cam,System.Collections.Generic.List<ThreeNozzleFlyShot>> Shots(int dir){return new System.Collections.Generic.Dictionary<Cam,System.Collections.Generic.List<ThreeNozzleFlyShot>>{{new Cam(),new System.Collections.Generic.List<ThreeNozzleFlyShot>{new ThreeNozzleFlyShot{TriggerY=dir*500},new ThreeNozzleFlyShot{TriggerY=dir*600}}}};}
  public static void Test(){
   bool quit=false,usable;ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled=false;
   var arm=new UpDownLoad();Check(arm.ThreeNozzleFlySpeedMove(ref quit,"off",1000,Shots(1),out usable)==MotionCtrl.EM_RES.OK&&usable&&arm.ax_y.Profiles.Count==0,"disabled must preserve original move");
   ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled=true;
   foreach(int dir in new[]{1,-1})foreach(int id in new[]{0,1}){
    arm=new UpDownLoad{id=id};
    Check(arm.ThreeNozzleFlySpeedMove(ref quit,"normal",dir*1000,Shots(dir),out usable)==MotionCtrl.EM_RES.OK&&usable,"successful speed profile");
    Check(arm.ax_y.Changes.SequenceEqual(new[]{ThreeNozzleHardwareConfig.Low[id],800.0}),"independent low then high, both travel directions");
    Check(arm.ax_y.Current==800&&arm.ax_y.spd_work==800,"restore profile and retain axis settings");
    int count=arm.ax_y.Changes.Count;arm.ax_y.Tick(false);Check(arm.ax_y.Changes.Count==count,"observer disposed after move");
   }
   arm=new UpDownLoad();arm.ax_y.fcmd_pos=450;
   Check(arm.ThreeNozzleFlySpeedMove(ref quit,"short",1000,Shots(1),out usable)==MotionCtrl.EM_RES.OK&&usable&&arm.ax_y.Profiles[0]==250,"short approach starts low");
   arm=new UpDownLoad();arm.ax_y.WrongSpeed=true;
   Check(arm.ThreeNozzleFlySpeedMove(ref quit,"bad speed",1000,Shots(1),out usable)==MotionCtrl.EM_RES.OK&&!usable,"wrong actual speed rejects images for static recapture");
   Check(arm.ax_y.Current==800&&!arm.ax_y.Stopped,"mismatch completes at low and restores work speed");
   arm=new UpDownLoad();arm.ax_y.FailChange=true;
   Check(arm.ThreeNozzleFlySpeedMove(ref quit,"change failed",1000,Shots(1),out usable)==MotionCtrl.EM_RES.ERR&&arm.ax_y.Stopped&&arm.ax_y.Current==800,"failed native change stops and restores");
   arm=new UpDownLoad();MT.QuitMove=true;
   Check(arm.ThreeNozzleFlySpeedMove(ref quit,"stop",1000,Shots(1),out usable)==MotionCtrl.EM_RES.QUIT&&arm.ax_y.Current==800,"quit propagates and restores");
   MT.QuitMove=false;quit=false;MT.ThrowMove=true;arm=new UpDownLoad();bool threw=false;
   try{arm.ThreeNozzleFlySpeedMove(ref quit,"throw",1000,Shots(1),out usable);}catch(System.InvalidOperationException){threw=true;}
   Check(threw&&arm.ax_y.Current==800,"exception restores profile");MT.ThrowMove=false;
   MT.SkipSamples=true;arm=new UpDownLoad();
   Check(arm.ThreeNozzleFlySpeedMove(ref quit,"no samples",1000,Shots(1),out usable)==MotionCtrl.EM_RES.OK&&!usable,"never accept unobserved capture speeds");MT.SkipSamples=false;
   arm=new UpDownLoad();arm.ax_y.FailRestore=true;
   Check(arm.ThreeNozzleFlySpeedMove(ref quit,"restore failed",1000,Shots(1),out usable)==MotionCtrl.EM_RES.ERR,"restore failure cannot be hidden");
   var a=new MotionCtrl.AXIS();var b=new MotionCtrl.AXIS();int hits=0;
   using(a.ObserveMoveProgress(()=>{hits++;return MotionCtrl.EM_RES.OK;})){
    b.Tick(false);a.Tick(true);Check(hits==0,"scope axis isolation and quit gate");
    a.Tick(false);Check(hits==1,"scope callback");
    var thread=new System.Threading.Thread(()=>a.Tick(false));thread.Start();thread.Join();Check(hits==1,"thread isolation");
    using(b.ObserveMoveProgress(()=>MotionCtrl.EM_RES.OK)){a.Tick(false);Check(hits==1,"nested scope isolation");}
    a.Tick(false);Check(hits==2,"nested scope restores previous observer");
   }
   a.Tick(false);Check(hits==2,"outer scope disposal");
   using(a.ObserveMoveProgress(()=>{throw new System.Exception();}))Check(a.Tick(false)==MotionCtrl.EM_RES.ERR&&a.Stopped,"observer exception stops axis");
   System.Console.WriteLine("PASS: "+checks+" production two-speed execution and scoped axis observer checks (simulated hardware boundaries).");
  }
 }
}
'@
$stub = $stub.Replace('/*SCOPE*/', $scope).Replace('/*TICK*/', $tick)
Add-Type -TypeDefinition ('using MotionCtrl;using System.Collections.Generic;' + $plan + $move + $stub)
[UI.UpDownLoad]::Test()
