using System;
using MotionCtrl;

namespace UI
{
    public static partial class MT
    {
        [ThreadStatic]
        private static int controlledArmMotionDepth;

        private static bool IsUncontrolledArmMotion(int axisId)
        {
            if (NozzleZCalibrationMotion.IsBusy && !NozzleZCalibrationMotion.IsOwnerThread) return true;
            if (controlledArmMotionDepth > 0 || (!BaseAction.bRuning && ThreeNozzleCoordinator.WorkersStopped)) return false;
            foreach (AXIS axis in Axlist_UDL1_ExpLC)
                if (axis.m_id == axisId) return true;
            foreach (AXIS axis in Axlist_UDL2_ExpLC)
                if (axis.m_id == axisId) return true;
            return false;
        }

        public const bool IsThreeNozzleHardware = true;

        public static AXIS[] GetArmZAxes(int armId)
        {
            if (armId == 0) return new[] { AXIS_UDL1_Z, AXIS_UDL1_Z2, AXIS_UDL1_Z3 };
            if (armId == 1) return new[] { AXIS_UDL2_Z, AXIS_UDL2_Z2, AXIS_UDL2_Z3 };
            throw new ArgumentOutOfRangeException("armId");
        }

        public static bool HasMixedArmLateralAndZ(AXIS[] axes)
        {
            bool lateral = false, z = false;
            foreach (AXIS axis in axes)
            {
                lateral |= GetLateralArmId(axis) >= 0;
                z |= IsArmZAxis(axis);
            }
            return lateral && z;
        }

        public static bool IsArmZAxis(AXIS axis)
        {
            if (axis == null) return false;
            for (int arm = 0; arm < 2; arm++)
                foreach (AXIS z in GetArmZAxes(arm))
                    if (z.m_id == axis.m_id) return true;
            return false;
        }

        public static int GetLateralArmId(AXIS axis)
        {
            if (axis == null) return -1;
            if (axis.m_id == AXIS_UDL1_X.m_id || axis.m_id == AXIS_UDL1_Y.m_id ||
                axis.m_id == AXIS_UDL1_U1.m_id || axis.m_id == AXIS_UDL1_U2.m_id || axis.m_id == AXIS_UDL1_U3.m_id) return 0;
            if (axis.m_id == AXIS_UDL2_X.m_id || axis.m_id == AXIS_UDL2_Y.m_id ||
                axis.m_id == AXIS_UDL2_U1.m_id || axis.m_id == AXIS_UDL2_U2.m_id || axis.m_id == AXIS_UDL2_U3.m_id) return 1;
            return -1;
        }

        #region 运动拦截原因（用于日志）
        private static string AxisName(AXIS axis) { return axis == null ? "?" : axis.disc; }

        /// <summary>横向运动（臂X/Y/U）被 Z 状态拦截时的原因：逐根列出本臂 Z 的状态。</summary>
        internal static string DescribeLateralBlock(AXIS axis, double target)
        {
            try
            {
                int arm = GetLateralArmId(axis);
                if (arm < 0) return "";
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat("{0} 目标 {1:F3} 被拦截：臂{2} 横向运动要求三根Z已回零、已停止且编码器在0±{3}mm。", AxisName(axis), target, arm + 1, ArmZSafeTolerance);
                foreach (AXIS z in GetArmZAxes(arm))
                {
                    bool ok = z.card.isReady && z.home_status == AXIS.HOME_STA.OK && z.isStop &&
                        !double.IsNaN(z.fenc_pos) && Math.Abs(z.fenc_pos) <= ArmZSafeTolerance;
                    sb.AppendFormat(" {0}[{1}]: 板卡{2} 回零={3} 停止={4} 编码器={5:F3} 指令={6:F3} 状态={7};",
                        z.disc, ok ? "OK" : "不满足", z.card.isReady ? "就绪" : "未就绪", z.home_status, z.isStop,
                        z.fenc_pos, z.fcmd_pos, z.str_status);
                }
                if (ThreeNozzleCoordinator.IsTrayOnlyPhase(arm)) sb.Append(" 当前处于只在料盘区阶段。");
                return sb.ToString();
            }
            catch (Exception ex) { return "拦截原因读取异常：" + ex.Message; }
        }

