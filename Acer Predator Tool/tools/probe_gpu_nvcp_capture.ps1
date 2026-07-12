<#
.SYNOPSIS
  Interactive BEFORE/AFTER capture for NVIDIA Control Panel Display Mode (DDS).

.DESCRIPTION
  1) Saves BEFORE snapshot (fingerprint + NVIDIA registry + optional Acer misc GET)
  2) Opens NVCP and waits for YOU to switch Display Mode
  3) Saves AFTER snapshot
  4) Writes a DIFF report of what changed

.USAGE
  .\probe_gpu_nvcp_capture.cmd
  .\probe_gpu_nvcp_capture.ps1
  .\probe_gpu_nvcp_capture.ps1 -TargetMode "Optimus"
  .\probe_gpu_nvcp_capture.ps1 -RediffSessionDir .\nvcp_capture_20260711_223047
#>
[CmdletBinding()]
param(
    [ValidateSet('NVIDIA GPU only', 'Optimus', 'Automatic', 'Custom')]
    [string]$TargetMode = 'NVIDIA GPU only',
    [switch]$SkipAcerMisc,
    [switch]$SkipOpenNvcp,
    [string]$RediffSessionDir
)

. "$PSScriptRoot\_gpu_common.ps1"

function Write-Cap {
    param(
        [Parameter(Mandatory)][string]$LogPath,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Message,
        [ConsoleColor]$Color = [ConsoleColor]::Gray
    )
    if ([string]::IsNullOrEmpty($Message)) {
        $Message = ' '
    }
    Write-GpuLog $LogPath $Message $Color
}

function Get-RegFlatMap {
    param([string[]]$Roots, [int]$MaxDepth = 6)

    $map = @{}
    function Walk([string]$Root, [int]$Depth) {
        if ($Depth -gt $MaxDepth) { return }
        if (-not (Test-Path -LiteralPath $Root)) { return }
        try {
            $item = Get-Item -LiteralPath $Root -ErrorAction Stop
            $path = ($item.PSPath -replace '^Microsoft\.PowerShell\.Core\\Registry::', '')
            foreach ($vn in @($item.GetValueNames())) {
                try {
                    $val = $item.GetValue($vn)
                    $shown = if ($null -eq $val) { '<null>' }
                        elseif ($val -is [byte[]]) { (($val | ForEach-Object { '{0:X2}' -f $_ }) -join ' ') }
                        elseif ($val -is [Array]) { ($val -join ',') }
                        else { "$val" }
                    if ($shown.Length -gt 400) { $shown = $shown.Substring(0, 400) + '...' }
                    $map["$path\$vn"] = $shown
                } catch { }
            }
            Get-ChildItem -LiteralPath $Root -ErrorAction SilentlyContinue | ForEach-Object {
                $child = ($_.PSPath -replace '^Microsoft\.PowerShell\.Core\\Registry::', '')
                Walk $child ($Depth + 1)
            }
        } catch { }
    }
    foreach ($r in $Roots) { Walk $r 0 }
    return $map
}

