using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevReport;
using MotionCtrl;

namespace UI
{
    public static class Turntable
    {

        static GPIO io_in_move = MT.GPIO_IN_TT_MOVE;
        static GPIO io_in_inp = MT.GPIO_IN_TT_INP;
        static GPIO io_in_alm = MT.GPIO_IN_TT_ALM;
        static GPIO io_out_fwd = MT.GPIO_OUT_TT_FWD;
        static GPIO io_out_rev = MT.GPIO_OUT_TT_REV;
        static GPIO io_out_reset = MT.GPIO_OUT_TT_RESET;
        static GPIO io_out_stop = MT.GPIO_OUT_TT_STOP;

        static GPIO io_in_sen0 = MT.GPIO_IN_TT_SEN0;
        static GPIO io_in_sen90 = MT.GPIO_IN_TT_SEN90;
        static GPIO io_in_sen270 = MT.GPIO_IN_TT_SEN270;

        #region 状态
        /// <summary>
        /// POS0~POS3对应 0~3，不能修改
        /// </summary>
        public enum EM_STA
        {
            [Description("POS[0]↓")]
            POS0,
            [Description("POS[1]←")]
            POS1,
            [Description("POS[2]↑")]
            POS2,
            [Description("POS[3]→")]
            POS3,
            [Description("MOVING")]
            MOVING,
            [Description("ERR")]
            ERR,
            [Description("ALM")]
            ALM,
            [Description("UNKNOW")]
            UNKNOW
        }
        public static EM_STA status = EM_STA.UNKNOW;
        #endregion

        public static int n = 0;
        public static double ct = 0;
        public static int tmr = 0;

        private static volatile bool motionCommandActive;
        public static bool NeedRecovery { get; private set; }

        public static EM_RES Home()
        {
            return RecoverPosition("Home");
        }

        private static EM_STA ReadPhysicalPosition()
        {
            if (io_in_alm != null && io_in_alm.isON) return EM_STA.ALM;
            if (io_in_move != null && io_in_move.isON) return EM_STA.MOVING;
            if (io_in_inp != null && io_in_inp.AssertOFF(3, 20)) return EM_STA.UNKNOW;

            bool bsen0 = io_in_sen0 != null && io_in_sen0.AssertON(3, 20);
            bool bsen90 = io_in_sen90 != null && io_in_sen90.AssertON(3, 20);
            bool bsen270 = io_in_sen270 != null && io_in_sen270.AssertOFF(3, 20);

            if (bsen0 && bsen90 && !bsen270) return EM_STA.POS0;
            if (!bsen0 && bsen90 && !bsen270) return EM_STA.POS1;
            if (!bsen0 && !bsen90 && bsen270) return EM_STA.POS2;
            if (bsen0 && !bsen90 && bsen270) return EM_STA.POS3;
            return EM_STA.UNKNOW;
        }