        /// <summary>
        /// 臂Z下降时，本臂X/Y必须已停止：横向运动前会检查三根Z在0，这里补上反方向——横向还在走时不许下Z
        /// （防止别的线程/流程正在平移本臂时，本臂线程继续下发取放料下探）。返回 null 表示允许。
        /// </summary>
        internal static string DescribeZDescentWhileArmMoving(AXIS[] axes, double[] targets)
        {
            try
            {
                for (int i = 0; i < axes.Length && i < targets.Length; i++)
                {
                    AXIS z = axes[i];
                    if (!IsArmZAxis(z) || double.IsNaN(targets[i]) || double.IsNaN(z.fenc_pos)) continue;
                    if (targets[i] >= z.fenc_pos - 0.05) continue;   // 抬升或原地，不拦
                    int arm = -1;
                    for (int a = 0; a < 2 && arm < 0; a++)
                        foreach (AXIS az in GetArmZAxes(a))
                            if (az.m_id == z.m_id) { arm = a; break; }
                    if (arm < 0) continue;
                    AXIS[] lateral = arm == 0 ? new[] { AXIS_UDL1_X, AXIS_UDL1_Y } : new[] { AXIS_UDL2_X, AXIS_UDL2_Y };
                    foreach (AXIS l in lateral)
                    {
                        if (l == null || l.isStop) continue;
                        return string.Format("{0} 下降到 {1:F3} 被拦截：臂{2} {3} 仍在运动（指令={4:F3} 编码器={5:F3}），横向未停止禁止下Z",
                            AxisName(z), targets[i], arm + 1, AxisName(l), l.fcmd_pos, l.fenc_pos);
                    }
                }
            }
            catch { }
            return null;
        }

        internal static string DescribeTrayMoveBlock(AXIS axis, double target)
        {
            try
            {
                int owner = GetLateralArmId(axis);
                AXIS y = owner == 0 ? AXIS_UDL1_Y : AXIS_UDL2_Y;
                double targetY = axis.m_id == y.m_id ? target : y.fenc_pos;
                return string.Format("{0} 目标 {1:F3} 被拦截：臂{2} 处于只在料盘区阶段（预取/尾部卸料），Y 当前 {3:F3}、目标 {4:F3} 不能超过料盘安全区 SafeTrayMaxY={5:F3}",
                    AxisName(axis), target, owner + 1, y.fenc_pos, targetY, ThreeNozzleHardwareConfig.SafeTrayMaxY(owner));
            }
            catch (Exception ex) { return "拦截原因读取异常：" + ex.Message; }
        }
        #endregion

        private static readonly bool[] trayOnlyManualBypassLogged = new bool[2];

