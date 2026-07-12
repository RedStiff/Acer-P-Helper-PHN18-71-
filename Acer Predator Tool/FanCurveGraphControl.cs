using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class FanCurveGraphControl : Control
    {
        private const int ColorBarHeight = 8;
        private const int NodeRadius = 6;
        private const int HitRadius = 12;

        private FanCurveConfig _config = new();
        private int _dragIndex = -1;
        private int _hoverIndex = -1;
        private int? _tooltipIndex;
        private FanKind _fanKind = FanKind.Cpu;

        private struct GraphLayout
        {
            public Rectangle Plot;
            public Font TickFont;
            public Font UnitFont;
            public int XTickStep;
            public int ColorBarY;
        }

        private GraphLayout _layout;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public FanCurveConfig Config
        {
            get => _config;
            set => BindConfig(value);
        }

        public void BindConfig(FanCurveConfig config)
        {
            _config = config;
            _config.EnsureSorted();
            Invalidate();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public FanKind FanKind
        {
            get => _fanKind;
            set
            {
                if (_fanKind == value) return;
                _fanKind = value;
                if (_tooltipIndex.HasValue) Invalidate();
            }
        }

        public event EventHandler? CurveChanged;

        public FanCurveGraphControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(640, 420);
            Cursor = Cursors.Cross;
            AppTheme.Changed += (_, _) => Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate(true);
        }

        private int MaxTemp => Math.Max(100, _config.MaxTemperature);

        private GraphLayout ComputeLayout(Graphics g)
        {
            float scale = Math.Clamp(Math.Min(Width, Height) / 480f, 0.7f, 1.3f);
            float tickSize = Math.Clamp(7.5f * scale, 6.5f, 9f);
            float unitSize = Math.Clamp(8f * scale, 7f, 9.5f);

            var tickFont = new Font("Segoe UI", tickSize);
            var unitFont = new Font("Segoe UI", unitSize);

            float yLabelW = g.MeasureString("100", tickFont).Width;
            int marginLeft = Math.Max(36, (int)Math.Ceiling(yLabelW) + 14);

            int tickH = (int)Math.Ceiling(tickFont.GetHeight(g));
            int marginBottom = tickH + 8 + ColorBarHeight + 6 + tickH + 4;
            int marginTop = tickH + 14;
            int marginRight = 12;

            int plotW = Math.Max(80, Width - marginLeft - marginRight);
            int plotH = Math.Max(60, Height - marginTop - marginBottom);

            var plot = new Rectangle(marginLeft, marginTop, plotW, plotH);

            int xTickStep = plot.Width switch
            {
                < 280 => 25,
                < 420 => 20,
                < 560 => 15,
                _ => 10
            };
            if (MaxTemp % xTickStep != 0 && xTickStep == 10)
                xTickStep = 10;

            int colorBarY = plot.Bottom + tickH + 6;

            return new GraphLayout
            {
                Plot = plot,
                TickFont = tickFont,
                UnitFont = unitFont,
                XTickStep = xTickStep,
                ColorBarY = colorBarY
            };
        }

        private void DisposeLayoutFonts(GraphLayout prev, GraphLayout next)
        {
            if (prev.TickFont != null && !ReferenceEquals(prev.TickFont, next.TickFont))
                prev.TickFont.Dispose();
            if (prev.UnitFont != null && !ReferenceEquals(prev.UnitFont, next.UnitFont))
                prev.UnitFont.Dispose();
        }

        private PointF PointToPixel(FanCurvePoint p) =>
            new(TempToX(p.Temperature), FanToY(p.FanPercent));

        private float TempToX(int temp) =>
            _layout.Plot.Left + temp / (float)MaxTemp * _layout.Plot.Width;

        private float FanToY(int fan) =>
            _layout.Plot.Bottom - fan / 100f * _layout.Plot.Height;

        private (int temp, int fan) PixelToPoint(Point pt)
        {
            var plot = _layout.Plot;
            float temp = (pt.X - plot.Left) / (float)plot.Width * MaxTemp;
            float fan = (plot.Bottom - pt.Y) / (float)plot.Height * 100f;
            return (
                Math.Clamp((int)Math.Round(temp), 0, MaxTemp),
                FanRpmMap.SnapPercent((int)Math.Round(fan)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 120 || Height < 100)
                return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent?.BackColor ?? AppTheme.FormBackground);

            var prev = _layout;
            _layout = ComputeLayout(g);
            DisposeLayoutFonts(prev, _layout);

            DrawAxes(g);
            DrawColorBar(g);
            DrawGrid(g);
            DrawCurve(g);
            DrawNodes(g);
            DrawLiveMarker(g);
            DrawTooltip(g);
        }

        private void DrawAxes(Graphics g)
        {
            using var textBrush = new SolidBrush(AppTheme.SecondaryText);
            using var axisPen = new Pen(AppTheme.Separator);

            var plot = _layout.Plot;
            var font = _layout.TickFont;
            var labelFont = _layout.UnitFont;

            g.DrawRectangle(axisPen, plot);

            for (int t = 0; t <= MaxTemp; t += _layout.XTickStep)
            {
                float x = TempToX(t);
                g.DrawLine(axisPen, x, plot.Bottom, x, plot.Bottom + 3);
                string label = t.ToString();
                var size = g.MeasureString(label, font);
                float lx = SafeClamp(x - size.Width / 2f, 0, Width - size.Width);
                g.DrawString(label, font, textBrush, lx, plot.Bottom + 5);
            }

            if (MaxTemp % _layout.XTickStep != 0)
            {
                float x = TempToX(MaxTemp);
                g.DrawLine(axisPen, x, plot.Bottom, x, plot.Bottom + 3);
                string label = MaxTemp.ToString();
                var size = g.MeasureString(label, font);
                float lx = SafeClamp(x - size.Width / 2f, 0, Width - size.Width);
                g.DrawString(label, font, textBrush, lx, plot.Bottom + 5);
            }

            for (int f = 0; f <= 100; f += FanRpmMap.EcStepPercent)
            {
                float y = FanToY(f);
                bool major = f % 20 == 0;
                g.DrawLine(axisPen, plot.Left - (major ? 3 : 2), y, plot.Left, y);
                if (!major) continue;

                string label = f.ToString();
                var size = g.MeasureString(label, font);
                float labelX = plot.Left - size.Width - 8;
                float labelY = f switch
                {
                    0 => plot.Bottom - size.Height,
                    100 => plot.Top + 1,
                    _ => y - size.Height / 2f
                };
                labelY = Math.Clamp(labelY, 1, Height - size.Height - 1);
                g.DrawString(label, font, textBrush, labelX, labelY);
            }

            var fanLabelSize = g.MeasureString("Fan %", labelFont);
            float fanLabelY = Math.Max(2, plot.Top - fanLabelSize.Height - 4);
            g.DrawString("Fan %", labelFont, textBrush, plot.Left - fanLabelSize.Width - 4, fanLabelY);

            var tempLabelSize = g.MeasureString("(°C)", labelFont);
            float tempLabelX = Math.Min(plot.Right - tempLabelSize.Width, Width - tempLabelSize.Width - 2);
            float tempLabelY = Math.Min(_layout.ColorBarY + ColorBarHeight + 2, Height - tempLabelSize.Height - 1);
            g.DrawString("(°C)", labelFont, textBrush, tempLabelX, tempLabelY);
        }

        private void DrawColorBar(Graphics g)
        {
            var plot = _layout.Plot;
            int barY = _layout.ColorBarY;
            if (barY + ColorBarHeight > Height - 2) return;

            var barRect = new Rectangle(plot.Left, barY, plot.Width, ColorBarHeight);

            using var brush = new LinearGradientBrush(barRect,
                Color.FromArgb(80, 180, 80), Color.FromArgb(220, 60, 50), LinearGradientMode.Horizontal);
            var blend = new ColorBlend(5)
            {
                Colors =
                [
                    Color.FromArgb(80, 180, 80),
                    Color.FromArgb(200, 200, 60),
                    Color.FromArgb(240, 160, 40),
                    Color.FromArgb(230, 80, 40),
                    Color.FromArgb(200, 40, 40)
                ],
                Positions = [0f, 0.22f, 0.30f, 0.55f, 1f]
            };
            brush.InterpolationColors = blend;
            g.FillRectangle(brush, barRect);
        }

        private void DrawGrid(Graphics g)
        {
            using var gridPen = new Pen(AppTheme.IsDark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0));
            var plot = _layout.Plot;

            for (int t = _layout.XTickStep; t < MaxTemp; t += _layout.XTickStep)
            {
                float x = TempToX(t);
                g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            }

            using var minorPen = new Pen(AppTheme.IsDark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0));
            for (int f = FanRpmMap.EcStepPercent; f < 100; f += FanRpmMap.EcStepPercent)
            {
                float y = FanToY(f);
                g.DrawLine(f % 20 == 0 ? gridPen : minorPen, plot.Left, y, plot.Right, y);
            }
        }

        private void DrawCurve(Graphics g)
        {
            DrawCurveBody(g);

            int? liveTemp = Tag is int t && t >= 0 ? t : null;
            if (liveTemp.HasValue && liveTemp.Value > MaxTemp)
            {
                var plot = _layout.Plot;
                using var warnFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                g.DrawString($"{liveTemp.Value}°C", warnFont, new SolidBrush(Color.FromArgb(255, 100, 80)),
                    plot.Right - 42, plot.Top + 2);
            }
        }

        private void DrawLiveMarker(Graphics g)
        {
            int? liveTemp = Tag is int t && t >= 0 ? t : null;
            if (!liveTemp.HasValue) return;

            var plot = _layout.Plot;
            int clampedTemp = Math.Min(liveTemp.Value, MaxTemp);
            // Sit on the drawn curve (ideal); EC still receives snapped Evaluate() from the controller.
            int fan = FanCurveEvaluator.EvaluateIdeal(_config, liveTemp.Value);
            float x = TempToX(clampedTemp);
            float y = FanToY(fan);

            Color liveColor = AppTheme.Accent;
            var bright = Color.FromArgb(255, liveColor);

            using (var glow = new SolidBrush(Color.FromArgb(70, bright)))
                g.FillEllipse(glow, x - 10, y - 10, 20, 20);

            using (var vPen = new Pen(Color.FromArgb(240, bright), 2f) { DashStyle = DashStyle.Dash, DashPattern = [4f, 3f] })
                g.DrawLine(vPen, x, plot.Top, x, plot.Bottom);

            using (var hPen = new Pen(Color.FromArgb(240, bright), 2f) { DashStyle = DashStyle.Dash, DashPattern = [4f, 3f] })
                g.DrawLine(hPen, plot.Left, y, plot.Right, y);

            using (var dot = new SolidBrush(bright))
                g.FillEllipse(dot, x - 6, y - 6, 12, 12);

            using (var ring = new Pen(AppTheme.IsDark ? Color.White : Color.FromArgb(30, 30, 30), 2f))
                g.DrawEllipse(ring, x - 6, y - 6, 12, 12);
        }

        private void DrawCurveBody(Graphics g)
        {
            _config.EnsureSorted();
            var points = _config.Points;
            if (points.Count < 2) return;

            using var curvePen = new Pen(AppTheme.Accent, 2.5f) { LineJoin = LineJoin.Round };

            if (_config.SlopeMode)
            {
                using var path = new GraphicsPath();
                path.AddLines(points.Select(p => PointToPixel(p)).ToArray());
                g.DrawPath(curvePen, path);
                return;
            }

            // Stair: hold previous %, vertical riser at the next control point (the knee).
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p0 = PointToPixel(points[i]);
                var p1 = PointToPixel(points[i + 1]);
                g.DrawLine(curvePen, p0.X, p0.Y, p1.X, p0.Y);
                g.DrawLine(curvePen, p1.X, p0.Y, p1.X, p1.Y);
            }
        }

        private void DrawNodes(Graphics g)
        {
            for (int i = 0; i < _config.Points.Count; i++)
            {
                var pt = PointToPixel(_config.Points[i]);
                bool isAnchor = i == 0 || i == _config.Points.Count - 1;
                bool active = i == _dragIndex || i == _hoverIndex;
                Color fill = active ? AppTheme.Accent
                    : isAnchor ? Color.FromArgb(255, 140, 60)
                    : AppTheme.IsDark ? Color.FromArgb(220, 220, 220) : Color.FromArgb(80, 80, 80);
                if (i == _tooltipIndex) fill = Color.FromArgb(255, 80, 60);

                int radius = isAnchor ? NodeRadius + 1 : NodeRadius;
                using var brush = new SolidBrush(fill);
                g.FillEllipse(brush, pt.X - radius, pt.Y - radius, radius * 2, radius * 2);
                using var border = new Pen(AppTheme.IsDark ? Color.White : Color.FromArgb(40, 40, 40), 1.5f);
                g.DrawEllipse(border, pt.X - radius, pt.Y - radius, radius * 2, radius * 2);
            }
        }

        private void DrawTooltip(Graphics g)
        {
            if (_tooltipIndex is not int idx || idx < 0 || idx >= _config.Points.Count) return;

            var p = _config.Points[idx];
            int fanPercent = FanRpmMap.SnapPercent(p.FanPercent);
            int rpm = FanCurveEvaluator.EstimateRpm(_fanKind, fanPercent);
            string text = $"{p.Temperature}°C · {fanPercent}% · ~{rpm} RPM";
            using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            var size = g.MeasureString(text, font);
            var pixel = PointToPixel(p);
            float tx = pixel.X + 10;
            float ty = pixel.Y - size.Height - 12;
            if (tx + size.Width > Width - 8) tx = pixel.X - size.Width - 10;
            ty = Math.Max(2, ty);

            var rect = new RectangleF(tx, ty, size.Width + 10, size.Height + 6);
            using var bg = new SolidBrush(AppTheme.IsDark ? Color.FromArgb(230, 50, 50, 55) : Color.FromArgb(240, 255, 255, 255));
            using var border = new Pen(AppTheme.Accent);
            g.FillRectangle(bg, rect);
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            g.DrawString(text, font, new SolidBrush(AppTheme.PrimaryText), tx + 5, ty + 2);
        }

        private static float SafeClamp(float value, float min, float max) =>
            max < min ? min : Math.Clamp(value, min, max);

        private void EnsureLayout()
        {
            if (_layout.Plot.Width > 0 && _layout.Plot.Height > 0) return;
            using var g = CreateGraphics();
            var prev = _layout;
            _layout = ComputeLayout(g);
            DisposeLayoutFonts(prev, _layout);
        }

        private int HitTest(Point location)
        {
            EnsureLayout();
            for (int i = _config.Points.Count - 1; i >= 0; i--)
            {
                var pt = PointToPixel(_config.Points[i]);
                float dx = location.X - pt.X;
                float dy = location.Y - pt.Y;
                if (dx * dx + dy * dy <= HitRadius * HitRadius) return i;
            }
            return -1;
        }

        private bool IsInPlotArea(Point location)
        {
            EnsureLayout();
            return _layout.Plot.Contains(location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int hit = HitTest(e.Location);
                if (hit >= 0 && _config.TryRemovePoint(hit))
                {
                    CurveChanged?.Invoke(this, EventArgs.Empty);
                    Invalidate();
                }
                return;
            }

            if (e.Button != MouseButtons.Left) return;
            _dragIndex = HitTest(e.Location);
            if (_dragIndex >= 0)
            {
                _tooltipIndex = _dragIndex;
                Capture = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (HitTest(e.Location) >= 0) return;
            if (!IsInPlotArea(e.Location)) return;

            var (temp, fan) = PixelToPoint(e.Location);
            if (_config.TryAddPoint(temp, fan))
            {
                CurveChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragIndex >= 0)
            {
                var (temp, fan) = PixelToPoint(e.Location);
                var point = _config.Points[_dragIndex];

                int minTemp = _dragIndex > 0
                    ? _config.Points[_dragIndex - 1].Temperature + FanCurveConfig.MinPointSpacing
                    : 0;
                int maxTemp = _dragIndex < _config.Points.Count - 1
                    ? _config.Points[_dragIndex + 1].Temperature - FanCurveConfig.MinPointSpacing
                    : MaxTemp;

                if (_dragIndex == 0) temp = 0;
                else if (_dragIndex == _config.Points.Count - 1) temp = MaxTemp;
                else temp = Math.Clamp(temp, minTemp, maxTemp);

                point.Temperature = temp;
                point.FanPercent = fan;
                _config.EnsureSorted();
                _dragIndex = _config.Points.IndexOf(point);
                CurveChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
            else
            {
                int hit = HitTest(e.Location);
                if (hit != _hoverIndex)
                {
                    _hoverIndex = hit;
                    _tooltipIndex = hit >= 0 ? hit : null;
                    Cursor = hit >= 0 ? Cursors.Hand : Cursors.Cross;
                    Invalidate();
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragIndex >= 0)
            {
                _dragIndex = -1;
                Capture = false;
                CurveChanged?.Invoke(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverIndex = -1;
            _tooltipIndex = null;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppTheme.Changed -= (_, _) => Invalidate();
                _layout.TickFont?.Dispose();
                _layout.UnitFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
