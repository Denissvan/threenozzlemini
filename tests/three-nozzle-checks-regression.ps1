param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$path = Join-Path $Root 'UI/Class/UpDownLoad.ThreeNozzle.Checks.cs'
if (!(Test-Path $path)) { throw 'Missing three-nozzle production check adapters' }
$source = Get-Content -Raw -Encoding UTF8 $path
$start = $source.IndexOf('        private EM_RES ThreeNozzleCheckAfterPlace(')
if ($start -lt 0) { throw 'Missing post-place check adapter' }
$bodyStart = $source.IndexOf('{', $start)
$depth = 1; $end = $bodyStart + 1
while ($depth -gt 0) { if ($source[$end] -eq '{') {$depth++}; if ($source[$end] -eq '}') {$depth--}; $end++ }
$method = $source.Substring($start, $end - $start).Replace('private EM_RES','public EM_RES')
$stub = @"
using System;
public enum EM_RES { OK, ERR, RETRY, CAM_ERR, QUIT }
public struct ST_XYN { public double x,y; public int n; public ST_XYN(double a,double b,int c){x=a;y=b;n=c;} }
public class XT {}
public class Pos { public double x,y; }
public class WS { public class MdDat { public int Num=7,res; public Pos[] st_pos={new Pos()}; public Pos[] st_CapQrcodePos={new Pos()}; } }
public static class PT_SET { public enum BAR_SCAN { NO_SCAN, UP_SCAN } public static int BarcodeMode; public static bool Isaloneset,bEnVsFB,bAddCapQrcode,bBarcodeCamBackEn; }
public static class NewSysInf { public static class UserParams { public static bool bUpWsChkQrCodeOff; } }
public static class CONST { public const string WsModUpFw="position",WsModQrcode_Shp="barcode"; }
public class Cam { public class VisionOutPutData {public bool bOK=true,bUpdate=true; public VisionOutPutData Clone(){return (VisionOutPutData)MemberwiseClone();}} public class VisionTask { public int Value;public string TaskName;public VisionOutPutData ResData; } public VisionTask curTask=new VisionTask(); public int Captures; public EM_RES Next=EM_RES.OK; public EM_RES FindTaskTriAndWait(string n,bool demo){Captures++; curTask=new VisionTask { Value=Captures };return Next;} }
public static class Msg {public enum EM_MSGTYPE{DBG,WAR}} public static class VAR {public static Logger msg=new Logger();public class Logger{public void AddMsg(Msg.EM_MSGTYPE kind,string text){}}} public static class MT { public static EM_RES Next=EM_RES.OK; public static EM_RES ZupMove(ref bool q,ref int x,double px,ref int y,double py){return q?EM_RES.QUIT:Next;} }
public class UpDownLoad { public string disc="arm";public bool Demo; public int id,ax_x,ax_y; public Cam upcam=new Cam(); public int QrCalls,PosCalls,PositionTask,PositionIndex; public WS.MdDat Seen; public EM_RES QrResult=EM_RES.OK,PosResult=EM_RES.OK; public bool Ishave3342err;
public EM_RES QrCodeChkShow(Cam.VisionTask t,WS.MdDat md){QrCalls++;Seen=md;if(QrResult==EM_RES.RETRY)md.res=2222;return QrResult;}
public EM_RES CheckWsPutOkShow(ref bool q,Cam.VisionTask t,ST_XYN p){PosCalls++; PositionTask=t.Value;PositionIndex=p.n;return PosResult;}
$method
}
public static class CheckCases {
static void Assert(bool b,string s){if(!b)throw new Exception(s);}
public static void Run(){bool quit=false; var ws=new WS();var xt=new XT();var md=new WS.MdDat();var u=new UpDownLoad();
Assert(u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md)==EM_RES.OK && u.upcam.Captures==0,"disabled checks must not capture");
PT_SET.Isaloneset=true;u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md);Assert(u.QrCalls==1 && Object.ReferenceEquals(md,u.Seen),"Isaloneset must reach real md");
u=new UpDownLoad();u.QrResult=EM_RES.RETRY;Assert(u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md)==EM_RES.RETRY && md.res==2222,"2222 retry must propagate for reload");
PT_SET.Isaloneset=false;PT_SET.BarcodeMode=1;NewSysInf.UserParams.bUpWsChkQrCodeOff=true;u=new UpDownLoad();u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md);Assert(u.upcam.Captures==0,"UP_SCAN off");
NewSysInf.UserParams.bUpWsChkQrCodeOff=false;PT_SET.bEnVsFB=true;PT_SET.bAddCapQrcode=true;PT_SET.bBarcodeCamBackEn=true;u=new UpDownLoad();u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md);Assert(u.upcam.Captures==2 && u.PositionTask==1 && u.PositionIndex==6,"separate barcode must retain position task and pocket index");
PT_SET.bAddCapQrcode=false;PT_SET.bBarcodeCamBackEn=false;
u=new UpDownLoad();var fly=new Cam.VisionOutPutData();Assert(u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md,fly)==EM_RES.OK && u.upcam.Captures==0 && u.QrCalls==1 && u.PosCalls==1,"valid post-placement flight reused for both checks without static exposure");
u=new UpDownLoad();u.QrResult=EM_RES.RETRY;Assert(u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md,fly)==EM_RES.RETRY && md.res==2222,"flight barcode rejection preserves real pocket and rework");
u=new UpDownLoad();Assert(u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md,new Cam.VisionOutPutData{bOK=false})==EM_RES.OK && u.upcam.Captures==1,"unusable flight must recapture");
u=new UpDownLoad();Assert(u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md,new Cam.VisionOutPutData{bUpdate=false})==EM_RES.OK && u.upcam.Captures==1,"unfinished flight must recapture");
PT_SET.bAddCapQrcode=true;PT_SET.bBarcodeCamBackEn=true;
u=new UpDownLoad();Assert(u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md,fly)==EM_RES.OK && u.upcam.Captures==1 && u.QrCalls==1 && u.PosCalls==1,"separate barcode retains static barcode capture while position uses flight");
u=new UpDownLoad();u.PosResult=EM_RES.QUIT;Assert(u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md)==EM_RES.QUIT,"operator stop propagation");
u=new UpDownLoad();MT.Next=EM_RES.ERR;Assert(u.ThreeNozzleCheckAfterPlace(ref quit,ws,xt,md)==EM_RES.ERR && u.QrCalls==0,"motion failure prevents stale checks");
}
}
"@
Add-Type -TypeDefinition $stub
[CheckCases]::Run()
'PASS: compiled production post-place adapter; disabled/Isaloneset/2222 RETRY/UP_SCAN off/separate barcode position/stop/motion failure'

