#Requires -RunAsAdministrator
# APGe keyboard idle timeout probe (Linuwu-Sense encoding for PHN18).
$ErrorActionPreference = 'Continue'

function Read-UiOutput($out) {
    $raw = $out['uiOutput']
    if ($null -eq $raw) { return @{ Type = '<null>'; U64 = 0 } }
    if ($raw -is [byte[]]) {
        $hex = ($raw | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
        $u64 = [uint64]0
        $len = [Math]::Min(8, $raw.Length)
        for ($i = 0; $i -lt $len; $i++) { $u64 = ($u64 -shl 8) -bor $raw[$i] }
        return @{ Type = "byte[$($raw.Length)]"; Hex = $hex; U64 = $u64 }
    }
    if ($raw -is [System.Array]) {
        return @{ Type = $raw.GetType().FullName; Value = ($raw | ForEach-Object { $_.ToString() }) -join ',' }
    }
    return @{ Type = $raw.GetType().FullName; Value = $raw; U64 = [uint64]$raw }
}

function Invoke-ApGe {
    param([string]$Class, [string]$Method, [object]$UiInput)

    try {
        $mc = New-Object System.Management.ManagementClass("\\.\root\WMI:$Class")
        $in = $mc.GetMethodParameters($Method)
        if ($Method -eq 'GetFunction') {
            $in['uiInput'] = [uint32]([uint64]$UiInput -band 0xFFFFFFFF)
        } else {
            $in['uiInput'] = [uint64]$UiInput
        }
        $out = $mc.InvokeMethod($Method, $in, $null)

        Write-Host ("  {0}::{1}(uiInput=0x{2:X})" -f $Class, $Method, $UiInput)
        if ($null -ne $out['ReturnValue']) {
            Write-Host ("    ReturnValue = {0}" -f $out['ReturnValue'])
        }
        $parsed = Read-UiOutput $out
        $parsed.GetEnumerator() | ForEach-Object {
            Write-Host ("    uiOutput.{0} = {1}" -f $_.Key, $_.Value)
        }
        return $true
    } catch {
        Write-Host "  ERROR: $($_.Exception.Message)"
        return $false
    }
}

foreach ($cls in @('APGeAction', 'AcerGenericMethod')) {
    Write-Host "=== $cls ==="
    Write-Host '-- GET (before) --'
    Invoke-ApGe $cls 'GetFunction' 0x88401 | Out-Null
    Write-Host '-- SET OFF (0x88402) --'
    Invoke-ApGe $cls 'SetFunction' 0x88402 | Out-Null
    Invoke-ApGe $cls 'GetFunction' 0x88401 | Out-Null
    Write-Host '-- SET 30s (0x1E0000088402) --'
    Invoke-ApGe $cls 'SetFunction' 0x1E0000088402 | Out-Null
    Invoke-ApGe $cls 'GetFunction' 0x88401 | Out-Null
    Write-Host ''
}

Write-Host 'Expected GET uiOutput.U64: 0x80000 = OFF, 0x1E0000080000 = 30s ON.'
Write-Host 'Physical test: static KB, wait 35s without keys after SET 30s.'
