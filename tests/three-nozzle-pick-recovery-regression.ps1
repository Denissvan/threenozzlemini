param([string]$RepoRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content (Join-Path $RepoRoot 'UI/Class/UpDownLoad.ThreeNozzle.PickRecovery.cs') -Raw -Encoding UTF8
$vacuum=Get-Content (Join-Path $RepoRoot 'UI/Class/UpDownLoad.ThreeNozzle.VacuumRecovery.cs') -Raw -Encoding UTF8
$source += [regex]::Replace($vacuum, '(?m)^using .*;\r?\n', '')
$stub=@'
namespace System.Windows.Forms { public enum DialogResult { OK,Cancel,Abort,None,Retry } }
namespace MotionCtrl { public enum EM_RES { OK,ERR,QUIT,CAM_ERR,TIMEOUT,PICK_ERR,SAFE_PROTECT,RETRY } }
namespace UI {
using System;using MotionCtrl;using System.Windows.Forms;
public class Product { public enum EM_CM_RES { CAMERR=-4,EMPTY=-2 } public class MdDat { public int res=-1;public string bardcode; } }
public class WS { public string disc="ws"; public class MdDat {public int Num=1,res=0;public string bardcode="p1";} }
public class XT {public string disc="nozzle3";public Product.MdDat XtMd;public Z ax_z=new Z();public class Z {public bool isALM;public double fenc_pos;}public Vac cy_zk=new Vac();public class Vac {public bool isONByChkSen;public int offs;public void SetOff(){offs++;isONByChkSen=false;}} }
public static class VAR {public static bool ClearMt;public static G gsys_set=new G();public static Log msg=new Log();public static Inf sys_inf=new Inf();public class G{public bool bquit;}public class Inf{public void Set(EM_ALM_STA a,string b,int c,bool d){}}public class Log {public void AddMsg(Msg.EM_MSGTYPE a,string b){}} }
public enum EM_ALM_STA {WAR_YELLOW_FLASH,NOR_GREEN}
public static class Msg {public enum EM_MSGTYPE {ERR,WAR,SYS}}
public static class COUNT_DATA {public static int SuctionAllcnt,SuctionErrcnt;}
public class warning {}
public static class MT {public static bool AreAllArmZSafe(int arm){return true;}public static void BeQuitEn(bool q){VAR.gsys_set.bquit=q;}public class ST_WARN {public string ok_txt,cancle_txt,abort_txt,title,msg,lb_msg;public WS ws;}}
public static class ERR_ALM {public enum EmErrItem {UpDownLoadAbnormal}}
public partial class UpDownLoad {
private static readonly object dualArmRecoveryDialogLock=new object();
private System.Collections.Generic.List<XT> list_xt=new System.Collections.Generic.List<XT>();
private int id=0;private bool Demo=false,recoveryLatched;private string disc="arm";private DialogResult answer;private EM_RES gateResult=EM_RES.OK,homeResult=EM_RES.OK;private int dialogs,homes;private Action onDialog;
private EM_RES ThreeNozzleError(string text,bool recovery=false){recoveryLatched|=recovery;return EM_RES.ERR;}
private DialogResult DisplayWarningWithDualArmRecoveryGate(warning f,MT.ST_WARN w,ERR_ALM.EmErrItem e,string s,bool keep,out EM_RES recovery,bool keepPositionOnUpdate=false){lock(dualArmRecoveryDialogLock){dialogs++;recovery=gateResult;if(onDialog!=null)onDialog();return answer;}}
private EM_RES GoZero(ref bool quit,bool wait){homes++;return homeResult;}
private EM_RES StopBackCheckAndReturnHome(ref bool quit,XT xt,string s){quit=true;homes++;return homeResult==EM_RES.OK?EM_RES.ERR:homeResult;}
private static void Check(bool b,string m){if(!b)throw new Exception(m);}
private static void Reset(){VAR.ClearMt=false;VAR.gsys_set.bquit=false;COUNT_DATA.SuctionAllcnt=COUNT_DATA.SuctionErrcnt=0;}
public static void RunRegression(){
bool quit=false;ThreeNozzlePickRecoveryAction action;Reset();var a=new UpDownLoad();var xt=new XT();
foreach(var failure in new[]{EM_RES.PICK_ERR,EM_RES.TIMEOUT,EM_RES.CAM_ERR,EM_RES.PICK_ERR}){var source=new Product.MdDat();Check(a.ThreeNozzleHandleTrayPickFailure(ref quit,xt,source,failure,out action)==EM_RES.OK&&action==ThreeNozzlePickRecoveryAction.NextTrayPocket,"failed tray pocket skipped");Check(source.res==-4&&xt.XtMd==null,"failure not a material transfer");}
Check(a.dialogs==0&&COUNT_DATA.SuctionAllcnt==4&&COUNT_DATA.SuctionErrcnt==4,"first four failures count without dialog");a.answer=DialogResult.Cancel;Check(a.ThreeNozzleHandleTrayPickFailure(ref quit,xt,new Product.MdDat(),EM_RES.TIMEOUT,out action)==EM_RES.OK&&action==ThreeNozzlePickRecoveryAction.ReplaceTray,"fifth failure offers replacement");Check(a.dialogs==1&&a.threeNozzleTrayFailures==0,"replacement resets consecutive count");
a.ThreeNozzleHandleTrayPickFailure(ref quit,xt,new Product.MdDat(),EM_RES.CAM_ERR,out action);a.ThreeNozzleTrayPickSucceeded();Check(a.threeNozzleTrayFailures==0,"real success resets consecutive count");
foreach(var choice in new[]{DialogResult.OK,DialogResult.Abort}){Reset();a=new UpDownLoad();a.threeNozzleTrayFailures=4;a.answer=choice;var r=a.ThreeNozzleHandleTrayPickFailure(ref quit,xt,new Product.MdDat(),EM_RES.CAM_ERR,out action);if(choice==DialogResult.OK)Check(r==EM_RES.CAM_ERR&&a.homes==1&&action==ThreeNozzlePickRecoveryAction.Stop,"home confirmation stops on original error");else Check(r==EM_RES.OK&&VAR.ClearMt&&action==ThreeNozzlePickRecoveryAction.Clear,"clear explicit decision");}
Reset();a=new UpDownLoad();a.answer=DialogResult.Cancel;var stuck=new Product.MdDat();xt.cy_zk.isONByChkSen=true;Check(a.ThreeNozzleHandleTrayPickFailure(ref quit,xt,stuck,EM_RES.CAM_ERR,out action)==EM_RES.OK&&action==ThreeNozzlePickRecoveryAction.NextTrayPocket&&a.dialogs==0&&!a.ThreeNozzleHasPendingVacuumIssue&&!a.recoveryLatched,"dual-nozzle parity: vacuum on after failed pick is switched off and the pocket is skipped without a dialog");Check(stuck.res==-4&&!xt.cy_zk.isONByChkSen&&xt.XtMd==null&&COUNT_DATA.SuctionErrcnt==1,"skipped pocket marked once and nozzle left empty");Reset();
Reset();a=new UpDownLoad();a.answer=DialogResult.OK;var retrySource=new Product.MdDat();
Check(a.ThreeNozzleHandleTrayPickFailure(ref quit,xt,retrySource,EM_RES.PICK_ERR,out action)==EM_RES.OK&&a.dialogs==0&&action==ThreeNozzlePickRecoveryAction.NextTrayPocket,"first physical pick failure advances without dialog");
Check(retrySource.res==-4&&xt.XtMd==null,"failed pocket marked without inventing held material");
var ws=new WS();var md=new WS.MdDat();foreach(var choice in new[]{DialogResult.OK,DialogResult.Abort,DialogResult.Cancel}){quit=false;a=new UpDownLoad();a.answer=choice;var r=a.ThreeNozzleHandleFixturePickFailure(ref quit,ws,md,xt,EM_RES.PICK_ERR,out action);if(choice==DialogResult.OK)Check(r==EM_RES.OK&&action==ThreeNozzlePickRecoveryAction.ConfirmedEmpty,"operator confirms empty pocket");if(choice==DialogResult.Abort)Check(r==EM_RES.OK&&action==ThreeNozzlePickRecoveryAction.RetrySame,"retry same physical nozzle");if(choice==DialogResult.Cancel)Check(r==EM_RES.ERR&&quit&&a.homes==1,"stop preserves safe-home semantics");Check(md.res==0&&md.bardcode=="p1"&&xt.XtMd==null,"decision helper never invents material or clears source");}
quit=false;a=new UpDownLoad();a.answer=DialogResult.OK;a.onDialog=()=>{if(a.dialogs==1)xt.cy_zk.isONByChkSen=true;else a.answer=DialogResult.Cancel;};Check(a.ThreeNozzleHandleFixturePickFailure(ref quit,ws,md,xt,EM_RES.PICK_ERR,out action)==EM_RES.OK&&action==ThreeNozzlePickRecoveryAction.RetrySame&&a.dialogs==1&&!a.ThreeNozzleHasPendingVacuumIssue&&!a.recoveryLatched,"vacuum appearing at give-up is switched off and the same pocket retried, no endless dialog");Check(md.res==0&&!xt.cy_zk.isONByChkSen,"give-up with vacuum must not clear the fixture record");Reset();
a=new UpDownLoad();a.answer=DialogResult.Abort;a.gateResult=EM_RES.SAFE_PROTECT;Check(a.ThreeNozzleHandleFixturePickFailure(ref quit,ws,md,xt,EM_RES.PICK_ERR,out action)==EM_RES.SAFE_PROTECT&&action==ThreeNozzlePickRecoveryAction.Stop,"dual-arm gate error propagates");
a=new UpDownLoad();Check(a.ThreeNozzleHandleFixturePickFailure(ref quit,ws,md,xt,EM_RES.SAFE_PROTECT,out action)==EM_RES.SAFE_PROTECT&&a.recoveryLatched&&action==ThreeNozzlePickRecoveryAction.Stop,"unknown fixture result latches recovery and retains code");
a=new UpDownLoad();Check(a.ThreeNozzleHandleTrayPickFailure(ref quit,xt,new Product.MdDat(),EM_RES.SAFE_PROTECT,out action)==EM_RES.SAFE_PROTECT&&a.recoveryLatched,"unknown tray result latches recovery and retains code");
a=new UpDownLoad();quit=true;xt.cy_zk.isONByChkSen=true;Check(a.ThreeNozzleHandleTrayPickFailure(ref quit,xt,new Product.MdDat(),EM_RES.PICK_ERR,out action)==EM_RES.QUIT&&!a.ThreeNozzleHasPendingVacuumIssue&&!xt.cy_zk.isONByChkSen,"stop still stops, vacuum already corrected so next start has nothing pending");quit=false;
a=new UpDownLoad();a.threeNozzleTrayFailures=4;a.answer=DialogResult.OK;a.homeResult=EM_RES.SAFE_PROTECT;Check(a.ThreeNozzleHandleTrayPickFailure(ref quit,xt,new Product.MdDat(),EM_RES.CAM_ERR,out action)==EM_RES.SAFE_PROTECT,"home error not swallowed");
// Both arms fail before the first operator confirmation; the second must revalidate after waiting.
Reset();
using(var entered=new System.Threading.ManualResetEventSlim(false))using(var release=new System.Threading.ManualResetEventSlim(false)){
 var arm1=new UpDownLoad();var arm2=new UpDownLoad();var md1=new WS.MdDat();var md2=new WS.MdDat();var xt1=new XT();var xt2=new XT();
 arm1.answer=DialogResult.Retry;arm1.onDialog=()=>{entered.Set();if(!release.Wait(3000))throw new Exception("confirmation timeout");md1.res=-2;md2.res=-2;};
 bool q1=false,q2=false;ThreeNozzlePickRecoveryAction action1=ThreeNozzlePickRecoveryAction.Stop,action2=ThreeNozzlePickRecoveryAction.Stop;
 var first=System.Threading.Tasks.Task.Run(()=>arm1.ThreeNozzleHandleFixturePickFailure(ref q1,ws,md1,xt1,EM_RES.PICK_ERR,out action1));
 Check(entered.Wait(1000),"first dialog not entered");
 var second=System.Threading.Tasks.Task.Run(()=>arm2.ThreeNozzleHandleFixturePickFailure(ref q2,ws,md2,xt2,EM_RES.PICK_ERR,out action2));
 bool premature;
 try{premature=second.Wait(100);}finally{release.Set();Check(System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[]{first,second},3000),"queued fixture recovery deadlock");}
 Check(!premature,"second arm did not wait for shared operator confirmation");
 Check(first.Result==EM_RES.OK&&second.Result==EM_RES.OK&&action1==ThreeNozzlePickRecoveryAction.ConfirmedEmpty&&action2==ThreeNozzlePickRecoveryAction.ConfirmedEmpty,"confirmed empty decision not shared");
 Check(arm1.dialogs==1&&arm2.dialogs==0,"second arm repeated obsolete dialog after operator cleared fixture");
 Check(xt1.XtMd==null&&xt2.XtMd==null,"skip manufactured nozzle material");
}
Reset();quit=false;a=new UpDownLoad();md=new WS.MdDat{res=-2};xt=new XT();xt.cy_zk.isONByChkSen=true;
a.answer=DialogResult.Cancel;Check(a.ThreeNozzleHandleFixturePickFailure(ref quit,ws,md,xt,EM_RES.PICK_ERR,out action)==EM_RES.OK&&action==ThreeNozzlePickRecoveryAction.ConfirmedEmpty&&a.dialogs==0&&!a.ThreeNozzleHasPendingVacuumIssue&&!a.recoveryLatched&&!xt.cy_zk.isONByChkSen,"operator-emptied pocket with vacuum on: vacuum switched off, continue without dialog");Reset();
xt.cy_zk.isONByChkSen=false;a=new UpDownLoad();quit=true;
Check(a.ThreeNozzleHandleFixturePickFailure(ref quit,ws,md,xt,EM_RES.PICK_ERR,out action)==EM_RES.QUIT&&a.dialogs==0,"queued stop must not be converted to successful skip");
Reset();quit=false;a=new UpDownLoad();a.answer=DialogResult.Abort;md=new WS.MdDat{res=1};xt=new XT();
Check(a.ThreeNozzleHandleFixturePickFailure(ref quit,ws,md,xt,EM_RES.PICK_ERR,out action)==EM_RES.OK&&a.dialogs==1&&action==ThreeNozzlePickRecoveryAction.RetrySame,"another occupied pocket must retain its own recovery dialog");
Check(md.res==1&&xt.XtMd==null,"recovery check must not automatically clear fixture records");
}
}
}
'@
Add-Type -TypeDefinition ($source+[Environment]::NewLine+$stub)
[UI.UpDownLoad]::RunRegression()
Write-Output 'PASS: tray failures/counts/five-attempt decisions, fixture give-up/retry/stop, physical ambiguity, safety-gate and home failure propagation.'

