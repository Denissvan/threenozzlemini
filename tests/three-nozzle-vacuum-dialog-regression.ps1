$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$path=Join-Path $root 'UI/Class/UpDownLoad.ThreeNozzle.VacuumRecovery.cs'
if (!(Test-Path $path)) { throw 'Missing vacuum auto-correction source' }
$source=Get-Content -Raw -Encoding UTF8 $path
# 与原双吸嘴一致：记录/真空不一致时自动清记录、关真空并继续，不弹窗、不留待处理问题。
$stub=@'
namespace System.Windows.Forms { public enum DialogResult { OK,Cancel,Abort } }
namespace MotionCtrl { public enum EM_RES { OK,QUIT,ERR } }
namespace UI {
 public class Md {public string bardcode="BC";public int res=-1;}
 public class XT {public string disc="N";public Md XtMd;public Vac cy_zk=new Vac();public class Vac{public bool isONByChkSen;public int offs;public void SetOff(){offs++;isONByChkSen=false;}}}
 public class Msg {public enum EM_MSGTYPE {SYS,WAR,ERR}}
 public class Log {public int errors;public void AddMsg(Msg.EM_MSGTYPE type,string text){if(type==Msg.EM_MSGTYPE.ERR)errors++;}}
 public static class VAR {public static Log msg=new Log();public class Sys{public bool bquit;}public static Sys gsys_set=new Sys();}
 public partial class UpDownLoad {
  private string disc="arm";private bool Demo=false;
  private System.Collections.Generic.List<XT> list_xt=new System.Collections.Generic.List<XT>();
  static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new System.Exception(message);}
  public static void Test(){
   bool quit=false;var arm=new UpDownLoad();
   // 1) 真空有料但没有记录（今天现场：料盘吸取失败后吸嘴带料）→ 关真空，一次通过
   var xt=new XT();xt.cy_zk.isONByChkSen=true;
   Check(arm.ThreeNozzleRequireVacuumCorrection(ref quit,xt,false,"tray pick")==MotionCtrl.EM_RES.OK,"stray vacuum part must be corrected and pass at once");
   Check(!xt.cy_zk.isONByChkSen&&xt.cy_zk.offs==1&&xt.XtMd==null&&!arm.ThreeNozzleHasPendingVacuumIssue,"vacuum switched off, no identity invented, nothing pending");
   // 2) 记录有料但真空无料 → 清记录，不改物料对象本身
   var md=new Md();xt=new XT();xt.XtMd=md;
   Check(arm.ThreeNozzleRequireVacuumCorrection(ref quit,xt,true,"before placement")==MotionCtrl.EM_RES.OK&&xt.XtMd==null,"lost part record must be cleared like the dual-nozzle flow");
   Check(md.bardcode=="BC"&&md.res==-1,"material object shared with tray/fixture must not be edited");
   // 3) 记录与真空一致 → 不动
   xt=new XT();xt.XtMd=md;xt.cy_zk.isONByChkSen=true;int before=VAR.msg.errors;
   Check(arm.ThreeNozzleRequireVacuumCorrection(ref quit,xt,true,"consistent")==MotionCtrl.EM_RES.OK&&xt.XtMd==md&&xt.cy_zk.isONByChkSen&&xt.cy_zk.offs==0&&VAR.msg.errors==before,"consistent held part must be left alone");
   // 4) 停止中：校正照做，但返回 QUIT 让调用方停下
   xt=new XT();xt.cy_zk.isONByChkSen=true;quit=true;
   Check(arm.ThreeNozzleRequireVacuumCorrection(ref quit,xt,false,"stopping")==MotionCtrl.EM_RES.QUIT&&!xt.cy_zk.isONByChkSen,"stop keeps stopping but leaves a corrected nozzle");quit=false;
   // 5) 停止打断时登记的待处理吸嘴：下次启动自动校正，不弹窗
   xt=new XT();xt.cy_zk.isONByChkSen=true;arm.threeNozzlePendingVacuum=xt;arm.threeNozzlePendingVacuumWhere="interrupted";
   Check(arm.ThreeNozzleCheckPendingVacuum(ref quit)==MotionCtrl.EM_RES.OK&&!arm.ThreeNozzleHasPendingVacuumIssue&&!xt.cy_zk.isONByChkSen,"pending issue is auto-corrected on next start");
   // 6) 整臂扫描：只校正不一致的吸嘴（今天现场：吸头6 记录有料但真空无料导致吸头4 无限弹窗）
   var a=new XT();var b=new XT();b.XtMd=new Md();var c=new XT();c.XtMd=new Md();c.cy_zk.isONByChkSen=true;
   arm.list_xt.Add(a);arm.list_xt.Add(b);arm.list_xt.Add(c);
   Check(arm.ThreeNozzleSweepVacuumMismatch("sweep")==1&&b.XtMd==null&&c.XtMd!=null&&c.cy_zk.isONByChkSen,"sweep fixes only the mismatched sibling");
   // 7) 校正后再扫描没有需要处理的吸嘴
   Check(arm.ThreeNozzleSweepVacuumMismatch("again")==0,"second sweep has nothing left to correct");
   arm.Demo=true;xt=new XT();xt.cy_zk.isONByChkSen=true;
   Check(arm.ThreeNozzleRequireVacuumCorrection(ref quit,xt,false,"demo")==MotionCtrl.EM_RES.OK&&xt.cy_zk.isONByChkSen,"dry run never switches vacuum");
   System.Console.WriteLine("PASS: "+checks+" dual-nozzle style vacuum auto-correction checks; no dialog, no hardware.");
  }
 }
}
'@
Add-Type -TypeDefinition ($source+$stub)
[UI.UpDownLoad]::Test()
