#Requires -Version 5.1
<#
.SYNOPSIS
  PHN18 GPU path matrix — find what actually changes MuxSignature on this machine.

.DESCRIPTION
  Runs a checklist of approaches, logs each result, writes MATRIX_SUMMARY.txt.
  Default is read-only / non-destructive. Destructive steps need explicit switches.

.PARAMETER ApplyPnp
  Run Endurance (disable NVIDIA) then immediately restore Standard (enable + NV restart).
  Captures pnp_matrix_capture_*/DIFF.txt

.PARAMETER ApplyAgentReg
  Temporarily set HKLM\...\AdvanceSettings\discrete_gpu_support=1, snapshot, restore.
  Unlikely to switch mux; for observation only.

.PARAMETER SkipNvcpl
  Skip compiling/running NvCpl HybridMode probe.

.EXAMPLE
  .\probe_gpu_phn18_matrix.cmd
  .\probe_gpu_phn18_matrix.ps1 -ApplyPnp
#>
[CmdletBinding()]
param(
    [switch]$ApplyPnp,
    [switch]$ApplyAgentReg,
    [switch]$SkipNvcpl
)

$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\_gpu_common.ps1"
. "$PSScriptRoot\_acer_service.ps1"
. "$PSScriptRoot\_acer_observe.ps1"

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$sessionDir = Join-Path $PSScriptRoot ("phn18_matrix_{0}" -f $stamp)
New-Item -ItemType Directory -Path $sessionDir -Force | Out-Null
$log = Join-Path $sessionDir 'matrix.log'
$summaryPath = Join-Path $sessionDir 'MATRIX_SUMMARY.txt'

function MLog([string]$Message, [ConsoleColor]$Color = [ConsoleColor]::Gray) {
    Write-GpuLog $log $Message $Color
}

$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Outcome,  # WORKS / NO_HIT / UNAVAILABLE / SKIPPED / ERROR
        [string]$Detail,
        [string]$MuxBefore = '',
        [string]$MuxAfter = ''
    )
    $row = [pscustomobject]@{
        Id        = $Id
        Name      = $Name
        Outcome   = $Outcome
        Detail    = $Detail
        MuxBefore = $MuxBefore
        MuxAfter  = $MuxAfter
        MuxHit    = ($(if ($MuxBefore -and $MuxAfter) { $MuxBefore -ne $MuxAfter } else { $false }))
    }
    [void]$results.Add($row)
    $color = switch ($Outcome) {
        'WORKS' { 'Green' }
        'NO_HIT' { 'Yellow' }
        'UNAVAILABLE' { 'DarkYellow' }
        'ERROR' { 'Red' }
        default { 'Gray' }
    }
    MLog ("[{0}] {1} => {2} | {3}" -f $Id, $Name, $Outcome, $Detail) $color
}

MLog '=== PHN18 GPU path matrix ===' White
MLog ("Session: {0}" -f $sessionDir)
MLog 'See also: REFERENCE_GPU_KEYS.md' Cyan

