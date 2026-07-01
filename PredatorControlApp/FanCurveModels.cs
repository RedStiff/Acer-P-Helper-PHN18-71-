using System.Text.Json;
using System.Text.Json.Serialization;

namespace PredatorControlApp
{
    public class FanCurvePoint
    {
        public int Temperature { get; set; }
        public int FanPercent { get; set; }

        public FanCurvePoint Clone() => new() { Temperature = Temperature, FanPercent = FanPercent };
    }

    public class FanCurveConfig
    {
        public const int MinPoints = 3;
        public const int MaxPoints = 16;
        public const int MinPointSpacing = 3;

        public bool SlopeMode { get; set; } = true;
        public int DeltaTemperature { get; set; } = 1;
        public int MaxTemperature { get; set; } = 110;
        public List<FanCurvePoint> Points { get; set; } = CreateDefaultPoints();

        public const int DefaultPointCount = 12;

        public static List<FanCurvePoint> CreateDefaultPoints() =>
        [
            new() { Temperature = 0, FanPercent = 0 },
            new() { Temperature = 25, FanPercent = 20 },
            new() { Temperature = 35, FanPercent = 25 },
            new() { Temperature = 45, FanPercent = 35 },
            new() { Temperature = 55, FanPercent = 45 },
            new() { Temperature = 65, FanPercent = 55 },
            new() { Temperature = 75, FanPercent = 68 },
            new() { Temperature = 82, FanPercent = 78 },
            new() { Temperature = 88, FanPercent = 88 },
            new() { Temperature = 93, FanPercent = 95 },
            new() { Temperature = 100, FanPercent = 100 },
            new() { Temperature = 110, FanPercent = 100 },
        ];

        public FanCurveConfig Clone()
        {
            return new FanCurveConfig
            {
                SlopeMode = SlopeMode,
                DeltaTemperature = DeltaTemperature,
                MaxTemperature = MaxTemperature,
                Points = Points.Select(p => p.Clone()).ToList()
            };
        }

        public void EnsureSorted()
        {
            if (MaxTemperature < 100) MaxTemperature = 110;

            Points = Points.OrderBy(p => p.Temperature).ToList();
            if (Points.Count < MinPoints)
                Points = CreateDefaultPoints();
            if (Points.Count > MaxPoints)
                Points = Points.Take(MaxPoints).ToList();

            Points[0].Temperature = 0;
            Points[^1].Temperature = MaxTemperature;
            DeltaTemperature = Math.Clamp(DeltaTemperature, 1, 10);
        }

        public void UpgradeLegacyPoints()
        {
            if (Points.Count < DefaultPointCount)
                Points = CreateDefaultPoints();
            EnsureSorted();
        }

        public bool TryAddPoint(int temperature, int fanPercent)
        {
            if (Points.Count >= MaxPoints) return false;

            temperature = Math.Clamp(temperature, MinPointSpacing, MaxTemperature - MinPointSpacing);
            fanPercent = Math.Clamp(fanPercent, 0, 100);

            foreach (var p in Points)
            {
                if (Math.Abs(p.Temperature - temperature) < MinPointSpacing)
                    return false;
            }

            Points.Add(new FanCurvePoint { Temperature = temperature, FanPercent = fanPercent });
            EnsureSorted();
            return true;
        }

        public bool TryRemovePoint(int index)
        {
            if (Points.Count <= MinPoints) return false;
            if (index <= 0 || index >= Points.Count - 1) return false;

            Points.RemoveAt(index);
            EnsureSorted();
            return true;
        }
    }

    public static class FanCurveEvaluator
    {
        public static int Evaluate(FanCurveConfig config, int temperature)
        {
            config.EnsureSorted();
            int maxTemp = config.MaxTemperature;
            temperature = Math.Clamp(temperature, 0, maxTemp);
            var points = config.Points;

            if (temperature <= points[0].Temperature)
                return Math.Clamp(points[0].FanPercent, 0, 100);

            if (temperature >= points[^1].Temperature)
                return Math.Clamp(points[^1].FanPercent, 0, 100);

            for (int i = 0; i < points.Count - 1; i++)
            {
                int t0 = points[i].Temperature;
                int t1 = points[i + 1].Temperature;
                if (temperature < t0 || temperature > t1) continue;

                int f0 = points[i].FanPercent;
                int f1 = points[i + 1].FanPercent;

                if (!config.SlopeMode || t1 == t0)
                    return Math.Clamp(f0, 0, 100);

                float ratio = (float)(temperature - t0) / (t1 - t0);
                return Math.Clamp((int)Math.Round(f0 + (f1 - f0) * ratio), 0, 100);
            }

            return Math.Clamp(points[^1].FanPercent, 0, 100);
        }

        public static bool ShouldUpdate(int temperature, int lastTemperature, int delta)
        {
            if (lastTemperature == int.MinValue) return true;
            return Math.Abs(temperature - lastTemperature) >= Math.Max(1, delta);
        }
    }

    public class FanCurveStore
    {
        private readonly string _savePath;

        public FanCurveConfig Cpu { get; private set; } = new();
        public FanCurveConfig Gpu { get; private set; } = new();

        public FanCurveStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PredatorControl");
            Directory.CreateDirectory(dir);
            _savePath = Path.Combine(dir, "fan_curves.json");
            Load();
        }

        public void SetCurves(FanCurveConfig cpu, FanCurveConfig gpu)
        {
            Cpu = cpu.Clone();
            Gpu = gpu.Clone();
            Save();
        }

        public void Save()
        {
            Cpu.EnsureSorted();
            Gpu.EnsureSorted();
            var data = new FanCurveStoreData { Cpu = Cpu, Gpu = Gpu };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(_savePath, json);
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_savePath)) return;
                var data = JsonSerializer.Deserialize<FanCurveStoreData>(File.ReadAllText(_savePath), JsonOptions);
                if (data?.Cpu != null) Cpu = data.Cpu;
                if (data?.Gpu != null) Gpu = data.Gpu;
                Cpu.UpgradeLegacyPoints();
                Gpu.UpgradeLegacyPoints();
            }
            catch { }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private class FanCurveStoreData
        {
            public FanCurveConfig? Cpu { get; set; }
            public FanCurveConfig? Gpu { get; set; }
        }
    }
}
