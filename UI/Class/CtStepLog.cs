using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// CT 分解日志（参照 WLT-MINI 上下料时序表）：自动运行时记录每一个动作的耗时。
    ///   ..\log\UpDownTiming\yyyy-MM-dd_CT分解明细.csv：原始记录，每个动作一行（执行方、动作、明细、上下文、周期内开始时刻、耗时）
    ///   ..\log\UpDownTiming\yyyy-MM-dd_CT分解按轮.csv：每个周期结束时整理一次——
    ///       每条臂按“轮”（一次进夹具换两颗：进夹具飞拍→取成品/放待测×2→离开夹具→放成品到料仓→取待测，
    ///       到下一次进夹具飞拍前为止）列出按顺序合并后的步骤和本轮合计；
    ///       工站动作（盖板、工位旋转、转盘）单独列；最后给出周期时长、放料颗数、CT(s/颗)、UPH。
    /// 记录来源：
    ///   1. 所有经过 MT.Move 的轴运动（臂 X/Y/U、各吸嘴 Z、供料/料仓轴等），按轴自动归类为
    ///      “X/Y移动”“Z下降”“Z上升”“供料X移动”等；
    ///   2. 工站动作：打开盖板(AllCyUp)、关闭盖板(AllCyDown)、工位旋转(TurnToFeed/Test/Pos2)、转盘旋转等；
    ///   3. 同一执行方两次动作之间超过 20ms 的空档记为“等待/其他”（真空、视觉、料盘锁、避障等待、计算等）。
    /// 上下文（取成品 穴5、取待测 料盘穴34 …）由三吸嘴流程在各步骤开始时设置。
    /// 周期：工站到上下料面（UpDownTimingLog.MarkArrive）开始一个新周期，到下一次到位为止，
    /// 所以本周期末尾为下一工站做的“提前预取”和转盘旋转也算在本周期里。
    /// 计时用 Stopwatch（DateTime.Now 只有约 15.6ms 分辨率）。只在设备自动运行（RUN）时记录；记录失败不影响流程。
    /// </summary>
    public static class CtStepLog
    {
        /// <summary>总开关（默认开）。</summary>
        public static bool Enabled = true;
        /// <summary>两次动作之间超过此值（秒）记一行“等待/其他”。</summary>
        public static double GapThreshold = 0.02;

        private const string WaitAction = "等待/其他";

        [ThreadStatic] private static string threadActor;
        [ThreadStatic] private static string threadContext;
        [ThreadStatic] private static int moveDepth;

        // 高精度时钟：程序启动时刻 + Stopwatch 经过时间
        private static readonly DateTime clockBase = DateTime.Now;
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static DateTime Now { get { return clockBase + clock.Elapsed; } }

        private static readonly object sync = new object();
        private static int cycle;
        private static string cycleStation = "";
        private static DateTime cycleStart = DateTime.MinValue;

        private sealed class ActorState
        {
            public int Seq;
            public DateTime LastEnd = DateTime.MinValue;
        }

        private sealed class Rec
        {
            public string Actor, Action, Detail, Context;
            public DateTime Start, End;
            public double Sec { get { return (End - Start).TotalSeconds; } }
        }

        private static readonly Dictionary<string, ActorState> actors = new Dictionary<string, ActorState>();
        private static readonly List<Rec> cycleRecs = new List<Rec>();

        private static readonly ConcurrentQueue<KeyValuePair<bool, string>> queue = new ConcurrentQueue<KeyValuePair<bool, string>>();
        private static readonly AutoResetEvent queueSignal = new AutoResetEvent(false);
        private static Thread writer;

        public sealed class MoveToken
        {
            internal bool Nested;
            internal DateTime Start;
            internal string Actor;
            internal string Action;
            internal string Detail;
            internal string Context;
        }

        private sealed class StepScope : IDisposable
        {
            private readonly string actor, action, detail, context;
            private readonly DateTime start;
            internal StepScope(string actor, string action, string detail)
            {
                this.actor = actor; this.action = action; this.detail = detail;
                context = threadContext ?? "";
                start = Now;
            }
            public void Dispose()
            {
                Record(actor, action, detail, context, start, Now, false);
            }
        }

        private static bool Active
        {
            get
            {
                try { return Enabled && VAR.gsys_set != null && VAR.gsys_set.status == EM_SYS_STA.RUN; }
                catch { return false; }
            }
        }

        /// <summary>当前线程的上下文（用于临时改上下文后恢复）。</summary>
        public static string CurrentContext { get { return threadContext; } }

        /// <summary>流程设置当前线程的执行方（如“上下料模块1”）和上下文（如“取成品 穴5”）。</summary>
        public static void SetContext(string actor, string context)
        {
            threadActor = actor;
            threadContext = context;
        }

        /// <summary>工站/转盘等非轴运动动作：using (CtStepLog.Step("工站1", "打开盖板")) { ... }。不在运行中时返回 null。</summary>
        public static IDisposable Step(string actor, string action, string detail = "")
        {
            if (!Active) return null;
            return new StepScope(actor, action, detail);
        }

        /// <summary>工站到上下料面：结束上一周期（写按轮分解），开始新周期。</summary>
        public static void NewCycle(string station)
        {
            try
            {
                lock (sync)
                {
                    DateTime now = Now;
                    if (cycleStart != DateTime.MinValue && cycleRecs.Count > 0) WriteRoundReportLocked(now);
                    cycle++;
                    cycleStation = station ?? "";
                    cycleStart = now;
                    actors.Clear();
                    cycleRecs.Clear();
                }
            }
            catch { }
        }

        #region 轴运动（由 MT.Move 调用）
        public static MoveToken BeginMove(AXIS[] axes, double[] targets)
        {
            if (!Active) return null;
            try
            {
                moveDepth++;
                var token = new MoveToken { Start = Now, Context = threadContext ?? "" };
                if (moveDepth > 1) { token.Nested = true; return token; }
                Classify(axes, targets, token);
                return token;
            }
            catch { return null; }
        }

        public static void EndMove(MoveToken token)
        {
            if (token == null) return;
            try
            {
                moveDepth = Math.Max(0, moveDepth - 1);
                if (token.Nested || token.Action == null) return;
                DateTime end = Now;
                if ((end - token.Start).TotalSeconds < 0.003) return;   // 原地不动、立即返回的不记
                Record(token.Actor, token.Action, token.Detail, token.Context, token.Start, end, true);
            }
            catch { }
        }

        private static void Classify(AXIS[] axes, double[] targets, MoveToken token)
        {
            string action = null;
            int arm = -1;
            bool anyMoving = false;
            var moved = new List<string>();
            for (int i = 0; i < axes.Length; i++)
            {
                AXIS axis = axes[i];
                if (axis == null) continue;
                double cur = axis.fcmd_pos;
                double target = targets[i];
                bool moving = Math.Abs(target - cur) > 0.001;
                if (moving)
                {
                    anyMoving = true;
                    moved.Add(string.Format("{0}:{1:F2}→{2:F2}", axis.disc, cur, target));
                }
                int lateral = MT.GetLateralArmId(axis);
                if (lateral >= 0)
                {
                    arm = lateral;
                    if (action == null || action.StartsWith("Z")) action = "X/Y移动";
                    continue;
                }
                int zArm = ZArmOf(axis);
                if (zArm >= 0)
                {
                    arm = zArm;
                    if (action == null && moving) action = Math.Abs(target) > Math.Abs(cur) ? "Z下降" : "Z上升";
                    continue;
                }
                if (action == null && moving) action = axis.disc + "移动";
            }
            // 没有任何轴位移（原地确认到位、避障预约等）：耗时算作等待
            if (!anyMoving) action = WaitAction;
            token.Action = action;
            token.Detail = string.Join(" ", moved.ToArray());
            token.Actor = arm == 0 ? "上下料模块1" : arm == 1 ? "上下料模块2" : (threadActor ?? "公共");
        }

        private static int ZArmOf(AXIS axis)
        {
            for (int arm = 0; arm < 2; arm++)
                foreach (AXIS z in MT.GetArmZAxes(arm))
                    if (z != null && z.m_id == axis.m_id) return arm;
            return -1;
        }
        #endregion

        private static void Record(string actor, string action, string detail, string context, DateTime start, DateTime end, bool fillGap)
        {
            try
            {
                lock (sync)
                {
                    if (cycleStart == DateTime.MinValue) { cycleStart = start; cycle = Math.Max(cycle, 1); }
                    actor = string.IsNullOrEmpty(actor) ? "公共" : actor;
                    ActorState st;
                    if (!actors.TryGetValue(actor, out st)) { st = new ActorState(); actors[actor] = st; }
                    // 只给两条臂补“等待/其他”（料盘、转盘等的空闲时间不算动作）
                    if (fillGap && IsArm(actor) && st.LastEnd != DateTime.MinValue && st.LastEnd >= cycleStart)
                    {
                        double gap = (start - st.LastEnd).TotalSeconds;
                        if (gap > GapThreshold) AppendLocked(st, actor, WaitAction, "", context, st.LastEnd, start);
                    }
                    AppendLocked(st, actor, action, detail, context, start, end);
                    if (end > st.LastEnd) st.LastEnd = end;
                }
            }
            catch { }
        }

        private static void AppendLocked(ActorState st, string actor, string action, string detail, string context, DateTime start, DateTime end)
        {
            st.Seq++;
            cycleRecs.Add(new Rec { Actor = actor, Action = action, Detail = detail, Context = context, Start = start, End = end });
            Enqueue(false, string.Format("{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3},{4},{5},{6},{7},{8:F3},{9:F3}",
                start, cycle, Csv(cycleStation), Csv(actor), st.Seq, Csv(action), Csv(detail), Csv(context),
                (start - cycleStart).TotalSeconds, (end - start).TotalSeconds));
        }

        #region 按轮整理
        private static bool IsArm(string actor) { return actor == "上下料模块1" || actor == "上下料模块2"; }
        private static bool IsRoundStart(string context) { return context != null && context.StartsWith("进夹具飞拍"); }
        private static bool IsPlaceIntoFixture(string context) { return context != null && context.StartsWith("放待测"); }

        private sealed class Merged
        {
            public string Name;
            public DateTime Start;
            public double Sec;
        }

        private static void WriteRoundReportLocked(DateTime now)
        {
            double cycleSec = (now - cycleStart).TotalSeconds;
            var placed = new HashSet<string>();
            var armTotals = new List<KeyValuePair<string, double>>();

            foreach (string arm in new[] { "上下料模块1", "上下料模块2" })
            {
                var recs = cycleRecs.FindAll(r => r.Actor == arm);
                if (recs.Count == 0) continue;
                recs.Sort((a, b) => a.Start.CompareTo(b.Start));

                // 分轮：进夹具飞拍开始新一轮；第一次进夹具之前的记为第 0 轮（开始前）
                var rounds = new List<List<Rec>>();
                List<Rec> cur = new List<Rec>();
                string lastContext = null;
                foreach (Rec r in recs)
                {
                    if (IsRoundStart(r.Context) && !IsRoundStart(lastContext) && (cur.Count > 0 || rounds.Count > 0))
                    {
                        rounds.Add(cur);
                        cur = new List<Rec>();
                    }
                    cur.Add(r);
                    lastContext = r.Context;
                    if (IsPlaceIntoFixture(r.Context) && (r.Action == "Z下降")) placed.Add(arm + "|" + r.Context);
                }
                if (cur.Count > 0) rounds.Add(cur);
                bool firstIsPre = rounds.Count > 0 && rounds[0].Count > 0 && !IsRoundStart(rounds[0][0].Context);

                double armSum = 0;
                for (int i = 0; i < rounds.Count; i++)
                {
                    int roundNo = firstIsPre ? i : i + 1;
                    string roundName = roundNo == 0 ? "开始前" : "第" + roundNo + "轮";
                    string pockets = RoundPockets(rounds[i]);
                    if (pockets.Length > 0) roundName += "(" + pockets + ")";

                    List<Merged> steps = MergeSteps(rounds[i]);
                    double roundSum = 0;
                    int seq = 0;
                    foreach (Merged m in steps)
                    {
                        seq++;
                        roundSum += m.Sec;
                        EnqueueRound(arm, roundName, seq.ToString(), m.Name, m.Sec, (m.Start - cycleStart).TotalSeconds);
                    }
                    EnqueueRound(arm, roundName, "", "【本轮合计】", roundSum, double.NaN);
                    armSum += roundSum;
                }
                EnqueueRound(arm, "", "", "【上下料合计】", armSum, double.NaN);
                armTotals.Add(new KeyValuePair<string, double>(arm, armSum));
            }

            // 工站动作（盖板、旋转、转盘、启动测试等），按时间顺序
            var others = cycleRecs.FindAll(r => !IsArm(r.Actor));
            others.Sort((a, b) => a.Start.CompareTo(b.Start));
            int oseq = 0;
            foreach (Rec r in others)
            {
                oseq++;
                string name = string.IsNullOrEmpty(r.Detail) || r.Detail.Contains("→") ? r.Action : r.Action + "(" + r.Detail + ")";
                EnqueueRound(r.Actor, "工站动作", oseq.ToString(), name, r.Sec, (r.Start - cycleStart).TotalSeconds);
            }

            // 两臂谁先做完：等待另一侧
            if (armTotals.Count == 2)
            {
                double diff = Math.Abs(armTotals[0].Value - armTotals[1].Value);
                string faster = armTotals[0].Value <= armTotals[1].Value ? armTotals[0].Key : armTotals[1].Key;
                EnqueueRound(faster, "", "", "【两臂合计差（快的一侧等另一侧）】", diff, double.NaN);
            }

            int pieces = placed.Count;
            EnqueueRound("周期", "", "", "【周期时长】", cycleSec, double.NaN);
            EnqueueRound("周期", "", "", "【放料颗数】", pieces, double.NaN);
            if (pieces > 0)
            {
                double ct = cycleSec / pieces;
                EnqueueRound("周期", "", "", "【CT(s/颗)】", ct, double.NaN);
                EnqueueRound("周期", "", "", "【UPH】", Math.Round(3600.0 / ct), double.NaN);
            }
        }

        /// <summary>短于此值（秒）的“等待/其他”并入下一步（和看视频分解一样，每步时间首尾相接）；更长的单独列一行。</summary>
        public static double ShortWaitFold = 0.2;

        /// <summary>
        /// 同一上下文、同一动作的连续记录合并成一步，名称“上下文：动作”。
        /// 短等待并入下一步，所以各步时间首尾相接、本轮合计等于本轮实际用时。
        /// </summary>
        private static List<Merged> MergeSteps(List<Rec> recs)
        {
            var list = new List<Merged>();
            Merged last = null;
            string lastKey = null;
            double pendingWait = 0;
            DateTime pendingStart = DateTime.MinValue;
            foreach (Rec r in recs)
            {
                if (r.Action == WaitAction && r.Sec < ShortWaitFold)
                {
                    if (pendingWait == 0) pendingStart = r.Start;
                    pendingWait += r.Sec;
                    continue;
                }
                string ctx = string.IsNullOrEmpty(r.Context) ? "" : r.Context + "：";
                string key = r.Context + "\u0001" + r.Action;
                if (last != null && key == lastKey)
                {
                    last.Sec += pendingWait + r.Sec;
                }
                else
                {
                    last = new Merged
                    {
                        Name = ctx + r.Action,
                        Start = pendingWait > 0 ? pendingStart : r.Start,
                        Sec = pendingWait + r.Sec,
                    };
                    list.Add(last);
                    lastKey = key;
                }
                pendingWait = 0;
            }
            if (pendingWait > 0)
            {
                if (last != null) last.Sec += pendingWait;
                else list.Add(new Merged { Name = WaitAction, Start = pendingStart, Sec = pendingWait });
            }
            return list;
        }

        private static string RoundPockets(List<Rec> recs)
        {
            foreach (Rec r in recs)
            {
                if (!IsRoundStart(r.Context)) continue;
                int a = r.Context.IndexOf('('), b = r.Context.IndexOf(')');
                if (a >= 0 && b > a) return r.Context.Substring(a + 1, b - a - 1);
            }
            return "";
        }

        private static void EnqueueRound(string actor, string round, string seq, string name, double sec, double startInCycle)
        {
            Enqueue(true, string.Format("{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3},{4},{5},{6},{7},{8}",
                cycleStart, cycle, Csv(cycleStation), Csv(actor), Csv(round), seq, Csv(name),
                name == "【放料颗数】" || name == "【UPH】" ? sec.ToString("F0") : sec.ToString("F3"),
                double.IsNaN(startInCycle) ? "" : startInCycle.ToString("F3")));
        }
        #endregion

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace(",", "；").Replace("\r", " ").Replace("\n", " ");
        }

        #region 后台写文件
        private static void Enqueue(bool round, string line)
        {
            queue.Enqueue(new KeyValuePair<bool, string>(round, line));
            EnsureWriter();
            queueSignal.Set();
        }

        private static void EnsureWriter()
        {
            if (writer != null) return;
            lock (queue)
            {
                if (writer != null) return;
                writer = new Thread(WriterLoop) { IsBackground = true, Name = "CtStepLogWriter" };
                writer.Start();
            }
        }

        private static void WriterLoop()
        {
            while (true)
            {
                queueSignal.WaitOne(1000);
                try
                {
                    if (queue.IsEmpty) continue;
                    var detail = new StringBuilder();
                    var round = new StringBuilder();
                    KeyValuePair<bool, string> item;
                    while (queue.TryDequeue(out item))
                        (item.Key ? round : detail).Append(item.Value).Append("\r\n");
                    string dir = UpDownTimingLog.Dir;
                    Directory.CreateDirectory(dir);
                    string day = DateTime.Now.ToString("yyyy-MM-dd");
                    if (detail.Length > 0)
                        AppendFile(Path.Combine(dir, day + "_CT分解明细v2.csv"),
                            "时间,周期,工站,执行方,序号,动作,明细,上下文,周期内开始(s),耗时(s)", detail.ToString());
                    if (round.Length > 0)
                        AppendFile(Path.Combine(dir, day + "_CT分解按轮.csv"),
                            "周期开始,周期,工站,执行方,轮次,序号,动作,耗时(s),周期内开始(s)", round.ToString());
                }
                catch
                {
                    // 记录失败不影响流程
                }
            }
        }

        private static void AppendFile(string path, string header, string body)
        {
            bool isNew = !File.Exists(path);
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                if (isNew)
                {
                    byte[] bom = Encoding.UTF8.GetPreamble();
                    fs.Write(bom, 0, bom.Length);
                    byte[] h = new UTF8Encoding(false).GetBytes(header + "\r\n");
                    fs.Write(h, 0, h.Length);
                }
                byte[] data = new UTF8Encoding(false).GetBytes(body);
                fs.Write(data, 0, data.Length);
            }
        }
        #endregion
    }
}