# --- A: baseline fingerprint ---
$fp0 = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $fp0 -Label 'BASE'
Save-AcerObserveJson $fp0 (Join-Path $sessionDir 'fingerprint_base.json')
Add-Result -Id 'A' -Name 'Fingerprint baseline' -Outcome 'WORKS' `
    -Detail $fp0.MuxSignature -MuxBefore $fp0.MuxSignature -MuxAfter $fp0.MuxSignature

# --- B: AcerService PreySense TCP ---
$svc = Get-AcerServiceSvcInfo -ProbeProtocol
Save-AcerObserveJson $svc (Join-Path $sessionDir 'acer_service_probe.json')
if ($svc.ProtocolReady) {
    Add-Result -Id 'B' -Name 'AcerService PreySense TCP (46933 GPU_MODE)' -Outcome 'WORKS' `
        -Detail ("commandPort={0}" -f $svc.CommandPort)
} else {
    Add-Result -Id 'B' -Name 'AcerService PreySense TCP (46933 GPU_MODE)' -Outcome 'UNAVAILABLE' `
        -Detail ("listen=[{0}]; {1}" -f ($svc.ListenPorts -join ','), ($svc.Notes -join ' | '))
}

# --- C: BIOS GetBiosOptions ---
$bios = Get-BiosOptionsData
Save-AcerObserveJson $bios (Join-Path $sessionDir 'bios_get.json')
if ($bios.Ok) {
    Add-Result -Id 'C' -Name 'BIOS GetBiosOptions (offset 80 display mode)' -Outcome 'WORKS' `
        -Detail ("offset80={0} len={1}" -f $bios.Data[80], $bios.Data.Length)
} else {
    Add-Result -Id 'C' -Name 'BIOS GetBiosOptions (offset 80 display mode)' -Outcome 'UNAVAILABLE' `
        -Detail "$($bios.Error)"
}

# --- D: AcerAgent caps ---
$caps = Get-AcerAgentGpuCapabilitySnapshot
Save-AcerObserveJson $caps (Join-Path $sessionDir 'acer_agent_caps.json')
Add-Result -Id 'D' -Name 'AcerAgent AdvanceSettings caps' -Outcome 'WORKS' `
    -Detail ("discrete_gpu_support={0}; dgpu_mode_capability={1}" -f `
        $caps['discrete_gpu_support'], $caps['dgpu_mode_capability'])

# --- E: Acer misc GET sample ---
$miscOutcome = 'SKIPPED'
$miscDetail = 'not admin'
if (Test-IsAdmin) {
    try {
        $inst = Get-AcerGaming
        $ids = @(0x01, 0x06, 0x08, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10)
        $map = [ordered]@{}
        foreach ($id in $ids) {
            $g = Get-MiscSetting -Inst $inst -Id ([uint64]$id)
            $map[('0x{0:X2}' -f $id)] = $(if ($g.Ok) {
                ('status=0x{0:X2} value=0x{1:X2}' -f $g.Status, $g.Value)
            } else { 'FAIL' })
        }
        Save-AcerObserveJson $map (Join-Path $sessionDir 'acer_misc_sample.json')
        $miscOutcome = 'WORKS'
        $miscDetail = ($map.GetEnumerator() | ForEach-Object { '{0}={1}' -f $_.Key, $_.Value }) -join '; '
    } catch {
        $miscOutcome = 'ERROR'
        $miscDetail = $_.Exception.Message
    }
}
Add-Result -Id 'E' -Name 'Acer misc GET sample (incl 0x0B power)' -Outcome $miscOutcome -Detail $miscDetail

# --- F: NvCpl HybridMode GET ---
if ($SkipNvcpl) {
    Add-Result -Id 'F' -Name 'NvCpl GetHybridMode' -Outcome 'SKIPPED' -Detail '-SkipNvcpl'
} else {
    try {
        $nvcplLog = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'probe_gpu_nvcpl_hybrid.ps1') 2>&1 |
            Out-String
        $nvcplLog | Set-Content (Join-Path $sessionDir 'nvcpl_hybrid_console.txt') -Encoding UTF8
        $hit = Select-String -Path (Join-Path $sessionDir 'nvcpl_hybrid_console.txt') -Pattern 'HybridMode|MsHybrid|FATAL|GetHybrid' -ErrorAction SilentlyContinue |
            Select-Object -First 8 |
            ForEach-Object { $_.Line.Trim() }
        Add-Result -Id 'F' -Name 'NvCpl GetHybridMode' -Outcome 'WORKS' `
            -Detail ($(if ($hit) { $hit -join ' || ' } else { 'ran; see nvcpl_hybrid_console.txt' }))
    } catch {
        Add-Result -Id 'F' -Name 'NvCpl GetHybridMode' -Outcome 'ERROR' -Detail $_.Exception.Message
    }
}

# --- G: NVIDIA registry interesting keys (read) ---
try {
    $regScript = Join-Path $PSScriptRoot 'probe_gpu_nv_registry.ps1'
    $regOut = & powershell -NoProfile -ExecutionPolicy Bypass -File $regScript 2>&1 | Out-String
    $regOut | Set-Content (Join-Path $sessionDir 'nv_registry_console.txt') -Encoding UTF8
    Add-Result -Id 'G' -Name 'NVIDIA registry dump' -Outcome 'WORKS' -Detail 'see nv_registry_console.txt / latest probe_gpu_nv_registry_*.log'
} catch {
    Add-Result -Id 'G' -Name 'NVIDIA registry dump' -Outcome 'ERROR' -Detail $_.Exception.Message
}

