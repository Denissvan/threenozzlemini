$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$text = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/UpDownLoad.ThreeNozzle.TrayFly.cs')
$method = [regex]::Match($text, '(?s)internal static bool ThreeNozzleTrayTrySharedCam\(.*?(?=\s*/// <summary>)').Value
if (!$method) { throw 'Production shared-position method not found' }
$source = @"
using System;
// Coordinate-only boundary stub. No motion or camera library is loaded.
public struct ST_XYZA {
 public double x,y,z,a;
 public ST_XYZA clone() { return this; }
}
public class TrayFlyProbe {
 static bool FlyFinite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
 $method
 static int checks;
 static void Check(bool value, string message) { checks++; if(!value) throw new Exception(message); }
 public static void Run() {
  var first = new ST_XYZA{x=-118.257,y=435.786,z=-32,a=420.048};
  foreach(double dx in new[]{0.0,0.055,0.130,14.944}) {
   var nominal = new ST_XYZA{x=first.x+dx,y=462.748,z=-31,a=first.a+dx};
   ST_XYZA actual;
   Check(ThreeNozzleTrayTrySharedCam(3,first,3,nominal,out actual),"same logical Row must group regardless of independent X difference");
   Check(actual.x==first.x && actual.a==first.a,"both exposures use the common physical X positions");
   Check(actual.y==nominal.y && actual.z==nominal.z,"each pocket keeps its own Y and height");
   Check(nominal.x==first.x+dx && nominal.a==first.a+dx,"taught coordinates must remain unchanged");
  }
  ST_XYZA rejected;
  Check(!ThreeNozzleTrayTrySharedCam(3,first,4,first,out rejected),"cross-row pair must fall back even when X matches");
  foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}) {
   var bad=first;bad.y=invalid;
   Check(!ThreeNozzleTrayTrySharedCam(3,first,3,bad,out rejected),"invalid Y must never become a flight");
   bad=first;bad.a=invalid;
   Check(!ThreeNozzleTrayTrySharedCam(3,bad,3,first,out rejected),"invalid common tray X must be rejected");
  }
  Console.WriteLine("PASS: "+checks+" actual shared-camera-coordinate regression checks; no hardware.");
 }
}
"@
Add-Type -TypeDefinition $source
[TrayFlyProbe]::Run()
