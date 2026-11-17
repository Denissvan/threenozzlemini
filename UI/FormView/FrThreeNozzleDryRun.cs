using System;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// 三吸嘴空跑窗体：仅有上下料部分（无转盘、无物料）时验证三吸嘴取放料动作与节拍。
    /// </summary>
    public partial class FrThreeNozzleDryRun : Form
    {
        private static FrThreeNozzleDryRun instance;
        private const int MaxLogLength = 200000;

        public FrThreeNozzleDryRun()
        {
            InitializeComponent();
            // 配方已统一使用实际轴坐标，此旧选项不再参与坐标换算。
            chk_nocal.Visible = false;
        }

        public static void ShowSingle(IWin32Window owner)
        {
            if (instance == null || instance.IsDisposed)
            {
                instance = new FrThreeNozzleDryRun();
                instance.Show(owner);
            }
            else
            {
                if (instance.WindowState == FormWindowState.Minimized) instance.WindowState = FormWindowState.Normal;
                instance.Activate();
            }
        }

        private void FrThreeNozzleDryRun_Load(object sender, EventArgs e)
        {
            cmb_station.Items.Clear();
            if (COM.list_ws != null)
                foreach (WS ws in COM.list_ws)
                    cmb_station.Items.Add(ws == null ? "-" : ws.disc);
            if (cmb_station.Items.Count > 0) cmb_station.SelectedIndex = 0;
            cmb_arm.SelectedIndex = 2;
            cmb_range.SelectedIndex = 0;
            cmb_scene.SelectedIndex = 0;
            ThreeNozzleDryRun.Log += OnDryRunLog;
            ThreeNozzleDryRun.SimChanged += OnSimChanged;
            SafetyDebugMode.Changed += OnSafetyDebugModeChanged;
            MT.UdAxisHomeDebugChanged += OnUdAxisHomeDebugChanged;
            SyncAxHomeCheck();
            UpdateDebugLabel();
            RefreshSimPreview();
            SetRunning(ThreeNozzleDryRun.IsRunning);
        }

        private void FrThreeNozzleDryRun_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ThreeNozzleDryRun.IsRunning) return;
            MessageBox.Show(this, "空跑运行中，请先点击「停止」并等待轴停止后再关闭。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }

        private void FrThreeNozzleDryRun_FormClosed(object sender, FormClosedEventArgs e)
        {
            tmr_live.Stop();
            ThreeNozzleDryRun.Log -= OnDryRunLog;
            ThreeNozzleDryRun.SimChanged -= OnSimChanged;
            SafetyDebugMode.Changed -= OnSafetyDebugModeChanged;
            MT.UdAxisHomeDebugChanged -= OnUdAxisHomeDebugChanged;
            MT.DisableSingleArmDebug("关闭三吸嘴空跑窗口");
            MT.DisableUdAxisHomeDebug("关闭三吸嘴空跑窗口");
            if (ReferenceEquals(instance, this)) instance = null;
        }

        private ThreeNozzleDryRunOptions ReadOptions()
        {
            return new ThreeNozzleDryRunOptions
            {
                StationIndex = cmb_station.SelectedIndex,
                ArmMode = cmb_arm.SelectedIndex,
                AllPockets = cmb_range.SelectedIndex == 1,
                Cycles = (int)num_cycles.Value,
                ZMarginMm = (double)num_margin.Value,
                DwellMs = (int)num_dwell.Value,
                ZDescend = chk_zdescend.Checked,
                IgnoreZCalibration = true,
                FixedDescent = chk_fixed.Checked,
                Scene = cmb_scene.SelectedIndex == 1 ? DryRunScene.UnloadOnly : DryRunScene.LoadOrExchange,
                CameraPass = chk_campass.Checked,
                ReturnToStandby = chk_return.Checked,
                PrefetchAhead = chk_prefetch.Checked,
            };
        }

        private void btn_start_Click(object sender, EventArgs e)
        {
            ThreeNozzleDryRunOptions options = ReadOptions();
            string error = ThreeNozzleDryRun.PreCheck(options);
            if (error != null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string confirm = string.Format(
                "即将空跑：{0}，{1}，{2}。\r\n\r\n" +
                "· 不开真空、不触发相机、不改变物料和料盘数据\r\n" +
                "· Z {3}\r\n" +
                "· 料仓只移动料盘X轴，不升降料盘\r\n" +
                (options.PrefetchAhead ? "· 预取提前：每轮先在料盘区取第一组新料并回安全位，Y 超出 SafeTrayMaxY 即中止\r\n" : "") +
                "\r\n" +
                "请确认：运行速度已调低、机台内无人员和异物、急停可随时按下。\r\n是否开始？",
                cmb_station.Text + "，" + cmb_scene.Text, cmb_arm.Text, cmb_range.Text,
                !options.ZDescend ? "不下探（仅XY/U/料盘X）" :
                options.FixedDescent ? "固定下探：从Z=0向下 " + options.ZMarginMm.ToString("0.0") + " mm 后回升（不看示教高度）" :
                 "在配方实际 Z 轴坐标上方预留 " + options.ZMarginMm.ToString("0.0") + " mm 后回升（不加测高补偿）");
            if (MessageBox.Show(this, confirm, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            txt_log.Clear();
            chk_live.Checked = false;   // 窗口空跑用模拟显示，退出实时显示
            if (!ThreeNozzleDryRun.Start(options, OnFinished, out error))
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetRunning(true);
        }

        private void btn_home_Click(object sender, EventArgs e)
        {
            if (ThreeNozzleDryRun.IsRunning) return;
            if (SafetyDebugMode.AutoRunActive)
            {
                MessageBox.Show(this, "系统自动运行中（含暂停），不能单独回零上下料。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // 回零范围跟随「臂」选项：仅臂1 / 仅臂2 单独回零（单臂调试），臂1后臂2 = 两臂一起回零。
            int armSel = cmb_arm.SelectedIndex;
            int armMask = armSel == 0 ? 1 : armSel == 1 ? 2 : 3;
            if (armMask == 3)
            {
                if (MessageBox.Show(this,
                    "仅回零两个上下料臂和料仓轴（先两臂 Z1-Z3，再 X/Y/U 和料盘轴），跳过转盘、OTP 和光箱。\r\n" +
                    "回零后系统不会进入就绪状态，只用于上下料调试和空跑。\r\n\r\n是否开始？",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                MT.DisableSingleArmDebug("两臂一起回零");
            }
            else
            {
                int active = armSel, parked = 1 - armSel;
                if (MessageBox.Show(this, string.Format(
                    "单臂调试：只回零上下料模块{0}（Z1-Z3 → U/X/Y）和料仓轴，上下料模块{1} 不动。\r\n\r\n" +
                    "上下料模块{1} 若未回零，软件无法知道它的位置，双臂防撞会按“它停在 X 原点侧”计算。请先人工确认：\r\n" +
                    "1. 上下料模块{1} 已手动推到 X 原点（外侧）一端；\r\n" +
                    "2. 上下料模块{1} 三个 Z 已全部抬到最高、不在料盘和夹具上方；\r\n" +
                    "3. 上下料模块{0} 和料盘的运动范围内没有它的任何部件。\r\n\r\n" +
                    "确认后进入单臂调试（关闭本窗口或系统复位时自动退出，期间禁止自动运行）。是否继续？",
                    active + 1, parked + 1),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                MT.EnableSingleArmDebug(active);
            }
            btn_home.Enabled = btn_start.Enabled = false;
            lbl_status.Text = "上下料回零中…";
            EM_RES result;
            try { result = COM.HomeUpDownLoadOnly(armMask); }
            finally { btn_home.Enabled = btn_start.Enabled = !ThreeNozzleDryRun.IsRunning; }
            lbl_status.Text = "上下料回零: " + result;
            if (result != EM_RES.OK)
                MessageBox.Show(this, "上下料回零未完成：" + result + "\r\n请查看报警日志。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // 选项变化时（不在运行中）重画初始模拟：哪些夹具穴参与、由哪个臂负责、料盘大小
        private void OnSimOptionChanged(object sender, EventArgs e)
        {
            RefreshSimPreview();
        }

        private void RefreshSimPreview()
        {
            if (chk_live.Checked) return;
            if (ThreeNozzleDryRun.IsRunning || cmb_station.SelectedIndex < 0 || cmb_arm.SelectedIndex < 0 || cmb_range.SelectedIndex < 0) return;
            simView.ShowSnapshot(ThreeNozzleDryRun.Preview(ReadOptions()));
        }

        private void btn_simclear_Click(object sender, EventArgs e)
        {
            ThreeNozzleSimSnapshot snap = ThreeNozzleDryRun.ResetSimFixture(ReadOptions(), false);
            if (snap != null) simView.ShowSnapshot(snap);
        }

        private void btn_simfill_Click(object sender, EventArgs e)
        {
            ThreeNozzleSimSnapshot snap = ThreeNozzleDryRun.ResetSimFixture(ReadOptions(), true);
            if (snap != null) simView.ShowSnapshot(snap);
        }

        private void OnSimChanged(ThreeNozzleSimSnapshot snapshot)
        {
            if (IsDisposed || !IsHandleCreated || chk_live.Checked) return;
            BeginInvoke(new Action(() => simView.ShowSnapshot(snapshot)));
        }

        private void chk_fixed_CheckedChanged(object sender, EventArgs e)
        {
            // 固定下探时“预留距离”的含义变成“从Z=0向下的距离”
            lbl_margin.Text = chk_fixed.Checked ? "Z下探(mm)" : "Z预留(mm)";
            chk_nocal.Enabled = !chk_fixed.Checked;
        }

        // ===== 允许上下料 XY 单独回零（调试）=====
        private bool syncingAxHome;

        private void chk_axhome_CheckedChanged(object sender, EventArgs e)
        {
            if (syncingAxHome) return;
            if (!chk_axhome.Checked)
            {
                MT.DisableUdAxisHomeDebug("取消勾选");
                return;
            }
            if (MessageBox.Show(this,
                "开启后，可在「系统-轴」列表里对上下料1/2 的 X、Y 单独点「回零」。\r\n\r\n" +
                "仍然生效的保护：\r\n" +
                "· 本臂三个 Z（Z1-Z3）必须已回零并停在安全高度，否则 X/Y 不动；\r\n" +
                "· 门禁 / 急停、两臂都已回零时的双臂防撞距离。\r\n\r\n" +
                "放开的检查：X 回零时不再要求“上下料 X 已复位”（回零中的轴本身就未复位）。\r\n" +
                "另一臂 X 未回零时软件不知道它的位置，回零前请人工确认 X 原点方向（外侧）没有另一臂和其它障碍。\r\n\r\n" +
                "只在本次运行有效，开启期间禁止自动运行；关闭本窗口或系统复位时自动关闭。是否开启？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                SyncAxHomeCheck();
                return;
            }
            MT.EnableUdAxisHomeDebug();
        }

        private void OnUdAxisHomeDebugChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(SyncAxHomeCheck));
        }

        private void SyncAxHomeCheck()
        {
            syncingAxHome = true;
            try
            {
                chk_axhome.Checked = MT.UdAxisHomeDebug;
                chk_axhome.ForeColor = MT.UdAxisHomeDebug ? System.Drawing.Color.Red : System.Drawing.SystemColors.ControlText;
            }
            finally { syncingAxHome = false; }
        }

        private void btn_debug_Click(object sender, EventArgs e)
        {
            FrSafetyDebugMode.ShowSingle(this);
        }

        private void OnSafetyDebugModeChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(UpdateDebugLabel));
        }

        private void UpdateDebugLabel()
        {
            bool active = SafetyDebugMode.IsActive;
            lbl_debug.Text = active
                ? "调试模式开启中，急停屏蔽：" + SafetyDebugMode.Describe(SafetyDebugMode.Ignored)
                : "调试模式：未开启";
            lbl_debug.ForeColor = active ? System.Drawing.Color.Red : System.Drawing.SystemColors.ControlText;
        }

        // ===== 跟随系统运行（实时）：系统页无料空跑/正常运行时，按真实物料记录显示吸嘴、上料位夹具、三个料盘 =====
        private void chk_live_CheckedChanged(object sender, EventArgs e)
        {
            if (chk_live.Checked && ThreeNozzleDryRun.IsRunning)
            {
                chk_live.Checked = false;
                return;
            }
            tmr_live.Enabled = chk_live.Checked;
            if (chk_live.Checked) RefreshLive();
            else RefreshSimPreview();
        }

        private void tmr_live_Tick(object sender, EventArgs e)
        {
            if (ThreeNozzleDryRun.IsRunning) { chk_live.Checked = false; return; }
            RefreshLive();
        }

        private void RefreshLive()
        {
            ThreeNozzleSimSnapshot snap = null;
            try { snap = ThreeNozzleDryRun.BuildLiveSnapshot(); }
            catch { /* 运行线程正在改物料列表时偶发，下一拍再刷 */ }
            if (snap != null) simView.ShowSnapshot(snap);
        }

        private void btn_stop_Click(object sender, EventArgs e)
        {
            ThreeNozzleDryRun.Stop();
        }

        private void OnFinished(bool ok, string summary)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                SetRunning(false);
                lbl_status.Text = summary;
            }));
        }

        private void OnDryRunLog(string line)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (txt_log.TextLength > MaxLogLength) txt_log.Clear();
                txt_log.AppendText(line + Environment.NewLine);
            }));
        }

        private void SetRunning(bool running)
        {
            btn_start.Enabled = !running;
            btn_stop.Enabled = running;
            btn_home.Enabled = !running;
            grp_params.Enabled = !running;
            chk_live.Enabled = !running;
            lbl_status.Text = running ? "空跑运行中…" : "就绪";
        }
    }
}
