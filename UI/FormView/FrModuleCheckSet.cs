using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using UI.ModuleCheck;

namespace UI
{
    /// <summary>
    /// 模组自动点检设置窗体（与“环境点检”设置分开）。
    /// 保存后只影响下一次任务；当前任务按创建时的参数快照执行。
    /// </summary>
    public partial class FrModuleCheckSet : Form
    {
        private static FrModuleCheckSet instance;

        public FrModuleCheckSet()
        {
            InitializeComponent();
        }

        public static void ShowSingle(IWin32Window owner)
        {
            if (instance == null || instance.IsDisposed)
            {
                instance = new FrModuleCheckSet();
                instance.Show(owner);
            }
            else
            {
                if (instance.WindowState == FormWindowState.Minimized) instance.WindowState = FormWindowState.Normal;
                instance.Activate();
            }
        }

        private void FrModuleCheckSet_Load(object sender, EventArgs e)
        {
            LoadFrom(ModuleCheckManager.Instance.Settings);
            dtp_time.Value = DateTime.Today.AddHours(8);
            RefreshStatus();
            tmr_refresh.Start();
        }

        private void FrModuleCheckSet_FormClosed(object sender, FormClosedEventArgs e)
        {
            tmr_refresh.Stop();
        }

        private void LoadFrom(ModuleCheckSettings s)
        {
            chk_enabled.Checked = s.Enabled;
            cmb_station.SelectedIndex = Math.Max(0, Math.Min(3, s.TargetStation));
            num_advance.Value = Clamp(s.AdvanceMinutes, num_advance);
            num_retry.Value = Clamp(s.RetryCount, num_retry);
            num_temp.Value = Clamp(s.CheckModeTemp, num_temp);
            num_timeout.Value = Clamp(s.ModeSwitchTimeoutSec, num_timeout);
            chk_borrow.Checked = s.BorrowFeedTray;
            lst_times.Items.Clear();
            foreach (var t in s.PlanTimes) lst_times.Items.Add(t);
            dgv_samples.Rows.Clear();
            foreach (var slot in s.Samples.OrderBy(x => x.FixturePocket)) AddRow(slot.TrayPocketIdx, slot.FixturePocket);
            RenumberRows();
        }

        private static decimal Clamp(int v, NumericUpDown n)
        {
            return Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }

        private ModuleCheckSettings Collect(out List<string> parseErrors)
        {
            parseErrors = new List<string>();
            var s = ModuleCheckManager.Instance.Settings.Clone();
            s.Enabled = chk_enabled.Checked;
            s.TargetStation = Math.Max(0, cmb_station.SelectedIndex);
            s.AdvanceMinutes = (int)num_advance.Value;
            s.RetryCount = (int)num_retry.Value;
            s.CheckModeTemp = (int)num_temp.Value;
            s.ModeSwitchTimeoutSec = (int)num_timeout.Value;
            s.BorrowFeedTray = chk_borrow.Checked;
            s.PlanTimes = lst_times.Items.Cast<object>().Select(o => o.ToString()).OrderBy(x => x).ToList();
            s.Samples = new List<ModuleCheckSampleSlot>();
            foreach (DataGridViewRow row in dgv_samples.Rows)
            {
                int tray, pocket;
                string id = Convert.ToString(row.Cells[col_id.Index].Value);
                if (!int.TryParse(Convert.ToString(row.Cells[col_tray.Index].Value), out tray))
                {
                    parseErrors.Add(string.Format("{0} 的点检盘穴位不是整数", id));
                    continue;
                }
                if (!int.TryParse(Convert.ToString(row.Cells[col_pocket.Index].Value), out pocket))
                {
                    parseErrors.Add(string.Format("{0} 的夹具穴位不是整数", id));
                    continue;
                }
                s.Samples.Add(new ModuleCheckSampleSlot { TrayPocketIdx = tray, FixturePocket = pocket });
            }
            return s;
        }

        private void AddRow(int tray, int pocket)
        {
            int i = dgv_samples.Rows.Add();
            dgv_samples.Rows[i].Cells[col_tray.Index].Value = tray;
            dgv_samples.Rows[i].Cells[col_pocket.Index].Value = pocket;
        }

