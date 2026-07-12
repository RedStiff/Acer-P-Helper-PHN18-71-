#Requires -RunAsAdministrator
# Records every WMI call AcerLightingService makes during its startup
# initialization, using the Microsoft-Windows-WMI-Activity trace channel.
# The service is started only for the capture and stopped again afterwards.
$ErrorActionPreference = 'Continue'

$traceChannel = 'Microsoft-Windows-WMI-Activity/Trace'

Write-Host '[1/5] Enabling WMI activity tracing...'
wevtutil sl $traceChannel /e:false 2>&1 | Out-Null
wevtutil cl $traceChannel 2>&1 | Out-Null
wevtutil sl $traceChannel /e:true

Write-Host '[2/5] Starting AcerLightingService for capture...'
Set-Service -Name 'AcerLightingService' -StartupType Manual -ErrorAction SilentlyContinue
Start-Service -Name 'AcerLightingService'
Write-Host '      Waiting 15s for the service to finish device registration...'
Start-Sleep -Seconds 15

Write-Host '[3/5] Stopping service and disabling trace...'
Stop-Service -Name 'AcerLightingService' -Force
Set-Service -Name 'AcerLightingService' -StartupType Disabled -ErrorAction SilentlyContinue
wevtutil sl $traceChannel /e:false

Write-Host '[4/5] Extracting Acer-related WMI operations (chronological):'
$events = wevtutil qe $traceChannel /f:text /c:4000 2>&1 | Out-String
$blocks = $events -split 'Event\['
$hits = 0
foreach ($block in $blocks) {
    if ($block -match 'Acer|GamingFunction') {
        $time = if ($block -match 'Date: (\S+ \S+)') { $Matches[1] } else { '?' }
        $operation = if ($block -match 'Operation = (.+)') { $Matches[1].Trim() } else { ($block -split "`n" | Select-String 'Gaming|Acer' | Select-Object -First 1) }
        Write-Host ("{0}  {1}" -f $time, $operation)
        $hits++
    }
}
if ($hits -eq 0) {
    Write-Host 'No Acer WMI operations captured. Full event dump follows:'
    Write-Host ($events.Substring(0, [Math]::Min(8000, $events.Length)))
}

Write-Host ''
Write-Host '[5/5] Done. AcerLightingService is stopped and disabled again.'