        public static EM_RES CheckThreeNozzleTrayMove(AXIS axis, double target)
        {
            int owner = GetLateralArmId(axis);
            if (owner < 0 || !ThreeNozzleCoordinator.IsTrayOnlyPhase(owner)) return EM_RES.OK;
            // [R11] 停机后（不在运行、上下料线程都已结束）“只在料盘区”是上一轮残留的状态，不再限制手动示教/校正/点动；
            // 19 号 16:01:28 停机后点模块2 移到工站拍照位 Y=972.982 被这里拦下（定位失败），只能先回零。
            // 再按开始时会先自动复位（抬Z→回安全位→对账），运行中照旧严格限制。
            if (VAR.gsys_set.status != EM_SYS_STA.RUN && !BaseAction.bRuning && ThreeNozzleCoordinator.WorkersStopped)
            {
                if (!trayOnlyManualBypassLogged[owner])
                {
                    trayOnlyManualBypassLogged[owner] = true;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("[R11] 臂{0} 停机中手动移动：忽略上一轮残留的“只在料盘区”限制（运行中仍限制 Y≤{1:F3}）",
                        owner + 1, ThreeNozzleHardwareConfig.SafeTrayMaxY(owner)));
                }
                return EM_RES.OK;
            }
            trayOnlyManualBypassLogged[owner] = false;
            double boundary = ThreeNozzleHardwareConfig.SafeTrayMaxY(owner);
            AXIS y = owner == 0 ? AXIS_UDL1_Y : AXIS_UDL2_Y;
            double targetY = axis.m_id == y.m_id ? target : y.fenc_pos;
            if (double.IsNaN(boundary) || double.IsInfinity(boundary) ||
                double.IsNaN(y.fenc_pos) || double.IsNaN(targetY) ||
                double.IsInfinity(y.fenc_pos) || double.IsInfinity(targetY) ||
                y.fenc_pos > boundary || targetY > boundary) return EM_RES.MOVE_PROTECT;
            return EM_RES.OK;
        }

        #region 单臂调试
        // 只调一个上下料臂、另一臂暂时无法回零时使用。操作员确认另一臂已手动停在 X 原点侧、
        // 三个 Z 已全部抬起且不在料盘上方后，双臂防撞检查把“未回零的另一臂 X”按停在原点（外伸=0）处理。
        // 只存在于本次运行，不保存；开启期间禁止自动运行；关闭空跑窗口或系统复位时自动退出。
        private static volatile int singleArmDebugArm = -1;
        public static int SingleArmDebugArm { get { return singleArmDebugArm; } }

