using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// Probe ProcessGetHandleInfo on NvAppSyncProxy OLD IStateData.
/// C++: ProcessGetHandleInfo(Handle, HandleInfo*)
/// COM likely: (GUID handle, out buffer) with optional filter context like Get/Set.
/// </summary>
class GhiProbe {
  static readonly Guid CLSID_AppSync = new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD = new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static Guid Handle = new Guid("747D8BF5-AB15-448B-91C5-52EFEC7C5850");

  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p, uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c, IntPtr o, uint ctx, ref Guid i, out IntPtr p);
  [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn1(IntPtr s, IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn2(IntPtr s, IntPtr a, IntPtr b);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn3(IntPtr s, IntPtr a, IntPtr b, IntPtr c);

  static IntPtr Vt(IntPtr o, int i) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size); }
  static IntPtr Z(int n) {
    var p = Marshal.AllocCoTaskMem(n);
    for (int i = 0; i < n; i++) Marshal.WriteByte(p, i, 0);
    return p;
  }
  static string H(int hr) { return "0x" + ((uint)hr).ToString("X8"); }
  static void Dump(IntPtr p, int n, string t) {
    if (p == IntPtr.Zero) { Console.WriteLine(t + " null"); return; }
    byte[] b = new byte[n];
    try { Marshal.Copy(p, b, 0, n); } catch (Exception ex) { Console.WriteLine(t + " " + ex.Message); return; }
    Console.Write(t + " ");
    for (int i = 0; i < n; i++) Console.Write(b[i].ToString("x2") + (i % 16 == 15 ? "\n     " : " "));
    Console.WriteLine();
  }

  static void Main(string[] args) {
    if (args.Length > 0) Handle = new Guid(args[0]);
    CoInitializeEx(IntPtr.Zero, 2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");

    Guid c = CLSID_AppSync, iu = IID_IUnknown;
    IntPtr sync; int hr = CoCreateInstance(ref c, IntPtr.Zero, 4, ref iu, out sync);
    Console.WriteLine("AppSync " + H(hr));
    Guid old = IID_OLD; IntPtr sd; hr = Marshal.QueryInterface(sync, ref old, out sd);
    Console.WriteLine("OLD " + H(hr) + " " + sd.ToString("X"));

    Guid fc = CLSID_AppFilter; IntPtr filter;
    hr = CoCreateInstance(ref fc, sync, 0x402, ref iu, out filter);
    Console.WriteLine("filter " + H(hr));
    IntPtr iface; old = IID_OLD; Marshal.QueryInterface(filter, ref old, out iface);

    IntPtr handleMem = Z(16);
    Marshal.Copy(Handle.ToByteArray(), 0, handleMem, 16);
    IntPtr info = Z(0x100);
    IntPtr infoPtr = Z(8);
    Marshal.WriteIntPtr(infoPtr, 0, info);

    Console.WriteLine("handle " + Handle);
    Console.WriteLine("sd vt[4]=" + Vt(sd, 4).ToString("X") + " iface vt[4]=" + Vt(iface, 4).ToString("X"));

    // A: Fn2(sd, handleGuid*, info*)
    try {
      var f = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn2));
      hr = f(sd, handleMem, info);
      Console.WriteLine("A sd Fn2(handle,info) " + H(hr)); Dump(info, 0x40, "info");
    } catch (Exception ex) { Console.WriteLine("A EX " + ex.Message); }

    // B: Fn2(sd, handleGuid*, info**)
    try {
      Marshal.WriteIntPtr(infoPtr, 0, IntPtr.Zero);
      var f = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn2));
      hr = f(sd, handleMem, infoPtr);
      Console.WriteLine("B sd Fn2(handle,info**) " + H(hr) + " -> " + Marshal.ReadIntPtr(infoPtr).ToString("X"));
      Dump(Marshal.ReadIntPtr(infoPtr), 0x40, "info*");
    } catch (Exception ex) { Console.WriteLine("B EX " + ex.Message); }

    // C: Fn2(sd, handle, filter) like Get/Set
    try {
      var f = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn2));
      hr = f(sd, handleMem, filter);
      Console.WriteLine("C sd Fn2(handle,filter) " + H(hr)); Dump(handleMem, 0x20, "handleMem");
    } catch (Exception ex) { Console.WriteLine("C EX " + ex.Message); }

    // D: Fn3(sd, handle, info, filter)
    try {
      for (int i = 0; i < 0x100; i++) Marshal.WriteByte(info, i, 0);
      var f = (Fn3)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn3));
      hr = f(sd, handleMem, info, filter);
      Console.WriteLine("D sd Fn3(handle,info,filter) " + H(hr)); Dump(info, 0x40, "info");
    } catch (Exception ex) { Console.WriteLine("D EX " + ex.Message); }

    // E: Fn3(sd, handle, info**, filter)
    try {
      Marshal.WriteIntPtr(infoPtr, 0, IntPtr.Zero);
      var f = (Fn3)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn3));
      hr = f(sd, handleMem, infoPtr, filter);
      Console.WriteLine("E sd Fn3(handle,info**,filter) " + H(hr) + " -> " + Marshal.ReadIntPtr(infoPtr).ToString("X"));
      Dump(Marshal.ReadIntPtr(infoPtr), 0x40, "info*");
    } catch (Exception ex) { Console.WriteLine("E EX " + ex.Message); }

    // F: wrapper iface Fn1/Fn2
    try {
      var f1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface, 4), typeof(Fn1));
      hr = f1(iface, handleMem);
      Console.WriteLine("F iface Fn1(handle) " + H(hr));
    } catch (Exception ex) { Console.WriteLine("F EX " + ex.Message); }
    try {
      for (int i = 0; i < 0x100; i++) Marshal.WriteByte(info, i, 0);
      var f2 = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(iface, 4), typeof(Fn2));
      hr = f2(iface, handleMem, info);
      Console.WriteLine("G iface Fn2(handle,info) " + H(hr)); Dump(info, 0x40, "info");
    } catch (Exception ex) { Console.WriteLine("G EX " + ex.Message); }

    // H: by-value GUID on stack — pass handleMem as first; also try sid-only
    IntPtr sidBuf = Z(4);
    Marshal.WriteInt16(sidBuf, 0, 0x7d);
    try {
      var f = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn2));
      hr = f(sd, sidBuf, info);
      Console.WriteLine("H sd Fn2(sid,info) " + H(hr)); Dump(info, 0x40, "info");
    } catch (Exception ex) { Console.WriteLine("H EX " + ex.Message); }

    // I: empty GUID = enumerate?
    IntPtr empty = Z(16);
    try {
      for (int i = 0; i < 0x100; i++) Marshal.WriteByte(info, i, 0);
      var f = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn2));
      hr = f(sd, empty, info);
      Console.WriteLine("I sd Fn2(empty,info) " + H(hr)); Dump(info, 0x40, "info");
    } catch (Exception ex) { Console.WriteLine("I EX " + ex.Message); }

    Console.WriteLine("done");
  }
}
