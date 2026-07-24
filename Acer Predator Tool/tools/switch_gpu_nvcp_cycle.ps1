#Requires -Version 5.1
<#
.SYNOPSIS
  Full interactive NVCP Display Mode cycle: NVIDIA GPU only -> Optimus -> Automatic.

.DESCRIPTION
  DDS Display Mode has no public SET API on PHN18. This script opens NVIDIA Control Panel
  and asks YOU to select each mode, then records MuxSignature after every switch.

  Expected fingerprints (typical PHN18):
    NVIDIA GPU only  -> owner=NVIDIA | nv_display=Enabled
    Optimus          -> owner=Intel  | nv_display=Disabled
    Automatic        -> usually same as Optimus while idle on iGPU

.PARAMETER SkipOpenNvcp
  Do not auto-launch nvcplui.exe

.PARAMETER SettleSeconds
  Wait after Enter before measuring (default 5)

.EXAMPLE
  .\switch_gpu_nvcp_cycle.ps1
  .\switch_gpu_nvcp_cycle.cmd
#>
[CmdletBinding()]
param(
    [switch]$SkipOpenNvcp,
    [int]$SettleSeconds = 5
)

$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\_gpu_common.ps1"
. "$PSScriptRoot\_acer_service.ps1"

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$sessionDir = Join-Path $PSScriptRoot ("nvcp_cycle_{0}" -f $stamp)
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
$log = Join-Path $sessionDir 'cycle.log'
$summaryPath = Join-Path $sessionDir 'CYCLE_SUMMARY.txt'

function Write-CycleLog {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ConsoleColor]$Color = [ConsoleColor]::Gray
    )
    Write-GpuLog $log $Message $Color
}

function Open-Nvcp {
    if ($SkipOpenNvcp) {
        Write-CycleLog 'SkipOpenNvcp - open NVIDIA Control Panel manually (Display -> Manage Display Mode)' Yellow
        return
    }
    $opened = Open-NvidiaDisplayModeUi -Source Tray
    if ($opened.Ok) {
        Write-CycleLog ("Opened display-mode UI via {0} ({1})" -f $opened.Method, $opened.Detail) Cyan
    } else {
        Write-CycleLog ("Could not open UI ({0}): {1}" -f $opened.Method, $opened.Detail) Yellow
        Write-CycleLog 'Open classic NVIDIA Control Panel from Start menu.' Yellow
    }
}

function Get-CycleSnap {
    param([Parameter(Mandatory)][string]$Label)

    $fp = Get-GpuFingerprint
    $devs = @(Get-NvidiaDisplayDevices)
    $snap = [pscustomobject]@{
        Label          = $Label
        Timestamp      = (Get-Date).ToString('o')
        MuxSignature   = $fp.MuxSignature
        OwnerKind      = $fp.OwnerKind
        NvidiaDisplay  = $fp.NvidiaDisplay
        NvidiaPowerW   = $fp.NvidiaPowerW
        NvidiaClockMhz = $fp.NvidiaClockMhz
        NvidiaPnP      = @($devs | ForEach-Object { '{0}:{1}' -f $_.Status, $_.FriendlyName })
        Fingerprint    = $fp
    }
    $jsonPath = Join-Path $sessionDir ("{0}.json" -f ($Label -replace '[^\w\-]+', '_'))
    $snap | Select-Object Label, Timestamp, MuxSignature, OwnerKind, NvidiaDisplay, NvidiaPowerW, NvidiaClockMhz, NvidiaPnP |
        ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath $jsonPath -Encoding UTF8
    Write-GpuFingerprint -LogPath $log -Fp $fp -Label $Label
    return $snap
}

function Test-ExpectedMux {
    param(
        [string]$ModeName,
        [string]$Mux
    )
    switch -Regex ($ModeName) {
        'NVIDIA' {
            if ($Mux -match 'owner=NVIDIA' -and $Mux -match 'nv_display=Enabled') { return 'PASS' }
            return 'FAIL'
        }
        'Optimus' {
            if ($Mux -match 'owner=Intel' -and $Mux -match 'nv_display=Disabled') { return 'PASS' }
            return 'FAIL'
        }
        'Automatic|Auto' {
            # Automatic often looks like Optimus at idle; accept Intel panel.
            if ($Mux -match 'owner=Intel') { return 'PASS_LIKELY' }
            if ($Mux -match 'owner=NVIDIA') { return 'PASS_ALT_NVIDIA' }
            return 'FAIL'
        }
        default { return 'UNKNOWN' }
    }
}

function Wait-UserModeStep {
    param(
        [Parameter(Mandatory)][string]$ModeName,
        [Parameter(Mandatory)][string]$UiLabel,
        [int]$Step,
        [int]$Total
    )

    Write-Host ''
    Write-Host '========================================' -ForegroundColor Cyan
    Write-Host ("  STEP {0}/{1}: {2}" -f $Step, $Total, $ModeName) -ForegroundColor Cyan
    Write-Host '========================================' -ForegroundColor Cyan
    Write-Host 'In NVIDIA Control Panel:'
    Write-Host '  Display  ->  Manage Display Mode'
    Write-Host '  (or Manage Power and Display mode)'
    Write-Host ''
    Write-Host ("Select:  {0}" -f $UiLabel) -ForegroundColor Yellow
    Write-Host 'Apply / confirm. Expect a short black screen (5-15s).'
    Write-Host 'When the desktop is back, return here.'
    Write-Host ''

    Open-Nvcp
    $null = Read-Host 'Press Enter AFTER the mode has finished applying'
    Write-CycleLog ("User confirmed step: {0}" -f $ModeName) Cyan
    Write-CycleLog ("Settling {0}s..." -f $SettleSeconds)
    Start-Sleep -Seconds $SettleSeconds
}

