#Requires -RunAsAdministrator
# WMI trace + state snapshots while changing logo/timeout in Predator Sense.
# Close PredatorControlApp_OLD before running. Run as Administrator.
$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\_sense_capture_common.ps1"

function Invoke-CaptureSection {
    param(
        [System.IO.TextWriter]$Writer,
        [string]$Title,
        [scriptblock]$Body
    )
    try {
        Write-CaptureSection -Writer $Writer -Title $Title -Body $Body
    }
    catch {
        $Writer.WriteLine("SECTION ERROR: $($_.Exception.Message)")
        Write-Host "WARNING: $Title failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

$captureDir = Join-Path $PSScriptRoot 'captures'
New-Item -ItemType Directory -Path $captureDir -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outFile = Join-Path $captureDir "capture_logo_$stamp.txt"
$startedAt = Get-Date

Write-Host '=== Predator Sense WMI capture: LOGO + TIMEOUT ===' -ForegroundColor Green
Write-Host "Output: $outFile"
Write-Host ''
Write-Host 'IMPORTANT: close PredatorControlApp_OLD before starting.' -ForegroundColor Yellow
Write-Host 'AcerLightingService should be Running.'
Read-Host 'Press Enter to start'

Enable-SenseVerboseLogging
Enable-WmiActivityTrace

$svc = Get-Service AcerLightingService -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Running') {
    Write-Host 'Starting AcerLightingService...'
    Start-Service AcerLightingService
    Start-Sleep -Seconds 3
}

Restart-Service AcerLightingService -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 4

$writer = New-Object System.IO.StreamWriter($outFile, $false, [Text.UTF8Encoding]::new($false))
try {
    $writer.WriteLine("Capture started: $startedAt")
    $writer.WriteLine("Machine: $env:COMPUTERNAME")
    $writer.WriteLine("DriverStore: $(Get-PredatorDriverStorePath)")

    Invoke-CaptureSection -Writer $writer -Title 'BASELINE WMI (before Predator Sense actions)' {
        $snap = Get-LogoWmiSnapshot
        Write-SnapshotLines -Writer $writer -Snapshot $snap
        $writer.WriteLine('')
        $writer.WriteLine('--- LightingProfile.ini [AcerECLogoLED] ---')
        Write-LogoProfileSection $writer
    }

    Write-Host ''
    Write-Host 'Open Predator Sense -> Lighting.' -ForegroundColor Yellow

    Wait-CaptureStep 'STEP 1: Logo STATIC, GOLD color, brightness 100%'
    Invoke-CaptureSection -Writer $writer -Title 'STEP 1: logo static gold 100%' {
        $snap = Get-LogoWmiSnapshot
        Write-SnapshotLines -Writer $writer -Snapshot $snap
    }

    Wait-CaptureStep 'STEP 2: Change logo color to RED (static, 100%)'
    Invoke-CaptureSection -Writer $writer -Title 'STEP 2: logo static red 100%' {
        $snap = Get-LogoWmiSnapshot
        Write-SnapshotLines -Writer $writer -Snapshot $snap
    }

    Wait-CaptureStep 'STEP 3: Logo brightness 50% (keep red)'
    Invoke-CaptureSection -Writer $writer -Title 'STEP 3: logo brightness 50%' {
        $snap = Get-LogoWmiSnapshot
        Write-SnapshotLines -Writer $writer -Snapshot $snap
    }

    Wait-CaptureStep 'STEP 4: Logo brightness 0% OR full OFF (same as Predator Sense off)'
    Invoke-CaptureSection -Writer $writer -Title 'STEP 4: logo off / brightness 0' {
        $snap = Get-LogoWmiSnapshot
        Write-SnapshotLines -Writer $writer -Snapshot $snap
        $writer.WriteLine('')
        $writer.WriteLine('--- LightingProfile.ini [AcerECLogoLED] after OFF ---')
        Write-LogoProfileSection $writer
    }

    Wait-CaptureStep 'STEP 5: Turn logo ON again (static, GREEN, 100%)'
    Invoke-CaptureSection -Writer $writer -Title 'STEP 5: logo static green 100%' {
        $snap = Get-LogoWmiSnapshot
        Write-SnapshotLines -Writer $writer -Snapshot $snap
    }

    Wait-CaptureStep 'STEP 6: Keyboard STATIC. Backlight timer -> OFF (always on)'
    Invoke-CaptureSection -Writer $writer -Title 'STEP 6: keyboard timeout OFF' {
        $snap = Get-LogoWmiSnapshot
        Write-SnapshotLines -Writer $writer -Snapshot $snap
    }

    Wait-CaptureStep 'STEP 7: Backlight timer -> 30 sec (do not touch keyboard 35s after Enter)'
    Invoke-CaptureSection -Writer $writer -Title 'STEP 7: keyboard timeout 30s' {
        $snap = Get-LogoWmiSnapshot
        Write-SnapshotLines -Writer $writer -Snapshot $snap
    }

    Write-Host ''
    Write-Host 'Collecting WMI trace and service logs...' -ForegroundColor Green
    Disable-WmiActivityTrace

    Invoke-CaptureSection -Writer $writer -Title 'WMI ACTIVITY TRACE (Acer / Gaming / APGe)' {
        $hits = Get-WmiTraceHits
        if ($hits.Count -eq 0) {
            $writer.WriteLine('(no matching WMI trace events - run as admin and repeat)')
        }
        else {
            foreach ($hit in $hits) {
                $writer.WriteLine("--- $($hit.Time) ---")
                if ($hit.Operation) { $writer.WriteLine("Operation: $($hit.Operation)") }
                if ($hit.Path) { $writer.WriteLine("Path: $($hit.Path)") }
                $writer.WriteLine($hit.Block)
                $writer.WriteLine('')
            }
        }
    }

    Invoke-CaptureSection -Writer $writer -Title 'SERVICE LOG LINES (filtered)' {
        $logLines = Get-NewServiceLogLines -Since $startedAt
        if ($logLines.Count -eq 0) {
            $writer.WriteLine('(no filtered log lines - OpenRGB log may not include WMI payloads at level 6)')
        }
        else {
            $logLines | ForEach-Object { $writer.WriteLine($_) }
        }
    }

    $writer.WriteLine('')
    $writer.WriteLine("Capture finished: $(Get-Date)")
}
finally {
    $writer.Close()
}

Write-Host ''
Write-Host "Done: $outFile" -ForegroundColor Green
Write-Host 'Send this file - we will implement logo OFF and timer like Predator Sense.'
