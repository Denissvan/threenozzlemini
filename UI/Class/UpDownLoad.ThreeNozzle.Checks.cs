using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MotionCtrl;
using UI.Class;

namespace UI
{
    public partial class UpDownLoad
    {
        // Explicit nozzle/pocket adapter for the legacy PickWsMod pre-pick check.
        // Keep the legacy warnings, risk upload, 2222 disposition and recap choices.
        private EM_RES ThreeNozzleCheckBeforePick(ref bool bquit, WS ws, XT xt, WS.MdDat md, Cam.VisionOutPutData flyResult = null)
        {
            if (PT_SET.BarcodeMode != (int)PT_SET.BAR_SCAN.UP_SCAN || !PT_SET.bOkCheck)
                return EM_RES.OK;
            EM_RES res = EM_RES.OK;
            bool bAlm = false;
            string[] Errmsg = new string[2];
        RECAP:
            // 三吸嘴飞拍：进夹具那一趟已飞拍到 WsMod_Shp 时，第一次检查直接用飞拍结果；
            // 弹窗选“重新拍照”回到 RECAP 时按原来的定拍重新拍。
            Cam.VisionOutPutData flyData = flyResult;
            flyResult = null;
            if (flyData != null)
            {
                res = EM_RES.OK;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} {1} 穴{2} 取料前检查使用飞拍结果：{3}", disc, ws.disc, md.Num, flyData));
            }
            else
            {
                res = MT.ZupMove(ref bquit, ref ax_x, md.st_pos[id].x, ref ax_y, md.st_pos[id].y);
                //ax_y.SetToWorkSpd();
                if (res != EM_RES.OK) return res;
                //int i = 0;
                //while (!upcam.FlushOk)
                //{
                //    Thread.Sleep(5);
                //    if (i++ > 30)
                //    {
                //        VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, "等待upcam.FlushOk为True超时(0.15S)");
                //        break;
                //    }
                //}
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, "Pick_Move_End_Cap->md:" + md.Num.ToString());
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("UpDnPathTrace PickWsMod backcheck ud={0}, ws={1}, md={2}, x={3:F3}, y={4:F3}",
                    disc, ws.disc, md.Num, md.st_pos[id].x, md.st_pos[id].y));
                res = upcam.FindTaskTriAndWait(CONST.WsModUpFw, Demo);
                if (res != EM_RES.OK && res != EM_RES.CAM_ERR) return res;
                if (res == EM_RES.OK)
                {
                    XT backCheckXt = xt;
                    CaptureWsModShp2PhotoOnly(ref bquit, backCheckXt,
                        new ST_XY(md.st_pos[id].x, md.st_pos[id].y), false);
                }
            }
            Cam.VisionOutPutData data = flyData ?? (upcam.curTask == null ? null : upcam.curTask.ResData) ?? new Cam.VisionOutPutData();
            //下料前进行二维码防呆
            if (PT_SET.OpenDownQrde && PT_SET.BarcodeMode != (int)PT_SET.BAR_SCAN.NO_SCAN)
            {
                String str1, str2, str3, str4, str5;
                str2 = "当前工站位置：" + ws.num + "工位号："+md.Num+ "回检二维码：" + data.BarCode + "记录二维码：" + md.bardcode;
                Utility.WriteStrToCSV("", str2);
                bool bAlmin = false;
                if (PT_SET.UpDownQrde)
                {
                    if (data.BarCode != null && data.BarCode.Length > 1 && data.BarCode != md.bardcode && !Demo)
                    {
                        if (md != null && PT_SET.upload)
                        {

                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "下料前检测二维码未通过," + md.bardcode);
                            if (md.bardcode != null)
                            {
                                if (!md.bardcode.Contains("Err"))
                                {

                                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "下料前检测二维码未通过:，上传风险系统" + md.bardcode);
                                    TestPC.UploadRiskCode(md.bardcode, PT_SET.citynum);
                                }
                            }

                        }

                        if (data != null && PT_SET.upload)
                        {
                            if (data.BarCode != null)
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "下料前检测二维码未通过:，上传风险系统" + data.BarCode);
                                TestPC.UploadRiskCode(data.BarCode, PT_SET.citynum);
                            }
                        }

                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}_工位:{1}->回检二维码不一致,实拍Barcode:{2},记录Barcode:{3},把记录二维码结果改为2222", ws.disc, md.Num, data.BarCode, md.bardcode) : string.Format(" {0}-WS:{1},barcodes are inconsistent,new barcode:{2},old barcode:{3},change the result to 111.         ({0}_工位:{1}->回检二维码不一致,实拍Barcode:{2},记录Barcode:{3},把记录二维码结果改为111)", ws.disc, md.Num, data.BarCode, md.bardcode));
                        if (md.res == 0) md.res = 2222;
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "下料前检测二维码未通过:" + md.bardcode);
                
                        MT.ST_WARN st_warn = new MT.ST_WARN();
                        warning fr_warn = new warning();
                        st_warn.ok_txt = VAR.IsChinese ? "确定" : "Give up";
                        st_warn.abort_txt = "不用";
                        st_warn.ws = ws;
                        st_warn.cancle_txt = VAR.IsChinese ? "停止运行" : "Stop running";
                        st_warn.title = VAR.IsChinese ? "提示:下料前检测二维码未通过!" : "Tip: Abnormal suction!";
                        st_warn.msg = VAR.IsChinese ? string.Format("{0}_工位:{1}->回检二维码不一致,实拍Barcode:{2},记录Barcode:{3},把记录二维码结果改为2222", ws.disc, md.Num, data.BarCode, md.bardcode) : string.Format(" {0}-WS:{1},barcodes are inconsistent,new barcode:{2},old barcode:{3},change the result to 111.         ({0}_工位:{1}->回检二维码不一致,实拍Barcode:{2},记录Barcode:{3},把记录二维码结果改为111)", ws.disc, md.Num, data.BarCode, md.bardcode);
                        st_warn.lb_msg = "提示:" + st_warn.msg + "请确认!\r\n  1.点击确定将继续运行!\r\n  " +
                            "2.点击停止将停止运行!\r\n  ";
                        VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, VAR.IsChinese ? "检测二维码未通过!" : "No Barcode", 20, true);
                        EM_RES recoveryRes;
                        DialogResult logres = DisplayWarningWithDualArmRecoveryGate(
                            fr_warn, st_warn, ERR_ALM.EmErrItem.UpDownLoadAbnormal, "下料前二维码回检", false, out recoveryRes);
                        if (recoveryRes != EM_RES.OK) return recoveryRes;
                        if (DialogResult.Cancel == logres)
                        {
                            return EM_RES.ERR;
                        }
                    }
                    else
                    {
                        if (md!=null)
                        {
                            if(md.bardcode != string.Empty)
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, "下料前检测二维码通过:" + md.bardcode);
                            }
                           
                        }
                        
                    }
                }
            }
            bAlm = false;
            bool CannotUpload = true;
            bool barcodeUnrecognized = false;
            if (!data.bUpdate || !data.bOK || Math.Abs(data.PosMM.x) > PT_SET.Vs_XYofs
                 || Math.Abs(data.PosMM.y) > PT_SET.Vs_XYofs || Math.Abs(data.PosMM.a) > PT_SET.Vs_Rofs)
            {
                Errmsg[0] = VAR.IsChinese ? "模组被带偏" : "Open deflected";
                Errmsg[1] = VAR.IsChinese ? string.Format("{0}_工位{1}上模组被带偏.", ws.disc, md.Num) : string.Format("{0}_ws{1}product was deflected by fixture.", ws.disc, md.Num);
                CannotUpload=false;
                bAlm = true;
            }
            else if (PT_SET.BarcodeMode == (int)PT_SET.BAR_SCAN.UP_SCAN && (res == EM_RES.OK && PT_SET.bBarcodeCamBackEn && (data.BarCode == null || data.BarCode.Length < 1)) && !Demo)
            {
                Errmsg[0] = VAR.IsChinese ? "没检到二维码!" : "No Barcode";
                Errmsg[1] = VAR.IsChinese
                    ? string.Format("{0}_工位{1}上模组二维码无法识别.", ws.disc, md.Num)
                    : string.Format("{0}_ws{1}can't recognize barcode.", ws.disc, md.Num);
                barcodeUnrecognized = true;
                bAlm = true;
            }

            //if (res == EM_RES.OK && (data.BarCode == null || data.BarCode.Length < 1))
            if (bAlm)
            {
                if (md != null && PT_SET.upload && CannotUpload)
                {

                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "下料前检测二维码未通过," + md.bardcode);
                    if ( md.bardcode != null)
                    {
                        if (!md.bardcode.Contains("Err"))
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "下料前检测二维码未通过:，上传风险系统" + md.bardcode);
                            TestPC.UploadRiskCode(md.bardcode, PT_SET.citynum);
                        }
                    }
                    
                }
                List<int> NgSameResNum = new List<int>();
                bAlm = false;
                VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, Errmsg[0], 20, true);
                MT.ST_WARN st_warn1 = new MT.ST_WARN();//增加语言
                warning fr_warn1 = new warning();
                st_warn1.ok_txt = MultiLanguage.TxtSelct("继续运行", "Keep running", "Tiếp tục chạy");
                st_warn1.abort_txt = MultiLanguage.TxtSelct("重新拍照", "Take a photo", "Chụp ảnh");
                st_warn1.ws = null;
                NgSameResNum.Add(md.Num);
                st_warn1.cancle_txt = MultiLanguage.TxtSelct("停止运行", "Stop running", "Ngừng chạy");
                st_warn1.msg = Errmsg[1];
                st_warn1.title = MultiLanguage.TxtSelct("提示:" + Errmsg[0], "Tip:" + Errmsg[0], "Mẹo:" + Errmsg[0]);
                st_warn1.lb_msg = MultiLanguage.TxtSelct(
                    "提示:" + st_warn1.msg + "请确认!\r\n  " +
                    "1.按'继续运行'键则放弃当前检查继续运行!\r\n " +
                    "2.如需确认问题请按'停止运行'键退出运行，待界面左上角显示就绪后再按运行键!\r\n " +
                    "3.如按'重新拍照'键则重新获取当前位置图像!",

                    "Tip: " + st_warn1.msg + "\r\nPlease confirm!\r\n  " +
                    "1.Press the 'Keep running' button to abandon the current check and continue running!\r\n  " +
                    "2.If you need to confirm the problem, press the 'Stop running' button to exit the operation. After the ready is displayed in the upper left corner of the interface, press the Run button!\r\n" +
                    "3.If you press the 'Take a photo' button, you will get the current position image again!",

                    "Mẹo:" + st_warn1.msg + "\r\nVui lòng xác nhận!\r\n  " +
                    "1.Nhấn nút 'Tiếp tục chạy' để bỏ kiểm tra hiện tại và tiếp tục chạy!\r\n " +
                    "2.Nếu bạn cần xác nhận sự cố, hãy nhấn nút 'Dừng chạy' để thoát khỏi hoạt động. Sau khi sự sẵn sàng hiển thị ở góc trên bên trái giao diện, hãy nhấn nút Run! \r\n" +
                    "3.Nếu bạn nhấn nút 'Chụp ảnh', bạn sẽ nhận lại được hình ảnh vị trí hiện tại!");
                EM_RES recoveryRes;
                DialogResult logres1 = DisplayWarningWithDualArmRecoveryGate(
                    fr_warn1, st_warn1, ERR_ALM.EmErrItem.UpDownLoadAbnormal, "上相机回检", true, out recoveryRes);
                if (recoveryRes != EM_RES.OK) return recoveryRes;
                if (barcodeUnrecognized)
                {
                    LogQrCodeDialogSelection(
                        st_warn1,
                        logres1,
                        "上相机",
                        string.Format("{0}_工位{1}", ws.disc, md.Num));
                }
                if (DialogResult.Cancel == logres1)
                {
                    if (barcodeUnrecognized)
                    {
                        return StopBackCheckAndReturnHome(ref bquit, null, "上相机二维码无法识别");
                    }

                    return StopBackCheckAndReturnHome(ref bquit, null, "上相机回检异常");
                }
                VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, VAR.IsChinese ? "运行" : "RUN", 0, true);
                if (DialogResult.Abort == logres1)
                {
                    goto RECAP;
                }
            } 
            return EM_RES.OK;
        }

        // Cache by physical material identity. A failed/aborted check is never
        // cached. Coordinator clears this on unload or explicit reconciliation.
        private readonly HashSet<Product.MdDat> threeNozzleFinishedBarcodeChecks = new HashSet<Product.MdDat>();
        private readonly Dictionary<Product.MdDat, string> threeNozzlePendingBarcodeWarnings = new Dictionary<Product.MdDat, string>();

        // Run before LeaveFixture, while camera motion is still permitted. The
        // tail unload can repeat this call; successful identities do not move.
        private EM_RES ThreeNozzleCheckFinishedDownBarcode(ref bool bquit, WS ws, XT xt)
        {
            string pendingCode;
            if (xt.XtMd != null && threeNozzlePendingBarcodeWarnings.TryGetValue(xt.XtMd, out pendingCode))
            {
                EM_RES pendingResult = ThreeNozzleDownBarcodeWarning(ref bquit, ws, xt, false, pendingCode);
                if (pendingResult == EM_RES.OK)
                {
                    threeNozzlePendingBarcodeWarnings.Remove(xt.XtMd);
                    threeNozzleFinishedBarcodeChecks.Add(xt.XtMd);
                }
                return pendingResult;
            }
            if (Demo || PT_SET.BarcodeMode != (int)PT_SET.BAR_SCAN.DW_SCAN ||
                !PT_SET.bBarcodeCamBackEn || xt.XtMd == null || xt.XtMd.res != 0)
                return EM_RES.OK;
            if (threeNozzleFinishedBarcodeChecks.Contains(xt.XtMd)) return EM_RES.OK;
            int nozzleIndex = list_xt.IndexOf(xt);
            if (nozzleIndex < 0 || nozzleIndex >= CONST.ModDwFw.Length) return EM_RES.ERR;
            Cam.VisionOutPutData data = new Cam.VisionOutPutData();
            EM_RES res = xt.DwCam(ref bquit, CONST.ModDwFw[nozzleIndex], ref data, Demo);
            if (res != EM_RES.OK && res != EM_RES.CAM_ERR) return res;
            if (res == EM_RES.OK && !string.IsNullOrEmpty(data.BarCode) && data.BarCode == xt.XtMd.bardcode)
            {
                threeNozzleFinishedBarcodeChecks.Add(xt.XtMd);
                return EM_RES.OK;
            }
            // Legacy FlyCamToTray performs a fixed-position recap after a bad
            // first frame. Neither frame overwrites the recorded material code.
            while (!bquit)
            {
                data = new Cam.VisionOutPutData();
                res = xt.DwCam(ref bquit, CONST.ModDwFw[nozzleIndex], ref data, Demo);
                if (res != EM_RES.OK && res != EM_RES.CAM_ERR) return res;
                if (res == EM_RES.CAM_ERR || string.IsNullOrEmpty(data.BarCode))
                {
                    res = ThreeNozzleDownBarcodeWarning(ref bquit, ws, xt, true, data.BarCode);
                    if (res == EM_RES.RETRY) continue;
                    return res;
                }
                if (PT_SET.OpenDownQrde && PT_SET.DownDownQrde &&
                    data.BarCode.Length > 1 && data.BarCode != xt.XtMd.bardcode)
                {
                    ThreeNozzleUploadDownBarcodeRisk(xt, data.BarCode);
                    xt.XtMd.res = 2222;
                    threeNozzlePendingBarcodeWarnings[xt.XtMd] = data.BarCode;
                    res = ThreeNozzleDownBarcodeWarning(ref bquit, ws, xt, false, data.BarCode);
                    if (res != EM_RES.OK) return res;
                    threeNozzlePendingBarcodeWarnings.Remove(xt.XtMd);
                }
                threeNozzleFinishedBarcodeChecks.Add(xt.XtMd);
                return EM_RES.OK;
            }
            return EM_RES.QUIT;
        }

        private void ThreeNozzleUploadDownBarcodeRisk(XT xt, string actual)
        {
            if (!PT_SET.upload) return;
            string recorded = xt.XtMd.bardcode;
            if (recorded != null && !recorded.Contains("Err"))
            {
                TestPC.UploadRiskCode(recorded, PT_SET.citynum);
                TestPC.UploadRiskCode(actual, PT_SET.citynum);
            }
            if (!actual.Contains("Err")) TestPC.UploadRiskCode(actual, PT_SET.citynum);
        }

        private EM_RES ThreeNozzleDownBarcodeWarning(ref bool bquit, WS ws, XT xt, bool missing, string actual)
        {
            MT.ST_WARN warn = new MT.ST_WARN();
            warning form = new warning();
            warn.ws = missing ? null : ws;
            warn.ok_txt = missing ? (PT_SET.bBackerrcontinue ? "继续运行" : "重新拍照") : "确定";
            if (missing && PT_SET.bBackerrcontinue) warn.abort_txt = "重新拍照";
            warn.cancle_txt = "停止运行";
            warn.title = missing ? "提示:没检到二维码" : "提示:下料前检测二维码未通过!";
            warn.msg = missing ? "下相机" + xt.disc + "上模组二维码无法识别。" :
                string.Format("下相机->{0}回检二维码不一致,实拍Barcode:{1},记录Barcode:{2},记录二维码结果0改为2222",
                    xt.disc, actual, xt.XtMd.bardcode);
            warn.lb_msg = warn.msg + (missing ? "\r\n请重新拍照，或停止运行后确认。" : "\r\n点击确定按NG继续下料；点击停止运行则停止。");
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, warn.msg);
            VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, warn.title, 20, true);
            EM_RES recovery;
            DialogResult answer = DisplayWarningWithDualArmRecoveryGate(form, warn,
                missing ? ERR_ALM.EmErrItem.BarcodeAbnormal : ERR_ALM.EmErrItem.UpDownLoadAbnormal,
                "下相机成品二维码回检", false, out recovery);
            if (recovery != EM_RES.OK) return recovery;
            LogQrCodeDialogSelection(warn, answer, "下相机", xt.disc);
            if (answer == DialogResult.Cancel)
                return StopBackCheckAndReturnHome(ref bquit, null, "下相机成品二维码回检");
            VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, VAR.IsChinese ? "运行" : "RUN", 0, true);
            return missing ? EM_RES.RETRY : EM_RES.OK;
        }
        // Validate a placed part while the fixture is still reserved (fixed capture or departure flight).
        // RETRY is a routing result: the caller retrieves the marked part with the
        // placing nozzle, unloads it as NG and loads a replacement after releasing any old held part.
        private EM_RES ThreeNozzleCheckAfterPlace(ref bool bquit, WS ws, XT xt, WS.MdDat md, Cam.VisionOutPutData flyResult = null)
        {
            bool barcodeCheck = PT_SET.Isaloneset ||
                (PT_SET.BarcodeMode == (int)PT_SET.BAR_SCAN.UP_SCAN &&
                 !NewSysInf.UserParams.bUpWsChkQrCodeOff);
            if (Demo || (!barcodeCheck && !PT_SET.bEnVsFB)) return EM_RES.OK;
            if (bquit) return EM_RES.QUIT;
            EM_RES res = EM_RES.OK;
            Cam.VisionTask positionTask;
            if (flyResult != null && flyResult.bUpdate && flyResult.bOK)
            {
                positionTask = new Cam.VisionTask { TaskName = CONST.WsModUpFw, ResData = flyResult.Clone() };
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 放料后回检：穴{1} 使用退离飞拍结果（{2}）", disc, md.Num, CONST.WsModUpFw));
            }
            else
            {
                res = MT.ZupMove(ref bquit, ref ax_x, md.st_pos[id].x, ref ax_y, md.st_pos[id].y);
                if (res != EM_RES.OK) return res;
                res = upcam.FindTaskTriAndWait(CONST.WsModUpFw, Demo);
                if (res != EM_RES.OK && res != EM_RES.CAM_ERR) return res;
                positionTask = upcam.curTask;
            }
            Cam.VisionTask barcodeTask = positionTask;
            if (barcodeCheck && PT_SET.bAddCapQrcode && PT_SET.bBarcodeCamBackEn)
            {
                res = MT.ZupMove(ref bquit, ref ax_x, md.st_pos[id].x,
                    ref ax_y, md.st_CapQrcodePos[id].y);
                if (res != EM_RES.OK) return res;
                res = upcam.FindTaskTriAndWait(CONST.WsModQrcode_Shp, Demo);
                if (res != EM_RES.OK && res != EM_RES.CAM_ERR) return res;
                barcodeTask = upcam.curTask;
            }
            if (barcodeCheck)
            {
                // QrCodeChkShow owns Isaloneset, bGetOrcodOnWs, ErrorCode and 2222.
                // Pass the live pocket record, never a clone or a nozzle index.
                res = QrCodeChkShow(barcodeTask, md);
                if (res != EM_RES.OK)
                {
                    if (res == EM_RES.RETRY) Ishave3342err = true;
                    return res;
                }
            }
            if (PT_SET.bEnVsFB)
                return CheckWsPutOkShow(ref bquit, positionTask,
                    new ST_XYN(md.st_pos[id].x, md.st_pos[id].y, md.Num - 1));
            return EM_RES.OK;
        }
    }
}



