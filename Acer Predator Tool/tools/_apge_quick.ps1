$ErrorActionPreference = 'Continue'

Write-Host '=== Instances ==='
foreach ($cls in @('APGeAction', 'AcerGenericMethod')) {
    $items = Get-CimInstance -Namespace root/wmi -ClassName $cls -ErrorAction SilentlyContinue
    if (-not $items) { Write-Host "$cls : no instances"; continue }
    $items | ForEach-Object { Write-Host "$cls PATH: $($_.CimInstanceProperties['__PATH'].Value)" }
}

function Test-ApGe {
    param([string]$Class, [string]$Method, [object]$UiInput, [string]$Label)
    Write-Host "-- $Label --"
    try {
        $mc = New-Object System.Management.ManagementClass("\\.\root\WMI:$Class")
        $in = $mc.GetMethodParameters($Method)
        $in['uiInput'] = $UiInput
        $out = $mc.InvokeMethod($Method, $in, $null)
        $rv = $out['ReturnValue']
        $uo = $out['uiOutput']
        Write-Host "  OK ReturnValue=$rv uiOutput=$uo type=$($uo.GetType().FullName)"
    } catch {
        Write-Host "  FAIL static: $($_.Exception.Message)"
    }

    $searcher = New-Object System.Management.ManagementObjectSearcher("root\WMI", "SELECT * FROM $Class")
    foreach ($inst in $searcher.Get()) {
        try {
            $in = $inst.GetMethodParameters($Method)
            $in['uiInput'] = $UiInput
            $out = $inst.InvokeMethod($Method, $in, $null)
            $rv = $out['ReturnValue']
            $uo = $out['uiOutput']
            Write-Host "  OK instance $($inst.Path) ReturnValue=$rv uiOutput=$uo"
        } catch {
            Write-Host "  FAIL instance $($inst.Path): $($_.Exception.Message)"
        }
    }
}

foreach ($cls in @('APGeAction', 'AcerGenericMethod')) {
    Write-Host "`n=== $cls ==="
    Test-ApGe $cls 'GetFunction' ([uint32]0x88401) 'GetFunction uint32'
    Test-ApGe $cls 'GetFunction' ([uint64]0x88401) 'GetFunction uint64'
    Test-ApGe $cls 'SetFunction' ([uint64]0x88402) 'SetFunction OFF uint64'
}
