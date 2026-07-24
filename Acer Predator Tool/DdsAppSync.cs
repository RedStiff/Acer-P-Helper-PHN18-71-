using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace PredatorControlApp
{
    /// <summary>
    /// No-App DDS apply via NvAppSyncProxy + App SessionFilter (OLD IStateData wrapper).
    /// Requires NVIDIA App installed (nvxdbat.dll + COM), not NVIDIA App.exe running.
    /// Handle discovery: cache/seed GHI, else elevated nvcontainer mem-scan + GHI.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class DdsAppSync
    {
        private static readonly Guid ClsidAppSync = new("6E435E38-4A67-45C1-9D49-B83A8EDECC8E");
        private static readonly Guid ClsidAppFilter = new("3F6374C2-3540-476A-A123-D1DA2B6DDF86");
        private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");
        private static readonly Guid IidStateDataOld = new("627D7951-9643-4DE6-898F-6C6B766AAB39");

        /// <summary>
        /// Last known-good DDS handle from App session (2026-07-21).
        /// Prefer cache; refresh via mem-scan when GHI fails.
        /// </summary>
        private static readonly Guid SeedDdsHandle = new("747D8BF5-AB15-448B-91C5-52EFEC7C5850");

        private const ushort UxdSettingIdDds = 0x7d;
        private const uint ClsctxLocalServer = 0x4;
        private const uint ClsctxFilter = 0x402;
        private const uint DescriptorFlagsApply = 4;
        private const uint NvidiaErrorInvalidHandle = 0xEAB00003;
        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint TokenQuery = 0x0008;
        private const uint SePrivilegeEnabled = 0x00000002;
        private const uint MemCommit = 0x1000;
        private const uint PageNoAccess = 0x01;
        private const uint PageGuard = 0x100;
        private const string AppNvxdBat =
            @"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvxdbat.dll";
        private const string HandleCacheKeyPath = @"Software\AcerPredatorTool\Gpu";
        private const string HandleCacheValueName = "DdsHandle";

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Fn1(IntPtr self, IntPtr arg);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Fn2(IntPtr self, IntPtr arg, IntPtr ctx);

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public Luid Luid;
            public uint Attributes;
        }

        public readonly struct ApplyResult
        {
            public bool ComOk { get; init; }
            public string Detail { get; init; }
        }

        public static bool IsAvailable(out string detail)
        {
            if (!File.Exists(AppNvxdBat))
            {
                detail = "NVIDIA App nvxdbat.dll missing: " + AppNvxdBat;
                return false;
            }

            string clsidKey =
                @"SOFTWARE\Classes\CLSID\{6E435E38-4A67-45C1-9D49-B83A8EDECC8E}\LocalServer32";
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(clsidKey);
            if (key?.GetValue(null) is not string server || string.IsNullOrWhiteSpace(server))
            {
                detail = "NvAppSyncProxy CLSID not registered";
                return false;
            }

            detail = "NvAppSyncProxy ready (" + server.Trim('"') + ")";
            return true;
        }

        public static ApplyResult TrySetMode(int muxState, bool automatic)
        {
            if (!IsAvailable(out string avail))
                return new ApplyResult { ComOk = false, Detail = avail };

            var allocations = new List<IntPtr>();
            IntPtr sync = IntPtr.Zero;
            IntPtr filter = IntPtr.Zero;
            IntPtr iface = IntPtr.Zero;
            IntPtr sdOld = IntPtr.Zero;

            try
            {
                LoadLibrary(AppNvxdBat);

                Guid clsid = ClsidAppSync;
                Guid iid = IidIUnknown;
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxLocalServer, ref iid, out sync);
                if (hr != 0 || sync == IntPtr.Zero)
                    return new ApplyResult { ComOk = false, Detail = "NvAppSyncProxy CoCreate HR=0x" + Hr(hr) };

                hr = Marshal.QueryInterface(sync, IidStateDataOld, out sdOld);
                if (hr != 0 || sdOld == IntPtr.Zero)
                    return new ApplyResult { ComOk = false, Detail = "NvAppSyncProxy QI OLD HR=0x" + Hr(hr) };

                clsid = ClsidAppFilter;
                iid = IidIUnknown;
                hr = CoCreateInstance(ref clsid, sync, ClsctxFilter, ref iid, out filter);
                if (hr != 0 || filter == IntPtr.Zero)
                    return new ApplyResult { ComOk = false, Detail = "SessionFilter CoCreate HR=0x" + Hr(hr) };

                hr = Marshal.QueryInterface(filter, IidStateDataOld, out iface);
                if (hr != 0 || iface == IntPtr.Zero)
                    return new ApplyResult { ComOk = false, Detail = "Filter QI OLD HR=0x" + Hr(hr) };

                // Prefer cache/seed; empty Get does not mint handles and can destabilize UXD.
                Guid handle = LoadCachedHandle() ?? SeedDdsHandle;
                if (!TryValidateDdsHandle(sdOld, handle, allocations, out string ghiDetail))
                {
                    if (!TryDiscoverDdsHandle(sdOld, allocations, out handle, out string discoverDetail))
                    {
                        return new ApplyResult
                        {
                            ComOk = false,
                            Detail = "DDS handle invalid (" + ghiDetail + "); discover failed ("
                                     + discoverDetail + "). Run elevated (SeDebug) or open NVIDIA App once."
                        };
                    }
                }

                IntPtr setColl = BuildSetCollection(handle, muxState, automatic ? 1 : 0, allocations);
                var set = Marshal.GetDelegateForFunctionPointer<Fn1>(Vtable(iface, 5));
                hr = set(iface, setColl);
                if ((uint)hr == NvidiaErrorInvalidHandle)
                {
                    // Stale handle — discover once and retry Set.
                    if (!TryDiscoverDdsHandle(sdOld, allocations, out handle, out string rediscover)
                        || !TryValidateDdsHandle(sdOld, handle, allocations, out _))
                    {
                        return new ApplyResult
                        {
                            ComOk = false,
                            Detail = "wrapper Set EAB00003; rediscover failed (" + rediscover + ")"
                        };
                    }

                    setColl = BuildSetCollection(handle, muxState, automatic ? 1 : 0, allocations);
                    hr = set(iface, setColl);
                }

                if ((uint)hr == NvidiaErrorInvalidHandle)
                {
                    return new ApplyResult
                    {
                        ComOk = false,
                        Detail = "wrapper Set EAB00003 (stale DDS handle " + handle + ")"
                    };
                }
                if (hr != 0)
                    return new ApplyResult { ComOk = false, Detail = "wrapper Set HR=0x" + Hr(hr) + " handle=" + handle };

                SaveCachedHandle(handle);
                return new ApplyResult
                {
                    ComOk = true,
                    Detail = "NvAppSync wrapper Set OK; handle=" + handle
                };
            }
            catch (Exception ex)
            {
                return new ApplyResult { ComOk = false, Detail = "NvAppSync exception: " + ex.Message };
            }
            finally
            {
                if (iface != IntPtr.Zero) Marshal.Release(iface);
                if (sdOld != IntPtr.Zero) Marshal.Release(sdOld);
                if (filter != IntPtr.Zero) Marshal.Release(filter);
                if (sync != IntPtr.Zero) Marshal.Release(sync);
                // Do not FreeCoTaskMem here: out-of-proc UXD may still touch descriptor
                // payloads briefly after Set returns; early free → heap corruption (0xC0000374).
                // Leak is tiny per switch (~hundreds of bytes).
            }
        }

        /// <summary>
        /// ProcessGetHandleInfo(Handle, HandleInfo*): settingId at +0, handle echo at +12.
        /// Validates only — does not enumerate or create handles.
        /// </summary>
        private static bool TryValidateDdsHandle(IntPtr sdOld, Guid handle, List<IntPtr> allocs, out string detail)
        {
            if (handle == Guid.Empty)
            {
                detail = "empty handle";
                return false;
            }

            IntPtr handleMem = AllocZero(16, allocs);
            IntPtr info = AllocZero(0x40, allocs);
            Marshal.Copy(handle.ToByteArray(), 0, handleMem, 16);
            var ghi = Marshal.GetDelegateForFunctionPointer<Fn2>(Vtable(sdOld, 4));
            int hr = ghi(sdOld, handleMem, info);
            if ((uint)hr == NvidiaErrorInvalidHandle)
            {
                detail = "GHI EAB00003 " + handle;
                return false;
            }
            if (hr != 0)
            {
                detail = "GHI HR=0x" + Hr(hr);
                return false;
            }

            int settingId = Marshal.ReadInt32(info, 0);
            if (settingId != UxdSettingIdDds)
            {
                detail = "GHI settingId=0x" + settingId.ToString("X") + " (want 0x7d) handle=" + handle;
                return false;
            }

            detail = "GHI OK settingId=0x7d handle=" + handle;
            return true;
        }

        /// <summary>
        /// Scan nvcontainer RW memory for DescriptorRaw (sid=0x7d), validate with GHI.
        /// Needs SeDebugPrivilege (admin) — protected containers deny OpenProcess otherwise.
        /// </summary>
        private static bool TryDiscoverDdsHandle(
            IntPtr sdOld, List<IntPtr> allocs, out Guid handle, out string detail)
        {
            handle = Guid.Empty;
            EnableSeDebugPrivilege();

            var candidates = new List<Guid>();
            var seen = new HashSet<Guid>();
            int opened = 0;
            int denied = 0;
            foreach (int pid in EnumContainerPids())
            {
                var list = ScanPidForDdsHandlesList(pid, out bool openedOk);
                if (openedOk) opened++;
                else denied++;
                foreach (Guid g in list)
                {
                    if (seen.Add(g))
                        candidates.Add(g);
                }
            }

            if (candidates.Count == 0)
            {
                detail = "mem-scan empty (opened=" + opened + " denied=" + denied
                         + "; need admin/SeDebug on NvXDCore nvcontainer)";
                return false;
            }

            foreach (Guid g in candidates)
            {
                if (!TryValidateDdsHandle(sdOld, g, allocs, out _))
                    continue;
                handle = g;
                detail = "mem-scan+GHI OK handle=" + g + " cands=" + candidates.Count;
                return true;
            }

            detail = "mem-scan cands=" + candidates.Count + " but none GHI sid=0x7d";
            return false;
        }

        private static IEnumerable<int> EnumContainerPids()
        {
            foreach (var p in Process.GetProcessesByName("nvcontainer"))
                yield return p.Id;
            foreach (var p in Process.GetProcessesByName("NVDisplay.Container"))
                yield return p.Id;
        }

        private static List<Guid> ScanPidForDdsHandlesList(int pid, out bool opened)
        {
            opened = false;
            var found = new List<Guid>();
            uint access = ProcessVmRead | ProcessQueryInformation | ProcessQueryLimitedInformation;
            IntPtr proc = OpenProcess(access, false, pid);
            if (proc == IntPtr.Zero)
                return found;

            opened = true;
            try
            {
                if (!ProcessLoadsUxdModules(proc))
                    return found;

                IntPtr addr = IntPtr.Zero;
                int regions = 0;
                int hits = 0;
                var seen = new HashSet<Guid>();
                while (regions < 5000 && hits <= 40)
                {
                    if (VirtualQueryEx(proc, addr, out MemoryBasicInformation mbi,
                            Marshal.SizeOf<MemoryBasicInformation>()) == 0)
                        break;

                    ulong size = mbi.RegionSize.ToUInt64();
                    bool readable = mbi.State == MemCommit
                                    && (mbi.Protect & PageNoAccess) == 0
                                    && (mbi.Protect & PageGuard) == 0
                                    && size > 0
                                    && size <= 64UL * 1024 * 1024;
                    if (readable)
                    {
                        regions++;
                        int chunk = (int)Math.Min(size, 1024 * 1024);
                        byte[] buf = new byte[chunk];
                        long offset = 0;
                        while (offset + 24 < (long)size && hits <= 40)
                        {
                            int toRead = (int)Math.Min(chunk, (long)size - offset);
                            IntPtr cur = new IntPtr(mbi.BaseAddress.ToInt64() + offset);
                            if (!ReadProcessMemory(proc, cur, buf, toRead, out int read) || read < 24)
                                break;

                            for (int i = 0; i + 24 <= read; i++)
                            {
                                if (buf[i + 18] != 0x7d || buf[i + 19] != 0)
                                    continue;
                                byte flags = buf[i + 20];
                                if (!(flags == 4 || flags == 1)
                                    || buf[i + 21] != 0 || buf[i + 22] != 0 || buf[i + 23] != 0)
                                    continue;
                                ushort infoId = (ushort)(buf[i + 16] | (buf[i + 17] << 8));
                                if (infoId < 1 || infoId > 4)
                                    continue;
                                byte[] gb = new byte[16];
                                Buffer.BlockCopy(buf, i, gb, 0, 16);
                                var g = new Guid(gb);
                                if (g == Guid.Empty || !LooksLikeUuidV4(g))
                                    continue;
                                if (!seen.Add(g))
                                    continue;
                                found.Add(g);
                                hits++;
                            }

                            offset += Math.Max(1, read - 23);
                        }
                    }

                    ulong next = (ulong)mbi.BaseAddress.ToInt64() + size;
                    if (next <= (ulong)addr.ToInt64())
                        break;
                    addr = new IntPtr((long)next);
                }
            }
            finally
            {
                CloseHandle(proc);
            }

            return found;
        }

        private static bool ProcessLoadsUxdModules(IntPtr proc)
        {
            try
            {
                var mods = new IntPtr[512];
                if (!EnumProcessModules(proc, mods, mods.Length * IntPtr.Size, out int needed))
                    return true;
                int count = Math.Min(needed / IntPtr.Size, mods.Length);
                var sb = new StringBuilder(260);
                for (int i = 0; i < count; i++)
                {
                    sb.Clear();
                    if (GetModuleBaseName(proc, mods[i], sb, sb.Capacity) == 0)
                        continue;
                    string n = sb.ToString();
                    if (n.Contains("NvXDCore", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("nvxdbat", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("nvxdplcy", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool LooksLikeUuidV4(Guid g)
        {
            byte[] b = g.ToByteArray();
            int version = (b[7] >> 4) & 0xF;
            return version == 4;
        }

        private static void EnableSeDebugPrivilege()
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out IntPtr token))
                return;
            try
            {
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out Luid luid))
                    return;
                var tp = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SePrivilegeEnabled
                };
                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(token);
            }
        }

        private static Guid? LoadCachedHandle()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(HandleCacheKeyPath);
                if (key?.GetValue(HandleCacheValueName) is string s && Guid.TryParse(s, out Guid g) && g != Guid.Empty)
                    return g;
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static void SaveCachedHandle(Guid handle)
        {
            if (handle == Guid.Empty)
                return;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(HandleCacheKeyPath);
                key?.SetValue(HandleCacheValueName, handle.ToString("D"));
            }
            catch
            {
                // ignore
            }
        }

        private static IntPtr BuildSetCollection(Guid handle, int mux, int automatic, List<IntPtr> allocs)
        {
            IntPtr coll = AllocZero(0x20, allocs);
            IntPtr items = AllocZero(0x50, allocs);
            IntPtr dMux = AllocZero(0x20, allocs);
            IntPtr dAuto = AllocZero(0x20, allocs);
            Marshal.WriteInt32(dMux, 0, 3);
            Marshal.WriteInt32(dMux, 4, 4);
            Marshal.WriteInt32(dMux, 8, mux);
            Marshal.WriteInt32(dAuto, 0, 5);
            Marshal.WriteInt32(dAuto, 4, 1);
            Marshal.WriteInt32(dAuto, 8, automatic);
            byte[] hb = handle.ToByteArray();
            Marshal.Copy(hb, 0, items, 16);
            Marshal.WriteInt16(items, 16, 1);
            Marshal.WriteInt16(items, 18, (short)UxdSettingIdDds);
            Marshal.WriteInt32(items, 20, (int)DescriptorFlagsApply);
            Marshal.WriteIntPtr(items, 24, dMux);
            IntPtr autoDesc = IntPtr.Add(items, 0x20);
            Marshal.Copy(hb, 0, autoDesc, 16);
            Marshal.WriteInt16(autoDesc, 16, 3);
            Marshal.WriteInt16(autoDesc, 18, (short)UxdSettingIdDds);
            Marshal.WriteInt32(autoDesc, 20, (int)DescriptorFlagsApply);
            Marshal.WriteIntPtr(autoDesc, 24, dAuto);
            Marshal.WriteIntPtr(coll, 0, items);
            Marshal.WriteInt64(coll, 8, 2);
            return coll;
        }

        private static IntPtr AllocZero(int size, List<IntPtr> allocs)
        {
            IntPtr p = Marshal.AllocCoTaskMem(size);
            for (int i = 0; i < size; i++)
                Marshal.WriteByte(p, i, 0);
            allocs.Add(p);
            return p;
        }

        private static IntPtr Vtable(IntPtr obj, int index) =>
            Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size);

        private static string Hr(int hr) => ((uint)hr).ToString("X8");

        [DllImport("ole32")]
        private static extern int CoCreateInstance(
            ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

        [DllImport("kernel32", SetLastError = true)]
        private static extern int VirtualQueryEx(
            IntPtr process, IntPtr address, out MemoryBasicInformation mbi, int length);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("kernel32")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

        [DllImport("advapi32", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr token, bool disable, ref TokenPrivileges neu, int length, IntPtr prev, IntPtr retLen);

        [DllImport("psapi", SetLastError = true)]
        private static extern bool EnumProcessModules(
            IntPtr process, IntPtr[] modules, int size, out int needed);

        [DllImport("psapi", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetModuleBaseName(
            IntPtr process, IntPtr module, StringBuilder name, int size);
    }
}
