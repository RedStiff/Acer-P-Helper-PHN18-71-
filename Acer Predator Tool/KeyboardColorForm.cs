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
        private const int ModeSectionHeight = 54;
        private const int SectionGap = 10;
        private const int SlideStepPx = 18;

        private static readonly Font FontTitle = new("Segoe UI", 11f, FontStyle.Bold);
        private static readonly Font FontSection = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontBody = new("Segoe UI", 9.5f, FontStyle.Regular);
        private static readonly string[] RgbModeNames =
            { "Static", "Breathing", "Neon", "Wave", "Shifting", "Zoom", "Meteor", "Twinkling" };

        private readonly KeyboardColorSettings _settings;
        private readonly bool _sidePanelMode;
        private int _rgbMode;
        private int _speed;
        private bool _suppressLiveEvents;

        private Panel _pnlTitle = null!;
        private Panel _pnlContent = null!;
        private TableLayoutPanel _contentLayout = null!;
        private Panel _pnlBottom = null!;
        private FlowLayoutPanel _bottomFlow = null!;
        private Panel _pnlPreviewHost = null!;
        private Panel? _pnlEffect;
        private Panel _pnlZoneBar = null!;
        private Panel _pnlFooter = null!;
        private Label _lblClose = null!;
        private KeyboardPreviewControl _preview = null!;
        private KeyboardColorPickerControl _colorPicker = null!;
        private CheckBox _chkFourZone = null!;
        private PredatorSlider _brightnessSlider = null!;
        private PredatorSlider? _speedSlider;
        private PredatorDropDown? _rgbModeDropDown;
        private Label _lblBrightness = null!;
        private Label? _lblSpeed;
        private Label _lblZoneHint = null!;
        private readonly PredatorButton[] _zoneButtons = new PredatorButton[4];
        private bool _uiReady;

        private Form? _ownerForm;
        private System.Windows.Forms.Timer? _slideTimer;
        private int _slideTargetLeft;
        private bool _slideClosing;

        private const uint SwpNomove = 0x0002;
        private const uint SwpNosize = 0x0001;
        private const uint SwpNoactivate = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        public KeyboardColorSettings Settings => _settings;
        public bool Applied { get; private set; }

        public event Action<LightingSettingsSnapshot>? SettingsLiveChanged;

        private KeyboardColorForm(KeyboardColorSettings initial, int rgbMode, int speed, bool sidePanelMode)
        {
            _settings = initial;
            _rgbMode = rgbMode;
            _speed = speed;
            _sidePanelMode = sidePanelMode;
            var ui = UiSettings.Load();

            Text = sidePanelMode ? "Lighting Settings" : "Keyboard Color";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = sidePanelMode ? FormStartPosition.Manual : FormStartPosition.CenterParent;
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            ClientSize = sidePanelMode ? ui.GetKeyboardColorClientSize() : ui.GetKeyboardColorModalClientSize();
            DoubleBuffered = true;
            ShowInTaskbar = false;
            BorderlessResizeHelper.ApplyTo(
                this,
                UiSettings.MinKeyboardColorWidth,
                sidePanelMode ? UiSettings.MinMainWindowHeight : UiSettings.MinKeyboardColorHeight);
            try { Icon = new Icon("appicon.ico"); } catch { }

            BuildUI();
            ApplyTheme();
            AppTheme.Changed += OnThemeChanged;
            SyncUIFromSettings();
            LayoutTitleBar(20);
            LayoutBottomFlow();
            LayoutEffectSection();
        }

        public static KeyboardColorForm CreateSidePanel(
            Form owner,
            KeyboardColorSettings colors,
            int rgbMode,
            int speed)
        {
            var form = new KeyboardColorForm(colors, rgbMode, speed, sidePanelMode: true);
            form._ownerForm = owner;
            return form;
        }

        public void ShowBesideOwner(Form owner)
        {
            _ownerForm = owner;
            AlignToOwner(finalPosition: false);

            // Owned forms are always drawn above their owner; show independently so the panel can sit behind during slide-in.
            Show();
            SendBehindOwner();
            StartSlideIn();

            owner.LocationChanged += OnOwnerMoved;
            owner.SizeChanged += OnOwnerMoved;
            owner.Activated += OnOwnerActivated;
            owner.FormClosing += OnOwnerClosing;
            Activated += OnPanelActivated;
        }

        private void OnPanelActivated(object? sender, EventArgs e)
        {
            if (_slideClosing || _slideTimer?.Enabled == true) return;
            BringWithOwner();
        }

        public void BringWithOwner()
        {
            if (!_sidePanelMode || _ownerForm == null || _ownerForm.IsDisposed || IsDisposed) return;
            if (!Visible || !IsHandleCreated || !_ownerForm.IsHandleCreated) return;

            uint flags = SwpNomove | SwpNosize | SwpNoactivate;
            SetWindowPos(_ownerForm.Handle, IntPtr.Zero, 0, 0, 0, 0, flags);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, flags);
        }

        public void BeginSlideClose()
        {
            if (_slideClosing) return;
            _slideClosing = true;
            Owner = null;
            DetachOwnerEvents();
            StartSlideOut();
        }

        private void OnOwnerActivated(object? sender, EventArgs e)
        {
            if (_slideClosing || _slideTimer?.Enabled == true || !Visible) return;
            BringWithOwner();
        }

        private void OnOwnerMoved(object? sender, EventArgs e)
        {
            if (_ownerForm == null) return;

            if (_slideTimer?.Enabled == true)
            {
                int remaining = Left - _slideTargetLeft;
                _slideTargetLeft = _ownerForm.Left - ClientSize.Width;
                Left = _slideTargetLeft + remaining;
                Top = _ownerForm.Top;
                return;
            }

            AlignToOwner(finalPosition: true);
        }

        private void OnOwnerClosing(object? sender, FormClosingEventArgs e) => BeginSlideClose();

        private void DetachOwnerEvents()
        {
            if (_ownerForm == null) return;
            _ownerForm.LocationChanged -= OnOwnerMoved;
            _ownerForm.SizeChanged -= OnOwnerMoved;
            _ownerForm.Activated -= OnOwnerActivated;
            _ownerForm.FormClosing -= OnOwnerClosing;
        }

        public void AlignToOwnerForShow(Form owner)
        {
            _ownerForm = owner;
            AlignToOwner(finalPosition: true);
            AttachToOwnerForeground();
        }

        public void AlignBesideOwnerOnly()
        {
            if (_ownerForm == null) return;
            AlignToOwner(finalPosition: true);
        }

        protected override void WndProc(ref Message m)
        {
            if (_sidePanelMode && m.Msg == BorderlessResizeHelper.WmNchitTest)
            {
                base.WndProc(ref m);
                return;
            }

            base.WndProc(ref m);
        }

        private void AttachToOwnerForeground()
        {
            if (_ownerForm == null || _ownerForm.IsDisposed || !_sidePanelMode) return;
            Owner = _ownerForm;
            BringWithOwner();
        }

        private void AlignToOwner(bool finalPosition)
        {
            if (_ownerForm == null) return;

            Top = _ownerForm.Top;
            _slideTargetLeft = _ownerForm.Left - ClientSize.Width;
            Left = finalPosition ? _slideTargetLeft : _ownerForm.Left;
        }


        private void SendBehindOwner()
        {
            if (_ownerForm == null || _ownerForm.IsDisposed || IsDisposed) return;
            if (!IsHandleCreated || !_ownerForm.IsHandleCreated) return;

            SetWindowPos(Handle, _ownerForm.Handle, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        }

        private void StartSlideIn()
        {
            _slideTimer?.Stop();
            _slideTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _slideTimer.Tick += (_, _) =>
            {
                SendBehindOwner();
                if (Left > _slideTargetLeft)
                {
                    Left = Math.Max(_slideTargetLeft, Left - SlideStepPx);
                    return;
                }
                _slideTimer!.Stop();
                AttachToOwnerForeground();
            };
            _slideTimer.Start();
        }

        private void StartSlideOut()
        {
            if (_ownerForm == null)
            {
                Close();
                return;
            }

            int hiddenLeft = _ownerForm.Left;
            _slideTimer?.Stop();
            _slideTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _slideTimer.Tick += (_, _) =>
            {
                SendBehindOwner();
                if (Left < hiddenLeft)
                {
                    Left = Math.Min(hiddenLeft, Left + SlideStepPx);
                    return;
                }
                _slideTimer!.Stop();
                Close();
            };
            _slideTimer.Start();
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

            if (!_sidePanelMode)
            {
                _pnlFooter = new Panel
                {
                    Height = FooterHeight,
                    Dock = DockStyle.Bottom,
                    BackColor = Color.Transparent,
                    Padding = new Padding(pad, 4, pad, 12)
                };
                Controls.Add(_pnlFooter);
            }

            _pnlTitle = new Panel { Height = 42, Dock = DockStyle.Top, BackColor = AppTheme.TitleBarBackground };
            _pnlTitle.MouseDown += TitleBar_MouseDown;
            Controls.Add(_pnlTitle);

            var lblTitle = new Label
            {
                Text = _sidePanelMode ? "Lighting Settings" : "Keyboard Color",
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
            _lblClose.Click += (_, _) =>
            {
                if (_sidePanelMode) BeginSlideClose();
                else Close();
            };
            _lblClose.MouseEnter += (_, _) => _lblClose.ForeColor = AppTheme.CaptionCloseHover;
            _lblClose.MouseLeave += (_, _) => _lblClose.ForeColor = AppTheme.CaptionButton;
            _pnlTitle.Controls.Add(_lblClose);
            _pnlTitle.Resize += (_, _) => LayoutTitleBar(pad);

            _contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = _sidePanelMode ? 3 : 2,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            if (_sidePanelMode)
            {
                _contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                _contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            else
            {
                _contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                _contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            _contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _pnlContent.Controls.Add(_contentLayout);

            if (_sidePanelMode)
            {
                _pnlEffect = new Panel
                {
                    Height = ModeSectionHeight,
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 0, 0, SectionGap)
                };
                _contentLayout.Controls.Add(_pnlEffect, 0, 0);
                BuildEffectSection();
            }

            _pnlPreviewHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 4),
                MinimumSize = new Size(0, _sidePanelMode ? 140 : 180)
            };
            _contentLayout.Controls.Add(_pnlPreviewHost, 0, _sidePanelMode ? 1 : 0);

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
            _contentLayout.Controls.Add(_pnlBottom, 0, _sidePanelMode ? 2 : 1);

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

            if (_sidePanelMode)
                BuildSpeedSection();

            _colorPicker = new KeyboardColorPickerControl
            {
                Height = ColorPickerHeight,
                MinimumSize = new Size(280, ColorPickerHeight),
                Margin = new Padding(0, 0, 0, SectionGap)
            };
            _colorPicker.ColorChanged += (_, _) => ApplyPickerColor();
            _colorPicker.ColorCommitted += (_, _) => ApplyPickerColor();
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
                NotifyLiveChanged();
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

            BuildBrightnessSection();

            _contentLayout.Resize += (_, _) =>
            {
                LayoutBottomFlow();
                LayoutEffectSection();
            };
            _uiReady = true;

            if (!_sidePanelMode)
                BuildModalFooter(pad);

            Paint += (_, e) =>
            {
                using var pen = new Pen(AppTheme.Separator);
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                e.Graphics.DrawLine(pen, 0, _pnlTitle.Bottom - 1, ClientSize.Width, _pnlTitle.Bottom - 1);
                if (_pnlFooter != null)
                    e.Graphics.DrawLine(pen, 0, _pnlFooter.Top, ClientSize.Width, _pnlFooter.Top);
            };
        }

        private void BuildEffectSection()
        {
            if (_pnlEffect == null) return;

            var lblMode = new Label
            {
                Text = "EFFECT",
                AutoSize = true,
                Font = FontSection,
                ForeColor = AppTheme.SectionHeader,
                Location = new Point(0, 2),
                BackColor = Color.Transparent
            };
            _pnlEffect.Controls.Add(lblMode);

            _rgbModeDropDown = new PredatorDropDown
            {
                Height = 32,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
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
            _pnlEffect.Resize += (_, _) => LayoutEffectSection();
        }

        private void LayoutEffectSection()
        {
            if (_pnlEffect == null || _rgbModeDropDown == null) return;
            _rgbModeDropDown.SetBounds(0, 22, _pnlEffect.ClientSize.Width, 32);
        }

        private void BuildSpeedSection()
        {
            var pnlSpeed = new Panel
            {
                Height = BrightSectionHeight,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, SectionGap)
            };
            _bottomFlow.Controls.Add(pnlSpeed);

            var pnlSpeedHeader = new Panel
            {
                Height = 20,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent
            };
            pnlSpeed.Controls.Add(pnlSpeedHeader);

            var lblSpeedTitle = new Label
            {
                Text = "EFFECT SPEED",
                AutoSize = true,
                Font = FontSection,
                ForeColor = AppTheme.SectionHeader,
                Location = new Point(0, 2),
                BackColor = Color.Transparent
            };
            pnlSpeedHeader.Controls.Add(lblSpeedTitle);

            _lblSpeed = new Label
            {
                Text = "50%",
                AutoSize = true,
                Font = FontBody,
                ForeColor = AppTheme.SecondaryText,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            pnlSpeedHeader.Controls.Add(_lblSpeed);

            _speedSlider = new PredatorSlider
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Minimum = 1,
                Maximum = 100,
                Value = 50
            };
            _speedSlider.ValueChanged += (_, _) =>
            {
                if (_lblSpeed == null || _speedSlider == null) return;
                _speed = _speedSlider.Value;
                _lblSpeed.Text = $"{_speed}%";
            };
            _speedSlider.ValueCommitted += (_, _) => NotifyLiveChanged();
            pnlSpeed.Controls.Add(_speedSlider);
            pnlSpeed.Resize += (_, _) => LayoutSpeedRow(pnlSpeedHeader);
        }

        private void BuildBrightnessSection()
        {
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
            _brightnessSlider.ValueCommitted += (_, _) => NotifyLiveChanged();
            pnlBright.Controls.Add(_brightnessSlider);
            pnlBright.Resize += (_, _) => LayoutBrightnessRow(pnlBrightHeader);
        }

        private void BuildModalFooter(int pad)
        {
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
        }

        private void LayoutTitleBar(int pad) =>
            _lblClose.Location = new Point(_pnlTitle.Width - pad - 12, 9);

        private void LayoutBrightnessRow(Panel header)
        {
            if (_lblBrightness == null) return;
            _lblBrightness.Location = new Point(Math.Max(0, header.ClientSize.Width - _lblBrightness.Width), 2);
        }

        private void LayoutSpeedRow(Panel header)
        {
            if (_lblSpeed == null) return;
            _lblSpeed.Location = new Point(Math.Max(0, header.ClientSize.Width - _lblSpeed.Width), 2);
        }

        private void LayoutBottomFlow()
        {
            if (!_uiReady || _bottomFlow == null || _contentLayout == null) return;

            int width = Math.Max(280, _contentLayout.ClientSize.Width);
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
            bool staticMode = _rgbMode == 0;
            bool showZones = staticMode && _chkFourZone.Checked;

            _chkFourZone.Visible = staticMode;
            _pnlZoneBar.Visible = showZones;
            _lblZoneHint.Visible = showZones;
            _colorPicker.Visible = staticMode;

            LayoutZoneButtons();
            LayoutBottomFlow();
        }

        private void UpdateModeDependentControls()
        {
            if (_speedSlider != null)
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

                if (_rgbModeDropDown != null)
                    _rgbModeDropDown.SelectedIndex = Math.Clamp(_rgbMode, 0, RgbModeNames.Length - 1);
                if (_speedSlider != null)
                {
                    _speedSlider.Value = Math.Clamp(_speed, 1, 100);
                    if (_lblSpeed != null) _lblSpeed.Text = $"{_speedSlider.Value}%";
                }

                UpdateModeDependentControls();
                SyncZoneButtons();
                SyncPickerFromSelection();
            }
            finally
            {
                _suppressLiveEvents = false;
            }
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
            NotifyLiveChanged();
        }

        private void NotifyLiveChanged()
        {
            if (_suppressLiveEvents || !_sidePanelMode) return;

            _preview.ExportSettings(_settings);
            SettingsLiveChanged?.Invoke(CreateSnapshot());
        }

        public LightingSettingsSnapshot CreateSnapshot()
        {
            _preview.ExportSettings(_settings);
            return new LightingSettingsSnapshot
            {
                Colors = _settings,
                RgbMode = _rgbMode,
                Speed = _speedSlider?.Value ?? _speed
            };
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

        protected override bool ShowWithoutActivation => _sidePanelMode;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_sidePanelMode && _slideTimer?.Enabled == true)
                SendBehindOwner();
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Activated -= OnPanelActivated;
            _slideTimer?.Stop();
            _slideTimer?.Dispose();
            DetachOwnerEvents();
            if (!_sidePanelMode)
                UiSettings.SaveKeyboardColorWindowSize(ClientSize);
            AppTheme.Changed -= OnThemeChanged;
            base.OnFormClosed(e);
        }

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            ReleaseCapture();
            if (_sidePanelMode && _ownerForm != null && !_ownerForm.IsDisposed && _ownerForm.IsHandleCreated)
                SendMessage(_ownerForm.Handle, 0xA1, 0x2, 0);
            else
                SendMessage(Handle, 0xA1, 0x2, 0);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        public static bool TryShow(IWin32Window owner, KeyboardColorSettings settings, out KeyboardColorSettings result)
        {
            using var form = new KeyboardColorForm(settings, rgbMode: 0, speed: 50, sidePanelMode: false);
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
