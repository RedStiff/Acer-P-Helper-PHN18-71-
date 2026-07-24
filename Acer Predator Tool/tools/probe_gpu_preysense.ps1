#Requires -Version 5.1
<#
.SYNOPSIS
  Probe Acer GPU modes using the PreySense approach, with deep AcerService observation.

.DESCRIPTION
  Based on https://github.com/hammadzaigham/PreySense

  PreySense model:
    UI Endurance (iGPU only)  -> keep mux hybrid(2); Disable NVIDIA PnP + stop NV service
    UI Standard  (hybrid)     -> mux hybrid(2); Enable NVIDIA PnP + restart NV service
    UI Ultimate  (dGPU only)  -> AcerService GPU_MODE=1 (discrete); REQUIRES REBOOT
    Leaving Ultimate          -> AcerService GPU_MODE=2; then PnP transition; REQUIRES REBOOT

  With -Apply (default), scripts write an observation session folder:
    acer_service_capture_YYYYMMDD_HHMMSS\
      before.json / after.json / DIFF.txt / tcp_gpu_mode.json / EMULATE.md

  Use DIFF.txt to see what AcerService changed so AcerPredatorTool can emulate
  it without depending on AcerServiceSvc.

.PARAMETER Action
  Status | Ultimate | Hybrid | Endurance | Standard | BiosMux

.PARAMETER Apply
  Actually perform SET / PnP changes (default: dry-run for non-Status).

.PARAMETER StartService
  Try to enable+start AcerServiceSvc if stopped/disabled (needed for GPU_MODE TCP).

.PARAMETER Reboot
  After a successful Ultimate/Hybrid mux SET, run shutdown /r /t 15.

.PARAMETER NoObserve
  Skip deep BEFORE/AFTER/DIFF capture (not recommended).

.EXAMPLE
  .\probe_gpu_preysense.ps1
  .\probe_gpu_preysense.ps1 -Action Ultimate -StartService -Apply
  .\probe_gpu_preysense.ps1 -Action Endurance -Apply
#>
[CmdletBinding()]
param(
    [ValidateSet('Status', 'Ultimate', 'Hybrid', 'Endurance', 'Standard', 'BiosMux')]
    [string]$Action = 'Status',

    [switch]$Apply,
    [switch]$StartService,
    [switch]$Reboot,
    [switch]$NoObserve,
    [ValidateSet(1, 2)]
    [int]$BiosMuxMode = 1
)

$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\_gpu_common.ps1"
. "$PSScriptRoot\_acer_service.ps1"
. "$PSScriptRoot\_acer_observe.ps1"

$Observe = -not $NoObserve

