using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// Probe: CoCreate SyncProxy via App ISyncProxy IID (463FE815) vs system (DC09760E),
/// then aggregate SessionFilter and try wrapper/classic Set for ACE HIT.
/// </summary>
class AppISync {
  static readonly Guid CLSID_Sync = new Guid("DCAB0989-1301-4319-BE5F-ADE89F88581C");
  static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid CLSID_SysFilter = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_ISync_App = new Guid("463FE815-7BC0-4463-9CE4-D8C8BD6EA257");
  static readonly Guid IID_ISync_Sys = new Guid("DC09760E-9FDA-454A-B9D2-7E663E58C39D");
  static readonly Guid IID_OLD = new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static readonly Guid IID_NEW = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
  static readonly Guid IID_ICallFactory = new Guid("0000001B-0000-0000-C000-000000000046");
  static readonly Guid OP = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
  const ushort SID = 0x7d;
  const uint CTX_LOCAL = 0x4;
  const uint CTX_FILTER = 0x402;

  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p, uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c, IntPtr o, uint ctx, ref Guid i, out IntPtr p);
  [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn1(IntPtr s, IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn2(IntPtr s, IntPtr a, IntPtr b);

  static IntPtr Vt(IntPtr o, int i) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size); }
  static IntPtr Z(int n) {
    var p = Marshal.AllocCoTaskMem(n);
    for (int i = 0; i < n; i++) Marshal.WriteByte(p, i, 0);
    return p;
  }
  static void Ace(string t) {
    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine(t + " state=" + k.GetValue("InternalMuxState") + " auto=" + k.GetValue("InternalMuxIsAutomaticMode") + " i2d=" + k.GetValue("ACESwitchedI2D"));
  }
  static string Qi(IntPtr obj, Guid iid, out IntPtr iface) {
    int hr = Marshal.QueryInterface(obj, ref iid, out iface);
    return "0x" + hr.ToString("X8") + (iface == IntPtr.Zero ? "" : " @" + iface.ToString("X"));
  }

  static void DumpQi(string tag, IntPtr obj) {
    IntPtr p;
    Console.WriteLine(tag + " IUnknown " + Qi(obj, IID_IUnknown, out p));
    if (p != IntPtr.Zero) Marshal.Release(p);
    Console.WriteLine(tag + " ISyncApp " + Qi(obj, IID_ISync_App, out p));
    if (p != IntPtr.Zero) Marshal.Release(p);
    Console.WriteLine(tag + " ISyncSys " + Qi(obj, IID_ISync_Sys, out p));
    if (p != IntPtr.Zero) Marshal.Release(p);
    Console.WriteLine(tag + " OLD     " + Qi(obj, IID_OLD, out p));
    if (p != IntPtr.Zero) Marshal.Release(p);
    Console.WriteLine(tag + " NEW     " + Qi(obj, IID_NEW, out p));
    if (p != IntPtr.Zero) Marshal.Release(p);
    Console.WriteLine(tag + " ICallF  " + Qi(obj, IID_ICallFactory, out p));
    if (p != IntPtr.Zero) Marshal.Release(p);
  }

  static IntPtr MakeGetColl(Guid handle, ushort infoId, out IntPtr data) {
    IntPtr coll = Z(0x20), items = Z(0x40); data = Z(0x40);
    if (handle != Guid.Empty) Marshal.Copy(handle.ToByteArray(), 0, items, 16);
    Marshal.WriteInt16(items, 16, (short)infoId);
    Marshal.WriteInt16(items, 18, (short)SID);
    Marshal.WriteInt32(items, 20, 4);
    Marshal.WriteIntPtr(items, 24, data);
    Marshal.WriteInt32(data, 0, infoId == 3 ? 5 : 3);
    Marshal.WriteInt32(data, 4, infoId == 3 ? 1 : 4);
    Marshal.WriteIntPtr(coll, 0, items);
    Marshal.WriteInt64(coll, 8, 1);
    return coll;
  }

  static IntPtr MakeSetColl(Guid handle, int mux, int aut) {
    IntPtr coll = Z(0x20), items = Z(0x50), dMux = Z(0x20), dAuto = Z(0x20);
    Marshal.WriteInt32(dMux, 0, 3); Marshal.WriteInt32(dMux, 4, 4); Marshal.WriteInt32(dMux, 8, mux);
    Marshal.WriteInt32(dAuto, 0, 5); Marshal.WriteInt32(dAuto, 4, 1); Marshal.WriteInt32(dAuto, 8, aut);
    byte[] hb = handle.ToByteArray();
    Marshal.Copy(hb, 0, items, 16);
    Marshal.WriteInt16(items, 16, 1); Marshal.WriteInt16(items, 18, (short)SID);
    Marshal.WriteInt32(items, 20, 4); Marshal.WriteIntPtr(items, 24, dMux);
    IntPtr d1 = IntPtr.Add(items, 0x20);
    Marshal.Copy(hb, 0, d1, 16);
    Marshal.WriteInt16(d1, 16, 3); Marshal.WriteInt16(d1, 18, (short)SID);
    Marshal.WriteInt32(d1, 20, 4); Marshal.WriteIntPtr(d1, 24, dAuto);
    Marshal.WriteIntPtr(coll, 0, items); Marshal.WriteInt64(coll, 8, 2);
    return coll;
  }

  static void TryPath(string name, Guid syncIid, Guid filterClsid, string batPath, int mux, int aut) {
    Console.WriteLine("\n======== " + name + " ========");
    LoadLibrary(batPath);
    Guid c = CLSID_Sync, iid = syncIid;
    IntPtr sync;
    int hr = CoCreateInstance(ref c, IntPtr.Zero, CTX_LOCAL, ref iid, out sync);
    Console.WriteLine("CoCreate sync iid=" + syncIid + " HR=0x" + hr.ToString("X8") + " " + sync.ToString("X"));
    if (hr != 0) {
      // fallback IUnknown
      iid = IID_IUnknown;
      hr = CoCreateInstance(ref c, IntPtr.Zero, CTX_LOCAL, ref iid, out sync);
      Console.WriteLine("fallback IUnknown HR=0x" + hr.ToString("X8") + " " + sync.ToString("X"));
      if (hr != 0) return;
    }
    DumpQi("sync", sync);

    IntPtr oldSd, newSd;
    Console.WriteLine("QI OLD " + Qi(sync, IID_OLD, out oldSd));
    Console.WriteLine("QI NEW " + Qi(sync, IID_NEW, out newSd));

    Guid fc = filterClsid, iu = IID_IUnknown;
    IntPtr filter;
    hr = CoCreateInstance(ref fc, sync, CTX_FILTER, ref iu, out filter);
    Console.WriteLine("filter HR=0x" + hr.ToString("X8") + " " + filter.ToString("X"));
    if (hr != 0) return;

    DumpQi("filter", filter);
    DumpQi("sync-after", sync);

    IntPtr iface = IntPtr.Add(filter, 0x18);
    IntPtr cache = Marshal.ReadIntPtr(iface, 0x40);
    Console.WriteLine("cache=" + cache.ToString("X"));
    if (cache != IntPtr.Zero) DumpQi("cache", cache);

    IntPtr filtOld;
    Console.WriteLine("filt QI OLD " + Qi(filter, IID_OLD, out filtOld));
    Console.WriteLine("filtOld==iface? " + (filtOld == iface));

    Ace("before");

    // Prefer OLD ProcessGet/Set if available on sync or cache
    IntPtr sd = oldSd != IntPtr.Zero ? oldSd : newSd;
    string sdTag = oldSd != IntPtr.Zero ? "OLD" : "NEW";
    if (sd == IntPtr.Zero && cache != IntPtr.Zero) {
      IntPtr cOld, cNew;
      Qi(cache, IID_OLD, out cOld);
      Qi(cache, IID_NEW, out cNew);
      if (cOld != IntPtr.Zero) { sd = cOld; sdTag = "cacheOLD"; }
      else if (cNew != IntPtr.Zero) { sd = cNew; sdTag = "cacheNEW"; }
    }
    if (sd == IntPtr.Zero) {
      Console.WriteLine("no IStateData");
      return;
    }
    Console.WriteLine("using " + sdTag + " @" + sd.ToString("X"));

    var get = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn2));
    var set = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn2));
    var dop = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn2));

    IntPtr data;
    IntPtr coll = MakeGetColl(Guid.Empty, 1, out data);
    try {
      hr = get(sd, coll, filter);
      Console.WriteLine("Get empty HR=0x" + hr.ToString("X8") + " val=" + Marshal.ReadInt32(data, 8));
    } catch (Exception ex) { Console.WriteLine("Get EX " + ex.Message); }

    Guid handle = Guid.Empty;
    long cnt = Marshal.ReadInt64(coll, 8);
    IntPtr items = Marshal.ReadIntPtr(coll, 0);
    if (items != IntPtr.Zero && cnt >= 1) {
      byte[] gb = new byte[16]; Marshal.Copy(items, gb, 0, 16); handle = new Guid(gb);
      Console.WriteLine("handle " + handle);
    }

    // Wrapper path if filter OLD is secondary iface
    if (filtOld != IntPtr.Zero) {
      var get1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(filtOld, 3), typeof(Fn1));
      var set1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(filtOld, 5), typeof(Fn1));
      IntPtr dataW;
      IntPtr collW = MakeGetColl(handle, 1, out dataW);
      try {
        hr = get1(filtOld, collW);
        Console.WriteLine("wrapper Get HR=0x" + hr.ToString("X8") + " val=" + Marshal.ReadInt32(dataW, 8));
        long cntW = Marshal.ReadInt64(collW, 8);
        IntPtr itemsW = Marshal.ReadIntPtr(collW, 0);
        if (itemsW != IntPtr.Zero && cntW >= 1) {
          byte[] gb = new byte[16]; Marshal.Copy(itemsW, gb, 0, 16); handle = new Guid(gb);
          Console.WriteLine("wrapper handle " + handle);
        }
      } catch (Exception ex) { Console.WriteLine("wrapper Get EX " + ex.Message); }

      if (handle != Guid.Empty) {
        IntPtr setColl = MakeSetColl(handle, mux, aut);
        try {
          hr = set1(filtOld, setColl);
          Console.WriteLine("wrapper Set mux=" + mux + " auto=" + aut + " HR=0x" + hr.ToString("X8"));
        } catch (Exception ex) { Console.WriteLine("wrapper Set EX " + ex.Message); }
      }
    }

    if (handle == Guid.Empty) handle = Guid.Empty; // still try empty?
    IntPtr setColl2 = MakeSetColl(handle == Guid.Empty ? new Guid("8FA752F3-70CA-49DC-BF80-58381E02E7F8") : handle, mux, aut);
    try {
      hr = set(sd, setColl2, filter);
      Console.WriteLine("classic Set HR=0x" + hr.ToString("X8"));
    } catch (Exception ex) { Console.WriteLine("classic Set EX " + ex.Message); }

    IntPtr op = Z(0x20);
    Marshal.Copy(OP.ToByteArray(), 0, op, 16);
    Marshal.WriteInt16(op, 16, 9); Marshal.WriteInt16(op, 18, 2);
    try {
      hr = dop(sd, op, filter);
      Console.WriteLine("DoOp HR=0x" + hr.ToString("X8"));
    } catch (Exception ex) { Console.WriteLine("DoOp EX " + ex.Message); }

    System.Threading.Thread.Sleep(2500);
    Ace("after");
  }

  static void Main(string[] args) {
    string mode = args.Length > 0 ? args[0] : "optimus";
    int mux = 1, aut = 0;
    if (mode == "dgpu") { mux = 2; aut = 0; }
    if (mode == "auto") { mux = 1; aut = 1; }
    CoInitializeEx(IntPtr.Zero, 2);
    Ace("start");

    string appBat = @"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll";
    string sysBat = @"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll";

    // 1) App ISyncProxy + App filter
    TryPath("AppISync+AppFilter", IID_ISync_App, CLSID_AppFilter, appBat, mux, aut);

    // 2) Sys ISyncProxy + Sys filter (baseline)
    // TryPath("SysISync+SysFilter", IID_ISync_Sys, CLSID_SysFilter, sysBat, mux, aut);
  }
}
