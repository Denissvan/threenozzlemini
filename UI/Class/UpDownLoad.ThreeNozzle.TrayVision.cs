using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MotionCtrl;
using UI.Class;

namespace UI
{
    public partial class UpDownLoad
    {
        private sealed class ThreeNozzleTrayVisionRecord
        {
            internal Product.Tray Tray;
            internal ST_XYA Compensation;
        }

        // Both arms use the same physical tray. Only its first empty exposure
        // determines compensation; later placed parts must not alter the pattern.
        private static readonly Dictionary<TrayBox, ThreeNozzleTrayVisionRecord> threeNozzleTrayVision =
            new Dictionary<TrayBox, ThreeNozzleTrayVisionRecord>();

        private static void ThreeNozzleInvalidateTrayCompensation(TrayBox tray)
        {
            lock (threeNozzleTrayVision) threeNozzleTrayVision.Remove(tray);
        }

        // Caller holds placetray. photoPosition supplies the arm's candidate X;
        // the actual empty-tray photo coordinates come from TrayVsPos, as in
        // FindOkOrNgTrayPos. No fixture return or FlyCamToTray is permitted here.
        private EM_RES ThreeNozzleGetTrayCompensation(ref bool quit, TrayBox tray,
            ST_XYZA photoPosition, out ST_XYA compensation)
        {
            compensation = new ST_XYA();
            if (quit) return EM_RES.QUIT;
            if (!PT_SET.bEnVsTray || tray.IsFeedTray) return EM_RES.OK;
            Product.Tray current = tray.tray_cur;
            if (current == null) return EM_RES.ERR;
            lock (threeNozzleTrayVision)
            {
                ThreeNozzleTrayVisionRecord cached;
                if (threeNozzleTrayVision.TryGetValue(tray, out cached))
                {
                    if (Object.ReferenceEquals(cached.Tray, current))
                    {
                        compensation = cached.Compensation;
                        return EM_RES.OK;
                    }
                    threeNozzleTrayVision.Remove(tray);
                }
            }
            // On a partially filled tray loaded at startup, preserve the old
            // zero-default behavior rather than photograph an occupied pattern.
            if (current.GetPosList(Product.EM_CM_RES.EMPTY).Count != current.list_pos.Count)
                return EM_RES.OK;
            photoPosition.y = current.TrayVsPos[id].y;
            photoPosition.a = -current.TrayVsPos[id].x + photoPosition.x + current.TrayVsPos[id].z;
            if (photoPosition.a < 0)
            {
                photoPosition.x -= photoPosition.a;
                photoPosition.a = 0;
            }
            Camcfgset(0U);
            EM_RES result = MT.ZupMove(ref quit, ref ax_x, photoPosition.x,
                ref ax_y, photoPosition.y, ref tray.ax_x, photoPosition.a);
            if (result != EM_RES.OK) return result;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (quit) return EM_RES.QUIT;
                result = upcam.FindTaskTriAndWait(CONST.TrayUpFw, Demo);
                if (result == EM_RES.OK)
                {
                    compensation = upcam.curTask.ResData.PosMM;
                    lock (threeNozzleTrayVision)
                        threeNozzleTrayVision[tray] = new ThreeNozzleTrayVisionRecord
                        { Tray = current, Compensation = compensation };
                    if (tray.IsOkTray) Vs_TrayOK = compensation;
                    else if (tray.IsNgTray) Vs_TrayNg = compensation;
                    return EM_RES.OK;
                }
                if (quit || result == EM_RES.QUIT) return EM_RES.QUIT;
                result = ThreeNozzleTrayVisionWarning(tray);
                if (result != EM_RES.RETRY) return result;
            }
            return EM_RES.ERR;
        }

        private EM_RES ThreeNozzleTrayVisionWarning(TrayBox tray)
        {
            VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, VAR.IsChinese ? "拍照错误!" : "Cam ERR", 20, true);
            MT.ST_WARN warn = new MT.ST_WARN();
            warning form = new warning();
            warn.ok_txt = MultiLanguage.TxtSelct("更换料盘", "Replace tray", "Thay khay");
            warn.cancle_txt = MultiLanguage.TxtSelct("重新拍照", "Take a photo", "Chụp ảnh");
            warn.abort_txt = MultiLanguage.TxtSelct("停止运行", "Stop running", "Ngừng chạy");
            warn.ws = null;
            warn.title = MultiLanguage.TxtSelct("提示:拍照错误!", "Tip: Failed to take a picture!", "Mẹo: Không chụp được ảnh!");
            string trayName = tray.IsOkTray ? "OK料盘" : "NG料盘";
            warn.msg = upcam.disc + "拍" + trayName + "失败!";
            warn.lb_msg = warn.msg + "\r\n1.料盘放反或拍照位有异物，请更换料盘。\r\n2.需重拍确认，请按重新拍照。\r\n3.需停止，请按停止运行。";
            DialogResult answer = MT.Display_frwarn(form, warn, ERR_ALM.EmErrItem.CaptureAbnomal);
            if (answer == DialogResult.Abort) return EM_RES.ERR;
            VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, VAR.IsChinese ? "运行" : "RUN", 0, true);
            return answer == DialogResult.OK ? EM_RES.END : EM_RES.RETRY;
        }
    }
}
