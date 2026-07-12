#Requires -RunAsAdministrator
# Tests whether EC uses SetGamingKBBacklight byte[8] as idle seconds (1-255).
$ErrorActionPreference = 'Stop'

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction

function Invoke-KbBacklight([byte[]]$Payload) {
    $r = Invoke-CimMethod -InputObject $inst -MethodName SetGamingKBBacklight -Arguments @{ gmInput = $Payload }
    $out = [uint64]$r.gmOutput
    $hex = ($Payload | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
    Write-Host ("  payload[{0}] -> 0x{1:X}" -f $hex, $out)
}

Write-Host '=== Static commit with byte[8]=30 (expect ~30s idle if EC reads byte[8]) ==='
$p30 = [byte[]](0, 0, 100, 0, 0, 0, 150, 255, 30, 1, 0, 0, 0, 0, 0, 0)
Invoke-KbBacklight $p30
Write-Host '>>> Wait 35s without typing — keyboard should turn off around 30s.'
Write-Host ''

Write-Host '=== Static commit with byte[8]=3 (static magic / ~3s without APGe) ==='
$p3 = [byte[]](0, 0, 100, 0, 0, 0, 150, 255, 3, 1, 0, 0, 0, 0, 0, 0)
Invoke-KbBacklight $p3
Write-Host '>>> Wait 6s — should turn off around 3s if APGe not set.'
