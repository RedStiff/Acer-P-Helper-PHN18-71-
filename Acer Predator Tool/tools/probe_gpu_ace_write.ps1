#Requires -Version 5.1
<#
.SYNOPSIS
  Write NVIDIA ACE Persistence Display Mode values and measure MuxSignature.
  No Acer services. Requires Administrator (HKLM).

.DESCRIPTION
  From captures on PHN18:
    Automatic : State=1 Auto=1 I2D=0
    Optimus   : State=1 Auto=0 I2D=0
    NvidiaOnly: State=2 Auto=0 I2D=1

  Registry write alone may be persistence-only (no live DDS). This probe measures that.

.PARAMETER Mode
  Automatic | Optimus | NvidiaOnly

.PARAMETER Force
  Skip YES confirmation.

.PARAMETER PulseNotify
  Toggle MuxChangeNotif 0->1 after write.

.EXAMPLE
  .\probe_gpu_ace_write.cmd -- -Mode NvidiaOnly -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('Automatic', 'Optimus', 'NvidiaOnly')]
    [string]$Mode = 'NvidiaOnly',
    [switch]$Force,
    [switch]$PulseNotify,
    [int]$SettleSeconds = 8
)

. "$PSScriptRoot\_gpu_common.ps1"
. "$PSScriptRoot\_acer_service.ps1"
Set-StrictMode -Off
$ErrorActionPreference = 'Continue'

$acePath = 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$sessionDir = Join-Path $PSScriptRoot ("ace_write_{0}_{1}" -f $Mode, $stamp)
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
$log = Join-Path $sessionDir 'probe.log'

function Write-AceLog([string]$Message, [ConsoleColor]$Color = [ConsoleColor]::Gray) {
    Write-GpuLog $log $Message $Color
}

function Get-AceSnapshot {
    if (-not (Test-Path -LiteralPath $acePath)) {
        return [pscustomobject]@{ Ok = $false; Detail = 'ACE path missing' }
    }
    $p = Get-ItemProperty -LiteralPath $acePath
    return [pscustomobject]@{
        Ok = $true
        InternalMuxState = [int]$p.InternalMuxState
        InternalMuxIsAutomaticMode = [int]$p.InternalMuxIsAutomaticMode
        ACESwitchedI2D = [int]$p.ACESwitchedI2D
        MuxChangeNotif = $(if ($null -eq $p.MuxChangeNotif) { $null } else { [int]$p.MuxChangeNotif })
    }
}

$modeMap = @{
    Automatic  = @{ State = 1; Auto = 1; I2D = 0 }
    Optimus    = @{ State = 1; Auto = 0; I2D = 0 }
    NvidiaOnly = @{ State = 2; Auto = 0; I2D = 1 }
}

Write-AceLog '=== ACE Persistence WRITE probe (NO Acer) ===' White
Write-AceLog ("Mode={0} PulseNotify={1} admin={2}" -f $Mode, [bool]$PulseNotify, (Test-IsAdmin))

if (-not (Test-IsAdmin)) {
    Write-AceLog 'ERROR: Administrator required to write HKLM ACE keys.' Red
    Write-Host 'Run via probe_gpu_ace_write.cmd (elevated).' -ForegroundColor Yellow
    exit 2
}

if (-not (Test-Path -LiteralPath $acePath)) {
    Write-AceLog ("ACE path missing: {0}" -f $acePath) Red
    exit 3
}

$target = $modeMap[$Mode]
$beforeFp = Get-GpuFingerprint
$beforeAce = Get-AceSnapshot
Write-GpuFingerprint -LogPath $log -Fp $beforeFp -Label 'BEFORE'
Write-AceLog ("BEFORE ACE State={0} Auto={1} I2D={2}" -f `
    $beforeAce.InternalMuxState, $beforeAce.InternalMuxIsAutomaticMode, $beforeAce.ACESwitchedI2D) Cyan

Write-Host ''
Write-Host ("Will write ACE for {0}: State={1} Auto={2} I2D={3}" -f `
    $Mode, $target.State, $target.Auto, $target.I2D) -ForegroundColor Yellow