$log = New-GpuLog 'probe_gpu_preysense'
Write-GpuLog $log '=== PreySense-style GPU probe + AcerService observation ===' White
Write-GpuLog $log ("Action={0} Apply={1} StartService={2} Reboot={3} Observe={4}" -f `
    $Action, [bool]$Apply, [bool]$StartService, [bool]$Reboot, $Observe)
Write-GpuLog $log 'Reference: https://github.com/hammadzaigham/PreySense'
Write-GpuLog $log 'Goal: discover side-effects to emulate WITHOUT Acer services.' Cyan
Write-GpuLog $log 'Close AcerPredatorTool / PreySense / PredatorSense / games before -Apply.' Yellow

function Write-PreySenseStatus {
    $svc = Get-AcerServiceSvcInfo -ProbeProtocol
    Write-GpuLog $log ("AcerServiceSvc present={0} status={1} start={2}" -f `
        $svc.Present, $svc.Status, $svc.StartType) Cyan
    Write-GpuLog $log ("PreySense TCP protocolReady={0} commandPort={1} listen=[{2}]" -f `
        $svc.ProtocolReady, $svc.CommandPort, ($svc.ListenPorts -join ',')) Cyan
    foreach ($n in @($svc.Notes)) { Write-GpuLog $log ("  probe: {0}" -f $n) }

    $aes = Get-AcerServiceAesKeyBytes
    Write-GpuLog $log ("AESkey: {0}" -f ($(if ($aes) { 'present (32 ASCII)' } else { 'missing -> plain JSON' })))

    $caps = Get-AcerAgentGpuCapabilitySnapshot
    Write-GpuLog $log ("AcerAgent discrete_gpu_support={0} dgpu_mode_capability={1}" -f `
        $caps['discrete_gpu_support'], $caps['dgpu_mode_capability']) Cyan

    if ($svc.ProtocolReady) {
        $hs = Invoke-AcerServiceHandshake
        if ($hs.Ok -or $hs.Response) {
            Write-GpuLog $log ("Handshake OK={0} AesUsed={1} ElapsedMs={2}" -f $hs.Ok, $hs.AesUsed, $hs.ElapsedMs) Green
            if ($hs.Response) {
                $snippet = $hs.Response
                if ($snippet.Length -gt 240) { $snippet = $snippet.Substring(0, 240) + '...' }
                Write-GpuLog $log ("  response: {0}" -f $snippet)
            }
            Write-GpuLog $log ("  REQ hex: {0}" -f $hs.RequestHex)
            Write-GpuLog $log ("  RSP hex: {0}" -f $hs.ResponseHex)
        } else {
            Write-GpuLog $log ("Handshake failed: {0}" -f $hs.Error) Yellow
        }
    } else {
        Write-GpuLog $log 'PreySense GPU_MODE TCP not available on this machine.' Yellow
        Write-GpuLog $log 'PHN18 AcerService often listens on 15152 (HTTPS PWA), not PreySense 46933.' Yellow
        Write-GpuLog $log 'Will use BIOS Data[80] / PnP observation paths instead.' Yellow
    }

    $nvDevs = @(Get-NvidiaDisplayDevices)
    if ($nvDevs.Count -eq 0) {
        Write-GpuLog $log 'NVIDIA PnP display: none present (disabled or missing)' Yellow
    } else {
        foreach ($d in $nvDevs) {
            Write-GpuLog $log ("NVIDIA PnP: status={0} '{1}'" -f $d.Status, $d.FriendlyName)
            Write-GpuLog $log ("  id={0}" -f $d.InstanceId)
        }
    }

    $nvSvc = Get-Service -Name 'NVDisplay.ContainerLocalSystem' -ErrorAction SilentlyContinue
    if ($nvSvc) {
        Write-GpuLog $log ("NVDisplay.ContainerLocalSystem: {0}" -f $nvSvc.Status)
    } else {
        Write-GpuLog $log 'NVDisplay.ContainerLocalSystem: not found'
    }

    $bios = Get-BiosDisplayModeSnapshot
    Write-GpuLog $log ("BIOS Data[80] display-mode: available={0} value={1} err={2}" -f `
        $bios.Available, $bios.Offset80, $bios.Error) Cyan
    if ($bios.WindowHex) { Write-GpuLog $log ("  window: {0}" -f $bios.WindowHex) }

    $fp = Get-GpuFingerprint
    Write-GpuFingerprint -LogPath $log -Fp $fp -Label 'NOW'

    Write-GpuLog $log '---'
    Write-GpuLog $log 'PreySense mapping (for -Action):' White
    Write-GpuLog $log '  Endurance -> Disable NVIDIA PnP + Stop NV service (mux stays Hybrid/2)'
    Write-GpuLog $log '  Standard  -> Enable NVIDIA PnP + Restart NV service'
    Write-GpuLog $log '  Ultimate  -> AcerService GPU_MODE=1 (Discrete) + reboot'
    Write-GpuLog $log '  Hybrid    -> AcerService GPU_MODE=2 (Hybrid mux) + reboot if leaving Ultimate'
    Write-GpuLog $log '  BiosMux   -> AcerBiosConfigurationTool Data[80] fallback (1=auto,3=dGPU)'
    Write-GpuLog $log '---'
    Write-GpuLog $log 'Observation: -Apply writes acer_service_capture_*/DIFF.txt (TCP + WMI + BIOS + registry + PnP).'
}

function Confirm-Destructive([string]$Message) {
    Write-Host ''
    Write-Host $Message -ForegroundColor Yellow
    Write-Host 'Type YES to continue:' -ForegroundColor Yellow
    $ans = Read-Host
    return ($ans -eq 'YES')
}

function Invoke-ObservedPnpTransition {
    param(
        [Parameter(Mandatory)][ValidateSet('Endurance', 'Standard')][string]$Kind
    )

    $sessionDir = New-AcerObserveSessionDir -Prefix ('pnp_{0}_capture' -f $Kind.ToLowerInvariant())
    Write-GpuLog $log ("OBSERVE PnP session: {0}" -f $sessionDir) White

    $before = Get-AcerObserveSnapshot -Label ("BEFORE_{0}" -f $Kind) -LogPath $log
    Save-AcerObserveJson $before (Join-Path $sessionDir 'before.json')

    if ($Kind -eq 'Endurance') {
        $pnp = Set-NvidiaDisplayDeviceState -Enable:$false
        Write-GpuLog $log ("PnP disable: Ok={0} {1}" -f $pnp.Ok, $pnp.Detail) $(if ($pnp.Ok) { 'Green' } else { 'Red' })
        Save-AcerObserveJson $pnp (Join-Path $sessionDir 'pnp_result.json')
        $svc = Set-NvidiaContainerService -Action Stop
        Write-GpuLog $log ("NV service stop: Ok={0} {1}" -f $svc.Ok, $svc.Detail)
        Save-AcerObserveJson $svc (Join-Path $sessionDir 'nv_service_result.json')
    } else {
        $pnp = Set-NvidiaDisplayDeviceState -Enable:$true
        Write-GpuLog $log ("PnP enable: Ok={0} {1}" -f $pnp.Ok, $pnp.Detail) $(if ($pnp.Ok) { 'Green' } else { 'Red' })
        Save-AcerObserveJson $pnp (Join-Path $sessionDir 'pnp_result.json')
        $svc = Set-NvidiaContainerService -Action Restart
        Write-GpuLog $log ("NV service restart: Ok={0} {1}" -f $svc.Ok, $svc.Detail)
        Save-AcerObserveJson $svc (Join-Path $sessionDir 'nv_service_result.json')
    }

    Start-Sleep -Seconds 3
    $after = Get-AcerObserveSnapshot -Label ("AFTER_{0}" -f $Kind) -LogPath $log
    Save-AcerObserveJson $after (Join-Path $sessionDir 'after.json')
    Write-AcerObserveDiff -Before $before -After $after -TcpTrace $null `
        -Action ("PreySense {0} PnP path" -f $Kind) `
        -DiffPath (Join-Path $sessionDir 'DIFF.txt') `
        -LogPath $log | Out-Null
    Write-AcerEmulateStub -Path (Join-Path $sessionDir 'EMULATE.md') -Action $Kind -TcpTrace $null
    Write-GpuLog $log ("PnP OBSERVE done: {0}" -f $sessionDir) Green
    return $sessionDir
}

