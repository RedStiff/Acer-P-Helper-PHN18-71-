#Requires -Version 5.1
<#
.SYNOPSIS
  Probe Windows CCD (QueryDisplayConfig/SetDisplayConfig) to switch internal panel
  path between Intel and NVIDIA targets — no Acer, no NVCP UI.

.PARAMETER Target
  Nvidia | Intel

.PARAMETER Force
  Skip confirmation.
#>
[CmdletBinding()]
param(
    [ValidateSet('Nvidia', 'Intel', 'Status')]
    [string]$Target = 'Status',
    [switch]$Force,
    [int]$SettleSeconds = 8
)

. "$PSScriptRoot\_gpu_common.ps1"
Set-StrictMode -Off
$ErrorActionPreference = 'Continue'

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$sessionDir = Join-Path $PSScriptRoot ("ccd_mux_{0}_{1}" -f $Target, $stamp)
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
$log = Join-Path $sessionDir 'probe.log'
function L([string]$m, [ConsoleColor]$c = [ConsoleColor]::Gray) { Write-GpuLog $log $m $c }

$work = Join-Path $sessionDir 'build'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$csproj = Join-Path $work 'CcdMux.csproj'
$program = Join-Path $work 'Program.cs'
$exeDir = Join-Path $work 'bin'

@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PlatformTarget>x64</PlatformTarget>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
</Project>
'@ | Set-Content $csproj -Encoding UTF8

@'
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private const uint QDC_ALL_PATHS = 0x00000001;
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const uint QDC_DATABASE_CURRENT = 0x00000004;
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    private const uint SDC_APPLY = 0x00000080;
    private const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    private const uint SDC_ALLOW_CHANGES = 0x00000400;
    private const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
    private const uint SDC_TOPOLOGY_CLONE = 0x00000002;
    private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    private const uint SDC_TOPOLOGY_EXTERNAL = 0x00000008;
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG_AND_FORCE_MODE_ENUMERATION = 0x00000020 | 0x00001000;
    private const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    private const int ERROR_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        // mode union is large; we allocate raw bytes separately
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, IntPtr modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint numPathArrayElements, DISPLAYCONFIG_PATH_INFO[] pathArray,
        uint numModeInfoArrayElements, IntPtr modeInfoArray,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private static string AdapterKind(LUID luid)
    {
        // Map via GDI devices: compare DeviceKey / enumerate video devices loosely via EnumDisplayDevices
        // Fallback string from path dump.
        return luid.LowPart.ToString("X8") + ":" + luid.HighPart.ToString("X8");
    }

    private static void DumpGdi()
    {
        for (uint i = 0; ; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            bool active = (dd.StateFlags & 0x1) != 0;
            bool primary = (dd.StateFlags & 0x4) != 0;
            string kind = dd.DeviceString.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                : dd.DeviceString.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "Other";
            Console.WriteLine($"GDI {dd.DeviceName} kind={kind} active={active} primary={primary} '{dd.DeviceString}' id={dd.DeviceID}");
        }
    }

    private static int Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0] : "status";
        DumpGdi();

        uint pathCount, modeCount;
        int rc = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out pathCount, out modeCount);
        Console.WriteLine($"GetDisplayConfigBufferSizes rc={rc} paths={pathCount} modes={modeCount}");
        if (rc != ERROR_SUCCESS) return 2;

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        // mode info structs are 64 bytes on x64 typically (DISPLAYCONFIG_MODE_INFO)
        int modeSize = 64; // DISPLAYCONFIG_MODE_INFO size on x64
        IntPtr modes = Marshal.AllocHGlobal((int)modeCount * modeSize);
        try
        {
            uint p = pathCount, m = modeCount;
            rc = QueryDisplayConfig(QDC_ALL_PATHS, ref p, paths, ref m, modes, IntPtr.Zero);
            Console.WriteLine($"QueryDisplayConfig rc={rc} paths={p} modes={m}");
            if (rc != ERROR_SUCCESS) return 3;

            for (int i = 0; i < p; i++)
            {
                var path = paths[i];
                bool active = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
                Console.WriteLine(
                    $"PATH[{i}] active={active} src={AdapterKind(path.sourceInfo.adapterId)}#{path.sourceInfo.id} " +
                    $"tgt={AdapterKind(path.targetInfo.adapterId)}#{path.targetInfo.id} tech={path.targetInfo.outputTechnology} avail={path.targetInfo.targetAvailable}");
            }

            if (cmd.Equals("status", StringComparison.OrdinalIgnoreCase))
                return 0;

            // Heuristic apply: keep only paths whose target adapter matches desired vendor via GDI correlation is hard.
            // Instead: for Nvidia — prefer activating paths where targetAvailable and currently inactive NVIDIA-like;
            // Practical approach used by some tools: SetDisplayConfig with SDC_TOPOLOGY_INTERNAL after enabling device.
            // Here we try: clone of active path list with forced active flag on first available path for each adapter set.

            bool wantNvidia = cmd.Equals("nvidia", StringComparison.OrdinalIgnoreCase);
            // Mark all paths inactive first, then activate candidates.
            var newPaths = (DISPLAYCONFIG_PATH_INFO[])paths.Clone();
            for (int i = 0; i < p; i++)
                newPaths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;

            // Without reliable adapter->vendor map from LUID alone, use outputTechnology / availability:
            // Internal panel often OUTPUT_TECHNOLOGY_INTERNAL (0x80000000) or DISPLAYPORT_EMBEDDED (0x80000001)
            const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;
            const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 0x80000001;

            int activated = 0;
            for (int i = 0; i < p; i++)
            {
                var t = newPaths[i].targetInfo;
                bool internalPanel = t.outputTechnology == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                    || t.outputTechnology == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                    || t.outputTechnology == 0x80000005; /* LVDS */
                if (!t.targetAvailable || !internalPanel) continue;

                // Activate ALL available internal targets that are currently in the topology database;
                // Windows/NVIDIA DDS typically has two internal targets (iGPU + dGPU).
                // Prefer activating exactly one: pick first for Intel request (lower adapter LUID often Intel),
                // last for NVIDIA — fragile. Better: activate path that was inactive when wanting switch.
                bool wasActive = (paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
                if (wantNvidia && !wasActive)
                {
                    newPaths[i].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                    activated++;
                    Console.WriteLine($"ACTIVATE for NVIDIA idx={i}");
                    break;
                }
                if (!wantNvidia && !wasActive)
                {
                    newPaths[i].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                    activated++;
                    Console.WriteLine($"ACTIVATE for INTEL idx={i}");
                    break;
                }
            }

            if (activated == 0)
            {
                // fallback: toggle first inactive internal
                for (int i = 0; i < p; i++)
                {
                    if (!newPaths[i].targetInfo.targetAvailable) continue;
                    bool wasActive = (paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
                    if (!wasActive)
                    {
                        newPaths[i].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                        activated++;
                        Console.WriteLine($"FALLBACK ACTIVATE idx={i}");
                        break;
                    }
                }
            }

            if (activated == 0)
            {
                Console.WriteLine("No candidate path to activate");
                return 4;
            }

            uint flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE;
            // Include modes from query
            rc = SetDisplayConfig(p, newPaths, m, modes, flags);
            Console.WriteLine($"SetDisplayConfig rc={rc} activated={activated}");
            return rc == ERROR_SUCCESS ? 0 : 5;
        }
        finally
        {
            Marshal.FreeHGlobal(modes);
        }
    }
}
'@ | Set-Content $program -Encoding UTF8

