using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// Hypothesis: SyncProxy / BatchEngine activation registers SessionFilter class object.
/// </summary>
class SyncThenFilter
{
    static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
    static readonly Guid CLSID_SysFilter = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
    static readonly Guid CLSID_SyncProxy = new Guid("DCAB0989-1301-4319-BE5F-ADE89F88581C");
    static readonly Guid CLSID_Batch = new Guid("1DC715B2-9126-4671-8086-299A44543E0F");
    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
    static readonly Guid HANDLE_DDS = new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");
    const ushort SID = 0x7d;

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr p, uint f);

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    static extern int CoGetClassObject(ref Guid clsid, uint ctx, IntPtr server, ref Guid iid, out IntPtr ppv);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibrary(string p);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn2(IntPtr self, IntPtr a, IntPtr b);

    static IntPtr Vt(IntPtr iface, int slot)
    {
        return Marshal.ReadIntPtr(Marshal.ReadIntPtr(iface), slot * IntPtr.Size);
    }

    static void Ace(string t)
    {
        using (var k = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
            Console.WriteLine(t + " state=" + k.GetValue("InternalMuxState")
                + " auto=" + k.GetValue("InternalMuxIsAutomaticMode")
                + " i2d=" + k.GetValue("ACESwitchedI2D"));
    }

    static IntPtr TryCci(Guid clsid, uint ctx, string tag)
    {
        Guid iid = IID_IUnknown;
        IntPtr p;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ctx, ref iid, out p);
        if (hr == 0 || ctx == 0x402 || ctx == 0x4 || ctx == 0x5 || ctx == 0x17)
            Console.WriteLine("CCI " + tag + " ctx=0x" + ctx.ToString("x") + " HR=0x" + hr.ToString("X8")
                + (hr == 0 ? " p=" + p.ToString("X") : ""));
        return hr == 0 ? p : IntPtr.Zero;
    }

    static void SweepFilter(string phase)
    {
        Console.WriteLine("--- filter sweep " + phase + " ---");
        uint[] ctxs = { 0x1, 0x2, 0x3, 0x4, 0x5, 0x7, 0x15, 0x17, 0x401, 0x402, 0x403, 0x407, 0x417 };
        IntPtr found = IntPtr.Zero;
        foreach (uint ctx in ctxs)
        {
            if (found == IntPtr.Zero) found = TryCci(CLSID_AppFilter, ctx, "app");
            if (found == IntPtr.Zero) found = TryCci(CLSID_SysFilter, ctx, "sys");
        }
        if (found != IntPtr.Zero)
            Console.WriteLine("FOUND filter " + found.ToString("X"));
        else
            Console.WriteLine("no filter");
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "dgpu";
        CoInitializeEx(IntPtr.Zero, 2);

        foreach (var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App"))
            try { p.Kill(); } catch { }
        System.Threading.Thread.Sleep(500);

        LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
        LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");

        SweepFilter("cold");

        // Activate related servers first
        string[] progs = {
            "NvXDCore.SyncProxy",
            "NVXDBatdll.NvXDBatchEngine",
            "NVXDBatdll.NvXDSerializer",
            "NVXDBatdll.OperationInterceptor",
        };
        foreach (string prog in progs)
        {
            try
            {
                Type t = Type.GetTypeFromProgID(prog, false);
                if (t == null) { Console.WriteLine("no prog " + prog); continue; }
                object o = Activator.CreateInstance(t);
                Console.WriteLine("created " + prog);
                SweepFilter("after " + prog);

                if (prog == "NvXDCore.SyncProxy")
                {
                    IntPtr punk = Marshal.GetIUnknownForObject(o);
                    Guid iid = IID_IStateData;
                    IntPtr sd;
                    int hr = Marshal.QueryInterface(punk, ref iid, out sd);
                    Console.WriteLine("QI IStateData HR=0x" + hr.ToString("X8"));
                    SweepFilter("after QI IStateData");

                    // Keep sync alive and try GetClassObject on filter
                    Guid cfid = IID_IClassFactory;
                    Guid c = CLSID_AppFilter;
                    IntPtr cf;
                    foreach (uint ctx in new uint[] { 1, 2, 4, 5, 0x402, 0x15, 0x17 })
                    {
                        hr = CoGetClassObject(ref c, ctx, IntPtr.Zero, ref cfid, out cf);
                        if (hr == 0 || ctx == 0x402 || ctx == 4)
                            Console.WriteLine("GCO app ctx=0x" + ctx.ToString("x") + " HR=0x" + hr.ToString("X8"));
                    }
                    c = CLSID_SysFilter;
                    foreach (uint ctx in new uint[] { 1, 2, 4, 5, 0x402, 0x15, 0x17 })
                    {
                        hr = CoGetClassObject(ref c, ctx, IntPtr.Zero, ref cfid, out cf);
                        if (hr == 0 || ctx == 0x402 || ctx == 4)
                            Console.WriteLine("GCO sys ctx=0x" + ctx.ToString("x") + " HR=0x" + hr.ToString("X8"));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + prog + " " + ex.Message);
            }
        }

        // Also CoCreate SyncProxy/Batch by CLSID with various ctx
        TryCci(CLSID_SyncProxy, 0x15, "sync");
        TryCci(CLSID_Batch, 0x15, "batch");
        SweepFilter("after clsid create");

        Ace("done " + mode);
    }
}
