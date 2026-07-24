using System;
using System.Runtime.InteropServices;

/// <summary>
/// Try to get nested Handle payload after DoOp warm; use Plcy as outer.
/// </summary>
class GetWarm {
  static readonly Guid CLSID_AppSync = new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid CLSID_Plcy = new Guid("11829530-E41B-40E3-B3E1-24EFF3D39144");
  static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD = new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static readonly Guid ROOT = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
  static readonly Guid OP = ROOT;

  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p, uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c, IntPtr o, uint ctx, ref Guid i, out IntPtr p);
  [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn1(IntPtr s, IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn2(IntPtr s, IntPtr a, IntPtr b);

  static IntPtr Vt(IntPtr o, int i) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size); }
  static IntPtr Z(int n) { var p = Marshal.AllocCoTaskMem(n); for (int i = 0; i < n; i++) Marshal.WriteByte(p, i, 0); return p; }
  static string H(int hr) { return "0x" + ((uint)hr).ToString("X8"); }
  static void Dump(IntPtr p, int n, string t) {
    byte[] b = new byte[n]; Marshal.Copy(p, b, 0, n);
    Console.Write(t + " "); foreach (var x in b) Console.Write(x.ToString("x2") + " "); Console.WriteLine();
  }

  static void TryGet(string tag, Fn1 get1, Fn2 get, IntPtr sd, IntPtr iface, IntPtr filter, ushort infoId) {
    IntPtr coll = Z(0x20), items = Z(0x40), data = Z(0x40);
    Marshal.Copy(ROOT.ToByteArray(), 0, items, 16);
    Marshal.WriteInt16(items, 16, (short)infoId);
    Marshal.WriteInt16(items, 18, 2);
    Marshal.WriteInt32(items, 20, 4);
    Marshal.WriteIntPtr(items, 24, data);
    Marshal.WriteInt32(data, 0, 1); Marshal.WriteInt32(data, 4, 16);
    Marshal.WriteIntPtr(coll, 0, items); Marshal.WriteInt64(coll, 8, 1);
    int hr = get1 != null ? get1(iface, coll) : get(sd, coll, filter);
    Console.Write(tag + " info=" + infoId + " " + H(hr) + " type=" + Marshal.ReadInt32(data, 0) + " size=" + Marshal.ReadInt32(data, 4));
    byte[] payload = new byte[16]; Marshal.Copy(IntPtr.Add(data, 8), payload, 0, 16);
    bool nz = false; foreach (var x in payload) if (x != 0) nz = true;
    Console.WriteLine(nz ? " PAYLOAD=" + new Guid(payload) : " payload=0");
  }

  static void Path(string name, IntPtr outer, IntPtr sd) {
    Console.WriteLine("\n==== " + name + " ====");
    Guid fc = CLSID_AppFilter, iu = IID_IUnknown;
    IntPtr filter; int hr = CoCreateInstance(ref fc, outer, 0x402, ref iu, out filter);
    Console.WriteLine("filter " + H(hr));
    if (hr != 0) return;
    Guid old = IID_OLD; IntPtr iface; Marshal.QueryInterface(filter, ref old, out iface);
    var get1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface, 3), typeof(Fn1));
    var get = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn2));
    var dop = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn2));

    // warm DoOp 9,2
    IntPtr op = Z(0x20);
    Marshal.Copy(OP.ToByteArray(), 0, op, 16);
    Marshal.WriteInt16(op, 16, 9); Marshal.WriteInt16(op, 18, 2);
    Console.WriteLine("DoOp9,2 " + H(dop(sd, op, filter)));

    foreach (ushort infoId in new ushort[] { 1, 2, 7 }) {
      TryGet("wrap", get1, null, sd, iface, filter, infoId);
      TryGet("classic", null, get, sd, iface, filter, infoId);
    }
  }

  static void Main() {
    CoInitializeEx(IntPtr.Zero, 2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdplcy.dll");

    Guid iu = IID_IUnknown, old = IID_OLD;

    Guid sc = CLSID_AppSync; IntPtr sync; CoCreateInstance(ref sc, IntPtr.Zero, 4, ref iu, out sync);
    IntPtr sdSync; old = IID_OLD; Marshal.QueryInterface(sync, ref old, out sdSync);
    Path("AppSync outer", sync, sdSync);

    Guid pc = CLSID_Plcy; IntPtr plcy; CoCreateInstance(ref pc, IntPtr.Zero, 1, ref iu, out plcy);
    IntPtr sdPlcy; old = IID_OLD; Marshal.QueryInterface(plcy, ref old, out sdPlcy);
    // Plcy may not be ICallFactory — try anyway
    try { Path("Plcy outer", plcy, sdPlcy); } catch (Exception ex) { Console.WriteLine("Plcy path " + ex.Message); }

    // Plcy outer + AppSync sd?
    try { Path("Plcy outer + sync sd", plcy, sdSync); } catch (Exception ex) { Console.WriteLine("mix " + ex.Message); }
  }
}
