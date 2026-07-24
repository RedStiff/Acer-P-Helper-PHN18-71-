using System;
using System.Runtime.InteropServices;

class FilterQI
{
    static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr p, uint f);

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibrary(string p);

    [DllImport("kernel32.dll")]
    static extern IntPtr LoadLibraryA(string p);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int FnQI(IntPtr self, ref Guid iid, out IntPtr ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint FnRef(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn0(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn1(IntPtr self, IntPtr a);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn2(IntPtr self, IntPtr a, IntPtr b);

    static IntPtr s_vt, s_obj, s_inner;
    static FnQI s_qi; static FnRef s_add, s_rel; static int s_ref = 1;

    static int OuterQI(IntPtr self, ref Guid iid, out IntPtr ppv)
    {
        if (iid.Equals(IID_IUnknown)) { ppv = self; OuterAddRef(self); return 0; }
        if (s_inner != IntPtr.Zero)
        {
            // Non-delegating: call inner's QI. For aggregated objects, inner's first vt is often
            // the controlling outer for public QI. Use secondary vt at +1 if present.
            try
            {
                var qi = (FnQI)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(s_inner), 0), typeof(FnQI));
                return qi(s_inner, ref iid, out ppv);
            }
            catch { }
        }
        ppv = IntPtr.Zero;
        return unchecked((int)0x80004002);
    }
    static uint OuterAddRef(IntPtr self) { return (uint)System.Threading.Interlocked.Increment(ref s_ref); }
    static uint OuterRelease(IntPtr self) { return (uint)System.Threading.Interlocked.Decrement(ref s_ref); }

    static IntPtr MakeOuter()
    {
        s_qi = OuterQI; s_add = OuterAddRef; s_rel = OuterRelease;
        s_vt = Marshal.AllocHGlobal(3 * IntPtr.Size);
        Marshal.WriteIntPtr(s_vt, 0, Marshal.GetFunctionPointerForDelegate(s_qi));
        Marshal.WriteIntPtr(s_vt, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(s_add));
        Marshal.WriteIntPtr(s_vt, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(s_rel));
        s_obj = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(s_obj, s_vt);
        return s_obj;
    }

    static void DumpVt(IntPtr obj, string tag)
    {
        IntPtr vt = Marshal.ReadIntPtr(obj);
        Console.WriteLine(tag + " vt=" + vt.ToString("X"));
        for (int i = 0; i < 16; i++)
        {
            IntPtr fn = Marshal.ReadIntPtr(vt, i * IntPtr.Size);
            Console.WriteLine("  vt[" + i + "]=" + fn.ToString("X"));
        }
        // secondary vt pointer at +0x18 often
        IntPtr p2 = Marshal.ReadIntPtr(obj, 0x18);
        Console.WriteLine(tag + " +0x18=" + p2.ToString("X"));
        if (p2 != IntPtr.Zero)
        {
            try
            {
                for (int i = 0; i < 8; i++)
                    Console.WriteLine("  vt2[" + i + "]=" + Marshal.ReadIntPtr(p2, i * IntPtr.Size).ToString("X"));
            }
            catch { }
        }
    }

    static void Main()
    {
        CoInitializeEx(IntPtr.Zero, 2);
        LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");

        // A) our outer
        IntPtr outer = MakeOuter();
        Guid c = CLSID_AppFilter, iid = IID_IUnknown;
        IntPtr filter;
        int hr = CoCreateInstance(ref c, outer, 0x402, ref iid, out filter);
        Console.WriteLine("ourOuter CCI HR=0x" + hr.ToString("X8") + (hr == 0 ? " " + filter.ToString("X") : ""));
        if (hr == 0)
        {
            s_inner = filter;
            DumpVt(filter, "ourFilter");
            foreach (var g in new Guid[] {
                IID_IUnknown, IID_IStateData, CLSID_AppFilter,
                new Guid("A3116D99-0A9B-400D-851E-84B3E387DBCC"),
                new Guid("4473E3A7-C2AD-4075-A1F8-935A584740A9"),
                new Guid("8DEEF6BE-6810-4817-956C-C54AE0B0FFAC"),
                new Guid("0D0497A9-19AD-49B9-A68E-4B2684AAF26C"),
                new Guid("E1A87F97-EC2C-4F55-867A-A2781571DE8E"),
                new Guid("DC09760E-9FDA-454A-B9D2-7E663E58C39D"),
                new Guid("463FE815-7BC0-4463-9CE4-D8C8BD6EA257"),
                new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39"),
                new Guid("F3D8CCF6-ED3C-4A0F-97E5-77F56E30B5DB"),
            })
            {
                IntPtr q; Guid gg = g;
                hr = Marshal.QueryInterface(filter, ref gg, out q);
                Console.WriteLine("  QI " + g.ToString("D").Substring(0, 8) + " HR=0x" + hr.ToString("X8")
                    + (hr == 0 ? " " + q.ToString("X") : ""));
                if (hr == 0 && q != filter) Marshal.Release(q);
            }

            // Try calling vt slots 3..10 as Fn0/Fn1 cautiously with SEH via separate tries
            for (int slot = 3; slot <= 10; slot++)
            {
                try
                {
                    var f0 = (Fn0)Marshal.GetDelegateForFunctionPointer(
                        Marshal.ReadIntPtr(Marshal.ReadIntPtr(filter), slot * IntPtr.Size), typeof(Fn0));
                    hr = f0(filter);
                    Console.WriteLine("  call vt[" + slot + "]() HR=0x" + hr.ToString("X8"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  call vt[" + slot + "] EX " + ex.GetType().Name + " " + ex.Message);
                    break;
                }
            }
        }

        // B) SyncProxy as outer — only create + QI, NO GetSettings
        Console.WriteLine("--- sync outer ---");
        object sync = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
        IntPtr syncUnk = Marshal.GetIUnknownForObject(sync);
        hr = CoCreateInstance(ref c, syncUnk, 0x402, ref iid, out filter);
        Console.WriteLine("syncOuter CCI HR=0x" + hr.ToString("X8") + (hr == 0 ? " " + filter.ToString("X") : ""));
        if (hr == 0)
        {
            DumpVt(filter, "syncFilter");
            // Does SyncProxy now expose anything new?
            foreach (var g in new Guid[] {
                CLSID_AppFilter,
                new Guid("F3D8CCF6-ED3C-4A0F-97E5-77F56E30B5DB"),
                IID_IStateData,
            })
            {
                IntPtr q; Guid gg = g;
                int hr2 = Marshal.QueryInterface(syncUnk, ref gg, out q);
                Console.WriteLine("  sync QI " + g.ToString("D").Substring(0, 8) + " HR=0x" + hr2.ToString("X8"));
                if (hr2 == 0) Marshal.Release(q);
            }
        }
    }
}
