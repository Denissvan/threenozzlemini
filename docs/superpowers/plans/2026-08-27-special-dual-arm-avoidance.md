# 特殊双臂避障最小让位 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让特殊双臂避障在预约失败时由对臂安全地最小让位，并保证自定义安全距离在勾选后可靠生效。

**Architecture:** `PT_SET` 继续是安全距离的唯一来源，系统参数页改用独立控件并在保存时显式同步。`MT` 保留预约锁并维护按让位臂分组的请求；`UpDownLoad` 仅在本臂流程循环的安全检查点执行自己的 Z 抬起和最小 X 内退。

**Tech Stack:** C# WinForms、.NET Task/Monitor、PowerShell 源码回归脚本、现有 MotionCtrl 轴控制层。

---

### Task 1: 为参数持久化和最小让位写失败回归用例

**Files:**
- Create: `tests/special-dual-arm-minimum-yield-regression.ps1`
- Modify: `tests/special-dual-arm-avoidance-wait-regression.ps1` only when the file exists in the working tree; otherwise keep the new script self-contained.

- [ ] **Step 1: 写入失败的配置与协调器结构检查**

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$frSys = Get-Content -LiteralPath (Join-Path $root 'UI\FormView\FrSys.DualArmSafetyDistance.cs') -Raw -Encoding Default
$frSysMain = Get-Content -LiteralPath (Join-Path $root 'UI\FormView\FrSys.cs') -Raw -Encoding Default
$mt = Get-Content -LiteralPath (Join-Path $root 'UI\Class\MT.cs') -Raw -Encoding Default
$ud = Get-Content -LiteralPath (Join-Path $root 'UI\Class\UpDownLoad.cs') -Raw -Encoding Default

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

Assert-Condition ($frSys -like '*NumericUpDown nudUdSafeDistance*') 'Dual-arm distance must use its own NumericUpDown.'
Assert-Condition ($frSys -like '*ApplyDualArmSafetyDistanceControls*') 'Saving must explicitly synchronize the dual-arm controls.'
Assert-Condition ($frSysMain -like '*ApplyDualArmSafetyDistanceControls();*') 'FrSys.GetData must synchronize dual-arm settings before product save.'
Assert-Condition ($mt -like '*DualArmYieldRequest*') 'MT must own a cooperative yield request type.'
Assert-Condition ($mt -like '*TryGetDualArmYieldRequest*') 'The owning arm must retrieve, rather than be directly driven by, a yield request.'
Assert-Condition ($ud -like '*ProcessPendingSpecialDualArmYield*') 'UpDownLoad must process its own pending yield at a flow safe point.'

$yieldStart = $ud.IndexOf('private EM_RES ProcessPendingSpecialDualArmYield')
$yieldEnd = $ud.IndexOf('#endregion', $yieldStart)
$yieldBlock = $ud.Substring($yieldStart, $yieldEnd - $yieldStart)
Assert-Condition ($yieldBlock -notmatch 'GoZero\(') 'Special avoidance yield must not call GoZero.'
Assert-Condition ($yieldBlock -match 'Zup\(ref bquit\)') 'Yield must raise this arm before its X move.'
Write-Host 'PASS: custom distance UI and owner-thread minimum-yield structure are present.'
```

- [ ] **Step 2: 运行用例，确认现有代码失败**

Run: `powershell -ExecutionPolicy Bypass -File tests\special-dual-arm-minimum-yield-regression.ps1`

Expected: FAIL because `nudUdSafeDistance`, explicit save synchronization, and owner-thread yield request handling do not yet exist.

- [ ] **Step 3: 在已有特殊预约回归中保留等待语义检查**

Ensure the existing or new script asserts all of the following:

```powershell
Assert-Condition ($mt -like '*Monitor.Wait(dualArmXReservationLock*') 'Reservation conflicts must wait without busy spinning.'
Assert-Condition ($mt -like '*Monitor.PulseAll(dualArmXReservationLock)*') 'Reservation release and yield completion must wake waiters.'
Assert-Condition ($mt -like '*return EM_RES.TIMEOUT*') 'An unresolved request must still have a bounded fault outcome.'
```

- [ ] **Step 4: 提交测试先行的失败用例**

```powershell
git add tests/special-dual-arm-minimum-yield-regression.ps1
git commit -m "测试：覆盖特殊避障最小让位"
```

### Task 2: 分离并可靠保存双臂安全距离参数

**Files:**
- Modify: `UI/FormView/FrSys.DualArmSafetyDistance.cs:10-75`
- Modify: `UI/FormView/FrSys.cs:1261` and `UI/FormView/FrSys.cs` `bEn` method
- Test: `tests/special-dual-arm-minimum-yield-regression.ps1`

- [ ] **Step 1: 替换复用的 `safepos` 控件**

In `FrSys.DualArmSafetyDistance.cs`, remove all changes to `groupBox60` and `safepos` from `PrepareDualArmSafetyDistanceEditor` and `InitDualArmSafetyDistanceControls`. Keep `groupBox60` and `safepos` for `PT_SET.safepos`; create these independent fields instead:

```csharp
private GroupBox groupBoxDualArmSafetyDistance;
private NumericUpDown nudUdSafeDistance;
private CheckBox chkCustomUdSafeDistance;
private CheckBox chkSpecialDualArmAvoidance;

