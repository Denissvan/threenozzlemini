$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$text=Get-Content -Raw -Encoding UTF8 (Join-Path $root 'MotionCtrl/MyType.cs')
$start=$text.IndexOf('    public class ERR_ALM')
$end=$text.IndexOf('    #endregion',$start)
$type=$text.Substring($start,$end-$start)
$mt=Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/MT.cs')
$display=[regex]::Match($mt,'(?s)        public static DialogResult Display_frwarn\(.*?(?=        //门禁提示)').Value
$logger=Get-Content -Raw -Encoding UTF8 (Join-Path $root 'MotionCtrl/MSG.cs')
$record=[regex]::Match($logger,'(?s)        public long AddDialogAlarm\(.*?(?=        public void AddMsg\()').Value
if (!$display -or !$record) {throw 'Actual dialog acknowledgement implementation missing'}
$integration=@'
public enum DialogResult {OK,Cancel}
public enum Color {Yellow,Red}
public class Widget {public string Text;public bool Visible;}
public class warning {
 public Widget btn_ok=new Widget(),btn_cancle=new Widget(),btn_abort=new Widget(),pnl_ws=new Widget(),pnl_wspos=new Widget(),lb_msg=new Widget();
 public object ws;public System.Collections.Generic.List<int> ws_num;public Color BackColor;public string title;public bool TopMost;
 public System.Func<DialogResult> Show=()=>DialogResult.OK;
 public DialogResult ShowDialog(){return Show();}
}
public static class DReport {public enum EmErrCode {Null}}
public class Log {
 private object LockObj=new object();public enum EM_MSGTYPE {ERR}
 public void AddMsg(EM_MSGTYPE type,string message,DReport.EmErrCode err,int hw,ERR_ALM.EmErrItem item){if(item!=ERR_ALM.EmErrItem.Null)VAR.SysErrAlm.SetCurrent(item,message);}
 /*RECORD*/
}
public static class VAR {
 public static bool IsChinese=true,IsErrAlm;
 public static ERR_ALM SysErrAlm=new ERR_ALM();public static Log msg=new Log();
 public class Info {public string info="运行";}public static Info sys_inf=new Info();
}
public static class MT {
 public class ST_WARN {public string ok_txt="OK",cancle_txt="Stop",abort_txt="不用",msg="failure",lb_msg="failure",title="failure";public object ws;public System.Collections.Generic.List<int> ws_num;}
 /*DISPLAY*/
}
public static class DialogTest {
 static void Check(bool ok,string text){if(!ok)throw new System.Exception(text);}
 public static void Run(){
  var warn=new MT.ST_WARN();var dialog=new warning {Show=()=>DialogResult.Cancel};
  Check(MT.Display_frwarn(dialog,warn,ERR_ALM.EmErrItem.UpDownLoadAbnormal)==DialogResult.Cancel&&VAR.SysErrAlm.ErrItem==ERR_ALM.EmErrItem.Null,"stop choice left old dialog alarm");
  dialog=new warning{Show=()=>{VAR.msg.AddDialogAlarm("new concurrent failure",ERR_ALM.EmErrItem.UpDownLoadAbnormal);return DialogResult.OK;}};
  MT.Display_frwarn(dialog,warn,ERR_ALM.EmErrItem.UpDownLoadAbnormal);
  Check(VAR.SysErrAlm.ErrStr=="new concurrent failure","dialog cleared newer concurrent failure");
  var empty=new MT.ST_WARN{msg=""};MT.Display_frwarn(new warning(),empty,ERR_ALM.EmErrItem.UpDownLoadAbnormal);
  Check(VAR.SysErrAlm.ErrStr=="new concurrent failure","empty dialog cleared another alarm");
  VAR.SysErrAlm.Clear();dialog=new warning{Show=()=>{throw new System.InvalidOperationException();}};bool threw=false;
  try{MT.Display_frwarn(dialog,warn,ERR_ALM.EmErrItem.UpDownLoadAbnormal);}catch(System.InvalidOperationException){threw=true;}
  Check(threw&&VAR.SysErrAlm.ErrItem==ERR_ALM.EmErrItem.Null,"dialog exception retained stale alarm");
  System.Console.WriteLine("PASS: 4 actual Display_frwarn/AddDialogAlarm integration checks.");
 }
}
'@
$integration=$integration.Replace('/*RECORD*/',$record).Replace('/*DISPLAY*/',$display)
$test=@'
public static class AlarmTest {
 static int checks;static void Check(bool ok,string text){checks++;if(!ok)throw new System.Exception(text);}
 public static void Run(){
  var alarm=new ERR_ALM();
  long first=alarm.SetCurrent(ERR_ALM.EmErrItem.UpDownLoadAbnormal,"pick failed");
  Check(alarm.ClearIfCurrent(first)&&alarm.ErrItem==ERR_ALM.EmErrItem.Null,"handled dialog alarm must clear");
  first=alarm.SetCurrent(ERR_ALM.EmErrItem.UpDownLoadAbnormal,"same error");
  long newer=alarm.SetCurrent(ERR_ALM.EmErrItem.UpDownLoadAbnormal,"same error");
  Check(!alarm.ClearIfCurrent(first)&&alarm.ErrStr=="same error","newer identical alarm must survive");
  Check(alarm.ClearIfCurrent(newer),"newer dialog acknowledgement");
  first=alarm.SetCurrent(ERR_ALM.EmErrItem.UpDownLoadAbnormal,"old");alarm.Clear();
  newer=alarm.SetCurrent(ERR_ALM.EmErrItem.UpDownLoadAbnormal,"new");
  Check(!alarm.ClearIfCurrent(first)&&alarm.Revision==newer,"reset invalidates old token");
  Check(!alarm.ClearIfCurrent(0),"no-alarm dialog must not clear another alarm");
  System.Console.WriteLine("PASS: "+checks+" production alarm-generation acknowledgement checks.");
 }
}
'@
Add-Type -TypeDefinition ('using System.ComponentModel;'+$type+$test+$integration)
[AlarmTest]::Run()
[DialogTest]::Run()
