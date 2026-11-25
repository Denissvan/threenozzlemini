using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// [R32] 只做诊断：一次多轴联动期间，后台每 4ms 读一次各轴指令/编码器位置，
    /// 记录每根轴开始动、指令走完、编码器到位（0.05mm 内）的时间，结束后打一行日志。
    /// 不发任何运动/IO 指令；读位置异常时直接放弃本次诊断，不影响运动结果。
    /// </summary>
    internal sealed class ThreeNozzleMoveDiag
    {
        public static bool Enabled = true;
        private const double CmdTol = 0.01, EncTol = 0.05, MovedTol = 0.005;

        internal sealed class Item
        {
            public readonly string Name;
            public readonly AXIS Axis;
            public readonly double Target;
            public double StartCmd = double.NaN, LastEnc = double.NaN;
            public long StartMs = -1, CmdDoneMs = -1, EncDoneMs = -1;
            public Item(string name, AXIS axis, double target) { Name = name; Axis = axis; Target = target; }
        }

        private readonly Item[] items;
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private volatile bool stop;
        private Thread thread;
        private volatile bool failed;

        private ThreeNozzleMoveDiag(Item[] items) { this.items = items; }

        public static ThreeNozzleMoveDiag Start(params Item[] items)
        {
            var diag = new ThreeNozzleMoveDiag(items.Where(i => i != null && i.Axis != null).ToArray());
            if (!Enabled || diag.items.Length == 0) { diag.failed = true; return diag; }
            try
            {
                foreach (Item i in diag.items) i.StartCmd = i.Axis.fcmd_pos;
                diag.thread = new Thread(diag.Poll) { IsBackground = true, Name = "R32MoveDiag" };
                diag.thread.Start();
            }
            catch { diag.failed = true; }
            return diag;
        }

        private void Poll()
        {
            try
            {
                while (!stop)
                {
                    Sample();
                    Thread.Sleep(4);
                }
                Sample();
            }
            catch { failed = true; }
        }

        private void Sample()
        {
            long now = watch.ElapsedMilliseconds;
            foreach (Item i in items)
            {
                double cmd = i.Axis.fcmd_pos;
                if (i.StartMs < 0 && Math.Abs(cmd - i.StartCmd) > MovedTol) i.StartMs = now;
                if (i.CmdDoneMs < 0 && Math.Abs(cmd - i.Target) <= CmdTol) i.CmdDoneMs = now;
                if (i.CmdDoneMs >= 0 && i.EncDoneMs < 0)
                {
                    double enc = i.Axis.fenc_pos;
                    i.LastEnc = enc;
                    if (Math.Abs(enc - i.Target) <= EncTol) i.EncDoneMs = now;
                }
            }
        }

        public void FinishAndLog(string head)
        {
            try
            {
                if (failed || thread == null) return;
                long total = watch.ElapsedMilliseconds;
                stop = true;
                thread.Join(50);
                if (failed) return;
                var sb = new StringBuilder();
                sb.AppendFormat("{0} 用时 {1}ms：", head, total);
                Item last = null;
                long lastMs = -1;
                foreach (Item i in items)
                {
                    double dist = Math.Abs(i.Target - i.StartCmd);
                    long done = i.EncDoneMs >= 0 ? i.EncDoneMs : i.CmdDoneMs;
                    if (dist < 0.01)
                        sb.AppendFormat("{0} 不动；", i.Name);
                    else
                        sb.AppendFormat("{0} 走{1:F1}mm 起步{2} 指令完{3} 到位{4}；", i.Name, dist,
                            Ms(i.StartMs), Ms(i.CmdDoneMs), Ms(i.EncDoneMs));
                    if (dist >= 0.01 && done > lastMs) { lastMs = done; last = i; }
                }
                sb.AppendFormat("最后到位={0}", last == null ? "无" : last.Name);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, sb.ToString());
            }
            catch { }
        }

        private static string Ms(long ms) { return ms < 0 ? "-" : ms + "ms"; }
    }
}
