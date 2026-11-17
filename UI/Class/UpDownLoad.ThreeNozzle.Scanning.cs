using System;
using System.Windows.Forms;
using System.Collections.Generic;
using MotionCtrl;
using UI.Class;
using static UI.Cam;

namespace UI
{
    public partial class UpDownLoad
    {
        private readonly Dictionary<Product.MdDat, string> threeNozzleDownMotorCodes = new Dictionary<Product.MdDat, string>();
        private readonly HashSet<Product.MdDat> threeNozzleMotorScanReady = new HashSet<Product.MdDat>();

        // Run before creating a plan/picking material. Enabled features validate
        // their own commissioned geometry and visual tasks before any motion.
        private EM_RES ThreeNozzleValidateScanningConfiguration()
        {
            if (PT_SET.bmotorphoto)
            {
                var ini = new MotionCtrl.IniFile(ThreeNozzleHardwareConfig.ConfigurationPath);
                string section = "Arm" + (id + 1);
                foreach (string key in new[] { "MotorScanY3", "MotorScanZ3", "MotorScanAngle3" })
                {
                    double value = ini.ReadDouble(section, key, double.NaN);
                    if (double.IsNaN(value) || double.IsInfinity(value))
                        return ThreeNozzleError(section + "." + key + " 马达扫码位置未标定");
                }
            }
            if (PT_SET.bDwAddCapQrcode || PT_SET.bdownqr || PT_SET.OvenCheck)
            {
                if (CONST.DwFindQrCodeFw.Length < list_xt.Count)
                    return ThreeNozzleError("缺少三吸嘴下相机附加扫码任务名称");
                for (int nozzle = 0; nozzle < list_xt.Count; nozzle++)
                {
                    string name = CONST.DwFindQrCodeFw[nozzle];
                    bool found = false;
                    foreach (Cam.VisionTask task in list_xt[nozzle].dwcam.List_vs_task)
                        if (task.TaskName == name) { found = true; break; }
                    if (!found) return ThreeNozzleError(list_xt[nozzle].disc + " 缺少下相机扫码任务: " + name);
                }
            }
            return EM_RES.OK;
        }
        // A successful return certifies this physical nozzle's feature and code
        // data. The caller retains it with the exact prepared material identity.
        /// <param name="motorOnly">true：只做电机扫码，下相机拍照推迟到进夹具时飞拍（三吸嘴飞拍，见 ThreeNozzleResolveDeferredDown）。</param>
        private EM_RES ThreeNozzlePrepareScanning(ref bool bquit, XT xt, ref VisionOutPutData downVision, bool motorOnly = false)
        {
            if (bquit) return EM_RES.QUIT;
            int nozzle = list_xt.IndexOf(xt);
            if (nozzle < 0 || xt.XtMd == null) return EM_RES.PARA_OUTOFRANG;
            EM_RES res;
            bool moduleCheckSample = !string.IsNullOrEmpty(xt.XtMd.ModuleCheckSampleId);   // 点检样品不扫码
            if (PT_SET.bmotorphoto && !moduleCheckSample && !threeNozzleMotorScanReady.Contains(xt.XtMd))
            {
                res = ThreeNozzleMotorScan(ref bquit, xt, nozzle);
                if (res != EM_RES.OK) return res;
                res = ThreeNozzleCheckMotorRate(xt);
                if (res != EM_RES.OK) return res;
                threeNozzleMotorScanReady.Add(xt.XtMd);
            }
            if (motorOnly) return bquit ? EM_RES.QUIT : EM_RES.OK;

            res = DwCamAction(ref bquit, xt, CONST.ModDwFw[nozzle], ref downVision);
            if (res != EM_RES.OK) return bquit ? EM_RES.QUIT : res;
            VisionOutPutData qr = downVision;
            // The legacy fixed-shot path also takes a separate code exposure
            // when probe checking selects that path (OvenCheck).
            if ((PT_SET.bDwAddCapQrcode || PT_SET.bdownqr || PT_SET.OvenCheck) && !moduleCheckSample)
            {
                qr = new VisionOutPutData();
                res = DwCamAction(ref bquit, xt, CONST.DwFindQrCodeFw[nozzle], ref qr);
                if (res != EM_RES.OK) return bquit ? EM_RES.QUIT : res;
            }
            if (PT_SET.BarcodeMode == (int)PT_SET.BAR_SCAN.DW_SCAN)
                xt.XtMd.bardcode = qr.BarCode;
            if (PT_SET.bdownqr)
            {
                threeNozzleDownMotorCodes[xt.XtMd] = qr.BarCode;
                if (!PT_SET.bmotorphoto) xt.XtMd.motor_barcode = qr.BarCode;
            }
            return bquit ? EM_RES.QUIT : EM_RES.OK;
        }

