using System;
using System.Collections.Generic;
using System.Linq;
using MotionCtrl;
using UI.Class;

namespace UI
{
    public partial class UpDownLoad
    {
        private bool ThreeNozzleDeferPostPlaceCheck(WS.MdDat md)
        {
            bool barcode = PT_SET.Isaloneset || (PT_SET.BarcodeMode == (int)PT_SET.BAR_SCAN.UP_SCAN &&
                !NewSysInf.UserParams.bUpWsChkQrCodeOff);
            return !Demo && !ThreeNozzleHasSpecialMode && ThreeNozzleFlyActive &&
                md != null && string.IsNullOrEmpty(md.ModuleCheckSampleId) && (barcode || PT_SET.bEnVsFB);
        }

        // Called inside LeaveFixture, before its sequence step and before the coordinator can release the fixture.
        // Images are local: a stop/restart always acquires fresh images for unaccepted placements.
        private const double ThreeNozzleBackCheckMaxColumnDx = 1.0;

        /// <summary>
        /// 开关：放料后回检从当前位置直接往外飞（默认开）。原来要先往夹具里退到“最远触发点 + 180mm”再飞，
        /// 放完料 Y 约 1078，要先退到约 1199（多走约 120mm 并停一次）。回检只看放料偏移，曝光 70µs，
        /// 触发点速度不到 2500 也不影响成像；触发补偿按触发点处的估算速度等比缩小（补偿量本身只有 0.1~0.2mm）。
        /// 关掉恢复原 180mm 助跑。双速飞拍开启时不用。
        /// </summary>
        public static bool ThreeNozzleBackCheckShortRunUpEnabled = true;
        /// <summary>短助跑时，起点离第一个触发点至少这么远（mm）。2500mm/s、Tacc 0.2s 时第一个触发点速度约 1000mm/s 以上。</summary>
        private const double ThreeNozzleBackCheckMinRunUp = 40.0;

        /// <summary>从静止起跑 runUp 毫米后的估算速度（梯形曲线），不超过工作速度。</summary>
        private double ThreeNozzleSpeedAfterRunUp(double runUp)
        {
            double v = ax_y.spd_work, ta = ax_y.tacc;
            if (ta > 10) ta /= 1000.0;
            if (!(v > 0) || !FlyFinite(ta) || ta <= 1e-6 || !(runUp > 0)) return v;
            return Math.Min(v, Math.Sqrt(2 * (v / ta) * runUp));
        }

