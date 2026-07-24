using System.Runtime.Versioning;

namespace PredatorControlApp.Tools;

/// <summary>
/// Headless verification of DdsControl DISPLAY MODE (native hidden host).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Program
{
    private static int Main()
    {
        var dds = new PredatorControlApp.DdsControl();
        if (!dds.EnsureUxdHealthy(TimeSpan.FromSeconds(20), out string uxd))
        {
            Console.WriteLine("UXD FAIL " + uxd);
            return 2;
        }

        Console.WriteLine("UXD " + uxd);
        Console.WriteLine("ACE " + PredatorControlApp.GpuAceReader.Read().Summary);

        var modes = new[]
        {
            PredatorControlApp.GpuDisplayMode.Nvidia,
            PredatorControlApp.GpuDisplayMode.Optimus,
            PredatorControlApp.GpuDisplayMode.Auto,
            PredatorControlApp.GpuDisplayMode.Nvidia,
            PredatorControlApp.GpuDisplayMode.Auto,
            PredatorControlApp.GpuDisplayMode.Optimus
        };

        int okCount = 0;
        foreach (var mode in modes)
        {
            Console.WriteLine("==== " + mode);
            var r = dds.SetDisplayMode(mode);
            var inferred = InferFromAce(r.AceAfter);
            Console.WriteLine("  ok=" + r.Ok + " inferred=" + inferred);
            Console.WriteLine("  " + r.Detail);
            if (r.Ok && inferred == mode)
                okCount++;
            Thread.Sleep(400);
        }

        Console.WriteLine("RESULT " + okCount + "/" + modes.Length);
        return okCount == modes.Length ? 0 : 3;
    }

    private static PredatorControlApp.GpuDisplayMode InferFromAce(PredatorControlApp.GpuAceSnapshot ace)
    {
        if (!ace.Ok) return PredatorControlApp.GpuDisplayMode.Unknown;
        if (ace.InternalMuxState == 2) return PredatorControlApp.GpuDisplayMode.Nvidia;
        if (ace.InternalMuxState == 1)
            return ace.InternalMuxIsAutomaticMode != 0
                ? PredatorControlApp.GpuDisplayMode.Auto
                : PredatorControlApp.GpuDisplayMode.Optimus;
        return PredatorControlApp.GpuDisplayMode.Unknown;
    }
}
