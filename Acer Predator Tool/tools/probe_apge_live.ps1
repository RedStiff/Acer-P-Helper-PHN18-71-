#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'

foreach ($className in @('APGeAction', 'AcerGenericMethod')) {
    Write-Host "=== CIM schema: $className ==="
    try {
        $cls = Get-CimClass -Namespace root/wmi -ClassName $className
        foreach ($m in $cls.CimClassMethods) {
            Write-Host $m.Name
            foreach ($p in $m.CimMethodParameters) {
                Write-Host ("  {0} {1}" -f $p.Name, $p.CimType)
            }
        }
    } catch {
        Write-Host "  schema error: $($_.Exception.Message)"
    }

    Write-Host "--- live calls ---"
    foreach ($test in @(
        @{ Method = 'GetFunction'; Input = [uint32]0x88401; Label = 'GET uint32' },
        @{ Method = 'GetFunction'; Input = [uint64]0x88401; Label = 'GET uint64' },
        @{ Method = 'SetFunction'; Input = [uint64]0x88402; Label = 'SET OFF' },
        @{ Method = 'SetFunction'; Input = [uint64]0x1E0000088402; Label = 'SET 30s' }
    )) {
        try {
            $mc = New-Object System.Management.ManagementClass("\\.\root\WMI:$className")
            $in = $mc.GetMethodParameters($test.Method)
            $in['uiInput'] = $test.Input
            $out = $mc.InvokeMethod($test.Method, $in, $null)
            $ui = $out['uiOutput']
            $uiText = if ($ui -is [byte[]]) { ($ui | ForEach-Object { '{0:X2}' -f $_ }) -join ' ' } else { $ui }
            Write-Host ("  OK {0} {1}: ReturnValue={2} uiOutput={3}" -f $className, $test.Label, $out['ReturnValue'], $uiText)
        } catch {
            Write-Host ("  FAIL {0} {1}: {2}" -f $className, $test.Label, $_.Exception.Message)
        }
    }
    Write-Host ''
}

Write-Host '=== WMI classes matching APGe / Generic ==='
Get-CimClass -Namespace root/wmi | Where-Object { $_.CimClassName -match 'APGe|GenericMethod|GenericEvent' } |
    Select-Object -ExpandProperty CimClassName