        /// <summary>
        /// 按待测盘穴位编号从小到大，自动分配样品号和首轮夹具穴位（两组尽量平均）。
        /// 员工只需要关心样品放在待测盘哪些穴。
        /// </summary>
        private void RenumberRows()
        {
            var valid = new List<KeyValuePair<int, DataGridViewRow>>();
            foreach (DataGridViewRow row in dgv_samples.Rows)
            {
                int tray;
                if (int.TryParse(Convert.ToString(row.Cells[col_tray.Index].Value), out tray)) valid.Add(new KeyValuePair<int, DataGridViewRow>(tray, row));
                else
                {
                    row.Cells[col_id.Index].Value = "-";
                    row.Cells[col_pocket.Index].Value = null;
                    row.Cells[col_group.Index].Value = "-";
                }
            }
            valid = valid.OrderBy(v => v.Key).ToList();
            var pockets = ModuleCheckSettings.AutoFixturePockets(valid.Count, ModuleCheckMachineAdapter.EnabledFixturePockets(Math.Max(0, cmb_station.SelectedIndex)));
            for (int i = 0; i < valid.Count; i++)
            {
                var row = valid[i].Value;
                row.Cells[col_id.Index].Value = string.Format("S{0:00}", i + 1);
                if (i < pockets.Count)
                {
                    row.Cells[col_pocket.Index].Value = pockets[i];
                    row.Cells[col_group.Index].Value = ModuleCheckGroups.Name(ModuleCheckGroups.GroupOf(pockets[i]));
                }
                else
                {
                    row.Cells[col_pocket.Index].Value = null;
                    row.Cells[col_group.Index].Value = "超出可用穴位";
                }
            }
        }

        private void btn_addtime_Click(object sender, EventArgs e)
        {
            string t = dtp_time.Value.ToString("HH:mm");
            if (lst_times.Items.Cast<object>().Any(o => o.ToString() == t)) return;
            lst_times.Items.Add(t);
            var sorted = lst_times.Items.Cast<object>().Select(o => o.ToString()).OrderBy(x => x).ToList();
            lst_times.Items.Clear();
            foreach (var x in sorted) lst_times.Items.Add(x);
        }

        private void btn_deltime_Click(object sender, EventArgs e)
        {
            if (lst_times.SelectedIndex >= 0) lst_times.Items.RemoveAt(lst_times.SelectedIndex);
        }

        /// <summary>读取待测盘里取消了“使用”的穴位，作为点检样品的位置</summary>
        private void btn_fill16_Click(object sender, EventArgs e)
        {
            var idx = ModuleCheckMachineAdapter.DisabledFeedTrayPockets();
            if (idx.Count == 0)
            {
                MessageBox.Show(this, "待测盘配置里没有取消“使用”的穴位。\r\n请先在 产品 → 料盘 的穴位表里，把放点检样品的穴位取消勾选“使用”并保存。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string msg = string.Format("待测盘里取消“使用”的穴位共 {0} 个：{1}\r\n{2}将用这些穴位替换当前样品表，是否继续？",
                idx.Count, string.Join(",", idx),
                idx.Count > ModuleCheckSettings.FixturePocketCount ? "超过16个，只取编号最小的16个。\r\n" : "");
            if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            dgv_samples.Rows.Clear();
            foreach (var slot in ModuleCheckSettings.SlotsFromTrayPockets(idx))
                AddRow(slot.TrayPocketIdx, slot.FixturePocket);
            RenumberRows();
        }

        /// <summary>换承载工站后按该工站启用的穴位重新分配首轮夹具穴</summary>
        private void cmb_station_SelectedIndexChanged(object sender, EventArgs e)
        {
            RenumberRows();
        }

        private void btn_addrow_Click(object sender, EventArgs e)
        {
            if (dgv_samples.Rows.Count >= ModuleCheckSettings.FixturePocketCount) return;
            var usedPockets = new HashSet<int>();
            var usedTray = new HashSet<int>();
            foreach (DataGridViewRow row in dgv_samples.Rows)
            {
                int v;
                if (int.TryParse(Convert.ToString(row.Cells[col_pocket.Index].Value), out v)) usedPockets.Add(v);
                if (int.TryParse(Convert.ToString(row.Cells[col_tray.Index].Value), out v)) usedTray.Add(v);
            }
            int tray = Enumerable.Range(0, 1000).First(p => !usedTray.Contains(p));
            AddRow(tray, 0);   // 夹具穴位由 RenumberRows 自动分配
            RenumberRows();
        }

        private void btn_delrow_Click(object sender, EventArgs e)
        {
            if (dgv_samples.CurrentRow == null) return;
            dgv_samples.Rows.Remove(dgv_samples.CurrentRow);
            RenumberRows();
        }

        private void dgv_samples_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            RenumberRows();
        }

