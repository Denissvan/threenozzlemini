using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// 三吸嘴空跑参数。空跑只做运动轨迹：不开真空、不触发相机、不读写物料/料盘状态。
    /// </summary>
    public sealed class ThreeNozzleDryRunOptions
    {
        /// <summary>COM.list_ws 下标，取该工站已示教的穴位坐标。</summary>
        public int StationIndex;
        /// <summary>0=仅臂1，1=仅臂2，2=臂1跑完再跑臂2，3=双臂同时（协同，靠双臂X避障互相等待）。</summary>
        public int ArmMode;
        /// <summary>false=按双臂常规分配（臂1:5-8/13-16，臂2:1-4/9-12）；true=所选臂跑全部穴位。</summary>
        public bool AllPockets;
        public int Cycles = 1;
        /// <summary>Z 下探在配方实际轴坐标上方预留的距离(mm)，距离为正。</summary>
        public double ZMarginMm = 5.0;
        public int DwellMs = 300;
        public bool ZDescend = true;
        /// <summary>
        /// 保留旧调用兼容；当前所有取放均使用实际轴坐标，此标志不再改变坐标换算。
        /// N2/N3 与 N1 的长度差不会被修正，只能配合足够的预留距离使用；只影响空跑，不影响生产。
        /// </summary>
        public bool IgnoreZCalibration;
        /// <summary>
        /// 固定下探：不看示教高度，也不用Z测高补偿，三个吸嘴每次都从 Z=0 向下走 ZMarginMm 再回升。
        /// 用于没有夹具、料盘、物料时只看动作顺序。
        /// </summary>
        public bool FixedDescent;
        /// <summary>场景：上料/交换（按模拟夹具状态决定每穴是只放还是先取后放）或只下料（清料）。</summary>
        public DryRunScene Scene = DryRunScene.LoadOrExchange;
        /// <summary>经过上/下相机拍照位（只走位，不拍照）。</summary>
        public bool CameraPass;
        public bool ReturnToStandby = true;
        /// <summary>
        /// 预取提前：模拟“上一工站卸完 → 转盘转动期间先取下一工站第一组新料”。
        /// 每轮先在料盘区取好第一组新料（N2/N3，勾“经过相机位”时含下相机位）并回安全位等待，
        /// 之后才算“工站到位”开始计工站占用时间。预取阶段每个目标 Y 都必须 ≤ SafeTrayMaxY，
        /// 超出直接报错中止（用来验证料盘安全区）。只用于上料/交换场景。
        /// </summary>
        public bool PrefetchAhead;

        public ThreeNozzleDryRunOptions Clone() { return (ThreeNozzleDryRunOptions)MemberwiseClone(); }
    }

    /// <summary>
    /// 适用于仅有上下料部分（无转盘、无物料）的机台：按三吸嘴交换顺序空跑
    /// 供料盘取料 → 夹具取旧料/放新料 → OK盘落料 的动作，并统计每步耗时。
    /// 运动在 NozzleZCalibrationMotion.RunExclusive 内执行，期间其他线程的手动臂运动被拒绝，
    /// 停止按钮或系统 bquit 会立即停下已登记的轴。
    /// </summary>
    public enum DryRunScene
    {
        /// <summary>穴上有料=取旧放新（交换，用 N1/N2/N3），穴上没料=只放新料（首次上料，用 N2/N3）。</summary>
        LoadOrExchange = 0,
        /// <summary>只取不放：穴上有料就取出落 OK 盘（A 穴 N1、B 穴 N2），没料跳过。</summary>
        UnloadOnly = 1,
    }

    public static class ThreeNozzleDryRun
    {
        private const double UnsetLimit = 1e6;
        private static int running;
        private static int feedCursor, okCursor;
        // 双臂同时运行时，供料盘/OK盘（含料盘X轴）同一时间只给一个臂用，与正式流程的 picktray/placetray 一致。
        private static readonly object feedTrayLock = new object(), okTrayLock = new object();

        private static bool ArmSelected(ThreeNozzleDryRunOptions o, int arm)
        {
            return o.ArmMode == arm || o.ArmMode == 2 || o.ArmMode == 3;
        }

        public static event Action<string> Log;
        /// <summary>模拟显示更新（在空跑线程触发，界面需要 BeginInvoke）。</summary>
        public static event Action<ThreeNozzleSimSnapshot> SimChanged;
        private static ThreeNozzleDryRunSim sim;
        private static string simKey;

        private static string SimKey(ThreeNozzleDryRunOptions o)
        {
            return o.StationIndex + "|" + o.ArmMode + "|" + o.AllPockets;
        }

        /// <summary>
        /// 模拟状态在窗口打开期间一直保留：上一次空跑放进夹具的料，下一次还在，不会每次都重来。
        /// 只有换了工站/臂/穴位范围，或者点“清空/装满模拟夹具”时才重建。
        /// </summary>
        private static ThreeNozzleDryRunSim GetOrCreateSim(ThreeNozzleDryRunOptions o)
        {
            string key = SimKey(o);
            if (sim == null || simKey != key)
            {
                sim = CreateSim(o);
                simKey = key;
            }
            return sim;
        }

        /// <summary>重置模拟夹具（运行中不允许）。full=装满旧料，false=清空。</summary>
        public static ThreeNozzleSimSnapshot ResetSimFixture(ThreeNozzleDryRunOptions o, bool full)
        {
            if (IsRunning) return null;
            try
            {
                ThreeNozzleDryRunSim s = GetOrCreateSim(o);
                s.ResetFixture(full);
                return s.Snapshot();
            }
            catch { return null; }
        }

        private static ThreeNozzleDryRunSim CreateSim(ThreeNozzleDryRunOptions o)
        {
            WS ws = COM.list_ws[o.StationIndex];
            bool[] selected = { ArmSelected(o, 0), ArmSelected(o, 1) };
            return new ThreeNozzleDryRunSim(ws, COM.tray_fd, COM.tray_ok, selected,
                arm => BuildPairs(arm == 0 ? COM.UDLoad1 : COM.UDLoad2, ws, o.AllPockets)
                    .SelectMany(p => p.Where(m => m != null).Select(m => m.Num)));
        }

        /// <summary>不运动，只按当前选项生成初始模拟画面（夹具哪些穴参与、料盘大小）。失败返回 null。</summary>
        public static ThreeNozzleSimSnapshot Preview(ThreeNozzleDryRunOptions o)
        {
            try
            {
                if (o == null || COM.list_ws == null || o.StationIndex < 0 || o.StationIndex >= COM.list_ws.Count || COM.list_ws[o.StationIndex] == null)
                    return null;
                return GetOrCreateSim(o).Snapshot();
            }
            catch { return null; }
        }

        private static void Publish()
        {
            Action<ThreeNozzleSimSnapshot> handler = SimChanged;
            ThreeNozzleDryRunSim current = sim;
            if (handler == null || current == null) return;
            try { handler(current.Snapshot()); } catch { }
        }

        private static void SimStep(int arm, int nozzle, string kind, int target, string text)
        {
            if (sim == null) return;
            sim.SetActive(arm, nozzle, kind, target, text);
            Publish();
        }
        public static bool IsRunning { get { return Volatile.Read(ref running) != 0; } }

        public static void Stop()
        {
            if (!IsRunning) return;
            Emit("收到停止指令，正在停止轴运动…");
            NozzleZCalibrationMotion.Cancel();
        }

        /// <summary>在后台线程启动空跑。返回 false 时 error 给出原因（不做任何运动）。</summary>
        public static bool Start(ThreeNozzleDryRunOptions options, Action<bool, string> finished, out string error)
        {
            error = PreCheck(options);
            if (error != null) return false;
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) { error = "空跑已在运行"; return false; }
            ThreeNozzleDryRunOptions opt = options.Clone();
            var thread = new Thread(() =>
            {
                bool ok = false;
                string summary;
                try
                {
                    summary = null;
                    NozzleZCalibrationMotion.RunExclusive(() => { summary = Execute(opt); });
                    ok = true;
                }
                catch (OperationCanceledException) { summary = "空跑已停止"; }
                catch (Exception ex) { summary = "空跑中止：" + ex.Message; }
                finally { Interlocked.Exchange(ref running, 0); }
                try
                {
                    VAR.msg.AddMsg(ok ? Msg.EM_MSGTYPE.SYS : Msg.EM_MSGTYPE.ERR, "[三吸嘴空跑] " + summary);
                }
                catch { }
                Emit(summary);
                if (finished != null) finished(ok, summary);
            });
            thread.IsBackground = true;
            thread.Name = "ThreeNozzleDryRun";
            thread.Start();
            return true;
        }

        #region 预检
        public static string PreCheck(ThreeNozzleDryRunOptions o)
        {
            if (o == null) return "参数为空";
            if (IsRunning) return "空跑已在运行";
            if (SafetyDebugMode.AutoRunActive) return "系统自动运行中（含暂停），不能启动窗口空跑；查看系统运行的物料请勾选「跟随系统运行(实时)」";
            try { NozzleZCalibrationMotion.CheckIdle(); }
            catch (Exception ex) { return ex.Message; }
            if (NozzleZCalibrationMotion.IsBusy) return "Z 标定动作正在执行";
            if (PT_SET.bUdMovSafe) return "请先关闭「上下料移动安全检查(UD_MOV_SAFE)」：该检查依赖转盘，无转盘机台会出错";
            if (!ThreeNozzleHardwareConfig.IsConfigured) return "三吸嘴硬件配置未加载(ThreeNozzleHardware.ini)";
            if (o.ArmMode == 3)
            {
                if (o.AllPockets) return "双臂同时运行必须用「按双臂分配」穴位，否则两臂会抢同一个穴";
                if (MT.SingleArmDebugArm >= 0) return "单臂调试中不能双臂同时运行，请先选「臂1→臂2 依次」做两臂一起回零";
                if (!(MT.EffectiveDualArmCombinedExtentLimit > 0)) return "双臂X避障上限为0（检查双臂安全距离/最大合计外伸参数），无法双臂同时运行";
            }
            if (o.ArmMode == 0 || o.ArmMode == 1)
            {
                int other = 1 - o.ArmMode;
                AXIS otherX = other == 0 ? MT.AXIS_UDL1_X : MT.AXIS_UDL2_X;
                if (otherX.home_status != AXIS.HOME_STA.OK && MT.SingleArmDebugArm != o.ArmMode)
                    return string.Format("上下料模块{0} 未回零。要单独调上下料模块{1}，请先在本窗口选「仅臂{1}」后点「仅上下料回零」，确认后进入单臂调试", other + 1, o.ArmMode + 1);
            }
            if (o.Cycles < 1 || o.Cycles > 1000) return "循环次数应为 1-1000";
            if (o.PrefetchAhead)
            {
                if (o.Scene != DryRunScene.LoadOrExchange) return "预取提前只用于「上料/交换」场景";
                foreach (UpDownLoad arm in SelectedArms(o))
                {
                    if (arm == null) continue;
                    double safeY = ThreeNozzleHardwareConfig.SafeTrayMaxY(arm.id);
                    if (double.IsNaN(safeY))
                        return arm.disc + " 料盘安全区 SafeTrayMaxY 未配置且无法自动计算（请先标定三个吸嘴的下相机拍照位）";
                    double standbyY = arm.list_xt != null && arm.list_xt.Count > 1 ? arm.list_xt[1].st_cap_pos.y : double.NaN;
                    if (!Finite(standbyY) || standbyY > safeY + 1e-6)
                        return string.Format("{0} 安全位 Y={1:0.000} 超出 SafeTrayMaxY={2:0.000}，请检查 ThreeNozzleHardware.ini", arm.disc, standbyY, safeY);
                }
            }
            if (o.DwellMs < 0 || o.DwellMs > 10000) return "停留时间应为 0-10000 ms";
            if (double.IsNaN(o.ZMarginMm) || o.ZMarginMm < 0.5 || o.ZMarginMm > 50) return "Z 预留距离应为 0.5-50 mm";
            if (COM.list_ws == null || o.StationIndex < 0 || o.StationIndex >= COM.list_ws.Count || COM.list_ws[o.StationIndex] == null)
                return "工站选择无效";
            if (COM.tray_fd == null || COM.tray_fd.list_pos == null || COM.tray_fd.list_pos.Count(p => !p.isDisable) == 0) return "供料盘模板无可用穴位";
            if (COM.tray_ok == null || COM.tray_ok.list_pos == null || COM.tray_ok.list_pos.Count(p => !p.isDisable) == 0) return "OK 料盘模板无可用穴位";

            foreach (UpDownLoad ud in SelectedArms(o))
            {
                string armError = CheckArm(ud);
                if (armError != null) return armError;
                List<WS.MdDat[]> pairs = BuildPairs(ud, COM.list_ws[o.StationIndex], o.AllPockets);
                if (pairs.Count == 0) return ud.disc + " 在所选范围内没有启用的穴位";
                foreach (WS.MdDat[] pair in pairs)
                    foreach (WS.MdDat md in pair)
                    {
                        if (md == null) continue;
                        if (!IsTaught(md.st_pos[ud.id]) || !IsTaught(md.st_pickpos[ud.id]))
                            return string.Format("{0} 穴位{1} 的放料/取料坐标未示教", ud.disc, md.Num);
                    }
            }
            foreach (TrayBox box in new[] { COM.traybox_fd, COM.traybox_ok })
                if (box == null || !AxisReady(box.ax_x)) return (box == null ? "料仓" : box.disc) + " 料盘X轴未就绪/未回零";
            return null;
        }

        private static string CheckArm(UpDownLoad ud)
        {
            if (ud == null) return "上下料模块不存在";
            if (ud.list_xt == null || ud.list_xt.Count < 3) return ud.disc + " 吸嘴配置不足 3 个";
            var axes = new List<AXIS> { ud.ax_x, ud.ax_y };
            axes.AddRange(MT.GetArmZAxes(ud.id));
            for (int i = 0; i < 3; i++) axes.Add(ud.list_xt[i].ax_u);
            foreach (AXIS ax in axes)
                if (!AxisReady(ax)) return ud.disc + " 轴 " + (ax == null ? "?" : ax.disc) + " 未就绪/未回零/未停止";
            return null;
        }

        private static bool AxisReady(AXIS ax)
        {
            return ax != null && ax.card != null && ax.card.isReady && ax.home_status == AXIS.HOME_STA.OK && ax.isStop;
        }

        private static bool IsTaught(ST_XYZ p)
        {
            return Finite(p.x) && Finite(p.y) && Finite(p.z) && Math.Abs(p.x) < UnsetLimit && Math.Abs(p.y) < UnsetLimit && Math.Abs(p.z) < UnsetLimit;
        }

        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        #endregion

        #region 执行
        private static IEnumerable<UpDownLoad> SelectedArms(ThreeNozzleDryRunOptions o)
        {
            if (ArmSelected(o, 0)) yield return COM.UDLoad1;
            if (ArmSelected(o, 1)) yield return COM.UDLoad2;
        }

        /// <summary>与 ThreeNozzleEnsurePlan 相同的遍历顺序与 (k, k+8) 分组；只看 benable，不看物料结果。</summary>
        private static List<WS.MdDat[]> BuildPairs(UpDownLoad ud, WS ws, bool allPockets)
        {
            UpDownLoad.EM_FIN_MOD mode = allPockets ? UpDownLoad.EM_FIN_MOD.ALL
                : ud.id == 0 ? UpDownLoad.EM_FIN_MOD.HIGH : UpDownLoad.EM_FIN_MOD.LOW;
            var groups = new Dictionary<int, WS.MdDat[]>();
            var ordered = new List<WS.MdDat[]>();
            for (int i = 0; i < ws.list_md.Count; i++)
            {
                int n = i % 2 == 0 ? i - i / 2 : i + (ws.list_md.Count - 1 - i) / 2;
                if ((mode == UpDownLoad.EM_FIN_MOD.LOW && n % 8 >= 4) || (mode == UpDownLoad.EM_FIN_MOD.HIGH && n % 8 < 4)) continue;
                WS.MdDat md = ws.list_md[n];
                if (md == null || !md.benable) continue;
                WS.MdDat[] pair;
                if (!groups.TryGetValue(md.Num % 8, out pair))
                {
                    pair = new WS.MdDat[2];
                    groups.Add(md.Num % 8, pair);
                    ordered.Add(pair);
                }
                pair[md.Num > 8 ? 1 : 0] = md;
            }
            return ordered;
        }

        private sealed class Ctx
        {
            public ThreeNozzleDryRunOptions Opt;
            public UpDownLoad Ud;
            public WS Ws;
            public readonly Dictionary<string, List<long>> Steps = new Dictionary<string, List<long>>();
            /// <summary>预取阶段：只允许在料盘区活动（Y ≤ SafeY）。</summary>
            public bool TrayOnly;
            public double SafeY = double.NaN;
            /// <summary>双臂同时 + 预取提前时，两臂都预取完才算“工站到位”。</summary>
            public PhaseGate Gate;
            public void Record(string step, long ms)
            {
                List<long> list;
                if (!Steps.TryGetValue(step, out list)) Steps.Add(step, list = new List<long>());
                list.Add(ms);
            }
        }

        /// <summary>两臂会合点：双臂同时运行时，两臂都预取完才一起进入“工站到位后换料”。可跨轮复用。</summary>
        private sealed class PhaseGate
        {
            private readonly object sync = new object();
            private readonly int parties;
            private int arrived, generation;
            public PhaseGate(int parties) { this.parties = parties; }
            public void Arrive()
            {
                lock (sync)
                {
                    int gen = generation;
                    if (++arrived >= parties)
                    {
                        arrived = 0;
                        generation++;
                        Monitor.PulseAll(sync);
                        return;
                    }
                    while (gen == generation)
                    {
                        if (VAR.gsys_set.bquit) { arrived = Math.Max(0, arrived - 1); throw new OperationCanceledException(); }
                        Monitor.Wait(sync, 50);
                    }
                }
            }
        }

        private static string Execute(ThreeNozzleDryRunOptions opt)
        {
            var all = new List<AXIS>();
            foreach (UpDownLoad ud in new[] { COM.UDLoad1, COM.UDLoad2 })
            {
                if (ud == null) continue;
                all.Add(ud.ax_x); all.Add(ud.ax_y);
                all.AddRange(MT.GetArmZAxes(ud.id));
                foreach (XT xt in ud.list_xt.Take(3)) all.Add(xt.ax_u);
            }
            all.Add(COM.traybox_fd.ax_x);
            all.Add(COM.traybox_ok.ax_x);
            NozzleZCalibrationMotion.RegisterAxes(all.Where(a => a != null).Distinct().ToArray());

            feedCursor = 0; okCursor = 0;
            GetOrCreateSim(opt);
            Publish();
            WS ws = COM.list_ws[opt.StationIndex];
            Emit(string.Format("开始空跑：{0}，{1}，循环 {2} 次，Z{3}，停留 {4} ms{5}", ws.disc,
                opt.ArmMode == 0 ? "臂1" : opt.ArmMode == 1 ? "臂2" : opt.ArmMode == 2 ? "臂1→臂2" : "双臂同时",
                opt.Cycles, opt.ZDescend ? "下探至工艺高度上方 " + opt.ZMarginMm.ToString("0.0") + " mm" : "不下探",
                opt.DwellMs, opt.CameraPass ? "，经过相机位" : ""));
            Emit(opt.Scene == DryRunScene.UnloadOnly ? "场景：只下料（清料），有料的穴取出落OK盘"
                : "场景：上料/交换，穴上有料=取旧放新，没料=只放新料");
            if (opt.ZDescend && opt.FixedDescent)
                Emit(string.Format("Z固定下探：每次从Z=0向下 {0:0.0} mm 后回升，不使用示教高度和测高补偿", opt.ZMarginMm));
            else if (opt.ZDescend)
            {
                Emit("Z使用配方实际轴坐标，沿下降反方向预留距离，不使用测高补偿");
            }
            if (opt.PrefetchAhead)
                Emit("预取提前：每轮先在料盘区取好第一组新料并回安全位（模拟转盘转动期间），再开始计“工站占用”；预取阶段 Y 超出 SafeTrayMaxY 会报错中止");
            PhaseGate gate = opt.ArmMode == 3 && opt.PrefetchAhead ? new PhaseGate(2) : null;
            var total = Stopwatch.StartNew();
            var summary = new StringBuilder();
            if (opt.ArmMode == 3)
                Emit(string.Format("双臂同时：两臂各跑自己的穴位，X 方向按双臂避障（合计外伸上限 {0:0.0} mm）互相等待；供料盘/OK盘同一时间只给一个臂用",
                    MT.EffectiveDualArmCombinedExtentLimit));
            for (int cycle = 1; cycle <= opt.Cycles; cycle++)
            {
                int cyc = cycle;
                if (opt.ArmMode == 3)
                    NozzleZCalibrationMotion.RunParallel(
                        () => RunArmCycle(opt, COM.UDLoad1, ws, cyc, summary, gate),
                        () => RunArmCycle(opt, COM.UDLoad2, ws, cyc, summary, gate));
                else
                    foreach (UpDownLoad ud in SelectedArms(opt))
                        RunArmCycle(opt, ud, ws, cyc, summary, null);
            }
            Emit(summary.ToString().TrimEnd());
            string done = string.Format("空跑完成，共 {0} 轮，总用时 {1:0.00} s", opt.Cycles, total.ElapsedMilliseconds / 1000.0);
            if (sim != null) { sim.Finish(done); Publish(); }
            return done;
        }

        private static void RunArmCycle(ThreeNozzleDryRunOptions opt, UpDownLoad ud, WS ws, int cycle, StringBuilder summary, PhaseGate gate)
        {
            var ctx = new Ctx { Opt = opt, Ud = ud, Ws = ws, Gate = gate };
            if (sim != null) { sim.BeginArmCycle(ud.id, cycle); Publish(); }
            var armWatch = Stopwatch.StartNew();
            List<WS.MdDat[]> pairs = BuildPairs(ud, ws, opt.AllPockets);
            bool prefetch = opt.PrefetchAhead && opt.Scene == DryRunScene.LoadOrExchange && pairs.Count > 0;
            long prefetchMs = 0;
            if (prefetch)
            {
                var prefetchWatch = Stopwatch.StartNew();
                PrefetchFirstPair(ctx, pairs[0]);
                prefetchMs = prefetchWatch.ElapsedMilliseconds;
                Emit(string.Format("第{0}轮 {1} 预取第一组[{2}] 用时 {3:0.00} s（转盘转动期间完成，不计工站占用）", cycle, ud.disc,
                    string.Join("/", pairs[0].Where(m => m != null).Select(m => m.Num.ToString())), prefetchMs / 1000.0));
                if (gate != null)
                {
                    Emit(ud.disc + " 预取完成，等另一臂预取完成（模拟工站到位）");
                    gate.Arrive();
                }
            }
            // 工站占用：工站到位 → 本臂最后一穴放完。最后一组取出的旧料落盘单独计时，
            // 正式流程中它由尾部卸料任务完成，可与合夹具/转盘转动并行（两种模式都这样统计，便于对比）。
            var occupyWatch = Stopwatch.StartNew();
            var tailDrops = new List<XT>();
            int pairNo = 0;
            foreach (WS.MdDat[] pair in pairs)
            {
                pairNo++;
                var pairWatch = Stopwatch.StartNew();
                RunPair(ctx, pair, prefetch && pairNo == 1, pairNo == pairs.Count ? tailDrops : null);
                Emit(string.Format("第{0}轮 {1} 组{2}[{3}] 用时 {4:0.00} s", cycle, ud.disc, pairNo,
                    string.Join("/", pair.Where(m => m != null).Select(m => m.Num.ToString())), pairWatch.ElapsedMilliseconds / 1000.0));
            }
            long occupyMs = occupyWatch.ElapsedMilliseconds;
            var tailWatch = Stopwatch.StartNew();
            foreach (XT outgoing in tailDrops)
            {
                XT xt = outgoing;
                Timed(ctx, xt.disc + " 尾部OK盘落料", () => OkTrayDrop(ctx, xt));
            }
            long tailMs = tailWatch.ElapsedMilliseconds;
            Require(MT.RaiseAllArmZ(ref VAR.gsys_set.bquit, ud.id), ud.disc + " 抬Z");
            for (int slot = 0; slot < 3; slot++)
            {
                AXIS u = ud.list_xt[slot].ax_u;
                Require(MT.Move(ref VAR.gsys_set.bquit, ref u, 0), ud.disc + " U回零位");
            }
            // 臂1→臂2 依次 / 双臂同时：做完的臂必须退回待机位让出 X 空间。
            // 否则停在外伸位置的臂不会再动，另一臂的双臂避障会一直等到 30s 超时，看起来就是“跑着跑着停住”。
            if (opt.ReturnToStandby || opt.ArmMode >= 2)
                Require(MT.ZupMove(ref VAR.gsys_set.bquit, ref ud.ax_x, 0, ref ud.ax_y, 0), ud.disc + " 回待机位");
            Emit(string.Format("第{0}轮 {1} 完成 {2} 组，用时 {3:0.00} s；{4}工站占用(到位→最后一穴放完) {5:0.00} s，尾部落盘(可与合夹具/转盘并行) {6:0.00} s",
                cycle, ud.disc, pairs.Count, armWatch.ElapsedMilliseconds / 1000.0,
                prefetch ? string.Format("预取 {0:0.00} s(转盘转动期间)，", prefetchMs / 1000.0) : "",
                occupyMs / 1000.0, tailMs / 1000.0));
            if (cycle == opt.Cycles) lock (summary) summary.AppendLine(StepReport(ctx));
        }

        private static bool PocketHasPart(WS.MdDat md)
        {
            ThreeNozzleDryRunSim current = sim;
            return current == null || current.HasPart(md.Num);
        }

        /// <summary>
        /// 一组 (k, k+8)，吸嘴分配与正式三吸嘴流程一致，每个穴按模拟夹具上有没有料决定动作：
        /// 上料/交换：
        ///   - 穴上有料（交换）：A 穴 N1 取旧、N2 放新；B 穴 N2 取旧、N3 放新；取出的旧料落 OK 盘。
        ///   - 穴上没料（首次上料）：只放新料，A 穴 N2 放、B 穴 N3 放，N1 不动。
        ///   顺序：N2/N3 先从供料盘取新料 → A 穴(取)放 → B 穴(取)放 → 旧料落盘。
        /// 只下料：A 穴 N1 取、B 穴 N2 取，然后落 OK 盘；没料的穴跳过。
        /// </summary>
        /// <summary>
        /// 预取提前阶段：N2/N3 从供料盘取第一组新料（勾“经过相机位”时再走下相机位），然后回安全位
        /// (X=0, Y=吸嘴2下相机拍照位) 等待工站到位。期间每个目标 Y 都检查不超过 SafeTrayMaxY。
        /// </summary>
        private static void PrefetchFirstPair(Ctx c, WS.MdDat[] pair)
        {
            UpDownLoad ud = c.Ud;
            c.SafeY = ThreeNozzleHardwareConfig.SafeTrayMaxY(ud.id);
            c.TrayOnly = true;
            try
            {
                Emit(string.Format("{0} 预取提前：只在料盘区活动，Y ≤ SafeTrayMaxY={1:0.000}", ud.disc, c.SafeY));
                for (int slot = 0; slot < 2; slot++)
                {
                    if (pair[slot] == null) continue;
                    XT incoming = ud.list_xt[slot + 1];
                    WS.MdDat md = pair[slot];
                    Timed(c, incoming.disc + " 预取:供料盘取料", () => FeedPick(c, incoming, md));
                }
                if (c.Opt.CameraPass)
                    for (int slot = 0; slot < 2; slot++)
                    {
                        if (pair[slot] == null) continue;
                        XT incoming = ud.list_xt[slot + 1];
                        Timed(c, incoming.disc + " 预取:下相机位", () => DownCameraPass(c, incoming));
                    }
                double standbyY = ud.list_xt[1].st_cap_pos.y;
                Timed(c, "预取:回安全位等待", () =>
                {
                    CheckTrayOnlyY(c, standbyY, "安全位");
                    Require(MT.ZupMove(ref VAR.gsys_set.bquit, ref ud.ax_x, 0, ref ud.ax_y, standbyY), ud.disc + " 回安全位");
                });
            }
            finally { c.TrayOnly = false; }
        }

        private static void CheckTrayOnlyY(Ctx c, double y, string what)
        {
            if (!c.TrayOnly) return;
            if (!Finite(y) || y > c.SafeY + 1e-6)
                throw new InvalidOperationException(string.Format(
                    "{0} 预取阶段 {1} 目标 Y={2:0.000} 超出料盘安全区 SafeTrayMaxY={3:0.000}：正式流程提前预取时会进入夹具区，已中止",
                    c.Ud.disc, what, y, c.SafeY));
        }

        private static void RunPair(Ctx c, WS.MdDat[] pair, bool prefetched, List<XT> deferredDrops)
        {
            UpDownLoad ud = c.Ud;
            var slots = new List<int>();
            for (int slot = 0; slot < 2; slot++) if (pair[slot] != null) slots.Add(slot);
            var hasPart = new bool[2];
            foreach (int slot in slots) hasPart[slot] = PocketHasPart(pair[slot]);

            if (c.Opt.Scene == DryRunScene.UnloadOnly)
            {
                var picked = slots.Where(slot => hasPart[slot]).ToList();
                if (picked.Count == 0)
                {
                    Emit(string.Format("{0} 穴{1} 模拟夹具无料，跳过下料", ud.disc,
                        string.Join("/", slots.Select(slot => pair[slot].Num.ToString()))));
                    return;
                }
                foreach (int slot in picked)
                {
                    WS.MdDat md = pair[slot];
                    XT outgoing = ud.list_xt[slot];
                    Timed(c, outgoing.disc + " 夹具取料(穴" + md.Num + ")", () => FixturePick(c, outgoing, md));
                }
                foreach (int slot in picked)
                {
                    XT outgoing = ud.list_xt[slot];
                    if (deferredDrops != null) { deferredDrops.Add(outgoing); continue; }
                    Timed(c, outgoing.disc + " OK盘落料", () => OkTrayDrop(c, outgoing));
                }
                return;
            }

            if (!prefetched)
                foreach (int slot in slots)
                {
                    XT incoming = ud.list_xt[slot + 1];
                    Timed(c, incoming.disc + " 供料盘取料", () => FeedPick(c, incoming, pair[slot]));
                }
            if (c.Opt.CameraPass && !prefetched)
                foreach (int slot in slots)
                {
                    XT incoming = ud.list_xt[slot + 1];
                    Timed(c, incoming.disc + " 下相机位", () => DownCameraPass(c, incoming));
                }

            foreach (int slot in slots)
            {
                WS.MdDat md = pair[slot];
                XT outgoing = ud.list_xt[slot];
                XT incoming = ud.list_xt[slot + 1];
                if (hasPart[slot])
                    Timed(c, outgoing.disc + " 夹具取料(穴" + md.Num + ")", () => FixturePick(c, outgoing, md));
                if (c.Opt.CameraPass)
                    Timed(c, incoming.disc + " 上相机看穴位", () => FixtureUpCameraPass(c, incoming, md));
                Timed(c, incoming.disc + (hasPart[slot] ? " 夹具放料(穴" : " 首次上料(穴") + md.Num + ")", () => FixturePlace(c, incoming, md));
            }
            foreach (int slot in slots)
            {
                if (!hasPart[slot]) continue;
                XT outgoing = ud.list_xt[slot];
                if (deferredDrops != null) { deferredDrops.Add(outgoing); continue; }
                Timed(c, outgoing.disc + " OK盘落料", () => OkTrayDrop(c, outgoing));
            }
        }

        private static void FeedPick(Ctx c, XT xt, WS.MdDat md)
        {
            WithTrayLock(feedTrayLock, c, "供料盘", () => FeedPickLocked(c, xt, md));
        }

        /// <summary>
        /// 双臂同时运行时进入料盘前的互斥。拿不到料盘（另一臂在用）时，本臂先把 X 退回 0 让位再等，
        /// 避免“我占着夹具外伸位置等料盘，对方占着料盘等我让 X”互相等待到避障超时。
        /// </summary>
        private static void WithTrayLock(object trayLock, Ctx c, string trayName, Action body)
        {
            if (!Monitor.TryEnter(trayLock))
            {
                UpDownLoad ud = c.Ud;
                if (c.Opt.ArmMode == 3 && Math.Abs(ud.ax_x.fenc_pos) > 0.5)
                {
                    Emit(string.Format("{0} 等{1}（另一臂在用），先退回 X=0 让位", ud.disc, trayName));
                    Require(MT.ZupMove(ref VAR.gsys_set.bquit, ref ud.ax_x, 0), ud.disc + " 让位退回X0");
                }
                var wait = Stopwatch.StartNew();
                while (!Monitor.TryEnter(trayLock, 50))
                {
                    if (VAR.gsys_set.bquit) throw new OperationCanceledException();
                    if (wait.ElapsedMilliseconds > 120000)
                        throw new TimeoutException(string.Format("{0} 等{1}超过 120s，另一臂可能已停止", ud.disc, trayName));
                }
            }
            try { body(); }
            finally { Monitor.Exit(trayLock); }
        }

        private static void FeedPickLocked(Ctx c, XT xt, WS.MdDat md)
        {
            UpDownLoad ud = c.Ud;
            Product.Tray.PosInf source = NextPos(COM.tray_fd, ref feedCursor);
            SimStep(ud.id, xt.SlotIndex, "feed", source.idx, string.Format("{0} {1} 去供料盘穴{2}取新料", ud.disc, xt.disc, source.idx));
            ST_XYZA cam = xt.CaliPos(source.Pos[ud.id], md.st_pos[ud.id].x);
            if (c.Opt.CameraPass)
                Move4(c, xt, cam.x, cam.y, 0, COM.traybox_fd.ax_x, cam.a, "供料上相机位");
            ST_XY nozzle = xt.XtCalPos(cam.ToXY());
            Move4(c, xt, nozzle.x, nozzle.y, 0, COM.traybox_fd.ax_x, cam.a, "供料穴" + source.idx);
            ZTouch(c, xt, source.Pos[ud.id].z);
            if (sim != null) { sim.FeedPick(ud.id, xt.SlotIndex, source.idx); Publish(); }
        }

        private static void DownCameraPass(Ctx c, XT xt)
        {
            CheckTrayOnlyY(c, xt.st_cap_pos.y, xt.disc + " 下相机位");
            Require(MT.ZupMove(ref VAR.gsys_set.bquit, ref c.Ud.ax_y, xt.st_cap_pos.y), xt.disc + " 下相机Y");
            Dwell(c);
        }

        private static void FixturePick(Ctx c, XT xt, WS.MdDat md)
        {
            UpDownLoad ud = c.Ud;
            SimStep(ud.id, xt.SlotIndex, "fixture", md.Num, string.Format("{0} {1} 去夹具穴{2}取旧料", ud.disc, xt.disc, md.Num));
            ST_XYZA p = md.st_pickpos[ud.id].ToXYZA() + xt.st_rol_cap.ToXYZA() -
                ud.list_xt[0].st_rol_cap.ToXYZA() + xt.st_vs_pos_xtshp.ToXY().ToXYZA();
            Move4(c, xt, p.x, p.y, PT_SET.isopendown_degree ? PT_SET.downdegree : 0, null, 0, "夹具取料位");
            ZTouch(c, xt, md.st_pickpos[ud.id].z);
            if (sim != null) { sim.FixturePick(ud.id, xt.SlotIndex, md.Num); Publish(); }
        }

        private static void FixtureUpCameraPass(Ctx c, XT xt, WS.MdDat md)
        {
            Move4(c, xt, md.st_pos[c.Ud.id].x, md.st_pos[c.Ud.id].y, 0, null, 0, "穴位上相机位");
            Dwell(c);
        }

        private static void FixturePlace(Ctx c, XT xt, WS.MdDat md)
        {
            SimStep(c.Ud.id, xt.SlotIndex, "fixture", md.Num, string.Format("{0} {1} 把新料放入夹具穴{2}", c.Ud.disc, xt.disc, md.Num));
            ST_XY p = xt.XtCalPos(md.st_pos[c.Ud.id].ToXY());
            Move4(c, xt, p.x, p.y, 0, null, 0, "夹具放料位");
            ZTouch(c, xt, md.st_pos[c.Ud.id].z);
            if (sim != null) { sim.FixturePlace(c.Ud.id, xt.SlotIndex, md.Num); Publish(); }
        }

        private static void OkTrayDrop(Ctx c, XT xt)
        {
            WithTrayLock(okTrayLock, c, "OK盘", () => OkTrayDropLocked(c, xt));
        }

        private static void OkTrayDropLocked(Ctx c, XT xt)
        {
            UpDownLoad ud = c.Ud;
            Product.Tray.PosInf target = NextPos(COM.tray_ok, ref okCursor);
            SimStep(ud.id, xt.SlotIndex, "ok", target.idx, string.Format("{0} {1} 把旧料落到OK盘穴{2}", ud.disc, xt.disc, target.idx));
            ST_XYZA pos = xt.CaliPos(target.Pos[ud.id]);
            double processZ = pos.z;
            pos.x += xt.st_rol_cap.x - ud.list_xt[0].st_rol_cap.x + xt.st_vs_pos_xtshp.x;
            pos.y += xt.st_cap_pos.y - ud.list_xt[0].st_rol_cap.y + xt.st_vs_pos_xtshp.y;
            Move4(c, xt, pos.x, pos.y, PT_SET.isopen_degree ? -PT_SET.degree : 0, COM.traybox_ok.ax_x, pos.a, "OK盘穴" + target.idx);
            ZTouch(c, xt, processZ);
            if (sim != null) { sim.OkDrop(ud.id, xt.SlotIndex, target.idx); Publish(); }
        }

        /// <summary>先抬起本臂三根Z，再定位 X/Y/本吸嘴U/(料盘X)。软限位越界时不动作直接中止。</summary>
        private static void Move4(Ctx c, XT xt, double x, double y, double u, AXIS trayX, double trayPos, string what)
        {
            UpDownLoad ud = c.Ud;
            CheckTrayOnlyY(c, y, what);
            CheckTarget(ud.ax_x, x, what); CheckTarget(ud.ax_y, y, what); CheckTarget(xt.ax_u, u, what);
            EM_RES r;
            if (trayX != null)
            {
                CheckTarget(trayX, trayPos, what);
                r = MT.ZupMove(ref VAR.gsys_set.bquit, ref ud.ax_x, x, ref ud.ax_y, y, ref xt.ax_u, u, ref trayX, trayPos);
            }
            else
                r = MT.ZupMove(ref VAR.gsys_set.bquit, ref ud.ax_x, x, ref ud.ax_y, y, ref xt.ax_u, u);
            Require(r, xt.disc + " " + what);
        }

        /// <summary>Z 下探到 (实际轴坐标 − 下降方向 × 预留距离)，停留后回到 0。</summary>
        private static void ZTouch(Ctx c, XT xt, double processZ)
        {
            if (!c.Opt.ZDescend) { Dwell(c); return; }
            if (c.Opt.FixedDescent)
            {
                double fixedAxisZ = xt.ZApproachDirection * c.Opt.ZMarginMm;
                CheckTarget(xt.ax_z, fixedAxisZ, "Z固定下探");
                AXIS fz = xt.ax_z;
                Require(MT.Move(ref VAR.gsys_set.bquit, ref fz, fixedAxisZ), xt.disc + " Z固定下探");
                Dwell(c);
                Require(MT.Move(ref VAR.gsys_set.bquit, ref fz, 0), xt.disc + " Z回升");
                return;
            }
            double axisZ = NozzleZCalibration.RetractAxisZ(processZ, xt.ZApproachDirection, c.Opt.ZMarginMm);
            if (xt.ZApproachDirection * axisZ <= 0)
            {
                Emit(string.Format("{0} 预留后的轴坐标 {1:0.000} 不在下探方向，跳过下探", xt.disc, axisZ));
                Dwell(c);
                return;
            }
            CheckTarget(xt.ax_z, axisZ, "Z下探");
            AXIS z = xt.ax_z;
            Require(MT.Move(ref VAR.gsys_set.bquit, ref z, axisZ), xt.disc + " Z下探");
            Dwell(c);
            Require(MT.Move(ref VAR.gsys_set.bquit, ref z, 0), xt.disc + " Z回升");
        }

        private static void Dwell(Ctx c)
        {
            int left = c.Opt.DwellMs;
            while (left > 0)
            {
                if (VAR.gsys_set.bquit) throw new OperationCanceledException();
                int step = Math.Min(20, left);
                Thread.Sleep(step);
                left -= step;
            }
        }

        private static Product.Tray.PosInf NextPos(Product.Tray tray, ref int cursor)
        {
            var usable = tray.list_pos.Where(p => !p.isDisable).ToList();
            Product.Tray.PosInf pos = usable[cursor % usable.Count];
            cursor++;
            return pos;
        }

        private static void Timed(Ctx c, string step, Action action)
        {
            if (VAR.gsys_set.bquit) throw new OperationCanceledException();
            var sw = Stopwatch.StartNew();
            action();
            c.Record(step.Contains("(穴") ? step.Substring(0, step.IndexOf("(穴")) : step, sw.ElapsedMilliseconds);
            try { VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("[三吸嘴空跑] {0} {1} {2} ms", c.Ud.disc, step, sw.ElapsedMilliseconds)); }
            catch { }
        }

        private static string StepReport(Ctx c)
        {
            var sb = new StringBuilder();
            sb.AppendLine(c.Ud.disc + " 步骤耗时（平均 / 最大, ms）：");
            foreach (var kv in c.Steps)
                sb.AppendLine(string.Format("  {0,-22} {1,7:0} / {2,7}", kv.Key, kv.Value.Average(), kv.Value.Max()));
            return sb.ToString();
        }

        private static void CheckTarget(AXIS ax, double target, string what)
        {
            if (ax == null) throw new InvalidOperationException(what + "：轴未配置");
            if (!Finite(target) || target < ax.sln || target > ax.slp)
                throw new InvalidOperationException(string.Format("{0}：{1} 目标 {2:0.000} 超出软限位 [{3:0.###}, {4:0.###}]", what, ax.disc, target, ax.sln, ax.slp));
        }

        private static void Require(EM_RES r, string what)
        {
            if (r == EM_RES.OK) return;
            if (r == EM_RES.QUIT || VAR.gsys_set.bquit) throw new OperationCanceledException();
            throw new InvalidOperationException(what + " 失败：" + r);
        }

        private static void Emit(string text)
        {
            Action<string> handler = Log;
            if (handler == null || string.IsNullOrEmpty(text)) return;
            try { handler(DateTime.Now.ToString("HH:mm:ss.fff ") + text); } catch { }
        }
        #endregion

        #region 系统运行实时显示（系统页无料空跑 / 正常运行，只读真实物料记录）
        private sealed class LiveAction
        {
            public int Nozzle = -1, Target = -1, Tick;
            public string Kind = "", Text = "";
        }
        private static readonly object liveSync = new object();
        private static readonly LiveAction[] liveActions = { new LiveAction(), new LiveAction() };
        /// <summary>动作开始后多久内算“当前动作”（高亮吸嘴和目标穴）。</summary>
        private const int LiveActiveMs = 6000;

        /// <summary>
        /// 三吸嘴正式流程每个取放动作开始时调用，只记录给实时显示用，不影响流程。
        /// kind: "feed"=供料盘, "fixture"=夹具穴, "ok"/"ng"=OK/NG盘。
        /// </summary>
        public static void NoteLiveAction(int arm, int nozzle, string kind, int target, string text)
        {
            if (arm < 0 || arm > 1) return;
            lock (liveSync)
            {
                LiveAction a = liveActions[arm];
                a.Nozzle = nozzle; a.Kind = kind ?? ""; a.Target = target; a.Text = text ?? "";
                a.Tick = Environment.TickCount;
            }
        }

        /// <summary>
        /// 按程序里真实的物料记录生成一帧显示：两臂 6 个吸嘴的持料（XtMd）、当前在上料位工站的夹具、
        /// 供料盘/OK盘/NG盘当前盘。只读，不改任何数据。
        /// </summary>
        public static ThreeNozzleSimSnapshot BuildLiveSnapshot()
        {
            var s = new ThreeNozzleSimSnapshot { Live = true };
            UpDownLoad[] uds = { COM.UDLoad1, COM.UDLoad2 };
            for (int arm = 0; arm < 2; arm++)
            {
                UpDownLoad ud = uds[arm];
                s.ArmSelected[arm] = ud != null;
                if (ud != null && !string.IsNullOrEmpty(ud.disc)) s.ArmNames[arm] = ud.disc;
                for (int n = 0; n < 3; n++) s.Nozzles[arm][n] = new SimSlot();
                if (ud == null || ud.list_xt == null) continue;
                foreach (XT xt in ud.list_xt.ToArray())
                {
                    if (xt == null || xt.SlotIndex < 0 || xt.SlotIndex > 2) continue;
                    bool early = false;
                    try { early = ud.ThreeNozzleIsEarlyHeldForView(xt); } catch { }
                    s.Nozzles[arm][xt.SlotIndex] = LiveSlotFromRes(xt.XtMd == null ? (int?)null : xt.XtMd.res, early ? "(提前)" : "");
                }
            }

            WS ws = null;
            if (COM.list_ws != null)
                foreach (WS w in COM.list_ws)
                    if (w != null && Turntable.GetWSSta(w.num) == Turntable.EM_STA.POS0) { ws = w; break; }
            s.StationName = ws == null ? "（上料位无工站/转盘位置未知）" : ws.disc + "（上料位）";
            int count = ws == null || ws.list_md == null ? 0 : ws.list_md.Count;
            s.Fixture = new SimSlot[count];
            s.FixtureEnabled = new bool[count];
            s.FixtureArm = new int[count];
            for (int i = 0; i < count; i++) { s.Fixture[i] = new SimSlot(); s.FixtureArm[i] = -1; }
            if (ws != null)
                foreach (WS.MdDat md in ws.list_md.ToArray())
                {
                    if (md == null || md.Num < 1 || md.Num > count) continue;
                    s.Fixture[md.Num - 1] = LiveSlotFromRes(md.res, "");
                    s.FixtureEnabled[md.Num - 1] = md.benable;
                    s.FixtureArm[md.Num - 1] = md.benable ? 2 : -1;
                }

            FillLiveTray(s.Feed, COM.traybox_fd, out s.FeedRows, out s.FeedCols);
            FillLiveTray(s.Ok, COM.traybox_ok, out s.OkRows, out s.OkCols);
            FillLiveTray(s.Ng, COM.traybox_ng, out s.NgRows, out s.NgCols);

            int now = Environment.TickCount;
            lock (liveSync)
            {
                for (int arm = 0; arm < 2; arm++)
                {
                    LiveAction a = liveActions[arm];
                    bool fresh = a.Text.Length > 0 && unchecked(now - a.Tick) >= 0 && unchecked(now - a.Tick) < LiveActiveMs;
                    s.ActiveNozzles[arm] = fresh ? a.Nozzle : -1;
                    s.ActiveKinds[arm] = fresh ? a.Kind : "";
                    s.ActiveTargets[arm] = fresh ? a.Target : -1;
                    s.ArmSteps[arm] = a.Text.Length == 0 ? "待机" : (fresh ? a.Text : "上一步：" + a.Text);
                }
            }
            s.StepText = "系统运行实时";
            return s;
        }

        /// <summary>null=无料；-2 空；-1 待测（新料）；0 OK；其余 NG/异常码。</summary>
        private static SimSlot LiveSlotFromRes(int? res, string suffix)
        {
            if (res == null || res.Value == (int)Product.EM_CM_RES.EMPTY) return new SimSlot();
            int r = res.Value;
            if (r == (int)Product.EM_CM_RES.UNTEST) return new SimSlot { Kind = SimPart.New, Label = "待测" + suffix };
            if (r == 0) return new SimSlot { Kind = SimPart.Old, Label = "OK" + suffix };
            return new SimSlot { Kind = SimPart.Ng, Label = "NG" + r + suffix };
        }

        private static void FillLiveTray(List<SimTrayCell> cells, TrayBox box, out int rows, out int cols)
        {
            rows = cols = 0;
            Product.Tray tray = box == null ? null : box.tray_cur;
            if (tray == null || tray.list_pos == null) return;
            foreach (Product.Tray.PosInf p in tray.list_pos.ToArray())
            {
                if (p == null) continue;
                var cell = new SimTrayCell { Idx = p.idx, Row = p.Row, Col = p.Col, Disabled = p.isDisable || p.isDisableTemp };
                cell.Content = LiveSlotFromRes(p.md == null ? (int?)null : p.md.res, "");
                if (cell.Content.Kind == SimPart.New) cell.Content.Label = "新";
                cells.Add(cell);
                rows = Math.Max(rows, p.Row + 1);
                cols = Math.Max(cols, p.Col + 1);
            }
            if (rows == 0 || cols == 0)
            {
                rows = Math.Max(1, tray.row);
                cols = Math.Max(1, tray.col);
            }
        }
        #endregion
    }
}