private void InitDualArmSafetyDistanceControls()
{
    groupBoxDualArmSafetyDistance = new GroupBox
    {
        Text = "双臂安全距离(mm)",
        Location = new Point(590, 704),
        Size = new Size(195, 112),
        Font = groupBox60.Font
    };
    nudUdSafeDistance = new NumericUpDown
    {
        DecimalPlaces = 1,
        Increment = 1,
        Minimum = (decimal)PT_SET.MinUdSafeDistance,
        Maximum = (decimal)PT_SET.MaxUdSafeDistance,
        Location = new Point(106, 70),
        Size = new Size(80, 28),
        Value = (decimal)PT_SET.UdSafeDistance
    };
    chkCustomUdSafeDistance = new CheckBox
    {
        Text = "启用自定义",
        AutoSize = true,
        Location = new Point(10, 22),
        Checked = PT_SET.bCustomUdSafeDistance
    };
    chkSpecialDualArmAvoidance = new CheckBox
    {
        Text = "特殊避障",
        AutoSize = true,
        Location = new Point(10, 46),
        Checked = PT_SET.bSpecialDualArmAvoidance
    };
    groupBoxDualArmSafetyDistance.Controls.Add(chkCustomUdSafeDistance);
    groupBoxDualArmSafetyDistance.Controls.Add(chkSpecialDualArmAvoidance);
    groupBoxDualArmSafetyDistance.Controls.Add(nudUdSafeDistance);
    tp_setparm_1.Controls.Add(groupBoxDualArmSafetyDistance);
}
```

Attach `CheckedChanged` and `ValueChanged` handlers after all initial values are assigned. Each handler updates only its corresponding `PT_SET` field, updates enabled state, and logs the four distance values through one helper.

- [ ] **Step 2: 在保存路径显式同步，而不依赖 UI 事件**

Add this method to the same partial form and call it as the first statement in `GetData()` before `PT_SET.safepos = (double)safepos.Value`:

```csharp
private void ApplyDualArmSafetyDistanceControls()
{
    if (nudUdSafeDistance == null) return;

    PT_SET.bCustomUdSafeDistance = chkCustomUdSafeDistance.Checked;
    PT_SET.bSpecialDualArmAvoidance = chkSpecialDualArmAvoidance.Checked;
    PT_SET.UdSafeDistance = (double)nudUdSafeDistance.Value;
    LogDualArmSafetyDistance("系统参数保存");
}

