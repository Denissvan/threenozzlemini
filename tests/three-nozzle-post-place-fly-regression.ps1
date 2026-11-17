$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$helper=Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/UpDownLoad.ThreeNozzle.BackCheckFly.cs')
$runtime=Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/UpDownLoad.ThreeNozzle.cs')
function Extract-Braced([string]$text,[string]$marker){
 $start=$text.IndexOf($marker);if($start -lt 0){throw "Missing $marker"};$pos=$text.IndexOf('{',$start);$depth=1;$end=$pos+1
 while($depth -gt 0){if($text[$end] -eq '{'){$depth++};if($text[$end] -eq '}'){$depth--};$end++}
 return $text.Substring($start,$end-$start)
}
$decisionStart=$runtime.IndexOf('                if (!completePostPlace && countNew')
$decisionEnd=$runtime.IndexOf('                pair.Accepted[slot] = true;', $decisionStart)
$decisionEnd=$runtime.IndexOf('return EM_RES.OK;', $decisionEnd)+'return EM_RES.OK;'.Length
$decision=$runtime.Substring($decisionStart,$decisionEnd-$decisionStart)
$rework=Extract-Braced $runtime 'if (pair.ReworkStage[slot] == 1)'
$stub=@'
namespace MotionCtrl {
 public enum EM_RES{OK,ERR,QUIT,RETRY}
 public class AXIS{public double fcmd_pos=1150,sln=0,slp=1500;}
 public struct ST_XYA{public double x,y,a;}
}
namespace UI.Class {}
namespace UI {
 using System;using System.Linq;using System.Collections.Generic;using MotionCtrl;
 public static class PT_SET{public enum BAR_SCAN{UP_SCAN}public static int BarcodeMode;public static bool Isaloneset,bEnVsFB=true,bNgControl=true;public static int EquipmentMT=1;}
 public static class NewSysInf{public static class UserParams{public static bool bUpWsChkQrCodeOff;}}
 public static class Msg{public enum EM_MSGTYPE{WAR,DBG}}
 public static class VAR{public static Logger msg=new Logger();public class Logger{public void AddMsg(Msg.EM_MSGTYPE t,string s){}}}
 public static class CONST{public const string WsModUpFw="WsMod_Shp";}
 public static class COUNT_DATA{public static int[] allcnt={0};public static int ngctrlallcnt,CurEquipmentMT;}
 public class WS{public int num;public class Pos{public double x=20,y;}public class MdDat{public int Num,res=-1;public string ModuleCheckSampleId;public MdDat Clone(){return (MdDat)MemberwiseClone();}public Pos[] st_pos={new Pos()};}}
 public class XT{public object XtMd;public double ws_cap_near_ofs=2,ws_cap_far_ofs=3;}
 public class Cam{public class VisionOutPutData{public bool bOK=true,bUpdate=true;public ST_XYA PosMM;public VisionOutPutData Clone(){return (VisionOutPutData)MemberwiseClone();}}}
 public partial class UpDownLoad{
  public class ThreeNozzleRuntimePair{public WS.MdDat[] Pockets={new WS.MdDat{Num=1,st_pos=new[]{new WS.Pos{y=970}}},new WS.MdDat{Num=9,st_pos=new[]{new WS.Pos{y=1020}}}};public bool[] Placed={true,true},Accepted={false,false};public bool[] CountAsNew={true,true};public int[] ReworkStage={0,0};public int PlacementRevision;}
  public class ThreeNozzleFlyShot{public Cam Camera;public string Task,Label;public double X,NominalY,TriggerY;public int Identity;public Cam.VisionOutPutData Result;}
  enum ThreeNozzleMode{Exchange,GrrReload} ThreeNozzleMode threeNozzleMode=ThreeNozzleMode.Exchange;int ErrTryPickCnt=0;class AfterCloseTarget{public AfterCloseTarget(WS.MdDat m,double x,int id){}}List<AfterCloseTarget> afterCloseTargets=new List<AfterCloseTarget>();void AddAfterCloseTargets(List<AfterCloseTarget> a,AfterCloseTarget[] b){a.AddRange(b);}
  int id=0;bool Demo=false,ThreeNozzleHasSpecialMode=false;bool ThreeNozzleFlyActive=true;Cam upcam=new Cam();AXIS ax_y=new AXIS();List<XT> list_xt=new List<XT>{new XT(),new XT(),new XT()};string disc="arm";WS threeNozzleSource=new WS();
  const double ThreeNozzleFlyRunUp=180,ThreeNozzleFlyRunOut=50;
  int flyCalls;List<int> checkedSlots=new List<int>();List<bool> imageUsed=new List<bool>();List<string> actions=new List<string>();
  ThreeNozzleFlyShot[] captured;EM_RES flightResult=EM_RES.OK,checkResult=EM_RES.OK,homeResult=EM_RES.OK,unloadResult=EM_RES.OK;int missing=-1;bool reviseFirst;
  static bool FlyFinite(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
  static double ThreeNozzleFlyTrigger(double y,double ofs,bool inc){return inc?y-ofs:y+ofs;}
  EM_RES ThreeNozzleFlyPass(ref bool quit,string flow,double x,double start,double end,List<ThreeNozzleFlyShot> shots){
   flyCalls++;captured=shots.ToArray();Assert(end<shots.Min(s=>s.TriggerY)&&start>=shots.Max(s=>s.TriggerY)+180,"reverse travel with runup");
   foreach(var s in shots)if(s.Identity!=missing)s.Result=new Cam.VisionOutPutData();return flightResult;
  }
  EM_RES ThreeNozzlePlaceOne(ref bool quit,ThreeNozzleRuntimePair pair,int slot,bool count,bool completing,Cam.VisionOutPutData image){Assert(completing,"must not defer again");checkedSlots.Add(slot);imageUsed.Add(image!=null);if(checkResult!=EM_RES.OK)return checkResult;if(reviseFirst&&slot==0)pair.PlacementRevision++;pair.Accepted[slot]=true;return EM_RES.OK;}
  EM_RES ThreeNozzleCheckFinishedDownBarcode(ref bool quit,WS ws,XT xt){actions.Add("finished-barcode");return EM_RES.OK;}
  int postChecks=0;EM_RES ThreeNozzleCheckAfterPlace(ref bool quit,WS ws,XT xt,WS.MdDat md,Cam.VisionOutPutData image){postChecks++;return checkResult;}
  EM_RES GoZero(ref bool quit){actions.Add("home");return homeResult;}
  EM_RES ThreeNozzleUnloadCore(ref bool quit,XT xt){actions.Add("unload");if(unloadResult==EM_RES.OK)xt.XtMd=null;return unloadResult;}
  EM_RES ThreeNozzlePickMaterial(ref bool quit,WS.MdDat md,XT xt,bool before){Assert(xt.XtMd==null,"cannot retrieve reject with occupied nozzle");actions.Add("pick-reject");return EM_RES.OK;}
  static int checks;static void Assert(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  public static void Run(){
   bool quit=false;var a=new UpDownLoad();var p=new ThreeNozzleRuntimePair();
   Assert(a.ThreeNozzleCompletePostPlaceChecks(ref quit,p)==EM_RES.OK&&a.flyCalls==1&&a.imageUsed.All(v=>v)&&p.Accepted.All(v=>v),"both post-place pockets use fresh flight");
   Assert(a.captured[0].Task=="WsMod_Shp"&&a.captured[0].Identity==0&&a.captured[1].Identity==8&&a.captured[0].TriggerY==972&&a.captured[1].TriggerY==1023,"task pocket identity and reverse offsets");
   Assert(a.ThreeNozzleCompletePostPlaceChecks(ref quit,p)==EM_RES.OK&&a.flyCalls==1,"accepted placements not repeated");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();p.Placed[1]=false;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.checkedSlots.SequenceEqual(new[]{0})&&!p.Accepted[1],"never inspect or accept unplaced pocket");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();a.missing=8;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.imageUsed.SequenceEqual(new[]{true,false}),"missing image falls back per pocket");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();a.flightResult=EM_RES.ERR;Assert(a.ThreeNozzleCompletePostPlaceChecks(ref quit,p)==EM_RES.ERR&&a.checkedSlots.Count==0&&p.Accepted.All(v=>!v),"motion and trigger failures block acceptance");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();a.checkResult=EM_RES.QUIT;Assert(a.ThreeNozzleCompletePostPlaceChecks(ref quit,p)==EM_RES.QUIT&&p.Accepted.All(v=>!v),"operator stop blocks handoff");a.checkResult=EM_RES.OK;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.flyCalls==2,"restart recaptures instead of using old pass");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();p.Pockets[1].st_pos[0].x=21;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.flyCalls==0&&a.imageUsed.All(v=>!v),"different columns fixed fallback");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();a.ax_y.slp=1100;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.flyCalls==0&&a.imageUsed.All(v=>!v),"insufficient runup fixed fallback");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();a.ThreeNozzleFlyActive=false;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.flyCalls==0,"master off retained");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();a.reviseFirst=true;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.imageUsed.SequenceEqual(new[]{true,false}),"replacement invalidates other old image");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();p.Pockets[1].st_pos[0].x=20.4;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.flyCalls==1,"tiny X mismatch allowed with coordinate correction");
   a=new UpDownLoad();var held=a.list_xt[1];held.XtMd=new object();p=new ThreeNozzleRuntimePair();p.ReworkStage[0]=1;Assert(a.ReworkForTest(ref quit,p,held,p.Pockets[0])==EM_RES.OK&&string.Join(",",a.actions)=="home,finished-barcode,unload,pick-reject"&&p.ReworkStage[0]==2,"actual rework branch unloads old product before retrieval");
   a=new UpDownLoad();held=a.list_xt[1];held.XtMd=new object();p=new ThreeNozzleRuntimePair();p.ReworkStage[0]=1;a.unloadResult=EM_RES.ERR;Assert(a.ReworkForTest(ref quit,p,held,p.Pockets[0])==EM_RES.ERR&&held.XtMd!=null&&p.ReworkStage[0]==1&&!a.actions.Contains("pick-reject"),"failed unload cannot retrieve or lose identity");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();p.ReworkStage[0]=2;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.flyCalls==0&&a.imageUsed.All(v=>!v),"interrupted rework cannot use images of an empty pocket");
