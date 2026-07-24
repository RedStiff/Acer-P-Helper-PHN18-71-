# AcerService TCP helpers (PreySense protocol).
# Dot-source from probe scripts. Port 46933, packets: ACER + u32 LE id + JSON/AES payload.
# Reference: https://github.com/hammadzaigham/PreySense (docs/acer_wmi_documentation.md)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$script:AcerServiceHost = '127.0.0.1'
# PreySense docs use 46933; PHN18 AcerService often listens on 15152 (HTTPS PWA, NOT this protocol).
$script:AcerServiceCommandPort = 46933
$script:AcerServiceInitPacket = [uint32]0
$script:AcerServiceGetPacket = [uint32]20
$script:AcerServiceSetPacket = [uint32]100
$script:AcerServiceProtocolReady = $false
$script:AcerBiosOptionsUnsupported = $false
$script:AcerBiosOptionsLastError = $null

# PreySense AcerWmi.GpuMux
$script:AcerGpuMuxDiscrete = 1  # Ultimate / dGPU exclusive (reboot)
$script:AcerGpuMuxHybrid = 2    # Endurance/Standard hybrid mux

function Get-ObjectPropValue {
    param(
        $Obj,
        [Parameter(Mandatory)][string[]]$Names
    )
    if ($null -eq $Obj) { return $null }

    foreach ($name in $Names) {
        $prop = $Obj.PSObject.Properties[$name]
        if ($null -ne $prop) { return $prop.Value }

        if ($Obj -is [System.Management.ManagementBaseObject]) {
            try {
                $mp = $Obj.Properties[$name]
                if ($null -ne $mp) { return $mp.Value }
            } catch { }
        }
    }
    return $null
}

function Get-AcerServiceAesKeyBytes {
    try {
        $key = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Acer\XSense' -ErrorAction SilentlyContinue).AESkey
        if ($key -is [string] -and $key.Length -eq 32) {
            return [Text.Encoding]::ASCII.GetBytes($key)
        }
    } catch { }
    return $null
}

function Protect-AcerServicePayload {
    param(
        [Parameter(Mandatory)][string]$Json,
        [byte[]]$AesKey
    )
    $plain = [Text.Encoding]::UTF8.GetBytes($Json)
    if (-not $AesKey) { return $plain }

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.Key = $AesKey
        $aes.Mode = [System.Security.Cryptography.CipherMode]::ECB
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $enc = $aes.CreateEncryptor()
        try {
            return $enc.TransformFinalBlock($plain, 0, $plain.Length)
        } finally {
            $enc.Dispose()
        }
    } finally {
        $aes.Dispose()
    }
}

function Unprotect-AcerServicePayload {
    param(
        [Parameter(Mandatory)][byte[]]$Payload,
        [byte[]]$AesKey
    )
    if (-not $AesKey) {
        return [Text.Encoding]::UTF8.GetString($Payload)
    }

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.Key = $AesKey
        $aes.Mode = [System.Security.Cryptography.CipherMode]::ECB
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $dec = $aes.CreateDecryptor()
        try {
            $plain = $dec.TransformFinalBlock($Payload, 0, $Payload.Length)
            return [Text.Encoding]::UTF8.GetString($plain)
        } finally {
            $dec.Dispose()
        }
    } finally {
        $aes.Dispose()
    }
}

function New-AcerServicePacket {
    param(
        [Parameter(Mandatory)][uint32]$PacketId,
        [Parameter(Mandatory)][string]$Json,
        [byte[]]$AesKey
    )
    $payload = Protect-AcerServicePayload -Json $Json -AesKey $AesKey
    $packet = New-Object byte[] (8 + $payload.Length)
    [Text.Encoding]::ASCII.GetBytes('ACER').CopyTo($packet, 0)
    [BitConverter]::GetBytes($PacketId).CopyTo($packet, 4)
    $payload.CopyTo($packet, 8)
    return $packet
}

function Test-TcpPortOpen {
    param(
        [Parameter(Mandatory)][int]$Port,
        [int]$TimeoutMs = 200
    )
    try {
        $client = [Net.Sockets.TcpClient]::new()
        $ar = $client.BeginConnect($script:AcerServiceHost, $Port, $null, $null)
        $ok = $ar.AsyncWaitHandle.WaitOne($TimeoutMs)
        if ($ok) {
            try { $client.EndConnect($ar) } catch { $ok = $false }
        }
        $client.Dispose()
        return [bool]$ok
    } catch {
        return $false
    }
}

