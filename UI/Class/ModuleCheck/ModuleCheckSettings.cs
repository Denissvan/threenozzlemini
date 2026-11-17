using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace UI.ModuleCheck
{
    /// <summary>一颗样品的配置：点检盘穴位 → 首轮夹具穴位</summary>
    public class ModuleCheckSampleSlot
    {
        /// <summary>点检盘穴位序号（从 0 开始，与料盘阵列 idx 一致）</summary>
        public int TrayPocketIdx;
        /// <summary>夹具穴位 1~16</summary>
        public int FixturePocket;
    }

    /// <summary>模组自动点检设置（保存在 syscfg\ModuleCheck\settings.json）</summary>
    public class ModuleCheckSettings
    {
        public const int FixturePocketCount = 16;
        public const int MaxAdvanceMinutes = 60;

        /// <summary>启用定时模组自动点检</summary>
        public bool Enabled = false;
        /// <summary>计划点检时间 T，格式 HH:mm，每天执行</summary>
        public List<string> PlanTimes = new List<string>();
        /// <summary>提前触发分钟数，触发窗口 [T-提前, T)</summary>
        public int AdvanceMinutes = 60;
        /// <summary>自动点检重试次数：首轮 NG 后最多再换料重测的轮数</summary>
        public int RetryCount = 3;
        /// <summary>承载点检样品的工站（0~3）</summary>
        public int TargetStation = 0;
        /// <summary>SetFactoryMode(点检) 时下发的 CheckModeTemp 参数</summary>
        public int CheckModeTemp = 3000;
        /// <summary>切参数（重启+登录）后等待回读确认的超时秒数</summary>
        public int ModeSwitchTimeoutSec = 60;
        /// <summary>点检盘结构件到位前，借用待测盘的一部分作为点检盘</summary>
        public bool BorrowFeedTray = true;
        /// <summary>样品配置</summary>
        public List<ModuleCheckSampleSlot> Samples = new List<ModuleCheckSampleSlot>();
        /// <summary>测试电脑 IP（PC_ID → IP），优先于工站配置 WS*Cfg.ini 里的 PC_IP；用于 PUCF 模式切换</summary>
        public Dictionary<int, string> PcIps = new Dictionary<int, string>();

        /// <summary>总轮数上限 = 首轮 + 重试次数</summary>
        [JsonIgnore]
        public int MaxRounds
        {
            get { return 1 + Math.Max(0, RetryCount); }
        }

        public ModuleCheckSettings Clone()
        {
            var c = (ModuleCheckSettings)MemberwiseClone();
            c.PlanTimes = new List<string>(PlanTimes);
            c.Samples = Samples.Select(s => new ModuleCheckSampleSlot { TrayPocketIdx = s.TrayPocketIdx, FixturePocket = s.FixturePocket }).ToList();
            c.PcIps = new Dictionary<int, string>(PcIps ?? new Dictionary<int, string>());
            return c;
        }

        public static bool TryParsePlanTime(string text, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            DateTime dt;
            if (!DateTime.TryParseExact(text.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return false;
            t = dt.TimeOfDay;
            return true;
        }

        /// <summary>每组（LOW/HIGH）当前配置的样品数</summary>
        public void CountGroups(out int low, out int high)
        {
            low = Samples.Count(s => ModuleCheckGroups.GroupOf(s.FixturePocket) == ModuleCheckGroup.Low);
            high = Samples.Count(s => ModuleCheckGroups.GroupOf(s.FixturePocket) == ModuleCheckGroup.High);
        }

        /// <summary>
        /// 不重复排列的最大轮数：组内循环移位，k 颗样品的组第 k+1 轮会回到首轮排列。
        /// 取有样品的组中最小的 k。
        /// </summary>
        public int MaxDistinctRounds()
        {
            int low, high;
            CountGroups(out low, out high);
            var ks = new List<int>();
            if (low > 0) ks.Add(low);
            if (high > 0) ks.Add(high);
            return ks.Count == 0 ? 0 : ks.Min();
        }

        /// <summary>校验设置；trayPocketCount&lt;=0 表示不校验点检盘穴数</summary>
        public List<string> Validate(int trayPocketCount)
        {
            var errs = new List<string>();
            if (Enabled && PlanTimes.Count == 0) errs.Add("已启用但没有设置点检时间");
            var seen = new HashSet<TimeSpan>();
            foreach (var p in PlanTimes)
            {
                TimeSpan t;
                if (!TryParsePlanTime(p, out t)) errs.Add(string.Format("点检时间格式错误：{0}（应为 HH:mm）", p));
                else if (!seen.Add(t)) errs.Add(string.Format("点检时间重复：{0}", p));
            }
            if (AdvanceMinutes < 1 || AdvanceMinutes > MaxAdvanceMinutes) errs.Add("提前触发分钟数须在 1~60");
            if (RetryCount < 0) errs.Add("重试次数不能为负");
            if (TargetStation < 0 || TargetStation > 3) errs.Add("承载工站须为工站1~4");
            if (ModeSwitchTimeoutSec < 5 || ModeSwitchTimeoutSec > 600) errs.Add("模式切换超时须在 5~600 秒");

            if (Samples.Count < 2) errs.Add("至少配置 2 颗样品");
            if (Samples.Count > FixturePocketCount) errs.Add("样品数不能超过 16");
            var pockets = new HashSet<int>();
            var trayIdx = new HashSet<int>();
            foreach (var s in Samples)
            {
                if (s.FixturePocket < 1 || s.FixturePocket > FixturePocketCount) errs.Add(string.Format("夹具穴位 {0} 超出 1~16", s.FixturePocket));
                else if (!pockets.Add(s.FixturePocket)) errs.Add(string.Format("夹具穴位 {0} 被重复使用", s.FixturePocket));
                if (s.TrayPocketIdx < 0) errs.Add(string.Format("点检盘穴位 {0} 无效", s.TrayPocketIdx));
                else if (trayPocketCount > 0 && s.TrayPocketIdx >= trayPocketCount) errs.Add(string.Format("点检盘穴位 {0} 超出点检盘穴数 {1}", s.TrayPocketIdx, trayPocketCount));
                else if (!trayIdx.Add(s.TrayPocketIdx)) errs.Add(string.Format("点检盘穴位 {0} 被重复使用", s.TrayPocketIdx));
            }

            int low, high;
            CountGroups(out low, out high);
            if (low == 1) errs.Add("LOW 组（穴1-4、9-12）只有 1 颗样品，无法组内换料，须 0 颗或至少 2 颗");
            if (high == 1) errs.Add("HIGH 组（穴5-8、13-16）只有 1 颗样品，无法组内换料，须 0 颗或至少 2 颗");

            int distinct = MaxDistinctRounds();
            if (distinct > 0 && MaxRounds > distinct)
                errs.Add(string.Format("重试次数过大：样品最少的组只有 {0} 颗，最多测 {0} 轮不重复排列，重试次数最多 {1}", distinct, distinct - 1));
            return errs;
        }

        /// <summary>
        /// 自动分配首轮夹具穴位：n 颗样品尽量平均分到 LOW/HIGH 两组（组内循环换料要求每组 0 颗或至少 2 颗），
        /// 返回从小到大的穴位，调用方按点检盘穴位编号从小到大依次对应。
        /// allowed 不为 null 时只用其中的穴位（工站启用的穴），放不下的部分不分配（返回个数可能小于 n）。
        /// </summary>
        public static List<int> AutoFixturePockets(int n, ICollection<int> allowed = null)
        {
            var lowSrc = ModuleCheckGroups.Low.Where(p => allowed == null || allowed.Contains(p)).ToList();
            var highSrc = ModuleCheckGroups.High.Where(p => allowed == null || allowed.Contains(p)).ToList();
            n = Math.Max(0, Math.Min(lowSrc.Count + highSrc.Count, n));
            int low = (n + 1) / 2;
            int high = n - low;
            if (low > lowSrc.Count) { high += low - lowSrc.Count; low = lowSrc.Count; }
            if (high > highSrc.Count) { low += high - highSrc.Count; high = highSrc.Count; }
            // 每组 0 颗或至少 2 颗
            if (high == 1) { if (low < lowSrc.Count) { low++; high = 0; } else if (highSrc.Count >= 2 && low > 2) { low--; high++; } else high = 0; }
            if (low == 1) { if (high < highSrc.Count) { high++; low = 0; } else if (lowSrc.Count >= 2 && high > 2) { high--; low++; } else low = 0; }
            var list = lowSrc.Take(low).Concat(highSrc.Take(high)).ToList();
            list.Sort();
            return list;
        }

        /// <summary>按点检盘穴位编号生成样品表，首轮夹具穴位自动分配</summary>
        public static List<ModuleCheckSampleSlot> SlotsFromTrayPockets(IEnumerable<int> trayPocketIdx, ICollection<int> allowed = null)
        {
            var idx = trayPocketIdx.Distinct().OrderBy(x => x).Take(FixturePocketCount).ToList();
            var pockets = AutoFixturePockets(idx.Count, allowed);
            var list = new List<ModuleCheckSampleSlot>();
            for (int i = 0; i < idx.Count && i < pockets.Count; i++)
                list.Add(new ModuleCheckSampleSlot { TrayPocketIdx = idx[i], FixturePocket = pockets[i] });
            return list;
        }

        /// <summary>按顺序生成 16 颗：点检盘穴 0~15 → 夹具穴 1~16</summary>
        public static List<ModuleCheckSampleSlot> DefaultSlots(int count)
        {
            var list = new List<ModuleCheckSampleSlot>();
            for (int i = 0; i < count && i < FixturePocketCount; i++)
                list.Add(new ModuleCheckSampleSlot { TrayPocketIdx = i, FixturePocket = i + 1 });
            return list;
        }
    }
}
