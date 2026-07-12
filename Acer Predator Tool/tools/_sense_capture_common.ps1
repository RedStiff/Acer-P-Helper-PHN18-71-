# Shared helpers for Predator Sense WMI capture scripts.
$script:SenseCaptureLogRegistry = 'HKLM:\SOFTWARE\OEM\AcerLightingService\Log'
$script:SenseCaptureLogDirectory = 'C:\ProgramData\OEM\AcerLightingService\Log'
$script:SenseCaptureProfileIni = 'C:\ProgramData\OEM\AcerLightingService\LightingProfile\LightingProfile.ini'
$script:SenseCaptureWmiTrace = 'Microsoft-Windows-WMI-Activity/Trace'

function Get-PredatorDriverStorePath {
    $dir = Get-ChildItem 'C:\WINDOWS\System32\DriverStore\FileRepository\predatorservice.inf_*' -Directory -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $dir) { return $null }
    return $dir.FullName
}

function Enable-SenseVerboseLogging {
    New-Item -Path $script:SenseCaptureLogRegistry -Force | Out-Null
    foreach ($name in @('Enable', 'LogLevel', 'log_level', 'loglevel', 'Level', 'TraceLevel', 'Debug', 'Verbose')) {
        Set-ItemProperty -Path $script:SenseCaptureLogRegistry -Name $name -Value 6 -Type DWord -ErrorAction SilentlyContinue
    }
}

function Enable-WmiActivityTrace {
    wevtutil sl $script:SenseCaptureWmiTrace /e:false 2>&1 | Out-Null
    wevtutil cl $script:SenseCaptureWmiTrace 2>&1 | Out-Null
    wevtutil sl $script:SenseCaptureWmiTrace /e:true 2>&1 | Out-Null
}

function Disable-WmiActivityTrace {
    wevtutil sl $script:SenseCaptureWmiTrace /e:false 2>&1 | Out-Null
}

function Format-SnapshotValue($Value) {
    if ($null -eq $Value) { return '<null>' }
    if ($Value -is [byte[]]) { return Format-ByteArray $Value }
    if ($Value -is [string]) { return $Value }
    if ($Value -is [System.Array]) {
        return (($Value | ForEach-Object { '{0:X2}' -f ([byte]$_) }) -join ' ')
    }
    return $Value.ToString()
}

function Write-SnapshotLines {
    param(
        [System.IO.TextWriter]$Writer,
        [System.Collections.IDictionary]$Snapshot
    )
    foreach ($key in $Snapshot.Keys) {
        $Writer.WriteLine($key + ' = ' + (Format-SnapshotValue $Snapshot[$key]))
    }
}

