#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Read-only Embedded Controller (EC) RAM dump helpers for PHN18-71 research.

.DESCRIPTION
  Does NOT write to the EC. Uses RWEverything (Rw.exe) if available to dump
  EC space via its command interface. Compare dumps while changing Custom fan
  % in Acer Predator Tool / PredatorSense to find which bytes track duty/RPM.

  Official Acer IntelliSense / PredatorSense v5 may later reveal a finer path;
  until then WMI Custom remains quantized to ~10% by firmware.

.PARAMETER RwPath
  Full path to Rw.exe (RWEverything). If omitted, searches common locations
  and PATH.

.PARAMETER OutDir
  Directory for dump files. Default: tools\ec_dumps\<timestamp>

.EXAMPLE
  .\ec_dump_readonly.ps1
  .\ec_dump_readonly.ps1 -RwPath 'C:\Tools\Rw.exe'
#>
[CmdletBinding()]
param(
    [string]$RwPath,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-RwExe {
    param([string]$Explicit)
    if ($Explicit -and (Test-Path -LiteralPath $Explicit)) { return (Resolve-Path $Explicit).Path }

    $candidates = @(
        (Join-Path $scriptDir 'Rw.exe'),
        (Join-Path $scriptDir 'RWEverything\Rw.exe'),
        'C:\Program Files\RWEverything\Rw.exe',
        'C:\Program Files (x86)\RWEverything\Rw.exe',
        'C:\Tools\Rw.exe',
        'C:\RWEverything\Rw.exe'
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { return (Resolve-Path $c).Path }
    }

    $fromPath = Get-Command Rw.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }
    return $null
}

function New-DumpDir {
    param([string]$Requested)
    if ($Requested) {
        New-Item -ItemType Directory -Force -Path $Requested | Out-Null
        return (Resolve-Path $Requested).Path
    }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $dir = Join-Path $scriptDir ("ec_dumps\{0}" -f $stamp)
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

Write-Host '=== EC read-only dump (no writes) ===' -ForegroundColor Cyan
Write-Host 'Target research model: Acer Predator PHN18-71'
Write-Host ''

$rw = Find-RwExe -Explicit $RwPath
$out = New-DumpDir -Requested $OutDir
$readmeOut = Join-Path $out 'README.txt'

@"
EC read-only dump session
=========================
Created: $(Get-Date -Format o)
Host: $env:COMPUTERNAME
User: $env:USERNAME

Purpose
-------
Capture EC RAM while Custom fan % changes, then diff dumps to locate duty/RPM
bytes. This script never issues EC writes.

Suggested workflow
------------------
1. Set fans to Custom in Acer Predator Tool.
2. Set CPU=40% GPU=40%, wait ~3s for RPM to settle, run this script (label A).
3. Set CPU=41% GPU=41%, dump again (label B) — if identical to A, firmware
   ignored 1% (expected on PHN18-71).
4. Set CPU=50% GPU=50%, dump (label C) — expect byte changes vs A.
5. Diff A/B/C (ec_diff_dumps.ps1).

RWEverything
------------
Download: https://rweverything.com/
Place Rw.exe next to this script, or pass -RwPath.
Windows may block the kernel driver (Vulnerable Driver Blocklist / HVCI).
Disable those only if you accept the security trade-off; still READ-ONLY here.

Acer IntelliSense / PredatorSense v5
------------------------------------
Later: capture WMI/ETW while the official app changes Custom fans and compare
with these dumps to see whether v5 uses the same SetGamingFanSpeed path or a
hidden EC register.
"@ | Set-Content -LiteralPath $readmeOut -Encoding UTF8

if (-not $rw) {
    Write-Warning 'Rw.exe not found. Wrote session notes only.'
    Write-Host "Notes: $readmeOut"
    Write-Host ''
    Write-Host 'Install RWEverything, then re-run:'
    Write-Host "  .\ec_dump_readonly.ps1 -RwPath 'C:\path\to\Rw.exe'"
    Write-Host ''
    Write-Host 'Meanwhile you can still use WMI fan probes:'
    Write-Host '  .\ec_wmi_fan_snapshot.ps1'
    exit 2
}

Write-Host "Rw.exe: $rw"
Write-Host "OutDir: $out"

# RWEverything command syntax varies by version. Try common EC dump forms.
# We only use read/dump style commands — never WEC / write variants.
$cmdFile = Join-Path $out 'rw_commands.txt'
@(
    '# Read-only EC dump attempts (RWEverything Command window syntax).'
    '# If one form fails, try the next manually in Rw.exe → Command.'
    'EC'
    'REC 0 256'
    'DumpEC'
) | Set-Content -LiteralPath $cmdFile -Encoding ASCII

$log = Join-Path $out 'rw_stdout.log'
$attempts = @(
    @{ Args = @('/Min', '/Nologo', '/Stdout', "/Command=`"EC`""); Name = 'EC' },
    @{ Args = @('/Min', '/Nologo', '/Stdout', "/Command=`"REC 0 256`""); Name = 'REC_0_256' },
    @{ Args = @('/Min', '/Nologo', '/Stdout', "/Command=`"DumpEC`""); Name = 'DumpEC' }
)

$anyOk = $false
foreach ($attempt in $attempts) {
    $attemptLog = Join-Path $out ("rw_{0}.log" -f $attempt.Name)
    Write-Host ("Trying: Rw.exe {0}" -f ($attempt.Args -join ' '))
    try {
        $p = Start-Process -FilePath $rw -ArgumentList $attempt.Args -Wait -PassThru `
            -RedirectStandardOutput $attemptLog -RedirectStandardError (Join-Path $out ("rw_{0}.err" -f $attempt.Name)) `
            -WindowStyle Hidden
        "exit=$($p.ExitCode)" | Add-Content -LiteralPath $attemptLog
        if ((Test-Path $attemptLog) -and ((Get-Item $attemptLog).Length -gt 8)) {
            $anyOk = $true
            Write-Host "  -> wrote $attemptLog" -ForegroundColor Green
        }
        else {
            Write-Host '  -> empty/failed' -ForegroundColor DarkYellow
        }
    }
    catch {
        Write-Host ("  -> error: {0}" -f $_.Exception.Message) -ForegroundColor DarkYellow
    }
}

# Also keep a combined pointer log
"RwPath=$rw`nAnyOutput=$anyOk`nSee rw_*.log and README.txt" | Set-Content -LiteralPath $log -Encoding UTF8

if (-not $anyOk) {
    Write-Warning @'
RWEverything CLI dump produced no usable stdout.
Open Rw.exe GUI → Embedded Controller, save/export the EC table manually into this OutDir
(e.g. ec_manual_40pct.txt), then use ec_diff_dumps.ps1.
'@
}

Write-Host ''
Write-Host "Done. Session folder: $out"
Write-Host 'Next: change Custom fan %, re-run with -OutDir pointing at a new folder, then:'
Write-Host '  .\ec_diff_dumps.ps1 -Left <dirA> -Right <dirB>'
