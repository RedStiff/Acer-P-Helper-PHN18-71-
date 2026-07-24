using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Runtime.InteropServices.ComTypes;

class MarshalHit
{
    static readonly Guid CLSID_Sys = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
    static readonly Guid CLSID_App = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    const ushort SID = 0x7d;

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr p, uint f);

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid c, IntPtr o, uint ctx, ref Guid i, out IntPtr p);

    [DllImport("ole32.dll")]
    static extern int CreateStreamOnHGlobal(IntPtr h, bool del, out IStream stream);

    [DllImport("ole32.dll")]
    static extern int CoMarshalInterface(IStream pStm, ref Guid riid, IntPtr pUnk,
        uint dwDestContext, IntPtr pvDestContext, uint mshlflags);

    [DllImport("ole32.dll")]
    static extern int CoUnmarshalInterface(IStream pStm, ref Guid riid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    static extern int CoGetInterfaceAndReleaseStream(IStream pStm, ref Guid iid, out IntPtr ppv);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibrary(string p);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn2(IntPtr s, IntPtr a, IntPtr b);

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

    static IntPtr ToProxy(IntPtr local, Guid iid, string tag)
    {
        IStream stm;
        int hr = CreateStreamOnHGlobal(IntPtr.Zero, true, out stm);
        Console.WriteLine(tag + " CreateStream HR=0x" + hr.ToString("X8"));
        if (hr != 0) return IntPtr.Zero;

        // MSHCTX_LOCAL=0, MSHCTX_NOSHAREDMEM=1, MSHCTX_INPROC=3, MSHCTX_DIFFERENTMACHINE=2
        foreach (uint dest in new uint[] { 0, 1, 3, 4 })
        {
            // rewind
            stm.Seek(0, 0, IntPtr.Zero);
            stm.SetSize(0);
            Guid ii = iid;
            hr = CoMarshalInterface(stm, ref ii, local, dest, IntPtr.Zero, 0);
            Console.WriteLine(tag + " Marshal dest=" + dest + " HR=0x" + hr.ToString("X8"));
            if (hr != 0) continue;
            stm.Seek(0, 0, IntPtr.Zero);
            IntPtr proxy;
            hr = CoUnmarshalInterface(stm, ref ii, out proxy);
            Console.WriteLine(tag + " Unmarshal HR=0x" + hr.ToString("X8")
                + (hr == 0 ? " " + proxy.ToString("X") + " vt=" + Marshal.ReadIntPtr(proxy).ToString("X") : ""));
            if (hr == 0) return proxy;
        }
        return IntPtr.Zero;
    }

    static void Main(string[] args)
    {
        string which = args.Length > 0 ? args[0] : "sys";
        CoInitializeEx(IntPtr.Zero, 2);
        LoadLibrary(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll");
        if (which == "app")
            LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");

        object syncObj = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy"));
        IntPtr sync = Marshal.GetIUnknownForObject(syncObj);
        Guid clsid = which == "app" ? CLSID_App : CLSID_Sys;
        Guid iu = IID_IUnknown;
        IntPtr local;
        int hr = CoCreateInstance(ref clsid, sync, 0x402, ref iu, out local);
        Console.WriteLine("local CCI HR=0x" + hr.ToString("X8") + " " + local.ToString("X"));
        if (hr != 0) return;
        Console.WriteLine("local vt=" + Marshal.ReadIntPtr(local).ToString("X"));

        IntPtr proxy = ToProxy(local, IID_IUnknown, "IUnknown");
        if (proxy == IntPtr.Zero)
        {
            Console.WriteLine("no proxy");
            return;
        }

        Guid isd = IID_IStateData;
        IntPtr sd;
        hr = Marshal.QueryInterface(sync, ref isd, out sd);
        Console.WriteLine("sd HR=0x" + hr.ToString("X8"));

        var get = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn2));
        IntPtr arena = Marshal.AllocHGlobal(0x1000);
        for (int i = 0; i < 0x1000; i++) Marshal.WriteByte(arena, i, 0);
        IntPtr coll = arena, items = IntPtr.Add(arena, 0x40), data = IntPtr.Add(arena, 0x200);
        Marshal.WriteIntPtr(coll, 0, items);
        Marshal.WriteInt64(coll, 8, 1);
        Marshal.WriteInt16(items, 16, 1);
        Marshal.WriteInt16(items, 18, (short)SID);
        Marshal.WriteInt32(items, 20, 4);
        Marshal.WriteIntPtr(items, 24, data);
        Marshal.WriteInt32(data, 0, 3);
        Marshal.WriteInt32(data, 4, 4);
        Marshal.WriteInt32(data, 8, 0);

        Ace("before");
        Console.WriteLine("Get with proxy...");
        Console.Out.Flush();
        try
        {
            hr = get(sd, coll, proxy);
            Console.WriteLine("Get HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Get EX " + ex.Message);
        }
        Ace("after");
    }
}
