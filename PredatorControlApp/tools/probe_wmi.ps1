$c = Get-CimClass -Namespace root/WMI -ClassName AcerGamingFunction
foreach ($methodName in @('SetGamingRgbKb','GetGamingRgbKb','SetGamingKBBacklight','SetGamingLEDColor')) {
    Write-Host "=== $methodName ==="
    $method = $c.CimClassMethods[$methodName]
    foreach ($p in $method.Parameters) {
        Write-Host ("  " + $p.Name + " : " + $p.CimType)
    }
}
