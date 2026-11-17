param([string]$RepoRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$path = Join-Path $RepoRoot 'UI/Class/UpDownLoad.ThreeNozzle.Scanning.cs'
$source = Get-Content -LiteralPath $path -Raw
# Compile the actual preparation/binding methods against deterministic camera
# doubles. No production initializer or hardware library is loaded.
$start = $source.IndexOf('        private readonly Dictionary<Product.MdDat, string>')
$end = $source.IndexOf('        private EM_RES ThreeNozzleMotorScan(ref bool')
if ($start -lt 0 -or $end -le $start) { throw 'Scanning production methods missing' }
$methods = $source.Substring($start, $end - $start)
$head = @'
using System;
using System.Collections.Generic;
namespace MotionCtrl{public class IniFile{public static double Value=0;public IniFile(string path){}public double ReadDouble(string section,string key,double value){return Value;}}}
namespace ScanTests {
public enum EM_RES { OK, ERR, QUIT, PARA_OUTOFRANG, CAM_ERR_PLACE_BACK }
public class VisionOutPutData { public string BarCode; public int PosMM; }
public class Product { public class MdDat { public string bardcode, motor_barcode; public bool bhaveMotoScan; } }
public class WS { public class MdDat { public string bardcode, motor_barcode; public int res = -1; } }
public static class ThreeNozzleHardwareConfig{public static string ConfigurationPath="test";} public class Cam {public class VisionTask{public string TaskName;}public List<VisionTask> List_vs_task=new List<VisionTask>{new VisionTask{TaskName="Q1"},new VisionTask{TaskName="Q2"},new VisionTask{TaskName="Q3"}};}
public class XT { public Cam dwcam=new Cam();public Product.MdDat XtMd = new Product.MdDat(); public string disc = "selected"; }
public static class CONST { public static string[] ModDwFw = { "F1", "F2", "F3" }; public static string[] DwFindQrCodeFw = { "Q1", "Q2", "Q3" }; }
public static class PT_SET {
 public enum BAR_SCAN { NO_SCAN, UP_SCAN, DW_SCAN }
 public static int BarcodeMode, motorBarcodeDigits = 8;
 public static bool bmotorphoto, bDwAddCapQrcode, bdownqr, OvenCheck, bsunnyqr, bsunnyqralm;
}
public static class NewSysInf { public static class UserParams { public static bool bCheckMotoCodeLength; } }
public class Msg { public enum EM_MSGTYPE { ERR } public void AddMsg(EM_MSGTYPE kind, string text) {} }
public static class VAR { public static Msg msg = new Msg(); }
public class UpDownLoad { public int id=0;private EM_RES ThreeNozzleError(string message){return EM_RES.ERR;}
 public List<XT> list_xt = new List<XT> { new XT(), new XT(), new XT() };
 public List<string> Calls = new List<string>();
 public EM_RES Failure = EM_RES.OK;
 private EM_RES ThreeNozzleMotorScan(ref bool quit, XT xt, int nozzle) { Calls.Add("M" + (nozzle + 1)); xt.XtMd.bhaveMotoScan = true; return Failure; }
 private EM_RES ThreeNozzleCheckMotorRate(XT xt) { return EM_RES.OK; }
 private EM_RES DwCamAction(ref bool quit, XT xt, string task, ref VisionOutPutData data) {
   Calls.Add(task); if (!Object.ReferenceEquals(xt, list_xt[2])) throw new Exception("Wrong nozzle");
   data = new VisionOutPutData { BarCode = task + "CODE", PosMM = task[0] == 'F' ? 123 : 999 }; return Failure;
 }
'@
$tail = @'
 private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
 public static void RunAll() {
   bool quit = false;
   var arm = new UpDownLoad(); var xt = arm.list_xt[2];
   var feature = new VisionOutPutData();
   PT_SET.BarcodeMode = (int)PT_SET.BAR_SCAN.DW_SCAN;
   Check(arm.ThreeNozzlePrepareScanning(ref quit, xt, ref feature) == EM_RES.OK, "DW feature scan");
   Check(xt.XtMd.bardcode == "F3CODE" && feature.PosMM == 123, "DW feature code binds N3");
   PT_SET.bDwAddCapQrcode = true; PT_SET.bdownqr = true;
   Check(arm.ThreeNozzlePrepareScanning(ref quit, xt, ref feature) == EM_RES.OK, "Extra scan");
   Check(xt.XtMd.bardcode == "Q3CODE" && feature.PosMM == 123, "QR must not replace placement feature");
   var target = new WS.MdDat(); arm.ThreeNozzleBindScanning(xt, target);
   Check(target.bardcode == "Q3CODE" && target.motor_barcode == "Q3CODE", "Same identity binds fixture");
   PT_SET.bmotorphoto = true; xt.XtMd.motor_barcode = "bad";
   arm.ThreeNozzlePrepareScanning(ref quit, xt, ref feature);
   arm.ThreeNozzlePrepareScanning(ref quit, xt, ref feature);
   Check(arm.Calls.FindAll(s => s == "M3").Count == 1, "Prepared material must not repeat successful motor scan");
   target = new WS.MdDat(); arm.ThreeNozzleBindScanning(xt, target);
   Check(target.res == 1 && target.motor_barcode == "Q3CODE", "Bad side code forces unload while retaining down code");
   xt.XtMd.motor_barcode = "12345678"; NewSysInf.UserParams.bCheckMotoCodeLength = true;
   arm.ThreeNozzlePrepareScanning(ref quit, xt, ref feature);
   target = new WS.MdDat(); arm.ThreeNozzleBindScanning(xt, target);
   Check(target.res == -1 && target.motor_barcode == "12345678", "Valid side code takes priority");
   PT_SET.bDwAddCapQrcode = false; PT_SET.OvenCheck = false; PT_SET.bsunnyqralm = false;
   xt.XtMd.motor_barcode = "bad"; target = new WS.MdDat(); arm.ThreeNozzleBindScanning(xt, target);
   Check(target.res == -1 && target.motor_barcode == "bad", "Fly placement with alarm disabled retains invalid code without forcing NG");
   PT_SET.bsunnyqralm = true; target = new WS.MdDat(); arm.ThreeNozzleBindScanning(xt, target);
   Check(target.res == 1, "Fly placement with alarm enabled forces NG");
   PT_SET.bmotorphoto = false;
   arm.Failure = EM_RES.CAM_ERR_PLACE_BACK; xt.XtMd.bardcode = "retained";
   Check(arm.ThreeNozzlePrepareScanning(ref quit, xt, ref feature) == EM_RES.CAM_ERR_PLACE_BACK, "Camera failure propagates");
   Check(xt.XtMd.bardcode == "retained", "Failure must not commit a fresh barcode");
   PT_SET.bmotorphoto = true;
   arm.ThreeNozzlePrepareScanning(ref quit, xt, ref feature);
   int failedAttempt = arm.Calls.Count;
   Check(arm.ThreeNozzlePrepareScanning(ref quit, xt, ref feature) == EM_RES.CAM_ERR_PLACE_BACK && arm.Calls[failedAttempt] == "M3", "Failed motor attempt must be retried even when bhaveMotoScan is true");
   PT_SET.bmotorphoto=true; MotionCtrl.IniFile.Value=double.NaN; int configCalls=arm.Calls.Count;
   Check(arm.ThreeNozzleValidateScanningConfiguration()==EM_RES.ERR&&arm.Calls.Count==configCalls,"missing N3 motor calibration blocks before motion");
   MotionCtrl.IniFile.Value=0;PT_SET.bDwAddCapQrcode=true;
   Check(arm.ThreeNozzleValidateScanningConfiguration()==EM_RES.OK,"configured scanning is accepted");
   arm.list_xt[2].dwcam.List_vs_task.Clear();
   Check(arm.ThreeNozzleValidateScanningConfiguration()==EM_RES.ERR,"missing N3 QR task fails preflight");
   quit = true; int before = arm.Calls.Count;
   Check(arm.ThreeNozzlePrepareScanning(ref quit, xt, ref feature) == EM_RES.QUIT && arm.Calls.Count == before, "Quit prevents motion/scan");
   Check(arm.list_xt[0].XtMd.bardcode == null && arm.list_xt[1].XtMd.bardcode == null, "Other mouths never modified");
 }
}
}
'@
Add-Type -TypeDefinition ($head + $methods + $tail)
[ScanTests.UpDownLoad]::RunAll()
if ($source -match 'ref ax_z|ref ax_u1|ref ax_u2|list_xt\s*=') { throw 'Scanning must not borrow shared Z/U or swap nozzle list' }
if ($source -notmatch 'if \(restore != EM_RES.OK\) return restore') { throw 'Motor recovery failure is not propagated' }
if ($source -notmatch 'MotorScanY3' -or $source -notmatch 'MotorScanZ3' -or $source -notmatch 'MotorScanAngle3') { throw 'N3 calibration missing' }
Write-Host 'PASS: three-nozzle scanning preparation, material identity, feature/code isolation, NG binding, failure/quit and independent axes contracts'
