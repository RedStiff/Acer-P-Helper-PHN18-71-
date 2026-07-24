using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Hit2
{
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    static readonly Guid OP_GUID = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
    static readonly Guid HANDLE_DDS = new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");

    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr a, uint f);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn2(IntPtr self, IntPtr a, IntPtr b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn1(IntPtr self, IntPtr a);

    static void Ace(string t)
    {
        using (var k = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
            Console.WriteLine(t + " state=" + k.GetValue("InternalMuxState")
                + " auto=" + k.GetValue("InternalMuxIsAutomaticMode"));
    }

    static IntPtr Vt(IntPtr i, int s) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(i), s * IntPtr.Size); }

    static IntPtr CreateCtx(string prog)
    {
        try
        {
            object o = Activator.CreateInstance(Type.GetTypeFromProgID(prog, true));
            IntPtr p = Marshal.GetIUnknownForObject(o);
            Console.WriteLine("ctx " + prog + " = " + p.ToString("X"));
            return p;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ctx " + prog + " FAIL " + ex.Message);
            return IntPtr.Zero;
        }
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "dgpu";
        CoInitializeEx(IntPtr.Zero, 2);
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App"))
            try { p.Kill(); } catch { }
        System.Threading.Thread.Sleep(500);

        string[] ctxProgs = {
            "NVXDBat.NvXDBatchEngine",
            "NVXDBatdll.NvXDSerializer",
            "NVXDBatdll.OperationInterceptor",
            "NvXDCore.SyncProxy",
        };

        object syncObj = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy", true));
        IntPtr punk = Marshal.GetIUnknownForObject(syncObj);
        Guid iid = IID_IStateData;
        IntPtr sd; Marshal.QueryInterface(punk, ref iid, out sd);
        var set2 = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn2));
        var dop2 = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn2));
        var set1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn1));
        var dop1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn1));

        int mux = mode == "auto" ? 1 : (mode == "igpu" ? 1 : 2);
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
        Marshal.WriteIntPtr(arena, 0, items);
        Marshal.WriteInt64(arena, 8, 2);

        IntPtr op = IntPtr.Add(arena, 0x400);
        Marshal.Copy(OP_GUID.ToByteArray(), 0, op, 16);
        Marshal.WriteInt16(op, 16, 9); Marshal.WriteInt16(op, 18, 2);

        Ace("before");

        // Trial 1-arg
        Console.WriteLine("=== 1-arg Set/DoOp ===");
        try { Console.WriteLine("set1 HR=0x" + set1(sd, arena).ToString("X8")); }
        catch (Exception ex) { Console.WriteLine("set1 EX " + ex.GetType().Name); }
        try { Console.WriteLine("dop1 HR=0x" + dop1(sd, op).ToString("X8")); }
        catch (Exception ex) { Console.WriteLine("dop1 EX " + ex.GetType().Name); }
        System.Threading.Thread.Sleep(1500);
        Ace("after-1arg");

        // Trial each ctx as 2nd arg
        foreach (var prog in ctxProgs)
        {
            IntPtr ctx = CreateCtx(prog);
            if (ctx == IntPtr.Zero) continue;
            Console.WriteLine("=== 2-arg with " + prog + " ===");
            try { Console.WriteLine("set2 HR=0x" + set2(sd, arena, ctx).ToString("X8")); }
            catch (Exception ex) { Console.WriteLine("set2 EX " + ex.GetType().Name + " " + ex.Message); }
            try { Console.WriteLine("dop2 HR=0x" + dop2(sd, op, ctx).ToString("X8")); }
            catch (Exception ex) { Console.WriteLine("dop2 EX " + ex.GetType().Name + " " + ex.Message); }
            System.Threading.Thread.Sleep(1500);
            Ace("after-" + prog);
            Marshal.Release(ctx);
        }

        // Also try sd / punk as ctx
        Console.WriteLine("=== 2-arg with self ===");
        try { Console.WriteLine("set2 self HR=0x" + set2(sd, arena, sd).ToString("X8")); }
        catch (Exception ex) { Console.WriteLine("set2 self EX " + ex.Message); }
        try { Console.WriteLine("dop2 self HR=0x" + dop2(sd, op, sd).ToString("X8")); }
        catch (Exception ex) { Console.WriteLine("dop2 self EX " + ex.Message); }
        System.Threading.Thread.Sleep(1500);
        Ace("after-self");

        Environment.Exit(0);
    }
}
