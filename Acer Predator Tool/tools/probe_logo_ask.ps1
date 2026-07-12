#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Interactive PHN18 lid-logo probe. After EACH hardware attempt asks:
    is color <expected>? Y/N
  Answers + WMI results are written to probe_logo_ask_<timestamp>.log

.USAGE
  Right-click PowerShell -> Run as administrator, then:
    cd $PSScriptRoot
    .\probe_logo_ask.ps1

  Close AcerPredatorTool.exe first so it does not fight for WMI.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$logPath = Join-Path $PSScriptRoot "probe_logo_ask_$stamp.log"

function Write-Log([string]$Message) {
    $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $line
}

function Ask-Physical([string]$Expected) {
    Write-Host ''
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ("ASK: is color {0}?  (Y/N)  [S=skip rest]" -f $Expected) -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
    while ($true) {
        $ans = (Read-Host 'Your answer').Trim().ToUpperInvariant()
        if ($ans -in @('Y', 'N', 'S')) { break }
        Write-Host 'Enter Y, N, or S'
    }
    Write-Log ("ANSWER expected='{0}' -> {1}" -f $Expected, $ans)
    return $ans
}

function Get-Gaming {
    try {
        return Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
    } catch {
        Write-Log ("FATAL: AcerGamingFunction: {0}" -f $_.Exception.Message)
        throw
    }
}

function Invoke-Gaming([string]$Method, $GmInput) {
    try {
        $r = Invoke-CimMethod -InputObject $script:Inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
        return $r
    } catch {
        Write-Log ("{0} ERR: {1}" -f $Method, $_.Exception.Message)
        return $null
    }
}

function Format-Bytes([byte[]]$Bytes) {
    if (-not $Bytes) { return '(null)' }
    return (($Bytes | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
}

function Out-Code($Result) {
    if (-not $Result) { return 'null' }
    try { return ('0x{0:X}' -f [uint32]$Result.gmOutput) } catch { return '?' }
}

function Pack-Nekro([byte]$R, [byte]$G, [byte]$B, [byte]$Brightness, [byte]$Enable) {
    [uint64]$v = 1
    $v = $v -bor ([uint64]$R -shl 8)
    $v = $v -bor ([uint64]$G -shl 16)
    $v = $v -bor ([uint64]$B -shl 24)
    $v = $v -bor ([uint64]$Brightness -shl 32)
    $v = $v -bor ([uint64]$Enable -shl 40)
    return $v
}

function New-VenatorPayload(
    [byte]$Mode,
    [byte]$Speed,
    [byte]$Brightness,
    [byte]$Direction,
    [byte]$R, [byte]$G, [byte]$B,
    [byte]$Select = 2
) {
    return [byte[]]@(
        $Mode, $Speed, $Brightness, 0, $Direction,
        $R, $G, $B, 3, $Select,
        0, 0, 0, 0, 0, 0
    )
}

function Show-Baseline {
    Write-Log '--- baseline ---'
    $led = Invoke-Gaming GetGamingLED ([uint32]0)
    if ($led) {
        Write-Log ("GetGamingLED ret=0x{0:X} out={1}" -f [uint32]$led.gmReturn, (Format-Bytes ([byte[]]$led.gmOutput)))
    }
    $kb = Invoke-Gaming GetGamingKBBacklight ([uint32]0)
    if ($kb) {
        Write-Log ("GetGamingKBBacklight ret=0x{0:X} out={1}" -f [uint32]$kb.gmReturn, (Format-Bytes ([byte[]]$kb.gmOutput)))
    }
    foreach ($mask in 0, 1, 2) {
        $c = Invoke-Gaming GetGamingLEDColor ([uint64]$mask)
        if ($c) { Write-Log ("GetGamingLEDColor mask={0} =0x{1:X}" -f $mask, [uint64]$c.gmOutput) }
    }
    $svc = Get-Service AcerLightingService -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Log ("AcerLightingService Status={0} StartType={1}" -f $svc.Status, $svc.StartType)
    } else {
        Write-Log 'AcerLightingService: not installed'
    }
    $hid = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match 'VID_0D62|BA51' }
    if ($hid) {
        $hid | ForEach-Object { Write-Log ("HID: {0} | {1}" -f $_.FriendlyName, $_.InstanceId) }
    } else {
        Write-Log 'HID Darfon 0D62/BA51: not present'
    }
}

