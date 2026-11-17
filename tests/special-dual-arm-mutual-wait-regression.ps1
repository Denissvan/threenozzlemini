param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw -Encoding UTF8 (Join-Path $RepoRoot 'UI/Class/MT.cs')
function Get-Section([string]$start, [string]$end) {
    $a = $source.IndexOf($start, [StringComparison]::Ordinal)
    $b = $source.IndexOf($end, $a, [StringComparison]::Ordinal)
    if ($a -lt 0 -or $b -le $a) { throw "Missing source section: $start" }
    return $source.Substring($a, $b - $a)
}
$fields = Get-Section 'private static readonly object dualArmXReservationLock' 'private static readonly object dualArmRecoveryGateLock'
# Shorten only the test clock; execute the production reservation methods unchanged.
$fields = $fields.Replace('DualArmAvoidanceWaitTimeoutMs = 30000', 'DualArmAvoidanceWaitTimeoutMs = 1500')
$methods = Get-Section 'private static int QueueDualArmYieldRequestUnsafe' 'private static int GetDualArmMotionId'
$harness = @'
using System;
using System.Threading;
using System.Threading.Tasks;
public enum EM_RES { OK, ERR, QUIT, TIMEOUT }
public class AXIS { public double fenc_pos; }
public static class PT_SET {
    public static bool bSpecialDualArmAvoidance = true;
    public static double EffectiveUdSafeDistance = 89;
}
public class Msg {
    public enum EM_MSGTYPE { DBG, SYS, ERR }
    public void AddMsg(EM_MSGTYPE type, string message) { }
}
public class State { public volatile bool bquit; }
public static class VAR {
    public static Msg msg = new Msg();
    public static State gsys_set = new State();
}
public static class MT {
    public static AXIS GuardZ;
    public static bool AreAllArmZSafe(int id) { return GuardZ != null && Math.Abs(GuardZ.fenc_pos) <= 0.5; }
    public static AXIS AXIS_UDL1_X = new AXIS(), AXIS_UDL2_X = new AXIS();
    public static double EffectiveDualArmCombinedExtentLimit = 345;
    public static void BeQuitEn(bool a, bool b) { VAR.gsys_set.bquit = true; }
__FIELDS__
__METHODS__
    static AXIS Axis(int id) { return id == 0 ? AXIS_UDL1_X : AXIS_UDL2_X; }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reset() {
        VAR.gsys_set.bquit = false;
        dualArmYieldPriority = -1;
        positioningYieldScope = null;
        for (int i=0; i<2; i++) {
            dualArmXReserved[i] = false;
            dualArmXReservedTarget[i] = 0;
            dualArmYieldRequests[i] = new DualArmYieldRequest();
        }
        AXIS_UDL1_X.fenc_pos = 201.172;
        AXIS_UDL2_X.fenc_pos = -139.424;
    }
    static void Clean() {
        Check(dualArmYieldPriority == -1, "Priority leaked");
        Check(!dualArmXReserved[0] && !dualArmXReserved[1], "Reservation leaked");
        Check(!dualArmYieldRequests[0].Pending && !dualArmYieldRequests[1].Pending, "Yield request leaked");
    }
    static EM_RES RunArm(int id, double target, bool failYield, bool scopeEnabled, Action yielded) {
        bool quit = false;
        int ownerThread = Thread.CurrentThread.ManagedThreadId;
        PositioningYieldHandler callback = delegate(ref bool q) {
            Check(!Monitor.IsEntered(dualArmXReservationLock), "Motion callback ran under reservation lock");
            Check(Thread.CurrentThread.ManagedThreadId == ownerThread, "Another thread commanded the arm");
            Check(positioningYieldScope == null, "Nested retreat can recursively yield");
            int generation; double retreat;
            if (!TryGetDualArmYieldRequest(id, out generation, out retreat)) return EM_RES.OK;
            if (failYield) return EM_RES.ERR;
            Check(Math.Abs(retreat) <= Math.Abs(Axis(id).fenc_pos), "Yield increased extension");
            EM_RES r = AcquireDualArmXReservation(ref q, id, retreat);
            if (r != EM_RES.OK) return r;
            Axis(id).fenc_pos = retreat;
            ReleaseDualArmXReservation(id);
            CompleteDualArmYieldRequest(id, generation, true, "test");
            yielded();
            return EM_RES.OK;
        };
        using (scopeEnabled ? new PositioningYieldScope(id, callback) : null) {
            EM_RES r = AcquireDualArmXReservation(ref quit, id, target);
            if (r != EM_RES.OK) return r;
            Check(Math.Abs(target) + Math.Abs(Axis(1-id).fenc_pos) <= 345.001, "Unsafe target admitted");
            Axis(id).fenc_pos = target;
            ReleaseDualArmXReservation(id);
            // Model completion of the business action, followed by its normal retreat.
            Thread.Sleep(20);
            r = AcquireDualArmXReservation(ref quit, id, 0);
            if (r != EM_RES.OK) return r;
            Axis(id).fenc_pos = 0;
            ReleaseDualArmXReservation(id);
        }
        Check(positioningYieldScope == null, "Scope leaked beyond positioning");
        return EM_RES.OK;
    }
    static void Pair(int first, bool fail, bool scoped) {
        Reset();
        int yields = 0;
        double[] targets = { 208.246, -151.267 };
        Task<EM_RES> a = Task.Run(() => RunArm(first, targets[first], false, scoped, () => Interlocked.Increment(ref yields)));
        Check(SpinWait.SpinUntil(() => { lock(dualArmXReservationLock) return dualArmYieldPriority == first; }, 1000), "First request not queued");
        Task<EM_RES> b = Task.Run(() => RunArm(1-first, targets[1-first], fail, scoped, () => Interlocked.Increment(ref yields)));
        Check(Task.WaitAll(new Task[] { a,b }, 5000), "Mutual wait did not finish");
        if (fail) Check(b.Result == EM_RES.ERR && a.Result == EM_RES.QUIT, "Yield failure did not stop both arms");
        else if (scoped) Check(a.Result == EM_RES.OK && b.Result == EM_RES.OK && yields > 0, "Both positioning steps must finish after yielding");
        else Check(a.Result == EM_RES.TIMEOUT && b.Result == EM_RES.TIMEOUT && yields == 0, "Unapproved step must not retreat");
        Clean();
    }
    public static void Run() {
        for (int i=0; i<10; i++) { Pair(0, false, true); Pair(1, false, true); }
        Pair(0, true, true);
        Pair(0, false, false);
        Reset();
        Task<EM_RES> stopped = Task.Run(() => RunArm(0, 208.246, false, false, () => {}));
        Check(SpinWait.SpinUntil(() => { lock(dualArmXReservationLock) return dualArmYieldPriority == 0; }, 1000), "Stop test did not enter wait");
        VAR.gsys_set.bquit = true;
        Check(stopped.Wait(2000) && stopped.Result == EM_RES.QUIT, "Stop during wait ignored");
        Clean();
        Reset();
        bool quit = true;
        Check(AcquireDualArmXReservation(ref quit, 0, 208.246) == EM_RES.QUIT, "Stop ignored");
        Clean();
        Console.WriteLine("PASS: both request orders, source-thread retreat, unchanged targets, failure, stop, unsafe-step timeout and cleanup.");
    }
}
'@
$upDown = Get-Content -Raw -Encoding UTF8 (Join-Path $RepoRoot 'UI/Class/UpDownLoad.cs')
$guardStart = $upDown.IndexOf('internal EM_RES ProcessPositioningYield')
$guardEnd = $upDown.IndexOf('private EM_RES ProcessPendingSpecialDualArmYield', $guardStart)
$guard = $upDown.Substring($guardStart, $guardEnd - $guardStart)
$guardHarness = @'
public class PositioningGuardTests {
    public int id;
    public PositioningGuardTests() { MT.GuardZ = ax_z; }
    public AXIS ax_z = new AXIS();
    public string disc = "test";
    public bool stopped = true;
    public int calls;
    private bool AreAxesStoppedForDualArmYield() { return stopped; }
    private EM_RES ProcessPendingSpecialDualArmYield(ref bool q) { calls++; return EM_RES.OK; }
__GUARD__
    public static void Run() {
        var test = new PositioningGuardTests();
        bool quit = false;
        test.ax_z.fenc_pos = 10;
        if (test.ProcessPositioningYield(ref quit) != EM_RES.ERR || test.calls != 0)
            throw new System.Exception("Lowered Z must not retreat");
        test.ax_z.fenc_pos = 0;
        test.stopped = false;
        test.ProcessPositioningYield(ref quit);
        if (test.calls != 0) throw new System.Exception("Moving axes must not retreat");
        test.stopped = true;
        quit = true;
        if (test.ProcessPositioningYield(ref quit) != EM_RES.QUIT || test.calls != 0)
            throw new System.Exception("Stopped workflow must not retreat");
        quit = false;
        if (test.ProcessPositioningYield(ref quit) != EM_RES.OK || test.calls != 1)
            throw new System.Exception("Safe positioning must service yield");
    }
}
'@
# Compile both production sections with the same simulated hardware.
Add-Type -TypeDefinition ($harness.Replace('__FIELDS__', $fields).Replace('__METHODS__', $methods) + $guardHarness.Replace('__GUARD__', $guard)) -Language CSharp -ErrorAction Stop
[MT]::Run()
[PositioningGuardTests]::Run()
Write-Host 'PASS: lowered Z, moving axes and stopped workflow cannot execute a positioning retreat.'
$xt = Get-Content -Raw -Encoding UTF8 (Join-Path $RepoRoot 'UI/Class/XT.cs')
if ([regex]::Matches($xt, 'res = MoveToPickPlacePosition\(ref bquit, (st_pos|pos), (false|true)\);').Count -ne 4) {
    throw 'All four pre-descent pick/place positioning branches must opt in.'
}
Write-Host 'PASS: pick/place integration covers all four pre-descent branches.'
