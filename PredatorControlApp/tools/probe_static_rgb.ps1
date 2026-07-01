# Probe static RGB sequences on PHN18 — run as admin if needed
$ErrorActionPreference = 'Continue'
$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
if (-not $inst) { Write-Host 'No AcerGamingFunction'; exit 1 }

function Invoke-Acer([string]$Method, $Input) {
    try {
        $r = Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $Input }
        $out = [uint64]$r.gmOutput
        $ok = ($out -band 0xFF) -eq 0
        Write-Host ("{0} input={1} -> out=0x{2:X16} ok={3}" -f $Method, $Input, $out, $ok)
        return $ok
    } catch {
        Write-Host ("{0} ERROR: {1}" -f $Method, $_.Exception.Message)
        return $false
    }
}

function Pack-Zone([byte]$mask, [byte]$r, [byte]$g, [byte]$b) {
    [uint64]($mask -bor ($r -shl 8) -bor ($g -shl 16) -bor ($b -shl 24))
}

function Send-KbBacklight([byte[]]$payload) {
    try {
        $r = Invoke-CimMethod -InputObject $inst -MethodName SetGamingKBBacklight -Arguments @{ gmInput = $payload }
        $out = [uint64]$r.gmOutput
        $ok = ($out -band 0xFF) -eq 0
        Write-Host ("SetGamingKBBacklight mode={0} bright={1} -> out=0x{2:X} ok={3}" -f $payload[0], $payload[2], $out, $ok)
        return $ok
    } catch {
        Write-Host "SetGamingKBBacklight ERROR: $($_.Exception.Message)"
        return $false
    }
}

$r=0; $g=150; $b=255
$masks = @(0x01, 0x02, 0x04, 0x08)

Write-Host "`n=== A: Init 0x07 + zones legacy + commit mode0 no RGB ==="
Invoke-Acer SetGamingLEDBehavior 0x07 | Out-Null
Start-Sleep -Milliseconds 50
foreach ($m in $masks) {
    $legacy = [uint64](0x06 -bor ($m -shl 8) -bor ($r -shl 16) -bor ($g -shl 24) -bor ($b -shl 32))
    Invoke-Acer SetGamingLEDColor (Pack-Zone $m $r $g $b) | Out-Null
    Invoke-Acer SetGamingLEDColor $legacy | Out-Null
    Invoke-Acer SetGamingLEDBehavior $legacy | Out-Null
    Start-Sleep -Milliseconds 20
}
$p = [byte[]](@(0)*16); $p[2]=100; $p[9]=1
Send-KbBacklight $p | Out-Null
Write-Host "Check keyboard now (A). Press Enter for B..."
Read-Host | Out-Null

Write-Host "`n=== B: SetGamingRgbKb zones + commit mode0 no RGB ==="
Invoke-Acer SetGamingLEDBehavior 0x07 | Out-Null
Start-Sleep -Milliseconds 50
foreach ($m in $masks) {
    Invoke-Acer SetGamingRgbKb (Pack-Zone $m $r $g $b) | Out-Null
    Start-Sleep -Milliseconds 20
}
Send-KbBacklight $p | Out-Null
Write-Host "Check keyboard now (B). Press Enter for C..."
Read-Host | Out-Null

Write-Host "`n=== C: RgbKb + legacy + commit mode0 no RGB ==="
Invoke-Acer SetGamingLEDBehavior 0x07 | Out-Null
Start-Sleep -Milliseconds 50
foreach ($m in $masks) {
    Invoke-Acer SetGamingRgbKb (Pack-Zone $m $r $g $b) | Out-Null
    $legacy = [uint64](0x06 -bor ($m -shl 8) -bor ($r -shl 16) -bor ($g -shl 24) -bor ($b -shl 32))
    Invoke-Acer SetGamingLEDColor (Pack-Zone $m $r $g $b) | Out-Null
    Invoke-Acer SetGamingLEDBehavior $legacy | Out-Null
    Start-Sleep -Milliseconds 20
}
Send-KbBacklight $p | Out-Null
Write-Host "Check keyboard now (C). Press Enter for D..."
Read-Host | Out-Null

Write-Host "`n=== D: RgbKb + dual commit (no RGB then with RGB) ==="
Invoke-Acer SetGamingLEDBehavior 0x07 | Out-Null
Start-Sleep -Milliseconds 50
foreach ($m in $masks) {
    Invoke-Acer SetGamingRgbKb (Pack-Zone $m $r $g $b) | Out-Null
    Start-Sleep -Milliseconds 20
}
Send-KbBacklight $p | Out-Null
Start-Sleep -Milliseconds 30
$p[0]=0; $p[1]=0; $p[5]=$r; $p[6]=$g; $p[7]=$b
Send-KbBacklight $p | Out-Null
Write-Host "Done D."
