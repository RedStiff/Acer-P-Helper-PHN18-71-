#Requires -Version 5.1
<#
.SYNOPSIS
  Switch NVIDIA Advanced Optimus / DDS Display Mode without Acer services.

.DESCRIPTION
  Restarts NVDisplay container if UXD is down, launches NVIDIA App on the
  interactive desktop (SW_HIDE + cef port hook) for CDP, then SetDDSState via
  cefQuery. Reuses CDP if already up. Do NOT use a private desktop — that steals
  the single-instance NVIDIA App from the user.

.PARAMETER Mode
  NvidiaOnly | Optimus | Automatic | Get

.EXAMPLE
  .\switch_dds.ps1 -Mode NvidiaOnly
  .\switch_dds.ps1 -Mode Optimus
  .\switch_dds.ps1 -Mode Automatic
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('NvidiaOnly', 'Optimus', 'Automatic', 'Get')]
    [string]$Mode
)

$ErrorActionPreference = 'Stop'
$ToolsRoot = $PSScriptRoot
$InjectDir = Join-Path $ToolsRoot 'inject_dds'
$Launcher = Join-Path $InjectDir 'launcher.exe'
$HitPy = Join-Path $InjectDir '_hit_setdds.py'
$Common = Join-Path $ToolsRoot '_gpu_common.ps1'
$UxdStartedEvent = 'Local\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57'
$CdpPort = 9333

if (-not (Test-Path $Launcher)) { throw "Missing launcher: $Launcher" }
if (-not (Test-Path $HitPy)) { throw "Missing hit script: $HitPy" }
. $Common

function Test-UxdServiceStarted {
    Add-Type -Namespace DdsSwitch -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true)]
public static extern System.IntPtr OpenEvent(uint desiredAccess, bool inherit, string name);
[System.Runtime.InteropServices.DllImport("kernel32", SetLastError=true)]
public static extern bool CloseHandle(System.IntPtr handle);
'@ -ErrorAction SilentlyContinue
    $handle = [DdsSwitch.Native]::OpenEvent(0x00100000, $false, $UxdStartedEvent)
    if ($handle -eq [IntPtr]::Zero) { return $false }
    [void][DdsSwitch.Native]::CloseHandle($handle)
    return $true
}

function Ensure-UxdHealthy {
    if (Test-UxdServiceStarted) {
        Write-Host 'UXD: Local\UXDServiceStarted present'
        return
    }
    Write-Host 'UXD: started event missing — restarting NVIDIA display/container services (admin)'
    $script = @'
Restart-Service NVDisplay.ContainerLocalSystem -Force
Restart-Service NvContainerLocalSystem -Force
Start-Sleep -Seconds 6
'@
    $temp = Join-Path $env:TEMP ('dds_uxd_restart_{0}.ps1' -f [guid]::NewGuid().ToString('N'))
    Set-Content -Path $temp -Value $script -Encoding UTF8
    try {
        $proc = Start-Process powershell -Verb RunAs -PassThru -Wait -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $temp
        )
        if ($proc.ExitCode -ne 0 -and $null -ne $proc.ExitCode) {
            Write-Warning "Elevated restart exit=$($proc.ExitCode)"
        }
    } finally {
        Remove-Item $temp -Force -ErrorAction SilentlyContinue
    }
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if (Test-UxdServiceStarted) {
            Write-Host 'UXD: recovered'
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw 'UXDServiceStarted still missing after service restart'
}

function Test-CdpUp {
    try {
        $null = Invoke-WebRequest -Uri ("http://127.0.0.1:{0}/json" -f $CdpPort) -UseBasicParsing -TimeoutSec 2
        return $true
    } catch {
        return $false
    }
}

function Ensure-NvidiaAppCdp {
    if (Test-CdpUp) {
        Write-Host "CDP: already on :$CdpPort"
        return
    }
    Write-Host 'CDP: launching NVIDIA App host (interactive desktop, SW_HIDE + cef port hook)'
    Get-Process -Name 'NVIDIA App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    $launch = Start-Process -FilePath $Launcher -WorkingDirectory $InjectDir -PassThru -Wait -NoNewWindow
    if ($launch.ExitCode -ne 0) { throw "launcher.exe failed exit=$($launch.ExitCode)" }
    $deadline = (Get-Date).AddSeconds(50)
    while ((Get-Date) -lt $deadline) {
        if (Test-CdpUp) {
            Write-Host "CDP: ready on :$CdpPort"
            Start-Sleep -Seconds 3
            return
        }
        Start-Sleep -Milliseconds 300
    }
    throw "CDP did not come up on :$CdpPort"
}

$pyMode = switch ($Mode) {
    'NvidiaOnly' { 'dgpu' }
    'Optimus' { 'igpu' }
    'Automatic' { 'auto' }
    'Get' { 'get' }
}

$before = Get-GpuFingerprint
Write-Host ("BEFORE mux={0}" -f $before.MuxSignature)

Ensure-UxdHealthy
Ensure-NvidiaAppCdp

$python = (Get-Command python -ErrorAction Stop).Source
& $python -u $HitPy $pyMode
$hitExit = $LASTEXITCODE

$after = Get-GpuFingerprint
Write-Host ("AFTER mux={0}" -f $after.MuxSignature)

if ($Mode -eq 'Get') { exit 0 }

$acePath = 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE'
$ace = Get-ItemProperty $acePath
Write-Host ("ACE state={0} auto={1} i2d={2}" -f $ace.InternalMuxState, $ace.InternalMuxIsAutomaticMode, $ace.ACESwitchedI2D)

# Optimus↔Automatic can keep the same MuxSignature; ACE auto flag is the discriminator.
if ($hitExit -eq 0) {
    Write-Host ("DDS HIT: {0} -> {1}" -f $before.MuxSignature, $after.MuxSignature) -ForegroundColor Green
    exit 0
}
Write-Host 'DDS NO_HIT' -ForegroundColor Red
exit 2
