using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MotionCtrl;
using UI.Class;
using UI.ModuleCheck;

namespace UI
{
    public partial class UpDownLoad
    {
        private sealed class ThreeNozzleRuntimePair
        {
            internal readonly WS.MdDat[] Pockets = new WS.MdDat[2];
            internal readonly bool[] Incoming = new bool[2];
            internal readonly Product.MdDat[] Prepared = new Product.MdDat[2];
            internal readonly Cam.VisionOutPutData[] DownVision = new Cam.VisionOutPutData[2];
            internal readonly bool[] DownReady = new bool[2];
            // 三吸嘴飞拍：已取料、已电机扫码，下相机推迟到进夹具时飞拍（ThreeNozzleResolveDeferredDown）
            internal readonly bool[] DownDeferred = new bool[2];
            // 三吸嘴飞拍：进夹具飞拍到的取料前检查结果（WsMod_Shp），取旧料时用一次
            internal readonly Cam.VisionOutPutData[] FlyBeforePick = new Cam.VisionOutPutData[2];
            // 调试页“放料前上相机进夹具飞拍”：进夹具时拍到的放料前上相机结果，放新料时用一次
            internal readonly Cam.VisionOutPutData[] FlyBeforePlace = new Cam.VisionOutPutData[2];
            // 上面那张图实际拍照时的 X（同一趟飞拍只走一列，可能与本穴示教 X 差零点几毫米）
            internal readonly double[] FlyBeforePlaceX = new double[2];
            // 上面那张图是空穴的 WsUp_Shp（与原双嘴 FlyToWs 一样）：放料时直接用，不受调试页模式影响
            internal readonly bool[] FlyBeforePlaceEmpty = new bool[2];
            internal readonly int[] IncomingNozzles = { 1, 2 };
            internal readonly int[] FinishedNozzles = { 0, 1 };
            internal readonly bool[] Placed = new bool[2];
            internal readonly int[] ReworkStage = new int[2];
            internal readonly bool[] CountAsNew = new bool[2];
            internal readonly bool[] Accepted = new bool[2];
            internal int PlacementRevision;
            internal readonly Product.Tray[] SourceTrays = new Product.Tray[2];
            internal readonly Product.Tray.PosInf[] SourcePositions = new Product.Tray.PosInf[2];
            internal readonly ST_XYZA[] PickedPositions = new ST_XYZA[2];
            internal readonly double[] PickedTrayXs = new double[2];
            internal readonly EM_RES[] ReturnCauses = new EM_RES[2];
            internal ThreeNozzleSequence Sequence;
        }

        private WS threeNozzleSource;
        private bool threeNozzleDrain;
        private readonly List<ThreeNozzleRuntimePair> threeNozzlePairs = new List<ThreeNozzleRuntimePair>();
        private readonly Dictionary<WS.MdDat, Product.MdDat> threeNozzleFixtureMaterials = new Dictionary<WS.MdDat, Product.MdDat>();
        private int threeNozzlePairIndex;
        private bool threeNozzleRecovery;
        private int threeNozzleExecutor;   // 0=空闲 1=任务执行中 2=被对臂借去主动让位
        private const int ThreeNozzleExecutorYield = 2;

