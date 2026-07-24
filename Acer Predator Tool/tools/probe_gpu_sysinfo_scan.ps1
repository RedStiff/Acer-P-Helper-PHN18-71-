#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Automatic GetGamingSysInfo / GetGamingProfileSetting scan for GPU-mode clues.

.DESCRIPTION
  Read-only by default. With -Apply: for each SysInfo input that returns a small
  enum-like value (0..3), try SetGamingProfile / SetGamingProfileSetting candidates
  carefully вЂ” only if -ApplyProfileWrites is also set (more invasive).

  Default -Apply only re-reads after no-op; use -ApplyMiscFocus separately for misc SET.

.USAGE
  .\probe_gpu_sysinfo_scan.ps1
  .\probe_gpu_sysinfo_scan.ps1 -SysInfoMax 0x40
#>
[CmdletBinding()]
param(
    [int]$SysInfoMax = 0x30,
    [int]$ProfileSettingMax = 0x20
)

. "$PSScriptRoot\_gpu_common.ps1"

if (-not (Confirm-GpuAdmin)) { exit 1 }

$log = New-GpuLog 'probe_gpu_sysinfo_scan'
Write-GpuLog $log '=== GetGamingSysInfo / ProfileSetting scan ===' Green

$inst = $null
try { $inst = Get-AcerGaming } catch {
    Write-GpuLog $log ("FATAL: {0}" -f $_.Exception.Message) Red
    exit 2
}

$fp = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $fp -Label 'START'

Write-GpuLog $log ("=== GetGamingSysInfo 0x00..0x{0:X} ===" -f $SysInfoMax)
$enumLike = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -le $SysInfoMax; $i++) {
    $r = Invoke-AcerGaming -Inst $inst -Method GetGamingSysInfo -GmInput ([uint64]$i)
    if (-not $r) {
        Write-GpuLog $log ("  in=0x{0:X} FAIL" -f $i)
        continue
    }
    $out = [uint64]$r.gmOutput
    Write-GpuLog $log ("  in=0x{0:X} out=0x{1:X}" -f $i, $out)
    if ($out -le 3) {
        [void]$enumLike.Add(('SysInfo[0x{0:X}]=0x{1:X}' -f $i, $out))
    }
}

Write-GpuLog $log '=== GetGamingProfile ==='
$rp = Invoke-AcerGaming -Inst $inst -Method GetGamingProfile -GmInput ([uint64]0)
if ($rp) {
    Write-GpuLog $log ("  GetGamingProfile(0) out=0x{0:X}" -f [uint64]$rp.gmOutput)
} else {
    Write-GpuLog $log '  GetGamingProfile FAIL' Yellow
}

Write-GpuLog $log ("=== GetGamingProfileSetting 0x00..0x{0:X} ===" -f $ProfileSettingMax)
for ($i = 0; $i -le $ProfileSettingMax; $i++) {
    $r = Invoke-AcerGaming -Inst $inst -Method GetGamingProfileSetting -GmInput ([uint64]$i)
    if (-not $r) {
        Write-GpuLog $log ("  id=0x{0:X} FAIL" -f $i)
        continue
    }
    $out = [uint64]$r.gmOutput
    $status = [byte]($out -band 0xFF)
    $value = [byte](($out -shr 8) -band 0xFF)
    Write-GpuLog $log ("  id=0x{0:X} raw=0x{1:X} status=0x{2:X2} value=0x{3:X2}" -f $i, $out, $status, $value)
    if ($status -eq 0 -and $value -le 3) {
        [void]$enumLike.Add(('ProfileSetting[0x{0:X}]=0x{1:X2}' -f $i, $value))
    }
}

Write-GpuLog $log '=== enum-like candidates (0..3) ===' Cyan
if ($enumLike.Count -eq 0) {
    Write-GpuLog $log '  (none)' Yellow
} else {
    foreach ($e in $enumLike) { Write-GpuLog $log ("  {0}" -f $e) }
}

$end = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $end -Label 'END'
Write-GpuLog $log ("DONE. Log: {0}" -f $log) Green
Write-Host "Log: $log" -ForegroundColor Cyan
