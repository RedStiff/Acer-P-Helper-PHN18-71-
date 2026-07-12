using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public sealed class LightingPanelControl : UserControl
    {
        private const int ColorPickerHeight = 180;
        private const int LogoColorPickerHeight = 150;
        private const int ZoneBarHeight = 36;
        private const int SliderSectionHeight = 48;
        private const int ModeSectionHeight = 56;
        private const int PreviewHeight = 140;
        private const int SectionGap = 8;
        private const int ContentPad = 14;
        private const int LogoEnableRowHeight = 34;

        private static readonly Font FontSection = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontBody = new("Segoe UI", 9.5f, FontStyle.Regular);
        private static readonly string[] RgbModeNames =
            { "Static", "Breathing", "Neon", "Wave", "Shifting", "Zoom", "Meteor", "Twinkling" };

        private readonly KeyboardColorSettings _settings;
        private readonly LogoLightingSettings _logo;
        private int _rgbMode;
        private int _speed;
        private bool _suppressLiveEvents;

        private Panel _pnlHeader = null!;
        private Panel _pnlScroll = null!;
        private FlowLayoutPanel _stack = null!;
        private Panel _pnlPreviewHost = null!;
        private Panel _pnlEffect = null!;
        private Panel _pnlZoneBar = null!;
        private Panel _pnlSpeed = null!;
        private Panel _pnlBright = null!;
        private Panel _pnlLogoSep = null!;
        private Panel _pnlLogoEnable = null!;
        private Panel _pnlLogoBright = null!;
        private Label _lblClose = null!;
        private KeyboardPreviewControl _preview = null!;
        private KeyboardColorPickerControl _colorPicker = null!;
        private KeyboardColorPickerControl _logoColorPicker = null!;
        private CheckBox _chkFourZone = null!;
        private PredatorSwitch _switchLogo = null!;
        private PredatorSlider _brightnessSlider = null!;
        private PredatorSlider _speedSlider = null!;
        private PredatorSlider _logoBrightnessSlider = null!;
        private PredatorDropDown _rgbModeDropDown = null!;
        private Label _lblBrightness = null!;
        private Label _lblSpeed = null!;
        private Label _lblLogoBrightness = null!;
        private Label _lblZoneHint = null!;
        private Label _lblLogoTitle = null!;
        private readonly PredatorButton[] _zoneButtons = new PredatorButton[4];
        private bool _uiReady;

        public event Action<LightingSettingsSnapshot>? SettingsLiveChanged;
        /// <summary>Fired on color drag end — request an immediate hardware flush.</summary>
        public event Action<LightingSettingsSnapshot>? SettingsFlushRequested;
        public event EventHandler? CloseRequested;

        public KeyboardColorSettings Settings => _settings;
        public LogoLightingSettings LogoSettings => _logo;

        public LightingPanelControl(
            KeyboardColorSettings initial,
            int rgbMode,
            int speed,
            LogoLightingSettings? logo = null)
        {
            _settings = initial;
            _logo = logo?.Clone() ?? new LogoLightingSettings();
            _rgbMode = rgbMode;
            _speed = speed;
            DoubleBuffered = true;
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            Dock = DockStyle.Fill;

            BuildUI();
            ApplyTheme();
            AppTheme.Changed += OnThemeChanged;
            SyncUIFromSettings();
            LayoutHeader();
            LayoutStack();
        }

        public void LoadFrom(
            KeyboardColorSettings settings,
            int rgbMode,
            int speed,
            LogoLightingSettings? logo = null)
        {
            _settings.FourZone = settings.FourZone;
            _settings.SolidColor = settings.SolidColor;
            _settings.Brightness = settings.Brightness;
            for (int i = 0; i < 4; i++)
                _settings.ZoneColors[i] = settings.ZoneColors[i];
            _rgbMode = rgbMode;
            _speed = speed;
            if (logo != null)
            {
                _logo.Enabled = logo.Enabled;
                _logo.Color = logo.Color;
                _logo.Brightness = logo.Brightness;
            }
            SyncUIFromSettings();
        }

        public LightingSettingsSnapshot CreateSnapshot()
        {
            _preview.ExportSettings(_settings);
            return new LightingSettingsSnapshot
            {
                Colors = _settings.Clone(),
                RgbMode = _rgbMode,
                Speed = _speedSlider?.Value ?? _speed,
                Logo = _logo.Clone()
            };
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

        private void BuildUI()
        {
            // Dock.Fill first, Dock.Top last — otherwise the scroll body paints over the header
            // and clips the EFFECT label / dropdown.
            _pnlScroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(ContentPad, 10, ContentPad, 10),
                BackColor = Color.Transparent
            };
            Controls.Add(_pnlScroll);

            _pnlHeader = new Panel
            {
                Height = 40,
                Dock = DockStyle.Top,
                BackColor = AppTheme.TitleBarBackground
            };
            Controls.Add(_pnlHeader);

            var lblTitle = new Label
            {
                Text = "Lighting",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = AppTheme.PrimaryText,
                AutoSize = true,
                Location = new Point(ContentPad, 11),
                BackColor = Color.Transparent
            };
            _pnlHeader.Controls.Add(lblTitle);

            _lblClose = new Label
            {
                Text = "\u2715",
                ForeColor = AppTheme.CaptionButton,
                Font = new Font("Segoe UI", 10f),
                AutoSize = true,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            _lblClose.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
            _lblClose.MouseEnter += (_, _) => _lblClose.ForeColor = AppTheme.CaptionCloseHover;
            _lblClose.MouseLeave += (_, _) => _lblClose.ForeColor = AppTheme.CaptionButton;
            _pnlHeader.Controls.Add(_lblClose);
            _pnlHeader.Resize += (_, _) => LayoutHeader();

            _stack = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                Dock = DockStyle.Top
            };
            _pnlScroll.Controls.Add(_stack);

            // 1) Effect
            _pnlEffect = CreateSectionPanel(ModeSectionHeight);
            _stack.Controls.Add(_pnlEffect);
            BuildEffectSection();

            // 2) Keyboard preview
            _pnlPreviewHost = CreateSectionPanel(PreviewHeight);
            _stack.Controls.Add(_pnlPreviewHost);
            _preview = new KeyboardPreviewControl { Dock = DockStyle.Fill };
            _preview.ZoneSelected += (_, _) =>
            {
                SyncZoneButtons();
                SyncPickerFromSelection();
            };
            _pnlPreviewHost.Controls.Add(_preview);

            // 3) 4-zone + zone buttons (directly under keyboard)
            _chkFourZone = new CheckBox
            {
                Text = "4-zone lighting (off = solid color)",
                Height = 26,
                Font = FontBody,
                ForeColor = AppTheme.PrimaryText,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 2, 0, 4),
                AutoSize = false
            };
            _chkFourZone.CheckedChanged += (_, _) =>
            {
                _settings.FourZone = _chkFourZone.Checked;
                _preview.FourZone = _chkFourZone.Checked;
                UpdateZoneControlsVisible();
                SyncPickerFromSelection();
                NotifyLiveChanged();
            };
            _stack.Controls.Add(_chkFourZone);

            _pnlZoneBar = CreateSectionPanel(ZoneBarHeight);
            _pnlZoneBar.Margin = new Padding(0, 0, 0, 2);
            _stack.Controls.Add(_pnlZoneBar);
            for (int i = 0; i < 4; i++)
            {
                int zone = i;
                _zoneButtons[i] = new PredatorButton
                {
                    Text = $"Zone {i + 1}",
                    Height = 30
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
                Height = 18,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = AppTheme.SecondaryText,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, SectionGap),
                AutoSize = false
            };
            _stack.Controls.Add(_lblZoneHint);

            // 4) Brightness
            _pnlBright = CreateSectionPanel(SliderSectionHeight);
            _stack.Controls.Add(_pnlBright);
            BuildSliderSection(
                _pnlBright,
                "BRIGHTNESS",
                out _lblBrightness,
                out _brightnessSlider,
                min: 1, max: 100, value: 100,
                onChanged: v =>
                {
                    _settings.Brightness = (byte)v;
                    _preview.Brightness = v;
                    _lblBrightness.Text = $"{v}%";
                    NotifyLiveChanged();
                },
                onCommitted: NotifyFlushRequested);

            // 5) Effect speed
            _pnlSpeed = CreateSectionPanel(SliderSectionHeight);
            _stack.Controls.Add(_pnlSpeed);
            BuildSliderSection(
                _pnlSpeed,
                "EFFECT SPEED",
                out _lblSpeed,
                out _speedSlider,
                min: 1, max: 100, value: 50,
                onChanged: v =>
                {
                    _speed = v;
                    _lblSpeed.Text = $"{v}%";
                    NotifyLiveChanged();
                },
                onCommitted: NotifyFlushRequested);

            // 6) Color picker
            _colorPicker = new KeyboardColorPickerControl
            {
                Height = ColorPickerHeight,
                MinimumSize = new Size(240, ColorPickerHeight),
                Margin = new Padding(0, SectionGap, 0, 0)
            };
            _colorPicker.ColorChanged += (_, _) => ApplyPickerColor(flushHardware: false);
            _colorPicker.ColorCommitted += (_, _) => ApplyPickerColor(flushHardware: true);
            _stack.Controls.Add(_colorPicker);

            BuildLogoSection();

            _stack.Resize += (_, _) => LayoutStack();
            _pnlScroll.Resize += (_, _) => LayoutStack();
            _uiReady = true;

            Paint += (_, e) =>
            {
                using var pen = new Pen(AppTheme.Separator);
                e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            };
        }

        private void BuildLogoSection()
        {
            _pnlLogoSep = new Panel
            {
                Height = 22,
                BackColor = Color.Transparent,
                Margin = new Padding(0, SectionGap * 2, 0, SectionGap)
            };
            _lblLogoTitle = new Label
            {
                Text = "LOGO BACKLIGHT",
                AutoSize = true,
                Font = FontSection,
                ForeColor = AppTheme.SectionHeader,
                BackColor = AppTheme.FormBackground
            };
            _pnlLogoSep.Controls.Add(_lblLogoTitle);
            _pnlLogoSep.Paint += (_, e) =>
            {
                int midY = _pnlLogoSep.Height / 2;
                int textW = _lblLogoTitle.PreferredWidth;
                _lblLogoTitle.Location = new Point(0, Math.Max(0, midY - _lblLogoTitle.PreferredHeight / 2));
                using var pen = new Pen(AppTheme.Separator);
                int lineX = textW + 10;
                if (lineX < _pnlLogoSep.Width)
                    e.Graphics.DrawLine(pen, lineX, midY, _pnlLogoSep.Width, midY);
            };
            _stack.Controls.Add(_pnlLogoSep);

            _pnlLogoEnable = CreateSectionPanel(LogoEnableRowHeight);
            _stack.Controls.Add(_pnlLogoEnable);
            var lblLogoOn = new Label
            {
                Text = "Enabled",
                AutoSize = true,
                Font = FontBody,
                ForeColor = AppTheme.PrimaryText,
                Location = new Point(0, 6),
                BackColor = Color.Transparent
            };
            _pnlLogoEnable.Controls.Add(lblLogoOn);
            _switchLogo = new PredatorSwitch
            {
                Size = new Size(52, 26),
                Checked = _logo.Enabled
            };
            _switchLogo.CheckedChanged += (_, _) =>
            {
                if (_suppressLiveEvents) return;
                _logo.Enabled = _switchLogo.Checked;
                UpdateLogoControlsEnabled();
                NotifyFlushRequested();
            };
            _pnlLogoEnable.Controls.Add(_switchLogo);
            _pnlLogoEnable.Resize += (_, _) =>
                _switchLogo.Location = new Point(Math.Max(0, _pnlLogoEnable.Width - _switchLogo.Width), 4);

            _pnlLogoBright = CreateSectionPanel(SliderSectionHeight);
            _stack.Controls.Add(_pnlLogoBright);
            BuildSliderSection(
                _pnlLogoBright,
                "BRIGHTNESS",
                out _lblLogoBrightness,
                out _logoBrightnessSlider,
                min: 0, max: 100, value: _logo.Brightness,
                onChanged: v =>
                {
                    _logo.Brightness = (byte)v;
                    _lblLogoBrightness.Text = $"{v}%";
                    NotifyLiveChanged();
                },
                onCommitted: NotifyFlushRequested);

            _logoColorPicker = new KeyboardColorPickerControl
            {
                Height = LogoColorPickerHeight,
                MinimumSize = new Size(240, LogoColorPickerHeight),
                Margin = new Padding(0, SectionGap, 0, SectionGap)
            };
            _logoColorPicker.ColorChanged += (_, _) => ApplyLogoPickerColor(flushHardware: false);
            _logoColorPicker.ColorCommitted += (_, _) => ApplyLogoPickerColor(flushHardware: true);
            _stack.Controls.Add(_logoColorPicker);
        }

        private static Panel CreateSectionPanel(int height) => new()
        {
            Height = height,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, SectionGap)
        };

        private void LayoutHeader() =>
            _lblClose.Location = new Point(Math.Max(ContentPad, _pnlHeader.Width - ContentPad - 14), 9);

        private void BuildEffectSection()
        {
            var lblMode = new Label
            {
                Text = "EFFECT",
                AutoSize = true,
                Font = FontSection,
                ForeColor = AppTheme.SectionHeader,
                Location = new Point(0, 0),
                BackColor = Color.Transparent
            };
            _pnlEffect.Controls.Add(lblMode);

            _rgbModeDropDown = new PredatorDropDown { Height = 32 };
            foreach (var name in RgbModeNames)
                _rgbModeDropDown.Items.Add(name);
            _rgbModeDropDown.SelectedIndexChanged += (_, _) =>
            {
                if (_suppressLiveEvents || _rgbModeDropDown.SelectedIndex < 0) return;
                _rgbMode = _rgbModeDropDown.SelectedIndex;
                UpdateModeDependentControls();
                NotifyLiveChanged();
            };
            _pnlEffect.Controls.Add(_rgbModeDropDown);
            _pnlEffect.Resize += (_, _) =>
                _rgbModeDropDown.SetBounds(0, 20, Math.Max(1, _pnlEffect.ClientSize.Width), 32);
        }

        private void BuildSliderSection(
            Panel host,
            string title,
            out Label valueLabel,
            out PredatorSlider slider,
            int min,
            int max,
            int value,
            Action<int> onChanged,
            Action onCommitted)
        {
            var lblTitle = new Label
            {
                Text = title,
                AutoSize = true,
                Font = FontSection,
                ForeColor = AppTheme.SectionHeader,
                Location = new Point(0, 0),
                BackColor = Color.Transparent
            };
            host.Controls.Add(lblTitle);

            valueLabel = new Label
            {
                Text = $"{value}%",
                AutoSize = true,
                Font = FontBody,
                ForeColor = AppTheme.SecondaryText,
                BackColor = Color.Transparent
            };
            host.Controls.Add(valueLabel);

            slider = new PredatorSlider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Height = 26
            };
            var capturedLabel = valueLabel;
            var capturedSlider = slider;
            slider.ValueChanged += (_, _) => onChanged(capturedSlider.Value);
            slider.ValueCommitted += (_, _) => onCommitted();
            host.Controls.Add(slider);

            host.Resize += (_, _) => LayoutSliderSection(host, capturedLabel, capturedSlider);
        }

        private static void LayoutSliderSection(Panel host, Label valueLabel, PredatorSlider slider)
        {
            int w = host.ClientSize.Width;
            if (w <= 0) return;

            // Reserve space on the right so "%" labels are never clipped.
            const int valueReserve = 48;
            valueLabel.Location = new Point(Math.Max(0, w - valueReserve), 0);
            slider.SetBounds(0, 20, Math.Max(1, w), 26);
        }

        private void LayoutStack()
        {
            if (!_uiReady || _stack == null || _pnlScroll == null) return;

            int width = Math.Max(220, _pnlScroll.ClientSize.Width - _pnlScroll.Padding.Horizontal);
            _stack.Width = width;

            foreach (Control child in _stack.Controls)
            {
                child.Width = width - child.Margin.Horizontal;
                if (child == _pnlEffect && _rgbModeDropDown != null)
                    _rgbModeDropDown.SetBounds(0, 20, Math.Max(1, _pnlEffect.ClientSize.Width), 32);
                if (child == _pnlBright)
                    LayoutSliderSection(_pnlBright, _lblBrightness, _brightnessSlider);
                if (child == _pnlSpeed)
                    LayoutSliderSection(_pnlSpeed, _lblSpeed, _speedSlider);
                if (child == _pnlLogoBright && _logoBrightnessSlider != null)
                    LayoutSliderSection(_pnlLogoBright, _lblLogoBrightness, _logoBrightnessSlider);
                if (child == _pnlLogoEnable && _switchLogo != null)
                    _switchLogo.Location = new Point(Math.Max(0, _pnlLogoEnable.Width - _switchLogo.Width), 4);
            }

            LayoutZoneButtons();
        }

        private void LayoutZoneButtons()
        {
            if (_pnlZoneBar == null || _pnlZoneBar.Width <= 0 || _zoneButtons[0] == null) return;

            const int gap = 6;
            int buttonW = Math.Max(56, (_pnlZoneBar.Width - gap * 3) / 4);
            for (int i = 0; i < 4; i++)
                _zoneButtons[i].SetBounds(i * (buttonW + gap), 2, buttonW, 30);
        }

        private void UpdateZoneControlsVisible()
        {
            bool staticMode = _rgbMode == 0;
            bool showZones = staticMode && _chkFourZone.Checked;

            _chkFourZone.Visible = staticMode;
            _pnlZoneBar.Visible = showZones;
            _lblZoneHint.Visible = showZones;
            _colorPicker.Visible = staticMode;

            LayoutZoneButtons();
            LayoutStack();
        }

        private void UpdateModeDependentControls()
        {
            _speedSlider.Enabled = _rgbMode != 0;
            UpdateZoneControlsVisible();
        }

        private void SyncUIFromSettings()
        {
            _suppressLiveEvents = true;
            try
            {
                _preview.LoadSettings(_settings);
                _chkFourZone.Checked = _settings.FourZone;
                _preview.FourZone = _settings.FourZone;
                _brightnessSlider.Value = _settings.Brightness;
                _preview.Brightness = _settings.Brightness;
                _lblBrightness.Text = $"{_settings.Brightness}%";

                _rgbModeDropDown.SelectedIndex = Math.Clamp(_rgbMode, 0, RgbModeNames.Length - 1);
                _speedSlider.Value = Math.Clamp(_speed, 1, 100);
                _lblSpeed.Text = $"{_speedSlider.Value}%";

                _switchLogo.Checked = _logo.Enabled;
                _logoBrightnessSlider.Value = Math.Clamp((int)_logo.Brightness, 0, 100);
                _lblLogoBrightness.Text = $"{_logoBrightnessSlider.Value}%";
                _logoColorPicker.SetColor(_logo.Color);

                UpdateModeDependentControls();
                UpdateLogoControlsEnabled();
                SyncZoneButtons();
                SyncPickerFromSelection();
                LayoutStack();
            }
            finally
            {
                _suppressLiveEvents = false;
            }
        }

        private void UpdateLogoControlsEnabled()
        {
            bool on = _switchLogo.Checked;
            _pnlLogoBright.Enabled = on;
            _logoColorPicker.Enabled = on;
            _logoBrightnessSlider.Enabled = on;
        }

        private void ApplyLogoPickerColor(bool flushHardware)
        {
            _logo.Color = _logoColorPicker.SelectedColor;
            if (_suppressLiveEvents) return;
            var snapshot = CreateSnapshot();
            if (flushHardware)
                SettingsFlushRequested?.Invoke(snapshot);
            else
                SettingsLiveChanged?.Invoke(snapshot);
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

        private void ApplyPickerColor(bool flushHardware)
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

            if (_suppressLiveEvents) return;
            _preview.ExportSettings(_settings);
            var snapshot = CreateSnapshot();
            if (flushHardware)
                SettingsFlushRequested?.Invoke(snapshot);
            else
                SettingsLiveChanged?.Invoke(snapshot);
        }

        private void NotifyLiveChanged()
        {
            if (_suppressLiveEvents) return;
            _preview.ExportSettings(_settings);
            SettingsLiveChanged?.Invoke(CreateSnapshot());
        }

        private void NotifyFlushRequested()
        {
            if (_suppressLiveEvents) return;
            _preview.ExportSettings(_settings);
            SettingsFlushRequested?.Invoke(CreateSnapshot());
        }

        private void ApplyTheme()
        {
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            _pnlHeader.BackColor = AppTheme.TitleBarBackground;
            _chkFourZone.ForeColor = AppTheme.PrimaryText;
            _preview.BackColor = AppTheme.FormBackground;
            _colorPicker.BackColor = AppTheme.FormBackground;
            _logoColorPicker.BackColor = AppTheme.FormBackground;
            _pnlPreviewHost.BackColor = AppTheme.FormBackground;
            if (_lblLogoTitle != null)
            {
                _lblLogoTitle.ForeColor = AppTheme.SectionHeader;
                _lblLogoTitle.BackColor = AppTheme.FormBackground;
            }
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                AppTheme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }
}
