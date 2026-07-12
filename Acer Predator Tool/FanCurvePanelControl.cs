using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public sealed class FanCurvePanelControl : UserControl
    {
        private const int FooterHeight = 76;
        private const int LiveApplyIntervalMs = 80;

        private static readonly Font FontSection = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontBody = new("Segoe UI", 9.5f, FontStyle.Regular);
        private static readonly Font FontHint = new("Segoe UI", 7.5f, FontStyle.Regular);

        private readonly FanCurveController _controller;
        private readonly WmiController _wmi;
        private readonly System.Windows.Forms.Timer _rpmTimer;
        private readonly System.Windows.Forms.Timer _liveApplyTimer;

        private Panel _pnlHeader = null!;
        private Panel _pnlFooter = null!;
        private Panel _pnlFooterRow = null!;
        private Panel _pnlProfiles = null!;
        private Label _lblDeltaTitle = null!;
        private Label _lblClose = null!;
        private Label _lblHint = null!;
        private Label _lblProfile = null!;
        private PredatorButton _btnCpu = null!;
        private PredatorButton _btnGpu = null!;
        private PredatorButton _btnSave = null!;
        private PredatorSlider _deltaSlider = null!;
        private Label _lblDelta = null!;
        private FanCurveEditorPanel _cpuEditor = null!;
        private FanCurveEditorPanel _gpuEditor = null!;
        private readonly PredatorButton[] _profileButtons = new PredatorButton[FanCurveStore.ProfileDescriptors.Length];

        private FanCurveConfig _cpuEdit = null!;
        private FanCurveConfig _gpuEdit = null!;
        private bool _activeCpu = true;
        private byte _editingPowerMode;
        private bool _suppressLive;
        private bool _liveDirty;

        public event EventHandler? CloseRequested;

        public FanCurvePanelControl(FanCurveController controller, WmiController wmi)
        {
            _controller = controller;
            _wmi = wmi;
            _editingPowerMode = controller.Store.ActivePowerMode;
            LoadEditFromProfile(_editingPowerMode);

            DoubleBuffered = true;
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            Dock = DockStyle.Fill;

            BuildUI();
            ApplyTheme();
            AppTheme.Changed += OnThemeChanged;
            _controller.ActiveProfileChanged += OnActiveProfileChanged;

            _rpmTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _rpmTimer.Tick += (_, _) => UpdateRpmLabels();

            _liveApplyTimer = new System.Windows.Forms.Timer { Interval = LiveApplyIntervalMs };
            _liveApplyTimer.Tick += (_, _) => FlushLiveApply();

            EnabledChanged += (_, _) =>
            {
                if (Enabled)
                {
                    _rpmTimer.Start();
                    SyncEditorsToActiveProfileIfNeeded();
                }
                else
                {
                    _rpmTimer.Stop();
                    FlushLiveApply();
                }
            };
            _rpmTimer.Start();
            SelectFan(true);
            SyncProfileButtons();
            LayoutHeader(16);
            LayoutFooter(16);
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

        private FanCurveConfig ActiveConfig => _activeCpu ? _cpuEdit : _gpuEdit;

        private void LoadEditFromProfile(byte powerMode)
        {
            var profile = _controller.Store.GetProfile(powerMode);
            _cpuEdit = profile.Cpu.Clone();
            _gpuEdit = profile.Gpu.Clone();
            _cpuEdit.UpgradeLegacyPoints();
            _gpuEdit.UpgradeLegacyPoints();
            _editingPowerMode = powerMode;
        }

        private void BuildUI()
        {
            int pad = 16;

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

            _pnlProfiles = new Panel
            {
                Height = 44,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Padding = new Padding(pad, 6, pad, 4)
            };
            Controls.Add(_pnlProfiles);

            _lblProfile = new Label
            {
                Text = "PROFILE",
                AutoSize = true,
                Font = FontSection,
                ForeColor = AppTheme.SectionHeader,
                Location = new Point(pad, 12),
                BackColor = Color.Transparent
            };
            _pnlProfiles.Controls.Add(_lblProfile);

            for (int i = 0; i < FanCurveStore.ProfileDescriptors.Length; i++)
            {
                var (mode, name) = FanCurveStore.ProfileDescriptors[i];
                byte captured = mode;
                _profileButtons[i] = new PredatorButton
                {
                    Text = name,
                    Size = new Size(78, 28),
                    Tag = mode
                };
                _profileButtons[i].Click += (_, _) => SelectProfile(captured);
                _pnlProfiles.Controls.Add(_profileButtons[i]);
            }
            _pnlProfiles.Resize += (_, _) => LayoutProfiles(pad);

            _pnlHeader = new Panel
            {
                Height = 40,
                Dock = DockStyle.Top,
                BackColor = AppTheme.TitleBarBackground
            };
            Controls.Add(_pnlHeader);

            var lblTitle = new Label
            {
                Text = "Curves",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = AppTheme.PrimaryText,
                AutoSize = true,
                Location = new Point(pad, 11),
                BackColor = Color.Transparent
            };
            _pnlHeader.Controls.Add(lblTitle);

            _btnCpu = new PredatorButton { Text = "CPU", Size = new Size(72, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnCpu.Click += (_, _) => SelectFan(true);
            _pnlHeader.Controls.Add(_btnCpu);

            _btnGpu = new PredatorButton { Text = "GPU", Size = new Size(72, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnGpu.Click += (_, _) => SelectFan(false);
            _pnlHeader.Controls.Add(_btnGpu);

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
                FlushLiveApply();
                CloseRequested?.Invoke(this, EventArgs.Empty);
            };
            _lblClose.MouseEnter += (_, _) => _lblClose.ForeColor = AppTheme.CaptionCloseHover;
            _lblClose.MouseLeave += (_, _) => _lblClose.ForeColor = AppTheme.CaptionButton;
            _pnlHeader.Controls.Add(_lblClose);
            _pnlHeader.Resize += (_, _) => LayoutHeader(pad);

            _btnSave = new PredatorButton
            {
                Text = "Save",
                Size = new Size(108, 32),
                Location = new Point(0, 2),
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            _btnSave.Click += (_, _) => SaveProfile();
            _pnlFooterRow.Controls.Add(_btnSave);

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
                Size = new Size(140, 28),
                Minimum = 1,
                Maximum = 10,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            _deltaSlider.ValueChanged += (_, _) =>
            {
                int v = _deltaSlider.Value;
                ActiveConfig.DeltaTemperature = v;
                _lblDelta.Text = $"{v} °C";
                ScheduleLiveApply();
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
            _cpuEditor.PointsChanged += (_, _) =>
            {
                UpdateHint();
                ScheduleLiveApply();
            };
            pnlContent.Controls.Add(_cpuEditor);

            _gpuEditor = new FanCurveEditorPanel(_gpuEdit, FanKind.Gpu);
            _gpuEditor.Dock = DockStyle.Fill;
            _gpuEditor.Visible = false;
            _gpuEditor.PointsChanged += (_, _) =>
            {
                UpdateHint();
                ScheduleLiveApply();
            };
            pnlContent.Controls.Add(_gpuEditor);

            Paint += (_, e) =>
            {
                using var pen = new Pen(AppTheme.Separator);
                e.Graphics.DrawLine(pen, 0, 0, 0, Height);
            };
        }

        private void LayoutHeader(int pad)
        {
            _lblClose.Location = new Point(_pnlHeader.Width - pad - 12, 9);
            _btnGpu.Location = new Point(_lblClose.Left - 80, 6);
            _btnCpu.Location = new Point(_btnGpu.Left - 80, 6);
        }

        private void LayoutProfiles(int pad)
        {
            int x = _lblProfile.Right + 12;
            const int gap = 6;
            int available = _pnlProfiles.ClientSize.Width - pad - x;
            int btnW = Math.Max(64, Math.Min(86, (available - gap * (_profileButtons.Length - 1)) / _profileButtons.Length));
            for (int i = 0; i < _profileButtons.Length; i++)
            {
                _profileButtons[i].SetBounds(x + i * (btnW + gap), 8, btnW, 28);
            }
        }

        private void LayoutFooter(int pad)
        {
            int y = 4;
            _btnSave.Location = new Point(0, y);

            int deltaLeft = _btnSave.Right + 24;
            _lblDeltaTitle.Location = new Point(deltaLeft, y + 8);

            int sliderLeft = _lblDeltaTitle.Right + 8;
            int sliderMaxRight = _pnlFooter.ClientSize.Width - pad - 48;
            int sliderWidth = Math.Max(80, Math.Min(160, sliderMaxRight - sliderLeft));
            _deltaSlider.Location = new Point(sliderLeft, y + 2);
            _deltaSlider.Width = sliderWidth;

            _lblDelta.Location = new Point(_deltaSlider.Right + 8, y + 8);
            UpdateSaveCaption();
        }

        private void UpdateSaveCaption()
        {
            string name = FanCurveStore.ProfileName(_editingPowerMode);
            _btnSave.Text = $"Save · {name}";
        }

        private void SelectProfile(byte powerMode)
        {
            if (powerMode == _editingPowerMode) return;

            FlushLiveApply();
            LoadEditFromProfile(powerMode);
            RebindEditors();
            SyncProfileButtons();
            SelectFan(_activeCpu);
            UpdateSaveCaption();
        }

        private void SyncEditorsToActiveProfileIfNeeded()
        {
            byte active = _controller.Store.ActivePowerMode;
            if (active == _editingPowerMode) return;
            SelectProfile(active);
        }

        private void OnActiveProfileChanged(object? sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(() => OnActiveProfileChanged(sender, e));
                return;
            }

            // Follow power-mode changes from the main dashboard while Curves is open.
            SelectProfile(_controller.Store.ActivePowerMode);
        }

        private void SyncProfileButtons()
        {
            for (int i = 0; i < _profileButtons.Length; i++)
            {
                byte mode = (byte)(_profileButtons[i].Tag ?? (byte)0x01);
                _profileButtons[i].IsActive = mode == _editingPowerMode;
            }
        }

        private void RebindEditors()
        {
            _suppressLive = true;
            try
            {
                _cpuEditor.Rebind(_cpuEdit);
                _gpuEditor.Rebind(_gpuEdit);
            }
            finally
            {
                _suppressLive = false;
            }
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
            _suppressLive = true;
            try
            {
                _deltaSlider.Value = delta;
                _lblDelta.Text = $"{delta} °C";
            }
            finally
            {
                _suppressLive = false;
            }

            UpdateHint();
            UpdateRpmLabels();
            LayoutFooter(16);
        }

        private void UpdateHint()
        {
            var cfg = ActiveConfig;
            string profile = FanCurveStore.ProfileName(_editingPowerMode);
            _lblHint.Text =
                $"{profile}  ·  Points: {cfg.Points.Count}/{FanCurveConfig.MaxPoints}  ·  Live  ·  Fan step {FanRpmMap.EcStepPercent}%  ·  Double-click add  ·  Right-click remove";
        }

        private void ScheduleLiveApply()
        {
            if (_suppressLive) return;
            _liveDirty = true;
            if (!_liveApplyTimer.Enabled)
                _liveApplyTimer.Start();
        }

        private void FlushLiveApply()
        {
            _liveApplyTimer.Stop();
            if (!_liveDirty) return;
            _liveDirty = false;

            ActiveConfig.DeltaTemperature = _deltaSlider.Value;
            _cpuEditor.Commit();
            _gpuEditor.Commit();
            _cpuEdit.DeltaTemperature = Math.Clamp(_cpuEdit.DeltaTemperature, 1, 10);
            _gpuEdit.DeltaTemperature = Math.Clamp(_gpuEdit.DeltaTemperature, 1, 10);
            _controller.ApplyLive(_cpuEdit, _gpuEdit, _editingPowerMode);
        }

        private void SaveProfile()
        {
            FlushLiveApply();
            _controller.SaveProfile(_cpuEdit, _gpuEdit, _editingPowerMode);
            UpdateHint();
        }

        private void UpdateRpmLabels()
        {
            int cpuRpm = _wmi.CpuFanRpm;
            int gpuRpm = _wmi.GpuFanRpm;
            _cpuEditor.UpdateLive(cpuRpm, _wmi.CpuTemp);
            _gpuEditor.UpdateLive(gpuRpm, _wmi.GpuTemp);
        }

        private void ApplyTheme()
        {
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            _pnlHeader.BackColor = AppTheme.TitleBarBackground;
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FlushLiveApply();
                AppTheme.Changed -= OnThemeChanged;
                _controller.ActiveProfileChanged -= OnActiveProfileChanged;
                _rpmTimer.Stop();
                _rpmTimer.Dispose();
                _liveApplyTimer.Stop();
                _liveApplyTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class FanCurveEditorPanel : Panel
        {
            private FanCurveConfig _config;

            private RadioButton _rbSlope = null!;
            private RadioButton _rbStair = null!;
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
                    PointsChanged?.Invoke(this, EventArgs.Empty);
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
                    PointsChanged?.Invoke(this, EventArgs.Empty);
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
                header.Resize += (_, _) => _lblRpm.Location = new Point(Math.Max(200, header.Width - _lblRpm.Width - 4), 11);
                Controls.Add(header);
            }

            public void Rebind(FanCurveConfig config)
            {
                _config = config;
                _graph.BindConfig(_config);
                _rbSlope.Checked = _config.SlopeMode;
                _rbStair.Checked = !_config.SlopeMode;
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
