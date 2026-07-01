using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class KeyboardColorForm : ResizableBorderlessForm
    {
        private const int FooterHeight = 56;
        private const int ColorPickerHeight = 196;
        private const int ZoneBarHeight = 40;
        private const int BrightSectionHeight = 56;
        private const int SectionGap = 10;

        private static readonly Font FontTitle = new("Segoe UI", 11f, FontStyle.Bold);
        private static readonly Font FontSection = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontBody = new("Segoe UI", 9.5f, FontStyle.Regular);

        private readonly KeyboardColorSettings _settings;

        private Panel _pnlTitle = null!;
        private Panel _pnlContent = null!;
        private TableLayoutPanel _contentLayout = null!;
        private Panel _pnlBottom = null!;
        private FlowLayoutPanel _bottomFlow = null!;
        private Panel _pnlPreviewHost = null!;
        private Panel _pnlZoneBar = null!;
        private Panel _pnlFooter = null!;
        private Label _lblClose = null!;
        private KeyboardPreviewControl _preview = null!;
        private KeyboardColorPickerControl _colorPicker = null!;
        private CheckBox _chkFourZone = null!;
        private PredatorSlider _brightnessSlider = null!;
        private Label _lblBrightness = null!;
        private Label _lblZoneHint = null!;
        private readonly PredatorButton[] _zoneButtons = new PredatorButton[4];
        private bool _uiReady;

        public KeyboardColorSettings Settings => _settings;
        public bool Applied { get; private set; }

        public KeyboardColorForm(KeyboardColorSettings initial)
        {
            _settings = initial;
            var ui = UiSettings.Load();

            Text = "Keyboard Color";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            ClientSize = ui.GetKeyboardColorClientSize();
            DoubleBuffered = true;
            ShowInTaskbar = false;
            BorderlessResizeHelper.ApplyTo(this, UiSettings.MinKeyboardColorWidth, UiSettings.MinKeyboardColorHeight);
            try { Icon = new Icon("appicon.ico"); } catch { }

            BuildUI();
            ApplyTheme();
            AppTheme.Changed += OnThemeChanged;
            SyncUIFromSettings();
            LayoutTitleBar(20);
            LayoutBottomFlow();
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

        private void BuildUI()
        {
            int pad = 20;

            _pnlContent = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(pad, 8, pad, 6),
                BackColor = Color.Transparent
            };
            Controls.Add(_pnlContent);

            _pnlFooter = new Panel
            {
                Height = FooterHeight,
                Dock = DockStyle.Bottom,
                BackColor = Color.Transparent,
                Padding = new Padding(pad, 4, pad, 12)
            };
            Controls.Add(_pnlFooter);

            _pnlTitle = new Panel { Height = 42, Dock = DockStyle.Top, BackColor = AppTheme.TitleBarBackground };
            _pnlTitle.MouseDown += TitleBar_MouseDown;
            Controls.Add(_pnlTitle);

            var lblTitle = new Label
            {
                Text = "Keyboard Color",
                Font = FontTitle,
                ForeColor = AppTheme.PrimaryText,
                AutoSize = true,
                Location = new Point(pad, 11),
                BackColor = Color.Transparent
            };
            lblTitle.MouseDown += TitleBar_MouseDown;
            _pnlTitle.Controls.Add(lblTitle);

            _lblClose = new Label
            {
                Text = "\u2715",
                ForeColor = AppTheme.CaptionButton,
                Font = new Font("Segoe UI", 10f),
                AutoSize = true,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _lblClose.Click += (_, _) => Close();
            _lblClose.MouseEnter += (_, _) => _lblClose.ForeColor = AppTheme.CaptionCloseHover;
            _lblClose.MouseLeave += (_, _) => _lblClose.ForeColor = AppTheme.CaptionButton;
            _pnlTitle.Controls.Add(_lblClose);
            _pnlTitle.Resize += (_, _) => LayoutTitleBar(pad);

            _contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _pnlContent.Controls.Add(_contentLayout);

            _pnlPreviewHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 4),
                MinimumSize = new Size(0, 180)
            };
            _contentLayout.Controls.Add(_pnlPreviewHost, 0, 0);

            _preview = new KeyboardPreviewControl { Dock = DockStyle.Fill };
            _preview.ZoneSelected += (_, _) =>
            {
                SyncZoneButtons();
                SyncPickerFromSelection();
            };
            _pnlPreviewHost.Controls.Add(_preview);

            _pnlBottom = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 6, 0, 0)
            };
            _contentLayout.Controls.Add(_pnlBottom, 0, 1);

            _bottomFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, SectionGap, 0, SectionGap),
                Dock = DockStyle.Top
            };
            _pnlBottom.Controls.Add(_bottomFlow);

            _colorPicker = new KeyboardColorPickerControl
            {
                Height = ColorPickerHeight,
                MinimumSize = new Size(360, ColorPickerHeight),
                Margin = new Padding(0, 0, 0, SectionGap)
            };
            _colorPicker.ColorChanged += (_, _) => ApplyPickerColor();
            _bottomFlow.Controls.Add(_colorPicker);

            _chkFourZone = new CheckBox
            {
                Text = "4-zone lighting (off = solid color)",
                Height = 28,
                Font = FontBody,
                ForeColor = AppTheme.PrimaryText,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, SectionGap)
            };
            _chkFourZone.CheckedChanged += (_, _) =>
            {
                _settings.FourZone = _chkFourZone.Checked;
                _preview.FourZone = _chkFourZone.Checked;
                UpdateZoneControlsVisible();
                SyncPickerFromSelection();
            };
            _bottomFlow.Controls.Add(_chkFourZone);

            _pnlZoneBar = new Panel
            {
                Height = ZoneBarHeight,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, SectionGap)
            };
            _bottomFlow.Controls.Add(_pnlZoneBar);

            for (int i = 0; i < 4; i++)
            {
                int zone = i;
                _zoneButtons[i] = new PredatorButton
                {
                    Text = $"Zone {i + 1}",
                    Height = 32,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left
                };
                _zoneButtons[i].Click += (_, _) =>
                {
                    _preview.SelectedZone = zone;
                    SyncZoneButtons();
                    SyncPickerFromSelection();
                };
                _pnlZoneBar.Controls.Add(_zoneButtons[i]);
            }

            _pnlZoneBar.Resize += (_, _) => LayoutZoneButtons();

            _lblZoneHint = new Label
            {
                Text = "Click a zone on the keyboard or use the buttons above.",
                Height = 20,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = AppTheme.SecondaryText,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, SectionGap)
            };
            _bottomFlow.Controls.Add(_lblZoneHint);

            var pnlBright = new Panel
            {
                Height = BrightSectionHeight,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            _bottomFlow.Controls.Add(pnlBright);

            var pnlBrightHeader = new Panel
            {
                Height = 20,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent
            };
            pnlBright.Controls.Add(pnlBrightHeader);

            var lblBrightTitle = new Label
            {
                Text = "BRIGHTNESS",
                AutoSize = true,
                Font = FontSection,
                ForeColor = AppTheme.SectionHeader,
                BackColor = Color.Transparent,
                Location = new Point(0, 2)
            };
            pnlBrightHeader.Controls.Add(lblBrightTitle);

            _lblBrightness = new Label
            {
                Text = "100%",
                AutoSize = true,
                Font = FontBody,
                ForeColor = AppTheme.SecondaryText,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            pnlBrightHeader.Controls.Add(_lblBrightness);

            _brightnessSlider = new PredatorSlider
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Minimum = 1,
                Maximum = 100,
                Value = 100
            };
            _brightnessSlider.ValueChanged += (_, _) =>
            {
                _settings.Brightness = (byte)_brightnessSlider.Value;
                _preview.Brightness = _brightnessSlider.Value;
                _lblBrightness.Text = $"{_brightnessSlider.Value}%";
            };
            pnlBright.Controls.Add(_brightnessSlider);
            pnlBright.Resize += (_, _) => LayoutBrightnessRow(pnlBrightHeader);

            _contentLayout.Resize += (_, _) => LayoutBottomFlow();
            _uiReady = true;

            var btnApply = new PredatorButton
            {
                Text = "Apply",
                Size = new Size(120, 36),
                Location = new Point(0, 4),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnApply.Click += (_, _) =>
            {
                _preview.ExportSettings(_settings);
                Applied = true;
                Close();
            };
            _pnlFooter.Controls.Add(btnApply);

            var btnCancel = new PredatorButton
            {
                Text = "Cancel",
                Size = new Size(120, 36),
                Location = new Point(130, 4),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnCancel.Click += (_, _) => Close();
            _pnlFooter.Controls.Add(btnCancel);

            Paint += (_, e) =>
            {
                using var pen = new Pen(AppTheme.Separator);
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                e.Graphics.DrawLine(pen, 0, _pnlTitle.Bottom - 1, ClientSize.Width, _pnlTitle.Bottom - 1);
                e.Graphics.DrawLine(pen, 0, _pnlFooter.Top, ClientSize.Width, _pnlFooter.Top);
            };
        }

        private void LayoutTitleBar(int pad)
        {
            _lblClose.Location = new Point(_pnlTitle.Width - pad - 12, 9);
        }

        private void LayoutBrightnessRow(Panel pnlBrightHeader)
        {
            if (_lblBrightness == null) return;
            _lblBrightness.Location = new Point(
                Math.Max(0, pnlBrightHeader.ClientSize.Width - _lblBrightness.Width),
                2);
        }

        private void LayoutBottomFlow()
        {
            if (!_uiReady || _bottomFlow == null || _contentLayout == null) return;

            int width = Math.Max(320, _contentLayout.ClientSize.Width);
            _bottomFlow.Width = width;

            foreach (Control child in _bottomFlow.Controls)
                child.Width = width - _bottomFlow.Padding.Horizontal - child.Margin.Horizontal;

            LayoutZoneButtons();
        }

        private void LayoutZoneButtons()
        {
            if (_pnlZoneBar == null || _pnlZoneBar.Width <= 0 || _zoneButtons[0] == null) return;

            const int gap = 8;
            int buttonW = Math.Max(72, (_pnlZoneBar.Width - gap * 3) / 4);
            for (int i = 0; i < 4; i++)
                _zoneButtons[i].SetBounds(i * (buttonW + gap), 4, buttonW, 32);
        }

        private void UpdateZoneControlsVisible()
        {
            bool show = _chkFourZone.Checked;
            _pnlZoneBar.Visible = show;
            _lblZoneHint.Visible = show;
            LayoutZoneButtons();
            LayoutBottomFlow();
        }

        private void SyncUIFromSettings()
        {
            _preview.LoadSettings(_settings);
            _chkFourZone.Checked = _settings.FourZone;
            _preview.FourZone = _settings.FourZone;
            _brightnessSlider.Value = _settings.Brightness;
            _preview.Brightness = _settings.Brightness;
            _lblBrightness.Text = $"{_settings.Brightness}%";
            UpdateZoneControlsVisible();
            SyncZoneButtons();
            SyncPickerFromSelection();
        }

        private void SyncZoneButtons()
        {
            if (_zoneButtons[0] == null) return;

            for (int i = 0; i < 4; i++)
            {
                _zoneButtons[i].IsActive = _preview.SelectedZone == i;
                _zoneButtons[i].CustomActiveColor = _preview.GetZoneColor(i);
            }
        }

        private void SyncPickerFromSelection()
        {
            Color current = _settings.FourZone
                ? _preview.GetZoneColor(_preview.SelectedZone)
                : _settings.SolidColor;
            _colorPicker.SetColor(current);
        }

        private void ApplyPickerColor()
        {
            Color picked = _colorPicker.SelectedColor;
            if (_settings.FourZone)
            {
                int z = _preview.SelectedZone;
                _preview.SetZoneColor(z, picked);
                _settings.ZoneColors[z] = picked;
            }
            else
            {
                _settings.SolidColor = picked;
                _preview.SolidColor = picked;
            }
            SyncZoneButtons();
        }

        private void ApplyTheme()
        {
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            _pnlTitle.BackColor = AppTheme.TitleBarBackground;
            _chkFourZone.ForeColor = AppTheme.PrimaryText;
            _preview.BackColor = AppTheme.FormBackground;
            _colorPicker.BackColor = AppTheme.FormBackground;
            _pnlPreviewHost.BackColor = AppTheme.FormBackground;
            Invalidate(true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UiSettings.SaveKeyboardColorWindowSize(ClientSize);
            AppTheme.Changed -= OnThemeChanged;
            base.OnFormClosed(e);
        }

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        public static bool TryShow(IWin32Window owner, KeyboardColorSettings settings, out KeyboardColorSettings result)
        {
            using var form = new KeyboardColorForm(settings);
            form.ShowDialog(owner);
            if (form.Applied)
            {
                result = form.Settings;
                return true;
            }
            result = settings;
            return false;
        }
    }
}