        /// <summary>
        /// 取任务执行权。对臂正借用本臂做主动让位（值=2，通常不到1秒）时等它结束；最多等 15 秒。
        /// 其它情况（本臂已有任务在执行）照原来立即返回 false。
        /// </summary>
        private bool ThreeNozzleAcquireExecutor(ref bool quit)
        {
            int start = Environment.TickCount;
            while (true)
            {
                int prev = Interlocked.CompareExchange(ref threeNozzleExecutor, 1, 0);
                if (prev == 0) return true;
                if (prev != ThreeNozzleExecutorYield || quit || VAR.gsys_set.bquit) return false;
                if (unchecked(Environment.TickCount - start) > 15000)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + " 三吸嘴: 等待主动让位结束超时（15秒）");
                    return false;
                }
                Thread.Sleep(20);
            }
        }

        /// <summary>对臂请本臂主动让位：只有本臂空闲、没有待核对的异常时才借给它。</summary>
        internal bool ThreeNozzleTryBorrowForYield()
        {
            if (threeNozzleRecovery || threeNozzlePendingVacuum != null) return false;
            return Interlocked.CompareExchange(ref threeNozzleExecutor, ThreeNozzleExecutorYield, 0) == 0;
        }

        internal void ThreeNozzleReleaseYieldBorrow()
        {
            Interlocked.CompareExchange(ref threeNozzleExecutor, 0, ThreeNozzleExecutorYield);
        }
        // 提前预取（尾部卸料后、转盘转动期间）取好的料：吸嘴2=A穴用，吸嘴3=B穴用。
        // 工站到位建计划后由 ThreeNozzleAdoptEarly 交给第一组，对不上的退回料盘原穴。
        private ThreeNozzleRuntimePair threeNozzleEarly;

        public bool ThreeNozzleRecoveryRequired { get { return threeNozzleRecovery; } }

        // 仅对物理动作已经停止且物料记录明确的普通 QUIT 放行，不清任何物料/恢复锁。
        internal bool ThreeNozzleCanResumeAfterStop()
        {
            try
            {
                if (threeNozzleRecovery || list_xt.Count != 3 ||
                    !ax_x.card.isReady || !ax_y.card.isReady ||
                    ax_x.home_status != AXIS.HOME_STA.OK || ax_y.home_status != AXIS.HOME_STA.OK ||
                    ax_x.isALM || ax_y.isALM || !ax_x.isStop || !ax_y.isStop || !MT.AreAllArmZSafe(id)) return false;
                foreach (double position in new[] { ax_x.fenc_pos, ax_x.fcmd_pos, ax_y.fenc_pos, ax_y.fcmd_pos })
                    if (double.IsNaN(position) || double.IsInfinity(position)) return false;
                foreach (XT xt in list_xt)
                {
                    if (xt.ax_z.isALM || xt.ax_u.isALM || !xt.ax_u.isStop ||
                        !xt.ax_u.card.isReady || xt.ax_u.home_status != AXIS.HOME_STA.OK ||
                        double.IsNaN(xt.ax_u.fenc_pos) || double.IsInfinity(xt.ax_u.fenc_pos) ||
                        double.IsNaN(xt.ax_u.fcmd_pos) || double.IsInfinity(xt.ax_u.fcmd_pos) ||
                        double.IsNaN(xt.ax_z.fcmd_pos) || Math.Abs(xt.ax_z.fcmd_pos) > MT.ArmZSafeTolerance ||
                        (!Demo && (xt.XtMd != null) != xt.cy_zk.isONByChkSen)) return false;
                }
                return true;
            }
            catch { return false; }
        }

        internal bool ThreeNozzleDrainReady
        {
            get
            {
                if (!ThreeNozzleCanResumeAfterStop() || threeNozzleSource != null || threeNozzleEarly != null ||
                    threeNozzlePairs.Count != 0 || list_xt.Any(xt => xt.XtMd != null)) return false;
                double safeY = list_xt[1].st_cap_pos.y + 3;
                return !double.IsNaN(safeY) && !double.IsInfinity(safeY) &&
                    !double.IsNaN(ax_x.fenc_pos) && !double.IsNaN(ax_x.fcmd_pos) &&
                    !double.IsNaN(ax_y.fenc_pos) && !double.IsNaN(ax_y.fcmd_pos) &&
                    !double.IsInfinity(ax_y.fenc_pos) && !double.IsInfinity(ax_y.fcmd_pos) &&
                    Math.Abs(ax_x.fenc_pos) <= 3 && Math.Abs(ax_x.fcmd_pos) <= 3 &&
                    ax_y.fenc_pos <= safeY && ax_y.fcmd_pos <= safeY;
            }
        }

        // 调用方持有本臂执行权。只释放不再持料的普通清料计划，不改任何穴位/料盘记录。
        private bool ThreeNozzleReleaseEmptyDrainPlan()
        {
            if (!threeNozzleDrain || ThreeNozzleHasSpecialMode || threeNozzleEarly != null ||
                !ThreeNozzleCanResumeAfterStop() ||
                list_xt.Any(xt => xt.XtMd != null || xt.cy_zk.isONByChkSen) ||
                threeNozzlePairs.Any(pair => pair.Prepared.Any(material => material != null))) return false;
            double safeY = list_xt[1].st_cap_pos.y + 3;
            if (double.IsNaN(safeY) || double.IsInfinity(safeY) ||
                Math.Abs(ax_x.fenc_pos) > 3 || Math.Abs(ax_x.fcmd_pos) > 3 ||
                ax_y.fenc_pos > safeY || ax_y.fcmd_pos > safeY) return false;
            threeNozzleSource = null;
            threeNozzlePairs.Clear();
            threeNozzlePairIndex = 0;
            threeNozzleDrain = false;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 上轮清料空计划已释放，三嘴无料且轴安全，保留夹具/料盘物料记录并重新规划");
            return true;
        }

        private bool ThreeNozzleCancelUnpickedForClear(ThreeNozzleRuntimePair pair, int slot)
        {
            if (!VAR.ClearMt || pair.Prepared[slot] != null) return false;
            pair.Incoming[slot] = false;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 已切清料，取消尚未取起的新料，不再开始供料盘取料");
            return true;
        }

        // These gates are also consumed by startup, before either arm moves.
        public static string ThreeNozzleUnsupportedReason()
        {
            if (CONST.ModDwFw.Length < 3)
                return "三吸嘴需要三个独立下视觉任务名称";
            return null;
        }

        private EM_RES ThreeNozzleError(string reason, bool recovery = false)
        {
            if (recovery)
            {
                threeNozzleRecovery = true;
                if (threeNozzlePairIndex < threeNozzlePairs.Count && threeNozzlePairs[threeNozzlePairIndex].Sequence != null)
                    threeNozzlePairs[threeNozzlePairIndex].Sequence.RequireRecovery();
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + " 三吸嘴: " + reason);
            return EM_RES.ERR;
        }

        // 预取/提前预取/换料入口：先处理上次停止留下的真空问题，再处理“无计划持料”。
        private EM_RES ThreeNozzleEntryPrepare(ref bool quit)
        {
            EM_RES result = ThreeNozzleCheckPendingVacuum(ref quit);
            if (result != EM_RES.OK) return result;
            return ThreeNozzleDisposeOrphans(ref quit);
        }

        /// <summary>
        /// 自动对账清掉运行计划后，吸嘴上仍有记录且真空一致的物料（以前要人工取下才能解锁）。
        /// 与双吸嘴一样不要求人工处理：先按记录落盘再继续。成品按结果落 OK/NG 盘；
        /// 未测料（结果小于0）已没有可回的计划，按 NG 落 NG 盘并记日志，请人工复测；点检样品按原流程放回点检盘。
        /// 只在本臂没有任何换料计划时动作，正常运行中不会进入。
        /// </summary>
        private EM_RES ThreeNozzleDisposeOrphans(ref bool quit)
        {
            if (threeNozzleSource != null || threeNozzleEarly != null || threeNozzlePairs.Count != 0) return EM_RES.OK;
            if (!list_xt.Any(xt => xt.XtMd != null || (!Demo && xt.cy_zk.isONByChkSen))) return EM_RES.OK;
            ThreeNozzleSweepVacuumMismatch("无换料计划时检查吸嘴");
            List<XT> held = list_xt.Where(xt => xt.XtMd != null).ToList();
            if (held.Count == 0) return EM_RES.OK;
            foreach (XT xt in held)
            {
                if (IsModuleCheckSample(xt.XtMd) || xt.XtMd.res >= 0) continue;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                    "{0} 三吸嘴: {1} 持有未测料(条码={2})但已没有换料计划，按NG落NG盘，请人工复测",
                    disc, xt.disc, xt.XtMd.bardcode ?? ""));
                xt.XtMd.res = (int)Product.EM_CM_RES.NG;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                "{0} 三吸嘴: 无换料计划时吸嘴仍持料（{1}），先落盘再继续（与双吸嘴一致，不需人工对账）",
                disc, string.Join("、", held.Select(xt => xt.disc))));
            CtStepLog.SetContext(disc, "无计划持料落盘");
            return ThreeNozzleUnloadCore(ref quit);
        }

        /// <summary>
        /// Maintenance entry after explicit operator reconciliation of fixture and
        /// tray records and removal/return of every held part. This never edits
        /// nozzle, fixture, tray or counter data and must not be called by Home.
        /// </summary>
        public EM_RES ThreeNozzleResetAfterManualReconciliation(bool operatorConfirmed)
        {
            if (!operatorConfirmed) return ThreeNozzleError("必须明确确认治具/料盘人工对账完成");
            bool noQuit = false;
            if (!ThreeNozzleAcquireExecutor(ref noQuit)) return EM_RES.ABORT;
            try
            {
                if (list_xt.Count != 3 || list_xt.Any(xt => xt.XtMd != null || xt.cy_zk.isONByChkSen))
                    return ThreeNozzleError("人工恢复要求三嘴物料数据为空且真空无料；禁止自动清数据");
                ThreeNozzleClearReconciledRuntime();
                return EM_RES.OK;
            }
            finally { Interlocked.Exchange(ref threeNozzleExecutor, 0); }
        }

        private void ThreeNozzleClearReconciledRuntime()
        {
            threeNozzleSource = null;
            threeNozzleEarly = null;
            threeNozzlePairs.Clear();
            threeNozzlePairIndex = 0;
            threeNozzleRecovery = false;
            threeNozzlePendingVacuum = null;
            threeNozzleDrain = false;
            // Reconciliation invalidates provenance only; no physical records or
            // counters are changed. This nonthrowing commit is shared by the dialog.
            threeNozzleFixtureMaterials.Clear();
            threeNozzleDownMotorCodes.Clear();
            threeNozzleMotorScanReady.Clear();
            threeNozzleFinishedBarcodeChecks.Clear();
            threeNozzlePendingBarcodeWarnings.Clear();
            ThreeNozzleResetModesRuntime();
        }

        private EM_RES ThreeNozzleEnsurePlan(WS ws)
        {
            if (status_ud == EM_STA.UNKNOW || status_ud == EM_STA.HOME)
                return ThreeNozzleError("未完成复位，禁止预取和交换");
            string unsupported = ThreeNozzleUnsupportedReason();
            if (unsupported != null) return ThreeNozzleError(unsupported);
            if (ws == null || list_xt.Count != 3) return ThreeNozzleError("工站或吸嘴数量无效");
            if (threeNozzleRecovery) return ThreeNozzleError("上次动作结果待核对，本轮不自动重试；停止后再按开始会自动复位（抬Z→回安全位→按真空自动对账），无需人工对账");
            if (threeNozzleSource != null && threeNozzleDrain && !VAR.ClearMt)
            {
                if (!ThreeNozzleReleaseEmptyDrainPlan())
                    return ThreeNozzleError("上轮清料计划尚未结束，禁止切回上料；请继续清料或核对吸嘴及料盘物料记录", true);
            }
            if (threeNozzleSource != null)
            {
                if (!Object.ReferenceEquals(threeNozzleSource, ws)) return ThreeNozzleError("前轮未卸完，不能更换源工站", true);
                return EM_RES.OK;
            }
            foreach (XT xt in list_xt)
            {
                if (ThreeNozzleIsEarlyHeld(xt)) continue;   // 提前预取的料，建计划后再分配
                if (xt.XtMd != null || (!Demo && xt.cy_zk.isONByChkSen))
                    return ThreeNozzleError("无计划时吸嘴持料（入口落盘未完成），停止后按开始会自动复位对账", true);
            }
            EM_RES scanningConfiguration = ThreeNozzleValidateScanningConfiguration();
            if (scanningConfiguration != EM_RES.OK) return scanningConfiguration;
            for (int slot = 0; slot < 3; slot++)
                if (!list_xt[slot].dwcam.List_vs_task.Any(t => t.TaskName == CONST.ModDwFw[slot]))
                    return ThreeNozzleError("缺少吸嘴下视觉任务: " + CONST.ModDwFw[slot]);
            threeNozzleSource = ws;
            threeNozzleDrain = VAR.ClearMt;
            threeNozzlePairIndex = 0;
            threeNozzlePairs.Clear();
            ThreeNozzleInitializeMode(ws);
            if (ThreeNozzleHasSpecialMode) return EM_RES.OK;
            var groups = new Dictionary<int, ThreeNozzleRuntimePair>();
            for (int i = 0; i < ws.list_md.Count; i++)
            {
                int n = i % 2 == 0 ? i - i / 2 : i + (ws.list_md.Count - 1 - i) / 2;
                if (FindMod == EM_FIN_MOD.NONE || (FindMod == EM_FIN_MOD.LOW && n % 8 >= 4) ||
                    (FindMod == EM_FIN_MOD.HIGH && n % 8 < 4)) continue;
                WS.MdDat md = ws.list_md[n];
                bool remove = md.res >= (int)Product.EM_CM_RES.OK;
                bool load = !threeNozzleDrain && ThreeNozzleCanLoadPocket(md) && md.benable && (remove || md.res == (int)Product.EM_CM_RES.EMPTY) &&
                    ModuleCheckHooks.AllowLoad(ws, md.Num);   // 模组点检装样品轮：只给配置了样品的穴上料
                if (!remove && !load) continue;
                ThreeNozzleRuntimePair pair;
                if (!groups.TryGetValue(md.Num % 8, out pair))
                {
                    pair = new ThreeNozzleRuntimePair();
                    groups.Add(md.Num % 8, pair);
                    threeNozzlePairs.Add(pair);
                }
                int slot = md.Num > 8 ? 1 : 0;
                if (pair.Pockets[slot] != null) return ThreeNozzleError("穴位分组冲突", true);
                pair.Pockets[slot] = md;
                pair.Incoming[slot] = load;
            }
            foreach (ThreeNozzleRuntimePair pair in threeNozzlePairs)
                pair.Sequence = new ThreeNozzleSequence(pair.Pockets[0] != null && pair.Pockets[0].res >= 0,
                    pair.Incoming[0], pair.Pockets[1] != null && pair.Pockets[1].res >= 0, pair.Incoming[1]);
            return EM_RES.OK;
        }

        public EM_RES ThreeNozzlePrefetch(ref bool quit, WS ws)
        {
            if (!ThreeNozzleAcquireExecutor(ref quit)) return EM_RES.ABORT;
            try
            {
                EM_RES pendingIssue = ThreeNozzleEntryPrepare(ref quit);
                if (pendingIssue != EM_RES.OK) return pendingIssue;
                EM_RES result = ThreeNozzleEnsurePlan(ws);
                if (result == EM_RES.OK) result = ThreeNozzleAdoptEarly(ref quit);
                if (result != EM_RES.OK || threeNozzlePairs.Count == 0) return result;
                ThreeNozzleRuntimePair pair = threeNozzlePairs[threeNozzlePairIndex];
                if (pair.Sequence.Current != ThreeNozzleAction.PrepareIncoming) return EM_RES.OK;
                CtStepLog.SetContext(disc, "预取待测");
                result = ThreeNozzlePreparePair(ref quit, pair, true);
                if (result == EM_RES.OK) pair.Sequence.Step(a => true);
                return result;
            }
            catch { threeNozzleRecovery = true; throw; }
            finally { Interlocked.Exchange(ref threeNozzleExecutor, 0); }
        }

        // Holds the shared tray workflow lock, never a state lock. Existing motion
        // reservation/yield code remains responsible for two-arm collision handling.
        private EM_RES ThreeNozzleTakeLock(ref bool quit, object sync)
        {
            while (!quit)
            {
                if (Monitor.TryEnter(sync, 100)) return EM_RES.OK;
                EM_RES result = ProcessPositioningYield(ref quit);
                if (result != EM_RES.OK) return result;
            }
            return EM_RES.QUIT;
        }

        /// <param name="allowDeferDown">true：三吸嘴飞拍生效时，下相机推迟到进夹具时飞拍（只在正常预取/提前预取/换料准备时传 true）。</param>
        private EM_RES ThreeNozzlePreparePair(ref bool quit, ThreeNozzleRuntimePair pair, bool allowDeferDown = false)
        {
            if (threeNozzleDrain) return ThreeNozzleError("清料禁止预取", true);
            // picktray 只保护供料盘/料盘X轴：选料位、退料、取料、提交料盘数据。
            // 电机扫码、下相机拍照不碰供料盘，放到锁外，另一臂可以同时取料（两臂避障仍由运动预约负责）。
            EM_RES result;
            bool held = false;
            threeNozzleTrayFlyResults.Clear();   // 料盘飞拍结果只在本次取料内有效
            try
            {
                for (int slot = 0; slot < 2; slot++)
                {
                    if (!pair.Incoming[slot] || pair.Placed[slot]) continue;
                    if (VAR.ClearMt && pair.Prepared[slot] == null)
                    {
                        pair.Incoming[slot] = false;
                        continue;
                    }
                    if (quit) return EM_RES.QUIT;
                    XT xt = list_xt[pair.IncomingNozzles[slot]];
                    if (!held && (pair.Prepared[slot] == null || pair.ReturnCauses[slot] != EM_RES.OK))
                    {
                        result = ThreeNozzleTakeLock(ref quit, picktray);
                        if (result != EM_RES.OK) return result;
                        held = true;
                        if (ThreeNozzleCancelUnpickedForClear(pair, slot)) continue;
                    }
                    if (pair.Prepared[slot] != null && pair.ReturnCauses[slot] != EM_RES.OK)
                    {
                        Product.MdDat returnedMaterial = pair.Prepared[slot];
                        result = ThreeNozzleReturnPrepared(ref quit, xt, pair.SourceTrays[slot],
                            pair.SourcePositions[slot], pair.PickedPositions[slot], pair.PickedTrayXs[slot]);
                        if (result != EM_RES.OK) return result;
                        if (IsModuleCheckSample(returnedMaterial))
                        {
                            // 点检样品原穴固定，不能像普通料一样换下一穴继续取：放回原穴后重取同一颗，
                            // 连续失败 ModuleCheckMaxVisionTries 次才停机。停机时本对的来料已清空，
                            // 重新运行时由 PlaceOne 重新取样品（不会进入“待人工对账”）。
                            string sampleId = returnedMaterial.ModuleCheckSampleId;
                            ModuleCheckHooks.OnSampleReturned(sampleId, returnedMaterial.idx);
                            pair.Prepared[slot] = null;
                            pair.DownReady[slot] = false;
                            pair.DownDeferred[slot] = false;
                            pair.ReturnCauses[slot] = EM_RES.OK;
                            int fails = ModuleCheckNoteVisionFail(sampleId);
                            if (fails < ModuleCheckMaxVisionTries)
                            {
                                ModuleCheckHooks.NoteRisk(ModuleCheckRisk.SampleVisionFail, string.Format(
                                    "{0} 点检样品{1}下视觉/扫码失败（第{2}次），已放回点检盘原穴，重新取同一颗", disc, sampleId, fails));
                                slot--;
                                continue;
                            }
                            ModuleCheckClearVisionFail(sampleId);
                            return ThreeNozzleError(string.Format("点检样品{0}下视觉/扫码连续失败{1}次，已放回点检盘原穴，请检查样品（摆放方向/位置/外观）后再运行",
                                sampleId, fails));
                        }
                        EM_RES cause = pair.ReturnCauses[slot];
                        pair.Prepared[slot] = null;
                        pair.DownReady[slot] = false;
                        pair.DownDeferred[slot] = false;
                        pair.ReturnCauses[slot] = EM_RES.OK;
                        if (cause == EM_RES.CAM_ERR_PLACE_BACK && ++ErrTryPickCnt > 1)
                        {
                            ErrTryPickCnt = 0;
                            return ThreeNozzleError("连续下视觉失败，物料已退回，请确认原因");
                        }
                        if (cause == EM_RES.MOTOR_CAM_ERR_PLACE_BACK && ++ErrMotorTryPickCnt > 4)
                            ErrMotorTryPickCnt = 0;
                        if (VAR.ClearMt) { pair.Incoming[slot] = false; continue; }
                    }
                    if (pair.Prepared[slot] == null)
                    {
                        if (xt.XtMd != null) return ThreeNozzleError("预取目标嘴已有物料记录", true);
                        if (!Demo && xt.cy_zk.isONByChkSen)
                        {
                            EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, false, "取料前真空有料但记录为空");
                            if (corrected != EM_RES.OK) return corrected;
                        }
                        ModuleCheckSample mcSample;
                        Product.Tray.PosInf mcSource;
                        string mcError;
                        if (ModuleCheckSourceFor(pair.Pockets[slot], out mcSample, out mcSource, out mcError))
                        {
                            // 模组点检装样品轮：从点检盘原穴取样品，不走待测盘换盘/条码检查
                            if (mcSource == null && mcSample != null && (!traybox_fd.IsReady || traybox_fd.tray_cur == null))
                            {
                                // 停机/重启后待测盘还没出料：先把当前待测盘出到工作位（只出盘，不换盘），再取样品
                                result = ModuleCheckEnsureFeedTrayPresent(ref quit);
                                if (result != EM_RES.OK) return result;
                                ModuleCheckSourceFor(pair.Pockets[slot], out mcSample, out mcSource, out mcError);
                            }
                            if (mcSource == null) return ThreeNozzleError(mcError, true);
                            result = ModuleCheckPickSample(ref quit, xt, pair, slot, mcSample, mcSource);
                            if (result != EM_RES.OK) return result;
                        }
                        else
                        {
                        result = ThreeNozzleEnsureTray(ref quit, traybox_fd, -1);
                        if (result != EM_RES.OK) return result;
                        result = ThreeNozzleCheckFeedTrayBarcode(ref quit);
                        if (result == EM_RES.END)
                        {
                            result = GoZero(ref quit);
                            if (result != EM_RES.OK) return result;
                            traybox_fd.IsReady = false;
                            slot--;
                            continue;
                        }
                        if (result != EM_RES.OK) return result;
                        if (ThreeNozzleCancelUnpickedForClear(pair, slot)) continue;
                        var positions = traybox_fd.tray_cur.GetPosList();
                        var source = positions[0];
                        if (PT_SET.bAddCapQrcode && (traybox_fd.tray_cur.list_AddCapQrcodepos == null ||
                            source.idx < 0 || source.idx >= traybox_fd.tray_cur.list_AddCapQrcodepos.Count))
                            return ThreeNozzleError("缺少源料穴附加扫码位置");
                        string barcode;
                        string ctPrev = CtStepLog.CurrentContext;
                        CtStepLog.SetContext(disc, string.Format("取待测 料盘穴{0} {1}", source.idx, xt.disc));
                        ThreeNozzleDryRun.NoteLiveAction(id, xt.SlotIndex, "feed", source.idx,
                            string.Format("{0} 从供料盘穴{1}取新料", xt.disc, source.idx));
                        // 供料盘飞拍：本组还要取的同列两穴先飞拍一趟（每组只做一次，结果按穴号保存）
                        if (!threeNozzleTrayFlyResults.ContainsKey(source.idx))
                        {
                            result = ThreeNozzleTrayFlyPrepare(ref quit, pair, slot, positions);
                            if (result != EM_RES.OK) { CtStepLog.SetContext(disc, ctPrev); return result; }
                        }
                        if (ThreeNozzleCancelUnpickedForClear(pair, slot))
                        {
                            CtStepLog.SetContext(disc, ctPrev);
                            continue;
                        }
                        Camcfgset(0U);
                        string blindWhyNot;
                        EM_RES? flyPick = ThreeNozzleTrayFlyPick(ref quit, xt, source, out barcode);
                        if (flyPick.HasValue)
                        {
                            result = flyPick.Value;
                        }
                        else if (ThreeNozzleBlindPickAllowed(out blindWhyNot))
                        {
                            barcode = "";
                            result = ThreeNozzleBlindTrayPick(ref quit, xt, source, pair.Pockets[slot].st_pos[id].x);
                        }
                        else
                        {
                            ThreeNozzleNoteBlindPickFallback(blindWhyNot);
                            result = xt.XtCapMovMod(ref quit, out barcode, source.Pos[id], CONST.ModUpFw,
                                pair.Pockets[slot].st_pos[id].x, XT.EM_XTFLOW.PICKMOD, true, Demo,
                                capQrcodePos: PT_SET.bAddCapQrcode ? traybox_fd.tray_cur.list_AddCapQrcodepos[source.idx].Pos[id] : new ST_XYZA());
                        }
                        CtStepLog.SetContext(disc, ctPrev);
                        if (result != EM_RES.OK)
                        {
                            ThreeNozzlePickRecoveryAction action;
                            EM_RES handled = ThreeNozzleHandleTrayPickFailure(ref quit, xt, source.md, result, out action);
                            if (handled != EM_RES.OK) return handled;
                            if (action != ThreeNozzlePickRecoveryAction.PickedOnInterrupt)
                            {
                                if (action == ThreeNozzlePickRecoveryAction.ReplaceTray)
                                {
                                    handled = GoZero(ref quit);
                                    if (handled != EM_RES.OK) return handled;
                                    traybox_fd.IsReady = false;
                                }
                                slot--;
                                continue;
                            }
                            // 被停止打断但料已在嘴上：按取料成功提交（取料位置在 XtCapMovMod 下降前已记录）
                        }
                        ThreeNozzleTrayPickSucceeded();
                        xt.XtMd = source.md;
                        source.md = null;
                        pair.Prepared[slot] = xt.XtMd;
                        pair.SourceTrays[slot] = traybox_fd.tray_cur;
                        pair.SourcePositions[slot] = source;
                        pair.PickedPositions[slot] = xt.xt_pos_pick_mod.clone();
                        pair.PickedTrayXs[slot] = xt.xt_pos_pick_tray_x;
                        if (PT_SET.BarcodeMode == (int)PT_SET.BAR_SCAN.UP_SCAN || PT_SET.bAddCapQrcode) xt.XtMd.bardcode = barcode;
                        COUNT_DATA.SuctionAllcnt++;
                        }
                    }
                    if (!Object.ReferenceEquals(xt.XtMd, pair.Prepared[slot])) return ThreeNozzleError("预取身份变化", true);
                    if (!pair.DownReady[slot] && !pair.DownDeferred[slot])
                    {
                        bool deferDown = allowDeferDown && ThreeNozzleCanDeferDown();
                        // 下相机推迟到进夹具飞拍、又不做电机扫码时，这里不做任何耗时动作：
                        // 不放开料盘锁，本臂接着取本组下一颗。否则会在两颗之间把料盘让给对臂，
                        // 两臂轮流各取一颗、供料盘 X 来回移动，表现为臂在料盘上方明显停顿等待。
                        bool scanTakesTime = !deferDown || PT_SET.bmotorphoto;
                        if (held && scanTakesTime) { Monitor.Exit(picktray); held = false; }
                        Cam.VisionOutPutData vision = new Cam.VisionOutPutData();
                        result = ThreeNozzlePrepareScanning(ref quit, xt, ref vision, deferDown);
                        if (result == EM_RES.CAM_ERR_PLACE_BACK || result == EM_RES.MOTOR_CAM_ERR_PLACE_BACK)
                        {
                            pair.ReturnCauses[slot] = result;
                            slot--;
                            continue;
                        }
                        if (result != EM_RES.OK) return result;
                        if (deferDown)
                        {
                            pair.DownDeferred[slot] = true;
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴飞拍: {1} 已取料，下相机推迟到进夹具时飞拍", disc, xt.disc));
                        }
                        else
                        {
                            pair.DownVision[slot] = vision;
                            pair.DownReady[slot] = true;
                        }
                    }
                }
                return EM_RES.OK;
            }
            finally { threeNozzleTrayFlyResults.Clear(); if (held) Monitor.Exit(picktray); }
        }

        #region 料盘盲取（不拍上相机，按料盘示教位置直接取）

        /// <summary>
        /// 供料盘盲取开关（调试模式窗口里勾选，只在本次运行有效，默认关闭）。
        /// 开启后从供料盘取新料不再“先到上相机位拍照、再移到取料位”，而是按料盘示教位置算出取料位直接取；
        /// 吸嘴上的偏移由进夹具时的下相机（飞拍/定拍）纠正，放料精度不变。
        /// 需要上相机读码（上相机扫码 / 附加扫码位）或探针检测时自动回到拍照取料。点检样品始终拍照取。
        /// </summary>
        public static bool ThreeNozzleBlindTrayPickEnabled = false;
        private static string lastBlindFallbackReason;

        private static bool ThreeNozzleBlindPickAllowed(out string whyNot)
        {
            whyNot = null;
            if (!ThreeNozzleBlindTrayPickEnabled) return false;
            if (PT_SET.BarcodeMode == (int)PT_SET.BAR_SCAN.UP_SCAN) whyNot = "条码由上相机读取（BarcodeMode=上相机扫码）";
            else if (PT_SET.bAddCapQrcode) whyNot = "启用了上相机附加扫码位（bAddCapQrcode）";
            else if (PT_SET.OvenCheck) whyNot = "启用了探针检测（OvenCheck，取料前要拍照）";
            return whyNot == null;
        }

        private void ThreeNozzleNoteBlindPickFallback(string whyNot)
        {
            if (string.IsNullOrEmpty(whyNot) || whyNot == lastBlindFallbackReason) return;
            lastBlindFallbackReason = whyNot;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, "三吸嘴: 已勾选料盘盲取，但" + whyNot + "，仍按拍照取料");
        }

        /// <summary>
        /// 盲取一颗：与 XtCapMovMod 拍照取料同一套坐标换算（CaliPos 定料盘 X、XtCalPos 算吸嘴取料位），
        /// 只是上相机偏移按 0 处理，臂直接走到取料位、料盘 X 同时到位，然后取料。调用方持有 picktray。
        /// </summary>
        /// <summary>
        /// 开关：盲取时总是让臂在“本组夹具那一列的 X”上取料、供料盘 X 去配合（默认开）。
        /// 原来只有臂在 X≈0 时才这样选；臂刚从料仓过来（X≈±50）时会就近在 X≈±40 取，
        /// 取完进夹具还要先把 X 走到那一列（约 0.45s，飞拍只能“X 先到位再起飞”）。
        /// 关掉恢复原来的按臂当前 X 选取料位。
        /// </summary>
        public static bool ThreeNozzleBlindPickAtColumn = true;

        private EM_RES ThreeNozzleBlindTrayPick(ref bool quit, XT xt, Product.Tray.PosInf source, double wsXPos)
        {
            ST_XYZA camPos;
            if (ThreeNozzleBlindPickAtColumn && Math.Abs(wsXPos) > 3)
            {
                // 让“吸嘴取料时臂的 X”正好等于本组夹具列 X：先按列 X 算一次，量出这个吸嘴相对上相机的 X 偏移，
                // 再把上相机 X 反推回去（供料盘 X 跟着补）。这样两颗取完臂 X 就在列上，飞拍不用再横移 X，
                // 可以从料盘直接起飞（不停在飞拍起点）
                camPos = xt.CaliPosAt(source.Pos[id], wsXPos, 0);
                double nozzleDx = xt.XtCalPos(camPos.ToXY(), new ST_XYA(), xt.st_vs_pos_xtshp, XT.EM_XTFLOW.PICKMOD).x - camPos.x;
                double camX = wsXPos - nozzleDx;
                if (FlyFinite(camX) && Math.Abs(camX) > 3 && Math.Abs(nozzleDx) < 50)
                    camPos = xt.CaliPosAt(source.Pos[id], camX, 0);
            }
            else camPos = xt.CaliPos(source.Pos[id], wsXPos);
            ST_XYZA pickPos = xt.XtCalPos(camPos.ToXY(), new ST_XYA(), xt.st_vs_pos_xtshp, XT.EM_XTFLOW.PICKMOD).ToXYZA();
            try { pickPos.z = NozzleZCalibration.ToAxisZ(id, xt.SlotIndex, source.Pos[id].z, xt.ZApproachDirection); }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "吸嘴Z标定不可用：" + ex.Message);
                return EM_RES.PARA_ERR;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} {1} 盲取 供料盘穴{2}：取料位 X={3:F3} Y={4:F3} Z={5:F3}，料盘X={6:F3}（不拍上相机）",
                disc, xt.disc, source.idx, pickPos.x, pickPos.y, pickPos.z, camPos.a));
            // 臂和料盘 X 一起到位（Z 先抬到安全高度，由 ZupMove 负责）
            EM_RES result = MT.ZupMove(ref quit, ref ax_x, pickPos.x, ref ax_y, pickPos.y, ref traybox_fd.ax_x, camPos.a);
            if (result != EM_RES.OK) return result;
            traybox_fd.MoveToLastPos[id] = true;   // 与拍照取料（XtCapMovMod）的料盘状态记录一致
            xt.xt_pos_pick_tray_x = traybox_fd.ax_x.fenc_pos;
            xt.xt_pos_pick_mod = pickPos;
            result = xt.PickOrPlace(ref quit, pickPos, true, Demo, true);
            if (result != EM_RES.OK) return result;
            AXIS u = xt.ax_u;
            MT.Move(ref quit, ref u, 0);
            return EM_RES.OK;
        }

        #endregion

        #region 提前预取（尾部卸料后、转盘转动期间）
        /// <summary>给实时显示用：该吸嘴上的料是否是提前预取、还没分配到工站的料。</summary>
        public bool ThreeNozzleIsEarlyHeldForView(XT xt) { return ThreeNozzleIsEarlyHeld(xt); }

        private bool ThreeNozzleIsEarlyHeld(XT xt)
        {
            ThreeNozzleRuntimePair early = threeNozzleEarly;
            if (early == null || xt == null || xt.XtMd == null) return false;
            for (int slot = 0; slot < 2; slot++)
                if (early.Prepared[slot] != null && Object.ReferenceEquals(list_xt[early.IncomingNozzles[slot]], xt) &&
                    Object.ReferenceEquals(xt.XtMd, early.Prepared[slot])) return true;
            return false;
        }

        /// <summary>不提前预取的原因；null=可以。</summary>
        private string ThreeNozzleEarlySkipReason(WS target)
        {
            if (status_ud == EM_STA.UNKNOW || status_ud == EM_STA.HOME) return "未完成复位";
            if (threeNozzleRecovery) return "待人工对账";
            if (threeNozzleSource != null) return "上一工站计划未结束";
            if (threeNozzleEarly != null) return "已有提前预取的料";
            if (FindMod == EM_FIN_MOD.NONE) return "本臂未分配穴位";
            if (ModuleCheckHooks.AffectsStation(target)) return "该工站是模组自动点检承载工站";
            if (VAR.ClearMt || PT_SET.bGrrFlow || VAR.isAutoChkMode || VAR.gsys_set.isChkMode) return "清料/GRR/点检流程";
            if (id == 0 ? PT_SET.bUpDn1XtOnlyOne : PT_SET.bUpDn2XtOnlyOne) return "单吸嘴模式";
            if (ThreeNozzleUnsupportedReason() != null) return ThreeNozzleUnsupportedReason();
            if (list_xt.Count != 3) return "吸嘴数量不是3";
            foreach (XT xt in list_xt)
                if (xt.XtMd != null || (!Demo && xt.cy_zk.isONByChkSen)) return xt.disc + "上还有料";
            for (int slot = 1; slot < 3; slot++)
                if (!list_xt[slot].dwcam.List_vs_task.Any(t => t.TaskName == CONST.ModDwFw[slot]))
                    return "缺少吸嘴下视觉任务 " + CONST.ModDwFw[slot];
            return null;
        }

        /// <summary>按与 ThreeNozzleEnsurePlan 相同的顺序和 (k, k+8) 分组，只看穴位启用，预判本臂第一组。</summary>
        private ThreeNozzleRuntimePair ThreeNozzleBuildEarlyPair(WS ws)
        {
            int key = -1;
            ThreeNozzleRuntimePair pair = null;
            for (int i = 0; i < ws.list_md.Count; i++)
            {
                int n = i % 2 == 0 ? i - i / 2 : i + (ws.list_md.Count - 1 - i) / 2;
                if ((FindMod == EM_FIN_MOD.LOW && n % 8 >= 4) || (FindMod == EM_FIN_MOD.HIGH && n % 8 < 4)) continue;
                WS.MdDat md = ws.list_md[n];
                if (md == null || !md.benable || !ThreeNozzleCanLoadPocket(md)) continue;
                if (key < 0) { key = md.Num % 8; pair = new ThreeNozzleRuntimePair(); }
                if (md.Num % 8 != key) continue;
                int slot = md.Num > 8 ? 1 : 0;
                if (pair.Pockets[slot] != null) continue;
                pair.Pockets[slot] = md;
                pair.Incoming[slot] = true;
            }
            return pair;
        }

        /// <summary>
        /// 尾部卸料完成后（协调器仍保持本臂只在料盘区），为下一个需要上料的工站提前取第一组新料：
        /// 吸嘴2/3 从供料盘取料、电机扫码、下相机，然后回安全位等待。不满足条件时直接跳过（返回 OK），
        /// 工站到位后按原流程预取。
        /// </summary>
        /// <summary>
        /// 提前预取开关（默认开）。关闭后尾部卸料完成即回安全位，不再为下一个工站提前取料，工站到位后按原流程预取。
        /// 调试页可临时切换，不写配置，重启软件恢复开启。用于对比提前预取对避障等待/弹窗/节拍的影响。
        /// </summary>
        public static bool ThreeNozzleEarlyPrefetchEnabled = true;

        public EM_RES ThreeNozzleEarlyPrefetch(ref bool quit, WS target)
        {
            if (target == null) return EM_RES.OK;
            if (!ThreeNozzleAcquireExecutor(ref quit)) return EM_RES.OK;
            try
            {
                EM_RES pendingIssue = ThreeNozzleEntryPrepare(ref quit);
                if (pendingIssue != EM_RES.OK) return pendingIssue;
                string skip = !ThreeNozzleEarlyPrefetchEnabled ? "提前预取已在调试页关闭" : ThreeNozzleEarlySkipReason(target);
                ThreeNozzleRuntimePair pair = skip == null ? ThreeNozzleBuildEarlyPair(target) : null;
                if (skip == null && pair == null) skip = target.disc + " 本臂范围内没有启用的穴位";
                if (skip != null)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴: 跳过提前预取（{1}），到位后再预取", disc, skip));
                    return EM_RES.OK;
                }
                var watch = Stopwatch.StartNew();
                CtStepLog.SetContext(disc, "提前预取待测(" + target.disc + ")");
                threeNozzleDrain = false;
                threeNozzleEarly = pair;   // 先登记：中途失败时吸嘴上的料仍有身份，不会被当成“无计划持料”
                EM_RES result = ThreeNozzlePreparePair(ref quit, pair, true);
                // 一颗都没取到（例如等料盘时被另一臂报错停止打断）就撤销登记，否则续接时会误判
                // “已有提前预取的料”而跳过预取，工站到位后才去料盘取（18日日志 13:40/14:02/14:10/14:31 共4次）。
                // 只在三根吸嘴确实都无料时撤销，吸嘴上有料仍保留登记，避免被当成无计划持料。
                if (pair.Prepared[0] == null && pair.Prepared[1] == null && ThreeNozzleAllNozzlesEmpty())
                {
                    if (result != EM_RES.OK && threeNozzleEarly == pair)
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴: 提前预取被打断（{1}），吸嘴均无料，撤销预取登记，续接时重新预取", disc, result));
                    threeNozzleEarly = null;
                }
                if (result != EM_RES.OK) return result;
                result = ThreeNozzleParkAfterPrefetch(ref quit, pair);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴: 为{1}提前预取完成，预判穴位[{2}]，用时 {3:F2}s",
                    disc, target.disc,
                    string.Join("/", pair.Pockets.Where(m => m != null).Select(m => m.Num.ToString())),
                    watch.ElapsedMilliseconds / 1000.0));
                return result;
            }
            catch { threeNozzleRecovery = true; throw; }
            finally { Interlocked.Exchange(ref threeNozzleExecutor, 0); }
        }

        /// <summary>三根吸嘴都没有物料记录且真空无料。</summary>
        private bool ThreeNozzleAllNozzlesEmpty()
        {
            foreach (XT xt in list_xt)
                if (xt.XtMd != null || (!Demo && xt.cy_zk.isONByChkSen)) return false;
            return true;
        }

        /// <summary>
        /// 建好计划后调用：提前预取的料交给当前第一组的同一个吸嘴位置（A=吸嘴2，B=吸嘴3）；
        /// 第一组不需要、特殊模式、清料等情况，把料退回料盘原穴。
        /// </summary>
        private EM_RES ThreeNozzleAdoptEarly(ref bool quit)
        {
            ThreeNozzleRuntimePair early = threeNozzleEarly;
            if (early == null) return EM_RES.OK;
            ThreeNozzleRuntimePair first = null;
            if (!ThreeNozzleHasSpecialMode && !threeNozzleDrain && threeNozzlePairIndex < threeNozzlePairs.Count &&
                !ModuleCheckHooks.IsCheckLoading(threeNozzleSource))   // 装样品轮：提前取的普通料一律退回
            {
                ThreeNozzleRuntimePair candidate = threeNozzlePairs[threeNozzlePairIndex];
                if (candidate.Sequence != null && candidate.Sequence.Current == ThreeNozzleAction.PrepareIncoming) first = candidate;
            }
            for (int slot = 0; slot < 2; slot++)
            {
                if (early.Prepared[slot] == null) continue;
                XT xt = list_xt[early.IncomingNozzles[slot]];
                if (!Object.ReferenceEquals(xt.XtMd, early.Prepared[slot]))
                    return ThreeNozzleError("提前预取的物料身份变化，请对账", true);
                if (first != null && first.Incoming[slot] && !first.Placed[slot] && first.Prepared[slot] == null &&
                    first.IncomingNozzles[slot] == early.IncomingNozzles[slot] && early.ReturnCauses[slot] == EM_RES.OK)
                {
                    first.Prepared[slot] = early.Prepared[slot];
                    first.DownVision[slot] = early.DownVision[slot];
                    first.DownReady[slot] = early.DownReady[slot];
                    first.DownDeferred[slot] = early.DownDeferred[slot];
                    first.SourceTrays[slot] = early.SourceTrays[slot];
                    first.SourcePositions[slot] = early.SourcePositions[slot];
                    first.PickedPositions[slot] = early.PickedPositions[slot];
                    first.PickedTrayXs[slot] = early.PickedTrayXs[slot];
                    early.Prepared[slot] = null;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴: {1} 提前预取的料交给穴{2}", disc, xt.disc,
                        first.Pockets[slot] == null ? "?" : first.Pockets[slot].Num.ToString()));
                    continue;
                }
                EM_RES result = ThreeNozzleTakeLock(ref quit, picktray);
                if (result != EM_RES.OK) return result;
                try
                {
                    result = ThreeNozzleReturnPrepared(ref quit, xt, early.SourceTrays[slot], early.SourcePositions[slot],
                        early.PickedPositions[slot], early.PickedTrayXs[slot], false);
                }
                finally { Monitor.Exit(picktray); }
                if (result != EM_RES.OK) return result;
                early.Prepared[slot] = null;
                early.DownReady[slot] = false;
                early.DownDeferred[slot] = false;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴: {1} 提前预取的料本组用不上，已退回料盘原穴", disc, xt.disc));
            }
            threeNozzleEarly = null;
            return EM_RES.OK;
        }
        #endregion

        public EM_RES ThreeNozzleRunExchange(ref bool quit, WS ws)
        {
            if (!ThreeNozzleAcquireExecutor(ref quit)) return EM_RES.ABORT;
            try
            {
                EM_RES pendingIssue = ThreeNozzleEntryPrepare(ref quit);
                if (pendingIssue != EM_RES.OK) return pendingIssue;
                EM_RES result = ThreeNozzleEnsurePlan(ws);
                if (result != EM_RES.OK) return result;
                // 中途停机再运行时不用上次进夹具拍的放料前照片（期间可能有人动过夹具），放料时按定拍
                foreach (ThreeNozzleRuntimePair p in threeNozzlePairs)
                    p.FlyBeforePlace[0] = p.FlyBeforePlace[1] = null;
                result = ThreeNozzleAdoptEarly(ref quit);
                if (result != EM_RES.OK) return result;
                if (ThreeNozzleHasSpecialMode) return ThreeNozzleRunSpecialMode(ref quit, ws);
                while (threeNozzlePairIndex < threeNozzlePairs.Count)
                {
                    if (quit) return EM_RES.QUIT;
                    ThreeNozzleRuntimePair pair = threeNozzlePairs[threeNozzlePairIndex];
                    if (VAR.ClearMt && pair.Sequence.Current == ThreeNozzleAction.PrepareIncoming &&
                        pair.Prepared.All(material => material == null))
                    {
                        pair.Incoming[0] = pair.Incoming[1] = false;
                        pair.Sequence = new ThreeNozzleSequence(pair.Pockets[0] != null && pair.Pockets[0].res >= 0,
                            false, pair.Pockets[1] != null && pair.Pockets[1].res >= 0, false);
                    }
                    ThreeNozzleAction action = pair.Sequence.Current;
                    if (action == ThreeNozzleAction.UnloadFinished && threeNozzlePairIndex == threeNozzlePairs.Count - 1)
                        return EM_RES.OK; // The coordinator owns the final handoff.
                    CtStepLog.SetContext(disc, ThreeNozzleCtLabel(action, pair));
                    switch (action)
                    {
                        case ThreeNozzleAction.PrepareIncoming: result = ThreeNozzlePreparePair(ref quit, pair, true); break;
                        case ThreeNozzleAction.WaitFixtureReady:
                            result = ws.FeedStatus == WS.EM_STA.REDAYFORUPDOWNLOAD || Demo ? EM_RES.OK : EM_RES.ABORT;
                            // 夹具就绪后进夹具：推迟的下相机和取料前检查一趟 Y 飞拍（飞拍未开启/条件不满足时按定拍补上）
                            if (result == EM_RES.OK) result = ThreeNozzleResolveDeferredDown(ref quit, pair, true);
                            break;
                        case ThreeNozzleAction.PickA: result = ThreeNozzlePickOne(ref quit, pair, 0); break;
                        case ThreeNozzleAction.PickB: result = ThreeNozzlePickOne(ref quit, pair, 1); break;
                        case ThreeNozzleAction.PlaceA: result = ThreeNozzlePlaceOne(ref quit, pair, 0); break;
                        case ThreeNozzleAction.PlaceB: result = ThreeNozzlePlaceOne(ref quit, pair, 1); break;
                        case ThreeNozzleAction.LeaveFixture:
                        {
                            // 回检飞拍的终点：接着要直接去料盘落料时飞到第一颗成品落料位的 Y，最后一组飞到等待位高度，
                            // 不在夹具口（触发点 − 50mm）停一下再另走一段
                            double backCheckEndY = double.NaN;
                            if (ThreeNozzleBackCheckRunThroughEnabled)
                            {
                                bool willGoDirect = ThreeNozzleDirectUnloadEnabled && threeNozzlePairIndex < threeNozzlePairs.Count - 1 &&
                                    list_xt.Any(nz => nz.XtMd != null && nz.XtMd.res >= 0);
                                bool willTailDirect = threeNozzlePairIndex == threeNozzlePairs.Count - 1 &&
                                    ThreeNozzleTailSkipHomeEnabled && !ThreeNozzleArmNeedsPostChecks(ws);
                                if (willGoDirect) backCheckEndY = ThreeNozzleEstimateFirstUnloadY();
                                else if (willTailDirect) backCheckEndY = list_xt[1].st_cap_pos.y;
                            }
                            result = ThreeNozzleCompletePostPlaceChecks(ref quit, pair, backCheckEndY);
                            if (result != EM_RES.OK) return result;
                            bool notLastPair = threeNozzlePairIndex < threeNozzlePairs.Count - 1;
                            // 不是最后一组、手上有成品接着要落料：不回零等待位停一下，直接从夹具去料盘
                            bool goDirect = ThreeNozzleDirectUnloadEnabled && notLastPair &&
                                list_xt.Any(nz => nz.XtMd != null && nz.XtMd.res >= 0);
                            // 不是最后一组（最后一组的卸料由协调线程做，锁不能跨线程带过去）：离开夹具的同时让成品料盘先走到本臂的落料位。
                            // 落料位要按“落料时臂的 X”算：直接去料盘时臂 X 不变（还在夹具这一列），回等待位时 X≈0
                            if (notLastPair) ThreeNozzlePrepositionPlaceTray(goDirect ? ax_x.fcmd_pos : 0);
                            // 最后一组：不再回 X=0，只把 Y 退到等待位高度（下相机拍照位），X 留在原地，由尾部卸料直接去料仓。
                            // 需要协调线程统一做后检查（BeginUnload 要求两臂 X≈0）时照旧回零。
                            bool tailDirect = !notLastPair && ThreeNozzleTailSkipHomeEnabled && !ThreeNozzleArmNeedsPostChecks(ws);
                            result = ThreeNozzleFinishChecksAndLeave(ref quit, goDirect, tailDirect);
                            break;
                        }
                        case ThreeNozzleAction.UnloadFinished: result = ThreeNozzleUnloadCore(ref quit); break;
                        case ThreeNozzleAction.Complete: threeNozzlePairIndex++; continue;
                        default: return ThreeNozzleError("未知动作", true);
                    }
                    // 取料/放料/置空/返工都会改夹具穴位状态；运行界面工站图只在 breschanged 时重画（原双嘴 Act 循环每轮都置位）
                    ws.breschanged = true;
                    if (result != EM_RES.OK) return result;
                    pair.Sequence.Step(a => true);
                }
                return GoZero(ref quit);
            }
            catch { threeNozzleRecovery = true; throw; }
            finally
            {
                if (ws != null) ws.breschanged = true;   // 特殊模式/中途出错/停止时也刷新一次工站图
                ThreeNozzleReleasePreHeldTray();   // 中途出错/停止时，预先占的成品料盘锁不能留着
                Interlocked.Exchange(ref threeNozzleExecutor, 0);
            }
        }

        #region 成品料盘预定位
        /// <summary>离开夹具时预先占用的成品料盘锁（OK/NG 盘），同一线程随后在 UnloadCore 里接着用。</summary>
        private object threeNozzlePreHeldTray;
        /// <summary>成品料盘 X 预定位的后台运动（与离开夹具的臂运动同时进行）。</summary>
        private System.Threading.Tasks.Task<EM_RES> threeNozzleTrayPreMove;
        /// <summary>开关：离开夹具时让 OK/NG 料盘先移到本臂第一颗成品的落料位（默认开）。</summary>
        public static bool ThreeNozzlePrepositionPlaceTrayEnabled = true;

        /// <summary>
        /// 离开夹具时：本臂第一颗要落的成品所在料盘（OK/NG）如果空闲，就先占住它，
        /// 在后台把料盘 X 移到这颗料的落料位，臂同时退出夹具；到料盘区时料盘已经在等，不用再等料盘 X 走过来。
        /// 料盘正被对臂使用时不等待，照原流程到时排队。需要换盘、点检样品等情况也交给原流程。
        /// </summary>
        private void ThreeNozzlePrepositionPlaceTray(double armXAtPlace)
        {
            if (!ThreeNozzlePrepositionPlaceTrayEnabled || !ThreeNozzleSplitPlaceTrayLock || threeNozzlePreHeldTray != null) return;
            try
            {
                IEnumerable<XT> order = NewSysInf.UserParams.bXtPrePlaceTrayByBigId
                    ? list_xt.OrderByDescending(nozzle => nozzle.id) : list_xt.OrderBy(nozzle => nozzle.id);
                XT first = order.FirstOrDefault(xt => xt.XtMd != null && !IsModuleCheckSample(xt.XtMd) && xt.XtMd.res >= 0);
                if (first == null) return;
                int res = first.XtMd.res;
                object need = res == 0 ? placeTrayOk : placeTrayNg;
                if (!Monitor.TryEnter(need)) return;   // 对臂正在用这个料盘：不等
                bool keep = false;
                try
                {
                    TrayBox tray = res == 0 ? traybox_ok : traybox_ng;
                    if (tray == null || !tray.IsReady || tray.trayActionRunning || tray.ChgML || tray.tray_cur == null || tray.ax_x == null) return;
                    var positions = tray.IsOkTray ? tray.tray_cur.GetPosList(Product.EM_CM_RES.EMPTY) :
                        tray.tray_cur.GetPosList(Product.EM_CM_RES.EMPTY, res);
                    if (positions == null || positions.Count == 0) return;   // 满盘要换：交给原流程
                    // CaliPos 按“落料时臂的 X”选料盘位置：回等待位时臂 X≈0；直接从夹具去料盘时臂 X 不变。
                    // 必须和真正落料时的臂 X 一致，否则预定位和真正落料位不一样，料盘 X 还要再走一次
                    double target = first.CaliPosAt(positions[0].Pos[id], 0, armXAtPlace).a;
                    AXIS trayAxis = tray.ax_x;
                    keep = true;
                    threeNozzlePreHeldTray = need;
                    if (Math.Abs(trayAxis.fcmd_pos - target) < 0.01) return;   // 已经在位
                    string actor = disc;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴: 离开夹具时{1}先移到本臂落料位 {2}X {3:F3}→{4:F3}（穴{5}）",
                        disc, tray.disc, tray.disc, trayAxis.fcmd_pos, target, positions[0].idx));
                    threeNozzleTrayPreMove = System.Threading.Tasks.Task.Run(() =>
                    {
                        CtStepLog.SetContext(tray.disc, "预定位到" + actor);   // 单独记在料盘名下，不和臂的动作叠加
                        return MT.Move(ref VAR.gsys_set.bquit, ref trayAxis, target);
                    });
                }
                finally { if (!keep) Monitor.Exit(need); }
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 成品料盘预定位跳过：" + ex.Message);
            }
        }

        /// <summary>等后台料盘预定位结束。失败不算错：落料时 ZupMove 会再把料盘 X 走到位。</summary>
        private void ThreeNozzleWaitTrayPreMove()
        {
            var task = threeNozzleTrayPreMove;
            threeNozzleTrayPreMove = null;
            if (task == null) return;
            try
            {
                EM_RES r = task.Result;
                if (r != EM_RES.OK)
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: 成品料盘预定位未完成（{1}），落料时再走到位", disc, r));
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 成品料盘预定位异常：" + ex.Message);
            }
        }

        /// <summary>交出预先占的料盘锁（先等预定位运动结束，避免对臂接手时料盘还在动）。</summary>
        private void ThreeNozzleReleasePreHeldTray()
        {
            ThreeNozzleWaitTrayPreMove();
            object held = threeNozzlePreHeldTray;
            threeNozzlePreHeldTray = null;
            if (held != null)
            {
                try { Monitor.Exit(held); } catch (SynchronizationLockException) { }
            }
        }
        #endregion

        /// <summary>CT 分解日志的上下文：当前在做哪一步、哪个穴</summary>
        private static string ThreeNozzleCtLabel(ThreeNozzleAction action, ThreeNozzleRuntimePair pair)
        {
            string a = pair.Pockets[0] == null ? "-" : pair.Pockets[0].Num.ToString();
            string b = pair.Pockets[1] == null ? "-" : pair.Pockets[1].Num.ToString();
            switch (action)
            {
                case ThreeNozzleAction.PrepareIncoming: return "取待测(穴" + a + "/" + b + ")";
                case ThreeNozzleAction.WaitFixtureReady: return "进夹具飞拍(穴" + a + "/" + b + ")";
                case ThreeNozzleAction.PickA: return "取成品 穴" + a;
                case ThreeNozzleAction.PlaceA: return "放待测 穴" + a;
                case ThreeNozzleAction.PickB: return "取成品 穴" + b;
                case ThreeNozzleAction.PlaceB: return "放待测 穴" + b;
                case ThreeNozzleAction.LeaveFixture: return "离开夹具";
                case ThreeNozzleAction.UnloadFinished: return "放成品到料仓";
                default: return action.ToString();
            }
        }

        private EM_RES ThreeNozzlePickOne(ref bool quit, ThreeNozzleRuntimePair pair, int slot)
        {
            WS.MdDat md = pair.Pockets[slot];
            XT xt = list_xt[pair.FinishedNozzles[slot]];
            Cam.VisionOutPutData flyCheck = pair.FlyBeforePick[slot];
            pair.FlyBeforePick[slot] = null;   // 只用一次：重试/返工时按定拍重新检查
            // 取料失败弹窗里“置空 → 更新状态执行”会把同一工站其它穴也改成空料(-2)，计划里这些穴仍排着取料。
            // 吸嘴确认无料时按人工确认空穴跳过（与“放弃吸取”的记录处理相同），后面的放新料照常。
            if (md != null && md.res == (int)Product.EM_CM_RES.EMPTY &&
                xt.XtMd == null && (Demo || !xt.cy_zk.isONByChkSen))
            {
                threeNozzleFixtureMaterials.Remove(md);
                md.bardcode = null;
                md.motor_barcode = null;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: {1}穴{2}已被人工置空，跳过取料",
                    disc, threeNozzleSource == null ? "" : threeNozzleSource.disc, md.Num));
                return EM_RES.OK;
            }
            return ThreeNozzlePickMaterial(ref quit, md, xt, true, flyCheck);
        }

        /// <summary>
        /// 开关：组与组之间离开夹具后直接去料盘落成品（默认开）。
        /// 关掉则恢复原来的“先回零等待位（X=0，Y=下相机拍照位）停一下，再去料盘”。
        /// 每个工站的最后一组照旧回等待位（之后由协调线程做尾部卸料，合夹具要等臂在等待位）。
        /// </summary>
        public static bool ThreeNozzleDirectUnloadEnabled = true;

        /// <summary>
        /// 开关：每个工站最后一组离开夹具时不回 X=0（默认开）。Y 照旧退到等待位高度（下相机拍照位，与原 GoZero 相同），
        /// X 留在夹具这一列，尾部卸料从这里直接去 OK/NG 料仓，省掉“回安全位再去料仓”的一段横移。
        /// 协调线程进入只在料盘区阶段（BeginArmUnload）时相应不再要求 X≈0，仍要求 Y 在 SafeTrayMaxY 以内、Z 安全、XY 停止。
        /// 关掉恢复原来的回零等待位。
        /// </summary>
        public static bool ThreeNozzleTailSkipHomeEnabled = true;

        /// <summary>开关：回检飞拍不在夹具口停，直接飞到接下来的落料位 Y（直接去料盘时）或等待位高度（最后一组）（默认开）。</summary>
        public static bool ThreeNozzleBackCheckRunThroughEnabled = true;

        /// <summary>开关：离开夹具时成品料盘还在预定位，先把臂走到第一颗成品的落料位上方（默认开），不在夹具口停着等料盘。</summary>
        public static bool ThreeNozzleApproachDuringTrayPreMoveEnabled = true;

        private EM_RES ThreeNozzleFinishChecksAndLeave(ref bool quit, bool goDirect = false, bool tailDirect = false)
        {
            foreach (XT xt in list_xt)
            {
                if (xt.XtMd == null || xt.XtMd.res < 0 || IsModuleCheckSample(xt.XtMd)) continue;
                EM_RES check = ThreeNozzleCheckFinishedDownBarcode(ref quit, threeNozzleSource, xt);
                if (check != EM_RES.OK) return check;
            }
            if (goDirect)
            {
                // 只抬 Z、U 回 0（GoZero 里也会做），X/Y 不停在等待位：接下来落料的 ZupMove 直接从夹具走到料盘
                EM_RES r = MT.RaiseAllArmZ(ref quit, id);
                if (r != EM_RES.OK) return r;
                return MT.Move(ref quit, ref ax_u1, 0, ref ax_u2, 0, ref list_xt[2].ax_u, 0);
            }
            if (tailDirect)
            {
                double waitY = list_xt[1].st_cap_pos.y;
                double safeY = ThreeNozzleHardwareConfig.SafeTrayMaxY(id);
                if (FlyFinite(waitY) && FlyFinite(safeY) && waitY <= safeY)
                {
                    EM_RES r = MT.RaiseAllArmZ(ref quit, id);
                    if (r != EM_RES.OK) return r;
                    double y = Math.Min(ax_y.fcmd_pos, waitY);
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} [R2] 三吸嘴: 最后一组离开夹具，不回 X=0，X 留在 {1:F3}，Y {2:F3}→{3:F3}，尾部卸料直接去料仓",
                        disc, ax_x.fcmd_pos, ax_y.fcmd_pos, y));
                    r = MT.ZupMove(ref quit, ref ax_y, y, ref ax_u1, 0, ref ax_u2, 0);
                    if (r != EM_RES.OK) return r;
                    return MT.Move(ref quit, ref list_xt[2].ax_u, 0);
                }
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} [R2] 三吸嘴: 等待位 Y={1:F3} / SafeTrayMaxY={2:F3} 不可用，最后一组照旧回零", disc, waitY, safeY));
            }
            return GoZero(ref quit);
        }

        // Diagnostics only: never clear records, change targets, or issue motion/IO commands here.
        private void ThreeNozzleLogFixturePick(string phase, WS.MdDat md, XT xt, ST_XYZA target,
            int attempt, EM_RES result, string decision = "")
        {
            try
            {
                var raw = md.st_pickpos[id];
                var photo = md.st_pos[id];
                var basis = list_xt[0].st_rol_cap;
                var nozzle = xt.st_rol_cap;
                var offset = xt.st_vs_pos_xtshp;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG,
                    $"[夹具取料诊断] {disc} 阶段={phase} 产品={VAR.gsys_set.cur_product_name} " +
                    $"工站={threeNozzleSource?.disc} 穴={md.Num} 吸头={xt.disc} 尝试={attempt} 返回={result} 决定={decision} " +
                    $"ClearMt={VAR.ClearMt} 计划清料={threeNozzleDrain} 模式={threeNozzleMode} 穴结果={md.res} " +
                    $"取料配置=WS{(threeNozzleSource == null ? 0 : threeNozzleSource.num + 1)}Cfg/MD{md.Num}/PX{id + 1},PY{id + 1},PZ{id + 1} " +
                    $"原始取料XYZ=({raw.x:F3},{raw.y:F3},{raw.z:F3}) 拍照XYZ=({photo.x:F3},{photo.y:F3},{photo.z:F3}) " +
                    $"本嘴机械XY=({nozzle.x:F3},{nozzle.y:F3}) 基准嘴机械XY=({basis.x:F3},{basis.y:F3}) " +
                    $"本嘴标定XY=({offset.x:F3},{offset.y:F3}) 最终目标XYZU=({target.x:F3},{target.y:F3},{target.z:F3},{target.a:F3}) " +
                    $"当前XY=({ax_x.fenc_pos:F3},{ax_y.fenc_pos:F3}) 当前Z={xt.ax_z.fenc_pos:F3} " +
                    $"Y指令={ax_y.fcmd_pos:F3} Y回零={ax_y.home_status} Y软限位=[{ax_y.sln:F3},{ax_y.slp:F3}] " +
                    $"目标Y超软限位={target.y < ax_y.sln || target.y > ax_y.slp} " +
                    $"Y工作速度={ax_y.spd_work:F1} Tacc={ax_y.tacc:F3} Tdec={ax_y.tdec:F3}");
            }
            catch { /* A diagnostic read/write failure must not change the pick/recovery result. */ }
        }

        private EM_RES ThreeNozzlePickMaterial(ref bool quit, WS.MdDat md, XT xt, bool checkBefore = true, Cam.VisionOutPutData flyCheck = null)
        {
            if (md == null || md.res < 0 || xt.XtMd != null)
                return ThreeNozzleError("取成品前穴位/吸嘴状态不符", true);
            if (!Demo && xt.cy_zk.isONByChkSen)
            {
                EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, false, "夹具取料前真空有料但记录为空");
                if (corrected != EM_RES.OK) return corrected;
            }
            Camcfgset();
            if (checkBefore)
            {
                EM_RES check = ThreeNozzleCheckBeforePick(ref quit, threeNozzleSource, xt, md, flyCheck);
                if (check != EM_RES.OK) return check;
            }
            Product.MdDat incomingIdentity;
            var snapshot = threeNozzleFixtureMaterials.TryGetValue(md, out incomingIdentity)
                ? incomingIdentity.Clone() : new Product.MdDat();
            snapshot.Num = md.Num;
            snapshot.WS_ID = md.WS_ID;
            snapshot.PC_ID = md.PC_ID;
            snapshot.PC_IP = md.PC_IP;
            snapshot.test_idx = md.test_idx;
            snapshot.TestBox_ID = md.TestBox_ID;
            snapshot.SN = md.SN;
            snapshot.res = md.res;
            snapshot.ct = md.ct;
            snapshot.bardcode = md.bardcode;
            snapshot.motor_barcode = md.motor_barcode;
            snapshot.ModuleCheckSampleId = md.ModuleCheckSampleId;
            snapshot.benable = md.benable;
            snapshot.st_pos = md.st_pos[id];
            ST_XYZA position = md.st_pickpos[id].ToXYZA() + xt.st_rol_cap.ToXYZA() -
                list_xt[0].st_rol_cap.ToXYZA() + xt.st_vs_pos_xtshp.ToXY().ToXYZA();
            try { position.z = NozzleZCalibration.ToAxisZ(id, xt.SlotIndex, md.st_pickpos[id].z, xt.ZApproachDirection); }
            catch (Exception ex) { return ThreeNozzleError("夹具取料Z标定不可用：" + ex.Message); }
            position.a = PT_SET.isopendown_degree ? PT_SET.downdegree : 0;
            ThreeNozzleDryRun.NoteLiveAction(id, xt.SlotIndex, "fixture", md.Num,
                string.Format("{0} 从{1}夹具穴{2}取已测料", xt.disc, threeNozzleSource == null ? "" : threeNozzleSource.disc, md.Num));
            bool confirmedEmpty = false;
            int pickAttempt = 0;
            while (true)
            {
                EM_RES result = EM_RES.ERR;
                int attempts = threeNozzleMode != ThreeNozzleMode.GrrReload && PT_SET.bWsPickAgain ? 2 : 1;
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    ThreeNozzleLogFixturePick("取料下发前", md, xt, position, ++pickAttempt, EM_RES.OK);
                    result = xt.Pick(ref quit, position, true, Demo, true, NewSysInf.UserParams.bPickWsDis);
                    ThreeNozzleLogFixturePick("取料返回", md, xt, position, pickAttempt, result);
                    if (result != EM_RES.PICK_ERR) break;
                }
                if (result == EM_RES.OK) break;
                ThreeNozzlePickRecoveryAction action;
                EM_RES handled = ThreeNozzleHandleFixturePickFailure(ref quit, threeNozzleSource, md, xt, result, out action);
                ThreeNozzleLogFixturePick("恢复处理返回", md, xt, position, pickAttempt, handled, action.ToString());
                if (handled != EM_RES.OK) return handled;
                if (action == ThreeNozzlePickRecoveryAction.RetrySame) continue;
                if (action == ThreeNozzlePickRecoveryAction.ConfirmedEmpty) { confirmedEmpty = true; break; }
                if (action == ThreeNozzlePickRecoveryAction.PickedOnInterrupt) break;   // 被停止打断但料已在嘴上
                return result;
            }
            if (!confirmedEmpty) xt.XtMd = snapshot;
            if (!confirmedEmpty && IsModuleCheckSample(snapshot))
                ModuleCheckHooks.OnSamplePicked(snapshot.ModuleCheckSampleId, id, xt.SlotIndex, "夹具穴" + md.Num);
            threeNozzleFixtureMaterials.Remove(md);
            md.res = (int)Product.EM_CM_RES.EMPTY;
            md.bardcode = null;
            md.motor_barcode = null;
            md.ModuleCheckSampleId = null;
            return EM_RES.OK;
        }

        private EM_RES ThreeNozzlePlaceOne(ref bool quit, ThreeNozzleRuntimePair pair, int slot, bool countNew = true,
            bool completePostPlace = false, Cam.VisionOutPutData postPlaceImage = null)
        {
            WS.MdDat md = pair.Pockets[slot];
            XT xt = list_xt[pair.IncomingNozzles[slot]];
            if (!pair.Incoming[slot]) return EM_RES.OK;
            if (pair.Accepted[slot]) return EM_RES.OK;
            pair.CountAsNew[slot] |= countNew;
            while (!quit)
            {
                if (pair.ReworkStage[slot] != 0)
                {
                    postPlaceImage = null; // Interrupted rework must not inspect the replacement with an older image.
                    EM_RES retry;
                    if (pair.ReworkStage[slot] == 1)
                    {
                        // After grouped inspection this nozzle may hold the other pocket's finished part.
                        // Unload that known finished part before retrieving the rejected new placement.
                        if (completePostPlace && xt.XtMd != null)
                        {
                            retry = GoZero(ref quit);
                            if (retry != EM_RES.OK) return retry;
                            retry = ThreeNozzleCheckFinishedDownBarcode(ref quit, threeNozzleSource, xt);
                            if (retry != EM_RES.OK) return retry;
                            retry = ThreeNozzleUnloadCore(ref quit, xt);
                            if (retry != EM_RES.OK) return retry;
                        }
                        retry = ThreeNozzlePickMaterial(ref quit, md, xt, false);
                        if (retry != EM_RES.OK) return retry;
                        pair.ReworkStage[slot] = 2;
                    }
                    if (pair.ReworkStage[slot] == 2)
                    {
                        retry = GoZero(ref quit);
                        if (retry != EM_RES.OK) return retry;
                        retry = ThreeNozzleUnloadCore(ref quit, xt);
                        if (retry != EM_RES.OK) return retry;
                        pair.Placed[slot] = false;
                        pair.ReworkStage[slot] = 3;
                    }
                    if (VAR.ClearMt && pair.Prepared[slot] == null)
                    {
                        pair.Incoming[slot] = false;
                        pair.ReworkStage[slot] = 0;
                        pair.Accepted[slot] = true;
                        return EM_RES.OK;
                    }
                    retry = ThreeNozzlePreparePair(ref quit, pair);
                    if (retry != EM_RES.OK) return retry;
                    pair.CountAsNew[slot] = true;
                    pair.ReworkStage[slot] = 0;
                }
                if (!pair.Placed[slot])
                {
                    if (md != null && !md.benable && pair.Prepared[slot] != null)
                    {
                        Product.MdDat cancelledMaterial = pair.Prepared[slot];
                        EM_RES cancelled = ThreeNozzleTakeLock(ref quit, picktray);
                        if (cancelled != EM_RES.OK) return cancelled;
                        try
                        {
                            cancelled = ThreeNozzleReturnPrepared(ref quit, xt, pair.SourceTrays[slot],
                                pair.SourcePositions[slot], pair.PickedPositions[slot], pair.PickedTrayXs[slot], false);
                            if (cancelled != EM_RES.OK) return cancelled;
                            if (IsModuleCheckSample(cancelledMaterial))
                                ModuleCheckHooks.OnSampleReturned(cancelledMaterial.ModuleCheckSampleId, cancelledMaterial.idx);
                            pair.Prepared[slot] = null;
                            pair.DownReady[slot] = false;
                            pair.DownDeferred[slot] = false;
                            pair.Incoming[slot] = false;
                            pair.Accepted[slot] = true;
                            return EM_RES.OK;
                        }
                        finally { Monitor.Exit(picktray); }
                    }
                    if (pair.Prepared[slot] == null && md != null && md.benable && md.res == (int)Product.EM_CM_RES.EMPTY &&
                        xt.XtMd == null && (Demo || !xt.cy_zk.isONByChkSen))
                    {
                        // 来料嘴是空的（上次停在“来料已退回料盘”之后，例如点检样品下视觉失败停机）：
                        // 重新取一颗再放，不当作身份不符锁定
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: {1}穴{2}待放新料但{3}为空，重新取料",
                            disc, threeNozzleSource == null ? "" : threeNozzleSource.disc, md.Num, xt.disc));
                        EM_RES again = ThreeNozzlePreparePair(ref quit, pair);
                        if (again != EM_RES.OK) return again;
                        if (!pair.Incoming[slot]) return EM_RES.OK;   // 清料等原因取消了本穴来料
                    }
                    if (pair.DownDeferred[slot] && !pair.DownReady[slot] && pair.Prepared[slot] != null)
                    {
                        // 正常情况下进夹具时已飞拍；万一还没拍（例如中途重进），这里按定拍补上
                        EM_RES deferred = ThreeNozzleResolveDeferredDown(ref quit, pair, false);
                        if (deferred != EM_RES.OK) return deferred;
                    }
                    if (pair.Prepared[slot] != null && Object.ReferenceEquals(xt.XtMd, pair.Prepared[slot]) && !Demo && !xt.cy_zk.isONByChkSen)
                    {
                        EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, true, "放新料前有物料记录但真空未到位");
                        if (corrected != EM_RES.OK) return corrected;
                    }
                    if (md == null || md.res != (int)Product.EM_CM_RES.EMPTY || !pair.DownReady[slot] ||
                        pair.Prepared[slot] == null || !Object.ReferenceEquals(xt.XtMd, pair.Prepared[slot]) ||
                        (!Demo && !xt.cy_zk.isONByChkSen)) return ThreeNozzleError("放新料前身份/真空/空穴不符", true);
                    ThreeNozzleDryRun.NoteLiveAction(id, xt.SlotIndex, "fixture", md.Num,
                        string.Format("{0} 放新料到{1}夹具穴{2}", xt.disc, threeNozzleSource == null ? "" : threeNozzleSource.disc, md.Num));
                    Cam.VisionOutPutData up = new Cam.VisionOutPutData();
                    ST_XYA upOffset;
                    ST_XY upPos;
                    EM_RES placed = ThreeNozzlePrePlaceUpVision(ref quit, pair, slot, xt, md, ref up, out upOffset, out upPos);
                    if (placed != EM_RES.OK) return placed;
                    placed = xt.XtPickOrPlaceMod(ref quit, upPos, upOffset,
                        pair.DownVision[slot].PosMM, md.st_pos[id].z, Demo, XT.EM_XTFLOW.PLACEMOD,
                        false, PT_SET.bModPasteUp, NewSysInf.UserParams.bPlaceWsDis);
                    if (placed != EM_RES.OK) return ThreeNozzleError("放新料结果待核对: " + placed, true);
                    md.res = (int)Product.EM_CM_RES.UNTEST;
                    string placedSampleId = xt.XtMd.ModuleCheckSampleId;
                    md.ModuleCheckSampleId = placedSampleId;
                    if (threeNozzleMode == ThreeNozzleMode.GrrReload || !string.IsNullOrEmpty(placedSampleId))
                    {
                        // GRR / 模组点检样品：沿用物料本身的条码，不做生产扫码绑定
                        // GRR reuses the original batch and its existing codes;
                        // production scan rejection must not replace that batch.
                        md.bardcode = xt.XtMd.bardcode;
                        md.motor_barcode = xt.XtMd.motor_barcode;
                    }
                    else ThreeNozzleBindScanning(xt, md);
                    md.IsNormal = VAR.Isnormal;
                    threeNozzleFixtureMaterials[md] = xt.XtMd;
                    xt.XtMd = null;
                    pair.Prepared[slot] = null;
                    pair.DownReady[slot] = false;
                    pair.DownDeferred[slot] = false;
                    pair.Placed[slot] = true;
                    pair.PlacementRevision++;
                    ModuleCheckHooks.OnSamplePlaced(placedSampleId, md.Num);
                    ModuleCheckClearVisionFail(placedSampleId);
                }
                if (!completePostPlace && countNew && ThreeNozzleDeferPostPlaceCheck(md))
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 放料后回检：穴{1} 已放料，待本组退离飞拍后确认", disc, md.Num));
                    return EM_RES.OK;
                }
                Cam.VisionOutPutData image = postPlaceImage;
                postPlaceImage = null; // A replacement must be checked from a new exposure.
                EM_RES check = threeNozzleMode == ThreeNozzleMode.GrrReload || !string.IsNullOrEmpty(md.ModuleCheckSampleId)
                    ? EM_RES.OK : ThreeNozzleCheckAfterPlace(ref quit, threeNozzleSource, xt, md, image);
                if (check == EM_RES.RETRY || (check == EM_RES.OK && md.res >= 0))
                {
                    // The just-emptied incoming nozzle owns rework. Other mouths
                    // may still hold the old A part and the next incoming part.
                    pair.ReworkStage[slot] = 1;
                    continue;
                }
                if (check != EM_RES.OK) return check;
                ErrTryPickCnt = 0;
                if (pair.CountAsNew[slot] && string.IsNullOrEmpty(md.ModuleCheckSampleId))   // 点检样品不计产量
                {
                    COUNT_DATA.allcnt[threeNozzleSource.num]++;
                    if (PT_SET.bNgControl) COUNT_DATA.ngctrlallcnt++;
                    if (PT_SET.EquipmentMT != 0) COUNT_DATA.CurEquipmentMT++;
                }
                AddAfterCloseTargets(afterCloseTargets, new[] { new AfterCloseTarget(md.Clone(), md.st_pos[id].x, id) });
                pair.Accepted[slot] = true;
                return EM_RES.OK;
            }
            return EM_RES.QUIT;
        }

        public EM_RES ThreeNozzleUnload(ref bool quit, WS ws)
        {
            if (!ThreeNozzleAcquireExecutor(ref quit)) return EM_RES.ABORT;
            try
            {
                EM_RES pendingIssue = ThreeNozzleCheckPendingVacuum(ref quit);
                if (pendingIssue != EM_RES.OK) return pendingIssue;
                if (threeNozzleRecovery) return ThreeNozzleError("上次动作结果待核对，本轮不卸料；停止后再按开始会自动复位对账");
                if (!Object.ReferenceEquals(threeNozzleSource, ws)) return ThreeNozzleError("尾部卸料源工站不匹配", true);
                if (ThreeNozzleHasSpecialMode && !threeNozzleModeDone)
                    return ThreeNozzleError("特殊模式尚未完成，禁止尾部放行");
                if (threeNozzlePairIndex < threeNozzlePairs.Count)
                {
                    var pair = threeNozzlePairs[threeNozzlePairIndex];
                    if (threeNozzlePairIndex != threeNozzlePairs.Count - 1 || !pair.Sequence.OutsideFixture)
                        return ThreeNozzleError("所有组完成并离场前禁止尾部放行");
                    EM_RES result = ThreeNozzleUnloadCore(ref quit);
                    if (result != EM_RES.OK) return result;
                    if (pair.Sequence.Current == ThreeNozzleAction.UnloadFinished) pair.Sequence.Step(a => true);
                }
                EM_RES home = GoZero(ref quit);
                if (home != EM_RES.OK) return home;
                ThreeNozzleResetModesRuntime();
                threeNozzleSource = null;
                threeNozzlePairs.Clear();
                threeNozzlePairIndex = 0;
                threeNozzleDrain = false;
                return EM_RES.OK;
            }
            catch { threeNozzleRecovery = true; throw; }
            finally { Interlocked.Exchange(ref threeNozzleExecutor, 0); }
        }

        /// <summary>
        /// 落料加锁方式。true（默认）：OK 盘、NG 盘各一把锁，两臂可以同时一个落 OK、一个落 NG；
        /// 同一料盘同一时间只给一个臂（料盘 X 要移到该臂位置）。点检样品放回待测盘仍用原来的 placetray。
        /// false：恢复原来的“一个臂把所有旧料落完、另一个臂才能开始”的整批一把锁。
        /// 如需改回，把这里改成 false 重新编译即可。
        /// </summary>
        public static bool ThreeNozzleSplitPlaceTrayLock = true;
        private static readonly object placeTrayOk = new object();
        private static readonly object placeTrayNg = new object();

        private EM_RES ThreeNozzleUnloadCore(ref bool quit, XT only = null)
        {
            CtStepLog.SetContext(disc, "放成品到料仓");
            IEnumerable<XT> unloadOrder = NewSysInf.UserParams.bXtPrePlaceTrayByBigId
                ? list_xt.OrderByDescending(nozzle => nozzle.id) : list_xt.OrderBy(nozzle => nozzle.id);
            List<XT> pending = unloadOrder.Where(xt => only == null || xt == only).ToList();
            foreach (XT xt in pending)
            {
                bool expected = xt.XtMd != null;
                if (!Demo && xt.cy_zk.isONByChkSen != expected)
                {
                    EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, expected,
                        "成品落盘前真空与记录不一致，先处理当前吸嘴");
                    if (corrected != EM_RES.OK) return corrected;
                }
            }
            if (!ThreeNozzleSplitPlaceTrayLock)
            {
                EM_RES taken = ThreeNozzleTakeLock(ref quit, placetray);
                if (taken != EM_RES.OK) return taken;
                try
                {
                    foreach (XT xt in pending)
                    {
                        if (quit) return EM_RES.QUIT;
                        EM_RES one = ThreeNozzleUnloadOne(ref quit, xt);
                        if (one != EM_RES.OK) return one;
                    }
                    return MT.RaiseAllArmZ(ref quit, id);
                }
                finally { Monitor.Exit(placetray); }
            }

            // 按料盘分别加锁：任何时候本臂最多只持有一把料盘锁（换料盘前先放开），两臂不会互相死等
            // 离开夹具时已经预先占住的料盘锁直接接过来用（料盘 X 已经在本臂这边）
            object held = threeNozzlePreHeldTray;
            threeNozzlePreHeldTray = null;
            try
            {
                // 料盘还在预定位时，臂先走到第一颗成品落料位上方（只动臂 X/Y；料盘锁已在手，空位不会被对臂改动）
                var preMove = threeNozzleTrayPreMove;
                if (ThreeNozzleApproachDuringTrayPreMoveEnabled && held != null && preMove != null && !preMove.IsCompleted)
                {
                    XT firstOnHeld = pending.FirstOrDefault(xt => Object.ReferenceEquals(ThreeNozzleUnloadLockFor(xt), held));
                    if (firstOnHeld != null) ThreeNozzleApproachPlaceWhileWaiting(ref quit, firstOnHeld, "正在预定位", true);
                }
                ThreeNozzleWaitTrayPreMove();
                while (pending.Count > 0)
                {
                    if (quit) return EM_RES.QUIT;
                    XT next = null;
                    // 1) 不需要锁的，或需要的正是已持有的锁：直接做（同一料盘连续落，料盘 X 不来回跑）
                    foreach (XT xt in pending)
                    {
                        object need = ThreeNozzleUnloadLockFor(xt);
                        if (need == null || Object.ReferenceEquals(need, held)) { next = xt; break; }
                    }
                    // 2) 其它料盘空闲就先做那一颗（对臂占着 NG 盘时先去落 OK 料）
                    if (next == null)
                    {
                        foreach (XT xt in pending)
                        {
                            object need = ThreeNozzleUnloadLockFor(xt);
                            if (Monitor.TryEnter(need))
                            {
                                if (held != null) Monitor.Exit(held);
                                held = need;
                                next = xt;
                                break;
                            }
                        }
                    }
                    // 3) 都被对臂占着：放开手上的锁，按原顺序排队等第一颗需要的料盘
                    if (next == null)
                    {
                        if (held != null) { Monitor.Exit(held); held = null; }
                        next = pending[0];
                        object need = ThreeNozzleUnloadLockFor(next);
                        // 等锁之前先把臂（只动臂的 X/Y，不动料盘）走到这颗料的落料位上方，
                        // 拿到锁后只剩料盘 X 移过来，不用臂和料盘一起从夹具那边走
                        if (need != null && !Monitor.IsEntered(need)) ThreeNozzleApproachPlaceWhileWaiting(ref quit, next);
                        EM_RES taken = ThreeNozzleTakeLock(ref quit, need);
                        if (taken != EM_RES.OK) return taken;
                        held = need;
                    }
                    pending.Remove(next);
                    EM_RES one = ThreeNozzleUnloadOne(ref quit, next);
                    if (one != EM_RES.OK) return one;
                }
                if (held != null) { Monitor.Exit(held); held = null; }
                return MT.RaiseAllArmZ(ref quit, id);
            }
            finally { if (held != null) Monitor.Exit(held); }
        }

        /// <summary>开关：落料要等对臂让出 OK/NG 料盘时，先把臂走到落料位上方等（默认开）。</summary>
        public static bool ThreeNozzleApproachPlaceWhileWaitingEnabled = true;

        /// <summary>
        /// 对臂占着料盘时：按料盘下一个空位估算本臂落料位，只移动臂的 X/Y 过去等（Z 在安全高度，不动料盘 X）。
        /// 料盘数据此时可能正被对臂改动，这里只是估算位置：拿到锁后 ThreeNozzleUnloadOne 照常按实际空位和料盘补偿走到位。
        /// 估算失败或运动失败都不影响后面的正常落料。
        /// </summary>
        private void ThreeNozzleApproachPlaceWhileWaiting(ref bool quit, XT xt, string why = "正被对臂使用", bool force = false)
        {
            if ((!ThreeNozzleApproachPlaceWhileWaitingEnabled && !force) || quit) return;
            try
            {
                TrayBox tray;
                double x, y;
                if (!ThreeNozzleEstimatePlaceXY(xt, out tray, out x, out y)) return;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} [R2] 三吸嘴: {1}{4}，先到落料位上方等 X={2:F3} Y={3:F3}",
                    disc, tray.disc, x, y, why));
                EM_RES r = MT.ZupMove(ref quit, ref ax_x, x, ref ax_y, y);
                if (r != EM_RES.OK)
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: 先到落料位上方等待未完成（{1}），拿到料盘后照常落料", disc, r));
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 先到落料位上方等待跳过：" + ex.Message);
            }
        }

        /// <summary>
        /// 估算这个吸嘴上的成品落到 OK/NG 料盘下一个空位时臂的 X/Y（与 ThreeNozzleUnloadOne 相同算法，不含料盘视觉补偿，臂 X 按当前位置）。
        /// 只读估算：不持锁时料盘数据可能被对臂改动，拿到锁后 ThreeNozzleUnloadOne 照常按实际空位走到位。
        /// </summary>
        private bool ThreeNozzleEstimatePlaceXY(XT xt, out TrayBox tray, out double x, out double y)
        {
            tray = null; x = y = double.NaN;
            try
            {
                if (xt == null || xt.XtMd == null || IsModuleCheckSample(xt.XtMd) || xt.XtMd.res < 0) return false;
                int res = xt.XtMd.res;
                tray = res == 0 ? traybox_ok : traybox_ng;
                if (tray == null || !tray.IsReady || tray.trayActionRunning || tray.ChgML || tray.tray_cur == null) return false;
                var positions = tray.IsOkTray ? tray.tray_cur.GetPosList(Product.EM_CM_RES.EMPTY) :
                    tray.tray_cur.GetPosList(Product.EM_CM_RES.EMPTY, res);
                if (positions == null || positions.Count == 0) return false;
                ST_XYZA pos = xt.CaliPosAt(positions[0].Pos[id], 0, ax_x.fcmd_pos);
                x = pos.x + xt.st_rol_cap.x - list_xt[0].st_rol_cap.x + xt.st_vs_pos_xtshp.x;
                y = pos.y + xt.st_cap_pos.y - list_xt[0].st_rol_cap.y + xt.st_vs_pos_xtshp.y;
                return FlyFinite(x) && FlyFinite(y);
            }
            catch { return false; }
        }

        /// <summary>本臂接下来第一颗要落盘的成品（与 ThreeNozzleUnloadCore 的顺序相同）落料位的 Y，估算不出返回 NaN。</summary>
        private double ThreeNozzleEstimateFirstUnloadY()
        {
            IEnumerable<XT> order = NewSysInf.UserParams.bXtPrePlaceTrayByBigId
                ? list_xt.OrderByDescending(nozzle => nozzle.id) : list_xt.OrderBy(nozzle => nozzle.id);
            XT first = order.FirstOrDefault(xt => xt.XtMd != null && !IsModuleCheckSample(xt.XtMd) && xt.XtMd.res >= 0);
            TrayBox tray; double x, y;
            return first != null && ThreeNozzleEstimatePlaceXY(first, out tray, out x, out y) ? y : double.NaN;
        }

        /// <summary>
        /// 开关：OK/NG 料盘放满最后一个空位后立即在后台换盘（默认开）。原来要等下一颗料要落时才换，
        /// 18 号日志满盘到开始换盘一般隔 5~12 秒，换盘约 6 秒都压在要落料的那台模块上。
        /// 换盘期间对臂要落这个料盘时，ThreeNozzleEnsureTray 会等 trayActionRunning 结束；后台换盘失败时下一次落料照原流程再换。
        /// </summary>
        public static bool ThreeNozzleEagerTrayChangeEnabled = true;

        private void ThreeNozzleStartEagerTrayChange(TrayBox tray)
        {
            if (!ThreeNozzleEagerTrayChangeEnabled || tray == null || tray.IsFeedTray) return;
            try
            {
                if (tray.trayActionRunning || tray.ChgML || !tray.IsReady || tray.tray_cur == null) return;
                var empty = tray.tray_cur.GetPosList(Product.EM_CM_RES.EMPTY);
                if (empty != null && empty.Count > 0) return;
                tray.trayActionRunning = true;   // 先占住：任务真正开始前，对臂也会看到正在换盘而等待，不会重复换
                bool demo = Demo;
                string actor = disc;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} [R4] 三吸嘴: {1}已放满，立即后台换盘（不等下一颗料要落时再换）", actor, tray.disc));
                System.Threading.Tasks.Task.Run(() =>
                {
                    EM_RES r = EM_RES.ERR;
                    try
                    {
                        CtStepLog.SetContext(tray.disc, "满盘立即换盘");
                        r = tray.Tray_Action(demo);
                        ThreeNozzleInvalidateTrayCompensation(tray);
                    }
                    catch (Exception ex)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} [R4] 三吸嘴: {1}后台换盘异常：{2}", actor, tray.disc, ex.Message));
                    }
                    finally
                    {
                        tray.trayActionRunning = false;   // Tray_Action 自己也会清；异常时这里兜底
                    }
                    VAR.msg.AddMsg(r == EM_RES.OK ? Msg.EM_MSGTYPE.DBG : Msg.EM_MSGTYPE.WAR, string.Format(
                        "{0} [R4] 三吸嘴: {1}后台换盘结束（{2}）{3}", actor, tray.disc, r, r == EM_RES.OK ? "" : "，下一次落料时按原流程再换"));
                });
            }
            catch (Exception ex)
            {
                tray.trayActionRunning = false;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " [R4] 三吸嘴: 满盘后台换盘未启动：" + ex.Message);
            }
        }

        /// <summary>卸这颗料要持有的锁：空嘴 null；点检样品放回待测盘 placetray；成品按结果 OK/NG 盘</summary>
        private object ThreeNozzleUnloadLockFor(XT xt)
        {
            if (xt.XtMd == null) return null;
            if (IsModuleCheckSample(xt.XtMd)) return placetray;
            return xt.XtMd.res == 0 ? placeTrayOk : placeTrayNg;
        }

        /// <summary>卸一个吸嘴上的料（调用方已持有对应的料盘锁）</summary>
        private EM_RES ThreeNozzleUnloadOne(ref bool quit, XT xt)
        {
            EM_RES result;
            if (quit) return EM_RES.QUIT;
            if (xt.XtMd == null)
            {
                if (!Demo && xt.cy_zk.isONByChkSen)
                    return ThreeNozzleRequireVacuumCorrection(ref quit, xt, false, "卸料时真空有料但记录为空，请核对本嘴异常物料");
                return EM_RES.OK;
            }
            if (IsModuleCheckSample(xt.XtMd))
            {
                // 模组点检样品按身份放回点检盘原穴，不进 OK/NG 盘
                ModuleCheckHooks.NoteSampleDiverted(xt.disc, xt.XtMd.ModuleCheckSampleId, xt.XtMd.res);
                result = ModuleCheckReturnSample(ref quit, xt);
                return result;
            }
            if (xt.XtMd.res < 0) return ThreeNozzleError("卸料目标仍是待测料，须核对身份", true);
            if (!Demo && !xt.cy_zk.isONByChkSen)
            {
                EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, true, "成品卸料前真空未到位");
                if (corrected != EM_RES.OK) return corrected;
                if (xt.XtMd == null) return EM_RES.OK;   // 与双吸嘴一致：真空没料就清记录、跳过本嘴
            }
            result = ThreeNozzleCheckFinishedDownBarcode(ref quit, threeNozzleSource, xt);
            if (result != EM_RES.OK) return result;
            TrayBox tray = xt.XtMd.res == 0 ? traybox_ok : traybox_ng;
        RetryTray:
            result = ThreeNozzleEnsureTray(ref quit, tray, xt.XtMd.res);
            if (result != EM_RES.OK) return result;
            var positions = tray.IsOkTray ? tray.tray_cur.GetPosList(Product.EM_CM_RES.EMPTY) :
                tray.tray_cur.GetPosList(Product.EM_CM_RES.EMPTY, xt.XtMd.res);
            var target = positions[0];
            ST_XYZA pos = xt.CaliPos(target.Pos[id]);
            ST_XYA compensation = new ST_XYA();
            if (PT_SET.bEnVsTray)
            {
                Camcfgset(0U);
                result = ThreeNozzleGetTrayCompensation(ref quit, tray, pos, out compensation);
                if (result == EM_RES.END)
                {
                    result = GoZero(ref quit);
                    if (result != EM_RES.OK) return result;
                    tray.IsReady = false;
                    goto RetryTray;
                }
                if (result != EM_RES.OK) return result;
            }
            pos.x += xt.st_rol_cap.x - list_xt[0].st_rol_cap.x + xt.st_vs_pos_xtshp.x - compensation.x;
            pos.y += xt.st_cap_pos.y - list_xt[0].st_rol_cap.y + xt.st_vs_pos_xtshp.y - compensation.y;
            try { pos.z = NozzleZCalibration.ToAxisZ(id, xt.SlotIndex, pos.z, xt.ZApproachDirection); }
            catch (Exception ex) { return ThreeNozzleError("成品落盘Z标定不可用：" + ex.Message); }
            ThreeNozzleDryRun.NoteLiveAction(id, xt.SlotIndex, tray.IsOkTray ? "ok" : "ng", target.idx,
                string.Format("{0} 把结果{1}落到{2}穴{3}", xt.disc, xt.XtMd.res, tray.disc, target.idx));
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} {1} 落{2} 穴{3}：目标 X={4:F3} Y={5:F3} Z={6:F3} U={7:F3}，{8}X 目标={9:F3}，料盘补偿 X={10:F3} Y={11:F3}，结果={12}",
                disc, xt.disc, tray.disc, target.idx, pos.x, pos.y, pos.z, PT_SET.isopen_degree ? -PT_SET.degree : 0,
                tray.disc, pos.a, compensation.x, compensation.y, xt.XtMd.res));
            result = MT.ZupMove(ref quit, ref ax_x, pos.x, ref ax_y, pos.y, ref xt.ax_u,
                PT_SET.isopen_degree ? -PT_SET.degree : 0, ref tray.ax_x, pos.a);
            if (result != EM_RES.OK) return result;
            pos.a = PT_SET.isopen_degree ? -PT_SET.degree : 0;
            result = xt.Place(ref quit, pos, true, Demo);
            if (result != EM_RES.OK) return ThreeNozzleError("成品落盘结果待核对: " + result, true);
            tray.tray_cur.Push(xt.XtMd, target.idx);
            threeNozzleFinishedBarcodeChecks.Remove(xt.XtMd);
            threeNozzlePendingBarcodeWarnings.Remove(xt.XtMd);
            xt.XtMd = null;
            ThreeNozzleStartEagerTrayChange(tray);
            return EM_RES.OK;
        }

        // The caller holds picktray/placetray, so two arms cannot start the same
        // change. Tray_Action retains existing sensor checks and manual full-box
        // confirmation. Its finally publishes IsReady only on actual completion.
        private EM_RES ThreeNozzleEnsureTray(ref bool quit, TrayBox tray, int resultCode)
        {
            while (!quit)
            {
                while (tray.trayActionRunning || tray.ChgML)
                {
                    if (quit) return EM_RES.QUIT;
                    EM_RES yieldResult = ProcessPositioningYield(ref quit);
                    if (yieldResult != EM_RES.OK) return yieldResult;
                    Thread.Sleep(20);
                }
                if (tray.IsReady && tray.tray_cur != null)
                {
                    var available = tray.IsFeedTray ? tray.tray_cur.GetPosList() :
                        tray.IsOkTray ? tray.tray_cur.GetPosList(Product.EM_CM_RES.EMPTY) :
                        tray.tray_cur.GetPosList(Product.EM_CM_RES.EMPTY, resultCode);
                    if (available != null && available.Count > 0) return EM_RES.OK;
                }
                if (tray.IsFeedTray)
                {
                    // 借用待测盘做点检盘：换盘会把点检区的样品一起带走
                    EM_RES guard = ModuleCheckGuardFeedTraySwap(ref quit);
                    if (guard == EM_RES.RETRY) continue;
                    if (guard != EM_RES.OK) return guard;
                }
                EM_RES result = GoZero(ref quit);
                if (result != EM_RES.OK) return result;
                result = tray.Tray_Action(Demo);
                if (result != EM_RES.OK) return result;
                ThreeNozzleInvalidateTrayCompensation(tray);
                if (!tray.IsReady || tray.tray_cur == null) return ThreeNozzleError("换盘完成状态无效");
            }
            return EM_RES.QUIT;
        }
    }
}
