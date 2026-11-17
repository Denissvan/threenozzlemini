using System;
using System.Collections.Generic;
using System.Linq;

namespace UI.ModuleCheck
{
    public enum ModuleCheckGroup
    {
        None = 0,
        /// <summary>穴 1-4、9-12（UpDownLoad.ThreeNozzleModeOwnsPocket: index%8&lt;4）</summary>
        Low = 1,
        /// <summary>穴 5-8、13-16（index%8&gt;=4）</summary>
        High = 2,
        /// <summary>单臂负责全部穴位（单臂运行 / GRR 流程：FindMod=ALL），穴 1-16 一个循环</summary>
        All = 3,
    }

    /// <summary>与三嘴 GRR 换位一致的分组：每条臂只在自己负责的一组内循环。</summary>
    public static class ModuleCheckGroups
    {
        /// <summary>组内循环顺序（与 GRR 按穴位升序一致）</summary>
        public static readonly int[] Low = { 1, 2, 3, 4, 9, 10, 11, 12 };
        public static readonly int[] High = { 5, 6, 7, 8, 13, 14, 15, 16 };
        /// <summary>单臂负责全部穴位时的循环顺序（与 ThreeNozzleInitializeMode 按穴位升序遍历一致）</summary>
        public static readonly int[] All = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        /// <summary>pocket 为 1~16</summary>
        public static ModuleCheckGroup GroupOf(int pocket)
        {
            if (pocket < 1 || pocket > 16) return ModuleCheckGroup.None;
            return ((pocket - 1) % 8) < 4 ? ModuleCheckGroup.Low : ModuleCheckGroup.High;
        }

        public static int[] Order(ModuleCheckGroup g)
        {
            if (g == ModuleCheckGroup.Low) return Low;
            if (g == ModuleCheckGroup.High) return High;
            if (g == ModuleCheckGroup.All) return All;
            return new int[0];
        }

        public static string Name(ModuleCheckGroup g)
        {
            if (g == ModuleCheckGroup.Low) return "LOW";
            if (g == ModuleCheckGroup.High) return "HIGH";
            if (g == ModuleCheckGroup.All) return "ALL";
            return "-";
        }
    }

    /// <summary>
    /// 一条臂在一次换料里走的一个循环。Order 必须与该臂实际换位动作的遍历顺序一致
    /// （ThreeNozzleInitializeMode：按穴位升序，只取本臂负责的穴）。
    /// </summary>
    public sealed class ModuleCheckSwapCycle
    {
        public ModuleCheckGroup Group;
        public int[] Order;
    }

    /// <summary>一步搬运</summary>
    public class ModuleCheckMove
    {
        public int Step;
        public ModuleCheckGroup Group;
        public string SampleId = "";
        /// <summary>取料穴位；0 表示从暂存嘴放下</summary>
        public int FromPocket;
        /// <summary>放料穴位；0 表示取起后暂存在 N1</summary>
        public int ToPocket;

        public override string ToString()
        {
            if (ToPocket == 0) return string.Format("{0}#{1} {2}: 穴{3}→N1暂存", Step, ModuleCheckGroups.Name(Group), SampleId, FromPocket);
            if (FromPocket == 0) return string.Format("{0}#{1} {2}: N1暂存→穴{3}", Step, ModuleCheckGroups.Name(Group), SampleId, ToPocket);
            return string.Format("{0}#{1} {2}: 穴{3}→穴{4}", Step, ModuleCheckGroups.Name(Group), SampleId, FromPocket, ToPocket);
        }
    }

    /// <summary>一次整批换料计划（两组各循环移位一次）</summary>
    public class ModuleCheckSwapPlan
    {
        public List<ModuleCheckMove> LowMoves = new List<ModuleCheckMove>();
        public List<ModuleCheckMove> HighMoves = new List<ModuleCheckMove>();
        /// <summary>换位前 穴位→样品</summary>
        public Dictionary<int, string> Before = new Dictionary<int, string>();
        /// <summary>换位后 穴位→样品</summary>
        public Dictionary<int, string> After = new Dictionary<int, string>();

        public IEnumerable<ModuleCheckMove> AllMoves
        {
            get { return LowMoves.Concat(HighMoves); }
        }

        public static string FormatMap(Dictionary<int, string> map)
        {
            return string.Join(",", map.OrderBy(k => k.Key).Select(k => string.Format("{0}={1}", k.Key, k.Value)));
        }
    }