a=new UpDownLoad();p=new ThreeNozzleRuntimePair();p.Placed[0]=false;p.ReworkStage[0]=3;p.Accepted[1]=true;a.ThreeNozzleCompletePostPlaceChecks(ref quit,p);Assert(a.checkedSlots.SequenceEqual(new[]{0})&&p.Accepted[0],"stopped replacement must resume even after rejected placement has been removed");
a=new UpDownLoad();p=new ThreeNozzleRuntimePair();COUNT_DATA.allcnt[0]=0;Assert(a.DecisionForTest(ref quit,p,0,false,null)==EM_RES.OK&&!p.Accepted[0]&&a.postChecks==0&&COUNT_DATA.allcnt[0]==0,"actual placement decision defers exposure acceptance and counts");
   Assert(a.DecisionForTest(ref quit,p,0,true,new Cam.VisionOutPutData())==EM_RES.OK&&p.Accepted[0]&&a.postChecks==1&&COUNT_DATA.allcnt[0]==1&&a.afterCloseTargets.Count==1&&a.ErrTryPickCnt==0,"actual completion commits count and after-close target only after verification");
   a=new UpDownLoad();p=new ThreeNozzleRuntimePair();a.checkResult=EM_RES.QUIT;Assert(a.DecisionForTest(ref quit,p,0,true,null)==EM_RES.QUIT&&!p.Accepted[0],"actual verification stop prevents acceptance");
   Console.WriteLine("PASS: "+checks+" actual post-place flight dispatch and rework branch assertions; no hardware.");
  }
