$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$text = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/UpDownLoad.ThreeNozzle.cs')
$methods = [regex]::Match($text, '(?s)        internal bool ThreeNozzleCanResumeAfterStop\(\).*?(?=        // These gates)').Value
$planGate = [regex]::Match($text, '(?s)        private EM_RES ThreeNozzleEnsurePlan\(WS ws\).*?(?=            foreach \(XT xt in list_xt\))').Value + '            return EM_RES.OK; }'
if (!$methods) { throw 'Production stop/drain state methods not found' }
$source = @"
using System;
using System.Linq;
using System.Collections.Generic;
public enum EM_RES {OK,ERR}
public class WS {public int FixtureRecord=7;}
public static class VAR {public static bool ClearMt;public static Logger msg=new Logger();public class Logger {public void AddMsg(Msg.EM_MSGTYPE t,string s){}}}
public static class Msg {public enum EM_MSGTYPE {WAR}}
public class AXIS {
 public enum HOME_STA {OK,ERR}
 public class Card {public bool isReady=true;}
 public Card card=new Card(); public HOME_STA home_status=HOME_STA.OK;
 public bool isALM,isStop=true; public double fenc_pos,fcmd_pos;
}
public class Vacuum {public bool isONByChkSen;}
public class Pose {public double y;}
public class XT {public AXIS ax_z=new AXIS(),ax_u=new AXIS();public object XtMd;public Vacuum cy_zk=new Vacuum();public Pose st_cap_pos=new Pose();}
public static class MT {public const double ArmZSafeTolerance=1; public static bool ZSafe=true;public static bool AreAllArmZSafe(int id){return ZSafe;}}
public class StopStateProbe {
 public enum EM_STA {UNKNOW,HOME,READY} public EM_STA status_ud=EM_STA.READY;
 public bool threeNozzleRecovery,Demo,threeNozzleDrain,ThreeNozzleHasSpecialMode; public int id,threeNozzlePairIndex;public string disc="arm";
 public WS threeNozzleSource; public object threeNozzleEarly;
 public class ThreeNozzleRuntimePair {public object[] Prepared=new object[2];public bool[] Incoming={true,true};}
 public class Pair : ThreeNozzleRuntimePair {}
 public List<Pair> threeNozzlePairs=new List<Pair>();
 public List<XT> list_xt=new List<XT>{new XT(),new XT(),new XT()};
 public AXIS ax_x=new AXIS(),ax_y=new AXIS();
$methods
$planGate
 private string ThreeNozzleUnsupportedReason(){return null;}
 private EM_RES ThreeNozzleError(string text,bool recovery=false){threeNozzleRecovery|=recovery;return EM_RES.ERR;}
 static int checks;
 static void Check(bool pass,string message){checks++;if(!pass)throw new Exception(message);}
 public static void Run(){
  var p=new StopStateProbe();Check(p.ThreeNozzleCanResumeAfterStop()&&p.ThreeNozzleDrainReady,"empty safe arm rejected");
  p.threeNozzleRecovery=true;Check(!p.ThreeNozzleCanResumeAfterStop(),"manual reconciliation lock bypassed");p.threeNozzleRecovery=false;
  p.list_xt[0].XtMd=new object();Check(!p.ThreeNozzleCanResumeAfterStop(),"record without material admitted");
  p.list_xt[0].cy_zk.isONByChkSen=true;Check(p.ThreeNozzleCanResumeAfterStop(),"known held material should remain resumable");Check(!p.ThreeNozzleDrainReady,"held material incorrectly declared cleared");
  p.list_xt[0].XtMd=null;Check(!p.ThreeNozzleCanResumeAfterStop(),"untracked vacuum material admitted");
  p.Demo=true;Check(p.ThreeNozzleCanResumeAfterStop(),"empty dry run requires real vacuum");p.Demo=false;p.list_xt[0].cy_zk.isONByChkSen=false;
  p.threeNozzleSource=new WS();Check(!p.ThreeNozzleDrainReady,"unfinished source plan declared cleared");p.threeNozzleSource=null;
  p.threeNozzleEarly=new object();Check(!p.ThreeNozzleDrainReady,"early prefetch declared cleared");p.threeNozzleEarly=null;
  p.threeNozzlePairs.Add(new Pair());Check(!p.ThreeNozzleDrainReady,"unfinished pairs declared cleared");p.threeNozzlePairs.Clear();
  p.ax_x.fenc_pos=49;Check(p.ThreeNozzleCanResumeAfterStop(),"tray-region X requires zero before resumable classification");Check(!p.ThreeNozzleDrainReady,"tray-region X declared parked");p.ax_x.fenc_pos=0;
  p.ax_y.fcmd_pos=4;Check(!p.ThreeNozzleDrainReady,"unsafe commanded Y declared parked");p.ax_y.fcmd_pos=0;
  foreach(double value in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}){
   p.ax_x.fenc_pos=value;Check(!p.ThreeNozzleCanResumeAfterStop(),"invalid X accepted for stop resume");p.ax_x.fenc_pos=0;
   p.ax_y.fcmd_pos=value;Check(!p.ThreeNozzleCanResumeAfterStop(),"invalid Y command accepted for stop resume");p.ax_y.fcmd_pos=0;
  }
  p.list_xt[2].ax_z.fcmd_pos=-30;Check(!p.ThreeNozzleCanResumeAfterStop(),"Z command still down admitted");p.list_xt[2].ax_z.fcmd_pos=0;
  p.list_xt[1].ax_u.isALM=true;Check(!p.ThreeNozzleCanResumeAfterStop(),"rotation alarm admitted");p.list_xt[1].ax_u.isALM=false;
  p.list_xt[1].ax_u.fcmd_pos=double.NaN;Check(!p.ThreeNozzleCanResumeAfterStop(),"invalid rotation command admitted");p.list_xt[1].ax_u.fcmd_pos=0;
  p.ax_y.isStop=false;Check(!p.ThreeNozzleCanResumeAfterStop(),"moving XY admitted");p.ax_y.isStop=true;
  MT.ZSafe=false;Check(!p.ThreeNozzleCanResumeAfterStop(),"unsafe Z admitted");MT.ZSafe=true;
  Check(p.ThreeNozzleDrainReady,"restored safe empty arm rejected");
  var ws=new WS();p=new StopStateProbe{threeNozzleSource=ws,threeNozzleDrain=true,threeNozzlePairIndex=2};p.threeNozzlePairs.Add(new Pair());
  Check(p.ThreeNozzleEnsurePlan(ws)==EM_RES.OK&&!p.threeNozzleRecovery,"empty old drain plan incorrectly locks next production start");
  Check(p.threeNozzleSource==null&&!p.threeNozzleDrain&&p.threeNozzlePairs.Count==0&&p.threeNozzlePairIndex==0,"old empty drain metadata retained");
  Check(ws.FixtureRecord==7,"releasing empty plan edited fixture material record");
  p=new StopStateProbe{threeNozzleSource=ws,threeNozzleDrain=true};p.list_xt[0].XtMd=new object();p.list_xt[0].cy_zk.isONByChkSen=true;
  Check(p.ThreeNozzleEnsurePlan(ws)!=EM_RES.OK&&p.threeNozzleSource==ws&&p.list_xt[0].XtMd!=null,"unfinished drain with held material was discarded");
  p=new StopStateProbe{threeNozzleSource=ws,threeNozzleDrain=true};p.threeNozzlePairs.Add(new Pair());p.threeNozzlePairs[0].Prepared[0]=new object();
  Check(p.ThreeNozzleEnsurePlan(ws)!=EM_RES.OK&&p.threeNozzlePairs[0].Prepared[0]!=null,"pending prepared identity was discarded");
  p=new StopStateProbe{threeNozzleSource=ws,threeNozzleDrain=true};p.ax_x.fenc_pos=49;
  Check(p.ThreeNozzleEnsurePlan(ws)!=EM_RES.OK&&p.threeNozzleSource==ws,"unparked drain plan discarded");
  p=new StopStateProbe{threeNozzleSource=ws,threeNozzleDrain=true};VAR.ClearMt=true;
  Check(p.ThreeNozzleEnsurePlan(ws)==EM_RES.OK&&p.threeNozzleSource==ws&&p.threeNozzleDrain,"pause during clearing discarded active progress");VAR.ClearMt=false;
  p=new StopStateProbe();var pair=new Pair();Check(!p.ThreeNozzleCancelUnpickedForClear(pair,0)&&pair.Incoming[0],"production incoming was cancelled");
  VAR.ClearMt=true;Check(p.ThreeNozzleCancelUnpickedForClear(pair,0)&&!pair.Incoming[0],"clear switched during tray wait still starts new pick");
  pair.Prepared[1]=new object();Check(!p.ThreeNozzleCancelUnpickedForClear(pair,1)&&pair.Incoming[1]&&pair.Prepared[1]!=null,"already picked material identity discarded");VAR.ClearMt=false;
  Console.WriteLine("PASS: "+checks+" actual stop/drain physical-state checks; no hardware.");
 }
}
"@
Add-Type -TypeDefinition $source
[StopStateProbe]::Run()
