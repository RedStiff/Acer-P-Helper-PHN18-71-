#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Interactive probe for PHN18 lid LOGO EFFECTS (Breathing / Neon).
  Color path is already confirmed (Nekro SetGamingLEDColor).
  After each step: ASK is effect <name>? Y/N

.USAGE
  Close AcerPredatorTool.exe. Admin PowerShell:
    cd $PSScriptRoot
    .\probe_logo_effects_ask.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$logPath = Join-Path $PSScriptRoot "probe_logo_effects_$stamp.log"

function Write-Log([string]$Message) {
    $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $line
}

function Ask-Physical([string]$Expected) {
    Write-Host ''
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ("ASK: is effect/color {0}?  (Y/N)  [S=skip rest]" -f $Expected) -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
    while ($true) {
        $ans = (Read-Host 'Your answer').Trim().ToUpperInvariant()
        if ($ans -in @('Y', 'N', 'S')) { break }
        Write-Host 'Enter Y, N, or S'
    }
    Write-Log ("ANSWER expected='{0}' -> {1}" -f $Expected, $ans)
    return $ans
}

function Pack-Nekro([byte]$SelectOrMode, [byte]$R, [byte]$G, [byte]$B, [byte]$Brightness, [byte]$Enable) {
    [uint64]$v = [uint64]$SelectOrMode
    $v = $v -bor ([uint64]$R -shl 8)
    $v = $v -bor ([uint64]$G -shl 16)
    $v = $v -bor ([uint64]$B -shl 24)
    $v = $v -bor ([uint64]$Brightness -shl 32)
    $v = $v -bor ([uint64]$Enable -shl 40)
    return $v
}

function Invoke-Gaming([string]$Method, $GmInput) {
    try {
        return Invoke-CimMethod -InputObject $script:Inst -MethodName $Method -Arguments @{ gmInput = $GmInput }
    } catch {
        Write-Log ("{0} ERR: {1}" -f $Method, $_.Exception.Message)
        return $null
    }
}

function Out-Code($Result) {
    if (-not $Result) { return 'null' }
    try { return ('0x{0:X}' -f [uint32]$Result.gmOutput) } catch { return '?' }
}

