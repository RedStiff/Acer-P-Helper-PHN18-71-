using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// Minimal no-App DDS HIT: NvAppSyncProxy outer + App SessionFilter + wrapper Set flags=4.
/// Usage: _appsync_hit.exe [optimus|auto|dgpu] [--batch]
/// </summary>
class AppSyncHit {
  static readonly Guid CLSID_AppSync = new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid CLSID_AppBatch = new Guid("9C793FCD-5185-47BB-BB30-21750359CA2C");
  static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD = new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static readonly Guid OP = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
  const ushort SID = 0x7d;

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
  static string H(int hr) { return "0x" + ((uint)hr).ToString("X8"); }
  static void Ace(string t) {
    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE")) {
      var s = k.GetValue("InternalMuxState");
      var a = k.GetValue("InternalMuxIsAutomaticMode");
      var i = k.GetValue("ACESwitchedI2D");
      Console.WriteLine(t + " state=" + s + " auto=" + a + " i2d=" + i);
    }
  }
  static int[] AceVals() {
    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      return new[] {
        Convert.ToInt32(k.GetValue("InternalMuxState")),
        Convert.ToInt32(k.GetValue("InternalMuxIsAutomaticMode")),
        Convert.ToInt32(k.GetValue("ACESwitchedI2D"))
      };
  }

  static IntPtr Create(Guid clsid, Guid iid, uint ctx, IntPtr outer, out int hr) {
    Guid c = clsid, i = iid;
    IntPtr p;
    hr = CoCreateInstance(ref c, outer, ctx, ref i, out p);
    return p;
  }

  static Guid ReadHandle(IntPtr coll) {
    long cnt = Marshal.ReadInt64(coll, 8);
    IntPtr items = Marshal.ReadIntPtr(coll, 0);
    if (items == IntPtr.Zero || cnt < 1) return Guid.Empty;
    byte[] gb = new byte[16];
    Marshal.Copy(items, gb, 0, 16);
    return new Guid(gb);
  }

  static IntPtr MakeGet(Guid handle, ushort infoId, out IntPtr data) {
    IntPtr coll = Z(0x20), items = Z(0x40);
    data = Z(0x40);
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

  static IntPtr MakeSet(Guid handle, int mux, int aut, uint flags) {
    IntPtr coll = Z(0x20), items = Z(0x50), dMux = Z(0x20), dAuto = Z(0x20);
    Marshal.WriteInt32(dMux, 0, 3); Marshal.WriteInt32(dMux, 4, 4); Marshal.WriteInt32(dMux, 8, mux);
    Marshal.WriteInt32(dAuto, 0, 5); Marshal.WriteInt32(dAuto, 4, 1); Marshal.WriteInt32(dAuto, 8, aut);
    byte[] hb = handle.ToByteArray();
    Marshal.Copy(hb, 0, items, 16);
    Marshal.WriteInt16(items, 16, 1); Marshal.WriteInt16(items, 18, (short)SID);
    Marshal.WriteInt32(items, 20, (int)flags); Marshal.WriteIntPtr(items, 24, dMux);
    IntPtr d1 = IntPtr.Add(items, 0x20);
    Marshal.Copy(hb, 0, d1, 16);
    Marshal.WriteInt16(d1, 16, 3); Marshal.WriteInt16(d1, 18, (short)SID);
    Marshal.WriteInt32(d1, 20, (int)flags); Marshal.WriteIntPtr(d1, 24, dAuto);
    Marshal.WriteIntPtr(coll, 0, items); Marshal.WriteInt64(coll, 8, 2);
    return coll;
  }

  static int Apply(int mux, int aut, bool useBatch) {
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    int hr;
    IntPtr batch = IntPtr.Zero;
    if (useBatch) {
      batch = Create(CLSID_AppBatch, IID_OLD, 0x1, IntPtr.Zero, out hr);
      Console.WriteLine("batch " + H(hr) + " " + batch.ToString("X"));
      if (hr != 0) return hr;
    }

    IntPtr sync = Create(CLSID_AppSync, IID_IUnknown, 0x4, IntPtr.Zero, out hr);
    Console.WriteLine("AppSync " + H(hr) + " " + sync.ToString("X"));
    if (hr != 0) return hr;

    IntPtr sdOld;
    Guid iidOld = IID_OLD;
    hr = Marshal.QueryInterface(sync, ref iidOld, out sdOld);
    Console.WriteLine("AppSync OLD " + H(hr) + " " + sdOld.ToString("X"));

    IntPtr filter = Create(CLSID_AppFilter, IID_IUnknown, 0x402, sync, out hr);
    Console.WriteLine("filter " + H(hr) + " " + filter.ToString("X"));
    if (hr != 0) return hr;

    IntPtr iface;
    iidOld = IID_OLD;
    hr = Marshal.QueryInterface(filter, ref iidOld, out iface);
    Console.WriteLine("filt OLD " + H(hr) + " " + iface.ToString("X") + " ==+18? " + (iface == IntPtr.Add(filter, 0x18)));
    if (hr != 0) return hr;

    var get1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface, 3), typeof(Fn1));
    var set1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface, 5), typeof(Fn1));

    Guid handle = Guid.Empty;
    foreach (ushort info in new ushort[] { 2, 1, 3 }) {
      IntPtr data;
      IntPtr coll = MakeGet(Guid.Empty, info, out data);
      hr = get1(iface, coll);
      Guid h = ReadHandle(coll);
      int val = Marshal.ReadInt32(data, 8);
      Console.WriteLine("Get info=" + info + " " + H(hr) + " val=" + val + " h=" + h);
      if (h != Guid.Empty) handle = h;
    }

    // Also try classic Get on sync OLD with filter ctx
    if (sdOld != IntPtr.Zero) {
      var get2 = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sdOld, 3), typeof(Fn2));
      IntPtr data;
      IntPtr coll = MakeGet(handle, 1, out data);
      hr = get2(sdOld, coll, filter);
      Guid h = ReadHandle(coll);
      Console.WriteLine("classic Get " + H(hr) + " val=" + Marshal.ReadInt32(data, 8) + " h=" + h);
      if (h != Guid.Empty) handle = h;
    }

    if (handle == Guid.Empty) {
      handle = new Guid("8FA752F3-70CA-49DC-BF80-58381E02E7F8");
      Console.WriteLine("fallback handle " + handle);
    }

    int[] before = AceVals();
    Ace("before");
    IntPtr setColl = MakeSet(handle, mux, aut, 4);
    hr = set1(iface, setColl);
    Console.WriteLine("wrap Set mux=" + mux + " auto=" + aut + " flags=4 " + H(hr));

    if (hr == 0 && sdOld != IntPtr.Zero) {
      var dop = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sdOld, 6), typeof(Fn2));
      IntPtr op = Z(0x20);
      Marshal.Copy(OP.ToByteArray(), 0, op, 16);
      Marshal.WriteInt16(op, 16, 9);
      Marshal.WriteInt16(op, 18, 2);
      int hrOp = dop(sdOld, op, filter);
      Console.WriteLine("DoOp " + H(hrOp));
    }

    System.Threading.Thread.Sleep(2500);
    int[] after = AceVals();
    Ace("after");
    bool hit = before[0] != after[0] || before[1] != after[1] || before[2] != after[2];
    // also hit if we reached target even if already there
    bool target = after[0] == mux && after[1] == aut;
    Console.WriteLine(hit ? "RESULT HIT" : (target ? "RESULT TARGET_ALREADY" : "RESULT NO_HIT"));
    return hit || target ? 0 : 1;
  }

  static void Main(string[] args) {
    string mode = "optimus";
    bool batch = false;
    bool cycle = false;
    foreach (var a in args) {
      if (a == "--batch") batch = true;
      else if (a == "--cycle") cycle = true;
      else mode = a;
    }
    CoInitializeEx(IntPtr.Zero, 2);
    Ace("start");

    if (cycle) {
      foreach (var m in new[] { "optimus", "auto", "dgpu", "optimus" }) {
        int mux = 1, aut = 0;
        if (m == "dgpu") { mux = 2; aut = 0; }
        if (m == "auto") { mux = 1; aut = 1; }
        Console.WriteLine("\n#### CYCLE " + m + " ####");
        Apply(mux, aut, batch);
        System.Threading.Thread.Sleep(1000);
      }
      return;
    }

    int muxT = 1, autT = 0;
    if (mode == "dgpu") { muxT = 2; autT = 0; }
    if (mode == "auto") { muxT = 1; autT = 1; }
    Environment.ExitCode = Apply(muxT, autT, batch);
  }
}
