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

        // True HSV (not WinForms HSL) — keeps the marker continuous while dragging.
        private float _hue;
        private float _saturation = 1f;
        private float _value = 1f;
        private bool _draggingSb;
        private bool _draggingHue;

        private Bitmap? _sbCache;
        private float _sbCacheHue = -1f;
        private Size _sbCacheSize;

        private readonly EventHandler _themeChangedHandler;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color SelectedColor
        {
            get => ColorFromHsv(_hue, _saturation, _value);
            set => SetColor(value);
        }

        public event EventHandler? ColorChanged;
        public event EventHandler? ColorCommitted;

        public KeyboardColorPickerControl()
        {
            _themeChangedHandler = (_, _) =>
            {
                InvalidateSbCache();
                Invalidate();
            };
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Size = new Size(480, 196);
            MinimumSize = new Size(240, 180);
            Cursor = Cursors.Default;
            AppTheme.Changed += _themeChangedHandler;
        }

        public void SetColor(Color color)
        {
            RgbToHsv(color, out float h, out float s, out float v);
            bool changed = Math.Abs(_hue - h) > 0.01f
                || Math.Abs(_saturation - s) > 0.001f
                || Math.Abs(_value - v) > 0.001f;

            _hue = h;
            _saturation = s;
            _value = v;
            if (_saturation <= 0.001f)
                _hue = 0;

            if (changed)
                Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            Focus();
            Capture = true;

            if (TryHitHue(e.Location, out _))
            {
                _draggingHue = true;
                UpdateHueFromPoint(e.Location, commit: false);
                return;
            }

            if (TryHitSb(e.Location, out _))
            {
                _draggingSb = true;
                UpdateSbFromPoint(e.Location, commit: false);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_draggingHue)
                UpdateHueFromPoint(e.Location, commit: false);
            else if (_draggingSb)
                UpdateSbFromPoint(e.Location, commit: false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasDragging = _draggingHue || _draggingSb;
            _draggingHue = false;
            _draggingSb = false;
            Capture = false;
            if (wasDragging)
                ColorCommitted?.Invoke(this, EventArgs.Empty);
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

            DrawSaturationValueField(g, sbRect, _hue);
            DrawHueBar(g, hueRect);
            DrawSwatch(g, swatchRect, SelectedColor);
            DrawSbMarker(g, sbRect);
            DrawHueMarker(g, hueRect);

            Color c = SelectedColor;
            string info = $"R {c.R}   G {c.G}   B {c.B}";
            using var valueBrush = new SolidBrush(AppTheme.SecondaryText);
            g.DrawString(info, FontValue, valueBrush, sbRect.Left, sbRect.Bottom + SectionGap);
        }

        private void DrawSaturationValueField(Graphics g, Rectangle bounds, float hue)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            EnsureSbCache(bounds, hue);
            if (_sbCache != null)
                g.DrawImageUnscaled(_sbCache, bounds.Location);

            using var border = new Pen(AppTheme.InputBorder);
            g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        private void EnsureSbCache(Rectangle bounds, float hue)
        {
            var size = bounds.Size;
            if (_sbCache != null
                && _sbCacheSize == size
                && Math.Abs(_sbCacheHue - hue) < 0.05f)
                return;

            InvalidateSbCache();
            _sbCache = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            _sbCacheSize = size;
            _sbCacheHue = hue;

            using var g = Graphics.FromImage(_sbCache);
            var local = new Rectangle(0, 0, _sbCache.Width, _sbCache.Height);
            Color pure = ColorFromHue(hue);

            using (var baseBrush = new SolidBrush(pure))
                g.FillRectangle(baseBrush, local);

            using (var white = new LinearGradientBrush(local, Color.White, Color.FromArgb(0, Color.White), 0f))
                g.FillRectangle(white, local);

            using (var black = new LinearGradientBrush(local, Color.Transparent, Color.Black, 90f))
                g.FillRectangle(black, local);
        }

        private void InvalidateSbCache()
        {
            _sbCache?.Dispose();
            _sbCache = null;
            _sbCacheHue = -1f;
            _sbCacheSize = Size.Empty;
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

            float x = sbRect.Left + _saturation * (sbRect.Width - 1);
            float y = sbRect.Top + (1f - _value) * (sbRect.Height - 1);
            DrawMarker(g, x, y);
        }

        private void DrawHueMarker(Graphics g, Rectangle hueRect)
        {
            if (hueRect.Height <= 0) return;

            float y = hueRect.Top + _hue / 360f * (hueRect.Height - 1);
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
            int width = Math.Max(120, Width - rightReserve);
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
            // Slightly wider hit area for easier dragging.
            var hit = Rectangle.Inflate(hueRect, 4, 0);
            return hit.Contains(p);
        }

        private void UpdateSbFromPoint(Point p, bool commit)
        {
            Rectangle sb = GetSbRect();
            if (sb.Width <= 1 || sb.Height <= 1) return;

            float s = Math.Clamp((p.X - sb.Left) / (float)(sb.Width - 1), 0f, 1f);
            float v = Math.Clamp(1f - (p.Y - sb.Top) / (float)(sb.Height - 1), 0f, 1f);

            if (Math.Abs(_saturation - s) < 0.0005f && Math.Abs(_value - v) < 0.0005f)
                return;

            _saturation = s;
            _value = v;
            Invalidate();
            RaiseColorChanged(commit);
        }

        private void UpdateHueFromPoint(Point p, bool commit)
        {
            Rectangle hue = GetHueRect();
            if (hue.Height <= 1) return;

            float h = Math.Clamp((p.Y - hue.Top) / (float)(hue.Height - 1) * 360f, 0f, 359.9f);
            if (Math.Abs(_hue - h) < 0.05f)
                return;

            _hue = h;
            InvalidateSbCache();
            Invalidate();
            RaiseColorChanged(commit);
        }

        private void RaiseColorChanged(bool commit)
        {
            ColorChanged?.Invoke(this, EventArgs.Empty);
            if (commit)
                ColorCommitted?.Invoke(this, EventArgs.Empty);
        }

        private static Color ColorFromHue(float hue) =>
            ColorFromHsv(hue, 1f, 1f);

        private static void RgbToHsv(Color color, out float h, out float s, out float v)
        {
            float r = color.R / 255f;
            float g = color.G / 255f;
            float b = color.B / 255f;

            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;

            v = max;
            s = max <= 0.00001f ? 0f : delta / max;

            if (delta <= 0.00001f)
            {
                h = 0f;
                return;
            }

            if (Math.Abs(max - r) < 0.00001f)
                h = 60f * (((g - b) / delta) % 6f);
            else if (Math.Abs(max - g) < 0.00001f)
                h = 60f * (((b - r) / delta) + 2f);
            else
                h = 60f * (((r - g) / delta) + 4f);

            if (h < 0f) h += 360f;
        }

        private static Color ColorFromHsv(float hue, float saturation, float value)
        {
            float c = value * saturation;
            float x = c * (1f - Math.Abs((hue / 60f % 2f) - 1f));
            float m = value - c;

            (float r, float g, float b) = hue switch
            {
                < 60f => (c, x, 0f),
                < 120f => (x, c, 0f),
                < 180f => (0f, c, x),
                < 240f => (0f, x, c),
                < 300f => (x, 0f, c),
                _ => (c, 0f, x)
            };

            return Color.FromArgb(
                (int)Math.Round((r + m) * 255f),
                (int)Math.Round((g + m) * 255f),
                (int)Math.Round((b + m) * 255f));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            InvalidateSbCache();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppTheme.Changed -= _themeChangedHandler;
                InvalidateSbCache();
            }
            base.Dispose(disposing);
        }
    }
}
