#Requires -RunAsAdministrator
# Replays AcerLightingService static EC registration via WMI only.
# MOF: SetGamingLED / SetGamingKBBacklight = UInt8Array(MAX=16); colours = UInt64.
$ErrorActionPreference = 'Stop'

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
if (-not $inst) { throw 'AcerGamingFunction WMI instance not found' }

function Convert-GmOutput {
    param($Raw)
    if ($null -eq $Raw) { return [uint64]0 }
    if ($Raw -is [byte[]]) {
        $buf = New-Object byte[] 8
        [Array]::Copy($Raw, $buf, [Math]::Min(8, $Raw.Length))
        return [BitConverter]::ToUInt64($buf, 0)
    }
    return [uint64]$Raw
}

function Invoke-Gaming {
    param([string]$Method, [object]$GmInput)
    $r = Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
    $out = Convert-GmOutput $r.gmOutput
    $ok = ($out -band ([uint64]0xFF)) -eq 0
    Write-Host ("  {0} -> 0x{1:X} ok={2}" -f $Method, $out, $ok)
    return $out
}

function Pack-Zone {
    param([uint64]$Mask, [uint64]$R, [uint64]$G, [uint64]$B)
    return [uint64]($Mask -bor ($R -shl 8) -bor ($G -shl 16) -bor ($B -shl 24))
}

function New-AllZonesBytes {
    $u = [uint64](8 -bor ([uint64]0x0F -shl 40))
    $b = New-Object byte[] 16
    [BitConverter]::GetBytes($u).CopyTo($b, 0)
    return $b
}

$allZonesBytes = New-AllZonesBytes
$wake = [uint64]0x07
$R = [uint64]0; $G = [uint64]150; $B = [uint64]255
$brightness = 100

Write-Host '=== PROBE (startup reads) ==='
Invoke-Gaming GetGamingLED ([uint32]0) | Out-Null
Invoke-Gaming GetGamingLED ([uint32]0) | Out-Null
Invoke-Gaming GetGamingLEDBehavior ([uint32]0) | Out-Null

function Apply-StaticRound {
    param([string]$Label)
    Write-Host "=== ROUND: $Label ==="
    Invoke-Gaming SetGamingLED $allZonesBytes | Out-Null
    Invoke-Gaming SetGamingLEDColor (Pack-Zone 0x0F $R $G $B) | Out-Null
    Invoke-Gaming SetGamingLEDBehavior $wake | Out-Null
    Start-Sleep -Milliseconds 30

    $solid = [byte[]](0, 0, $brightness, 0, 0, [byte]$R, [byte]$G, [byte]$B, 3, 1, 0, 0, 0, 0, 0, 0)
    Invoke-Gaming SetGamingKBBacklight $solid | Out-Null
    Start-Sleep -Milliseconds 30

    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x01 $R $G $B) | Out-Null
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x02 $R $G $B) | Out-Null
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x04 $R $G $B) | Out-Null
    Invoke-Gaming SetGamingRgbKb (Pack-Zone 0x08 $R $G $B) | Out-Null
    Write-Host ">>> After $Label keyboard should be STATIC cyan. Waiting 4s..."
    Start-Sleep -Seconds 4
}

Apply-StaticRound 'registration round 1'
Apply-StaticRound 'registration round 2'
Write-Host 'Done. Static should persist (WMI-only).'
