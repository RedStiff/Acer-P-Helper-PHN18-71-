#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Master automatic GPU-switch research suite (no app integration).

.DESCRIPTION
  Phases:
    1) fingerprint          (always)
    2) nv_registry          (always, no admin needed but fine elevated)
    3) nvcpl_hybrid         (always)
    4) baseline             (Acer WMI GET)
    5) sysinfo_scan         (Acer SysInfo/ProfileSetting GET)
    6) wmi_bruteforce       DryRun by default; -Apply for focused SET

  With -Apply: runs focused misc SET on 0x0C,0x0F,0x01,0x06,0x08 (not full 61).
  Pass -FullApply to SET all mode-like candidates (long, may blank screen).

.USAGE
  .\probe_gpu_auto.ps1
  .\probe_gpu_auto.ps1 -Apply
  .\probe_gpu_auto.ps1 -Apply -FullApply
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$FullApply,
    [switch]$SkipNvcpl,
    [switch]$SkipRegistry,
    [int]$SettleMs = 8000
)

. "$PSScriptRoot\_gpu_common.ps1"

if (-not (Confirm-GpuAdmin)) { exit 1 }

# Close app so it does not fight WMI / lighting during probes.
Get-Process -Name AcerPredatorTool -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$master = New-GpuLog 'probe_gpu_auto'
$summaryPath = Join-Path $PSScriptRoot ('probe_gpu_auto_summary_{0}.txt' -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
$failed = 0
$hits = New-Object System.Collections.Generic.List[string]

function Run-Phase {
    param(
        [string]$Name,
        [string]$Script,
        [hashtable]$ScriptArgs = @{}
    )
    Write-GpuLog $master ("=== PHASE {0} ===" -f $Name) Cyan
    if (-not (Test-Path -LiteralPath $Script)) {
        Write-GpuLog $master ("MISSING {0}" -f $Script) Red
        $script:failed++
        return
    }
    & $Script @ScriptArgs
    $code = $LASTEXITCODE
    if ($null -ne $code -and $code -ne 0) {
        Write-GpuLog $master ("phase {0} exit={1}" -f $Name, $code) Red
        $script:failed++
    } else {
        Write-GpuLog $master ("phase {0} ok" -f $Name) Green
    }
}

Write-GpuLog $master '=== probe_gpu_auto master ===' Green
Write-GpuLog $master ("Apply={0} FullApply={1} SettleMs={2}" -f $Apply.IsPresent, $FullApply.IsPresent, $SettleMs)
Write-GpuLog $master ("host={0} user={1}" -f $env:COMPUTERNAME, $env:USERNAME)

$fp0 = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $master -Fp $fp0 -Label 'MASTER_START'

Run-Phase -Name 'fingerprint' -Script (Join-Path $PSScriptRoot 'probe_gpu_fingerprint.ps1')

if (-not $SkipRegistry) {
    Run-Phase -Name 'nv_registry' -Script (Join-Path $PSScriptRoot 'probe_gpu_nv_registry.ps1')
}

if (-not $SkipNvcpl) {
    Run-Phase -Name 'nvcpl_hybrid' -Script (Join-Path $PSScriptRoot 'probe_gpu_nvcpl_hybrid.ps1')
}

Run-Phase -Name 'baseline' -Script (Join-Path $PSScriptRoot 'probe_gpu_baseline.ps1')
Run-Phase -Name 'sysinfo_scan' -Script (Join-Path $PSScriptRoot 'probe_gpu_sysinfo_scan.ps1')

$bfArgs = @{ SettleMs = $SettleMs; Values = @(0, 1, 2) }
if ($Apply) {
    $bfArgs.Apply = $true
    if (-not $FullApply) {
        $bfArgs.Ids = [uint64[]]@(0x0C, 0x0F, 0x01, 0x06, 0x08)
        Write-GpuLog $master 'bruteforce: FOCUSED Apply Ids=0x0C,0x0F,0x01,0x06,0x08' Yellow
    } else {
        Write-GpuLog $master 'bruteforce: FULL Apply (all mode-like candidates)' Yellow
    }
} else {
    Write-GpuLog $master 'bruteforce: DryRun (pass -Apply for SET)' Yellow
}

Run-Phase -Name 'wmi_bruteforce' -Script (Join-Path $PSScriptRoot 'probe_gpu_wmi_bruteforce.ps1') -ScriptArgs $bfArgs

# Collect latest bruteforce HIT lines into summary
$latestBf = Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'probe_gpu_wmi_bruteforce_*.log' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($latestBf) {
    Select-String -LiteralPath $latestBf.FullName -Pattern '^\[.+\] HIT ' | ForEach-Object {
        [void]$hits.Add($_.Line)
    }
}

$fp1 = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $master -Fp $fp1 -Label 'MASTER_END'

$summary = @()
$summary += "probe_gpu_auto summary $(Get-Date -Format o)"
$summary += "Apply=$($Apply.IsPresent) FullApply=$($FullApply.IsPresent) failed=$failed"
$summary += "start_mux=$($fp0.MuxSignature)"
$summary += "end_mux=$($fp1.MuxSignature)"
$summary += "master_log=$master"
if ($hits.Count -eq 0) {
    $summary += 'WMI_HITS=0'
    $summary += 'Conclusion: no Acer misc SET changed panel mux fingerprint in this run (or DryRun).'
    $summary += 'Next: NVCP Display Mode is likely the only DDS control (no public SetHybridMode).'
} else {
    $summary += "WMI_HITS=$($hits.Count)"
    $summary += $hits
}

$summary | Set-Content -LiteralPath $summaryPath -Encoding UTF8
foreach ($line in $summary) { Write-GpuLog $master $line Cyan }

Write-GpuLog $master ("MASTER DONE failed={0} summary={1}" -f $failed, $summaryPath) Green
Write-Host ''
Write-Host "Master log: $master" -ForegroundColor Cyan
Write-Host "Summary:    $summaryPath" -ForegroundColor Cyan
exit $failed
