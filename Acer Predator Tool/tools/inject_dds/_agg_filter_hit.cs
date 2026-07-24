using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// SessionFilter CreateInstance requires aggregation (non-null outer).
/// Proven in App spawn: CCI ctx=0x402 outer!=0 → REAL hr=0, obj vt=nvxdbat+0xf0100.
/// </summary>
class AggFilterHit
{
    static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
    static readonly Guid CLSID_SysFilter = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
    static readonly Guid HANDLE_DDS = new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");
    static readonly Guid OP_GUID = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
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
    delegate int FnCreate(IntPtr self, IntPtr outer, ref Guid iid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn2(IntPtr self, IntPtr a, IntPtr b);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int FnQI(IntPtr self, ref Guid iid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint FnRef(IntPtr self);

    // Proper aggregating outer: delegates non-IUnknown QI to inner once set
    static IntPtr s_vt;
    static IntPtr s_obj;
    static IntPtr s_inner;
    static FnQI s_qi;
    static FnRef s_add, s_rel;
    static int s_ref = 1;

    static int OuterQI(IntPtr self, ref Guid iid, out IntPtr ppv)
    {
        if (iid.Equals(IID_IUnknown))
        {
            ppv = self;
            OuterAddRef(self);
            return 0;
        }
        if (s_inner != IntPtr.Zero)
        {
            var qi = (FnQI)Marshal.GetDelegateForFunctionPointer(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(s_inner), 0), typeof(FnQI));
            return qi(s_inner, ref iid, out ppv);
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
        s_inner = IntPtr.Zero;
        s_ref = 1;
        return s_obj;
    }

    static IntPtr Vt(IntPtr o, int i)
    {
        return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size);
    }

    static void Ace(string t)
    {
        using (var k = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
            Console.WriteLine(t + " state=" + k.GetValue("InternalMuxState")
                + " auto=" + k.GetValue("InternalMuxIsAutomaticMode")
                + " i2d=" + k.GetValue("ACESwitchedI2D"));
    }

    static void Dump(IntPtr p, int n, string tag)
    {
        if (p == IntPtr.Zero) { Console.WriteLine(tag + " null"); return; }
        byte[] b = new byte[n];
        try { Marshal.Copy(p, b, 0, n); }
        catch (Exception ex) { Console.WriteLine(tag + " " + ex.Message); return; }
        Console.Write(tag + " ");
        for (int i = 0; i < n; i++)
            Console.Write(b[i].ToString("x2") + (i % 16 == 15 ? "\n     " : " "));
        Console.WriteLine();
    }

    static IntPtr TryCreateFilter(IntPtr outer, string tag)
    {
        Guid iid = IID_IUnknown;
        IntPtr obj;
        foreach (uint ctx in new uint[] { 0x402, 0x2, 0x3, 0x17 })
        {
            Guid c = CLSID_AppFilter;
            int hr = CoCreateInstance(ref c, outer, ctx, ref iid, out obj);
            Console.WriteLine(tag + " CCI app ctx=0x" + ctx.ToString("x") + " HR=0x" + hr.ToString("X8")
                + (hr == 0 ? " " + obj.ToString("X") : ""));
            if (hr == 0) return obj;

            c = CLSID_SysFilter;
            hr = CoCreateInstance(ref c, outer, ctx, ref iid, out obj);
            Console.WriteLine(tag + " CCI sys ctx=0x" + ctx.ToString("x") + " HR=0x" + hr.ToString("X8")
                + (hr == 0 ? " " + obj.ToString("X") : ""));
            if (hr == 0) return obj;
        }

        // Via GCO + CreateInstance with outer
        Guid cfid = IID_IClassFactory;
        Guid clsid = CLSID_AppFilter;
        IntPtr cf;
        int ghr = CoGetClassObject(ref clsid, 0x402, IntPtr.Zero, ref cfid, out cf);
        Console.WriteLine(tag + " GCO HR=0x" + ghr.ToString("X8"));
        if (ghr == 0)
        {
            var create = (FnCreate)Marshal.GetDelegateForFunctionPointer(Vt(cf, 3), typeof(FnCreate));
            int hr = create(cf, outer, ref iid, out obj);
            Console.WriteLine(tag + " Create(app) HR=0x" + hr.ToString("X8")
                + (hr == 0 ? " " + obj.ToString("X") : ""));
            Marshal.Release(cf);
            if (hr == 0) return obj;
        }
        return IntPtr.Zero;
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

        object syncObj = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
        IntPtr syncUnk = Marshal.GetIUnknownForObject(syncObj);
        Console.WriteLine("sync unk=" + syncUnk.ToString("X"));

        // Try 1: SyncProxy as outer
        IntPtr filter = TryCreateFilter(syncUnk, "syncOuter");

        // Try 2: our aggregating outer
        if (filter == IntPtr.Zero)
        {
            IntPtr outer = MakeOuter();
            filter = TryCreateFilter(outer, "ourOuter");
            if (filter != IntPtr.Zero)
                s_inner = filter; // complete aggregation
        }

        // Try 3: Serializer / OperationInterceptor as outer
        if (filter == IntPtr.Zero)
        {
            foreach (string prog in new[] {
                "NVXDBatdll.NvXDSerializer", "NVXDBatdll.OperationInterceptor" })
            {
                try
                {
                    object o = Activator.CreateInstance(Type.GetTypeFromProgID(prog));
                    IntPtr u = Marshal.GetIUnknownForObject(o);
                    Console.WriteLine("try outer " + prog + " " + u.ToString("X"));
                    filter = TryCreateFilter(u, prog);
                    if (filter != IntPtr.Zero) break;
                }
                catch (Exception ex) { Console.WriteLine(prog + " " + ex.Message); }
            }
        }

        if (filter == IntPtr.Zero)
        {
            Console.WriteLine("FAIL no filter");
            return;
        }

        Console.WriteLine("FILTER OK " + filter.ToString("X"));
        Dump(filter, 0x50, "filter");

        Guid iid = IID_IStateData;
        IntPtr sd;
        int hr = Marshal.QueryInterface(syncUnk, ref iid, out sd);
        Console.WriteLine("QI IStateData HR=0x" + hr.ToString("X8"));
        if (hr != 0) return;

        var get = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn2));
        var set = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn2));
        var dop = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn2));

        Ace("before");
        IntPtr arena = Marshal.AllocHGlobal(0x4000);
        for (int i = 0; i < 0x4000; i++) Marshal.WriteByte(arena, i, 0);

        IntPtr coll = arena, items = IntPtr.Add(arena, 0x40), data = IntPtr.Add(arena, 0x200);
        Marshal.WriteIntPtr(coll, 0, items);
        Marshal.WriteInt64(coll, 8, 1);
        Marshal.WriteInt16(items, 16, 1);
        Marshal.WriteInt16(items, 18, (short)SID);
        Marshal.WriteInt32(items, 20, 4);
        Marshal.WriteIntPtr(items, 24, data);

        try { hr = get(sd, coll, filter); Console.WriteLine("Get HR=0x" + hr.ToString("X8")); }
        catch (Exception ex) { Console.WriteLine("Get EX " + ex.Message); return; }

        long cnt = Marshal.ReadInt64(coll, 8);
        IntPtr items2 = Marshal.ReadIntPtr(coll, 0);
        Guid handle = HANDLE_DDS;
        if (items2 != IntPtr.Zero && cnt >= 1)
        {
            byte[] gb = new byte[16];
            Marshal.Copy(items2, gb, 0, 16);
            Guid gh = new Guid(gb);
            Console.WriteLine("handle " + gh + " count=" + cnt);
            if (gh != Guid.Empty) handle = gh;
            Dump(items2, 0x40, "items");
        }

        int mux = 1, automatic = 0;
        if (mode == "dgpu" || mode == "nvidia") { mux = 2; automatic = 0; }
        else if (mode == "auto") { mux = 1; automatic = 1; }

        IntPtr setColl = IntPtr.Add(arena, 0x1000);
        IntPtr setItems = IntPtr.Add(arena, 0x1040);
        IntPtr dataMux = IntPtr.Add(arena, 0x1100);
        IntPtr dataAuto = IntPtr.Add(arena, 0x1140);
        Marshal.WriteInt32(dataMux, 0, 3); Marshal.WriteInt32(dataMux, 4, 4); Marshal.WriteInt32(dataMux, 8, mux);
        Marshal.WriteInt32(dataAuto, 0, 5); Marshal.WriteInt32(dataAuto, 4, 1); Marshal.WriteInt32(dataAuto, 8, automatic);
        byte[] hb = handle.ToByteArray();
        Marshal.Copy(hb, 0, setItems, 16);
        Marshal.WriteInt16(setItems, 16, 1); Marshal.WriteInt16(setItems, 18, (short)SID);
        Marshal.WriteInt32(setItems, 20, 4); Marshal.WriteIntPtr(setItems, 24, dataMux);
        IntPtr d1 = IntPtr.Add(setItems, 0x20);
        Marshal.Copy(hb, 0, d1, 16);
        Marshal.WriteInt16(d1, 16, 3); Marshal.WriteInt16(d1, 18, (short)SID);
        Marshal.WriteInt32(d1, 20, 4); Marshal.WriteIntPtr(d1, 24, dataAuto);
        Marshal.WriteIntPtr(setColl, 0, setItems); Marshal.WriteInt64(setColl, 8, 2);

        try { hr = set(sd, setColl, filter); Console.WriteLine("Set HR=0x" + hr.ToString("X8")); }
        catch (Exception ex) { Console.WriteLine("Set EX " + ex.Message); return; }

        IntPtr op = IntPtr.Add(arena, 0x1200);
        Marshal.Copy(OP_GUID.ToByteArray(), 0, op, 16);
        Marshal.WriteInt16(op, 16, 9);
        Marshal.WriteInt16(op, 18, 2);
        try { hr = dop(sd, op, filter); Console.WriteLine("DoOp HR=0x" + hr.ToString("X8")); }
        catch (Exception ex) { Console.WriteLine("DoOp EX " + ex.Message); }

        System.Threading.Thread.Sleep(2000);
        Ace("after");
    }
}
