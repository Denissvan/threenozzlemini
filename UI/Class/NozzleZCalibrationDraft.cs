using System;
using System.IO;
using System.Xml.Serialization;

namespace UI
{
    // Review-stage data only. Automatic motion must never load this draft file.
    public sealed class NozzleZCalibrationDraft
    {
        public int Version = 1;
        public ArmDraft[] Arms = { new ArmDraft(), new ArmDraft() };

        public sealed class ArmDraft
        {
            public double Speed = 1, Step = 0.02, MaxSpread = 0.05;
            public bool SettingsSaved;
            public double? Baseline;
            public double?[] Contact = new double?[3];
            public string Updated = "";
            public PointDraft[] Points = { new PointDraft(), new PointDraft(), new PointDraft() };
        }

        public sealed class PointDraft
        {
            public double? X, Y, U, Approach, End;
            public PointDraft Copy() { return (PointDraft)MemberwiseClone(); }
        }

        private static bool Valid(double x) { return !double.IsNaN(x) && !double.IsInfinity(x) && x > 0; }

        public void Validate()
        {
            if (Version != 1 || Arms == null || Arms.Length != 2) throw new InvalidDataException("标定草稿版本或机械臂数量无效");
            foreach (ArmDraft arm in Arms)
            {
                if (arm == null || arm.Contact == null || arm.Contact.Length != 3) throw new InvalidDataException("吸嘴数量无效");
                if (arm.Points == null || arm.Points.Length != 3) throw new InvalidDataException("基准点数量无效");
                foreach (var point in arm.Points)
                {
                    if (point == null) throw new InvalidDataException("基准点无效");
                    foreach (var value in new[] { point.X, point.Y, point.U, point.Approach, point.End })
                        if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
                            throw new InvalidDataException("基准点须为有限数值");
                    if ((point.Approach.HasValue && point.Approach < 0) || (point.End.HasValue && point.End <= 0) ||
                        (point.Approach.HasValue && point.End.HasValue && point.End <= point.Approach))
                        throw new InvalidDataException("向下正值：接近高度须非负，探测终点须大于接近高度");
                }
                if (arm.Baseline.HasValue && !Valid(arm.Baseline.Value)) throw new InvalidDataException("基准值无效");
                foreach (double? value in arm.Contact)
                    if (value.HasValue != arm.Baseline.HasValue || (value.HasValue && !Valid(value.Value)))
                        throw new InvalidDataException("接触高度必须完整且为向下正值");
            }
        }

        public double Offset(int arm, int nozzle)
        {
            return Arms[arm].Contact[nozzle].Value - Arms[arm].Baseline.Value;
        }

        public NozzleZCalibrationDraft Copy()
        {
            var copy = new NozzleZCalibrationDraft();
            for (int i = 0; i < 2; i++)
                copy.Arms[i] = new ArmDraft { Baseline = Arms[i].Baseline, SettingsSaved = Arms[i].SettingsSaved, Speed = Arms[i].Speed, Step = Arms[i].Step, MaxSpread = Arms[i].MaxSpread,
                    Contact = (double?[])Arms[i].Contact.Clone(), Updated = Arms[i].Updated,
                    Points = new[] { Arms[i].Points[0].Copy(), Arms[i].Points[1].Copy(), Arms[i].Points[2].Copy() } };
            return copy;
        }

        public void Apply(int arm, double?[] measurements)
        {
            Validate();
            if (arm < 0 || arm > 1 || measurements == null || measurements.Length != 3) throw new ArgumentException("请选择机械臂及三个吸嘴");
            bool any = false;
            foreach (double? value in measurements)
            {
                if (!value.HasValue) continue;
                any = true;
                if (!Valid(value.Value)) throw new ArgumentException("接触高度须为有限的正数（向下为正）");
            }
            if (!any) throw new ArgumentException("请先录入本次测量值");
            var target = Arms[arm];
            if (!target.Baseline.HasValue)
                foreach (double? value in measurements)
                    if (!value.HasValue) throw new ArgumentException("首次建立基准需录入本臂三个嘴的接触高度");
            if (!target.Baseline.HasValue) target.Baseline = measurements[0];
            for (int i = 0; i < 3; i++) if (measurements[i].HasValue) target.Contact[i] = measurements[i];
            target.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static NozzleZCalibrationDraft Load(string path)
        {
            if (!File.Exists(path)) return new NozzleZCalibrationDraft();
            using (var reader = File.OpenRead(path))
            {
                var result = (NozzleZCalibrationDraft)new XmlSerializer(typeof(NozzleZCalibrationDraft)).Deserialize(reader);
                result.Validate();
                return result;
            }
        }

        public void Save(string path)
        {
            Validate();
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp)) new XmlSerializer(typeof(NozzleZCalibrationDraft)).Serialize(stream, this);
                Load(temp); // Verify the complete candidate before replacing the old draft.
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
