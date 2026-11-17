param([string]$RepoRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition ((Get-Content -LiteralPath (Join-Path $RepoRoot 'UI/Class/ThreeNozzleCoordinator.cs') -Raw -Encoding UTF8) + @'
namespace MotionCtrl {
 public enum EM_RES { OK, ERR, PARA_ERR, SAFE_PROTECT, QUIT }
 public class CARD { public bool isReady=true; }
 public class AXIS { public enum HOME_STA { OK, ERR }; public HOME_STA home_status=HOME_STA.OK; public CARD card=new CARD(); public double fenc_pos,fcmd_pos; public bool isStop=true; }
 public class Msg { public enum EM_MSGTYPE { ERR, WAR, DBG, SYS }; public void AddMsg(EM_MSGTYPE type,string text){} }
 public class SYS { public bool bquit,isChkMode; }
 public static class VAR { public static SYS gsys_set=new SYS(); public static Msg msg=new Msg(); public static bool ClearMt,isAutoChkMode; }
}
namespace UI {
 public class WS { public enum EM_STA { WAIT, REDAYFORUPDOWNLOAD, REDAYFORTEST }; public enum EM_TEST_STA { ERROR, EMPTY }; public class MdDat { public bool benable; } public System.Collections.Generic.List<MdDat> list_md=new System.Collections.Generic.List<MdDat>(); public EM_TEST_STA TestStatus=EM_TEST_STA.EMPTY; public string disc="station"; public EM_STA FeedStatus=EM_STA.WAIT; public bool isInFeedPos=true; }
 public class Pose { public double y; }
 public class XT { public Pose st_cap_pos=new Pose(); }
 public class UpDownLoad {
  public static bool bquit;
  public static int Stops;
  public MotionCtrl.AXIS ax_x=new MotionCtrl.AXIS(),ax_y=new MotionCtrl.AXIS();
  public System.Collections.Generic.List<XT> list_xt=new System.Collections.Generic.List<XT>{new XT(),new XT(),new XT()};
  public int PrefetchCount,UnloadCount,EarlyCount;
  public string disc="arm";
  public bool ResumeSafe=true,DrainReady=true,SafeStop,ThreeNozzleHasPendingVacuumIssue;
  public bool ThreeNozzleCanResumeAfterStop(){return ResumeSafe;}
  public int Sweeps;public int ThreeNozzleSweepVacuumMismatch(string where){Sweeps++;return 0;}
  public bool ThreeNozzleDrainReady {get{return DrainReady;}}
  public bool ThreeNozzleTakeSafeStop(){bool result=SafeStop;SafeStop=false;return result;}
  public System.Func<MotionCtrl.EM_RES> Early=()=>MotionCtrl.EM_RES.OK;public MotionCtrl.EM_RES ThreeNozzleEarlyPrefetch(ref bool quit,WS ws){System.Threading.Interlocked.Increment(ref EarlyCount);return Early();}
  public System.Func<MotionCtrl.EM_RES> Prefetch=()=>MotionCtrl.EM_RES.OK,Unload=()=>MotionCtrl.EM_RES.OK;
  public MotionCtrl.EM_RES ThreeNozzlePrefetch(ref bool quit,WS ws){ System.Threading.Interlocked.Increment(ref PrefetchCount);return Prefetch(); }
  public MotionCtrl.EM_RES ThreeNozzleUnload(ref bool quit,WS ws){ System.Threading.Interlocked.Increment(ref UnloadCount);return Unload(); }
  public static void StopAllUpDownLoadMotion(){System.Threading.Interlocked.Increment(ref Stops);}
 }
 public static class COM { public static UpDownLoad UDLoad1=new UpDownLoad(),UDLoad2=new UpDownLoad(); }
 public static class ThreeNozzleHardwareConfig { public static double[] Limits={10,10}; public static double SafeTrayMaxY(int arm){return Limits[arm];} }
 public static class PT_SET {public static bool bGrrFlow,HallEn,bUpDn1XtOnlyOne,bUpDn2XtOnlyOne;}
 public static class Turntable {public enum EM_STA {POS1,POS2,POS3}; public static EM_STA GetCurSta=EM_STA.POS1;public static WS GetWSOnPos(EM_STA a,EM_STA b){return null;}}
 public static class MT { public static readonly object MotionGate=new object(); public static bool ZSafe=true; public static bool AreAllArmZSafe(int arm){return ZSafe;} public static void BeQuitEn(bool a,bool b=false){if(b)MotionCtrl.VAR.gsys_set.bquit=a;UpDownLoad.bquit=a;} }
}
namespace UI.ModuleCheck {public static class ModuleCheckHooks {public static bool AffectsStation(UI.WS ws){return false;}}}
namespace CoordinatorTests {
using MotionCtrl;
using UI;
using System;
using System.Threading;
using System.Threading.Tasks;
public static class Regression {
 static void Check(bool pass,string message){if(!pass)throw new Exception(message);}
 static void WaitWorkers(){Check(SpinWait.SpinUntil(()=>ThreeNozzleCoordinator.WorkersStopped,3000),"worker timeout/deadlock");}
 static void Reset(){WaitWorkers();Check(ThreeNozzleCoordinator.ResetAfterHome()==EM_RES.OK,"reset failed");VAR.gsys_set.bquit=false;UpDownLoad.bquit=false;UpDownLoad.Stops=0;COM.UDLoad1=new UpDownLoad();COM.UDLoad2=new UpDownLoad();ThreeNozzleHardwareConfig.Limits=new double[]{10,10};MT.ZSafe=true;}
 static WS Prefetch(){var ws=new WS();Check(ThreeNozzleCoordinator.BeginPrefetch(ws)==EM_RES.OK,"start failed");WaitWorkers();return ws;}
 public static void RunAll(){
  var failures=new System.Collections.Generic.List<string>();
  int cases=0;
  Action<string,Action> test=(name,run)=>{cases++;try{Reset();run();}catch(Exception ex){failures.Add(name+": "+ex.Message);}finally{VAR.gsys_set.bquit=false;UpDownLoad.bquit=false;}};
  test("fast arm enters while peer prefetch is blocked",()=>{
   var method=typeof(ThreeNozzleCoordinator).GetMethod("WaitPrefetch",new[]{typeof(WS),typeof(int)});
   Check(method!=null,"missing per-arm prefetch release");
   using(var release=new ManualResetEventSlim(false))using(var entered=new ManualResetEventSlim(false)){
    COM.UDLoad2.Prefetch=()=>{entered.Set();return release.Wait(3000)?EM_RES.OK:EM_RES.ERR;};
    var ws=new WS{FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD};Task<EM_RES> fast=null;
    try{
     Check(ThreeNozzleCoordinator.BeginPrefetch(ws)==EM_RES.OK&&entered.Wait(1000),"prefetch start");
     fast=Task.Run(()=>(EM_RES)method.Invoke(null,new object[]{ws,0}));
     Check(fast.Wait(400)&&fast.Result==EM_RES.OK,"fast arm waited for peer");
     Check(!ThreeNozzleCoordinator.IsTrayOnlyPhase(0)&&ThreeNozzleCoordinator.IsTrayOnlyPhase(1),"release must affect only own lease");
     Check(!ThreeNozzleCoordinator.CanMoveFixture()||COM.UDLoad1.ax_x.fenc_pos==0,"fixture interlock removed");
    }finally{release.Set();if(fast!=null)fast.Wait(3000);WaitWorkers();}
   }
  });
  test("arm starts tail without peer exchange completion",()=>{
   var releaseArm=typeof(ThreeNozzleCoordinator).GetMethod("WaitPrefetch",new[]{typeof(WS),typeof(int)});
   var tail=typeof(ThreeNozzleCoordinator).GetMethod("BeginArmUnload");
   Check(releaseArm!=null&&tail!=null,"missing independent tail API");
   var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;
   Check((EM_RES)releaseArm.Invoke(null,new object[]{ws,0})==EM_RES.OK,"own release");
   Check((EM_RES)tail.Invoke(null,new object[]{ws,0})==EM_RES.OK,"own tail start");
   Check(SpinWait.SpinUntil(()=>COM.UDLoad1.UnloadCount==1,500),"own tail did not run");
   Check(COM.UDLoad2.UnloadCount==0,"peer tail started before peer exchange");WaitWorkers();
   Check((EM_RES)releaseArm.Invoke(null,new object[]{ws,1})==EM_RES.OK,"peer release");
   Check((EM_RES)tail.Invoke(null,new object[]{ws,1})==EM_RES.OK,"peer tail");WaitWorkers();
   Check(ThreeNozzleCoordinator.BeginUnload(ws)==EM_RES.OK,"shared handoff");WaitWorkers();
   Check(COM.UDLoad1.UnloadCount==1&&COM.UDLoad2.UnloadCount==1,"shared handoff repeated tail");
  });
  test("next station fast arm ignores peer unfinished tail",()=>{
   var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws)==EM_RES.OK,"initial release");
   using(var release=new ManualResetEventSlim(false))using(var entered=new ManualResetEventSlim(false)){
    COM.UDLoad2.Unload=()=>{entered.Set();return release.Wait(3000)?EM_RES.OK:EM_RES.ERR;};
    Task<EM_RES> fast=null;
    try{
     Check(ThreeNozzleCoordinator.BeginUnload(ws)==EM_RES.OK&&entered.Wait(1000),"tail start");
     var next=new WS{FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD};
     Check(ThreeNozzleCoordinator.BeginPrefetch(next)==EM_RES.OK,"next station publication blocked");
     fast=Task.Run(()=>ThreeNozzleCoordinator.WaitPrefetch(next,0));
     Check(fast.Wait(400)&&fast.Result==EM_RES.OK,"fast arm waited for peer tail at next station");
     Check(ThreeNozzleCoordinator.IsTrayOnlyPhase(1)&&!ThreeNozzleCoordinator.IsTrayOnlyPhase(0),"peer unfinished tail lease lost");
    }finally{release.Set();if(fast!=null)fast.Wait(3000);WaitWorkers();}
   }
  });
  test("early prefetch stop never repeats committed tail",()=>{
   var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws)==EM_RES.OK,"entry");
   int attempts=0;COM.UDLoad1.Early=()=>{if(++attempts==1){UpDownLoad.bquit=true;return EM_RES.QUIT;}return EM_RES.OK;};
   var next=new WS{FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD};
   Check(ThreeNozzleCoordinator.BeginUnload(ws,next)==EM_RES.OK,"tail start");WaitWorkers();
   Check(UpDownLoad.Stops==0,"normal early stop locked fault");UpDownLoad.bquit=false;VAR.gsys_set.bquit=false;
   Check(ThreeNozzleCoordinator.BeginPrefetch(next)==EM_RES.OK,"restart rejected");WaitWorkers();
   Check(COM.UDLoad1.UnloadCount==1&&COM.UDLoad1.EarlyCount==2,"already committed tail repeated on early restart");
   Check(COM.UDLoad2.UnloadCount==1,"peer tail repeated");
  });
  test("operator vacuum stop rechecks without recovery latch",()=>{
   int attempts=0;COM.UDLoad1.ResumeSafe=false;COM.UDLoad1.ThreeNozzleHasPendingVacuumIssue=true;
   COM.UDLoad1.Prefetch=()=>{if(++attempts==1){UpDownLoad.bquit=true;return EM_RES.QUIT;}COM.UDLoad1.ThreeNozzleHasPendingVacuumIssue=false;COM.UDLoad1.ResumeSafe=true;return EM_RES.OK;};
   var ws=Prefetch();Check(UpDownLoad.Stops==0,"operator vacuum stop demanded reconciliation");
   UpDownLoad.bquit=false;VAR.gsys_set.bquit=false;
   Check(ThreeNozzleCoordinator.BeginPrefetch(ws)==EM_RES.OK,"current-fault preflight cannot resume");WaitWorkers();
   Check(COM.UDLoad1.PrefetchCount==2&&COM.UDLoad2.PrefetchCount==1,"resumed wrong arm");
  });
  test("independent tail rejects nonfinite X",()=>{
   var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws,0)==EM_RES.OK,"own entry");
   COM.UDLoad1.ax_x.fcmd_pos=double.NaN;
   Check(ThreeNozzleCoordinator.BeginArmUnload(ws,0)==EM_RES.SAFE_PROTECT&&COM.UDLoad1.UnloadCount==0,"invalid own pose dispatched tail");
  });
  test("partial exchange stop preserves completed arm and current epoch",()=>{
   var completed=typeof(ThreeNozzleCoordinator).GetMethod("IsArmExchangeCompleted");Check(completed!=null,"missing per-arm exchange completion identity");
   var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws,0)==EM_RES.OK,"A enters");
   int attempts=0;using(var release=new ManualResetEventSlim(false))using(var entered=new ManualResetEventSlim(false)){
    COM.UDLoad1.Unload=()=>{if(++attempts==1){UpDownLoad.bquit=true;return EM_RES.QUIT;}entered.Set();return release.Wait(3000)?EM_RES.OK:EM_RES.ERR;};
    try{
     Check(ThreeNozzleCoordinator.BeginArmUnload(ws,0)==EM_RES.OK,"A tail");WaitWorkers();
     UpDownLoad.bquit=false;VAR.gsys_set.bquit=false;
     Check(ThreeNozzleCoordinator.BeginPrefetch(ws)==EM_RES.OK&&entered.Wait(1000),"resume A tail");
     Check((bool)completed.Invoke(null,new object[]{ws,0}),"completed arm would repeat exchange during restarted tail");
     Check(!(bool)completed.Invoke(null,new object[]{ws,1}),"incomplete peer incorrectly skipped");
    }finally{release.Set();WaitWorkers();}
   }
   Check(ThreeNozzleCoordinator.WaitPrefetch(ws,1)==EM_RES.OK&&ThreeNozzleCoordinator.BeginArmUnload(ws,1)==EM_RES.OK,"finish B");WaitWorkers();
   Check(ThreeNozzleCoordinator.BeginUnload(ws)==EM_RES.OK,"station handoff");
   Check(ThreeNozzleCoordinator.BeginPrefetch(ws)==EM_RES.OK,"same station next rotation");
   Check(!(bool)completed.Invoke(null,new object[]{ws,0})&&!(bool)completed.Invoke(null,new object[]{ws,1}),"prior rotation completion leaked into new epoch");
  });
  test("mixed independent and post-check tail keeps running arm",()=>{
   var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws)==EM_RES.OK,"exchange ready");
   using(var release=new ManualResetEventSlim(false))using(var entered=new ManualResetEventSlim(false)){
    COM.UDLoad1.Unload=()=>{entered.Set();return release.Wait(3000)?EM_RES.OK:EM_RES.ERR;};
    try{
     Check(ThreeNozzleCoordinator.BeginArmUnload(ws,0)==EM_RES.OK&&entered.Wait(1000),"independent A tail");
     Check(ThreeNozzleCoordinator.BeginUnload(ws)==EM_RES.OK,"post-check B handoff blocked on A tail");
     Check(SpinWait.SpinUntil(()=>COM.UDLoad2.UnloadCount==1,500),"deferred B tail not started");
     Check(COM.UDLoad1.UnloadCount==1,"running A tail duplicated");
    }finally{release.Set();WaitWorkers();}
   }
  });
  test("same-station idempotency",()=>{var ws=Prefetch();Check(ThreeNozzleCoordinator.BeginPrefetch(ws)==EM_RES.OK,"repeat rejected");WaitWorkers();Check(COM.UDLoad1.PrefetchCount==1&&COM.UDLoad2.PrefetchCount==1,"duplicate prefetch");});
  test("not-ready lease retention",()=>{var ws=Prefetch();Check(ThreeNozzleCoordinator.WaitPrefetch(ws)!=EM_RES.OK,"not-ready station admitted");Check(ThreeNozzleCoordinator.IsTrayOnlyPhase(0)&&ThreeNozzleCoordinator.IsTrayOnlyPhase(1),"not-ready released tray lease");ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws)==EM_RES.OK,"ready station rejected");Check(!ThreeNozzleCoordinator.IsTrayOnlyPhase(0)&&!ThreeNozzleCoordinator.IsTrayOnlyPhase(1),"ready lease not transferred");});
  test("worker error stops all and latches fault",()=>{COM.UDLoad1.Prefetch=()=>EM_RES.ERR;var ws=new WS();Check(ThreeNozzleCoordinator.BeginPrefetch(ws)==EM_RES.OK,"dispatch failed");WaitWorkers();Check(UpDownLoad.Stops>0,"worker failure did not stop axes");Check(!ThreeNozzleCoordinator.CanMoveFixture(),"fault permits fixture motion");Check(ThreeNozzleCoordinator.WaitPending()!=EM_RES.OK,"fault discarded");Check(ThreeNozzleCoordinator.BeginUnload(ws)!=EM_RES.OK,"fault permits tail");});
  test("worker exception stops all",()=>{COM.UDLoad1.Prefetch=()=>{throw new InvalidOperationException("injected");};Prefetch();Check(UpDownLoad.Stops>0&&!ThreeNozzleCoordinator.CanMoveFixture(),"exception not latched");});
  test("ordinary stop preserves task and resumes only interrupted arm",()=>{
   int attempts=0;COM.UDLoad1.Prefetch=()=>{if(++attempts==1){UpDownLoad.bquit=true;return EM_RES.QUIT;}return EM_RES.OK;};
   var ws=Prefetch();Check(UpDownLoad.Stops==0,"ordinary QUIT incorrectly latched/stopped both arms");
   Check(ThreeNozzleCoordinator.WaitPending()==EM_RES.QUIT,"active stop ignored");
   UpDownLoad.bquit=false;VAR.gsys_set.bquit=false;
   Check(ThreeNozzleCoordinator.BeginPrefetch(ws)==EM_RES.OK,"start after ordinary stop rejected");WaitWorkers();
   Check(ThreeNozzleCoordinator.WaitPending()==EM_RES.OK,"stale QUIT caused a second stop");
   Check(COM.UDLoad1.PrefetchCount==2&&COM.UDLoad2.PrefetchCount==1,"completed arm repeated or interrupted arm lost");
  });
  test("uncertain material still locks on QUIT",()=>{COM.UDLoad1.ResumeSafe=false;COM.UDLoad1.Prefetch=()=>EM_RES.QUIT;Prefetch();Check(UpDownLoad.Stops>0&&!ThreeNozzleCoordinator.CanMoveFixture(),"uncertain stop was made resumable");});
  test("QUIT without stop request remains a fault",()=>{COM.UDLoad1.Prefetch=()=>EM_RES.QUIT;Prefetch();Check(UpDownLoad.Stops>0,"unexplained QUIT would automatically retry forever");});
  test("concurrent waiters resume one worker only",()=>{
   int attempts=0;COM.UDLoad1.Prefetch=()=>{if(Interlocked.Increment(ref attempts)==1){UpDownLoad.bquit=true;return EM_RES.QUIT;}Thread.Sleep(80);return EM_RES.OK;};
   Prefetch();UpDownLoad.bquit=false;VAR.gsys_set.bquit=false;
   var a=Task.Run(()=>ThreeNozzleCoordinator.WaitPending());var b=Task.Run(()=>ThreeNozzleCoordinator.WaitPending());
   Check(Task.WaitAll(new Task[]{a,b},3000),"concurrent wait deadlock");Check(a.Result==EM_RES.OK&&b.Result==EM_RES.OK,"stale task snapshot rejected another waiter's restart");
   Check(COM.UDLoad1.PrefetchCount==2&&COM.UDLoad2.PrefetchCount==1,"concurrent restart duplicated a worker");
  });
  test("changed stop state blocks resume without discarding task",()=>{
   int attempts=0;COM.UDLoad1.Prefetch=()=>{if(++attempts==1){UpDownLoad.bquit=true;return EM_RES.QUIT;}return EM_RES.OK;};
   Prefetch();UpDownLoad.bquit=false;COM.UDLoad1.ResumeSafe=false;
   Check(ThreeNozzleCoordinator.WaitPending()==EM_RES.SAFE_PROTECT,"changed material state admitted");Check(COM.UDLoad1.PrefetchCount==1,"unsafe resume moved arm");
   COM.UDLoad1.ResumeSafe=true;Check(ThreeNozzleCoordinator.WaitPending()==EM_RES.OK,"blocked task discarded");
  });
  test("tail stop resumes tail before another station",()=>{
   var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws)==EM_RES.OK,"exchange entry");
   int attempts=0;COM.UDLoad1.Unload=()=>{if(++attempts==1){UpDownLoad.bquit=true;return EM_RES.QUIT;}return EM_RES.OK;};
   Check(ThreeNozzleCoordinator.BeginUnload(ws)==EM_RES.OK,"tail dispatch");WaitWorkers();
   Check(UpDownLoad.Stops==0,"ordinary tail stop latched");UpDownLoad.bquit=false;VAR.gsys_set.bquit=false;
   Check(ThreeNozzleCoordinator.BeginPrefetch(new WS())==EM_RES.OK,"new station did not resume old tail");WaitWorkers();
   Check(COM.UDLoad1.UnloadCount==2&&COM.UDLoad2.UnloadCount==1,"tail resume duplicated completed arm");
  });
  test("clear waits for tail before reporting completion",()=>{
   var method=typeof(ThreeNozzleCoordinator).GetMethod("WaitClearCompletion");Check(method!=null,"clear has no tail completion barrier");
   var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws)==EM_RES.OK,"exchange entry");
   using(var release=new ManualResetEventSlim(false))using(var entered=new ManualResetEventSlim(false)){
    COM.UDLoad1.Unload=()=>{entered.Set();return release.Wait(3000)?EM_RES.OK:EM_RES.ERR;};
    Check(ThreeNozzleCoordinator.BeginUnload(ws)==EM_RES.OK,"tail start");Check(entered.Wait(1000),"tail enter");
    var wait=Task.Run(()=>(EM_RES)method.Invoke(null,null));
    try{Check(!wait.Wait(100),"clear completed before tail");}finally{release.Set();Check(wait.Wait(3000),"clear barrier deadlocked");WaitWorkers();}
    Check(wait.Result==EM_RES.OK,"completed empty safe tail rejected");
   }
  });
  test("clear rejects material or unsafe arm",()=>{
   var method=typeof(ThreeNozzleCoordinator).GetMethod("WaitClearCompletion");Check(method!=null,"clear has no state validation");
   COM.UDLoad1.DrainReady=false;Check((EM_RES)method.Invoke(null,null)!=EM_RES.OK,"clear admitted remaining material/unsafe pose");
  });
  test("clear interrupted by stop never permits tray collection",()=>{
   var method=typeof(ThreeNozzleCoordinator).GetMethod("WaitClearCompletion");var ready=typeof(ThreeNozzleCoordinator).GetProperty("CanCompleteClear");
   Check(method!=null&&ready!=null,"clear barrier/collection predicate missing");
   using(var release=new ManualResetEventSlim(false))using(var entered=new ManualResetEventSlim(false)){
    COM.UDLoad1.Prefetch=()=>{entered.Set();release.Wait(3000);return EM_RES.QUIT;};
    Check(ThreeNozzleCoordinator.BeginPrefetch(new WS())==EM_RES.OK&&entered.Wait(1000),"worker start");
    try{UpDownLoad.bquit=true;Check((EM_RES)method.Invoke(null,null)==EM_RES.QUIT,"clear ignored stop");Check(!(bool)ready.GetValue(null,null),"unfinished task permits collection");}
    finally{release.Set();WaitWorkers();}
    Check(!(bool)ready.GetValue(null,null),"completed QUIT permits collection");
   }
  });
  test("tail blocks next cycle and reset",()=>{
   var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws)==EM_RES.OK,"exchange entry failed");
   using(var release=new ManualResetEventSlim(false))using(var entered=new ManualResetEventSlim(false)){
    Task<EM_RES> next=null;COM.UDLoad1.Unload=()=>{entered.Set();if(!release.Wait(3000))return EM_RES.ERR;return EM_RES.OK;};
    try{Check(ThreeNozzleCoordinator.BeginUnload(ws)==EM_RES.OK,"tail start failed");Check(entered.Wait(1000),"tail not entered");Check(ThreeNozzleCoordinator.ResetAfterHome()!=EM_RES.OK,"reset erased running worker");next=Task.Run(()=>ThreeNozzleCoordinator.BeginPrefetch(new WS()));Check(next.Wait(400),"next station dispatch waited for peer tail");Check(COM.UDLoad1.PrefetchCount==1,"next prefetch overlapped tail");}
    finally{release.Set();if(next!=null)Check(next.Wait(3000),"next cycle deadlocked");WaitWorkers();}
    Check(next!=null&&next.Result==EM_RES.OK,"next cycle failed after tail complete");
   }
  });
  test("lease live encoder Y",()=>{Prefetch();COM.UDLoad1.ax_y.fenc_pos=11;Check(!ThreeNozzleCoordinator.CanMoveFixture(),"encoder Y outside lease admitted");});
  test("lease live commanded Y",()=>{Prefetch();COM.UDLoad1.ax_y.fcmd_pos=11;Check(!ThreeNozzleCoordinator.CanMoveFixture(),"command Y outside lease fell back to parked pose");});
  test("lease NaN commanded Y",()=>{Prefetch();COM.UDLoad1.ax_y.fcmd_pos=double.NaN;Check(!ThreeNozzleCoordinator.CanMoveFixture(),"NaN command Y admitted");});
  test("lease NaN encoder Y",()=>{Prefetch();COM.UDLoad1.ax_y.fenc_pos=double.NaN;Check(!ThreeNozzleCoordinator.CanMoveFixture(),"NaN encoder Y admitted");});
  test("parked NaN Y",()=>{COM.UDLoad1.ax_y.fenc_pos=double.NaN;Check(!ThreeNozzleCoordinator.CanMoveFixture(),"parked NaN Y admitted");});
  test("parked commanded Y",()=>{COM.UDLoad1.ax_y.fcmd_pos=11;Check(!ThreeNozzleCoordinator.CanMoveFixture(),"parked unsafe command Y admitted");});
  test("missing safe region",()=>{ThreeNozzleHardwareConfig.Limits[0]=double.NaN;Check(ThreeNozzleCoordinator.BeginPrefetch(new WS())!=EM_RES.OK,"uncommissioned region admitted");Check(COM.UDLoad1.PrefetchCount==0&&COM.UDLoad2.PrefetchCount==0,"partial worker dispatch");});
  test("failed tail start remains failed",()=>{var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;Check(ThreeNozzleCoordinator.WaitPrefetch(ws)==EM_RES.OK,"exchange entry");ThreeNozzleHardwareConfig.Limits[0]=double.NaN;Check(ThreeNozzleCoordinator.BeginUnload(ws)!=EM_RES.OK,"unsafe first tail start admitted");Check(ThreeNozzleCoordinator.BeginUnload(ws)!=EM_RES.OK,"failed tail was marked started");});
  test("dispatch in flight blocks phase changes",()=>{var ws=Prefetch();ws.FeedStatus=WS.EM_STA.REDAYFORUPDOWNLOAD;ThreeNozzleCoordinator.MarkMotionStarted(0);try{Check(ThreeNozzleCoordinator.WaitPrefetch(ws)!=EM_RES.OK,"in-flight dispatch released lease");Check(ThreeNozzleCoordinator.ResetAfterHome()!=EM_RES.OK,"in-flight dispatch reset");Check(ThreeNozzleCoordinator.IsTrayOnlyPhase(0),"dispatch lost lease");}finally{ThreeNozzleCoordinator.MarkMotionEnded(0);}Check(ThreeNozzleCoordinator.WaitPrefetch(ws)==EM_RES.OK,"dispatch completed but lease stuck");});
  Reset();if(failures.Count>0)throw new Exception(string.Join("\n",failures.ToArray()));
  Console.WriteLine("PASS: "+cases+" real coordinator cases, including stop/resume, concurrent waiters and clear completion.");
 }
}
}
'@)
[CoordinatorTests.Regression]::RunAll()
Write-Host 'PASS: real coordinator task ownership, station readiness, failures, tail ordering, reset and live safety bounds.'
