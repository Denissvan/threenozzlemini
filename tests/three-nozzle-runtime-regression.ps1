param([string]$RepoRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
# Compile the actual runtime adapter and sequence against narrow hardware fakes.
# Fakes record primitive commands; they do not reimplement exchange decisions.
$adapter = Get-Content (Join-Path $RepoRoot 'UI/Class/UpDownLoad.ThreeNozzle.cs') -Raw
$recovery = Get-Content (Join-Path $RepoRoot 'UI/Class/UpDownLoad.ThreeNozzle.PickRecovery.cs') -Raw
$returns = Get-Content (Join-Path $RepoRoot 'UI/Class/UpDownLoad.ThreeNozzle.Returns.cs') -Raw
$sequence = Get-Content (Join-Path $RepoRoot 'UI/Class/ThreeNozzleSequence.cs') -Raw
$fakes = @'
using System;
using System.Collections.Generic;
using System.Linq;
namespace MotionCtrl {
 public enum EM_RES { OK, ERR, ABORT, END, QUIT, RETRY, CAM_ERR_PLACE_BACK, MOTOR_CAM_ERR_PLACE_BACK, CAM_ERR, TIMEOUT, PICK_ERR }
 public class AXIS {public double fenc_pos;public bool isStop=true;}
 public struct ST_XY { public double x,y; public ST_XYZA ToXYZA(){return new ST_XYZA{x=x,y=y};} }
 public struct ST_XYA {public double x,y,a; public ST_XY ToXY(){return new ST_XY{x=x,y=y};} public ST_XYZA ToXYZA(){return new ST_XYZA{x=x,y=y,a=a};}}
 public struct ST_XYZ { public double x,y,z; public ST_XY ToXY(){return new ST_XY{x=x,y=y};} public ST_XYZA ToXYZA(){return new ST_XYZA{x=x,y=y,z=z};}}
 public struct ST_XYZA {public ST_XYZA clone(){return this;}public double x,y,z,a; public static ST_XYZA operator +(ST_XYZA a,ST_XYZA b){return new ST_XYZA{x=a.x+b.x,y=a.y+b.y,z=a.z+b.z};} public static ST_XYZA operator -(ST_XYZA a,ST_XYZA b){return new ST_XYZA{x=a.x-b.x,y=a.y-b.y,z=a.z-b.z};}}
 public static class CONST {public static string[] ModDwFw={"dw0","dw1","dw2"};public const string ModUpFw="module",WsUpFw="fixture",TrayUpFw="tray";}
 public class Msg {public enum EM_MSGTYPE {ERR,WAR} public void AddMsg(EM_MSGTYPE t,string s){UI.Fake.Log.Add("error:"+s);}}
}
namespace System.Windows.Forms {public enum DialogResult{OK,Cancel,Abort}}
namespace UI.Class { }
namespace UI {
 using MotionCtrl;
 public enum EM_ALM_STA{WAR_YELLOW_FLASH,NOR_GREEN}public class warning{}public static class MultiLanguage{public static string TxtSelct(params string[] text){return text[0];}}public static class ERR_ALM{public enum EmErrItem{MaterialPosErr,UpDownLoadAbnormal}}
 public static class NozzleZCalibration { public static double ToAxisZ(int arm,int slot,double z,int direction){return direction*z;} }
 public static class Fake { public static List<string> Log=new List<string>(); public static int FailPick=-1, FeedNumber=0; public static bool FailVision; public static int RetryAfterPlace, PauseAfterPlace;public static bool FailHomeAfterRework,FailReturnMotion;public static EM_RES ScanFailure=EM_RES.OK;public static int ScanFailures,TrayPickFailures,FixturePickFailures;public static System.Windows.Forms.DialogResult RecoveryChoice=System.Windows.Forms.DialogResult.Cancel; }
 public static class PT_SET {
  public enum BAR_SCAN {NO_SCAN,UP_SCAN,DW_SCAN}
  public static int BarcodeMode,EquipmentMT;
  public static bool bWsPickAgain,bGrrFlow,bAddCapQrcode,bDwAddCapQrcode,bmotorphoto,bdownqr,TrayBarcodeEn,bBarcodeCamBackEn,OpenDownQrde,bOkCheck,bUpDn1XtOnlyOne,bUpDn2XtOnlyOne,isopendown_degree,bModPasteUp,bNgControl,isopen_degree,bEnVsTray,bEnVsFB,Isaloneset;
  public static double downdegree,degree;
 }
 public static class VAR {public static bool isAutoChkMode,ClearMt,Isnormal=true;public static Sys gsys_set=new Sys();public static Msg msg=new Msg();public static Alarm sys_inf=new Alarm();public class Alarm{public void Set(EM_ALM_STA state,string text,int value,bool on){}}public class Sys{public bool isChkMode,bquit;}}
 public static class NewSysInf {public static Params UserParams=new Params();public class Params{public bool bXtPrePlaceTrayByBigId,bPickWsDis,bPlaceWsDis,bGetOrcodOnWs;public bool bUpWsChkQrCodeOff=true;}}
 public static class COUNT_DATA {public static int SuctionErrcnt,SuctionAllcnt,ngctrlallcnt,CurEquipmentMT;public static int[] allcnt=new int[8];}
 public class Cam {public class VisionOutPutData {public ST_XYA PosMM;public string BarCode;}public class VisionTask{public string TaskName;public VisionOutPutData ResData=new VisionOutPutData();}public List<VisionTask> List_vs_task=new List<VisionTask>{new VisionTask{TaskName="dw0"},new VisionTask{TaskName="dw1"},new VisionTask{TaskName="dw2"}};public VisionTask curTask=new VisionTask();public EM_RES FindTaskTriAndWait(string name,bool demo){Fake.Log.Add("cam:"+name);return EM_RES.OK;}}
 public class Product {
  public enum EM_CM_RES{EMPTY=-2,UNTEST=-1,OK=0,CAMERR=19}
  public class MdDat {public int Num,WS_ID,PC_ID,test_idx,TestBox_ID,SN,res,ct,idx;public string PC_IP,bardcode,motor_barcode,tray_barcode;public bool benable,bhaveMotoScan;public ST_XYZ st_pos;public MdDat Clone(){return (MdDat)MemberwiseClone();}}
  public class Tray {
   public class PosInf{public MdDat md;public int idx;public ST_XYZA[] Pos={new ST_XYZA(),new ST_XYZA()};}
   public bool Feed;public List<PosInf> list_pos=new List<PosInf>(),list_AddCapQrcodepos=new List<PosInf>();
   public List<PosInf> GetPosList(){return list_pos.Where(p=>p.md!=null&&p.md.res==-1).ToList();}
   public List<PosInf> GetPosList(EM_CM_RES r){return list_pos.Where(p=>p.md==null).ToList();}
   public List<PosInf> GetPosList(EM_CM_RES r,int result){return GetPosList(r);}
   public void Push(MdDat md,int idx){list_pos.First(p=>p.idx==idx).md=md.Clone();Fake.Log.Add("push:"+md.bardcode+":"+md.res);}
  }
 }
 public class WS { public string disc="fixture"; public enum EM_STA{REDAYFORUPDOWNLOAD,REDAYFORTEST}public EM_STA FeedStatus;public int num;public List<MdDat> list_md=new List<MdDat>();public class MdDat {
  public int Num,WS_ID,PC_ID,test_idx,TestBox_ID,SN,res,ct;public string PC_IP,bardcode,motor_barcode;public bool benable,IsNormal;public ST_XYZ[] st_pos={new ST_XYZ(),new ST_XYZ()},st_pickpos={new ST_XYZ(),new ST_XYZ()};public MdDat Clone(){return (MdDat)MemberwiseClone();}
 }}
 public class TrayBox {public bool IsReady=true,runin,IsFeedTray,IsOkTray,trayActionRunning,ChgML;public AXIS ax_x=new AXIS();public Product.Tray tray_cur=new Product.Tray();public int Changes;public EM_RES Tray_Action(bool demo){Changes++;Fake.Log.Add("change");IsReady=true;foreach(var p in tray_cur.list_pos)p.md=IsFeedTray?new Product.MdDat{res=-1,bardcode="refill"}:null;return EM_RES.OK;}}
 public class Cylinder{public bool isONByChkSen;}
 public class XT {
  public ST_XYZA xt_pos_pick_mod;public double xt_pos_pick_tray_x;public string disc="nozzle";public int id{get{return Slot;}}public int Slot;public int ZApproachDirection=1;public int SlotIndex{get{return Slot;}}public Product.MdDat XtMd;public Cylinder cy_zk=new Cylinder();public Cam dwcam=new Cam();public AXIS ax_u=new AXIS();public ST_XYA st_rol_cap,st_vs_pos_xtshp;public ST_XYZ st_cap_pos;
  public enum EM_XTFLOW{PICKMOD,PLACEMOD}
  public ST_XYZA CaliPos(ST_XYZA p,double x=0){return p;}
  public EM_RES XtCapMovMod(ref bool q,out string barcode,ST_XYZA p,string flow,double x,EM_XTFLOW f,bool pick,bool demo,ST_XYZA capQrcodePos=new ST_XYZA()){barcode="new"+(++Fake.FeedNumber);Fake.Log.Add("prefetch:"+Slot);if(Fake.TrayPickFailures>0){Fake.TrayPickFailures--;return EM_RES.CAM_ERR;}cy_zk.isONByChkSen=true;return EM_RES.OK;}
  public EM_RES Pick(ref bool q,ST_XYZA p,bool a,bool demo,bool b,bool c){Fake.Log.Add("pick:"+Slot);if(Fake.FixturePickFailures>0){Fake.FixturePickFailures--;return EM_RES.PICK_ERR;}if(Fake.FailPick==Slot)return EM_RES.ERR;cy_zk.isONByChkSen=true;return EM_RES.OK;}
  public EM_RES UpCam(ref bool q,ST_XY p,string f,ref Cam.VisionOutPutData v,bool demo){Fake.Log.Add("cam:"+f);return EM_RES.OK;}
  public EM_RES XtPickOrPlaceMod(ref bool q,ST_XY p,ST_XYA u,ST_XYA d,double z,bool demo,EM_XTFLOW f,bool a,bool b,bool c){Fake.Log.Add("place:"+Slot);cy_zk.isONByChkSen=false;return EM_RES.OK;}
  public EM_RES PickOrPlace(ref bool q,ST_XYZA p,bool pick,bool demo){Fake.Log.Add("return:"+Slot);cy_zk.isONByChkSen=false;return EM_RES.OK;}
  public EM_RES Place(ref bool q,ST_XYZA p,bool a,bool demo){Fake.Log.Add("unload:"+Slot);cy_zk.isONByChkSen=false;return EM_RES.OK;}
 }
 public static class MT {
  public class ST_WARN{public object ws;public string ok_txt,cancle_txt,abort_txt,title,msg,lb_msg;}public static System.Windows.Forms.DialogResult Display_frwarn(warning w,ST_WARN text,ERR_ALM.EmErrItem item){Fake.Log.Add("manual-return");return System.Windows.Forms.DialogResult.Cancel;}
  public static EM_RES ZupMove(ref bool q,ref AXIS a,double av,ref AXIS b,double bv,ref AXIS c,double cv){return Fake.FailReturnMotion?EM_RES.ERR:EM_RES.OK;}
  public static EM_RES ZupMove(ref bool q,ref AXIS a,double av,ref AXIS b,double bv,ref AXIS c,double cv,ref AXIS d,double dv){return EM_RES.OK;}
  public static EM_RES RaiseAllArmZ(ref bool q,int id){Fake.Log.Add("raise");return EM_RES.OK;}
 }
 public partial class UpDownLoad {
  public enum EM_STA { UNKNOW, HOME, READY } public EM_STA status_ud = EM_STA.READY;
  public enum EM_FIN_MOD {NONE,ALL,LOW,HIGH}public EM_FIN_MOD FindMod=EM_FIN_MOD.ALL;public int id;public bool Demo;public string disc="arm";public List<XT> list_xt=new List<XT>{new XT{Slot=0},new XT{Slot=1},new XT{Slot=2}};
  private static object picktray=new object(),placetray=new object();public AXIS ax_x=new AXIS(),ax_y=new AXIS();public Cam upcam=new Cam();public TrayBox traybox_fd=new TrayBox{IsFeedTray=true},traybox_ok=new TrayBox{IsOkTray=true},traybox_ng=new TrayBox();
  public class AfterCloseTarget{public AfterCloseTarget(WS.MdDat m,double x,int id){}}private List<AfterCloseTarget> afterCloseTargets=new List<AfterCloseTarget>();private static void AddAfterCloseTargets(List<AfterCloseTarget> t,IEnumerable<AfterCloseTarget> s){t.AddRange(s);}
  public EM_RES PlaceGrrForTest(ref bool quit,WS ws,Product.MdDat held){
   threeNozzleMode=ThreeNozzleMode.GrrReload;threeNozzleSource=ws;
   var pair=new ThreeNozzleRuntimePair();pair.Pockets[0]=ws.list_md[0];pair.Incoming[0]=true;
   pair.Prepared[0]=held;pair.DownReady[0]=true;pair.DownVision[0]=new Cam.VisionOutPutData();
   list_xt[1].XtMd=held;list_xt[1].cy_zk.isONByChkSen=true;
   return ThreeNozzlePlaceOne(ref quit,pair,0,false);
  }
  public EM_RES ReturnForTest(ref bool quit,XT xt,Product.Tray tray,Product.Tray.PosInf pos,bool rejected){return ThreeNozzleReturnPrepared(ref quit,xt,tray,pos,new ST_XYZA(),0,rejected);}
  private System.Windows.Forms.DialogResult DisplayWarningWithDualArmRecoveryGate(warning form,MT.ST_WARN warn,ERR_ALM.EmErrItem item,string text,bool gate,out EM_RES recovery){Fake.Log.Add("recovery-dialog");recovery=EM_RES.OK;return Fake.RecoveryChoice;}
  private EM_RES StopBackCheckAndReturnHome(ref bool q,XT xt,string text){return EM_RES.QUIT;}
  private EM_RES ThreeNozzleGetTrayCompensation(ref bool q,TrayBox tray,MotionCtrl.ST_XYZA pos,out MotionCtrl.ST_XYA compensation){compensation=new ST_XYA();return EM_RES.OK;}private void ThreeNozzleInvalidateTrayCompensation(TrayBox tray){}
  private int ErrTryPickCnt=0,ErrMotorTryPickCnt=0;private void Camcfgset(uint light=0){}
  private readonly HashSet<Product.MdDat> threeNozzleFinishedBarcodeChecks=new HashSet<Product.MdDat>(),threeNozzlePendingBarcodeWarnings=new HashSet<Product.MdDat>();
  private EM_RES ThreeNozzleCheckFinishedDownBarcode(ref bool q,WS ws,XT xt){return EM_RES.OK;}
  private readonly Dictionary<Product.MdDat,string> threeNozzleDownMotorCodes=new Dictionary<Product.MdDat,string>();
  private readonly HashSet<Product.MdDat> threeNozzleMotorScanReady=new HashSet<Product.MdDat>();
  private enum ThreeNozzleMode{Normal,GrrReload}private ThreeNozzleMode threeNozzleMode=ThreeNozzleMode.Normal;private bool threeNozzleModeDone=false;private void ThreeNozzleResetModesRuntime(){}
  private void ThreeNozzleInitializeMode(WS ws){}
  private bool ThreeNozzleHasSpecialMode {get{return false;}}
  private bool ThreeNozzleCanLoadPocket(WS.MdDat md){return true;}
  private EM_RES ThreeNozzleRunSpecialMode(ref bool q,WS ws){return EM_RES.OK;}
  private EM_RES ThreeNozzleValidateScanningConfiguration(){return EM_RES.OK;}
  private EM_RES ThreeNozzleCheckFeedTrayBarcode(ref bool q){return EM_RES.OK;}
  private EM_RES ThreeNozzlePrepareScanning(ref bool q,XT xt,ref Cam.VisionOutPutData v){if(Fake.ScanFailures>0){Fake.ScanFailures--;return Fake.ScanFailure;}return DwCamAction(ref q,xt,"",ref v);}
  private void ThreeNozzleBindScanning(XT xt,WS.MdDat md){Fake.Log.Add("bind");md.bardcode=xt.XtMd.bardcode;md.motor_barcode=xt.XtMd.motor_barcode;}
  private EM_RES ThreeNozzleCheckBeforePick(ref bool q,WS ws,XT xt,WS.MdDat md){return EM_RES.OK;}
  private EM_RES ThreeNozzleCheckAfterPlace(ref bool q,WS ws,XT xt,WS.MdDat md){
   if(md.res==-2)throw new Exception("Inspection replayed on empty pocket");Fake.Log.Add("inspect:"+md.Num);
   if(Fake.PauseAfterPlace>0){Fake.PauseAfterPlace--;return EM_RES.ERR;}
   if(Fake.RetryAfterPlace>0){Fake.RetryAfterPlace--;md.res=19;return EM_RES.RETRY;}
   return EM_RES.OK;
  }
  public EM_RES GoZero(ref bool q,bool full=false){Fake.Log.Add("leave");if(Fake.FailHomeAfterRework&&Fake.Log.Contains("place:1")){Fake.FailHomeAfterRework=false;return EM_RES.ERR;}return EM_RES.OK;}public EM_RES ProcessPositioningYield(ref bool q){return EM_RES.OK;}
  public EM_RES DwCamAction(ref bool q,XT xt,string flow,ref Cam.VisionOutPutData v){Fake.Log.Add("down:"+xt.Slot);return Fake.FailVision?EM_RES.ERR:EM_RES.OK;}
 }
 public static class RuntimeRegression {
  static void Check(bool c,string m){if(!c)throw new Exception(m+"; trace="+String.Join(",",Fake.Log));}
  static UpDownLoad Arm(){Fake.Log.Clear();Fake.FeedNumber=0;Fake.FailPick=-1;Fake.FailVision=false;Fake.RetryAfterPlace=0;Fake.PauseAfterPlace=0;Fake.FailHomeAfterRework=false;Fake.FailReturnMotion=false;Fake.ScanFailure=EM_RES.OK;Fake.ScanFailures=0;Fake.TrayPickFailures=0;Fake.FixturePickFailures=0;Fake.RecoveryChoice=System.Windows.Forms.DialogResult.Cancel;PT_SET.bWsPickAgain=false;COUNT_DATA.allcnt[0]=0;VAR.ClearMt=false;PT_SET.BarcodeMode=1;var a=new UpDownLoad();for(int i=0;i<12;i++){a.traybox_fd.tray_cur.list_pos.Add(new Product.Tray.PosInf{idx=i,md=new Product.MdDat{res=-1,tray_barcode="source",motor_barcode="motor"}});a.traybox_ok.tray_cur.list_pos.Add(new Product.Tray.PosInf{idx=i});a.traybox_ng.tray_cur.list_pos.Add(new Product.Tray.PosInf{idx=i});}return a;}
  static WS Station(bool old,int pairs){var w=new WS();for(int i=0;i<16;i++){bool en=i%8<pairs;w.list_md.Add(new WS.MdDat{Num=i+1,benable=en,res=old&&en?(i<8?0:27):-2,bardcode="old"+(i+1),motor_barcode="m"+(i+1),PC_ID=42});}return w;}
  public static void Run(){bool quit=false;
   var unhomed=Arm();unhomed.status_ud=UpDownLoad.EM_STA.UNKNOW;Check(unhomed.ThreeNozzlePrefetch(ref quit,Station(false,1))!=EM_RES.OK && !Fake.Log.Any(s=>s.StartsWith("prefetch:")),"unhomed cannot prefetch");
   var a=Arm();var w=Station(true,2);Check(a.ThreeNozzlePrefetch(ref quit,w)==EM_RES.OK,"prefetch");Check(!Fake.Log.Any(s=>s.StartsWith("pick:")||s.StartsWith("place:")),"no fixture transfer during prefetch");Check(a.list_xt[0].XtMd==null&&a.list_xt[1].XtMd.bardcode=="new1"&&a.list_xt[2].XtMd.bardcode=="new2","incoming slots and UP barcode");
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK,"normal exchange");var moves=Fake.Log.Where(s=>s.StartsWith("pick:")||s.StartsWith("place:")).ToArray();Check(String.Join(",",moves)=="pick:0,place:1,pick:1,place:2,pick:0,place:1,pick:1,place:2","two pair ordering");Check(COUNT_DATA.allcnt[0]==4,"exact placement counts");Check(a.list_xt[0].XtMd.bardcode=="old2"&&a.list_xt[1].XtMd.res==27,"tail identities before handoff");Fake.Log.Clear();Check(a.ThreeNozzleUnload(ref quit,w)==EM_RES.OK,"tail");Check(!Fake.Log.Any(s=>s=="cam:fixture"||s.StartsWith("pick:")||s.StartsWith("place:")),"tail must not reenter fixture operations");Check(a.traybox_ng.tray_cur.list_pos.Count(p=>p.md!=null&&p.md.res==27)==2,"preserve extended NG code");Check(a.list_xt.All(x=>x.XtMd==null),"tail empty");
   a=Arm();w=Station(false,1);Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK,"empty fixture");Check(!Fake.Log.Any(s=>s.StartsWith("pick:")),"empty no old pick");Check(a.ThreeNozzleUnload(ref quit,w)==EM_RES.OK,"empty tail completion");
   // The next cycle retains incoming tray provenance through the live fixture record.
   w.list_md[0].res=0;w.list_md[8].res=27;Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK,"next cycle");Check(a.list_xt[0].XtMd.tray_barcode=="source"&&a.list_xt[0].XtMd.motor_barcode=="motor","identity survives fixture cycle");
   a=Arm();w=Station(true,1);VAR.ClearMt=true;Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK,"drain");Check(!Fake.Log.Any(s=>s.StartsWith("prefetch:")||s.StartsWith("place:")),"clear no incoming");Check(a.ThreeNozzleUnload(ref quit,w)==EM_RES.OK,"drain tail");VAR.ClearMt=false;
   a=Arm();w=Station(false,1);Fake.FailVision=true;Check(a.ThreeNozzlePrefetch(ref quit,w)!=EM_RES.OK,"vision failure");Fake.FailVision=false;Check(a.ThreeNozzlePrefetch(ref quit,w)==EM_RES.OK,"vision retry");Check(Fake.Log.Count(s=>s=="prefetch:1")==1,"first successful pick not replayed");
   a=Arm();w=Station(true,1);Fake.FailPick=1;Check(a.ThreeNozzleRunExchange(ref quit,w)!=EM_RES.OK&&a.ThreeNozzleRecoveryRequired,"uncertain PickB latches");int calls=Fake.Log.Count;Check(a.ThreeNozzleRunExchange(ref quit,w)!=EM_RES.OK,"cannot blindly resume");Check(Fake.Log.Skip(calls).All(s=>s.StartsWith("error:")),"locked no motion");Check(a.ThreeNozzleResetAfterManualReconciliation(true)!=EM_RES.OK,"cannot reset occupied nozzles");foreach(var xt in a.list_xt){xt.XtMd=null;xt.cy_zk.isONByChkSen=false;}Check(a.ThreeNozzleResetAfterManualReconciliation(false)!=EM_RES.OK,"requires explicit confirmation");Check(a.ThreeNozzleResetAfterManualReconciliation(true)==EM_RES.OK&&!a.ThreeNozzleRecoveryRequired,"manual empty reconciliation unlocks");
   a=Arm();w=Station(true,1);a.traybox_fd.tray_cur.list_pos.ForEach(p=>p.md=null);Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK&&a.traybox_fd.Changes==1,"feed tray change internal");a.traybox_ok.tray_cur.list_pos.ForEach(p=>p.md=new Product.MdDat());Check(a.ThreeNozzleUnload(ref quit,w)==EM_RES.OK&&a.traybox_ok.Changes==1,"output tray change internal");
   a=Arm();w=Station(false,1);Check(a.ThreeNozzlePrefetch(ref quit,w)==EM_RES.OK,"prefetch before disable");var held=a.list_xt[1].XtMd;w.list_md[0].benable=false;Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK&&!a.ThreeNozzleRecoveryRequired,"disabled target returns prepared material");Check(a.list_xt[1].XtMd==null&&a.traybox_fd.tray_cur.list_pos.Any(p=>Object.ReferenceEquals(p.md,held)&&p.md.res==-1)&&!Fake.Log.Contains("place:1"),"disabled target returns the original untested material without placing it");
   // Real PlaceOne executes re-pick, tray NG transfer, replacement prefetch and
   // replacement placement, with the other held materials still accounted for.
   a=Arm();w=Station(true,1);Fake.RetryAfterPlace=1;
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK,"post-place RETRY replacement completes");
   moves=Fake.Log.Where(s=>s.StartsWith("pick:")||s.StartsWith("place:")).ToArray();
   Check(String.Join(",",moves)=="pick:0,place:1,pick:1,place:1,pick:1,place:2","rework must use the newly emptied incoming nozzle");
   Check(Fake.Log.Count(s=>s=="prefetch:1")==2&&Fake.Log.Count(s=>s=="prefetch:2")==1,"replenish rejected A only, retain prepared B");
   Check(a.traybox_ng.tray_cur.list_pos.Count(p=>p.md!=null&&p.md.bardcode=="new1"&&p.md.res==19)==1,"rejected material physically committed to NG once");
   Check(a.list_xt[0].XtMd.bardcode=="old1"&&a.list_xt[1].XtMd.bardcode=="old9","rework must not unload either old finished identity early");
   Check(w.list_md[0].bardcode=="new3"&&w.list_md[8].bardcode=="new2"&&COUNT_DATA.allcnt[0]==2,"only accepted replacement counts, correct fixture identities");
   Check(a.ThreeNozzleUnload(ref quit,w)==EM_RES.OK,"rework tail completes");
   Check(a.traybox_ng.tray_cur.list_pos.Count(p=>p.md!=null)==2,"rework NG plus old B NG both retained");
   a=Arm();w=Station(true,1);Fake.RetryAfterPlace=1;Fake.FailHomeAfterRework=true;
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.ERR,"rework retreat failure propagates");
   Check(w.list_md[0].res==-2&&a.list_xt[1].XtMd.bardcode=="new1","rework pick commits before failed retreat");
   Check(Fake.Log.Count(s=>s=="pick:1")==1,"rework picked once before pause");
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK&&!a.ThreeNozzleRecoveryRequired,"resume rework at retreat stage");
   Check(Fake.Log.Count(s=>s=="pick:1")==2,"resume cannot pick rejected part twice; second pick is original B");
   Check(Fake.Log.Count(s=>s=="inspect:1")==2,"inspection only sees original and replacement, never empty pocket");
   Check(a.traybox_ng.tray_cur.list_pos.Count(p=>p.md!=null&&p.md.bardcode=="new1")==1,"paused rework must unload rejected part exactly once");
   Check(w.list_md[0].bardcode=="new3"&&COUNT_DATA.allcnt[0]==2,"paused replacement identity and count");
   // A stop from inspection occurs after physical placement committed. Resume
   // inspection rather than placing the same part a second time.
   a=Arm();w=Station(false,1);Fake.PauseAfterPlace=1;
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.ERR,"inspection pause propagates");
   Check(a.list_xt[1].XtMd==null&&w.list_md[0].bardcode=="new1","placed identity committed before inspection pause");
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK,"inspection resumes");
   Check(Fake.Log.Count(s=>s=="place:1")==1&&COUNT_DATA.allcnt[0]==2,"inspection retry does not replay place or count twice");
   a=Arm();w=Station(true,1);Fake.FixturePickFailures=1;Fake.RecoveryChoice=System.Windows.Forms.DialogResult.OK;
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK,"confirmed-empty pick recovers");
   Check(a.list_xt[0].XtMd==null&&Fake.Log.Count(s=>s=="pick:0")==1,"confirmed empty must not manufacture finished A");
   Check(a.ThreeNozzleUnload(ref quit,w)==EM_RES.OK&&!a.traybox_ok.tray_cur.list_pos.Any(p=>p.md!=null),"confirmed empty cannot book an OK tray unload");
   a=Arm();w=Station(true,1);Fake.FixturePickFailures=1;PT_SET.bWsPickAgain=true;
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK,"automatic fixture retry succeeds");
   Check(Fake.Log.Count(s=>s=="pick:0")==2&&!Fake.Log.Contains("recovery-dialog"),"bWsPickAgain performs exactly one automatic retry");
   a=Arm();w=Station(true,1);Fake.FixturePickFailures=1;Fake.RecoveryChoice=System.Windows.Forms.DialogResult.Abort;
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK&&Fake.Log.Count(s=>s=="pick:0")==2&&Fake.Log.Count(s=>s=="recovery-dialog")==1,"operator retry uses the same nozzle and pocket");
   a=Arm();w=Station(false,1);Fake.TrayPickFailures=1;
   Check(a.ThreeNozzlePrefetch(ref quit,w)==EM_RES.OK,"tray failed pocket advances");
   Check(a.traybox_fd.tray_cur.list_pos[0].md.res==19&&a.list_xt[1].XtMd.bardcode=="new2","failed pocket remains CAMERR and cannot become nozzle identity");
   a=Arm();w=Station(false,1);Fake.TrayPickFailures=5;Fake.RecoveryChoice=System.Windows.Forms.DialogResult.Cancel;
   Check(a.ThreeNozzlePrefetch(ref quit,w)==EM_RES.OK&&a.traybox_fd.Changes==1,"five failures allow explicit replacement and resume");
   Check(Fake.Log.Count(s=>s=="recovery-dialog")==1&&Fake.Log.Count(s=>s=="prefetch:1")==6,"five failures prompt once then successful replacement pick");
   a=Arm();w=Station(true,1);Fake.TrayPickFailures=5;Fake.RecoveryChoice=System.Windows.Forms.DialogResult.Abort;
   Check(a.ThreeNozzleRunExchange(ref quit,w)==EM_RES.OK&&VAR.ClearMt,"tray failure clear decision completes drain exchange");
   Check(Fake.Log.Count(s=>s.StartsWith("prefetch:"))==5&&!Fake.Log.Any(s=>s.StartsWith("place:")),"clear must not take a sixth new part or place new material");
   a=Arm();w=Station(false,1);PT_SET.bmotorphoto=true;PT_SET.bDwAddCapQrcode=true;
   var grrMaterial=new Product.MdDat{res=-1,bardcode="grr-original",motor_barcode="bad",tray_barcode="grr-source"};
   Check(a.PlaceGrrForTest(ref quit,w,grrMaterial)==EM_RES.OK,"GRR reload succeeds with existing short motor code");
   Check(w.list_md[0].res==-1&&w.list_md[0].bardcode=="grr-original"&&w.list_md[0].motor_barcode=="bad","GRR retains same code and UNTEST state without production NG classification");
   Check(!Fake.Log.Contains("bind")&&!Fake.Log.Any(s=>s.StartsWith("inspect:")||s.StartsWith("prefetch:")||s.StartsWith("pick:")||s.StartsWith("return:")),"GRR bypasses production barcode bind, after-check and replacement flow");
   Check(Fake.Log.Count(s=>s=="place:1")==1&&COUNT_DATA.allcnt[0]==0&&a.list_xt[1].XtMd==null,"GRR performs one placement without new-production count");
   PT_SET.bmotorphoto=false;PT_SET.bDwAddCapQrcode=false;
   foreach(var failure in new[]{EM_RES.CAM_ERR_PLACE_BACK,EM_RES.MOTOR_CAM_ERR_PLACE_BACK}){
    a=Arm();w=Station(false,1);Fake.ScanFailures=1;Fake.ScanFailure=failure;
    Check(a.ThreeNozzlePrefetch(ref quit,w)==EM_RES.OK,"recoverable scan failure returns and replenishes");
    Check(Fake.Log.Count(s=>s=="return:1")==1&&Fake.Log.Count(s=>s=="prefetch:1")==2,"scan failure returns only failed nozzle once");
    Check(a.traybox_fd.tray_cur.list_pos[0].md.res==19&&a.traybox_fd.tray_cur.list_pos[0].md.bardcode=="new1","returned scan reject stays represented as CAMERR");
    Check(a.list_xt[1].XtMd.bardcode=="new2"&&a.list_xt[2].XtMd.bardcode=="new3","replacement uses next UNTEST source");
   }
   a=Arm();var originalTray=a.traybox_fd.tray_cur;var source=originalTray.list_pos[0];var returnMd=source.md;source.md=null;
   a.list_xt[1].XtMd=returnMd;a.list_xt[1].cy_zk.isONByChkSen=true;Fake.FailReturnMotion=true;
   Check(a.ReturnForTest(ref quit,a.list_xt[1],originalTray,source,true)==EM_RES.ERR,"failed return positioning propagates");
   Check(Object.ReferenceEquals(a.list_xt[1].XtMd,returnMd)&&source.md==null&&!Fake.Log.Contains("return:1"),"failed positioning cannot clear or physically place material");
   Fake.FailReturnMotion=false;a.traybox_fd.tray_cur=new Product.Tray();Fake.Log.Clear();
   Check(a.ReturnForTest(ref quit,a.list_xt[1],originalTray,source,true)==EM_RES.ERR,"source tray changed requires manual choice");
   Check(Fake.Log.Contains("manual-return")&&!Fake.Log.Contains("return:1")&&Object.ReferenceEquals(a.list_xt[1].XtMd,returnMd),"manual stop keeps identity and never returns into replacement tray");
   NewSysInf.UserParams.bPickWsDis=true;NewSysInf.UserParams.bUpWsChkQrCodeOff=false;PT_SET.bEnVsFB=true;
   Check(UpDownLoad.ThreeNozzleUnsupportedReason()==null,"legacy production features must not be rejected by blanket mode gates");
   NewSysInf.UserParams.bPickWsDis=false;NewSysInf.UserParams.bUpWsChkQrCodeOff=true;PT_SET.bEnVsFB=false;
  }
 }
}
'@
# Multiple compilation units retain their own using directives.
$temp = Join-Path ([IO.Path]::GetTempPath()) ('three-nozzle-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp) | Out-Null
try {
    [IO.File]::WriteAllText((Join-Path $temp 'Adapter.cs'), $adapter)
    [IO.File]::WriteAllText((Join-Path $temp 'Recovery.cs'), $recovery)
    [IO.File]::WriteAllText((Join-Path $temp 'Returns.cs'), $returns)
    [IO.File]::WriteAllText((Join-Path $temp 'Sequence.cs'), $sequence)
    [IO.File]::WriteAllText((Join-Path $temp 'Fakes.cs'), $fakes)
    Add-Type -Path @((Join-Path $temp 'Adapter.cs'), (Join-Path $temp 'Sequence.cs'), (Join-Path $temp 'Fakes.cs'), (Join-Path $temp 'Returns.cs'), (Join-Path $temp 'Recovery.cs'))
    [UI.RuntimeRegression]::Run()
    Write-Host 'PASS: actual runtime adapter - exchange/grouping, empty/drain, identity, retry, pure tray tail, changes and manual recovery.'
} finally {
    # Delete only the three files and the exact fresh directory created above.
    foreach ($name in @('Adapter.cs','Sequence.cs','Fakes.cs','Returns.cs','Recovery.cs')) { Remove-Item -LiteralPath (Join-Path $temp $name) -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
}

if ([regex]::Matches($adapter,'position\.z = NozzleZCalibration\.ToAxisZ\(id, xt\.SlotIndex, md\.st_pickpos\[id\]\.z, xt\.ZApproachDirection\)').Count -ne 1) {throw 'Fixture pickup must convert process Z exactly once'}
if ([regex]::Matches($adapter,'pos\.z = NozzleZCalibration\.ToAxisZ\(id, xt\.SlotIndex, pos\.z, xt\.ZApproachDirection\)').Count -ne 1) {throw 'Finished tray placement must convert process Z exactly once'}
Write-Host 'PASS: fixture pickup and finished tray placement each use the shared Z conversion once.'
