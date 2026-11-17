using System;

namespace UI.Class
{
    public enum EnvironmentInspectionScheduleMode
    {
        FixedTime = 0,
        HourlyInterval = 1
    }

    public static class EnvironmentInspectionSchedule
    {
        public static bool HasDueTime(
            DateTime previous,
            DateTime current,
            EnvironmentInspectionScheduleMode mode,
            TimeSpan fixedTime1,
            TimeSpan fixedTime2,
            int intervalHours,
            out DateTime dueTime)
        {
            dueTime = DateTime.MinValue;
            if (current <= previous)
            {
                return false;
            }

            bool found = false;
            for (DateTime day = previous.Date; day <= current.Date; day = day.AddDays(1))
            {
                if (mode == EnvironmentInspectionScheduleMode.HourlyInterval)
                {
                    int hours = Math.Max(1, Math.Min(24, intervalHours));
                    for (int hour = hours; hour <= 24; hour += hours)
                    {
                        DateTime candidate = day.AddHours(hour);
                        SetDueTime(candidate, previous, current, ref found, ref dueTime);
                    }
                }
                else
                {
                    SetDueTime(day.Add(fixedTime1), previous, current, ref found, ref dueTime);
                    SetDueTime(day.Add(fixedTime2), previous, current, ref found, ref dueTime);
                }
            }

            return found;
        }

        private static void SetDueTime(
            DateTime candidate,
            DateTime previous,
            DateTime current,
            ref bool found,
            ref DateTime dueTime)
        {
            if (candidate > previous && candidate <= current)
            {
                if (!found || candidate > dueTime)
                {
                    dueTime = candidate;
                }
                found = true;
            }
        }
    }
}
