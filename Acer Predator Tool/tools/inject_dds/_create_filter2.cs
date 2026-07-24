using System;
using System.Runtime.InteropServices;

class T {
  [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr a,uint f);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h,string n);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int DllGCO(ref Guid c, ref Guid i, out IntPtr p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int CFCreate(IntPtr self, IntPtr outer, ref Guid iid, out IntPtr ppv);

  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
  static readonly Guid IID_IStateData = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");

  static void TryCreate(string dll, Guid clsid, Guid iid, string tag) {
    IntPtr h = LoadLibrary(dll);
    var gco = (DllGCO)Marshal.GetDelegateForFunctionPointer(GetProcAddress(h,"DllGetClassObject"), typeof(DllGCO));
    Guid cfid = IID_IClassFactory;
    IntPtr cf; int hr = gco(ref clsid, ref cfid, out cf);
    Console.WriteLine(tag + " GCO HR=0x" + hr.ToString("X8"));
    if (hr!=0) return;
    var create = (CFCreate)Marshal.GetDelegateForFunctionPointer(
      Marshal.ReadIntPtr(Marshal.ReadIntPtr(cf), 3*IntPtr.Size), typeof(CFCreate));
    IntPtr obj;
    hr = create(cf, IntPtr.Zero, ref iid, out obj);
    Console.WriteLine(tag + " Create iid=" + iid + " HR=0x" + hr.ToString("X8") + (hr==0?" "+obj.ToString("X"):""));
    if (hr==0) {
      // dump vt
      for (int i=0;i<8;i++)
        Console.WriteLine("  vt["+i+"]="+Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), i*IntPtr.Size).ToString("X"));
      Marshal.Release(obj);
    }
    Marshal.Release(cf);
  }

  static void Main() {
    CoInitializeEx(IntPtr.Zero, 2);
    string appDll = @"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll";
    string sysDll = @"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll";
    Guid app = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
    Guid sys = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
    Guid[] iids = {
      IID_IUnknown,
      app, // try clsid as iid
      sys,
      IID_IStateData,
      new Guid("A3116D99-0A9B-400D-851E-84B3E387DBCC"), // IStateDataReadOnly
      new Guid("4473E3A7-C2AD-4075-A1F8-935A584740A9"), // IStateEvents
      new Guid("8DEEF6BE-6810-4817-956C-C54AE0B0FFAC"),
    };
    foreach (var iid in iids) {
      TryCreate(appDll, app, iid, "app/"+iid.ToString("D").Substring(0,8));
      TryCreate(sysDll, sys, iid, "sys/"+iid.ToString("D").Substring(0,8));
    }

    // Known working coclasses
    string[] progs = {
      "NVXDBat.NvXDBatchEngine","NVXDBatdll.NvXDBatchEngine","NvXDCore.SyncProxy",
      "NVXDBatdll.NvXDBatchEngine","NVXDBatdll.NvXDSerializer","NVXDBatdll.OperationInterceptor"
    };
    foreach (var p in progs) {
      try {
        var t = Type.GetTypeFromProgID(p, false);
        if (t==null) { Console.WriteLine("no "+p); continue; }
        var o = Activator.CreateInstance(t);
        Console.WriteLine("OK "+p+" "+o.GetType().Name);
        Marshal.FinalReleaseComObject(o);
      } catch (Exception ex) { Console.WriteLine("FAIL "+p+" "+ex.Message); }
    }
  }
}
