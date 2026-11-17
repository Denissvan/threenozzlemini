using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace UI
{
    /// <summary>
    /// 三吸嘴空跑的模拟显示：两臂各三个吸嘴、夹具 16 穴（k 与 k+8 上下对齐）、供料盘、OK 盘。
    /// 只负责画图，数据来自 ThreeNozzleDryRun.SimChanged 的快照。
    /// 颜色：绿=新料，橙=旧料，白=空；蓝框=当前动作的吸嘴，红框=当前动作的目标穴。
    /// </summary>
    public partial class ThreeNozzleSimView : UserControl
    {
        private ThreeNozzleSimSnapshot snap;
        private static readonly Color NewColor = Color.FromArgb(120, 200, 120);
        private static readonly Color OldColor = Color.FromArgb(240, 170, 80);
        private static readonly Color NgColor = Color.FromArgb(235, 110, 110);
        private static readonly Color EmptyColor = Color.White;
        private static readonly Color OffColor = Color.FromArgb(225, 225, 225);
        private static readonly Color ActiveNozzleColor = Color.RoyalBlue;
        private static readonly Color ActiveTargetColor = Color.Red;

        public ThreeNozzleSimView()
        {
            InitializeComponent();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public void ShowSnapshot(ThreeNozzleSimSnapshot snapshot)
        {
            snap = snapshot;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (snap == null)
            {
                TextRenderer.DrawText(g, "选择工站和臂后显示模拟夹具、料盘和吸嘴", Font, ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            int w = ClientSize.Width, h = ClientSize.Height;
            const int pad = 8;
            using (var bold = new Font(Font, FontStyle.Bold))
            {
                // 1. 当前步骤
                var stepRect = new Rectangle(pad, pad, w - 2 * pad, 40);
                using (var b = new SolidBrush(Color.FromArgb(245, 248, 255))) g.FillRectangle(b, stepRect);
                g.DrawRectangle(Pens.SteelBlue, stepRect);
                string stepText = snap.ArmSteps != null && snap.ArmSteps.Count(t => !string.IsNullOrEmpty(t)) == 2
                    ? "臂1：" + snap.ArmSteps[0] + "\n臂2：" + snap.ArmSteps[1]
                    : (snap.Cycle > 0 ? "第" + snap.Cycle + "轮  " : "") + snap.StepText;
                TextRenderer.DrawText(g, stepText, bold, stepRect, Color.Black,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                int y = stepRect.Bottom + pad;

                // 2. 两臂吸嘴
                int armH = 74;
                int armW = (w - 3 * pad) / 2;
                for (int arm = 0; arm < 2; arm++)
                    DrawArm(g, bold, new Rectangle(pad + arm * (armW + pad), y, armW, armH), arm);
                y += armH + pad;

                // 3. 夹具
                int fixH = Math.Max(96, (int)((h - y) * 0.36));
                DrawFixture(g, bold, new Rectangle(pad, y, w - 2 * pad, fixH));
                y += fixH + pad;

                // 4. 料盘
                int trayH = h - y - 26;
                if (snap.Live)
                {
                    // 系统运行实时：供料盘 / OK盘 / NG盘 三个当前盘
                    int tw = (w - 4 * pad) / 3;
                    DrawTray(g, bold, new Rectangle(pad, y, tw, trayH), "供料盘", snap.Feed, snap.FeedRows, snap.FeedCols, "feed");
                    DrawTray(g, bold, new Rectangle(2 * pad + tw, y, tw, trayH), "OK盘", snap.Ok, snap.OkRows, snap.OkCols, "ok");
                    DrawTray(g, bold, new Rectangle(3 * pad + 2 * tw, y, tw, trayH), "NG盘", snap.Ng, snap.NgRows, snap.NgCols, "ng");
                }
                else
                {
                    int trayW = (w - 3 * pad) / 2;
                    DrawTray(g, bold, new Rectangle(pad, y, trayW, trayH), "供料盘（取新料）", snap.Feed, snap.FeedRows, snap.FeedCols, "feed");
                    DrawTray(g, bold, new Rectangle(2 * pad + trayW, y, trayW, trayH), "OK盘（落旧料）", snap.Ok, snap.OkRows, snap.OkCols, "ok");
                }

                // 5. 图例
                DrawLegend(g, new Rectangle(pad, h - 22, w - 2 * pad, 18));
            }
        }

        private void DrawArm(Graphics g, Font bold, Rectangle r, int arm)
        {
            bool selected = snap.ArmSelected[arm];
            bool activeArm = snap.ActiveArm == arm || (snap.ActiveNozzles != null && snap.ActiveNozzles[arm] >= 0);
            using (var b = new SolidBrush(selected ? Color.White : OffColor)) g.FillRectangle(b, r);
            using (var p = new Pen(activeArm ? ActiveNozzleColor : Color.Gray, activeArm ? 2 : 1)) g.DrawRectangle(p, r);
            TextRenderer.DrawText(g, snap.ArmNames[arm] + (selected ? "" : "（本次不跑）"), bold,
                new Rectangle(r.X + 4, r.Y + 2, r.Width - 8, 20), Color.Black, TextFormatFlags.Left);
            int cw = (r.Width - 16) / 3;
            for (int i = 0; i < 3; i++)
            {
                var cell = new Rectangle(r.X + 4 + i * (cw + 4), r.Y + 24, cw, r.Height - 28);
                SimSlot slot = snap.Nozzles[arm][i];
                bool active = snap.ActiveNozzles != null ? snap.ActiveNozzles[arm] == i : activeArm && snap.ActiveNozzle == i;
                DrawSlot(g, cell, "N" + (i + 1), slot, selected, active ? ActiveNozzleColor : Color.DimGray, active ? 3 : 1);
            }
        }

        private void DrawFixture(Graphics g, Font bold, Rectangle r)
        {
            g.DrawRectangle(Pens.Gray, r);
            TextRenderer.DrawText(g, "夹具 " + snap.StationName + (snap.Live ? "（上排 k，下排 k+8）" : "（上排 k，下排 k+8，同列为一组）"), bold,
                new Rectangle(r.X + 4, r.Y + 2, r.Width - 8, 20), Color.Black, TextFormatFlags.Left);
            int n = snap.Fixture.Length;
            if (n == 0) return;
            int cols = (n + 1) / 2;
            int cw = (r.Width - 8 - (cols - 1) * 3) / cols;
            int ch = (r.Height - 30) / 2;
            for (int i = 0; i < n; i++)
            {
                int row = i < cols ? 0 : 1;
                int col = i < cols ? i : i - cols;
                var cell = new Rectangle(r.X + 4 + col * (cw + 3), r.Y + 24 + row * (ch + 3), cw, ch - 3);
                bool inRange = snap.FixtureArm[i] >= 0;
                bool active = IsActiveTarget("fixture", i + 1);
                string title = "穴" + (i + 1) + (snap.Live ? "" : snap.FixtureArm[i] == 0 ? " 臂1" : snap.FixtureArm[i] == 1 ? " 臂2" : snap.FixtureArm[i] == 2 ? " 双臂" : "");
                if (!snap.FixtureEnabled[i]) title += " 禁用";
                DrawSlot(g, cell, title, snap.Fixture[i], inRange, active ? ActiveTargetColor : Color.DimGray, active ? 3 : 1);
            }
        }

        private void DrawTray(Graphics g, Font bold, Rectangle r, string title, System.Collections.Generic.List<SimTrayCell> cells,
            int rows, int cols, string kind)
        {
            g.DrawRectangle(Pens.Gray, r);
            int left = cells.Count(c => !c.Disabled && c.Content.Kind != SimPart.Empty);
            TextRenderer.DrawText(g, title + "  " + (kind == "feed" ? "剩余 " : "已放 ") + left, bold,
                new Rectangle(r.X + 4, r.Y + 2, r.Width - 8, 20), Color.Black, TextFormatFlags.Left);
            if (cells.Count == 0 || rows <= 0 || cols <= 0)
            {
                TextRenderer.DrawText(g, "料盘模板无穴位", Font, r, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            int cw = Math.Max(4, (r.Width - 8) / cols);
            int ch = Math.Max(4, (r.Height - 28) / rows);
            bool showText = cw >= 26 && ch >= 16;
            foreach (SimTrayCell c in cells)
            {
                var cell = new Rectangle(r.X + 4 + c.Col * cw, r.Y + 24 + c.Row * ch, cw - 2, ch - 2);
                bool active = IsActiveTarget(kind, c.Idx);
                Color fill = c.Disabled ? OffColor : Fill(c.Content.Kind);
                using (var b = new SolidBrush(fill)) g.FillRectangle(b, cell);
                using (var p = new Pen(active ? ActiveTargetColor : Color.Silver, active ? 3 : 1)) g.DrawRectangle(p, cell);
                if (showText)
                    TextRenderer.DrawText(g, c.Content.Kind == SimPart.Empty ? c.Idx.ToString() : c.Content.Label, Font, cell,
                        Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private bool IsActiveTarget(string kind, int target)
        {
            if (snap.ActiveKinds != null)
                for (int arm = 0; arm < 2; arm++)
                    if (snap.ActiveKinds[arm] == kind && snap.ActiveTargets[arm] == target) return true;
            return snap.ActiveKind == kind && snap.ActiveTarget == target;
        }

        private void DrawSlot(Graphics g, Rectangle cell, string title, SimSlot slot, bool enabled, Color border, int borderWidth)
        {
            Color fill = !enabled ? OffColor : Fill(slot.Kind);
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, cell);
            using (var p = new Pen(border, borderWidth)) g.DrawRectangle(p, cell);
            string content = slot.Kind == SimPart.Empty ? "空" : slot.Label;
            TextRenderer.DrawText(g, title + "\n" + content, Font, cell, Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        private static Color Fill(SimPart kind)
        {
            return kind == SimPart.New ? NewColor : kind == SimPart.Old ? OldColor : kind == SimPart.Ng ? NgColor : EmptyColor;
        }

        private void DrawLegend(Graphics g, Rectangle r)
        {
            int x = r.X;
            var items = snap.Live
                ? new[] {
                    Tuple.Create(NewColor, "待测", Color.Silver), Tuple.Create(OldColor, "OK", Color.Silver),
                    Tuple.Create(NgColor, "NG", Color.Silver), Tuple.Create(EmptyColor, "空", Color.Silver),
                    Tuple.Create(OffColor, "禁用", Color.Silver),
                    Tuple.Create(EmptyColor, "当前吸嘴", ActiveNozzleColor), Tuple.Create(EmptyColor, "当前目标", ActiveTargetColor) }
                : new[] {
                    Tuple.Create(NewColor, "新料", Color.Silver), Tuple.Create(OldColor, "旧料", Color.Silver),
                    Tuple.Create(EmptyColor, "空", Color.Silver), Tuple.Create(OffColor, "不参与", Color.Silver),
                    Tuple.Create(EmptyColor, "当前吸嘴", ActiveNozzleColor), Tuple.Create(EmptyColor, "当前目标", ActiveTargetColor) };
            foreach (var item in items)
            {
                var box = new Rectangle(x, r.Y + 3, 14, 12);
                using (var b = new SolidBrush(item.Item1)) g.FillRectangle(b, box);
                using (var p = new Pen(item.Item3, item.Item3 == Color.Silver ? 1 : 2)) g.DrawRectangle(p, box);
                TextRenderer.DrawText(g, item.Item2, Font, new Point(x + 18, r.Y + 1), Color.Black);
                x += 18 + TextRenderer.MeasureText(item.Item2, Font).Width + 12;
            }
        }
    }
}
