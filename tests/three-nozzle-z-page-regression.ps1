param([string]$Root=(Split-Path $PSScriptRoot -Parent),[string]$OutputDirectory=(Join-Path $env:TEMP 'mini-nozzle-z-page-preview'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$stub=@'
using System;
namespace UI {public static class NozzleZCalibrationMotion {
 public static bool IsBusy {get {return false;}}
 public static void Cancel(){throw new Exception("Hardware stub called");}
 public static void CheckIdle(){throw new Exception("Hardware stub called");}
 public static void RunExclusive(Action action){throw new Exception("Hardware stub called");}
 public static void Measure(NozzleZCalibrationData data,int arm,int nozzle){throw new Exception("Hardware stub called");}
 public static void Position(NozzleZCalibrationData data,int arm,int nozzle){throw new Exception("Hardware stub called");}
 public static NozzleZCalibrationDraft.PointDraft Learn(int arm,int nozzle){throw new Exception("Hardware stub called");}
}}
'@
$test=@'
using System;
using System.IO;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using UI;
public static class PageCases {
 static object Get(object p,string name){return p.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p);}
 static void Invoke(object p,string name,params object[] args){p.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(p,args);}
 static void Check(bool test,string message){if(!test)throw new Exception(message);}
 static NozzleZCalibrationData Data(){var d=new NozzleZCalibrationData();for(int a=0;a<2;a++)for(int n=0;n<3;n++){
 d.Arms[a].Nozzles[n].Point=new NozzleZCalibrationDraft.PointDraft{X=100+n,Y=200,U=0,Approach=10,End=25};
 double h=20+a+n*0.1;d.Record(a,n,new[]{h,h+0.001,h-0.001},1);
 }return d;}
 public static string Run(string directory){
 var d=Data();typeof(NozzleZCalibration).GetField("active",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,d.Copy());
 var path=Path.Combine(directory,"settings-test.xml");if(File.Exists(path))File.Delete(path);
 using(var form=new Form{ClientSize=new Size(876,306),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)})
 using(var p=new NozzleZCalibrationPage(0,path)){
 form.Controls.Add(p);p.Dock=DockStyle.Fill;form.Show();Application.DoEvents();
 var tabs=(TabControl)Get(p,"tabs");string findings="";
 for(int i=0;i<3;i++){
 tabs.SelectedIndex=i;Application.DoEvents();p.PerformLayout();
 using(var image=new Bitmap(p.Width,p.Height)){p.DrawToBitmap(image,p.ClientRectangle);image.Save(Path.Combine(directory,"tab-"+i+".png"));}
 var grid=(DataGridView)Get(p,i==0?"grid":i==1?"points":"records");var row=grid.GetRowDisplayRectangle(2,false);
 findings+="Tab "+i+": page="+p.Size+", grid="+grid.ClientSize+", row3="+row+", complete="+(row.Bottom<=grid.ClientSize.Height)+Environment.NewLine;
 if(!(row.Height>0&&row.Bottom<=grid.ClientSize.Height))findings+="LAYOUT FAIL: third nozzle row clipped\n";
 if(i<2){var actions=(FlowLayoutPanel)Get(p,i==0?"actions":"pointActions");foreach(Control c in actions.Controls){if(c is Button){findings+="Button "+c.Text+" bounds="+c.Bounds+" host="+actions.ClientSize+Environment.NewLine;if(!(c.Right<=actions.ClientSize.Width&&c.Bottom<=actions.ClientSize.Height))findings+="LAYOUT FAIL: button clipped\n";}}}
 }
 Invoke(p,"SetRunning",true);
 Check(((Button)Get(p,"cancel")).Enabled&&((Button)Get(p,"cancelPosition")).Enabled,"running disables a stop button");
 foreach(string group in new[]{"actions","pointActions"})foreach(Control control in ((FlowLayoutPanel)Get(p,group)).Controls){if(control is Button&&control!=(Button)Get(p,"cancel")&&control!=(Button)Get(p,"cancelPosition"))Check(!control.Enabled,"running allows "+control.Text);}
 Check(!((ComboBox)Get(p,"armSelector")).Enabled&&((DataGridView)Get(p,"points")).ReadOnly,"running permits arm or point changes");
 foreach(string name in new[]{"speed","step","spread"})Check(!((NumericUpDown)Get(p,name)).Enabled,"running permits option edit");
 Invoke(p,"SetRunning",false);Check(!((Button)Get(p,"cancel")).Enabled&&!((Button)Get(p,"cancelPosition")).Enabled,"idle leaves stop buttons enabled");
 tabs.SelectedIndex=1;Application.DoEvents();var points=(DataGridView)Get(p,"points");points.Rows[1].Cells[1].Value="102.000";
 Invoke(p,"SaveSettings",null,EventArgs.Empty);
 var pending=(NozzleZCalibrationData)Get(p,"pending");Check(pending.Arms[0].Nozzles[1].Samples.Length==0,"edited point kept candidate samples");
 Check(pending.Arms[0].Nozzles[0].Samples.Length==3&&pending.Arms[0].Nozzles[2].Samples.Length==3,"point edit cleared unrelated nozzle candidates");
 Check(Math.Abs(NozzleZCalibration.ToAxisZ(0,1,15,1)-15.1)<0.00001,"draft save changed official correction");
 var loaded=NozzleZCalibrationDraft.Load(path);Check(loaded.Arms[0].Points[1].X==102,"draft setting not persisted");
 ((NumericUpDown)Get(p,"speed")).Value=2;Invoke(p,"SaveSettings",null,EventArgs.Empty);pending=(NozzleZCalibrationData)Get(p,"pending");
 for(int n=0;n<3;n++)Check(pending.Arms[0].Nozzles[n].Samples.Length==0,"speed edit retained candidate samples");
 Check(NozzleZCalibration.GetSnapshot().Arms[0].Nozzles[0].Samples.Length==3,"formal samples changed");
 using(var reopened=new NozzleZCalibrationPage(0,path)){
 var restored=(NozzleZCalibrationData)Get(reopened,"pending");
 Check(restored.Arms[0].Speed==2&&restored.Arms[0].Nozzles[1].Point.X==102,"reopened calibrated arm ignored saved draft settings");
 for(int n=0;n<3;n++)Check(restored.Arms[0].Nozzles[n].Samples.Length==0,"reopened changed settings retained formal records as candidates");
 Check(Math.Abs(NozzleZCalibration.ToAxisZ(0,1,15,1)-15.1)<0.00001,"draft reload changed formal compensation");
 }
 File.WriteAllText(Path.Combine(directory,"layout-report.txt"),findings);Check(!findings.Contains("LAYOUT FAIL"),"Layout incomplete; see layout-report.txt (settings behavior checks passed)");return findings+"PASS: real Page/Core/Draft; point/global settings invalidate candidates only; saved draft reloads; both stop buttons enabled and edits blocked while running; formal calibration unchanged; no hardware linked.";
 }}
}
'@
$files=@((Join-Path $Root 'UI/Class/NozzleZCalibration.cs'),(Join-Path $Root 'UI/Class/NozzleZCalibrationDraft.cs'),(Join-Path $Root 'UI/Compment/NozzleZCalibrationPage.cs'))
$stubPath=Join-Path $OutputDirectory 'MotionStub.cs';$testPath=Join-Path $OutputDirectory 'PageCases.cs'
[IO.File]::WriteAllText($stubPath,$stub);[IO.File]::WriteAllText($testPath,$test)
Add-Type -IgnoreWarnings -Path ($files+@($stubPath,$testPath)) -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Core,System.Xml,System
[PageCases]::Run($OutputDirectory)
Write-Output "Previews: $OutputDirectory"
