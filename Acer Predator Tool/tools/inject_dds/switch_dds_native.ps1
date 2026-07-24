#Requires -Version 5.1
param(
    [ValidateSet('igpu', 'auto', 'dgpu', 'ping', 'cycle')]
    [string]$Mode = 'ping',
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Inject = Join-Path $Here 'inject_native_dds.exe'
$AcePath = 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE'

function Get-Ace {
    $v = Get-ItemProperty $AcePath
    "state=$($v.InternalMuxState)|auto=$($v.InternalMuxIsAutomaticMode)|i2d=$($v.ACESwitchedI2D)"
}

function Invoke-Native([string]$m) {
    $args = @()
    if ($Launch) { $args += '--launch' }
    $args += $m
    $before = Get-Ace
    Write-Host "BEFORE $before  cmd=$m"
    & $Inject @args
    $code = $LASTEXITCODE
    Start-Sleep -Seconds 2
    $after = Get-Ace
    $hit = $before -ne $after
    Write-Host "AFTER  $after  HIT=$hit exit=$code"
    return $hit
}

if (-not (Test-Path $Inject)) { throw "Build inject_native_dds.exe first" }

if ($Mode -eq 'cycle') {
    $ok = $true
    foreach ($m in @('dgpu', 'igpu', 'auto', 'dgpu', 'auto')) {
        if (-not (Invoke-Native $m)) { $ok = $false }
        Start-Sleep -Seconds 1
    }
    if ($ok) { Write-Host 'CYCLE OK' -ForegroundColor Green; exit 0 }
    Write-Host 'CYCLE FAIL' -ForegroundColor Red; exit 2
}

$null = Invoke-Native $Mode
exit 0