function Invoke-MuxSet {
    param(
        [Parameter(Mandatory)][ValidateSet(1, 2)][int]$MuxMode,
        [string]$Label
    )

    Write-GpuLog $log ("Mux SET target={0} ({1})" -f $MuxMode, $Label) Cyan

    if (-not $Apply) {
        $fp = Get-GpuFingerprint
        Write-GpuFingerprint -LogPath $log -Fp $fp -Label 'BEFORE'
        Write-GpuLog $log 'DRY-RUN: would observe + call AcerService GPU_MODE, then BIOS fallback.' Yellow
        Write-GpuLog $log 'Re-run with -Apply -StartService to capture TCP/WMI/BIOS/registry DIFF.'
        return
    }

    if (-not (Confirm-GpuAdmin)) { return }

    if ($Observe) {
        $obs = Invoke-AcerServiceObservedGpuMode -MuxMode $MuxMode -ActionLabel $Label `
            -LogPath $log -StartService:$StartService
        $ok = [bool]$obs.Ok

        if (-not $ok) {
            Write-GpuLog $log 'Observing BIOS fallback SetBiosOptions Data[80]...' Yellow
            $sessionDir = $obs.SessionDir
            $beforeBios = Get-AcerObserveSnapshot -Label 'BEFORE_BIOS_FALLBACK' -LogPath $log
            Save-AcerObserveJson $beforeBios (Join-Path $sessionDir 'before_bios_fallback.json')
            $bios = Set-GpuMuxBiosOffset80 -MuxMode $MuxMode
            Save-AcerObserveJson $bios (Join-Path $sessionDir 'bios_fallback_result.json')
            Write-GpuLog $log ("BIOS mux: Ok={0} {1}" -f $bios.Ok, $bios.Detail) $(if ($bios.Ok) { 'Green' } else { 'Red' })
            Start-Sleep -Seconds 2
            $afterBios = Get-AcerObserveSnapshot -Label 'AFTER_BIOS_FALLBACK' -LogPath $log
            Save-AcerObserveJson $afterBios (Join-Path $sessionDir 'after_bios_fallback.json')
            Write-AcerObserveDiff -Before $beforeBios -After $afterBios -TcpTrace $null `
                -Action ("BIOS fallback mux={0}" -f $MuxMode) `
                -DiffPath (Join-Path $sessionDir 'DIFF_bios_fallback.txt') `
                -LogPath $log | Out-Null
            $ok = [bool]$bios.Ok
        }

        Write-GpuLog $log ("MUX_CHANGED(pre-reboot)={0}" -f ($obs.Before.MuxSignature -ne $obs.After.MuxSignature)) Yellow
        Write-GpuLog $log ("Session: {0}" -f $obs.SessionDir) White
        Write-GpuLog $log ("DIFF:    {0}" -f $obs.DiffPath) White

        if ($ok -and $Reboot) {
            Write-GpuLog $log 'Reboot in 15s (shutdown /r /t 15). Cancel: shutdown /a' Red
            Write-GpuLog $log 'After reboot: run Status and compare BIOS offset80 / mux with before.json' Cyan
            Start-Process -FilePath 'shutdown.exe' -ArgumentList '/r','/t','15','/c','AcerPredatorTool AcerService observe' -WindowStyle Hidden
        } elseif ($ok) {
            Write-GpuLog $log 'SET path finished. Review DIFF.txt. Use -Reboot when ready (Ultimate usually needs it).' Green
        } else {
            Write-GpuLog $log 'SET failed on AcerService and BIOS fallback. Session still has logs.' Red
        }
        return
    }

    # Minimal path without observe
    if ($StartService -or -not (Test-AcerServicePortOpen)) {
        $start = Start-AcerServiceForProbe
        Write-GpuLog $log ("StartService: Ok={0} {1}" -f $start.Ok, $start.Detail)
    }
    $svcResult = $null
    if (Test-AcerServicePortOpen) {
        $svcResult = Set-AcerServiceGpuMode -MuxMode $MuxMode
        Write-GpuLog $log ("AcerService GPU_MODE={0}: Ok={1}" -f $MuxMode, $svcResult.Ok)
    }
    $ok = [bool]($svcResult -and $svcResult.Ok)
    if (-not $ok) {
        $bios = Set-GpuMuxBiosOffset80 -MuxMode $MuxMode
        Write-GpuLog $log ("BIOS mux: Ok={0} {1}" -f $bios.Ok, $bios.Detail)
        $ok = [bool]$bios.Ok
    }
    if ($ok -and $Reboot) {
        Start-Process shutdown.exe -ArgumentList '/r','/t','15' -WindowStyle Hidden
    }
}

