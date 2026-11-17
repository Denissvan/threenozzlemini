using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UI;
public static class ZCalibrationRegression {
 static void Assert(bool b,string message){if(!b)throw new Exception(message);}
 static void Reject(Action action,string text){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,text);}
 public static void Run(){
  var d=new NozzleZCalibrationData();
  for(int a=0;a<2;a++)for(int n=0;n<3;n++)d.Arms[a].Nozzles[n].Point=new NozzleZCalibrationDraft.PointDraft{X=1,Y=2,U=0,Approach=24,End=26};
  Reject(()=>d.Record(0,1,new[]{25.0,25.0,25.0},1),"N2 cannot establish reference");
  d.Record(0,0,new[]{25.0,25.01,24.99},1);d.Record(0,1,new[]{25.3,25.31,25.29},-1);d.Record(0,2,new[]{24.8,24.81,24.79},1);d.ValidateArm(0);
  d.Record(0,0,new[]{25.1,25.11,25.09},1);Assert(Math.Abs(d.Arms[0].Baseline.Value-25)<1e-8,"N1 changed B");
  Reject(()=>d.Record(0,1,new[]{25.0,25.5,25.0},1),"spread accepted");Assert(d.Arms[0].Nozzles[1].Direction==-1,"failure partially changed record");
  var copy=d.Copy();copy.Arms[0].Nozzles[0].Point.X=9;Assert(d.Arms[0].Nozzles[0].Point.X==1,"copy alias");
  double pos=0;bool vac=false;var moves=new List<double>();
  var samples=NozzleZProbe.Measure(24,26,0.02,z=>{pos=z;moves.Add(z);},()=>pos,on=>vac=on,()=>vac&&pos>=25,()=>{},ms=>{});
  Assert(samples.Length==3&&!vac&&moves.Max()<=26,"probe result/cleanup/bounds");
  moves.Clear();Reject(()=>NozzleZProbe.Measure(24,24.1,0.02,z=>{pos=z;moves.Add(z);},()=>pos,on=>vac=on,()=>false,()=>{},ms=>{}),"missing sensor accepted");Assert(moves.Max()<=24.1&&!vac,"end exceeded or vacuum retained");
  moves.Clear();Reject(()=>NozzleZProbe.Measure(24,26,0.02,z=>moves.Add(z),()=>25,on=>vac=on,()=>true,()=>{},ms=>{}),"initial vacuum accepted");Assert(moves.Count==1,"initial vacuum descended");
  moves.Clear();int checks=0;Reject(()=>NozzleZProbe.Measure(24,26,0.02,z=>moves.Add(z),()=>25,on=>vac=on,()=>false,()=>{if(++checks>2)throw new OperationCanceledException();},ms=>{}),"cancel accepted");Assert(!vac&&moves.Count==1,"continued after cancel");
  string root=Path.Combine(Path.GetTempPath(),"mini-z-regression-"+Guid.NewGuid().ToString("N"));string previous=Directory.GetCurrentDirectory();
  try{Directory.CreateDirectory(Path.Combine(root,"work"));Directory.SetCurrentDirectory(Path.Combine(root,"work"));NozzleZCalibration.SaveArm(d,0);
   Assert(Math.Abs(NozzleZCalibration.ToAxisZ(0,1,-10,-1)+10)<1e-8,"signed axis target changed by stored offset");
   Assert(Math.Abs(NozzleZCalibration.ToProcessZ(0,1,-10.3,-1)+10.3)<1e-8,"signed teaching coordinate changed");
   Assert(NozzleZCalibration.ToAxisZ(1,0,10,1)==10,"uncalibrated arm blocked");Assert(NozzleZCalibration.ToAxisZ(0,1,10,1)==10,"legacy record direction affected direct coordinates");
   Assert(NozzleZCalibrationData.Load(NozzleZCalibration.ConfigurationPath).Arms[0].Nozzles[1].Samples.Length==3,"roundtrip failed");
  }finally{Directory.SetCurrentDirectory(previous);if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
 }
}