        private static void GuardIdleDirectionOutputs()
        {
            if (motionCommandActive || !MT.CARD_ECI2400_0.isReady || io_out_fwd == null || io_out_rev == null) return;
            bool fwdOn = io_out_fwd.isON;
            bool revOn = io_out_rev.isON;
            if (!fwdOn && !revOn) return;

            EM_RES fwdRes = io_out_fwd.SetOff();
            EM_RES revRes = io_out_rev.SetOff();
            NeedRecovery = true;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                "TurntableUnexpectedOutput FwdOn={0} RevOn={1} FwdOff={2} RevOff={3} CardReady={4}",
                fwdOn, revOn, fwdRes, revRes, MT.CARD_ECI2400_0.isReady));
        }

        public static EM_RES RecoverPosition(string trigger)
        {
            if (io_out_fwd == null || io_out_rev == null || io_out_stop == null || io_out_reset == null)
            {
                NeedRecovery = true;
                return EM_RES.PARA_ERR;
            }

            if (!MT.CARD_ECI2400_0.isReady)
            {
                NeedRecovery = true;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("TurntableRecover Trigger={0} Reason=CardOffline", trigger));
                return EM_RES.ERR;
            }

            EM_RES refreshRes = MT.CARD_ECI2400_0.UpdateIOBuff(true);
            if (refreshRes != EM_RES.OK)
            {
                NeedRecovery = true;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("TurntableRecover Trigger={0} Reason=IORefreshFailed Res={1}", trigger, refreshRes));
                return refreshRes;
            }

            EM_RES fwdRes = io_out_fwd.SetOff();
            EM_RES revRes = io_out_rev.SetOff();
            EM_RES resetOffRes = io_out_reset.SetOff();
            if (fwdRes != EM_RES.OK || revRes != EM_RES.OK || resetOffRes != EM_RES.OK)
            {
                NeedRecovery = true;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                    "TurntableRecover Trigger={0} Reason=ClearOutputFailed Fwd={1} Rev={2} Reset={3}",
                    trigger, fwdRes, revRes, resetOffRes));
                return EM_RES.ERR;
            }

            bool stopOn = io_out_stop.isON;
            bool alarmOn = io_in_alm != null && io_in_alm.isON;
            if (stopOn || alarmOn)
            {
                EM_RES stopOffRes = io_out_stop.SetOff();
                if (stopOffRes != EM_RES.OK)
                {
                    NeedRecovery = true;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("TurntableRecover Trigger={0} Reason=StopReleaseFailed Res={1}", trigger, stopOffRes));
                    return stopOffRes;
                }
                Thread.Sleep(50);
                EM_RES resetOnRes = io_out_reset.SetOn();
                Thread.Sleep(100);
                EM_RES resetOffResult = io_out_reset.SetOff();
                if (resetOnRes != EM_RES.OK || resetOffResult != EM_RES.OK)
                {
                    NeedRecovery = true;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                        "TurntableRecover Trigger={0} Reason=ResetFailed On={1} Off={2}", trigger, resetOnRes, resetOffResult));
                    return EM_RES.ERR;
                }
                Thread.Sleep(500);
            }

            refreshRes = MT.CARD_ECI2400_0.UpdateIOBuff(true);
            EM_STA sta = refreshRes == EM_RES.OK ? ReadPhysicalPosition() : EM_STA.UNKNOW;
            bool valid = sta >= EM_STA.POS0 && sta <= EM_STA.POS3;
            if (valid) mCurPos = sta;
            NeedRecovery = !valid;
            VAR.msg.AddMsg(valid ? Msg.EM_MSGTYPE.NOR : Msg.EM_MSGTYPE.ERR, string.Format(
                "TurntableRecover Trigger={0} StopWasOn={1} AlarmWasOn={2} Position={3} CardReady={4} Res={5}",
                trigger, stopOn, alarmOn, sta, MT.CARD_ECI2400_0.isReady, valid ? EM_RES.OK : EM_RES.ERR));
            return valid ? EM_RES.OK : EM_RES.ERR;
        }

        public static EM_RES PauseSafely()
        {
            if (io_out_fwd == null || io_out_rev == null || io_out_stop == null || io_out_reset == null)
            {
                NeedRecovery = true;
                return EM_RES.PARA_ERR;
            }

            if (!MT.CARD_ECI2400_0.isReady || MT.CARD_ECI2400_0.UpdateIOBuff(true) != EM_RES.OK)
            {
                NeedRecovery = true;
                return ForceSafeStop("PauseIOUnavailable");
            }

            EM_STA sta = ReadPhysicalPosition();
            if (sta == EM_STA.MOVING || sta == EM_STA.ALM || sta == EM_STA.UNKNOW)
            {
                NeedRecovery = true;
                return ForceSafeStop("PauseUnsafeState:" + sta);
            }

            EM_RES fwdRes = io_out_fwd.SetOff();
            EM_RES revRes = io_out_rev.SetOff();
            EM_RES resetRes = io_out_reset.SetOff();
            EM_RES result = fwdRes == EM_RES.OK && revRes == EM_RES.OK && resetRes == EM_RES.OK ? EM_RES.OK : EM_RES.ERR;
            VAR.msg.AddMsg(result == EM_RES.OK ? Msg.EM_MSGTYPE.NOR : Msg.EM_MSGTYPE.ERR, string.Format(
                "TurntablePauseHold Position={0} FwdOff={1} RevOff={2} ResetOff={3} StopUnchanged={4} Res={5}",
                sta, fwdRes, revRes, resetRes, io_out_stop.Status, result));
            return result;
        }
        public static EM_RES ForceSafeStop(string trigger)
        {
            NeedRecovery = true;
            EM_RES darkCylinderRes = PT_SET.bOtpDarkInspectionEnabled
                ? DarkInspectionCylinder.ForceOff("TurntableSafeStop:" + trigger)
                : EM_RES.OK;
            EM_RES stopRes = io_out_stop.SetOn();
            EM_RES fwdRes = io_out_fwd.SetOff();
            EM_RES revRes = io_out_rev.SetOff();
            EM_RES resetRes = io_out_reset.SetOff();
            EM_RES result = stopRes == EM_RES.OK && fwdRes == EM_RES.OK && revRes == EM_RES.OK
                && resetRes == EM_RES.OK && darkCylinderRes == EM_RES.OK
                ? EM_RES.OK
                : EM_RES.ERR;

            VAR.msg.AddMsg(result == EM_RES.OK ? Msg.EM_MSGTYPE.NOR : Msg.EM_MSGTYPE.ERR, string.Format(
                "TurntableSafeStop Trigger={0} Stop={1} FwdOff={2} RevOff={3} ResetOff={4} DarkCylinderOff={5} CardReady={6} Res={7}",
                trigger, stopRes, fwdRes, revRes, resetRes, darkCylinderRes, MT.CARD_ECI2400_0.isReady, result));
            return result;
        }

        public static EM_RES PrepareOtpHomeRecovery(string trigger)
        {
            if (io_in_move == null || io_out_fwd == null || io_out_rev == null || io_out_stop == null || io_out_reset == null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("OtpHomeRecovery Trigger={0} Reason=IOParameterMissing", trigger));
                return EM_RES.PARA_ERR;
            }

            if (!MT.CARD_ECI2400_0.isReady)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("OtpHomeRecovery Trigger={0} Reason=CardOffline", trigger));
                return EM_RES.ERR;
            }

            EM_RES refreshRes = MT.CARD_ECI2400_0.UpdateIOBuff(true);
            if (refreshRes != EM_RES.OK)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("OtpHomeRecovery Trigger={0} Reason=IORefreshFailed Res={1}", trigger, refreshRes));
                return refreshRes;
            }

            EM_RES stopRes = ForceSafeStop("OtpHomeRecovery:" + trigger);
            if (stopRes != EM_RES.OK) return stopRes;

            Thread.Sleep(100);
            refreshRes = MT.CARD_ECI2400_0.UpdateIOBuff(true);
            if (refreshRes != EM_RES.OK)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("OtpHomeRecovery Trigger={0} Reason=VerifyRefreshFailed Res={1}", trigger, refreshRes));
                return refreshRes;
            }

            bool moving = io_in_move.isON || !io_in_move.AssertOFF(3, 20);
            bool fwdOn = io_out_fwd.isON;
            bool revOn = io_out_rev.isON;
            bool stopOn = io_out_stop.isON;
            EM_RES result = !moving && !fwdOn && !revOn && stopOn ? EM_RES.OK : EM_RES.ERR;
            VAR.msg.AddMsg(result == EM_RES.OK ? Msg.EM_MSGTYPE.NOR : Msg.EM_MSGTYPE.ERR, string.Format(
                "OtpHomeRecovery Trigger={0} Moving={1} FwdOn={2} RevOn={3} StopOn={4} Position={5} Res={6}",
                trigger, moving, fwdOn, revOn, stopOn, ReadPhysicalPosition(), result));
            return result;
        }

        public static EM_RES ChkSafe(int id = 0, GPIO.IO_STA io_set = GPIO.IO_STA.OUT_ON)
        {
            EM_RES ret = EM_RES.OK;
            if (io_set == GPIO.IO_STA.OUT_OFF) return EM_RES.OK;
            if (!ThreeNozzleCoordinator.CanMoveFixture()) return EM_RES.SAFE_PROTECT;

            //check OTP
            if (!MT.AXIS_BOX_OTP_Z.isORG)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "OPT光源未在安全位置,转盘禁止动作!" : "The OPT light source is not in a safe position, and the turntable is prohibited from moving!        (OPT光源未在安全位置,转盘禁止动作!)", DReport.EmErrCode.SoftwareProtect, (int)DReport.EmHareware.TurnTable, ERR_ALM.EmErrItem.MoveProtect, ErrCode: ShowErrMsg.WsMoveStop);
                return EM_RES.SAFE_PROTECT;
            }

            //转盘
            ret = MT.ChkAllSafeSen(0x02);
            if (ret != EM_RES.OK) return ret;


            //check moving and stop
            EM_STA sta = GetCurSta;

            //限位
            if (io_in_sen0.isON && io_in_sen90.isON)
            {
                io_out_rev.SetOff();
                if (id == io_out_rev.id && io_set == GPIO.IO_STA.OUT_ON) return EM_RES.ERR;
            }
            if (io_in_sen0.isON && io_in_sen90.isOFF)
            {
                io_out_fwd.SetOff();
                if (id == io_out_fwd.id && io_set == GPIO.IO_STA.OUT_ON) return EM_RES.ERR;
            }


            if (sta == EM_STA.MOVING)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? "转盘运行中!" : "Turntable is running        (转盘运行中!)");
                io_out_fwd.SetOff();
                io_out_rev.SetOff();
                for (int n = 0; n < 30; n++)
                {
                    Thread.Sleep(100);
                    Application.DoEvents();
                }
                sta = GetCurSta;
                if (sta == EM_STA.MOVING)
                {
                    return EM_RES.ERR;
                }
            }
            //check alm and reset
            if (sta == EM_STA.ALM)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "转盘报警复位!" : "Turntable alarms when it resets!     (转盘报警复位!)", DReport.EmErrCode.MoveError, (int)DReport.EmHareware.TurnTable);
                if (io_out_reset != null)
                {
                    io_out_stop.SetOff();
                    Thread.Sleep(100);
                    io_out_reset.SetOn();
                    Thread.Sleep(100);
                    io_out_reset.SetOff();
                    Thread.Sleep(500);
                    sta = GetCurSta;
                    if (sta == EM_STA.ALM)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "转盘报警无法复位!" : "Turntable cannot be reset cause it alarms!      (转盘报警无法复位!)", DReport.EmErrCode.MoveError, (int)DReport.EmHareware.TurnTable, ERR_ALM.EmErrItem.MoveError);
                        return EM_RES.ERR;
                    }
                }
            }

            //检查工装状态，旋转不在0位置，气缸不在压紧位，则异常
            foreach (WS ws in COM.list_ws)
            {
                if (!ws.isInTestPos)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}不在测试位，转盘禁止动作!", ws.disc) : string.Format("{0} is not in test position, turntable is prohibited!      ({0}不在测试位，转盘禁止动作!)", ws.disc), DReport.EmErrCode.SoftwareProtect, (int)DReport.EmHareware.TurnTable + ws.num + 1, ERR_ALM.EmErrItem.MoveProtect, ErrCode: ShowErrMsg.WsMoveStop);
                    return EM_RES.ERR;
                }
            }
            return EM_RES.OK;
        }

        public static EM_RES MoveToNext(ref bool bquit)
        {
            EM_RES recoverRes = RecoverPosition("MoveToNext");
            if (recoverRes != EM_RES.OK) return recoverRes;

            //get current posInf
            EM_STA sta = GetCurSta;

            //next posInf
            if (sta == EM_STA.POS0) sta = EM_STA.POS1;
            else if (sta == EM_STA.POS1) sta = EM_STA.POS2;
            else if (sta == EM_STA.POS2) sta = EM_STA.POS3;
            else if (sta == EM_STA.POS3) sta = EM_STA.POS0;
            else sta = EM_STA.POS0;

            return MoveToIndex(ref bquit, sta, true);
        }
        public static EM_RES MoveToPre(ref bool bquit)
        {
            EM_RES recoverRes = RecoverPosition("MoveToPre");
            if (recoverRes != EM_RES.OK) return recoverRes;

            //get current posInf
            EM_STA sta = GetCurSta;

            //next posInf
            if (sta == EM_STA.POS0) sta = EM_STA.POS3;
            else if (sta == EM_STA.POS1) sta = EM_STA.POS0;
            else if (sta == EM_STA.POS2) sta = EM_STA.POS1;
            else if (sta == EM_STA.POS3) sta = EM_STA.POS2;
            else sta = EM_STA.POS0;

            return MoveToIndex(ref bquit, sta, true);
        }
        /// <summary>
        /// 定位到指定位置编号
        /// 在正运动控制器中增加当感应到(MOVE&&!INP)后撤销FWD/REV信号。
        /// </summary>
        /// <param name="bquit"></param>
        /// <param name="idx">位置编号</param>
        /// <returns></returns>
        public static EM_RES MoveToIndex(ref bool bquit, EM_STA pos)
        {
            return MoveToIndex(ref bquit, pos, false);
        }

        /// <summary>[R35] MoveToNext/MoveToPre 刚做过 RecoverPosition（清输出、读位置），MoveToIndex 里不再重复做一次（实测每次约 0.27s）。</summary>
        public static bool SkipDuplicateRecover = true;

        private static EM_RES MoveToIndex(ref bool bquit, EM_STA pos, bool justRecovered)
        {
            using (CtStepLog.Step("转盘", "转盘旋转", "到" + pos))
                return MoveToIndexCore(ref bquit, pos, justRecovered);
        }

        private static EM_RES MoveToIndexCore(ref bool bquit, EM_STA pos, bool justRecovered)
        {
            //check
            if (io_out_fwd == null || io_out_rev == null || io_in_inp == null) return EM_RES.PARA_ERR;
            if (pos > EM_STA.POS3 || pos < EM_STA.POS0) return EM_RES.PARA_OUTOFRANG;
            if (!MT.CARD_ECI2400_0.isReady)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "TurntableMoveBlocked Reason=CardOffline Card=ECI2400(192.168.0.100)");
                return EM_RES.ERR;
            }
            EM_RES ioRefreshRes = MT.CARD_ECI2400_0.UpdateIOBuff(true);
            if (ioRefreshRes != EM_RES.OK)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("TurntableMoveBlocked Reason=IORefreshFailed Res={0}", ioRefreshRes));
                return ioRefreshRes;
            }
            long ioAge = Math.Abs(VAR.msg.sw.ElapsedMilliseconds - MT.CARD_ECI2400_0.CardIOBuf.tickcnt);
            if (MT.CARD_ECI2400_0.CardIOBuf.tickcnt == 0 || ioAge > 200)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("TurntableMoveBlocked Reason=StaleIO IOAge={0}ms", ioAge));
                return EM_RES.ERR;
            }

            if (PT_SET.bOtpDarkInspectionEnabled)
            {
                EM_RES darkCylinderRes = DarkInspectionCylinder.Retract(ref bquit, "TurntableMoveToIndex:" + pos);
                if (darkCylinderRes != EM_RES.OK)
                {
                    DarkInspectionCylinder.ForceOff("TurntableMoveBlocked:" + pos);
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                        "TurntableMoveBlocked Reason=DarkInspectionCylinderNotRetracted Position={0} Res={1}",
                        pos, darkCylinderRes));
                    return darkCylinderRes;
                }
            }

            //protect
            EM_RES res = ChkSafe(-1);
            if (res != EM_RES.OK) return res;

            if (!(justRecovered && SkipDuplicateRecover && !NeedRecovery))
            {
                EM_RES recoverRes = RecoverPosition("MoveToIndex");
                if (recoverRes != EM_RES.OK) return recoverRes;
            }



            EM_STA sta = GetCurSta;
            EM_STA cur_pos = EM_STA.POS0;
            if (sta == EM_STA.POS0) cur_pos = EM_STA.POS0;
            else if (sta == EM_STA.POS1) cur_pos = EM_STA.POS1;
            else if (sta == EM_STA.POS2) cur_pos = EM_STA.POS2;
            else if (sta == EM_STA.POS3) cur_pos = EM_STA.POS3;
            else cur_pos = mCurPos;

            if (VAR.gsys_set.status == EM_SYS_STA.RUN)
            {
                if ((System.Environment.TickCount - tmr) > 1000)
                {
                    ct = (System.Environment.TickCount - tmr) / 1000.0;
                    if (PT_SET.bCycle) /*VAR.dttt.Rows.Add(DateTime.Now.ToString(), ct.ToString("000.0s"));*/DRpt.Report_Opration(3000, 0, ct.ToString("000.0s"));
                }
                n++;
            }

            tmr = System.Environment.TickCount;


            bool quit = false;
            //启动检测线程
            Task TaskChk = new Task(() =>
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? "转盘保护线程启动" : " Turntable protection thread start     (转盘保护线程启动)");
                GPIO.IO_STA inp_sta = io_in_inp.Status;
                //int cnt = 0;
                while (!quit)
                {
                    //check ws
                    foreach (WS ws in COM.list_ws)
                    {
                        if (quit) break;
                        if (!ws.isInTestPos)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}不在测试位，转盘禁止动作!", ws.disc) : string.Format("{0} is not in test position, turntable is prohibited!      ({0}不在测试位，转盘禁止动作!)", ws.disc), DReport.EmErrCode.SoftwareProtect, (int)DReport.EmHareware.TurnTable, ERR_ALM.EmErrItem.MoveProtect, ErrCode: ShowErrMsg.WsMoveStop);
                            if (io_out_stop != null) io_out_stop.SetOn();
                            quit = true;
                            break;
                        }
                    }
                    //check light box
                    if (PT_SET.LbEn)
                    {
                        if (!COM.OTPLightBox.isInSafePos || !COM.LeftLightBox.isInSafePos || !COM.RightLightBox.isInSafePos)
                        {
                            if (io_out_stop != null) io_out_stop.SetOn();
                            quit = true;
                        }
                    }
                    else
                    {
                        if (!COM.OTPLightBox.isInSafePos)
                        {
                            if (io_out_stop != null) io_out_stop.SetOn();
                            quit = true;
                        }
                    }
                    Thread.Sleep(50);
                    Application.DoEvents();
                }
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? "转盘保护线程退出" : "Turntable protection thread exits     (转盘保护线程退出)");
            }
            );
            //int cnt = posInf - cur_pos;
            Task TaskCnt = new Task(() =>
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? "计数线程启动" : "Count thread start     (计数线程启动)");
                GPIO.IO_STA inp_sta = io_in_inp.Status;
                while (!quit)
                {
                    //计数
                    //到位下降沿
                    //GPIO.IO_STA io_temp = io_in_inp.Status;
                    //if (inp_sta == GPIO.IO_STA.IN_ON && io_in_inp.Status == GPIO.IO_STA.IN_OFF && io_in_move.isON)
                    if (inp_sta == GPIO.IO_STA.IN_ON && io_in_inp.Status == GPIO.IO_STA.IN_OFF)
                    {
                        inp_sta = GPIO.IO_STA.IN_OFF;
                        //if (io_out_fwd.isON && io_out_rev.isON) ;
                        //else if (io_out_fwd.isON && io_out_rev.isOFF) mCurPos++;
                        //else if (io_out_rev.isON && io_out_fwd.isOFF) mCurPos--;
                        //else mCurPos = GetCurSta;

                        if (io_out_fwd.isON) mCurPos++;
                        else mCurPos--;

                        Thread.Sleep(100);
                        io_out_fwd.SetOff();
                        io_out_rev.SetOff();

                        ////Thread.Sleep(100);
                        //if (io_in_inp.Status == GPIO.IO_STA.IN_OFF)
                        //{
                        //    io_out_fwd.SetOff();
                        //    io_out_rev.SetOff();
                        //}
                        //else
                        //{
                        //    io_in_alm.SetOn();
                        //}

                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("转盘当前位置[{0}],目标{1}", mCurPos, pos) : string.Format("Current position [{0}] of the turntable, target location {1}     (转盘当前位置[{0}],目标{1})", mCurPos, pos));
                    }
                    else inp_sta = io_in_inp.Status;

                    if (io_out_rev.isON && (mCurPos < EM_STA.POS0 || io_in_sen0.isON && io_in_sen90.isON))
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("负限位,mCurPos{0},pos{1}", mCurPos, pos) : string.Format("Negative limit,mCurPos{0},pos{1}      (负限位,mCurPos{0},pos{1})", mCurPos, pos));
                        io_out_rev.SetOff();
                        io_out_fwd.SetOff();
                        // VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("负限位"));
                        break;
                    }
                    if (io_out_fwd.isON && (mCurPos > EM_STA.POS3 || io_in_sen0.isON && io_in_sen90.isOFF))
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("正限位,mCurPos{0},pos{1}", mCurPos, pos) : string.Format("Positive limit,mCurPos{0},pos{1}     (正限位,mCurPos{0},pos{1})", mCurPos, pos));
                        io_out_fwd.SetOff();
                        io_out_rev.SetOff();
                        //VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("正限位"));
                        break;
                    }

                    //check
                    if (mCurPos == pos)
                    {
                        io_out_fwd.SetOff();
                        io_out_rev.SetOff();
                        break;
                    }
                    else if (mCurPos < pos)
                    {
                        if (io_in_inp.Status == GPIO.IO_STA.IN_ON || (io_in_move != null && io_in_move.isOFF))
                        {
                            bool bsen0 = io_in_sen0.AssertON(3, 20);
                            bool bsen90 = io_in_sen90.AssertON(3, 20);
                            bool bsen270 = io_in_sen270.AssertOFF(3, 20);
                            EM_STA Temp_mCurPos = EM_STA.POS0;
                            if (bsen0 && bsen90 && !bsen270)
                            {
                                Temp_mCurPos = EM_STA.POS0;
                            }
                            if (!bsen0 && bsen90 && !bsen270)
                            {
                                Temp_mCurPos = EM_STA.POS1;
                            }
                            if (!bsen0 && !bsen90 && bsen270)
                            {
                                Temp_mCurPos = EM_STA.POS2;
                            }
                            if (bsen0 && !bsen90 && bsen270)
                            {
                                Temp_mCurPos = EM_STA.POS3;
                            }
                            if (mCurPos != Temp_mCurPos)
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("原来mCurPos{0}", mCurPos) : string.Format("old  mCurPos{0}    (原来mCurPos{0})", mCurPos));
                                mCurPos = Temp_mCurPos;
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("补偿mCurPos{0}", mCurPos) : string.Format("cmp:mCurPos{0}      (补偿mCurPos{0})", mCurPos));
                                count++;
                                if (count < 2)
                                    break;
                            }
                            else
                            {
                                io_out_rev.SetOff();
                                if (!io_out_fwd.isON)
                                {
                                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("TurntableOutput FWD=ON REV=OFF Target={0} Position={1}", pos, mCurPos));
                                    io_out_fwd.SetOn();
                                }
                            }
                            //if (mCurPos != Temp_mCurPos&& mCurPos < EM_STA.POS3)
                            //{
                            //    //VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese?string.Format("原来mCurPos{0}", mCurPos): string.Format("old:mCurPos{0}    (原来mCurPos{0})", mCurPos));
                            //    //mCurPos = Temp_mCurPos;
                            //    //VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("补偿mCurPos{0}", mCurPos): string.Format("cmp:mCurPos{0}        (补偿mCurPos{0})", mCurPos));
                            //    io_out_rev.SetOff();
                            //    io_out_fwd.SetOn();
                            //    count++;
                            //    if (count >= 2)
                            //    {
                            //        VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("计数位置与检测位置2次不符合，mCurPos{0}", mCurPos) : string.Format("Err:mCurPos!=Temp_mCurPos,mCurPos={0}        (计数位置与检测位置2次不符合，mCurPos{0})", mCurPos));
                            //        io_out_rev.SetOff();
                            //        break;
                            //    }

                            //}
                            ////else if(mCurPos == Temp_mCurPos)
                            ////{
                            ////    io_out_rev.SetOff();
                            ////    io_out_fwd.SetOn();
                            ////}
                        }
                    }
                    else if (mCurPos > pos)
                    {
                        if (io_in_inp.Status == GPIO.IO_STA.IN_ON || (io_in_move != null && io_in_move.isOFF))
                        {
                            bool bsen0 = io_in_sen0.AssertON(3, 20);
                            bool bsen90 = io_in_sen90.AssertON(3, 20);
                            bool bsen270 = io_in_sen270.AssertOFF(3, 20);
                            EM_STA Temp_mCurPos = EM_STA.POS0;
                            if (bsen0 && bsen90 && !bsen270)
                            {
                                Temp_mCurPos = EM_STA.POS0;
                            }
                            if (!bsen0 && bsen90 && !bsen270)
                            {
                                Temp_mCurPos = EM_STA.POS1;
                            }
                            if (!bsen0 && !bsen90 && bsen270)
                            {
                                Temp_mCurPos = EM_STA.POS2;
                            }
                            if (bsen0 && !bsen90 && bsen270)
                            {
                                Temp_mCurPos = EM_STA.POS3;
                            }
                            if (mCurPos != Temp_mCurPos)
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("原来mCurPos{0}", mCurPos) : string.Format("old  mCurPos{0}    (原来mCurPos{0})", mCurPos));
                                mCurPos = Temp_mCurPos;
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("补偿mCurPos{0}", mCurPos) : string.Format("cmp:mCurPos{0}      (补偿mCurPos{0})", mCurPos));
                                count++;
                                if (count < 2)
                                    break;
                            }
                            else
                            {
                                io_out_fwd.SetOff();
                                if (!io_out_rev.isON)
                                {
                                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("TurntableOutput FWD=OFF REV=ON Target={0} Position={1}", pos, mCurPos));
                                    io_out_rev.SetOn();
                                }
                            }
                        }
                    }

                    //inp_sta = io_in_inp.Status;
                    Thread.Sleep(10);
                    //Application.DoEvents();
                }

                count = 0;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? "计数线程退出" : "Counting thread exits      (计数线程退出)");
            }
            );

            try
            {
                motionCommandActive = true;
                //check light box
                if (PT_SET.LbEn)
                {
                    if (!COM.OTPLightBox.isInSafePos || !COM.LeftLightBox.isInSafePos || !COM.RightLightBox.isInSafePos)
                    {
                        return EM_RES.MOVE_PROTECT;
                    }
                }
                else
                {
                    if (!COM.OTPLightBox.isInSafePos)
                    {
                        return EM_RES.MOVE_PROTECT;
                    }
                }

                //check ws
                foreach (WS ws in COM.list_ws)
                {
                    if (!ws.isInTestPos)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}不在测试位，转盘禁止动作!", ws.disc) : string.Format("{0}is not in test position, turntable is prohibited!      ({0}不在测试位，转盘禁止动作!)", ws.disc), DReport.EmErrCode.SoftwareProtect, (int)DReport.EmHareware.TurnTable, ERR_ALM.EmErrItem.MoveProtect, ErrCode: ShowErrMsg.WsMoveStop);
                        return EM_RES.MOVE_PROTECT;
                    }
                }

                //reset alm
                if (io_out_stop != null)
                {
                    if (io_out_stop.isON)
                    {
                        io_out_stop.SetOff();
                        Thread.Sleep(50);
                        io_out_reset.SetOn();
                        Thread.Sleep(100);
                        io_out_reset.SetOff();
                    }
                }
                //启动保护线程
                TaskChk.Start();
                io_out_fwd.ChkSafe = null;
                io_out_rev.ChkSafe = null;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("转盘当前位置[{0}],距离目标{1}", mCurPos, pos) : string.Format("Current position [{0}] of the turntable,target distance{1}         (转盘当前位置[{0}],距离目标{1})", mCurPos, pos));

                TaskCnt.Start();
                while (!bquit)
                {
                    Thread.Sleep(20);
                    Application.DoEvents();
                    if (TaskCnt.IsCompleted) break;
                }

                //reset
                if (io_in_inp.AssertOFF())
                {
                    io_out_fwd.SetOff();
                    io_out_rev.SetOff();
                }

                //wait
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? "等转盘停止" : "Wait for the turntable to stop     (等转盘停止)");
                bool _bquit = false;
                res = io_in_move.WaitForSta(ref _bquit, GPIO.IO_STA.IN_OFF, 5000);
                if (res != EM_RES.OK) return res;

                //check posInf
                cur_pos = GetCurSta;
                if (cur_pos != pos)
                {
                    io_out_fwd.SetOff();
                    io_out_rev.SetOff();
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("转盘当前位置[{0}],未转到指定位置[{1}]", cur_pos, pos) : string.Format("Current position [{0}] of the turntable.Turntable did not go to the specified location       (转盘当前位置[{0}],未转到指定位置[{1}])", cur_pos, pos), DReport.EmErrCode.MoveError, (int)DReport.EmHareware.TurnTable, ERR_ALM.EmErrItem.MoveError);
                    VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, VAR.IsChinese ? "转盘异常!" : "TurnTable Error", 20, true);
                    MT.ST_WARN warn = new MT.ST_WARN();//鞥加语言
                    warning fr_warn = new warning();
                    warn.ok_txt = MultiLanguage.TxtSelct("确认", "OK", "xác nhận");
                    warn.abort_txt = MultiLanguage.TxtSelct("不用", "Need not", "Không cần");
                    warn.cancle_txt = MultiLanguage.TxtSelct("不用", "Need not", "Không cần");
                    warn.ws = null;
                    warn.title = MultiLanguage.TxtSelct("提示:转盘异常", "Tip: TurnTable Error", "Mẹo: Bàn xoay không bình thường");
                    warn.msg = MultiLanguage.TxtSelct("转盘异常", "TurnTable Error", "Bàn xoay không bình thường");
                    warn.lb_msg = MultiLanguage.TxtSelct(
                        "转盘当前位置[" + cur_pos.ToString() + "],未转到指定位置[" + pos.ToString() + "],请确认转盘到位信号与角度光电开关，确认无误后才能继续运行!",
                        "The current position of the turntable[" + cur_pos.ToString() + "],Did not go to the specified location[" + pos.ToString() + "], Please confirm the turntable in-position signal and the angle photoelectric switch, and continue to run after confirmation! ",
                        "Vị trí hiện tại của bàn xoay[" + cur_pos.ToString() + "],Đã không đi đến vị trí được chỉ định[" + pos.ToString() + "],Vui lòng xác nhận tín hiệu tại vị trí của bàn xoay và công tắc quang điện góc, và tiếp tục chạy sau khi xác nhận!");
                    DialogResult logres = MT.Display_frwarn(fr_warn, warn, ERR_ALM.EmErrItem.MaterialPosErr);
                    return EM_RES.ERR;
                }
            }
            finally
            {
                quit = true;
                io_out_fwd.SetOff();
                io_out_rev.SetOff();
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? "结束" : "END");
                //reset alm
                if (io_out_stop != null)
                {
                    if (io_out_stop.isON)
                    {
                        io_out_stop.SetOff();
                        Thread.Sleep(50);
                        io_out_reset.SetOn();
                        Thread.Sleep(100);
                        io_out_reset.SetOff();
                    }
                }

                io_out_fwd.SetOff();
                io_out_rev.SetOff();
                io_out_fwd.ChkSafe = ChkSafe;
                io_out_rev.ChkSafe = ChkSafe;
                motionCommandActive = false;
            }

            return EM_RES.OK;
        }

        public static int count = 0;
        //检查当前位置
        public static EM_STA mCurPos = EM_STA.POS0;
        public static EM_STA GetCurSta
        {
            get
            {
                GuardIdleDirectionOutputs();
                EM_STA sta = ReadPhysicalPosition();
                if (sta >= EM_STA.POS0 && sta <= EM_STA.POS3) mCurPos = sta;
                return sta;
            }
        }
        /// <summary>
        /// 获取指定编号工装的当前位置编号
        /// </summary>
        /// <param name="ws_num"></param>
        /// <returns></returns>
        public static EM_STA GetWSSta(int ws_num)
        {
            EM_STA sta = GetCurSta;

            if (sta < EM_STA.POS0 || sta > EM_STA.POS3)
                return EM_STA.UNKNOW;

            sta = (EM_STA)(((int)sta + ws_num) % 4);
            return sta;
        }
        /// <summary>
        /// 获取处于上下料位置的工装
        /// </summary>
        public static WS GetWSOnFeedPos
        {
            get
            {
                return GetWSOnPos(EM_STA.POS0);
            }
        }
        public static WS GetWSOnPos(EM_STA pos)
        {
            if (pos < EM_STA.POS0 || pos > EM_STA.POS3) return null;
            // 转盘位置只读一次：GetCurSta 每次都要对 4 个传感器做 AssertON/OFF(3,20) 去抖（约 0.2s），
            // 原来对每个工站调一次 GetWSSta，找一个工站要读最多 4 次
            return GetWSOnPos(pos, GetCurSta);
        }

        /// <summary>按已读到的转盘位置 cur 找处于 pos 的工站（不再读传感器）。</summary>
        public static WS GetWSOnPos(EM_STA pos, EM_STA cur)
        {
            if (pos < EM_STA.POS0 || pos > EM_STA.POS3) return null;
            if (cur < EM_STA.POS0 || cur > EM_STA.POS3) return null;
            foreach (WS ws in COM.list_ws)
            {
                if ((EM_STA)(((int)cur + ws.num) % 4) == pos) return ws;
            }
            return null;
        }
    }
}
