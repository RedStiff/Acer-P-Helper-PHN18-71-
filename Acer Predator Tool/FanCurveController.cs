using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class FanCurveController : IDisposable
    {
        public const byte FanModeAdvanced = 0x04;

        private const int TickIntervalMs = 750;

        private readonly WmiController _wmi;
        private readonly FanCurveStore _store;
        private readonly System.Windows.Forms.Timer _timer;

        private int _lastCpuTemp = int.MinValue;
        private int _lastGpuTemp = int.MinValue;
        private int _appliedCpuPercent = -1;
        private int _appliedGpuPercent = -1;
        private int _targetCpuPercent = -1;
        private int _targetGpuPercent = -1;
        private int _peakCpuRpm;
        private int _peakGpuRpm;

        public bool IsActive { get; private set; }
        public FanCurveStore Store => _store;
        public int PeakCpuRpm => _peakCpuRpm;
        public int PeakGpuRpm => _peakGpuRpm;

        public event EventHandler? ActiveProfileChanged;

        public FanCurveController(WmiController wmi)
        {
            _wmi = wmi;
            _store = new FanCurveStore();
            _timer = new System.Windows.Forms.Timer { Interval = TickIntervalMs };
            _timer.Tick += (_, _) => Tick();
        }

        public void Start()
        {
            if (!_wmi.SupportsCustomFanSpeed) return;

            IsActive = true;
            ResetRuntimeState();
            _wmi.SetFanBehavior(0x03);
            _timer.Start();
            Tick();
        }

        public void Stop()
        {
            IsActive = false;
            _timer.Stop();
        }

        public void SetPowerModeProfile(byte powerMode)
        {
            byte previous = _store.ActivePowerMode;
            _store.SetActivePowerMode(powerMode);
            if (previous == _store.ActivePowerMode) return;

            ResetRuntimeState();
            ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
            if (IsActive) Tick();
        }

        /// <summary>Live-apply edited curves to the active (or specified) profile without saving to disk.</summary>
        public void ApplyLive(FanCurveConfig cpu, FanCurveConfig gpu, byte? powerMode = null)
        {
            byte mode = powerMode ?? _store.ActivePowerMode;
            _store.ApplyLive(mode, cpu, gpu);
            ResetRuntimeState();
            if (IsActive && mode == _store.ActivePowerMode)
                Tick();
        }

        public void SaveProfile(FanCurveConfig cpu, FanCurveConfig gpu, byte? powerMode = null)
        {
            byte mode = powerMode ?? _store.ActivePowerMode;
            _store.SaveProfile(mode, cpu, gpu);
            ResetRuntimeState();
            if (IsActive && mode == _store.ActivePowerMode)
                Tick();
        }

        public void ReloadFromStore()
        {
            _store.Load();
            ResetRuntimeState();
            if (IsActive) Tick();
        }

        private void ResetRuntimeState()
        {
            _lastCpuTemp = int.MinValue;
            _lastGpuTemp = int.MinValue;
            _targetCpuPercent = -1;
            _targetGpuPercent = -1;
            // Keep applied percents so ramp continues from the last EC duty.
        }

        private void Tick()
        {
            if (!IsActive || !_wmi.SupportsCustomFanSpeed) return;

            int cpuTemp = _wmi.CpuTemp;
            int gpuTemp = _wmi.GpuTemp;
            int cpuRpm = _wmi.CpuFanRpm;
            int gpuRpm = _wmi.GpuFanRpm;
            if (cpuRpm > _peakCpuRpm) _peakCpuRpm = cpuRpm;
            if (gpuRpm > _peakGpuRpm) _peakGpuRpm = gpuRpm;

            UpdateTarget(FanKind.Cpu, cpuTemp, ref _lastCpuTemp, _store.Cpu, ref _targetCpuPercent);
            UpdateTarget(FanKind.Gpu, gpuTemp, ref _lastGpuTemp, _store.Gpu, ref _targetGpuPercent);

            ApplyRamp(FanKind.Cpu, ref _appliedCpuPercent, _targetCpuPercent);
            ApplyRamp(FanKind.Gpu, ref _appliedGpuPercent, _targetGpuPercent);
        }

        private static void UpdateTarget(
            FanKind kind,
            int temperature,
            ref int lastTemperature,
            FanCurveConfig config,
            ref int targetPercent)
        {
            if (temperature <= 0) return;
            if (!FanCurveEvaluator.ShouldUpdate(temperature, lastTemperature, config.DeltaTemperature)
                && targetPercent >= 0)
                return;

            targetPercent = FanCurveEvaluator.Evaluate(config, temperature);
            lastTemperature = temperature;
        }

        private void ApplyRamp(FanKind kind, ref int appliedPercent, int targetPercent)
        {
            if (targetPercent < 0) return;

            byte fanId = kind == FanKind.Cpu ? WmiController.FanIdCpu : WmiController.FanIdGpu;
            int next = appliedPercent < 0
                ? targetPercent
                : FanRpmMap.StepToward(appliedPercent, targetPercent);

            if (next == appliedPercent) return;

            if (_wmi.SetFanSpeed(fanId, (byte)next))
                appliedPercent = next;
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }
}
