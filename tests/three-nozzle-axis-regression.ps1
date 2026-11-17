param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
$mt=Get-Content (Join-Path $RepoRoot 'UI/Class/MT.cs') -Raw
$partial=Join-Path $RepoRoot 'UI/Class/MT.ThreeNozzle.cs'
if (!(Test-Path $partial)) { throw 'Missing independent Z safety implementation: six Z axes must be protected.' }
$helpers=Get-Content $partial -Raw
$defs=([regex]::Matches($mt,'(?m)^\s*public static (?:CARD|AXIS|List<CARD>|List<AXIS>) [^\r\n]+;') | ForEach-Object Value) -join "`n"
$sensorStart=$mt.IndexOf('public static EM_RES ChkSafeSen(')
$sensorEnd=$mt.IndexOf('        public static bool isSafeSen',$sensorStart)
$sensor=$mt.Substring($sensorStart,$sensorEnd-$sensorStart)
$code=@"
using System;
using System.Collections.Generic;
namespace MotionCtrl {
public enum EM_RES { OK, QUIT, MOVE_PROTECT, PARA_ERR, MOVE_ERR, SAFE_PROTECT }
public class GPIO { public enum IO_STA { OUT_ON } }
public class CARD { public enum BRAND { ZMOTION, LEADSHINE, ORIENTALMOTOR } public enum TYPE { MOTION, IO } public bool isReady=true; public int id; public List<AXIS> AxList=new List<AXIS>(); public CARD(int id,params object[] args){this.id=id;} }
public class AXIS { public enum MT_TYPE { SEVER, STEP } public enum ENC_TYPE { YES,NO } public enum HOME_STA { OK,ERROR } public HOME_STA home_status=HOME_STA.OK; public bool isStop=true; public double fenc_pos=8; public int m_id; public CARD card; public bool Fail; public AXIS(int n,CARD c,params object[] args){card=c;m_id=(c.id<<8)+n;c.AxList.Add(this);} public void Stop(){isStop=true;} }
}
namespace UI { using MotionCtrl;
public static class NozzleZCalibrationMotion { public static bool IsBusy, IsOwnerThread; }
public static class BaseAction { public static bool bRuning; }
public static class ThreeNozzleCoordinator { public static bool WorkersStopped=true; public static bool TrayOnly; public static bool IsTrayOnlyPhase(int id){return TrayOnly;} }
public static class ThreeNozzleHardwareConfig { public static double Boundary=double.NaN; public static double SafeTrayMaxY(int id){return Boundary;} }
public static class RuntimeMachineMode { public static bool IsTrayBoxSwapped=false; }
public static partial class MT {
$defs
public static void EnterControlled(){controlledArmMotionDepth++;}
public static void ExitControlled(){controlledArmMotionDepth--;}
public static EM_RES ChkAllSafeSen(int n){return EM_RES.OK;}
$sensor
public static List<int> Commands=new List<int>();
public static EM_RES Move(ref bool quit,ref AXIS a,double p,int timeout=20000,bool events=false){Commands.Add(a.m_id);if(quit)return EM_RES.QUIT;if(a.Fail)return EM_RES.MOVE_ERR;a.fenc_pos=p;return EM_RES.OK;}
}
}
"@
# Compile actual production helpers alongside extracted production axis declarations.
Add-Type -TypeDefinition ($code+"`n"+($helpers -replace '(?m)^using [^;]+;','' -replace 'namespace UI\s*\{','namespace UI { using MotionCtrl;'))
$z=[UI.MT]::GetArmZAxes(0)
if (($z.m_id -join ',') -ne '2054,2056,2058') { throw 'Arm1 Z mapping mismatch' }
if (([UI.MT]::GetArmZAxes(1).m_id -join ',') -ne '2060,2062,2064') { throw 'Arm2 Z mapping mismatch' }
if ([UI.MT]::CARD_ZMC432_8.AxList.Count -ne 12) { throw 'New card must register exactly twelve Z/U axes' }
if ([UI.MT]::AXIS_UDL1_X.card.id -ne 4) { throw 'XY migrated unexpectedly' }
if ([UI.MT]::AreAllArmZSafe(0)) { throw 'Extended Z must block lateral move' }
$q=$false
if ([UI.MT]::RaiseAllArmZ([ref]$q,0,20000,$false) -ne 'OK') { throw 'Raise failed' }
if (([UI.MT]::Commands -join ',') -ne '2054,2056,2058') { throw 'Every independent Z must finish before lateral move' }
if (![UI.MT]::AreAllArmZSafe(0)) { throw 'All Z safe expected' }
$z[2].fenc_pos=9;$z[2].Fail=$true
if ([UI.MT]::RaiseAllArmZ([ref]$q,0,20000,$false) -ne 'MOVE_ERR') { throw 'Z failure must propagate' }
if ([UI.MT]::AreAllArmZSafe(0)) { throw 'Failed raise must not publish safe' }
Write-Host 'PASS: production mapping, registration, all-Z raise order, and failed-Z safety gate.'



