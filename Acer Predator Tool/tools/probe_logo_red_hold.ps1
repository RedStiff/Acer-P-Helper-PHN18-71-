#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"
$log = Join-Path $PSScriptRoot "probe_logo_red_hold.log"
function L($m) { $m | Tee-Object -FilePath $log -Append }
$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
function Inv($m, $in) {
  try { return Invoke-CimMethod -InputObject $inst -MethodName $m -Arguments @{ gmInput = $in } }
  catch { L "$m ERR $($_.Exception.Message)"; return $null }
}
function PackNekro([byte]$r,[byte]$g,[byte]$b,[byte]$br,[byte]$en) {
  $v = [uint64]1
  $v = $v -bor ([uint64]$r -shl 8)
  $v = $v -bor ([uint64]$g -shl 16)
  $v = $v -bor ([uint64]$b -shl 24)
  $v = $v -bor ([uint64]$br -shl 32)
  $v = $v -bor ([uint64]$en -shl 40)
  return $v
}

L "Setting LOGO to BRIGHT RED and holding..."
# 1) color via 0x0C
$p = PackNekro 255 0 0 100 1
L ("SetGamingLEDColor 0x{0:X}" -f $p)
Inv SetGamingLEDColor $p | Out-Null
# 2) effect static via method 20 select 2
$payload = [byte[]]@(0xFF, 0, 100, 0, 1, 255, 0, 0, 3, 2, 0,0,0,0,0,0)
Inv SetGamingKBBacklight $payload | Out-Null
# 3) nekro enable gate
Inv SetGamingKBBacklight ([byte[]]@(1,0,0,0,0,0,0,0,0,2,0,0,0,0,0,0)) | Out-Null
# 4) also try SetGamingLEDBehavior wake like keyboard
Inv SetGamingLEDBehavior ([uint64]7) | Out-Null
Inv SetGamingLED ([byte[]]@(8,0,0,0,0,0x0F,0,0,0,0,0,0,0,0,0,0)) | Out-Null

Start-Sleep 1
$r = Inv GetGamingLEDColor ([uint64]1)
L ("Color1 now=0x{0:X}" -f [uint64]$r.gmOutput)
$r = Inv GetGamingKBBacklight ([uint32]0)
L ("KBBL ret={0:X} {1}" -f [uint32]$r.gmReturn, (($r.gmOutput|%{'{0:X2}' -f $_}) -join ' '))

L "LOGO SHOULD BE RED NOW - left in this state. Check the lid."
L "If still green, EC logo is NOT driven by these WMI methods on PHN18."