Write-Host 'This may be persistence-only (no live DDS switch).' -ForegroundColor DarkGray
if (-not $Force) {
    if ((Read-Host 'Type YES to write') -ne 'YES') {
        Write-AceLog 'Aborted' Yellow
        exit 1
    }
}

try {
    Set-ItemProperty -LiteralPath $acePath -Name 'InternalMuxState' -Value ([int]$target.State) -Type DWord -Force
    Set-ItemProperty -LiteralPath $acePath -Name 'InternalMuxIsAutomaticMode' -Value ([int]$target.Auto) -Type DWord -Force
    Set-ItemProperty -LiteralPath $acePath -Name 'ACESwitchedI2D' -Value ([int]$target.I2D) -Type DWord -Force
    Write-AceLog 'ACE values written' Green

    if ($PulseNotify) {
        try {
            Set-ItemProperty -LiteralPath $acePath -Name 'MuxChangeNotif' -Value 0 -Type DWord -Force
            Start-Sleep -Milliseconds 200
            Set-ItemProperty -LiteralPath $acePath -Name 'MuxChangeNotif' -Value 1 -Type DWord -Force
            Write-AceLog 'Pulsed MuxChangeNotif 0->1' Cyan
        } catch {
            Write-AceLog ("MuxChangeNotif pulse failed: {0}" -f $_.Exception.Message) Yellow
        }
    }
} catch {
    Write-AceLog ("WRITE FAILED: {0}" -f $_.Exception.Message) Red
    exit 4
}

Write-AceLog ("Settling {0}s..." -f $SettleSeconds)
Start-Sleep -Seconds $SettleSeconds

$afterFp = Get-GpuFingerprint
$afterAce = Get-AceSnapshot
Write-GpuFingerprint -LogPath $log -Fp $afterFp -Label 'AFTER'
Write-AceLog ("AFTER ACE State={0} Auto={1} I2D={2}" -f `
    $afterAce.InternalMuxState, $afterAce.InternalMuxIsAutomaticMode, $afterAce.ACESwitchedI2D) Cyan

$muxChanged = $beforeFp.MuxSignature -ne $afterFp.MuxSignature
$aceApplied = (
    $afterAce.InternalMuxState -eq $target.State -and
    $afterAce.InternalMuxIsAutomaticMode -eq $target.Auto -and
    $afterAce.ACESwitchedI2D -eq $target.I2D
)

$result = [pscustomobject]@{
    Mode = $Mode
    Target = $target
    BeforeMux = $beforeFp.MuxSignature
    AfterMux = $afterFp.MuxSignature
    MuxChanged = $muxChanged
    AceWritePersisted = $aceApplied
    BeforeAce = $beforeAce
    AfterAce = $afterAce
    PulseNotify = [bool]$PulseNotify
}
$result | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $sessionDir 'result.json') -Encoding UTF8

$summary = @(
    'ACE WRITE SUMMARY',
    ("Session: {0}" -f $sessionDir),
    ("Mode: {0}" -f $Mode),
    ("BEFORE mux: {0}" -f $beforeFp.MuxSignature),
    ("AFTER  mux: {0}" -f $afterFp.MuxSignature),
    ("MUX_CHANGED={0}" -f $muxChanged),
    ("ACE_WRITE_PERSISTED={0}" -f $aceApplied),
    ("PulseNotify={0}" -f [bool]$PulseNotify),
    'If MUX_CHANGED=False but ACE persisted: keys are persistence mirror, need driver apply API.'
) -join "`r`n"
Set-Content (Join-Path $sessionDir 'SUMMARY.txt') $summary -Encoding UTF8
Write-Host $summary -ForegroundColor $(if ($muxChanged) { 'Green' } else { 'Yellow' })
Write-AceLog ("DONE. {0}" -f $sessionDir) Green
$null = Read-Host 'Press Enter to close'