function Format-ByteArray([byte[]]$Bytes) {
    if ($null -eq $Bytes -or $Bytes.Length -eq 0) { return '<empty>' }
    return (($Bytes | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
}

function Convert-GmOutputToBytes($raw) {
    if ($null -eq $raw) { return [byte[]]@() }
    if ($raw -is [byte[]]) { return $raw }
    if ($raw -is [string]) {
        $parts = $raw -split '\s+' | Where-Object { $_ -ne '' }
        if ($parts.Count -gt 0) {
            return [byte[]]($parts | ForEach-Object { [Convert]::ToByte($_, 16) })
        }
        return [byte[]]@()
    }
    if ($raw -is [System.Array]) {
        return [byte[]]($raw | ForEach-Object { [byte]$_ })
    }
    return [byte[]]@([byte]([uint64]$raw -band 0xFF))
}

function Read-ApGeOutput($out) {
    $raw = $out['uiOutput']
    if ($null -eq $raw) { return 0 }
    if ($raw -is [byte[]]) {
        $u64 = [uint64]0
        $len = [Math]::Min(8, $raw.Length)
        for ($i = 0; $i -lt $len; $i++) { $u64 = ($u64 -shl 8) -bor $raw[$i] }
        return $u64
    }
    if ($raw -is [System.Array] -and $raw.Length -gt 0) { return [uint64]$raw[0] }
    return [uint64]$raw
}

function Get-LogoWmiSnapshot {
    $snap = [ordered]@{}

    try {
        $inst = Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction -ErrorAction Stop

        $led = Invoke-CimMethod -InputObject $inst -MethodName GetGamingLED -Arguments @{ gmInput = [uint32]0 }
        $snap.GetGamingLED_gmOutput = Format-ByteArray (Convert-GmOutputToBytes $led.gmOutput)

        $color = Invoke-CimMethod -InputObject $inst -MethodName GetGamingLEDColor -Arguments @{ gmInput = [uint64]0x01 }
        $snap.GetGamingLEDColor_mask01 = ('0x{0:X}' -f [uint64]$color.gmOutput)

        $kb = Invoke-CimMethod -InputObject $inst -MethodName GetGamingKBBacklight -Arguments @{ gmInput = [uint32]0 }
        $snap.GetGamingKBBacklight_gmOutput = Format-ByteArray (Convert-GmOutputToBytes $kb.gmOutput)
    } catch {
        $snap.AcerGamingFunction_error = $_.Exception.Message
    }

    foreach ($cls in @('APGeAction', 'AcerGenericMethod')) {
        try {
            $mc = New-Object System.Management.ManagementClass("\\.\root\WMI:$cls")
            $in = $mc.GetMethodParameters('GetFunction')
            $in['uiInput'] = [uint32]0x88401
            $out = $mc.InvokeMethod('GetFunction', $in, $null)
            $snap["${cls}_timeout"] = ('0x{0:X} RV={1}' -f (Read-ApGeOutput $out), $out['ReturnValue'])
        } catch {
            $snap["${cls}_timeout_error"] = $_.Exception.Message
        }
    }

    return $snap
}

function Write-LogoProfileSection([System.IO.TextWriter]$Writer) {
    if (-not (Test-Path $script:SenseCaptureProfileIni)) { return }
    $lines = Get-Content $script:SenseCaptureProfileIni
    $inLogo = $false
    foreach ($line in $lines) {
        if ($line -match '^\[AcerECLogoLED') { $inLogo = $true }
        elseif ($line -match '^\[' -and $inLogo) { break }
        if ($inLogo) { $Writer.WriteLine($line) }
    }
}

function Get-NewServiceLogLines([datetime]$Since) {
    if (-not (Test-Path $script:SenseCaptureLogDirectory)) { return @() }
    $lines = @()
    Get-ChildItem $script:SenseCaptureLogDirectory -File |
        Where-Object { $_.LastWriteTime -ge $Since } |
        ForEach-Object {
            $lines += "===== $($_.Name) ====="
            $lines += Get-Content $_.FullName -ErrorAction SilentlyContinue |
                Where-Object { $_ -match 'Logo|LED|Gaming|Function|WMI|SetGaming|brightness|8840|timeout|STATIC|Direct|EC' }
        }
    return $lines
}

function Get-WmiTraceHits {
    param([string[]]$Patterns = @('Acer', 'Gaming', 'APGe', 'SetFunction', 'GetFunction', 'LED', 'Backlight', '8840'))

    $events = wevtutil qe $script:SenseCaptureWmiTrace /f:text /c:8000 2>&1 | Out-String
    $blocks = $events -split '(?=Event\[)'
    $hits = @()
    foreach ($block in $blocks) {
        if ([string]::IsNullOrWhiteSpace($block)) { continue }
        $matched = $false
        foreach ($p in $Patterns) {
            if ($block -match [regex]::Escape($p)) { $matched = $true; break }
        }
        if (-not $matched) { continue }

        $time = if ($block -match 'Date:\s+(\S+\s+\S+)') { $Matches[1] } else { '?' }
        $op = if ($block -match 'Operation\s*=\s*(.+)') { $Matches[1].Trim() } else { '' }
        $path = if ($block -match 'Path\s*=\s*(.+)') { $Matches[1].Trim() } else { '' }
        $hits += [pscustomobject]@{ Time = $time; Operation = $op; Path = $path; Block = $block.Trim() }
    }
    return $hits
}

function Write-CaptureSection {
    param(
        [System.IO.TextWriter]$Writer,
        [string]$Title,
        [scriptblock]$Body
    )
    $Writer.WriteLine('')
    $Writer.WriteLine(('=' * 72))
    $Writer.WriteLine($Title)
    $Writer.WriteLine(('=' * 72))
    & $Body
}

function Wait-CaptureStep {
    param([string]$Prompt)
    Write-Host ''
    Write-Host $Prompt -ForegroundColor Cyan
    Read-Host '  Press Enter after the action in Predator Sense'
}
