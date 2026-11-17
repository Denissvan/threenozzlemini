using System;
using System.Collections.Generic;
using System.Threading;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// 调试模式：按输入点屏蔽软件急停判断，或临时忽略所选组的门禁使能，
    /// 只用于上下料调试和空跑。
    /// 约束：
    /// 1. 只存在于本次运行，不写入任何配置文件，软件重启后一定是关闭状态；
    /// 2. 不修改原门禁配置，不影响硬件急停回路（伺服断电/STO）和软限位；
    /// 3. 调试模式开启期间，无料空跑/有料空跑允许（机台门禁、急停未接全时调试用）；正常生产只允许联调，
    ///    每次启动弹窗确认并写日志（见 BaseAction.ConfirmJointDebugRun）；
    ///    自动运行中不允许开启或关闭调试模式（避免运行中途光箱启用状态等突然变化）；
    /// 4. 开启、关闭和屏蔽内容都写日志。
    /// </summary>
    public static class SafetyDebugMode
    {
        [Flags]
        public enum EmgInput
        {
            None = 0,
            TurnTable = 1,
            LeftLightBox = 2,
            RightLightBox = 4,
            UpDownLoad = 8,
        }

        [Flags]
        public enum DoorGroup
        {
            None = 0,
            UpDownLoad = 1,
            TurnTable = 2,
            RightLightBox = 4,
            LeftLightBox = 8,
        }

        // Low four bits: emergency inputs; next four bits: door groups; bit 8: 屏蔽左右光箱. Applied atomically.
        private const int LightBoxMaskBit = 1 << 8;
        // 调试启用与安全屏蔽独立：允许全部安全检查正常启用时进入调试。
        private const int DebugEnabledBit = 1 << 9;
        private static int ignored;
        private static DateTime activatedAt;

        /// <summary>开启/关闭/屏蔽内容变化时触发（可能在非 UI 线程）。</summary>
        public static event Action Changed;

        public static bool IsActive { get { return Volatile.Read(ref ignored) != 0; } }
        public static EmgInput Ignored { get { return (EmgInput)(Volatile.Read(ref ignored) & 15); } }
        public static DoorGroup IgnoredDoors { get { return (DoorGroup)((Volatile.Read(ref ignored) >> 4) & 15); } }
        /// <summary>调试屏蔽左右光箱（板卡未接入时用）：运行期间按“光箱未启用”处理，不写配置。</summary>
        public static bool LightBoxMasked { get { return (Volatile.Read(ref ignored) & LightBoxMaskBit) != 0; } }
        public static DateTime ActivatedAt { get { return activatedAt; } }

        public static bool ShouldCheckDoor(bool configuredEnabled, DoorGroup group)
        {
            return configuredEnabled && (IgnoredDoors & group) == 0;
        }

        public static string CurrentDescription { get { return Describe(Ignored, IgnoredDoors, LightBoxMasked); } }

        public static IEnumerable<KeyValuePair<EmgInput, GPIO>> EmgInputs
        {
            get
            {
                yield return new KeyValuePair<EmgInput, GPIO>(EmgInput.TurnTable, MT.GPIO_IN_EMG0);
                yield return new KeyValuePair<EmgInput, GPIO>(EmgInput.LeftLightBox, MT.GPIO_IN_EMG1);
                yield return new KeyValuePair<EmgInput, GPIO>(EmgInput.RightLightBox, MT.GPIO_IN_EMG2);
                yield return new KeyValuePair<EmgInput, GPIO>(EmgInput.UpDownLoad, MT.GPIO_IN_EMG3);
            }
        }

        /// <summary>软件是否把该急停输入判为“按下”。被调试模式屏蔽的输入永远返回 false。</summary>
        public static bool IsPressed(GPIO input, EmgInput key)
        {
            if ((Ignored & key) == key) return false;
            // 屏蔽左右光箱时，光箱板卡上的急停输入随板卡一起不参与判断（板卡未接入，读数无意义）
            if (IsMaskedByLightBox(key)) return false;
            return ReadRawPressed(input);
        }

        public static bool IsMaskedByLightBox(EmgInput key)
        {
            return LightBoxMasked && (key == EmgInput.LeftLightBox || key == EmgInput.RightLightBox);
        }

        /// <summary>按板卡定义读取原始急停状态，供界面显示，不应用调试屏蔽。</summary>
        public static bool ReadRawPressed(GPIO input)
        {
            return input != null && input.Status ==
                (input.card == null ? GPIO.IO_STA.IN_OFF : input.card.EmgActiveState);
        }

        /// <summary>未屏蔽的急停输入中是否有按下的。</summary>
        public static bool AnyEffectiveEmgPressed()
        {
            foreach (var item in EmgInputs)
                if (IsPressed(item.Value, item.Key)) return true;
            return false;
        }

        /// <summary>开启调试模式，屏蔽指定急停输入。自动运行中不允许开启。</summary>
        public static bool Enable(EmgInput inputs, string operatorName, out string error)
        {
            return Enable(inputs, DoorGroup.None, operatorName, out error);
        }

        public static bool Enable(EmgInput inputs, DoorGroup doors, string operatorName, out string error)
        {
            return Enable(inputs, doors, false, operatorName, out error);
        }

        public static bool Enable(EmgInput inputs, DoorGroup doors, bool maskLightBox, string operatorName, out string error)
        {
            error = null;
            if (VAR.gsys_set.status == EM_SYS_STA.RUN || VAR.gsys_set.status == EM_SYS_STA.PAUSE ||
                BaseAction.bRuning || BaseAction.brun || UpDownLoad.brun)
            {
                error = "自动运行中禁止进入调试模式，请先停止";
                return false;
            }
            bool wasActive = IsActive;
            bool wasLbMasked = PT_SET.LbDebugMasked;
            Interlocked.Exchange(ref ignored, DebugEnabledBit | (int)inputs | ((int)doors << 4) | (maskLightBox ? LightBoxMaskBit : 0));
            PT_SET.SetLbDebugMask(maskLightBox);
            if (!wasActive) activatedAt = DateTime.Now;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("【调试模式】应用设置：{0}，操作员：{1}",
                Describe(inputs, doors, maskLightBox), string.IsNullOrEmpty(operatorName) ? "未知" : operatorName));
            if (maskLightBox != wasLbMasked)
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, maskLightBox
                    ? string.Format("【调试模式】屏蔽左右光箱：运行期间按“左右光箱未启用”处理（不检查左/右光箱板卡、门禁，系统复位不回光箱，转台不检查光箱位置），原配置={0}，不写入配置", PT_SET.LbEnConfigured ? "启用" : "未启用")
                    : string.Format("【调试模式】取消屏蔽左右光箱，恢复原配置：{0}", PT_SET.LbEn ? "启用" : "未启用"));
            Raise();
            return true;
        }

        /// <summary>自动运行（含暂停）或上下料线程是否在跑。</summary>
        public static bool AutoRunActive
        {
            get
            {
                return VAR.gsys_set.status == EM_SYS_STA.RUN || VAR.gsys_set.status == EM_SYS_STA.PAUSE ||
                    BaseAction.bRuning || BaseAction.brun || UpDownLoad.brun;
            }
        }

        /// <summary>调试模式下是否允许以该运行方式启动自动运行：只允许无料空跑、有料空跑。</summary>
        public static bool AllowAutoRunPattern(int runPattern)
        {
            return runPattern == (int)PT_SET.RUN_PATTERN.RUN_EMPTY || runPattern == (int)PT_SET.RUN_PATTERN.RUN_UPDW;
        }

        public static void Disable(string reason)
        {
            if (!IsActive) return;
            bool wasLbMasked = PT_SET.LbDebugMasked;
            Interlocked.Exchange(ref ignored, 0);
            PT_SET.SetLbDebugMask(false);
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, "【调试模式】关闭，急停、门禁及光箱启用判断恢复原配置：" + (reason ?? ""));
            if (wasLbMasked)
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("【调试模式】取消屏蔽左右光箱，恢复原配置：{0}。光箱轴需重新回零", PT_SET.LbEn ? "启用" : "未启用"));
            Raise();
        }

        public static string Describe(EmgInput inputs)
        {
            var names = new List<string>();
            foreach (var item in EmgInputs)
                if ((inputs & item.Key) == item.Key) names.Add(item.Value == null ? item.Key.ToString() : item.Value.str_disc);
            return names.Count == 0 ? "无" : string.Join("、", names);
        }

        public static string Describe(EmgInput inputs, DoorGroup doors, bool maskLightBox)
        {
            return Describe(inputs, doors) + "；光箱屏蔽：" + (maskLightBox ? "左右光箱" : "无");
        }

        public static string Describe(EmgInput inputs, DoorGroup doors)
        {
            var names = new List<string>();
            if ((doors & DoorGroup.UpDownLoad) != 0) names.Add("上下料");
            if ((doors & DoorGroup.TurnTable) != 0) names.Add("转盘");
            if ((doors & DoorGroup.LeftLightBox) != 0) names.Add("左光箱");
            if ((doors & DoorGroup.RightLightBox) != 0) names.Add("右光箱");
            return "急停屏蔽：" + Describe(inputs) + "；门禁忽略：" + (names.Count == 0 ? "无" : string.Join("、", names));
        }

        private static void Raise()
        {
            Action handler = Changed;
            if (handler == null) return;
            try { handler(); } catch { }
        }
    }
}
