#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Automatic Acer WMI misc candidate probe for GPU mux (Integrated/Auto/Discrete).

.DESCRIPTION
  For each candidate GetGamingMiscSetting ID with status=0 (and not in skip list):
    1) Snapshot GPU fingerprint
    2) SET value 0,1,2 (or -Values)
    3) Wait SettleMs (DDS switch can blank screen several seconds)
    4) Snapshot again; log SignatureChanged
    5) Restore original value

  Default is DryRun (GET + plan only). Pass -Apply to perform SET.

  WARNING: -Apply can blank the screen briefly if a real mux/DDS path is hit.
  Do not run on battery critical; close AcerPredatorTool / PredatorSense first.

.USAGE
  # Plan only
  .\probe_gpu_wmi_bruteforce.ps1

  # Real writes (careful)
  .\probe_gpu_wmi_bruteforce.ps1 -Apply

  # Narrow
  .\probe_gpu_wmi_bruteforce.ps1 -Apply -Ids 0x0E,0x0F,0x12 -Values 0,1,2
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [uint64[]]$Ids,
    [byte[]]$Values = @(0, 1, 2),
    [int]$SettleMs = 8000,
    [int]$MiscScanMax = 0x40,
    # Prefer IDs whose current GET value looks like a mode enum (0..3), not 0xFF stubs.
    [switch]$AllStatusOk,
    [switch]$IncludeFfStubs
)

. "$PSScriptRoot\_gpu_common.ps1"

if (-not (Ensure-Admin)) { exit 1 }

$log = New-GpuLog 'probe_gpu_wmi_bruteforce'
Write-GpuLog $log '=== GPU WMI misc bruteforce ===' Green
Write-GpuLog $log ("Apply={0} SettleMs={1} Values={2}" -f $Apply.IsPresent, $SettleMs, ($Values -join ','))

if (-not $Apply) {
    Write-GpuLog $log 'DryRun mode - no SetGamingMiscSetting calls. Re-run with -Apply to write.' Yellow
}

$inst = $null
try { $inst = Get-AcerGaming } catch {
    Write-GpuLog $log ("FATAL: {0}" -f $_.Exception.Message) Red
    exit 2
}

$baseline = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $baseline -Label 'START'

# Build candidate list
$candidates = New-Object System.Collections.Generic.List[uint64]
if ($Ids -and $Ids.Count -gt 0) {
    foreach ($id in $Ids) { [void]$candidates.Add([uint64]$id) }
} else {
    Write-GpuLog $log ("Scanning misc 0x00..0x{0:X} for GPU-mode-like candidates ..." -f $MiscScanMax)
    $modeLike = New-Object System.Collections.Generic.List[uint64]
    $ffStubs = New-Object System.Collections.Generic.List[uint64]
    for ($id = 0; $id -le $MiscScanMax; $id++) {
        $uid = [uint64]$id
        if ($script:GpuMiscSkipIds -contains $uid) { continue }
        $g = Get-MiscSetting -Inst $inst -Id $uid
        if (-not ($g.Ok -and $g.Status -eq 0)) { continue }
        if ($g.Value -le 3) {
            [void]$modeLike.Add($uid)
            Write-GpuLog $log ("  mode-like id=0x{0:X2} value=0x{1:X2}" -f $uid, $g.Value)
        } elseif ($g.Value -eq 0xFF) {
            [void]$ffStubs.Add($uid)
        } else {
            Write-GpuLog $log ("  other id=0x{0:X2} value=0x{1:X2} (skipped unless -AllStatusOk)" -f $uid, $g.Value)
            if ($AllStatusOk) { [void]$modeLike.Add($uid) }
        }
    }

    # Priority order: known historical GPU guesses first, then other mode-like.
    foreach ($id in @(0x0C, 0x0F, 0x01, 0x06, 0x08)) {
        $uid = [uint64]$id
        if (($modeLike -contains $uid) -and -not ($candidates -contains $uid)) {
            [void]$candidates.Add($uid)
        }
    }
    foreach ($uid in $modeLike) {
        if (-not ($candidates -contains $uid)) { [void]$candidates.Add($uid) }
    }

    # Always keep 0x0D/0x0E historical guesses (GET may return status!=0).
    foreach ($id in @(0x0D, 0x0E)) {
        $uid = [uint64]$id
        if (-not ($candidates -contains $uid)) {
            [void]$candidates.Add($uid)
            Write-GpuLog $log ("  historical-guess id=0x{0:X2}" -f $uid)
        }
    }

    if ($IncludeFfStubs -or $AllStatusOk) {
        foreach ($uid in $ffStubs) {
            if (-not ($candidates -contains $uid)) {
                [void]$candidates.Add($uid)
                Write-GpuLog $log ("  ff-stub id=0x{0:X2}" -f $uid)
            }
        }
    } else {
        Write-GpuLog $log ("Skipped {0} value=0xFF stubs (pass -IncludeFfStubs to test them)." -f $ffStubs.Count)
    }
}

