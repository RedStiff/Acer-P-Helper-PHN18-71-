# Deep BEFORE/AFTER observation around AcerService GPU_MODE (and related actions).
# Goal: log side-effects so AcerPredatorTool can emulate them without AcerServiceSvc.
# Dot-source after _gpu_common.ps1 and _acer_service.ps1.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

function Get-ObserveRegFlatMap {
    param([string[]]$Roots, [int]$MaxDepth = 6)

    $map = @{}
    function Walk([string]$Root, [int]$Depth) {
        if ($Depth -gt $MaxDepth) { return }
        $rootPath = ConvertTo-PsRegistryPath -Path $Root
        if (-not (Test-Path -LiteralPath $rootPath)) { return }
        try {
            $item = Get-Item -LiteralPath $rootPath -ErrorAction Stop
            $path = ConvertTo-PsRegistryPath -Path $item.PSPath
            foreach ($vn in @($item.GetValueNames())) {
                try {
                    $val = $item.GetValue($vn)
                    $shown = if ($null -eq $val) { '<null>' }
                        elseif ($val -is [byte[]]) { (($val | ForEach-Object { '{0:X2}' -f $_ }) -join ' ') }
                        elseif ($val -is [Array]) { ($val -join ',') }
                        else { "$val" }
                    if ($null -eq $shown) { $shown = '<null>' }
                    if ($shown.Length -gt 500) { $shown = $shown.Substring(0, 500) + '...' }
                    $name = if ([string]::IsNullOrEmpty($vn)) { '(default)' } else { $vn }
                    $map["$path\$name"] = $shown
                } catch { }
            }
            Get-ChildItem -LiteralPath $rootPath -ErrorAction SilentlyContinue | ForEach-Object {
                Walk -Root $_.PSPath -Depth ($Depth + 1)
            }
        } catch { }
    }
    foreach ($r in $Roots) { Walk -Root $r -Depth 0 }
    return $map
}

function Convert-ObserveMapFromObject($Obj) {
    $map = @{}
    if ($null -eq $Obj) { return $map }
    if ($Obj -is [System.Collections.IDictionary]) {
        foreach ($k in @($Obj.Keys)) { $map["$k"] = "$($Obj[$k])" }
        return $map
    }
    foreach ($p in @($Obj.PSObject.Properties)) {
        if ($p.Name -in @('Count', 'Keys', 'Values', 'SyncRoot', 'IsSynchronized', 'IsFixedSize', 'IsReadOnly')) { continue }
        $map["$($p.Name)"] = "$($p.Value)"
    }
    return $map
}

function Compare-ObserveMaps {
    param($Before, $After)
    $bMap = Convert-ObserveMapFromObject $Before
    $aMap = Convert-ObserveMapFromObject $After
    $changes = New-Object System.Collections.Generic.List[string]
    $keys = @(@($bMap.Keys) + @($aMap.Keys) | Select-Object -Unique | Sort-Object)
    foreach ($k in $keys) {
        $b = if ($bMap.ContainsKey($k)) { $bMap[$k] } else { '<missing>' }
        $a = if ($aMap.ContainsKey($k)) { $aMap[$k] } else { '<missing>' }
        if ("$b" -ne "$a") {
            [void]$changes.Add(("CHANGED  {0}`n    BEFORE: {1}`n    AFTER:  {2}" -f $k, $b, $a))
        }
    }
    return [pscustomobject]@{ Count = $changes.Count; Items = @($changes.ToArray()) }
}

