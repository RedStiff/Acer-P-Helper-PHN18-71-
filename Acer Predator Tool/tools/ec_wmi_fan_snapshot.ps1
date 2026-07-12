#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Snapshot WMI Custom fan % and sensor RPM/temps (no EC port access).

.DESCRIPTION
  Safe baseline while researching PHN18-71 fan quantization. Pair with
  ec_dump_readonly.ps1: change Custom %, run both, compare.

.EXAMPLE
  .\ec_wmi_fan_snapshot.ps1
  .\ec_wmi_fan_snapshot.ps1 -Label 'cpu50_gpu50'
#>
[CmdletBinding()]
param(
    [string]$Label = '',
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $OutDir) {
    $OutDir = Join-Path $scriptDir 'ec_dumps\wmi'
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction -ErrorAction Stop

function Invoke-GamingU64 {
    param([string]$Method, [uint64]$GmInput)
    $r = Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = [uint64]$GmInput }
    return [uint64]$r.gmOutput
}

function Get-Sensor {
    param([uint64]$SensorId)
    # Same encoding as WmiController.GetSensorReading → GetGamingSysInfo
    $raw = Invoke-GamingU64 -Method 'GetGamingSysInfo' -GmInput ([uint64](0x0001 -bor ($SensorId -shl 8)))
    if (($raw -band 0xFF) -ne 0) { return -1 }
    return [int](($raw -shr 8) -band 0xFFFF)
}

function Get-FanSpeedPercent {
    param([byte]$FanId)
    $raw = Invoke-GamingU64 -Method 'GetGamingFanSpeed' -GmInput ([uint64]$FanId)
    if (($raw -band 0xFF) -ne 0) { return -1 }
    return [int](($raw -shr 8) -band 0xFF)
}

$cpuFanId = 0x01
$gpuFanId = 0x04

$obj = [ordered]@{
    TimestampUtc     = (Get-Date).ToUniversalTime().ToString('o')
    Label            = $Label
    ComputerName     = $env:COMPUTERNAME
    CpuFanPercent    = Get-FanSpeedPercent -FanId $cpuFanId
    GpuFanPercent    = Get-FanSpeedPercent -FanId $gpuFanId
    CpuTempC         = Get-Sensor -SensorId 0x01
    CpuFanRpm        = Get-Sensor -SensorId 0x02
    GpuFanRpm        = Get-Sensor -SensorId 0x06
    GpuTempC         = Get-Sensor -SensorId 0x0A
    Note             = 'PHN18-71: Custom WMI % is accepted 0-100 but EC applies ~10% bands.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$suffix = if ($Label) { '_' + ($Label -replace '[^\w\-]+', '_') } else { '' }
$path = Join-Path $OutDir ("wmi_fan_{0}{1}.json" -f $stamp, $suffix)
$obj | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $path -Encoding UTF8

Write-Host ($obj | ConvertTo-Json -Depth 4)
Write-Host ''
Write-Host "Saved: $path" -ForegroundColor Green
