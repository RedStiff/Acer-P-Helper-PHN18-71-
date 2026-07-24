using System;
using System.Runtime.InteropServices;

class CoCreateFilterExact
{
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr a, uint f);

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(
        ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    static extern int CoGetClassObject(
        ref Guid clsid, uint ctx, IntPtr serverInfo, ref Guid iid, out IntPtr ppv);

    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
    static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
    static readonly Guid CLSID_SysFilter = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");

    // Common CLSCTX combinations
    static readonly uint[] Ctxs = {
        0x1,      // INPROC_SERVER
        0x2,      // INPROC_HANDLER
        0x3,      // INPROC_SERVER|HANDLER
        0x4,      // LOCAL_SERVER
        0x5,      // INPROC_SERVER|LOCAL_SERVER
        0x7,      // INPROC_SERVER|HANDLER|LOCAL_SERVER
        0x17,     // ALL
        0x400,    // ENABLE_CLOAKING?
        0x1000,   // APPCONTAINER
        0x10001,  // ACTIVATE_32BIT_SERVER | INPROC_SERVER? 
        0x8000,   // NO_CODE_DOWNLOAD related variants
        0x43,     // INPROC_SERVER|LOCAL_SERVER|DISABLE_AAA?
    };

    static void Try(string tag, Guid clsid, uint ctx)
    {
        Guid iid = IID_IUnknown;
        IntPtr p;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ctx, ref iid, out p);
        if (hr == 0 || (hr != unchecked((int)0x80040154) && hr != unchecked((int)0x80070057)))
            Console.WriteLine(tag + " ctx=0x" + ctx.ToString("X") + " HR=0x" + hr.ToString("X8")
                + (hr == 0 ? " OK " + p.ToString("X") : ""));
        else if (hr == 0)
            Console.WriteLine(tag + " ctx=0x" + ctx.ToString("X") + " OK");
        // always print failures that aren't CLASSNOTREG for visibility of near-misses
        if (hr != 0 && hr != unchecked((int)0x80040154))
            Console.WriteLine(tag + " ctx=0x" + ctx.ToString("X") + " HR=0x" + hr.ToString("X8"));
        if (hr == 0) Marshal.Release(p);
    }

    static void DumpReg()
    {
        // Print InprocServer32 / LocalServer32 / InprocHandler32 from both HKCR and Wow6432
        string[] roots = {
            @"CLSID\{3F6374C2-3540-476A-A123-D1DA2B6DDF86}",
            @"CLSID\{5387A36B-6F55-4C66-B085-E18393FCEA87}",
            @"WOW6432Node\CLSID\{3F6374C2-3540-476A-A123-D1DA2B6DDF86}",
        };
        foreach (var r in roots)
        {
            Console.WriteLine("==== HKCR\\" + r);
            try
            {
                using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(r))
                {
                    if (k == null) { Console.WriteLine("  missing"); continue; }
                    Console.WriteLine("  default=" + k.GetValue(null));
                    foreach (var skn in k.GetSubKeyNames())
                    {
                        using (var sk = k.OpenSubKey(skn))
                            Console.WriteLine("  " + skn + "=" + sk.GetValue(null)
                                + " threading=" + sk.GetValue("ThreadingModel"));
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("  " + ex.Message); }
        }
    }

    static void Main(string[] args)
    {
        CoInitializeEx(IntPtr.Zero, 2); // COINIT_APARTMENTTHREADED
        DumpReg();

        bool keepApp = args.Length > 0 && args[0] == "withapp";
        if (!keepApp)
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("NVIDIA App"))
                try { p.Kill(); } catch { }
            System.Threading.Thread.Sleep(500);
        }
        else
            Console.WriteLine("keeping NVIDIA App running");

        Console.WriteLine("=== STA CoCreate AppFilter ===");
        foreach (var c in Ctxs) Try("app", CLSID_AppFilter, c);

        Console.WriteLine("=== STA CoCreate SysFilter ===");
        foreach (var c in Ctxs) Try("sys", CLSID_SysFilter, c);

        // Also try MTA
        Console.WriteLine("=== done STA ===");
    }
}
