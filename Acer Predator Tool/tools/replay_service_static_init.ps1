#Requires -RunAsAdministrator
# Replays the AcerLightingService static EC registration captured via trace_service_init.ps1.
# Use when static keyboard RGB is dead after an EC profile loss (system crash).
# Does NOT require AcerLightingService to keep running — only WMI + predatorservice driver.
$ErrorActionPreference = 'Stop'

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
if (-not $inst) { throw 'AcerGamingFunction WMI instance not found' }

function Invoke-Gaming {
    param([string]$Method, [object]$GmInput)
    $r = Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
    $out = [uint64]$r.gmOutput
    $ok = ($out -band 0xFF) -eq 0
    Write-Host ("  {0} -> 0x{1:X} ok={2}" -f $Method, $out, $ok)
    return $out
}

function Pack-Zone {
    param([uint64]$Mask, [uint64]$R, [uint64]$G, [uint64]$B)
    return $Mask -bor ($R -shl 8) -bor ($G -shl 16) -bor ($B -shl 24)
}

$allZones = 8 -bor (0x0F -shl 40)
$wake = 0x07

# Default test color: cyan (change if you want)
$R = 0; $G = 150; $B = 255
$brightness = 100

Write-Host '=== PROBE (AcerLightingService startup reads) ==='
Invoke-Gaming GetGamingLED ([uint64]0) | Out-Null
Invoke-Gaming GetGamingLED ([uint64]0) | Out-Null
Invoke-Gaming GetGamingLEDBehavior ([uint64]0) | Out-Null

function Apply-StaticRound {
    param([string]$Label)
    Write-Host "=== ROUND: $Label ==="
    Invoke-Gaming SetGamingLED $allZones | Out-Null
    Invoke-Gaming SetGamingLEDColor (Pack-Zone 0x0F $R $G $B) | Out-Null
    Invoke-Gaming SetGamingLEDBehavior $wake | Out-Null
    Start-Sleep -Milliseconds 30

    # mode 0, brightness, R,G,B, [8]=3, [9]=1
    $solid = [byte[]](0, 0, $brightness, 0, 0, $R, $G, $B, 3, 1, 0, 0, 0, 0, 0, 0)
    Invoke-Gaming SetGamingKBBacklight $solid | Out-Null
    Start-Sleep -Milliseconds 30

    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x01 $R $G $B) | Out-Null
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x02 $R $G $B) | Out-Null
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x04 $R $G $B) | Out-Null
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x08 $R $G $B) | Out-Null
    Write-Host ">>> After $Label the keyboard should be STATIC. Waiting 5s..."
    Start-Sleep -Seconds 5
}

Apply-StaticRound 'registration round 1'
Apply-StaticRound 'registration round 2'

Write-Host 'Done. Static should persist now (same as after AcerLightingService start).'
