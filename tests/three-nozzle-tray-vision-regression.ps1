param([string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$p=Join-Path $Root 'UI/Class/UpDownLoad.ThreeNozzle.TrayVision.cs'
if(!(Test-Path $p)){throw 'Missing production tray vision adapter'}
$s=Get-Content -Raw -Encoding UTF8 $p
function Extract-Method([string]$name){$a=$s.IndexOf($name);if($a -lt 0){throw "Missing $name"};$b=$s.IndexOf('{',$a);$d=1;$e=$b+1;while($d -gt 0){if($s[$e] -eq '{'){$d++};if($s[$e] -eq '}'){$d--};$e++};$s.Substring($a,$e-$a).Replace('private EM_RES','public EM_RES').Replace('private static void','public static void')}
$method=Extract-Method 'private EM_RES ThreeNozzleGetTrayCompensation('
$invalidate=Extract-Method 'private static void ThreeNozzleInvalidateTrayCompensation('
$code=@"
using System;using System.Collections.Generic;
public enum EM_RES {OK,ERR,END,RETRY,QUIT,CAM_ERR}
public struct ST_XYA {public double x,y,a;}
public struct ST_XYZ {public double x,y,z;}
public struct ST_XYZA {public double x,y,z,a;}
public static class PT_SET {public static bool bEnVsTray=true;}
public class Product {public enum EM_CM_RES{EMPTY};public class Tray {public List<int> list_pos=new List<int>{1,2};public int Empty=2;public ST_XYZ[] TrayVsPos={new ST_XYZ{x=100,y=500,z=30}}; public List<int> GetPosList(EM_CM_RES r){return new List<int>(new int[Empty]);}}}
public class TrayBox {public bool IsFeedTray,IsOkTray=true,IsNgTray;public Product.Tray tray_cur=new Product.Tray();public int ax_x;}
public static class CONST {public const string TrayUpFw="tray";}
public class Cam {public class Data{public ST_XYA PosMM=new ST_XYA{x=3,y=4};}public class Task {public Data ResData=new Data();}public Task curTask=new Task();public int Calls;public Queue<EM_RES> Results=new Queue<EM_RES>();public EM_RES FindTaskTriAndWait(string n,bool d){Calls++;return Results.Count>0?Results.Dequeue():EM_RES.OK;}}
public static class MT {public static ST_XYZA Last;public static int Calls;public static EM_RES ZupMove(ref bool q,ref int x,double px,ref int y,double py,ref int t,double pt){Calls++;Last=new ST_XYZA{x=px,y=py,a=pt};return q?EM_RES.QUIT:EM_RES.OK;}}
public class UpDownLoad {
private sealed class ThreeNozzleTrayVisionRecord {internal Product.Tray Tray;internal ST_XYA Compensation;}
private static readonly Dictionary<TrayBox,ThreeNozzleTrayVisionRecord> threeNozzleTrayVision=new Dictionary<TrayBox,ThreeNozzleTrayVisionRecord>();
public static ST_XYA Vs_TrayOK,Vs_TrayNg;public int ax_x,ax_y,id;public bool Demo;public Cam upcam=new Cam();public int Warnings;public EM_RES Choice=EM_RES.RETRY;
public void Camcfgset(uint index){}
public EM_RES ThreeNozzleTrayVisionWarning(TrayBox t){Warnings++;return Choice;}
$method
$invalidate
}
public static class Cases {static void Check(bool b,string s){if(!b)throw new Exception(s);}public static void Run(){bool q=false;var t=new TrayBox();var a=new UpDownLoad();ST_XYA c;var p=new ST_XYZA{x=20,y=999,a=999};
Check(a.ThreeNozzleGetTrayCompensation(ref q,t,p,out c)==EM_RES.OK&&c.x==3&&a.upcam.Calls==1,"first empty tray capture");Check(MT.Last.x==70&&MT.Last.y==500&&MT.Last.a==0,"dedicated TrayVsPos formula with negative tray x clamp");t.tray_cur.Empty=1;var b=new UpDownLoad();b.ThreeNozzleGetTrayCompensation(ref q,t,p,out c);Check(b.upcam.Calls==0&&c.x==3,"same physical tray compensation shared by arms");
t.tray_cur=new Product.Tray();b.upcam.Results.Enqueue(EM_RES.CAM_ERR);b.upcam.Results.Enqueue(EM_RES.OK);Check(b.ThreeNozzleGetTrayCompensation(ref q,t,p,out c)==EM_RES.OK&&b.Warnings==1&&b.upcam.Calls==2,"new tray recaptures and operator retry");
UpDownLoad.ThreeNozzleInvalidateTrayCompensation(t);b.Choice=EM_RES.END;b.upcam.Results.Enqueue(EM_RES.CAM_ERR);Check(b.ThreeNozzleGetTrayCompensation(ref q,t,p,out c)==EM_RES.END,"replace tray propagated");
b.Choice=EM_RES.RETRY;for(int i=0;i<3;i++)b.upcam.Results.Enqueue(EM_RES.CAM_ERR);int calls=b.upcam.Calls;Check(b.ThreeNozzleGetTrayCompensation(ref q,t,p,out c)==EM_RES.ERR&&b.upcam.Calls==calls+3,"at most three exposures");
b.Choice=EM_RES.ERR;b.upcam.Results.Enqueue(EM_RES.CAM_ERR);Check(b.ThreeNozzleGetTrayCompensation(ref q,t,p,out c)==EM_RES.ERR,"operator stop");
var partial=new TrayBox();partial.tray_cur.Empty=1;calls=b.upcam.Calls;Check(b.ThreeNozzleGetTrayCompensation(ref q,partial,p,out c)==EM_RES.OK&&c.x==0&&calls==b.upcam.Calls,"unknown nonempty tray never photographed or given other tray offsets");
}}
"@
Add-Type -TypeDefinition $code
[Cases]::Run()
'PASS: actual tray vision adapter compiled; empty-first/cross-arm cache/TrayVsPos/new tray/retry/replace/three-attempt-limit/stop/nonempty'
