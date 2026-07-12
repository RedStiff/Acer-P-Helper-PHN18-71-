$ErrorActionPreference = 'Stop'
$mc = New-Object System.Management.ManagementClass('\\.\root\WMI:AcerGamingFunction')
foreach ($m in @('GetGamingLED','SetGamingLED','SetGamingKBBacklight','GetGamingKBBacklight')) {
    Write-Host "=== $m ==="
    $p = $mc.GetMethodParameters($m)
    foreach ($prop in $p.Properties) {
        $arr = ''
        try { if ($prop.IsArray) { $arr = '[]' } } catch {}
        Write-Host ("  {0}{1} : {2}" -f $prop.Name, $arr, $prop.Type)
    }
}

$mc2 = New-Object System.Management.ManagementClass('\\.\root\WMI:APGeAction')
foreach ($m in @('GetFunction','SetFunction')) {
    Write-Host "=== APGeAction::$m ==="
    $p = $mc2.GetMethodParameters($m)
    foreach ($prop in $p.Properties) {
        $arr = ''
        try { if ($prop.IsArray) { $arr = '[]' } } catch {}
        Write-Host ("  in {0}{1} : {2}" -f $prop.Name, $arr, $prop.Type)
    }
}
