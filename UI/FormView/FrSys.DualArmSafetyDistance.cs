using System;
using System.Drawing;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    public partial class FrSys
    {
        private bool dualArmSafetyDistanceControlsEnabled = true;
        private GroupBox groupBoxDualArmSafetyDistance;
        private NumericUpDown nudUdSafeDistance;
        private NumericUpDown nudUdMaxCombinedExtent;
        private CheckBox chkCustomUdSafeDistance;
        private CheckBox chkSpecialDualArmAvoidance;

        private void PrepareDualArmSafetyDistanceEditor()
        {
        }

        private void InitDualArmSafetyDistanceControls()
        {
            if (groupBoxDualArmSafetyDistance != null) return;

            groupBoxDualArmSafetyDistance = new GroupBox();
            groupBoxDualArmSafetyDistance.Font = groupBox60.Font;
            groupBoxDualArmSafetyDistance.Location = new Point(435, 895);
            groupBoxDualArmSafetyDistance.Name = "groupBoxDualArmSafetyDistance";
            groupBoxDualArmSafetyDistance.Size = new Size(195, 144);
            groupBoxDualArmSafetyDistance.TabStop = false;
            groupBoxDualArmSafetyDistance.Text = "双臂安全距离(mm)";

            chkCustomUdSafeDistance = new CheckBox();
            chkCustomUdSafeDistance.AutoSize = true;
            chkCustomUdSafeDistance.Location = new Point(10, 22);
            chkCustomUdSafeDistance.Name = "chkCustomUdSafeDistance";
            chkCustomUdSafeDistance.Text = "启用自定义";
            chkCustomUdSafeDistance.UseVisualStyleBackColor = true;
            chkCustomUdSafeDistance.Checked = PT_SET.bCustomUdSafeDistance;

            chkSpecialDualArmAvoidance = new CheckBox();
            chkSpecialDualArmAvoidance.AutoSize = true;
            chkSpecialDualArmAvoidance.Location = new Point(10, 46);
            chkSpecialDualArmAvoidance.Name = "chkSpecialDualArmAvoidance";
            chkSpecialDualArmAvoidance.Text = "特殊避障";
            chkSpecialDualArmAvoidance.UseVisualStyleBackColor = true;
            chkSpecialDualArmAvoidance.Checked = PT_SET.bSpecialDualArmAvoidance;

            Label labelUdSafeDistance = new Label();
            labelUdSafeDistance.AutoSize = true;
            labelUdSafeDistance.Location = new Point(10, 76);
            labelUdSafeDistance.Text = "距离";

            nudUdSafeDistance = new NumericUpDown();
            nudUdSafeDistance.DecimalPlaces = 1;
            nudUdSafeDistance.Increment = 1;
            nudUdSafeDistance.Location = new Point(60, 70);
            nudUdSafeDistance.Maximum = (decimal)PT_SET.MaxUdSafeDistance;
            nudUdSafeDistance.Minimum = (decimal)PT_SET.MinUdSafeDistance;
            nudUdSafeDistance.Name = "nudUdSafeDistance";
            nudUdSafeDistance.Size = new Size(125, 28);
            nudUdSafeDistance.TextAlign = HorizontalAlignment.Center;
            nudUdSafeDistance.Value = (decimal)PT_SET.UdSafeDistance;

            Label labelUdMaxCombinedExtent = new Label();
            labelUdMaxCombinedExtent.AutoSize = true;
            labelUdMaxCombinedExtent.Location = new Point(10, 108);
            labelUdMaxCombinedExtent.Text = "合计上限";

            nudUdMaxCombinedExtent = new NumericUpDown();
            nudUdMaxCombinedExtent.DecimalPlaces = 1;
            nudUdMaxCombinedExtent.Increment = 1;
            nudUdMaxCombinedExtent.Location = new Point(85, 102);
            nudUdMaxCombinedExtent.Maximum = (decimal)PT_SET.MaxUdMaxCombinedExtent;
            nudUdMaxCombinedExtent.Minimum = (decimal)PT_SET.MinUdMaxCombinedExtent;
            nudUdMaxCombinedExtent.Name = "nudUdMaxCombinedExtent";
            nudUdMaxCombinedExtent.Size = new Size(100, 28);
            nudUdMaxCombinedExtent.TextAlign = HorizontalAlignment.Center;
            nudUdMaxCombinedExtent.Value = (decimal)PT_SET.UdMaxCombinedExtent;

            groupBoxDualArmSafetyDistance.Controls.Add(chkCustomUdSafeDistance);
            groupBoxDualArmSafetyDistance.Controls.Add(chkSpecialDualArmAvoidance);
            groupBoxDualArmSafetyDistance.Controls.Add(labelUdSafeDistance);
            groupBoxDualArmSafetyDistance.Controls.Add(nudUdSafeDistance);
            groupBoxDualArmSafetyDistance.Controls.Add(labelUdMaxCombinedExtent);
            groupBoxDualArmSafetyDistance.Controls.Add(nudUdMaxCombinedExtent);
            tp_setparm_1.Controls.Add(groupBoxDualArmSafetyDistance);
            groupBoxDualArmSafetyDistance.BringToFront();

            chkCustomUdSafeDistance.CheckedChanged += chkCustomUdSafeDistance_CheckedChanged;
            chkSpecialDualArmAvoidance.CheckedChanged += chkSpecialDualArmAvoidance_CheckedChanged;
            nudUdSafeDistance.ValueChanged += nudUdSafeDistance_ValueChanged;
            nudUdMaxCombinedExtent.ValueChanged += nudUdMaxCombinedExtent_ValueChanged;
            UpdateDualArmSafetyDistanceControls();
            LogDualArmSafetyDistance("系统参数加载");
        }

        private void chkCustomUdSafeDistance_CheckedChanged(object sender, EventArgs e)
        {
            PT_SET.bCustomUdSafeDistance = chkCustomUdSafeDistance.Checked;
            UpdateDualArmSafetyDistanceControls();
            LogDualArmSafetyDistance("自定义安全距离开关");
        }

        private void nudUdSafeDistance_ValueChanged(object sender, EventArgs e)
        {
            PT_SET.UdSafeDistance = (double)nudUdSafeDistance.Value;
            LogDualArmSafetyDistance("自定义安全距离数值");
        }

        private void nudUdMaxCombinedExtent_ValueChanged(object sender, EventArgs e)
        {
            PT_SET.UdMaxCombinedExtent = (double)nudUdMaxCombinedExtent.Value;
            LogDualArmSafetyDistance("双臂合计上限");
        }

        private void chkSpecialDualArmAvoidance_CheckedChanged(object sender, EventArgs e)
        {
            PT_SET.bSpecialDualArmAvoidance = chkSpecialDualArmAvoidance.Checked;
            LogDualArmSafetyDistance("特殊避障开关");
        }

        private void UpdateDualArmSafetyDistanceControls()
        {
            if (nudUdSafeDistance == null) return;
            nudUdSafeDistance.Enabled = dualArmSafetyDistanceControlsEnabled && chkCustomUdSafeDistance.Checked;
            nudUdMaxCombinedExtent.Enabled = dualArmSafetyDistanceControlsEnabled;
        }

        private void SetDualArmSafetyDistanceControlsEnabled(bool enabled)
        {
            dualArmSafetyDistanceControlsEnabled = enabled;
            if (groupBoxDualArmSafetyDistance == null) return;
            chkCustomUdSafeDistance.Enabled = enabled;
            chkSpecialDualArmAvoidance.Enabled = enabled;
            UpdateDualArmSafetyDistanceControls();
        }

        private void ApplyDualArmSafetyDistanceControls()
        {
            if (nudUdSafeDistance == null) return;
            PT_SET.bCustomUdSafeDistance = chkCustomUdSafeDistance.Checked;
            PT_SET.bSpecialDualArmAvoidance = chkSpecialDualArmAvoidance.Checked;
            PT_SET.UdSafeDistance = (double)nudUdSafeDistance.Value;
            PT_SET.UdMaxCombinedExtent = (double)nudUdMaxCombinedExtent.Value;
            LogDualArmSafetyDistance("系统参数保存");
        }

        private void LogDualArmSafetyDistance(string source)
        {
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format(
                "{0}: DualArmSafeDistance CustomEnabled={1} SpecialAvoidance={2} Configured={3:F1} Effective={4:F1} MaxCombinedExtent={5:F1}",
                source, PT_SET.bCustomUdSafeDistance, PT_SET.bSpecialDualArmAvoidance,
                PT_SET.UdSafeDistance, PT_SET.EffectiveUdSafeDistance, PT_SET.UdMaxCombinedExtent));
        }
    }
}
