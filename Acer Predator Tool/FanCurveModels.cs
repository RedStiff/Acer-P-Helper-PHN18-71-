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

        // Fan % only on EC steps (0/10/…/100) — firmware does not honor 1% Custom duty.
        public static List<FanCurvePoint> CreateDefaultPoints() =>
        [
            new() { Temperature = 0, FanPercent = 0 },
            new() { Temperature = 25, FanPercent = 20 },
            new() { Temperature = 35, FanPercent = 30 },
            new() { Temperature = 45, FanPercent = 40 },
            new() { Temperature = 55, FanPercent = 50 },
            new() { Temperature = 65, FanPercent = 60 },
            new() { Temperature = 75, FanPercent = 70 },
            new() { Temperature = 82, FanPercent = 80 },
            new() { Temperature = 88, FanPercent = 90 },
            new() { Temperature = 93, FanPercent = 90 },
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

            foreach (var point in Points)
                point.FanPercent = FanRpmMap.SnapPercent(point.FanPercent);

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
            fanPercent = FanRpmMap.SnapPercent(fanPercent);

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

    public sealed class FanCurveProfile
    {
        public FanCurveConfig Cpu { get; set; } = new();
        public FanCurveConfig Gpu { get; set; } = new();

        public FanCurveProfile Clone() => new()
        {
            Cpu = Cpu.Clone(),
            Gpu = Gpu.Clone()
        };

        public void EnsureValid()
        {
            Cpu.UpgradeLegacyPoints();
            Gpu.UpgradeLegacyPoints();
        }
    }

    public static class FanCurveEvaluator
    {
        /// <summary>
        /// Logical curve value for UI (smooth slope / stair knees at control points).
        /// </summary>
        public static int EvaluateIdeal(FanCurveConfig config, int temperature)
        {
            config.EnsureSorted();
            int maxTemp = config.MaxTemperature;
            temperature = Math.Clamp(temperature, 0, maxTemp);
            var points = config.Points;

            if (temperature <= points[0].Temperature)
                return Math.Clamp(points[0].FanPercent, 0, 100);

            if (temperature >= points[^1].Temperature)
                return Math.Clamp(points[^1].FanPercent, 0, 100);

            if (!config.SlopeMode)
            {
                // Stair: each point is the knee where its fan % becomes active.
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    if (temperature >= points[i].Temperature)
                        return Math.Clamp(points[i].FanPercent, 0, 100);
                }
                return Math.Clamp(points[0].FanPercent, 0, 100);
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                int t0 = points[i].Temperature;
                int t1 = points[i + 1].Temperature;
                if (temperature < t0 || temperature > t1) continue;
                if (t1 == t0)
                    return Math.Clamp(points[i].FanPercent, 0, 100);

                int f0 = points[i].FanPercent;
                int f1 = points[i + 1].FanPercent;
                float ratio = (float)(temperature - t0) / (t1 - t0);
                return Math.Clamp((int)Math.Round(f0 + (f1 - f0) * ratio), 0, 100);
            }

            return Math.Clamp(points[^1].FanPercent, 0, 100);
        }

        /// <summary>Hardware target: ideal curve snapped to EC ~10% bands.</summary>
        public static int Evaluate(FanCurveConfig config, int temperature) =>
            FanRpmMap.SnapPercent(EvaluateIdeal(config, temperature));

        public static bool ShouldUpdate(int temperature, int lastTemperature, int delta)
        {
            if (lastTemperature == int.MinValue) return true;
            return Math.Abs(temperature - lastTemperature) >= Math.Max(1, delta);
        }

        public static int EstimateRpm(FanKind kind, int fanPercent) =>
            FanRpmMap.EstimateRpm(kind, fanPercent);
    }

    public class FanCurveStore
    {
        public static readonly byte[] PowerModes = [0x00, 0x01, 0x04, 0x05, 0x06];

        public static readonly (byte Mode, string Name)[] ProfileDescriptors =
        [
            (0x00, "Silent"),
            (0x01, "Balanced"),
            (0x04, "Perf"),
            (0x05, "Turbo"),
            (0x06, "Eco")
        ];

        private readonly string _savePath;
        private readonly Dictionary<byte, FanCurveProfile> _profiles = new();

        public byte ActivePowerMode { get; private set; } = 0x01;

        public FanCurveConfig Cpu => ActiveProfile.Cpu;
        public FanCurveConfig Gpu => ActiveProfile.Gpu;
        public FanCurveProfile ActiveProfile => GetProfile(ActivePowerMode);

        public FanCurveStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PredatorControl");
            Directory.CreateDirectory(dir);
            _savePath = Path.Combine(dir, "fan_curves.json");

            foreach (var (mode, _) in ProfileDescriptors)
                _profiles[mode] = new FanCurveProfile();

            Load();
        }

        public static string ProfileName(byte powerMode) =>
            ProfileDescriptors.FirstOrDefault(p => p.Mode == powerMode).Name ?? "Balanced";

        public FanCurveProfile GetProfile(byte powerMode)
        {
            powerMode = NormalizeMode(powerMode);
            if (!_profiles.TryGetValue(powerMode, out var profile))
            {
                profile = new FanCurveProfile();
                _profiles[powerMode] = profile;
            }
            return profile;
        }

        public void SetActivePowerMode(byte powerMode)
        {
            ActivePowerMode = NormalizeMode(powerMode);
        }

        /// <summary>Update profile in memory (live apply). Does not write disk.</summary>
        public void ApplyLive(byte powerMode, FanCurveConfig cpu, FanCurveConfig gpu)
        {
            powerMode = NormalizeMode(powerMode);
            var profile = new FanCurveProfile { Cpu = cpu.Clone(), Gpu = gpu.Clone() };
            profile.EnsureValid();
            _profiles[powerMode] = profile;
        }

        /// <summary>Persist profile to disk.</summary>
        public void SaveProfile(byte powerMode, FanCurveConfig cpu, FanCurveConfig gpu)
        {
            ApplyLive(powerMode, cpu, gpu);
            Save();
        }

        public void SetCurves(FanCurveConfig cpu, FanCurveConfig gpu)
        {
            SaveProfile(ActivePowerMode, cpu, gpu);
        }

        public void Save()
        {
            foreach (var profile in _profiles.Values)
                profile.EnsureValid();

            var data = new FanCurveStoreData
            {
                Version = 2,
                ActivePowerMode = ActivePowerMode,
                Profiles = ProfileDescriptors.Select(d => new FanCurveProfileData
                {
                    PowerMode = d.Mode,
                    Cpu = GetProfile(d.Mode).Cpu,
                    Gpu = GetProfile(d.Mode).Gpu
                }).ToList()
            };
            File.WriteAllText(_savePath, JsonSerializer.Serialize(data, JsonOptions));
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_savePath)) return;
                var data = JsonSerializer.Deserialize<FanCurveStoreData>(File.ReadAllText(_savePath), JsonOptions);
                if (data == null) return;

                if (data.Profiles is { Count: > 0 })
                {
                    foreach (var entry in data.Profiles)
                    {
                        byte mode = NormalizeMode((byte)entry.PowerMode);
                        var profile = new FanCurveProfile
                        {
                            Cpu = entry.Cpu ?? new FanCurveConfig(),
                            Gpu = entry.Gpu ?? new FanCurveConfig()
                        };
                        profile.EnsureValid();
                        _profiles[mode] = profile;
                    }
                }
                else if (data.Cpu != null || data.Gpu != null)
                {
                    // Legacy single-curve file → seed every power profile.
                    var legacy = new FanCurveProfile
                    {
                        Cpu = data.Cpu ?? new FanCurveConfig(),
                        Gpu = data.Gpu ?? new FanCurveConfig()
                    };
                    legacy.EnsureValid();
                    foreach (var (mode, _) in ProfileDescriptors)
                        _profiles[mode] = legacy.Clone();
                }

                if (data.ActivePowerMode is int modeValue)
                    ActivePowerMode = NormalizeMode((byte)modeValue);

                foreach (var (mode, _) in ProfileDescriptors)
                    GetProfile(mode).EnsureValid();
            }
            catch { }
        }

        private static byte NormalizeMode(byte powerMode) =>
            PowerModes.Contains(powerMode) ? powerMode : (byte)0x01;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private class FanCurveStoreData
        {
            public int Version { get; set; } = 1;
            public int? ActivePowerMode { get; set; }
            public List<FanCurveProfileData>? Profiles { get; set; }

            // Legacy v1 fields
            public FanCurveConfig? Cpu { get; set; }
            public FanCurveConfig? Gpu { get; set; }
        }

        private class FanCurveProfileData
        {
            public int PowerMode { get; set; }
            public FanCurveConfig? Cpu { get; set; }
            public FanCurveConfig? Gpu { get; set; }
        }
    }
}
