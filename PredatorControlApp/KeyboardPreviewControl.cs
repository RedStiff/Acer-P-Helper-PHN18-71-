using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class KeyboardPreviewControl : Control
    {
        private const float KeyboardAspect = 4.15f;
        private const float ZoneStripHeight = 20f;
        private const float ChassisPad = 9f;
        private const float KeyGap = 3.2f;
        private const float KeyCornerRadius = 4.8f;

        private static readonly Color ChassisFill = Color.FromArgb(6, 6, 8);
        private static readonly Color KeyTop = Color.FromArgb(54, 54, 58);
        private static readonly Color KeyBottom = Color.FromArgb(20, 20, 24);
        private static readonly Color KeyEdge = Color.FromArgb(12, 12, 14);

        // Full-size ANSI layout (F-row + main block + numpad cluster).
        private static readonly float[][] RowWidths =
        [
            [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f],
            [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 2f, 1f, 1f, 1f, 1f],
            [1.5f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1.75f, 1f, 1f, 1f, 1f, 1f],
            [1.75f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 2.25f, 1f, 1f, 1f, 1f, 1f],
            [2.25f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 2.75f, 1.25f, 1f, 1f, 1f, 1f],
            [1.25f, 1.25f, 1.25f, 6.25f, 1.25f, 1.25f, 1.25f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f]
        ];

        private static readonly float[] RowOffsets = [0f, 0f, 0.08f, 0.22f, 0.38f, 0.52f];

        private bool _fourZone;
        private Color _solidColor = AppTheme.Accent;
        private readonly Color[] _zoneColors = new Color[4];
        private int _brightness = 100;
        private int _selectedZone;
        private readonly List<KeyRect> _keys = [];
        private PreviewLayout _layout;
        private float _referenceUnit;

        private sealed record KeyRect(RectangleF Bounds, int Zone);

        private readonly struct PreviewLayout
        {
            public RectangleF Chassis { get; init; }
            public RectangleF KeyArea { get; init; }
            public RectangleF ZoneStrip { get; init; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool FourZone
        {
            get => _fourZone;
            set { _fourZone = value; RebuildLayout(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color SolidColor
        {
            get => _solidColor;
            set { _solidColor = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Brightness
        {
            get => _brightness;
            set { _brightness = Math.Clamp(value, 0, 100); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedZone
        {
            get => _selectedZone;
            set { _selectedZone = Math.Clamp(value, 0, 3); Invalidate(); }
        }

        public event EventHandler? ZoneSelected;

        private readonly EventHandler _themeChangedHandler;

        public KeyboardPreviewControl()
        {
            _themeChangedHandler = (_, _) =>
            {
                BackColor = AppTheme.FormBackground;
                Invalidate();
            };
            _zoneColors[0] = Color.FromArgb(220, 40, 30);
            _zoneColors[1] = Color.FromArgb(255, 170, 0);
            _zoneColors[2] = Color.FromArgb(0, 210, 255);
            _zoneColors[3] = Color.FromArgb(180, 150, 60);

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MinimumSize = new Size(480, 220);
            Cursor = Cursors.Hand;
            BackColor = AppTheme.FormBackground;
            AppTheme.Changed += _themeChangedHandler;
        }

        public void SetZoneColor(int zone, Color color)
        {
            if (zone < 0 || zone > 3) return;
            _zoneColors[zone] = color;
            Invalidate();
        }

        public Color GetZoneColor(int zone) => zone is >= 0 and <= 3 ? _zoneColors[zone] : _solidColor;

        public void LoadSettings(KeyboardColorSettings settings)
        {
            _fourZone = settings.FourZone;
            _solidColor = settings.SolidColor;
            _brightness = settings.Brightness;
            for (int i = 0; i < 4; i++)
                _zoneColors[i] = settings.ZoneColors[i];
            RebuildLayout();
        }

        public void ExportSettings(KeyboardColorSettings settings)
        {
            settings.FourZone = _fourZone;
            settings.SolidColor = _solidColor;
            settings.Brightness = (byte)_brightness;
            for (int i = 0; i < 4; i++)
                settings.ZoneColors[i] = _zoneColors[i];
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RebuildLayout();
        }

        private void RebuildLayout()
        {
            _layout = ComputeLayout();
            BuildKeyLayout();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_fourZone) return;

            foreach (var key in _keys)
            {
                if (!key.Bounds.Contains(e.Location)) continue;
                SelectedZone = key.Zone;
                ZoneSelected?.Invoke(this, EventArgs.Empty);
                break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SetClip(ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(BackColor);

            if (_keys.Count == 0) RebuildLayout();

            var chassis = _layout.Chassis;
            var keyArea = _layout.KeyArea;
            if (chassis.Width < 40 || chassis.Height < 40) return;

            DrawChassis(g, chassis);
            DrawPlateGlow(g, keyArea);
            foreach (var key in _keys)
                DrawKeyCap(g, key);

            if (_fourZone)
                DrawZoneLabels(g, _layout.ZoneStrip, keyArea);
        }

        private void DrawChassis(Graphics g, RectangleF chassis)
        {
            using var body = new SolidBrush(ChassisFill);
            g.FillRoundedRect(body, chassis, 8);
        }

        private void DrawPlateGlow(Graphics g, RectangleF keyArea)
        {
            float strength = 0.45f + 0.55f * (_brightness / 100f);
            using var platePath = RoundedRectPath(Inflate(keyArea, -1f), 4f);
            g.SetClip(platePath);

            using (var wash = CreatePlateGradientBrush(keyArea, strength))
                g.FillRectangle(wash, keyArea);

            foreach (var key in _keys)
            {
                float cx = key.Bounds.Left + key.Bounds.Width / 2f;
                Color glow = SampleColorAt(cx, keyArea);
                for (int pass = 2; pass >= 1; pass--)
                {
                    float expand = KeyGap * (0.25f + pass * 0.35f);
                    int alpha = (int)(strength * 55 / pass);
                    var glowRect = Inflate(key.Bounds, expand);
                    using var path = RoundedRectPath(glowRect, KeyCornerRadius + pass * 0.5f);
                    using var brush = new SolidBrush(Color.FromArgb(alpha, glow));
                    g.FillPath(brush, path);
                }
            }

            g.ResetClip();
            g.SetClip(ClientRectangle);
        }

        private Brush CreatePlateGradientBrush(RectangleF keyArea, float strength)
        {
            if (!_fourZone)
            {
                Color lit = ApplyBrightness(_solidColor, _brightness);
                return new SolidBrush(Color.FromArgb((int)(255 * strength), lit));
            }

            Color z0 = ApplyBrightness(_zoneColors[0], _brightness);
            Color z1 = ApplyBrightness(_zoneColors[1], _brightness);
            Color z2 = ApplyBrightness(_zoneColors[2], _brightness);
            Color z3 = ApplyBrightness(_zoneColors[3], _brightness);

            var blend = new ColorBlend
            {
                Colors =
                [
                    Color.FromArgb((int)(255 * strength), z0),
                    Color.FromArgb((int)(255 * strength), LerpColor(z0, z1, 0.5f)),
                    Color.FromArgb((int)(255 * strength), z1),
                    Color.FromArgb((int)(255 * strength), LerpColor(z1, z2, 0.5f)),
                    Color.FromArgb((int)(255 * strength), z2),
                    Color.FromArgb((int)(255 * strength), LerpColor(z2, z3, 0.5f)),
                    Color.FromArgb((int)(255 * strength), z3)
                ],
                Positions = [0f, 0.14f, 0.28f, 0.42f, 0.58f, 0.76f, 1f]
            };

            var brush = new LinearGradientBrush(keyArea, z0, z3, 0f);
            brush.InterpolationColors = blend;
            return brush;
        }

        private void DrawKeyCap(Graphics g, KeyRect key)
        {
            var bounds = key.Bounds;
            using var path = RoundedRectPath(bounds, KeyCornerRadius);

            using (var fill = new LinearGradientBrush(bounds, KeyTop, KeyBottom, 90f))
                g.FillPath(fill, path);

            using (var highlight = new Pen(Color.FromArgb(36, 255, 255, 255), 0.8f))
            {
                float y = bounds.Top + 1f;
                g.DrawLine(highlight, bounds.Left + KeyCornerRadius, y, bounds.Right - KeyCornerRadius, y);
            }

            using var edge = new Pen(KeyEdge, 1f);
            g.DrawPath(edge, path);

            if (_fourZone && key.Zone == _selectedZone)
            {
                using var sel = new Pen(Color.FromArgb(140, 255, 255, 255), 1.2f);
                g.DrawRoundedRect(sel, Inflate(bounds, 0.8f), KeyCornerRadius + 0.5f);
            }
        }

        private void DrawZoneLabels(Graphics g, RectangleF strip, RectangleF keyArea)
        {
            if (strip.Height < 8) return;

            using var font = new Font("Segoe UI", 7f);
            using var brush = new SolidBrush(Color.FromArgb(100, 170, 170, 180));
            string[] labels = ["Zone 1", "Zone 2", "Zone 3", "Zone 4"];

            for (int i = 0; i < 4; i++)
            {
                float x0 = keyArea.Left + keyArea.Width * i / 4f;
                float x1 = keyArea.Left + keyArea.Width * (i + 1) / 4f;
                float cx = (x0 + x1) / 2f;
                var size = g.MeasureString(labels[i], font);
                g.DrawString(labels[i], font, brush, cx - size.Width / 2f, strip.Top + 3);

                if (i > 0)
                {
                    using var divider = new Pen(Color.FromArgb(28, 255, 255, 255), 1f);
                    g.DrawLine(divider, x0, strip.Top + 2, x0, strip.Bottom - 2);
                }
            }
        }

        private Color SampleColorAt(float centerX, RectangleF keyArea)
        {
            float t = Math.Clamp((centerX - keyArea.Left) / keyArea.Width, 0f, 1f);

            if (!_fourZone)
                return ApplyBrightness(_solidColor, _brightness);

            float pos = t * 3f;
            int i = Math.Min((int)pos, 2);
            int j = i + 1;
            float blend = pos - i;
            return ApplyBrightness(LerpColor(_zoneColors[i], _zoneColors[j], blend), _brightness);
        }

        private PreviewLayout ComputeLayout()
        {
            const float margin = 8f;
            float zoneStrip = _fourZone ? ZoneStripHeight : 0f;
            float extras = zoneStrip + ChassisPad * 2f + 8f;

            float availW = Math.Max(100, Width - margin * 2);
            float availH = Math.Max(80, Height - margin * 2);

            float maxKeyW = availW - ChassisPad * 2f;
            float maxKeyH = Math.Max(42, availH - extras);

            float keyW = maxKeyW;
            float keyH = keyW / KeyboardAspect;
            if (keyH > maxKeyH)
            {
                keyH = maxKeyH;
                keyW = keyH * KeyboardAspect;
            }

            float chassisW = keyW + ChassisPad * 2f;
            float chassisH = keyH + extras;
            float cx = (Width - chassisW) / 2f;
            float cy = margin;

            var chassis = new RectangleF(cx, cy, chassisW, chassisH);
            var keyArea = new RectangleF(
                chassis.Left + ChassisPad,
                chassis.Top + ChassisPad,
                keyW,
                keyH);

            var zoneStripRect = new RectangleF(
                keyArea.Left,
                keyArea.Bottom + 5f,
                keyArea.Width,
                zoneStrip);

            return new PreviewLayout
            {
                Chassis = chassis,
                KeyArea = keyArea,
                ZoneStrip = zoneStripRect
            };
        }

        private void BuildKeyLayout()
        {
            _keys.Clear();
            var area = _layout.KeyArea;
            if (area.Width < 20 || area.Height < 20) return;

            // Number row spans the full width — use it as the reference for stagger offsets.
            float refTotal = RowWidths[1].Sum();
            _referenceUnit = (area.Width - KeyGap * (RowWidths[1].Length - 1)) / refTotal;

            float rowH = (area.Height - KeyGap * (RowWidths.Length - 1)) / RowWidths.Length;

            for (int row = 0; row < RowWidths.Length; row++)
            {
                float[] widths = RowWidths[row];
                float totalW = widths.Sum();
                float offsetPx = RowOffsets[row] * _referenceUnit;
                float availableW = area.Width - offsetPx;
                float unit = (availableW - KeyGap * (widths.Length - 1)) / totalW;

                float x = area.Left + offsetPx;
                float y = area.Top + row * (rowH + KeyGap);
                float rowRightLimit = area.Right;

                for (int col = 0; col < widths.Length; col++)
                {
                    float w = widths[col] * unit;
                    if (x + w > rowRightLimit + 0.5f)
                        w = Math.Max(2f, rowRightLimit - x);

                    var rect = new RectangleF(
                        MathF.Floor(x),
                        MathF.Floor(y),
                        MathF.Max(2f, MathF.Floor(w)),
                        MathF.Max(2f, MathF.Floor(rowH)));

                    int zone = ZoneFromX(rect.Left + rect.Width / 2f, area);
                    _keys.Add(new KeyRect(rect, zone));
                    x += w + KeyGap;

                    if (x >= rowRightLimit) break;
                }
            }
        }

        private static int ZoneFromX(float x, RectangleF area)
        {
            float rel = (x - area.Left) / area.Width;
            return Math.Clamp((int)(rel * 4f), 0, 3);
        }

        private static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static Color ApplyBrightness(Color c, int brightness)
        {
            float b = brightness / 100f;
            return Color.FromArgb(
                (int)(c.R * b),
                (int)(c.G * b),
                (int)(c.B * b));
        }

        private static RectangleF Inflate(RectangleF r, float amount) =>
            new(r.X - amount, r.Y - amount, r.Width + amount * 2, r.Height + amount * 2);

        private static GraphicsPath RoundedRectPath(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
            float d = r * 2;
            if (d <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                AppTheme.Changed -= _themeChangedHandler;
            base.Dispose(disposing);
        }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRect(this Graphics g, Brush brush, RectangleF bounds, float radius)
        {
            using var path = RoundedRect(bounds, radius);
            g.FillPath(brush, path);
        }

        public static void DrawRoundedRect(this Graphics g, Pen pen, RectangleF bounds, float radius)
        {
            using var path = RoundedRect(bounds, radius);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
            float d = r * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
