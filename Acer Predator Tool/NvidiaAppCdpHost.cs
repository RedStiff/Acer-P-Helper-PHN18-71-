using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Starts NVIDIA App suspended, injects hook_cef_port.dll (CDP :9333), resumes.
    /// No separate launcher.exe — DLL is extracted from embedded resource or sidecar file.
    /// Uses the interactive desktop (does not steal single-instance App onto a private desktop).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class NvidiaAppCdpHost
    {
        private const string NvidiaAppExe =
            @"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe";
        private const string NvidiaAppCwd =
            @"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF";
        private const string HookDllFileName = "hook_cef_port.dll";
        private const uint CreateSuspended = 0x00000004;
        private const int SwHide = 0;

        public static bool TryLaunch(out string detail)
        {
            if (!File.Exists(NvidiaAppExe))
            {
                detail = "NVIDIA App.exe not found";
                return false;
            }

            string dllPath;
            try
            {
                dllPath = EnsureHookDllOnDisk();
            }
            catch (Exception ex)
            {
                detail = "hook dll: " + ex.Message;
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
                    CreateSuspended,
                    IntPtr.Zero,
                    NvidiaAppCwd,
                    ref si,
                    out pi))
            {
                detail = "CreateProcess failed " + Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                byte[] pathBytes = System.Text.Encoding.Unicode.GetBytes(dllPath + "\0");

                IntPtr remote = VirtualAllocEx(
                    pi.hProcess, IntPtr.Zero, (UIntPtr)pathBytes.Length,
                    0x3000 /* MEM_COMMIT|RESERVE */, 0x04 /* PAGE_READWRITE */);
                if (remote == IntPtr.Zero)
                {
                    detail = "VirtualAllocEx " + Marshal.GetLastWin32Error();
                    TerminateProcess(pi.hProcess, 1);
                    return false;
                }

                if (!WriteProcessMemory(pi.hProcess, remote, pathBytes, (UIntPtr)pathBytes.Length, out _))
                {
                    detail = "WriteProcessMemory " + Marshal.GetLastWin32Error();
                    TerminateProcess(pi.hProcess, 1);
                    return false;
                }

                IntPtr k32 = GetModuleHandleW("kernel32.dll");
                IntPtr load = GetProcAddress(k32, "LoadLibraryW");
                if (load == IntPtr.Zero)
                {
                    detail = "LoadLibraryW not found";
                    TerminateProcess(pi.hProcess, 1);
                    return false;
                }

                IntPtr thread = CreateRemoteThread(
                    pi.hProcess, IntPtr.Zero, UIntPtr.Zero, load, remote, 0, IntPtr.Zero);
                if (thread == IntPtr.Zero)
                {
                    detail = "CreateRemoteThread " + Marshal.GetLastWin32Error();
                    TerminateProcess(pi.hProcess, 1);
                    return false;
                }

                WaitForSingleObject(thread, 15000);
                GetExitCodeThread(thread, out uint remoteMod);
                CloseHandle(thread);
                if (remoteMod == 0)
                {
                    detail = "LoadLibraryW remote returned 0";
                    TerminateProcess(pi.hProcess, 1);
                    return false;
                }

                ResumeThread(pi.hThread);
                detail = $"CDP host pid={pi.dwProcessId} hook=ok";
                return true;
            }
            finally
            {
                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            }
        }

        public static void KillAll()
        {
            foreach (var p in Process.GetProcessesByName("NVIDIA App"))
            {
                try { p.Kill(entireProcessTree: true); }
                catch { /* ignore */ }
                finally { p.Dispose(); }
            }
        }

        private static string EnsureHookDllOnDisk()
        {
            string sidecar = Path.Combine(AppContext.BaseDirectory, "inject_dds", HookDllFileName);
            if (File.Exists(sidecar))
                return Path.GetFullPath(sidecar);

            string tools = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "tools", "inject_dds", HookDllFileName));
            if (File.Exists(tools))
                return tools;

            var asm = typeof(NvidiaAppCdpHost).Assembly;
            string? resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(HookDllFileName, StringComparison.OrdinalIgnoreCase));
            if (resName == null)
                throw new FileNotFoundException("Embedded " + HookDllFileName + " missing");

            string dir = Path.Combine(Path.GetTempPath(), "AcerPredatorTool", "inject_dds");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, HookDllFileName);
            using (var src = asm.GetManifestResourceStream(resName)!)
            using (var dst = File.Create(dest))
                src.CopyTo(dst);
            return dest;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string? lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref StartupInfo lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(
            IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool WriteProcessMemory(
            IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(
            IntPtr hProcess, IntPtr lpThreadAttributes, UIntPtr dwStackSize,
            IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32")]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    }
}