Write-GpuLog $log ("Candidate count: {0}" -f $candidates.Count)

$changedHits = New-Object System.Collections.Generic.List[string]

foreach ($id in $candidates) {
    if ($script:GpuMiscSkipIds -contains $id) {
        Write-GpuLog $log ("SKIP protected id=0x{0:X2}" -f $id) Yellow
        continue
    }

    $beforeGet = Get-MiscSetting -Inst $inst -Id $id
    Write-GpuLog $log ("--- id=0x{0:X2} get status=0x{1} value=0x{2:X2} raw=0x{3:X} ---" -f `
        $id,
        $(if ($null -eq $beforeGet.Status) { '?' } else { '{0:X2}' -f $beforeGet.Status }),
        $(if ($null -eq $beforeGet.Value) { 0 } else { $beforeGet.Value }),
        $(if ($null -eq $beforeGet.Raw) { 0 } else { $beforeGet.Raw }))

    if (-not $Apply) { continue }

    $original = $beforeGet.Value
    if ($null -eq $original) { $original = [byte]0 }

    foreach ($val in $Values) {
        if ($val -eq $original) {
            Write-GpuLog $log ("  skip value=0x{0:X2} (already current)" -f $val)
            continue
        }

        $fpBefore = Get-GpuFingerprint
        Write-GpuLog $log ("  SET id=0x{0:X2} value=0x{1:X2} ..." -f $id, $val)
        $set = Set-MiscSetting -Inst $inst -Id $id -Value ([byte]$val)
        Write-GpuLog $log ("    set ok={0} status=0x{1:X2} raw=0x{2:X}" -f $set.Ok, `
            $(if ($null -eq $set.Status) { 0xFF } else { $set.Status }), `
            $(if ($null -eq $set.Raw) { 0 } else { $set.Raw }))

        Start-Sleep -Milliseconds $SettleMs

        $fpAfter = Get-GpuFingerprint
        $muxChanged = $fpBefore.MuxSignature -ne $fpAfter.MuxSignature
        $telemChanged = $fpBefore.Signature -ne $fpAfter.Signature
        $getAfter = Get-MiscSetting -Inst $inst -Id $id
        Write-GpuLog $log ("    after get value=0x{0:X2} status=0x{1:X2} muxChanged={2} telemChanged={3}" -f `
            $(if ($null -eq $getAfter.Value) { 0 } else { $getAfter.Value }),
            $(if ($null -eq $getAfter.Status) { 0xFF } else { $getAfter.Status }),
            $muxChanged,
            $telemChanged)

        if ($muxChanged) {
            Write-GpuFingerprint -LogPath $log -Fp $fpBefore -Label '    BEFORE'
            Write-GpuFingerprint -LogPath $log -Fp $fpAfter -Label '    AFTER'
            $hit = 'HIT id=0x{0:X2} value=0x{1:X2} {2} -> {3}' -f `
                $id, $val, $fpBefore.MuxSignature, $fpAfter.MuxSignature
            Write-GpuLog $log $hit Green
            [void]$changedHits.Add($hit)
        } elseif ($telemChanged) {
            Write-GpuLog $log ("    NOTE telemetry-only change (power/clock), not mux: {0} -> {1}" -f `
                $fpBefore.Signature, $fpAfter.Signature) Yellow
        }

        # Restore immediately after each attempt to limit disruption.
        $rest = Set-MiscSetting -Inst $inst -Id $id -Value ([byte]$original)
        Write-GpuLog $log ("    restore value=0x{0:X2} ok={1}" -f $original, $rest.Ok)
        Start-Sleep -Milliseconds ([Math]::Min(3000, $SettleMs))
    }
}

$end = Get-GpuFingerprint
Write-GpuFingerprint -LogPath $log -Fp $end -Label 'END'

Write-GpuLog $log '=== SUMMARY ===' Cyan
if ($changedHits.Count -eq 0) {
    Write-GpuLog $log 'No fingerprint changes detected on probed misc IDs.' Yellow
    Write-GpuLog $log 'Likely: PHN18 GPU mux is NVIDIA DDS (NVCP), not AcerGamingFunction misc.'
} else {
    foreach ($h in $changedHits) { Write-GpuLog $log $h Green }
}

Write-GpuLog $log ("DONE. Log: {0}" -f $log) Green
Write-Host ''
Write-Host "Log: $log" -ForegroundColor Cyan
if ($changedHits.Count -gt 0) {
    Write-Host 'Hits found — see log SUMMARY.' -ForegroundColor Green
}
