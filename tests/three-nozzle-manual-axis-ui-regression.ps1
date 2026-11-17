$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
function Method($file,$signature) {
 $source=Get-Content (Join-Path $root $file) -Raw -Encoding UTF8
 $start=$source.IndexOf($signature);if($start -lt 0){throw "Missing $signature"}
 $i=$source.IndexOf('{',$start);$depth=1;$end=$i+1
 while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
 $source.Substring($start,$end-$start).Replace('private void','public void')
}
$resolve=Method 'UI/FormView/FrMain.ManualNozzles.cs' 'internal static AXIS ResolveManualAxis('
$mapping=Method 'UI/FormView/FrMain.cs' 'private void StrToAxis('
$down=Method 'UI/FormView/FrMain.cs' 'private void tsb_xn_MouseDown('
$up=Method 'UI/FormView/FrMain.cs' 'private void tsb_xn_MouseUp('
$code=@"
using System;using System.Collections.Generic;
public class AXIS {public enum AX_DIR {P,N} public int Moves,Stops; public double Step;public string disc="axis";public void JOG_Step(ref bool q,AX_DIR d,double step){Moves++;Step=step;}public void JOG_Stop(){Stops++;}}
public class XT {public AXIS ax_z=new AXIS(),ax_u=new AXIS();}
public class Arm {public List<XT> list_xt=new List<XT>();}
public static class COM {public static List<Arm> List_UDLoad=new List<Arm>();}
public static class MT {public static AXIS AXIS_UDL1_X=new AXIS(),AXIS_UDL1_Y=new AXIS(),AXIS_UDL2_X=new AXIS(),AXIS_UDL2_Y=new AXIS();}
public class Selector {public int SelectedIndex;}
public class MouseEventArgs:EventArgs {}
public class ToolStripButton {public string Name,Text;}
public static class Thread {public static void Sleep(int n){}}
public class Msg {public enum EM_MSGTYPE{NOR}public void AddMsg(EM_MSGTYPE type,string text){}}
public static class VAR {public static Msg msg=new Msg();public static Setting gsys_set=new Setting();public class Setting {public bool bquit;}}
public class Page {public Selector[] manualNozzleSelectors={new Selector(),new Selector()};public Dictionary<ToolStripButton,AXIS> manualPressedAxes=new Dictionary<ToolStripButton,AXIS>(); public double step=10;
$resolve
$mapping
$down
$up
}
public static class Cases {static void Check(bool ok,string text){if(!ok)throw new Exception(text);}public static void Run(){
 for(int a=0;a<2;a++){var arm=new Arm();for(int n=0;n<3;n++)arm.list_xt.Add(new XT());COM.List_UDLoad.Add(arm);}
 for(int a=0;a<2;a++)for(int n=0;n<3;n++){
 string prefix="tsb_"+(a+1);var xt=COM.List_UDLoad[a].list_xt[n];
 Check(Page.ResolveManualAxis(prefix+"zp",n)==xt.ax_z&&Page.ResolveManualAxis(prefix+"zn",n)==xt.ax_z,"selected independent Z");
 Check(Page.ResolveManualAxis(prefix+"up",n)==xt.ax_u&&Page.ResolveManualAxis(prefix+"un",n)==xt.ax_u,"selected independent U");
 Check(Page.ResolveManualAxis(prefix+"u1p",n)==COM.List_UDLoad[a].list_xt[0].ax_u&&Page.ResolveManualAxis(prefix+"u2n",n)==COM.List_UDLoad[a].list_xt[1].ax_u,"legacy U1/U2 fixed physical nozzles");}
 Check(Page.ResolveManualAxis("tsb_1xp",2)==MT.AXIS_UDL1_X&&Page.ResolveManualAxis("tsb_2yn",2)==MT.AXIS_UDL2_Y,"XY unchanged");
 Check(Page.ResolveManualAxis("tsb_3zp",0)==null&&Page.ResolveManualAxis("tsb_1zp",-1)==null&&Page.ResolveManualAxis("tsb_1unknown",0)==null,"invalid mapping rejected");
 var p=new Page();p.manualNozzleSelectors[1].SelectedIndex=2;var b=new ToolStripButton{Name="tsb_2zp",Text="N3 Z+"};var axis=COM.List_UDLoad[1].list_xt[2].ax_z;p.tsb_xn_MouseDown(b,null);Check(axis.Moves==1&&axis.Step==1,"N3 Z step capped");p.manualNozzleSelectors[1].SelectedIndex=0;p.tsb_xn_MouseUp(b,null);Check(axis.Stops==1&&COM.List_UDLoad[1].list_xt[0].ax_z.Stops==0,"release stops pressed axis despite selection change");
 p.manualNozzleSelectors[1].SelectedIndex=-1;p.tsb_xn_MouseDown(b,null);Check(axis.Moves==1&&p.manualPressedAxes.Count==0,"invalid mapping dispatches no motion");
 COM.List_UDLoad[1].list_xt[2]=null;Check(Page.ResolveManualAxis("tsb_2zp",2)==null,"missing nozzle rejected");COM.List_UDLoad=null;Check(Page.ResolveManualAxis("tsb_1up",0)==null,"uninitialized hardware rejected");
}}
"@
Add-Type -TypeDefinition $code
[Cases]::Run()
'PASS: actual manual mapping and press/release methods compile; six Z/U axes, fixed legacy U1/U2, XY, invalid/missing mapping, Z step cap, physical-axis release'
