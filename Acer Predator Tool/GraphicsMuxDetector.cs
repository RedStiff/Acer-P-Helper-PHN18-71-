using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Detects which GPU owns the primary laptop panel (DDS / Advanced Optimus result).
    /// Switching is performed by <see cref="GpuControlService"/> (PnP + NVIDIA App SetDDSState), not Acer WMI.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class GraphicsMuxDetector
    {
        public enum PanelOwner
        {
            Unknown = -1,
            /// <summary>Primary desktop on Intel (Optimus / Automatic idle).</summary>
            Integrated = WmiController.GraphicsModeIntegrated,
            /// <summary>Not distinguishable from Integrated while idle — reserved.</summary>
            Auto = WmiController.GraphicsModeAuto,
            /// <summary>Primary desktop on NVIDIA (NVIDIA GPU only / DDS discrete).</summary>
            Discrete = WmiController.GraphicsModeDiscrete
        }

        private static readonly object Sync = new();
        private static PanelOwner _cachedOwner = PanelOwner.Unknown;
        private static long _cachedAtMs;
        private const int CacheTtlMs = 2000;

        private const int DisplayDeviceActive = 0x00000001;
        private const int DisplayDevicePrimaryDevice = 0x00000004;
        private const int DisplayDeviceMirroringDriver = 0x00000008;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DisplayDevice
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(
            string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

        public static void InvalidateCache()
        {
            lock (Sync)
            {
                _cachedAtMs = 0;
                _cachedOwner = PanelOwner.Unknown;
            }
        }

        public static PanelOwner DetectPanelOwner()
        {
            long now = Environment.TickCount64;
            lock (Sync)
            {
                if (_cachedAtMs != 0 && (now - _cachedAtMs) < CacheTtlMs)
                    return _cachedOwner;
            }

            PanelOwner owner = DetectPanelOwnerCore();
            lock (Sync)
            {
                _cachedOwner = owner;
                _cachedAtMs = now;
            }
            return owner;
        }

        private static PanelOwner DetectPanelOwnerCore()
        {
            string? ownerKind = null;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var adapter = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
                    if (!EnumDisplayDevices(null, i, ref adapter, 0))
                        break;

                    if ((adapter.StateFlags & DisplayDeviceMirroringDriver) != 0)
                        continue;

                    bool primary = (adapter.StateFlags & DisplayDevicePrimaryDevice) != 0;
                    bool active = (adapter.StateFlags & DisplayDeviceActive) != 0;
                    if (!primary && ownerKind != null)
                        continue;
                    if (!primary && !active)
                        continue;

                    string kind = Classify(adapter.DeviceString);
                    if (primary)
                    {
                        ownerKind = kind;
                        break;
                    }

                    ownerKind ??= kind;
                }
            }
            catch
            {
                ownerKind = null;
            }

            return ownerKind switch
            {
                "NVIDIA" => PanelOwner.Discrete,
                "Intel" => PanelOwner.Integrated,
                _ => PanelOwner.Unknown
            };
        }

        /// <summary>
        /// Maps detected panel owner to UI indicator mode.
        /// Automatic vs Optimus cannot be told apart while the panel is on iGPU.
        /// </summary>
        public static byte? ToIndicatorMode(PanelOwner owner) => owner switch
        {
            PanelOwner.Integrated => WmiController.GraphicsModeIntegrated,
            PanelOwner.Discrete => WmiController.GraphicsModeDiscrete,
            _ => null
        };

        private static string Classify(string? deviceString)
        {
            if (string.IsNullOrEmpty(deviceString))
                return "Other";
            string u = deviceString.ToUpperInvariant();
            if (u.Contains("NVIDIA") || u.Contains("GEFORCE") || u.Contains("RTX") || u.Contains("GTX"))
                return "NVIDIA";
            if (u.Contains("INTEL") || u.Contains("UHD") || u.Contains("IRIS") || u.Contains("ARC"))
                return "Intel";
            if (u.Contains("AMD") || u.Contains("RADEON"))
                return "AMD";
            return "Other";
        }
    }
}
