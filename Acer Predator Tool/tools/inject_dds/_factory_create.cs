using System;
using System.Runtime.InteropServices;

/// <summary>
/// CoGetClassObject for SessionFilter succeeds; probe CreateInstance variants.
/// </summary>
class FactoryCreate
{
    static readonly Guid CLSID_App = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
    static readonly Guid CLSID_Sys = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
    static readonly Guid IID_IClassFactory2 = new Guid("B196B28F-BAB4-101A-B69C-00AA00341D07");
    static readonly Guid IID_IPersist = new Guid("0000010C-0000-0000-C000-000000000046");
    static readonly Guid IID_IPersistStream = new Guid("00000109-0000-0000-C000-000000000046");
    static readonly Guid IID_IPersistStorage = new Guid("0000010A-0000-0000-C000-000000000046");
    static readonly Guid IID_IOleObject = new Guid("00000112-0000-0000-C000-000000000046");
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    static readonly Guid IID_IMarshal = new Guid("00000003-0000-0000-C000-000000000046");
    static readonly Guid IID_IStdMarshalInfo = new Guid("00000018-0000-0000-C000-000000000046");
    static readonly Guid IID_IPSFactoryBuffer = new Guid("D5F569D0-593B-101A-B569-08002B2DBF7A");

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr p, uint f);

    [DllImport("ole32.dll")]
    static extern int CoGetClassObject(ref Guid clsid, uint ctx, IntPtr server, ref Guid iid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    static extern int OleInitialize(IntPtr p);

    [DllImport("ole32.dll")]
    static extern int OleCreate(ref Guid clsid, ref Guid iid, uint render, IntPtr formatEtc,
        IntPtr clientSite, IntPtr stg, out IntPtr ppv);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibrary(string p);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int FnCreate(IntPtr self, IntPtr outer, ref Guid iid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int FnQI(IntPtr self, ref Guid iid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint FnAddRef(IntPtr self);

    // Minimal aggregating outer: QI only IUnknown
    static IntPtr s_outerVtbl;
    static IntPtr s_outerObj;
    static FnQI s_qiKeep;
    static FnAddRef s_addKeep;
    static FnAddRef s_relKeep;

    static int OuterQI(IntPtr self, ref Guid iid, out IntPtr ppv)
    {
        if (iid == IID_IUnknown)
        {
            ppv = self;
            Marshal.WriteInt32(self, IntPtr.Size, Marshal.ReadInt32(self, IntPtr.Size) + 1); // fake ref via +8? skip
            return 0;
        }
        ppv = IntPtr.Zero;
        return unchecked((int)0x80004002);
    }

    static uint OuterAddRef(IntPtr self) { return 2; }
    static uint OuterRelease(IntPtr self) { return 1; }

    static IntPtr MakeOuter()
    {
        s_qiKeep = OuterQI;
        s_addKeep = OuterAddRef;
        s_relKeep = OuterRelease;
        s_outerVtbl = Marshal.AllocHGlobal(3 * IntPtr.Size);
        Marshal.WriteIntPtr(s_outerVtbl, 0, Marshal.GetFunctionPointerForDelegate(s_qiKeep));
        Marshal.WriteIntPtr(s_outerVtbl, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(s_addKeep));
        Marshal.WriteIntPtr(s_outerVtbl, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(s_relKeep));
        s_outerObj = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(s_outerObj, s_outerVtbl);
        return s_outerObj;
    }

    static IntPtr Vt(IntPtr o, int i)
    {
        return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size);
    }

    static void ProbeFactory(Guid clsid, uint ctx, string tag)
    {
        Guid cfid = IID_IClassFactory;
        IntPtr cf;
        int hr = CoGetClassObject(ref clsid, ctx, IntPtr.Zero, ref cfid, out cf);
        Console.WriteLine(tag + " GCO ctx=0x" + ctx.ToString("x") + " HR=0x" + hr.ToString("X8"));
        if (hr != 0) return;

        Console.WriteLine("  factory vt0=" + Marshal.ReadIntPtr(Marshal.ReadIntPtr(cf)).ToString("X"));
        // QI factory for interesting IIDs
        foreach (var g in new[] { IID_IClassFactory2, IID_IUnknown, IID_IPSFactoryBuffer, clsid,
            IID_IMarshal, IID_IStdMarshalInfo })
        {
            IntPtr q; Guid gg = g;
            hr = Marshal.QueryInterface(cf, ref gg, out q);
            Console.WriteLine("  factory QI " + g.ToString("D").Substring(0, 8) + " HR=0x" + hr.ToString("X8"));
            if (hr == 0 && q != cf) Marshal.Release(q);
        }

        var create = (FnCreate)Marshal.GetDelegateForFunctionPointer(Vt(cf, 3), typeof(FnCreate));
        IntPtr outer = MakeOuter();

        Guid[] iids = {
            IID_IUnknown,
            clsid,
            IID_IStateData,
            IID_IPersist,
            IID_IPersistStream,
            IID_IPersistStorage,
            IID_IOleObject,
            IID_IMarshal,
            new Guid("A3116D99-0A9B-400D-851E-84B3E387DBCC"),
            new Guid("4473E3A7-C2AD-4075-A1F8-935A584740A9"),
            new Guid("8DEEF6BE-6810-4817-956C-C54AE0B0FFAC"),
            new Guid("DC09760E-9FDA-454A-B9D2-7E663E58C39D"),
            new Guid("463FE815-7BC0-4463-9CE4-D8C8BD6EA257"),
            new Guid("0D0497A9-19AD-49B9-A68E-4B2684AAF26C"),
            new Guid("E1A87F97-EC2C-4F55-867A-A2781571DE8E"),
            new Guid("693C25B2-E6AF-4783-8F8F-3BB222071D58"),
        };

        foreach (var iid in iids)
        {
            Guid ii = iid;
            IntPtr obj;
            hr = create(cf, IntPtr.Zero, ref ii, out obj);
            if (hr == 0 || hr == unchecked((int)0x80070057) || hr == unchecked((int)0x80040110))
                Console.WriteLine("  Create(null," + iid.ToString("D").Substring(0, 8) + ") HR=0x" + hr.ToString("X8")
                    + (hr == 0 ? " " + obj.ToString("X") : ""));
            if (hr == 0) { DumpObj(obj); Marshal.Release(obj); }

            hr = create(cf, outer, ref ii, out obj);
            if (hr == 0 || (hr != unchecked((int)0x80004002) && hr != unchecked((int)0x80070057)))
                Console.WriteLine("  Create(outer," + iid.ToString("D").Substring(0, 8) + ") HR=0x" + hr.ToString("X8")
                    + (hr == 0 ? " " + obj.ToString("X") : ""));
            else if (hr == unchecked((int)0x80040110)) // CLASS_E_NOAGGREGATION
                Console.WriteLine("  Create(outer," + iid.ToString("D").Substring(0, 8) + ") NOAGG");
            if (hr == 0) { DumpObj(obj); Marshal.Release(obj); }
        }

        // LockServer?
        try
        {
            var lockFn = (FnCreate)Marshal.GetDelegateForFunctionPointer(Vt(cf, 4), typeof(FnCreate));
            // actually LockServer(BOOL) - different sig; skip
        }
        catch { }

        Marshal.Release(cf);
    }

    static void DumpObj(IntPtr obj)
    {
        try
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            Console.WriteLine("    vt=" + vt.ToString("X"));
            byte[] b = new byte[0x40];
            Marshal.Copy(obj, b, 0, 0x40);
            Console.Write("    ");
            for (int i = 0; i < 0x40; i++) Console.Write(b[i].ToString("x2") + (i % 16 == 15 ? "\n    " : " "));
            Console.WriteLine();
        }
        catch (Exception ex) { Console.WriteLine("    dump " + ex.Message); }
    }

    static void Main()
    {
        OleInitialize(IntPtr.Zero);
        CoInitializeEx(IntPtr.Zero, 2);
        LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
        LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");

        // Ensure SyncProxy alive (previously GCO worked after SyncProxy)
        try
        {
            object sync = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
            Console.WriteLine("SyncProxy ok");
            GC.KeepAlive(sync);
        }
        catch (Exception ex) { Console.WriteLine("SyncProxy " + ex.Message); }

        foreach (uint ctx in new uint[] { 0x2, 0x402, 0x17 })
        {
            ProbeFactory(CLSID_App, ctx, "APP");
            ProbeFactory(CLSID_Sys, ctx, "SYS");
        }

        // OleCreate
        foreach (var clsid in new[] { CLSID_App, CLSID_Sys })
        {
            Guid c = clsid;
            Guid iid = IID_IUnknown;
            IntPtr obj;
            int hr = OleCreate(ref c, ref iid, 1 /*OLERENDER_NONE*/, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out obj);
            Console.WriteLine("OleCreate " + clsid.ToString("D").Substring(0, 8) + " HR=0x" + hr.ToString("X8"));
            if (hr == 0) { DumpObj(obj); Marshal.Release(obj); }
        }

        // CoCreate with outer aggregation
        IntPtr outer = MakeOuter();
        foreach (var clsid in new[] { CLSID_App, CLSID_Sys })
        {
            Guid c = clsid;
            Guid iid = IID_IUnknown;
            IntPtr obj;
            foreach (uint ctx in new uint[] { 0x2, 0x402, 0x17, 0x3 })
            {
                int hr = CoCreateInstance(ref c, outer, ctx, ref iid, out obj);
                Console.WriteLine("CCI outer " + clsid.ToString("D").Substring(0, 8) +
                    " ctx=0x" + ctx.ToString("x") + " HR=0x" + hr.ToString("X8"));
                if (hr == 0) { DumpObj(obj); Marshal.Release(obj); }

                hr = CoCreateInstance(ref c, IntPtr.Zero, ctx, ref iid, out obj);
                Console.WriteLine("CCI null  " + clsid.ToString("D").Substring(0, 8) +
                    " ctx=0x" + ctx.ToString("x") + " HR=0x" + hr.ToString("X8"));
                if (hr == 0) { DumpObj(obj); Marshal.Release(obj); }
            }
        }
    }
}
