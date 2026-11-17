using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UI.Class;
using UI.ModuleCheck;

namespace UI
{
    /// <summary>
    /// 模组点检联调工具：对某一台测试电脑单独读模式、发切换指令、读握手/心跳。
    /// 每次调用都显示返回值和耗时，并记到点检日志（类别“模式切换”，前缀[联调]）。
    /// 所有 DLL 调用在后台线程执行（切参数会让测试软件重启，DLL 可能阻塞到登录完成）。
    /// </summary>
    public partial class FrModuleCheckDebug : Form
    {
        private static FrModuleCheckDebug instance;
        private volatile bool busy;
        private List<ModuleCheckPcInfo> pcs = new List<ModuleCheckPcInfo>();

        public FrModuleCheckDebug()
        {
            InitializeComponent();
        }

        public static void ShowSingle(IWin32Window owner)
        {
            if (instance == null || instance.IsDisposed)
            {
                instance = new FrModuleCheckDebug();
                instance.Show(owner);
            }
            else
            {
                if (instance.WindowState == FormWindowState.Minimized) instance.WindowState = FormWindowState.Normal;
                instance.Activate();
            }
        }

        private void FrModuleCheckDebug_Load(object sender, EventArgs e)
        {
            var s = ModuleCheckManager.Instance.Settings;
            cmb_dmode.SelectedIndex = 1;
            num_dtemp.Value = Math.Max(num_dtemp.Minimum, Math.Min(num_dtemp.Maximum, s.CheckModeTemp));
            num_dwait.Value = Math.Max(num_dwait.Minimum, Math.Min(num_dwait.Maximum, s.ModeSwitchTimeoutSec));
            cmb_dstation.SelectedIndex = Math.Max(0, Math.Min(3, s.TargetStation));
            if (WS.Demo) Log("当前为空跑(Demo)模式：不调用 DLL，联调请先退出空跑。");
        }