[UI.ThreeNozzleCoordinator]::TrayOnly=$true
if ([UI.MT]::CheckThreeNozzleTrayMove([UI.MT]::AXIS_UDL1_Y,0) -ne 'MOVE_PROTECT') { throw 'Missing boundary must fail closed' }
[UI.ThreeNozzleHardwareConfig]::Boundary=100
[UI.MT]::AXIS_UDL1_Y.fenc_pos=50
if ([UI.MT]::CheckThreeNozzleTrayMove([UI.MT]::AXIS_UDL1_Y,101) -ne 'MOVE_PROTECT') { throw 'Entering fixture during tray-only phase must be rejected' }
if ([UI.MT]::CheckThreeNozzleTrayMove([UI.MT]::AXIS_UDL1_Y,99) -ne 'OK') { throw 'Tray path should remain available' }
[UI.MT]::AXIS_UDL1_Y.fenc_pos=101
if ([UI.MT]::CheckThreeNozzleTrayMove([UI.MT]::AXIS_UDL1_X,0) -ne 'MOVE_PROTECT') { throw 'Path starting outside safe tray area must be rejected' }
Write-Host 'PASS: tray-only path checks current and target Y and fails closed without boundary.'
# Compile the actual Place method and inject movement outcomes to exercise finally return semantics.
$xt=Get-Content (Join-Path $RepoRoot 'UI/Class/XT.cs') -Raw
$start=$xt.IndexOf('public EM_RES Place(ref bool')
$open=$xt.IndexOf('{',$start);$depth=1;$end=$open+1
while($depth -gt 0){if($xt[$end] -eq '{'){$depth++};if($xt[$end] -eq '}'){$depth--};$end++}
$place=$xt.Substring($start,$end-$start)
$fixture=@"
using System;
namespace CleanupProbe {
public enum EM_RES { OK, PARA_ERR, PLACE_ERR, MOVE_ERR }
public struct ST_XYZA { public double z; }
public class Axis {}
public class Valve { public bool isONByChkSen=true; public void SetOff(){} public void SetOn(){} }
public static class Thread { public static void Sleep(int n){} }
public class Msg { public enum EM_MSGTYPE { ERR } public void AddMsg(params object[] x){} }
public static class VAR { public static bool IsChinese=true; public static Msg msg=new Msg(); }
public static class DReport { public enum EmErrCode { PlaceFailed } public enum EmHareware { Nozzle } }
public static class ERR_ALM { public enum EmErrItem { UpDownLoadAbnormal } }
public static class MT { public static EM_RES Down=EM_RES.OK, Up=EM_RES.OK; public static EM_RES Move(ref bool b,ref Axis a,double p){return p==0?Up:Down;} }
public class Probe {
 public int ZApproachDirection=1,id=0;public string disc="test"; public Axis ax_z=new Axis(); public Valve cy_zk=new Valve(),gpio_pzk=new Valve();
 public EM_RES PositionResult=EM_RES.OK; public EM_RES MoveToPickPlacePosition(ref bool q,ST_XYZA p,bool z){return PositionResult;}
 $place
}
}
"@
Add-Type -TypeDefinition $fixture
$probe=[CleanupProbe.Probe]::new();$pos=[CleanupProbe.ST_XYZA]::new();$pos.z=10
[CleanupProbe.MT]::Up='MOVE_ERR'
if ($probe.Place([ref]$q,$pos,$false,$false) -ne 'MOVE_ERR') {throw 'Successful place followed by failed raise must fail'}
[CleanupProbe.MT]::Up='OK';[CleanupProbe.MT]::Down='MOVE_ERR'
if ($probe.Place([ref]$q,$pos,$false,$false) -ne 'MOVE_ERR') {throw 'Successful cleanup must preserve descent failure'}
[CleanupProbe.MT]::Down='OK';$probe.PositionResult='MOVE_ERR'
if ($probe.Place([ref]$q,$pos,$false,$false) -ne 'MOVE_ERR') {throw 'Successful cleanup must preserve position failure'}
Write-Host 'PASS: actual XT.Place preserves initial failure and propagates cleanup failure.'
# Execute the production ZupMove staging block against a command recorder.
$begin=$mt.IndexOf('            // Cross-card lateral movement must finish')
$finish=$mt.IndexOf('            return ret;',$begin)
$stage=$mt.Substring($begin,$finish-$begin)
$stageFixture=@"
using System;
using System.Collections.Generic;
namespace StageProbe {
public enum EM_RES { OK, MOVE_ERR }
public class AXIS { public string Name; public bool Z; }
public static class Run {
 public static List<string> Calls=new List<string>(); public static bool FailLateral;
 public static bool IsArmZAxis(AXIS a){return a!=null && a.Z;}
 public static EM_RES Move(ref bool q,ref AXIS a,double p,int timeout,bool events){Calls.Add(a.Name);return EM_RES.OK;}
 public static EM_RES Move(ref bool q,ref AXIS a,double ap,ref AXIS b,double bp,ref AXIS c,double cp,ref AXIS d,double dp,int timeout,bool events){Calls.Add("lateral-complete");if(IsArmZAxis(a)||IsArmZAxis(b)||IsArmZAxis(c)||IsArmZAxis(d))throw new Exception("Z dispatched with XY");return FailLateral?EM_RES.MOVE_ERR:EM_RES.OK;}
 public static EM_RES Execute(){bool bquit=false,bdoevent=false;int time_out_ms=20000;double xpos=1,ypos=2,zpos=3,rpos=4;AXIS ax_x=new AXIS{Name="X"},ax_y=new AXIS{Name="Y"},ax_z=new AXIS{Name="Z",Z=true},ax_r=null;EM_RES ret;
 $stage
 return ret;
 }
}
}
"@
Add-Type -TypeDefinition $stageFixture
if ([StageProbe.Run]::Execute() -ne 'OK' -or ([StageProbe.Run]::Calls -join ',') -ne 'lateral-complete,Z') {throw 'ZupMove must complete XY before Z descent'}
[StageProbe.Run]::Calls.Clear();[StageProbe.Run]::FailLateral=$true
if ([StageProbe.Run]::Execute() -ne 'MOVE_ERR' -or ([StageProbe.Run]::Calls -join ',') -ne 'lateral-complete') {throw 'Lateral failure must prevent Z descent'}
if (![UI.MT]::HasMixedArmLateralAndZ(@([UI.MT]::AXIS_UDL1_X,[UI.MT]::AXIS_UDL1_Z3))) {throw 'Direct mixed XY/Z batch must be rejected'}
Write-Host 'PASS: production ZupMove stages Z after lateral completion and suppresses descent after failure.'
$ui=Get-Content (Join-Path $RepoRoot 'UI/Compment/XtOfsAdj.cs') -Raw
if ($ui -notmatch 'NozzleDisplayIds = \{ 0, 1, 4, 2, 3, 5 \}' -or $ui -match 'xt\.id\s*[/%%]\s*2') {throw 'Six-nozzle calibration selector and explicit ownership mapping required'}
if ($ui -notmatch 'xt.SaveOffset\(VAR.gsys_set.cur_product_name\)') {throw 'Selected-nozzle calibration save must remain connected'}
Write-Host 'PASS: six-nozzle calibration selection/source wiring contract.'

