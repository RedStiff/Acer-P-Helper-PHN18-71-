$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
Write-Host "Instance: $($inst.InstanceName)"

for ($i = 0; $i -le 10; $i++) {
    try {
        $r = Invoke-CimMethod -InputObject $inst -MethodName GetGamingRgbKb -Arguments @{ gmInput = [uint32]$i }
        $out = [uint64]$r.gmOutput
        Write-Host ("GetGamingRgbKb({0}) = 0x{1:X16} ({2})" -f $i, $out, $out)
    } catch {
        Write-Host ("GetGamingRgbKb({0}) error: {1}" -f $i, $_.Exception.Message)
    }
}

# Current backlight state
try {
    $r2 = Invoke-CimMethod -InputObject $inst -MethodName GetGamingKBBacklight -Arguments @{ gmInput = [byte[]](@(0)*16) }
    Write-Host ("GetGamingKBBacklight gmOutput = 0x{0:X}" -f [uint32]$r2.gmOutput)
} catch {
    Write-Host "GetGamingKBBacklight error: $($_.Exception.Message)"
}