    /// <summary>
    /// 组内循环换料计划。
    /// 组内有样品的穴按组顺序 p0..p(k-1)：p(i+1) 的样品移到 p(i)，p0 的样品移到 p(k-1)。
    /// 动作顺序与 ThreeNozzleRunGrrReload 一致：N1 取 p0 暂存 → N2 依次 p1→p0、p2→p1 … → N1 放 p(k-1)。
    /// 与 GRR“放最早空穴”不同，这里按预先生成的映射表放料，组内有空穴/屏蔽穴时也严格循环。
    /// </summary>
    public static class ModuleCheckSwapPlanner
    {
        public static ModuleCheckSwapPlan Plan(Dictionary<int, string> fixtureMap, out string error)
        {
            return Plan(fixtureMap, null, out error);
        }

        /// <summary>
        /// cycles：每条臂负责的循环（按当前运行模式的臂分工）。null/空 = 双臂默认（LOW、HIGH 各一个循环）。
        /// 单臂运行或 GRR 流程时只有一条臂（FindMod=ALL）走全部穴位，这时必须按一个 ALL 循环规划，
        /// 否则计划按两组各自循环，而实际动作是一个大循环，两组交界处的两个穴会对调（例如 穴4/穴8）。
        /// </summary>
        public static ModuleCheckSwapPlan Plan(Dictionary<int, string> fixtureMap, IList<ModuleCheckSwapCycle> cycles, out string error)
        {
            error = "";
            if (cycles == null || cycles.Count == 0)
                cycles = new[]
                {
                    new ModuleCheckSwapCycle { Group = ModuleCheckGroup.Low, Order = ModuleCheckGroups.Low },
                    new ModuleCheckSwapCycle { Group = ModuleCheckGroup.High, Order = ModuleCheckGroups.High },
                };
            var covered = new HashSet<int>(cycles.SelectMany(c => c.Order ?? new int[0]));
            var outside = fixtureMap.Keys.Where(p => !covered.Contains(p)).OrderBy(p => p).ToList();
            if (outside.Count > 0)
            {
                error = string.Format("穴{0} 的样品不在任何一条臂的换位范围内（当前臂分工：{1}）",
                    string.Join("、", outside), string.Join("、", cycles.Select(c => ModuleCheckGroups.Name(c.Group))));
                return null;
            }
            var plan = new ModuleCheckSwapPlan();
            foreach (var kv in fixtureMap) plan.Before[kv.Key] = kv.Value;
            foreach (var kv in fixtureMap) plan.After[kv.Key] = kv.Value;

            int step = 1;
            foreach (ModuleCheckSwapCycle cycle in cycles)
            {
                ModuleCheckGroup g = cycle.Group;
                var occupied = (cycle.Order ?? new int[0]).Where(p => fixtureMap.ContainsKey(p)).ToList();
                if (occupied.Count == 0) continue;
                if (occupied.Count == 1)
                {
                    error = string.Format("{0} 组只有 1 颗样品（穴{1}），无法组内换料", ModuleCheckGroups.Name(g), occupied[0]);
                    return null;
                }
                var moves = g == ModuleCheckGroup.High ? plan.HighMoves : plan.LowMoves;   // ALL 记在 LowMoves
                int k = occupied.Count;
                string held = fixtureMap[occupied[0]];
                moves.Add(new ModuleCheckMove { Step = step++, Group = g, SampleId = held, FromPocket = occupied[0], ToPocket = 0 });
                for (int i = 1; i < k; i++)
                {
                    string id = fixtureMap[occupied[i]];
                    moves.Add(new ModuleCheckMove { Step = step++, Group = g, SampleId = id, FromPocket = occupied[i], ToPocket = occupied[i - 1] });
                    plan.After[occupied[i - 1]] = id;
                }
                moves.Add(new ModuleCheckMove { Step = step++, Group = g, SampleId = held, FromPocket = 0, ToPocket = occupied[k - 1] });
                plan.After[occupied[k - 1]] = held;
            }
            return plan;
        }

        /// <summary>检查新排列是否与历史某一轮完全相同（循环回到旧排列）</summary>
        public static int FindRepeatedRound(IEnumerable<ModuleCheckRound> history, Dictionary<int, string> map)
        {
            foreach (var r in history)
            {
                if (r.Mapping.Count != map.Count) continue;
                bool same = true;
                foreach (var kv in map)
                {
                    string v;
                    if (!r.Mapping.TryGetValue(kv.Key, out v) || v != kv.Value) { same = false; break; }
                }
                if (same) return r.RoundNo;
            }
            return 0;
        }
    }
}
