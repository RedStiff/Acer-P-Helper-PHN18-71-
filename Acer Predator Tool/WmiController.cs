using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class WmiController : IDisposable
    {
        private ManagementObject? _cachedObj;
        private readonly object _lock = new();

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

        private static readonly Guid OVERLAY_EFFICIENCY = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
        private static readonly Guid OVERLAY_BALANCED = new("00000000-0000-0000-0000-000000000000");
        private static readonly Guid OVERLAY_PERFORMANCE = new("ded574b5-45a0-4f42-8737-46345c09c238");

        private byte _lastR = 0, _lastG = 150, _lastB = 255;
        private byte _brightness = 100;
        private byte _speed = 5;       
        private byte _direction = 0;   
        private int _lastMode = 3;
        private bool _fourZoneStatic;
        private readonly byte[] _zoneR = new byte[4];
        private readonly byte[] _zoneG = new byte[4];
        private readonly byte[] _zoneB = new byte[4];

        private const ulong KeyboardLedBehaviorWake = 0x07ul;
        private const ulong KeyboardAllZones = 8ul | (0x0Ful << 40);

        public byte LastR => _lastR;
        public byte LastG => _lastG;
        public byte LastB => _lastB;
        public byte Brightness => _brightness;
        public byte Speed => _speed;
        public byte Direction => _direction;
        public int LastRgbMode => _lastMode;

        private ManagementObject? GetWmiObject()
        {
            lock (_lock)
            {
                if (_cachedObj != null) return _cachedObj;
                try
                {
                    using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGamingFunction");
                    _cachedObj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                }
                catch { _cachedObj = null; }
                return _cachedObj;
            }
        }

        private void InvalidateCache()
        {
            lock (_lock) { _cachedObj = null; }
        }

        private (bool success, ulong output) SendCommand(string method, ulong input)
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return (false, 0);

                using var inParams = obj.GetMethodParameters(method);
                inParams["gmInput"] = input;
                using var outParams = obj.InvokeMethod(method, inParams, null);
                ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                return ((result & 0xFF) == 0, result);
            }
            catch
            {
                InvalidateCache();
                return (false, 0);
            }
        }

        /// <summary>
        /// MOF UInt8Array methods (SetGamingLED, SetGamingKBBacklight). Passing ulong fails under CIM.
        /// </summary>
        private (bool success, ulong output) SendByteArrayCommand(string method, byte[] input)
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return (false, 0);

                using var inParams = obj.GetMethodParameters(method);
                inParams["gmInput"] = input;
                using var outParams = obj.InvokeMethod(method, inParams, null);
                object raw = outParams["gmOutput"];
                ulong result = raw switch
                {
                    byte[] bytes when bytes.Length >= 8 => BitConverter.ToUInt64(bytes, 0),
                    byte[] bytes when bytes.Length > 0 => bytes[0],
                    _ => Convert.ToUInt64(raw)
                };
                return ((result & 0xFF) == 0, result);
            }
            catch
            {
                InvalidateCache();
                return (false, 0);
            }
        }

        private static byte[] PackKeyboardAllZonesLed()
        {
            // MAX=16; value KeyboardAllZones = 8 | (0x0F << 40) little-endian in first 8 bytes.
            byte[] payload = new byte[16];
            BitConverter.GetBytes(KeyboardAllZones).CopyTo(payload, 0);
            return payload;
        }

        private bool SendLedCommand(byte[] payload)
        {
            var (ok, _) = SendByteArrayCommand("SetGamingKBBacklight", payload);
            return ok;
        }

        private int GetSensorReading(ulong sensorId)
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return 0;

                using var inParams = obj.GetMethodParameters("GetGamingSysInfo");
                inParams["gmInput"] = (ulong)(0x0001 | (sensorId << 8));
                using var outParams = obj.InvokeMethod("GetGamingSysInfo", inParams, null);
                var raw = (ulong)outParams["gmOutput"];
                if ((raw & 0xFF) == 0) return (int)((raw >> 8) & 0xFFFF);
            }
            catch { InvalidateCache(); }
            return 0;
        }

        public const byte GraphicsModeIntegrated = 0;
        public const byte GraphicsModeAuto = 1;
        public const byte GraphicsModeDiscrete = 2;

        public bool SupportsGraphicsMode { get; private set; }

        public void ProbeGraphicsModeSupport()
        {
            // PHN18: GPU device + DDS display mode via GpuControlService (not Acer WMI).
            SupportsGraphicsMode = true;
        }

        /// <summary>
        /// Panel owner from EnumDisplayDevices.
        /// Returns Integrated or Discrete; Auto vs Optimus needs ACE (see GpuControlService).
        /// </summary>
        public int GetGraphicsMode()
        {
            var owner = GraphicsMuxDetector.DetectPanelOwner();
            byte? mode = GraphicsMuxDetector.ToIndicatorMode(owner);
            return mode ?? -1;
        }

        /// <summary>Unused — GPU SET goes through GpuControlService.</summary>
        public bool SetGraphicsMode(byte mode)
        {
            _ = mode;
            return false;
        }

        public void SetPowerMode(byte mode)
        {
            SendCommand("SetGamingMiscSetting", (ulong)0x0B | ((ulong)mode << 8));
            SyncWindowsPowerMode(mode);
        }

        public void SetFanBehavior(byte mode)
        {
            const ushort fanBitmap = 0x01 | 0x08; // CPU bit 0, GPU bit 3
            ulong input = fanBitmap | ((ulong)mode << 16) | ((ulong)mode << 22);
            SendCommand("SetGamingFanBehavior", input);
        }

        public const byte FanIdCpu = 0x01;
        public const byte FanIdGpu = 0x04;

        public bool SupportsCustomFanSpeed { get; private set; }

        public void ProbeCustomFanSupport()
        {
            var (success, result) = SendCommand("GetGamingFanSpeed", FanIdCpu);
            SupportsCustomFanSpeed = success && (result & 0xFF) == 0;
        }

        public bool SetFanSpeed(byte fanId, byte percent)
        {
            // EC firmware maps Custom duty to ~10% bands; send the quantized value
            // so UI/curve logic and hardware stay aligned.
            percent = FanRpmMap.QuantizePercent(percent);
            ulong input = fanId | ((ulong)percent << 8);
            var (success, result) = SendCommand("SetGamingFanSpeed", input);
            return success && (result & 0xFF) == 0;
        }

        public int GetFanSpeed(byte fanId)
        {
            var (success, result) = SendCommand("GetGamingFanSpeed", fanId);
            if (!success || (result & 0xFF) != 0) return -1;
            return (int)((result >> 8) & 0xFF);
        }

        public bool SetCustomFanSpeeds(byte cpuPercent, byte gpuPercent)
        {
            SetFanBehavior(0x03);
            bool cpuOk = SetFanSpeed(FanIdCpu, cpuPercent);
            bool gpuOk = SetFanSpeed(FanIdGpu, gpuPercent);
            return cpuOk && gpuOk;
        }

        public int CpuFanRpm => GetSensorReading(0x02);
        public int GpuFanRpm => GetSensorReading(0x06);

        public void SetRgbMode(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction)
        {
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = brightness;
            _speed = speed;
            _direction = direction;
            _lastMode = mode;
            if (mode != 0)
                _fourZoneStatic = false;
            ApplyLightingMode(mode);
        }

        public void SetBrightness(byte brightness)
        {
            _brightness = brightness;
            ApplyLightingMode(_lastMode);
        }

        public void SetSpeed(byte speed)
        {
            _speed = speed;
            ApplyLightingMode(_lastMode);
        }

        public void SetDirection(byte direction)
        {
            _direction = direction;
            ApplyLightingMode(_lastMode);
        }

        public void SetStaticColor(byte r, byte g, byte b, byte brightness)
        {
            _fourZoneStatic = false;
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = brightness;
            _lastMode = 0;
            ApplyLightingMode(0);
        }

        public void ApplyKeyboardColorSettings(KeyboardColorSettings settings)
        {
            if (settings.FourZone)
            {
                ApplyStaticFourZoneColors(
                    settings.ZoneColors.Select(c => c.R).ToArray(),
                    settings.ZoneColors.Select(c => c.G).ToArray(),
                    settings.ZoneColors.Select(c => c.B).ToArray(),
                    settings.Brightness);
            }
            else
            {
                SetStaticColor(settings.SolidColor.R, settings.SolidColor.G, settings.SolidColor.B, settings.Brightness);
            }
        }

        public void ApplyStaticFourZoneColors(byte[] r, byte[] g, byte[] b, byte brightness)
        {
            if (r.Length < 4 || g.Length < 4 || b.Length < 4) return;

            _fourZoneStatic = true;
            _brightness = brightness;
            _lastMode = 0;
            for (int i = 0; i < 4; i++)
            {
                _zoneR[i] = r[i];
                _zoneG[i] = g[i];
                _zoneB[i] = b[i];
            }
            _lastR = r[0];
            _lastG = g[0];
            _lastB = b[0];

            PushStaticZoneColors(_zoneR, _zoneG, _zoneB);
        }

        private void ApplyLightingMode(int mode)
        {
            if (mode == 0)
            {
                if (_fourZoneStatic)
                    PushStaticZoneColors(_zoneR, _zoneG, _zoneB);
                else
                    ApplySolidStaticColor(_lastR, _lastG, _lastB);
                return;
            }

            BeginLightingUpdate();
            if (mode != 2 && mode != 3)
                SendZoneColor(0x0F, _lastR, _lastG, _lastB);

            SendStaticLedPayload((byte)mode);
        }

        /// <summary>
        /// Static on a cold EC (after crash / lost EC profile): two full WMI rounds only.
        /// No AcerLightingService / OpenRGB — same ACPI path as Linuwu-Sense on Linux.
        /// Short BeginLightingUpdate (0x07) is enough for effect modes but not static.
        /// </summary>
        private void ApplySolidStaticColor(byte r, byte g, byte b)
        {
            _lastR = r;
            _lastG = g;
            _lastB = b;
            RunStaticEcRecovery(() => WriteUniformZoneColors(r, g, b));
        }

        /// <summary>
        /// Per-zone static: same two-round WMI EC registration as solid,
        /// with individual zone colors via SetGamingRgbKb.
        /// </summary>
        private void PushStaticZoneColors(byte[] r, byte[] g, byte[] b)
        {
            _lastR = r[0];
            _lastG = g[0];
            _lastB = b[0];
            RunStaticEcRecovery(() => WriteStaticZoneColors(r, g, b));
        }

        private void WriteUniformZoneColors(byte r, byte g, byte b)
        {
            byte[] masks = [0x01, 0x02, 0x04, 0x08];
            for (int i = 0; i < 4; i++)
            {
                SendCommand("SetGamingRgbKb", PackStaticZoneColor(masks[i], r, g, b));
                Thread.Sleep(20);
            }
        }

        private void WriteStaticZoneColors(byte[] r, byte[] g, byte[] b)
        {
            byte[] masks = [0x01, 0x02, 0x04, 0x08];
            for (int i = 0; i < 4; i++)
            {
                SendCommand("SetGamingRgbKb", PackStaticZoneColor(masks[i], r[i], g[i], b[i]));
                Thread.Sleep(20);
            }
        }

        /// <summary>
        /// Effect-mode wake (Wave, Neon, …). Insufficient for static after EC loses its profile.
        /// </summary>
        private void BeginLightingUpdate()
        {
            SendCommand("GetGamingLED", 0);
            SendCommand("GetGamingLEDBehavior", 0);
            SendCommand("SetGamingLEDBehavior", KeyboardLedBehaviorWake);
            Thread.Sleep(50);
            SendByteArrayCommand("SetGamingLED", PackKeyboardAllZonesLed());
            SendCommand("SetGamingLEDBehavior", KeyboardAllZones);
            Thread.Sleep(20);
        }

        /// <summary>
        /// Cold-static unlock via AcerGamingFunction WMI only (no Acer userland lighting).
        /// Probe + two rounds: SetGamingLED → SetGamingLEDColor → SetGamingLEDBehavior →
        /// SetGamingKBBacklight (mode 0, [8]=3,[9]=1) → SetGamingRgbKb x4.
        /// Linuwu uses the same KB backlight buffer; GetGamingKBBacklight(1) mirrors Linux get.
        /// </summary>
        private void RunStaticEcRecovery(Action writeZones)
        {
            SendCommand("GetGamingLED", 0);
            SendCommand("GetGamingLED", 0);
            SendCommand("GetGamingLEDBehavior", 0);
            SendCommand("GetGamingKBBacklight", 1);

            ApplyStaticEcRound(writeZones);
            Thread.Sleep(50);
            ApplyStaticEcRound(writeZones);
        }

        private void ApplyStaticEcRound(Action writeZones)
        {
            SendByteArrayCommand("SetGamingLED", PackKeyboardAllZonesLed());
            SendZoneColor(0x0F, _lastR, _lastG, _lastB);
            SendCommand("SetGamingLEDBehavior", KeyboardLedBehaviorWake);
            Thread.Sleep(30);
            // Linuwu set_per_zone: commit static mode with RGB=0, then zone colours.
            SendStaticLedPayload(mode: 0, speed: 0, includeRgbInPayload: false);
            Thread.Sleep(30);
            writeZones();
            SendCommand("SetGamingLEDBehavior", KeyboardAllZones);
        }

        private static ulong PackStaticZoneColor(byte zoneMask, byte r, byte g, byte b) =>
            zoneMask | ((ulong)r << 8) | ((ulong)g << 16) | ((ulong)b << 24);

        private void SendZoneColor(byte zoneMask, byte r, byte g, byte b)
        {
            SendCommand("SetGamingLEDColor", PackStaticZoneColor(zoneMask, r, g, b));
            Thread.Sleep(20);
        }

        private const byte WaveMode = 3;
        private const byte ShiftingMode = 4;

        /// <summary>SetGamingKBBacklight byte[9]: 1 = keyboard (Linuwu), 2 = lid logo / lightbar.</summary>
        private const byte KbBacklightSelectKeyboard = 1;
        private const byte KbBacklightSelectLogo = 2;

        /// <summary>
        /// PHN18 lid logo — Nekro-Sense Arg1=0x0C packed into SetGamingLEDColor u64:
        /// {1, R, G, B, brightness, enable}. Confirmed by interactive probe (color + off).
        /// Effect modes are not supported on this chassis via WMI.
        /// </summary>
        private static ulong PackLogoNekroColor(byte r, byte g, byte b, byte brightness, byte enable) =>
            1ul
            | ((ulong)r << 8)
            | ((ulong)g << 16)
            | ((ulong)b << 24)
            | ((ulong)brightness << 32)
            | ((ulong)enable << 40);

        /// <summary>
        /// Apply lid logo static color / brightness / on-off.
        /// </summary>
        public bool SetLogoLighting(LogoLightingSettings settings)
        {
            byte r = settings.Color.R;
            byte g = settings.Color.G;
            byte b = settings.Color.B;
            byte brightness = settings.Brightness;
            bool on = settings.Enabled && brightness > 0;

            if (!on)
            {
                var (colorOk, _) = SendCommand("SetGamingLEDColor", PackLogoNekroColor(r, g, b, 0, 0));
                bool gateOk = SendLogoEnableGate(enable: false);
                return colorOk && gateOk;
            }

            var (setOk, _) = SendCommand(
                "SetGamingLEDColor",
                PackLogoNekroColor(r, g, b, brightness, enable: 1));
            bool enableOk = SendLogoEnableGate(enable: true);
            return setOk && enableOk;
        }

        /// <summary>
        /// Nekro LBLE power gate via SetGamingKBBacklight, select LB=2.
        /// </summary>
        private bool SendLogoEnableGate(bool enable)
        {
            byte[] payload = new byte[16];
            payload[0] = (byte)(enable ? 1 : 0);
            payload[9] = KbBacklightSelectLogo;
            return SendLedCommand(payload);
        }

        /// <summary>
        /// Linuwu-Sense payload: {mode, speed, brightness, 0, direction, R, G, B, 3, 1, 0...}.
        /// Byte [8]=3 is required on PHN18 — without it firmware treats mode 0 as off.
        /// </summary>
        private void SendStaticLedPayload(byte mode, byte? speed = null, bool includeRgbInPayload = true)
        {
            byte direction = _direction;
            if ((mode == WaveMode || mode == ShiftingMode) && direction == 0)
                direction = 1;
            if (mode == 0)
                direction = 0;

            byte[] payload = new byte[16];
            payload[0] = mode;
            payload[1] = speed ?? _speed;
            payload[2] = _brightness;
            payload[4] = direction;

            if (includeRgbInPayload)
            {
                payload[5] = _lastR;
                payload[6] = _lastG;
                payload[7] = _lastB;
            }

            payload[8] = 3;
            payload[9] = KbBacklightSelectKeyboard;
            SendLedCommand(payload);
        }

        public int CpuTemp => GetSensorReading(0x01);
        public int GpuTemp => GetSensorReading(0x0A);

        private void SyncWindowsPowerMode(byte acerMode)
        {
            try
            {
                Guid overlay = acerMode switch
                {
                    0x00 or 0x06 => OVERLAY_EFFICIENCY,
                    0x04 or 0x05 => OVERLAY_PERFORMANCE,
                    _ => OVERLAY_BALANCED
                };
                PowerSetActiveOverlayScheme(overlay);
            }
            catch { }
        }

        private ManagementObject? GetBatteryControlObject()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryControl");
                return searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        public bool SetBatteryChargeLimit(bool enable)
        {
            try
            {
                using var obj = GetBatteryControlObject();
                if (obj == null) return false;

                using var inParams = obj.GetMethodParameters("SetBatteryHealthControl");
                inParams["uBatteryNo"] = (byte)1;
                inParams["uFunctionMask"] = (byte)1;
                inParams["uFunctionStatus"] = (byte)(enable ? 1 : 0);
                inParams["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };

                using var outParams = obj.InvokeMethod("SetBatteryHealthControl", inParams, null);
                ushort result = Convert.ToUInt16(outParams["uReturn"]);
                return result == 0;
            }
            catch
            {
                return false;
            }
        }
        public bool IsBatteryControlSupported()
        {
            try
            {
                using var obj = GetBatteryControlObject();
                return obj != null;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _cachedObj?.Dispose();
                _cachedObj = null;
            }
        }
    }
}