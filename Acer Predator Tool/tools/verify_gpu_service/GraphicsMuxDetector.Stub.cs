using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>Minimal stub for verify_gpu_service — ACE-only DDS checks.</summary>
    [SupportedOSPlatform("windows")]
    internal static class GraphicsMuxDetector
    {
        public enum PanelOwner
        {
            Unknown = -1,
            Integrated = 0,
            Auto = 1,
            Discrete = 2
        }

        public static PanelOwner DetectPanelOwner() => PanelOwner.Unknown;

        public static void InvalidateCache() { }
    }
}
