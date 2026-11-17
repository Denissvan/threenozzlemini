$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$text=Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/UpDownLoad.cs')
$start=$text.IndexOf('        private bool ThreeNozzleArmNeedsPostChecks(')
$end=$text.IndexOf('        #endregion',$start)
if ($start -lt 0 -or $end -lt 0) {throw 'Independent UdLoad dispatch implementation missing'}
$methods=$text.Substring($start,$end-$start)
$source=@'
using System;
using MotionCtrl;
namespace MotionCtrl {public enum EM_RES {OK,ERR,QUIT,MOVE_ERR,MOVE_TIMEOUT,MOVE_PARA_ERR}public class AXIS{public int home_status;public bool isALM;public double fenc_pos;}}
namespace UI {
 public static class Msg{public enum EM_MSGTYPE{NOR,ERR,DBG}}
 public class Logger{public void AddMsg(Msg.EM_MSGTYPE type,string message){}}
 public static class VAR{public static bool IsChinese=true,ClearMt;public static Logger msg=new Logger();public class Sys{public bool isChkMode;}public static Sys gsys_set=new Sys();}
 public class WS{public bool bjigSan;public string disc="ws";}
 public static class Turntable{public static WS GetWSOnFeedPos=new WS();}
 public static class PT_SET{public static bool bJigDownPhoto,HallEn,bboxCheck,bJigSan,bBarcodeCamBackEnOnly;public static int UpDownRunMode;public enum RUN_MD{MD1_WORK,MD2_WORK}}
 public static class MT{public static void BeQuitEn(bool quit,bool all=false){UpDownLoad.bquit=quit;}}
 public static class ThreeNozzleCoordinator{
  public static bool[] Completed={false,false};public static int Waits,Tails,LastArm=-1;
  public static bool IsArmExchangeCompleted(WS ws,int arm){return Completed[arm];}
  public static EM_RES WaitPrefetch(WS ws,int arm){Waits++;LastArm=arm;return EM_RES.OK;}
  public static EM_RES BeginArmUnload(WS ws,int arm){Tails++;LastArm=arm;return EM_RES.OK;}
 }
 public class UpDownLoad {
  public enum EM_STA{UNKNOW,HOME,READY,QUIT,ERR}
  private EM_STA status_ud=EM_STA.READY;private bool brun_ud=true;public static bool bquit;
  private string disc="arm",englishdisc="arm";private int FindMod=0,id=0;private AXIS ax_x=new AXIS(),ax_y=new AXIS();private long LastUdTaskMs;
  private bool threeNozzlePostBoxCheck=false;private int exchanges;private EM_RES ThreeNozzleRunExchange(ref bool quit,WS ws){exchanges++;return EM_RES.OK;}
  private string DescribeUdLoadFailure(EM_RES res,bool prefetch){return "test";}private static void StopAllUpDownLoadMotion(){}
  /*METHODS*/
  static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
  static void Reset(){ThreeNozzleCoordinator.Waits=ThreeNozzleCoordinator.Tails=0;ThreeNozzleCoordinator.Completed=new[]{false,false};bquit=false;PT_SET.bJigDownPhoto=PT_SET.HallEn=PT_SET.bboxCheck=PT_SET.bJigSan=PT_SET.bBarcodeCamBackEnOnly=false;PT_SET.UpDownRunMode=0;VAR.gsys_set.isChkMode=false;Turntable.GetWSOnFeedPos.bjigSan=false;}
  public static void Run(){
   Reset();var a=new UpDownLoad{id=1};a.UdLoad();Check(a.exchanges==1&&ThreeNozzleCoordinator.Waits==1&&ThreeNozzleCoordinator.LastArm==1&&ThreeNozzleCoordinator.Tails==1,"normal arm must wait/release/unload only itself");
   Reset();ThreeNozzleCoordinator.Completed[0]=true;a=new UpDownLoad();a.UdLoad();Check(a.exchanges==0&&ThreeNozzleCoordinator.Waits==0&&ThreeNozzleCoordinator.Tails==0,"restart repeated already-completed exchange while own tail running");
   foreach(string mode in new[]{"photo","hall","box","check"}){
    Reset();PT_SET.bJigDownPhoto=mode=="photo";PT_SET.HallEn=mode=="hall";PT_SET.bboxCheck=mode=="box";VAR.gsys_set.isChkMode=mode=="check";
    a=new UpDownLoad{threeNozzlePostBoxCheck=mode=="box"};a.UdLoad();Check(a.exchanges==1&&ThreeNozzleCoordinator.Tails==0,"post-check mode competed with independent tail: "+mode);
   }
   Reset();PT_SET.bboxCheck=true;a=new UpDownLoad();a.UdLoad();Check(ThreeNozzleCoordinator.Tails==1,"enabled box check outside scheduled time unnecessarily delayed tail");
   Reset();PT_SET.bboxCheck=true;PT_SET.bBarcodeCamBackEnOnly=true;a=new UpDownLoad();a.UdLoad();Check(ThreeNozzleCoordinator.Tails==0,"barcode back-check competed with tail");
   Reset();PT_SET.bJigSan=true;Turntable.GetWSOnFeedPos.bjigSan=true;PT_SET.UpDownRunMode=(int)PT_SET.RUN_MD.MD2_WORK;
   a=new UpDownLoad{id=0};a.UdLoad();Check(ThreeNozzleCoordinator.Tails==1,"unrelated arm waited for peer fixture scanner");
   a=new UpDownLoad{id=1};a.UdLoad();Check(ThreeNozzleCoordinator.Tails==1,"scanner arm started competing tail");
   Check(!a.brun_ud&&a.status_ud==EM_STA.READY&&a.LastUdTaskMs>=0,"dispatch exit ownership");
   Console.WriteLine("PASS: "+checks+" actual UdLoad normal/restart/post-check ownership checks.");
  }
 }
}
'@
Add-Type -TypeDefinition $source.Replace('/*METHODS*/',$methods)
[UI.UpDownLoad]::Run()
