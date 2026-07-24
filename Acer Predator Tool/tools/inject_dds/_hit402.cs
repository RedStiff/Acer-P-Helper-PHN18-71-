using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Hit402
{
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr a, uint f);
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid c, IntPtr o, uint ctx, ref Guid i, out IntPtr p);

    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
    static readonly Guid CLSID_SysFilter = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
    static readonly Guid OP_GUID = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
    static readonly Guid HANDLE_DDS = new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn2(IntPtr self, IntPtr a, IntPtr b);

    static void Ace(string t)
    {
        using (var k = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
            Console.WriteLine(t + " state=" + k.GetValue("InternalMuxState")
                + " auto=" + k.GetValue("InternalMuxIsAutomaticMode"));
    }

    static IntPtr Vt(IntPtr i, int s) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(i), s * IntPtr.Size); }

    static void Dump(IntPtr p, int n, string tag)
    {
        byte[] b = new byte[n];
        Marshal.Copy(p, b, 0, n);
        Console.Write(tag + " ");
        for (int i = 0; i < n; i++) Console.Write(b[i].ToString("x2") + (i % 16 == 15 ? "\n     " : " "));
        Console.WriteLine();
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "dgpu";
        CoInitializeEx(IntPtr.Zero, 2);
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App"))
            try { p.Kill(); } catch { }
        System.Threading.Thread.Sleep(800);

        uint[] ctxs = { 0x402, 0x2, 0x403, 0x407, 0x417, 0x400|0x1, 0x400|0x3 };
        IntPtr filter = IntPtr.Zero;
        foreach (var ctx in ctxs)
        {
            foreach (var clsid in new[] { CLSID_AppFilter, CLSID_SysFilter })
            {
                Guid c = clsid; Guid iid = IID_IUnknown; IntPtr p;
                int hr = CoCreateInstance(ref c, IntPtr.Zero, ctx, ref iid, out p);
                Console.WriteLine("CoCreate " + clsid.ToString("D").Substring(0,8) + " ctx=0x" + ctx.ToString("X")
                    + " HR=0x" + hr.ToString("X8") + (hr == 0 ? " OK " + p.ToString("X") : ""));
                if (hr == 0 && filter == IntPtr.Zero)
                {
                    filter = p;
                    Dump(p, 0x40, "filter");
                    Console.WriteLine("vt=" + Marshal.ReadIntPtr(p).ToString("X"));
                }
                else if (hr == 0) Marshal.Release(p);
            }
        }

        if (filter == IntPtr.Zero)
        {
            Console.WriteLine("NO FILTER - abort");
            return;
        }

        object syncObj = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy", true));
        IntPtr punk = Marshal.GetIUnknownForObject(syncObj);
        Guid isd = IID_IStateData; IntPtr sd;
        int qihr = Marshal.QueryInterface(punk, ref isd, out sd);
        Console.WriteLine("QI IStateData HR=0x" + qihr.ToString("X8"));
        var set = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn2));
        var dop = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn2));

        int mux = mode == "igpu" ? 1 : (mode == "auto" ? 1 : 2);
        int automatic = mode == "auto" ? 1 : 0;

        IntPtr arena = Marshal.AllocHGlobal(0x2000);
        for (int i = 0; i < 0x2000; i++) Marshal.WriteByte(arena, i, 0);
        IntPtr items = IntPtr.Add(arena, 0x40);
        IntPtr dataMux = IntPtr.Add(arena, 0x100);
        IntPtr dataAuto = IntPtr.Add(arena, 0x140);
        Marshal.WriteInt32(dataMux, 0, 3); Marshal.WriteInt32(dataMux, 4, 4); Marshal.WriteInt32(dataMux, 8, mux);
        Marshal.WriteInt32(dataAuto, 0, 5); Marshal.WriteInt32(dataAuto, 4, 1); Marshal.WriteInt32(dataAuto, 8, automatic);
        byte[] hb = HANDLE_DDS.ToByteArray();
        Marshal.Copy(hb, 0, items, 16);
        Marshal.WriteInt16(items, 16, 1); Marshal.WriteInt16(items, 18, 0x7d);
        Marshal.WriteInt32(items, 20, 4); Marshal.WriteIntPtr(items, 24, dataMux);
        IntPtr d1 = IntPtr.Add(items, 0x20);
        Marshal.Copy(hb, 0, d1, 16);
        Marshal.WriteInt16(d1, 16, 3); Marshal.WriteInt16(d1, 18, 0x7d);
        Marshal.WriteInt32(d1, 20, 4); Marshal.WriteIntPtr(d1, 24, dataAuto);
        Marshal.WriteIntPtr(arena, 0, items); Marshal.WriteInt64(arena, 8, 2);

        IntPtr op = IntPtr.Add(arena, 0x400);
        Marshal.Copy(OP_GUID.ToByteArray(), 0, op, 16);
        Marshal.WriteInt16(op, 16, 9); Marshal.WriteInt16(op, 18, 2);

        Ace("before");
        Console.WriteLine("SetSettings mux=" + mux + " auto=" + automatic);
        try { Console.WriteLine("Set HR=0x" + set(sd, arena, filter).ToString("X8")); }
        catch (Exception ex) { Console.WriteLine("Set EX " + ex.GetType().Name + " " + ex.Message); }
        try { Console.WriteLine("DoOp HR=0x" + dop(sd, op, filter).ToString("X8")); }
        catch (Exception ex) { Console.WriteLine("DoOp EX " + ex.GetType().Name + " " + ex.Message); }
        System.Threading.Thread.Sleep(3000);
        Ace("after");
        Environment.Exit(0);
    }
}
