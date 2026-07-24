using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class SyncProxyDds2
{
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    static readonly Guid IID_ISyncProxy = new Guid("DC09760E-9FDA-454A-B9D2-7E663E58C39D");
    static readonly Guid OP_GUID = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr p, uint f);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn1(IntPtr self, IntPtr a);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Fn2(IntPtr self, IntPtr a, IntPtr b);

    static void Ace(string t)
    {
        using (var k = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
            Console.WriteLine(t + " state=" + k.GetValue("InternalMuxState")
                + " auto=" + k.GetValue("InternalMuxIsAutomaticMode"));
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
        Console.Write(tag + " @" + p.ToString("X") + " ");
        for (int i = 0; i < n; i++)
        {
            Console.Write(b[i].ToString("x2"));
            Console.Write(i % 16 == 15 ? "\n" + new string(' ', tag.Length + 3) : " ");
        }
        Console.WriteLine();
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "probe";
        CoInitializeEx(IntPtr.Zero, 2);
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App"))
            try { p.Kill(); } catch { }
        System.Threading.Thread.Sleep(800);

        object o = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy", true));
        IntPtr punk = Marshal.GetIUnknownForObject(o);

        // Dump ISyncProxy vtable methods 3..5
        Guid isync = IID_ISyncProxy;
        IntPtr sync;
        int hr = Marshal.QueryInterface(punk, ref isync, out sync);
        Console.WriteLine("QI ISyncProxy HR=0x" + hr.ToString("X8") + " " + sync);
        if (hr == 0)
        {
            for (int i = 0; i < 8; i++)
                Console.WriteLine("  sync vt[" + i + "]=" + Vt(sync, i).ToString("X"));
        }

        Guid iid = IID_IStateData;
        IntPtr sd;
        hr = Marshal.QueryInterface(punk, ref iid, out sd);
        Console.WriteLine("QI IStateData HR=0x" + hr.ToString("X8") + " " + sd);

        var get = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn1));
        var set = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn1));
        var dop = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn1));

        Ace("before");

        // Allocate collection in a way that survives: 16-byte header + space for many descriptors
        IntPtr arena = Marshal.AllocHGlobal(0x2000);
        for (int i = 0; i < 0x2000; i++) Marshal.WriteByte(arena, i, 0);

        // collection at arena+0: items*, count
        // items at arena+0x40
        IntPtr items = IntPtr.Add(arena, 0x40);
        Marshal.WriteIntPtr(arena, 0, items);
        Marshal.WriteInt64(arena, 8, 1);

        // descriptor: empty handle, info=1, sid=0x7d, flags=4, data=arena+0x200
        IntPtr dataBuf = IntPtr.Add(arena, 0x200);
        // write descriptor fields manually
        // handle GUID zeros already
        Marshal.WriteInt16(items, 16, 1);      // infoId
        Marshal.WriteInt16(items, 18, 0x7d);   // settingId
        Marshal.WriteInt32(items, 20, 4);      // flags
        Marshal.WriteIntPtr(items, 24, dataBuf);

        Dump(arena, 0x40, "coll-before");
        Dump(items, 0x20, "desc-before");

        Console.WriteLine("GetSettings...");
        try
        {
            hr = get(sd, arena);
            Console.WriteLine("HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex);
        }

        IntPtr itemsAfter = Marshal.ReadIntPtr(arena, 0);
        long countAfter = Marshal.ReadInt64(arena, 8);
        Console.WriteLine("coll items=" + itemsAfter.ToString("X") + " count=" + countAfter);
        Dump(arena, 0x40, "coll-after");
        if (itemsAfter != IntPtr.Zero && countAfter > 0 && countAfter < 64)
        {
            Dump(itemsAfter, (int)Math.Min(countAfter * 0x20, 0x100), "items-after");
            for (int i = 0; i < (int)countAfter && i < 8; i++)
            {
                IntPtr d = IntPtr.Add(itemsAfter, i * 0x20);
                byte[] gb = new byte[16];
                Marshal.Copy(d, gb, 0, 16);
                Guid g = new Guid(gb);
                ushort info = (ushort)Marshal.ReadInt16(d, 16);
                ushort sid = (ushort)Marshal.ReadInt16(d, 18);
                uint flags = (uint)Marshal.ReadInt32(d, 20);
                IntPtr dp = Marshal.ReadIntPtr(d, 24);
                Console.WriteLine("[" + i + "] handle=" + g + " info=" + info + " sid=0x" + sid.ToString("x")
                    + " flags=" + flags + " data=" + dp.ToString("X"));
                Dump(dp, 0x30, "  data");
            }
        }
        Dump(dataBuf, 0x80, "dataBuf");

        if (mode != "probe" && itemsAfter != IntPtr.Zero && countAfter >= 1)
        {
            IntPtr d0 = itemsAfter;
            byte[] gb = new byte[16];
            Marshal.Copy(d0, gb, 0, 16);
            Guid handle = new Guid(gb);
            // if handle empty, scan dataBuf for a real handle GUID pattern from Frida
            Console.WriteLine("set handle candidate " + handle);

            int mux = 1, automatic = 0;
            if (mode == "dgpu") { mux = 2; }
            if (mode == "auto") { automatic = 1; }

            // Build set descriptor into arena+0x800
            IntPtr setItems = IntPtr.Add(arena, 0x800);
            IntPtr setData = IntPtr.Add(arena, 0x900);
            for (int i = 0; i < 0x100; i++) Marshal.WriteByte(setData, i, 0);

            // try layout: type=5 size=1 then mux bytes OR raw 16
            string layout = args.Length > 1 ? args[1] : "raw";
            if (layout == "hdr5")
            {
                Marshal.WriteInt32(setData, 0, 5);
                Marshal.WriteInt32(setData, 4, 1);
                Marshal.WriteByte(setData, 8, 1);
                Marshal.WriteByte(setData, 9, (byte)automatic);
                Marshal.WriteInt32(setData, 12, mux);
            }
            else
            {
                Marshal.WriteByte(setData, 0, 1);
                Marshal.WriteByte(setData, 1, (byte)automatic);
                Marshal.WriteInt32(setData, 4, mux);
            }

            Marshal.Copy(gb, 0, setItems, 16);
            Marshal.WriteInt16(setItems, 16, 3);
            Marshal.WriteInt16(setItems, 18, 0x7d);
            Marshal.WriteInt32(setItems, 20, 4);
            Marshal.WriteIntPtr(setItems, 24, setData);

            IntPtr setColl = IntPtr.Add(arena, 0x7C0);
            Marshal.WriteIntPtr(setColl, 0, setItems);
            Marshal.WriteInt64(setColl, 8, 1);

            Dump(setItems, 0x20, "setDesc");
            Dump(setData, 0x20, "setData");
            Console.WriteLine("SetSettings mux=" + mux + " auto=" + automatic);
            try { Console.WriteLine("Set HR=0x" + set(sd, setColl).ToString("X8")); }
            catch (Exception ex) { Console.WriteLine("Set EX " + ex.Message); }

            IntPtr op = IntPtr.Add(arena, 0xA00);
            byte[] og = OP_GUID.ToByteArray();
            Marshal.Copy(og, 0, op, 16);
            Marshal.WriteInt16(op, 16, 9);
            Marshal.WriteInt16(op, 18, 2);
            Dump(op, 0x20, "op");
            try { Console.WriteLine("DoOp HR=0x" + dop(sd, op).ToString("X8")); }
            catch (Exception ex) { Console.WriteLine("DoOp EX " + ex.Message); }

            System.Threading.Thread.Sleep(2500);
            Ace("after");
        }

        // do not free aggressively — process exit
        Console.WriteLine("done");
        Environment.Exit(0);
    }
}