'@
$wrapped=$stub+"`n EM_RES ReworkForTest(ref bool quit,ThreeNozzleRuntimePair pair,XT xt,WS.MdDat md){int slot=0;bool completePostPlace=true;EM_RES retry;"+$rework+" return EM_RES.OK;}`n}}"
$wrapped=$wrapped.Substring(0,$wrapped.Length-2)+" EM_RES DecisionForTest(ref bool quit,ThreeNozzleRuntimePair pair,int slot,bool completePostPlace,Cam.VisionOutPutData postPlaceImage){bool countNew=true;var md=pair.Pockets[slot];var xt=list_xt[1];while(!quit){"+$decision+"}return EM_RES.QUIT;}}}"
$helper=[regex]::Replace($helper,'(?m)^using .*;\r?\n','')
Add-Type -TypeDefinition ("using System;using System.Linq;using System.Collections.Generic;using MotionCtrl;using UI.Class;`n"+$helper+$wrapped)
[UI.UpDownLoad]::Run()
$leave=$runtime.IndexOf('case ThreeNozzleAction.LeaveFixture:')
$check=$runtime.IndexOf('ThreeNozzleCompletePostPlaceChecks(ref quit, pair)',$leave)
$preposition=$runtime.IndexOf('ThreeNozzlePrepositionPlaceTray(goDirect',$leave)
$handoff=$runtime.IndexOf('pair.Sequence.Step(a => true);',$leave)
if($check -lt $leave -or $check -gt $preposition -or $check -gt $handoff){throw 'Post-place checks must precede preposition and sequence handoff'}
Write-Output 'PASS: post-placement verification precedes fixture release.'


