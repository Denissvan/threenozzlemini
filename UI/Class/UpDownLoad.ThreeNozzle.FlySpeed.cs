using System;
using System.Collections.Generic;
using System.Linq;
using MotionCtrl;

namespace UI
{
    public partial class UpDownLoad
    {
        // Uses the existing guarded move/wait path. No background thread commands the axis.
        private EM_RES ThreeNozzleFlySpeedMove(ref bool quit, string what, double endY,
            Dictionary<Cam, List<ThreeNozzleFlyShot>> ordered, out bool usable)
        {
            usable = true;
            if (!ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled)
                return MT.ZupMove(ref quit, ref ax_y, endY);
            if (ax_y.card.brand != CARD.BRAND.LEADSHINE) return EM_RES.PARA_ERR;

            FlyTwoSpeedPlan plan;
            try
            {
                plan = new FlyTwoSpeedPlan(ax_y.fcmd_pos, endY,
                    ordered.Values.SelectMany(s => s).Select(s => s.TriggerY).ToArray(),
                    ax_y.spd_work, ThreeNozzleHardwareConfig.FlyLowSpeed(id), ax_y.tacc, ax_y.tdec, ax_y.ts);
            }
            catch (ArgumentException ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + " 飞拍两段速参数错误：" + ex.Message);
                return EM_RES.PARA_ERR;
            }

            bool valid = true;
            EM_RES result = EM_RES.ERR, restore = EM_RES.ERR;
            double initial = plan.StartLow ? plan.Low : plan.High;
            Action<string> log = message =>
            {
                string text = string.Format("{0} 飞拍两段速[{1}] {2}", disc, what, message);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, text);
                foreach (Cam camera in ordered.Keys) camera.FlyDiagnosticRecord(text);
            };
            log(string.Format("高速={0:F1} 低速={1:F1} 起步={2:F1} 减速点={3:F3} 恢复点={4:F3}",
                plan.High, plan.Low, initial, plan.SwitchY, plan.ExitY));
            try
            {
                result = ax_y.SetSpeed(Math.Min(ax_y.spd_start, initial), initial,
                    Math.Min(ax_y.spd_stop, initial), ax_y.tacc, ax_y.tdec);
                if (result == EM_RES.OK)
                {
                    using (ax_y.ObserveMoveProgress(() =>
                    {
                        if (!valid) return EM_RES.OK;
                        double y = ax_y.fcmd_pos, actual = ax_y.ReadMotionSpeed();
                        double? command; string error;
                        if (!plan.Update(y, actual, out command, out error))
                        {
                            valid = false;
                            log(string.Format("结果作废：{0} Y={1:F3} 实际速度={2:F1}；完成本趟后定拍补拍", error, y, actual));
                            // Finish at low speed; the caller's existing static recapture uses null results.
                            return ax_y.ChangSpeed(plan.Low);
                        }
                        if (command.HasValue)
                        {
                            log(string.Format("切换速度={0:F1} Y={1:F3} 实际速度={2:F1}", command.Value, y, actual));
                            return ax_y.ChangSpeed(command.Value);
                        }
                        return EM_RES.OK;
                    }))
                        result = MT.ZupMove(ref quit, ref ax_y, endY);
                }
            }
            finally
            {
                restore = ax_y.SetSpeed(ax_y.spd_start, plan.High, ax_y.spd_stop, ax_y.tacc, ax_y.tdec);
                usable = valid && plan.LowConfirmed;
                log(string.Format("结束 返回={0} 低速已确认={1} 图像可用={2} 恢复工作速度={3}", result, plan.LowConfirmed, usable, restore));
            }
            if (restore != EM_RES.OK) return restore;
            return result;
        }
    }
}
