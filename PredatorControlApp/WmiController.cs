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
        private bool? _supportsRgbKbStatic;

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

        private bool SendLedCommand(byte[] payload)
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return false;

                using var inParams = obj.GetMethodParameters("SetGamingKBBacklight");
                inParams["gmInput"] = payload;
                using var outParams = obj.InvokeMethod("SetGamingKBBacklight", inParams, null);
                ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                return (result & 0xFF) == 0;
            }
            catch
            {
                InvalidateCache();
                return false;
            }
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
            // UI placeholder only — PHN18 uses NVIDIA DDS, not WMI misc for GPU mux.
            SupportsGraphicsMode = true;
        }

        public int GetGraphicsMode() => -1;

        public bool SetGraphicsMode(byte mode)
        {
            _ = mode;
            return SupportsGraphicsMode;
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
            percent = Math.Clamp(percent, (byte)0, (byte)100);
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
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = brightness;
            _lastMode = 0;
            ApplyLightingMode(0);
        }

        private void ApplyLightingMode(int mode)
        {
            if (mode == 0)
            {
                ApplySolidStaticColor(_lastR, _lastG, _lastB);
                return;
            }

            BeginLightingUpdate();
            if (mode != 2 && mode != 3)
                SendZoneColor(0x0F, _lastR, _lastG, _lastB);

            SendStaticLedPayload((byte)mode);
        }

        private void ApplySolidStaticColor(byte r, byte g, byte b)
        {
            PushStaticZoneColors(
                [r, r, r, r],
                [g, g, g, g],
                [b, b, b, b]);
        }

        public void ApplyStaticFourZoneColors(byte[] r, byte[] g, byte[] b, byte brightness)
        {
            if (r.Length < 4 || g.Length < 4 || b.Length < 4) return;

            _brightness = brightness;
            _lastMode = 0;
            _lastR = r[0];
            _lastG = g[0];
            _lastB = b[0];

            PushStaticZoneColors(r, g, b);
        }

        private void PushStaticZoneColors(byte[] r, byte[] g, byte[] b)
        {
            bool predatorV5 = SupportsRgbKbStatic();

            ProbeKeyboardBacklight();
            SendCommand("SetGamingLEDBehavior", 0x07ul);
            Thread.Sleep(50);

            // allZones behavior payload resets per-zone static palette on Predator v5 (PHN18).
            if (!predatorV5)
                EnableKeyboardZones();

            byte[] masks = [0x01, 0x02, 0x04, 0x08];
            for (int i = 0; i < 4; i++)
            {
                if (predatorV5)
                {
                    SendRgbKbZoneColor(masks[i], r[i], g[i], b[i]);
                    SendLegacyZoneColor(masks[i], r[i], g[i], b[i]);
                }
                else
                {
                    SendAllZoneColorMethods(masks[i], r[i], g[i], b[i]);
                }
            }

            if (predatorV5)
                SendLegacyZoneColor(0x0F, _lastR, _lastG, _lastB);

            CommitStaticLighting();
        }

        private void CommitStaticLighting()
        {
            if (SupportsRgbKbStatic())
            {
                // facer_rgb: static commit — mode 0, brightness only.
                SendStaticLedPayload(0, includeRgbInPayload: false);
                Thread.Sleep(30);
                // PHN18 ignores palette in mode 0; breathing @ speed 0 = no animation (solid).
                SendStaticLedPayload(1, speed: 0, includeRgbInPayload: true);
                return;
            }

            SendStaticLedPayload(0, includeRgbInPayload: false);
            Thread.Sleep(30);
            SendStaticLedPayload(0, includeRgbInPayload: true);
        }

        private bool SupportsRgbKbStatic()
        {
            if (_supportsRgbKbStatic.HasValue) return _supportsRgbKbStatic.Value;

            _supportsRgbKbStatic = false;
            try
            {
                var obj = GetWmiObject();
                if (obj != null)
                    _supportsRgbKbStatic = obj.GetMethodParameters("SetGamingRgbKb") != null;
            }
            catch { }

            return _supportsRgbKbStatic.Value;
        }

        private void BeginLightingUpdate()
        {
            ProbeKeyboardBacklight();
            SendCommand("SetGamingLEDBehavior", 0x07ul);
            Thread.Sleep(50);

            if (!SupportsRgbKbStatic())
                EnableKeyboardZones();
        }

        private void SendLegacyZoneColor(byte zoneMask, byte r, byte g, byte b)
        {
            ulong zonePayload = 0x06ul | ((ulong)zoneMask << 8)
                | ((ulong)r << 16) | ((ulong)g << 24) | ((ulong)b << 32);
            SendCommand("SetGamingLEDBehavior", zonePayload);
            Thread.Sleep(20);
        }

        private void ProbeKeyboardBacklight()
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return;

                using var inParams = obj.GetMethodParameters("GetGamingSysInfo");
                inParams["gmInput"] = (uint)0;
                obj.InvokeMethod("GetGamingSysInfo", inParams, null);
            }
            catch
            {
                InvalidateCache();
            }
        }

        private void EnableKeyboardZones()
        {
            const ulong allZones = 8ul | (0x0Ful << 40);
            SendCommand("SetGamingLED", allZones);
            SendCommand("SetGamingLEDBehavior", allZones);
            Thread.Sleep(20);
        }

        private static ulong PackStaticZoneColor(byte zoneMask, byte r, byte g, byte b) =>
            zoneMask | ((ulong)r << 8) | ((ulong)g << 16) | ((ulong)b << 24);

        private void SendRgbKbZoneColor(byte zoneMask, byte r, byte g, byte b)
        {
            SendCommand("SetGamingRgbKb", PackStaticZoneColor(zoneMask, r, g, b));
            Thread.Sleep(20);
        }

        private void SendAllZoneColorMethods(byte zoneMask, byte r, byte g, byte b)
        {
            ulong legacy = 0x06ul | ((ulong)zoneMask << 8)
                | ((ulong)r << 16) | ((ulong)g << 24) | ((ulong)b << 32);

            SendCommand("SetGamingLEDColor", PackStaticZoneColor(zoneMask, r, g, b));
            SendCommand("SetGamingLEDColor", legacy);
            SendCommand("SetGamingLEDBehavior", legacy);
            Thread.Sleep(20);
        }

        private void SendZoneColor(byte zoneMask, byte r, byte g, byte b)
        {
            SendCommand("SetGamingLEDColor", PackStaticZoneColor(zoneMask, r, g, b));
            Thread.Sleep(20);
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
                SetRgbMode(0, settings.SolidColor.R, settings.SolidColor.G, settings.SolidColor.B,
                    settings.Brightness, _speed, _direction);
            }
        }

        private void SendStaticLedPayload(byte mode, byte? speed = null, bool includeRgbInPayload = true)
        {
            byte[] payload = new byte[16];
            payload[0] = mode;
            payload[1] = speed ?? (mode == 0 ? (byte)0 : _speed);
            payload[2] = _brightness;
            payload[3] = mode switch
            {
                3 => 8,
                0 => 0,
                _ => _direction
            };

            if (includeRgbInPayload)
            {
                payload[5] = _lastR;
                payload[6] = _lastG;
                payload[7] = _lastB;
            }

            payload[9] = 1;
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