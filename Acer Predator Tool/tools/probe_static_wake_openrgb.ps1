#Requires -RunAsAdministrator
# Lab/RE only — NOT the product path (product = verify_selfsufficiency.ps1, WMI only).
# Historical: cold static via OEM OpenRGB detect, then WMI cyan.
$ErrorActionPreference = 'Continue'

Write-Host '[1] Clear EcWakeBoot marker + stop AcerLightingService/OpenRGB'
Remove-ItemProperty -Path 'HKCU:\Software\AcerPredatorTool\Lighting' -Name 'EcWakeBootUtc' -ErrorAction SilentlyContinue
Stop-Service AcerLightingService -Force -ErrorAction SilentlyContinue
Set-Service AcerLightingService -StartupType Manual -ErrorAction SilentlyContinue
Get-Process OpenRGB -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Write-Host ("  service=" + (Get-Service AcerLightingService).Status)

$repo = Get-ChildItem 'C:\WINDOWS\System32\DriverStore\FileRepository' -Directory -Filter 'predatorservice.inf_amd64_*' |
    Where-Object {
        (Test-Path (Join-Path $_.FullName 'OpenRGB.exe')) -and
        (Test-Path (Join-Path $_.FullName 'AcerECKeyboardController.dll'))
    } |
    Select-Object -First 1
if (-not $repo) { throw 'OEM OpenRGB / AcerECKeyboardController not found in DriverStore' }
$openrgb = Join-Path $repo.FullName 'OpenRGB.exe'
Write-Host ("  OpenRGB=" + $openrgb)

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
function Invoke-Gaming([string]$Method, $GmInput) {
    $r = Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
    $raw = $r.gmOutput
    $out = [uint64]0
    if ($raw -is [byte[]]) {
        $buf = New-Object byte[] 8
        [Array]::Copy($raw, $buf, [Math]::Min(8, $raw.Length))
        $out = [BitConverter]::ToUInt64($buf, 0)
    } elseif ($null -ne $raw) { $out = [uint64]$raw }
    Write-Host ("  {0} -> 0x{1:X}" -f $Method, $out)
}

Write-Host '[2] Force DARK (no [8]=3)'
$dark = [byte[]](0, 0, 100, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0)
Invoke-Gaming SetGamingKBBacklight $dark
Start-Sleep 3

Write-Host '[3] OEM OpenRGB --list-devices (EC register, no AcerLightingService)'
$p = Start-Process -FilePath $openrgb -ArgumentList '--list-devices','--noautoconnect','--loglevel','3' `
    -WorkingDirectory $repo.FullName -PassThru -WindowStyle Hidden
if (-not $p.WaitForExit(20000)) { Stop-Process -Id $p.Id -Force; throw 'OpenRGB timeout' }
Write-Host ("  OpenRGB exit=" + $p.ExitCode)

Write-Host '[4] WMI static recovery (2 rounds)'
& "$PSScriptRoot\replay_service_static_init.ps1"

Write-Host ''
Write-Host 'DONE. If keyboard is STATIC cyan with AcerLightingService=Stopped, product path is OK.'
Write-Host ("service=" + (Get-Service AcerLightingService).Status)
Get-Process OpenRGB -ErrorAction SilentlyContinue | ForEach-Object { Write-Host ("WARNING OpenRGB still running pid=" + $_.Id) }
