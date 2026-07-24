$ErrorActionPreference="Continue"
$out = Join-Path $PSScriptRoot "probe_ec_diff.txt"
function L($m){ Add-Content $out $m; Write-Host $m }
""|Set-Content $out

Stop-Service AcerLightingService -Force -EA SilentlyContinue
Get-Process OpenRGB -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 1

$inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction
function G($Method, $In){
  $r = Invoke-CimMethod -InputObject $inst -MethodName $Method -Arguments @{ gmInput = $In }
  $raw=$r.gmOutput
  if($raw -is [byte[]]){ return (($raw|ForEach-Object { $_.ToString("X2") }) -join "-") }
  return ("0x{0:X}" -f [uint64]$raw)
}
function Snap($tag){
  L "=== SNAP $tag ==="
  L ("GetGamingLED 0 = " + (G GetGamingLED ([uint32]0)))
  L ("GetGamingLEDBehavior 0 = " + (G GetGamingLEDBehavior ([uint32]0)))
  L ("GetGamingKBBacklight 0 = " + (G GetGamingKBBacklight ([uint32]0)))
  L ("GetGamingLEDColor 0 = " + (G GetGamingLEDColor ([uint32]0)))
  foreach($i in 0..3){ L ("GetGamingRgbKb $i = " + (G GetGamingRgbKb ([uint32]$i))) }
}

# Enable WMI trace
$ch='Microsoft-Windows-WMI-Activity/Trace'
wevtutil sl $ch /e:false 2>$null | Out-Null
wevtutil cl $ch 2>$null | Out-Null
wevtutil sl $ch /e:true 2>$null | Out-Null

Snap "BEFORE OpenRGB"

$repo = (Get-ChildItem C:\WINDOWS\System32\DriverStore\FileRepository -Directory -Filter 'predatorservice.inf_amd64_*' |
  Where-Object { (Test-Path (Join-Path $_.FullName 'OpenRGB.exe')) }).FullName | Select -First 1
$o = Join-Path $repo 'OpenRGB.exe'
L "OpenRGB detect..."
$p = Start-Process $o -ArgumentList '--list-devices','--noautoconnect','--loglevel','3' -WorkingDirectory $repo -PassThru -WindowStyle Hidden
$p.WaitForExit(20000) | Out-Null
L ("exit="+$p.ExitCode)

Snap "AFTER OpenRGB detect"

wevtutil sl $ch /e:false 2>$null | Out-Null
$events = wevtutil qe $ch /f:text /c:3000 2>&1 | Out-String
$hits=0
foreach($block in ($events -split 'Event\[')){
  if($block -match 'Gaming|AcerGaming|SetGaming|GetGaming'){
    $op = if($block -match 'Operation = (.+)'){ $Matches[1].Trim() } else { ($block | Select-String 'Gaming|Acer' | Select -First 1) }
    L ("WMI " + $op)
    $hits++
  }
}
L ("WMI acer hits=$hits")
L "DONE"
