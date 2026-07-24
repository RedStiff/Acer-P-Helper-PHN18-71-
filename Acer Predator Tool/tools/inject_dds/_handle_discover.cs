using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// After confirming GetHandleInfo is validate-only, try other no-App handle discovery.
/// </summary>
class HandleDiscover {
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
  static void Dump(IntPtr p, int n, string t) {
    if (p == IntPtr.Zero) { Console.WriteLine(t + " null"); return; }
    byte[] b = new byte[n];
    try { Marshal.Copy(p, b, 0, n); } catch { Console.WriteLine(t + " fail"); return; }
    Console.Write(t + " ");
    for (int i = 0; i < n; i++) Console.Write(b[i].ToString("x2") + ((i % 16 == 15) ? "\n     " : " "));
    Console.WriteLine();
    // try extract guid at 0 and at 12
    if (n >= 16) {
      byte[] g = new byte[16];
      Buffer.BlockCopy(b, 0, g, 0, 16);
      Console.WriteLine(t + " guid0=" + new Guid(g));
    }
    if (n >= 28) {
      byte[] g = new byte[16];
      Buffer.BlockCopy(b, 12, g, 0, 16);
      Console.WriteLine(t + " guid12=" + new Guid(g));
    }
  }

  static void Main() {
    CoInitializeEx(IntPtr.Zero, 2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");

    // optional batch
    Guid bc = CLSID_AppBatch, iu = IID_IUnknown, old = IID_OLD;
    IntPtr batch; int hr = CoCreateInstance(ref bc, IntPtr.Zero, 1, ref old, out batch);
    Console.WriteLine("batch OLD " + H(hr) + " " + batch.ToString("X"));

    Guid sc = CLSID_AppSync;
    IntPtr sync; hr = CoCreateInstance(ref sc, IntPtr.Zero, 4, ref iu, out sync);
    Console.WriteLine("sync " + H(hr));
    IntPtr sd; old = IID_OLD; hr = Marshal.QueryInterface(sync, ref old, out sd);
    Console.WriteLine("sd OLD " + H(hr));

    Guid fc = CLSID_AppFilter;
    IntPtr filter; hr = CoCreateInstance(ref fc, sync, 0x402, ref iu, out filter);
    Console.WriteLine("filter " + H(hr));
    IntPtr iface; old = IID_OLD; Marshal.QueryInterface(filter, ref old, out iface);

    var ghi = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn2));
    var get = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn2));
    var set = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn2));
    var dop = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 6), typeof(Fn2));
    var get1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface, 3), typeof(Fn1));
    var set1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface, 5), typeof(Fn1));

    // 1) GHI empty handle
    IntPtr empty = Z(16), info = Z(0x80);
    hr = ghi(sd, empty, info);
    Console.WriteLine("GHI empty " + H(hr)); Dump(info, 0x40, "info");

    // 2) GHI with OP guid as handle?
    IntPtr opH = Z(16); Marshal.Copy(OP.ToByteArray(), 0, opH, 16);
    for (int i = 0; i < 0x80; i++) Marshal.WriteByte(info, i, 0);
    hr = ghi(sd, opH, info);
    Console.WriteLine("GHI opguid " + H(hr)); Dump(info, 0x40, "info");

    // 3) DoOp variants that might register/create
    ushort[,] ops = {
      {9,2},{1,0},{1,1},{1,2},{2,0},{2,1},{3,0},{3,1},{4,0},{5,0},{6,0},{7,0},{8,0},{9,0},{9,1},{9,3},{10,0},{0,0}
    };
    for (int i = 0; i < ops.GetLength(0); i++) {
      IntPtr op = Z(0x20);
      Marshal.Copy(OP.ToByteArray(), 0, op, 16);
      Marshal.WriteInt16(op, 16, (short)ops[i, 0]);
      Marshal.WriteInt16(op, 18, (short)ops[i, 1]);
      hr = dop(sd, op, filter);
      if (hr == 0 || (uint)hr == 0xEAB00003 || hr != unchecked((int)0x8000FFFF))
        Console.WriteLine("DoOp " + ops[i, 0] + "," + ops[i, 1] + " " + H(hr));
    }

    // 4) Get with empty handle, various flags / info, hope server fills handle
    foreach (ushort infoId in new ushort[] { 0, 1, 2, 3, 4, 5 }) {
      foreach (uint flags in new uint[] { 0, 1, 2, 4, 8, 0x10, 0x100 }) {
        IntPtr coll = Z(0x20), items = Z(0x40), data = Z(0x40);
        Marshal.WriteInt16(items, 16, (short)infoId);
        Marshal.WriteInt16(items, 18, (short)SID);
        Marshal.WriteInt32(items, 20, (int)flags);
        Marshal.WriteIntPtr(items, 24, data);
        Marshal.WriteInt32(data, 0, 3); Marshal.WriteInt32(data, 4, 4);
        Marshal.WriteIntPtr(coll, 0, items); Marshal.WriteInt64(coll, 8, 1);
        hr = get1(iface, coll);
        Guid hg = Guid.Empty;
        byte[] gb = new byte[16]; Marshal.Copy(items, gb, 0, 16); hg = new Guid(gb);
        if (hr == 0 && hg != Guid.Empty) {
          Console.WriteLine("GET FILL info=" + infoId + " flags=" + flags + " h=" + hg + " val=" + Marshal.ReadInt32(data, 8));
        }
        // also classic
        hr = get(sd, coll, filter);
        Marshal.Copy(items, gb, 0, 16); hg = new Guid(gb);
        if (hr == 0 && hg != Guid.Empty) {
          Console.WriteLine("GET2 FILL info=" + infoId + " flags=" + flags + " h=" + hg);
        }
      }
    }

    // 5) Get with count=0 — ask server to populate collection?
    IntPtr coll0 = Z(0x20), items0 = Z(0x200);
    Marshal.WriteIntPtr(coll0, 0, items0);
    Marshal.WriteInt64(coll0, 8, 0);
    hr = get1(iface, coll0);
    Console.WriteLine("GET count0 wrap " + H(hr) + " count=" + Marshal.ReadInt64(coll0, 8));
    hr = get(sd, coll0, filter);
    Console.WriteLine("GET count0 classic " + H(hr) + " count=" + Marshal.ReadInt64(coll0, 8));
    Dump(Marshal.ReadIntPtr(coll0, 0) == IntPtr.Zero ? items0 : Marshal.ReadIntPtr(coll0, 0), 0x40, "items0");

    // 6) If batch is StateData, try Get/GHI on batch
    if (batch != IntPtr.Zero) {
      try {
        for (int i = 0; i < 0x80; i++) Marshal.WriteByte(info, i, 0);
        hr = ((Fn2)Marshal.GetDelegateForFunctionPointer(Vt(batch, 4), typeof(Fn2)))(batch, empty, info);
        Console.WriteLine("batch GHI empty " + H(hr)); Dump(info, 0x40, "binfo");
      } catch (Exception ex) { Console.WriteLine("batch GHI " + ex.Message); }
    }

    Console.WriteLine("done");
  }
}
