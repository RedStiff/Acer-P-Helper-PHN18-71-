using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// Probe SyncProxy IStateData ProcessGetSettings / SetSettings / DoOperation without NVIDIA App.
/// </summary>
class SyncProxyDds
{
    static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
    // Operation GUID from Frida dump of ProcessDoOperation
    static readonly Guid OP_GUID = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");

    const ushort SETTING_DDS_UXD = 0x7d;

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr p, uint f);

    [StructLayout(LayoutKind.Sequential)]
    struct DescriptorCollectionRaw
    {
        public IntPtr items;
        public ulong count;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DescriptorRaw
    {
        public Guid handle;
        public ushort infoId;
        public ushort settingId;
        public uint flags;
        public IntPtr data;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int FnGetSettings(IntPtr self, IntPtr collection);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int FnGetHandleInfo(IntPtr self, IntPtr handle, IntPtr info);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int FnSetSettings(IntPtr self, IntPtr collection);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int FnDoOperation(IntPtr self, IntPtr opDesc);

    static void ReadAce(string tag)
    {
        using (var k = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
        {
            Console.WriteLine(tag + " ACE state=" + k.GetValue("InternalMuxState")
                + " auto=" + k.GetValue("InternalMuxIsAutomaticMode")
                + " i2d=" + k.GetValue("ACESwitchedI2D"));
        }
    }

    static IntPtr GetVtableFn(IntPtr iface, int slot)
    {
        IntPtr vt = Marshal.ReadIntPtr(iface);
        return Marshal.ReadIntPtr(vt, slot * IntPtr.Size);
    }

    static void Hex(string tag, IntPtr p, int n)
    {
        if (p == IntPtr.Zero) { Console.WriteLine(tag + " null"); return; }
        byte[] b = new byte[n];
        Marshal.Copy(p, b, 0, n);
        Console.Write(tag + " ");
        for (int i = 0; i < n; i++) Console.Write(b[i].ToString("x2") + (i % 16 == 15 ? "\n     " : " "));
        Console.WriteLine();
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "get";
        CoInitializeEx(IntPtr.Zero, 2);

        // Kill App so we prove no-App path
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App"))
        {
            try { p.Kill(); } catch { }
        }
        System.Threading.Thread.Sleep(500);

        Type t = Type.GetTypeFromProgID("NvXDCore.SyncProxy", true);
        object o = Activator.CreateInstance(t);
        IntPtr punk = Marshal.GetIUnknownForObject(o);
        Guid iid = IID_IStateData;
        IntPtr iface;
        int hr = Marshal.QueryInterface(punk, ref iid, out iface);
        Console.WriteLine("QI IStateData HR=0x" + hr.ToString("X8") + " iface=" + iface);
        if (hr != 0) return;

        var getSettings = (FnGetSettings)Marshal.GetDelegateForFunctionPointer(
            GetVtableFn(iface, 3), typeof(FnGetSettings));
        var setSettings = (FnSetSettings)Marshal.GetDelegateForFunctionPointer(
            GetVtableFn(iface, 5), typeof(FnSetSettings));
        var doOp = (FnDoOperation)Marshal.GetDelegateForFunctionPointer(
            GetVtableFn(iface, 6), typeof(FnDoOperation));

        ReadAce("before");

        // Build a get request for setting 0x7d with null handle (discover)
        // Try empty collection get, and try with setting id filled
        DescriptorRaw desc = new DescriptorRaw();
        desc.handle = Guid.Empty;
        desc.infoId = 1; // get current?
        desc.settingId = SETTING_DDS_UXD;
        desc.flags = 4;
        // data: allocate room for value
        IntPtr data = Marshal.AllocHGlobal(0x100);
        for (int i = 0; i < 0x100; i++) Marshal.WriteByte(data, i, 0);
        desc.data = data;

        IntPtr items = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DescriptorRaw)));
        Marshal.StructureToPtr(desc, items, false);

        IntPtr coll = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DescriptorCollectionRaw)));
        Marshal.WriteIntPtr(coll, items);
        Marshal.WriteInt64(coll, IntPtr.Size, 1);

        Console.WriteLine("calling ProcessGetSettings...");
        try
        {
            hr = getSettings(iface, coll);
            Console.WriteLine("GetSettings HR=0x" + hr.ToString("X8"));
            DescriptorRaw outDesc = (DescriptorRaw)Marshal.PtrToStructure(items, typeof(DescriptorRaw));
            Console.WriteLine("out handle=" + outDesc.handle);
            Console.WriteLine("out info=" + outDesc.infoId + " sid=0x" + outDesc.settingId.ToString("x")
                + " flags=" + outDesc.flags + " data=" + outDesc.data);
            Hex("data", outDesc.data != IntPtr.Zero ? outDesc.data : data, 0x40);
        }
        catch (Exception ex)
        {
            Console.WriteLine("GetSettings EX: " + ex.GetType().Name + " " + ex.Message);
        }

        if (mode == "get")
        {
            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(items);
            Marshal.FreeHGlobal(coll);
            Marshal.Release(iface);
            Marshal.Release(punk);
            Marshal.FinalReleaseComObject(o);
            return;
        }

        // Pack mux value like NvCpl: byte0=1, byte1=auto, dword1=mux
        int mux = 1;
        int automatic = 0;
        if (mode == "dgpu" || mode == "nvidia") { mux = 2; automatic = 0; }
        else if (mode == "auto") { mux = 1; automatic = 1; }
        else if (mode == "igpu" || mode == "optimus") { mux = 1; automatic = 0; }

        // Re-read handle from get result
        DescriptorRaw got = (DescriptorRaw)Marshal.PtrToStructure(items, typeof(DescriptorRaw));
        Guid handle = got.handle;
        Console.WriteLine("using handle " + handle + " for set mux=" + mux + " auto=" + automatic);

        // Prepare set data — trial layouts
        byte[] val = new byte[16];
        val[0] = 1;
        val[1] = (byte)automatic;
        BitConverter.GetBytes(mux).CopyTo(val, 4);

        // Trial A: raw 16-byte val as data
        // Trial B: prefix GenericData header 05 00 00 00 01 00 00 00 + val
        // We'll try based on arg
        string layout = args.Length > 1 ? args[1] : "raw";
        IntPtr setData;
        if (layout == "hdr5")
        {
            setData = Marshal.AllocHGlobal(0x20);
            Marshal.WriteInt32(setData, 0, 5);
            Marshal.WriteInt32(setData, 4, 1);
            Marshal.Copy(val, 0, IntPtr.Add(setData, 8), 16);
            Hex("setData hdr5", setData, 0x18);
        }
        else if (layout == "hdr3")
        {
            setData = Marshal.AllocHGlobal(0x20);
            Marshal.WriteInt32(setData, 0, 3);
            Marshal.WriteInt32(setData, 4, 4);
            Marshal.WriteInt32(setData, 8, mux | (automatic << 8));
            Hex("setData hdr3", setData, 0x10);
        }
        else
        {
            setData = Marshal.AllocHGlobal(16);
            Marshal.Copy(val, 0, setData, 16);
            Hex("setData raw", setData, 16);
        }

        DescriptorRaw setDesc = new DescriptorRaw();
        setDesc.handle = handle;
        setDesc.infoId = 3;
        setDesc.settingId = SETTING_DDS_UXD;
        setDesc.flags = 4;
        setDesc.data = setData;

        IntPtr setItems = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DescriptorRaw)));
        Marshal.StructureToPtr(setDesc, setItems, false);
        IntPtr setColl = Marshal.AllocHGlobal(16);
        Marshal.WriteIntPtr(setColl, setItems);
        Marshal.WriteInt64(setColl, 8, 1);

        Console.WriteLine("calling ProcessSetSettings...");
        try
        {
            hr = setSettings(iface, setColl);
            Console.WriteLine("SetSettings HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("SetSettings EX: " + ex.GetType().Name + " " + ex.Message);
        }

        // DoOperation — pack like Frida: GUID + 09 00 02 00 ...
        IntPtr op = Marshal.AllocHGlobal(0x40);
        for (int i = 0; i < 0x40; i++) Marshal.WriteByte(op, i, 0);
        byte[] og = OP_GUID.ToByteArray();
        Marshal.Copy(og, 0, op, 16);
        Marshal.WriteInt16(op, 16, 9);
        Marshal.WriteInt16(op, 18, 2);

        Console.WriteLine("calling ProcessDoOperation...");
        try
        {
            hr = doOp(iface, op);
            Console.WriteLine("DoOperation HR=0x" + hr.ToString("X8"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("DoOperation EX: " + ex.GetType().Name + " " + ex.Message);
        }

        System.Threading.Thread.Sleep(2000);
        ReadAce("after");

        Marshal.FreeHGlobal(op);
        Marshal.FreeHGlobal(setData);
        Marshal.FreeHGlobal(setItems);
        Marshal.FreeHGlobal(setColl);
        Marshal.FreeHGlobal(data);
        Marshal.FreeHGlobal(items);
        Marshal.FreeHGlobal(coll);
        Marshal.Release(iface);
        Marshal.Release(punk);
        Marshal.FinalReleaseComObject(o);
    }
}