# --- main ---
Write-CycleLog '=== NVCP Display Mode FULL CYCLE ===' White
Write-CycleLog ("Session: {0}" -f $sessionDir)
Write-CycleLog 'Modes: NVIDIA GPU only -> Optimus -> Automatic'

Write-Host ''
Write-Host 'PHN18 NVCP full cycle test' -ForegroundColor Green
Write-Host 'Close AcerPredatorTool / games / PreySense before switching.' -ForegroundColor Yellow
Write-Host ("Session folder: {0}" -f $sessionDir)
Write-Host ''

# Preflight: NVIDIA must be present (not Endurance-disabled)
$preDevs = @(Get-NvidiaDisplayDevices | Where-Object { $_.Status -eq 'OK' })
if ($preDevs.Count -eq 0) {
    Write-Host 'WARNING: NVIDIA display device is not OK (Endurance/PnP disabled?).' -ForegroundColor Red
    Write-Host 'Restore first:  .\switch_gpu.ps1 -Mode Standard -Force' -ForegroundColor Yellow
    Write-Host ''
    $cont = Read-Host 'Continue anyway? Type YES'
    if ($cont -ne 'YES') {
        Write-CycleLog 'Aborted: NVIDIA PnP not OK' Red
        exit 2
    }
}

$baseline = Get-CycleSnap -Label '00_BASELINE'
Write-CycleLog ("BASELINE mux={0}" -f $baseline.MuxSignature) Green

$steps = @(
    @{ Id = '01_NVIDIA'; Name = 'NVIDIA GPU only'; Ui = 'NVIDIA GPU only' },
    @{ Id = '02_Optimus'; Name = 'Optimus'; Ui = 'Optimus' },
    @{ Id = '03_Automatic'; Name = 'Automatic'; Ui = 'Automatic' }
)

$results = New-Object System.Collections.Generic.List[object]
$prev = $baseline
$i = 0
foreach ($step in $steps) {
    $i++
    Wait-UserModeStep -ModeName $step.Name -UiLabel $step.Ui -Step $i -Total $steps.Count
    $snap = Get-CycleSnap -Label $step.Id
    $verdict = Test-ExpectedMux -ModeName $step.Name -Mux $snap.MuxSignature
    $changed = $prev.MuxSignature -ne $snap.MuxSignature
    $row = [pscustomobject]@{
        Step            = $i
        Mode            = $step.Name
        MuxSignature    = $snap.MuxSignature
        ChangedFromPrev = $changed
        PrevMux         = $prev.MuxSignature
        Verdict         = $verdict
        Timestamp       = $snap.Timestamp
    }
    [void]$results.Add($row)
    $color = switch ($verdict) {
        'PASS' { [ConsoleColor]::Green }
        'PASS_LIKELY' { [ConsoleColor]::Green }
        'PASS_ALT_NVIDIA' { [ConsoleColor]::Yellow }
        default { [ConsoleColor]::Red }
    }
    Write-CycleLog ("STEP {0} mux={1} changed={2} verdict={3}" -f $step.Name, $snap.MuxSignature, $changed, $verdict) $color
    Write-Host ("  mux={0}" -f $snap.MuxSignature) -ForegroundColor $color
    Write-Host ("  changed_from_prev={0}  verdict={1}" -f $changed, $verdict) -ForegroundColor $color
    $prev = $snap
}

$results | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $sessionDir 'results.json') -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add('NVCP Display Mode CYCLE_SUMMARY')
[void]$lines.Add(('Created: {0}' -f (Get-Date).ToString('o')))
[void]$lines.Add(('Session: {0}' -f $sessionDir))
[void]$lines.Add(('Baseline: {0}' -f $baseline.MuxSignature))
[void]$lines.Add('')
foreach ($r in $results) {
    [void]$lines.Add(('[{0}] {1}' -f $r.Step, $r.Mode))
    [void]$lines.Add(('    mux={0}' -f $r.MuxSignature))
    [void]$lines.Add(('    changed_from_prev={0}' -f $r.ChangedFromPrev))
    [void]$lines.Add(('    verdict={0}' -f $r.Verdict))
    [void]$lines.Add('')
}
$passCount = @($results | Where-Object { $_.Verdict -match '^PASS' }).Count
[void]$lines.Add(('PASS_COUNT={0}/{1}' -f $passCount, $results.Count))
[void]$lines.Add('')
[void]$lines.Add('Notes:')
[void]$lines.Add('- PASS = matches expected MuxSignature for that NVCP mode')
[void]$lines.Add('- PASS_LIKELY = Automatic idle often looks like Optimus (Intel panel)')
[void]$lines.Add('- DDS Ultimate/discrete is NVIDIA GPU only; Optimus/Auto keep hybrid mux')
[void]$lines.Add('- PnP Endurance (switch_gpu.ps1) is a different mechanism')

$text = $lines -join "`r`n"
Set-Content -LiteralPath $summaryPath -Value $text -Encoding UTF8
Write-CycleLog ("SUMMARY: {0}" -f $summaryPath) Green

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host $text
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ("Session: {0}" -f $sessionDir) -ForegroundColor Cyan
Write-Host ''
$null = Read-Host 'Press Enter to close'