function Test-AcerServicePortOpen {
    param([int]$TimeoutMs = 200)
    return (Test-TcpPortOpen -Port $script:AcerServiceCommandPort -TimeoutMs $TimeoutMs)
}

function Get-AcerProcessListenPorts {
    $names = @('AcerService', 'AcerServiceWrapper', 'AcerLightingService', 'AcerAgentService', 'AcerCentralService')
    $pids = @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $names -contains $_.ProcessName } |
        Select-Object -ExpandProperty Id)
    if ($pids.Count -eq 0) { return @() }

    @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $pids -contains $_.OwningProcess } |
        Select-Object -ExpandProperty LocalPort -Unique |
        Sort-Object)
}

function Test-PreySenseProtocolOnPort {
    <#
    .SYNOPSIS
      Send ACER VERSION packet; require a non-empty response (PreySense command protocol).
      Port 15152 on PHN18 accepts TCP but answers 0 bytes (HTTPS PWA) — treated as NOT ready.
    #>
    param([Parameter(Mandatory)][int]$Port)

    try {
        $json = '{"Function":"VERSION"}'
        $aes = Get-AcerServiceAesKeyBytes
        $packet = New-AcerServicePacket -PacketId $script:AcerServiceInitPacket -Json $json -AesKey $aes
        $client = [Net.Sockets.TcpClient]::new()
        $client.NoDelay = $true
        $client.ReceiveTimeout = 1500
        $client.SendTimeout = 1500
        $client.Connect($script:AcerServiceHost, $Port)
        $stream = $client.GetStream()
        $stream.Write($packet, 0, $packet.Length)
        $deadline = (Get-Date).AddMilliseconds(1500)
        $buf = New-Object byte[] 8192
        $total = 0
        while ((Get-Date) -lt $deadline -and $total -eq 0) {
            if ($stream.DataAvailable -or $client.Available -gt 0) {
                $n = $stream.Read($buf, $total, $buf.Length - $total)
                if ($n -le 0) { break }
                $total += $n
            } else {
                Start-Sleep -Milliseconds 50
            }
        }
        $client.Dispose()
        if ($total -le 0) {
            return [pscustomobject]@{ Ok = $false; Port = $Port; Detail = 'connected_but_no_response (likely not PreySense protocol)' }
        }
        return [pscustomobject]@{
            Ok = $true; Port = $Port; Detail = ("response_bytes={0}" -f $total)
        }
    } catch {
        return [pscustomobject]@{ Ok = $false; Port = $Port; Detail = $_.Exception.Message }
    }
}

function Resolve-AcerServiceCommandPort {
    $candidates = New-Object System.Collections.Generic.List[int]
    foreach ($p in @(46933, 46753)) { if (-not $candidates.Contains($p)) { [void]$candidates.Add($p) } }
    foreach ($p in @(Get-AcerProcessListenPorts)) {
        if (-not $candidates.Contains([int]$p)) { [void]$candidates.Add([int]$p) }
    }

    $notes = New-Object System.Collections.Generic.List[string]
    foreach ($port in $candidates) {
        if (-not (Test-TcpPortOpen -Port $port -TimeoutMs 150)) {
            [void]$notes.Add(("port {0}: closed" -f $port))
            continue
        }
        $probe = Test-PreySenseProtocolOnPort -Port $port
        [void]$notes.Add(("port {0}: {1}" -f $port, $probe.Detail))
        if ($probe.Ok) {
            $script:AcerServiceCommandPort = $port
            $script:AcerServiceProtocolReady = $true
            return [pscustomobject]@{
                Ok = $true; Port = $port; ListenPorts = @($candidates.ToArray()); Notes = @($notes.ToArray())
            }
        }
    }

    $script:AcerServiceProtocolReady = $false
    return [pscustomobject]@{
        Ok = $false; Port = $null; ListenPorts = @(Get-AcerProcessListenPorts); Notes = @($notes.ToArray())
    }
}

