using System;
using System.Collections.Generic;
namespace UI
{
    // Hardware-free bounded probe algorithm, exercised with fake motion/sensors in regression tests.
    public static class NozzleZProbe
    {
        public static double[] Measure(double approach, double end, double step, Action<double> move,
            Func<double> position, Action<bool> vacuum, Func<bool> sensed, Action check, Action<int> delay)
        {
            if (!NozzleZCalibrationData.Finite(approach) || !NozzleZCalibrationData.Finite(end) ||
                !NozzleZCalibrationData.Finite(step) || approach < 0 || end <= approach || step <= 0 || step > 0.1 ||
                (end - approach) / step > 10000) throw new ArgumentException("探测范围/步距无效或步数过大");
            var samples = new List<double>();
            try
            {
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    check(); vacuum(false); delay(150); move(approach);
                    vacuum(true); delay(150); check();
                    if (sensed()) throw new InvalidOperationException("接近位置已检测真空，请检查吸嘴及接近高度");
                    double target = approach;
                    bool contact = false;
                    while (target < end)
                    {
                        check(); target = Math.Min(end, target + step); move(target); delay(30); check();
                        if (!sensed()) continue;
                        delay(50); check();
                        if (!sensed()) continue;
                        double measured = position();
                        if (!NozzleZCalibrationData.Finite(measured) || measured <= approach || measured > end + 0.001)
                            throw new InvalidOperationException("接触位置反馈超出探测范围");
                        samples.Add(measured); contact = true; break;
                    }
                    if (!contact) throw new InvalidOperationException("已到探测终点仍未检测到接触，测高失败");
                    vacuum(false); delay(150); check(); move(approach);
                }
                return samples.ToArray();
            }
            finally { vacuum(false); }
        }
    }
}
