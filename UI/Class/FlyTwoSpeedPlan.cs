using System;
using System.Linq;

namespace UI
{
    // Only plans/validates speeds; never commands an axis or changes material records.
    internal sealed class FlyTwoSpeedPlan
    {
        internal readonly double High, Low, SwitchY, ExitY;
        internal readonly bool StartLow;
        private readonly double origin, direction, first, last, switchDistance, exitDistance, tolerance;
        private bool lowRequested, lowConfirmed, highRestored;

        internal FlyTwoSpeedPlan(double start, double end, double[] triggers,
            double high, double low, double accelerationTime, double decelerationTime, double smoothTime)
        {
            if (!Finite(start) || !Finite(end) || start == end || !Finite(high) || !Finite(low) ||
                low <= 0 || high <= 0 || low > high || triggers == null || triggers.Length == 0 ||
                !Finite(accelerationTime) || accelerationTime < 0 || !Finite(decelerationTime) || decelerationTime < 0 ||
                !Finite(smoothTime) || smoothTime < 0)
                throw new ArgumentException("两段速速度、行程或加减速参数无效");
            origin = start;
            direction = end > start ? 1 : -1;
            double length = Math.Abs(end - start);
            double[] distances = triggers.Select(y => (y - start) * direction).ToArray();
            if (distances.Any(d => !Finite(d) || d <= 0 || d >= length))
                throw new ArgumentException("飞拍触发点不在本趟行程内");
            first = distances.Min(); last = distances.Max();
            High = high; Low = low;
            // Include deceleration and a 100 ms polling/command margin before the stable low-speed lead.
            double settle = Math.Max(10, low * 0.05);
            switchDistance = first - settle - (high + low) * 0.5 * (decelerationTime + smoothTime) - high * 0.1;
            StartLow = switchDistance <= 1 || high == low;
            lowRequested = StartLow;
            exitDistance = last + Math.Max(50, low * 0.1);
            SwitchY = start + direction * Math.Max(0, switchDistance);
            ExitY = start + direction * exitDistance;
            tolerance = Math.Max(5, low * 0.05);
        }

        internal bool Update(double position, double actualSpeed, out double? command, out string error)
        {
            command = null; error = null;
            if (!Finite(position) || !Finite(actualSpeed)) { error = "无法读取有效的运动位置/实际速度"; return false; }
            double distance = (position - origin) * direction;
            if (highRestored) return true;
            if (!lowRequested)
            {
                if (distance >= first - 5) { error = "未在首拍前及时下发低速，本趟改定拍"; return false; }
                if (distance >= switchDistance) { lowRequested = true; command = Low; }
                return true;
            }
            bool atLow = Math.Abs(Math.Abs(actualSpeed) - Low) <= tolerance;
            if (distance <= first && atLow) lowConfirmed = true;
            if (distance >= first - 5 && distance <= last + 5 && (!lowConfirmed || !atLow))
            { error = "拍照区实际速度未稳定在设定低速，本趟改定拍"; return false; }
            if (distance > last + 5 && !lowConfirmed)
            { error = "未确认低速拍照状态，本趟改定拍"; return false; }
            if (distance >= exitDistance && lowConfirmed) { command = High; highRestored = true; }
            return true;
        }

        internal bool LowConfirmed { get { return lowConfirmed; } }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
