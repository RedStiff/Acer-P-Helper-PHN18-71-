#Requires -Version 5.1
<#
.SYNOPSIS
  Probe direct NVIDIA Display Mode SET without Acer services.

.DESCRIPTION
  Calls nvcpl.dll!NvCplSetDisplayMode in an isolated helper process (AV-safe).
  HIT metric: MuxSignature change after each attempt.

.PARAMETER Apply
  Call Set attempts (may blank the screen).

.PARAMETER Force
  Skip YES confirmation.

.PARAMETER ModeValue
  Integers to try as first argument (default 0..3).

.EXAMPLE
  .\probe_gpu_nvcp_setdisplaymode.ps1
  .\probe_gpu_nvcp_setdisplaymode.ps1 -Apply -Force
  .\probe_gpu_nvcp_setdisplaymode.ps1 -Apply -Force -ModeValue 1,2
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$Force,
    [int[]]$ModeValue = @(0, 1, 2, 3)
)

. "$PSScriptRoot\_gpu_common.ps1"

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$sessionDir = Join-Path $PSScriptRoot ("nvcp_setdisplaymode_{0}" -f $stamp)
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
$log = Join-Path $sessionDir 'probe.log'
Write-GpuLog $log '=== NvCpl SetDisplayMode probe (NO Acer) ===' White
Write-GpuLog $log ("Apply={0} ModeValue=[{1}]" -f $Apply, ($ModeValue -join ','))