        private void btn_save_Click(object sender, EventArgs e)
        {
            dgv_samples.EndEdit();
            List<string> errs;
            var s = Collect(out errs);
            if (errs.Count == 0)
            {
                List<string> verrs;
                if (ModuleCheckManager.Instance.SaveSettings(s, out verrs))
                {
                    string msg = "已保存。";
                    var t = ModuleCheckManager.Instance.ActiveTask;
                    if (t != null && t.IsActive) msg += "\r\n当前任务按创建时的参数继续，新设置从下一次点检生效。";
                    MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshStatus();
                    return;
                }
                errs.AddRange(verrs);
            }
            MessageBox.Show(this, "设置有误，未保存：\r\n" + string.Join("\r\n", errs), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void btn_debug_Click(object sender, EventArgs e)
        {
            FrModuleCheckDebug.ShowSingle(this);
        }

        private void btn_close_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btn_cancel_Click(object sender, EventArgs e)
        {
            var t = ModuleCheckManager.Instance.ActiveTask;
            if (t == null || !t.IsActive) return;
            bool running = SafetyDebugMode.AutoRunActive;
            if (t.CancelRequested)
            {
                // 已请求取消、机台停着：员工已经人工处理了样品，直接结束
                if (!running) ForceEnd(t);
                return;
            }
            if (running)
            {
                if (MessageBox.Show(this, "取消后样品会放回点检盘原穴，测试电脑切回生产参数。确定取消本次点检？", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                ModuleCheckManager.Instance.RequestCancel();
                RefreshStatus();
                return;
            }
            DialogResult how = MessageBox.Show(this,
                "取消本次点检，样品怎么处理？\r\n\r\n" +
                "“是”= 由机台归还：工站下次到上下料面时把夹具里的样品放回待测盘原穴\r\n" +
                "“否”= 样品已人工全部放回待测盘原穴（夹具、吸嘴里都没有样品），直接结束本次点检\r\n" +
                "“取消”= 不取消", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
            if (how == DialogResult.Cancel) return;
            if (how == DialogResult.No)
            {
                ForceEnd(t);
                return;
            }
            if (t.NeedsReconcile && !ConfirmRestartReconcile(t, true)) return;   // 机台归还要知道样品在哪
            ModuleCheckManager.Instance.RequestCancel();
            RefreshStatus();
        }

        private void btn_manual_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "按已保存的设置立即创建一次模组自动点检任务（不占用计划时间）。\r\n本工站当前这一轮普通料测完、到上下料面时开始装样品。是否继续？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string err;
            var t = ModuleCheckManager.Instance.CreateManualTask(out err);
            if (t == null) MessageBox.Show(this, "未能创建点检任务：\r\n" + err.Replace("；", "\r\n"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshStatus();
        }

        private void btn_reconcile_Click(object sender, EventArgs e)
        {
            var t = ModuleCheckManager.Instance.ActiveTask;
            if (t == null || !t.IsActive) return;
            if (!t.NeedsReconcile)
            {
                ManualHomeSamples();
                return;
            }
            if (!ConfirmRestartReconcile(t, false)) return;
            RefreshStatus();
        }

        /// <summary>员工人工结束：确认样品已全部人工放回原穴，只剩测试电脑恢复生产参数</summary>
        private void ForceEnd(ModuleCheckTask t)
        {
            string pockets = string.Join("，", t.Samples.Select(s => string.Format("{0}→穴位{1}", s.Id, s.TrayPocketIdx)));
            string msg = "人工结束本次点检。请确认：\r\n\r\n" +
                "1. 所有样品已放回待测盘原穴：" + pockets + "\r\n" +
                "2. 工站" + (t.Snapshot.TargetStation + 1) + "夹具和两臂吸嘴上都没有样品\r\n\r\n" +
                "确认后测试电脑在后台切回生产参数，回读确认后任务结束，然后可以重新点检。是否继续？";
            if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            bool gaveUp;
            bool pcManual = false;
            ModuleCheckManager.Instance.IsProductionRestored(out gaveUp);
            if (gaveUp)
            {
                // 自动恢复已失败 3 次：允许员工用联调工具确认后人工结束
                string pcs = string.Join("，", t.Pcs.Where(p => p.CheckRequested && !p.ProductionRestored).Select(p => "PC" + p.PcId));
                DialogResult r = MessageBox.Show(this, pcs + " 自动恢复生产参数已失败 3 次。\r\n\r\n" +
                    "“是”= 我已用“联调工具”读取确认这些电脑是生产(0)模式，直接结束\r\n“否”= 再自动恢复一次",
                    Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r == DialogResult.Cancel) return;
                pcManual = r == DialogResult.Yes;
            }
            ModuleCheckManager.Instance.ForceEndByOperator(pcManual);
            RefreshStatus();
        }

        /// <summary>
        /// 程序重启后的人工核对：夹具里的样品按记录恢复身份；记录在吸嘴上的样品（重启后吸嘴记录不可信）
        /// 要求员工放回点检盘原穴并记为已归还。forCancel=true 时同一个对话框里确认取消。
        /// </summary>
        private bool ConfirmRestartReconcile(ModuleCheckTask t, bool forCancel)
        {
            var onFixture = t.Samples.Where(s => s.Location == ModuleCheckSampleLocation.Fixture).ToList();
            var offTray = t.Samples.Where(s => s.Location == ModuleCheckSampleLocation.Nozzle || s.Location == ModuleCheckSampleLocation.Unknown).ToList();
            var sb = new System.Text.StringBuilder();
            sb.Append("程序重启前点检任务没有结束，请到机台核对：\r\n\r\n");
            sb.Append("1. 工站").Append(t.Snapshot.TargetStation + 1).Append("夹具里应有的样品：");
            sb.Append(onFixture.Count == 0 ? "无" : string.Join("，", onFixture.Select(s => string.Format("穴{0}={1}", s.FixturePocket, s.Id))));
            sb.Append("\r\n   （不一致请人工调整成和记录一致，或把样品取出放回待测盘原穴后再核对）\r\n");
            if (offTray.Count > 0)
            {
                sb.Append("2. 以下样品记录在吸嘴上/位置未知，请取下并放回待测盘原穴：\r\n   ");
                sb.Append(string.Join("，", offTray.Select(s => string.Format("{0}→穴位{1}", s.Id, s.TrayPocketIdx))));
                sb.Append("\r\n");
            }
            sb.Append(offTray.Count > 0 ? "3. " : "2. ").Append("其余样品应在待测盘原穴；吸嘴上不能有任何物料。\r\n\r\n");
            sb.Append(forCancel
                ? "确认后取消本次点检：工站下次到上下料面时把夹具里的样品放回待测盘原穴，测试电脑切回生产参数。\r\n核对一致并取消请按“是”。"
                : "核对一致请按“是”继续本次点检；不一致请按“否”，人工处理后再核对，或取消本次点检。");
            if (MessageBox.Show(this, sb.ToString(), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return false;
            if (offTray.Count > 0)
                ModuleCheckManager.Instance.MarkSamplesManuallyHomed(offTray.Select(s => s.Id), forCancel ? "取消时人工核对" : "重启后人工核对");
            ModuleCheckManager.Instance.ConfirmReconciled(forCancel ? "取消时一并核对" : "设置界面人工核对");
            return true;
        }

        /// <summary>
        /// 样品记录在吸嘴上（例如人工对账时被清掉吸嘴记录前就取下了）：员工把样品放回点检盘原穴后在这里确认，
        /// 任务按原穴重新取。只在自动运行停止时允许。
        /// </summary>
        private void ManualHomeSamples()
        {
            if (SafetyDebugMode.AutoRunActive)
            {
                MessageBox.Show(this, "请先停止自动运行。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var list = ModuleCheckManager.Instance.SamplesOffTrayAndFixture();
            if (list.Count == 0) return;
            string items = string.Join("\r\n", list.Select(s => string.Format("{0}：记录在 {1} → 放回待测盘穴位 {2}", s.Id, s.DescribeLocation(), s.TrayPocketIdx)));
            string msg = "以下点检样品记录在吸嘴上，但吸嘴实际可能已经没有料：\r\n\r\n" + items +
                "\r\n\r\n请到机台确认：吸嘴上没有这些样品，并已把它们放回待测盘对应穴位（不要留在夹具里）。\r\n" +
                "确认后按“是”，本次点检会从原穴重新取这些样品。";
            if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            ModuleCheckManager.Instance.MarkSamplesManuallyHomed(list.Select(s => s.Id), "设置界面人工归位");
            RefreshStatus();
        }

        private void btn_openlog_Click(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(ModuleCheckLog.Dir);
                System.Diagnostics.Process.Start("explorer.exe", ModuleCheckLog.Dir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开日志目录失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void tmr_refresh_Tick(object sender, EventArgs e)
        {
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            List<string> errs;
            var preview = Collect(out errs);
            lbl_next.Text = ModuleCheckScheduler.DescribeNext(preview, DateTime.Now);
            var t = ModuleCheckManager.Instance.ActiveTask;
            if (t == null || !t.IsActive)
            {
                lbl_task.Text = "无进行中的点检任务";
                btn_cancel.Text = "取消本次点检";
                btn_cancel.Enabled = false;
                btn_reconcile.Enabled = false;
                btn_manual.Enabled = true;
            }
            else
            {
                lbl_task.Text = t.Summary() + (t.CancelRequested ? "（已请求取消）" : "") + (t.NeedsReconcile ? "（程序重启后待核对）" : "");
                bool running = SafetyDebugMode.AutoRunActive;
                btn_cancel.Text = t.CancelRequested ? "人工结束本次点检" : "取消本次点检";
                btn_cancel.Enabled = !t.CancelRequested || !running;
                btn_reconcile.Enabled = t.NeedsReconcile ||
                    (!SafetyDebugMode.AutoRunActive && ModuleCheckManager.Instance.SamplesOffTrayAndFixture().Count > 0);
                btn_manual.Enabled = false;
            }
            lbl_flow.Text = ModuleCheckManager.FlowIntegrated
                ? ""
                : "注意：本版本只包含设置、调度、日志与持久化，运行流程尚未接入，到点不会执行点检。";
        }
    }
}
