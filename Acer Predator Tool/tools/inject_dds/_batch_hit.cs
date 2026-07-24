using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// No-App DDS HIT probe using BatchEngine / NvAppSyncProxy outer paths discovered via Frida.
/// </summary>
class BatchHit {
  static readonly Guid CLSID_AppBatch = new Guid("9C793FCD-5185-47BB-BB30-21750359CA2C");
  static readonly Guid CLSID_SysBatch = new Guid("1DC715B2-9126-4671-8086-299A44543E0F");
  static readonly Guid CLSID_AppSync = new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid CLSID_SysSync = new Guid("DCAB0989-1301-4319-BE5F-ADE89F88581C");
  static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid CLSID_SysFilter = new Guid("5387A36B-6F55-4C66-B085-E18393FCEA87");
  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD = new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static readonly Guid IID_NEW = new Guid("E6AB4158-38B8-4FDF-85CF-ADC2E9870970");
  static readonly Guid IID_ISync_App = new Guid("463FE815-7BC0-4463-9CE4-D8C8BD6EA257");
  static readonly Guid IID_ISync_Sys = new Guid("DC09760E-9FDA-454A-B9D2-7E663E58C39D");
  static readonly Guid IID_ICallFactory = new Guid("0000001B-0000-0000-C000-000000000046");
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
  static void Ace(string t) {
    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine(t + " state=" + k.GetValue("InternalMuxState") + " auto=" + k.GetValue("InternalMuxIsAutomaticMode") + " i2d=" + k.GetValue("ACESwitchedI2D"));
  }
  static string H(int hr) { return "0x" + ((uint)hr).ToString("X8"); }
  static int Qi(IntPtr obj, Guid iid, out IntPtr iface) {
    return Marshal.QueryInterface(obj, ref iid, out iface);
  }
  static void DumpQi(string tag, IntPtr obj) {
    IntPtr p; int hr;
    Guid[] names = { IID_IUnknown, IID_OLD, IID_NEW, IID_ISync_App, IID_ISync_Sys, IID_ICallFactory };
    string[] labs = { "IUnknown", "OLD", "NEW", "ISyncApp", "ISyncSys", "ICallF" };
    for (int i = 0; i < names.Length; i++) {
      hr = Qi(obj, names[i], out p);
      Console.WriteLine(tag + " " + labs[i] + " " + H(hr) + (p == IntPtr.Zero ? "" : " @" + p.ToString("X")));
      if (p != IntPtr.Zero) Marshal.Release(p);
    }
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

  static void TryHit(string name, IntPtr outer, Guid filterClsid, IntPtr sd, string sdTag, int mux, int aut) {
    Console.WriteLine("--- HIT " + name + " sd=" + sdTag + " ---");
    if (outer == IntPtr.Zero || sd == IntPtr.Zero) {
      Console.WriteLine("skip missing outer/sd");
      return;
    }
    int hr;
    IntPtr filter = Create(filterClsid, IID_IUnknown, 0x402, outer, out hr);
    Console.WriteLine("filter " + H(hr) + " " + filter.ToString("X"));
    if (hr != 0) return;

    IntPtr filtOld;
    hr = Qi(filter, IID_OLD, out filtOld);
    Console.WriteLine("filt OLD " + H(hr) + " " + filtOld.ToString("X"));
    IntPtr iface = IntPtr.Add(filter, 0x18);
    Console.WriteLine("cache=" + Marshal.ReadIntPtr(iface, 0x40).ToString("X"));

    Ace("before");
    Guid handle = Guid.Empty;

    // Wrapper Get if available
    if (filtOld != IntPtr.Zero) {
      try {
        var get1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(filtOld, 3), typeof(Fn1));
        IntPtr data;
        IntPtr coll = MakeGet(Guid.Empty, 2, out data);
        hr = get1(filtOld, coll);
        Console.WriteLine("wrap Get info2 " + H(hr) + " val=" + Marshal.ReadInt32(data, 8) + " h=" + ReadHandle(coll));
        coll = MakeGet(Guid.Empty, 1, out data);
        hr = get1(filtOld, coll);
        handle = ReadHandle(coll);
        Console.WriteLine("wrap Get info1 " + H(hr) + " val=" + Marshal.ReadInt32(data, 8) + " h=" + handle);
      } catch (Exception ex) {
        Console.WriteLine("wrap Get EX " + ex.Message);
      }
    }

    // Classic Get via sd
    try {
      var get = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn2));
      IntPtr data;
      IntPtr coll = MakeGet(handle, 1, out data);
      hr = get(sd, coll, filter);
      Guid h2 = ReadHandle(coll);
      if (h2 != Guid.Empty) handle = h2;
      Console.WriteLine("classic Get " + H(hr) + " val=" + Marshal.ReadInt32(data, 8) + " h=" + handle);
    } catch (Exception ex) {
      Console.WriteLine("classic Get EX " + ex.Message);
    }

    if (handle == Guid.Empty) {
      Console.WriteLine("no handle — still trying empty/live");
      handle = new Guid("8FA752F3-70CA-49DC-BF80-58381E02E7F8");
    }

    // Wrapper Set
    if (filtOld != IntPtr.Zero) {
      try {
        var set1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(filtOld, 5), typeof(Fn1));
        foreach (uint fl in new uint[] { 4, 0 }) {
          IntPtr setColl = MakeSet(handle, mux, aut, fl);
          hr = set1(filtOld, setColl);
          Console.WriteLine("wrap Set flags=" + fl + " " + H(hr));
          if (hr == 0) break;
        }
      } catch (Exception ex) {
        Console.WriteLine("wrap Set EX " + ex.Message);
      }
    }

    // Classic Set + DoOp
    try {
      var set = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn2));
      var dop = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn2));
      foreach (uint fl in new uint[] { 4, 0 }) {
        IntPtr setColl = MakeSet(handle, mux, aut, fl);
        hr = set(sd, setColl, filter);
        Console.WriteLine("classic Set flags=" + fl + " " + H(hr));
        if (hr == 0) {
          IntPtr op = Z(0x20);
          Marshal.Copy(OP.ToByteArray(), 0, op, 16);
          Marshal.WriteInt16(op, 16, 9);
          Marshal.WriteInt16(op, 18, 2);
          hr = dop(sd, op, filter);
          Console.WriteLine("DoOp " + H(hr));
          break;
        }
      }
    } catch (Exception ex) {
      Console.WriteLine("classic Set EX " + ex.Message);
    }

    System.Threading.Thread.Sleep(2500);
    Ace("after");
  }

  static void Path(string name, Guid engineClsid, Guid engineIid, uint engineCtx,
                   Guid syncClsid, Guid syncIid, uint syncCtx,
                   Guid filterClsid, string bat, int mux, int aut) {
    Console.WriteLine("\n======== " + name + " ========");
    if (!string.IsNullOrEmpty(bat)) LoadLibrary(bat);
    int hr = -1;
    IntPtr eng = IntPtr.Zero;
    if (engineClsid != Guid.Empty) {
      eng = Create(engineClsid, engineIid, engineCtx, IntPtr.Zero, out hr);
      Console.WriteLine("engine " + H(hr) + " " + eng.ToString("X"));
      if (hr != 0 && engineIid != IID_IUnknown) {
        eng = Create(engineClsid, IID_IUnknown, engineCtx, IntPtr.Zero, out hr);
        Console.WriteLine("engine IUnknown " + H(hr) + " " + eng.ToString("X"));
      }
      if (hr == 0 && eng != IntPtr.Zero) DumpQi("eng", eng);
    }

    IntPtr sync = IntPtr.Zero;
    if (syncClsid != Guid.Empty) {
      sync = Create(syncClsid, syncIid, syncCtx, IntPtr.Zero, out hr);
      Console.WriteLine("sync " + H(hr) + " " + sync.ToString("X"));
      if (hr != 0 && syncIid != IID_IUnknown) {
        sync = Create(syncClsid, IID_IUnknown, syncCtx, IntPtr.Zero, out hr);
        Console.WriteLine("sync IUnknown " + H(hr) + " " + sync.ToString("X"));
      }
      if (hr == 0 && sync != IntPtr.Zero) DumpQi("sync", sync);
    }

    // Prefer outer that has ICallFactory
    IntPtr outer = sync != IntPtr.Zero ? sync : eng;
    IntPtr icf;
    if (outer != IntPtr.Zero) {
      hr = Qi(outer, IID_ICallFactory, out icf);
      Console.WriteLine("outer ICallF " + H(hr));
      if (hr != 0 && eng != IntPtr.Zero && outer != eng) {
        hr = Qi(eng, IID_ICallFactory, out icf);
        Console.WriteLine("eng ICallF " + H(hr));
        if (hr == 0) outer = eng;
      }
    }

    IntPtr sd = IntPtr.Zero; string sdTag = "?";
    IntPtr p;
    if (eng != IntPtr.Zero && Qi(eng, IID_OLD, out p) == 0) { sd = p; sdTag = "engOLD"; }
    else if (sync != IntPtr.Zero && Qi(sync, IID_OLD, out p) == 0) { sd = p; sdTag = "syncOLD"; }
    else if (sync != IntPtr.Zero && Qi(sync, IID_NEW, out p) == 0) { sd = p; sdTag = "syncNEW"; }
    else if (eng != IntPtr.Zero && Qi(eng, IID_NEW, out p) == 0) { sd = p; sdTag = "engNEW"; }
    Console.WriteLine("sd pick " + sdTag + " " + sd.ToString("X"));

    // Try engine as outer if sync lacks OLD (App pattern may use SyncProxy-like outer)
    if (outer != IntPtr.Zero)
      TryHit(name + "/outer", outer, filterClsid, sd, sdTag, mux, aut);

    // Also try eng as outer explicitly if different
    if (eng != IntPtr.Zero && eng != outer)
      TryHit(name + "/engOuter", eng, filterClsid, sd != IntPtr.Zero ? sd : eng, sdTag, mux, aut);
  }

  static void Main(string[] args) {
    string mode = args.Length > 0 ? args[0] : "optimus";
    int mux = 1, aut = 0;
    if (mode == "dgpu") { mux = 2; aut = 0; }
    if (mode == "auto") { mux = 1; aut = 1; }
    CoInitializeEx(IntPtr.Zero, 2); // STA — App BatchEngine is Apartment
    Ace("start");

    string appBat = @"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll";
    string sysBat = @"C:\WINDOWS\System32\DriverStore\FileRepository\nvacsi.inf_amd64_1463ab6df6c1e184\NVXDBat.dll";

    // A) App BatchEngine (OLD) — App Frida sequence
    Path("A AppBatch+AppFilter",
      CLSID_AppBatch, IID_OLD, 0x1,
      Guid.Empty, Guid.Empty, 0,
      CLSID_AppFilter, appBat, mux, aut);

    // B) App BatchEngine + NvAppSyncProxy as outer
    Path("B AppBatch+AppSync+AppFilter",
      CLSID_AppBatch, IID_OLD, 0x1,
      CLSID_AppSync, IID_IUnknown, 0x4,
      CLSID_AppFilter, appBat, mux, aut);

    // C) NvAppSyncProxy alone + AppFilter
    Path("C AppSync+AppFilter",
      Guid.Empty, Guid.Empty, 0,
      CLSID_AppSync, IID_IUnknown, 0x4,
      CLSID_AppFilter, appBat, mux, aut);

    // D) System BatchEngine + SysFilter
    Path("D SysBatch+SysFilter",
      CLSID_SysBatch, IID_NEW, 0x1,
      Guid.Empty, Guid.Empty, 0,
      CLSID_SysFilter, sysBat, mux, aut);

    // E) System Batch + SysSync + SysFilter
    Path("E SysBatch+SysSync+SysFilter",
      CLSID_SysBatch, IID_NEW, 0x1,
      CLSID_SysSync, IID_IUnknown, 0x4,
      CLSID_SysFilter, sysBat, mux, aut);

    Ace("final");
  }
}
