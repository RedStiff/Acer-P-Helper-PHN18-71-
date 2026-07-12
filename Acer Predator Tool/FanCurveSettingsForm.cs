using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class FanCurveSettingsForm : ResizableBorderlessForm
    {
        private const int FooterHeight = 76;

        private static readonly Font FontTitle = new("Segoe UI", 11f, FontStyle.Bold);
        private static readonly Font FontSection = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontBody = new("Segoe UI", 9.5f, FontStyle.Regular);
        private static readonly Font FontHint = new("Segoe UI", 7.5f, FontStyle.Regular);

        private readonly FanCurveController _controller;
        private readonly WmiController _wmi;
        private readonly System.Windows.Forms.Timer _rpmTimer;

        private Panel _pnlTitle = null!;
        private Panel _pnlFooter = null!;
        private Panel _pnlFooterRow = null!;
        private Label _lblDeltaTitle = null!;
        private Label _lblClose = null!;
        private Label _lblHint = null!;
        private PredatorButton _btnCpu = null!;
        private PredatorButton _btnGpu = null!;
        private PredatorButton _btnSave = null!;
        private PredatorButton _btnCancel = null!;
        private PredatorSlider _deltaSlider = null!;
        private Label _lblDelta = null!;
        private FanCurveEditorPanel _cpuEditor = null!;
        private FanCurveEditorPanel _gpuEditor = null!;

        private readonly FanCurveConfig _cpuEdit;
        private readonly FanCurveConfig _gpuEdit;
        private bool _activeCpu = true;

        public FanCurveSettingsForm(FanCurveController controller, WmiController wmi)
        {
            _controller = controller;
            _wmi = wmi;
            _cpuEdit = controller.Store.Cpu.Clone();
            _gpuEdit = controller.Store.Gpu.Clone();
            _cpuEdit.UpgradeLegacyPoints();
            _gpuEdit.UpgradeLegacyPoints();

            var ui = UiSettings.Load();

            Text = "Curves";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            ClientSize = ui.GetCurvesClientSize();
            DoubleBuffered = true;
            ShowInTaskbar = false;
            BorderlessResizeHelper.ApplyTo(this, UiSettings.MinCurvesWidth, UiSettings.MinCurvesHeight);
            try { Icon = new Icon("appicon.ico"); } catch { }

            BuildUI();
            ApplyTheme();
            AppTheme.Changed += OnThemeChanged;

            _rpmTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _rpmTimer.Tick += (_, _) => UpdateRpmLabels();
            _rpmTimer.Start();
            SelectFan(true);
            LayoutTitleBar(20);
            LayoutFooter(20);
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

        private FanCurveConfig ActiveConfig => _activeCpu ? _cpuEdit : _gpuEdit;

        private void BuildUI()
        {
            int pad = 20;

            var pnlContent = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(pad, 6, pad, 4),
                BackColor = Color.Transparent
            };
            Controls.Add(pnlContent);

            _pnlFooter = new Panel
            {
                Height = FooterHeight,
                Dock = DockStyle.Bottom,
                BackColor = Color.Transparent,
                Padding = new Padding(pad, 0, pad, 10)
            };
            Controls.Add(_pnlFooter);

            _pnlFooterRow = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            _pnlFooter.Controls.Add(_pnlFooterRow);

            _lblHint = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Font = FontHint,
                ForeColor = AppTheme.SecondaryText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false
            };
            _pnlFooter.Controls.Add(_lblHint);
            _pnlFooter.Resize += (_, _) => LayoutFooter(pad);

            _pnlTitle = new Panel { Height = 48, Dock = DockStyle.Top, BackColor = AppTheme.TitleBarBackground };
            _pnlTitle.MouseDown += TitleBar_MouseDown;
            Controls.Add(_pnlTitle);

            var lblTitle = new Label
            {
                Text = "Curves",
                Font = FontTitle,
                ForeColor = AppTheme.PrimaryText,
                AutoSize = true,
                Location = new Point(pad, 13),
                BackColor = Color.Transparent
            };
            lblTitle.MouseDown += TitleBar_MouseDown;
            _pnlTitle.Controls.Add(lblTitle);

            _btnCpu = new PredatorButton { Text = "CPU Fan", Size = new Size(120, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnCpu.Click += (_, _) => SelectFan(true);
            _pnlTitle.Controls.Add(_btnCpu);

            _btnGpu = new PredatorButton { Text = "GPU Fan", Size = new Size(120, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnGpu.Click += (_, _) => SelectFan(false);
            _pnlTitle.Controls.Add(_btnGpu);

            var captionFont = new Font("Segoe UI", 10f);
            _lblClose = new Label
            {
                Text = "\u2715",
                ForeColor = AppTheme.CaptionButton,
                Font = captionFont,
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

            _btnSave = new PredatorButton
            {
                Text = "Save",
                Size = new Size(108, 34),
                Location = new Point(0, 2),
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            _btnSave.Click += (_, _) => SaveAndClose();
            _pnlFooterRow.Controls.Add(_btnSave);

            _btnCancel = new PredatorButton
            {
                Text = "Cancel",
                Size = new Size(108, 34),
                Location = new Point(116, 2),
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            _btnCancel.Click += (_, _) => Close();
            _pnlFooterRow.Controls.Add(_btnCancel);

            _lblDeltaTitle = new Label
            {
                Text = "Δ-Temp",
                AutoSize = true,
                Font = FontSection,
                ForeColor = AppTheme.SectionHeader,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            _pnlFooterRow.Controls.Add(_lblDeltaTitle);

            _deltaSlider = new PredatorSlider
            {
                Size = new Size(160, 28),
                Minimum = 1,
                Maximum = 10,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            _deltaSlider.ValueChanged += (_, _) =>
            {
                int v = _deltaSlider.Value;
                ActiveConfig.DeltaTemperature = v;
                _lblDelta.Text = $"{v} °C";
            };
            _pnlFooterRow.Controls.Add(_deltaSlider);

            _lblDelta = new Label
            {
                AutoSize = true,
                Font = FontBody,
                ForeColor = AppTheme.SecondaryText,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            _pnlFooterRow.Controls.Add(_lblDelta);

            _cpuEditor = new FanCurveEditorPanel(_cpuEdit, FanKind.Cpu);
            _cpuEditor.Dock = DockStyle.Fill;
            _cpuEditor.PointsChanged += (_, _) => UpdateHint();
            pnlContent.Controls.Add(_cpuEditor);

            _gpuEditor = new FanCurveEditorPanel(_gpuEdit, FanKind.Gpu);
            _gpuEditor.Dock = DockStyle.Fill;
            _gpuEditor.Visible = false;
            _gpuEditor.PointsChanged += (_, _) => UpdateHint();
            pnlContent.Controls.Add(_gpuEditor);

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
            _lblClose.Location = new Point(_pnlTitle.Width - pad - 12, 11);
            _btnGpu.Location = new Point(_lblClose.Left - 128, 8);
            _btnCpu.Location = new Point(_btnGpu.Left - 128, 8);
        }

        private void LayoutFooter(int pad)
        {
            int y = 4;
            _btnSave.Location = new Point(0, y);
            _btnCancel.Location = new Point(_btnSave.Right + 8, y);

            int deltaLeft = _btnCancel.Right + 24;
            _lblDeltaTitle.Location = new Point(deltaLeft, y + 10);

            int sliderLeft = _lblDeltaTitle.Right + 8;
            int sliderMaxRight = _pnlFooter.ClientSize.Width - pad - 52;
            int sliderWidth = Math.Max(90, Math.Min(180, sliderMaxRight - sliderLeft));
            _deltaSlider.Location = new Point(sliderLeft, y + 4);
            _deltaSlider.Width = sliderWidth;

            _lblDelta.Location = new Point(_deltaSlider.Right + 8, y + 10);
        }

        private void SelectFan(bool cpu)
        {
            _activeCpu = cpu;
            _btnCpu.IsActive = cpu;
            _btnGpu.IsActive = !cpu;
            _cpuEditor.Visible = cpu;
            _gpuEditor.Visible = !cpu;
            if (cpu) _cpuEditor.BringToFront();
            else _gpuEditor.BringToFront();

            int delta = Math.Clamp(ActiveConfig.DeltaTemperature, 1, 10);
            _deltaSlider.Value = delta;
            _lblDelta.Text = $"{delta} °C";
            UpdateHint();
            UpdateRpmLabels();
            LayoutFooter(20);
        }

        private void UpdateHint()
        {
            var cfg = ActiveConfig;
            _lblHint.Text =
                $"Points: {cfg.Points.Count}/{FanCurveConfig.MaxPoints}  ·  Double-click on graph to add  ·  Right-click a point to remove";
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        private void SaveAndClose()
        {
            ActiveConfig.DeltaTemperature = _deltaSlider.Value;
            _cpuEditor.Commit();
            _gpuEditor.Commit();
            _cpuEdit.DeltaTemperature = Math.Clamp(_cpuEdit.DeltaTemperature, 1, 10);
            _gpuEdit.DeltaTemperature = Math.Clamp(_gpuEdit.DeltaTemperature, 1, 10);
            _controller.Store.SetCurves(_cpuEdit, _gpuEdit);
            _controller.ReloadFromStore();
            Close();
        }

        private void UpdateRpmLabels()
        {
            _cpuEditor.UpdateLive(_wmi.CpuFanRpm, _wmi.CpuTemp);
            _gpuEditor.UpdateLive(_wmi.GpuFanRpm, _wmi.GpuTemp);
        }

        private void ApplyTheme()
        {
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            _pnlTitle.BackColor = AppTheme.TitleBarBackground;
            Invalidate(true);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UiSettings.SaveCurvesWindowSize(ClientSize);
            _rpmTimer.Stop();
            _rpmTimer.Dispose();
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

        private sealed class FanCurveEditorPanel : Panel
        {
            private readonly FanCurveConfig _config;

            private RadioButton _rbSlope = null!, _rbStair = null!;
            private Label _lblRpm = null!;
            private FanCurveGraphControl _graph = null!;

            public event EventHandler? PointsChanged;

            public FanCurveEditorPanel(FanCurveConfig config, FanKind fanKind)
            {
                _config = config;
                BackColor = Color.Transparent;
                DoubleBuffered = true;
                BuildUI(fanKind);
            }

            private void BuildUI(FanKind fanKind)
            {
                _graph = new FanCurveGraphControl { Dock = DockStyle.Fill, FanKind = fanKind };
                _graph.BindConfig(_config);
                _graph.CurveChanged += (_, _) =>
                {
                    _config.EnsureSorted();
                    PointsChanged?.Invoke(this, EventArgs.Empty);
                };
                Controls.Add(_graph);

                var header = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 40,
                    BackColor = Color.Transparent
                };

                _rbSlope = new RadioButton
                {
                    Text = "Slope Mode",
                    Location = new Point(0, 10),
                    AutoSize = true,
                    Checked = _config.SlopeMode,
                    Font = FontBody,
                    ForeColor = AppTheme.PrimaryText,
                    BackColor = Color.Transparent
                };
                _rbSlope.CheckedChanged += (_, _) =>
                {
                    if (!_rbSlope.Checked) return;
                    _config.SlopeMode = true;
                    _graph.Invalidate();
                };
                header.Controls.Add(_rbSlope);

                _rbStair = new RadioButton
                {
                    Text = "Stair Mode",
                    Location = new Point(144, 10),
                    AutoSize = true,
                    Checked = !_config.SlopeMode,
                    Font = FontBody,
                    ForeColor = AppTheme.PrimaryText,
                    BackColor = Color.Transparent
                };
                _rbStair.CheckedChanged += (_, _) =>
                {
                    if (!_rbStair.Checked) return;
                    _config.SlopeMode = false;
                    _graph.Invalidate();
                };
                header.Controls.Add(_rbStair);

                _lblRpm = new Label
                {
                    Text = "-- RPM",
                    AutoSize = true,
                    Font = FontSection,
                    ForeColor = AppTheme.Accent,
                    BackColor = Color.Transparent,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                header.Controls.Add(_lblRpm);
                header.Resize += (_, _) => _lblRpm.Location = new Point(Math.Max(260, header.Width - _lblRpm.Width - 4), 11);
                Controls.Add(header);
            }

            public void UpdateLive(int rpm, int temp)
            {
                if (!Visible) return;
                _lblRpm.Text = rpm > 0 ? $"{rpm} RPM" : "-- RPM";
                _graph.Tag = temp > 0 ? temp : -1;
                _graph.Invalidate();
            }

            public void Commit()
            {
                _config.SlopeMode = _rbSlope.Checked;
                _config.DeltaTemperature = Math.Clamp(_config.DeltaTemperature, 1, 10);
                _config.EnsureSorted();
            }
        }
    }
}
