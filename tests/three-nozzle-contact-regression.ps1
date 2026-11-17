param([string]$RepoRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $RepoRoot 'UI/Class/MT.ContactOffset.cs') -Raw
$fake = @'
namespace MotionCtrl {
 public enum EM_RES { OK, QUIT, MOVE_PROTECT }
 public class CARD { public bool isReady=true; }
 public class AXIS { public enum HOME_STA { OK }; public HOME_STA home_status; public CARD card=new CARD(); public bool isStop=true; public double fenc_pos,fcmd_pos; }
}
namespace UI {
 using MotionCtrl;
 public class UpDownLoad { public int id; }
 public class XT { public UpDownLoad Parent=new UpDownLoad(); public AXIS ax_x=new AXIS(),ax_y=new AXIS(),ax_z=new AXIS(),ax_u=new AXIS(); }
 public static class ThreeNozzleCoordinator { public static bool Tray; public static bool IsTrayOnlyPhase(int arm){return Tray;} }
 public static partial class MT {
  public static XT N=new XT(); public static AXIS Z2=new AXIS(),Z3=new AXIS(); public static bool ThrowMove;
  public static int GetLateralArmId(AXIS a){return a==N.ax_x||a==N.ax_y||a==N.ax_u?0:-1;}
  public static AXIS[] GetArmZAxes(int arm){return new[]{N.ax_z,Z2,Z3};}
  public static bool AreAllArmZSafe(int arm){foreach(var z in GetArmZAxes(arm)) if(System.Math.Abs(z.fenc_pos)>0.5) return false;return true;}
  public static EM_RES Move(ref bool q,ref AXIS x,double xp,ref AXIS y,double yp,bool bchkps=true) {
   if(!IsThreeNozzleLateralMoveSafe(x,xp)||!IsThreeNozzleLateralMoveSafe(y,yp)||IsThreeNozzleLateralMoveSafe(N.ax_u,90)) throw new System.Exception("scope violation");
   if(ThrowMove)throw new System.Exception("injected");
   x.fenc_pos=x.fcmd_pos=xp;y.fenc_pos=y.fcmd_pos=yp;return EM_RES.OK;
  }
  static void Check(bool value,string message){if(!value)throw new System.Exception(message);}
  public static void Test(){bool quit=false;N.ax_z.fenc_pos=N.ax_z.fcmd_pos=10;
   Check(!IsThreeNozzleLateralMoveSafe(N.ax_x,1),"ordinary low Z rejected");
   Check(MoveContactOffset(ref quit,N,1,2,10)==EM_RES.OK,"bounded selected nozzle offset");
   Check(!IsThreeNozzleLateralMoveSafe(N.ax_x,2),"scope must close");
   Check(MoveContactOffset(ref quit,N,30,2,10)!=EM_RES.OK,"oversize rejected");
   Z2.fenc_pos=1;Check(MoveContactOffset(ref quit,N,2,2,10)!=EM_RES.OK,"other nozzle down rejected");Z2.fenc_pos=0;
   Check(MoveContactOffset(ref quit,N,2,2,0)!=EM_RES.OK,"wrong active Z rejected");
   ThreeNozzleCoordinator.Tray=true;Check(MoveContactOffset(ref quit,N,2,2,10)!=EM_RES.OK,"tray lease rejects contact");ThreeNozzleCoordinator.Tray=false;
   ThrowMove=true;try{MoveContactOffset(ref quit,N,2,2,10);}catch(System.Exception){}ThrowMove=false;
   Check(!IsThreeNozzleLateralMoveSafe(N.ax_x,2),"exception clears scope");
   Check(MoveContactOffset(ref quit,N,double.NaN,2,10)!=EM_RES.OK,"invalid coordinate rejected");
  }
 }
}
'@
Add-Type -TypeDefinition ($source + $fake)
[UI.MT]::Test()
Write-Host 'PASS: actual contact offset guard, selected Z, other Z clearance, limits, tray lease and exception cleanup.'