function Invoke-EnduranceApply {
    Write-GpuLog $log 'Endurance path: Disable NVIDIA display + Stop NVDisplay service (PreySense iGPU-only).' Cyan
    if (-not $Apply) {
        Write-GpuLog $log 'DRY-RUN: would Disable-PnpDevice VEN_10DE + Stop-Service NVDisplay.ContainerLocalSystem' Yellow
        Write-GpuLog $log 'With -Apply, observation session pnp_endurance_capture_* is written.'
        return
    }
    if (-not (Confirm-GpuAdmin)) { return }
    if (-not (Confirm-Destructive 'This DISABLES the NVIDIA GPU device (like PreySense Endurance). Type YES:')) {
        Write-GpuLog $log 'Aborted by user.' Yellow
        return
    }
    if ($Observe) {
        $dir = Invoke-ObservedPnpTransition -Kind Endurance
        Write-GpuLog $log "Restore: .\probe_gpu_preysense.ps1 -Action Standard -Apply"
        Write-GpuLog $log ("Session: {0}" -f $dir)
    } else {
        Set-NvidiaDisplayDeviceState -Enable:$false | Out-Null
        Set-NvidiaContainerService -Action Stop | Out-Null
    }
}

function Invoke-StandardApply {
    Write-GpuLog $log 'Standard path: Enable NVIDIA display + Restart NVDisplay service (PreySense hybrid).' Cyan
    if (-not $Apply) {
        Write-GpuLog $log 'DRY-RUN: would Enable-PnpDevice VEN_10DE + Restart-Service NVDisplay.ContainerLocalSystem' Yellow
        return
    }
    if (-not (Confirm-GpuAdmin)) { return }
    if ($Observe) {
        $dir = Invoke-ObservedPnpTransition -Kind Standard
        Write-GpuLog $log ("Session: {0}" -f $dir)
    } else {
        Set-NvidiaDisplayDeviceState -Enable:$true | Out-Null
        Set-NvidiaContainerService -Action Restart | Out-Null
    }
}

