#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Short confirmation probe for the T05 HIT path (Nekro SetGamingLEDColor + enable gate).
  Asks Y/N after each step. Isolates color-only vs gate-only vs both.

.USAGE
  Close AcerPredatorTool.exe, then (admin PowerShell):
    cd $PSScriptRoot
    .\probe_logo_nekro_confirm.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$logPath = Join-Path $PSScriptRoot "probe_logo_nekro_$stamp.log"

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

function Pack-Nekro([byte]$R, [byte]$G, [byte]$B, [byte]$Brightness, [byte]$Enable) {
    [uint64]$v = 1
    $v = $v -bor ([uint64]$R -shl 8)
    $v = $v -bor ([uint64]$G -shl 16)
    $v = $v -bor ([uint64]$B -shl 24)
    $v = $v -bor ([uint64]$Brightness -shl 32)
    $v = $v -bor ([uint64]$Enable -shl 40)
    return $v
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

function Out-Code($Result) {
    if (-not $Result) { return 'null' }
    try { return ('0x{0:X}' -f [uint32]$Result.gmOutput) } catch { return '?' }
}

function Set-LogoNekro([byte]$R, [byte]$G, [byte]$B, [byte]$Brightness, [byte]$Enable, [bool]$SendGate) {
    $pack = Pack-Nekro $R $G $B $Brightness $Enable
    Write-Log ("nekro pack=0x{0:X} R={1} G={2} B={3} br={4} en={5}" -f $pack, $R, $G, $B, $Brightness, $Enable)
    $r = Invoke-Gaming SetGamingLEDColor $pack
    Write-Log ("SetGamingLEDColor -> {0}" -f (Out-Code $r))
    if ($SendGate) {
        $gate = [byte[]]@([byte]$Enable, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0)
        $r2 = Invoke-Gaming SetGamingKBBacklight $gate
        Write-Log ("enable-gate en={0} -> {1}" -f $Enable, (Out-Code $r2))
    }
}

Write-Log "=== probe_logo_nekro_confirm start ==="
Write-Log "Log: $logPath"
Write-Host 'Look at LID logo. Confirms T05 path (Nekro color + optional gate).' -ForegroundColor Magenta
$null = Read-Host 'Press Enter when ready'

$script:Inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
if (-not $script:Inst) { throw 'AcerGamingFunction not found' }

$c = Invoke-Gaming GetGamingLEDColor ([uint64]1)
if ($c) { Write-Log ("baseline Color1=0x{0:X}" -f [uint64]$c.gmOutput) }

$steps = @(
    @{ Id='C01'; Ask='RED'; Desc='COLOR+GATE: RED 100 enable=1'; Action={ Set-LogoNekro 255 0 0 100 1 $true } }
    @{ Id='C02'; Ask='BLUE'; Desc='COLOR+GATE: BLUE 100 enable=1'; Action={ Set-LogoNekro 0 0 255 100 1 $true } }
    @{ Id='C03'; Ask='OFF (dark / no light)'; Desc='COLOR+GATE: enable=0 brightness=0'; Action={ Set-LogoNekro 0 0 255 0 0 $true } }
    @{ Id='C04'; Ask='GREEN'; Desc='COLOR+GATE: GREEN 100 enable=1'; Action={ Set-LogoNekro 0x22 0xC7 0x10 100 1 $true } }
    @{ Id='C05'; Ask='RED'; Desc='COLOR ONLY (no gate): RED'; Action={ Set-LogoNekro 255 0 0 100 1 $false } }
    @{ Id='C06'; Ask='BLUE'; Desc='GATE ONLY after red color already set: enable pulse? (sets blue color first then gate-only would be wrong - instead: set blue via color, then re-send gate=1 only with OLD pack skipped)'; Action={
        # Isolate gate: first set color RED without asking, then only toggle gate off/on with blue already in EC from prior? 
        # Clearer: set BLUE via color only was C05 style; here send ONLY gate enable=1 without new color.
        $gate = [byte[]]@(1, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0)
        $r2 = Invoke-Gaming SetGamingKBBacklight $gate
        Write-Log ("gate-only enable=1 -> {0}" -f (Out-Code $r2))
    } }
    @{ Id='C07'; Ask='OFF (dark / no light)'; Desc='GATE ONLY enable=0 (no color write)'; Action={
        $gate = [byte[]]@(0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0)
        $r2 = Invoke-Gaming SetGamingKBBacklight $gate
        Write-Log ("gate-only enable=0 -> {0}" -f (Out-Code $r2))
    } }
    @{ Id='C08'; Ask='MAGENTA'; Desc='COLOR+GATE: MAGENTA (T05 replay)'; Action={ Set-LogoNekro 255 0 255 100 1 $true } }
)

foreach ($step in $steps) {
    Write-Host ''
    Write-Log ("=== {0}: {1} ===" -f $step.Id, $step.Desc)
    & $step.Action
    Start-Sleep -Milliseconds 700
    $ans = Ask-Physical $step.Ask
    if ($ans -eq 'S') { Write-Log 'Skipped remaining'; break }
    if ($ans -eq 'Y') { Write-Log ("HIT {0}" -f $step.Id) }
}

$c = Invoke-Gaming GetGamingLEDColor ([uint64]1)
if ($c) { Write-Log ("final Color1=0x{0:X}" -f [uint64]$c.gmOutput) }
Write-Log '=== DONE ==='
Write-Host ''
Write-Host "Log: $logPath" -ForegroundColor Green
