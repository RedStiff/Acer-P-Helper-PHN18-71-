#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory = $true)]
    [string]$CaptureFile
)

if (-not (Test-Path $CaptureFile)) {
    Write-Error "File not found: $CaptureFile"
    exit 1
}

. "$PSScriptRoot\_sense_capture_common.ps1"

Write-Host "=== Parsed summary: $CaptureFile ===" -ForegroundColor Green

$content = Get-Content $CaptureFile -Raw
$steps = [regex]::Matches($content, 'STEP \d+:[^\r\n]+') | ForEach-Object { $_.Value }
if ($steps) {
    Write-Host ''
    Write-Host 'Steps in capture:'
    $steps | ForEach-Object { Write-Host "  $_" }
}

Write-Host ''
Write-Host 'GetGamingLED bytes per step:'
[regex]::Matches($content, 'STEP \d+:[^\r\n]+[\s\S]*?GetGamingLED_gmOutput = ([0-9A-F ]+|<empty>)') |
    ForEach-Object {
        $step = if ($_.Value -match '(STEP \d+:[^\r\n]+)') { $Matches[1] } else { '?' }
        $bytes = if ($_.Value -match 'GetGamingLED_gmOutput = (.+)') { $Matches[1].Trim() } else { '?' }
        Write-Host ("  {0}" -f $step)
        Write-Host ("    GetGamingLED: {0}" -f $bytes)
    }

Write-Host ''
Write-Host 'APGe timeout reads:'
Select-String -Path $CaptureFile -Pattern 'APGeAction_timeout|AcerGenericMethod_timeout' |
    ForEach-Object { Write-Host "  $($_.Line)" }

Write-Host ''
Write-Host 'WMI trace operations (unique):'
Select-String -Path $CaptureFile -Pattern '^Operation:\s*(.+)$' |
    ForEach-Object { $_.Matches.Groups[1].Value.Trim() } |
    Sort-Object -Unique |
    ForEach-Object { Write-Host "  $_" }

Write-Host ''
Write-Host 'Paths in WMI trace:'
Select-String -Path $CaptureFile -Pattern '^Path:\s*(.+)$' |
    ForEach-Object { $_.Matches.Groups[1].Value.Trim() } |
    Sort-Object -Unique |
    ForEach-Object { Write-Host "  $_" }