[UI.BaseAction]::bRuning=$true
if ([UI.MT]::ChkSafeSen([UI.MT]::AXIS_UDL1_Z3.m_id) -ne 'SAFE_PROTECT') {throw 'Direct manual third-Z movement during auto must fail closed'}
[UI.MT]::EnterControlled()
try {
 if ([UI.MT]::ChkSafeSen([UI.MT]::AXIS_UDL1_Z3.m_id) -ne 'OK') {throw 'Controlled automatic movement must remain available'}
} finally { [UI.MT]::ExitControlled() }
if ([UI.MT]::ChkSafeSen([UI.MT]::AXIS_UDL2_U3.m_id) -ne 'SAFE_PROTECT') {throw 'After token cleanup, manual movement must be blocked again'}
[UI.BaseAction]::bRuning=$false;[UI.ThreeNozzleCoordinator]::WorkersStopped=$false
if ([UI.MT]::ChkSafeSen([UI.MT]::AXIS_UDL1_X.m_id) -ne 'SAFE_PROTECT') {throw 'Pending worker must protect arm even after COM run ends'}
[UI.ThreeNozzleCoordinator]::WorkersStopped=$true
if ([UI.MT]::ChkSafeSen([UI.MT]::AXIS_UDL1_X.m_id) -ne 'OK') {throw 'Stopped manual mode should remain available'}
Write-Host 'PASS: actual ChkSafeSen rejects manual bypass, allows controlled token, restores protection after cleanup.'

[UI.NozzleZCalibrationMotion]::IsBusy=$true
if ([UI.MT]::ChkSafeSen([UI.MT]::AXIS_UDL1_U3.m_id) -ne 'SAFE_PROTECT') {throw 'Calibration lease must reject nonowner manual movement'}
[UI.NozzleZCalibrationMotion]::IsOwnerThread=$true
if ([UI.MT]::ChkSafeSen([UI.MT]::AXIS_UDL1_U3.m_id) -ne 'OK') {throw 'Calibration owner must retain its motion access'}
[UI.NozzleZCalibrationMotion]::IsBusy=$false
[UI.NozzleZCalibrationMotion]::IsOwnerThread=$false
Write-Host 'PASS: calibration lease rejects nonowner and permits owner at actual sensor gate.'
