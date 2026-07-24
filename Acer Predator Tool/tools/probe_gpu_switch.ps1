#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Master automatic GPU-switch research runner (no app integration).

.DESCRIPTION
  1) probe_gpu_baseline.ps1   вЂ” read-only inventory + misc GET scan
  2) probe_gpu_wmi_bruteforce вЂ” DryRun plan by default; -Apply for SET tests

.USAGE
  cd $PSScriptRoot
  .\probe_gpu_switch.ps1              # baseline + dry-run plan
  .\probe_gpu_switch.ps1 -Apply       # also try SET on candidates (may blank screen)
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$SkipBaseline,
    [switch]$SkipBruteforce,
    [uint64[]]$Ids,
    [byte[]]$Values = @(0, 1, 2),
    [int]$SettleMs = 8000
)

. "$PSScriptRoot\_gpu_common.ps1"

if (-not (Confirm-GpuAdmin)) { exit 1 }

$master = New-GpuLog 'probe_gpu_switch'
Write-GpuLog $master '=== probe_gpu_switch master ===' Green
Write-GpuLog $master ("Apply={0}" -f $Apply.IsPresent)

$failed = 0

if (-not $SkipBaseline) {
    Write-GpuLog $master '--- running probe_gpu_baseline.ps1 ---' Cyan
    & "$PSScriptRoot\probe_gpu_baseline.ps1"
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        Write-GpuLog $master ("baseline exit={0}" -f $LASTEXITCODE) Red
        $failed++
    }
}

if (-not $SkipBruteforce) {
    Write-GpuLog $master '--- running probe_gpu_wmi_bruteforce.ps1 ---' Cyan
    $bfArgs = @{ SettleMs = $SettleMs; Values = $Values }
    if ($Apply) { $bfArgs.Apply = $true }
    if ($Ids) { $bfArgs.Ids = $Ids }
    & "$PSScriptRoot\probe_gpu_wmi_bruteforce.ps1" @bfArgs
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        Write-GpuLog $master ("bruteforce exit={0}" -f $LASTEXITCODE) Red
        $failed++
    }
}

Write-GpuLog $master ("MASTER DONE failed={0} log={1}" -f $failed, $master) Green
Write-Host ''
Write-Host "Master log: $master" -ForegroundColor Cyan
exit $failed
