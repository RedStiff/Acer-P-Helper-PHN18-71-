<#
.SYNOPSIS
  Non-admin GPU fingerprint snapshot (no Acer WMI).

.USAGE
  .\probe_gpu_fingerprint.ps1
#>
[CmdletBinding()]
param()

. "$PSScriptRoot\_gpu_common.ps1"

$log = New-GpuLog 'probe_gpu_fingerprint'
Write-GpuLog $log '=== GPU fingerprint (no admin / no WMI) ===' Green
$fp = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $fp -Label 'NOW'
Write-GpuLog $log ("DONE. Log: {0}" -f $log) Green
Write-Host "Log: $log" -ForegroundColor Cyan
