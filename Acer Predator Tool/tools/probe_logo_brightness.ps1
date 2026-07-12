#Requires -RunAsAdministrator
# PHN18 logo: brightness in SetGamingLEDColor bits 32-39 (Predator Sense capture).
$ErrorActionPreference = 'Stop'

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction

function Invoke-Gaming {
    param([string]$Method, [object]$GmInput)
    return Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
}

function Pack-LogoColor([byte]$R, [byte]$G, [byte]$B, [byte]$Brightness) {
    $rgb = [uint64]$R -shl 8 -bor ([uint64]$G -shl 16) -bor ([uint64]$B -shl 24)
    $tag = [uint64]0x03 -shl 40
    return $tag -bor ([uint64]$Brightness -shl 32) -bor $rgb
}

function Build-LogoPayload([byte]$R, [byte]$G, [byte]$B, [byte]$Brightness) {
    $p = [byte[]](@(0) * 16)
    $p[0] = 0
    $p[1] = $Brightness
    $p[4] = $R
    $p[5] = $G
    $p[6] = $B
    $p[8] = 1
    return $p
}

function Apply-Logo([byte]$R, [byte]$G, [byte]$B, [byte]$Brightness) {
    Invoke-Gaming SetGamingLED (Build-LogoPayload $R $G $B $Brightness) | Out-Null
    Start-Sleep -Milliseconds 30
    $packed = Pack-LogoColor $R $G $B $Brightness
    Invoke-Gaming SetGamingLEDColor $packed | Out-Null
    $get = Invoke-Gaming GetGamingLEDColor ([uint64]0x01)
    Write-Host ("  SetGamingLEDColor=0x{0:X}  Get=0x{1:X}" -f $packed, [uint64]$get.gmOutput)
}

Write-Host '=== Logo brightness probe (gold C7 AE 6E) ==='
Write-Host 'Step 1: brightness 100%'
Apply-Logo 199 174 110 100
Start-Sleep -Seconds 3
Write-Host 'Step 2: brightness 50%'
Apply-Logo 199 174 110 50
Start-Sleep -Seconds 3
Write-Host 'Step 3: brightness 0% (should be OFF)'
Apply-Logo 199 174 110 0
Write-Host '>>> Did brightness change and did step 3 turn logo off?'
