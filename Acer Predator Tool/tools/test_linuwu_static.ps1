#Requires -RunAsAdministrator
# Replays the exact Linuwu-Sense static sequences to verify them without the app.
# Step 1: solid static via SetGamingKBBacklight with color IN the payload.
# Step 2: per-zone static via mode-0 commit + SetGamingRgbKb per zone.
$ErrorActionPreference = 'Stop'

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
if (-not $inst) { throw 'AcerGamingFunction WMI instance not found' }

function Invoke-Gaming {
    param([string]$Method, [object]$GmInput)
    $r = Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
    $out = [uint64]$r.gmOutput
    Write-Host ("  {0} -> 0x{1:X} ok={2}" -f $Method, $out, (($out -band 0xFF) -eq 0))
}

Write-Host '=== STEP 1: solid static RED, color in payload (Linuwu four_zone_mode 0) ==='
# {mode=0, speed=0, brightness=100, 0, direction=0, R, G, B, 3, 1, 0...}
$solid = [byte[]](0, 0, 100, 0, 0, 255, 0, 0, 3, 1, 0, 0, 0, 0, 0, 0)
Invoke-Gaming SetGamingKBBacklight $solid
Write-Host '>>> KEYBOARD SHOULD BE SOLID RED NOW. Waiting 8s...'
Start-Sleep -Seconds 8

Write-Host '=== STEP 2: per-zone static (commit mode 0 + SetGamingRgbKb x4) ==='
# Commit: {0, 0, brightness, 0, 0, 0, 0, 0, 3, 1, 0...}
$commit = [byte[]](0, 0, 100, 0, 0, 0, 0, 0, 3, 1, 0, 0, 0, 0, 0, 0)
Invoke-Gaming SetGamingKBBacklight $commit
Start-Sleep -Milliseconds 50

function Pack-Zone {
    param([uint64]$Mask, [uint64]$R, [uint64]$G, [uint64]$B)
    return $Mask -bor ($R -shl 8) -bor ($G -shl 16) -bor ($B -shl 24)
}
Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x01 255 0   0)
Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x02 0   255 0)
Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x04 0   0   255)
Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x08 255 255 255)
Write-Host '>>> KEYBOARD SHOULD BE RED/GREEN/BLUE/WHITE BY ZONES NOW.'
