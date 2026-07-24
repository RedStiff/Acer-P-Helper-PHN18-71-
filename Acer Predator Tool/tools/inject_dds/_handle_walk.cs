using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// Walk UXD handle tree from root DoOp handle; GHI each child until SettingId==0x7d.
/// </summary>
class HandleWalk {
  static readonly Guid CLSID_AppSync = new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD = new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  static readonly Guid ROOT = new Guid("D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD");
  const ushort SID_DDS = 0x7d;

  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p, uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c, IntPtr o, uint ctx, ref Guid i, out IntPtr p);
  [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn2(IntPtr s, IntPtr a, IntPtr b);

  static IntPtr Vt(IntPtr o, int i) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size); }
  static IntPtr Z(int n) {
    var p = Marshal.AllocCoTaskMem(n);
    for (int i = 0; i < n; i++) Marshal.WriteByte(p, i, 0);
    return p;
  }
  static string H(int hr) { return "0x" + ((uint)hr).ToString("X8"); }

  static int Ghi(Fn2 ghi, IntPtr sd, Guid handle, out int settingId, out byte[] raw) {
    IntPtr hMem = Z(16), info = Z(0x80);
    Marshal.Copy(handle.ToByteArray(), 0, hMem, 16);
    int hr = ghi(sd, hMem, info);
    raw = new byte[0x40];
    Marshal.Copy(info, raw, 0, 0x40);
    settingId = Marshal.ReadInt32(info, 0);
    return hr;
  }

  static int GetOne(Fn2 get, IntPtr sd, IntPtr filter, Guid handle, ushort infoId, ushort sid, uint flags,
                    out Guid outHandle, out int type, out int size, out byte[] payload) {
    IntPtr coll = Z(0x20), items = Z(0x40), data = Z(0x100);
    Marshal.Copy(handle.ToByteArray(), 0, items, 16);
    Marshal.WriteInt16(items, 16, (short)infoId);
    Marshal.WriteInt16(items, 18, (short)sid);
    Marshal.WriteInt32(items, 20, (int)flags);
    Marshal.WriteIntPtr(items, 24, data);
    // pre-size guess
    Marshal.WriteInt32(data, 0, 0);
    Marshal.WriteInt32(data, 4, 0);
    Marshal.WriteIntPtr(coll, 0, items);
    Marshal.WriteInt64(coll, 8, 1);
    int hr = get(sd, coll, filter);
    byte[] hb = new byte[16];
    Marshal.Copy(items, hb, 0, 16);
    outHandle = new Guid(hb);
    type = Marshal.ReadInt32(data, 0);
    size = Marshal.ReadInt32(data, 4);
    payload = new byte[Math.Min(Math.Max(size, 16), 0x80)];
    try { Marshal.Copy(IntPtr.Add(data, 8), payload, 0, payload.Length); } catch { }
    return hr;
  }

  static List<Guid> ExtractHandles(byte[] payload, int size) {
    var list = new List<Guid>();
    if (payload == null || size < 16) return list;
    int n = size / 16;
    for (int i = 0; i < n; i++) {
      byte[] g = new byte[16];
      Buffer.BlockCopy(payload, i * 16, g, 0, 16);
      var guid = new Guid(g);
      if (guid != Guid.Empty) list.Add(guid);
    }
    return list;
  }

  static void Ace() {
    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine("ACE state=" + k.GetValue("InternalMuxState") + " auto=" + k.GetValue("InternalMuxIsAutomaticMode"));
  }

  static void Main() {
    CoInitializeEx(IntPtr.Zero, 2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");
    Guid c = CLSID_AppSync, iu = IID_IUnknown;
    IntPtr sync; CoCreateInstance(ref c, IntPtr.Zero, 4, ref iu, out sync);
    Guid old = IID_OLD; IntPtr sd; Marshal.QueryInterface(sync, ref old, out sd);
    Guid fc = CLSID_AppFilter; IntPtr filter; CoCreateInstance(ref fc, sync, 0x402, ref iu, out filter);

    var ghi = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn2));
    var get = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 3), typeof(Fn2));
    var set = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 5), typeof(Fn2));

    int sidRoot; byte[] raw;
    Console.WriteLine("GHI root " + H(Ghi(ghi, sd, ROOT, out sidRoot, out raw)) + " settingId=" + sidRoot);
    Dump(raw, "rootinfo");

    var queue = new Queue<Guid>();
    var seen = new HashSet<Guid>();
    queue.Enqueue(ROOT);
    Guid? dds = null;

    // Also seed with other handles seen in App capture
    foreach (var s in new[] {
      "E411FB35-B251-445B-91E7-3463884E2BAD",
      "5FD294CF-CB81-4E69-80FC-BE10585A7AAA",
      "397A4A3D-65B1-4E64-854F-9CA153B56451"
    }) queue.Enqueue(new Guid(s));

    int steps = 0;
    while (queue.Count > 0 && steps < 80) {
      Guid h = queue.Dequeue();
      if (!seen.Add(h)) continue;
      steps++;
      int sid; byte[] infoRaw;
      int hr = Ghi(ghi, sd, h, out sid, out infoRaw);
      Console.WriteLine("GHI " + h + " " + H(hr) + " sid=" + sid + "/0x" + sid.ToString("X"));
      if (hr != 0) continue;
      if (sid == SID_DDS) {
        Console.WriteLine("FOUND DDS HANDLE " + h);
        dds = h;
        break;
      }

      // Probe Get for this handle: use its setting id, various infoIds
      ushort setting = (ushort)(sid & 0xffff);
      if (setting == 0) continue;
      foreach (ushort infoId in new ushort[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }) {
        Guid oh; int type, size; byte[] payload;
        hr = GetOne(get, sd, filter, h, infoId, setting, 4, out oh, out type, out size, out payload);
        if (hr != 0) continue;
        Console.WriteLine("  Get info=" + infoId + " type=" + type + " size=" + size + " " + H(hr));
        if (type == 1 && size >= 16) {
          foreach (var child in ExtractHandles(payload, size)) {
            Console.WriteLine("    child handle " + child);
            if (!seen.Contains(child)) queue.Enqueue(child);
          }
        }
        // type may be vector: sometimes size=N*16 with type other than 1
        if (size >= 16 && size % 16 == 0 && type != 3 && type != 5 && type != 7) {
          foreach (var child in ExtractHandles(payload, size)) {
            if (child != h && !seen.Contains(child)) {
              Console.WriteLine("    maybe handle " + child + " (type=" + type + ")");
              queue.Enqueue(child);
            }
          }
        }
      }
    }

    if (dds == null) {
      Console.WriteLine("DDS not found via walk");
      Ace();
      return;
    }

    // Verify Set HIT with discovered handle
    Ace();
    IntPtr setColl = Z(0x20), setItems = Z(0x50), dMux = Z(0x20), dAuto = Z(0x20);
    Marshal.WriteInt32(dMux, 0, 3); Marshal.WriteInt32(dMux, 4, 4); Marshal.WriteInt32(dMux, 8, 1); // optimus
    Marshal.WriteInt32(dAuto, 0, 5); Marshal.WriteInt32(dAuto, 4, 1); Marshal.WriteInt32(dAuto, 8, 0);
    byte[] hb = dds.Value.ToByteArray();
    Marshal.Copy(hb, 0, setItems, 16);
    Marshal.WriteInt16(setItems, 16, 1); Marshal.WriteInt16(setItems, 18, (short)SID_DDS);
    Marshal.WriteInt32(setItems, 20, 4); Marshal.WriteIntPtr(setItems, 24, dMux);
    IntPtr d1 = IntPtr.Add(setItems, 0x20);
    Marshal.Copy(hb, 0, d1, 16);
    Marshal.WriteInt16(d1, 16, 3); Marshal.WriteInt16(d1, 18, (short)SID_DDS);
    Marshal.WriteInt32(d1, 20, 4); Marshal.WriteIntPtr(d1, 24, dAuto);
    Marshal.WriteIntPtr(setColl, 0, setItems); Marshal.WriteInt64(setColl, 8, 2);
    int hrSet = set(sd, setColl, filter);
    Console.WriteLine("classic Set optimus " + H(hrSet));
    // also try via filter wrapper — need iface
    System.Threading.Thread.Sleep(2000);
    Ace();
  }

  static void Dump(byte[] b, string t) {
    Console.Write(t + " ");
    for (int i = 0; i < b.Length; i++) Console.Write(b[i].ToString("x2") + " ");
    Console.WriteLine();
  }
}
