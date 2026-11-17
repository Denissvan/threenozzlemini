using System;
using System.Collections.Generic;
using System.Linq;

namespace UI
{
    /// <summary>空跑模拟显示里一个位置（吸嘴、夹具穴、料盘穴）上的“虚拟料”。</summary>
    public enum SimPart { Empty, New, Old, Ng }

    public sealed class SimSlot
    {
        public SimPart Kind;
        public string Label = "";
        public SimSlot Clone() { return new SimSlot { Kind = Kind, Label = Label }; }
    }

    public sealed class SimTrayCell
    {
        public int Idx, Row, Col;
        public bool Disabled;
        public SimSlot Content = new SimSlot();
        public SimTrayCell Clone() { return new SimTrayCell { Idx = Idx, Row = Row, Col = Col, Disabled = Disabled, Content = Content.Clone() }; }
    }

    /// <summary>某一时刻的模拟状态快照（只读拷贝，供界面绘制）。</summary>
    public sealed class ThreeNozzleSimSnapshot
    {
        public string[] ArmNames = { "上下料1", "上下料2" };
        public bool[] ArmSelected = new bool[2];
        public SimSlot[][] Nozzles = { new SimSlot[3], new SimSlot[3] };
        public SimSlot[] Fixture = new SimSlot[0];      // 下标 = 穴号-1
        public bool[] FixtureEnabled = new bool[0];
        public int[] FixtureArm = new int[0];           // 该穴由哪个臂负责：-1=本次不跑，0/1=臂1/臂2，2=两臂都跑
        public List<SimTrayCell> Feed = new List<SimTrayCell>();
        public List<SimTrayCell> Ok = new List<SimTrayCell>();
        public int FeedRows, FeedCols, OkRows, OkCols;
        /// <summary>NG 盘（仅系统运行实时显示用；空跑模拟不填）。</summary>
        public List<SimTrayCell> Ng = new List<SimTrayCell>();
        public int NgRows, NgCols;
        /// <summary>true=系统运行实时显示（数据来自真实物料记录），false=空跑模拟。</summary>
        public bool Live;
        public int Cycle;
        public int ActiveArm = -1, ActiveNozzle = -1;
        /// <summary>"feed"/"fixture"/"ok"/""</summary>
        public string ActiveKind = "";
        public int ActiveTarget = -1;
        public string StepText = "";
        public string StationName = "";
        // 双臂同时运行时两个臂各有自己的当前动作
        public int[] ActiveNozzles = { -1, -1 };
        public string[] ActiveKinds = { "", "" };
        public int[] ActiveTargets = { -1, -1 };
        public string[] ArmSteps = { "", "" };
    }

    /// <summary>
    /// 空跑的虚拟物料模型：不读写任何真实物料/料盘数据，只按空跑执行的每一步更新，
    /// 让界面能看到“哪个吸嘴拿着哪颗料、夹具/料盘各穴现在是什么”。
    /// 规则与正式三吸嘴交换一致：开始时参与的夹具穴都有旧料，供料盘满、OK盘空；
    /// 每轮开始时，上一轮放进夹具的新料视为已测完，变成旧料。
    /// </summary>
    internal sealed class ThreeNozzleDryRunSim
    {
        private readonly object sync = new object();
        private readonly ThreeNozzleSimSnapshot s = new ThreeNozzleSimSnapshot();
        private readonly HashSet<int>[] armPockets = { new HashSet<int>(), new HashSet<int>() };

        public ThreeNozzleDryRunSim(WS ws, Product.Tray feed, Product.Tray ok, bool[] armSelected, Func<int, IEnumerable<int>> pocketsOfArm,
            bool fixtureFull = true)
        {
            s.StationName = ws == null ? "" : ws.disc;
            s.ArmSelected = (bool[])armSelected.Clone();
            for (int arm = 0; arm < 2; arm++)
                for (int i = 0; i < 3; i++) s.Nozzles[arm][i] = new SimSlot();
            int count = ws == null || ws.list_md == null ? 0 : ws.list_md.Count;
            s.Fixture = new SimSlot[count];
            s.FixtureEnabled = new bool[count];
            s.FixtureArm = Enumerable.Repeat(-1, count).ToArray();
            for (int i = 0; i < count; i++)
            {
                s.Fixture[i] = new SimSlot();
                WS.MdDat md = ws.list_md.FirstOrDefault(m => m != null && m.Num == i + 1);
                s.FixtureEnabled[i] = md != null && md.benable;
            }
            for (int arm = 0; arm < 2; arm++)
            {
                if (!armSelected[arm]) continue;
                foreach (int num in pocketsOfArm(arm))
                {
                    if (num < 1 || num > count) continue;
                    armPockets[arm].Add(num);
                    s.FixtureArm[num - 1] = s.FixtureArm[num - 1] >= 0 && s.FixtureArm[num - 1] != arm ? 2 : arm;
                    s.Fixture[num - 1] = fixtureFull ? new SimSlot { Kind = SimPart.Old, Label = "旧" + num } : new SimSlot();
                }
            }
            FillTray(s.Feed, feed, out s.FeedRows, out s.FeedCols, true);
            FillTray(s.Ok, ok, out s.OkRows, out s.OkCols, false);
            s.StepText = fixtureFull ? "准备：参与的夹具穴都有旧料，供料盘满，OK盘空" : "准备：夹具全空，供料盘满，OK盘空";
        }