function Get-AcerMiscSnapshot {
    $out = @{}
    if (-not (Test-IsAdmin)) { return $out }
    try {
        $inst = Get-AcerGaming
        foreach ($id in @(0x01, 0x06, 0x08, 0x0C, 0x0F, 0x0D, 0x0E)) {
            $g = Get-MiscSetting -Inst $inst -Id ([uint64]$id)
            $out[('misc_0x{0:X2}' -f $id)] = ('status=0x{0:X2};value=0x{1:X2};raw=0x{2:X}' -f `
                $(if ($null -eq $g.Status) { 0xFF } else { $g.Status }),
                $(if ($null -eq $g.Value) { 0 } else { $g.Value }),
                $(if ($null -eq $g.Raw) { 0 } else { $g.Raw }))
        }
    } catch {
        $out['acer_error'] = $_.Exception.Message
    }
    return $out
}

function Convert-MapFromObject($Obj) {
    $map = @{}
    if ($null -eq $Obj) { return $map }
    if ($Obj -is [System.Collections.IDictionary]) {
        foreach ($k in @($Obj.Keys)) { $map["$k"] = "$($Obj[$k])" }
        return $map
    }
    foreach ($p in @($Obj.PSObject.Properties)) {
        if ($p.Name -in @('Count', 'Keys', 'Values', 'SyncRoot', 'IsSynchronized', 'IsFixedSize', 'IsReadOnly')) { continue }
        $map["$($p.Name)"] = "$($p.Value)"
    }
    return $map
}

function Compare-Maps {
    param($Before, $After)
    $bMap = Convert-MapFromObject $Before
    $aMap = Convert-MapFromObject $After
    $changes = New-Object System.Collections.Generic.List[string]
    $keys = @(@($bMap.Keys) + @($aMap.Keys) | Select-Object -Unique | Sort-Object)
    foreach ($k in $keys) {
        $b = if ($bMap.ContainsKey($k)) { $bMap[$k] } else { '<missing>' }
        $a = if ($aMap.ContainsKey($k)) { $aMap[$k] } else { '<missing>' }
        if ("$b" -ne "$a") {
            [void]$changes.Add(("CHANGED  {0}`n    BEFORE: {1}`n    AFTER:  {2}" -f $k, $b, $a))
        }
    }
    # Wrap in object so PowerShell never unwraps empty/single collections.
    return [pscustomobject]@{
        Count = $changes.Count
        Items = @($changes.ToArray())
    }
}

function Get-CaptureSnapshot {
    param(
        [string]$Label,
        [string]$LogPath,
        [switch]$SkipAcerMisc
    )
    Write-Cap $LogPath ("Taking {0} snapshot..." -f $Label) Cyan
    $fp = Get-GpuFingerprint
    Write-GpuFingerprint -LogPath $LogPath -Fp $fp -Label $Label

    $regRoots = @(
        'HKLM:\SOFTWARE\NVIDIA Corporation',
        'HKCU:\Software\NVIDIA Corporation',
        'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm',
        'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers',
        'HKCU:\Software\Microsoft\DirectX'
    )
    $reg = Get-RegFlatMap -Roots $regRoots -MaxDepth 7
    Write-Cap $LogPath ("  registry values captured: {0}" -f $reg.Count)

    $misc = @{}
    if (-not $SkipAcerMisc) {
        if (Test-IsAdmin) {
            $misc = Get-AcerMiscSnapshot
            Write-Cap $LogPath ("  acer misc keys: {0}" -f $misc.Count)
        } else {
            Write-Cap $LogPath '  acer misc skipped (not admin) - OK for NVCP capture' Yellow
        }
    }

    return [pscustomobject]@{
        Label          = $Label
        Timestamp      = (Get-Date).ToString('o')
        MuxSignature   = $fp.MuxSignature
        Signature      = $fp.Signature
        OwnerKind      = $fp.OwnerKind
        NvidiaDisplay  = $fp.NvidiaDisplay
        NvidiaPowerW   = $fp.NvidiaPowerW
        NvidiaClockMhz = $fp.NvidiaClockMhz
        Registry       = $reg
        AcerMisc       = $misc
        Displays       = @($fp.Displays | ForEach-Object {
            [pscustomobject]@{
                AdapterName   = $_.AdapterName
                Kind          = $_.Kind
                Active        = $_.Active
                Primary       = $_.Primary
                AdapterString = $_.AdapterString
                MonitorString = $_.MonitorString
            }
        })
    }
}

function Save-Snapshot($Snap, [string]$Path) {
    $Snap | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Build-DiffLines {
    param($Before, $After, [string]$TargetMode)
    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("NVCP capture DIFF  $($Before.Timestamp)  ->  $($After.Timestamp)")
    [void]$lines.Add("TargetMode=$TargetMode")
    [void]$lines.Add('')
    [void]$lines.Add("BEFORE mux=$($Before.MuxSignature)")
    [void]$lines.Add("AFTER  mux=$($After.MuxSignature)")
    $muxOk = "$($Before.MuxSignature)" -ne "$($After.MuxSignature)"
    [void]$lines.Add("MUX_CHANGED=$muxOk")
    [void]$lines.Add('')

    if ($muxOk) {
        [void]$lines.Add('SUCCESS: panel mux fingerprint changed - this run is useful.')
    } else {
        [void]$lines.Add('WARNING: mux fingerprint DID NOT change.')
        [void]$lines.Add('Possible causes: wrong NVCP page, switch failed, or still on same mode.')
    }

    [void]$lines.Add('')
    [void]$lines.Add('=== Registry changes ===')
    $regDiff = Compare-Maps -Before $Before.Registry -After $After.Registry
    if ($regDiff.Count -eq 0) {
        [void]$lines.Add('(none in captured registry roots)')
    } else {
        foreach ($c in @($regDiff.Items)) {
            [void]$lines.Add($c)
            [void]$lines.Add(' ')
        }
    }

    [void]$lines.Add('=== Acer misc changes ===')
    $miscDiff = Compare-Maps -Before $Before.AcerMisc -After $After.AcerMisc
    if ($miscDiff.Count -eq 0) {
        [void]$lines.Add('(none or Acer misc not captured)')
    } else {
        foreach ($c in @($miscDiff.Items)) {
            [void]$lines.Add($c)
            [void]$lines.Add(' ')
        }
    }

    return [pscustomobject]@{
        Lines = @($lines.ToArray())
        MuxOk = $muxOk
    }
}

# --- Rediff-only mode for an existing session ---
if ($RediffSessionDir) {
    $sessionDir = $RediffSessionDir
    if (-not [System.IO.Path]::IsPathRooted($sessionDir)) {
        $sessionDir = Join-Path $PSScriptRoot $sessionDir
    }
    $beforeJson = Join-Path $sessionDir 'before.json'
    $afterJson = Join-Path $sessionDir 'after.json'
    $diffPath = Join-Path $sessionDir 'DIFF.txt'
    $log = Join-Path $sessionDir 'capture_rediff.log'
    if (-not (Test-Path $beforeJson) -or -not (Test-Path $afterJson)) {
        Write-Host "Missing before.json/after.json in $sessionDir" -ForegroundColor Red
        exit 2
    }
    $before = Get-Content -LiteralPath $beforeJson -Raw -Encoding UTF8 | ConvertFrom-Json
    $after = Get-Content -LiteralPath $afterJson -Raw -Encoding UTF8 | ConvertFrom-Json
    $built = Build-DiffLines -Before $before -After $after -TargetMode $TargetMode
    $built.Lines | Set-Content -LiteralPath $diffPath -Encoding UTF8
    foreach ($l in $built.Lines) {
        $color = [ConsoleColor]::Gray
        if ($l -match 'SUCCESS|MUX_CHANGED=True') { $color = [ConsoleColor]::Green }
        elseif ($l -match 'WARNING|MUX_CHANGED=False') { $color = [ConsoleColor]::Yellow }
        Write-Cap $log $l $color
    }
    Write-Host "Rediff written: $diffPath" -ForegroundColor Cyan
    exit 0
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$sessionDir = Join-Path $PSScriptRoot ("nvcp_capture_{0}" -f $stamp)
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
$log = Join-Path $sessionDir 'capture.log'
$diffPath = Join-Path $sessionDir 'DIFF.txt'
$beforeJson = Join-Path $sessionDir 'before.json'
$afterJson = Join-Path $sessionDir 'after.json'

Write-Cap $log '=== GPU NVCP Display Mode capture ===' Green
Write-Cap $log ("session={0}" -f $sessionDir)
Write-Cap $log ("target_mode={0} admin={1}" -f $TargetMode, (Test-IsAdmin))
Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  NVCP Display Mode CAPTURE' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host "Folder: $sessionDir"
Write-Host "Target: $TargetMode"
Write-Host ''

$before = Get-CaptureSnapshot -Label 'BEFORE' -LogPath $log -SkipAcerMisc:$SkipAcerMisc
Save-Snapshot $before $beforeJson
Write-Cap $log ("BEFORE mux={0}" -f $before.MuxSignature) Green

if ($before.MuxSignature -match 'owner=NVIDIA' -and $TargetMode -eq 'NVIDIA GPU only') {
    Write-Host ''
    Write-Host 'NOTE: panel already on NVIDIA. For a clean switch-to-dGPU capture, start from Optimus.' -ForegroundColor Yellow
}

$nvcplui = "$env:ProgramFiles\NVIDIA Corporation\Control Panel Client\nvcplui.exe"
Write-Host ''
Write-Host '>>> YOUR STEPS <<<' -ForegroundColor Yellow
Write-Host '1. Close AcerPredatorTool / games if open.'
Write-Host '2. In NVIDIA Control Panel go to:'
Write-Host '     Display  ->  Manage Display Mode   (or Manage Power and Display mode)'
Write-Host "3. Select:  $TargetMode"
Write-Host '4. Confirm / Apply. Expect a short black screen (DDS switch).'
Write-Host '5. Wait until desktop returns (~5-15s).'
Write-Host '6. Come back HERE and press Enter.'
Write-Host ''

if (-not $SkipOpenNvcp -and (Test-Path $nvcplui)) {
    try {
        Start-Process $nvcplui | Out-Null
        Write-Cap $log 'Opened nvcplui.exe' Cyan
    } catch {
        Write-Cap $log ("Could not open NVCP: {0}" -f $_.Exception.Message) Yellow
    }
} else {
    Write-Host "Open manually: $nvcplui"
}

Write-Host ''
$null = Read-Host 'Press Enter AFTER the Display Mode switch has finished'

Write-Cap $log 'Waiting 5s for display settle...'
Start-Sleep -Seconds 5

$after = Get-CaptureSnapshot -Label 'AFTER' -LogPath $log -SkipAcerMisc:$SkipAcerMisc
Save-Snapshot $after $afterJson
Write-Cap $log ("AFTER mux={0}" -f $after.MuxSignature) Green

try {
    $built = Build-DiffLines -Before $before -After $after -TargetMode $TargetMode
    $built.Lines | Set-Content -LiteralPath $diffPath -Encoding UTF8
    foreach ($l in $built.Lines) {
        $color = [ConsoleColor]::Gray
        if ($l -match 'SUCCESS|MUX_CHANGED=True') { $color = [ConsoleColor]::Green }
        elseif ($l -match 'WARNING|MUX_CHANGED=False') { $color = [ConsoleColor]::Yellow }
        Write-Cap $log $l $color
    }
    $muxOk = $built.MuxOk
} catch {
    Write-Cap $log ("DIFF ERROR: {0}" -f $_.Exception.Message) Red
    Write-Cap $log $_.ScriptStackTrace Red
    $muxOk = $false
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host " Session: $sessionDir"
Write-Host " DIFF:    $diffPath"
if ($muxOk) {
    Write-Host ' Mux changed - send the session folder (or DIFF.txt) back.' -ForegroundColor Green
} else {
    Write-Host ' Mux did NOT change - retry with NVIDIA GPU only, then Optimus.' -ForegroundColor Yellow
}
Write-Host ''
Write-Host 'Restore tip: set Display Mode back to Automatic / Optimus when done.' -ForegroundColor DarkGray
Write-Host '========================================' -ForegroundColor Cyan

Write-Cap $log ("DONE. Diff: {0}" -f $diffPath) Green
Write-Host ''
pause