# Compile the actual finished-part lower-camera adapter with deterministic camera
# and operator responses. The comparison always precedes destination routing.
$name = '        private EM_RES ThreeNozzleCheckFinishedDownBarcode('
$start = $source.IndexOf($name)
if ($start -lt 0) { throw 'Missing DW_SCAN finished-product backcheck adapter' }
$bodyStart=$source.IndexOf('{',$start); $depth=1; $end=$bodyStart+1
while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
$downMethod=$source.Substring($start,$end-$start).Replace('private EM_RES','public EM_RES')
$downStub=@"
using System;
using System.Collections.Generic;
namespace DownTests {
public enum EM_RES { OK,ERR,RETRY,QUIT,CAM_ERR }
public class WS {}
public static class PT_SET { public enum BAR_SCAN { NO_SCAN,UP_SCAN,DW_SCAN } public static int BarcodeMode=2; public static bool bBarcodeCamBackEn=true,OpenDownQrde=true,DownDownQrde=true; }
public static class CONST { public static string[] ModDwFw={"N1","N2","N3"}; }
public class Cam { public class VisionOutPutData {public string BarCode;} }
public class Product {public class MdDat {public int res;public string bardcode="Expected";}}
public class XT {public Product.MdDat XtMd=new Product.MdDat();public int Calls;public Queue<string> Codes=new Queue<string>();public EM_RES Next=EM_RES.OK;public EM_RES DwCam(ref bool q,string task,ref Cam.VisionOutPutData v,bool demo){Calls++;v.BarCode=Codes.Count>0?Codes.Dequeue():"Expected";return Next;} }
public class UpDownLoad {
public Dictionary<Product.MdDat,string> threeNozzlePendingBarcodeWarnings=new Dictionary<Product.MdDat,string>();public HashSet<Product.MdDat> threeNozzleFinishedBarcodeChecks=new HashSet<Product.MdDat>();public bool Demo; public List<XT> list_xt=new List<XT>();public int MissingWarnings,MismatchWarnings,Uploads;public EM_RES Choice=EM_RES.RETRY,MismatchChoice=EM_RES.OK;
public EM_RES ThreeNozzleDownBarcodeWarning(ref bool q,WS ws,XT xt,bool missing,string actual){if(missing)MissingWarnings++;else MismatchWarnings++;return missing?Choice:MismatchChoice;}
public void ThreeNozzleUploadDownBarcodeRisk(XT xt,string actual){Uploads++;}
$downMethod
}
public static class Cases {
static void Assert(bool v,string m){if(!v)throw new Exception(m);}
public static void Run(){bool quit=false;var ws=new WS();var xt=new XT();var u=new UpDownLoad();u.list_xt.Add(xt);
Assert(u.ThreeNozzleCheckFinishedDownBarcode(ref quit,ws,xt)==EM_RES.OK&&xt.Calls==1,"matching finished barcode");
Assert(u.ThreeNozzleCheckFinishedDownBarcode(ref quit,ws,xt)==EM_RES.OK&&xt.Calls==1,"cached identity avoids tail camera motion");u.threeNozzleFinishedBarcodeChecks.Clear();xt.Calls=0;xt.XtMd.res=2222;u.ThreeNozzleCheckFinishedDownBarcode(ref quit,ws,xt);Assert(xt.Calls==0,"NG does not recheck");xt.XtMd.res=0;
xt.Codes.Enqueue("Wrong");xt.Codes.Enqueue("Wrong");Assert(u.ThreeNozzleCheckFinishedDownBarcode(ref quit,ws,xt)==EM_RES.OK&&xt.XtMd.res==2222&&u.MismatchWarnings==1&&u.Uploads==1,"mismatch after recapture routes to 2222 before unload");
u.threeNozzleFinishedBarcodeChecks.Clear();xt.XtMd.res=0;xt.Codes.Enqueue("");xt.Codes.Enqueue("");xt.Codes.Enqueue("Expected");u.ThreeNozzleCheckFinishedDownBarcode(ref quit,ws,xt);Assert(u.MissingWarnings==1&&xt.XtMd.res==0,"empty barcode operator retry recaptures");
u.threeNozzleFinishedBarcodeChecks.Clear();xt.Codes.Enqueue("");xt.Codes.Enqueue("");u.Choice=EM_RES.QUIT;Assert(u.ThreeNozzleCheckFinishedDownBarcode(ref quit,ws,xt)==EM_RES.QUIT,"stop returns immediately");
u.threeNozzleFinishedBarcodeChecks.Clear();xt.XtMd.res=0;xt.Codes.Enqueue("Wrong");xt.Codes.Enqueue("Wrong");u.MismatchChoice=EM_RES.QUIT;Assert(u.ThreeNozzleCheckFinishedDownBarcode(ref quit,ws,xt)==EM_RES.QUIT&&xt.XtMd.res==2222&&u.threeNozzlePendingBarcodeWarnings.ContainsKey(xt.XtMd),"cancelled 2222 warning remains pending");int calls=xt.Calls;u.MismatchChoice=EM_RES.OK;Assert(u.ThreeNozzleCheckFinishedDownBarcode(ref quit,ws,xt)==EM_RES.OK&&xt.Calls==calls&&!u.threeNozzlePendingBarcodeWarnings.ContainsKey(xt.XtMd),"pending confirmation cannot be skipped by NG status and must not move again");
PT_SET.bBarcodeCamBackEn=false;xt.Calls=0;u.ThreeNozzleCheckFinishedDownBarcode(ref quit,ws,xt);Assert(xt.Calls==0,"disabled backcheck");
}
}
}
"@
Add-Type -TypeDefinition $downStub
[DownTests.Cases]::Run()
'PASS: compiled DW_SCAN adapter; matching/NG skip/mismatch 2222/risk callback/empty retry/operator stop/disabled'



