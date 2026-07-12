using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    internal sealed class HardwareClockReader : IDisposable
    {
        private const int NvmlSuccess = 0;
        private const int NvmlClockGraphics = 0;
        private const int MinActiveGpuClockMhz = 100;
        private const uint MinActiveGpuPowerMw = 1000;

        private PerformanceCounter? _cpuFrequencyCounter;
        private PerformanceCounter? _cpuPerformanceCounter;
        private readonly int _cpuMaxMhz;
        private bool _cpuFrequencyPrimed;

        private IntPtr _nvmlModule;
        private NvmlInitDelegate? _nvmlInit;
        private NvmlShutdownDelegate? _nvmlShutdown;
        private NvmlDeviceGetCountDelegate? _nvmlDeviceGetCount;
        private NvmlDeviceGetHandleByIndexDelegate? _nvmlDeviceGetHandleByIndex;
        private NvmlDeviceGetNameDelegate? _nvmlDeviceGetName;
        private NvmlDeviceGetClockInfoDelegate? _nvmlDeviceGetClockInfo;
        private NvmlDeviceGetPowerUsageDelegate? _nvmlDeviceGetPowerUsage;
        private IntPtr _discreteGpuHandle;
        private bool _nvmlReady;

        public HardwareClockReader()
        {
            _cpuMaxMhz = QueryCpuMaxMhz();
            TryCreateCpuCounters();
            TryInitNvml();
        }

        public int? GetCpuFrequencyMhz()
        {
            if (_cpuFrequencyCounter != null)
            {
                try
                {
                    if (!_cpuFrequencyPrimed)
                    {
                        _ = _cpuFrequencyCounter.NextValue();
                        _cpuFrequencyPrimed = true;
                    }

                    int mhz = (int)Math.Round(_cpuFrequencyCounter.NextValue());
                    if (mhz > 0) return mhz;
                }
                catch { }
            }

            if (_cpuPerformanceCounter != null && _cpuMaxMhz > 0)
            {
                try
                {
                    float perf = _cpuPerformanceCounter.NextValue();
                    int mhz = (int)Math.Round(_cpuMaxMhz * perf / 100f);
                    if (mhz > 0) return mhz;
                }
                catch { }
            }

            return null;
        }

        public int? GetDiscreteGpuFrequencyMhz(bool readingAllowed, bool requirePoweredState)
        {
            if (!readingAllowed || !_nvmlReady || _discreteGpuHandle == IntPtr.Zero)
                return null;

            if (_nvmlDeviceGetClockInfo == null)
                return null;

            int result = _nvmlDeviceGetClockInfo(_discreteGpuHandle, NvmlClockGraphics, out uint clockMhz);
            if (result != NvmlSuccess || clockMhz < MinActiveGpuClockMhz)
                return null;

            if (requirePoweredState && !IsDiscreteGpuPowered())
                return null;

            return (int)clockMhz;
        }

        private bool IsDiscreteGpuPowered()
        {
            if (_nvmlDeviceGetPowerUsage == null)
                return true;

            int result = _nvmlDeviceGetPowerUsage(_discreteGpuHandle, out uint powerMw);
            if (result != NvmlSuccess)
                return true;

            return powerMw >= MinActiveGpuPowerMw;
        }

        private void TryCreateCpuCounters()
        {
            try
            {
                _cpuFrequencyCounter = new PerformanceCounter(
                    "Processor Information", "Processor Frequency", "_Total");
                _ = _cpuFrequencyCounter.NextValue();
                _cpuFrequencyPrimed = true;
                return;
            }
            catch { }

            try
            {
                _cpuPerformanceCounter = new PerformanceCounter(
                    "Processor Information", "% Processor Performance", "_Total");
                _ = _cpuPerformanceCounter.NextValue();
            }
            catch { }
        }

        private static int QueryCpuMaxMhz()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    int mhz = Convert.ToInt32(obj["MaxClockSpeed"]);
                    if (mhz > 0) return mhz;
                }
            }
            catch { }

            return 0;
        }

        private void TryInitNvml()
        {
            string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            foreach (string dllName in new[] { "nvml.dll", "nvidia-ml.dll" })
            {
                string path = Path.Combine(systemDir, dllName);
                if (!File.Exists(path))
                    continue;

                try
                {
                    _nvmlModule = NativeLibrary.Load(path);
                    break;
                }
                catch { }
            }

            if (_nvmlModule == IntPtr.Zero)
                return;

            try
            {
                _nvmlInit = GetNvmlDelegate<NvmlInitDelegate>("nvmlInit");
                _nvmlShutdown = GetNvmlDelegate<NvmlShutdownDelegate>("nvmlShutdown");
                _nvmlDeviceGetCount = GetNvmlDelegate<NvmlDeviceGetCountDelegate>("nvmlDeviceGetCount");
                _nvmlDeviceGetHandleByIndex = GetNvmlDelegate<NvmlDeviceGetHandleByIndexDelegate>("nvmlDeviceGetHandleByIndex");
                _nvmlDeviceGetName = GetNvmlDelegate<NvmlDeviceGetNameDelegate>("nvmlDeviceGetName");
                _nvmlDeviceGetClockInfo = GetNvmlDelegate<NvmlDeviceGetClockInfoDelegate>("nvmlDeviceGetClockInfo");
                _nvmlDeviceGetPowerUsage = GetNvmlDelegate<NvmlDeviceGetPowerUsageDelegate>("nvmlDeviceGetPowerUsage");

                if (_nvmlInit == null || _nvmlDeviceGetCount == null || _nvmlDeviceGetHandleByIndex == null ||
                    _nvmlDeviceGetName == null || _nvmlDeviceGetClockInfo == null)
                    return;

                if (_nvmlInit() != NvmlSuccess)
                    return;

                _discreteGpuHandle = FindDiscreteGpuHandle();
                _nvmlReady = _discreteGpuHandle != IntPtr.Zero;
            }
            catch
            {
                ReleaseNvml();
            }
        }

        private T? GetNvmlDelegate<T>(string exportName) where T : Delegate
        {
            if (_nvmlModule == IntPtr.Zero)
                return null;

            try
            {
                IntPtr proc = NativeLibrary.GetExport(_nvmlModule, exportName);
                return Marshal.GetDelegateForFunctionPointer<T>(proc);
            }
            catch
            {
                return null;
            }
        }

        private IntPtr FindDiscreteGpuHandle()
        {
            if (_nvmlDeviceGetCount == null || _nvmlDeviceGetHandleByIndex == null || _nvmlDeviceGetName == null)
                return IntPtr.Zero;

            if (_nvmlDeviceGetCount(out uint count) != NvmlSuccess || count == 0)
                return IntPtr.Zero;

            IntPtr fallback = IntPtr.Zero;
            var nameBuffer = new byte[64];

            for (uint i = 0; i < count; i++)
            {
                if (_nvmlDeviceGetHandleByIndex(i, out IntPtr device) != NvmlSuccess)
                    continue;

                Array.Clear(nameBuffer);
                if (_nvmlDeviceGetName(device, nameBuffer, (uint)nameBuffer.Length) != NvmlSuccess)
                    continue;

                string name = Encoding.ASCII.GetString(nameBuffer).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Quadro", StringComparison.OrdinalIgnoreCase))
                    return device;

                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    fallback = device;
            }

            return fallback;
        }

        private void ReleaseNvml()
        {
            if (_nvmlReady)
            {
                try { _nvmlShutdown?.Invoke(); } catch { }
                _nvmlReady = false;
            }

            _discreteGpuHandle = IntPtr.Zero;

            if (_nvmlModule != IntPtr.Zero)
            {
                try { NativeLibrary.Free(_nvmlModule); } catch { }
                _nvmlModule = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            ReleaseNvml();
            _cpuFrequencyCounter?.Dispose();
            _cpuPerformanceCounter?.Dispose();
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlInitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlShutdownDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetCountDelegate(out uint deviceCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetHandleByIndexDelegate(uint index, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetNameDelegate(IntPtr device, byte[] name, uint length);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetClockInfoDelegate(IntPtr device, int type, out uint clock);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetPowerUsageDelegate(IntPtr device, out uint powerMw);
    }
}
