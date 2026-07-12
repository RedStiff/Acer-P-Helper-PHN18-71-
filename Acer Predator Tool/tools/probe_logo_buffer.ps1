#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"
$log = "$PSScriptRoot\probe_logo_buffer.log"
[IO.File]::WriteAllText($log, "")
function L([string]$m) { [IO.File]::AppendAllText($log, $m + "`r`n"); Write-Host $m }
$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
function Inv($m, $in) {
  try { return Invoke-CimMethod -InputObject $inst -MethodName $m -Arguments @{ gmInput = $in } }
  catch { L "$m ERR $($_.Exception.Message)"; return $null }
}
function OutCode($r) { if ($r) { return ("0x{0:X}" -f [uint32]$r.gmOutput) } else { return "null" } }

# Pure cyan for visibility against green
$R=0; $G=255; $B=255; $BR=100; $EN=1

L "=== Try SetGamingLED with nekro 6-byte logo buffer (padded) ==="
$buf6 = [byte[]]@(1, $R, $G, $B, $BR, $EN, 0,0,0,0,0,0,0,0,0,0)
$r = Inv SetGamingLED $buf6; L ("SetLED nekro6 " + (OutCode $r) + " in=" + (($buf6|%{'{0:X2}' -f $_}) -join ' '))

L "=== Try SetGamingLED with only 6 bytes (not padded) ==="
$buf6only = [byte[]]@(1, $R, $G, $B, $BR, $EN)
$r = Inv SetGamingLED $buf6only; L ("SetLED nekro6only " + (OutCode $r))

L "=== Try SetGamingLED 12-byte variants ==="
$variants = @(
  ,[byte[]]@(1, $R, $G, $B, $BR, $EN, 0,0,0,0,0,0)
  ,[byte[]]@(0, 1, $R, $G, $B, $BR, $EN, 0,0,0,0,0)
  ,[byte[]]@($EN, $BR, $R, $G, $B, 1, 0,0,0,0,0,0)
  ,[byte[]]@(2, $R, $G, $B, $BR, $EN, 0,0,0,0,0,0)  # select 2?
  ,[byte[]]@(0x0C, 1, $R, $G, $B, $BR, $EN, 0,0,0,0,0)
)
$i=0
foreach ($v in $variants) {
  $r = Inv SetGamingLED $v
  L ("var$i " + (OutCode $r) + " " + (($v|%{'{0:X2}' -f $_}) -join ' '))
  $i++
}

L "=== Try SetGamingLEDBehavior with byte array ==="
try {
  $r = Invoke-CimMethod -InputObject $inst -MethodName SetGamingLEDBehavior -Arguments @{ gmInput = [byte[]]@(7,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0) }
  L ("SetBeh bytes " + (OutCode $r))
} catch { L "SetBeh bytes ERR $($_.Exception.Message)" }

L "=== Combo: SetLED nekro6 + KBBL static cyan sel2 + enable gate ==="
Inv SetGamingLED ([byte[]]@(1, $R, $G, $B, $BR, $EN, 0,0,0,0,0,0,0,0,0,0)) | Out-Null
$static = [byte[]]@(0xFF, 0, $BR, 0, 1, $R, $G, $B, 3, 2, 0,0,0,0,0,0)
Inv SetGamingKBBacklight $static | Out-Null
Inv SetGamingKBBacklight ([byte[]]@(1,0,0,0,0,0,0,0,0,2,0,0,0,0,0,0)) | Out-Null
Start-Sleep 1
$r = Inv GetGamingLEDColor ([uint64]1); L ("Color1=0x{0:X}" -f [uint64]$r.gmOutput)
$r = Inv GetGamingLED ([uint32]0); L ("GetLED ret=0x{0:X} {1}" -f [uint32]$r.gmReturn, (($r.gmOutput|%{'{0:X2}' -f $_}) -join ' '))

L "LEFT CYAN ATTEMPT ON - check lid vs previous green"