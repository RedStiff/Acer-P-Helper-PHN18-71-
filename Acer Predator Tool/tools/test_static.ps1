#Requires -RunAsAdministrator
# Static-RGB diagnostic for PHN18: find the packing SetGamingRgbKb really accepts.
# Watch the KEYBOARD after each COMMIT step and note which one lights it.
$ErrorActionPreference = 'Stop'

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
if (-not $inst) { throw 'AcerGamingFunction WMI instance not found' }
Write-Host "Instance: $($inst.InstanceName)"

function Invoke-Gaming {
    param([string]$Method, [object]$GmInput)
    try {
        $r = Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
        $out = [uint64]$r.gmOutput
        $ok = ($out -band 0xFF) -eq 0
        Write-Host ("  {0}({1}) -> 0x{2:X16} ok={3}" -f $Method, $GmInput, $out, $ok)
        return $out
    } catch {
        Write-Host ("  {0}({1}) EXCEPTION: {2}" -f $Method, $GmInput, $_.Exception.Message)
        return [uint64]0xFFFFFFFFFFFFFFFF
    }
}

function Read-Zones {
    param([string]$Label)
    Write-Host "--- GetGamingRgbKb readback: $Label ---"
    foreach ($i in 0..5) {
        Invoke-Gaming GetGamingRgbKb ([uint32]$i) | Out-Null
    }
}

function Send-Commit {
    param([string]$Label)
    Write-Host "--- COMMIT ($Label): mode0, brightness=100, flag[9]=1 ---"
    $p = [byte[]](@(0) * 16)
    $p[2] = 100
    $p[9] = 1
    Invoke-Gaming SetGamingKBBacklight $p | Out-Null
    Write-Host ">>> LOOK AT KEYBOARD NOW ($Label). Waiting 5s..."
    Start-Sleep -Seconds 5
}

function Pack-Color {
    param([uint64]$Zone, [uint64]$R, [uint64]$G, [uint64]$B)
    return $Zone -bor ($R -shl 8) -bor ($G -shl 16) -bor ($B -shl 24)
}

Read-Zones "initial state"

Write-Host "`n=== TEST A: zone as MASK (0x01,0x02,0x04,0x08), layout [zone,R,G,B] ==="
Invoke-Gaming SetGamingRgbKb (Pack-Color 0x01 255 0   0)   | Out-Null
Invoke-Gaming SetGamingRgbKb (Pack-Color 0x02 0   255 0)   | Out-Null
Invoke-Gaming SetGamingRgbKb (Pack-Color 0x04 0   0   255) | Out-Null
Invoke-Gaming SetGamingRgbKb (Pack-Color 0x08 255 255 255) | Out-Null
Read-Zones "after mask-form writes"
Send-Commit "A: mask form"

Write-Host "`n=== TEST B: zone as INDEX (1,2,3,4), layout [zone,R,G,B] ==="
Invoke-Gaming SetGamingRgbKb (Pack-Color 1 255 0   0)   | Out-Null
Invoke-Gaming SetGamingRgbKb (Pack-Color 2 0   255 0)   | Out-Null
Invoke-Gaming SetGamingRgbKb (Pack-Color 3 0   0   255) | Out-Null
Invoke-Gaming SetGamingRgbKb (Pack-Color 4 255 255 255) | Out-Null
Read-Zones "after index-form writes"
Send-Commit "B: index form"

Write-Host "`n=== TEST C: all zones mask 0x0F cyan ==="
Invoke-Gaming SetGamingRgbKb (Pack-Color 0x0F 0 255 255) | Out-Null
Read-Zones "after 0x0F write"
Send-Commit "C: all-zones mask"

Write-Host "`n=== TEST D: commit WITH RGB in payload (mode0 + magenta) ==="
$p = [byte[]](@(0) * 16)
$p[2] = 100
$p[5] = 255
$p[6] = 0
$p[7] = 255
$p[9] = 1
Invoke-Gaming SetGamingKBBacklight $p | Out-Null
Write-Host ">>> LOOK AT KEYBOARD NOW (D). Waiting 5s..."
Start-Sleep -Seconds 5

Write-Host "`nDone. Report: which test (A/B/C/D) lit the keyboard, plus full output above."