private void LogDualArmSafetyDistance(string source)
{
    VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format(
        "{0}: DualArmSafeDistance CustomEnabled={1} SpecialAvoidance={2} Configured={3:F1} Effective={4:F1}",
        source, PT_SET.bCustomUdSafeDistance, PT_SET.bSpecialDualArmAvoidance,
        PT_SET.UdSafeDistance, PT_SET.EffectiveUdSafeDistance));
}
```

Expose a null-safe `SetDualArmSafetyDistanceControlsEnabled(bool enabled)` and invoke it from `FrSys.bEn(bool en)` so the dynamically created controls follow the existing permission model.

- [ ] **Step 3: 运行回归用例，确认参数页结构通过**

Run: `powershell -ExecutionPolicy Bypass -File tests\special-dual-arm-minimum-yield-regression.ps1`

Expected: the first three UI assertions PASS; coordinator assertions remain FAIL until Task 3.

- [ ] **Step 4: 提交参数页变更**

```powershell
git add UI/FormView/FrSys.DualArmSafetyDistance.cs UI/FormView/FrSys.cs tests/special-dual-arm-minimum-yield-regression.ps1
git commit -m "修复：可靠保存双臂自定义安全距离"
```

### Task 3: 在预约协调器中登记最小让位请求

**Files:**
- Modify: `UI/Class/MT.cs:120-130` and `UI/Class/MT.cs:1625-1695`
- Test: `tests/special-dual-arm-minimum-yield-regression.ps1`

- [ ] **Step 1: 定义请求数据和边界常量**

Add the following near the existing reservation fields. A request is indexed by the arm that must yield, so only that arm's owning thread can complete it.

```csharp
private const double DualArmYieldMargin = 1.0;

private sealed class DualArmYieldRequest
{
    public bool Pending;
    public int Generation;
    public int RequesterArmId;
    public double RequesterExtent;
    public double YieldTargetExtent;
}

private static readonly DualArmYieldRequest[] dualArmYieldRequests =
{
    new DualArmYieldRequest(), new DualArmYieldRequest()
};
```

- [ ] **Step 2: 在预约失败时登记请求，但仍保持等待**

Inside `AcquireDualArmXReservation`, after calculating `requestedExtent` and before `Monitor.Wait`, queue or update a request for `otherId`. Compute the requested target as follows:

```csharp
double allowedOtherExtent = Math.Max(0,
    UpDownLoad.DisAxisX - PT_SET.EffectiveUdSafeDistance - requestedExtent - DualArmYieldMargin);

QueueDualArmYieldRequest(otherId, armId, requestedExtent, allowedOtherExtent);
```

`QueueDualArmYieldRequest` must lock `dualArmXReservationLock`, increment the request generation only for a newly pending request or a changed tighter target, store requester id and target extent, write a `特殊避障最小让位请求` log, and call `Monitor.PulseAll`.

```csharp
private static int QueueDualArmYieldRequest(int yieldingArmId, int requesterArmId,
    double requesterExtent, double yieldTargetExtent)
{
    lock (dualArmXReservationLock)
    {
        DualArmYieldRequest request = dualArmYieldRequests[yieldingArmId];
        bool tighterRequest = !request.Pending ||
            request.RequesterArmId != requesterArmId ||
            yieldTargetExtent < request.YieldTargetExtent - 0.01;
        if (tighterRequest)
        {
            request.Pending = true;
            request.Generation++;
            request.RequesterArmId = requesterArmId;
            request.RequesterExtent = requesterExtent;
            request.YieldTargetExtent = yieldTargetExtent;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format(
                "特殊避障最小让位请求：请求模块={0}，让位模块={1}，请求外伸={2:F3}，让位目标外伸={3:F3}",
                requesterArmId + 1, yieldingArmId + 1, requesterExtent, yieldTargetExtent));
        }
        Monitor.PulseAll(dualArmXReservationLock);
        return request.Generation;
    }
}

