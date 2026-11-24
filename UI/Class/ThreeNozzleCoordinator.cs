using System;
using System.Threading;
using System.Threading.Tasks;
using MotionCtrl;

namespace UI
{
    // The tray lease remains held after a worker finishes, until the next exchange owns the arm.
    // MT.Move enforces this lease for every lateral command; station release alone is insufficient.
    public static class ThreeNozzleCoordinator
    {
        private static readonly object Sync = new object();
        private static readonly bool[] TrayOnly = new bool[2];
        private static Task<EM_RES>[] pending;
        private static WS station;
        // 尾部卸料完成后提前预取的目标工站（按转盘顺序下一个需要上料的工站）；null=不提前预取
        private static WS earlyTarget;
        private static bool unloading;
        private static volatile bool faulted;
        private static readonly bool[] exchangeReleased = new bool[2];
        private static long exchangeEpoch;
        private static readonly long[] tailStartedEpoch = new long[2];
        private static readonly WS[] workerStation = new WS[2], workerNext = new WS[2];
        private static readonly bool[] workerUnload = new bool[2], retryable = new bool[2];
        private static readonly WS[] tailStartedFor = new WS[2], tailCompletedFor = new WS[2];
        private static bool workersStarted;
        private static readonly object[] Gates = { new object(), new object() };
        private static readonly int[] Moving = new int[2];
        public static object MotionGate(int armId) { return Gates[armId]; }
        public static void MarkMotionStarted(int armId) { Interlocked.Increment(ref Moving[armId]); }
        public static void MarkMotionEnded(int armId) { Interlocked.Decrement(ref Moving[armId]); }
        /// <summary>该臂此刻没有正在执行的轴运动（主动让位前检查）。</summary>
        public static bool ArmMotionIdle(int armId) { return armId >= 0 && armId < 2 && Volatile.Read(ref Moving[armId]) == 0; }
        private static bool MotionIdle { get { return Volatile.Read(ref Moving[0]) == 0 && Volatile.Read(ref Moving[1]) == 0; } }
        /// <summary>三吸嘴调度故障（需复位）。开始键前据此自动复位，见 COM.ThreeNozzleAutoResetBeforeRun。</summary>
        public static bool Faulted { get { return faulted; } }

        public static bool IsTrayOnlyPhase(int armId)
        {
            lock (Sync) return armId >= 0 && armId < 2 && TrayOnly[armId];
        }

        public static bool IsArmOutsideFixture(int armId)
        {
            UpDownLoad arm = armId == 0 ? COM.UDLoad1 : COM.UDLoad2;
            if (arm == null || !IsTrayOnlyPhase(armId)) return false;
            double limit = ThreeNozzleHardwareConfig.SafeTrayMaxY(armId);
            return !faulted && !double.IsNaN(limit) && !double.IsInfinity(limit) &&
                arm.ax_y.card.isReady && arm.ax_y.home_status == AXIS.HOME_STA.OK &&
                !double.IsNaN(arm.ax_y.fenc_pos) && !double.IsInfinity(arm.ax_y.fenc_pos) &&
                !double.IsInfinity(arm.ax_y.fcmd_pos) && arm.ax_y.fenc_pos <= limit &&
                arm.ax_y.fcmd_pos <= limit;
        }