        /// <summary>该夹具穴（模拟）上是否有料。</summary>
        public bool HasPart(int pocketNum)
        {
            lock (sync)
                return pocketNum >= 1 && pocketNum <= s.Fixture.Length && s.Fixture[pocketNum - 1].Kind != SimPart.Empty;
        }

        /// <summary>重置模拟夹具：full=参与的穴全部放旧料，否则全部清空。吸嘴同时清空，料盘不变。</summary>
        public void ResetFixture(bool full)
        {
            lock (sync)
            {
                for (int i = 0; i < s.Fixture.Length; i++)
                    s.Fixture[i] = full && s.FixtureArm[i] >= 0 ? new SimSlot { Kind = SimPart.Old, Label = "旧" + (i + 1) } : new SimSlot();
                for (int arm = 0; arm < 2; arm++)
                    for (int n = 0; n < 3; n++) s.Nozzles[arm][n] = new SimSlot();
                s.Cycle = 0; s.ActiveArm = -1; s.ActiveNozzle = -1; s.ActiveKind = ""; s.ActiveTarget = -1;
                s.StepText = full ? "模拟夹具已装满旧料（参与的穴）" : "模拟夹具已清空";
            }
        }

        private static void FillTray(List<SimTrayCell> cells, Product.Tray tray, out int rows, out int cols, bool full)
        {
            rows = cols = 0;
            if (tray == null || tray.list_pos == null) return;
            foreach (Product.Tray.PosInf p in tray.list_pos)
            {
                var cell = new SimTrayCell { Idx = p.idx, Row = p.Row, Col = p.Col, Disabled = p.isDisable };
                if (full && !p.isDisable) cell.Content = new SimSlot { Kind = SimPart.New, Label = "F" + p.idx };
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

        public ThreeNozzleSimSnapshot Snapshot()
        {
            lock (sync)
            {
                var c = new ThreeNozzleSimSnapshot
                {
                    ArmNames = (string[])s.ArmNames.Clone(),
                    ArmSelected = (bool[])s.ArmSelected.Clone(),
                    Fixture = s.Fixture.Select(x => x.Clone()).ToArray(),
                    FixtureEnabled = (bool[])s.FixtureEnabled.Clone(),
                    FixtureArm = (int[])s.FixtureArm.Clone(),
                    Feed = s.Feed.Select(x => x.Clone()).ToList(),
                    Ok = s.Ok.Select(x => x.Clone()).ToList(),
                    FeedRows = s.FeedRows, FeedCols = s.FeedCols, OkRows = s.OkRows, OkCols = s.OkCols,
                    Cycle = s.Cycle, ActiveArm = s.ActiveArm, ActiveNozzle = s.ActiveNozzle,
                    ActiveKind = s.ActiveKind, ActiveTarget = s.ActiveTarget, StepText = s.StepText,
                    StationName = s.StationName,
                    ActiveNozzles = (int[])s.ActiveNozzles.Clone(),
                    ActiveKinds = (string[])s.ActiveKinds.Clone(),
                    ActiveTargets = (int[])s.ActiveTargets.Clone(),
                    ArmSteps = (string[])s.ArmSteps.Clone(),
                };
                for (int arm = 0; arm < 2; arm++) c.Nozzles[arm] = s.Nozzles[arm].Select(x => x.Clone()).ToArray();
                return c;
            }
        }

        /// <summary>某臂开始新一轮：上一轮放进本臂穴位的新料视为测完，变成旧料。</summary>
        public void BeginArmCycle(int arm, int cycle)
        {
            lock (sync)
            {
                s.Cycle = cycle;
                for (int i = 0; i < s.Fixture.Length; i++)
                    if (armPockets[arm].Contains(i + 1) && s.Fixture[i].Kind == SimPart.New)
                        s.Fixture[i].Kind = SimPart.Old;
                s.ActiveArm = arm; s.ActiveNozzle = -1; s.ActiveKind = ""; s.ActiveTarget = -1;
                s.StepText = string.Format("第{0}轮 {1} 开始（上一轮放入的新料视为已测完，变为旧料）", cycle, s.ArmNames[arm]);
                s.ActiveNozzles[arm] = -1; s.ActiveKinds[arm] = ""; s.ActiveTargets[arm] = -1; s.ArmSteps[arm] = s.StepText;
            }
        }

        public void SetActive(int arm, int nozzle, string kind, int target, string text)
        {
            lock (sync)
            {
                s.ActiveArm = arm; s.ActiveNozzle = nozzle; s.ActiveKind = kind ?? ""; s.ActiveTarget = target;
                s.StepText = text ?? "";
                if (arm >= 0 && arm < 2)
                {
                    s.ActiveNozzles[arm] = nozzle; s.ActiveKinds[arm] = kind ?? ""; s.ActiveTargets[arm] = target;
                    s.ArmSteps[arm] = text ?? "";
                }
            }
        }

        /// <summary>供料盘取一颗新料到吸嘴。取完一整盘后模拟换满盘。</summary>
        public void FeedPick(int arm, int nozzle, int feedIdx)
        {
            lock (sync)
            {
                SimTrayCell cell = s.Feed.FirstOrDefault(c => c.Idx == feedIdx);
                string label = "F" + feedIdx;
                if (cell != null)
                {
                    if (cell.Content.Kind == SimPart.Empty)          // 走到空穴说明已经轮回：模拟换新盘
                        foreach (SimTrayCell c in s.Feed.Where(c => !c.Disabled))
                            c.Content = new SimSlot { Kind = SimPart.New, Label = "F" + c.Idx };
                    label = cell.Content.Label;
                    cell.Content = new SimSlot();
                }
                s.Nozzles[arm][nozzle] = new SimSlot { Kind = SimPart.New, Label = label };
            }
        }

        /// <summary>吸嘴从夹具穴取出旧料。</summary>
        public void FixturePick(int arm, int nozzle, int pocketNum)
        {
            lock (sync)
            {
                SimSlot taken = new SimSlot { Kind = SimPart.Old, Label = "旧" + pocketNum };
                if (pocketNum >= 1 && pocketNum <= s.Fixture.Length)
                {
                    if (s.Fixture[pocketNum - 1].Kind != SimPart.Empty)
                        taken = new SimSlot { Kind = SimPart.Old, Label = s.Fixture[pocketNum - 1].Label.Replace("新", "旧") };
                    s.Fixture[pocketNum - 1] = new SimSlot();
                }
                s.Nozzles[arm][nozzle] = taken;
            }
        }

        /// <summary>吸嘴把新料放进夹具穴。</summary>
        public void FixturePlace(int arm, int nozzle, int pocketNum)
        {
            lock (sync)
            {
                SimSlot held = s.Nozzles[arm][nozzle];
                string label = held.Kind == SimPart.Empty ? "新?" : held.Label;
                if (!label.StartsWith("新")) label = "新" + label;
                if (pocketNum >= 1 && pocketNum <= s.Fixture.Length)
                    s.Fixture[pocketNum - 1] = new SimSlot { Kind = SimPart.New, Label = label };
                s.Nozzles[arm][nozzle] = new SimSlot();
            }
        }

        /// <summary>吸嘴把旧料落到 OK 盘。OK 盘放满后模拟换空盘。</summary>
        public void OkDrop(int arm, int nozzle, int okIdx)
        {
            lock (sync)
            {
                SimSlot held = s.Nozzles[arm][nozzle];
                SimTrayCell cell = s.Ok.FirstOrDefault(c => c.Idx == okIdx);
                if (cell != null)
                {
                    if (cell.Content.Kind != SimPart.Empty)          // 走到已放过的穴说明已经轮回：模拟换空盘
                        foreach (SimTrayCell c in s.Ok) c.Content = new SimSlot();
                    cell.Content = new SimSlot { Kind = SimPart.Old, Label = held.Kind == SimPart.Empty ? "旧?" : held.Label };
                }
                s.Nozzles[arm][nozzle] = new SimSlot();
            }
        }

        public void Finish(string text)
        {
            lock (sync)
            {
                s.ActiveNozzle = -1; s.ActiveKind = ""; s.ActiveTarget = -1;
                s.StepText = text ?? "";
                for (int arm = 0; arm < 2; arm++)
                {
                    s.ActiveNozzles[arm] = -1; s.ActiveKinds[arm] = ""; s.ActiveTargets[arm] = -1; s.ArmSteps[arm] = "";
                }
            }
        }
    }
}
