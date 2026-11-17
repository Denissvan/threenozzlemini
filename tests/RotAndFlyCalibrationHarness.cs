// Offline harness: compiles the real RotAndFly calibration partial against inert motion/camera stubs.
// Does not reference UI.exe, device SDKs, or the production entry point.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MotionCtrl;
namespace MotionCtrl {
 public enum EM_RES { OK, ERR, PARA_ERR, QUIT }
 public class ST_XYN { public double x,y; public int n; public ST_XYN(double a,double b,int c){x=a;y=b;n=c;} }
 public class AXIS {
  public int Stops;public void Stop(){Stops++;}
  public double[] Triggers; public Dictionary<ushort,double[]> Channels=new Dictionary<ushort,double[]>(); public int Disabled; public bool FailDisable;
  public EM_RES SetHcmp(double[] positions, ushort io){Triggers=positions; Channels[io]=positions; return EM_RES.OK;}
  public EM_RES DisableHcmp(ushort io){Disabled++;return FailDisable?EM_RES.ERR:EM_RES.OK;}
 }
}
namespace UI {
 public class Output {public bool bOK=true,bUpdate=true; public TestPosition PosMM=new TestPosition();}
 public class TestPosition {public double x,y;}
 public class GPIO {public bool isONByChkSen=true;public ushort num;}
 public class XT {public int SlotIndex,id; public UpDownLoad Parent; public TestPosition st_cap_pos=new TestPosition();public GPIO cy_zk=new GPIO();public AXIS ax_x=new AXIS(),ax_y=new AXIS(),ax_z=new AXIS(),ax_u=new AXIS();public double xt_cap_ofs,ws_cap_near_ofs,ws_cap_far_ofs; public void SaveHwCfg(){} }
 public class WS {public class MdDat {public int Num; public TestPosition[] st_pos={new TestPosition(),new TestPosition()};} public List<MdDat> list_md=new List<MdDat>();}
 public class Cam {
  public class VisionTask {public string TaskName;public ST_XYN TriPos;public Output ResData=new Output();}
  public List<VisionTask> List_vs_task=new List<VisionTask>(),List_vs_task_cur=new List<VisionTask>();public VisionTask curTask=new VisionTask();public GPIO TriIO=new GPIO();public int inputImageCnt;
  public EM_RES StopLive(){return EM_RES.OK;}
  public EM_RES FindTaskTriAndWait(string name){return EM_RES.OK;}
  public EM_RES ListTaskCfg(List<string> names,List<ST_XYN> positions){List_vs_task_cur=names.Select((n,i)=>new VisionTask{TaskName=n,TriPos=positions[i]}).ToList();inputImageCnt=names.Count;return EM_RES.OK;}
 }
 public class UpDownLoad {public int id;public bool Demo;public List<XT> list_xt=new List<XT>();public Cam dwcam=new Cam(),upcam=new Cam();public AXIS ax_x=new AXIS(),ax_y=new AXIS();}
 public static class CONST {public static string[] ModDwFw={"N1","N2","N3"};public static string WsUpFw="Fixture";}
 public static class PT_SET {public static bool issmall;public static int bitOpenMode=2;}
 public static class Turntable {public static WS GetWSOnFeedPos;}
 public class Msg {public enum EM_MSGTYPE {ERR,NOR} public void AddMsg(EM_MSGTYPE t,string s){} }
 public static class VAR {public static bool IsChinese=true;public static Msg msg=new Msg();public class Settings {public bool bquit;} public static Settings gsys_set=new Settings();}
 public static class MT {public static Action OnMove;public static void SetAllAxToWorkSpd(){}public static EM_RES ZupMove(ref bool quit,ref AXIS axis,double pos,bool other){if(OnMove!=null)OnMove();return quit?EM_RES.QUIT:EM_RES.OK;} }
 public static class COM {public static List<UpDownLoad> List_UDLoad=new List<UpDownLoad>();}
 public static class NozzleZCalibrationMotion {public static bool IsBusy;public static AXIS[] Registered;
  public static void RegisterAxes(params AXIS[] axes){Registered=axes.Distinct().ToArray();}
  public static void Cancel(){VAR.gsys_set.bquit=true;if(Registered!=null)foreach(var axis in Registered)axis.Stop();}
  public static void RunExclusive(Action a){IsBusy=true;VAR.gsys_set.bquit=false;try{a();}finally{IsBusy=false;}}
 }
}
namespace UI.Compment {
 public partial class RotAndFly : UserControl {
  Control tb_xt1_cap=new Control(),tb_xt2_cap=new Control(),tb_xt1_ofs=new Control(),tb_xt2_ofs=new Control(),tb_ws_cap_before=new Control(),tb_ws_cap_after=new Control(),tb_ws_ofs_before=new Control(),tb_ws_ofs_after=new Control(),lbl_xt1_cap=new Control(),lbl_xt2_cap=new Control(),lbl_frontws=new Control(),lbl_behindws=new Control(),lbl_flycam_offset=new Control(),lbl_flycam_test=new Control(),lbl_mk=new Control();
 }
}
public static class Harness {
 static int checks;
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;}
 static object Call(object obj,string method,params object[] args){try{return obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);}catch(TargetInvocationException e){throw e.InnerException;}}
 static object Field(object o,string name){return o.GetType().GetField(name).GetValue(o);}
 [STAThread] public static int Main(){
  var arm=new UI.UpDownLoad();arm.dwcam.TriIO.num=1;arm.upcam.TriIO.num=2;
  // Intentionally non-physical list order: binding must use SlotIndex, sorting actual Y.
  foreach(int slot in new[]{2,0,1}) {var xt=new UI.XT{SlotIndex=slot,id=slot,Parent=arm};xt.st_cap_pos.y=200-slot*50;arm.list_xt.Add(xt);arm.dwcam.List_vs_task.Add(new UI.Cam.VisionTask{TaskName=UI.CONST.ModDwFw[slot]});}
  arm.upcam.List_vs_task.Add(new UI.Cam.VisionTask{TaskName=UI.CONST.WsUpFw});
  var ws=new UI.WS();for(int n=1;n<=16;n++){var md=new UI.WS.MdDat{Num=n};md.st_pos[0].x=20;md.st_pos[0].y=300+n*10;ws.list_md.Add(md);}
  var control=new UI.Compment.RotAndFly();control.Size=new System.Drawing.Size(794,289);
  Call(control,"InitializeCalibrationResults");Call(control,"RefreshFlyResults",arm);
  var grid=(DataGridView)control.GetType().GetField("flyResults",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(control);
  Check(grid.Rows.Count==5,"grid includes three nozzles plus two fixture rows");
  Check(grid.ColumnHeadersHeight+grid.Rows.Cast<DataGridViewRow>().Sum(r=>r.Height)+2<=grid.Height,"all five result rows fit without scrolling at base scale");
  using(var bitmap=new System.Drawing.Bitmap(control.Width,control.Height)){control.DrawToBitmap(bitmap,control.ClientRectangle);bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"RotAndFly-grid-offline.png"));}
  object samples=null;
  foreach(int mode in new[]{1,2,3,4}){
   UI.PT_SET.bitOpenMode=mode;samples=Call(control,"BuildFlySamples",arm,ws);
   var rows=((System.Collections.IEnumerable)samples).Cast<object>().ToArray();
   Check(rows.Length==(mode<=2?5:4),"independent nozzle/fixture counts mode "+mode);
   int[] expected=mode==1?new[]{7,15}:mode==2?new[]{8,16}:mode==3?new[]{16}:new[]{8};
   Check(rows.Skip(3).Select(r=>((UI.WS.MdDat)Field(r,"Fixture")).Num).SequenceEqual(expected),"preserve physical fixture modes "+mode);
   Check(rows.Take(3).Select(r=>(string)Field(r,"TaskName")).SequenceEqual(new[]{"N1","N2","N3"}),"slot mapping mode "+mode);
  }
  UI.PT_SET.issmall=true;UI.PT_SET.bitOpenMode=3;
  Check(((System.Collections.IEnumerable)Call(control,"BuildFlySamples",arm,ws)).Cast<object>().Count()==5,"small mode retains two rows");UI.PT_SET.issmall=false;
  arm.dwcam.List_vs_task.RemoveAt(0);bool refused=false;try{Call(control,"BuildFlySamples",arm,ws);}catch(InvalidOperationException){refused=true;}Check(refused,"missing N3 template rejected before movement");arm.dwcam.List_vs_task.Add(new UI.Cam.VisionTask{TaskName="N3"});
  arm.list_xt[0].cy_zk.isONByChkSen=false;refused=false;try{Call(control,"BuildFlySamples",arm,ws);}catch(InvalidOperationException){refused=true;}Check(refused,"missing held calibration part refused");arm.list_xt[0].cy_zk.isONByChkSen=true;
  UI.PT_SET.bitOpenMode=2;samples=Call(control,"BuildFlySamples",arm,ws);
  // A camera output is tied to nozzle/fixture identity, irrespective of task order.
  UI.MT.OnMove=()=>{foreach(var cam in new[]{arm.dwcam,arm.upcam})foreach(var t in cam.List_vs_task_cur)t.ResData.PosMM.y=t.TriPos.n+0.25;};
  Check((EM_RES)Call(control,"CaptureFlyPass",arm,samples,false)==EM_RES.OK,"raw pass success");
  var measured=((System.Collections.IEnumerable)samples).Cast<object>().ToArray();
  Check(measured.Take(3).Select(r=>(double)Field(r,"Offset")).SequenceEqual(new[]{0.25,1.25,2.25}),"offset mapped back to N1/N2/N3 identity");
  Check(arm.dwcam.List_vs_task_cur.Select(t=>t.TaskName).SequenceEqual(new[]{"N3","N2","N1"}),"physical trigger order independent of nozzle number");
  UI.MT.OnMove=()=>{foreach(var cam in new[]{arm.dwcam,arm.upcam})foreach(var t in cam.List_vs_task_cur)t.ResData.PosMM.y=0.01;};
  Check((EM_RES)Call(control,"CaptureFlyPass",arm,samples,true)==EM_RES.OK,"corrected pass success");
  Check(arm.ax_y.Channels[1].SequenceEqual(new[]{97.75,148.75,199.75}),"each physical nozzle gets its own measured trigger correction");
  Check(measured.All(r=>Math.Abs((double)Field(r,"Residual")-0.01)<1e-10),"residuals separate from original corrections");
  Check((double)Field(measured[2],"Offset")==2.25,"verification does not overwrite saved candidate");
  UI.MT.OnMove=()=>{foreach(var cam in new[]{arm.dwcam,arm.upcam})foreach(var t in cam.List_vs_task_cur)t.ResData.bOK=false;};
  int before=arm.ax_y.Disabled;Check((EM_RES)Call(control,"CaptureFlyPass",arm,samples,false)==EM_RES.ERR,"NG image rejects pass");Check(arm.ax_y.Disabled==before+2,"both trigger channels cleaned on failure");
  UI.MT.OnMove=()=>{foreach(var cam in new[]{arm.dwcam,arm.upcam})cam.inputImageCnt=0;};Check((EM_RES)Call(control,"CaptureFlyPass",arm,samples,false)==EM_RES.ERR,"exposure count mismatch rejected");
  UI.MT.OnMove=()=>{foreach(var cam in new[]{arm.dwcam,arm.upcam})foreach(var t in cam.List_vs_task_cur)t.ResData.PosMM.y=double.NaN;};Check((EM_RES)Call(control,"CaptureFlyPass",arm,samples,false)==EM_RES.ERR,"nonfinite result rejected");
  UI.MT.OnMove=null;arm.ax_y.FailDisable=true;refused=false;try{Call(control,"CaptureFlyPass",arm,samples,false);}catch(InvalidOperationException){refused=true;}Check(refused,"cleanup failure prevents successful return");
  arm.ax_y.FailDisable=false;UI.VAR.gsys_set.bquit=false;
  UI.COM.List_UDLoad.Add(arm);var other=new UI.UpDownLoad();UI.COM.List_UDLoad.Add(other);
  foreach(int slot in new[]{0,1,2})other.list_xt.Add(new UI.XT{SlotIndex=slot,Parent=other});
  foreach(var testArm in UI.COM.List_UDLoad)foreach(var xt in testArm.list_xt){xt.ax_x=testArm.ax_x;xt.ax_y=testArm.ax_y;}
  var pending=(System.Threading.Tasks.Task<EM_RES>)Call(control,"RunCalibration",new Func<EM_RES>(()=>{
   Check(UI.NozzleZCalibrationMotion.Registered.Length==16,"register both XY plus all six Z/U for cancellation");
   UI.NozzleZCalibrationMotion.Cancel();return EM_RES.QUIT;
  }));
  for(int i=0;i<1000&&!pending.IsCompleted;i++){Application.DoEvents();System.Threading.Thread.Sleep(10);}
  Check(pending.IsCompleted&&pending.Result==EM_RES.QUIT,"cancellation returns through calibration task");
  Check(UI.NozzleZCalibrationMotion.Registered.All(axis=>axis.Stops==1),"cancellation stops every registered motion axis");
  Console.WriteLine("PASS: "+checks+" offline calibration planner/scan checks; no hardware or production assembly loaded");return 0;
 }
}
