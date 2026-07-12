#Requires -RunAsAdministrator
# Verifies static RGB recovery without AcerLightingService using the full
# two-round EC registration sequence from AcerLightingService WMI trace.
$ErrorActionPreference = 'Continue'

Write-Host '[1/4] Stopping and disabling Acer lighting services...'
foreach ($service in 'AcerLightingService', 'AASSvc', 'AcerServiceSvc') {
    Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
    Set-Service -Name $service -StartupType Disabled -ErrorAction SilentlyContinue
}
Get-Service AcerLightingService, AASSvc, AcerServiceSvc -ErrorAction SilentlyContinue |
    Format-Table Name, Status, StartType -AutoSize

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
if (-not $inst) { throw 'AcerGamingFunction WMI instance not found' }

function Invoke-Gaming {
    param([string]$Method, [object]$GmInput)
    $r = Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
    $out = [uint64]$r.gmOutput
    Write-Host ("  {0} -> 0x{1:X} ok={2}" -f $Method, $out, (($out -band 0xFF) -eq 0))
}

function Pack-Zone {
    param([uint64]$Mask, [uint64]$R, [uint64]$G, [uint64]$B)
    return $Mask -bor ($R -shl 8) -bor ($G -shl 16) -bor ($B -shl 24)
}

$allZones = 8 -bor (0x0F -shl 40)

Write-Host ''
Write-Host '[2/4] Forcing DARK state (mode 0 without byte [8]=3)...'
$dark = [byte[]](0, 0, 100, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0)
Invoke-Gaming SetGamingKBBacklight $dark
Write-Host '>>> KEYBOARD SHOULD BE DARK. Waiting 6s...'
Start-Sleep -Seconds 6

Write-Host '[3/4] Full static EC recovery (2 rounds)...'
& "$PSScriptRoot\replay_service_static_init.ps1"

Write-Host '[4/4] Per-zone test (commit + R/G/B/W zones)...'
function Apply-ZoneRound {
    $commit = [byte[]](0, 0, 100, 0, 0, 0, 0, 0, 3, 1, 0, 0, 0, 0, 0, 0)
    Invoke-Gaming GetGamingLED ([uint64]0) | Out-Null
    Invoke-Gaming GetGamingLED ([uint64]0) | Out-Null
    Invoke-Gaming GetGamingLEDBehavior ([uint64]0) | Out-Null
    Invoke-Gaming SetGamingLED $allZones | Out-Null
    Invoke-Gaming SetGamingLEDColor (Pack-Zone 0x0F 255 0 0) | Out-Null
    Invoke-Gaming SetGamingLEDBehavior 0x07 | Out-Null
    Start-Sleep -Milliseconds 30
    Invoke-Gaming SetGamingKBBacklight $commit | Out-Null
    Start-Sleep -Milliseconds 30
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x01 255 0   0)
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x02 0   255 0)
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x04 0   0   255)
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x08 255 255 255)
}

Apply-ZoneRound
Start-Sleep -Seconds 2
Apply-ZoneRound
Write-Host '>>> KEYBOARD SHOULD SHOW RED/GREEN/BLUE/WHITE ZONES.'
Write-Host ''
Write-Host 'If static persists, PredatorControlApp no longer needs AcerLightingService.'
