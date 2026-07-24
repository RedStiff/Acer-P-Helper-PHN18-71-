# Shared helpers for PHN18 GPU / DDS mux research probes.
# Dot-source from probe_gpu_*.ps1 scripts.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Confirm-GpuAdmin {
    if (Test-IsAdmin) { return $true }
    Write-Host 'ERROR: Administrator required for AcerGamingFunction WMI.' -ForegroundColor Red
    Write-Host 'Right-click PowerShell -> Run as administrator, then re-run the script.'
    return $false
}

function New-GpuLog([string]$Prefix) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    return (Join-Path $PSScriptRoot ("{0}_{1}.log" -f $Prefix, $stamp))
}

function Write-GpuLog {
    param(
        [Parameter(Mandatory)][string]$LogPath,
        [Parameter(Mandatory)][string]$Message,
        [ConsoleColor]$Color = [ConsoleColor]::Gray
    )
    $line = '[{0:HH:mm:ss}] {1}' -f (Get-Date), $Message
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    Write-Host $line -ForegroundColor $Color
}

function ConvertTo-PsRegistryPath {
    param([Parameter(Mandatory)][string]$Path)
    # Child PSPath becomes HKEY_LOCAL_MACHINE\... which Test-Path rejects.
    $p = $Path
    $p = $p -replace '^Microsoft\.PowerShell\.Core\\Registry::', ''
    $p = $p -replace '^Registry::', ''
    $p = $p -replace '^HKEY_LOCAL_MACHINE\\', 'HKLM:\'
    $p = $p -replace '^HKEY_CURRENT_USER\\', 'HKCU:\'
    $p = $p -replace '^HKEY_CLASSES_ROOT\\', 'HKCR:\'
    $p = $p -replace '^HKEY_USERS\\', 'HKU:\'
    return $p
}

function Get-AcerGaming {
    return Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
}

function Invoke-AcerGaming {
    param(
        [Parameter(Mandatory)]$Inst,
        [Parameter(Mandatory)][string]$Method,
        $GmInput
    )
    try {
        return Invoke-CimMethod -InputObject $Inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
    } catch {
        return $null
    }
}

function Get-MiscSetting {
    param(
        [Parameter(Mandatory)]$Inst,
        [Parameter(Mandatory)][uint64]$Id
    )
    $r = Invoke-AcerGaming -Inst $Inst -Method GetGamingMiscSetting -GmInput $Id
    if (-not $r) {
        return [pscustomobject]@{
            Id = $Id; Ok = $false; Raw = $null; Status = $null; Value = $null; Error = 'invoke_failed'
        }
    }
    $raw = [uint64]$r.gmOutput
    return [pscustomobject]@{
        Id     = $Id
        Ok     = $true
        Raw    = $raw
        Status = [byte]($raw -band 0xFF)
        Value  = [byte](($raw -shr 8) -band 0xFF)
        Error  = $null
    }
}

function Set-MiscSetting {
    param(
        [Parameter(Mandatory)]$Inst,
        [Parameter(Mandatory)][uint64]$Id,
        [Parameter(Mandatory)][byte]$Value
    )
    $payload = [uint64]$Id -bor ([uint64]$Value -shl 8)
    $r = Invoke-AcerGaming -Inst $Inst -Method SetGamingMiscSetting -GmInput $payload
    if (-not $r) {
        return [pscustomobject]@{ Ok = $false; Raw = $null; Status = $null }
    }
    $raw = [uint32]$r.gmOutput
    return [pscustomobject]@{
        Ok     = ($raw -band 0xFF) -eq 0
        Raw    = $raw
        Status = [byte]($raw -band 0xFF)
    }
}

