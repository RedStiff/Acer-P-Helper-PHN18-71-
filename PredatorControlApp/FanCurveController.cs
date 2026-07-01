using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class FanCurveController : IDisposable
    {
        public const byte FanModeAdvanced = 0x04;

        private readonly WmiController _wmi;
        private readonly FanCurveStore _store;
        private readonly System.Windows.Forms.Timer _timer;

        private int _lastCpuTemp = int.MinValue;
        private int _lastGpuTemp = int.MinValue;
        private int _lastCpuPercent = -1;
        private int _lastGpuPercent = -1;

        public bool IsActive { get; private set; }
        public FanCurveStore Store => _store;

        public FanCurveController(WmiController wmi)
        {
            _wmi = wmi;
            _store = new FanCurveStore();
            _timer = new System.Windows.Forms.Timer { Interval = 1500 };
            _timer.Tick += (_, _) => Tick();
        }

        public void Start()
        {
            if (!_wmi.SupportsCustomFanSpeed) return;

            IsActive = true;
            _lastCpuTemp = int.MinValue;
            _lastGpuTemp = int.MinValue;
            _lastCpuPercent = -1;
            _lastGpuPercent = -1;
            _wmi.SetFanBehavior(0x03);
            _timer.Start();
            Tick();
        }

        public void Stop()
        {
            IsActive = false;
            _timer.Stop();
        }

        public void ReloadFromStore()
        {
            _store.Load();
            _lastCpuTemp = int.MinValue;
            _lastGpuTemp = int.MinValue;
            _lastCpuPercent = -1;
            _lastGpuPercent = -1;
            if (IsActive) Tick();
        }

        private void Tick()
        {
            if (!IsActive || !_wmi.SupportsCustomFanSpeed) return;

            int cpuTemp = _wmi.CpuTemp;
            int gpuTemp = _wmi.GpuTemp;

            if (cpuTemp > 0 && FanCurveEvaluator.ShouldUpdate(cpuTemp, _lastCpuTemp, _store.Cpu.DeltaTemperature))
            {
                int cpuPercent = FanCurveEvaluator.Evaluate(_store.Cpu, cpuTemp);
                if (cpuPercent != _lastCpuPercent)
                {
                    _wmi.SetFanSpeed(WmiController.FanIdCpu, (byte)cpuPercent);
                    _lastCpuPercent = cpuPercent;
                }
                _lastCpuTemp = cpuTemp;
            }

            if (gpuTemp > 0 && FanCurveEvaluator.ShouldUpdate(gpuTemp, _lastGpuTemp, _store.Gpu.DeltaTemperature))
            {
                int gpuPercent = FanCurveEvaluator.Evaluate(_store.Gpu, gpuTemp);
                if (gpuPercent != _lastGpuPercent)
                {
                    _wmi.SetFanSpeed(WmiController.FanIdGpu, (byte)gpuPercent);
                    _lastGpuPercent = gpuPercent;
                }
                _lastGpuTemp = gpuTemp;
            }
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }
}
