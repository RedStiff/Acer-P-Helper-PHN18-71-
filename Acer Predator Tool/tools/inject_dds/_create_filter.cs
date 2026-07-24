using System;
using System.Runtime.InteropServices;

class CreateFilter
{
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr a, uint f);

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr h, string name);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int DllGetClassObject_t(ref Guid clsid, ref Guid iid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int IClassFactory_CreateInstance(IntPtr self, IntPtr outer, ref Guid iid, out IntPtr ppv);

    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");

    const uint CLSCTX_INPROC_SERVER = 0x1;
    const uint CLSCTX_INPROC_HANDLER = 0x2;
    const uint CLSCTX_LOCAL_SERVER = 0x4;
    const uint CLSCTX_ALL = 0x17;

    static void TryCoCreate(string name, Guid clsid, uint ctx)
    {
        Guid iid = IID_IUnknown;
        IntPtr p;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ctx, ref iid, out p);
        Console.WriteLine("CoCreate " + name + " ctx=0x" + ctx.ToString("X") + " HR=0x" + hr.ToString("X8")
            + (hr == 0 ? " OK " + p.ToString("X") : ""));
        if (hr == 0) Marshal.Release(p);
    }

    static void TryDllFactory(string path, Guid clsid)
    {
        IntPtr h = LoadLibrary(path);
        Console.WriteLine("LoadLibrary " + path + " = " + h.ToString("X"));
        if (h == IntPtr.Zero) return;
        IntPtr proc = GetProcAddress(h, "DllGetClassObject");
        if (proc == IntPtr.Zero) { Console.WriteLine("no DllGetClassObject"); return; }
        var gco = (DllGetClassObject_t)Marshal.GetDelegateForFunctionPointer(proc, typeof(DllGetClassObject_t));
        Guid ifactory = IID_IClassFactory;
        IntPtr cf;
        int hr = gco(ref clsid, ref ifactory, out cf);
        Console.WriteLine("DllGetClassObject HR=0x" + hr.ToString("X8"));
        if (hr != 0) return;
        // CreateInstance via vtable slot 3
        IntPtr createFn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(cf), 3 * IntPtr.Size);
        var create = (IClassFactory_CreateInstance)Marshal.GetDelegateForFunctionPointer(createFn, typeof(IClassFactory_CreateInstance));
        Guid iu = IID_IUnknown;
        IntPtr obj;
        hr = create(cf, IntPtr.Zero, ref iu, out obj);
        Console.WriteLine("CreateInstance HR=0x" + hr.ToString("X8") + (hr == 0 ? " " + obj.ToString("X") : ""));
        if (hr == 0) Marshal.Release(obj);
        Marshal.Release(cf);
    }

    static void Main()
    {
        CoInitializeEx(IntPtr.Zero, 2);
        Guid app = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
        Guid sys = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");

        uint[] ctxs = { CLSCTX_INPROC_SERVER, CLSCTX_INPROC_HANDLER, CLSCTX_LOCAL_SERVER,
            CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER, CLSCTX_ALL };
        foreach (var c in ctxs)
        {
            TryCoCreate("NvAppFilter", app, c);
            TryCoCreate("SysFilter", sys, c);
        }

        TryDllFactory(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll", app);
        TryDllFactory(@"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll", sys);

        // Call ISyncProxy methods 3/4/5
        Console.WriteLine("=== ISyncProxy methods ===");
        object o = Activator.CreateInstance(Type.GetTypeFromProgID("NvXDCore.SyncProxy", true));
        IntPtr punk = Marshal.GetIUnknownForObject(o);
        Guid isync = new Guid("DC09760E-9FDA-454A-B9D2-7E663E58C39D");
        IntPtr sync;
        int hr = Marshal.QueryInterface(punk, ref isync, out sync);
        Console.WriteLine("QI ISyncProxy 0x" + hr.ToString("X8"));
        // try call each as HRESULT Method(out IUnknown*)
        for (int slot = 3; slot <= 5; slot++)
        {
            IntPtr fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(sync), slot * IntPtr.Size);
            // try: HRESULT f(this, out IntPtr)
            var d = (GetObj)Marshal.GetDelegateForFunctionPointer(fn, typeof(GetObj));
            IntPtr outp = IntPtr.Zero;
            try
            {
                hr = d(sync, out outp);
                Console.WriteLine("sync[" + slot + "](out) HR=0x" + hr.ToString("X8") + " out=" + outp.ToString("X"));
                if (hr == 0 && outp != IntPtr.Zero) Marshal.Release(outp);
            }
            catch (Exception ex) { Console.WriteLine("sync[" + slot + "](out) EX " + ex.Message); }

            // try: HRESULT f(this) 
            var d0 = (Get0)Marshal.GetDelegateForFunctionPointer(fn, typeof(Get0));
            try
            {
                hr = d0(sync);
                Console.WriteLine("sync[" + slot + "]() HR=0x" + hr.ToString("X8"));
            }
            catch (Exception ex) { Console.WriteLine("sync[" + slot + "]() EX " + ex.Message); }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetObj(IntPtr self, out IntPtr p);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Get0(IntPtr self);
}
