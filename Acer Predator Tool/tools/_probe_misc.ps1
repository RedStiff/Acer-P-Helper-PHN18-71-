#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'
$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction

Write-Host '=== GetGamingKBBacklight ==='
$kb = Invoke-CimMethod -InputObject $inst -MethodName GetGamingKBBacklight -Arguments @{ gmInput = [uint32]0 }
$bytes = $kb.gmOutput
if ($bytes -is [byte[]]) {
    Write-Host (($bytes | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
} else {
    Write-Host "raw=$($kb.gmOutput)"
}

Write-Host ''
Write-Host '=== GetGamingMiscSetting scan ==='
foreach ($id in 0..0x30) {
    try {
        $r = Invoke-CimMethod -InputObject $inst -MethodName GetGamingMiscSetting -Arguments @{ gmInput = [uint64]$id }
        $out = [uint64]$r.gmOutput
        if ($out -ne 0) {
            Write-Host ("id=0x{0:X2} gmOutput=0x{1:X}" -f $id, $out)
        }
    } catch {
        Write-Host ("id=0x{0:X2} error: {1}" -f $id, $_.Exception.Message)
    }
}

Write-Host ''
Write-Host '=== MiscSetting timeout candidates ==='
foreach ($payload in @(
    [uint64]0x88401,
    [uint64]0x88402,
    [uint64]0x1E0000088402
)) {
    try {
        $r = Invoke-CimMethod -InputObject $inst -MethodName GetGamingMiscSetting -Arguments @{ gmInput = $payload }
        Write-Host ("GET misc 0x{0:X} -> 0x{1:X}" -f $payload, [uint64]$r.gmOutput)
    } catch {
        Write-Host ("GET misc 0x{0:X} fail: {1}" -f $payload, $_.Exception.Message)
    }
}
