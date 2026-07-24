using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>GHI-validate a handle after hard UXD restart (no App).</summary>
class GhiCheck {
  static readonly Guid CLSID_AppSync = new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD = new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");

  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p, uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c, IntPtr o, uint ctx, ref Guid i, out IntPtr p);
  [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn2(IntPtr s, IntPtr a, IntPtr b);

  static IntPtr Vt(IntPtr o, int i) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size); }
  static IntPtr Z(int n) { var p = Marshal.AllocCoTaskMem(n); for (int i = 0; i < n; i++) Marshal.WriteByte(p, i, 0); return p; }

  static void Main(string[] args) {
    Guid h = new Guid(args.Length > 0 ? args[0] : "747D8BF5-AB15-448B-91C5-52EFEC7C5850");
    CoInitializeEx(IntPtr.Zero, 2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    Guid c = CLSID_AppSync, iu = IID_IUnknown;
    IntPtr sync; int hr = CoCreateInstance(ref c, IntPtr.Zero, 4, ref iu, out sync);
    Console.WriteLine("sync 0x" + hr.ToString("X8"));
    Guid old = IID_OLD; IntPtr sd; hr = Marshal.QueryInterface(sync, ref old, out sd);
    var ghi = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn2));
    IntPtr hm = Z(16), info = Z(0x40);
    Marshal.Copy(h.ToByteArray(), 0, hm, 16);
    hr = ghi(sd, hm, info);
    int sid = Marshal.ReadInt32(info, 0);
    Console.WriteLine("GHI " + h + " HR=0x" + ((uint)hr).ToString("X8") + " settingId=0x" + sid.ToString("X"));
    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine("ACE state=" + k.GetValue("InternalMuxState") + " auto=" + k.GetValue("InternalMuxIsAutomaticMode"));
  }
}
