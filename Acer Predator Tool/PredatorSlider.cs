using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class PredatorSlider : Control
    {
        private int _value = 100;
        private int _minimum = 0;
        private int _maximum = 100;
        private int _step = 1;
        private bool _isDragging;

        private const int TrackHeight = 4;
        private const int ThumbRadius = 7;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set
            {
                int snapped = Snap(value);
                if (snapped != _value) { _value = snapped; Invalidate(); }
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                _value = Snap(_value);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = value;
                _value = Snap(_value);
                Invalidate();
            }
        }

        /// <summary>Discrete step size. 1 = continuous; 10 = jump in 10% bands.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Step
        {
            get => _step;
            set
            {
                _step = Math.Max(1, value);
                _value = Snap(_value);
                Invalidate();
            }
        }

        public event EventHandler? ValueChanged;

        public event EventHandler? ValueCommitted;

        public PredatorSlider()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Selectable, true);

            Size = new Size(300, 28);
            Cursor = Cursors.Hand;

            AppTheme.Changed += OnThemeChanged;
        }

        private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

        private int TrackLeft => ThumbRadius;
        private int TrackRight => Width - ThumbRadius;
        private int TrackWidth => TrackRight - TrackLeft;
        private float Fraction => (_maximum > _minimum) ? (float)(_value - _minimum) / (_maximum - _minimum) : 0;
        private int ThumbX => TrackLeft + (int)(TrackWidth * Fraction);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? AppTheme.FormBackground);

            int cy = Height / 2;
            int trackY = cy - TrackHeight / 2;
            int fillWidth = ThumbX - TrackLeft;

            using (var brush = new SolidBrush(AppTheme.SliderTrack))
                g.FillRectangle(brush, TrackLeft, trackY, TrackWidth, TrackHeight);

            if (fillWidth > 0)
            {
                using var brush = new SolidBrush(AppTheme.SliderFill);
                g.FillRectangle(brush, TrackLeft, trackY, fillWidth, TrackHeight);
            }

            using (var thumbBrush = new SolidBrush(AppTheme.SliderThumb))
                g.FillEllipse(thumbBrush, ThumbX - ThumbRadius, cy - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);

            int innerR = ThumbRadius - 3;
            if (innerR > 0)
            {
                Color innerColor = AppTheme.IsDark
                    ? Color.FromArgb(80, 255, 255, 255)
                    : Color.FromArgb(120, 255, 255, 255);
                using var innerBrush = new SolidBrush(innerColor);
                g.FillEllipse(innerBrush, ThumbX - innerR, cy - innerR, innerR * 2, innerR * 2);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                Capture = true;
                UpdateValueFromMouse(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging) UpdateValueFromMouse(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                Capture = false;
                ValueCommitted?.Invoke(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        private int Snap(int value)
        {
            value = Math.Clamp(value, _minimum, _maximum);
            if (_step <= 1 || _maximum <= _minimum) return value;

            int span = value - _minimum;
            int steps = (int)Math.Round(span / (double)_step);
            int snapped = _minimum + steps * _step;
            if (snapped > _maximum) snapped = _maximum;
            // Keep the true maximum reachable (e.g. 100 with step 10).
            if (value == _maximum) snapped = _maximum;
            return Math.Clamp(snapped, _minimum, _maximum);
        }

        private void UpdateValueFromMouse(int mouseX)
        {
            float fraction = (float)(mouseX - TrackLeft) / Math.Max(1, TrackWidth);
            fraction = Math.Clamp(fraction, 0f, 1f);
            int raw = _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
            int newValue = Snap(raw);
            if (newValue != _value)
            {
                _value = newValue;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = _step;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down)
            {
                Value = _value - step;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                ValueCommitted?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up)
            {
                Value = _value + step;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                ValueCommitted?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                AppTheme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }
}
