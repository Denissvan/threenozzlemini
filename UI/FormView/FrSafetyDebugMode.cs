using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// 调试模式窗体：急停输入屏蔽与门禁使能临时忽略。只在本次运行有效，开启期间禁止自动运行。
    /// </summary>
    public partial class FrSafetyDebugMode : Form
    {
        private static FrSafetyDebugMode instance;
        private readonly List<Row> rows = new List<Row>();
        private readonly List<DoorRow> doorRows = new List<DoorRow>();

        private sealed class DoorRow
        {
            public SafetyDebugMode.DoorGroup Key;
            public CheckBox Check;
            public Label State;
            public Func<bool> ConfiguredEnabled;
        }

        private sealed class Row
        {
            public SafetyDebugMode.EmgInput Key;
            public GPIO Input;
            public CheckBox Check;
            public Label State;
        }

        public FrSafetyDebugMode()
        {
            InitializeComponent();
        }

        public static void ShowSingle(IWin32Window owner)
        {
            if (instance == null || instance.IsDisposed)
            {
                instance = new FrSafetyDebugMode();
                instance.Show(owner);
            }
            else
            {
                if (instance.WindowState == FormWindowState.Minimized) instance.WindowState = FormWindowState.Normal;
                instance.Activate();
            }
        }

        /// <summary>无料空跑模拟测试结果切换（只影响空跑，不影响正常生产；软件重启后恢复默认“全部OK”）</summary>
        private void cmb_demores_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmb_demores.SelectedIndex < 0 || WS.DemoResultMode == cmb_demores.SelectedIndex) return;
            WS.DemoResultMode = cmb_demores.SelectedIndex;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, "【调试】无料空跑模拟测试结果切换为：" + cmb_demores.SelectedItem + "（下一轮测试结果起生效）");
        }

        private void chk_blindpick_CheckedChanged(object sender, EventArgs e)
        {
            if (UpDownLoad.ThreeNozzleBlindTrayPickEnabled == chk_blindpick.Checked) return;
            UpDownLoad.ThreeNozzleBlindTrayPickEnabled = chk_blindpick.Checked;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, "【调试】供料盘盲取" + (chk_blindpick.Checked ? "开启：取新料不拍上相机，按示教位置直接取" : "关闭：恢复拍照取料"));
        }

        private void chk_earlyprefetch_CheckedChanged(object sender, EventArgs e)
        {
            if (UpDownLoad.ThreeNozzleEarlyPrefetchEnabled == chk_earlyprefetch.Checked) return;
            UpDownLoad.ThreeNozzleEarlyPrefetchEnabled = chk_earlyprefetch.Checked;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, "【调试】三吸嘴提前预取" + (chk_earlyprefetch.Checked
                ? "开启：尾部卸料后为下一个工站提前取料"
                : "关闭：尾部卸料后直接回安全位，工站到位后再预取（下一次卸料起生效，本次运行有效）"));
        }

        /// <summary>放料前上相机进夹具飞拍：关闭 / 对比 / 启用（本次运行有效，不写配置）</summary>
        private void cmb_preplacefly_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmb_preplacefly.SelectedIndex < 0) return;
            var mode = (UpDownLoad.ThreeNozzlePrePlaceFlyMode)cmb_preplacefly.SelectedIndex;
            if (UpDownLoad.ThreeNozzlePrePlaceFly == mode) return;
            UpDownLoad.ThreeNozzlePrePlaceFly = mode;
            string note = "";
            if (mode != UpDownLoad.ThreeNozzlePrePlaceFlyMode.Off && !UpDownLoad.ThreeNozzleFlyActive)
                note = "；注意：三吸嘴飞拍当前不生效（" + UpDownLoad.ThreeNozzleFlyDisabledReason() + "），本项不起作用";
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("【调试】放料前上相机飞拍切换为：{0}，视觉任务={1}（下一次进夹具起生效）{2}",
                cmb_preplacefly.SelectedItem, UpDownLoad.ThreeNozzlePrePlaceFlyTask, note));
        }

        private void cmb_preplacetask_TextChanged(object sender, EventArgs e)
        {
            string task = (cmb_preplacetask.Text ?? "").Trim();
            if (task.Length == 0 || task == UpDownLoad.ThreeNozzlePrePlaceFlyTask) return;
            UpDownLoad.ThreeNozzlePrePlaceFlyTask = task;
        }

        /// <summary>候选任务：两条臂上相机已加载的视觉任务名</summary>
        private void LoadPrePlaceTaskNames()
        {
            var names = new List<string>();
            try
            {
                foreach (UpDownLoad ud in new[] { COM.UDLoad1, COM.UDLoad2 })
                {
                    if (ud == null || ud.upcam == null || ud.upcam.List_vs_task == null) continue;
                    foreach (var t in ud.upcam.List_vs_task)
                        if (t != null && !string.IsNullOrEmpty(t.TaskName) && !names.Contains(t.TaskName)) names.Add(t.TaskName);
                }
            }
            catch { }
            cmb_preplacetask.Items.Clear();
            foreach (string n in names) cmb_preplacetask.Items.Add(n);
            cmb_preplacetask.Text = UpDownLoad.ThreeNozzlePrePlaceFlyTask;
        }

        private void FrSafetyDebugMode_Load(object sender, EventArgs e)
        {
            cmb_demores.SelectedIndex = Math.Max(0, Math.Min(2, WS.DemoResultMode));
            chk_blindpick.Checked = UpDownLoad.ThreeNozzleBlindTrayPickEnabled;
            chk_earlyprefetch.Checked = UpDownLoad.ThreeNozzleEarlyPrefetchEnabled;
            LoadPrePlaceTaskNames();
            cmb_preplacefly.SelectedIndex = (int)UpDownLoad.ThreeNozzlePrePlaceFly;
            CheckBox[] checks = { chk_emg0, chk_emg1, chk_emg2, chk_emg3 };
            Label[] states = { lbl_emg0, lbl_emg1, lbl_emg2, lbl_emg3 };
            int i = 0;
            foreach (var item in SafetyDebugMode.EmgInputs)
            {
                var row = new Row { Key = item.Key, Input = item.Value, Check = checks[i], State = states[i] };
                row.Check.Text = item.Value == null ? item.Key.ToString() : item.Value.str_disc;
                // 只回显已生效的屏蔽项；新增门禁设置时不自动附带急停屏蔽。
                row.Check.Checked = (SafetyDebugMode.Ignored & item.Key) == item.Key;
                rows.Add(row);
                i++;
            }
            AddDoorRow(SafetyDebugMode.DoorGroup.UpDownLoad, chk_door_ud, lbl_door_ud, () => PT_SET.bEnUpDownDr);
            AddDoorRow(SafetyDebugMode.DoorGroup.TurnTable, chk_door_tt, lbl_door_tt, () => PT_SET.bEnTrayDr);
            AddDoorRow(SafetyDebugMode.DoorGroup.LeftLightBox, chk_door_left, lbl_door_left, () => PT_SET.bEnLBoxDr);
            AddDoorRow(SafetyDebugMode.DoorGroup.RightLightBox, chk_door_right, lbl_door_right, () => PT_SET.bEnRBoxDr);
            chk_mask_lb.Checked = SafetyDebugMode.LightBoxMasked;
            RefreshView();
            tmr_refresh.Start();
        }

        private void AddDoorRow(SafetyDebugMode.DoorGroup key, CheckBox check, Label state, Func<bool> configuredEnabled)
        {
            check.Checked = (SafetyDebugMode.IgnoredDoors & key) != 0;
            doorRows.Add(new DoorRow { Key = key, Check = check, State = state, ConfiguredEnabled = configuredEnabled });
        }

        private void FrSafetyDebugMode_FormClosed(object sender, FormClosedEventArgs e)
        {
            tmr_refresh.Stop();
            if (ReferenceEquals(instance, this)) instance = null;
        }

        private static bool ReadPressed(GPIO input, out GPIO.IO_STA raw)
        {
            // 与 IO 监控的 isON 使用同一个 Status 读取入口；一次采样同时用于原始值和急停判定。
            try { raw = input == null ? GPIO.IO_STA.ERR : input.Status; }
            catch { raw = GPIO.IO_STA.ERR; }
            return input != null && raw ==
                (input.card == null ? GPIO.IO_STA.IN_OFF : input.card.EmgActiveState);
        }

        private void tmr_refresh_Tick(object sender, EventArgs e)
        {
            RefreshView();
        }

        private void RefreshView()
        {
            SafetyDebugMode.EmgInput ignored = SafetyDebugMode.Ignored;
            foreach (Row row in rows)
            {
                GPIO.IO_STA raw;
                bool pressed = ReadPressed(row.Input, out raw);
                bool valid = raw == GPIO.IO_STA.IN_ON || raw == GPIO.IO_STA.IN_OFF;
                bool masked = (ignored & row.Key) == row.Key || SafetyDebugMode.IsMaskedByLightBox(row.Key);
                string activeLevel = row.Input != null && row.Input.card != null && row.Input.card.EmgActiveState == GPIO.IO_STA.IN_ON ? "ON" : "OFF";
                row.State.Text = (valid ? (pressed ? "急停" : "正常") + " IO:" + (raw == GPIO.IO_STA.IN_ON ? "ON" : "OFF") : "读取异常 IO:ERR")
                    + " 急停:" + activeLevel + (masked ? "（屏蔽）" : "");
                row.State.ForeColor = !valid ? Color.DarkOrange : pressed ? (masked ? Color.DarkOrange : Color.Red) : Color.Green;
            }
            foreach (DoorRow row in doorRows)
            {
                bool enabled = row.ConfiguredEnabled();
                bool ignoredDoor = (SafetyDebugMode.IgnoredDoors & row.Key) != 0;
                row.State.Text = (enabled ? "原配置启用" : "原配置未启用") + (ignoredDoor ? "（调试已忽略）" : "");
                row.State.ForeColor = ignoredDoor ? Color.DarkOrange : SystemColors.ControlText;
            }
            bool lbMasked = SafetyDebugMode.LightBoxMasked;
            lbl_mask_lb.Text = (PT_SET.LbEnConfigured ? "原配置启用" : "原配置未启用") + (lbMasked ? "（调试已屏蔽）" : "");
            lbl_mask_lb.ForeColor = lbMasked ? Color.DarkOrange : SystemColors.ControlText;
            bool active = SafetyDebugMode.IsActive;
            lbl_mode.Text = active
                ? string.Format("调试模式开启中（{0:HH:mm:ss} 起）\r\n{1}", SafetyDebugMode.ActivatedAt, SafetyDebugMode.CurrentDescription)
                : "调试模式未开启，按原配置检查急停、门禁和光箱。\r\n勾选后点击“应用调试设置”才生效。";
            lbl_mode.ForeColor = active ? Color.Red : Color.Green;
            btn_disable.Enabled = active;
        }

        private SafetyDebugMode.EmgInput SelectedInputs()
        {
            SafetyDebugMode.EmgInput inputs = SafetyDebugMode.EmgInput.None;
            foreach (Row row in rows)
                if (row.Check.Checked) inputs |= row.Key;
            return inputs;
        }

        private SafetyDebugMode.DoorGroup SelectedDoors()
        {
            SafetyDebugMode.DoorGroup doors = SafetyDebugMode.DoorGroup.None;
            foreach (DoorRow row in doorRows)
                if (row.Check.Checked) doors |= row.Key;
            return doors;
        }

        private static bool HasEngineerPermission(out string operatorName)
        {
            operatorName = "";
            try
            {
                User.ST_USER user = FrMain.frsuser.user1.cur_user; // 结构体，不会为 null
                operatorName = user.ToString();
                return user.pms >= User.PERMISSION.Engineer;
            }
            catch { return false; }
        }

        private void btn_enable_Click(object sender, EventArgs e)
        {
            string operatorName;
            if (!HasEngineerPermission(out operatorName))
            {
                MessageBox.Show(this, "需要工程师及以上权限才能进入调试模式。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SafetyDebugMode.EmgInput inputs = SelectedInputs();
            SafetyDebugMode.DoorGroup doors = SelectedDoors();
            bool maskLb = chk_mask_lb.Checked;
            string confirm =
                "即将应用以下调试设置：\r\n" + SafetyDebugMode.Describe(inputs, doors, maskLb) + "\r\n\r\n" +
                (inputs == SafetyDebugMode.EmgInput.None && doors == SafetyDebugMode.DoorGroup.None && !maskLb
                    ? "· 未选择任何屏蔽项，急停、门禁和光箱继续按原配置检查\r\n"
                    : "· 所选急停输入或门禁组将不再参与相应的软件检查\r\n") +
                (maskLb ? "· 屏蔽左右光箱：本次运行按“左右光箱未启用”处理——不检查左/右光箱板卡和门禁，系统复位跳过光箱，转台复位/转动不再检查光箱位置。请确认光箱机构不会与转台干涉\r\n" : "") +
                "· 不修改原门禁和光箱启用配置；退出调试模式后恢复原配置检查\r\n" +
                "· 硬件急停回路和软限位不受这些选项影响\r\n" +
                "· 调试模式期间：无料空跑/有料空跑允许自动运行；正常生产需每次启动时弹窗确认（仅用于与测试软件联调，被屏蔽的急停/门禁不会让软件停机）\r\n" +
                "· 自动运行中不能开启或关闭调试模式；软件重启后自动关闭\r\n\r\n" +
                "仅用于机台结构不全时的上下料调试。是否进入调试模式？";
            if (MessageBox.Show(this, confirm, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            string error;
            if (!SafetyDebugMode.Enable(inputs, doors, maskLb, operatorName, out error))
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RefreshView();
        }

        /// <summary>
        /// 三吸嘴复位（不回零）：解除“待对账”锁和三吸嘴调度故障。
        /// 两臂全部 Z 抬起 → 回安全位 → 按真空自动对账 → 清故障；轴报警/未回零时拒绝，改用上下料回零。
        /// 现在异常停机后直接按开始也会自动执行同样的复位（COM.ThreeNozzleAutoResetBeforeRun）。
        /// </summary>
        private void btn_tnreset_Click(object sender, EventArgs e)
        {
            if (SafetyDebugMode.AutoRunActive)
            {
                MessageBox.Show(this, "请先停止运行，等上下料线程结束后再复位。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this,
                "三吸嘴复位（不回零）：\r\n" +
                "1. 两个上下料臂的全部 Z 抬到最高；\r\n" +
                "2. 两臂回安全位（会移动 X/Y，请确认运动范围内无人、无障碍）；\r\n" +
                "3. 按真空自动对账（与双吸嘴一致）：真空无料的吸嘴清除物料记录，真空有料但无记录的吸嘴关真空（料会落下）；\r\n" +
                "4. 清除“待对账”锁和三吸嘴调度故障。仍吸着且有记录的物料，启动后先落盘。\r\n\r\n" +
                "轴报警、急停断过伺服、撞机或怀疑丢步时不要用这个，请用上下料回零。是否继续？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            btn_tnreset.Enabled = false;
            EM_RES res;
            try { res = COM.ThreeNozzleResetWithoutHome(); }
            finally { btn_tnreset.Enabled = true; }
            if (res == EM_RES.OK)
                MessageBox.Show(this, "三吸嘴复位完成，可以按开始运行。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, "三吸嘴复位未完成：" + res + "\r\n请查看日志（可能抬Z/回安全位失败，或上下料任务未退出）。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void btn_disable_Click(object sender, EventArgs e)
        {
            if (SafetyDebugMode.AutoRunActive)
            {
                MessageBox.Show(this, "自动运行中不能关闭调试模式（关闭会让光箱启用、急停/门禁判断在运行中途突然变化），请先停止运行。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SafetyDebugMode.Disable("操作员手动关闭");
            foreach (Row row in rows) row.Check.Checked = false;
            foreach (DoorRow row in doorRows) row.Check.Checked = false;
            chk_mask_lb.Checked = false;
            RefreshView();
        }
    }
}