# --- H: optional AcerAgent reg toggle observe ---
if (-not $ApplyAgentReg) {
    Add-Result -Id 'H' -Name 'AcerAgent discrete_gpu_support toggle' -Outcome 'SKIPPED' `
        -Detail 'pass -ApplyAgentReg to observe (auto-restores)'
} elseif (-not (Test-IsAdmin)) {
    Add-Result -Id 'H' -Name 'AcerAgent discrete_gpu_support toggle' -Outcome 'ERROR' -Detail 'admin required'
} else {
    $path = 'HKLM:\SOFTWARE\OEM\AcerAgentService\AdvanceSettings'
    $beforeFp = Get-GpuFingerprint
    $old = $null
    try {
        $old = (Get-ItemProperty -LiteralPath $path -ErrorAction Stop).discrete_gpu_support
        $snapBefore = Get-AcerObserveSnapshot -Label 'BEFORE_AGENT_REG' -LogPath $log
        Set-ItemProperty -LiteralPath $path -Name 'discrete_gpu_support' -Value 1 -Type DWord -ErrorAction Stop
        Start-Sleep -Seconds 2
        $snapAfter = Get-AcerObserveSnapshot -Label 'AFTER_AGENT_REG' -LogPath $log
        Set-ItemProperty -LiteralPath $path -Name 'discrete_gpu_support' -Value $old -Type DWord -ErrorAction SilentlyContinue
        $afterFp = Get-GpuFingerprint
        $capDir = Join-Path $sessionDir 'agent_reg_capture'
        New-Item -ItemType Directory -Path $capDir -Force | Out-Null
        Save-AcerObserveJson $snapBefore (Join-Path $capDir 'before.json')
        Save-AcerObserveJson $snapAfter (Join-Path $capDir 'after.json')
        Write-AcerObserveDiff -Before $snapBefore -After $snapAfter -TcpTrace $null `
            -Action 'discrete_gpu_support=1 (restored)' `
            -DiffPath (Join-Path $capDir 'DIFF.txt') -LogPath $log | Out-Null
        $hit = $beforeFp.MuxSignature -ne $afterFp.MuxSignature
        Add-Result -Id 'H' -Name 'AcerAgent discrete_gpu_support toggle' `
            -Outcome ($(if ($hit) { 'WORKS' } else { 'NO_HIT' })) `
            -Detail ("old={0} temporary=1 restored; see agent_reg_capture/DIFF.txt" -f $old) `
            -MuxBefore $beforeFp.MuxSignature -MuxAfter $afterFp.MuxSignature
    } catch {
        if ($null -ne $old) {
            try { Set-ItemProperty -LiteralPath $path -Name 'discrete_gpu_support' -Value $old -Type DWord } catch { }
        }
        Add-Result -Id 'H' -Name 'AcerAgent discrete_gpu_support toggle' -Outcome 'ERROR' -Detail $_.Exception.Message
    }
}

