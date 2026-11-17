$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
function Method($file,$signature) {
 $source=Get-Content (Join-Path $root $file) -Raw -Encoding UTF8
 $start=$source.IndexOf($signature);if($start -lt 0){throw "Missing $signature"}
 $i=$source.IndexOf('{',$start);$depth=1;$end=$i+1
 while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
 $source.Substring($start,$end-$start).Replace('private void','public void')
}
$learn=Method 'UI/FormView/FrProduct.cs' 'private void LearnTrayProcessZ()'
$trial=Method 'UI/Compment/XtOfsAdj.cs' 'private void btn_PickWl_Click('
$code=@"
using System;using System.Collections.Generic;
public class Axis {public double fenc_pos;}
public class Control {public int SelectedIndex;public decimal Value;public string Text;}
public struct ST_XYZA {public double x,y,z,a;}
public class PositionControl {public ST_XYZA Value;}
public class Arm {public int id;public List<XT> list_xt=new List<XT>();}
public class XT {public Arm Parent;public int SlotIndex,ZApproachDirection=1;public Axis ax_z=new Axis();}
public static class COM {public static List<Arm> List_UDLoad=new List<Arm>();}
public static class VAR {public static bool IsChinese=true;public static Setting gsys_set=new Setting();public class Setting{public bool bquit;}}
public static class MessageBox {public static int Calls;public static void Show(string s){Calls++;}}
public static class NozzleZCalibration {public static bool Valid=true;public static int Arm,Slot;public static double ToAxisZ(int a,int n,double z,int d){Arm=a;Slot=n;if(!Valid)throw new InvalidOperationException();return d*(z+2.5);}public static double ToProcessZ(int a,int n,double z,int d){Arm=a;Slot=n;if(!Valid)throw new InvalidOperationException();return d*z-2.5;}}
public static class MT {public static int Calls;public static double Target;public static void SetAllAxToWorkSpd(){}public static void Move(ref bool q,ref Axis axis,double z){Calls++;Target=z;}}
public class ProductPage {public Control ctb_udload_cfg=new Control();public PositionControl xyzu_tray_study=new PositionControl();$learn}
public class VisualPage {public XT xt;public Control nub_Zhigh=new Control(),btn_PickWl=new Control();public double ZHigh;$trial}
public static class Cases {static void Check(bool ok,string message){if(!ok)throw new Exception(message);} public static void Run(){
 var a0=new Arm{id=0};var a1=new Arm{id=1};a0.list_xt.Add(new XT{Parent=a0});a1.list_xt.Add(new XT{Parent=a1,ZApproachDirection=-1});COM.List_UDLoad.Add(a0);COM.List_UDLoad.Add(a1);
 var page=new ProductPage();page.ctb_udload_cfg.SelectedIndex=1;page.xyzu_tray_study.Value=new ST_XYZA{x=12,y=34,z=99,a=56};a1.list_xt[0].ax_z.fenc_pos=-17.5;page.LearnTrayProcessZ();var p=page.xyzu_tray_study.Value;Check(p.z==15&&p.x==12&&p.y==34&&p.a==56&&NozzleZCalibration.Arm==1&&NozzleZCalibration.Slot==0,"N1 inverse preserves XY and tray axis");
 NozzleZCalibration.Valid=false;page.LearnTrayProcessZ();Check(page.xyzu_tray_study.Value.z==15&&MessageBox.Calls==1,"unmarked teaching leaves stored height unchanged");
 var trial=new VisualPage{xt=new XT{Parent=a1,SlotIndex=2,ZApproachDirection=-1}};trial.nub_Zhigh.Value=15;trial.btn_PickWl_Click(null,EventArgs.Empty);Check(MT.Calls==0,"unmarked trial must not move");NozzleZCalibration.Valid=true;trial.btn_PickWl_Click(null,EventArgs.Empty);Check(MT.Calls==1&&MT.Target==-17.5&&NozzleZCalibration.Slot==2,"N3 direction and correction applied once");
 NozzleZCalibration.Valid=false;trial.xt.ax_z.fenc_pos=-17.5;trial.btn_PickWl_Click(null,EventArgs.Empty);Check(MT.Calls==2&&MT.Target==0,"retraction remains uncompensated even if calibration unavailable");
}}
"@
Add-Type -TypeDefinition $code
[Cases]::Run()
$ws=Get-Content (Join-Path $root 'UI/Compment/WsModuelDef.cs') -Raw -Encoding UTF8
if($ws -match 'MdDat\.st_\w+\[id\]\.z = COM.List_UDLoad\[id\].ax_z'){throw 'raw shared-Z teaching remains'}
$tray=Get-Content (Join-Path $root 'UI/FormView/FrProduct.cs') -Raw -Encoding UTF8
if($tray -notmatch 'pos.z = targetProcessZ' -or $tray -match 'st_pos.z = ax_z != null'){throw 'XY teaching overwrites process Z'}
'PASS: actual UI methods compile; N1 inverse teaching, coordinate preservation, N3 corrected trial, uncalibrated rejection, zero retraction; XY/cross-arm source guards'
