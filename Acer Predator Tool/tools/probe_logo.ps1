#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction

function Invoke-Gaming {
    param([string]$Method, [object]$GmInput)
    return Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
}

function Pack-LogoColor([uint64]$Mask, [uint64]$R, [uint64]$G, [uint64]$B) {
    $packed = $Mask -bor ($R -shl 8) -bor ($G -shl 16) -bor ($B -shl 24)
    if ($B -eq 0) { $packed = $packed -bor (0x80 -shl 24) }
    return $packed
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

Write-Host '=== Logo: Color + LED only (no SetGamingLEDBehavior) ==='
Invoke-Gaming SetGamingLEDColor (Pack-LogoColor 0x01 255 0 0) | Out-Null
Start-Sleep -Milliseconds 30
$r = Invoke-Gaming SetGamingLED (Build-LogoPayload 255 0 0 100)
Write-Host ("SetGamingLED gmOutput=0x{0:X}" -f [uint64]$r.gmOutput)
Write-Host '>>> Logo should be RED. Did it change physically?'
