using System;
using System.Drawing;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    public partial class FrSys
    {
        private GroupBox groupOtpDarkInspection;
        private CheckBox chkOtpDarkInspectionEnabled;
        private bool refreshingOtpDarkInspection;

        private void InitOtpDarkInspectionControls()
        {
            groupOtpDarkInspection = new GroupBox();
            groupOtpDarkInspection.Font = new Font("Microsoft YaHei", 16F, FontStyle.Regular, GraphicsUnit.Point, 134);
            groupOtpDarkInspection.Location = new Point(768, 280);
            groupOtpDarkInspection.Name = "groupOtpDarkInspection";
            groupOtpDarkInspection.Size = new Size(300, 110);
            groupOtpDarkInspection.TabIndex = 164;
            groupOtpDarkInspection.TabStop = false;
            groupOtpDarkInspection.Text = "OTP\u6697\u6001\u6d4b\u8bd5";

            chkOtpDarkInspectionEnabled = new CheckBox();
            chkOtpDarkInspectionEnabled.AutoSize = true;
            chkOtpDarkInspectionEnabled.Font = new Font("Microsoft YaHei", 14F, FontStyle.Regular, GraphicsUnit.Point, 134);
            chkOtpDarkInspectionEnabled.Location = new Point(20, 48);
            chkOtpDarkInspectionEnabled.Name = "chkOtpDarkInspectionEnabled";
            chkOtpDarkInspectionEnabled.Text = "\u542f\u7528OTP\u6697\u6001\u6d4b\u8bd5";
            chkOtpDarkInspectionEnabled.UseVisualStyleBackColor = true;
            chkOtpDarkInspectionEnabled.CheckedChanged += chkOtpDarkInspectionEnabled_CheckedChanged;
            groupOtpDarkInspection.Controls.Add(chkOtpDarkInspectionEnabled);
            tabPage8.Enter += tabPage8OtpDarkInspection_Enter;
            tabPage8.Controls.Add(groupOtpDarkInspection);

            RefreshOtpDarkInspectionSetting();
        }

        private void tabPage8OtpDarkInspection_Enter(object sender, EventArgs e)
        {
            RefreshOtpDarkInspectionSetting();
        }

        private void RefreshOtpDarkInspectionSetting()
        {
            refreshingOtpDarkInspection = true;
            chkOtpDarkInspectionEnabled.Checked = PT_SET.bOtpDarkInspectionEnabled;
            refreshingOtpDarkInspection = false;
        }

        private void chkOtpDarkInspectionEnabled_CheckedChanged(object sender, EventArgs e)
        {
            if (refreshingOtpDarkInspection) return;
            PT_SET.bOtpDarkInspectionEnabled = chkOtpDarkInspectionEnabled.Checked;
        }
    }
}
