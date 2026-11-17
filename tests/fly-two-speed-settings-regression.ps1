$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Compile the production settings and actual WinForms page against read-only machine stubs.
# Redirect only the INI path into a private temp directory. Never launch the machine program.
$config = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/ThreeNozzleHardwareConfig.cs')
$config = $config.Replace('Path.Combine(Application.StartupPath, "ThreeNozzleHardware.ini")', 'TestPaths.Config')
$page = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/FormView/FrSys.FlySpeed.cs')
$page = [regex]::Replace($page, '(?m)^using .*;\r?\n', '')
$stub = @'
namespace MotionCtrl { public class AXIS { public double spd_work; } }
namespace UI {
 public static class TestPaths { public static string Config; }
 public static class MT { public static MotionCtrl.AXIS AXIS_UDL1_Y=new MotionCtrl.AXIS{spd_work=800}, AXIS_UDL2_Y=new MotionCtrl.AXIS{spd_work=600}; }
 public static class SafetyDebugMode { public static bool AutoRunActive; }
 public static class NozzleZCalibrationMotion { public static bool IsBusy; }
 public static class PT_SET { public static bool bmotorphoto,TrayBarcodeEn; }
 public class Position { public double y; }
 public class XT { public int id; public Position st_cap_pos=new Position(); }
 public class Tray { public double[] motor_barcode_pos; public double tray_barcode_y1,tray_barcode_y2; }
 public class UpDownLoad { public System.Collections.Generic.List<XT> list_xt; public Tray traybox_fd; }
 public static class COM { public static UpDownLoad UDLoad1,UDLoad2; }
 public class Msg { public enum EM_MSGTYPE { SYS,WAR }; public void AddMsg(EM_MSGTYPE type,string text){} }
 public static class VAR { public static Msg msg=new Msg(); }
 public class Session { public Session user1 { get { return this; } } public object cur_user="operator"; }
 public static class FrMain { public static Session frsuser; }
 public partial class FrSys : System.Windows.Forms.Form {
  private System.Windows.Forms.TabControl ctb_SetParm=new System.Windows.Forms.TabControl();
  public FrSys(){Size=new System.Drawing.Size(1050,540);ctb_SetParm.Dock=System.Windows.Forms.DockStyle.Fill;Controls.Add(ctb_SetParm);InitFlySpeedPage();}
  static int checks;
  static void Check(bool ok,string text){checks++;if(!ok)throw new System.Exception(text);}
  static void Reload(){typeof(ThreeNozzleHardwareConfig).GetField("initialized",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).SetValue(null,false);ThreeNozzleHardwareConfig.Initialize();}
  public static void Test(string dir){
   System.IO.Directory.CreateDirectory(dir);TestPaths.Config=System.IO.Path.Combine(dir,"settings.ini");
   System.IO.File.WriteAllText(TestPaths.Config,"; retain comment\r\n[Machine]\r\nCalibrationConfirmed=true\r\nFlyCapture=true\r\n[Arm1]\r\nSafeTrayMaxY=123\r\n[Arm2]\r\nSafeTrayMaxY=456\r\n");
   Reload();Check(ThreeNozzleHardwareConfig.IsConfigured,"old INI compatibility");
   Check(!ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled&&ThreeNozzleHardwareConfig.FlyLowSpeed(0)==250&&ThreeNozzleHardwareConfig.FlyLowSpeed(1)==250,"defaults");
   string error;
   Check(ThreeNozzleHardwareConfig.SetFlyTwoSpeed(true,200,300,"test",out error),"independent save: "+error);
   Reload();Check(ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled&&ThreeNozzleHardwareConfig.FlyLowSpeed(0)==200&&ThreeNozzleHardwareConfig.FlyLowSpeed(1)==300,"round trip independent values");
   string before=System.IO.File.ReadAllText(TestPaths.Config);
   Check(before.Contains("; retain comment")&&before.Contains("SafeTrayMaxY=123")&&before.Contains("FlyCapture=true"),"preserve unrelated INI");
   foreach(double bad in new[]{0.0,-1,double.NaN,double.PositiveInfinity,801})
    Check(!ThreeNozzleHardwareConfig.SetFlyTwoSpeed(true,bad,300,"test",out error),"reject invalid module1");
   Check(!ThreeNozzleHardwareConfig.SetFlyTwoSpeed(true,200,601,"test",out error),"module2 uses own work speed");
   SafetyDebugMode.AutoRunActive=true;
   Check(!ThreeNozzleHardwareConfig.SetFlyTwoSpeed(false,250,250,"test",out error),"block changes during run");SafetyDebugMode.AutoRunActive=false;
   Check(System.IO.File.ReadAllText(TestPaths.Config)==before,"rejections must not write");
   NozzleZCalibrationMotion.IsBusy=true;
   Check(!ThreeNozzleHardwareConfig.SetFlyTwoSpeed(false,250,250,"test",out error),"block changes during calibration");NozzleZCalibrationMotion.IsBusy=false;
   MT.AXIS_UDL2_Y.spd_work=100;
   Check(!ThreeNozzleHardwareConfig.ValidateForAutomatic(out error),"reject stale low after decreasing axis work speed");MT.AXIS_UDL2_Y.spd_work=600;
   using(var f=new FrSys()){
    f.Show();System.Windows.Forms.Application.DoEvents();
    Check(f.flyLowInputs[0].Value==200&&f.flyLowInputs[1].Value==300,"actual UI loads distinct lows");
    Check(f.flyHighLabels[0].Text.StartsWith("800.0")&&f.flyHighLabels[1].Text.StartsWith("600.0"),"actual UI reads each high");
    MT.AXIS_UDL2_Y.spd_work=700;f.RefreshFlySpeed();Check(f.flyHighLabels[1].Text.StartsWith("700.0"),"high follows work speed");
    f.flyLowInputs[0].Value=220;f.flyLowInputs[1].Value=330;f.SaveFlySpeed(null,System.EventArgs.Empty);
    Reload();Check(ThreeNozzleHardwareConfig.FlyLowSpeed(0)==220&&ThreeNozzleHardwareConfig.FlyLowSpeed(1)==330,"UI save applies independently");
    foreach(System.Windows.Forms.Control c in f.ctb_SetParm.TabPages[0].Controls[0].Controls)
     Check(c.Left>=0&&c.Top>=0&&c.Right<=850&&c.Bottom<=390,"UI child outside panel: "+c.Text);
    using(var bitmap=new System.Drawing.Bitmap(f.Width,f.Height)){f.DrawToBitmap(bitmap,f.ClientRectangle);bitmap.Save(System.IO.Path.Combine(dir,"fly-two-speed-page.png"));}
    f.Close();
   }
   System.IO.File.AppendAllText(TestPaths.Config,"\r\n");
   string data=System.IO.File.ReadAllText(TestPaths.Config).Replace("FlyLowSpeed1=220","FlyLowSpeed1=NaN");System.IO.File.WriteAllText(TestPaths.Config,data);
   Reload();Check(!ThreeNozzleHardwareConfig.IsConfigured&&!ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled,"invalid persisted low disables automatic config");
   System.Console.WriteLine("PASS: "+checks+" actual INI and WinForms checks; preview="+System.IO.Path.Combine(dir,"fly-two-speed-page.png"));
  }
 }
}
'@
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition ('using System.Drawing;' + $config + $page + $stub)
$testDir = Join-Path $env:TEMP ('MINI-TwoSpeed-Settings-' + [guid]::NewGuid().ToString('N'))
[UI.FrSys]::Test($testDir)
