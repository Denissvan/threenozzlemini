using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UI;
namespace MotionCtrl {
 public enum EM_RES { OK, ERR }
 public enum EM_SYS_STA { IDLE, RUN, PAUSE, RESET }
 public class Card { public bool isReady=true; }
 public class AXIS {
  public enum HOME_STA { OK }
  public Card card=new Card(); public HOME_STA home_status; public bool isStop=true;
  public double fenc_pos, sln=-100, slp=100, spd_cur=20;
  public int Stops; public bool FailStop; public ManualResetEventSlim Stopped=new ManualResetEventSlim();
  public bool FailRestore;
  public EM_RES SetSpeed(double v){spd_cur=v;return FailRestore&&v==20?EM_RES.ERR:EM_RES.OK;}
  public EM_RES Stop(){Interlocked.Increment(ref Stops);isStop=true;Stopped.Set();return FailStop?EM_RES.ERR:EM_RES.OK;}
 }
 public class GPIO {public enum IO_STA {IN_ON, IN_OFF, ERR} public Card card=new Card(); public IO_STA State=IO_STA.IN_OFF; public Func<IO_STA> Read; public IO_STA Status {get{return Read==null?State:Read();}} public int Off;public bool FailOff;public EM_RES SetOff(){Off++;return FailOff?EM_RES.ERR:EM_RES.OK;}}
 public class Cylinder {public GPIO io_sen_on=new GPIO();public GPIO.IO_STA io_sen_on_active=GPIO.IO_STA.IN_ON;public bool On,FailOff;public int Off;public bool isONByChkSen{get{return io_sen_on.Status==io_sen_on_active;}}public EM_RES SetOff(){On=false;Off++;return FailOff?EM_RES.ERR:EM_RES.OK;}public EM_RES SetOn(){On=true;return EM_RES.OK;}}
}
namespace UI {
 using MotionCtrl;
 public static class VAR {public static Settings gsys_set=new Settings();public class Settings {public bool bquit;public EM_SYS_STA status;}}
 public static class BaseAction {public static bool bRuning,brun;}
 public class UpDownLoad {public static bool brun;public bool brun_ud;public static Task UpDownLoadTask;public Task UpDownTask;public List<XT> list_xt=new List<XT>();}
 public static class COM {public static List<UpDownLoad> List_UDLoad=new List<UpDownLoad>();}
 public static class ThreeNozzleCoordinator {public static bool WorkersStopped=true;}
 public static class ThreeNozzleHardwareConfig {public static bool IsConfigured=true;}
 public class XT {public int ZApproachDirection=1;public AXIS ax_x=new AXIS(),ax_y=new AXIS(),ax_z=new AXIS(),ax_u=new AXIS();public object XtMd;public Cylinder cy_zk=new Cylinder();public GPIO gpio_pzk=new GPIO();}
 public static class MT {
  public static Action<AXIS,double> OnMove;public static int Moves;
  public static EM_RES Move(ref bool q,ref AXIS a,double p){if(q)return EM_RES.ERR;Moves++;if(OnMove!=null)OnMove(a,p);if(q)return EM_RES.ERR;a.fenc_pos=p;return EM_RES.OK;}
  public static EM_RES RaiseAllArmZ(ref bool q,int arm){foreach(var x in COM.List_UDLoad[arm].list_xt){var z=x.ax_z;var r=Move(ref q,ref z,0);if(r!=EM_RES.OK)return r;}return EM_RES.OK;}
  public static EM_RES ZupMove(ref bool q,ref AXIS x,double px,ref AXIS y,double py){var r=Move(ref q,ref x,px);return r==EM_RES.OK?Move(ref q,ref y,py):r;}
 }
}
public static class ZCalibrationMotionHarness {
 static void Assert(bool b,string s){if(!b)throw new Exception(s);}
 static void Reject(Action a){bool failed=false;try{a();}catch{failed=true;}Assert(failed,"expected rejection");}
 static NozzleZCalibrationData Setup(){COM.List_UDLoad.Clear();MT.OnMove=null;MT.Moves=0;VAR.gsys_set.bquit=false;for(int a=0;a<2;a++){var arm=new UpDownLoad();for(int n=0;n<3;n++)arm.list_xt.Add(new XT());COM.List_UDLoad.Add(arm);}var d=new NozzleZCalibrationData();for(int a=0;a<2;a++)for(int n=0;n<3;n++)d.Arms[a].Nozzles[n].Point=new NozzleZCalibrationDraft.PointDraft{X=1,Y=2,U=0,Approach=1,End=2};return d;}
 public static void Run(){var failures=new List<string>();Action<string,Action> test=(name,body)=>{try{body();}catch(Exception ex){failures.Add(name+": "+ex.Message);}};
  test("loaded preflight keeps vacuum and axes",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];x.XtMd=new object();x.cy_zk.On=true;Reject(()=>NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Measure(d,0,0)));Assert(x.cy_zk.On&&x.cy_zk.Off==0&&x.gpio_pzk.Off==0&&x.ax_z.Stops==0&&MT.Moves==0,"preflight touched hardware");});
  test("sensor fault aborts before further descent",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];int descents=0;x.cy_zk.io_sen_on.Read=()=>descents>0?MotionCtrl.GPIO.IO_STA.ERR:MotionCtrl.GPIO.IO_STA.IN_OFF;MT.OnMove=(a,p)=>{if(a==x.ax_z&&p>1)descents++;};Reject(()=>NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Measure(d,0,0)));Assert(descents==1,"continued descent after IO fault: "+descents);});
  test("standalone positioning restores speed",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Position(d,0,0));Assert(x.ax_z.spd_cur==20,"probe speed leaked");});
  test("preflight IO error has no hardware side effects",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];x.cy_zk.io_sen_on.State=MotionCtrl.GPIO.IO_STA.ERR;Reject(()=>NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Measure(d,0,0)));Assert(MT.Moves==0&&x.cy_zk.Off==0&&x.ax_z.Stops==0,"IO error accepted in preflight");});
  test("active-low contact is respected",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];x.cy_zk.io_sen_on_active=MotionCtrl.GPIO.IO_STA.IN_OFF;x.cy_zk.io_sen_on.State=MotionCtrl.GPIO.IO_STA.IN_ON;NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Position(d,0,0));Assert(MT.Moves>0,"inactive active-low input rejected");});
  test("failed positioning restores speed",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];MT.OnMove=(a,p)=>{if(a==x.ax_z&&p==1)throw new Exception("driver fault");};Reject(()=>NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Position(d,0,0)));Assert(x.ax_z.spd_cur==20,"speed leaked after error");});
  test("cancel stops in-flight movement",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];var entered=new ManualResetEventSlim();bool prompt=false;MT.OnMove=(a,p)=>{if(a==x.ax_x){a.isStop=false;entered.Set();prompt=a.Stopped.Wait(500);}};var task=Task.Run(()=>{try{NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Position(d,0,0));}catch{}});Assert(entered.Wait(2000),"movement never started");NozzleZCalibrationMotion.Cancel();Assert(task.Wait(2000),"worker did not exit");Assert(prompt&&x.ax_x.Stops>0,"cancel waited for normal completion");});
  test("cancel during driver dispatch race is re-stopped",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];bool stopped=false;MT.OnMove=(a,p)=>{if(a==x.ax_x){NozzleZCalibrationMotion.Cancel();a.Stopped.Reset();a.isStop=false;stopped=a.Stopped.Wait(500);}};Reject(()=>NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Position(d,0,0)));Assert(stopped,"late dispatch escaped cancellation");});
  test("busy production refusal preserves quit",()=>{Setup();VAR.gsys_set.bquit=true;VAR.gsys_set.status=MotionCtrl.EM_SYS_STA.RUN;try{Reject(()=>NozzleZCalibrationMotion.RunExclusive(()=>{}));Assert(VAR.gsys_set.bquit,"refusal cleared production quit");}finally{VAR.gsys_set.status=MotionCtrl.EM_SYS_STA.IDLE;}});
  test("registered visual axes stop and registration is owner-only",()=>{Setup();var a=new MotionCtrl.AXIS();Reject(()=>NozzleZCalibrationMotion.RegisterAxes(a));NozzleZCalibrationMotion.RunExclusive(()=>{Reject(()=>NozzleZCalibrationMotion.RegisterAxes(new MotionCtrl.AXIS[]{a,null}));Assert(a.Stops==0,"null list partially acquired");NozzleZCalibrationMotion.RegisterAxes(a);NozzleZCalibrationMotion.Cancel();Assert(a.Stops>0,"registered axis not stopped");Reject(()=>NozzleZCalibrationMotion.RegisterAxes(new MotionCtrl.AXIS()));});});
  test("cleanup return failures reject completed measurement",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];x.cy_zk.io_sen_on.Read=()=>x.cy_zk.On&&x.ax_z.fenc_pos>1?MotionCtrl.GPIO.IO_STA.IN_ON:MotionCtrl.GPIO.IO_STA.IN_OFF;bool measured=false;MT.OnMove=(a,p)=>{if(a==x.ax_z&&p>1)measured=true;if(a==x.ax_z&&p==0&&measured){x.cy_zk.FailOff=true;x.gpio_pzk.FailOff=true;x.ax_z.FailRestore=true;}};AggregateException failure=null;try{NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Measure(d,0,0));}catch(AggregateException ex){failure=ex;}Assert(failure!=null&&failure.InnerExceptions.Count==3,"cleanup error reported success or skipped subsequent cleanup");Assert(x.ax_z.spd_cur==20&&x.gpio_pzk.Off>=2,"not all cleanup attempted");});
  test("cleanup keeps original probe error",()=>{var d=Setup();var x=COM.List_UDLoad[0].list_xt[0];MT.OnMove=(a,p)=>{if(a==x.ax_z&&p>1){x.cy_zk.io_sen_on.State=MotionCtrl.GPIO.IO_STA.ERR;x.gpio_pzk.FailOff=true;}};AggregateException failure=null;try{NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Measure(d,0,0));}catch(AggregateException ex){failure=ex;}Assert(failure!=null&&failure.InnerExceptions.Count==2&&failure.InnerExceptions[0].Message.Contains("读取失败"),"original probe error was hidden");});
  test("position registers both arms sixteen axes",()=>{var d=Setup();foreach(var arm in COM.List_UDLoad){var first=arm.list_xt[0];foreach(var n in arm.list_xt){n.ax_x=first.ax_x;n.ax_y=first.ax_y;}}var axes=COM.List_UDLoad.SelectMany(a=>a.list_xt.SelectMany(n=>new[]{n.ax_x,n.ax_y,n.ax_z,n.ax_u})).Distinct().ToArray();Assert(axes.Length==16,"fixture topology");NozzleZCalibrationMotion.RunExclusive(()=>{NozzleZCalibrationMotion.Position(d,0,0);NozzleZCalibrationMotion.Cancel();Assert(axes.All(a=>a.Stops>0),"avoidance axes missing from cancel");});});
  test("final stop ERR rejects success and preserves worker failure",()=>{Setup();var a=new MotionCtrl.AXIS{FailStop=true};var b=new MotionCtrl.AXIS();AggregateException failure=null;try{NozzleZCalibrationMotion.RunExclusive(()=>{NozzleZCalibrationMotion.RegisterAxes(a,b);throw new Exception("original");});}catch(AggregateException ex){failure=ex;}Assert(failure!=null&&failure.InnerExceptions.Count==2&&failure.InnerExceptions[0].Message=="original"&&b.Stops>0,"stop error swallowed or original hidden");Assert(!NozzleZCalibrationMotion.IsBusy,"busy leaked");Reject(()=>NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.RegisterAxes(a)));});
  test("timer stop ERR is reported on worker not timer",()=>{Setup();var a=new MotionCtrl.AXIS{FailStop=true};AggregateException failure=null;try{NozzleZCalibrationMotion.RunExclusive(()=>{NozzleZCalibrationMotion.RegisterAxes(a);NozzleZCalibrationMotion.Cancel();Thread.Sleep(65);a.FailStop=false;});}catch(AggregateException ex){failure=ex;}Assert(a.Stops>=2&&failure!=null,"timer failure lost after later successful stop");});
  if(failures.Count>0)throw new Exception(string.Join("\n",failures));
 }
}
