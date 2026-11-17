using System;
using System.Collections.Generic;
using System.Linq;

namespace UI.ModuleCheck
{
    /// <summary>已处理的计划（持久化，防止重复触发/重启后重复补做）</summary>
    public class ModuleCheckPlanRegistry
    {
        /// <summary>只考虑计划时间不早于此刻的计划（启用功能或首次运行时设置），避免首次启用补做一堆历史计划</summary>
        public DateTime ActiveSince = DateTime.MinValue;
        /// <summary>计划号 → 处理说明（已触发/补做/合并跳过）</summary>
        public Dictionary<string, string> Handled = new Dictionary<string, string>();

        public bool IsHandled(string planId)
        {
            return Handled.ContainsKey(planId);
        }

        public void MarkHandled(string planId, string note)
        {
            Handled[planId] = string.Format("{0:yyyy-MM-dd HH:mm:ss} {1}", DateTime.Now, note);
            // 只保留最近 300 条
            if (Handled.Count > 300)
            {
                foreach (var k in Handled.Keys.OrderBy(k => k).Take(Handled.Count - 300).ToList())
                    Handled.Remove(k);
            }
        }
    }

    /// <summary>需要执行的计划</summary>
    public class ModuleCheckDuePlan
    {
        public string PlanId = "";
        public DateTime PlanTime;
        public DateTime WindowStart;
        public ModuleCheckTrigger Trigger;
        /// <summary>同时被合并（不再单独补做）的旧计划</summary>
        public List<string> MergedPlanIds = new List<string>();
    }

    /// <summary>
    /// 定时调度：只判断“该不该建任务”，不做任何运动。
    /// 计划时间 T 的触发窗口为 [T-提前分钟, T)。
    /// - 窗口内且未处理 → 准时触发；
    /// - 已过 T 且未处理（窗口期间停机等）→ 补做，连续错过多个只补做最近的一个，其余记为合并；
    /// 判定用“时刻已过且未登记”，不用时间字符串相等，Tick 漏掉一次也不会错过。
    /// </summary>
    public static class ModuleCheckScheduler
    {
        /// <summary>最多回看的天数</summary>
        public const int LookBackDays = 2;

        public static string PlanIdOf(DateTime planTime)
        {
            return planTime.ToString("yyyyMMdd-HHmm");
        }

        public static List<DateTime> CandidatePlanTimes(ModuleCheckSettings s, DateTime now)
        {
            var list = new List<DateTime>();
            var times = new List<TimeSpan>();
            foreach (var p in s.PlanTimes)
            {
                TimeSpan t;
                if (ModuleCheckSettings.TryParsePlanTime(p, out t)) times.Add(t);
            }
            for (int d = -LookBackDays; d <= 1; d++)
            {
                DateTime day = now.Date.AddDays(d);
                foreach (var t in times) list.Add(day + t);
            }
            list.Sort();
            return list;
        }

        public static ModuleCheckDuePlan FindDue(ModuleCheckSettings s, DateTime now, ModuleCheckPlanRegistry reg)
        {
            if (s == null || !s.Enabled) return null;
            int adv = Math.Max(1, Math.Min(ModuleCheckSettings.MaxAdvanceMinutes, s.AdvanceMinutes));
            var open = new List<DateTime>();
            foreach (var t in CandidatePlanTimes(s, now))
            {
                if (t < reg.ActiveSince) continue;
                if (t.AddMinutes(-adv) > now) continue;      // 窗口还没开始
                if (reg.IsHandled(PlanIdOf(t))) continue;
                open.Add(t);
            }
            if (open.Count == 0) return null;

            var due = new ModuleCheckDuePlan();
            DateTime inWindow = open.FirstOrDefault(t => now < t);
            if (inWindow != default(DateTime))
            {
                due.PlanTime = inWindow;
                due.Trigger = ModuleCheckTrigger.OnTime;
            }
            else
            {
                due.PlanTime = open.Max();
                due.Trigger = ModuleCheckTrigger.MakeUp;
            }
            due.PlanId = PlanIdOf(due.PlanTime);
            due.WindowStart = due.PlanTime.AddMinutes(-adv);
            due.MergedPlanIds = open.Where(t => t != due.PlanTime).Select(PlanIdOf).ToList();
            return due;
        }

        /// <summary>下一个计划的预览文字（设置界面用）</summary>
        public static string DescribeNext(ModuleCheckSettings s, DateTime now)
        {
            if (s == null || s.PlanTimes.Count == 0) return "未设置点检时间";
            int adv = Math.Max(1, Math.Min(ModuleCheckSettings.MaxAdvanceMinutes, s.AdvanceMinutes));
            var next = CandidatePlanTimes(s, now).Where(t => t > now).OrderBy(t => t).FirstOrDefault();
            if (next == default(DateTime)) return "未设置点检时间";
            return string.Format("下一次计划 {0:MM-dd HH:mm}，触发窗口 {1:HH:mm}~{0:HH:mm}{2}",
                next, next.AddMinutes(-adv), s.Enabled ? "" : "（未启用）");
        }
    }
}
