#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"
$log = Join-Path $PSScriptRoot ("probe_logo2_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))
function L($m) { $m | Tee-Object -FilePath $log -Append }
$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
function Inv($m, $in) {
  try { return Invoke-CimMethod -InputObject $inst -MethodName $m -Arguments @{ gmInput = $in } }
  catch { L "$m ERR $($_.Exception.Message)"; return $null }
}
function PackNekro([byte]$r,[byte]$g,[byte]$b,[byte]$br,[byte]$en) {
  # force uint64 shifts
  $v = [uint64]1
  $v = $v -bor ([uint64]$r -shl 8)
  $v = $v -bor ([uint64]$g -shl 16)
  $v = $v -bor ([uint64]$b -shl 24)
  $v = $v -bor ([uint64]$br -shl 32)
  $v = $v -bor ([uint64]$en -shl 40)
  return $v
}
function PackCapture([byte]$r,[byte]$g,[byte]$b,[byte]$br) {
  # from user capture GetGamingLEDColor: ((0x300+br)<<32) | (R<<24)|(G<<16)|(B<<8)
  return (([uint64](0x300 + $br) -shl 32) -bor ([uint64]$r -shl 24) -bor ([uint64]$g -shl 16) -bor ([uint64]$b -shl 8))
}

L "Services: $((Get-Service AcerLightingService,AcerHardwareService,AcerAgentService -EA SilentlyContinue | ForEach-Object { \"$($_.Name)=$($_.Status)\" }) -join ', ')"

L "=== baseline ==="
$r = Inv GetGamingLED ([uint32]0); if ($r) { L ("GetLED ret={0:X} {1}" -f [uint32]$r.gmReturn, (($r.gmOutput|%{'{0:X2}' -f $_}) -join ' ')) }
$r = Inv GetGamingLEDColor ([uint64]1); if ($r) { L ("Color1=0x{0:X}" -f [uint64]$r.gmOutput) }
$r = Inv GetGamingKBBacklight ([uint32]0); if ($r) { L ("KBBL ret={0:X} {1}" -f [uint32]$r.gmReturn, (($r.gmOutput|%{'{0:X2}' -f $_}) -join ' ')) }

L "=== A: correct nekro SetGamingLEDColor RED 100 enable=1 ==="
$p = PackNekro 255 0 0 100 1
L ("pack=0x{0:X}" -f $p)
$r = Inv SetGamingLEDColor $p; if ($r) { L ("out=0x{0:X}" -f [uint32]$r.gmOutput) }
$r = Inv SetGamingKBBacklight ([byte[]]@(1,0,0,0,0,0,0,0,0,2,0,0,0,0,0,0)); if ($r) { L ("gate=0x{0:X}" -f [uint32]$r.gmOutput) }
Start-Sleep 3
$r = Inv GetGamingLEDColor ([uint64]1); if ($r) { L ("Color1 after A=0x{0:X}" -f [uint64]$r.gmOutput) }
L ">>> A done (RED?). pause 4s"; Start-Sleep 4

L "=== B: capture-style SetGamingLEDColor RED 100 ==="
$p = PackCapture 255 0 0 100
L ("pack=0x{0:X}" -f $p)
$r = Inv SetGamingLEDColor $p; if ($r) { L ("out=0x{0:X}" -f [uint32]$r.gmOutput) }
Start-Sleep 3
$r = Inv GetGamingLEDColor ([uint64]1); if ($r) { L ("Color1 after B=0x{0:X}" -f [uint64]$r.gmOutput) }
L ">>> B done"; Start-Sleep 4

L "=== C: SetGamingLED then venator static MAGENTA bright 255 ==="
# mirror GetGamingLED shape then apply
$led = [byte[]]@(0xFF,5,255,0,1,255,0,255,3,2,0,0,0,0,0,0)
$r = Inv SetGamingLED $led; if ($r) { L ("SetLED=0x{0:X}" -f [uint32]$r.gmOutput) }
$r = Inv SetGamingKBBacklight $led; if ($r) { L ("KBBL=0x{0:X}" -f [uint32]$r.gmOutput) }
Start-Sleep 3
L ">>> C done (MAGENTA?)"; Start-Sleep 4

L "=== D: off via venator mode0 ==="
$off = [byte[]]@(0,0,0,0,1,0,0,0,3,2,0,0,0,0,0,0)
$r = Inv SetGamingKBBacklight $off; if ($r) { L ("off=0x{0:X}" -f [uint32]$r.gmOutput) }
$r = Inv SetGamingLEDColor (PackNekro 0 0 0 0 0); if ($r) { L ("colorOff=0x{0:X}" -f [uint32]$r.gmOutput) }
Start-Sleep 3
L ">>> D done (OFF?)"; Start-Sleep 4

L "=== E: restore green static via venator ==="
$g = [byte[]]@(0xFF,0,100,0,1,0x22,0xC7,0x10,3,2,0,0,0,0,0,0)
$r = Inv SetGamingKBBacklight $g; if ($r) { L ("green=0x{0:X}" -f [uint32]$r.gmOutput) }
$r = Inv SetGamingLEDColor (PackNekro 0x22 0xC7 0x10 100 1); if ($r) { L ("greenColor=0x{0:X}" -f [uint32]$r.gmOutput) }

L "DONE log=$log"
