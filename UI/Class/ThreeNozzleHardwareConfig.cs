using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    /// <summary>三吸嘴调机参数（ThreeNozzleHardware.ini）。加载时不发出任何 IO 或运动指令。</summary>
    public static class ThreeNozzleHardwareConfig
    {
        private static readonly object sync = new object();
        private static bool initialized;
        private static readonly double[] safeTrayMaxY = { double.NaN, double.NaN };
        // [臂, 嘴] Z 向下时轴坐标符号，缺省 1（向下为正）。
        private static readonly int[,] zDownDirection = { { 1, 1, 1 }, { 1, 1, 1 } };

        /// <summary>
        /// Z 向下时轴坐标的符号（1 或 -1）。ini：[ArmN] ZDownDirection=-1 作用于本臂三嘴，
        /// ZDownDirectionN1/N2/N3 可单独覆盖；用于接触动作和退让方向，不改变配方 Z 的符号。
        /// </summary>
        public static int ZDownDirection(int arm, int slot)
        {
            if (arm < 0 || arm > 1 || slot < 0 || slot > 2) return 1;
            return zDownDirection[arm, slot];
        }
        // 第三嘴的真空/破真空/真空感应点位已写死在 MT.cs（GPIO_OUT_UDLn_ZK_N3 等，见 WLTmini IO分配表 / 技改新增轴卡），
        // 本文件只读调机参数：SafeTrayMaxY、ZDownDirection、CalibrationConfirmed。ini 里旧的 Vacuum/VacuumBreak/VacuumSense 键会被忽略。

        public static string ConfigurationPath { get { return Path.Combine(Application.StartupPath, "ThreeNozzleHardware.ini"); } }
        public static bool IsConfigured { get; private set; }
        public static bool CalibrationConfirmed { get; private set; }
        /// <summary>系统页“三吸嘴飞拍”：[Machine] FlyCapture=true。false 时三吸嘴全部定拍（原定拍流程）。</summary>
        public static bool FlyCaptureEnabled { get; private set; }
        public static bool FlyTwoSpeedEnabled { get; private set; }
        private static readonly double[] flyLowSpeed = { 250, 250 };
        public static double FlyLowSpeed(int arm)
        {
            if (arm < 0 || arm > 1) throw new ArgumentOutOfRangeException("arm");
            return flyLowSpeed[arm];
        }

        public static bool SetFlyTwoSpeed(bool enabled, double low1, double low2, string operatorName, out string error)
        {
            error = null;
            if (SafetyDebugMode.AutoRunActive) { error = "自动运行中不能修改飞拍两段速，请先停止运行"; return false; }
            if (NozzleZCalibrationMotion.IsBusy) { error = "标定动作执行中不能修改飞拍两段速，请先结束标定"; return false; }
            lock (sync)
            {
                Initialize();
                double[] lows = { low1, low2 };
                for (int arm = 0; arm < 2; arm++)
                {
                    double high = arm == 0 ? MT.AXIS_UDL1_Y.spd_work : MT.AXIS_UDL2_Y.spd_work;
                    if (!Finite(lows[arm]) || lows[arm] <= 0 || (enabled && (!Finite(high) || high <= 0 || lows[arm] > high)))
                    { error = "模块" + (arm + 1) + "低速必须大于0，启用时不得超过对应Y轴工作速度"; return false; }
                }
                if (!WriteMachineValues(new Dictionary<string, string> {
                    { "FlyTwoSpeed", enabled ? "true" : "false" },
                    { "FlyLowSpeed1", low1.ToString("R", CultureInfo.InvariantCulture) },
                    { "FlyLowSpeed2", low2.ToString("R", CultureInfo.InvariantCulture) }
                }, out error)) return false;
                flyLowSpeed[0] = low1; flyLowSpeed[1] = low2;
                FlyTwoSpeedEnabled = enabled;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("飞拍两段速 {0}，模块1低速={1:F1}，模块2低速={2:F1} mm/s，操作员={3}", enabled ? "开启" : "关闭", low1, low2, operatorName));
            return true;
        }
        public static string ConfigurationError { get; private set; }

        static ThreeNozzleHardwareConfig()
        {
            ConfigurationError = "三吸嘴硬件配置尚未加载。";
        }

        /// <summary>自动取值时在最大 Y 上加的余量（mm），与原安全判断“下相机拍照位 + 3”一致。</summary>
        public const double AutoSafeTrayMargin = 3.0;
        private static readonly double[] lastAutoSafeTrayMaxY = { double.NaN, double.NaN };

        /// <summary>
        /// 预取阶段（只在料盘侧活动）允许的最大 Y（mm）。arm 从 0 开始。
        /// ini 里 [ArmN] SafeTrayMaxY 填了数字就用填的值；没填（NaN）时自动取：
        /// 本臂三个吸嘴下相机拍照位 st_cap_pos.y 的最大值（开了电机扫码时再与电机扫码位比较取大）+ AutoSafeTrayMargin。
        /// 返回 NaN 表示既没配置、也无法自动计算（此时禁止预取和自动运行）。
        /// </summary>
        public static double SafeTrayMaxY(int arm)
        {
            if (arm < 0 || arm > 1) throw new ArgumentOutOfRangeException("arm");
            double configured = safeTrayMaxY[arm];
            if (!double.IsNaN(configured)) return configured;
            return AutoSafeTrayMaxY(arm);
        }

        /// <summary>当前是否为自动取值（ini 未配置）。</summary>
        public static bool IsSafeTrayMaxYAuto(int arm)
        {
            return arm >= 0 && arm <= 1 && double.IsNaN(safeTrayMaxY[arm]);
        }

        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        private static double AutoSafeTrayMaxY(int arm)
        {
            try
            {
                UpDownLoad ud = arm == 0 ? COM.UDLoad1 : COM.UDLoad2;
                if (ud == null || ud.list_xt == null || ud.list_xt.Count == 0) return double.NaN;
                double max = double.NegativeInfinity;
                string source = "";
                for (int slot = 0; slot < ud.list_xt.Count; slot++)
                {
                    XT xt = ud.list_xt[slot];
                    if (xt == null || !Finite(xt.st_cap_pos.y)) return double.NaN;   // 拍照位未标定，不自动放开
                    if (xt.st_cap_pos.y > max) { max = xt.st_cap_pos.y; source = string.Format("吸嘴{0}下相机拍照位", slot + 1); }
                }
                if (PT_SET.bmotorphoto)
                {
                    // 电机扫码位：吸嘴1/2 沿用原供料盘参数，吸嘴3 取 ini 的 MotorScanY3（与 ThreeNozzleMotorScan 一致）
                    for (int slot = 0; slot < ud.list_xt.Count && slot < 2; slot++)
                    {
                        XT xt = ud.list_xt[slot];
                        double[] motor = ud.traybox_fd == null ? null : ud.traybox_fd.motor_barcode_pos;
                        if (motor != null && xt.id >= 0 && xt.id < motor.Length && Finite(motor[xt.id]) && motor[xt.id] > max)
                        { max = motor[xt.id]; source = string.Format("吸嘴{0}电机扫码位", slot + 1); }
                    }
                    double y3;
                    Dictionary<string, string> values = File.Exists(ConfigurationPath) ? ReadIni(ConfigurationPath) : new Dictionary<string, string>();
                    if (double.TryParse(Get(values, "Arm" + (arm + 1) + ".MotorScanY3"), NumberStyles.Float, CultureInfo.InvariantCulture, out y3) &&
                        Finite(y3) && y3 > max)
                    { max = y3; source = "吸嘴3电机扫码位"; }
                }
                if (PT_SET.TrayBarcodeEn && ud.traybox_fd != null)
                {
                    // 换盘扫料盘码位置也在预取阶段走到
                    double trayCodeY = arm == 0 ? ud.traybox_fd.tray_barcode_y1 : ud.traybox_fd.tray_barcode_y2;
                    if (Finite(trayCodeY) && trayCodeY > max) { max = trayCodeY; source = "料盘扫码位"; }
                }
                if (!Finite(max)) return double.NaN;
                double limit = max + AutoSafeTrayMargin;
                if (!(Math.Abs(limit - lastAutoSafeTrayMaxY[arm]) < 1e-6))
                {
                    lastAutoSafeTrayMaxY[arm] = limit;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format(
                        "三吸嘴 臂{0} 料盘安全区 SafeTrayMaxY 自动取值={1:F3}（{2} Y={3:F3} + {4}）。ini 填数字可覆盖",
                        arm + 1, limit, source, max, AutoSafeTrayMargin));
                }
                return limit;
            }
            catch
            {
                return double.NaN;
            }
        }

        public static void Initialize()
        {
            lock (sync)
            {
                if (initialized) return;
                initialized = true;
                try
                {
                    Dictionary<string, string> values = ReadIni(ConfigurationPath);
                    bool confirmed;
                    CalibrationConfirmed = bool.TryParse(Get(values, "Machine.CalibrationConfirmed"), out confirmed) && confirmed;
                    bool fly;
                    FlyCaptureEnabled = bool.TryParse(Get(values, "Machine.FlyCapture"), out fly) && fly;
                    bool twoSpeed;
                    FlyTwoSpeedEnabled = bool.TryParse(Get(values, "Machine.FlyTwoSpeed"), out twoSpeed) && twoSpeed;
                    for (int arm = 0; arm < 2; arm++)
                    {
                        string raw = Get(values, "Machine.FlyLowSpeed" + (arm + 1));
                        double low;
                        if (string.IsNullOrWhiteSpace(raw)) low = 250;
                        else if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out low) || !Finite(low) || low <= 0)
                            throw new FormatException("模块" + (arm + 1) + "飞拍低速配置无效");
                        flyLowSpeed[arm] = low;
                    }
                    double[] limits = new double[2];
                    for (int arm = 0; arm < 2; arm++)
                    {
                        double limit;
                        limits[arm] = double.TryParse(Get(values, "Arm" + (arm + 1) + ".SafeTrayMaxY"), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out limit) && !double.IsNaN(limit) && !double.IsInfinity(limit) ? limit : double.NaN;
                    }
                    int[,] directions = new int[2, 3];
                    for (int arm = 0; arm < 2; arm++)
                    {
                        string section = "Arm" + (arm + 1);
                        int armDir = ParseDirection(Get(values, section + ".ZDownDirection"), section + ".ZDownDirection", 1);
                        for (int slot = 0; slot < 3; slot++)
                            directions[arm, slot] = ParseDirection(Get(values, section + ".ZDownDirectionN" + (slot + 1)),
                                section + ".ZDownDirectionN" + (slot + 1), armDir);
                    }
                    safeTrayMaxY[0] = limits[0];
                    safeTrayMaxY[1] = limits[1];
                    for (int arm = 0; arm < 2; arm++)
                        for (int slot = 0; slot < 3; slot++)
                            zDownDirection[arm, slot] = directions[arm, slot];
                    IsConfigured = true;
                    ConfigurationError = null;
                }
                catch (Exception ex)
                {
                    IsConfigured = false;
                    CalibrationConfirmed = false;
                    FlyCaptureEnabled = false;
                    FlyTwoSpeedEnabled = false;
                    ConfigurationError = "三吸嘴硬件配置无效：" + ex.Message;
                }
            }
        }

        /// <summary>
        /// 系统页“三吸嘴标定已确认”：写回 ini 的 [Machine] CalibrationConfirmed 并立即生效（不用重启）。
        /// 只改这一行，其它内容原样保留。自动运行中不允许修改。
        /// </summary>
        public static bool SetCalibrationConfirmed(bool confirmed, string operatorName, out string error)
        {
            error = null;
            if (SafetyDebugMode.AutoRunActive) { error = "自动运行中不能修改三吸嘴标定确认，请先停止运行"; return false; }
            lock (sync)
            {
                Initialize();
                try
                {
                    string path = ConfigurationPath;
                    List<string> lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                    string value = "CalibrationConfirmed=" + (confirmed ? "true" : "false");
                    int header = lines.FindIndex(l => l.Trim().Equals("[Machine]", StringComparison.OrdinalIgnoreCase));
                    if (header < 0)
                    {
                        lines.Insert(0, "[Machine]");
                        lines.Insert(1, value);
                        lines.Insert(2, "");
                    }
                    else
                    {
                        int end = lines.FindIndex(header + 1, l => l.Trim().StartsWith("["));
                        if (end < 0) end = lines.Count;
                        int key = -1;
                        for (int i = header + 1; i < end; i++)
                        {
                            string t = lines[i].Trim();
                            int eq = t.IndexOf('=');
                            if (eq > 0 && t.Substring(0, eq).Trim().Equals("CalibrationConfirmed", StringComparison.OrdinalIgnoreCase)) { key = i; break; }
                        }
                        if (key >= 0) lines[key] = value;
                        else lines.Insert(header + 1, value);
                    }
                    File.WriteAllLines(path, lines, new UTF8Encoding(true));
                    CalibrationConfirmed = confirmed;
                }
                catch (Exception ex)
                {
                    error = "写入 " + ConfigurationPath + " 失败：" + ex.Message;
                    return false;
                }
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("三吸嘴标定确认已{0}，操作员：{1}，已写入 ThreeNozzleHardware.ini",
                confirmed ? "勾选（允许自动运行）" : "取消（禁止自动运行）", string.IsNullOrEmpty(operatorName) ? "未知" : operatorName));
            return true;
        }

        /// <summary>
        /// 系统页“三吸嘴飞拍”：写回 ini 的 [Machine] FlyCapture 并立即生效。自动运行中不允许修改。
        /// </summary>
        public static bool SetFlyCapture(bool enabled, string operatorName, out string error)
        {
            error = null;
            if (SafetyDebugMode.AutoRunActive) { error = "自动运行中不能切换三吸嘴飞拍，请先停止运行"; return false; }
            lock (sync)
            {
                Initialize();
                if (!WriteMachineKey("FlyCapture", enabled ? "true" : "false", out error)) return false;
                FlyCaptureEnabled = enabled;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("三吸嘴飞拍已{0}，操作员：{1}，已写入 ThreeNozzleHardware.ini",
                enabled ? "开启" : "关闭（全部定拍）", string.IsNullOrEmpty(operatorName) ? "未知" : operatorName));
            return true;
        }

        /// <summary>只改 [Machine] 段里的一个键，其它内容原样保留。调用方持有 sync。</summary>
        private static bool WriteMachineKey(string keyName, string value, out string error)
        {
            return WriteMachineValues(new Dictionary<string, string> { { keyName, value } }, out error);
        }

        private static bool WriteMachineValues(Dictionary<string, string> values, out string error)
        {
            error = null;
            try
            {
                string path = ConfigurationPath;
                List<string> lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                int header = lines.FindIndex(l => l.Trim().Equals("[Machine]", StringComparison.OrdinalIgnoreCase));
                if (header < 0) { lines.Insert(0, "[Machine]"); lines.Insert(1, ""); header = 0; }
                foreach (var item in values)
                {
                    int end = lines.FindIndex(header + 1, l => l.Trim().StartsWith("["));
                    if (end < 0) end = lines.Count;
                    int key = -1;
                    for (int i = header + 1; i < end; i++)
                    {
                        string t = lines[i].Trim(); int eq = t.IndexOf('=');
                        if (eq > 0 && t.Substring(0, eq).Trim().Equals(item.Key, StringComparison.OrdinalIgnoreCase)) { key = i; break; }
                    }
                    if (key >= 0) lines[key] = item.Key + "=" + item.Value;
                    else lines.Insert(header + 1, item.Key + "=" + item.Value);
                }
                File.WriteAllLines(path, lines, new UTF8Encoding(true));
                return true;
            }
            catch (Exception ex) { error = "写入 " + ConfigurationPath + " 失败：" + ex.Message; return false; }
        }

        public static bool ValidateForAutomatic(out string error)
        {
            if (!IsConfigured) { error = ConfigurationError; return false; }
            if (NozzleZCalibrationMotion.IsBusy) { error = "标定动作正在执行"; return false; }
            if (!CalibrationConfirmed) { error = "三吸嘴的轴方向、原点、吸嘴偏移及取放位置尚未确认标定。"; return false; }
            for (int arm = 0; arm < 2; arm++)
            {
                double high = arm == 0 ? MT.AXIS_UDL1_Y.spd_work : MT.AXIS_UDL2_Y.spd_work;
                if (FlyTwoSpeedEnabled && (!Finite(high) || high <= 0 || flyLowSpeed[arm] > high))
                {
                    error = string.Format("模块{0}飞拍低速 {1:F1} 超过或无法匹配Y轴工作速度 {2:F1}，请检查飞拍两段速参数。", arm + 1, flyLowSpeed[arm], high);
                    return false;
                }
                if (double.IsNaN(SafeTrayMaxY(arm)))
                {
                    error = string.Format("三吸嘴臂{0}的料盘安全区域 SafeTrayMaxY 未配置，且无法自动计算（请先标定三个吸嘴的下相机拍照位）。", arm + 1);
                    return false;
                }
            }
            error = null;
            return true;
        }

        private static int ParseDirection(string text, string key, int fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            int value;
            if (!int.TryParse(text.Trim(), out value) || (value != 1 && value != -1))
                throw new InvalidDataException(key + " 只能是 1（向下坐标增大）或 -1（向下坐标减小）");
            return value;
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : "";
        }

        private static Dictionary<string, string> ReadIni(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int equals = line.IndexOf('=');
                if (section.Length == 0 || equals <= 0) throw new InvalidDataException("INI行格式错误：" + raw);
                string key = section + "." + line.Substring(0, equals).Trim();
                if (result.ContainsKey(key)) throw new InvalidDataException("INI键重复：" + key);
                result.Add(key, line.Substring(equals + 1).Trim());
            }
            return result;
        }
    }
}
