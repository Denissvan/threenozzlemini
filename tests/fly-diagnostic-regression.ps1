$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cam = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'UI/Class/Cam.Hik.cs')
$axis = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'MotionCtrl/AXIS.cs')
$buffer = [regex]::Match($cam, '(?s)// BEGIN FLY_DIAGNOSTIC_BUFFER.*?(?=\s*// END FLY_DIAGNOSTIC_BUFFER)').Value
$read = [regex]::Match($axis, '(?s)public string ReadHcmpDiagnostic\(ushort io\).*?(?=\s*//已触发点数)').Value
if (!$buffer -or !$read) { throw 'Production diagnostic code not found' }
$source = @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
$buffer
public class CARD { public enum BRAND { LEADSHINE, ZMOTION } public BRAND brand; public int card_id; }
public static class LTDMC {
 public static int Calls; public static ushort Channel; public static short ReturnCode; public static bool Fail;
 public static short dmc_hcmp_get_current_state(ushort card, ushort channel, ref int left, ref int current, ref int ran) {
  Calls++; Channel=channel; if(Fail) throw new Exception("mock read failure"); left=1;current=2;ran=3;return ReturnCode;
 }
}
public class AXIS { public CARD card; public int num; public int res=123; $read }
public static class Probe {
 static int checks;
 static void Check(bool ok,string why) { checks++;if(!ok)throw new Exception(why); }
 public static void Run() {
  var b=new FlyDiagnosticBuffer("pass-1");var rows=new List<string>();
  b.Record("SDK entered frame=11");b.Record("assigned frame=11");b.Drain(rows.Add);
  Check(rows.Count==2 && rows[0].Contains("SDK entered") && rows[1].Contains("assigned"),"trace order");
  b.Drain(rows.Add);Check(rows.Count==2,"drain must not duplicate");
  b.Ended=true;b.Record("late frame=12");b.Drain(rows.Add);
  Check(rows[2].Contains("[飞拍结束后]") && b.Id=="pass-1","late trace retains old pass");
  var bounded=new FlyDiagnosticBuffer("burst");var burst=new List<string>();
  Parallel.For(0,1000,i=>bounded.Record("frame="+i));bounded.Drain(burst.Add);
  Check(burst.Count==129 && burst[128].Contains("872"),"concurrent trace must be bounded and count omitted events");
  bounded.Drain(burst.Add);Check(burst.Count==129,"omission count drained once");
  var ax=new AXIS{card=new CARD{brand=CARD.BRAND.LEADSHINE,card_id=4},num=1};
  for(ushort io=12;io<=15;io++) { string s=ax.ReadHcmpDiagnostic(io);
   Check(LTDMC.Channel==io-12 && s.Contains("已执行=3") && s.Contains("剩余=1"),"actual channel read");
   Check(ax.res==123,"diagnostics must not change motion result");
  }
  LTDMC.ReturnCode=-7;Check(ax.ReadHcmpDiagnostic(12).Contains("RC=-7"),"failed read must show return code");
  LTDMC.Fail=true;Check(ax.ReadHcmpDiagnostic(12).Contains("mock read failure"),"read exception contained");LTDMC.Fail=false;
  int calls=LTDMC.Calls;ax.card.brand=CARD.BRAND.ZMOTION;
  Check(ax.ReadHcmpDiagnostic(12).Contains("不支持") && LTDMC.Calls==calls,"other board must not call LTDMC");
  ax.card.brand=CARD.BRAND.LEADSHINE;Check(ax.ReadHcmpDiagnostic(11).Contains("不支持") && LTDMC.Calls==calls,"invalid output must not read channel");
  ax.card=null;Check(ax.ReadHcmpDiagnostic(12).Contains("不支持"),"null board contained");
  Console.WriteLine("PASS: "+checks+" actual trace-buffer/channel-reader checks; no hardware loaded.");
 }
}
"@
Add-Type -TypeDefinition $source
[Probe]::Run()