        private void FrModuleCheckDebug_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!busy) return;
            MessageBox.Show(this, "调用还在进行中（测试软件可能正在重启），请等结果返回后再关闭。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }

        private void cmb_dstation_SelectedIndexChanged(object sender, EventArgs e)
        {
            List<string> errs;
            pcs = ModuleCheckPcMap.Build(new ModuleCheckMachineAdapter().GetStationPcPairs(cmb_dstation.SelectedIndex), out errs);
            cmb_dpc.Items.Clear();
            foreach (var pc in pcs) cmb_dpc.Items.Add(string.Format("PC{0}", pc.PcId));
            if (cmb_dpc.Items.Count > 0) cmb_dpc.SelectedIndex = 0;
            else txt_dip.Text = "";
            foreach (var err in errs) Log("配置问题：" + err + "（可在上方填 IP 后点“保存IP”）");
        }

        private void cmb_dpc_SelectedIndexChanged(object sender, EventArgs e)
        {
            int i = cmb_dpc.SelectedIndex;
            txt_dip.Text = i >= 0 && i < pcs.Count ? pcs[i].Ip : "";
        }

        /// <summary>读取所选电脑当前连接的 IP 填到输入框（不保存）</summary>
        private void btn_dautoip_Click(object sender, EventArgs e)
        {
            var pc = SelectedPc(false);
            if (pc == null) return;
            Run(() =>
            {
                if (WS.Demo) { Log("空跑模式不调用 DLL"); return; }
                int ret;
                string ip = ModuleCheckMachineAdapter.ReadConnectedIp(pc.PcId, out ret);
                if (string.IsNullOrEmpty(ip))
                {
                    Log(string.Format("PC{0} 读取连接IP失败（GetInfo ret={1}）：测试软件可能没连上电控", pc.PcId, ret), true);
                    return;
                }
                Log(string.Format("PC{0} 当前连接的IP = {1}（如需固定请点“保存IP”）", pc.PcId, ip), true);
                BeginInvoke(new Action(() => txt_dip.Text = ip));
            });
        }

        /// <summary>4 个工站的所有测试电脑：从连接读取 IP 并保存</summary>
        private void btn_dautoall_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "读取所有已连接测试电脑的 IP 并保存到点检设置（已手动保存的会被覆盖），是否继续？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Run(() =>
            {
                if (WS.Demo) { Log("空跑模式不调用 DLL"); return; }
                var adapter = new ModuleCheckMachineAdapter();
                var ids = new SortedSet<int>();
                for (int st = 0; st < 4; st++)
                    foreach (var p in adapter.GetStationPcPairs(st)) ids.Add(p.Key);
                int ok = 0, fail = 0;
                foreach (int id in ids)
                {
                    int ret;
                    string ip = ModuleCheckMachineAdapter.ReadConnectedIp(id, out ret);
                    if (string.IsNullOrEmpty(ip))
                    {
                        fail++;
                        Log(string.Format("PC{0} 未读到连接IP（GetInfo ret={1}），保持原配置", id, ret), true);
                        continue;
                    }
                    string err;
                    if (ModuleCheckManager.Instance.SavePcIp(id, ip, out err)) { ok++; Log(string.Format("PC{0} = {1} 已保存", id, ip), true); }
                    else { fail++; Log(string.Format("PC{0} 保存失败：{1}", id, err), true); }
                }
                Log(string.Format("读取完成：保存 {0} 台，未读到/失败 {1} 台", ok, fail), true);
                BeginInvoke(new Action(() =>
                {
                    int keep = cmb_dpc.SelectedIndex;
                    cmb_dstation_SelectedIndexChanged(null, EventArgs.Empty);
                    if (keep >= 0 && keep < cmb_dpc.Items.Count) cmb_dpc.SelectedIndex = keep;
                }));
            });
        }

        private void btn_dsaveip_Click(object sender, EventArgs e)
        {
            int i = cmb_dpc.SelectedIndex;
            if (i < 0 || i >= pcs.Count) return;
            string ip = txt_dip.Text.Trim();
            string err;
            if (!ModuleCheckManager.Instance.SavePcIp(pcs[i].PcId, ip, out err))
            {
                MessageBox.Show(this, "保存失败：" + err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Log(string.Format("PC{0} 的 IP 已保存为 {1}", pcs[i].PcId, ip.Length == 0 ? "(空，改用工站配置)" : ip), true);
            int keep = i;
            cmb_dstation_SelectedIndexChanged(null, EventArgs.Empty);
            if (keep < cmb_dpc.Items.Count) cmb_dpc.SelectedIndex = keep;
        }

        /// <summary>当前选中的电脑；IP 用输入框里的值（可以先不保存直接试）</summary>
        private ModuleCheckPcInfo SelectedPc(bool needIp = true)
        {
            int i = cmb_dpc.SelectedIndex;
            if (i < 0 || i >= pcs.Count)
            {
                MessageBox.Show(this, "请先选择测试电脑", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            string ip = txt_dip.Text.Trim();
            if (!needIp) return new ModuleCheckPcInfo { PcId = pcs[i].PcId, Ip = ip };   // 握手/心跳只用 PC_ID
            System.Net.IPAddress parsed;
            if (ip.Length == 0 || !System.Net.IPAddress.TryParse(ip, out parsed) || ip.Count(ch => ch == '.') != 3)
            {
                MessageBox.Show(this, "请填写正确的 IP（例如 192.168.1.101）", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            if (ip != pcs[i].Ip) Log(string.Format("本次使用输入框里的 IP {0}（尚未保存）", ip));
            return new ModuleCheckPcInfo { PcId = pcs[i].PcId, Ip = ip };
        }

        private static string ModeText(PUCFFactoryMode m)
        {
            switch (m)
            {
                case PUCFFactoryMode.PUCFFactoryMode_Product: return "生产(0)";
                case PUCFFactoryMode.PUCFFactoryMode_Check: return "点检(1)";
                case PUCFFactoryMode.PUCFFactoryMode_Create: return "点检生成(2)";
                case PUCFFactoryMode.PUCFFactoryMode_UnKnown: return "未知(-1)";
                default: return ((int)m).ToString();
            }
        }

        private bool ConfirmSwitch(ModuleCheckPcInfo pc, PUCFFactoryMode mode)
        {
            var t = ModuleCheckManager.Instance.ActiveTask;
            if (t != null && t.IsActive)
            {
                MessageBox.Show(this, "模组自动点检任务进行中（" + t.TaskId + "），联调期间不能手动切换，请先取消或等任务结束。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            string extra = mode == PUCFFactoryMode.PUCFFactoryMode_Create
                ? "\r\n\r\n注意：通讯协议文档的 FactoryMode 只定义了 0/1，点检生成(2)需先和测试端确认 DLL 是否支持。" : "";
            return MessageBox.Show(this, string.Format("向 PC{0}（{1}）发送切换到 {2}？\r\n切换参数会让测试软件重启，请确认这台电脑当前没有在测试。{3}",
                pc.PcId, pc.Ip, ModeText(mode), extra), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void btn_dget_Click(object sender, EventArgs e)
        {
            var pc = SelectedPc();
            if (pc == null) return;
            Run(() => GetMode(pc));
        }

        private void btn_dset_Click(object sender, EventArgs e)
        {
            var pc = SelectedPc();
            if (pc == null) return;
            var mode = (PUCFFactoryMode)cmb_dmode.SelectedIndex;
            int temp = (int)num_dtemp.Value;
            if (!ConfirmSwitch(pc, mode)) return;
            Run(() => SetMode(pc, mode, temp));
        }

        private void btn_dsetwait_Click(object sender, EventArgs e)
        {
            var pc = SelectedPc();
            if (pc == null) return;
            var mode = (PUCFFactoryMode)cmb_dmode.SelectedIndex;
            int temp = (int)num_dtemp.Value;
            int waitSec = (int)num_dwait.Value;
            if (!ConfirmSwitch(pc, mode)) return;
            Run(() =>
            {
                var sw = Stopwatch.StartNew();
                if (SetMode(pc, mode, temp) != 0) return;
                int n = 0;
                while (sw.ElapsedMilliseconds < waitSec * 1000L)
                {
                    Thread.Sleep(1000);
                    n++;
                    PUCFFactoryMode cur;
                    int curTemp;
                    int ret = GetModeRaw(pc, out cur, out curTemp);
                    bool match = ret == 0 && cur == mode && (mode != PUCFFactoryMode.PUCFFactoryMode_Check || curTemp == temp);
                    Log(string.Format("  第{0}次回读 ret={1} 模式={2} 参数={3} 已用{4:0.0}s{5}", n, ret, ret == 0 ? ModeText(cur) : "-", ret == 0 ? curTemp.ToString() : "-",
                        sw.ElapsedMilliseconds / 1000.0, match ? "  → 已确认" : ""));
                    if (match)
                    {
                        Log(string.Format("PC{0} 切换到 {1} 已确认，从发送到确认共 {2:0.0}s", pc.PcId, ModeText(mode), sw.ElapsedMilliseconds / 1000.0), true);
                        return;
                    }
                }
                Log(string.Format("PC{0} 等待 {1}s 仍未回读到 {2}", pc.PcId, waitSec, ModeText(mode)), true);
            });
        }

        private void btn_dinfo_Click(object sender, EventArgs e)
        {
            var pc = SelectedPc(false);
            if (pc == null) return;
            Run(() =>
            {
                if (WS.Demo) { Log("空跑模式不调用 DLL"); return; }
                var sw = Stopwatch.StartNew();
                TestPC.InfoData info = new TestPC.InfoData();
                int ret;
                try { ret = TestPC.GetInfo(pc.PcId, ref info); }
                catch (Exception ex) { Log("GetInfo 异常：" + ex.Message, true); return; }
                if (ret != 0) { Log(string.Format("PC{0} GetInfo ret={1}（{2}ms）", pc.PcId, ret, sw.ElapsedMilliseconds), true); return; }
                Log(string.Format("PC{0} GetInfo ret=0（{1}ms）：从站号={2} 启动标志={3} 结果标志={4} 位置/状态={5} 结果数={6} 连接状态={7}",
                    pc.PcId, sw.ElapsedMilliseconds, info.slaveid,
                    ModuleCheckHandshake.Describe(info.start_mark), ModuleCheckHandshake.Describe(info.result_mark),
                    info.pos_idx, info.res_num, info.status), true);
                string reason;
                Log(ModuleCheckHandshake.IsReadyForStart(ret, info.result_mark, out reason)
                    ? "  → 结果标志为 END，可以启动新一轮测试"
                    : "  → 现在不能启动新一轮：" + reason);
            });
        }

        private void btn_dstatus_Click(object sender, EventArgs e)
        {
            var pc = SelectedPc(false);
            if (pc == null) return;
            Run(() =>
            {
                if (WS.Demo) { Log("空跑模式不调用 DLL"); return; }
                int firstTick = -1;
                for (int i = 0; i < 3; i++)
                {
                    int sta = -1;
                    int ret;
                    try { ret = TestPC.GetStatus(pc.PcId, ref sta); }
                    catch (Exception ex) { Log("GetStatus 异常：" + ex.Message, true); return; }
                    int st = sta >> 8, tick = sta & 0xFF;
                    string stText = st == 1 ? "就绪" : (st == 2 ? "测试中" : (st == 3 ? "等待" : st.ToString()));
                    Log(string.Format("PC{0} GetStatus ret={1} 状态={2} 心跳={3}", pc.PcId, ret, ret == 0 ? stText : "-", ret == 0 ? tick.ToString() : "-"), i == 2);
                    if (i == 0) firstTick = tick;
                    if (i == 2 && ret == 0)
                        Log(tick != firstTick ? "  → 心跳在变化，测试软件在线" : "  → 2秒内心跳没有变化，测试软件可能离线或在重启");
                    if (i < 2) Thread.Sleep(1000);
                }
            });
        }

        private int GetModeRaw(ModuleCheckPcInfo pc, out PUCFFactoryMode mode, out int temp)
        {
            mode = PUCFFactoryMode.PUCFFactoryMode_UnKnown;
            temp = 0;
            if (WS.Demo) return -1;
            try { return AutoChkDLL.iPUCFTestServiceGetFactoryMode(pc.Ip, out mode, out temp); }
            catch (Exception ex) { Log("GetFactoryMode 异常：" + ex.Message, true); return -9999; }
        }

        private void GetMode(ModuleCheckPcInfo pc)
        {
            if (WS.Demo) { Log("空跑模式不调用 DLL"); return; }
            var sw = Stopwatch.StartNew();
            PUCFFactoryMode mode;
            int temp;
            int ret = GetModeRaw(pc, out mode, out temp);
            Log(string.Format("PC{0}({1}) GetFactoryMode ret={2} 模式={3} 参数={4} 耗时{5}ms",
                pc.PcId, pc.Ip, ret, ret == 0 ? ModeText(mode) : "-", ret == 0 ? temp.ToString() : "-", sw.ElapsedMilliseconds), true);
        }

        private int SetMode(ModuleCheckPcInfo pc, PUCFFactoryMode mode, int temp)
        {
            if (WS.Demo) { Log("空跑模式不调用 DLL"); return -1; }
            int sendTemp = mode == PUCFFactoryMode.PUCFFactoryMode_Product ? 0 : temp;
            Log(string.Format("PC{0}({1}) 发送 SetFactoryMode({2}, {3}) ……", pc.PcId, pc.Ip, ModeText(mode), sendTemp), true);
            var sw = Stopwatch.StartNew();
            int ret;
            try { ret = AutoChkDLL.iPUCFTestServiceSetFactoryMode(pc.Ip, mode, sendTemp); }
            catch (Exception ex) { Log("SetFactoryMode 异常：" + ex.Message, true); return -9999; }
            Log(string.Format("PC{0} SetFactoryMode 返回 ret={1}，耗时{2:0.0}s{3}", pc.PcId, ret, sw.ElapsedMilliseconds / 1000.0,
                ret == 0 ? "" : "（非0 视为失败）"), true);
            return ret;
        }

        private void Run(Action work)
        {
            if (busy) return;
            busy = true;
            SetButtons(false);
            Task.Run(() =>
            {
                try { work(); }
                catch (Exception ex) { Log("异常：" + ex.Message, true); }
                finally
                {
                    busy = false;
                    try { BeginInvoke(new Action(() => SetButtons(true))); } catch { }
                }
            });
        }

        private void SetButtons(bool enabled)
        {
            btn_dget.Enabled = btn_dset.Enabled = btn_dsetwait.Enabled = btn_dinfo.Enabled = btn_dstatus.Enabled = btn_dsaveip.Enabled = btn_dautoip.Enabled = btn_dautoall.Enabled = enabled;
            cmb_dstation.Enabled = cmb_dpc.Enabled = enabled;
        }

        /// <summary>写到窗口，toFile=true 时同时记到点检日志</summary>
        private void Log(string text, bool toFile = false)
        {
            string line = string.Format("{0:HH:mm:ss.fff} {1}", DateTime.Now, text);
            if (toFile) ModuleCheckLog.Info(ModuleCheckLogCat.Mode, null, "[联调] " + text);
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(new Action(() => AppendLine(line)));
                else AppendLine(line);
            }
            catch { }
        }

        private void AppendLine(string line)
        {
            if (txt_dlog.TextLength > 100000) txt_dlog.Clear();
            txt_dlog.AppendText(line + "\r\n");
        }

        private void btn_dclear_Click(object sender, EventArgs e)
        {
            txt_dlog.Clear();
        }

        private void btn_dclose_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
