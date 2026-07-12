# Removes the PredatorSense Store app. The AcerLightingService driver package
# is left intact by default because it restores keyboard lighting at boot.
# Run with -Full (as Administrator) to also remove the Acer services/driver package
# if they are the source of driver conflicts.
param([switch]$Full)

Write-Host '[1/2] Removing PredatorSense Store app...'
Get-Process -Name 'PredatorSense' -ErrorAction SilentlyContinue | Stop-Process -Force
Get-AppxPackage -Name 'ULICTekInc.PredatorSenseforNotebook' | Remove-AppxPackage
Write-Host 'PredatorSense app removed.'

if (-not $Full) {
    Write-Host '[2/2] AcerLightingService and driver package predatorservice.inf kept'
    Write-Host '      (they restore keyboard lighting at boot).'
    Write-Host '      Re-run with -Full as Administrator to remove them too.'
    return
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw '-Full requires an elevated PowerShell.' }

Write-Host '[2/2] Stopping and removing Acer services + predatorservice.inf...'
foreach ($service in 'AcerLightingService', 'AASSvc') {
    Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
    Set-Service -Name $service -StartupType Disabled -ErrorAction SilentlyContinue
}

$driver = pnputil /enum-drivers |
    Select-String -Context 0, 6 -Pattern 'predatorservice\.inf' |
    ForEach-Object { ($_.Context.PreContext + $_.Line) -match 'oem\d+\.inf' | Out-Null; $Matches[0] }
$published = (pnputil /enum-drivers | Out-String) -split 'Published Name:' |
    Where-Object { $_ -match 'predatorservice\.inf' } |
    ForEach-Object { ($_ -split "`n")[0].Trim() }

if ($published) {
    foreach ($inf in $published) {
        Write-Host "Deleting driver package $inf..."
        pnputil /delete-driver $inf /uninstall /force
    }
} else {
    Write-Host 'predatorservice.inf not found among published drivers.'
}
Write-Host 'Done. Reboot to verify our app now initializes lighting on its own.'
