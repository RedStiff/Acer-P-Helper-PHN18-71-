namespace PredatorControlApp
{
    /// <summary>
    /// PHN18 lid logo via Nekro SetGamingLEDColor (static color + brightness + on/off).
    /// Firmware effects (Breathing/Neon) are not available on this chassis via WMI.
    /// </summary>
    public sealed class LogoLightingSettings
    {
        public bool Enabled { get; set; } = true;
        public Color Color { get; set; } = Color.FromArgb(0xFF, 0x00, 0x00);
        public byte Brightness { get; set; } = 100;

        public LogoLightingSettings Clone() => new()
        {
            Enabled = Enabled,
            Color = Color,
            Brightness = Brightness
        };
    }

    public sealed class LightingSettingsSnapshot
    {
        public KeyboardColorSettings Colors { get; set; } = new();
        public int RgbMode { get; set; }
        public int Speed { get; set; }
        public LogoLightingSettings Logo { get; set; } = new();

        public byte MappedSpeed =>
            (byte)Math.Clamp(Math.Round(Speed * 9.0 / 100.0), 1, 9);
    }
}
