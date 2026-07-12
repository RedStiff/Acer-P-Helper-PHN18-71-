$ErrorActionPreference = "Continue"
$log = "$PSScriptRoot\probe_logo_ask_step5.out.txt"
function W($m){ Add-Content $log $m; Write-Host $m }
"" | Set-Content $log
try {
  $svc = Get-Service AcerLightingService
  W ("before Status={0} StartType={1}" -f $svc.Status, $svc.StartType)
  if ($svc.StartType -eq "Disabled") {
    Set-Service AcerLightingService -StartupType Manual
    W "Set StartType=Manual"
  }
  Start-Service AcerLightingService
  Start-Sleep 3
  $svc.Refresh()
  W ("after Status={0}" -f $svc.Status)
  W "ASK: Did the lid LED change when AcerLightingService started?"
} catch {
  W ("ERR: " + $_.Exception.Message)
}
