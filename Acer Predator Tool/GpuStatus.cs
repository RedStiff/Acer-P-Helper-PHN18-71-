using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PredatorControlApp
{
    /// <summary>PnP layer: NVIDIA dGPU present vs disabled (PreySense Endurance/Standard).</summary>
    internal enum GpuDeviceState
    {
        Unknown = 0,
        /// <summary>NVIDIA display PnP disabled / absent — iGPU only.</summary>
        IgpuOnly = 1,
        /// <summary>NVIDIA display PnP enabled — hybrid available.</summary>
        Hybrid = 2
    }

    /// <summary>DDS / Advanced Optimus Display Mode.</summary>
    internal enum GpuDisplayMode
    {
        Unknown = 0,
        Optimus = 1,
        Auto = 2,
        Nvidia = 3
    }

    internal sealed class GpuAceSnapshot
    {
        public bool Ok { get; init; }
        public int InternalMuxState { get; init; }
        public int InternalMuxIsAutomaticMode { get; init; }
        public int AceSwitchedI2D { get; init; }

        public string Summary =>
            Ok
                ? $"state={InternalMuxState}|auto={InternalMuxIsAutomaticMode}|i2d={AceSwitchedI2D}"
                : "ace_unavailable";
    }

    internal sealed class GpuStatus
    {
        public GpuDeviceState DeviceState { get; init; }
        public GpuDisplayMode DisplayMode { get; init; }
        public GraphicsMuxDetector.PanelOwner PanelOwner { get; init; }
        public GpuAceSnapshot Ace { get; init; } = new();
        public string MuxSignature { get; init; } = "owner=?|nv_display=?";
        public string Detail { get; init; } = "";
    }

    [SupportedOSPlatform("windows")]
    internal static class GpuAceReader
    {
        public const string AcePath =
            @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE";

        public static GpuAceSnapshot Read()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(AcePath);
                if (key == null)
                    return new GpuAceSnapshot { Ok = false };

                return new GpuAceSnapshot
                {
                    Ok = true,
                    InternalMuxState = ReadDword(key, "InternalMuxState"),
                    InternalMuxIsAutomaticMode = ReadDword(key, "InternalMuxIsAutomaticMode"),
                    AceSwitchedI2D = ReadDword(key, "ACESwitchedI2D")
                };
            }
            catch
            {
                return new GpuAceSnapshot { Ok = false };
            }
        }

        public static GpuDisplayMode InferDisplayMode(GpuAceSnapshot ace, GraphicsMuxDetector.PanelOwner owner)
        {
            if (!ace.Ok)
            {
                return owner switch
                {
                    GraphicsMuxDetector.PanelOwner.Discrete => GpuDisplayMode.Nvidia,
                    GraphicsMuxDetector.PanelOwner.Integrated => GpuDisplayMode.Optimus,
                    _ => GpuDisplayMode.Unknown
                };
            }

            if (ace.InternalMuxState == 2)
                return GpuDisplayMode.Nvidia;

            if (ace.InternalMuxState == 1)
                return ace.InternalMuxIsAutomaticMode != 0 ? GpuDisplayMode.Auto : GpuDisplayMode.Optimus;

            return GpuDisplayMode.Unknown;
        }

        private static int ReadDword(RegistryKey key, string name)
        {
            object? value = key.GetValue(name);
            return value switch
            {
                int i => i,
                long l => (int)l,
                _ => 0
            };
        }
    }
}
