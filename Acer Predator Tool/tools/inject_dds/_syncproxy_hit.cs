using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// No-App DDS via SyncProxy IStateData + StateDataSessionFilter context.
/// </summary>
class SyncProxyHit
{
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid OP_GUID = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
    // Stable handle observed during App DDS HIT (may be machine/session specific)
    static readonly Guid HANDLE_DDS = new Guid("AFE3D677-141F-424B-808D-340D9EC4ACD6");

    const ushort SID_DDS = 0x7d;

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr p, uint f);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn2(IntPtr self, IntPtr a, IntPtr b);

    static void Ace(string t)
    {
        using (var k = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
            Console.WriteLine(t + " state=" + k.GetValue("InternalMuxState")
                + " auto=" + k.GetValue("InternalMuxIsAutomaticMode")
                + " i2d=" + k.GetValue("ACESwitchedI2D"));
    }

    static IntPtr Vt(IntPtr iface, int slot)
    {
        return Marshal.ReadIntPtr(Marshal.ReadIntPtr(iface), slot * IntPtr.Size);
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

    static object TryCreate(string prog)
    {
        try
        {
            Type t = Type.GetTypeFromProgID(prog, false);
            if (t == null) { Console.WriteLine("no type " + prog); return null; }
            object o = Activator.CreateInstance(t);
            Console.WriteLine("created " + prog + " ok");
            return o;
        }
        catch (Exception ex)
        {
            Console.WriteLine("create " + prog + " FAIL " + ex.GetType().Name + " " + ex.Message);
            return null;
        }
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "dgpu";
        string filterProg = args.Length > 1 ? args[1] : "system";
        CoInitializeEx(IntPtr.Zero, 2);

        foreach (var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App"))
            try { p.Kill(); } catch { }
        System.Threading.Thread.Sleep(800);

        string filtName = filterProg == "app"
            ? "NVXDBatdll.NvAppStateDataSessionFilter"
            : "NVXDBatdll.StateDataSessionFilter";

        object filtObj = TryCreate(filtName);
        if (filtObj == null && filterProg != "app")
            filtObj = TryCreate("NVXDBatdll.NvAppStateDataSessionFilter");
        if (filtObj == null)
        {
            Console.WriteLine("no session filter");
            return;
        }
        IntPtr filtUnk = Marshal.GetIUnknownForObject(filtObj);
        Console.WriteLine("filter unk=" + filtUnk.ToString("X"));

        object syncObj = TryCreate("NvXDCore.SyncProxy");
        if (syncObj == null) return;
        IntPtr punk = Marshal.GetIUnknownForObject(syncObj);
        Guid iid = IID_IStateData;
        IntPtr sd;
        int hr = Marshal.QueryInterface(punk, ref iid, out sd);
        Console.WriteLine("QI IStateData HR=0x" + hr.ToString("X8") + " " + sd.ToString("X"));
        if (hr != 0) return;

        var get = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn2));
        var set = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn2));
        var dop = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn2));

        Ace("before");

        IntPtr arena = Marshal.AllocHGlobal(0x4000);
        for (int i = 0; i < 0x4000; i++) Marshal.WriteByte(arena, i, 0);

        // --- GetSettings to discover handle ---
        IntPtr coll = arena;
        IntPtr items = IntPtr.Add(arena, 0x40);
        IntPtr dataBuf = IntPtr.Add(arena, 0x200);
        Marshal.WriteIntPtr(coll, 0, items);
        Marshal.WriteInt64(coll, 8, 1);
        // empty handle, info=1, sid=0x7d
        Marshal.WriteInt16(items, 16, 1);
        Marshal.WriteInt16(items, 18, (short)SID_DDS);
        Marshal.WriteInt32(items, 20, 4);
        Marshal.WriteIntPtr(items, 24, dataBuf);

        Console.WriteLine("GetSettings with filter...");
        try
        {
            hr = get(sd, coll, filtUnk);
            Console.WriteLine("Get HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex) { Console.WriteLine("Get EX " + ex.Message); }

        IntPtr itemsAfter = Marshal.ReadIntPtr(coll, 0);
        long countAfter = Marshal.ReadInt64(coll, 8);
        Console.WriteLine("count=" + countAfter + " items=" + itemsAfter.ToString("X"));
        Guid handle = HANDLE_DDS;
        if (itemsAfter != IntPtr.Zero && countAfter >= 1)
        {
            byte[] gb = new byte[16];
            Marshal.Copy(itemsAfter, gb, 0, 16);
            Guid gh = new Guid(gb);
            Console.WriteLine("got handle " + gh);
            if (gh != Guid.Empty) handle = gh;
            Dump(itemsAfter, (int)Math.Min(countAfter * 0x20, 0x60), "items");
            IntPtr dp = Marshal.ReadIntPtr(itemsAfter, 24);
            Dump(dp, 0x20, "data0");
        }

        int mux = 1, automatic = 0;
        if (mode == "dgpu" || mode == "nvidia") { mux = 2; automatic = 0; }
        else if (mode == "auto") { mux = 1; automatic = 1; }
        else { mux = 1; automatic = 0; }

        // --- SetSettings: 2 descriptors (info=1 dword mux, info=3 byte auto) like App ---
        IntPtr setColl = IntPtr.Add(arena, 0x1000);
        IntPtr setItems = IntPtr.Add(arena, 0x1040);
        IntPtr dataMux = IntPtr.Add(arena, 0x1100);
        IntPtr dataAuto = IntPtr.Add(arena, 0x1140);

        // GenericData DWORD: type=3 size=4 value=mux
        Marshal.WriteInt32(dataMux, 0, 3);
        Marshal.WriteInt32(dataMux, 4, 4);
        Marshal.WriteInt32(dataMux, 8, mux);

        // GenericData BYTE: type=5 size=1 value=automatic
        Marshal.WriteInt32(dataAuto, 0, 5);
        Marshal.WriteInt32(dataAuto, 4, 1);
        Marshal.WriteInt32(dataAuto, 8, automatic);

        byte[] hb = handle.ToByteArray();
        // desc[0] info=1
        Marshal.Copy(hb, 0, setItems, 16);
        Marshal.WriteInt16(setItems, 16, 1);
        Marshal.WriteInt16(setItems, 18, (short)SID_DDS);
        Marshal.WriteInt32(setItems, 20, 4);
        Marshal.WriteIntPtr(setItems, 24, dataMux);
        // desc[1] info=3
        IntPtr d1 = IntPtr.Add(setItems, 0x20);
        Marshal.Copy(hb, 0, d1, 16);
        Marshal.WriteInt16(d1, 16, 3);
        Marshal.WriteInt16(d1, 18, (short)SID_DDS);
        Marshal.WriteInt32(d1, 20, 4);
        Marshal.WriteIntPtr(d1, 24, dataAuto);

        Marshal.WriteIntPtr(setColl, 0, setItems);
        Marshal.WriteInt64(setColl, 8, 2);

        Dump(setItems, 0x40, "setItems");
        Dump(dataMux, 0x10, "dataMux");
        Dump(dataAuto, 0x10, "dataAuto");

        Console.WriteLine("SetSettings mux=" + mux + " auto=" + automatic + " handle=" + handle);
        try
        {
            hr = set(sd, setColl, filtUnk);
            Console.WriteLine("Set HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex) { Console.WriteLine("Set EX " + ex.Message); }

        // --- DoOperation ---
        IntPtr op = IntPtr.Add(arena, 0x1200);
        Marshal.Copy(OP_GUID.ToByteArray(), 0, op, 16);
        Marshal.WriteInt16(op, 16, 9);
        Marshal.WriteInt16(op, 18, 2);
        // second arg for DoOp was a different object in Frida — try filter first, then op-like struct
        Console.WriteLine("DoOperation...");
        try
        {
            hr = dop(sd, op, filtUnk);
            Console.WriteLine("DoOp HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex) { Console.WriteLine("DoOp EX " + ex.Message); }

        System.Threading.Thread.Sleep(3000);
        Ace("after");
        Console.WriteLine("done");
        Environment.Exit(0);
    }
}