function Format-Bytes([byte[]]$Bytes) {
    return (($Bytes | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
}

function Set-NekroColor([byte]$R, [byte]$G, [byte]$B, [byte]$Br = 100, [byte]$En = 1, [byte]$Sel = 1) {
    $pack = Pack-Nekro $Sel $R $G $B $Br $En
    Write-Log ("SetGamingLEDColor pack=0x{0:X}" -f $pack)
    $r = Invoke-Gaming SetGamingLEDColor $pack
    Write-Log ("SetGamingLEDColor -> {0}" -f (Out-Code $r))
}

function Set-Kbbl([byte[]]$Payload) {
    Write-Log ("KBBL payload={0}" -f (Format-Bytes $Payload))
    $r = Invoke-Gaming SetGamingKBBacklight $Payload
    Write-Log ("SetGamingKBBacklight -> {0}" -f (Out-Code $r))
}

function New-Kbbl([byte]$Mode, [byte]$Speed, [byte]$Br, [byte]$Dir, [byte]$R, [byte]$G, [byte]$B, [byte]$Sel, [byte]$Marker8 = 3) {
    return [byte[]]@($Mode, $Speed, $Br, 0, $Dir, $R, $G, $B, $Marker8, $Sel, 0, 0, 0, 0, 0, 0)
}

Write-Log "=== probe_logo_effects_ask start ==="
Write-Log "Log: $logPath"
Write-Host 'LID logo effects probe. Breathing = pulsing brightness. Neon = rainbow cycle.' -ForegroundColor Magenta
$null = Read-Host 'Press Enter when ready'

$script:Inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
if (-not $script:Inst) { throw 'AcerGamingFunction not found' }

# Known-good static red baseline so we see transitions
Write-Log '--- baseline static RED via Nekro ---'
Set-NekroColor 255 0 0 100 1 1
Start-Sleep -Milliseconds 500
$base = Ask-Physical 'RED static (baseline)'
if ($base -eq 'S') { Write-Log 'Aborted'; exit 0 }

$R = 255; $G = 0; $B = 0; $Br = 100

$steps = @(
    @{
        Id = 'E01'
        Ask = 'RED breathing (pulsing)'
        Desc = 'Nekro color then Venator KBBL mode=1 BREATHING sel=2'
        Action = {
            Set-NekroColor $R $G $B $Br 1 1
            Set-Kbbl (New-Kbbl 0x01 5 $Br 1 $R $G $B 2)
        }
    }
    @{
        Id = 'E02'
        Ask = 'Neon / rainbow cycling'
        Desc = 'Nekro color then Venator KBBL mode=2 NEON sel=2'
        Action = {
            Set-NekroColor $R $G $B $Br 1 1
            Set-Kbbl (New-Kbbl 0x02 5 $Br 1 $R $G $B 2)
        }
    }
    @{
        Id = 'E03'
        Ask = 'RED breathing (pulsing)'
        Desc = 'KBBL breathing ONLY (no Nekro rewrite) mode=1 sel=2'
        Action = {
            Set-Kbbl (New-Kbbl 0x01 5 $Br 1 $R $G $B 2)
        }
    }
    @{
        Id = 'E04'
        Ask = 'RED breathing (pulsing)'
        Desc = 'Nekro first-byte=mode(1) instead of select(1) - breathing as color cmd'
        Action = {
            Set-NekroColor $R $G $B $Br 1 1
            $pack = Pack-Nekro 1 $R $G $B $Br 1  # same as normal
            # Try mode value in byte0: Breathing=1 already is select. Try AcerECLogoLED BREATHING value with enable
            # Use byte0 = 1 (breathing id) - already default. Try packing mode into enable nibble? 
            # Alternate: SetGamingLEDColor with mode in high bits after enable
            $alt = Pack-Nekro 1 $R $G $B $Br 1
            $alt = $alt -bor ([uint64]1 -shl 48)  # effect hint?
            Write-Log ("alt pack with bit48=0x{0:X}" -f $alt)
            $r = Invoke-Gaming SetGamingLEDColor $alt
            Write-Log ("SetGamingLEDColor alt -> {0}" -f (Out-Code $r))
            Set-Kbbl (New-Kbbl 0x01 3 $Br 1 $R $G $B 2)
        }
    }
    @{
        Id = 'E05'
        Ask = 'RED breathing (pulsing)'
        Desc = 'Nekro + KBBL with AcerECLogoLED STATIC/BREATHING values: mode=1, marker8=0, sel=2'
        Action = {
            Set-NekroColor $R $G $B $Br 1 1
            Set-Kbbl (New-Kbbl 0x01 3 $Br 0 $R $G $B 2 0)
        }
    }
    @{
        Id = 'E06'
        Ask = 'RED breathing (pulsing)'
        Desc = 'Nekro + SetGamingLED (not KBBL) breathing buffer sel=2'
        Action = {
            Set-NekroColor $R $G $B $Br 1 1
            $p = New-Kbbl 0x01 5 $Br 1 $R $G $B 2
            Write-Log ("SetGamingLED payload={0}" -f (Format-Bytes $p))
            $r = Invoke-Gaming SetGamingLED $p
            Write-Log ("SetGamingLED -> {0}" -f (Out-Code $r))
            Set-Kbbl $p
        }
    }
    @{
        Id = 'E07'
        Ask = 'RED breathing (pulsing)'
        Desc = 'Gate-style KBBL with speed in byte1: en=1, speed=5, br=100, effect=1?, sel=2'
        Action = {
            Set-NekroColor $R $G $B $Br 1 1
            # Nekro gate layout comments: LBLE, LBLS, LBBP, 0, LBED, R,G,B, LLES, select
            $gate = [byte[]]@(1, 5, $Br, 0, 1, $R, $G, $B, 0, 2, 0, 0, 0, 0, 0, 0)
            Set-Kbbl $gate
        }
    }
    @{
        Id = 'E08'
        Ask = 'Neon / rainbow cycling'
        Desc = 'Nekro + KBBL mode=0xFF static then mode=2 neon (mode switch sequence)'
        Action = {
            Set-NekroColor $R $G $B $Br 1 1
            Set-Kbbl (New-Kbbl 0xFF 0 $Br 1 $R $G $B 2)
            Start-Sleep -Milliseconds 300
            Set-Kbbl (New-Kbbl 0x02 5 $Br 1 0 0 0 2)
        }
    }
    @{
        Id = 'E09'
        Ask = 'RED breathing (pulsing)'
        Desc = 'KBBL mode=1 sel=2 with brightness 255 scale'
        Action = {
            Set-NekroColor $R $G $B 100 1 1
            Set-Kbbl (New-Kbbl 0x01 5 255 1 $R $G $B 2)
        }
    }
    @{
        Id = 'E10'
        Ask = 'RED static (back to solid)'
        Desc = 'Control: Nekro static only - should be solid RED not pulsing'
        Action = {
            Set-NekroColor $R $G $B $Br 1 1
        }
    }
    @{
        Id = 'E11'
        Ask = 'RED breathing (pulsing)'
        Desc = 'Nekro byte0=BREATHING profile value already 1; try GetKBBL then set mode via byte0 of color as 0x81?'
        Action = {
            # Some EC use flags|mode. Try select=1 color then KBBL mode1 with [8]=3 [9]=2 and speed in 1..5
            Set-NekroColor $R $G $B $Br 1 1
            foreach ($spd in @(1, 3, 5, 9)) {
                Set-Kbbl (New-Kbbl 0x01 ([byte]$spd) $Br 1 $R $G $B 2)
                Start-Sleep -Milliseconds 150
            }
        }
    }
    @{
        Id = 'E12'
        Ask = 'Neon / rainbow cycling'
        Desc = 'Nekro green then KBBL neon; colors ignored for neon in profile'
        Action = {
            Set-NekroColor 0 255 0 100 1 1
            Set-Kbbl (New-Kbbl 0x02 3 100 1 0 0 0 2)
        }
    }
)

foreach ($step in $steps) {
    Write-Host ''
    Write-Log ("=== {0}: {1} ===" -f $step.Id, $step.Desc)
    try {
        & $step.Action
    } catch {
        Write-Log ("STEP ERR: {0}" -f $_.Exception.Message)
    }
    Start-Sleep -Milliseconds 900
    $ans = Ask-Physical $step.Ask
    if ($ans -eq 'S') { Write-Log 'Skipped remaining'; break }
    if ($ans -eq 'Y') { Write-Log ("HIT {0}" -f $step.Id) }
}

Write-Log '=== DONE ==='
Write-Host ''
Write-Host "Log: $logPath" -ForegroundColor Green
Write-Host 'Tell the agent when finished.'
