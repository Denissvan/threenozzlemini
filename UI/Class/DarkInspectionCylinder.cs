using System.Diagnostics;
using System.Threading;
using MotionCtrl;

namespace UI
{
    public static class DarkInspectionCylinder
    {
        public const int DarkInspectionPositionCode = 106;
        public const int TimeoutMs = 1000;
        public const int PollIntervalMs = 10;

        private static GPIO Output => MT.GPIO_OUT_DARK_INSPECTION;
        private static GPIO ExtendedSensor => MT.GPIO_IN_DARK_INSPECTION_EXTENDED;
        private static GPIO RetractedSensor => MT.GPIO_IN_DARK_INSPECTION_RETRACTED;

        public static bool IsActive { get; private set; }
        public static bool IsExtended => ExtendedSensor.isON && RetractedSensor.isOFF;
        public static bool IsRetracted => RetractedSensor.isON && ExtendedSensor.isOFF;

        public static EM_RES Extend(ref bool bquit, string trigger)
        {
            EM_RES result = Move(ref bquit, true, trigger);
            if (result == EM_RES.OK) IsActive = true;
            return result;
        }

        public static EM_RES Retract(ref bool bquit, string trigger)
        {
            EM_RES result = Move(ref bquit, false, trigger);
            if (result == EM_RES.OK) IsActive = false;
            return result;
        }

        public static EM_RES ForceOff(string trigger)
        {
            EM_RES result = Output == null ? EM_RES.PARA_ERR : Output.SetOff();
            Trace(trigger, "ForceOff", 0, result);
            return result;
        }

        private static EM_RES Move(ref bool bquit, bool extend, string trigger)
        {
            string action = extend ? "Extend" : "Retract";
            if (Output == null || ExtendedSensor == null || RetractedSensor == null)
            {
                Trace(trigger, action + "IoMissing", 0, EM_RES.PARA_ERR);
                return EM_RES.PARA_ERR;
            }

            if (!MT.CARD_ECI2400_0.isReady)
            {
                Trace(trigger, action + "CardOffline", 0, EM_RES.ERR);
                return EM_RES.ERR;
            }

            EM_RES result = extend ? Output.SetOn() : Output.SetOff();
            if (result != EM_RES.OK)
            {
                Trace(trigger, action + "WriteFailed", 0, result);
                return result;
            }

            Stopwatch timer = Stopwatch.StartNew();
            bool sensorConflictLogged = false;
            while (timer.ElapsedMilliseconds < TimeoutMs)
            {
                if (bquit || VAR.gsys_set.bquit)
                {
                    ForceOff(trigger + ":Quit");
                    return EM_RES.QUIT;
                }

                result = MT.CARD_ECI2400_0.UpdateIOBuff(true);
                if (result != EM_RES.OK)
                {
                    ForceOff(trigger + ":RefreshFailed");
                    Trace(trigger, action + "RefreshFailed", timer.ElapsedMilliseconds, result);
                    return result;
                }

                bool sensorConflict = ExtendedSensor.isON == RetractedSensor.isON;
                if (sensorConflict && !sensorConflictLogged)
                {
                    sensorConflictLogged = true;
                    Trace(trigger, action + "SensorInvalid", timer.ElapsedMilliseconds, EM_RES.ERR);
                }
                else if (!sensorConflict && (extend ? IsExtended : IsRetracted))
                {
                    Trace(trigger, extend ? "Extended" : "Retracted", timer.ElapsedMilliseconds, EM_RES.OK);
                    return EM_RES.OK;
                }

                Thread.Sleep(PollIntervalMs);
            }

            if (extend) ForceOff(trigger + ":ExtendTimeout");
            Trace(trigger, action + "Timeout", timer.ElapsedMilliseconds, EM_RES.TIMEOUT);
            return EM_RES.TIMEOUT;
        }

        private static void Trace(string trigger, string phase, long elapsedMs, EM_RES result)
        {
            string message = string.Format(
                "DarkInspectionTrace Trigger={0} Phase={1} Out4={2} In14={3} In15={4} Elapsed={5}ms Res={6}",
                trigger,
                phase,
                Output == null ? GPIO.IO_STA.NULL : Output.Status,
                ExtendedSensor == null ? GPIO.IO_STA.NULL : ExtendedSensor.Status,
                RetractedSensor == null ? GPIO.IO_STA.NULL : RetractedSensor.Status,
                elapsedMs,
                result);
            VAR.msg.AddMsg(result == EM_RES.OK ? Msg.EM_MSGTYPE.NOR : Msg.EM_MSGTYPE.ERR, message);
        }
    }
}