# --- main ---
if ($StartService -and $Action -eq 'Status') {
    if (Confirm-GpuAdmin) {
        if ($Observe) {
            $sessionDir = New-AcerObserveSessionDir -Prefix 'acer_service_start_capture'
            $before = Get-AcerObserveSnapshot -Label 'BEFORE_SERVICE' -LogPath $log
            Save-AcerObserveJson $before (Join-Path $sessionDir 'before.json')
            $start = Start-AcerServiceForProbe
            Write-GpuLog $log ("StartService: Ok={0} {1}" -f $start.Ok, $start.Detail) $(if ($start.Ok) { 'Green' } else { 'Yellow' })
            Start-Sleep -Seconds 2
            $after = Get-AcerObserveSnapshot -Label 'AFTER_SERVICE' -LogPath $log
            Save-AcerObserveJson $after (Join-Path $sessionDir 'after.json')
            Write-AcerObserveDiff -Before $before -After $after -TcpTrace $null `
                -Action 'Start AcerServiceSvc only' `
                -DiffPath (Join-Path $sessionDir 'DIFF.txt') `
                -LogPath $log | Out-Null
            Write-GpuLog $log ("Service-start OBSERVE session: {0}" -f $sessionDir) White
        } else {
            $start = Start-AcerServiceForProbe
            Write-GpuLog $log ("StartService: Ok={0} {1}" -f $start.Ok, $start.Detail) $(if ($start.Ok) { 'Green' } else { 'Yellow' })
        }
    }
}

switch ($Action) {
    'Status'    { Write-PreySenseStatus }
    'Ultimate'  { Invoke-MuxSet -MuxMode $script:AcerGpuMuxDiscrete -Label 'Ultimate/Discrete' }
    'Hybrid'    { Invoke-MuxSet -MuxMode $script:AcerGpuMuxHybrid -Label 'Hybrid/Optimus' }
    'BiosMux'   {
        Write-GpuLog $log ("BiosMux mode param={0} (1=Discrete->BIOS3, 2=Hybrid->BIOS1)" -f $BiosMuxMode)
        if (-not $Apply) {
            $bios = Get-BiosDisplayModeSnapshot
            Write-GpuLog $log ("Current offset80={0}" -f $bios.Offset80)
            Write-GpuLog $log 'DRY-RUN: would SetBiosOptions Data[80]. Use -Apply (writes DIFF).' Yellow
        } elseif (Confirm-GpuAdmin) {
            $sessionDir = New-AcerObserveSessionDir -Prefix 'bios_mux_capture'
            $before = Get-AcerObserveSnapshot -Label 'BEFORE_BIOS' -LogPath $log
            Save-AcerObserveJson $before (Join-Path $sessionDir 'before.json')
            $bios = Set-GpuMuxBiosOffset80 -MuxMode $BiosMuxMode
            Save-AcerObserveJson $bios (Join-Path $sessionDir 'bios_result.json')
            Write-GpuLog $log ("BIOS: Ok={0} {1}" -f $bios.Ok, $bios.Detail) $(if ($bios.Ok) { 'Green' } else { 'Red' })
            Start-Sleep -Seconds 2
            $after = Get-AcerObserveSnapshot -Label 'AFTER_BIOS' -LogPath $log
            Save-AcerObserveJson $after (Join-Path $sessionDir 'after.json')
            Write-AcerObserveDiff -Before $before -After $after -TcpTrace $null `
                -Action ("BiosMux={0}" -f $BiosMuxMode) `
                -DiffPath (Join-Path $sessionDir 'DIFF.txt') `
                -LogPath $log | Out-Null
            Write-GpuLog $log ("Session: {0}" -f $sessionDir) White
            if ($bios.Ok -and $Reboot) {
                Write-GpuLog $log 'Reboot in 15s...' Red
                Start-Process shutdown.exe -ArgumentList '/r','/t','15' -WindowStyle Hidden
            }
        }
    }
    'Endurance' { Invoke-EnduranceApply }
    'Standard'  { Invoke-StandardApply }
}

Write-GpuLog $log ("Log: {0}" -f $log) White
Write-Host ''
Write-Host "Log saved: $log" -ForegroundColor Cyan
Write-Host 'Look for acer_service_capture_*/DIFF.txt (or pnp_*/DIFF.txt) after -Apply.' -ForegroundColor Cyan