        public static bool CanMoveFixture()
        {
            if (faulted) return false;
            for (int i = 0; i < 2; ++i)
            {
                UpDownLoad arm = i == 0 ? COM.UDLoad1 : COM.UDLoad2;
                if (IsTrayOnlyPhase(i))
                {
                    if (!IsArmOutsideFixture(i)) return false;
                    continue;
                }
                if (arm == null || !arm.ax_x.card.isReady || !arm.ax_y.card.isReady ||
                    arm.ax_x.home_status != AXIS.HOME_STA.OK || arm.ax_y.home_status != AXIS.HOME_STA.OK || Volatile.Read(ref Moving[i]) != 0 || !MT.AreAllArmZSafe(i) || !arm.ax_x.isStop || !arm.ax_y.isStop ||
                    double.IsNaN(arm.ax_x.fenc_pos) || double.IsNaN(arm.ax_y.fenc_pos) ||
                    double.IsNaN(arm.ax_x.fcmd_pos) || double.IsNaN(arm.ax_y.fcmd_pos) ||
                    double.IsInfinity(arm.ax_y.fenc_pos) || double.IsInfinity(arm.ax_y.fcmd_pos) ||
                    Math.Abs(arm.ax_x.fenc_pos) > 3 || Math.Abs(arm.ax_x.fcmd_pos) > 3 ||
                    arm.ax_y.fenc_pos > arm.list_xt[1].st_cap_pos.y + 3 || arm.ax_y.fcmd_pos > arm.list_xt[1].st_cap_pos.y + 3)
                    return false;
            }
            return true;
        }

        /// <summary>预取料启动不了时的原因（只读状态，不发运动指令），用于日志。</summary>
        public static string DescribePrefetchBlock()
        {
            try
            {
                var sb = new System.Text.StringBuilder("原因检查：");
                lock (Sync)
                {
                    if (faulted) sb.Append("三吸嘴任务处于故障状态(停止后再按开始会自动复位，或用上下料回零)；");
                    if (pending != null && (!pending[0].IsCompleted || !pending[1].IsCompleted)) sb.Append("上一轮预取/卸料任务未结束；");
                }
                if (!MotionIdle) sb.Append("有臂正在运动；");
                for (int i = 0; i < 2; ++i)
                {
                    UpDownLoad arm = i == 0 ? COM.UDLoad1 : COM.UDLoad2;
                    string name = "臂" + (i + 1);
                    if (arm == null) { sb.Append(name + "未初始化；"); continue; }
                    if (!arm.ax_x.card.isReady || !arm.ax_y.card.isReady) sb.Append(name + " XY板卡未就绪；");
                    if (arm.ax_x.home_status != AXIS.HOME_STA.OK || arm.ax_y.home_status != AXIS.HOME_STA.OK) sb.Append(name + " XY未回零；");
                    if (!MT.AreAllArmZSafe(i)) sb.Append(name + " 三个Z未全部回零/停止/在安全高度；");
                    if (Math.Abs(arm.ax_x.fenc_pos) > 3) sb.Append(string.Format("{0} X={1:F3}不在原点±3；", name, arm.ax_x.fenc_pos));
                    if (arm.ax_y.fenc_pos > arm.list_xt[1].st_cap_pos.y + 3)
                        sb.Append(string.Format("{0} Y={1:F3}超过下相机拍照位+3({2:F3})；", name, arm.ax_y.fenc_pos, arm.list_xt[1].st_cap_pos.y + 3));
                    double limit = ThreeNozzleHardwareConfig.SafeTrayMaxY(i);
                    if (double.IsNaN(limit) || double.IsInfinity(limit))
                        sb.Append(string.Format("{0} 未配置 ThreeNozzleHardware.ini [Arm{1}] SafeTrayMaxY（当前NaN），预取料不允许启动；", name, i + 1));
                    else if (arm.ax_y.fenc_pos > limit || arm.ax_y.fcmd_pos > limit)
                        sb.Append(string.Format("{0} Y={1:F3}大于SafeTrayMaxY={2:F3}；", name, arm.ax_y.fenc_pos, limit));
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "原因检查异常：" + ex.Message;
            }
        }

        public static EM_RES BeginPrefetch(WS ws)
        {
            EM_RES resumed = ResumeStoppedWorkers();
            if (resumed != EM_RES.OK) return resumed;
            lock (Sync)
            {
                if (faulted) return EM_RES.ERR;
                if (station == ws && !unloading && workersStarted) return EM_RES.OK;
                // Previous tails stay in the tray area. Each arm adopts/waits for its own task.
                if (unloading && workersStarted)
                {
                    if (!CanMoveFixture()) return EM_RES.SAFE_PROTECT;
                    station = ws;
                    exchangeEpoch++;
                    unloading = false;
                    exchangeReleased[0] = exchangeReleased[1] = false;
                    return EM_RES.OK;
                }
            }
            EM_RES result = WaitPending();
            if (result != EM_RES.OK) return result;
            lock (Gates[0]) lock (Gates[1]) lock (Sync)
            {
                if (faulted) return EM_RES.ERR;
                station = ws;
                exchangeEpoch++;
                unloading = false;
                earlyTarget = null;
                exchangeReleased[0] = exchangeReleased[1] = false;
                return StartWorkers(false);
            }
        }

        public static bool IsPrefetchStarted(WS ws)
        {
            lock (Sync) return ws != null && ReferenceEquals(station, ws) && !unloading && workersStarted;
        }

        // Compatibility for callers which really require both arms; UdLoad uses the arm overload.
        public static EM_RES WaitPrefetch(WS ws)
        {
            EM_RES result = WaitPrefetch(ws, 0);
            return result == EM_RES.OK ? WaitPrefetch(ws, 1) : result;
        }

        public static EM_RES WaitPrefetch(WS ws, int armId)
        {
            if (armId < 0 || armId > 1) return EM_RES.PARA_ERR;
            lock (Sync)
            {
                if (station != ws || unloading || !workersStarted) return EM_RES.PARA_ERR;
                if (exchangeReleased[armId]) return EM_RES.OK;
                if (VAR.gsys_set.bquit || UpDownLoad.bquit) return EM_RES.QUIT;
            }
            EM_RES result = WaitArmPending(armId);
            if (result != EM_RES.OK) return result;
            lock (Gates[armId]) lock (Sync)
            {
                if (faulted) return EM_RES.ERR;
                if (station != ws || unloading) return EM_RES.PARA_ERR;
                if (exchangeReleased[armId]) return EM_RES.OK;
                if (ws == null || !ws.isInFeedPos || ws.FeedStatus != WS.EM_STA.REDAYFORUPDOWNLOAD ||
                    Volatile.Read(ref Moving[armId]) != 0) return EM_RES.SAFE_PROTECT;
                TrayOnly[armId] = false;
                exchangeReleased[armId] = true;
                tailStartedFor[armId] = tailCompletedFor[armId] = null;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("三吸嘴: 模块{0} 本臂预取就绪，独立进入{1}（不等待另一臂预取）", armId + 1, ws.disc));
            }
            return EM_RES.OK;
        }

