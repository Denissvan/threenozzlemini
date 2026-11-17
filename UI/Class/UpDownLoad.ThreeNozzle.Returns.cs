using System;
using System.Threading;
using System.Windows.Forms;
using MotionCtrl;
using UI.Class;

namespace UI
{
    public partial class UpDownLoad
    {
        // The caller owns picktray and the prepared-slot identity. Only this
        // nozzle may move material; adjacent mouths can hold finished/new parts.
        private EM_RES ThreeNozzleReturnPrepared(ref bool quit, XT xt,
            Product.Tray sourceTray, Product.Tray.PosInf source,
            ST_XYZA pickedPosition, double pickedTrayX, bool rejected = true)
        {
            if (quit) return EM_RES.QUIT;
            if (xt == null || !list_xt.Contains(xt) || xt.XtMd == null)
                return ThreeNozzleError("退回待测料时吸嘴/物料身份无效", true);
            Product.MdDat material = xt.XtMd;
            bool sourceAvailable = Object.ReferenceEquals(traybox_fd.tray_cur, sourceTray) &&
                sourceTray != null && source != null && sourceTray.list_pos.Contains(source) &&
                (source.md == null || source.md.res == (int)Product.EM_CM_RES.EMPTY);
            if (!sourceAvailable || (!Demo && !xt.cy_zk.isONByChkSen))
            {
                EM_RES home = GoZero(ref quit);
                if (home != EM_RES.OK) return home;
                // Preserve the original continue/stop choice, and require the
                // nozzle vacuum to confirm removal before forgetting identity.
                while (!quit)
                {
                    VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "退回料盘异常", 10, true);
                    MT.ST_WARN warn = new MT.ST_WARN();
                    warn.ok_txt = MultiLanguage.TxtSelct("继续", "GoOn", "Đi tiếp");
                    warn.cancle_txt = MultiLanguage.TxtSelct("停止", "Stop", "Ngừng lại");
                    warn.title = "提示:退回料盘异常";
                    warn.msg = xt.disc + "原料盘/源穴或真空状态已变化";
                    warn.lb_msg = warn.msg + "，请人工取走此吸嘴物料并核对原料盘，按继续确认；按停止则停机。";
                    DialogResult choice = MT.Display_frwarn(new warning(), warn, ERR_ALM.EmErrItem.MaterialPosErr);
                    if (choice != DialogResult.OK) return EM_RES.ERR;
                    if (xt.cy_zk.isONByChkSen) continue;
                    if (!Object.ReferenceEquals(xt.XtMd, material))
                        return ThreeNozzleError("人工移除期间物料身份变化", true);
                    ThreeNozzleForgetPreparedScan(material);
                    xt.XtMd = null;
                    VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行", 0, true);
                    return EM_RES.OK;
                }
                return EM_RES.QUIT;
            }

            int started = Environment.TickCount;
            while (traybox_fd.runin || !traybox_fd.ax_x.isStop)
            {
                if (quit) return EM_RES.QUIT;
                if (unchecked(Environment.TickCount - started) > 5000)
                    return ThreeNozzleError("等待供料盘停止超过5秒，保留待退回物料");
                Thread.Sleep(10);
            }
            // Preserve XtMovPickPos geometry without changing the recorded pick
            // position or swallowing a failed move as the old helper does.
            double armX = ax_x.fenc_pos;
            if (Math.Abs(armX) < 50) armX = id == 0 ? 50 : -50;
            double trayX = -pickedPosition.x + armX + pickedTrayX;
            ST_XYZA target = pickedPosition.clone();
            if (trayX < 0) { target.x = armX - trayX; trayX = 0; }
            else target.x = armX;
            EM_RES move = MT.ZupMove(ref quit, ref ax_x, target.x, ref ax_y, target.y,
                ref traybox_fd.ax_x, trayX);
            if (move != EM_RES.OK) return move;
            if (!Object.ReferenceEquals(traybox_fd.tray_cur, sourceTray) ||
                !Object.ReferenceEquals(xt.XtMd, material) ||
                (source.md != null && source.md.res != (int)Product.EM_CM_RES.EMPTY))
                return ThreeNozzleError("定位退料过程中源盘/源穴/吸嘴身份变化", true);
            move = xt.PickOrPlace(ref quit, target, false, Demo);
            if (move != EM_RES.OK)
                return ThreeNozzleError("源穴退料动作结果待核对: " + move, true);
            // Keep the returned physical part represented in the tray. Rejected
            // parts are excluded from GetPosList(UNTEST), so they cannot loop.
            if (rejected) material.res = (int)Product.EM_CM_RES.CAMERR;
            source.md = material;
            ThreeNozzleForgetPreparedScan(material);
            xt.XtMd = null;
            return EM_RES.OK;
        }

        private void ThreeNozzleForgetPreparedScan(Product.MdDat material)
        {
            threeNozzleDownMotorCodes.Remove(material);
            threeNozzleMotorScanReady.Remove(material);
        }
    }
}
