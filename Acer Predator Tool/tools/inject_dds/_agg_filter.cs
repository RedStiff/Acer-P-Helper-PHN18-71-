using System;
using System.Runtime.InteropServices;

/// <summary>
/// Create NvAppStateDataSessionFilter via aggregation (InprocHandler requires outer).
/// </summary>
class AggFilter
{
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr a, uint f);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string n);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int DllGCO(ref Guid c, ref Guid i, out IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CFCreate(IntPtr self, IntPtr outer, ref Guid iid, out IntPtr ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int QiDel(IntPtr self, ref Guid iid, out IntPtr ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint RefDel(IntPtr self);

    static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");

    // Minimal controlling IUnknown in unmanaged memory
    static IntPtr AllocControllingUnknown()
    {
        // vtable with QI/AddRef/Release
        IntPtr vt = Marshal.AllocHGlobal(3 * IntPtr.Size);
        QiDel qi = delegate(IntPtr self, ref Guid iid, out IntPtr ppv)
        {
            if (iid == IID_IUnknown)
            {
                ppv = self;
                Marshal.AddRef(self);
                return 0;
            }
            ppv = IntPtr.Zero;
            return unchecked((int)0x80004002);
        };
        RefDel add = delegate(IntPtr self) { return 2; };
        RefDel rel = delegate(IntPtr self) { return 1; };
        // keep delegates alive
        GCHandle.Alloc(qi); GCHandle.Alloc(add); GCHandle.Alloc(rel);
        Marshal.WriteIntPtr(vt, 0, Marshal.GetFunctionPointerForDelegate(qi));
        Marshal.WriteIntPtr(vt, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(add));
        Marshal.WriteIntPtr(vt, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(rel));
        IntPtr obj = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(obj, vt);
        return obj;
    }

    static void Main()
    {
        CoInitializeEx(IntPtr.Zero, 2);
        string appDll = @"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll";
        string sysDll = @"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll";
        Guid app = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
        Guid sys = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");

        IntPtr outer = AllocControllingUnknown();
        Console.WriteLine("outer=" + outer.ToString("X"));

        foreach (var pair in new[] {
            new { dll = appDll, clsid = app, tag = "app" },
            new { dll = sysDll, clsid = sys, tag = "sys" },
        })
        {
            IntPtr h = LoadLibrary(pair.dll);
            var gco = (DllGCO)Marshal.GetDelegateForFunctionPointer(GetProcAddress(h, "DllGetClassObject"), typeof(DllGCO));
            Guid cfid = IID_IClassFactory;
            Guid clsid = pair.clsid;
            IntPtr cf;
            int hr = gco(ref clsid, ref cfid, out cf);
            Console.WriteLine(pair.tag + " GCO 0x" + hr.ToString("X8"));
            var create = (CFCreate)Marshal.GetDelegateForFunctionPointer(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(cf), 3 * IntPtr.Size), typeof(CFCreate));

            // 1) aggregate with outer, request IUnknown
            Guid iu = IID_IUnknown;
            IntPtr obj;
            hr = create(cf, outer, ref iu, out obj);
            Console.WriteLine(pair.tag + " Create AGG IUnknown HR=0x" + hr.ToString("X8") + (hr == 0 ? " " + obj.ToString("X") : ""));

            // 2) aggregate, request CLSID as IID
            Guid cid = pair.clsid;
            hr = create(cf, outer, ref cid, out obj);
            Console.WriteLine(pair.tag + " Create AGG clsid-as-iid HR=0x" + hr.ToString("X8") + (hr == 0 ? " " + obj.ToString("X") : ""));

            // 3) non-agg again for reference
            hr = create(cf, IntPtr.Zero, ref iu, out obj);
            Console.WriteLine(pair.tag + " Create NOAGG HR=0x" + hr.ToString("X8"));

            Marshal.Release(cf);
        }
    }
}