        public static void SetNextFeed(WS ws, WS nextFeed)
        {
            lock (Sync) { if (station == ws && !unloading) earlyTarget = nextFeed; }
        }

        public static bool IsArmExchangeCompleted(WS ws, int armId)
        {
            lock (Sync) return armId >= 0 && armId < 2 && station == ws &&
                tailStartedFor[armId] == ws && tailStartedEpoch[armId] == exchangeEpoch;
        }

        public static EM_RES BeginArmUnload(WS ws, int armId)
        {
            if (armId < 0 || armId > 1) return EM_RES.PARA_ERR;
            lock (Gates[armId]) lock (Sync)
            {
                if (faulted) return EM_RES.ERR;
                if (station != ws || !exchangeReleased[armId]) return EM_RES.PARA_ERR;
                if (VAR.gsys_set.bquit || UpDownLoad.bquit) return EM_RES.QUIT;
                if (tailStartedFor[armId] == ws && tailStartedEpoch[armId] == exchangeEpoch) return EM_RES.OK;
                UpDownLoad arm = armId == 0 ? COM.UDLoad1 : COM.UDLoad2;
                if (arm == null || Volatile.Read(ref Moving[armId]) != 0 || !MT.AreAllArmZSafe(armId) ||
                    !arm.ax_x.card.isReady || !arm.ax_y.card.isReady ||
                    arm.ax_x.home_status != AXIS.HOME_STA.OK || arm.ax_y.home_status != AXIS.HOME_STA.OK ||
                    !arm.ax_x.isStop || !arm.ax_y.isStop ||
                    double.IsNaN(arm.ax_x.fenc_pos) || double.IsNaN(arm.ax_x.fcmd_pos) ||
                    // 最后一组不回零时 X 留在夹具这一列：只在料盘区阶段靠 Y≤SafeTrayMaxY（下面 IsArmOutsideFixture）保证离开夹具，X 交给双臂 X 预约
                    (!UpDownLoad.ThreeNozzleTailSkipHomeEnabled && (Math.Abs(arm.ax_x.fenc_pos) > 3 || Math.Abs(arm.ax_x.fcmd_pos) > 3)))
                    return EM_RES.SAFE_PROTECT;
                TrayOnly[armId] = true;
                if (!IsArmOutsideFixture(armId)) { TrayOnly[armId] = false; return EM_RES.SAFE_PROTECT; }
                tailStartedFor[armId] = ws;
                tailStartedEpoch[armId] = exchangeEpoch;
                QueueWorker(armId, ws, true, earlyTarget);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("三吸嘴: 模块{0} 已离开{1}，独立启动尾部卸料（不等待另一臂换料）", armId + 1, ws.disc));
                return EM_RES.OK;
            }
        }