        public static void EnableSingleArmDebug(int armId)
        {
            if (armId < 0 || armId > 1) return;
            singleArmDebugArm = armId;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                "【单臂调试】开启：只调上下料模块{0}，未回零的上下料模块{1} X 按停在原点处理（操作员已确认）",
                armId + 1, 2 - armId));
        }

        public static void DisableSingleArmDebug(string reason)
        {
            if (singleArmDebugArm < 0) return;
            singleArmDebugArm = -1;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, "【单臂调试】关闭：" + (reason ?? ""));
        }

        /// <summary>单臂调试中，该臂是被放置不动、且 X 未回零的那一臂。</summary>
        internal static bool IsParkedUnhomedArm(int armId)
        {
            int active = singleArmDebugArm;
            if (active < 0 || active == armId) return false;
            AXIS x = armId == 0 ? AXIS_UDL1_X : AXIS_UDL2_X;
            return x.home_status != AXIS.HOME_STA.OK;
        }

        /// <summary>防撞计算用的另一臂 X 外伸量：单臂调试中未回零的放置臂按 0 处理，其余按实际位置。</summary>
        internal static double ArmXExtentForSafety(int armId)
        {
            if (IsParkedUnhomedArm(armId)) return 0;
            AXIS x = armId == 0 ? AXIS_UDL1_X : AXIS_UDL2_X;
            return Math.Abs(x.fenc_pos);
        }
        #endregion

        #region 上下料 XY 单独回零（调试）
        // 默认情况下，上下料 X 单独回零会被 ChkSafePos 里的“上下料 X 未复位禁止移动”挡住（回零中的轴自己就是未复位），
        // 只能走模块整体回零。开启后，仅当这根 X/Y 轴自己正在回零（home_status == HOMING）时跳过这条检查；
        // “本臂三个 Z 已回零并在安全高度”、门禁、双臂防撞等其余检查不变。
        // 只存在于本次运行，不保存；开启期间禁止自动运行；关闭空跑窗口或系统复位时自动关闭。
        private static volatile bool udAxisHomeDebug;
        public static bool UdAxisHomeDebug { get { return udAxisHomeDebug; } }
        /// <summary>开关状态变化（界面同步复选框用）。</summary>
        public static event Action UdAxisHomeDebugChanged;

        public static void EnableUdAxisHomeDebug()
        {
            if (udAxisHomeDebug) return;
            udAxisHomeDebug = true;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, "【XY单独回零】开启：上下料 X/Y 可在轴列表里单独回零（Z 安全、门禁、防撞检查仍生效）");
            Action h = UdAxisHomeDebugChanged;
            if (h != null) h();
        }

        public static void DisableUdAxisHomeDebug(string reason)
        {
            if (!udAxisHomeDebug) return;
            udAxisHomeDebug = false;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, "【XY单独回零】关闭：" + (reason ?? ""));
            Action h = UdAxisHomeDebugChanged;
            if (h != null) h();
        }

        /// <summary>开关开启，且该轴自己正在回零。</summary>
        internal static bool IsUdAxisSelfHoming(AXIS axis)
        {
            return udAxisHomeDebug && axis != null && axis.home_status == AXIS.HOME_STA.HOMING;
        }
        #endregion

        /// <summary>
        /// 臂上三根Z视为“在安全高度(0位)”的编码器容差(mm)。现场确认 Z 比0位低 1mm 以内不会碰到夹具/料盘；
        /// 目前 Z 停在0位时编码器常有 -0.6~-0.74mm 的静差，原 0.5mm 会误拦截横向运动。
        /// </summary>
        public const double ArmZSafeTolerance = 1.0;

        /// <summary>本臂三根 Z 的指令位置都已是 0（安全高度）。</summary>
        private static bool AreAllArmZCommandedHome(int armId)
        {
            foreach (AXIS axis in GetArmZAxes(armId))
                if (double.IsNaN(axis.fcmd_pos) || Math.Abs(axis.fcmd_pos) > 0.001) return false;
            return true;
        }

        public static bool AreAllArmZSafe(int armId, double tolerance = ArmZSafeTolerance)
        {
            foreach (AXIS axis in GetArmZAxes(armId))
            {
                if (!axis.card.isReady || axis.home_status != AXIS.HOME_STA.OK || !axis.isStop ||
                    double.IsNaN(axis.fenc_pos) || Math.Abs(axis.fenc_pos) > tolerance) return false;
            }
            return true;
        }

        public static EM_RES RaiseAllArmZ(ref bool quit, int armId, int timeout = 20000, bool events = false)
        {
            // 三根 Z 都已经停在安全高度（指令和编码器都在 0）：不再逐根下发“走到 0”的空运动。
            // 每次空运动也要走一遍板卡通讯和联锁检查（实测约 13ms），每个 ZupMove 都会先调这里，
            // 一轮换料要调二三十次，累计约 1s。
            if (AreAllArmZSafe(armId) && AreAllArmZCommandedHome(armId)) return EM_RES.OK;
            // Complete every Z on the new card before permitting X/Y on the old card.
            foreach (AXIS axis in GetArmZAxes(armId))
            {
                if (quit) return EM_RES.QUIT;
                if (!axis.card.isReady || axis.home_status != AXIS.HOME_STA.OK)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("运动拦截：抬升臂{0}的Z时 {1} 板卡{2}、回零状态={3}，禁止运动",
                        armId + 1, axis.disc, axis.card.isReady ? "就绪" : "未就绪", axis.home_status));
                    return EM_RES.MOVE_PROTECT;
                }
                AXIS moving = axis;
                EM_RES result = Move(ref quit, ref moving, 0, timeout, events);
                if (result != EM_RES.OK) return result;
            }
            if (AreAllArmZSafe(armId)) return EM_RES.OK;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "运动拦截：臂" + (armId + 1) + " 抬升全部Z后仍不满足安全高度。" +
                DescribeLateralBlock(armId == 0 ? AXIS_UDL1_X : AXIS_UDL2_X, double.NaN));
            return EM_RES.MOVE_PROTECT;
        }
    }
}
