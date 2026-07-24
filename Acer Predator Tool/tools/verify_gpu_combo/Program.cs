using System.Runtime.Versioning;
using PredatorControlApp;

namespace PredatorControlApp.Tools;

/// <summary>
/// Step 3: PnP (Endurance/Standard) + DDS AppSync (Optimus/Auto/NVIDIA) combo.
/// Run elevated (admin) — PnP + SeDebug mem-scan.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Program
{
    private static int Main()
    {
        var gpu = new GpuControlService();
        int pass = 0;
        int total = 0;

        void Step(string name, Func<bool> action)
        {
            total++;
            Console.WriteLine();
            Console.WriteLine("==== " + name);
            try
            {
                bool ok = action();
                Console.WriteLine(ok ? "PASS" : "FAIL");
                if (ok) pass++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL EX " + ex.Message);
            }
        }

        void Dump(string tag)
        {
            var s = gpu.GetStatus();
            Console.WriteLine(tag + " " + s.Detail + " | " + s.MuxSignature);
        }

        Dump("START");

        // A) Ensure Hybrid baseline
        Step("Hybrid baseline", () =>
        {
            var s0 = gpu.GetStatus();
            if (s0.DeviceState == GpuDeviceState.Hybrid)
            {
                Console.WriteLine("  already Hybrid");
                return true;
            }

            var r = gpu.ApplyDevice(GpuDeviceState.Hybrid);
            Console.WriteLine("  " + r.Detail);
            Dump("  after");
            return r.After.DeviceState == GpuDeviceState.Hybrid;
        });

        // B) DDS cycle via product path (AppSync)
        foreach (var mode in new[]
                 {
                     GpuDisplayMode.Optimus,
                     GpuDisplayMode.Nvidia,
                     GpuDisplayMode.Auto,
                     GpuDisplayMode.Optimus
                 })
        {
            var target = mode;
            Step("DDS " + target, () =>
            {
                var r = gpu.ApplyDisplayMode(target);
                Console.WriteLine("  " + r.Detail);
                Dump("  after");
                return r.Ok && r.After.DisplayMode == target;
            });
        }

        // C) Endurance (iGPU Only) — DDS unavailable
        Step("Endurance iGPU Only", () =>
        {
            var r = gpu.ApplyDevice(GpuDeviceState.IgpuOnly);
            Console.WriteLine("  " + r.Detail);
            Dump("  after");
            return r.After.DeviceState == GpuDeviceState.IgpuOnly
                   && r.After.DisplayMode == GpuDisplayMode.Unknown;
        });

        // D) DISPLAY MODE while Endurance — auto Hybrid + DDS NVIDIA
        Step("Endurance -> NVIDIA (auto Hybrid+DDS)", () =>
        {
            var r = gpu.ApplyDisplayMode(GpuDisplayMode.Nvidia);
            Console.WriteLine("  " + r.Detail);
            Dump("  after");
            return r.Ok
                   && r.After.DeviceState == GpuDeviceState.Hybrid
                   && r.After.DisplayMode == GpuDisplayMode.Nvidia;
        });

        // E) Leave safe: Optimus + Hybrid
        Step("Leave Optimus", () =>
        {
            var r = gpu.ApplyDisplayMode(GpuDisplayMode.Optimus);
            Console.WriteLine("  " + r.Detail);
            Dump("  after");
            return r.Ok && r.After.DisplayMode == GpuDisplayMode.Optimus;
        });

        Console.WriteLine();
        Console.WriteLine("RESULT " + pass + "/" + total);
        Dump("END");
        return pass == total ? 0 : 3;
    }
}