# ---- steps ----
$script:Inst = $null
Write-Log "=== probe_logo_ask start host=$env:COMPUTERNAME ==="
Write-Log "Log file: $logPath"
Write-Host ''
Write-Host 'Close AcerPredatorTool before continuing.' -ForegroundColor Magenta
Write-Host 'Look at the LID logo (not keyboard). Answer Y/N after each ASK.' -ForegroundColor Magenta
Write-Host ''
$null = Read-Host 'Press Enter when ready'

$script:Inst = Get-Gaming
Show-Baseline

Write-Host ''
Write-Host 'Current lid state before tests:' -ForegroundColor Cyan
$baseAns = Ask-Physical 'GREEN static (current baseline)'
if ($baseAns -eq 'S') {
    Write-Log 'Aborted at baseline'
    Write-Host "Log: $logPath"
    exit 0
}

$steps = @(
    @{
        Id = 'T01'
        Ask = 'RED'
        Desc = 'Venator KBBL static RED sel=2 bright=100'
        Action = {
            $p = New-VenatorPayload 0xFF 0 100 1 255 0 0 2
            Write-Log ("payload={0}" -f (Format-Bytes $p))
            $r = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("SetGamingKBBacklight -> {0}" -f (Out-Code $r))
        }
    }
    @{
        Id = 'T02'
        Ask = 'BLUE'
        Desc = 'Venator KBBL static BLUE sel=2 bright=100'
        Action = {
            $p = New-VenatorPayload 0xFF 0 100 1 0 0 255 2
            Write-Log ("payload={0}" -f (Format-Bytes $p))
            $r = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("SetGamingKBBacklight -> {0}" -f (Out-Code $r))
        }
    }
    @{
        Id = 'T03'
        Ask = 'OFF (dark / no light)'
        Desc = 'Venator KBBL mode OFF sel=2'
        Action = {
            $p = New-VenatorPayload 0x00 0 0 1 0 0 0 2
            Write-Log ("payload={0}" -f (Format-Bytes $p))
            $r = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("SetGamingKBBacklight -> {0}" -f (Out-Code $r))
        }
    }
    @{
        Id = 'T04'
        Ask = 'RED'
        Desc = 'Venator static RED bright=255 (0-255 scale)'
        Action = {
            $p = New-VenatorPayload 0xFF 5 255 1 255 0 0 2
            Write-Log ("payload={0}" -f (Format-Bytes $p))
            $r = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("SetGamingKBBacklight -> {0}" -f (Out-Code $r))
        }
    }
    @{
        Id = 'T05'
        Ask = 'MAGENTA'
        Desc = 'Nekro SetGamingLEDColor pack + enable-gate KBBL sel=2'
        Action = {
            $pack = Pack-Nekro 255 0 255 100 1
            Write-Log ("nekro pack=0x{0:X}" -f $pack)
            $r = Invoke-Gaming SetGamingLEDColor $pack
            Write-Log ("SetGamingLEDColor -> {0}" -f (Out-Code $r))
            $gate = [byte[]]@(1, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0)
            $r2 = Invoke-Gaming SetGamingKBBacklight $gate
            Write-Log ("enable-gate -> {0} payload={1}" -f (Out-Code $r2), (Format-Bytes $gate))
        }
    }
    @{
        Id = 'T06'
        Ask = 'CYAN'
        Desc = 'SetGamingLED 16-byte venator buffer then KBBL same'
        Action = {
            $p = New-VenatorPayload 0xFF 0 100 1 0 255 255 2
            Write-Log ("payload={0}" -f (Format-Bytes $p))
            $r1 = Invoke-Gaming SetGamingLED $p
            Write-Log ("SetGamingLED -> {0}" -f (Out-Code $r1))
            $r2 = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("SetGamingKBBacklight -> {0}" -f (Out-Code $r2))
        }
    }
    @{
        Id = 'T07'
        Ask = 'YELLOW'
        Desc = 'Keyboard wake (LEDBehavior/LED zones) then logo KBBL YELLOW'
        Action = {
            $zones = [uint64](8 -bor (0x0FUL -shl 40))
            $r0 = Invoke-Gaming SetGamingLEDBehavior ([uint64]7)
            Write-Log ("SetGamingLEDBehavior(7) -> {0}" -f (Out-Code $r0))
            $r1 = Invoke-Gaming SetGamingLED $zones
            Write-Log ("SetGamingLED(zones) -> {0}" -f (Out-Code $r1))
            $p = New-VenatorPayload 0xFF 0 100 1 255 255 0 2
            $r2 = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("KBBL yellow -> {0} payload={1}" -f (Out-Code $r2), (Format-Bytes $p))
        }
    }
    @{
        Id = 'T08'
        Ask = 'RED breathing (pulsing)'
        Desc = 'Venator BREATHING mode=1 RED'
        Action = {
            $p = New-VenatorPayload 0x01 5 100 1 255 0 0 2
            Write-Log ("payload={0}" -f (Format-Bytes $p))
            $r = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("SetGamingKBBacklight -> {0}" -f (Out-Code $r))
        }
    }
    @{
        Id = 'T09'
        Ask = 'WHITE'
        Desc = 'KBBL static WHITE with select byte sweep hit: try sel=0 (some EC maps)'
        Action = {
            $p = New-VenatorPayload 0xFF 0 100 1 255 255 255 0
            Write-Log ("payload={0}" -f (Format-Bytes $p))
            $r = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("SetGamingKBBacklight sel0 -> {0}" -f (Out-Code $r))
        }
    }
    @{
        Id = 'T10'
        Ask = 'ORANGE'
        Desc = 'Start AcerLightingService (if present), then Venator ORANGE'
        Action = {
            $svc = Get-Service AcerLightingService -ErrorAction SilentlyContinue
            if ($svc) {
                if ($svc.StartType -eq 'Disabled') {
                    Set-Service AcerLightingService -StartupType Manual -ErrorAction SilentlyContinue
                    Write-Log 'AcerLightingService StartType -> Manual'
                }
                if ($svc.Status -ne 'Running') {
                    Start-Service AcerLightingService -ErrorAction SilentlyContinue
                    Start-Sleep -Seconds 2
                }
                $svc2 = Get-Service AcerLightingService
                Write-Log ("AcerLightingService now Status={0}" -f $svc2.Status)
            } else {
                Write-Log 'AcerLightingService missing - skip start'
            }
            $p = New-VenatorPayload 0xFF 0 100 1 255 128 0 2
            $r = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("KBBL orange -> {0} payload={1}" -f (Out-Code $r), (Format-Bytes $p))
        }
    }
    @{
        Id = 'T11'
        Ask = 'PURPLE'
        Desc = 'Mode STATIC=0 (AcerECLogoLED Mode2 value) PURPLE sel=2'
        Action = {
            $p = New-VenatorPayload 0x00 0 100 1 128 0 255 2
            Write-Log ("payload={0}" -f (Format-Bytes $p))
            $r = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("SetGamingKBBacklight mode0 -> {0}" -f (Out-Code $r))
        }
    }
    @{
        Id = 'T12'
        Ask = 'RED'
        Desc = 'Nekro 6-byte via SetGamingLED (padded 16) + KBBL static RED'
        Action = {
            $buf = [byte[]]@(1, 255, 0, 0, 100, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            Write-Log ("SetGamingLED buf={0}" -f (Format-Bytes $buf))
            $r1 = Invoke-Gaming SetGamingLED $buf
            Write-Log ("SetGamingLED -> {0}" -f (Out-Code $r1))
            $p = New-VenatorPayload 0xFF 0 100 1 255 0 0 2
            $r2 = Invoke-Gaming SetGamingKBBacklight $p
            Write-Log ("KBBL -> {0}" -f (Out-Code $r2))
        }
    }
)

foreach ($step in $steps) {
    Write-Host ''
    Write-Log ("=== {0}: {1} ===" -f $step.Id, $step.Desc)
    & $step.Action
    Start-Sleep -Milliseconds 800
    $ans = Ask-Physical $step.Ask
    if ($ans -eq 'S') {
        Write-Log 'User skipped remaining steps'
        break
    }
    if ($ans -eq 'Y') {
        Write-Log ("HIT {0} - physical change confirmed" -f $step.Id)
    }
}

Write-Log '--- final baseline ---'
Show-Baseline
Write-Log '=== probe_logo_ask DONE ==='
Write-Host ''
Write-Host "Finished. Log saved to:" -ForegroundColor Green
Write-Host $logPath -ForegroundColor Green
Write-Host 'Tell the agent when done - it will read this log + your Y/N answers.'
