using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// No-App DDS: SessionFilter aggregated onto SyncProxy proxy (ICallFactory outer).
/// </summary>
class SafeHit
{
    static readonly Guid CLSID_Filter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    static readonly Guid IID_ICallFactory = new Guid("0000001B-0000-0000-C000-000000000046");
    static readonly Guid HANDLE_DDS = new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");
    static readonly Guid OP_GUID = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
    const ushort SID = 0x7d;

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr p, uint f);

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibrary(string p);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn0(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn1(IntPtr self, IntPtr a);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn2(IntPtr self, IntPtr a, IntPtr b);

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

    static int Call0(IntPtr obj, int slot, string tag)
    {
        try
        {
            var f = (Fn0)Marshal.GetDelegateForFunctionPointer(Vt(obj, slot), typeof(Fn0));
            int hr = f(obj);
            Console.WriteLine(tag + " vt[" + slot + "]() HR=0x" + hr.ToString("X8"));
            return hr;
        }
        catch (Exception ex)
        {
            Console.WriteLine(tag + " vt[" + slot + "] EX " + ex.Message);
            return -1;
        }
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "getonly";
        string ctxMode = args.Length > 1 ? args[1].ToLowerInvariant() : "filter";
        CoInitializeEx(IntPtr.Zero, 2);
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App"))
            try { p.Kill(); } catch { }
        System.Threading.Thread.Sleep(400);

        LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");

        object syncObj = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
        IntPtr syncUnk = Marshal.GetIUnknownForObject(syncObj);
        Console.WriteLine("sync=" + syncUnk.ToString("X"));

        Guid icf = IID_ICallFactory;
        IntPtr callFactory;
        int hr = Marshal.QueryInterface(syncUnk, ref icf, out callFactory);
        Console.WriteLine("sync ICallFactory HR=0x" + hr.ToString("X8"));

        Guid c = CLSID_Filter, iu = IID_IUnknown;
        IntPtr filter;
        hr = CoCreateInstance(ref c, syncUnk, 0x402, ref iu, out filter);
        Console.WriteLine("filter CCI HR=0x" + hr.ToString("X8") + (hr == 0 ? " " + filter.ToString("X") : ""));
        if (hr != 0) return;

        // Probe filter methods (likely getters / init) вЂ” stop on first hard failure
        Console.WriteLine("skip filter method probe");

        Guid isd = IID_IStateData;
        IntPtr sd;
        hr = Marshal.QueryInterface(syncUnk, ref isd, out sd);
        Console.WriteLine("QI IStateData HR=0x" + hr.ToString("X8"));
        if (hr != 0) return;

        IntPtr ctx = ctxMode == "sync" ? syncUnk : filter;
        Console.WriteLine("ctx=" + ctxMode + " " + ctx.ToString("X"));

        var get = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn2));
        var set = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn2));
        var dop = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn2));

        Ace("before");

        // Allocate on native heap with clear pattern
        IntPtr arena = Marshal.AllocHGlobal(0x4000);
        for (int i = 0; i < 0x4000; i++) Marshal.WriteByte(arena, i, 0);

        // Minimal Get: 1 descriptor, empty handle, info=1, sid=0x7d, flags=4, data ptr
        IntPtr coll = arena;
        IntPtr items = IntPtr.Add(arena, 0x100);
        IntPtr dataBuf = IntPtr.Add(arena, 0x300);
        // Pre-size data buffer as GenericData empty
        Marshal.WriteInt32(dataBuf, 0, 3); // type dword
        Marshal.WriteInt32(dataBuf, 4, 4);
        Marshal.WriteInt32(dataBuf, 8, 0);

        Marshal.WriteIntPtr(coll, 0, items);
        Marshal.WriteInt64(coll, 8, 1);
        // zero handle guid
        Marshal.WriteInt16(items, 16, 1);
        Marshal.WriteInt16(items, 18, (short)SID);
        Marshal.WriteInt32(items, 20, 4);
        Marshal.WriteIntPtr(items, 24, dataBuf);

        Console.WriteLine("calling GetSettings...");
        Console.Out.Flush();
        try
        {
            hr = get(sd, coll, ctx);
            Console.WriteLine("Get HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Get EX " + ex.GetType().Name + " " + ex.Message);
            return;
        }

        long cnt = Marshal.ReadInt64(coll, 8);
        IntPtr items2 = Marshal.ReadIntPtr(coll, 0);
        Console.WriteLine("count=" + cnt + " items=" + items2.ToString("X"));
        Guid handle = HANDLE_DDS;
        if (items2 != IntPtr.Zero && cnt >= 1)
        {
            byte[] gb = new byte[16];
            Marshal.Copy(items2, gb, 0, 16);
            handle = new Guid(gb);
            Console.WriteLine("handle " + handle);
            IntPtr dp = Marshal.ReadIntPtr(items2, 24);
            if (dp != IntPtr.Zero)
            {
                byte[] db = new byte[16];
                try
                {
                    Marshal.Copy(dp, db, 0, 16);
                    Console.Write("data ");
                    foreach (byte x in db) Console.Write(x.ToString("x2") + " ");
                    Console.WriteLine();
                }
                catch { }
            }
        }

        if (mode == "getonly")
        {
            Ace("after-get");
            return;
        }

        int mux = 1, automatic = 0;
        if (mode == "dgpu" || mode == "nvidia") { mux = 2; automatic = 0; }
        else if (mode == "auto") { mux = 1; automatic = 1; }
        else if (mode == "optimus") { mux = 1; automatic = 0; }

        IntPtr setColl = IntPtr.Add(arena, 0x1000);
        IntPtr setItems = IntPtr.Add(arena, 0x1100);
        IntPtr dataMux = IntPtr.Add(arena, 0x1200);
        IntPtr dataAuto = IntPtr.Add(arena, 0x1280);
        Marshal.WriteInt32(dataMux, 0, 3); Marshal.WriteInt32(dataMux, 4, 4); Marshal.WriteInt32(dataMux, 8, mux);
        Marshal.WriteInt32(dataAuto, 0, 5); Marshal.WriteInt32(dataAuto, 4, 1); Marshal.WriteInt32(dataAuto, 8, automatic);
        if (handle == Guid.Empty) handle = HANDLE_DDS;
        byte[] hb = handle.ToByteArray();
        Marshal.Copy(hb, 0, setItems, 16);
        Marshal.WriteInt16(setItems, 16, 1); Marshal.WriteInt16(setItems, 18, (short)SID);
        Marshal.WriteInt32(setItems, 20, 4); Marshal.WriteIntPtr(setItems, 24, dataMux);
        IntPtr d1 = IntPtr.Add(setItems, 0x20);
        Marshal.Copy(hb, 0, d1, 16);
        Marshal.WriteInt16(d1, 16, 3); Marshal.WriteInt16(d1, 18, (short)SID);
        Marshal.WriteInt32(d1, 20, 4); Marshal.WriteIntPtr(d1, 24, dataAuto);
        Marshal.WriteIntPtr(setColl, 0, setItems);
        Marshal.WriteInt64(setColl, 8, 2);

        Console.WriteLine("Set mux=" + mux + " auto=" + automatic);
        Console.Out.Flush();
        try
        {
            hr = set(sd, setColl, ctx);
            Console.WriteLine("Set HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Set EX " + ex.Message);
            return;
        }

        IntPtr op = IntPtr.Add(arena, 0x1400);
        Marshal.Copy(OP_GUID.ToByteArray(), 0, op, 16);
        Marshal.WriteInt16(op, 16, 9);
        Marshal.WriteInt16(op, 18, 2);
        try
        {
            hr = dop(sd, op, ctx);
            Console.WriteLine("DoOp HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("DoOp EX " + ex.Message);
        }

        System.Threading.Thread.Sleep(2000);
        Ace("after");
    }
}

