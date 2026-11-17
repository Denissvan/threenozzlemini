$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$path=Join-Path $root 'UI/Class/FlyTwoSpeedPlan.cs'
if (!(Test-Path $path)) { throw 'Missing production two-speed planner' }
$source=Get-Content -Raw -Encoding UTF8 $path
$probe=@'
namespace UI { public static class TwoSpeedTests {
 static int checks; static void Check(bool ok,string message){checks++;if(!ok)throw new System.Exception(message);}
 public static void Run(){
  foreach(int dir in new[]{1,-1}){
   var p=new FlyTwoSpeedPlan(0,dir*1000,new[]{dir*500.0,dir*600.0},800,250,.1,.1,.02);
   Check(!p.StartLow,"long lead must allow high approach");double? speed;string error;
   Check(p.Update(0,0,out speed,out error)&&speed==null,"initial state");
   Check(p.Update(p.SwitchY,800,out speed,out error)&&speed==250,"must request low before trigger");
   Check(p.Update(dir*480,250,out speed,out error),"stable low before first trigger");
   Check(p.Update(dir*550,250,out speed,out error)&&speed==null,"low throughout exposures");
   Check(p.Update(p.ExitY,250,out speed,out error)&&speed==800,"resume high after all exposures");
   Check(p.Update(dir*900,800,out speed,out error)&&speed==null,"do not issue high twice");
   var late=new FlyTwoSpeedPlan(0,dir*1000,new[]{dir*500.0,dir*600.0},800,250,.1,.1,.02);
   Check(!late.Update(dir*499,800,out speed,out error)&&error!=null,"late deceleration must reject flight");
  }
  var shortPlan=new FlyTwoSpeedPlan(450,1000,new[]{500.0,600.0},800,250,.1,.1,.02);
  Check(shortPlan.StartLow,"short lead must start low");
  double? command;string why;
  Check(shortPlan.Update(480,250,out command,out why),"low start becomes stable");
  Check(!shortPlan.Update(550,500,out command,out why),"unexpected speed in capture zone must reject results");
  foreach(double low in new[]{0.0,-1.0,801.0,double.NaN,double.PositiveInfinity}){
   bool threw=false;try{new FlyTwoSpeedPlan(0,1000,new[]{500.0},800,low,.1,.1,0);}catch(System.ArgumentException){threw=true;}
   Check(threw,"invalid low speed accepted");
  }
  bool outside=false;try{new FlyTwoSpeedPlan(0,100,new[]{150.0},800,250,.1,.1,0);}catch(System.ArgumentException){outside=true;}
  Check(outside,"trigger outside path accepted");
  System.Console.WriteLine("PASS: "+checks+" actual two-speed planning and speed-integrity checks.");
 }
}}
'@
Add-Type -TypeDefinition ($source+[Environment]::NewLine+$probe)
[UI.TwoSpeedTests]::Run()
