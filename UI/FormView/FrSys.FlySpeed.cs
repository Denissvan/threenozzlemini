using System;
using System.Drawing;
using System.Windows.Forms;

namespace UI
{
    public partial class FrSys
    {
        private CheckBox chkFlyTwoSpeed;
        private readonly NumericUpDown[] flyLowInputs = new NumericUpDown[2];
        private readonly Label[] flyHighLabels = new Label[2];
        private Label flySpeedStatus;

        private void InitFlySpeedPage()
        {
            var page = new TabPage("飞拍两段速") { AutoScroll = true, BackColor = SystemColors.Control };
            var panel = new Panel { Location = new Point(24, 24), Size = new Size(850, 390) };
            chkFlyTwoSpeed = new CheckBox { Text = "启用三吸嘴飞拍两段速", AutoSize = true, Location = new Point(0, 0) };
            panel.Controls.Add(chkFlyTwoSpeed);
            panel.Controls.Add(new Label { Text = "模块", Location = new Point(0, 53), AutoSize = true });
            panel.Controls.Add(new Label { Text = "拍照区低速（mm/s）", Location = new Point(135, 53), AutoSize = true });
            panel.Controls.Add(new Label { Text = "高速：Y轴工作速度（mm/s）", Location = new Point(365, 53), AutoSize = true });
            for (int arm = 0; arm < 2; arm++)
            {
                int y = 90 + arm * 55;
                panel.Controls.Add(new Label { Text = "上下料模块" + (arm + 1), Location = new Point(0, y + 5), AutoSize = true });
                flyLowInputs[arm] = new NumericUpDown { Location = new Point(135, y), Size = new Size(160, 30), DecimalPlaces = 1, Minimum = 0.1M, Maximum = 100000, Increment = 10 };
                flyHighLabels[arm] = new Label { Location = new Point(365, y + 5), AutoSize = true };
                panel.Controls.Add(flyLowInputs[arm]);
                panel.Controls.Add(flyHighLabels[arm]);
            }
            panel.Controls.Add(new Label { Text = "高速自动跟随各自Y轴的工作速度；拍照区使用本模块低速。\r\n修改低速后，请重新进行对应模块的飞拍标定。保存前请停止自动运行。",
                Location = new Point(0, 210), Size = new Size(830, 65) });
            var save = new Button { Text = "保存并应用", Location = new Point(0, 290), Size = new Size(150, 40) };
            save.Click += SaveFlySpeed;
            panel.Controls.Add(save);
            flySpeedStatus = new Label { Location = new Point(0, 345), Size = new Size(830, 45) };
            panel.Controls.Add(flySpeedStatus);
            page.Controls.Add(panel);
            page.Enter += (s, e) => RefreshFlySpeed();
            ctb_SetParm.TabPages.Add(page);
            RefreshFlySpeed();
        }

        private void RefreshFlySpeed()
        {
            ThreeNozzleHardwareConfig.Initialize();
            chkFlyTwoSpeed.Checked = ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled;
            for (int arm = 0; arm < 2; arm++)
            {
                decimal low = (decimal)Math.Min((double)flyLowInputs[arm].Maximum, ThreeNozzleHardwareConfig.FlyLowSpeed(arm));
                flyLowInputs[arm].Value = Math.Min(flyLowInputs[arm].Maximum, Math.Max(flyLowInputs[arm].Minimum, low));
                double high = arm == 0 ? MT.AXIS_UDL1_Y.spd_work : MT.AXIS_UDL2_Y.spd_work;
                flyHighLabels[arm].Text = high.ToString("F1") + "（自动读取）";
            }
            flySpeedStatus.Text = ThreeNozzleHardwareConfig.IsConfigured ? "参数已加载；两模块低速独立保存。" : ThreeNozzleHardwareConfig.ConfigurationError;
        }

        private void SaveFlySpeed(object sender, EventArgs e)
        {
            string name = "未知";
            if (FrMain.frsuser != null) name = FrMain.frsuser.user1.cur_user.ToString();
            string error;
            if (!ThreeNozzleHardwareConfig.SetFlyTwoSpeed(chkFlyTwoSpeed.Checked, (double)flyLowInputs[0].Value,
                (double)flyLowInputs[1].Value, name, out error))
            {
                MessageBox.Show(this, error, "飞拍两段速", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RefreshFlySpeed();
            flySpeedStatus.Text = "已保存并应用；修改低速后请重新飞拍标定。";
        }
    }
}
