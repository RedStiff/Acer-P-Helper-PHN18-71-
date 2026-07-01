using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class KeyboardColorPickerControl : Control
    {
        private const int HueBarWidth = 22;
        private const int SwatchSize = 52;
        private const int InnerPad = 6;
        private const int SectionGap = 8;
        private const int HeaderHeight = 22;
        private const int ValueRowHeight = 22;

        private static readonly Font FontSection = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontValue = new("Segoe UI", 8.5f, FontStyle.Regular);

        private float _hue;
        private float _saturation = 1f;
        private float _brightness = 1f;
        private bool _draggingSb;
        private bool _draggingHue;

        private readonly EventHandler _themeChangedHandler;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color SelectedColor
        {
            get => ColorFromHsv(_hue, _saturation, _brightness);
            set => SetColor(value);
        }

        public event EventHandler? ColorChanged;

        public KeyboardColorPickerControl()
        {
            _themeChangedHandler = (_, _) => Invalidate();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(480, 196);
            MinimumSize = new Size(360, 196);
            Cursor = Cursors.Default;
            AppTheme.Changed += _themeChangedHandler;
        }

        public void SetColor(Color color)
        {
            _hue = color.GetHue();
            _saturation = color.GetSaturation();
            _brightness = color.GetBrightness();
            if (_saturation <= 0.001f)
                _hue = 0;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (TryHitHue(e.Location, out _))
            {
                _draggingHue = true;
                UpdateHueFromPoint(e.Location);
                return;
            }

            if (TryHitSb(e.Location, out _))
            {
                _draggingSb = true;
                UpdateSbFromPoint(e.Location);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_draggingHue)
                UpdateHueFromPoint(e.Location);
            else if (_draggingSb)
                UpdateSbFromPoint(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _draggingHue = false;
            _draggingSb = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(BackColor);

            using var headerBrush = new SolidBrush(AppTheme.SectionHeader);
            g.DrawString("COLOR", FontSection, headerBrush, 0, 0);

            Rectangle sbRect = GetSbRect();
            Rectangle hueRect = GetHueRect();
            Rectangle swatchRect = GetSwatchRect();

            DrawSaturationBrightnessField(g, sbRect, _hue);
            DrawHueBar(g, hueRect);
            DrawSwatch(g, swatchRect, SelectedColor);
            DrawSbMarker(g, sbRect);
            DrawHueMarker(g, hueRect);

            Color c = SelectedColor;
            string info = $"R {c.R}   G {c.G}   B {c.B}";
            using var valueBrush = new SolidBrush(AppTheme.SecondaryText);
            g.DrawString(info, FontValue, valueBrush, sbRect.Left, sbRect.Bottom + SectionGap);
        }

        private void DrawSaturationBrightnessField(Graphics g, Rectangle bounds, float hue)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            Color pure = ColorFromHue(hue);
            using (var baseBrush = new SolidBrush(pure))
                g.FillRectangle(baseBrush, bounds);

            using (var white = new LinearGradientBrush(bounds, Color.White, Color.FromArgb(0, Color.White), 0f))
                g.FillRectangle(white, bounds);

            using (var black = new LinearGradientBrush(bounds, Color.Transparent, Color.Black, 90f))
                g.FillRectangle(black, bounds);

            using var border = new Pen(AppTheme.InputBorder);
            g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        private void DrawHueBar(Graphics g, Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            using var brush = new LinearGradientBrush(
                bounds,
                Color.FromArgb(255, 255, 0, 0),
                Color.FromArgb(255, 255, 0, 0),
                LinearGradientMode.Vertical);
            brush.InterpolationColors = new ColorBlend
            {
                Colors =
                [
                    Color.FromArgb(255, 255, 0, 0),
                    Color.FromArgb(255, 255, 255, 0),
                    Color.FromArgb(255, 0, 255, 0),
                    Color.FromArgb(255, 0, 255, 255),
                    Color.FromArgb(255, 0, 0, 255),
                    Color.FromArgb(255, 255, 0, 255),
                    Color.FromArgb(255, 255, 0, 0)
                ],
                Positions = [0f, 0.17f, 0.33f, 0.5f, 0.67f, 0.83f, 1f]
            };
            g.FillRectangle(brush, bounds);

            using var border = new Pen(AppTheme.InputBorder);
            g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        private static void DrawSwatch(Graphics g, Rectangle bounds, Color color)
        {
            using var fill = new SolidBrush(color);
            g.FillRoundedRect(fill, bounds, 6);
            using var border = new Pen(AppTheme.Separator, 1.5f);
            g.DrawRoundedRect(border, bounds, 6);
        }

        private void DrawSbMarker(Graphics g, Rectangle sbRect)
        {
            if (sbRect.Width <= 0 || sbRect.Height <= 0) return;

            float x = sbRect.Left + _saturation * sbRect.Width;
            float y = sbRect.Top + (1f - _brightness) * sbRect.Height;
            DrawMarker(g, x, y);
        }

        private void DrawHueMarker(Graphics g, Rectangle hueRect)
        {
            if (hueRect.Height <= 0) return;

            float y = hueRect.Top + _hue / 360f * hueRect.Height;
            using var pen = new Pen(Color.White, 2f);
            g.DrawLine(pen, hueRect.Left - 1, y, hueRect.Right + 1, y);
            using var penDark = new Pen(Color.FromArgb(120, Color.Black), 1f);
            g.DrawLine(penDark, hueRect.Left - 1, y + 1, hueRect.Right + 1, y + 1);
        }

        private static void DrawMarker(Graphics g, float x, float y)
        {
            const float r = 6f;
            using var ring = new Pen(Color.White, 2f);
            g.DrawEllipse(ring, x - r, y - r, r * 2, r * 2);
            using var inner = new Pen(Color.FromArgb(140, Color.Black), 1f);
            g.DrawEllipse(inner, x - r + 1, y - r + 1, (r - 1) * 2, (r - 1) * 2);
        }

        private Rectangle GetSbRect()
        {
            int rightReserve = HueBarWidth + SwatchSize + InnerPad * 2;
            int height = Math.Max(80, Height - HeaderHeight - ValueRowHeight - SectionGap);
            int width = Math.Max(160, Width - rightReserve);
            return new Rectangle(0, HeaderHeight, width, height);
        }

        private Rectangle GetHueRect()
        {
            Rectangle sb = GetSbRect();
            return new Rectangle(sb.Right + InnerPad, sb.Top, HueBarWidth, sb.Height);
        }

        private Rectangle GetSwatchRect()
        {
            Rectangle hue = GetHueRect();
            return new Rectangle(hue.Right + InnerPad, hue.Top, SwatchSize, SwatchSize);
        }

        private bool TryHitSb(Point p, out Rectangle sbRect)
        {
            sbRect = GetSbRect();
            return sbRect.Contains(p);
        }

        private bool TryHitHue(Point p, out Rectangle hueRect)
        {
            hueRect = GetHueRect();
            return hueRect.Contains(p);
        }

        private void UpdateSbFromPoint(Point p)
        {
            Rectangle sb = GetSbRect();
            if (sb.Width <= 0 || sb.Height <= 0) return;

            _saturation = Math.Clamp((p.X - sb.Left) / (float)sb.Width, 0f, 1f);
            _brightness = Math.Clamp(1f - (p.Y - sb.Top) / (float)sb.Height, 0f, 1f);
            Invalidate();
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateHueFromPoint(Point p)
        {
            Rectangle hue = GetHueRect();
            if (hue.Height <= 0) return;

            _hue = Math.Clamp((p.Y - hue.Top) / (float)hue.Height * 360f, 0f, 359.9f);
            Invalidate();
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }

        private static Color ColorFromHue(float hue) =>
            ColorFromHsv(hue, 1f, 1f);

        private static Color ColorFromHsv(float hue, float saturation, float brightness)
        {
            int hi = Convert.ToInt32(MathF.Floor(hue / 60f)) % 6;
            float f = hue / 60f - MathF.Floor(hue / 60f);

            float v = brightness;
            float p = v * (1f - saturation);
            float q = v * (1f - f * saturation);
            float t = v * (1f - (1f - f) * saturation);

            (float r, float g, float b) = hi switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q)
            };

            return Color.FromArgb(
                (int)(r * 255),
                (int)(g * 255),
                (int)(b * 255));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                AppTheme.Changed -= _themeChangedHandler;
            base.Dispose(disposing);
        }
    }
}
