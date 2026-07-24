#Requires -Version 5.1
<#
.SYNOPSIS
  Switch GPU presentation on PHN18 using the proven PnP path (PreySense Endurance/Standard).

.DESCRIPTION
  Based on probe matrix results (REFERENCE_GPU_KEYS.md / phn18_matrix_*):

    Endurance  = disable NVIDIA display PnP (VEN_10DE) + stop NVDisplay.ContainerLocalSystem
    Standard   = enable NVIDIA display PnP + restart NVDisplay.ContainerLocalSystem

  This is NOT NVIDIA DDS "GPU only" (that still needs NVCP Display Mode).
  AcerService GPU_MODE / BIOS offset 80 are unavailable on PHN18.

.PARAMETER Mode
  Status     - show MuxSignature + NVIDIA PnP state (no changes)
  Endurance  - iGPU-only style (disable dGPU device)
  Standard   - restore hybrid (enable dGPU device)
  Cycle      - Endurance, wait, then Standard (self-test)

.PARAMETER Force
  Skip YES confirmation for Endurance/Standard.

.EXAMPLE
  .\switch_gpu.ps1 -Mode Status
  .\switch_gpu.ps1 -Mode Endurance
  .\switch_gpu.ps1 -Mode Standard
  .\switch_gpu.ps1 -Mode Cycle -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('Status', 'Endurance', 'Standard', 'Cycle')]
    [string]$Mode = 'Status',

    [switch]$Force,
    [int]$CycleHoldSeconds = 4
)

$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\_gpu_common.ps1"
. "$PSScriptRoot\_acer_service.ps1"

$log = New-GpuLog 'switch_gpu'
Write-GpuLog $log '=== switch_gpu (PHN18 PnP path) ===' White
Write-GpuLog $log ("Mode={0} Force={1}" -f $Mode, [bool]$Force)
Write-GpuLog $log 'Endurance/Standard = Windows device enable/disable, not NVCP DDS Ultimate.' Yellow

function Get-GpuSwitchSnapshot {
    $fp = Get-GpuFingerprint
    $devs = @(Get-NvidiaDisplayDevices)
    $nvSvc = Get-Service -Name 'NVDisplay.ContainerLocalSystem' -ErrorAction SilentlyContinue
    return [pscustomobject]@{
        Timestamp     = (Get-Date).ToString('o')
        MuxSignature  = $fp.MuxSignature
        OwnerKind     = $fp.OwnerKind
        NvidiaDisplay = $fp.NvidiaDisplay
        NvidiaDevices = @($devs)
        NvService     = $(if ($nvSvc) { "$($nvSvc.Status)" } else { 'missing' })
        Fingerprint   = $fp
    }
}

function Write-GpuSwitchSnapshot {
    param([string]$Label, $Snap)
    Write-GpuLog $log ("{0} mux={1} nv_svc={2}" -f $Label, $Snap.MuxSignature, $Snap.NvService) Cyan
    if ($Snap.NvidiaDevices.Count -eq 0) {
        Write-GpuLog $log '  NVIDIA PnP: (none present вЂ” disabled or removed)' Yellow
    } else {
        foreach ($d in $Snap.NvidiaDevices) {
            Write-GpuLog $log ("  NVIDIA PnP: status={0} '{1}'" -f $d.Status, $d.FriendlyName)
        }
    }
    Write-GpuFingerprint -LogPath $log -Fp $Snap.Fingerprint -Label $Label
}

function Confirm-GpuSwitch([string]$Message) {
    if ($Force) { return $true }
    Write-Host ''
    Write-Host $Message -ForegroundColor Yellow
    Write-Host 'Type YES to continue:' -ForegroundColor Yellow
    return ((Read-Host) -eq 'YES')
}

function Invoke-GpuEndurance {
    Write-GpuLog $log 'Applying Endurance (disable NVIDIA display + stop NV service)...' Yellow
    if (-not (Confirm-GpuSwitch 'Disable NVIDIA GPU device (iGPU-only style). Games/CUDA will not see dGPU.')) {
        Write-GpuLog $log 'Aborted.' Yellow
        return [pscustomobject]@{ Ok = $false; Detail = 'aborted' }
    }
    if (-not (Confirm-GpuAdmin)) {
        return [pscustomobject]@{ Ok = $false; Detail = 'admin_required' }
    }

    $before = Get-GpuSwitchSnapshot
    Write-GpuSwitchSnapshot -Label 'BEFORE' -Snap $before

    $pnp = Set-NvidiaDisplayDeviceState -Enable:$false
    Write-GpuLog $log ("PnP disable: Ok={0} {1}" -f $pnp.Ok, $pnp.Detail) $(if ($pnp.Ok) { 'Green' } else { 'Red' })
    $svc = Set-NvidiaContainerService -Action Stop
    Write-GpuLog $log ("NV service stop: Ok={0} {1}" -f $svc.Ok, $svc.Detail)

    Start-Sleep -Seconds 2
    $after = Get-GpuSwitchSnapshot
    Write-GpuSwitchSnapshot -Label 'AFTER' -Snap $after

    $hit = $before.MuxSignature -ne $after.MuxSignature -or $before.NvidiaDevices.Count -ne $after.NvidiaDevices.Count
    $ok = [bool]$pnp.Ok
    Write-GpuLog $log ("RESULT Endurance Ok={0} StateChanged={1}" -f $ok, $hit) $(if ($ok) { 'Green' } else { 'Red' })
    Write-GpuLog $log 'Restore: .\switch_gpu.ps1 -Mode Standard' White
    return [pscustomobject]@{
        Ok = $ok; Detail = $pnp.Detail; Before = $before; After = $after; StateChanged = $hit
    }
}