function Initialize-DisplayEnumHelper {
    if ('DisplayEnumProbe' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class DisplayEnumProbe
{
    private const int EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;
    private const int DISPLAY_DEVICE_ACTIVE = 0x00000001;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
    private const int DISPLAY_DEVICE_MIRRORING_DRIVER = 0x00000008;
    private const int DISPLAY_DEVICE_VGA_COMPATIBLE = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    public sealed class DisplayInfo
    {
        public string AdapterName;
        public string AdapterString;
        public string AdapterId;
        public bool Active;
        public bool Primary;
        public string Kind;
        public string MonitorName;
        public string MonitorString;
    }

    private static string Classify(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Other";
        string u = s.ToUpperInvariant();
        if (u.Contains("NVIDIA") || u.Contains("GEFORCE") || u.Contains("RTX") || u.Contains("GTX")) return "NVIDIA";
        if (u.Contains("INTEL") || u.Contains("UHD") || u.Contains("IRIS") || u.Contains("ARC")) return "Intel";
        if (u.Contains("AMD") || u.Contains("RADEON")) return "AMD";
        return "Other";
    }

    public static List<DisplayInfo> Snapshot()
    {
        var list = new List<DisplayInfo>();
        for (uint i = 0; ; i++)
        {
            var adapter = new DISPLAY_DEVICE();
            adapter.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            if (!EnumDisplayDevices(null, i, ref adapter, 0))
                break;

            if ((adapter.StateFlags & DISPLAY_DEVICE_MIRRORING_DRIVER) != 0)
                continue;

            string monitorName = "";
            string monitorString = "";
            var monitor = new DISPLAY_DEVICE();
            monitor.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            if (EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0))
            {
                monitorName = monitor.DeviceName;
                monitorString = monitor.DeviceString;
            }

            list.Add(new DisplayInfo
            {
                AdapterName = adapter.DeviceName,
                AdapterString = adapter.DeviceString,
                AdapterId = adapter.DeviceID,
                Active = (adapter.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0,
                Primary = (adapter.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                Kind = Classify(adapter.DeviceString),
                MonitorName = monitorName,
                MonitorString = monitorString
            });
        }
        return list;
    }
}
'@
}

function Get-DisplayAdapterSnapshot {
    Initialize-DisplayEnumHelper
    return [DisplayEnumProbe]::Snapshot()
}

function Get-NvidiaSmiSnapshot {
    $exe = Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue
    if (-not $exe) {
        return [pscustomobject]@{
            Available = $false; Lines = @(); DisplayActive = $null; PowerW = $null; ClockMhz = $null; Util = $null
        }
    }
    $csv = & $exe.Source --query-gpu=name,display_active,power.draw,clocks.current.graphics,utilization.gpu --format=csv,noheader,nounits 2>&1
    $lines = @($csv | ForEach-Object { "$_".Trim() } | Where-Object { $_ -and ($_ -notmatch '^(ERROR|Field|Failed)') })
    $display = $null
    $power = $null
    $clock = $null
    $util = $null
    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    if ($lines.Count -gt 0 -and $lines[0] -notmatch 'not a valid|Failed') {
        $parts = @($lines[0] -split ',\s*')
        if ($parts.Count -ge 2) { $display = $parts[1].Trim() }
        if ($parts.Count -ge 3) {
            $p = 0.0
            if ([double]::TryParse($parts[2].Trim(), [System.Globalization.NumberStyles]::Float, $inv, [ref]$p)) {
                $power = [math]::Round($p, 1)
            }
        }
        if ($parts.Count -ge 4) {
            $c = 0
            if ([int]::TryParse($parts[3].Trim(), [System.Globalization.NumberStyles]::Integer, $inv, [ref]$c)) {
                $clock = $c
            }
        }
        if ($parts.Count -ge 5) {
            $u = 0
            if ([int]::TryParse($parts[4].Trim(), [System.Globalization.NumberStyles]::Integer, $inv, [ref]$u)) {
                $util = $u
            }
        }
    }
    return [pscustomobject]@{
        Available     = $true
        Lines         = $lines
        DisplayActive = $display
        PowerW        = $power
        ClockMhz      = $clock
        Util          = $util
    }
}

function Get-GpuFingerprint {
    $displays = @()
    try { $displays = @(Get-DisplayAdapterSnapshot) } catch { $displays = @() }

    $primary = ($displays | Where-Object { $_.Primary } | Select-Object -First 1)
    if (-not $primary) { $primary = ($displays | Where-Object { $_.Active } | Select-Object -First 1) }

    $ownerKind = if ($primary) { $primary.Kind } else { 'NONE' }
    $ownerDesc = if ($primary) { $primary.AdapterString } else { '' }

    $nv = Get-NvidiaSmiSnapshot
    $adapters = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
        ForEach-Object { [pscustomobject]@{ Name = $_.Name; Status = $_.Status; PNPDeviceID = $_.PNPDeviceID } })

    $powerSig = if ($null -eq $nv.PowerW) { '' } else { $nv.PowerW.ToString([System.Globalization.CultureInfo]::InvariantCulture) }
    $clockSig = if ($null -eq $nv.ClockMhz) { '' } else { $nv.ClockMhz.ToString([System.Globalization.CultureInfo]::InvariantCulture) }

    # MuxSignature: only panel owner + nvidia display_active (stable for HIT detection).
    # Full Signature keeps power/clock for telemetry but must NOT drive mux HIT decisions.
    $muxSig = 'owner={0}|nv_display={1}' -f $ownerKind, $nv.DisplayActive

    return [pscustomobject]@{
        Timestamp        = (Get-Date).ToString('o')
        OwnerKind        = $ownerKind
        OwnerDesc        = $ownerDesc
        Displays         = $displays
        NvidiaDisplay    = $nv.DisplayActive
        NvidiaPowerW     = $nv.PowerW
        NvidiaClockMhz   = $nv.ClockMhz
        NvidiaUtil       = $nv.Util
        NvidiaLines      = $nv.Lines
        VideoControllers = $adapters
        MuxSignature     = $muxSig
        Signature        = ('{0}|nv_power={1}|nv_clock={2}' -f $muxSig, $powerSig, $clockSig)
    }
}

function Write-GpuFingerprint {
    param(
        [Parameter(Mandatory)][string]$LogPath,
        [Parameter(Mandatory)]$Fp,
        [string]$Label = 'FP'
    )
    Write-GpuLog $LogPath ("{0} mux={1} full={2}" -f $Label, $Fp.MuxSignature, $Fp.Signature) Cyan
    foreach ($d in @($Fp.Displays)) {
        Write-GpuLog $LogPath ("  DISP {0} kind={1} active={2} primary={3} '{4}' mon='{5}'" -f `
            $d.AdapterName, $d.Kind, $d.Active, $d.Primary, $d.AdapterString, $d.MonitorString)
    }
    foreach ($line in @($Fp.NvidiaLines)) {
        Write-GpuLog $LogPath ("  nvidia-smi: {0}" -f $line)
    }
    foreach ($v in @($Fp.VideoControllers)) {
        Write-GpuLog $LogPath ("  Win32: {0} status={1}" -f $v.Name, $v.Status)
    }
}

function Get-NvidiaAppProcesses {
    # Tray / CEF host may appear as "NVIDIA App" or nvcplui.
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($p in @(Get-Process -ErrorAction SilentlyContinue)) {
        if ($p.ProcessName -eq 'nvcplui' -or $p.ProcessName -eq 'NVIDIA App') {
            [void]$out.Add($p)
            continue
        }
        try {
            $path = $p.Path
            if ($path -and $path -match 'NVIDIA Corporation\\NVIDIA App') {
                [void]$out.Add($p)
            }
        } catch { }
    }
    return @($out)
}

function Open-NvidiaDisplayModeUi {
    <#
    .SYNOPSIS
      Open or detect UI for Display Mode DDS.
      Preferred path on PHN18: NVIDIA App from system tray.
      Avoid Control Panel Client nvcplui.exe without CEF working directory - missing libcef.
    #>
    param(
        [ValidateSet('Tray', 'AppLaunch', 'ClassicNvcp')]
        [string]$Source = 'Tray',
        [switch]$ForceRestart
    )

    $classicAppId = 'NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel'
    $nvidiaAppExe = Join-Path $env:ProgramFiles 'NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe'
    $nvidiaAppCwd = Join-Path $env:ProgramFiles 'NVIDIA Corporation\NVIDIA App\CEF'
    $brokenStub = Join-Path $env:ProgramFiles 'NVIDIA Corporation\Control Panel Client\nvcplui.exe'

    if (-not $ForceRestart) {
        $running = @(Get-NvidiaAppProcesses)
        if ($running.Count -gt 0) {
            return [pscustomobject]@{
                Ok = $true
                Method = 'already_running'
                Detail = (($running | ForEach-Object { $_.ProcessName }) -join ',')
            }
        }
    }

    if ($Source -eq 'Tray') {
        # Do not auto-launch: tray activation is what the user actually uses.
        return [pscustomobject]@{
            Ok = $true
            Method = 'wait_tray'
            Detail = 'Open NVIDIA App from the system tray (NVIDIA icon), then continue.'
        }
    }

    if ($Source -eq 'AppLaunch') {
        if ((Test-Path -LiteralPath $nvidiaAppExe) -and (Test-Path -LiteralPath $nvidiaAppCwd)) {
            try {
                Start-Process -FilePath $nvidiaAppExe -WorkingDirectory $nvidiaAppCwd -ErrorAction Stop | Out-Null
                return [pscustomobject]@{ Ok = $true; Method = 'nvidia_app_cef'; Detail = $nvidiaAppExe }
            } catch {
                return [pscustomobject]@{ Ok = $false; Method = 'nvidia_app_cef'; Detail = $_.Exception.Message }
            }
        }
    }

    if ($Source -eq 'ClassicNvcp') {
        try {
            Start-Process ("shell:AppsFolder\{0}" -f $classicAppId) -ErrorAction Stop | Out-Null
            return [pscustomobject]@{ Ok = $true; Method = 'classic_nvcp_uwp'; Detail = $classicAppId }
        } catch {
            return [pscustomobject]@{ Ok = $false; Method = 'classic_nvcp_uwp'; Detail = $_.Exception.Message }
        }
    }

    return [pscustomobject]@{
        Ok = $false
        Method = 'none'
        Detail = ("Open NVIDIA App from tray. Avoid stub without CEF cwd: {0}" -f $brokenStub)
    }
}

# Never brute-force these (power / OC / known unrelated).
$script:GpuMiscSkipIds = @(
    [uint64]0x05, # OC1
    [uint64]0x07, # OC2
    [uint64]0x0A, # supported profiles
    [uint64]0x0B  # platform / power profile
)

# Historical guess range used by older Acer helpers for "GPU mode".
$script:GpuMiscCandidateIds = @(
    0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15,
    0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
    0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29,
    0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30
)
