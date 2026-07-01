namespace PredatorControlApp
{
    public sealed class KeyboardColorSettings
    {
        public bool FourZone { get; set; }
        public Color SolidColor { get; set; } = Color.FromArgb(0, 150, 255);
        public Color[] ZoneColors { get; set; } =
        [
            Color.FromArgb(0, 150, 255),
            Color.FromArgb(0, 200, 120),
            Color.FromArgb(255, 140, 0),
            Color.FromArgb(200, 60, 255)
        ];
        public byte Brightness { get; set; } = 100;

        public static KeyboardColorSettings FromWmi(WmiController wmi)
        {
            return new KeyboardColorSettings
            {
                SolidColor = Color.FromArgb(wmi.LastR, wmi.LastG, wmi.LastB),
                Brightness = wmi.Brightness,
                ZoneColors =
                [
                    Color.FromArgb(wmi.LastR, wmi.LastG, wmi.LastB),
                    ShiftHue(Color.FromArgb(wmi.LastR, wmi.LastG, wmi.LastB), 0.12f),
                    ShiftHue(Color.FromArgb(wmi.LastR, wmi.LastG, wmi.LastB), 0.28f),
                    ShiftHue(Color.FromArgb(wmi.LastR, wmi.LastG, wmi.LastB), 0.45f)
                ]
            };
        }

        private static Color ShiftHue(Color c, float amount)
        {
            float h = c.GetHue() / 360f;
            float s = c.GetSaturation();
            float l = c.GetBrightness();
            h = (h + amount) % 1f;
            return FromHsl(h, s, l);
        }

        private static Color FromHsl(float h, float s, float v)
        {
            int hi = (int)(h * 6f) % 6;
            float f = h * 6f - hi;
            float p = v * (1 - s);
            float q = v * (1 - f * s);
            float t = v * (1 - (1 - f) * s);
            (float r, float g, float b) = hi switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q)
            };
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }
    }
}
