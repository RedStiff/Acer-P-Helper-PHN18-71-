$ErrorActionPreference = 'Stop'

function Show-MethodParams($className, $methodName) {
    $mc = New-Object System.Management.ManagementClass("\\.\root\WMI:$className")
    $p = $mc.GetMethodParameters($methodName)
    Write-Host "=== ${className}::$methodName ==="
    foreach ($prop in $p.Properties) {
        Write-Host ("  {0} : {1}" -f $prop.Name, $prop.Type)
    }
}

foreach ($cls in @('APGeAction', 'AcerGenericMethod')) {
    foreach ($m in @('GetFunction', 'SetFunction')) {
        Show-MethodParams $cls $m
    }
}

# Try live call if admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host ''
    Write-Host 'Not admin — skipping live WMI calls'
    exit 0
}

function Read-UiOutput($out) {
    $raw = $out['uiOutput']
    if ($null -eq $raw) { return $null }
    if ($raw -is [byte[]]) {
        $hex = ($raw | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
        $u64 = 0
        $len = [Math]::Min(8, $raw.Length)
        for ($i = 0; $i -lt $len; $i++) { $u64 = ($u64 -shl 8) -bor $raw[$i] }
        return @{ Hex = $hex; U64 = [uint64]$u64; Type = 'byte[]' }
    }
    if ($raw -is [System.Array]) {
        return @{ Value = ($raw | ForEach-Object { $_.ToString() }) -join ','; Type = $raw.GetType().FullName }
    }
    return @{ Value = $raw; U64 = [uint64]$raw; Type = $raw.GetType().FullName }
}

Write-Host ''
foreach ($cls in @('APGeAction', 'AcerGenericMethod')) {
    Write-Host "=== Live GetFunction on $cls ==="
    try {
        $mc = New-Object System.Management.ManagementClass("\\.\root\WMI:$cls")
        $in = $mc.GetMethodParameters('GetFunction')
        $in['uiInput'] = [uint64]0x88401
        $out = $mc.InvokeMethod('GetFunction', $in, $null)
        Write-Host ("ReturnValue = {0}" -f $out['ReturnValue'])
        $parsed = Read-UiOutput $out
        $parsed.GetEnumerator() | ForEach-Object { Write-Host ("  uiOutput.{0} = {1}" -f $_.Key, $_.Value) }
    } catch {
        Write-Host "  ERROR: $_"
    }
}
