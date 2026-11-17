using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// 「系统校准-相机」页：输入序列号添加海康相机、给原有相机位置换海康序列号、选软/硬触发。
    /// 硬触发固定 Line0（上升沿），光源输出固定 Line1，触发 IO 由程序按相机位置绑定，界面不让员工选。
    /// 选中一行时触发 CameraSelected，宿主（FrSys）把上方 VisionPro 显示控件切到该相机，用控件自带的触发/实况按钮取图。
    /// </summary>
    public partial class HikCameraPanel : UserControl
    {
        private readonly Dictionary<string, HikCamera.DeviceEntry> online = new Dictionary<string, HikCamera.DeviceEntry>(StringComparer.OrdinalIgnoreCase);
        private bool scanned, updating;

        /// <summary>选中相机（宿主联动 VisionPro 显示控件）。</summary>
        public event Action<Cam> CameraSelected;
        /// <summary>相机已删除（宿主从显示控件里移除）。</summary>
        public event Action<Cam> CameraRemoved;
        /// <summary>相机列表或参数变化（宿主刷新显示控件的相机下拉框）。</summary>
        public event Action CamerasChanged;

        public HikCameraPanel()
        {
            InitializeComponent();
            cmb_source.SelectedIndex = 0;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!DesignMode) RefreshList();   // 第一次显示相机页时加载列表
        }

        private Cam SelectedCam
        {
            get
            {
                if (dgv_cams.CurrentRow == null) return null;
                return dgv_cams.CurrentRow.Tag as Cam;
            }
        }

        /// <summary>重新读取 COM.ListCam 并刷新表格，保留当前选中。</summary>
        public void RefreshList()
        {
            if (DesignMode) return;
            COM.LoadAddedCams();
            Cam keep = SelectedCam;
            updating = true;
            try
            {
                dgv_cams.Rows.Clear();
                foreach (Cam cam in COM.ListCam)
                {
                    int i = dgv_cams.Rows.Add(cam.mName, (cam.disc ?? "").Trim(), cam.mSerialNumber, cam.BackendText,
                        StateText(cam), OnlineText(cam));
                    dgv_cams.Rows[i].Tag = cam;
                    if (string.IsNullOrWhiteSpace(cam.mSerialNumber)) dgv_cams.Rows[i].DefaultCellStyle.ForeColor = System.Drawing.Color.Gray;
                }
                if (keep != null)
                    foreach (DataGridViewRow row in dgv_cams.Rows)
                        if (row.Tag == keep) { dgv_cams.CurrentCell = row.Cells[0]; break; }
            }
            finally { updating = false; }
            ShowSelected(false);
        }

        private static string StateText(Cam cam)
        {
            // 不调用 isLive：VisionPro 相机的 isLive 会去查采集卡状态，断开时很慢，会拖慢切页
            if (cam.isInit && cam.HasAcquisition) return cam.bIsLive ? "实况中" : "已连接";
            return string.IsNullOrWhiteSpace(cam.mSerialNumber) ? "未设SN" : "未连接";
        }

        private string OnlineText(Cam cam)
        {
            if (!scanned) return "";
            HikCamera.DeviceEntry entry;
            if (!string.IsNullOrWhiteSpace(cam.mSerialNumber) && online.TryGetValue(cam.mSerialNumber.Trim(), out entry))
                return "在线 " + entry.Interface + " " + entry.Model;
            return cam.IsHik ? "未找到" : "";
        }

        private void dgv_cams_SelectionChanged(object sender, EventArgs e)
        {
            if (updating) return;
            ShowSelected(true);
        }

        private void ShowSelected(bool raise)
        {
            Cam cam = SelectedCam;
            btn_remove.Enabled = cam != null;
            btn_apply.Enabled = btn_connect.Enabled = cam != null;
            if (cam == null) return;
            updating = true;
            try
            {
                cmb_sn.Text = cam.mSerialNumber ?? "";
                txt_disc.Text = (cam.disc ?? "").Trim();
                cmb_source.SelectedIndex = cam.HikSource == Cam.HIK_SOURCE.SOFT ? 0 : 1;
            }
            finally { updating = false; }
            if (raise && CameraSelected != null) CameraSelected(cam);
        }

        private void cmb_sn_SelectedIndexChanged(object sender, EventArgs e)
        {
            // 下拉项格式 “SN  型号  接口”，只保留 SN
            if (updating || cmb_sn.SelectedItem == null) return;
            string sn = cmb_sn.SelectedItem.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            BeginInvoke(new Action(() => cmb_sn.Text = sn));
        }

        private static bool MachineBusy(out string reason)
        {
            reason = null;
            if (VAR.gsys_set.status == EM_SYS_STA.RUN || VAR.gsys_set.status == EM_SYS_STA.PAUSE || VAR.gsys_set.status == EM_SYS_STA.RESET)
                reason = "请先停止自动运行/复位再修改相机";
            else if (NozzleZCalibrationMotion.IsBusy)
                reason = "Z 测高动作执行中";
            return reason != null;
        }

        private void btn_scan_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                List<HikCamera.DeviceEntry> list = HikCamera.Enumerate();
                online.Clear();
                foreach (var d in list) if (!string.IsNullOrEmpty(d.SerialNumber)) online[d.SerialNumber] = d;
                scanned = true;
                string current = cmb_sn.Text;
                cmb_sn.Items.Clear();
                foreach (var d in list) cmb_sn.Items.Add(d.ToString());
                cmb_sn.Text = current;
                var unused = list.Where(d => !COM.ListCam.Any(c => string.Equals((c.mSerialNumber ?? "").Trim(), d.SerialNumber, StringComparison.OrdinalIgnoreCase))).ToList();
                lbl_status.Text = list.Count == 0
                    ? "没有扫描到海康相机：检查 USB3 线/供电，并确认相机没有被 MVS 客户端占用"
                    : string.Format("扫描到 {0} 台海康相机，其中 {1} 台未添加：{2}", list.Count, unused.Count, string.Join("、", unused.Select(d => d.SerialNumber)));
                RefreshList();
            }
            catch (Exception ex)
            {
                // 没装 MVS / 找不到 MvCameraControl.dll 时会走到这里（DllNotFoundException）
                lbl_status.Text = "扫描失败：" + ex.Message + "（请确认机台已安装海康 MVS 运行库（MvCameraControl.dll））";
            }
            finally { Cursor = Cursors.Default; }
        }

        // 硬触发固定 Line0
        private Cam.HIK_SOURCE EditorSource { get { return cmb_source.SelectedIndex == 1 ? Cam.HIK_SOURCE.LINE0 : Cam.HIK_SOURCE.SOFT; } }
        private string EditorSn { get { return cmb_sn.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""; } }

        private void btn_add_Click(object sender, EventArgs e)
        {
            string reason;
            if (MachineBusy(out reason)) { MessageBox.Show(this, reason, "相机", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string error;
            Cam cam = COM.AddCamBySerial(EditorSn, txt_disc.Text, EditorSource, true, "", "LINE1", out error);
            if (cam == null) { MessageBox.Show(this, error, "添加相机", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Connect(cam);
            if (CamerasChanged != null) CamerasChanged();
            RefreshList();
            SelectRow(cam);
        }

        private void btn_apply_Click(object sender, EventArgs e)
        {
            Cam cam = SelectedCam;
            if (cam == null) return;
            string reason;
            if (MachineBusy(out reason)) { MessageBox.Show(this, reason, "相机", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string sn = EditorSn;
            if (sn.Length == 0) { MessageBox.Show(this, "序列号不能为空", "相机", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Cam same = COM.ListCam.FirstOrDefault(c => c != cam && string.Equals((c.mSerialNumber ?? "").Trim(), sn, StringComparison.OrdinalIgnoreCase));
            if (same != null) { MessageBox.Show(this, string.Format("序列号 {0} 已被「{1}」使用", sn, (same.disc ?? "").Trim()), "相机", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            cam.mSerialNumber = sn;
            string disc = txt_disc.Text.Trim();
            if (disc.Length > 3) cam.disc = disc;
            cam.Backend = Cam.CAM_BACKEND.Hik;
            cam.HikSource = EditorSource;
            if (cam.SaveCfg() != EM_RES.OK)
            {
                MessageBox.Show(this, "保存到 syscfg.ini 失败", "相机", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Connect(cam);
            if (CamerasChanged != null) CamerasChanged();
            RefreshList();
        }

        private void btn_connect_Click(object sender, EventArgs e)
        {
            Cam cam = SelectedCam;
            if (cam == null) return;
            string reason;
            if (MachineBusy(out reason)) { MessageBox.Show(this, reason, "相机", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Connect(cam);
            RefreshList();
            if (CameraSelected != null) CameraSelected(cam);
        }

        private void Connect(Cam cam)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                EM_RES res = cam.Reconnect();
                lbl_status.Text = res == EM_RES.OK
                    ? string.Format("「{0}」连接成功（{1}）。在上方 VisionPro 窗口选中它，点「实况」看画面、点「触发」拍一张。", (cam.disc ?? "").Trim(), cam.BackendText)
                    : string.Format("「{0}」连接失败：{1}", (cam.disc ?? "").Trim(),
                        string.IsNullOrEmpty(cam.LastHikError) ? res + "，详情见报警日志" : cam.LastHikError);
            }
            catch (Exception ex)
            {
                // 没装 MVS 时打开海康相机会抛 DllNotFoundException
                lbl_status.Text = string.Format("「{0}」连接异常：{1}", (cam.disc ?? "").Trim(), ex.Message);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void btn_remove_Click(object sender, EventArgs e)
        {
            Cam cam = SelectedCam;
            if (cam == null) return;
            string reason;
            if (MachineBusy(out reason)) { MessageBox.Show(this, reason, "相机", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string ask = cam.UserAdded
                ? string.Format("删除相机「{0}」(SN={1})？", (cam.disc ?? "").Trim(), cam.mSerialNumber)
                : string.Format("「{0}」是流程里直接使用的原有相机（{1}），不能从列表移除。\r\n删除将断开它并清空序列号，之后不再初始化；需要时重新填序列号即可。\r\n\r\n继续？",
                    (cam.disc ?? "").Trim(), cam.mName);
            if (MessageBox.Show(this, ask, "删除相机", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string error;
            if (!COM.RemoveAddedCam(cam, out error)) { MessageBox.Show(this, error, "删除相机", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (cam.UserAdded && CameraRemoved != null) CameraRemoved(cam);
            if (CamerasChanged != null) CamerasChanged();
            RefreshList();
        }

        private void SelectRow(Cam cam)
        {
            foreach (DataGridViewRow row in dgv_cams.Rows)
                if (row.Tag == cam) { dgv_cams.CurrentCell = row.Cells[0]; break; }
        }
    }
}