private static void ClearDualArmYieldRequest(int yieldingArmId, int generation, string source)
{
    if (generation <= 0) return;
    lock (dualArmXReservationLock)
    {
        DualArmYieldRequest request = dualArmYieldRequests[yieldingArmId];
        if (!request.Pending || request.Generation != generation) return;
        request.Pending = false;
        VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format(
            "特殊避障最小让位请求清除：让位模块={0}，代次={1}，来源={2}",
            yieldingArmId + 1, generation, source));
        Monitor.PulseAll(dualArmXReservationLock);
    }
}
```

On `QUIT` and on the existing 30 second `TIMEOUT` branch, clear only the matching request generation and pulse waiters. Do not return `ABORT`; preserve the existing bounded `Monitor.Wait` behavior.

- [ ] **Step 3: 提供仅供对臂线程使用的读取和完成接口**

Add the following methods under the same lock:

```csharp
public static bool TryGetDualArmYieldRequest(int yieldingArmId,
    out int generation, out double targetX)
{
    generation = 0;
    targetX = 0;
    if (yieldingArmId < 0 || yieldingArmId >= dualArmYieldRequests.Length) return false;
    lock (dualArmXReservationLock)
    {
        DualArmYieldRequest request = dualArmYieldRequests[yieldingArmId];
        if (!request.Pending) return false;
        generation = request.Generation;
        targetX = yieldingArmId == 0 ? request.YieldTargetExtent : -request.YieldTargetExtent;
        return true;
    }
}

public static void CompleteDualArmYieldRequest(int yieldingArmId, int generation,
    bool completed, string source)
{
    lock (dualArmXReservationLock)
    {
        DualArmYieldRequest request = dualArmYieldRequests[yieldingArmId];
        if (!request.Pending || request.Generation != generation) return;
        request.Pending = false;
        VAR.msg.AddMsg(completed ? Msg.EM_MSGTYPE.SYS : Msg.EM_MSGTYPE.ERR, string.Format(
            "特殊避障最小让位{0}：模块={1}，代次={2}，来源={3}",
            completed ? "完成" : "失败", yieldingArmId + 1, generation, source));
        Monitor.PulseAll(dualArmXReservationLock);
    }
}
```

Neither method may call `AXIS.MoveTo`, `MT.Move`, `GoZero`, or any `COM.UDLoad*` motion method.

- [ ] **Step 4: 运行回归用例，确认协调器结构通过**

Run: `powershell -ExecutionPolicy Bypass -File tests\special-dual-arm-minimum-yield-regression.ps1`

Expected: request type and request retrieval assertions PASS; the owner safe-point assertion remains FAIL until Task 4.

- [ ] **Step 5: 提交预约请求变更**

```powershell
git add UI/Class/MT.cs tests/special-dual-arm-minimum-yield-regression.ps1
git commit -m "功能：特殊避障增加最小让位请求"
```

### Task 4: 由上下料所属线程在安全点执行最小让位

**Files:**
- Modify: `UI/Class/UpDownLoad.cs:6888-6910`
- Modify: `UI/Class/UpDownLoad.cs` near `Zup(ref bool bquit)` for private helpers
- Test: `tests/special-dual-arm-minimum-yield-regression.ps1`

- [ ] **Step 1: 添加本臂安全点处理方法**

Add these methods to `UpDownLoad`:

```csharp
private bool AreAxesStoppedForDualArmYield()
{
    return ax_x.isStop && ax_y.isStop && ax_z.isStop &&
           ax_u1.isStop && ax_u2.isStop;
}

