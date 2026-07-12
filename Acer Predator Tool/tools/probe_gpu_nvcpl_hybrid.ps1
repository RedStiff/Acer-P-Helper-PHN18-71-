<#
.SYNOPSIS
  Automatic NvCpl HybridMode / Muxd probe (read GetHybridMode; optional Set attempts later).

.DESCRIPTION
  Compiles a short C# helper that:
    - starts nvcplui /s if needed
    - loads nvcpl.dll
    - NvCplDaemon + NvCplApiInit(hwnd) + NvCplApiMuxdInitialize
    - NvCplApiGetHybridMode / NvCplApiMsHybridStatus
  No public SetHybridMode exists in nvcpl.dll exports — this probe documents GET only.
  Correlate HybridMode number with MuxSignature (Intel vs NVIDIA panel owner).

.USAGE
  .\probe_gpu_nvcpl_hybrid.ps1
#>
[CmdletBinding()]
param()

. "$PSScriptRoot\_gpu_common.ps1"

$log = New-GpuLog 'probe_gpu_nvcpl_hybrid'
Write-GpuLog $log '=== NvCpl HybridMode / Muxd probe ===' Green

$fp = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $fp -Label 'BEFORE'

$work = Join-Path $PSScriptRoot '_nvcpl_hybrid_build'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$csproj = Join-Path $work 'NvcplHybridProbe.csproj'
$program = Join-Path $work 'Program.cs'
$exeDir = Join-Path $work 'bin'

@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath $csproj -Encoding UTF8

@'
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Fn0();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnHwnd(IntPtr h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnRef(ref int v);

    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        string nvcplui = @"C:\Program Files\NVIDIA Corporation\Control Panel Client\nvcplui.exe";
        string nvcpl = @"C:\WINDOWS\system32\nvcpl.dll";
        if (!File.Exists(nvcpl))
        {
            Console.WriteLine("FATAL nvcpl.dll missing");
            return 2;
        }

        if (Process.GetProcessesByName("nvcplui").Length == 0 && File.Exists(nvcplui))
        {
            Process.Start(new ProcessStartInfo(nvcplui, "/s")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Thread.Sleep(2000);
        }

        int exit = 1;
        using var f = new Form
        {
            Opacity = 0,
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000)
        };

        f.Shown += (_, _) =>
        {
            try
            {
                IntPtr h = NativeLibrary.Load(nvcpl);
                Console.WriteLine("loaded=" + nvcpl);

                void Call0(string name)
                {
                    if (!NativeLibrary.TryGetExport(h, name, out IntPtr p))
                    {
                        Console.WriteLine(name + "=MISSING");
                        return;
                    }
                    int rc = Marshal.GetDelegateForFunctionPointer<Fn0>(p)();
                    Console.WriteLine(name + "=" + rc);
                }

                void CallHwnd(string name, IntPtr hwnd)
                {
                    if (!NativeLibrary.TryGetExport(h, name, out IntPtr p))
                    {
                        Console.WriteLine(name + "=MISSING");
                        return;
                    }
                    int rc = Marshal.GetDelegateForFunctionPointer<FnHwnd>(p)(hwnd);
                    Console.WriteLine(name + "=" + rc);
                }

                void CallRef(string name)
                {
                    if (!NativeLibrary.TryGetExport(h, name, out IntPtr p))
                    {
                        Console.WriteLine(name + "=MISSING");
                        return;
                    }
                    int v = -1;
                    int rc = Marshal.GetDelegateForFunctionPointer<FnRef>(p)(ref v);
                    Console.WriteLine(name + "_rc=" + rc + " value=" + v);
                }

                Call0("NvCplDaemon");
                int installed = -1;
                if (NativeLibrary.TryGetExport(h, "NvCplApiIsClientInstalled", out IntPtr pInst))
                {
                    Marshal.GetDelegateForFunctionPointer<FnRef>(pInst)(ref installed);
                    Console.WriteLine("NvCplApiIsClientInstalled=" + installed);
                }
                CallHwnd("NvCplApiInit", f.Handle);
                for (int i = 0; i < 20; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(50);
                }
                Call0("NvCplApiMuxdInitialize");
                CallRef("NvCplApiGetHybridMode");
                CallRef("NvCplApiMsHybridStatus");
                Call0("NvCplApiMuxdClose");
                exit = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("EX: " + ex);
                exit = 3;
            }
            finally
            {
                f.Close();
            }
        };

        Application.Run(f);
        return exit;
    }
}
'@ | Set-Content -LiteralPath $program -Encoding UTF8

Write-GpuLog $log ("building helper in {0}" -f $work)
$buildOut = & dotnet build $csproj -c Release -o $exeDir --nologo 2>&1 | Out-String
foreach ($line in ($buildOut -split "`r?`n")) {
    if ($line.Trim()) { Write-GpuLog $log ("  build: {0}" -f $line.Trim()) }
}

$exe = Join-Path $exeDir 'NvcplHybridProbe.exe'
if (-not (Test-Path $exe)) {
    Write-GpuLog $log 'FATAL: helper exe not built' Red
    exit 2
}

Write-GpuLog $log 'running NvcplHybridProbe.exe ...' Cyan
$probeOut = & $exe 2>&1 | Out-String
foreach ($line in ($probeOut -split "`r?`n")) {
    if ($line.Trim()) { Write-GpuLog $log ("  nvcpl: {0}" -f $line.Trim()) }
}

Start-Sleep -Milliseconds 1500
$fp2 = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $fp2 -Label 'AFTER'
Write-GpuLog $log ("muxChanged={0}" -f ($fp.MuxSignature -ne $fp2.MuxSignature))

Write-GpuLog $log 'NOTE: nvcpl.dll exports GetHybridMode but no SetHybridMode (NVIDIA: NVCP-only).' Yellow
Write-GpuLog $log ("DONE. Log: {0}" -f $log) Green
Write-Host "Log: $log" -ForegroundColor Cyan
