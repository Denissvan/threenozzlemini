using System;
using MotionCtrl;

namespace UI
{
    public static partial class MT
    {
        [ThreadStatic] private static ContactOffset contactOffset;
        private sealed class ContactOffset
        {
            internal XT Nozzle;
            internal double X0, Y0, X1, Y1, Z;
        }

        private static bool FiniteContactCoordinate(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        // The only low-Z lateral exception: one explicitly selected nozzle,
        // the existing <=20 mm recipe offset, and both other Z axes retracted.
        internal static bool IsThreeNozzleLateralMoveSafe(AXIS axis, double target)
        {
            int arm = GetLateralArmId(axis);
            if (arm < 0) return true;
            if (AreAllArmZSafe(arm)) return true;
            ContactOffset scope = contactOffset;
            if (scope == null || scope.Nozzle.Parent == null || scope.Nozzle.Parent.id != arm ||
                ThreeNozzleCoordinator.IsTrayOnlyPhase(arm) || !FiniteContactCoordinate(target)) return false;
            XT nozzle = scope.Nozzle;
            if (axis != nozzle.ax_x && axis != nozzle.ax_y) return false;
            foreach (AXIS z in GetArmZAxes(arm))
            {
                double expected = z == nozzle.ax_z ? scope.Z : 0;
                if (!z.card.isReady || z.home_status != AXIS.HOME_STA.OK || !z.isStop ||
                    !FiniteContactCoordinate(z.fenc_pos) || !FiniteContactCoordinate(z.fcmd_pos) ||
                    Math.Abs(z.fenc_pos - expected) > ArmZSafeTolerance || Math.Abs(z.fcmd_pos - expected) > ArmZSafeTolerance)
                    return false;
            }
            double min = axis == nozzle.ax_x ? Math.Min(scope.X0, scope.X1) : Math.Min(scope.Y0, scope.Y1);
            double max = axis == nozzle.ax_x ? Math.Max(scope.X0, scope.X1) : Math.Max(scope.Y0, scope.Y1);
            return FiniteContactCoordinate(axis.fenc_pos) && axis.fenc_pos >= min - 0.01 &&
                axis.fenc_pos <= max + 0.01 && target >= min - 0.01 && target <= max + 0.01;
        }

        internal static EM_RES MoveContactOffset(ref bool quit, XT nozzle, double x, double y, double z)
        {
            if (quit) return EM_RES.QUIT;
            if (nozzle == null || nozzle.Parent == null || contactOffset != null ||
                !FiniteContactCoordinate(x) || !FiniteContactCoordinate(y) || !FiniteContactCoordinate(z) ||
                !FiniteContactCoordinate(nozzle.ax_x.fenc_pos) || !FiniteContactCoordinate(nozzle.ax_y.fenc_pos) ||
                Math.Abs(nozzle.ax_x.fenc_pos - x) > 20.01 || Math.Abs(nozzle.ax_y.fenc_pos - y) > 20.01)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("运动拦截：{0} 低位偏移移动参数无效或超过20mm，当前X={1:F3} Y={2:F3}，目标X={3:F3} Y={4:F3} Z={5:F3}",
                    nozzle == null ? "?" : nozzle.disc, nozzle == null ? double.NaN : nozzle.ax_x.fenc_pos, nozzle == null ? double.NaN : nozzle.ax_y.fenc_pos, x, y, z));
                return EM_RES.MOVE_PROTECT;
            }
            contactOffset = new ContactOffset { Nozzle = nozzle, X0 = nozzle.ax_x.fenc_pos,
                Y0 = nozzle.ax_y.fenc_pos, X1 = x, Y1 = y, Z = z };
            try
            {
                if (!IsThreeNozzleLateralMoveSafe(nozzle.ax_x, x) || !IsThreeNozzleLateralMoveSafe(nozzle.ax_y, y))
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("运动拦截：{0} 低位偏移移动不满足条件（只允许本吸嘴Z在 {1:F3}、其它Z在0、偏移不超过20mm）。{2}",
                        nozzle.disc, z, DescribeLateralBlock(nozzle.ax_x, x)));
                    return EM_RES.MOVE_PROTECT;
                }
                // Ordinary MT.Move still owns X reservations, stop checks, limits,
                // command dispatch gates and result propagation.
                return Move(ref quit, ref nozzle.ax_x, x, ref nozzle.ax_y, y, bchkps: false);
            }
            finally { contactOffset = null; }
        }
    }
}
