using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UI.ModuleCheck
{
    public enum ModuleCheckLogLevel
    {
        Info = 0,
        Warn = 1,
        Error = 2,
    }

    /// <summary>日志类别</summary>
    public static class ModuleCheckLogCat
    {
        public const string Task = "任务";
        public const string Schedule = "调度";
        public const string Mode = "模式切换";
        public const string Test = "测试握手";
        public const string Judge = "判定";
        public const string Material = "物料";
        public const string Alarm = "报警";
        public const string Timing = "耗时";
        public const string Config = "设置";
        public const string Risk = "风险";
    }

    /// <summary>需要单独记录、方便追溯的风险点</summary>
    public enum ModuleCheckRisk
    {
        /// <summary>启动测试时上一轮握手未走完（标志不是 END）</summary>
        StartWhileBusy = 1,
        /// <summary>切参数后重启等待超时 / 回读与期望不一致</summary>
        ModeSwitchProblem = 2,
        /// <summary>PC_ID 与 IP 对应不上，或恢复生产漏了某台 PC</summary>
        PcMappingMismatch = 3,
        /// <summary>同一轮结果被重复读到</summary>
        DuplicateResult = 4,
        /// <summary>夹具实际物料与任务记录不一致</summary>
        SampleIdentityMismatch = 5,
        /// <summary>样品差点进入普通 OK/NG 盘被拦截</summary>
        SampleToProductTrayBlocked = 6,
        /// <summary>点检中员工清料或测试端发来旧 279/276</summary>
        ClearOrOldTriggerDuringTask = 7,
        /// <summary>借用待测盘期间自动换盘被拦截</summary>
        FeedTraySwapBlocked = 8,
        /// <summary>切参数窗口内出现联机错误</summary>
        LinkErrDuringSwitch = 9,
        /// <summary>程序重启后持久化记录与机台状态不一致</summary>
        RecoveryMismatch = 10,
        /// <summary>点检样品下视觉/扫码失败，放回原穴重取</summary>
        SampleVisionFail = 11,
        /// <summary>人工对账/人工归位改写了样品位置</summary>
        ManualSampleFix = 12,
    }

    /// <summary>
    /// 独立点检日志：
    ///   ..\log\ModuleCheck\yyyy-MM-dd_modulecheck.log  过程日志（UTF-8 BOM）
    ///   ..\log\ModuleCheck\点检结果_yyyy-MM.csv           每颗样品每轮一行（Excel 可直接打开）
    /// 关键节点同时通过 Mirror 写一行到主日志，带 [点检] 前缀，便于和整机动作对时间。
    /// </summary>
    public static class ModuleCheckLog
    {
        private static readonly object fileLock = new object();

        /// <summary>镜像到主日志：(文本, 是否错误)。由 ModuleCheckMachineAdapter 设置。</summary>
        public static Action<string, ModuleCheckLogLevel> Mirror;

        /// <summary>新日志行事件（运行页显示用）</summary>
        public static event Action<string> LineWritten;

        public static string Dir
        {
            get { return Path.Combine(Path.GetFullPath(".."), "log", "ModuleCheck"); }
        }

        public static string TodayLogPath
        {
            get { return Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM-dd") + "_modulecheck.log"); }
        }

        public static void Info(string cat, ModuleCheckTask t, string msg, bool mirror = false)
        {
            Write(ModuleCheckLogLevel.Info, cat, t, msg, mirror);
        }

        public static void Warn(string cat, ModuleCheckTask t, string msg, bool mirror = true)
        {
            Write(ModuleCheckLogLevel.Warn, cat, t, msg, mirror);
        }

        public static void Error(string cat, ModuleCheckTask t, string msg, bool mirror = true)
        {
            Write(ModuleCheckLogLevel.Error, cat, t, msg, mirror);
        }

        /// <summary>风险点：单独一条警告并镜像主日志</summary>
        public static void Risk(ModuleCheckRisk risk, ModuleCheckTask t, string msg)
        {
            Write(ModuleCheckLogLevel.Warn, ModuleCheckLogCat.Risk, t, string.Format("[R{0:00}-{1}] {2}", (int)risk, risk, msg), true);
        }

        public static void Write(ModuleCheckLogLevel level, string cat, ModuleCheckTask t, string msg, bool mirror)
        {
            string taskId = t == null ? "-" : t.TaskId;
            string round = t == null ? "-" : t.CurrentRoundNo.ToString();
            string lv = level == ModuleCheckLogLevel.Info ? "INFO" : (level == ModuleCheckLogLevel.Warn ? "WARN" : "ERR ");
            string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} | {1} | 轮{2} | {3} | {4} | {5}",
                DateTime.Now, taskId, round, lv, cat, (msg ?? "").Replace("\r", " ").Replace("\n", " "));
            try
            {
                lock (fileLock)
                {
                    Directory.CreateDirectory(Dir);
                    AppendWithBom(TodayLogPath, line + "\r\n");
                }
            }
            catch
            {
                // 日志失败不影响流程
            }
            try
            {
                var h = LineWritten;
                if (h != null) h(line);
            }
            catch { }
            if (mirror)
            {
                try
                {
                    var m = Mirror;
                    if (m != null) m(string.Format("[点检]{0} {1}: {2}", taskId == "-" ? "" : taskId, cat, msg), level);
                }
                catch { }
            }
        }

        /// <summary>一轮判定后写结果表，每颗样品一行</summary>
        public static void AppendResultRows(ModuleCheckTask t, ModuleCheckRound r)
        {
            if (t == null || r == null) return;
            string path = Path.Combine(Dir, string.Format("点检结果_{0:yyyy-MM}.csv", DateTime.Now));
            var sb = new StringBuilder();
            foreach (var pr in r.Results.OrderBy(x => x.Pocket))
            {
                if (string.IsNullOrEmpty(pr.SampleId)) continue;
                var s = t.FindSample(pr.SampleId);
                sb.AppendFormat("{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}\r\n",
                    r.EndTime ?? DateTime.Now,
                    Csv(t.TaskId),
                    Csv(t.PlanTime.HasValue ? t.PlanTime.Value.ToString("yyyy-MM-dd HH:mm") : ""),
                    Csv(ModuleCheckText.Trigger(t.Trigger)),
                    t.Snapshot.TargetStation + 1,
                    r.RoundNo,
                    pr.Pocket,
                    Csv(pr.SampleId),
                    s == null ? -1 : s.TrayPocketIdx,
                    pr.PcId,
                    pr.Code,
                    Csv(ModuleCheckText.Verdict(r.Verdict)));
            }
            try
            {
                lock (fileLock)
                {
                    Directory.CreateDirectory(Dir);
                    if (!File.Exists(path))
                        AppendWithBom(path, "时间,任务号,计划时间,触发方式,工站,轮次,夹具穴,样品,点检盘穴,PC,结果码,本轮判定\r\n");
                    AppendWithBom(path, sb.ToString());
                }
            }
            catch { }
        }

        private static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static void AppendWithBom(string path, string text)
        {
            bool isNew = !File.Exists(path);
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                if (isNew)
                {
                    byte[] bom = Encoding.UTF8.GetPreamble();
                    fs.Write(bom, 0, bom.Length);
                }
                byte[] data = new UTF8Encoding(false).GetBytes(text);
                fs.Write(data, 0, data.Length);
            }
        }
    }
}