function Get-AcerServiceSvcInfo {
    param([switch]$ProbeProtocol)

    $svc = Get-Service -Name 'AcerServiceSvc' -ErrorAction SilentlyContinue
    $listen = @(Get-AcerProcessListenPorts)
    $resolve = $null
    if ($ProbeProtocol) {
        $resolve = Resolve-AcerServiceCommandPort
        $listen = @($resolve.ListenPorts)
    }

    if (-not $svc) {
        return [pscustomobject]@{
            Present       = $false
            Status        = $null
            StartType     = $null
            PortOpen      = $false
            CommandPort   = $script:AcerServiceCommandPort
            ProtocolReady = [bool]$script:AcerServiceProtocolReady
            ListenPorts   = $listen
            Notes         = $(if ($resolve) { @($resolve.Notes) } else { @() })
        }
    }
    return [pscustomobject]@{
        Present       = $true
        Status        = "$($svc.Status)"
        StartType     = "$($svc.StartType)"
        PortOpen      = $(if ($resolve) { [bool]$resolve.Ok } else { [bool]$script:AcerServiceProtocolReady })
        CommandPort   = $(if ($resolve -and $resolve.Port) { $resolve.Port } else { $script:AcerServiceCommandPort })
        ProtocolReady = $(if ($resolve) { [bool]$resolve.Ok } else { [bool]$script:AcerServiceProtocolReady })
        ListenPorts   = $listen
        Notes         = $(if ($resolve) { @($resolve.Notes) } else { @() })
    }
}

function Start-AcerServiceForProbe {
    <#
    .SYNOPSIS
      Best-effort enable+start AcerServiceSvc (+ Lighting) and detect PreySense TCP protocol.
    #>
    param([switch]$Persist)

    $svc = Get-Service -Name 'AcerServiceSvc' -ErrorAction SilentlyContinue
    if (-not $svc) {
        return [pscustomobject]@{ Ok = $false; Detail = 'AcerServiceSvc not installed' }
    }

    try {
        foreach ($name in @('AcerServiceSvc', 'AcerLightingService')) {
            $s = Get-Service -Name $name -ErrorAction SilentlyContinue
            if (-not $s) { continue }
            try {
                if ($Persist -or $s.StartType -eq 'Disabled') {
                    Set-Service -Name $name -StartupType Manual -ErrorAction SilentlyContinue
                }
                if ($s.Status -ne 'Running') {
                    Start-Service -Name $name -ErrorAction SilentlyContinue
                }
            } catch { }
        }

        Start-Sleep -Seconds 1
        $resolve = Resolve-AcerServiceCommandPort
        $running = (Get-Service AcerServiceSvc).Status -eq 'Running'
        $lighting = Get-Service AcerLightingService -ErrorAction SilentlyContinue
        $detail = "AcerServiceSvc=$running; Lighting=$($lighting.Status); ProtocolReady=$($resolve.Ok); CommandPort=$($resolve.Port); Listen=[$($resolve.ListenPorts -join ',')]; Notes=$($resolve.Notes -join ' | ')"
        return [pscustomobject]@{
            Ok     = ($running -and $resolve.Ok)
            Detail = $detail
            Resolve = $resolve
        }
    } catch {
        return [pscustomobject]@{ Ok = $false; Detail = $_.Exception.Message }
    }
}

