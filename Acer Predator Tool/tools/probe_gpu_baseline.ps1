#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Automatic GPU / display-mux baseline for PHN18 (no WMI writes).

.DESCRIPTION
  Collects objective state used later to detect Integrated / Auto / Discrete changes:
  - Win32 video controllers
  - EnumDisplayDevices (which GPU owns the primary desktop)
  - nvidia-smi display_active / power / clocks
  - Acer GetGamingMiscSetting scan 0x00..0x40
  - GetGamingProfile / GetGamingProfileSetting samples
  - NVIDIA Control Panel / nvml presence

.USAGE
  cd $PSScriptRoot
  .\probe_gpu_baseline.ps1
#>
[CmdletBinding()]
param(
    [int]$MiscMax = 0x40
)

. "$PSScriptRoot\_gpu_common.ps1"

if (-not (Ensure-Admin)) { exit 1 }

$log = New-GpuLog 'probe_gpu_baseline'
Write-GpuLog $log '=== GPU baseline (read-only) ===' Green
Write-GpuLog $log ("host={0} user={1} admin={2}" -f $env:COMPUTERNAME, $env:USERNAME, (Test-IsAdmin))

$fp = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $fp -Label 'BASE'

Write-GpuLog $log '=== NVIDIA tooling ==='
@(
    "$env:ProgramFiles\NVIDIA Corporation\Control Panel Client\nvcplui.exe",
    "$env:SystemRoot\System32\nvidia-smi.exe",
    "$env:SystemRoot\System32\nvml.dll",
    "$env:SystemRoot\System32\nvapi64.dll"
) | ForEach-Object {
    Write-GpuLog $log ("  {0} exists={1}" -f $_, (Test-Path $_))
}

Write-GpuLog $log '=== AcerGamingFunction methods ==='
try {
    $methods = (Get-CimClass -Namespace root/WMI -ClassName AcerGamingFunction).CimClassMethods.Name | Sort-Object
    foreach ($m in $methods) { Write-GpuLog $log ("  {0}" -f $m) }
} catch {
    Write-GpuLog $log ("  ERR: {0}" -f $_.Exception.Message) Red
}

$inst = $null
try { $inst = Get-AcerGaming } catch {
    Write-GpuLog $log ("FATAL AcerGamingFunction: {0}" -f $_.Exception.Message) Red
    Write-Host "Log: $log"
    exit 2
}

Write-GpuLog $log ("=== GetGamingMiscSetting 0x00..0x{0:X} ===" -f $MiscMax)
$interesting = @()
for ($id = 0; $id -le $MiscMax; $id++) {
    $g = Get-MiscSetting -Inst $inst -Id ([uint64]$id)
    if (-not $g.Ok) {
        Write-GpuLog $log ("  id=0x{0:X2} FAIL" -f $id)
        continue
    }
    # Always log non-zero raw OR status==0 with any value (supported setting).
    if ($g.Raw -ne 0 -or $g.Status -eq 0) {
        $line = '  id=0x{0:X2} raw=0x{1:X} status=0x{2:X2} value=0x{3:X2}' -f $id, $g.Raw, $g.Status, $g.Value
        Write-GpuLog $log $line
        if ($g.Status -eq 0) { $interesting += $g }
    }
}

Write-GpuLog $log ("Supported misc IDs (status=0): {0}" -f (($interesting | ForEach-Object { '0x{0:X2}' -f $_.Id }) -join ', '))

Write-GpuLog $log '=== GetGamingSysInfo samples ==='
foreach ($in in @(0, 1, 2, 3, 4, 5, 8, 0x10, 0x100, 0x200)) {
    $r = Invoke-AcerGaming -Inst $inst -Method GetGamingSysInfo -GmInput ([uint32]$in)
    if ($r) {
        Write-GpuLog $log ("  in=0x{0:X} out=0x{1:X}" -f $in, [uint64]$r.gmOutput)
    } else {
        Write-GpuLog $log ("  in=0x{0:X} FAIL" -f $in)
    }
}

Write-GpuLog $log '=== GetGamingProfile / ProfileSetting samples ==='
foreach ($in in @(0, 1, 2, 3)) {
    $r = Invoke-AcerGaming -Inst $inst -Method GetGamingProfile -GmInput ([uint32]$in)
    if ($r) { Write-GpuLog $log ("  GetGamingProfile in=0x{0:X} out=0x{1:X}" -f $in, [uint64]$r.gmOutput) }
    $r2 = Invoke-AcerGaming -Inst $inst -Method GetGamingProfileSetting -GmInput ([uint32]$in)
    if ($r2) { Write-GpuLog $log ("  GetGamingProfileSetting in=0x{0:X} out=0x{1:X}" -f $in, [uint64]$r2.gmOutput) }
}

Write-GpuLog $log '=== NVIDIA registry / tray hints ==='
$paths = @(
    'HKCU:\SOFTWARE\NVIDIA Corporation\Global\NVTweak',
    'HKLM:\SOFTWARE\NVIDIA Corporation\Global\NVTweak',
    'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NVTweak',
    'HKCU:\SOFTWARE\NVIDIA Corporation\Global\CoProcManager'
)
foreach ($p in $paths) {
    if (-not (Test-Path $p)) {
        Write-GpuLog $log ("  missing {0}" -f $p)
        continue
    }
    Write-GpuLog $log ("  {0}" -f $p)
    Get-ItemProperty $p -ErrorAction SilentlyContinue | Out-String |
        ForEach-Object { $_.TrimEnd() -split "`r?`n" } |
        Where-Object { $_ -and $_ -notmatch '^PS' } |
        ForEach-Object { Write-GpuLog $log ("    {0}" -f $_) }
}

Write-GpuLog $log ("DONE. Log: {0}" -f $log) Green
Write-Host ''
Write-Host "Log: $log" -ForegroundColor Cyan