L 'Building CCD helper...' Cyan
$build = & dotnet build $csproj -c Release -o $exeDir --nologo 2>&1 | Out-String
$build -split "`r?`n" | Where-Object { $_ } | ForEach-Object { L ("  {0}" -f $_.Trim()) }
$exe = Join-Path $exeDir 'CcdMux.exe'
if (-not (Test-Path $exe)) { L 'Build failed' Red; exit 2 }

$before = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $before -Label 'BEFORE'
L ("BEFORE mux={0}" -f $before.MuxSignature) Green

if ($Target -eq 'Status') {
    & $exe status 2>&1 | ForEach-Object { L ("ccd: {0}" -f $_) }
    exit 0
}

if (-not $Force) {
    Write-Host ("Will try CCD switch to {0}" -f $Target) -ForegroundColor Yellow
    if ((Read-Host 'Type YES') -ne 'YES') { exit 1 }
}

L ("Running CCD Target={0}" -f $Target) Cyan
$arg = if ($Target -eq 'Nvidia') { 'nvidia' } else { 'intel' }
& $exe $arg 2>&1 | ForEach-Object { L ("ccd: {0}" -f $_) }
Start-Sleep -Seconds $SettleSeconds

$after = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $after -Label 'AFTER'
$changed = $before.MuxSignature -ne $after.MuxSignature
L ("AFTER mux={0}" -f $after.MuxSignature) Green
L ("MUX_CHANGED={0}" -f $changed) $(if ($changed) { 'Green' } else { 'Yellow' })
$summary = @(
    'CCD MUX SUMMARY',
    ("Session: {0}" -f $sessionDir),
    ("Target: {0}" -f $Target),
    ("BEFORE: {0}" -f $before.MuxSignature),
    ("AFTER:  {0}" -f $after.MuxSignature),
    ("MUX_CHANGED={0}" -f $changed)
) -join "`r`n"
Set-Content (Join-Path $sessionDir 'SUMMARY.txt') $summary -Encoding UTF8
Write-Host $summary -ForegroundColor Cyan
