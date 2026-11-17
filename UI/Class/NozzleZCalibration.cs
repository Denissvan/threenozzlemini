using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace UI
{
    public sealed class NozzleZCalibrationData
    {
        public int Version = 1;
        public ArmData[] Arms = { new ArmData(), new ArmData() };
        public sealed class ArmData
        {
            public double? Baseline;
            public double Speed = 1, Step = 0.02, MaxSpread = 0.05;
            public NozzleData[] Nozzles = { new NozzleData(), new NozzleData(), new NozzleData() };
        }
        public sealed class NozzleData
        {
            public NozzleZCalibrationDraft.PointDraft Point = new NozzleZCalibrationDraft.PointDraft();
            public double[] Samples = new double[0];
            public int Direction;
            public string MeasuredAt = "";
            public double Mean { get { return Samples.Average(); } }
            public double Spread { get { return Samples.Max() - Samples.Min(); } }
        }
        public static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        public NozzleZCalibrationData Copy()
        {
            var serializer = new XmlSerializer(typeof(NozzleZCalibrationData));
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, this); stream.Position = 0;
                return (NozzleZCalibrationData)serializer.Deserialize(stream);
            }
        }
        public void ValidateStructure()
        {
            if (Version != 1 || Arms == null || Arms.Length != 2) throw new InvalidDataException("标定文件版本或机械臂数量无效");
            foreach (var arm in Arms)
            {
                if (arm == null || arm.Nozzles == null || arm.Nozzles.Length != 3) throw new InvalidDataException("吸嘴数量无效");
                if (arm.Baseline.HasValue && (!Finite(arm.Baseline.Value) || arm.Baseline <= 0)) throw new InvalidDataException("固定基准无效");
                if (!Finite(arm.Speed) || arm.Speed <= 0 || arm.Speed > 10 || !Finite(arm.Step) || arm.Step <= 0 || arm.Step > 0.1 ||
                    !Finite(arm.MaxSpread) || arm.MaxSpread <= 0 || arm.MaxSpread > 1) throw new InvalidDataException("测高参数超出允许范围");
                foreach (var nozzle in arm.Nozzles)
                    if (nozzle == null || nozzle.Point == null || nozzle.Samples == null) throw new InvalidDataException("吸嘴记录缺失");
            }
        }
        public void ValidateProbe(int armIndex, int nozzleIndex)
        {
            ValidateStructure();
            var p = Arms[armIndex].Nozzles[nozzleIndex].Point;
            var values = new[] { p.X, p.Y, p.U, p.Approach, p.End };
            if (values.Any(v => !v.HasValue || !Finite(v.Value))) throw new InvalidOperationException("请先学习/填写该嘴的XY、U、接近高度和探测终点");
            if (p.Approach < 0 || p.End <= p.Approach) throw new InvalidOperationException("探测终点必须大于接近高度（向下为正）");
        }
        public void ValidateArm(int armIndex)
        {
            ValidateStructure();
            var arm = Arms[armIndex];
            if (!arm.Baseline.HasValue) throw new InvalidOperationException("本臂未建立N1固定基准");
            for (int i = 0; i < 3; i++)
            {
                ValidateProbe(armIndex, i);
                var n = arm.Nozzles[i];
                if (n.Samples.Length != 3 || n.Samples.Any(v => !Finite(v) || v <= 0 || v <= n.Point.Approach || v > n.Point.End) ||
                    (n.Direction != 1 && n.Direction != -1) || string.IsNullOrEmpty(n.MeasuredAt) || n.Spread > arm.MaxSpread)
                    throw new InvalidOperationException("N" + (i + 1) + " 缺少合格的三次测高记录");
            }
        }
        public void Record(int armIndex, int nozzleIndex, double[] samples, int direction)
        {
            ValidateProbe(armIndex, nozzleIndex);
            var arm = Arms[armIndex];
            var p = arm.Nozzles[nozzleIndex].Point;
            if (samples == null || samples.Length != 3 || samples.Any(v => !Finite(v) || v <= p.Approach || v > p.End) ||
                (direction != 1 && direction != -1) || samples.Max() - samples.Min() > arm.MaxSpread)
                throw new InvalidOperationException("三次测高不完整或重复性超差，未更新标定结果");
            if (!arm.Baseline.HasValue && nozzleIndex != 0) throw new InvalidOperationException("首次请先测本臂N1建立基准");
            double mean = samples.Average();
            if (!arm.Baseline.HasValue) arm.Baseline = mean;
            var n = arm.Nozzles[nozzleIndex];
            n.Samples = (double[])samples.Clone(); n.Direction = direction;
            n.MeasuredAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        public double Offset(int arm, int nozzle) { return Arms[arm].Nozzles[nozzle].Mean - Arms[arm].Baseline.Value; }
        public static NozzleZCalibrationData Load(string path)
        {
            if (!File.Exists(path)) return new NozzleZCalibrationData();
            using (var stream = File.OpenRead(path))
            {
                var data = (NozzleZCalibrationData)new XmlSerializer(typeof(NozzleZCalibrationData)).Deserialize(stream);
                data.ValidateStructure();
                for (int i = 0; i < 2; i++) if (data.Arms[i].Baseline.HasValue) data.ValidateArm(i);
                return data;
            }
        }
        public void Save(string path)
        {
            ValidateStructure();
            for (int i = 0; i < 2; i++) if (Arms[i].Baseline.HasValue) ValidateArm(i);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp)) new XmlSerializer(typeof(NozzleZCalibrationData)).Serialize(stream, this);
                Load(temp);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public static class NozzleZCalibration
    {
        private static readonly object Sync = new object();
        private static NozzleZCalibrationData active;
        public static string ConfigurationPath { get { return Path.Combine(Path.GetFullPath(".."), "syscfg", "NozzleZCalibration.xml"); } }
        public static NozzleZCalibrationData GetSnapshot()
        {
            lock (Sync) { if (active == null) active = NozzleZCalibrationData.Load(ConfigurationPath); return active.Copy(); }
        }
        public static void SaveArm(NozzleZCalibrationData candidate, int arm)
        {
            candidate.ValidateArm(arm);
            lock (Sync)
            {
                var merged = GetSnapshot();
                // Re-measurement may never silently replace an established reference.
                if (merged.Arms[arm].Baseline.HasValue && candidate.Arms[arm].Baseline != merged.Arms[arm].Baseline)
                    throw new InvalidOperationException("固定基准已变化，请重新加载后标定");
                merged.Arms[arm] = candidate.Copy().Arms[arm];
                merged.Save(ConfigurationPath); active = merged;
            }
        }
        private static void ValidateZArgs(int arm, int nozzle, double z, int direction)
        {
            if (arm < 0 || arm > 1 || nozzle < 0 || nozzle > 2)
                throw new InvalidOperationException("机械臂或吸嘴编号无效");
            if (!NozzleZCalibrationData.Finite(z)) throw new InvalidOperationException("Z轴坐标无效");
            if (direction != 1 && direction != -1) throw new InvalidOperationException("Z下降方向配置无效");
        }

        /// <summary>
        /// 工艺 Z（按本臂 N1 示教的实际轴坐标）→ 本吸嘴实际轴目标。
        /// 加上逐嘴 Z 补偿（NozzleZCompensation，相对 N1，向下为正）：目标 = 工艺Z + 下降方向 × 补偿。
        /// 工艺 Z 为 0（抬起/安全位）不补偿；补偿后越过 0 点视为参数错误，拒绝运动。
        /// </summary>
        public static double ToAxisZ(int arm, int nozzle, double processZ, int direction)
        {
            ValidateZArgs(arm, nozzle, processZ, direction);
            if (Math.Abs(processZ) < 1e-9) return 0;
            double comp = NozzleZCompensation.Get(arm, nozzle);
            if (comp == 0) return processZ;
            double axisZ = processZ + direction * comp;
            if (Math.Sign(axisZ) != Math.Sign(processZ))
                throw new InvalidOperationException(string.Format(
                    "吸嘴Z补偿后越过0点（臂{0} N{1} 工艺Z={2:F3} 补偿={3:F3}），请检查示教Z或补偿值", arm + 1, nozzle + 1, processZ, comp));
            return axisZ;
        }

        /// <summary>只校验、不补偿：用于界面上直接输入“实际轴坐标”的地方（如视觉对位页的轴Z）。</summary>
        public static double CheckAxisZ(int arm, int nozzle, double axisZ, int direction)
        {
            ValidateZArgs(arm, nozzle, axisZ, direction);
            return axisZ;
        }

        /// <summary>该臂是否已建立测高基准（是否做过并保存了 Z 测高）。</summary>
        public static bool IsArmCalibrated(int arm)
        {
            lock (Sync)
            {
                if (active == null) active = NozzleZCalibrationData.Load(ConfigurationPath);
                return arm >= 0 && arm < active.Arms.Length && active.Arms[arm].Baseline.HasValue;
            }
        }

        /// <summary>示教：本吸嘴实际轴坐标 → 工艺 Z（ToAxisZ 的反算，扣掉本嘴补偿）。</summary>
        public static double ToProcessZ(int arm, int nozzle, double axisZ, int direction)
        {
            ValidateZArgs(arm, nozzle, axisZ, direction);
            if (Math.Abs(axisZ) < 1e-9) return 0;
            return axisZ - direction * NozzleZCompensation.Get(arm, nozzle);
        }

        /// <summary>从实际目标沿下降反方向预留距离；调用方仍须检查方向和轴软限位。</summary>
        public static double RetractAxisZ(double axisZ, int direction, double clearance)
        {
            if (!NozzleZCalibrationData.Finite(axisZ) || !NozzleZCalibrationData.Finite(clearance) || clearance < 0)
                throw new InvalidOperationException("Z轴坐标或预留距离无效");
            if (direction != 1 && direction != -1) throw new InvalidOperationException("Z下降方向配置无效");
            return axisZ - direction * clearance;
        }
    }

    /// <summary>
    /// 三吸嘴逐嘴 Z 补偿（硬件属性，存在系统配置 syscfg\NozzleZCompensation.ini，换产品不变）。
    /// 单位 mm，相对本臂 N1，向下为正：正值 = 该嘴要比 N1 多下探；N1 恒为 0。
    /// 由产品页“组合测高”测出各嘴接触高度后确认保存；ToAxisZ 在工艺下降时加上，抬起回 0 不加。
    /// </summary>
    public static class NozzleZCompensation
    {
        /// <summary>允许保存的最大补偿（mm）；超过说明吸嘴装配高度差过大，应先机械调整。</summary>
        public const double MaxAbsMm = 3.0;
        private static readonly object Sync = new object();
        private static double[,] offsets;

        public static string FilePath { get { return Path.Combine(Path.GetFullPath(".."), "syscfg", "NozzleZCompensation.ini"); } }

        public static double Get(int arm, int nozzle)
        {
            if (arm < 0 || arm > 1 || nozzle < 0 || nozzle > 2) return 0;
            lock (Sync) { EnsureLoaded(); return offsets[arm, nozzle]; }
        }

        public static double[] GetArm(int arm)
        {
            var values = new double[3];
            for (int i = 0; i < 3; i++) values[i] = Get(arm, i);
            return values;
        }

        /// <summary>保存一臂三个吸嘴的补偿（values[0] 按 N1=0 处理）。</summary>
        public static void SaveArm(int arm, double[] values)
        {
            if (arm < 0 || arm > 1) throw new InvalidOperationException("机械臂编号无效");
            if (values == null || values.Length != 3) throw new InvalidOperationException("补偿数据须为 3 个吸嘴");
            for (int i = 1; i < 3; i++)
            {
                if (!NozzleZCalibrationData.Finite(values[i]) || Math.Abs(values[i]) > MaxAbsMm)
                    throw new InvalidOperationException(string.Format("N{0} 补偿 {1:F3}mm 超出允许范围 ±{2:F1}mm，请先机械调整吸嘴高度", i + 1, values[i], MaxAbsMm));
            }
            lock (Sync)
            {
                EnsureLoaded();
                double[,] next = (double[,])offsets.Clone();
                next[arm, 0] = 0;
                next[arm, 1] = Math.Round(values[1], 4);
                next[arm, 2] = Math.Round(values[2], 4);
                Write(next);
                offsets = next;
            }
        }

        private static void EnsureLoaded()
        {
            if (offsets != null) return;
            var loaded = new double[2, 3];
            try
            {
                if (File.Exists(FilePath))
                {
                    int arm = -1;
                    foreach (string raw in File.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith(";")) continue;
                        if (line.StartsWith("[Arm") && line.EndsWith("]"))
                        {
                            int n;
                            arm = int.TryParse(line.Substring(4, line.Length - 5), out n) ? n - 1 : -1;
                            continue;
                        }
                        int eq = line.IndexOf('=');
                        if (arm < 0 || arm > 1 || eq < 2 || line[0] != 'N') continue;
                        int nozzle;
                        double value;
                        if (!int.TryParse(line.Substring(1, eq - 1), out nozzle) || nozzle < 2 || nozzle > 3) continue;
                        if (!double.TryParse(line.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out value)) continue;
                        // 文件被手改成越界值时不采用（按 0），避免异常值直接用于下降
                        if (NozzleZCalibrationData.Finite(value) && Math.Abs(value) <= MaxAbsMm) loaded[arm, nozzle - 1] = value;
                    }
                }
            }
            catch { loaded = new double[2, 3]; }
            offsets = loaded;
        }

        private static void Write(double[,] values)
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("; 三吸嘴逐嘴Z补偿（mm，相对本臂N1，向下为正；正值=比N1多下探）。由产品页“组合测高”保存，请勿手改。");
            sb.AppendLine("; 保存时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            for (int arm = 0; arm < 2; arm++)
            {
                sb.AppendLine("[Arm" + (arm + 1) + "]");
                for (int n = 0; n < 3; n++)
                    sb.AppendLine("N" + (n + 1) + "=" + values[arm, n].ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            }
            string temp = path + ".tmp";
            File.WriteAllText(temp, sb.ToString(), System.Text.Encoding.UTF8);
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", true);
                File.Delete(path);
            }
            File.Move(temp, path);
        }
    }
}
