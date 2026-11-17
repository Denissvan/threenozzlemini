using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    public partial class FrMain
    {
        private readonly ToolStripComboBox[] manualNozzleSelectors = new ToolStripComboBox[2];
        private readonly Dictionary<ToolStripButton, AXIS> manualPressedAxes = new Dictionary<ToolStripButton, AXIS>();

        private void InitializeManualNozzles()
        {
            ToolStrip[] strips = { ts_manual, ts_manual2 };
            for (int arm = 0; arm < strips.Length; arm++)
            {
                ToolStrip strip = strips[arm];
                var selector = new ToolStripComboBox { Name = "manualNozzle" + arm,
                    DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 118,
                    Font = new Font("Microsoft Sans Serif", 11F) };
                selector.Items.AddRange(new object[] { "N1", "N2", "N3" });
                selector.SelectedIndex = 0;
                manualNozzleSelectors[arm] = selector;
                strip.Items.Insert(0, new ToolStripLabel(VAR.IsChinese ? "臂" + (arm + 1) + " 实际轴" : "Arm " + (arm + 1) + " axes"));
                strip.Items.Insert(1, selector);
                foreach (string suffix in new[] { "un", "up" })
                {
                    var button = new ToolStripButton { Name = "tsb_" + (arm + 1) + suffix,
                        Text = "U " + (suffix == "up" ? "+" : "-"), DisplayStyle = ToolStripItemDisplayStyle.Text };
                    button.MouseDown += tsb_xn_MouseDown;
                    button.MouseUp += tsb_xn_MouseUp;
                    strip.Items.Insert(strip.Items.Count - 1, button);
                }
                // Fit selected-nozzle Z/U and the original fixed U1/U2 controls in the existing strip.
                foreach (ToolStripItem item in strip.Items)
                {
                    var button = item as ToolStripButton;
                    if (button == null) continue;
                    button.DisplayStyle = ToolStripItemDisplayStyle.Text;
                    button.AutoSize = false;
                    button.Size = new Size(118, 36);
                    button.Font = new Font("Microsoft Sans Serif", 11F);
                }
                int selectedArm = arm;
                selector.SelectedIndexChanged += (s, e) => UpdateManualNozzleLabels(selectedArm);
                UpdateManualNozzleLabels(arm);
            }
            // 三吸嘴空跑入口：无转盘、无物料时只验证取放料动作与节拍。
            var dryRun = new ToolStripButton { Name = "tsb_three_nozzle_dry_run",
                Text = VAR.IsChinese ? "三吸嘴空跑" : "3-Nozzle dry run",
                DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = false,
                Size = new Size(118, 36), Font = new Font("Microsoft Sans Serif", 11F) };
            dryRun.Click += (s, e) => FrThreeNozzleDryRun.ShowSingle(this);
            ts_manual.Items.Add(dryRun);
            // 调试模式入口：开启时按钮变红，提示急停或门禁检查已调整。
            var debugMode = new ToolStripButton { Name = "tsb_safety_debug_mode",
                Text = VAR.IsChinese ? "调试模式" : "Debug mode",
                DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = false,
                Size = new Size(118, 36), Font = new Font("Microsoft Sans Serif", 11F) };
            debugMode.Click += (s, e) => FrSafetyDebugMode.ShowSingle(this);
            ts_manual.Items.Add(debugMode);
            SafetyDebugMode.Changed += () =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() => UpdateSafetyDebugIndicator(debugMode)));
            };
            UpdateSafetyDebugIndicator(debugMode);
        }

        private static void UpdateSafetyDebugIndicator(ToolStripButton button)
        {
            bool active = SafetyDebugMode.IsActive;
            button.BackColor = active ? Color.Red : SystemColors.Control;
            button.ForeColor = active ? Color.White : SystemColors.ControlText;
            button.Text = active ? (VAR.IsChinese ? "调试模式中" : "DEBUG ON") : (VAR.IsChinese ? "调试模式" : "Debug mode");
            button.ToolTipText = active ? SafetyDebugMode.CurrentDescription : "";
        }

        private void UpdateManualNozzleLabels(int arm)
        {
            ToolStrip strip = arm == 0 ? ts_manual : ts_manual2;
            int slot = manualNozzleSelectors[arm].SelectedIndex;
            foreach (ToolStripItem item in strip.Items)
            {
                string name = item.Name ?? "";
                string prefix = "tsb_" + (arm + 1);
                if (!name.StartsWith(prefix)) continue;
                string command = name.Substring(prefix.Length);
                if (command == "zp" || command == "zn" || command == "up" || command == "un")
                    item.Text = "N" + (slot + 1) + " " + char.ToUpperInvariant(command[0]) + (command[1] == 'p' ? "+" : "-");
                else if (command.StartsWith("u1") || command.StartsWith("u2"))
                    item.Text = "N" + command[1] + " U" + (command[2] == 'p' ? "+" : "-");
                AXIS mapped = ResolveManualAxis(name, slot);
                item.ToolTipText = mapped == null ? (VAR.IsChinese ? "轴未映射" : "Axis unavailable") : mapped.disc;
            }
        }

        internal static AXIS ResolveManualAxis(string name, int selectedSlot)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("tsb_") || name.Length < 7) return null;
            int arm = name[4] - '1';
            if (arm < 0 || arm > 1) return null;
            string command = name.Substring(5);
            if (command == "xp" || command == "xn") return arm == 0 ? MT.AXIS_UDL1_X : MT.AXIS_UDL2_X;
            if (command == "yp" || command == "yn") return arm == 0 ? MT.AXIS_UDL1_Y : MT.AXIS_UDL2_Y;
            bool z = command == "zp" || command == "zn";
            bool u = command == "up" || command == "un";
            int slot = selectedSlot;
            if (command == "u1p" || command == "u1n") { u = true; slot = 0; }
            if (command == "u2p" || command == "u2n") { u = true; slot = 1; }
            if ((!z && !u) || slot < 0 || slot > 2 || COM.List_UDLoad == null || COM.List_UDLoad.Count <= arm) return null;
            var ud = COM.List_UDLoad[arm];
            if (ud == null || ud.list_xt == null || ud.list_xt.Count <= slot || ud.list_xt[slot] == null) return null;
            XT nozzle = ud.list_xt[slot];
            return z ? nozzle.ax_z : nozzle.ax_u;
        }
    }
}