        /// <summary>
        /// 飞拍拿到的下相机结果按定拍同样的规则写回物料（只在不需要单独下相机扫码时推迟下相机，
        /// 所以二维码取自 ModDw 结果本身，与 ThreeNozzlePrepareScanning 中 qr = downVision 的情况一致）。
        /// </summary>
        private void ThreeNozzleApplyDeferredDownVision(XT xt, VisionOutPutData downVision)
        {
            if (xt == null || xt.XtMd == null || downVision == null) return;
            if (PT_SET.BarcodeMode == (int)PT_SET.BAR_SCAN.DW_SCAN)
                xt.XtMd.bardcode = downVision.BarCode;
        }

        // Invoke only after physical placement succeeds, before clearing XtMd.
        private void ThreeNozzleBindScanning(XT xt, WS.MdDat target)
        {
            Product.MdDat material = xt.XtMd;
            if (material == null) throw new InvalidOperationException("扫码绑定时吸嘴物料为空");
            target.bardcode = material.bardcode;
            string downMotor;
            if (PT_SET.bdownqr && threeNozzleDownMotorCodes.TryGetValue(material, out downMotor))
                target.motor_barcode = downMotor;
            bool motorValid = true;
            // Fixed-shot placement always rejects invalid side codes. The old
            // fly-shot path does so only when the scanner alarm is enabled.
            if (PT_SET.bmotorphoto && !PT_SET.bsunnyqr &&
                (PT_SET.bDwAddCapQrcode || PT_SET.OvenCheck || PT_SET.bsunnyqralm))
            {
                string code = material.motor_barcode;
                if (string.IsNullOrEmpty(code) || code.Length < 5 ||
                    (NewSysInf.UserParams.bCheckMotoCodeLength && code.Length != PT_SET.motorBarcodeDigits))
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, xt.disc + "放工站的马达二维码异常，强制下料！");
                    target.res = 1;
                    motorValid = false;
                }
            }
            if (PT_SET.bmotorphoto && motorValid) target.motor_barcode = material.motor_barcode;
            threeNozzleDownMotorCodes.Remove(material);
            threeNozzleMotorScanReady.Remove(material);
        }

        private EM_RES ThreeNozzleMotorScan(ref bool bquit, XT xt, int nozzle)
        {
            double scanY, scanZ, scanAngle;
            if (nozzle < 2)
            {
                scanY = traybox_fd.motor_barcode_pos[xt.id];
                scanZ = id == 0 ? (nozzle == 0 ? PT_SET.MotorZ1 : PT_SET.MotorZ2)
                                 : (nozzle == 0 ? PT_SET.MotorZ3 : PT_SET.MotorZ4);
                scanAngle = id == 0 ? (nozzle == 0 ? PT_SET.MotorAngle1 : PT_SET.MotorAngle2)
                                     : (nozzle == 0 ? PT_SET.MotorAngle3 : PT_SET.MotorAngle4);
            }
            else
            {
                // N3 has no legacy motor geometry. Never borrow N2's calibration.
                var ini = new MotionCtrl.IniFile(ThreeNozzleHardwareConfig.ConfigurationPath);
                string section = "Arm" + (id + 1);
                scanY = ini.ReadDouble(section, "MotorScanY3", double.NaN);
                scanZ = ini.ReadDouble(section, "MotorScanZ3", double.NaN);
                scanAngle = ini.ReadDouble(section, "MotorScanAngle3", double.NaN);
            }
            if (double.IsNaN(scanY) || double.IsInfinity(scanY) ||
                double.IsNaN(scanZ) || double.IsInfinity(scanZ) ||
                double.IsNaN(scanAngle) || double.IsInfinity(scanAngle))
                return ThreeNozzleError(xt.disc + "马达扫码位置未标定：MotorScanY3/MotorScanZ3/MotorScanAngle3");

            EM_RES result = EM_RES.ERR;
            EM_RES restore = EM_RES.ERR;
            try { result = ThreeNozzleMotorScanCore(ref bquit, xt, scanY, scanZ, scanAngle); }
            finally
            {
                // Raise the selected independent Z before rotating its U. A
                // failed recovery must not be turned into successful scanning.
                bool cleanupQuit = false;
                restore = MT.Move(ref cleanupQuit, ref xt.ax_z, 0);
                if (restore == EM_RES.OK)
                    restore = MT.ZupMove(ref cleanupQuit, ref xt.ax_u, 0);
            }
            if (restore != EM_RES.OK) return restore;
            return bquit ? EM_RES.QUIT : result;
        }

        private EM_RES ThreeNozzleCheckMotorRate(XT xt)
        {
            string code = xt.XtMd.motor_barcode ?? "";
            if (code.Contains("ERROR")) PT_SET.qrngnum++;
            else PT_SET.qroknum++;
            int total = PT_SET.qrngnum + PT_SET.qroknum;
            double rate = 100.0 * PT_SET.qroknum / Math.Max(1, total);
            if (rate >= PT_SET.Motorrate) return EM_RES.OK;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, xt.disc + "当前侧边扫码良率低：" + rate);
            VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "当前侧边扫码良率低", 10, true);
            MT.ST_WARN warn = new MT.ST_WARN();
            warn.ok_txt = "继续运行";
            warn.cancle_txt = "停止运行";
            warn.title = "提示:扫码异常";
            warn.msg = "当前侧边扫码良率低!";
            warn.lb_msg = "点击继续运行清空统计；点击停止运行停止设备并进行确认。";
            DialogResult choice = MT.Display_frwarn(new warning(), warn, ERR_ALM.EmErrItem.MaterialPosErr);
            PT_SET.qrngnum = 0;
            PT_SET.qroknum = 0;
            if (choice != DialogResult.OK) return EM_RES.QUIT;
            VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行", 0, true);
            return EM_RES.OK;
        }
        // Called while the feed-tray ownership lock is held, before selecting material.
        private EM_RES ThreeNozzleCheckFeedTrayBarcode(ref bool bquit)
        {
            if (bquit) return EM_RES.QUIT;
            EM_RES ret = EM_RES.OK, res = EM_RES.OK;
            string str = "", barcodetemp = "";
            int trycnt = 0;
            bool search = false;
            if (traybox_fd.ischangetray && PT_SET.TrayBarcodeEn)
            {
                // Keep pending until the scan and operator decision succeed.
                ret = MT.Move(ref bquit, ref MT.AXIS_UDL_FD_X, id == COM.UDLoad1.id ? traybox_fd.tray_barcode_a1 : traybox_fd.tray_barcode_a2);
                if (ret != EM_RES.OK) return ret;
                ret = MT.Move(ref bquit, ref ax_x, id == COM.UDLoad1.id ? traybox_fd.tray_barcode_x1 : traybox_fd.tray_barcode_x2, ref ax_y, id == COM.UDLoad1.id ? traybox_fd.tray_barcode_y1 : traybox_fd.tray_barcode_y2);
                if (ret != EM_RES.OK) return ret;
                while (trycnt++ < 3)
                {
                    if (id == COM.UDLoad1.id)
                        ret = COM.CamUp1.FindTaskTriAndWait(CONST.TrayUpFw, Demo);
                    else
                        ret = COM.CamUp2.FindTaskTriAndWait(CONST.TrayUpFw, Demo);
                    if (ret != EM_RES.OK) continue;
                    Cam.VisionTask task = id == COM.UDLoad1.id ? COM.CamUp1.curTask : COM.CamUp2.curTask;
                    if (task.ResData.BarCode == "") continue;
                    barcodetemp = task.ResData.BarCode;
                    if (barcodetemp != barcodestr)
                    {

                        bool delete = database.DeleteIndex(barcodestr, out str);
                        if (!delete)
                        {
                            delete = database.DeleteIndex(barcodestr, out str);
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, str);
                        }
                        barcodestr = barcodetemp;
                    }
                    search = database.IsExitIndex(barcodestr, out str);
                    if (!search)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, str);
                        break;
                        ////界面提示                 
                        //VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "未震动", 10, true);
                        //MT.ST_WARN warn = new MT.ST_WARN();
                        //warning fr_warn = new warning();
                        //warn.ws = null;
                        //warn.ok_txt = "更换料盘";
                        //warn.cancle_txt = "停止运行";
                        //warn.abort_txt = "重新拍照";
                        //warn.title = "提示:供料盘未震动!";
                        //warn.msg = disc + "供料盘没有震动请确认后按确定继续!";
                        //warn.lb_msg = "提示:" + "供料盘没有震动请确认后按确定继续!";
                        //DialogResult logres = MT.Display_frwarn(fr_warn, warn, ERR_ALM.EmErrItem.Null);
                        //if (logres == DialogResult.OK)
                        //{
                        //    VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行", 0, true);
                        //    return EM_RES.END;
                        //}
                        //else if (logres == DialogResult.Cancel)
                        //{
                        //    res = EM_RES.ERR;
                        //    return res;
                        //}
                        //else if (logres == DialogResult.Abort)
                        //{
                        //    VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行", 0, true);
                        //    continue;
                        //}
                    }
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, "当前供料盘已进行过震动。");
                    traybox_fd.ischangetray = false;
                    break;
                }
                if (trycnt == 4)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, string.Format("连续3次拍不到料盘二维码"));
                    //界面提示                 
                    VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "未震动", 10, true);
                    MT.ST_WARN warn = new MT.ST_WARN();
                    warning fr_warn = new warning();//增加语言
                    warn.ws = null;
                    warn.ok_txt = MultiLanguage.TxtSelct("更换料盘", "Replace tray", "Thay khay");
                    warn.cancle_txt = MultiLanguage.TxtSelct("停止运行", "Stop running", "Ngừng chạy");
                    warn.title = MultiLanguage.TxtSelct("提示:供料盘未震动!", "Tip: Feed tray is not shaken!", "Tip: Feed tray is not shaken!");
                    warn.msg = MultiLanguage.TxtSelct(
                        disc + "供料盘没有震动请确认后按确定继续!",
                        englishdisc + "The feed tray is not vibrating, please confirm after pressing 'Replace tray' key to continue!",
                        disc + "Khay nạp không rung, vui lòng xác nhận sau khi nhấn phím 'Thay khay' để tiếp tục!");
                    warn.lb_msg = MultiLanguage.TxtSelct("" +
                        "提示:" + disc + "供料盘没有震动请确认后按确定继续!",
                        "Tip:" + englishdisc + "The feed tray is not vibrating, please confirm after pressing 'Replace tray' key to continue!",
                        "Mẹo:" + disc + "Khay nạp không rung, vui lòng xác nhận sau khi nhấn phím 'Thay khay' để tiếp tục!");
                    DialogResult logres = MT.Display_frwarn(fr_warn, warn, ERR_ALM.EmErrItem.Null);
                    if (logres == DialogResult.OK)
                    {
                        VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行", 0, true);
                        return EM_RES.END;
                    }
                    else if (logres == DialogResult.Cancel)
                    {
                        res = EM_RES.ERR;
                        return res;
                    }
                }

            }



            return EM_RES.OK;
        }
        // Scanner protocol and operator choices are retained from MotoScan. Only
        // its physical positioning is replaced with the selected nozzle's axes.
        private EM_RES ThreeNozzleMotorScanCore(ref bool bquit, XT xt, double scanY, double scanZ, double scanAngle)
        {
            string barcode = "";
            EM_RES res = EM_RES.OK;
            try
            {
                var com = id == 0 ? MT.COM3 : MT.COM4;
                if (!xt.bCanMotoScan)
                {
                    if (xt.XtMd == null)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, xt.disc + "不需要扫马达二维码吸头空");
                    }
                    else
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, xt.disc + "不需要扫马达二维码:" + xt.XtMd.motor_barcode);
                    }
                    return EM_RES.OK;
                }

                res = MT.ZupMove(ref bquit, ref ax_y, scanY, ref xt.ax_u, scanAngle);
                if (res == EM_RES.OK) res = MT.Move(ref bquit, ref xt.ax_z, scanZ);
                if (res != EM_RES.OK)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, xt.disc + "定位马达二维码失败");
                    return res;
                }
                if (PT_SET.bsunnyqr)
                {
                    REQRACTION:
                    string Code = "ERRORCODE";
                    if(id==0)
                    {
                        res = COM.Sunnnyqr0.QrAction(ref bquit, out Code);
                    }
                    else
                    {
                        res = COM.Sunnnyqr1.QrAction(ref bquit, out Code);
                    }
                    xt.XtMd.bhaveMotoScan = true;//已经马达扫码
                    if (res != EM_RES.OK)
                    {
                        if (PT_SET.bsunnyqralm)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + xt.disc + "马达二维码NG" + barcode);
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "扫码枪5秒没有返回结果!");
                            MT.ST_WARN warn = new MT.ST_WARN();
                            warning fr_warn = new warning();
                            warn.ok_txt = MultiLanguage.TxtSelct("重新扫码", "GoOn", "Đi tiếp");
                            warn.abort_txt = MultiLanguage.TxtSelct("继续运行", "GoOn", "Đi tiếp");
                            warn.cancle_txt = MultiLanguage.TxtSelct("停止运行", "Stop", "Ngừng lại");
                            warn.title = MultiLanguage.TxtSelct("提示:扫码异常", "Tip: Fly Err Back Place Tray Error", "Mẹo: Fly Err Back Place Tray Error");
                            VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, VAR.IsChinese ? "扫码异常" : "Place Tray Error", 10, true);
                            warn.msg = MultiLanguage.TxtSelct("侧边扫码异常!", "Place Tray Error", "Lỗi đặt khay");
                            warn.lb_msg = MultiLanguage.TxtSelct($"当前侧边扫码异常\r\n" +
                                "1、点击重新扫码，则重新进行扫码动作\r\n" +
                                "2、点击继续运行，则不在扫码，二维码默认ErrorCode\r\n" +
                                "3、点击停止运行，则不在扫码，设备停止\r\n");
                            DialogResult logres = MT.Display_frwarn(fr_warn, warn, ERR_ALM.EmErrItem.MaterialPosErr);
                            if (logres == DialogResult.OK)
                            {
                                VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, VAR.IsChinese ? "运行" : "RUN", 0, true);
                                goto REQRACTION;

                            }
                            else if (logres == DialogResult.Abort)
                            {
                                VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, VAR.IsChinese ? "运行" : "RUN", 0, true);
                                Code = "ERRORCODE";
                            }
                            else if (logres == DialogResult.Cancel)
                            {
                                return EM_RES.QUIT;
                            }
                        }

                        Code = "ERRORCODE";
                        xt.XtMd.motor_barcode = Code;
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + xt.disc + $"马达二维码失败{xt.XtMd.motor_barcode}");
                        return EM_RES.OK;
                    }
                    else
                    {
                        Code = Code.Trim();
                        xt.XtMd.motor_barcode = Code;
                        if ((Code.Length) != PT_SET.motorBarcodeDigits && NewSysInf.UserParams.bCheckMotoCodeLength)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, disc + xt.disc + $"马达二维码位数校验失败，扫码长度:{Code.Length},设置长度:{PT_SET.motorBarcodeDigits}");
                            return EM_RES.ERR;
                        }
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, disc + xt.disc + $"马达二维码成功{xt.XtMd.motor_barcode}");
                        return EM_RES.OK;
                    }
                }
                else if (PT_SET.bkeyenceqr)
                {
                    string Code = "ERRORCODE";
                REQKEYENCEACTION:
                    var keyence = id == 0 ? COM.Keyence : COM.Keyence2;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, $"{disc}{xt.disc}基恩士马达扫码开始,IP:{keyence.m_reader.IpAddress}");
                    string rawMsg = "";
                    try
                    {
                        rawMsg = keyence.m_reader.ExecCommand("LON");
                    }
                    catch (Exception ex)
                    {
                        xt.XtMd.bhaveMotoScan = true;
                        xt.XtMd.motor_barcode = Code;
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, $"{disc}{xt.disc}基恩士马达扫码ExecCommand异常:{ex}");
                        return EM_RES.OK;
                    }

                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, $"{disc}{xt.disc}基恩士马达扫码返回,Raw:[{rawMsg ?? "NULL"}]");
                    string msg = rawMsg == null ? null : rawMsg.Trim();
                    xt.XtMd.bhaveMotoScan = true;
                    if (string.IsNullOrWhiteSpace(msg))
                    {
                        if (PT_SET.bsunnyqralm)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + xt.disc + "马达二维码NG" + barcode);
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("扫码枪5秒没有返回有效结果! Raw:[{0}]", rawMsg ?? "NULL"));
                            MT.ST_WARN warn = new MT.ST_WARN();
                            warning fr_warn = new warning();
                            warn.ok_txt = MultiLanguage.TxtSelct("重新扫码", "GoOn", "Đi tiếp");
                            warn.abort_txt = MultiLanguage.TxtSelct("继续运行", "GoOn", "Đi tiếp");
                            warn.cancle_txt = MultiLanguage.TxtSelct("停止运行", "Stop", "Ngừng lại");
                            warn.title = MultiLanguage.TxtSelct("提示:扫码异常", "Tip: Fly Err Back Place Tray Error", "Mẹo: Fly Err Back Place Tray Error");
                            VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, VAR.IsChinese ? "扫码异常" : "Place Tray Error", 10, true);
                            warn.msg = MultiLanguage.TxtSelct("侧边扫码异常!", "Place Tray Error", "Lỗi đặt khay");
                            warn.lb_msg = MultiLanguage.TxtSelct($"当前侧边扫码异常\r\n" +
                                "1、点击重新扫码，则重新进行扫码动作\r\n" +
                                "2、点击继续运行，则不在扫码，二维码默认ErrorCode\r\n" +
                                "3、点击停止运行，则不在扫码，设备停止\r\n");
                            DialogResult logres = MT.Display_frwarn(fr_warn, warn, ERR_ALM.EmErrItem.MaterialPosErr);
                            if (logres == DialogResult.OK)
                            {
                                VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, VAR.IsChinese ? "运行" : "RUN", 0, true);
                                goto REQKEYENCEACTION;
                            }
                            else if (logres == DialogResult.Abort)
                            {
                                VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, VAR.IsChinese ? "运行" : "RUN", 0, true);
                                msg = Code;
                            }
                            else if (logres == DialogResult.Cancel)
                            {
                                return EM_RES.QUIT;
                            }
                        }
                        else
                        {
                            msg = Code;
                        }
                    }

                    xt.XtMd.motor_barcode = msg;
                    if ((msg.Length) != PT_SET.motorBarcodeDigits && NewSysInf.UserParams.bCheckMotoCodeLength)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, disc + xt.disc + $"基恩士马达二维码位数校验失败，二维码:{msg},扫码长度:{msg.Length},设置长度:{PT_SET.motorBarcodeDigits}");
                        return EM_RES.ERR;
                    }

                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, disc + xt.disc + $"基恩士马达二维码成功{xt.XtMd.motor_barcode}");
                    return EM_RES.OK;
                }
                else
                {
                    if (com.bComInitOK == false)
                    {
                        com.ReInit();
                        if (com.bComInitOK == false)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, xt.disc + "马达二维码串口初始化失败");
                            return EM_RES.ERR;
                        }
                    }
                    com.bCheckQrLength = NewSysInf.UserParams.bCheckMotoCodeLength;
                    bool bok = com.ReadDataByString(out barcode, 200, 3);
                    barcode = barcode.Trim();
                    xt.XtMd.bhaveMotoScan = true;//已经马达扫码
                    xt.XtMd.motor_barcode = barcode;
                    if (!bok)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + xt.disc + "马达二维码NG" + barcode);
                        if(PT_SET.bsunnyqralmtray)
                        {
                            return EM_RES.MOTOR_CAM_ERR_PLACE_BACK;
                        }
                        else
                        {
                            return EM_RES.OK;
                        }
                           
                    }
                    else
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, disc + xt.disc + $"马达二维码成功{barcode}");
                        return EM_RES.OK;
                    }
                }
                    
            }
            catch (Exception ee)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, xt.disc + "马达二维码异常" + ee.ToString());
                return EM_RES.ERR;
            }
            return res;
        }
    }
}