function Initialize-AcerBiosNativeHelper {
    if ('AcerBiosNative' -as [type]) { return }
    Add-Type -ReferencedAssemblies @('System.Management') -TypeDefinition @'
using System;
using System.Linq;
using System.Management;

public static class AcerBiosNative
{
    public class Result
    {
        public bool Ok;
        public int Ret;
        public byte[] Data;
        public string Error;
        public int Before;
        public int After;
    }

    private static ManagementObject GetBiosObject()
    {
        using (var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerBiosConfigurationTool"))
        using (var collection = searcher.Get())
        {
            return collection.Cast<ManagementObject>().FirstOrDefault();
        }
    }

    private static int ReadRet(ManagementBaseObject outParams)
    {
        if (outParams == null) return -1;
        if (outParams.Properties["ReturnCode"] != null && outParams["ReturnCode"] != null)
            return Convert.ToInt32(outParams["ReturnCode"]);
        if (outParams.Properties["ReturnValue"] != null && outParams["ReturnValue"] != null)
            return Convert.ToInt32(outParams["ReturnValue"]);
        return 0;
    }

    private static Result TryGetOptions(ManagementObject bios, ushort passwordLen, byte[] password, string tag)
    {
        var r = new Result();
        try
        {
            var inParams = bios.GetMethodParameters("GetBiosOptions");
            inParams["PasswordLen"] = passwordLen;
            inParams["Password"] = password ?? Array.Empty<byte>();
            using (var outParams = bios.InvokeMethod("GetBiosOptions", inParams, null))
            {
                r.Ret = ReadRet(outParams);
                if (r.Ret != 0)
                {
                    r.Error = tag + ": GetBiosOptions ret=" + r.Ret;
                    return r;
                }
                r.Data = outParams["Data"] as byte[];
                if (r.Data == null || r.Data.Length == 0)
                {
                    r.Error = tag + ": GetBiosOptions Data missing/empty";
                    return r;
                }
                r.Ok = true;
                r.Error = tag;
                return r;
            }
        }
        catch (ManagementException mex)
        {
            r.Error = tag + ": " + mex.Message + " (" + mex.ErrorCode + ")";
            return r;
        }
        catch (Exception ex)
        {
            r.Error = tag + ": " + ex.Message;
            return r;
        }
    }

    public static Result GetOptions()
    {
        var r = new Result();
        try
        {
            var bios = GetBiosObject();
            if (bios == null)
            {
                r.Error = "AcerBiosConfigurationTool missing";
                return r;
            }
            using (bios)
            {
                ushort[] lens = { 0, 128, 0, 0, 64 };
                byte[][] pws = {
                    new byte[128],
                    new byte[128],
                    Array.Empty<byte>(),
                    new byte[64],
                    new byte[64]
                };
                string[] tags = { "len0_pw128", "len128_pw128", "len0_pwEmpty", "len0_pw64", "len64_pw64" };

                var errors = new System.Text.StringBuilder();
                for (int i = 0; i < tags.Length; i++)
                {
                    var trial = TryGetOptions(bios, lens[i], pws[i], tags[i]);
                    if (trial.Ok) return trial;
                    if (errors.Length > 0) errors.Append(" | ");
                    errors.Append(trial.Error);
                }

                r.Error = errors.ToString();
                return r;
            }
        }
        catch (Exception ex)
        {
            r.Error = ex.Message;
            return r;
        }
    }

    public static Result SetDisplayModeOffset80(byte value)
    {
        var r = new Result();
        try
        {
            var got = GetOptions();
            if (!got.Ok)
            {
                r.Error = got.Error;
                r.Ret = got.Ret;
                return r;
            }
            if (got.Data.Length <= 80)
            {
                r.Error = "BIOS data buffer too small";
                return r;
            }

            r.Before = got.Data[80];
            var data = (byte[])got.Data.Clone();
            data[80] = value;
            r.After = value;

            var bios = GetBiosObject();
            if (bios == null)
            {
                r.Error = "AcerBiosConfigurationTool missing";
                return r;
            }
            using (bios)
            {
                var inParams = bios.GetMethodParameters("SetBiosOptions");
                inParams["PasswordLen"] = (ushort)0;
                inParams["Password"] = new byte[128];
                inParams["Data"] = data;
                using (var outParams = bios.InvokeMethod("SetBiosOptions", inParams, null))
                {
                    r.Ret = ReadRet(outParams);
                    r.Ok = (r.Ret == 0 || r.Ret == 8);
                    r.Data = data;
                    if (!r.Ok)
                        r.Error = "SetBiosOptions ret=" + r.Ret;
                    return r;
                }
            }
        }
        catch (Exception ex)
        {
            r.Error = ex.Message;
            return r;
        }
    }
}
'@
}

function Get-BiosOptionsData {
    <#
    .SYNOPSIS
      Read AcerBiosConfigurationTool buffer via native System.Management helper.
    #>
    if ($script:AcerBiosOptionsUnsupported) {
        return [pscustomobject]@{
            Ok    = $false
            Data  = $null
            Ret   = $null
            Error = $script:AcerBiosOptionsLastError
        }
    }

    Initialize-AcerBiosNativeHelper
    $native = [AcerBiosNative]::GetOptions()
    if (-not $native.Ok) {
        # PHN18 often exposes the class but rejects GetBiosOptions (InvalidParameter).
        if ("$($native.Error)" -match 'InvalidParameter|Invalid parameter') {
            $script:AcerBiosOptionsUnsupported = $true
            $script:AcerBiosOptionsLastError = 'AcerBiosConfigurationTool.GetBiosOptions unsupported on this machine (InvalidParameter)'
            return [pscustomobject]@{
                Ok = $false; Data = $null; Ret = $native.Ret; Error = $script:AcerBiosOptionsLastError
            }
        }
    }
    return [pscustomobject]@{
        Ok    = [bool]$native.Ok
        Data  = $native.Data
        Ret   = $native.Ret
        Error = $native.Error
    }
}

function ConvertTo-HexDump {
    param(
        [byte[]]$Bytes,
        [int]$MaxBytes = 256
    )
    if (-not $Bytes -or $Bytes.Length -eq 0) { return '' }
    $n = [Math]::Min($Bytes.Length, $MaxBytes)
    $slice = New-Object byte[] $n
    [Array]::Copy($Bytes, 0, $slice, 0, $n)
    $hex = ($slice | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
    if ($Bytes.Length -gt $MaxBytes) {
        $hex += (' ... (+{0} bytes)' -f ($Bytes.Length - $MaxBytes))
    }
    return $hex
}

function Invoke-AcerServiceCommand {
    param(
        [Parameter(Mandatory)][uint32]$PacketId,
        [Parameter(Mandatory)][string]$Json,
        [int]$TimeoutMs = 2000
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $aes = Get-AcerServiceAesKeyBytes
    $packet = New-AcerServicePacket -PacketId $PacketId -Json $Json -AesKey $aes
    $base = [pscustomobject]@{
        Ok              = $false
        Response        = $null
        Error           = $null
        AesUsed         = [bool]$aes
        PacketId        = [uint32]$PacketId
        JsonRequest     = $Json
        RequestBytes    = $packet.Length
        RequestHex      = (ConvertTo-HexDump -Bytes $packet -MaxBytes 128)
        ResponseBytes   = 0
        ResponseHex     = ''
        ResponsePacketId = $null
        ElapsedMs       = 0
        Host            = $script:AcerServiceHost
        Port            = $script:AcerServiceCommandPort
    }

    try {
        $client = [Net.Sockets.TcpClient]::new()
        $client.SendTimeout = $TimeoutMs
        $client.ReceiveTimeout = $TimeoutMs
        $client.Connect($script:AcerServiceHost, $script:AcerServiceCommandPort)
        $stream = $client.GetStream()
        $stream.Write($packet, 0, $packet.Length)

        $buf = New-Object byte[] 16384
        $read = $stream.Read($buf, 0, $buf.Length)
        $client.Dispose()
        $sw.Stop()
        $base.ElapsedMs = [int]$sw.ElapsedMilliseconds

        if ($read -le 0) {
            $base.Error = 'empty_response'
            return $base
        }

        $raw = New-Object byte[] $read
        [Array]::Copy($buf, 0, $raw, 0, $read)
        $base.ResponseBytes = $read
        $base.ResponseHex = ConvertTo-HexDump -Bytes $raw -MaxBytes 128

        $jsonStart = 0
        if ($read -ge 8 -and $buf[0] -eq 0x41 -and $buf[1] -eq 0x43 -and $buf[2] -eq 0x45 -and $buf[3] -eq 0x52) {
            $jsonStart = 8
            $base.ResponsePacketId = [BitConverter]::ToUInt32($buf, 4)
        }

        $payload = New-Object byte[] ($read - $jsonStart)
        [Array]::Copy($buf, $jsonStart, $payload, 0, $payload.Length)
        $text = Unprotect-AcerServicePayload -Payload $payload -AesKey $aes

        $success = $text -match '"result"' -and ($text -match '"0"' -or $text -match ':\s*0')
        $base.Ok = [bool]$success
        $base.Response = $text
        # VERSION handshake may not include "result":0 — still return body.
        if (-not $success -and $PacketId -eq $script:AcerServiceInitPacket -and $text) {
            $base.Ok = $true
        }
        return $base
    } catch {
        $sw.Stop()
        $base.ElapsedMs = [int]$sw.ElapsedMilliseconds
        $base.Error = $_.Exception.Message
        return $base
    }
}

function Invoke-AcerServiceHandshake {
    return Invoke-AcerServiceCommand -PacketId $script:AcerServiceInitPacket -Json '{"Function":"VERSION"}'
}

function Set-AcerServiceGpuMode {
    param(
        [Parameter(Mandatory)][ValidateSet(1, 2)][int]$MuxMode
    )
    $json = "{{`"Function`":`"GPU_MODE`",`"Parameter`":{{`"mode`":{0}}}}}" -f $MuxMode
    return Invoke-AcerServiceCommand -PacketId $script:AcerServiceSetPacket -Json $json
}

function Get-NvidiaDisplayDevices {
    Get-PnpDevice -Class Display -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match 'VEN_10DE' } |
        ForEach-Object {
            [pscustomobject]@{
                Status       = $_.Status
                FriendlyName = $_.FriendlyName
                InstanceId   = $_.InstanceId
                Problem      = $_.Problem
            }
        }
}

function Set-NvidiaDisplayDeviceState {
    param(
        [Parameter(Mandatory)][bool]$Enable
    )
    $devices = @(Get-NvidiaDisplayDevices)
    if ($devices.Count -eq 0) {
        return [pscustomobject]@{ Ok = $false; Detail = 'No NVIDIA display PnP device found'; Changed = @() }
    }

    $changed = @()
    foreach ($d in $devices) {
        try {
            if ($Enable) {
                Enable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction Stop
            } else {
                Disable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction Stop
            }
            $changed += $d.InstanceId
        } catch {
            return [pscustomobject]@{
                Ok = $false; Detail = $_.Exception.Message; Changed = $changed; FailedId = $d.InstanceId
            }
        }
    }
    return [pscustomobject]@{
        Ok = $true; Detail = ("{0} device(s)" -f $changed.Count); Changed = $changed
    }
}

function Set-NvidiaContainerService {
    param([Parameter(Mandatory)][ValidateSet('Stop', 'Restart')][string]$Action)
    $name = 'NVDisplay.ContainerLocalSystem'
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if (-not $svc) {
        return [pscustomobject]@{ Ok = $false; Detail = "$name not found" }
    }
    try {
        if ($Action -eq 'Stop') {
            Stop-Service -Name $name -Force -ErrorAction Stop
        } else {
            Restart-Service -Name $name -Force -ErrorAction Stop
        }
        return [pscustomobject]@{ Ok = $true; Detail = "$((Get-Service $name).Status)" }
    } catch {
        return [pscustomobject]@{ Ok = $false; Detail = $_.Exception.Message }
    }
}

function Set-GpuMuxBiosOffset80 {
    <#
    .SYNOPSIS
      PreySense WMI fallback: AcerBiosConfigurationTool Data[80]
      1 = Auto Select, 2 = Optimus, 3 = dGPU (maps from mux 1->3, mux 2->1).
    #>
    param(
        [Parameter(Mandatory)][ValidateSet(1, 2)][int]$MuxMode
    )

    $biosVal = if ($MuxMode -eq 1) { [byte]3 } else { [byte]1 }

    if ($script:AcerBiosOptionsUnsupported) {
        return [pscustomobject]@{
            Ok = $false
            Detail = $script:AcerBiosOptionsLastError
            BiosValue = $biosVal
            Before = $null
        }
    }

    try {
        Initialize-AcerBiosNativeHelper
        $native = [AcerBiosNative]::SetDisplayModeOffset80([byte]$biosVal)
        if (-not $native.Ok -and ("$($native.Error)" -match 'InvalidParameter|Invalid parameter')) {
            $script:AcerBiosOptionsUnsupported = $true
            $script:AcerBiosOptionsLastError = 'AcerBiosConfigurationTool.SetBiosOptions unsupported on this machine (InvalidParameter)'
        }
        $detail = if ($native.Ok) {
            "SetBiosOptions ret=$($native.Ret) before=$($native.Before) after=$($native.After)"
        } else {
            $(if ($script:AcerBiosOptionsLastError) { $script:AcerBiosOptionsLastError } else { $native.Error })
        }
        return [pscustomobject]@{
            Ok        = [bool]$native.Ok
            Detail    = $detail
            BiosValue = $biosVal
            Before    = $native.Before
        }
    } catch {
        return [pscustomobject]@{ Ok = $false; Detail = $_.Exception.Message; BiosValue = $biosVal }
    }
}