function New-SetHelperExe {
    param([string]$WorkDir)
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    $csproj = Join-Path $WorkDir 'SetMode.csproj'
    $program = Join-Path $WorkDir 'Program.cs'
    $exeDir = Join-Path $WorkDir 'bin'
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath $csproj -Encoding UTF8

    @'
using System.Runtime.InteropServices;

internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Fn0();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnHwnd(IntPtr h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnIntCdecl(int v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnIntStd(int v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Fn2Cdecl(int a, int b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Fn2Std(int a, int b);

    private static int Main(string[] args)
    {
        // args: <convention> <mode> [arg2]
        // convention: cdecl1 | stdcall1 | cdecl2 | stdcall2 | tray
        if (args.Length < 1) { Console.WriteLine("usage: SetMode <conv> [mode]"); return 2; }
        string conv = args[0];
        int mode = args.Length > 1 ? int.Parse(args[1]) : 0;
        int arg2 = args.Length > 2 ? int.Parse(args[2]) : 0;

        string nvcpl = @"C:\WINDOWS\system32\nvcpl.dll";
        IntPtr h = NativeLibrary.Load(nvcpl);
        Console.WriteLine("loaded");

        if (conv == "tray")
        {
            if (!NativeLibrary.TryGetExport(h, "NvCplApiShowOptimusTrayUI", out IntPtr pTray))
            {
                Console.WriteLine("tray=MISSING");
                return 3;
            }
            try
            {
                int rc = Marshal.GetDelegateForFunctionPointer<Fn0>(pTray)();
                Console.WriteLine("tray_cdecl0=" + rc);
            }
            catch (Exception ex) { Console.WriteLine("tray_EX=" + ex.GetType().Name); return 4; }
            return 0;
        }

        // Optional soft init — failures ignored
        try
        {
            if (NativeLibrary.TryGetExport(h, "NvCplDaemon", out IntPtr pD))
                Console.WriteLine("daemon=" + Marshal.GetDelegateForFunctionPointer<Fn0>(pD)());
        }
        catch { Console.WriteLine("daemon_EX"); }

        if (!NativeLibrary.TryGetExport(h, "NvCplSetDisplayMode", out IntPtr pSet))
        {
            Console.WriteLine("SetDisplayMode=MISSING");
            return 5;
        }
        Console.WriteLine("SetDisplayMode=HIT");

        try
        {
            int rc = conv switch
            {
                "cdecl1" => Marshal.GetDelegateForFunctionPointer<FnIntCdecl>(pSet)(mode),
                "stdcall1" => Marshal.GetDelegateForFunctionPointer<FnIntStd>(pSet)(mode),
                "cdecl2" => Marshal.GetDelegateForFunctionPointer<Fn2Cdecl>(pSet)(mode, arg2),
                "stdcall2" => Marshal.GetDelegateForFunctionPointer<Fn2Std>(pSet)(mode, arg2),
                _ => throw new ArgumentException("bad conv")
            };
            Console.WriteLine("rc=" + rc + " conv=" + conv + " mode=" + mode);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CALL_EX=" + ex.GetType().Name + " " + ex.Message);
            return 6;
        }
    }
}
'@ | Set-Content -LiteralPath $program -Encoding UTF8

    $build = & dotnet build $csproj -c Release -o $exeDir --nologo 2>&1 | Out-String
    foreach ($line in ($build -split "`r?`n")) {
        if ($line.Trim()) { Write-GpuLog $log ("  build: {0}" -f $line.Trim()) }
    }
    $exe = Join-Path $exeDir 'SetMode.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw 'SetMode.exe build failed' }
    return $exe
}

$before = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $before -Label 'BEFORE'
Write-GpuLog $log ("BEFORE mux={0}" -f $before.MuxSignature) Green

$opened = Open-NvidiaDisplayModeUi -Source Tray
Write-GpuLog $log ("UI open: Ok={0} method={1}" -f $opened.Ok, $opened.Method) Cyan
Start-Sleep -Seconds 1

$exe = New-SetHelperExe -WorkDir (Join-Path $sessionDir 'build')
Write-GpuLog $log ("helper={0}" -f $exe) Cyan

$results = New-Object System.Collections.Generic.List[object]

function Invoke-IsolatedCall {
    param(
        [string]$Conv,
        [int]$Mode = 0,
        [int]$Arg2 = 0
    )
    $argList = @($Conv, "$Mode")
    if ($Conv -match '2$') { $argList += "$Arg2" }
    $mux0 = (Get-GpuFingerprint).MuxSignature
    Write-GpuLog $log ("CALL conv={0} mode={1} before={2}" -f $Conv, $Mode, $mux0) Cyan
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = ($argList -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [Diagnostics.Process]::Start($psi)
    if (-not $p.WaitForExit(15000)) {
        try { $p.Kill() } catch {}
        Write-GpuLog $log '  helper TIMEOUT' Red
        return [pscustomobject]@{ Conv=$Conv; Mode=$Mode; Exit=-1; Out='TIMEOUT'; MuxBefore=$mux0; MuxAfter=$mux0; Changed=$false }
    }
    $out = ($p.StandardOutput.ReadToEnd() + $p.StandardError.ReadToEnd()).Trim()
    Write-GpuLog $log ("  exit={0} out={1}" -f $p.ExitCode, ($out -replace "`r?`n", ' | '))
    Start-Sleep -Seconds 2
    $mux1 = (Get-GpuFingerprint).MuxSignature
    $changed = $mux0 -ne $mux1
    if ($changed) { Write-GpuLog $log ("  MUX HIT {0} -> {1}" -f $mux0, $mux1) Green }
    return [pscustomobject]@{
        Conv = $Conv; Mode = $Mode; Exit = $p.ExitCode; Out = $out
        MuxBefore = $mux0; MuxAfter = $mux1; Changed = $changed
    }
}

# Always try tray once (safe-ish)
[void]$results.Add((Invoke-IsolatedCall -Conv 'tray'))

if (-not $Apply) {
    Write-GpuLog $log 'DRY-RUN complete. Re-run with -Apply -Force to call NvCplSetDisplayMode.' Yellow
} else {
    if (-not $Force) {
        Write-Host 'WARNING: will call NvCplSetDisplayMode. Screen may blank.' -ForegroundColor Yellow
        if ((Read-Host 'Type YES') -ne 'YES') { Write-GpuLog $log 'Aborted'; exit 1 }
    }
    $convs = @('cdecl1', 'stdcall1', 'cdecl2', 'stdcall2')
    foreach ($mode in $ModeValue) {
        foreach ($conv in $convs) {
            $row = Invoke-IsolatedCall -Conv $conv -Mode $mode
            [void]$results.Add($row)
            if ($row.Changed) {
                Write-GpuLog $log 'Stopping early: mux changed.' Green
                break
            }
        }
        if (@($results | Where-Object Changed).Count -gt 0) { break }
    }
}

$after = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $after -Label 'AFTER'
$results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $sessionDir 'results.json') -Encoding UTF8

$hits = @($results | Where-Object { $_.Changed })
$summary = @(
    'NvCpl SetDisplayMode probe SUMMARY',
    ("Session: {0}" -f $sessionDir),
    ("BEFORE: {0}" -f $before.MuxSignature),
    ("AFTER:  {0}" -f $after.MuxSignature),
    ("MUX_CHANGED={0}" -f ($before.MuxSignature -ne $after.MuxSignature)),
    ("HITS={0}" -f $hits.Count),
    ("Apply={0}" -f [bool]$Apply),
    'No Acer services used.'
) -join "`r`n"
if ($hits.Count -gt 0) {
    foreach ($h in $hits) {
        $summary += ("`r`nHIT conv={0} mode={1} {2}->{3}" -f $h.Conv, $h.Mode, $h.MuxBefore, $h.MuxAfter)
    }
}
Set-Content -LiteralPath (Join-Path $sessionDir 'SUMMARY.txt') -Value $summary -Encoding UTF8
Write-Host $summary -ForegroundColor Cyan
Write-GpuLog $log ("DONE. {0}" -f $sessionDir) Green
