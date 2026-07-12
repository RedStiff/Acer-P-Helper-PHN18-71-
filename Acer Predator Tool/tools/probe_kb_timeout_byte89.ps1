#Requires -RunAsAdministrator
# PHN18 keyboard idle: byte[8]=seconds, byte[9]=1 enables timeout (fallback when APGe fails).
$ErrorActionPreference = 'Stop'

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction

function Invoke-KbBacklight([byte[]]$Payload) {
    $r = Invoke-CimMethod -InputObject $inst -MethodName SetGamingKBBacklight -Arguments @{ gmInput = $Payload }
    $hex = ($Payload | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
    Write-Host ("  [{0}] -> 0x{1:X}" -f $hex, [uint64]$r.gmOutput)
}

function Build-StaticPayload([byte]$IdleSec, [byte]$IdleEnable) {
    return [byte[]](0, 0, 100, 0, 0, 0, 150, 255, $IdleSec, $IdleEnable, 0, 0, 0, 0, 0, 0)
}

Write-Host '=== Timer OFF (always on): byte[8]=3 byte[9]=0 ==='
Invoke-KbBacklight (Build-StaticPayload 3 0)
Write-Host '>>> Wait 10s — keyboard should stay ON.'
Start-Sleep -Seconds 10

Write-Host ''
Write-Host '=== Timer 30s: byte[8]=30 byte[9]=1 ==='
Invoke-KbBacklight (Build-StaticPayload 30 1)
Write-Host '>>> Wait 35s without typing — keyboard should turn off around 30s.'
