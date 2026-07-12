#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"
$log = "$PSScriptRoot\probe_enable_lighting_svc.log"
function L($m){ Add-Content $log $m; Write-Host $m }
"" | Set-Content $log
try {
  Set-Service AcerLightingService -StartupType Manual -ErrorAction Stop
  Start-Service AcerLightingService -ErrorAction Stop
  L "AcerLightingService started: $((Get-Service AcerLightingService).Status)"
} catch { L "Start failed: $($_.Exception.Message)" }
Start-Sleep 3
Get-Process | Where-Object { $_.Name -match 'OpenRGB|AcerLighting|AcerHardware' } | ForEach-Object { L ("PROC $($_.Name) $($_.Id)") }
# Check listening ports for OpenRGB SDK default 6742
netstat -ano | Select-String '6742|6743|6777' | ForEach-Object { L $_.Line }
L "Done"