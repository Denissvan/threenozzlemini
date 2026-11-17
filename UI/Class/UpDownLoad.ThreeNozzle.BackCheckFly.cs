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

        private EM_RES ThreeNozzleCompletePostPlaceChecks(ref bool quit, ThreeNozzleRuntimePair pair)
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
                foreach (int slot in slots)
                {
                    WS.MdDat md = pair.Pockets[slot];
                    shots.Add(new ThreeNozzleFlyShot {
                        Camera = upcam, Task = CONST.WsModUpFw, X = x, NominalY = md.st_pos[id].y,
                        TriggerY = ThreeNozzleFlyTrigger(md.st_pos[id].y,
                            md.Num <= 8 ? list_xt[0].ws_cap_near_ofs : list_xt[0].ws_cap_far_ofs, false),
                        Identity = md.Num - 1, Label = "放后回检穴" + md.Num
                    });
                }
                double min = shots.Min(s => s.TriggerY), max = shots.Max(s => s.TriggerY);
                double start = Math.Max(ax_y.fcmd_pos, max + ThreeNozzleFlyRunUp);
                double end = Math.Max(ax_y.sln, min - ThreeNozzleFlyRunOut);
                canFly = FlyFinite(x) && FlyFinite(start) && FlyFinite(end) &&
                    start <= ax_y.slp && end < min - 5 && start - max >= ThreeNozzleFlyRunUp - 0.001 &&
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
