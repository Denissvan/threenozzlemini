using System;
using MotionCtrl;
using UI.Class;

namespace UI
{
    public partial class FrMain
    {
        private DateTime _lastEnvironmentScheduleCheck;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ReplaceEnvironmentScheduleTimerHandler();
            // 模组自动点检：加载设置/未完成任务并启动调度
            UI.ModuleCheck.ModuleCheckMachineAdapter.StartModule();
        }

        private void ReplaceEnvironmentScheduleTimerHandler()
        {
            if (_eightHourTimer == null)
            {
                return;
            }

            _eightHourTimer.Tick -= EightHourTimer_Tick;
            _eightHourTimer.Tick += EnvironmentScheduleTimer_Tick;
            _lastEnvironmentScheduleCheck = DateTime.Now;
        }

        private void EnvironmentScheduleTimer_Tick(object sender, EventArgs e)
        {
            AutoInspectionParameter._elapsedMilliseconds += _eightHourTimer.Interval;

            DateTime now = DateTime.Now;
            DateTime previous = _lastEnvironmentScheduleCheck;
            _lastEnvironmentScheduleCheck = now;

            EnvironmentInspectionScheduleMode mode =
                PT_SET.EnvironmentCheckScheduleMode == (int)EnvironmentInspectionScheduleMode.HourlyInterval
                    ? EnvironmentInspectionScheduleMode.HourlyInterval
                    : EnvironmentInspectionScheduleMode.FixedTime;

            TimeSpan fixedTime1 = TimeSpan.Zero;
            TimeSpan fixedTime2 = TimeSpan.Zero;
            if (mode == EnvironmentInspectionScheduleMode.FixedTime &&
                (!TimeSpan.TryParse(PT_SET.CheckTimeMorning1, out fixedTime1) ||
                 !TimeSpan.TryParse(PT_SET.CheckTimeEvening2, out fixedTime2)))
            {
                return;
            }

            DateTime dueTime;
            bool due = EnvironmentInspectionSchedule.HasDueTime(
                previous,
                now,
                mode,
                fixedTime1,
                fixedTime2,
                (int)Math.Round(PT_SET.CheckTimer),
                out dueTime);
            if (!due)
            {
                return;
            }

            AutoInspectionParameter.ifcheckontime = true;
            AutoInspectionParameter.ifcheck = false;
            string modeText = mode == EnvironmentInspectionScheduleMode.HourlyInterval
                ? "\u6309\u5c0f\u65f6\u5468\u671f"
                : "\u56fa\u5b9a\u65f6\u95f4";
            VAR.msg.AddMsg(
                Msg.EM_MSGTYPE.DBG,
                string.Format("\u73af\u5883\u70b9\u68c0\u65f6\u95f4\u5230,\u6a21\u5f0f:{0},\u8ba1\u5212\u65f6\u95f4:{1:yyyy-MM-dd HH:mm:ss}", modeText, dueTime));
        }
    }
}
