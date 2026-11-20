using System;
using System.Drawing;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    /// <summary>[R14] 系统页“三吸嘴（工程师）”组里的“回检不过弹窗(OK盘提前定位)”开关。</summary>
    public partial class FrSys
    {
        private CheckBox chk_tn_backalarm;
        private bool refreshingBackCheckAlarm;

        private void InitBackCheckAlarmOption()
        {
            chk_tn_backalarm = new CheckBox
            {
                AutoSize = true,
                Font = new Font("宋体", 12F, FontStyle.Bold, GraphicsUnit.Point, 134),
                Location = new Point(14, 108),
                Name = "chk_tn_backalarm",
                Text = "回检不过弹窗(OK盘提前定位)",
                UseVisualStyleBackColor = true
            };
            grp_three_nozzle.Height = Math.Max(grp_three_nozzle.Height, 142);
            grp_three_nozzle.Controls.Add(chk_tn_backalarm);
            chk_tn_backalarm.CheckedChanged += chk_tn_backalarm_CheckedChanged;
            tp_setparm_1.Enter += (s, e) => RefreshBackCheckAlarm();
            RefreshBackCheckAlarm();
            // [R15] 调试模式可把延时调到 50 以下：放开系统页输入框下限，否则加载/同步时超范围报错
            nud_PlaceDly.Minimum = 40;
            nud_MovUpDly.Minimum = 0;
        }

        /// <summary>[R15] 调试模式应用取放延时后同步系统页显示，避免系统页之后保存时用旧值覆盖。</summary>
        public void SyncPickPlaceDelays()
        {
            Chk_ModPasteUp.Checked = PT_SET.bModPasteUp;
            nud_PlaceDly.Value = Math.Max(nud_PlaceDly.Minimum, Math.Min(nud_PlaceDly.Maximum, PT_SET.PlaceDly));
            nud_MovUpDly.Value = Math.Max(nud_MovUpDly.Minimum, Math.Min(nud_MovUpDly.Maximum, PT_SET.MovUpDly));
        }

        private void RefreshBackCheckAlarm()
        {
            if (chk_tn_backalarm == null) return;
            refreshingBackCheckAlarm = true;
            try
            {
                ThreeNozzleHardwareConfig.Initialize();
                chk_tn_backalarm.Checked = ThreeNozzleHardwareConfig.BackCheckAlarmEnabled;
                chk_tn_backalarm.ForeColor = chk_tn_backalarm.Checked ? Color.Green : SystemColors.ControlText;
            }
            finally { refreshingBackCheckAlarm = false; }
        }

        private void chk_tn_backalarm_CheckedChanged(object sender, EventArgs e)
        {
            if (refreshingBackCheckAlarm) return;
            bool want = chk_tn_backalarm.Checked;
            string operatorName = "";
            bool engineer = false;
            try
            {
                User.ST_USER user = FrMain.frsuser.user1.cur_user;
                operatorName = user.ToString();
                engineer = user.pms >= User.PERMISSION.Engineer;
            }
            catch { }
            if (!engineer)
            {
                MessageBox.Show(this, "需要工程师及以上权限才能切换回检不过弹窗。", "回检不过弹窗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshBackCheckAlarm();
                return;
            }
            if (want && MessageBox.Show(this,
                "开启后：\r\n" +
                "· 放料后回检条码不过（读不到/与记录不一致）时，先自动定拍复检一次；仍不过则弹窗，\r\n" +
                "  由员工选择“人工确认继续”（物料留在夹具，成品照常去OK/NG盘）、“取回放NG”（原自动流程）或“停止运行”，\r\n" +
                "  不再自动取回放 NG 料盘。\r\n" +
                "· 成品料盘（按测试结果去 OK 或 NG 盘）在放第二颗新料时就提前移到落料位，不等回检结束。\r\n" +
                "  提前占用料盘期间对臂要用同一个料盘需等约 1~1.5 秒。\r\n\r\n" +
                "放偏检测原来就是弹窗，不受影响。是否开启？",
                "回检不过弹窗", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                RefreshBackCheckAlarm();
                return;
            }
            string error;
            if (!ThreeNozzleHardwareConfig.SetBackCheckAlarm(want, operatorName, out error))
                MessageBox.Show(this, error, "回检不过弹窗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshBackCheckAlarm();
        }
    }
}
