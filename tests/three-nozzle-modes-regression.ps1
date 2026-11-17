param([string]$RepoRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $RepoRoot 'UI/Class/UpDownLoad.ThreeNozzle.Modes.cs') -Raw
# Execute production mode adapters against recording motion boundaries, without hardware.
$stubs = @'
namespace MotionCtrl { public enum EM_RES { OK, ERR, ABORT, QUIT } }
namespace UI {
using System; using System.Collections.Generic; using System.Linq; using MotionCtrl;
public static class PT_SET { public static bool bUpDn1XtOnlyOne,bUpDn2XtOnlyOne,xt1firsten,bGrrFlow; }
public static class VAR { public static bool isAutoChkMode,ClearMt; public static G gsys_set=new G(); public class G { public bool bquit; } }
public static class CONST { public static string[] ModDwFw={"D0","D1","D2"}; }
public class Product { public enum EM_CM_RES { EMPTY=-2,UNTEST=-1 } public class MdDat { public int res; public string bardcode; } }
public class WS { public enum EM_STA { REDAYFORUPDOWNLOAD } public EM_STA FeedStatus; public int GrrUDLcnt; public List<MdDat> list_md=new List<MdDat>(); public class MdDat { public bool benable=true,bAutoChkOk; public int Num,res; public string bardcode; } }
public class XT { public Product.MdDat XtMd; }
public class Cam { public struct VisionOutPutData {} }
public partial class UpDownLoad {
private enum EM_FIN_MOD { ALL,NONE,LOW,HIGH }
private EM_FIN_MOD FindMod=EM_FIN_MOD.ALL;
private int id=0; private bool Demo=false,threeNozzleDrain=false;
private List<XT> list_xt=new List<XT>{new XT(),new XT(),new XT()};
private List<string> calls=new List<string>(); private int fresh,placedCount; private Action onPlace; private bool failVision,failAfterPlace;
private sealed class ThreeNozzleRuntimePair { public WS.MdDat[] Pockets=new WS.MdDat[2]; public bool[] Incoming=new bool[2],DownReady=new bool[2],Placed=new bool[2],CountAsNew=new bool[2]; public int[] IncomingNozzles={1,2},FinishedNozzles={0,1}; public Product.MdDat[] Prepared=new Product.MdDat[2]; public Cam.VisionOutPutData[] DownVision=new Cam.VisionOutPutData[2]; }
private EM_RES ThreeNozzleError(string text,bool recovery=false) { calls.Add("error:"+text);return EM_RES.ERR; }
private EM_RES GoZero(ref bool quit) { calls.Add("home"); return EM_RES.OK; }
private EM_RES ThreeNozzlePickMaterial(ref bool quit,WS.MdDat md,XT xt,bool checkBefore=true) { if(xt.XtMd!=null || md.res<0) throw new Exception("occupied pick"); calls.Add("pick:"+list_xt.IndexOf(xt)+":"+md.Num); xt.XtMd=new Product.MdDat{res=md.res,bardcode=md.bardcode}; md.res=-2;md.bardcode=null;return EM_RES.OK; }
private EM_RES ThreeNozzleUnloadCore(ref bool quit,XT only=null) { foreach(var xt in list_xt) if((only==null || only==xt)&&xt.XtMd!=null) { calls.Add("unload:"+list_xt.IndexOf(xt)+":"+xt.XtMd.bardcode+":"+xt.XtMd.res); xt.XtMd=null;}return EM_RES.OK; }
private EM_RES ThreeNozzlePreparePair(ref bool quit,ThreeNozzleRuntimePair pair) { int n=pair.IncomingNozzles[0];if(pair.Prepared[0]==null){if(list_xt[n].XtMd!=null)throw new Exception("prefetch occupied");list_xt[n].XtMd=new Product.MdDat{res=-1,bardcode="new"};pair.Prepared[0]=list_xt[n].XtMd;fresh++;calls.Add("feed:"+n);}pair.DownReady[0]=true;return EM_RES.OK; }
private EM_RES ThreeNozzlePlaceOne(ref bool quit,ThreeNozzleRuntimePair pair,int slot,bool countNew=true) { int n=pair.IncomingNozzles[0];var md=pair.Pockets[0]; if(!pair.Placed[0]){if(md.res!=-2 || list_xt[n].XtMd==null)throw new Exception("place invariant");calls.Add("place:"+n+":"+md.Num);md.res=-1;md.bardcode=list_xt[n].XtMd.bardcode;list_xt[n].XtMd=null;pair.Prepared[0]=null;pair.Placed[0]=true;}if(failAfterPlace){failAfterPlace=false;return EM_RES.ERR;}if(countNew)placedCount++;if(onPlace!=null)onPlace();return EM_RES.OK; }
private EM_RES DwCamAction(ref bool quit,XT xt,string name,ref Cam.VisionOutPutData vision) { calls.Add("vision:"+name);if(failVision){failVision=false;return EM_RES.ERR;}return EM_RES.OK; }
private static void Check(bool value,string text){if(!value)throw new Exception(text);}
private static WS Station(int count) {var ws=new WS();for(int i=0;i<count;i++)ws.list_md.Add(new WS.MdDat{Num=i+1,res=0,bardcode="p"+(i+1)});return ws;}
private static void ResetFlags(){PT_SET.bGrrFlow=false;PT_SET.bUpDn1XtOnlyOne=false;PT_SET.bUpDn2XtOnlyOne=false;PT_SET.xt1firsten=false;VAR.ClearMt=false;VAR.isAutoChkMode=false;}
public static void RunRegression(){
bool quit=false;ResetFlags();var a=new UpDownLoad();var ws=Station(3);a.ThreeNozzleInitializeMode(ws);Check(!a.ThreeNozzleHasSpecialMode,"normal route");
PT_SET.bGrrFlow=true;ws.GrrUDLcnt=0;a.ThreeNozzleInitializeMode(ws);Check(!a.ThreeNozzleHasSpecialMode,"GRR initial feed uses tray");
ws.GrrUDLcnt=1;a.ThreeNozzleInitializeMode(ws);Check(a.ThreeNozzleHasSpecialMode,"GRR repeat route");Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.OK,"GRR complete");Check(String.Join(",",ws.list_md.Select(x=>x.bardcode))=="p2,p3,p1","GRR same-batch rotation");Check(a.fresh==0&&a.placedCount==0&&ws.GrrUDLcnt==1,"GRR preserves counters owned by myfunc");int before=a.calls.Count;a.ThreeNozzleRunSpecialMode(ref quit,ws);Check(a.calls.Count==before,"GRR completed visit idempotent");
ResetFlags();PT_SET.bGrrFlow=true;a=new UpDownLoad();ws=Station(3);ws.GrrUDLcnt=1;a.onPlace=()=>VAR.ClearMt=true;a.ThreeNozzleInitializeMode(ws);Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.OK,"GRR mid-clear complete");Check(ws.list_md.All(x=>x.res==-2)&&a.list_xt.All(x=>x.XtMd==null),"mid-clear drains reloaded and held parts");Check(a.calls.Count(x=>x.StartsWith("unload:"))==3,"GRR clear unload count");
ResetFlags();PT_SET.bGrrFlow=true;a=new UpDownLoad();ws=Station(3);ws.GrrUDLcnt=1;a.failVision=true;a.ThreeNozzleInitializeMode(ws);Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.ERR,"injected vision failure");Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.OK,"resume held GRR parts");Check(a.calls.Count(x=>x.StartsWith("pick:"))==3,"resume never repicks committed module");
ResetFlags();PT_SET.bGrrFlow=true;a=new UpDownLoad();ws=Station(3);ws.GrrUDLcnt=1;a.failAfterPlace=true;a.ThreeNozzleInitializeMode(ws);Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.ERR,"post-place check interrupted");Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.OK,"post-place check resumes with empty nozzle");Check(String.Join(",",ws.list_md.Select(x=>x.bardcode))=="p2,p3,p1","post-place retry preserves rotation");Check(a.calls.Count(x=>x.StartsWith("place:"))==3,"no replay of physically committed GRR place");
ResetFlags();PT_SET.bGrrFlow=true;a=new UpDownLoad();ws=Station(3);ws.GrrUDLcnt=1;a.failAfterPlace=true;a.ThreeNozzleInitializeMode(ws);a.ThreeNozzleRunSpecialMode(ref quit,ws);VAR.ClearMt=true;Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.OK&&ws.list_md.All(x=>x.res==-2),"clear after committed place but interrupted check");
foreach(bool first in new[]{false,true}) {ResetFlags();PT_SET.bUpDn1XtOnlyOne=true;PT_SET.xt1firsten=first;a=new UpDownLoad();ws=Station(2);a.ThreeNozzleInitializeMode(ws);Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.OK,"single complete");string n=first?"0":"1";Check(a.calls.Where(x=>x.StartsWith("pick:")||x.StartsWith("place:")||x.StartsWith("feed:")).All(x=>x.Split(':')[1]==n),"single selected physical nozzle");Check(a.fresh==2&&a.placedCount==2,"single normal counts");Check(a.calls.IndexOf("unload:"+n+":p1:0")<a.calls.IndexOf("feed:"+n),"single unload before prefetch");}
ResetFlags();PT_SET.bUpDn1XtOnlyOne=true;a=new UpDownLoad();ws=Station(4);a.onPlace=()=>VAR.ClearMt=true;a.ThreeNozzleInitializeMode(ws);a.ThreeNozzleRunSpecialMode(ref quit,ws);Check(a.fresh==1&&ws.list_md[1].res==-2&&ws.list_md[2].res==-2,"single clear stops subsequent feed");
ResetFlags();PT_SET.bUpDn1XtOnlyOne=true;VAR.ClearMt=true;a=new UpDownLoad();ws=Station(4);a.threeNozzleDrain=true;a.ThreeNozzleInitializeMode(ws);Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.OK&&a.fresh==0&&ws.list_md.All(x=>x.res==-2),"initial single clear never prefetches");
ResetFlags();PT_SET.bGrrFlow=true;VAR.ClearMt=true;a=new UpDownLoad();ws=Station(4);ws.GrrUDLcnt=1;a.ThreeNozzleInitializeMode(ws);Check(!a.ThreeNozzleHasSpecialMode,"initial GRR clear routes normal unloading");
ResetFlags();PT_SET.bUpDn1XtOnlyOne=true;VAR.ClearMt=true;a=new UpDownLoad();ws=Station(0);a.ThreeNozzleInitializeMode(ws);Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.OK&&a.threeNozzleModeDone,"all-empty special clear releases after home");
ResetFlags();PT_SET.bUpDn1XtOnlyOne=true;VAR.isAutoChkMode=true;a=new UpDownLoad();ws=Station(2);ws.list_md[0].bAutoChkOk=true;a.ThreeNozzleInitializeMode(ws);Check(a.ThreeNozzleRunSpecialMode(ref quit,ws)==EM_RES.OK&&a.fresh==1&&ws.list_md[0].res==-2,"passed inspection pocket is unloaded but not refilled");
ResetFlags();VAR.isAutoChkMode=true;a=new UpDownLoad();var md=new WS.MdDat{bAutoChkOk=true};Check(!a.ThreeNozzleCanLoadPocket(md),"passed inspection pocket skipped");VAR.isAutoChkMode=false;Check(a.ThreeNozzleCanLoadPocket(md),"inspection marker does not affect production");md.benable=false;Check(!a.ThreeNozzleCanLoadPocket(md),"disabled pockets not loaded");
ResetFlags();PT_SET.bGrrFlow=true;a=new UpDownLoad();ws=Station(8);ws.GrrUDLcnt=1;a.FindMod=EM_FIN_MOD.LOW;a.ThreeNozzleInitializeMode(ws);a.ThreeNozzleRunSpecialMode(ref quit,ws);Check(ws.list_md.Skip(4).All(x=>x.res==0),"GRR respects other arm reservation partition");
}
}
}
'@
Add-Type -TypeDefinition ($source + [Environment]::NewLine + $stubs)
[UI.UpDownLoad]::RunRegression()
Write-Output 'PASS: production mode adapters: GRR rotation, counter ownership, resume, mid-clear, fixed single nozzle, inspection filter, arm partition.'