# --- I: optional PnP Endurance cycle ---
if (-not $ApplyPnp) {
    Add-Result -Id 'I' -Name 'PnP Endurance cycle (disable+restore NVIDIA)' -Outcome 'SKIPPED' `
        -Detail 'pass -ApplyPnp (will briefly disable dGPU then restore)'
} elseif (-not (Test-IsAdmin)) {
    Add-Result -Id 'I' -Name 'PnP Endurance cycle' -Outcome 'ERROR' -Detail 'admin required'
} else {
    $beforeFp = Get-GpuFingerprint
    $capDir = Join-Path $sessionDir 'pnp_cycle_capture'
    New-Item -ItemType Directory -Path $capDir -Force | Out-Null
    try {
        $snapBefore = Get-AcerObserveSnapshot -Label 'BEFORE_PNP' -LogPath $log
        Save-AcerObserveJson $snapBefore (Join-Path $capDir 'before.json')
        $dis = Set-NvidiaDisplayDeviceState -Enable:$false
        Save-AcerObserveJson $dis (Join-Path $capDir 'disable_result.json')
        $null = Set-NvidiaContainerService -Action Stop
        Start-Sleep -Seconds 3
        $midFp = Get-GpuFingerprint
        Write-GpuFingerprint -LogPath $log -Fp $midFp -Label 'PNP_DISABLED'
        $snapMid = Get-AcerObserveSnapshot -Label 'AFTER_DISABLE' -LogPath $log
        Save-AcerObserveJson $snapMid (Join-Path $capDir 'after_disable.json')

        $en = Set-NvidiaDisplayDeviceState -Enable:$true
        Save-AcerObserveJson $en (Join-Path $capDir 'enable_result.json')
        $null = Set-NvidiaContainerService -Action Restart
        Start-Sleep -Seconds 3
        $afterFp = Get-GpuFingerprint
        Write-GpuFingerprint -LogPath $log -Fp $afterFp -Label 'PNP_RESTORED'
        $snapAfter = Get-AcerObserveSnapshot -Label 'AFTER_RESTORE' -LogPath $log
        Save-AcerObserveJson $snapAfter (Join-Path $capDir 'after_restore.json')
        Write-AcerObserveDiff -Before $snapBefore -After $snapMid -TcpTrace $null `
            -Action 'PnP disable NVIDIA' -DiffPath (Join-Path $capDir 'DIFF_disable.txt') -LogPath $log | Out-Null
        Write-AcerObserveDiff -Before $snapMid -After $snapAfter -TcpTrace $null `
            -Action 'PnP restore NVIDIA' -DiffPath (Join-Path $capDir 'DIFF_restore.txt') -LogPath $log | Out-Null

        $hit = $beforeFp.MuxSignature -ne $midFp.MuxSignature
        Add-Result -Id 'I' -Name 'PnP Endurance cycle (disable+restore NVIDIA)' `
            -Outcome ($(if ($hit -and $dis.Ok) { 'WORKS' } elseif ($dis.Ok) { 'NO_HIT' } else { 'ERROR' })) `
            -Detail ("disableOk={0}; restoreOk={1}; mid={2}" -f $dis.Ok, $en.Ok, $midFp.MuxSignature) `
            -MuxBefore $beforeFp.MuxSignature -MuxAfter $midFp.MuxSignature
    } catch {
        try { Set-NvidiaDisplayDeviceState -Enable:$true | Out-Null } catch { }
        try { Set-NvidiaContainerService -Action Restart | Out-Null } catch { }
        Add-Result -Id 'I' -Name 'PnP Endurance cycle' -Outcome 'ERROR' -Detail $_.Exception.Message
    }
}

# --- J: reminder NVCP ---
Add-Result -Id 'J' -Name 'NVCP Display Mode (manual)' -Outcome 'WORKS' `
    -Detail 'Known working DDS path. Run probe_gpu_nvcp_capture.cmd for DIFF.'

# --- summary file ---
$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add('PHN18 GPU path MATRIX_SUMMARY')
[void]$lines.Add(('Created: {0}' -f (Get-Date).ToString('o')))
[void]$lines.Add(('Base mux: {0}' -f $fp0.MuxSignature))
[void]$lines.Add('')
[void]$lines.Add('Outcome legend: WORKS = usable on this machine; NO_HIT = ran but mux unchanged; UNAVAILABLE = API/service missing')
[void]$lines.Add('')
foreach ($r in $results) {
    [void]$lines.Add(('[{0}] {1}' -f $r.Id, $r.Name))
    [void]$lines.Add(('    Outcome={0} MuxHit={1}' -f $r.Outcome, $r.MuxHit))
    if ($r.MuxBefore) { [void]$lines.Add(('    MuxBefore={0}' -f $r.MuxBefore)) }
    if ($r.MuxAfter -and $r.MuxAfter -ne $r.MuxBefore) { [void]$lines.Add(('    MuxAfter ={0}' -f $r.MuxAfter)) }
    [void]$lines.Add(('    {0}' -f $r.Detail))
    [void]$lines.Add('')
}
[void]$lines.Add('Working candidates for app emulation:')
foreach ($r in $results | Where-Object { $_.Outcome -eq 'WORKS' -and ($_.MuxHit -or $_.Id -in @('A', 'J')) }) {
    [void]$lines.Add(('  - [{0}] {1}' -f $r.Id, $r.Name))
}
[void]$lines.Add('')
[void]$lines.Add('Reference: REFERENCE_GPU_KEYS.md')
$lines -join "`r`n" | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Save-AcerObserveJson @($results) (Join-Path $sessionDir 'results.json')

MLog ("SUMMARY: {0}" -f $summaryPath) Green
Write-Host ''
Write-Host "Session: $sessionDir" -ForegroundColor Cyan
Write-Host "Summary: $summaryPath" -ForegroundColor Cyan
Get-Content -LiteralPath $summaryPath