        /// <summary>已经在为这个工站尾部卸料（不用再算下一个上料工站、不用再启动）。</summary>
        public static bool IsUnloadingFor(WS ws)
        {
            lock (Sync) return !faulted && ws != null && station == ws && unloading && workersStarted;
        }

        public static EM_RES BeginUnload(WS ws)
        {
            return BeginUnload(ws, null);
        }

        /// <summary>
        /// 尾部卸料。nextFeed 不为空时，两臂卸完后接着在料盘区为它提前预取第一组新料
        /// （转盘转动/合夹具期间完成，两臂全程保持只在料盘区）。
        /// </summary>
        public static EM_RES BeginUnload(WS ws, WS nextFeed)
        {
            lock (Sync)
                if (!faulted && station == ws && unloading && workersStarted) return EM_RES.OK;
            lock (Gates[0]) lock (Gates[1]) lock (Sync)
            {
                if (faulted) return EM_RES.ERR;
                if (station == ws && unloading && workersStarted) return EM_RES.OK;
                if (station != ws || !CanMoveFixture()) return EM_RES.SAFE_PROTECT;
                for (int i = 0; i < 2; i++)
                {
                    if (tailStartedFor[i] == ws && tailStartedEpoch[i] == exchangeEpoch) continue;
                    if (pending != null && !pending[i].IsCompleted) return EM_RES.ERR;
                    double limit = ThreeNozzleHardwareConfig.SafeTrayMaxY(i);
                    UpDownLoad arm = i == 0 ? COM.UDLoad1 : COM.UDLoad2;
                    if (double.IsNaN(limit) || double.IsInfinity(limit) || arm == null ||
                        arm.ax_y.fenc_pos > limit || arm.ax_y.fcmd_pos > limit) return EM_RES.SAFE_PROTECT;
                }
                unloading = true;
                earlyTarget = nextFeed;
                workersStarted = true;
                for (int i = 0; i < 2; i++)
                {
                    if (tailStartedFor[i] == ws && tailStartedEpoch[i] == exchangeEpoch) continue;
                    TrayOnly[i] = true;
                    tailStartedFor[i] = ws;
                    tailStartedEpoch[i] = exchangeEpoch;
                    QueueWorker(i, ws, true, nextFeed);
                }
                return EM_RES.OK;
            }
        }