        private EM_RES ThreeNozzleCompletePostPlaceChecks(ref bool quit, ThreeNozzleRuntimePair pair, double preferredEndY = double.NaN)
        {
            int[] slots = Enumerable.Range(0, 2).Where(s => (pair.Placed[s] || pair.ReworkStage[s] != 0) && !pair.Accepted[s]).ToArray();
            if (slots.Length == 0) return EM_RES.OK;
            var images = new Cam.VisionOutPutData[2];
            var shots = new List<ThreeNozzleFlyShot>();
            double x = pair.Pockets[slots[0]].st_pos[id].x;
            // 与放料前飞拍相同的同列 X 允许差（模块2 上下两排示教 X 差约 0.5mm，0.5 时几乎全退回定拍）。
            bool canFly = slots.All(s => pair.ReworkStage[s] == 0 && ThreeNozzleDeferPostPlaceCheck(pair.Pockets[s]) &&
                Math.Abs(pair.Pockets[s].st_pos[id].x - x) <= ThreeNozzleBackCheckMaxColumnDx);
            if (canFly)
            {
                // 短助跑：起点 = max(当前 Y, 最远名义拍照位 + 40mm)，一般就是当前 Y，不再往夹具里退
                bool shortRunUp = ThreeNozzleBackCheckShortRunUpEnabled && !ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled;
                double maxNominal = slots.Max(s => pair.Pockets[s].st_pos[id].y);
                double plannedStart = shortRunUp ? Math.Max(ax_y.fcmd_pos, maxNominal + ThreeNozzleBackCheckMinRunUp + 1.0) : double.NaN;
                double vWork = ax_y.spd_work;
                foreach (int slot in slots)
                {
                    WS.MdDat md = pair.Pockets[slot];
                    double offset = md.Num <= 8 ? list_xt[0].ws_cap_near_ofs : list_xt[0].ws_cap_far_ofs;
                    if (shortRunUp && vWork > 0)
                        offset *= ThreeNozzleSpeedAfterRunUp(plannedStart - md.st_pos[id].y) / vWork;   // 补偿 = 延迟 × 速度
                    shots.Add(new ThreeNozzleFlyShot {
                        Camera = upcam, Task = CONST.WsModUpFw, X = x, NominalY = md.st_pos[id].y,
                        TriggerY = ThreeNozzleFlyTrigger(md.st_pos[id].y, offset, false),
                        Identity = md.Num - 1, Label = "放后回检穴" + md.Num
                    });
                }
                double min = shots.Min(s => s.TriggerY), max = shots.Max(s => s.TriggerY);
                double runUp = shortRunUp ? ThreeNozzleBackCheckMinRunUp : ThreeNozzleFlyRunUp;
                double start = shortRunUp ? Math.Max(plannedStart, max + runUp) : Math.Max(ax_y.fcmd_pos, max + runUp);
                double end = Math.Max(ax_y.sln, min - ThreeNozzleFlyRunOut);
                // 接着要去的方向和回检飞的方向一致（Y 减小）：直接飞到那里，不在夹具口停
                if (FlyFinite(preferredEndY) && preferredEndY < end && preferredEndY >= ax_y.sln)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 放料后回检：终点 {1:F3} 改为接下来的位置 Y={2:F3}，飞完不停直接到", disc, end, preferredEndY));
                    end = preferredEndY;
                }
                if (shortRunUp)
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format(
                        "{0} 放料后回检：短助跑，从 Y={1:F3} 起飞（当前 Y={2:F3}，助跑 {3:F1}mm，第一个触发点速度约 {4:F0}mm/s，补偿按速度缩放）",
                        disc, start, ax_y.fcmd_pos, start - max, ThreeNozzleSpeedAfterRunUp(start - max)));
                canFly = FlyFinite(x) && FlyFinite(start) && FlyFinite(end) &&
                    start <= ax_y.slp && end < min - 5 && start - max >= runUp - 0.001 &&
                    shots.All(s => FlyFinite(s.TriggerY)) &&
                    (shots.Count == 1 || Math.Abs(shots[0].TriggerY - shots[1].TriggerY) >= 0.001);
                if (canFly)
                {
                    EM_RES capture = ThreeNozzleFlyPass(ref quit, "放料后回检", x, start, end, shots);
                    // Motion/trigger failures propagate. Only missing/unusable images use static fallback.
                    if (capture != EM_RES.OK) return capture;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        Cam.VisionOutPutData data = shots[i].Result;
                        if (data == null || !data.bUpdate || !data.bOK) continue;
                        data = data.Clone();
                        // Convert the small common-X difference back to the taught inspection position.
                        data.PosMM.x += x - pair.Pockets[slots[i]].st_pos[id].x;
                        images[slots[i]] = data;
                    }
                }
            }
            if (!canFly)
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                    "{0} 放料后回检：飞拍未启用、返工续接、拍照X差超过1.0mm或退离行程不足，按定拍回检；当前Y={1:F3} 软限位={2:F3}~{3:F3} 拍照位={4}",
                    disc, ax_y.fcmd_pos, ax_y.sln, ax_y.slp,
                    string.Join(";", slots.Select(s => string.Format("穴{0}:X={1:F3},Y={2:F3}",
                        pair.Pockets[s].Num, pair.Pockets[s].st_pos[id].x, pair.Pockets[s].st_pos[id].y)))));
            int revision = pair.PlacementRevision;
            foreach (int slot in slots)
            {
                if (quit) return EM_RES.QUIT;
                // Replacing a rejected part invalidates the remaining images from the original pass.
                Cam.VisionOutPutData image = pair.PlacementRevision == revision ? images[slot] : null;
                if (image == null)
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 放料后回检：穴{1} 无可用飞拍结果，定拍补检",
                        disc, pair.Pockets[slot].Num));
                EM_RES result = ThreeNozzlePlaceOne(ref quit, pair, slot, true, true, image);
                if (result != EM_RES.OK) return result;
            }
            return EM_RES.OK;
        }
    }
}
