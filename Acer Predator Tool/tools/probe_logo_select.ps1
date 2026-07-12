#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"
$log = "$PSScriptRoot\probe_logo_select.log"
[IO.File]::WriteAllText($log, "")
function L([string]$m) {
  [IO.File]::AppendAllText($log, $m + [Environment]::NewLine)
  Write-Host $m
}
$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
function Inv($m, $in) {
  try { return Invoke-CimMethod -InputObject $inst -MethodName $m -Arguments @{ gmInput = $in } }
  catch { L "$m ERR $($_.Exception.Message)"; return $null }
}

L "Sweep KBBacklight select byte 0..15 with STATIC blue"
foreach ($sel in 0..15) {
  $payload = [byte[]]@(0xFF, 0, 100, 0, 1, 0, 0, 255, 3, [byte]$sel, 0,0,0,0,0,0)
  $r = Inv SetGamingKBBacklight $payload
  $code = if ($r) { [uint32]$r.gmOutput } else { 0xFFFFFFFF }
  L ("sel={0} out=0x{1:X}" -f $sel, $code)
  Start-Sleep -Milliseconds 200
}

L "Sweep mode ids with sel=2 brightness 100 color yellow"
foreach ($mode in @(0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0xFF,0xFE,0x80,0x0A)) {
  $payload = [byte[]]@([byte]$mode, 3, 100, 0, 1, 255, 255, 0, 3, 2, 0,0,0,0,0,0)
  $r = Inv SetGamingKBBacklight $payload
  $code = if ($r) { [uint32]$r.gmOutput } else { 0xFFFFFFFF }
  L ("mode=0x{0:X2} out=0x{1:X}" -f $mode, $code)
}

L "Try SetGamingLED ulong like keyboard then logo KBBL"
$kbZones = [uint64](8 -bor (0x0FUL -shl 40))
Inv SetGamingLEDBehavior ([uint64]7) | Out-Null
$r = Inv SetGamingLED $kbZones
L ("SetGamingLED zones out=0x{0:X}" -f [uint32]$r.gmOutput)
# logo color via LEDColor with nekro AND zone1
function PackNekro([byte]$r,[byte]$g,[byte]$b,[byte]$br,[byte]$en) {
  return [uint64](1 -bor ([uint64]$r -shl 8) -bor ([uint64]$g -shl 16) -bor ([uint64]$b -shl 24) -bor ([uint64]$br -shl 32) -bor ([uint64]$en -shl 40))
}
$r = Inv SetGamingLEDColor (PackNekro 255 0 255 100 1)
L ("nekro magenta color out=0x{0:X}" -f [uint32]$r.gmOutput)
$r = Inv SetGamingKBBacklight ([byte[]]@(0xFF,0,100,0,1,255,0,255,3,2,0,0,0,0,0,0))
L ("static magenta sel2 out=0x{0:X}" -f [uint32]$r.gmOutput)

# Try GetGamingLEDColor with different masks
foreach ($m in 0,1,2,3,4,5,8,0x10,0x0F) {
  $r = Inv GetGamingLEDColor ([uint64]$m)
  if ($r) { L ("GetColor mask={0} =0x{1:X}" -f $m, [uint64]$r.gmOutput) }
}

L "DONE - look at lid. If still green, need DLL reverse / OpenRGB SDK."