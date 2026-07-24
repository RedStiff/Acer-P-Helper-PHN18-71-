using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace PredatorControlApp
{
    /// <summary>
    /// DDS via inject of dds_native_helper.dll into stock NVIDIA App (NvCpl + UXD).
    /// Host is launched SW_HIDE; helper continuously hides windows — no visible App UI required.
    /// Named pipe: \\.\pipe\AcerPredatorDds
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class DdsNativeHost
    {
        private const string NvidiaAppExe =
            @"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe";
        private const string NvidiaAppCwd =
            @"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF";
        private const string HelperDllFileName = "dds_native_helper.dll";
        private const string PipeName = "AcerPredatorDds";
        private const int SwHide = 0;

        public static bool TrySetMode(GpuDisplayMode mode, out string detail)
        {
            string cmd = mode switch
            {
                GpuDisplayMode.Optimus => "igpu",
                GpuDisplayMode.Auto => "auto",
                GpuDisplayMode.Nvidia => "dgpu",
                _ => ""
            };
            if (cmd.Length == 0)
            {
                detail = "Unknown display mode";
                return false;
            }

            // Fast path first — do not touch helper DLL on disk while host holds it locked.
            if (PipeCommand("ping", out string ping, TimeSpan.FromSeconds(1))
                && ping.StartsWith("PONG|", StringComparison.OrdinalIgnoreCase))
            {
                if (!PipeCommand(cmd, out string fastResp, TimeSpan.FromSeconds(8)))
                {
                    detail = fastResp;
                    return false;
                }

                bool fastOk = fastResp.StartsWith("OK|", StringComparison.OrdinalIgnoreCase);
                detail = "reuse-pipe; " + fastResp;
                return fastOk;
            }

            string dllPath;
            try
            {
                dllPath = EnsureHelperDllOnDisk();
            }
            catch (Exception ex)
            {
                detail = "helper dll: " + ex.Message;
                return false;
            }

            int pid = FindNvcplPid();
            bool launched = false;
            if (pid == 0)
            {
                if (!TryLaunchHidden(out string launchDetail))
                {
                    detail = launchDetail;
                    return false;
                }

                launched = true;
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
                while (pid == 0 && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(400);
                    pid = FindNvcplPid();
                }

                if (pid == 0)
                {
                    detail = "NVIDIA App started but NvCpl.dll never loaded";
                    return false;
                }
            }

            if (!ModuleLoaded(pid, HelperDllFileName))
            {
                if (!Inject(pid, dllPath, out string injDetail))
                {
                    detail = injDetail;
                    return false;
                }

                Thread.Sleep(400);
            }

            if (!PipeCommand(cmd, out string resp, TimeSpan.FromSeconds(8)))
            {
                detail = resp;
                return false;
            }

            bool ok = resp.StartsWith("OK|", StringComparison.OrdinalIgnoreCase);
            detail = (launched ? "hidden-host+inject; " : "reuse-host; ") + resp;
            return ok;
        }

        public static bool IsHelperAlive() =>
            PipeCommand("ping", out string ping, TimeSpan.FromMilliseconds(500))
            && ping.StartsWith("PONG|", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Prefer an already-running NVIDIA App that has NvCpl (user-opened or our hidden host).
        /// Only launches a new hidden process when none exists.
        /// </summary>
        public static bool EnsureHostReady(out string detail)
        {
            try
            {
                EnsureHelperDllOnDisk();
            }
            catch (Exception ex)
            {
                detail = "helper dll: " + ex.Message;
                return false;
            }

            int pid = FindNvcplPid();
            if (pid != 0)
            {
                detail = "reuse pid=" + pid;
                return true;
            }

            if (!TryLaunchHidden(out string launchDetail))
            {
                detail = launchDetail;
                return false;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            while (pid == 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(400);
                pid = FindNvcplPid();
            }

            if (pid == 0)
            {
                detail = "NVIDIA App started but NvCpl.dll never loaded";
                return false;
            }

            detail = "launched hidden pid=" + pid;
            return true;
        }

        private static bool TryLaunchHidden(out string detail)
        {
            if (!File.Exists(NvidiaAppExe))
            {
                detail = "NVIDIA App.exe not found";
                return false;
            }

            var si = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                dwFlags = 0x00000001, // STARTF_USESHOWWINDOW
                wShowWindow = (short)SwHide
            };
            var pi = new ProcessInformation();
            string cmd = "\"" + NvidiaAppExe + "\"";
            if (!CreateProcessW(
                    NvidiaAppExe,
                    cmd,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    0,
                    IntPtr.Zero,
                    NvidiaAppCwd,
                    ref si,
                    out pi))
            {
                detail = "CreateProcess failed " + Marshal.GetLastWin32Error();
                return false;
            }

            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            detail = "launched hidden pid=" + pi.dwProcessId;
            return true;
        }

        private static int FindNvcplPid()
        {
            int fallback = 0;
            foreach (var p in Process.GetProcessesByName("NVIDIA App"))
            {
                try
                {
                    bool hasNvcpl = ModuleLoaded(p.Id, "NvCpl.dll") || ModuleLoaded(p.Id, "nvcpl.dll");
                    if (!hasNvcpl)
                        continue;
                    // Prefer a process that already has our helper (stable reuse).
                    if (ModuleLoaded(p.Id, HelperDllFileName))
                        return p.Id;
                    if (fallback == 0)
                        fallback = p.Id;
                }
                catch
                {
                    // access denied / exited
                }
            }

            return fallback;
        }

        private static bool ModuleLoaded(int pid, string moduleName)
        {
            IntPtr snap = CreateToolhelp32Snapshot(0x00000008 | 0x00000010, (uint)pid);
            if (snap == InvalidHandleValue)
                return false;

            try
            {
                var me = new ModuleEntry32W { dwSize = (uint)Marshal.SizeOf<ModuleEntry32W>() };
                if (!Module32FirstW(snap, ref me))
                    return false;
                do
                {
                    if (string.Equals(me.szModule, moduleName, StringComparison.OrdinalIgnoreCase))
                        return true;
                } while (Module32NextW(snap, ref me));
                return false;
            }
            finally
            {
                CloseHandle(snap);
            }
        }

        private static bool Inject(int pid, string dllPath, out string detail)
        {
            IntPtr proc = OpenProcess(
                0x0002 | 0x0400 | 0x0008 | 0x0020 | 0x0010, // CREATE_THREAD|QUERY|VM_OP|WRITE|READ
                false,
                (uint)pid);
            if (proc == IntPtr.Zero)
            {
                detail = "OpenProcess " + Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                byte[] pathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
                IntPtr remote = VirtualAllocEx(
                    proc, IntPtr.Zero, (UIntPtr)pathBytes.Length,
                    0x3000, 0x04);
                if (remote == IntPtr.Zero)
                {
                    detail = "VirtualAllocEx " + Marshal.GetLastWin32Error();
                    return false;
                }

                if (!WriteProcessMemory(proc, remote, pathBytes, (UIntPtr)pathBytes.Length, out _))
                {
                    detail = "WriteProcessMemory " + Marshal.GetLastWin32Error();
                    return false;
                }

                IntPtr loadLib = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
                if (loadLib == IntPtr.Zero)
                {
                    detail = "LoadLibraryW not found";
                    return false;
                }

                IntPtr th = CreateRemoteThread(proc, IntPtr.Zero, UIntPtr.Zero, loadLib, remote, 0, IntPtr.Zero);
                if (th == IntPtr.Zero)
                {
                    detail = "CreateRemoteThread " + Marshal.GetLastWin32Error();
                    return false;
                }

                WaitForSingleObject(th, 15000);
                GetExitCodeThread(th, out uint code);
                CloseHandle(th);
                if (code == 0)
                {
                    detail = "LoadLibraryW returned 0";
                    return false;
                }

                detail = "injected";
                return true;
            }
            finally
            {
                CloseHandle(proc);
            }
        }

        private static bool PipeCommand(string cmd, out string resp, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                    pipe.Connect(200);
                    pipe.ReadMode = PipeTransmissionMode.Message;
                    byte[] req = Encoding.ASCII.GetBytes(cmd);
                    pipe.Write(req, 0, req.Length);
                    pipe.Flush();
                    byte[] buf = new byte[512];
                    int n = pipe.Read(buf, 0, buf.Length);
                    resp = Encoding.ASCII.GetString(buf, 0, n).TrimEnd('\0', '\r', '\n');
                    return true;
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }

            resp = "ERR|pipe timeout";
            return false;
        }

        private static string EnsureHelperDllOnDisk()
        {
            string dir = Path.Combine(Path.GetTempPath(), "AcerPredatorTool");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, HelperDllFileName);

            string? sidecar = FindSidecar();
            if (File.Exists(path))
            {
                // Host may lock the injected image — reuse existing file.
                try
                {
                    if (sidecar != null)
                    {
                        var src = new FileInfo(sidecar);
                        var dst = new FileInfo(path);
                        if (src.Length == dst.Length && src.LastWriteTimeUtc <= dst.LastWriteTimeUtc)
                            return path;
                        File.Copy(sidecar, path, overwrite: true);
                    }
                }
                catch (IOException)
                {
                    return path;
                }

                return path;
            }

            if (sidecar != null)
            {
                File.Copy(sidecar, path, overwrite: true);
                return path;
            }

            var asm = typeof(DdsNativeHost).Assembly;
            using Stream? s = asm.GetManifestResourceStream(HelperDllFileName);
            if (s == null)
                throw new FileNotFoundException("Embedded " + HelperDllFileName + " missing");

            using (var fs = File.Create(path))
                s.CopyTo(fs);
            return path;
        }

        private static string? FindSidecar()
        {
            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "inject_dds", HelperDllFileName),
                Path.Combine(baseDir, HelperDllFileName),
                Path.Combine(baseDir, "tools", "inject_dds", HelperDllFileName)
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c))
                    return c;
            }

            return null;
        }

        private static readonly IntPtr InvalidHandleValue = new(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
            public int dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ModuleEntry32W
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExePath;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string? lpApplicationName, string lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
            string? lpCurrentDirectory, ref StartupInfo lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(
            IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(
            IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(
            IntPtr hProcess, IntPtr lpThreadAttributes, UIntPtr dwStackSize,
            IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Module32FirstW(IntPtr hSnapshot, ref ModuleEntry32W lpme);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Module32NextW(IntPtr hSnapshot, ref ModuleEntry32W lpme);
    }
}
