using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

/// <summary>
/// No-App DDS handle discovery: CoCreate NvAppSyncProxy, scan nvcontainer
/// memory for DescriptorRaw with sid=0x7d, validate via ProcessGetHandleInfo.
/// Requires SeDebugPrivilege (or admin) to OpenProcess protected nvcontainer PIDs.
/// </summary>
class MemHandleDiscover {
  static readonly Guid CLSID_AppSync = new Guid("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
  static readonly Guid CLSID_AppFilter = new Guid("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
  static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
  static readonly Guid IID_OLD = new Guid("627D7951-9643-4DE6-898F-6C6B766AAB39");
  const ushort SID_DDS = 0x7d;
  const uint PROCESS_VM_READ = 0x0010;
  const uint PROCESS_QUERY_INFORMATION = 0x0400;
  const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
  const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
  const uint TOKEN_QUERY = 0x0008;
  const uint SE_PRIVILEGE_ENABLED = 0x00000002;

  [DllImport("ole32")] static extern int CoInitializeEx(IntPtr p, uint f);
  [DllImport("ole32")] static extern int CoCreateInstance(ref Guid c, IntPtr o, uint ctx, ref Guid i, out IntPtr p);
  [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
  [DllImport("kernel32", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
  [DllImport("kernel32", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr proc, IntPtr addr, byte[] buf, int size, out int read);
  [DllImport("kernel32", SetLastError = true)]
  static extern int VirtualQueryEx(IntPtr proc, IntPtr addr, out MEMORY_BASIC_INFORMATION mbi, int len);
  [DllImport("kernel32", SetLastError = true)]
  static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
  [DllImport("kernel32")] static extern IntPtr GetCurrentProcess();
  [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
  static extern bool LookupPrivilegeValue(string system, string name, out LUID luid);
  [DllImport("advapi32", SetLastError = true)]
  static extern bool AdjustTokenPrivileges(IntPtr token, bool disable, ref TOKEN_PRIVILEGES neu, int len, IntPtr prev, IntPtr retLen);
  [DllImport("psapi", SetLastError = true)]
  static extern bool EnumProcessModules(IntPtr proc, IntPtr[] mods, int size, out int needed);
  [DllImport("psapi", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern uint GetModuleBaseName(IntPtr proc, IntPtr mod, StringBuilder name, int size);

  [StructLayout(LayoutKind.Sequential)]
  struct MEMORY_BASIC_INFORMATION {
    public IntPtr BaseAddress;
    public IntPtr AllocationBase;
    public uint AllocationProtect;
    public UIntPtr RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct LUID { public uint LowPart; public int HighPart; }

  [StructLayout(LayoutKind.Sequential)]
  struct TOKEN_PRIVILEGES {
    public uint PrivilegeCount;
    public LUID Luid;
    public uint Attributes;
  }

  const uint MEM_COMMIT = 0x1000;
  const uint PAGE_NOACCESS = 0x01;
  const uint PAGE_GUARD = 0x100;

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn1(IntPtr s, IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  delegate int Fn2(IntPtr s, IntPtr a, IntPtr b);

  static IntPtr Vt(IntPtr o, int i) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size); }
  static IntPtr Z(int n) { var p = Marshal.AllocCoTaskMem(n); for (int i = 0; i < n; i++) Marshal.WriteByte(p, i, 0); return p; }
  static string H(int hr) { return "0x" + ((uint)hr).ToString("X8"); }

  static void Ace(string t) {
    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE"))
      Console.WriteLine(t + " state=" + k.GetValue("InternalMuxState") + " auto=" + k.GetValue("InternalMuxIsAutomaticMode") + " i2d=" + k.GetValue("ACESwitchedI2D"));
  }

  static bool EnableSeDebug() {
    IntPtr token;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
      return false;
    try {
      LUID luid;
      if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out luid))
        return false;
      TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES {
        PrivilegeCount = 1,
        Luid = luid,
        Attributes = SE_PRIVILEGE_ENABLED
      };
      return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
    } finally {
      CloseHandle(token);
    }
  }

  static int Ghi(Fn2 ghi, IntPtr sd, Guid handle, out int settingId) {
    IntPtr hm = Z(16), info = Z(0x40);
    Marshal.Copy(handle.ToByteArray(), 0, hm, 16);
    int hr = ghi(sd, hm, info);
    settingId = Marshal.ReadInt32(info, 0);
    Marshal.FreeCoTaskMem(hm);
    Marshal.FreeCoTaskMem(info);
    return hr;
  }

  /// <summary>Prefer RFC4122 version-4 GUIDs (App/UXD DDS handles observed as such).</summary>
  static bool LooksLikeUuidV4(Guid g) {
    byte[] b = g.ToByteArray();
    // Guid layout: time_hi_and_version at bytes 6-7; version in high nibble of byte 7
    int version = (b[7] >> 4) & 0xF;
    return version == 4;
  }

  static IEnumerable<int> CandidatePids() {
    foreach (var p in Process.GetProcessesByName("nvcontainer"))
      yield return p.Id;
    foreach (var p in Process.GetProcessesByName("NVDisplay.Container"))
      yield return p.Id;
  }

  static bool ProcessLoadsUxd(IntPtr proc) {
    try {
      IntPtr[] mods = new IntPtr[512];
      int needed;
      if (!EnumProcessModules(proc, mods, mods.Length * IntPtr.Size, out needed))
        return true; // cannot enum — still scan
      int count = needed / IntPtr.Size;
      var sb = new StringBuilder(260);
      for (int i = 0; i < count && i < mods.Length; i++) {
        sb.Clear();
        if (GetModuleBaseName(proc, mods[i], sb, sb.Capacity) == 0) continue;
        string n = sb.ToString();
        if (n.IndexOf("NvXDCore", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("nvxdbat", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("nvxdplcy", StringComparison.OrdinalIgnoreCase) >= 0)
          return true;
      }
      return false;
    } catch {
      return true;
    }
  }

  static List<Guid> ScanPidForDdsHandles(int pid) {
    var found = new List<Guid>();
    var seen = new HashSet<Guid>();
    uint access = PROCESS_VM_READ | PROCESS_QUERY_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION;
    IntPtr proc = OpenProcess(access, false, pid);
    if (proc == IntPtr.Zero) {
      Console.WriteLine("OpenProcess " + pid + " failed err=" + Marshal.GetLastWin32Error());
      return found;
    }
    try {
      bool uxd = ProcessLoadsUxd(proc);
      Console.WriteLine("pid " + pid + " uxdModules=" + uxd);
      if (!uxd) return found;

      IntPtr addr = IntPtr.Zero;
      int regions = 0, hits = 0;
      while (regions < 5000) {
        MEMORY_BASIC_INFORMATION mbi;
        int q = VirtualQueryEx(proc, addr, out mbi, Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION)));
        if (q == 0) break;
        ulong size = mbi.RegionSize.ToUInt64();
        bool readable = mbi.State == MEM_COMMIT
          && (mbi.Protect & PAGE_NOACCESS) == 0
          && (mbi.Protect & PAGE_GUARD) == 0
          && size > 0 && size <= 64UL * 1024 * 1024;
        if (readable) {
          regions++;
          int chunk = (int)Math.Min(size, 1024 * 1024);
          byte[] buf = new byte[chunk];
          long offset = 0;
          while (offset + 24 < (long)size) {
            int toRead = (int)Math.Min(chunk, (long)size - offset);
            int read;
            IntPtr cur = new IntPtr(mbi.BaseAddress.ToInt64() + offset);
            if (!ReadProcessMemory(proc, cur, buf, toRead, out read) || read < 24) break;
            // DescriptorRaw: [guid:16][infoId:2][sid:2][flags:4]
            for (int i = 0; i + 24 <= read; i++) {
              if (buf[i + 18] != 0x7d || buf[i + 19] != 0x00) continue;
              byte flags = buf[i + 20];
              if (!(flags == 4 || flags == 1) || buf[i + 21] != 0 || buf[i + 22] != 0 || buf[i + 23] != 0)
                continue;
              ushort infoId = (ushort)(buf[i + 16] | (buf[i + 17] << 8));
              if (infoId < 1 || infoId > 4) continue;
              byte[] gb = new byte[16];
              Buffer.BlockCopy(buf, i, gb, 0, 16);
              Guid g = new Guid(gb);
              if (g == Guid.Empty) continue;
              if (!LooksLikeUuidV4(g)) continue;
              if (seen.Add(g)) {
                found.Add(g);
                hits++;
                Console.WriteLine("  cand " + g + " info=" + infoId + " flags=" + flags + " @" + pid);
              }
            }
            offset += Math.Max(1, read - 23);
            if (hits > 40) break;
          }
        }
        ulong next = (ulong)mbi.BaseAddress.ToInt64() + size;
        if (next <= (ulong)addr.ToInt64()) break;
        addr = new IntPtr((long)next);
        if (hits > 40) break;
      }
      Console.WriteLine("pid " + pid + " regions~" + regions + " unique=" + found.Count);
    } finally {
      CloseHandle(proc);
    }
    return found;
  }

  static void Main(string[] args) {
    int mux = 1, aut = 0;
    string mode = args.Length > 0 ? args[0] : "optimus";
    if (mode == "dgpu") { mux = 2; aut = 0; }
    if (mode == "auto") { mux = 1; aut = 1; }

    bool dbg = EnableSeDebug();
    Console.WriteLine("SeDebugPrivilege enabled=" + dbg + " err=" + Marshal.GetLastWin32Error());

    CoInitializeEx(IntPtr.Zero, 2);
    LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll");

    Guid c = CLSID_AppSync, iu = IID_IUnknown;
    IntPtr sync; int hr = CoCreateInstance(ref c, IntPtr.Zero, 4, ref iu, out sync);
    Console.WriteLine("AppSync " + H(hr));
    Guid old = IID_OLD; IntPtr sd; Marshal.QueryInterface(sync, ref old, out sd);
    var ghi = (Fn2)Marshal.GetDelegateForFunctionPointer(Vt(sd, 4), typeof(Fn2));

    var candidates = new List<Guid>();
    var seen = new HashSet<Guid>();
    foreach (int pid in CandidatePids()) {
      Console.WriteLine("scan pid " + pid);
      foreach (var g in ScanPidForDdsHandles(pid)) {
        if (seen.Add(g)) candidates.Add(g);
      }
    }
    Console.WriteLine("candidates=" + candidates.Count);

    Guid? dds = null;
    foreach (var g in candidates) {
      int sid;
      hr = Ghi(ghi, sd, g, out sid);
      Console.WriteLine("GHI " + g + " " + H(hr) + " sid=0x" + sid.ToString("X"));
      if (hr == 0 && sid == SID_DDS) {
        dds = g;
        Console.WriteLine("DISCOVERED " + g);
        break;
      }
    }

    if (dds == null) {
      Console.WriteLine("NO_HANDLE");
      Environment.ExitCode = 2;
      return;
    }

    Guid fc = CLSID_AppFilter;
    IntPtr filter; hr = CoCreateInstance(ref fc, sync, 0x402, ref iu, out filter);
    Console.WriteLine("filter " + H(hr));
    IntPtr iface; old = IID_OLD; Marshal.QueryInterface(filter, ref old, out iface);
    var set1 = (Fn1)Marshal.GetDelegateForFunctionPointer(Vt(iface, 5), typeof(Fn1));

    IntPtr coll = Z(0x20), items = Z(0x50), dMux = Z(0x20), dAuto = Z(0x20);
    Marshal.WriteInt32(dMux, 0, 3); Marshal.WriteInt32(dMux, 4, 4); Marshal.WriteInt32(dMux, 8, mux);
    Marshal.WriteInt32(dAuto, 0, 5); Marshal.WriteInt32(dAuto, 4, 1); Marshal.WriteInt32(dAuto, 8, aut);
    byte[] hb = dds.Value.ToByteArray();
    Marshal.Copy(hb, 0, items, 16);
    Marshal.WriteInt16(items, 16, 1); Marshal.WriteInt16(items, 18, (short)SID_DDS);
    Marshal.WriteInt32(items, 20, 4); Marshal.WriteIntPtr(items, 24, dMux);
    IntPtr d1 = IntPtr.Add(items, 0x20);
    Marshal.Copy(hb, 0, d1, 16);
    Marshal.WriteInt16(d1, 16, 3); Marshal.WriteInt16(d1, 18, (short)SID_DDS);
    Marshal.WriteInt32(d1, 20, 4); Marshal.WriteIntPtr(d1, 24, dAuto);
    Marshal.WriteIntPtr(coll, 0, items); Marshal.WriteInt64(coll, 8, 2);

    Ace("before");
    hr = set1(iface, coll);
    Console.WriteLine("Set mux=" + mux + " auto=" + aut + " " + H(hr));
    System.Threading.Thread.Sleep(2500);
    Ace("after");

    try {
      using (var k = Registry.CurrentUser.CreateSubKey(@"Software\AcerPredatorTool\Gpu"))
        k.SetValue("DdsHandle", dds.Value.ToString("D"));
      Console.WriteLine("cached " + dds.Value);
    } catch { }
  }
}
