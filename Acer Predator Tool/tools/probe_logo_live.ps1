#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"
$log = Join-Path $PSScriptRoot ("probe_logo_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))
function L($m) { $m | Tee-Object -FilePath $log -Append }

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
if (-not $inst) { L "NO AcerGamingFunction"; exit 1 }

function Inv($m, $in) {
  try { return Invoke-CimMethod -InputObject $inst -MethodName $m -Arguments @{ gmInput = $in } }
  catch { L "$m ERR $($_.Exception.Message)"; return $null }
}
function HexBytes($b) { if (-not $b) { return "" }; return (($b | ForEach-Object { "{0:X2}" -f $_ }) -join " ") }
function PackNekro([byte]$r,[byte]$g,[byte]$b,[byte]$br,[byte]$en) {
  return [uint64](1 -bor ($r -shl 8) -bor ($g -shl 16) -bor ($b -shl 24) -bor ([uint64]$br -shl 32) -bor ([uint64]$en -shl 40))
}

L "=== baseline ==="
$r = Inv GetGamingKBBacklight ([uint32]0)
if ($r) { L ("KBBL ret=0x{0:X} out={1}" -f [uint32]$r.gmReturn, (HexBytes $r.gmOutput)) }
$r = Inv GetGamingLEDColor ([uint64]1)
if ($r) { L ("LEDColor1=0x{0:X}" -f [uint64]$r.gmOutput) }
$r = Inv GetGamingLED ([uint32]0)
if ($r) { L ("LED ret=0x{0:X} out={1}" -f [uint32]$r.gmReturn, (HexBytes $r.gmOutput)) }

L "=== A: nekro SetGamingLEDColor red + enable gate ==="
$p = PackNekro 255 0 0 100 1
L ("pack=0x{0:X}" -f $p)
$r = Inv SetGamingLEDColor $p
if ($r) { L ("SetGamingLEDColor=0x{0:X}" -f [uint32]$r.gmOutput) }
$r = Inv SetGamingKBBacklight ([byte[]]@(1,0,0,0,0,0,0,0,0,2,0,0,0,0,0,0))
if ($r) { L ("gate=0x{0:X}" -f [uint32]$r.gmOutput) }
Start-Sleep -Seconds 2
L ">>> LOOK AT LOGO now (expect RED if A worked). Waiting 5s..."
Start-Sleep -Seconds 5

L "=== B: venator static blue ==="
$r = Inv SetGamingKBBacklight ([byte[]]@(0xFF,0,100,0,1,0,0,255,3,2,0,0,0,0,0,0))
if ($r) { L ("venator=0x{0:X}" -f [uint32]$r.gmOutput) }
Start-Sleep -Seconds 5
L ">>> LOOK AT LOGO (expect BLUE if B worked)"

L "=== C: keyboard-style payload but select=2 mode0 static green via recovery-ish ==="
# mode 0 with [8]=3 [9]=2 like keyboard static but logo select
$r = Inv SetGamingKBBacklight ([byte[]]@(0,0,100,0,0,0,255,0,3,2,0,0,0,0,0,0))
if ($r) { L ("mode0sel2=0x{0:X}" -f [uint32]$r.gmOutput) }
Start-Sleep -Seconds 3

L "=== D: SetGamingRgbKb zone masks with high bits ==="
# try packing like keyboard zones but weird masks
foreach ($mask in 0x10,0x20,0x40,0x80,0x100) {
  $val = [uint64]$mask -bor (255 -shl 8) -bor (0 -shl 16) -bor (255 -shl 24) # magenta-ish
  $r = Inv SetGamingRgbKb $val
  L ("RgbKb mask=0x{0:X} out=0x{1:X}" -f $mask, $(if($r){[uint32]$r.gmOutput}else{-1}))
}

L "=== E: GetGamingLEDColor after ==="
$r = Inv GetGamingLEDColor ([uint64]1)
if ($r) { L ("LEDColor1=0x{0:X}" -f [uint64]$r.gmOutput) }

L "LOG=$log"
Write-Host "Wrote $log"