        /// <summary>
        /// 按转盘顺序找下一个需要上料的工站：先看 POS3、POS2、POS1 上的工站（下一个转到上料位的是 POS3），
        /// 都不需要时看当前工站自己（只有一个工站有料时，转一圈回来还是它）。
        /// 只按穴位启用状态预判；GRR/单吸嘴/点检/清料/霍尔等特殊流程一律返回 null（不提前预取）。
        /// 预判错了也没关系：提前取的料到位时只交给实际计划的第一组，对不上的退回料盘原穴。
        /// </summary>
        public static WS FindNextFeedStation(WS current)
        {
            try
            {
                if (VAR.ClearMt || PT_SET.bGrrFlow || VAR.isAutoChkMode || VAR.gsys_set.isChkMode || PT_SET.HallEn ||
                    PT_SET.bUpDn1XtOnlyOne || PT_SET.bUpDn2XtOnlyOne)
                    return null;
                Turntable.EM_STA cur = Turntable.GetCurSta;   // 只读一次转盘位置（每次读约 0.2s）
                foreach (Turntable.EM_STA pos in new[] { Turntable.EM_STA.POS3, Turntable.EM_STA.POS2, Turntable.EM_STA.POS1 })
                {
                    WS ws = Turntable.GetWSOnPos(pos, cur);
                    if (ws != null && !ReferenceEquals(ws, current) && LikelyNeedsFeed(ws)) return EarlyAllowedFor(ws);
                }
                return current != null && LikelyNeedsFeed(current) ? EarlyAllowedFor(current) : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// 模组自动点检只影响它的承载工站：下一个上料工站正是点检承载工站时不提前预取
        /// （这一批可能要装样品，必须到位后再定），其它工站照常提前预取。
        /// </summary>
        private static WS EarlyAllowedFor(WS ws)
        {
            return UI.ModuleCheck.ModuleCheckHooks.AffectsStation(ws) ? null : ws;
        }

        private static bool LikelyNeedsFeed(WS ws)
        {
            if (ws == null || ws.list_md == null || ws.TestStatus == WS.EM_TEST_STA.ERROR) return false;
            foreach (WS.MdDat md in ws.list_md)
                if (md != null && md.benable) return true;
            return false;
        }

        private static EM_RES StartWorkers(bool unload, Task<EM_RES>[] stoppedTasks = null)
        {
            workersStarted = false;
            if (faulted || !MotionIdle || !CanMoveFixture()) return EM_RES.SAFE_PROTECT;
            TrayOnly[0] = true;
            TrayOnly[1] = true;
            // Check both live positions before issuing any command on either arm.
            if (!IsArmOutsideFixture(0) || !IsArmOutsideFixture(1))
            {
                TrayOnly[0] = TrayOnly[1] = false;
                return EM_RES.SAFE_PROTECT;
            }
            workersStarted = true;
            WS captured = station;
            WS capturedNext = unload ? earlyTarget : null;
            pending = new Task<EM_RES>[2];
            for (int i = 0; i < 2; ++i)
            {
                // 正常停止后只续接未完成的一臂，另一臂已提交的落盘不能重做。
                if (stoppedTasks != null && stoppedTasks[i].Result == EM_RES.OK)
                {
                    pending[i] = stoppedTasks[i];
                    continue;
                }
                int armId = i;
                QueueWorker(armId, captured, unload, capturedNext);
            }
            return EM_RES.OK;
        }

        private static void QueueWorker(int armId, WS ws, bool unload, WS nextFeed)
        {
            workerStation[armId] = ws; workerUnload[armId] = unload; workerNext[armId] = nextFeed;
            retryable[armId] = false;
            pending[armId] = Task.Run(() => RunWorker(armId, ws, unload, nextFeed));
        }

        private static EM_RES RunWorker(int armId, WS ws, bool unload, WS nextFeed)
        {
            EM_RES result;
            UpDownLoad arm = armId == 0 ? COM.UDLoad1 : COM.UDLoad2;
            if (arm != null) arm.ThreeNozzleTakeSafeStop();   // 清掉上次遗留的标记
            try
            {
                bool tailDone;
                lock (Sync) tailDone = unload && tailCompletedFor[armId] == ws;
                result = unload ? (tailDone ? EM_RES.OK : arm.ThreeNozzleUnload(ref UpDownLoad.bquit, ws))
                                : arm.ThreeNozzlePrefetch(ref UpDownLoad.bquit, ws);
                if (unload && result == EM_RES.OK) lock (Sync) tailCompletedFor[armId] = ws;
                // 卸完后仍保持只在料盘区（TrayOnly），接着为下一个需要上料的工站提前取料
                if (unload && result == EM_RES.OK && nextFeed != null)
                    result = arm.ThreeNozzleEarlyPrefetch(ref UpDownLoad.bquit, nextFeed);
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "三吸嘴任务异常：" + ex);
                result = EM_RES.ERR;
            }
            if (result != EM_RES.OK)
            {
                bool safeStop = arm != null && arm.ThreeNozzleTakeSafeStop();
                if (result == EM_RES.QUIT && (VAR.gsys_set.bquit || UpDownLoad.bquit) &&
                    arm != null && (arm.ThreeNozzleHasPendingVacuumIssue || arm.ThreeNozzleCanResumeAfterStop()))
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                        "三吸嘴: {0} 预取/卸料停止，保留任务；下次开始{1}（不锁故障）", arm.disc,
                        arm.ThreeNozzleHasPendingVacuumIssue ? "先弹窗处理当前吸取/真空异常" : "核对轴及物料记录后续接"));
                }
                else if (safeStop && result != EM_RES.QUIT)
                {
                    lock (Sync) retryable[armId] = true;
                    MT.BeQuitEn(true);
                    // 与原双嘴一致（料盘连续取料失败 → 归位确认）：本臂已回原点、吸嘴无料，状态明确。
                    // 不锁故障、不急停另一臂（让它把当前动作做完），由 WaitPending 报一次失败后停止运行。
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                        "三吸嘴: {0} 预取/卸料返回 {1}（本臂已归位），停止运行；下次开始续接本臂，不锁故障",
                        arm.disc, result));
                }
                else
                {
                    faulted = true;
                    MT.BeQuitEn(true, true);
                    UpDownLoad.StopAllUpDownLoadMotion();
                }
            }
            return result;
        }

        private static EM_RES ResumeStoppedWorkers()
        {
            if (VAR.gsys_set.bquit || UpDownLoad.bquit) return EM_RES.QUIT;
            for (int armId = 0; armId < 2; armId++)
            {
                EM_RES result = RestartArmIfStopped(armId);
                if (result != EM_RES.OK) return result;
            }
            return EM_RES.OK;
        }

        private static EM_RES RestartArmIfStopped(int armId)
        {
            lock (Gates[armId]) lock (Sync)
            {
                if (faulted) return EM_RES.ERR;
                Task<EM_RES> task = pending == null ? null : pending[armId];
                if (task == null || !task.IsCompleted || task.Status != TaskStatus.RanToCompletion || task.Result == EM_RES.OK) return EM_RES.OK;
                if (VAR.gsys_set.bquit || UpDownLoad.bquit) return EM_RES.QUIT;
                if (task.Result != EM_RES.QUIT && !retryable[armId]) return task.Result;
                UpDownLoad arm = armId == 0 ? COM.UDLoad1 : COM.UDLoad2;
                if (arm != null) arm.ThreeNozzleSweepVacuumMismatch("续接前检查吸嘴");
                if (arm == null || (!arm.ThreeNozzleHasPendingVacuumIssue && !arm.ThreeNozzleCanResumeAfterStop())) return EM_RES.SAFE_PROTECT;
                QueueWorker(armId, workerStation[armId], workerUnload[armId], workerNext[armId]);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("三吸嘴: 模块{0} 续接本臂预取/卸料任务，另一臂不重复执行", armId + 1));
                return EM_RES.OK;
            }
        }

        private static EM_RES WaitArmPending(int armId)
        {
            while (true)
            {
                Task<EM_RES> task;
                lock (Sync) task = pending == null ? null : pending[armId];
                if (task == null) return faulted ? EM_RES.ERR : EM_RES.OK;
                while (!task.IsCompleted)
                {
                    if (VAR.gsys_set.bquit || UpDownLoad.bquit) return EM_RES.QUIT;
                    Thread.Sleep(10);
                }
                if (task.Status != TaskStatus.RanToCompletion) return EM_RES.ERR;
                if (task.Result == EM_RES.QUIT && !faulted && !VAR.gsys_set.bquit && !UpDownLoad.bquit)
                {
                    EM_RES resumed = RestartArmIfStopped(armId);
                    if (resumed != EM_RES.OK) return resumed;
                    continue;
                }
                if (task.Result != EM_RES.OK && task.Result != EM_RES.QUIT && !faulted)
                    MT.BeQuitEn(true);
                return faulted && task.Result == EM_RES.OK ? EM_RES.ERR : task.Result;
            }
        }

        public static EM_RES WaitPending()
        {
            EM_RES result = WaitArmPending(0);
            return result == EM_RES.OK ? WaitArmPending(1) : result;
        }

        /// <summary>[R22] 该臂的预取/卸料/提前预取任务是否还在执行。</summary>
        public static bool IsWorkerRunning(int armId)
        {
            lock (Sync) return pending != null && armId >= 0 && armId < pending.Length && pending[armId] != null && !pending[armId].IsCompleted;
        }

        /// <summary>[R22] 该工站的换料是否已经开始（夹具已到位、协调线程已切到它）。</summary>
        public static bool ExchangeStartedFor(WS ws)
        {
            lock (Sync) return ws != null && station == ws && !unloading;
        }

        public static bool WorkersStopped
        {
            get { lock (Sync) return pending == null || (pending[0].IsCompleted && pending[1].IsCompleted); }
        }

        // 清料提示和最终收盘共用这个只读条件。工站为空不代表吸嘴成品已经落盘。
        public static bool CanCompleteClear
        {
            get
            {
                lock (Sync)
                {
                    if (faulted || !WorkersStopped || !MotionIdle) return false;
                    if (pending != null && (pending[0].Status != TaskStatus.RanToCompletion ||
                        pending[1].Status != TaskStatus.RanToCompletion ||
                        pending[0].Result != EM_RES.OK || pending[1].Result != EM_RES.OK)) return false;
                }
                return COM.UDLoad1 != null && COM.UDLoad2 != null &&
                    COM.UDLoad1.ThreeNozzleDrainReady && COM.UDLoad2.ThreeNozzleDrainReady;
            }
        }

        public static EM_RES WaitClearCompletion()
        {
            EM_RES result = WaitPending();
            if (result != EM_RES.OK) return result;
            if (VAR.gsys_set.bquit || UpDownLoad.bquit) return EM_RES.QUIT;
            return CanCompleteClear ? EM_RES.OK : EM_RES.SAFE_PROTECT;
        }

        // Called only by an explicit reset, after all workers have stopped.
        public static EM_RES ResetAfterHome()
        {
            lock (Gates[0]) lock (Gates[1]) lock (Sync)
            {
                if (!WorkersStopped || !MotionIdle) return EM_RES.ERR;
                pending = null;
                workersStarted = false;
                station = null;
                earlyTarget = null;
                exchangeReleased[0] = exchangeReleased[1] = false;
                faulted = false;
                unloading = false;
                TrayOnly[0] = TrayOnly[1] = false;
                for (int i = 0; i < 2; i++)
                {
                    workerStation[i] = workerNext[i] = tailStartedFor[i] = tailCompletedFor[i] = null;
                    workerUnload[i] = retryable[i] = false;
                    tailStartedEpoch[i] = 0;
                }
                return EM_RES.OK;
            }
        }
    }
}
