<#
.SYNOPSIS
  Automatic NVIDIA / Windows registry dump for Advanced Optimus / DDS clues.

.DESCRIPTION
  Read-only. Walks known NVIDIA and graphics driver keys, logs value names that
  look related to hybrid / mux / display mode, and dumps a fixed set of paths.

.USAGE
  .\probe_gpu_nv_registry.ps1
#>
[CmdletBinding()]
param(
    [int]$MaxDepth = 7
)

. "$PSScriptRoot\_gpu_common.ps1"

$log = New-GpuLog 'probe_gpu_nv_registry'
Write-GpuLog $log '=== GPU NVIDIA registry dump (read-only) ===' Green

$fp = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $fp -Label 'NOW'

function Get-RegPath([string]$PsPath) {
    return ($PsPath -replace '^Microsoft\.PowerShell\.Core\\Registry::', '')
}

function Write-RegValue {
    param([string]$Path, [string]$Name, $Value)
    $shown = if ($null -eq $Value) { '<null>' }
        elseif ($Value -is [byte[]]) { (($Value | ForEach-Object { '{0:X2}' -f $_ }) -join ' ') }
        elseif ($Value -is [Array]) { ($Value -join ',') }
        else { "$Value" }
    if ($shown.Length -gt 240) { $shown = $shown.Substring(0, 240) + '...' }
    Write-GpuLog $log ("  {0}\{1} = {2}" -f $Path, $Name, $shown)
}

$interestingName = 'Hybrid|Mux|DDS|DisplayMode|Optimus|CoProc|GpuMode|GraphicsMode|PreferredGPU|DynamicDisplay|AdvancedOptimus|PanelMux|DisplayMux|NvDisplay'

$roots = @(
    'HKLM:\SOFTWARE\NVIDIA Corporation',
    'HKCU:\Software\NVIDIA Corporation',
    'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm',
    'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers',
    'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows'
)

Write-GpuLog $log '=== interesting value-name scan ==='
$hitCount = 0
function Walk-RegInteresting([string]$Root, [int]$Depth) {
    if ($Depth -gt $MaxDepth) { return }
    if (-not (Test-Path -LiteralPath $Root)) { return }
    try {
        $item = Get-Item -LiteralPath $Root -ErrorAction Stop
        $path = Get-RegPath $item.PSPath
        foreach ($vn in $item.GetValueNames()) {
            if ($vn -match $interestingName) {
                Write-RegValue -Path $path -Name $vn -Value ($item.GetValue($vn))
                $script:hitCount++
            }
        }
        Get-ChildItem -LiteralPath $Root -ErrorAction SilentlyContinue | ForEach-Object {
            Walk-RegInteresting (Get-RegPath $_.PSPath) ($Depth + 1)
        }
    } catch {
        Write-GpuLog $log ("  walk ERR {0}: {1}" -f $Root, $_.Exception.Message) Yellow
    }
}

foreach ($r in $roots) {
    Write-GpuLog $log ("scan root: {0}" -f $r)
    Walk-RegInteresting $r 0
}
Write-GpuLog $log ("interesting hits: {0}" -f $hitCount)

$fixed = @(
    'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NVTweak',
    'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters',
    'HKCU:\Software\NVIDIA Corporation\Global\NVTweak',
    'HKCU:\Software\NVIDIA Corporation\Global\CoProcManager',
    'HKLM:\SOFTWARE\NVIDIA Corporation\Global\CoProcManager',
    'HKLM:\SOFTWARE\NVIDIA Corporation\Global\NGXCore',
    'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration'
)

Write-GpuLog $log '=== fixed path dumps ==='
foreach ($p in $fixed) {
    if (-not (Test-Path -LiteralPath $p)) {
        Write-GpuLog $log ("MISS {0}" -f $p) Yellow
        continue
    }
    Write-GpuLog $log ("PATH {0}" -f $p) Cyan
    try {
        $item = Get-Item -LiteralPath $p -ErrorAction Stop
        foreach ($vn in $item.GetValueNames()) {
            Write-RegValue -Path $p -Name $vn -Value ($item.GetValue($vn))
        }
        $children = @(Get-ChildItem -LiteralPath $p -ErrorAction SilentlyContinue)
        Write-GpuLog $log ("  subkeys={0}" -f $children.Count)
        foreach ($c in ($children | Select-Object -First 30)) {
            Write-GpuLog $log ("  sub {0}" -f (Get-RegPath $c.PSPath))
        }
    } catch {
        Write-GpuLog $log ("  ERR: {0}" -f $_.Exception.Message) Yellow
    }
}

# Display adapter PnP instance ids + ConfigFlags (disabled?)
Write-GpuLog $log '=== Win32_VideoController PnP ==='
Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | ForEach-Object {
    Write-GpuLog $log ("  {0} PNP={1} Status={2} ConfigManagerErrorCode={3}" -f `
        $_.Name, $_.PNPDeviceID, $_.Status, $_.ConfigManagerErrorCode)
}

Write-GpuLog $log ("DONE. Log: {0}" -f $log) Green
Write-Host "Log: $log" -ForegroundColor Cyan
