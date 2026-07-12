# Lists Acer/Gaming WMI classes and whether SetFunction/GetFunction exist.
$ErrorActionPreference = 'Continue'

Write-Host '=== Acer / Gaming / APGe WMI classes ==='
Get-CimClass -Namespace root/WMI |
    Where-Object { $_.CimClassName -match 'Acer|Gaming|APGe' } |
    ForEach-Object {
        $methods = $_.CimClassMethods.Name
        $hasSetFn = $methods -contains 'SetFunction'
        $hasGetFn = $methods -contains 'GetFunction'
        $flags = @()
        if ($hasSetFn) { $flags += 'SetFunction' }
        if ($hasGetFn) { $flags += 'GetFunction' }
        $extra = if ($flags.Count) { " [$($flags -join ', ')]" } else { '' }
        Write-Host ("  {0}{1}" -f $_.CimClassName, $extra)
    }

Write-Host ''
Write-Host '=== AcerGamingFunction methods ==='
(Get-CimClass -Namespace root/WMI -ClassName AcerGamingFunction).CimClassMethods.Name |
    Sort-Object |
    ForEach-Object { Write-Host "  $_" }
