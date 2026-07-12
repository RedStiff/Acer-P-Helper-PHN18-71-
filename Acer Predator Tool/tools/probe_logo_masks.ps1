#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"
$log = Join-Path $PSScriptRoot "probe_logo_masks.log"
"" | Set-Content $log
function L($m) { $m | Tee-Object -FilePath $log -Append }
$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
function Inv($m, $in) {
  try { return Invoke-CimMethod -InputObject $inst -MethodName $m -Arguments @{ gmInput = $in } }
  catch { return $null }
}
function PackZone([uint64]$mask,[byte]$r,[byte]$g,[byte]$b) {
  return $mask -bor ([uint64]$r -shl 8) -bor ([uint64]$g -shl 16) -bor ([uint64]$b -shl 24)
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

# Try keyboard-like EC recovery but for candidate logo masks, color = pure BLUE
$blueR=0; $blueG=0; $blueB=255

L "=== Try SetGamingLED ulong masks (keyboard style) + color + behavior + KBBL sel2 ==="
$masks = @(
  0x07,
  0x08,
  (8 -bor (0x0FUL -shl 40)),
  (1 -bor (0x10UL -shl 40)),
  (8 -bor (0x10UL -shl 40)),
  (8 -bor (0xF0UL -shl 40)),
  (0x10UL),
  (0x10UL -bor (0x10UL -shl 8)),
  0x100000000000, # bit 44?
  0x200000000000,
  0x400000000000,
  0x800000000000,
  (1UL -shl 40),
  (2UL -shl 40),
  (4UL -shl 40),
  (0x10UL -shl 40),
  (0x20UL -shl 40),
  (0x40UL -shl 40),
  (0x80UL -shl 40)
)

foreach ($mask in $masks) {
  $r1 = Inv SetGamingLEDBehavior ([uint64]7)
  $r2 = Inv SetGamingLED ([uint64]$mask)
  $color = PackZone 0x0F $blueR $blueG $blueB
  # also try color with low mask nibble from mask
  $r3 = Inv SetGamingLEDColor (PackZone ([byte]($mask -band 0xFF)) $blueR $blueG $blueB)
  $r3b = Inv SetGamingLEDColor (PackNekro $blueR $blueG $blueB 100 1)
  $payload = [byte[]]@(0xFF,0,100,0,1,$blueR,$blueG,$blueB,3,2,0,0,0,0,0,0)
  $r4 = Inv SetGamingKBBacklight $payload
  $ok = @([uint32]$r1.gmOutput,[uint32]$r2.gmOutput,$(if($r3){[uint32]$r3.gmOutput}else{-1}),[uint32]$r4.gmOutput)
  L ("mask=0x{0:X} LEDBeh={1:X} LED={2:X} Color={3:X} KBBL={4:X}" -f $mask, $ok[0], $ok[1], $ok[2], $ok[3])
}

L "=== Try SetGamingLEDColor with group selectors 0..8 as low byte + RGB blue ==="
foreach ($sel in 0..8) {
  $v = PackZone ([byte]$sel) 0 0 255
  $r = Inv SetGamingLEDColor $v
  L ("colorSel={0} pack=0x{1:X} out=0x{2:X}" -f $sel, $v, $(if($r){[uint32]$r.gmOutput}else{-1}))
}

L "=== Final readback ==="
$r = Inv GetGamingLEDColor ([uint64]1); L ("Color1=0x{0:X}" -f [uint64]$r.gmOutput)
$r = Inv GetGamingLED ([uint32]0); L ("LED ret={0:X} {1}" -f [uint32]$r.gmReturn, (($r.gmOutput|%{'{0:X2}' -f $_}) -join ' '))
$r = Inv GetGamingKBBacklight ([uint32]0); L ("KBBL ret={0:X} {1}" -f [uint32]$r.gmReturn, (($r.gmOutput|%{'{0:X2}' -f $_}) -join ' '))
L "CHECK LID: should be BLUE if any path worked. Left in last state."
