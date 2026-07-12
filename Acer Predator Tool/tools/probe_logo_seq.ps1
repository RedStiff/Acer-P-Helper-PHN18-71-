#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"
$log = "$PSScriptRoot\probe_logo_seq.log"
[IO.File]::WriteAllText($log, "")
function L([string]$m) { [IO.File]::AppendAllText($log, $m + "`r`n"); Write-Host $m }
$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
function Inv($m, $in) {
  try { return Invoke-CimMethod -InputObject $inst -MethodName $m -Arguments @{ gmInput = $in } }
  catch { L "$m ERR $($_.Exception.Message)"; return $null }
}
function PackCapture([byte]$r,[byte]$g,[byte]$b,[byte]$br) {
  return (([uint64](0x300 + $br) -shl 32) -bor ([uint64]$r -shl 24) -bor ([uint64]$g -shl 16) -bor ([uint64]$b -shl 8))
}
function PackNekro([byte]$r,[byte]$g,[byte]$b,[byte]$br,[byte]$en) {
  return [uint64](1 -bor ([uint64]$r -shl 8) -bor ([uint64]$g -shl 16) -bor ([uint64]$b -shl 24) -bor ([uint64]$br -shl 32) -bor ([uint64]$en -shl 40))
}
function OutCode($r) { if ($r) { return ("0x{0:X}" -f [uint32]$r.gmOutput) } else { return "null" } }

# Pure RED for visibility
$R=255; $G=0; $B=0; $BR=100
$static = [byte[]]@(0xFF, 0, $BR, 0, 1, $R, $G, $B, 3, 2, 0,0,0,0,0,0)

L "SEQ1: SetGamingLED(byte16 static) -> SetGamingLEDColor(capture) -> SetGamingLEDBehavior(7) -> KBBL"
$r = Inv SetGamingLED $static; L ("SetLED bytes " + (OutCode $r))
$r = Inv SetGamingLEDColor (PackCapture $R $G $B $BR); L ("SetColor capture " + (OutCode $r) + " pack=0x{0:X}" -f (PackCapture $R $G $B $BR))
$r = Inv SetGamingLEDBehavior ([uint64]7); L ("SetBeh " + (OutCode $r))
$r = Inv SetGamingKBBacklight $static; L ("KBBL " + (OutCode $r))
Start-Sleep 2
$r = Inv GetGamingLEDColor ([uint64]1); L ("Color1=0x{0:X}" -f [uint64]$r.gmOutput)
L ">>> SEQ1 done - is lid RED?"
Start-Sleep 5

L "SEQ2: keyboard-style wake then capture color then KBBL"
$zones = [uint64](8 -bor (0x0FUL -shl 40))
Inv SetGamingLEDBehavior ([uint64]7) | Out-Null
$r = Inv SetGamingLED $zones; L ("SetLED ulong zones " + (OutCode $r))
$r = Inv SetGamingLEDColor (PackCapture $R $G $B $BR); L ("SetColor capture " + (OutCode $r))
$r = Inv SetGamingKBBacklight $static; L ("KBBL " + (OutCode $r))
Start-Sleep 2
$r = Inv GetGamingLEDColor ([uint64]1); L ("Color1=0x{0:X}" -f [uint64]$r.gmOutput)
L ">>> SEQ2 done"
Start-Sleep 5

L "SEQ3: nekro color + SetGamingLED bytes + KBBL + LEDBehavior 0x07 and logo-ish behaviors"
foreach ($beh in @(7, 1, 2, 3, 0x10, 0x100, (7UL -bor (2UL -shl 8)), (2UL -bor (7UL -shl 8)))) {
  Inv SetGamingLEDColor (PackNekro $R $G $B $BR 1) | Out-Null
  Inv SetGamingLED $static | Out-Null
  $r = Inv SetGamingLEDBehavior ([uint64]$beh)
  Inv SetGamingKBBacklight $static | Out-Null
  L ("beh=0x{0:X} out={1}" -f $beh, (OutCode $r))
}
Start-Sleep 2
$r = Inv GetGamingLEDColor ([uint64]1); L ("Color1=0x{0:X}" -f [uint64]$r.gmOutput)
L ">>> SEQ3 done"

L "SEQ4: try SetGamingLEDColor with capture AFTER successful nekro write (hybrid)"
Inv SetGamingLEDColor (PackNekro $R $G $B $BR 1) | Out-Null
$r = Inv SetGamingLEDColor (PackCapture $R $G $B $BR); L ("capture after nekro " + (OutCode $r))
Inv SetGamingKBBacklight $static | Out-Null
Start-Sleep 2
$r = Inv GetGamingLEDColor ([uint64]1); L ("Color1=0x{0:X}" -f [uint64]$r.gmOutput)

L "DONE. Please tell if lid was ever RED during this run."