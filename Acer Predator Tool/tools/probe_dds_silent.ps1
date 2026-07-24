#Requires -Version 5.1
param(
    [ValidateSet('NvidiaOnly', 'Optimus', 'Automatic', 'Get')]
    [string]$Mode = 'Get',
    [int]$WatchSeconds = 20
)

$ErrorActionPreference = 'Stop'
$InjectDir = Join-Path $PSScriptRoot 'inject_dds'
$Launcher = Join-Path $InjectDir 'launcher.exe'
$HitPy = Join-Path $InjectDir '_hit_setdds.py'
$Log = Join-Path $env:TEMP ('dds_silent_probe_{0}.log' -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
$CdpPort = 9333

function Write-Log([string]$msg) {
    $line = '{0} {1}' -f (Get-Date -Format 'HH:mm:ss.fff'), $msg
    Write-Host $line
    Add-Content -Path $Log -Value $line
}

function Test-CdpUp {
    try {
        $null = Invoke-WebRequest -Uri ("http://127.0.0.1:{0}/json" -f $CdpPort) -UseBasicParsing -TimeoutSec 2
        return $true
    } catch { return $false }
}

function Get-Ace {
    $p = 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE'
    try {
        $v = Get-ItemProperty $p -ErrorAction Stop
        return "state=$($v.InternalMuxState)|auto=$($v.InternalMuxIsAutomaticMode)|i2d=$($v.ACESwitchedI2D)"
    } catch { return 'ACE=unreadable' }
}

function Get-VisibleNvidiaTitles {
    $list = @()
    foreach ($p in Get-Process -Name 'NVIDIA App' -ErrorAction SilentlyContinue) {
        if ($p.MainWindowHandle -ne [IntPtr]::Zero -and -not [string]::IsNullOrWhiteSpace($p.MainWindowTitle)) {
            $list += ('pid={0} title={1}' -f $p.Id, $p.MainWindowTitle)
        }
    }
    return $list
}

Write-Log "LOG=$Log"
Write-Log ('ACE before: {0}' -f (Get-Ace))

$flashSamples = 0
$maxVisible = 0
$watchStop = (Get-Date).AddSeconds($WatchSeconds)

if (Test-CdpUp) {
    Write-Log 'CDP already up - will reuse'
} else {
    Write-Log 'Killing existing NVIDIA App (if any)'
    Get-Process -Name 'NVIDIA App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    Write-Log 'Launching silent CDP host via launcher.exe'
    $p = Start-Process -FilePath $Launcher -WorkingDirectory $InjectDir -Wait -PassThru -NoNewWindow
    Write-Log ('launcher exit={0}' -f $p.ExitCode)
    if ($p.ExitCode -ne 0) { throw 'launcher failed' }

    $deadline = (Get-Date).AddSeconds(50)
    while ((Get-Date) -lt $deadline) {
        $visNow = @(Get-VisibleNvidiaTitles)
        if ($visNow.Count -gt 0) {
            $flashSamples++
            if ($visNow.Count -gt $maxVisible) { $maxVisible = $visNow.Count }
            Write-Log ('FLASH during wait: {0}' -f ($visNow -join ' | '))
        }
        if (Test-CdpUp) { break }
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-CdpUp)) { throw 'CDP did not come up' }
    Write-Log 'CDP ready'
    Start-Sleep -Seconds 4
}

$vis = @(Get-VisibleNvidiaTitles)
Write-Log ('Visible after launch: {0}' -f $(if ($vis.Count) { $vis -join ' | ' } else { 'NONE' }))
if ($vis.Count -gt 0) {
    $flashSamples++
    if ($vis.Count -gt $maxVisible) { $maxVisible = $vis.Count }
}

$pyMode = switch ($Mode) {
    'NvidiaOnly' { 'dgpu' }
    'Optimus' { 'igpu' }
    'Automatic' { 'auto' }
    'Get' { 'get' }
}

Write-Log ('Running _hit_setdds.py {0}' -f $pyMode)
$python = (Get-Command python).Source
& $python -u $HitPy $pyMode
$hit = $LASTEXITCODE
Write-Log ('hit exit={0} ACE after={1}' -f $hit, (Get-Ace))

$vis2 = @(Get-VisibleNvidiaTitles)
Write-Log ('Visible after SetDDS: {0}' -f $(if ($vis2.Count) { $vis2 -join ' | ' } else { 'NONE' }))
if ($vis2.Count -gt 0) {
    $flashSamples++
    if ($vis2.Count -gt $maxVisible) { $maxVisible = $vis2.Count }
}

if ($Mode -ne 'Get') {
    $next = if ($Mode -eq 'NvidiaOnly') { 'igpu' } else { 'dgpu' }
    Write-Log ('Reuse CDP second hit: {0}' -f $next)
    & $python -u $HitPy $next
    Write-Log ('second hit exit={0} ACE={1}' -f $LASTEXITCODE, (Get-Ace))
    $vis3 = @(Get-VisibleNvidiaTitles)
    Write-Log ('Visible after 2nd SetDDS: {0}' -f $(if ($vis3.Count) { $vis3 -join ' | ' } else { 'NONE' }))
    if ($vis3.Count -gt 0) {
        $flashSamples++
        if ($vis3.Count -gt $maxVisible) { $maxVisible = $vis3.Count }
    }
}

while ((Get-Date) -lt $watchStop) {
    $v = @(Get-VisibleNvidiaTitles)
    if ($v.Count -gt 0) {
        $flashSamples++
        if ($v.Count -gt $maxVisible) { $maxVisible = $v.Count }
    }
    Start-Sleep -Milliseconds 200
}

$silentOk = ($flashSamples -eq 0)
$ddsOk = ($hit -eq 0 -or $Mode -eq 'Get')
Write-Log ('WATCH flashSamples={0} maxVisible={1}' -f $flashSamples, $maxVisible)
Write-Log ('RESULT silent={0} dds_ok={1}' -f $silentOk, $ddsOk)
Write-Host ('Full log: {0}' -f $Log)
if (-not $silentOk) { exit 3 }
if (-not $ddsOk -and $Mode -ne 'Get') { exit 2 }
exit 0