private EM_RES ProcessPendingSpecialDualArmYield(ref bool bquit)
{
    if (!PT_SET.bSpecialDualArmAvoidance) return EM_RES.OK;

    int generation;
    double targetX;
    if (!MT.TryGetDualArmYieldRequest(id, out generation, out targetX)) return EM_RES.OK;
    for (int waitCount = 0; !AreAxesStoppedForDualArmYield(); waitCount++)
    {
        if (bquit) return EM_RES.QUIT;
        if (waitCount >= 1000)
        {
            MT.CompleteDualArmYieldRequest(id, generation, false,
                string.Format("{0}, 等待本臂静止超时", disc));
            return EM_RES.ERR;
        }
        Thread.Sleep(20);
    }

    VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format(
        "特殊避障最小让位开始：{0}，当前X={1:F3}，目标X={2:F3}",
        disc, ax_x.fenc_pos, targetX));
    EM_RES res = Zup(ref bquit);
    if (res == EM_RES.OK)
        res = MT.Move(ref bquit, ref ax_x, targetX);
    MT.CompleteDualArmYieldRequest(id, generation, res == EM_RES.OK,
        string.Format("{0}, result={1}", disc, res));
    return res;
}
```

Do not add `GoZero`, Y-axis repositioning, direct commands to `COM.UDLoad1` or `COM.UDLoad2`, or a forced material discard path. `Zup` is required before the X-only minimum retreat.

- [ ] **Step 2: 在每个新业务步骤前处理该请求**

At the top of the `while (!bquit && !bfinal)` loop in `UpDownLoadModleAct`, after the `bquit` check and before `Proc == CHECK` dispatch, call:

```csharp
res = ProcessPendingSpecialDualArmYield(ref bquit);
if (res != EM_RES.OK) return res;
```

This placement guarantees that the owner completes the previous synchronous flow action first, handles the request before starting its next business action, and retains ownership of its own axes.

- [ ] **Step 3: 处理尚未能到达安全点的请求**

If `AreAxesStoppedForDualArmYield()` is false, leave the request pending and do not clear it. The requester remains in the existing bounded wait. Add a rate-limited log on the first deferred observation, not on every loop pass. If `Zup` or the X move fails, complete the request as failed so the requester emits its existing timeout/fault path rather than retrying a stale request forever.

- [ ] **Step 4: 运行全部相关回归脚本**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tests\special-dual-arm-minimum-yield-regression.ps1
powershell -ExecutionPolicy Bypass -File tests\special-dual-arm-avoidance-wait-regression.ps1
powershell -ExecutionPolicy Bypass -File tests\manual-dual-arm-positioning-regression.ps1
powershell -ExecutionPolicy Bypass -File tests\dual-arm-recovery-gate-regression.ps1
```

Expected: all available scripts PASS. If an existing script is intentionally absent from the working tree, report it as unavailable instead of restoring it.

- [ ] **Step 5: 静态检查和提交**

Run:

```powershell
git diff --check
git diff -- UI/Class/MT.cs UI/Class/UpDownLoad.cs UI/FormView/FrSys.DualArmSafetyDistance.cs UI/FormView/FrSys.cs tests/special-dual-arm-minimum-yield-regression.ps1
```

Confirm that the new yield method contains no `GoZero(` and no direct command to the other `COM.UDLoad` instance, then commit:

```powershell
git add UI/Class/MT.cs UI/Class/UpDownLoad.cs UI/FormView/FrSys.DualArmSafetyDistance.cs UI/FormView/FrSys.cs tests/special-dual-arm-minimum-yield-regression.ps1
git commit -m "修复：特殊避障按最小距离协作让位"
```

### Task 5: 现场验证记录

**Files:**
- Modify: `docs/superpowers/specs/2026-08-27-special-dual-arm-avoidance-design.md` only if actual implementation differs from the approved behavior.
- Test: machine log from a controlled dry-run.

- [ ] **Step 1: 按两组配置确认启动日志**

1. 仅勾选特殊避障：启动日志必须为 `CustomEnabled=False` 且 `Effective=89.0`。
2. 同时勾选自定义并设为 84 mm、特殊避障：启动日志必须为 `CustomEnabled=True` 且 `Effective=84.0`。

- [ ] **Step 2: 复现外侧等待场景**

Place module 1 at a completed tray-place position and request the conflicting module 2 outward X movement. Confirm logs appear in order:

```text
特殊避障最小让位请求
特殊避障最小让位开始
特殊避障最小让位完成
双臂避障等待结束
双臂避障允许
```

Confirm neither arm logs `GoZero` as part of this sequence and module 1's X endpoint is the calculated minimum inward target, not X=0.

- [ ] **Step 3: 记录不能由源码验证的硬件风险**

Full project build may remain unavailable because the legacy solution references local hardware/runtime assemblies. Report static regression results separately from the required controlled machine dry-run; do not claim collision risk is physically eliminated until the dry-run succeeds.
