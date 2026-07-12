using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class PredatorButton : Control
    {
        private bool _isHover;
        private bool _isActive;
        private bool _hoverEnabled = true;
        private Color? _customActiveColor;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; Invalidate(); }
        }

        /// <summary>When false, mouse-over does not change appearance (status indicators).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool HoverEnabled
        {
            get => _hoverEnabled;
            set { _hoverEnabled = value; if (!_hoverEnabled) _isHover = false; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? CustomActiveColor
        {
            get => _customActiveColor;
            set { _customActiveColor = value; Invalidate(); }
        }

        private string _leadingText = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string LeadingText
        {
            get => _leadingText;
            set { _leadingText = value ?? ""; Invalidate(); }
        }

        public PredatorButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);

            Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            Size = new Size(96, 40);
            Cursor = Cursors.Hand;

            AppTheme.Changed += OnThemeChanged;
        }

        private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            g.Clear(Parent?.BackColor ?? AppTheme.FormBackground);

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = RoundedRect(rect, 4);

            Color bg, border, textColor;
            float borderWidth;

            if (!Enabled)
            {
                bg = AppTheme.ButtonDisabled;
                border = AppTheme.ButtonBorderDisabled;
                textColor = AppTheme.ButtonTextDisabled;
                borderWidth = 1f;
            }
            else if (_isActive)
            {
                bg = AppTheme.ButtonActiveBackground;
                border = AppTheme.Accent;
                textColor = _customActiveColor ?? AppTheme.Accent;
                borderWidth = 1.5f;
            }
            else if (_isHover && _hoverEnabled)
            {
                bg = AppTheme.ButtonHover;
                border = AppTheme.ButtonBorderHover;
                textColor = AppTheme.ButtonTextHover;
                borderWidth = 1f;
            }
            else
            {
                bg = AppTheme.ButtonBackground;
                border = AppTheme.ButtonBorder;
                textColor = AppTheme.ButtonText;
                borderWidth = 1f;
            }

            using (var bgBrush = new SolidBrush(bg))
                g.FillPath(bgBrush, path);

            using (var pen = new Pen(border, borderWidth))
                g.DrawPath(pen, path);

            if (!string.IsNullOrEmpty(LeadingText))
            {
                var prefixRect = new Rectangle(12, 0, Width - 12, Height);
                TextRenderer.DrawText(g, LeadingText, Font, prefixRect, AppTheme.SecondaryText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            if (Enabled && _hoverEnabled) { _isHover = true; Invalidate(); }
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled) { _isHover = false; Cursor = Cursors.Default; }
            else { Cursor = Cursors.Hand; }
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                AppTheme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }
}