function Invoke-GpuStandard {
    Write-GpuLog $log 'Applying Standard (enable NVIDIA display + restart NV service)...' Yellow
    if (-not (Confirm-GpuSwitch 'Re-enable NVIDIA GPU device (hybrid / Optimus-style availability).')) {
        Write-GpuLog $log 'Aborted.' Yellow
        return [pscustomobject]@{ Ok = $false; Detail = 'aborted' }
    }
    if (-not (Confirm-GpuAdmin)) {
        return [pscustomobject]@{ Ok = $false; Detail = 'admin_required' }
    }

    $before = Get-GpuSwitchSnapshot
    Write-GpuSwitchSnapshot -Label 'BEFORE' -Snap $before

    $pnp = Set-NvidiaDisplayDeviceState -Enable:$true
    Write-GpuLog $log ("PnP enable: Ok={0} {1}" -f $pnp.Ok, $pnp.Detail) $(if ($pnp.Ok) { 'Green' } else { 'Red' })
    $svc = Set-NvidiaContainerService -Action Restart
    Write-GpuLog $log ("NV service restart: Ok={0} {1}" -f $svc.Ok, $svc.Detail)

    Start-Sleep -Seconds 3
    $after = Get-GpuSwitchSnapshot
    Write-GpuSwitchSnapshot -Label 'AFTER' -Snap $after

    $present = $after.NvidiaDevices.Count -gt 0 -and ($after.NvidiaDevices | Where-Object { $_.Status -eq 'OK' })
    $ok = [bool]$pnp.Ok -and [bool]$present
    Write-GpuLog $log ("RESULT Standard Ok={0} NvidiaPresentOk={1}" -f $ok, [bool]$present) $(if ($ok) { 'Green' } else { 'Red' })
    return [pscustomobject]@{
        Ok = $ok; Detail = $pnp.Detail; Before = $before; After = $after; StateChanged = ($before.MuxSignature -ne $after.MuxSignature)
    }
}

function Show-GpuStatus {
    $snap = Get-GpuSwitchSnapshot
    Write-GpuSwitchSnapshot -Label 'STATUS' -Snap $snap

    $modeGuess = 'Unknown'
    $okDevs = @($snap.NvidiaDevices | Where-Object { $_.Status -eq 'OK' })
    if ($okDevs.Count -eq 0) {
        $modeGuess = 'Endurance-like (NVIDIA PnP absent/disabled)'
    } elseif ($snap.OwnerKind -eq 'NVIDIA') {
        $modeGuess = 'Discrete panel (DDS / NVIDIA GPU only) вЂ” set via NVCP'
    } else {
        $modeGuess = 'Standard-like hybrid (NVIDIA present, panel on Intel)'
    }
    Write-GpuLog $log ("Interpreted mode: {0}" -f $modeGuess) White
    Write-GpuLog $log 'Commands: -Mode Endurance | -Mode Standard | -Mode Cycle' Gray
    return $snap
}

# --- main ---
$result = $null
switch ($Mode) {
    'Status' {
        $null = Show-GpuStatus
    }
    'Endurance' {
        $result = Invoke-GpuEndurance
    }
    'Standard' {
        $result = Invoke-GpuStandard
    }
    'Cycle' {
        Write-GpuLog $log 'Self-test Cycle: Endurance -> hold -> Standard' Cyan
        $Force = $true
        $r1 = Invoke-GpuEndurance
        if (-not $r1.Ok) {
            Write-GpuLog $log 'Cycle aborted: Endurance failed.' Red
            $result = $r1
            break
        }
        Write-GpuLog $log ("Holding {0}s..." -f $CycleHoldSeconds)
        Start-Sleep -Seconds $CycleHoldSeconds
        $r2 = Invoke-GpuStandard
        $result = [pscustomobject]@{
            Ok = ($r1.Ok -and $r2.Ok)
            Detail = ("Endurance Ok={0} changed={1}; Standard Ok={2}" -f $r1.Ok, $r1.StateChanged, $r2.Ok)
            Endurance = $r1
            Standard = $r2
        }
        Write-GpuLog $log ("CYCLE RESULT Ok={0} {1}" -f $result.Ok, $result.Detail) $(if ($result.Ok) { 'Green' } else { 'Red' })
    }
}

Write-GpuLog $log ("Log: {0}" -f $log) White
Write-Host ''
Write-Host "Log: $log" -ForegroundColor Cyan
if ($result -and -not $result.Ok) { exit 1 }
exit 0