function Get-AcerMiscObserveSnapshot {
    $out = [ordered]@{}
    if (-not (Test-IsAdmin)) {
        $out['acer_misc'] = 'skipped_not_admin'
        return $out
    }
    try {
        $inst = Get-AcerGaming
        # Broad scan: power + historical GPU-candidate misc IDs (read-only GET).
        $ids = @(
            0x01, 0x06, 0x08, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
            0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
            0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
            0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30
        )
        foreach ($id in $ids) {
            $g = Get-MiscSetting -Inst $inst -Id ([uint64]$id)
            if (-not $g.Ok) {
                $out[('misc_0x{0:X2}' -f $id)] = 'GET_FAIL'
                continue
            }
            $out[('misc_0x{0:X2}' -f $id)] = ('status=0x{0:X2};value=0x{1:X2};raw=0x{2:X}' -f `
                $g.Status, $g.Value, $g.Raw)
        }
    } catch {
        $out['acer_error'] = $_.Exception.Message
    }
    return $out
}

function Get-BiosDisplayModeSnapshot {
    $result = [ordered]@{
        Available = $false
        Offset80  = $null
        WindowHex = $null
        Length    = 0
        Error     = $null
    }
    if (-not (Test-IsAdmin)) {
        $result.Error = 'not_admin'
        return [pscustomobject]$result
    }
    try {
        $got = Get-BiosOptionsData
        if (-not $got.Ok) {
            $result.Error = $got.Error
            return [pscustomobject]$result
        }
        $data = [byte[]]$got.Data
        if ($data.Length -le 80) {
            $result.Error = 'buffer_too_small'
            return [pscustomobject]$result
        }
        $result.Available = $true
        $result.Length = $data.Length
        $result.Offset80 = [int]$data[80]
        $start = 70
        $end = [Math]::Min(90, $data.Length - 1)
        $slice = $data[$start..$end]
        $result.WindowHex = (@(for ($i = 0; $i -lt $slice.Length; $i++) {
            ('[{0}]={1:X2}' -f ($start + $i), $slice[$i])
        }) -join ' ')
    } catch {
        $result.Error = $_.Exception.Message
    }
    return [pscustomobject]$result
}

function Get-AcerAgentGpuCapabilitySnapshot {
    $map = [ordered]@{}
    $path = 'HKLM:\SOFTWARE\OEM\AcerAgentService\AdvanceSettings'
    if (-not (Test-Path -LiteralPath $path)) {
        $map['AdvanceSettings'] = 'missing'
        return $map
    }
    try {
        $item = Get-Item -LiteralPath $path
        foreach ($vn in @($item.GetValueNames())) {
            $map[$vn] = "$($item.GetValue($vn))"
        }
    } catch {
        $map['error'] = $_.Exception.Message
    }
    return $map
}

function Get-AcerRelatedServicesSnapshot {
    $names = @(
        'AcerServiceSvc', 'AcerLightingService', 'AcerQAAgentSvis', 'AcerCCAgentSvis',
        'AcerDIAgentSvis', 'ASMSvc', 'AcerDeviceEnablingServiceV2', 'PredatorService'
    )
    $map = [ordered]@{}
    foreach ($n in $names) {
        $s = Get-Service -Name $n -ErrorAction SilentlyContinue
        if ($s) {
            $map[$n] = ('Status={0};StartType={1}' -f $s.Status, $s.StartType)
        } else {
            $map[$n] = 'missing'
        }
    }
    Get-Service -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'Acer|Predator|Sense' -and $names -notcontains $_.Name } |
        ForEach-Object { $map[$_.Name] = ('Status={0};StartType={1}' -f $_.Status, $_.StartType) }
    return $map
}

function Get-DisplayPnpSnapshot {
    $map = [ordered]@{}
    Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | ForEach-Object {
        $key = $_.InstanceId
        $map[$key] = ('Status={0};Problem={1};Name={2}' -f $_.Status, $_.Problem, $_.FriendlyName)
    }
    return $map
}

function Get-AcerServiceProcessSnapshot {
    $map = [ordered]@{}
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -match 'Acer|Predator|PreySense|XSense' -or
            ($_.ExecutablePath -and $_.ExecutablePath -match 'Acer|Predator|PreySense')
        } |
        ForEach-Object {
            $map[("$($_.ProcessId):$($_.Name)")] = ("Path={0};Cmd={1}" -f $_.ExecutablePath, $_.CommandLine)
        }
    return $map
}

function New-AcerObserveSessionDir {
    param([string]$Prefix = 'acer_service_capture')
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $dir = Join-Path $PSScriptRoot ("{0}_{1}" -f $Prefix, $stamp)
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

function Get-AcerObserveSnapshot {
    param(
        [string]$Label,
        [string]$LogPath
    )
    Write-GpuLog $LogPath ("OBSERVE snapshot {0}..." -f $Label) Cyan

    $fp = Get-GpuFingerprint
    Write-GpuFingerprint -LogPath $LogPath -Fp $fp -Label $Label

    $regRoots = @(
        'HKLM:\SOFTWARE\Acer',
        'HKCU:\Software\Acer',
        'HKLM:\SOFTWARE\OEM\Acer',
        'HKCU:\Software\OEM\Acer',
        'HKLM:\SOFTWARE\NVIDIA Corporation',
        'HKCU:\Software\NVIDIA Corporation',
        'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm',
        'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers',
        'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}',
        'HKCU:\Software\Microsoft\DirectX'
    )
    $reg = Get-ObserveRegFlatMap -Roots $regRoots -MaxDepth 6
    Write-GpuLog $LogPath ("  registry values: {0}" -f $reg.Count)

    $misc = Get-AcerMiscObserveSnapshot
    Write-GpuLog $LogPath ("  acer misc keys: {0}" -f (@($misc.Keys).Count))

    $bios = Get-BiosDisplayModeSnapshot
    Write-GpuLog $LogPath ("  BIOS offset80={0} available={1} err={2}" -f $bios.Offset80, $bios.Available, $bios.Error)

    $services = Get-AcerRelatedServicesSnapshot
    $pnp = Get-DisplayPnpSnapshot
    $procs = Get-AcerServiceProcessSnapshot
    $svcInfo = Get-AcerServiceSvcInfo
    $agentCaps = Get-AcerAgentGpuCapabilitySnapshot

    Write-GpuLog $LogPath ("  services={0} displayPnp={1} acerProcs={2} protocolReady={3} listen=[{4}]" -f `
        (@($services.Keys).Count), (@($pnp.Keys).Count), (@($procs.Keys).Count), `
        $svcInfo.ProtocolReady, ($svcInfo.ListenPorts -join ','))

    return [pscustomobject]@{
        Label            = $Label
        Timestamp        = (Get-Date).ToString('o')
        MuxSignature     = $fp.MuxSignature
        Signature        = $fp.Signature
        OwnerKind        = $fp.OwnerKind
        NvidiaDisplay    = $fp.NvidiaDisplay
        NvidiaPowerW     = $fp.NvidiaPowerW
        NvidiaClockMhz   = $fp.NvidiaClockMhz
        AcerServicePort  = [bool]$svcInfo.ProtocolReady
        AcerServiceInfo  = $svcInfo
        AcerAgentCaps    = $agentCaps
        Registry         = $reg
        AcerMisc         = $misc
        BiosDisplayMode  = $bios
        Services         = $services
        DisplayPnp       = $pnp
        AcerProcesses    = $procs
        Displays         = @($fp.Displays | ForEach-Object {
            [pscustomobject]@{
                AdapterName   = $_.AdapterName
                Kind          = $_.Kind
                Active        = $_.Active
                Primary       = $_.Primary
                AdapterString = $_.AdapterString
                MonitorString = $_.MonitorString
            }
        })
        VideoControllers = @($fp.VideoControllers)
    }
}

function Save-AcerObserveJson($Obj, [string]$Path) {
    $Obj | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-AcerObserveDiff {
    param(
        $Before,
        $After,
        $TcpTrace,
        [string]$Action,
        [string]$DiffPath,
        [string]$LogPath
    )

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add('AcerService observation DIFF')
    [void]$lines.Add(('Action={0}' -f $Action))
    [void]$lines.Add(('BEFORE {0}' -f $Before.Timestamp))
    [void]$lines.Add(('AFTER  {0}' -f $After.Timestamp))
    [void]$lines.Add('')
    [void]$lines.Add(('BEFORE mux={0}' -f $Before.MuxSignature))
    [void]$lines.Add(('AFTER  mux={0}' -f $After.MuxSignature))
    $muxChanged = "$($Before.MuxSignature)" -ne "$($After.MuxSignature)"
    [void]$lines.Add(('MUX_CHANGED={0}' -f $muxChanged))
    [void]$lines.Add('')
    [void]$lines.Add('Goal: find durable side-effects to emulate in AcerPredatorTool WITHOUT AcerServiceSvc.')
    [void]$lines.Add('Note: Ultimate mux often applies only after reboot - post-reboot Status compare is required.')
    [void]$lines.Add('')

    if ($TcpTrace) {
        [void]$lines.Add('=== AcerService TCP (what we sent / what service answered) ===')
        [void]$lines.Add(('Ok={0} ElapsedMs={1} AesUsed={2}' -f $TcpTrace.Ok, $TcpTrace.ElapsedMs, $TcpTrace.AesUsed))
        [void]$lines.Add(('PacketId={0} Host={1}:{2}' -f $TcpTrace.PacketId, $TcpTrace.Host, $TcpTrace.Port))
        [void]$lines.Add(('JsonRequest={0}' -f $TcpTrace.JsonRequest))
        [void]$lines.Add(('RequestBytes={0}' -f $TcpTrace.RequestBytes))
        [void]$lines.Add(('RequestHex={0}' -f $TcpTrace.RequestHex))
        [void]$lines.Add(('ResponsePacketId={0} ResponseBytes={1}' -f $TcpTrace.ResponsePacketId, $TcpTrace.ResponseBytes))
        [void]$lines.Add(('ResponseHex={0}' -f $TcpTrace.ResponseHex))
        [void]$lines.Add(('ResponseText={0}' -f $TcpTrace.Response))
        if ($TcpTrace.Error) { [void]$lines.Add(('Error={0}' -f $TcpTrace.Error)) }
        [void]$lines.Add('')
    }

    [void]$lines.Add('=== BIOS display-mode byte (offset 80) ===')
    [void]$lines.Add(('BEFORE offset80={0} window={1}' -f $Before.BiosDisplayMode.Offset80, $Before.BiosDisplayMode.WindowHex))
    [void]$lines.Add(('AFTER  offset80={0} window={1}' -f $After.BiosDisplayMode.Offset80, $After.BiosDisplayMode.WindowHex))
    if ("$($Before.BiosDisplayMode.Offset80)" -ne "$($After.BiosDisplayMode.Offset80)") {
        [void]$lines.Add('BIOS_OFFSET80_CHANGED=True  << strong emulation candidate (SetBiosOptions)')
    } else {
        [void]$lines.Add('BIOS_OFFSET80_CHANGED=False')
    }
    [void]$lines.Add('')

    $sections = @(
        @{ Title = 'Acer misc WMI (GetGamingMiscSetting)'; B = $Before.AcerMisc; A = $After.AcerMisc },
        @{ Title = 'AcerAgent AdvanceSettings (GPU caps)'; B = $Before.AcerAgentCaps; A = $After.AcerAgentCaps },
        @{ Title = 'Registry (Acer + NVIDIA + display class)'; B = $Before.Registry; A = $After.Registry },
        @{ Title = 'Display PnP devices'; B = $Before.DisplayPnp; A = $After.DisplayPnp },
        @{ Title = 'Acer-related services'; B = $Before.Services; A = $After.Services },
        @{ Title = 'Acer/Predator processes'; B = $Before.AcerProcesses; A = $After.AcerProcesses }
    )
    foreach ($sec in $sections) {
        [void]$lines.Add(("=== {0} ===" -f $sec.Title))
        $d = Compare-ObserveMaps -Before $sec.B -After $sec.A
        if ($d.Count -eq 0) {
            [void]$lines.Add('(no changes)')
        } else {
            [void]$lines.Add(('changes={0}' -f $d.Count))
            foreach ($c in @($d.Items)) {
                [void]$lines.Add($c)
                [void]$lines.Add(' ')
            }
        }
        [void]$lines.Add('')
    }

    [void]$lines.Add('=== Emulation checklist (fill after reading DIFF) ===')
    [void]$lines.Add('[ ] Reproduce TCP JSON locally? (only if another local endpoint exists)')
    [void]$lines.Add('[ ] Call AcerBiosConfigurationTool SetBiosOptions Data[80]=1/2/3')
    [void]$lines.Add('[ ] Call AcerGamingFunction SetGamingMiscSetting for any CHANGED misc_* IDs')
    [void]$lines.Add('[ ] Apply registry values that changed under SOFTWARE\Acer or NVIDIA')
    [void]$lines.Add('[ ] Enable/Disable NVIDIA PnP like PreySense Endurance/Standard')
    [void]$lines.Add('[ ] Require reboot? (Ultimate usually yes)')
    [void]$lines.Add('')
    [void]$lines.Add('If DIFF is empty before reboot: reboot, then run Status and compare with before.json manually.')

    $text = ($lines -join "`r`n")
    Set-Content -LiteralPath $DiffPath -Value $text -Encoding UTF8
    Write-GpuLog $LogPath ("DIFF written: {0}" -f $DiffPath) Green
    return $DiffPath
}

function Write-AcerEmulateStub {
    param(
        [string]$Path,
        [string]$Action,
        $TcpTrace
    )
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('# Emulation notes (auto stub)')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine(('Action: {0}' -f $Action))
    [void]$sb.AppendLine('Source protocol: PreySense AcerServiceClient.SetGpuMode')
    [void]$sb.AppendLine('https://github.com/hammadzaigham/PreySense')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## What we send to AcerService')
    if ($TcpTrace) {
        [void]$sb.AppendLine('```')
        [void]$sb.AppendLine($TcpTrace.JsonRequest)
        [void]$sb.AppendLine('```')
        [void]$sb.AppendLine(('PacketId SET_DEVICE_DATA = {0}' -f $TcpTrace.PacketId))
        [void]$sb.AppendLine(('Service result Ok = {0}' -f $TcpTrace.Ok))
    } else {
        [void]$sb.AppendLine('(no TCP trace - service unavailable or action was PnP-only)')
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Next')
    [void]$sb.AppendLine('1. Open DIFF.txt')
    [void]$sb.AppendLine('2. List every CHANGED key that survives reboot')
    [void]$sb.AppendLine('3. Implement the same writes in AcerPredatorTool (WMI/BIOS/PnP), not via AcerServiceSvc')
    Set-Content -LiteralPath $Path -Value $sb.ToString() -Encoding UTF8
}

function Invoke-AcerServiceObservedGpuMode {
    <#
    .SYNOPSIS
      Start AcerService if needed, snapshot, send GPU_MODE, snapshot, write DIFF session.
    #>
    param(
        [Parameter(Mandatory)][ValidateSet(1, 2)][int]$MuxMode,
        [Parameter(Mandatory)][string]$ActionLabel,
        [Parameter(Mandatory)][string]$LogPath,
        [switch]$StartService,
        [switch]$SkipServiceStartSnapshot
    )

    $sessionDir = New-AcerObserveSessionDir -Prefix 'acer_service_capture'
    Write-GpuLog $LogPath ("OBSERVE session: {0}" -f $sessionDir) White

    $tcpPath = Join-Path $sessionDir 'tcp_gpu_mode.json'
    $beforePath = Join-Path $sessionDir 'before.json'
    $afterPath = Join-Path $sessionDir 'after.json'
    $diffPath = Join-Path $sessionDir 'DIFF.txt'
    $emulatePath = Join-Path $sessionDir 'EMULATE.md'
    $metaPath = Join-Path $sessionDir 'meta.json'

    $svcInfo = Get-AcerServiceSvcInfo
    if (($StartService -or -not $svcInfo.PortOpen) -and -not $SkipServiceStartSnapshot) {
        if (-not $svcInfo.PortOpen) {
            $preSvcPath = Join-Path $sessionDir 'before_service.json'
            $preSvc = Get-AcerObserveSnapshot -Label 'BEFORE_SERVICE' -LogPath $LogPath
            Save-AcerObserveJson $preSvc $preSvcPath

            Write-GpuLog $LogPath 'Starting AcerServiceSvc for observation...' Yellow
            $start = Start-AcerServiceForProbe
            Write-GpuLog $LogPath ("StartService: Ok={0} {1}" -f $start.Ok, $start.Detail) $(if ($start.Ok) { 'Green' } else { 'Yellow' })

            $postSvc = Get-AcerObserveSnapshot -Label 'AFTER_SERVICE' -LogPath $LogPath
            Save-AcerObserveJson $postSvc (Join-Path $sessionDir 'after_service.json')
            Write-AcerObserveDiff -Before $preSvc -After $postSvc -TcpTrace $null `
                -Action 'Start AcerServiceSvc' `
                -DiffPath (Join-Path $sessionDir 'DIFF_service_start.txt') `
                -LogPath $LogPath | Out-Null
        }
    } elseif ($StartService) {
        $start = Start-AcerServiceForProbe
        Write-GpuLog $LogPath ("StartService: Ok={0} {1}" -f $start.Ok, $start.Detail)
    }

    $before = Get-AcerObserveSnapshot -Label 'BEFORE_GPU_MODE' -LogPath $LogPath
    Save-AcerObserveJson $before $beforePath

    $tcp = $null
    $biosFallback = $null
    $ok = $false

    $proto = Resolve-AcerServiceCommandPort
    Save-AcerObserveJson $proto (Join-Path $sessionDir 'tcp_port_probe.json')
    Write-GpuLog $LogPath ("TCP port probe: Ok={0} Port={1} Listen=[{2}]" -f `
        $proto.Ok, $proto.Port, ($proto.ListenPorts -join ',')) Cyan
    foreach ($n in @($proto.Notes)) { Write-GpuLog $LogPath ("  {0}" -f $n) }

    if ($proto.Ok) {
        # Also try querying GPU_MODE even if PreySense does not (may return useful state).
        $queryJson = '{"Function":"GPU_MODE"}'
        $query = Invoke-AcerServiceCommand -PacketId $script:AcerServiceGetPacket -Json $queryJson
        Save-AcerObserveJson $query (Join-Path $sessionDir 'tcp_query_gpu_mode.json')
        Write-GpuLog $LogPath ("GET GPU_MODE Ok={0} Resp={1}" -f $query.Ok, $query.Response) Cyan

        Write-GpuLog $LogPath ("SEND SetDeviceData GPU_MODE mode={0}" -f $MuxMode) Yellow
        $tcp = Set-AcerServiceGpuMode -MuxMode $MuxMode
        Save-AcerObserveJson $tcp $tcpPath
        Write-GpuLog $LogPath ("TCP Ok={0} ElapsedMs={1}" -f $tcp.Ok, $tcp.ElapsedMs) $(if ($tcp.Ok) { 'Green' } else { 'Yellow' })
        Write-GpuLog $LogPath ("  JSON: {0}" -f $tcp.JsonRequest)
        Write-GpuLog $LogPath ("  REQ:  {0}" -f $tcp.RequestHex)
        Write-GpuLog $LogPath ("  RSP:  {0}" -f $tcp.ResponseHex)
        if ($tcp.Response) { Write-GpuLog $LogPath ("  TEXT: {0}" -f $tcp.Response) }
        if ($tcp.Error) { Write-GpuLog $LogPath ("  ERR:  {0}" -f $tcp.Error) Red }
        $ok = [bool]$tcp.Ok
    } else {
        Write-GpuLog $LogPath 'PreySense TCP protocol not present - skipping GPU_MODE send (expected on PHN18).' Yellow
    }

    if (-not $ok) {
        Write-GpuLog $LogPath 'AcerService SET failed/unavailable - BIOS fallback will also be observed if used by caller.' Yellow
    }

    Start-Sleep -Seconds 2
    $after = Get-AcerObserveSnapshot -Label 'AFTER_GPU_MODE' -LogPath $LogPath
    Save-AcerObserveJson $after $afterPath

    Write-AcerObserveDiff -Before $before -After $after -TcpTrace $tcp `
        -Action $ActionLabel -DiffPath $diffPath -LogPath $LogPath | Out-Null
    Write-AcerEmulateStub -Path $emulatePath -Action $ActionLabel -TcpTrace $tcp

    $meta = [pscustomobject]@{
        Action           = $ActionLabel
        MuxMode          = $MuxMode
        SessionDir       = $sessionDir
        TcpOk            = $(if ($tcp) { $tcp.Ok } else { $false })
        MuxChanged       = ($before.MuxSignature -ne $after.MuxSignature)
        BiosOffset80Before = $before.BiosDisplayMode.Offset80
        BiosOffset80After  = $after.BiosDisplayMode.Offset80
        Created          = (Get-Date).ToString('o')
    }
    Save-AcerObserveJson $meta $metaPath

    Write-GpuLog $LogPath ("OBSERVE done. Open DIFF.txt in: {0}" -f $sessionDir) Green
    return [pscustomobject]@{
        SessionDir = $sessionDir
        DiffPath   = $diffPath
        Tcp        = $tcp
        Ok         = $ok
        Before     = $before
        After      = $after
        BiosFallback = $biosFallback
    }
}
