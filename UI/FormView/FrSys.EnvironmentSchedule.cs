using System;
using System.Drawing;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    public partial class FrSys
    {
        private RadioButton rbtnEnvironmentFixedTime;
        private RadioButton rbtnEnvironmentHourly;
        private Label labelEnvironmentScheduleMode;
        private Label labelEnvironmentIntervalUnit;

        protected override void OnLoad(EventArgs e)
        {
            PrepareDualArmSafetyDistanceEditor();
            base.OnLoad(e);
            InitEnvironmentScheduleControls();
            InitDualArmSafetyDistanceControls();
            InitOtpDarkInspectionControls();
        }

        private void InitEnvironmentScheduleControls()
        {
            AutoCheckTimer.DecimalPlaces = 0;
            AutoCheckTimer.Minimum = 1;
            AutoCheckTimer.Maximum = 24;
            AutoCheckTimer.Location = new Point(705, 151);
            AutoCheckTimer.Value = Math.Max(1, Math.Min(24, (decimal)PT_SET.CheckTimer));
            AutoCheckTimer.Visible = true;

            labelEnvironmentScheduleMode = new Label();
            labelEnvironmentScheduleMode.AutoSize = true;
            labelEnvironmentScheduleMode.Location = new Point(548, 73);
            labelEnvironmentScheduleMode.Text = "\u70b9\u68c0\u5b9a\u65f6\u6a21\u5f0f";

            rbtnEnvironmentFixedTime = new RadioButton();
            rbtnEnvironmentFixedTime.AutoSize = true;
            rbtnEnvironmentFixedTime.Location = new Point(548, 112);
            rbtnEnvironmentFixedTime.Text = "\u56fa\u5b9a\u65f6\u95f4";

            rbtnEnvironmentHourly = new RadioButton();
            rbtnEnvironmentHourly.AutoSize = true;
            rbtnEnvironmentHourly.Location = new Point(548, 155);
            rbtnEnvironmentHourly.Text = "\u6309\u5c0f\u65f6\u5468\u671f";

            labelEnvironmentIntervalUnit = new Label();
            labelEnvironmentIntervalUnit.AutoSize = true;
            labelEnvironmentIntervalUnit.Location = new Point(814, 157);
            labelEnvironmentIntervalUnit.Text = "\u5c0f\u65f6";

            AutoCheckParamSetting.Controls.Add(labelEnvironmentScheduleMode);
            AutoCheckParamSetting.Controls.Add(rbtnEnvironmentFixedTime);
            AutoCheckParamSetting.Controls.Add(rbtnEnvironmentHourly);
            AutoCheckParamSetting.Controls.Add(labelEnvironmentIntervalUnit);

            rbtnEnvironmentFixedTime.CheckedChanged += EnvironmentScheduleMode_CheckedChanged;
            rbtnEnvironmentHourly.CheckedChanged += EnvironmentScheduleMode_CheckedChanged;

            rbtnEnvironmentHourly.Checked = PT_SET.EnvironmentCheckScheduleMode == 1;
            rbtnEnvironmentFixedTime.Checked = !rbtnEnvironmentHourly.Checked;
            UpdateEnvironmentScheduleControls();
        }

        private void EnvironmentScheduleMode_CheckedChanged(object sender, EventArgs e)
        {
            UpdateEnvironmentScheduleControls();
        }

        private void UpdateEnvironmentScheduleControls()
        {
            bool fixedTime = rbtnEnvironmentFixedTime.Checked;
            dateTimePicker3.Enabled = fixedTime;
            dateTimePicker4.Enabled = fixedTime;
            labeltime.Enabled = fixedTime;
            label104.Enabled = fixedTime;
            label112.Enabled = fixedTime;
            AutoCheckTimer.Enabled = !fixedTime;
            labelEnvironmentIntervalUnit.Enabled = !fixedTime;
            PT_SET.EnvironmentCheckScheduleMode = rbtnEnvironmentHourly.Checked ? 1 : 0;
        }
    }
}
