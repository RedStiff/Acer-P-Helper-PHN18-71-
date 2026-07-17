using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public partial class Form1 : BorderlessForm
    {
        #region Win32 Interop — Single Instance

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int HWND_BROADCAST = 0xffff;
        private static readonly uint WM_SHOWME = RegisterWindowMessage("PREDATOR_CONTROL_SHOW_INSTANCE");
        private static readonly Mutex _appMutex = new(true, "PredatorControlApp_Unique_System_Mutex_999");

        #endregion

        #region Win32 Interop — Foreground

        private const int SwRestore = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VkMenu = 0x12;
        private const uint KeyeventfExtendedkey = 0x0001;
        private const uint KeyeventfKeyup = 0x0002;

        #endregion

        #region Win32 Interop — Window Dragging

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        #endregion

        #region Win32 Interop — Display Control

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll")]
        private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int DM_DISPLAYFREQUENCY = 0x400000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
            public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        #endregion

        #region Fields

        private WmiController _wmi = new();
        private HardwareClockReader _clockReader = new();
        private System.Windows.Forms.Timer _timer = new();
        private NotifyIcon _trayIcon = new();
        private ContextMenuStrip _trayMenu = new();
        private KeyboardColorSettings _keyboardColorSettings = new();

        private int _cpuTemp, _gpuTemp;
        private int _cpuFanRpm, _gpuFanRpm;
        private int? _lastTrayCpuMhz, _lastTrayGpuMhz;

        private bool? _isPluggedIn = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        private bool _isClosing;
        private int _maxHz;
        private float _dpiScale = 1f; 
        private int _formW;
        private int _fixedClientHeight;
        private int _lightingTargetW;
        private int _curvesTargetW;
        private Panel _pnlShell = null!;
        private Panel _pnlMain = null!;
        private Panel _pnlCurves = null!;
        private LightingSideForm? _lightingSideForm;
        private FanCurvePanelControl? _curvesControl;
        private ShellPanelAnimator? _shellAnimator;
        private LiveLightingApplier? _liveLighting;
        private bool _lightingPanelOpen;
        private bool _curvesPanelOpen;

        private static readonly Font FontTitle = new("Segoe UI", 9.5f, FontStyle.Bold);
        private static readonly Font FontSectionHeader = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontHeaderLight = new("Segoe UI", 8.5f, FontStyle.Regular);
        private static readonly Font FontBody = new("Segoe UI", 9.5f, FontStyle.Regular);
        private static readonly Font FontBodyBold = new("Segoe UI", 9.5f, FontStyle.Bold);

        private Panel _pnlTitle = null!;
        private Panel _pnlBody = null!;
        private Label _lblTitle = null!, _lblCpuTemp = null!, _lblGpuTemp = null!;
        private Label _lblCpuClock = null!, _lblGpuClock = null!;
        private Label _lblCpuFanRpm = null!, _lblGpuFanRpm = null!;
        private Label _lblMinimize = null!, _lblClose = null!;
        private Label _lblPowerStatus = null!, _lblFanStatus = null!;

        private PredatorButton _btnQuiet = null!, _btnBalanced = null!, _btnPerform = null!,
                               _btnTurbo = null!, _btnEco = null!;

        private PredatorButton? _btnGpuIntegrated, _btnGpuDiscrete;
        private ToolTip? _graphicsToolTip;

        private PredatorButton _btnAutoFan = null!, _btnMaxFan = null!, _btnCustomFan = null!, _btnAdvancedFan = null!;
        private PredatorSlider _cpuFanSlider = null!, _gpuFanSlider = null!;
        private Label _lblCpuFanHdr = null!, _lblGpuFanHdr = null!;
        private Panel _pnlCustomFans = null!;
        private Panel _pnlAdvancedFans = null!;
        private PredatorButton _btnGraphicalSettings = null!;
        private FanCurveController _fanCurve = null!;
        private bool _isUpdatingFanSliders;
        private PredatorButton _btn60Hz = null!, _btnMaxHz = null!;

        private PredatorButton _btnLightingSettings = null!;
        private int _currentRgbMode = 3;
        private int _currentRgbSpeed = 50;
        private LogoLightingSettings _logoSettings = new();
        
        private PredatorButton? _activePowerBtn, _activeGraphicsBtn, _activeFanBtn, _activeDisplayBtn;
        private bool _isUpdatingBattery;

        private PredatorSwitch _switchBatteryLimit = null!;
        private Label _lblBatteryStatus = null!;

        
        private GameSyncController _gameSync = null!;
        private PredatorToggle _switchGameSync = null!;
        private Label _lblGameSyncStatus = null!;
        private PredatorButton _btnConfigureGames = null!;
        private bool _isGameSyncOverriding;
        private PredatorKeyListener? _predatorKey;
        private GlobalHotkeyListener? _globalHotkey;
        private AppSettings _appSettings = AppSettings.Load();

        private static readonly string[] RgbModeNames = { "Static", "Breathing", "Neon", "Wave", "Shifting", "Zoom", "Meteor", "Twinkling" };

        private ToolStripMenuItem _trayPowerQuiet = null!, _trayPowerBal = null!, _trayPowerPerf = null!,
                                  _trayPowerTurbo = null!, _trayPowerEco = null!;
        private ToolStripMenuItem? _trayGraphicsIntegrated, _trayGraphicsDiscrete;
        private ToolStripMenuItem _trayFanAuto = null!, _trayFanMax = null!, _trayFanCustom = null!, _trayFanAdvanced = null!;
        private ToolStripMenuItem _trayDisplay60 = null!, _trayDisplayMax = null!;
        private ToolStripMenuItem _trayBatteryLimit80 = null!, _trayBatteryLimit100 = null!;
        private ToolStripMenuItem _trayBatteryMenu = null!;
        private ToolStripMenuItem _trayRgbStatic = null!, _trayRgbBreathe = null!, _trayRgbNeon = null!,
                                  _trayRgbWave = null!, _trayRgbShift = null!, _trayRgbZoom = null!,
                                  _trayRgbMeteor = null!, _trayRgbTwinkle = null!;

        #endregion

        #region DPI Scaling Helper

        private int S(int px) => (int)(px * _dpiScale);

        #endregion

        #region Constructor

        public Form1()
        {
            if (!_appMutex.WaitOne(TimeSpan.Zero, true))
            {
                PostMessage((IntPtr)HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                Environment.Exit(0);
                return;
            }

            InitializeComponent();
            this.DoubleBuffered = true;
            _maxHz = GetMaxRefreshRate();
            _wmi.ProbeCustomFanSupport();
            _wmi.ProbeGraphicsModeSupport();
            _fanCurve = new FanCurveController(_wmi);

            _dpiScale = this.DeviceDpi / 96f;

            BuildUI();
            _liveLighting = new LiveLightingApplier(
                this,
                applyHardware: ApplyLightingSnapshot,
                saveState: PersistLightingSnapshot);
            BuildTrayMenu();
            SetupSystemTray();
            RegisterStartup();
            ApplyTheme();
            HandleCreated += (_, _) => SetupPredatorKey();
            AppTheme.Changed += OnThemeChanged;

            if (GetCurrentRefreshRate() <= 60)
            {
                HighlightBtn(_btn60Hz, ref _activeDisplayBtn);
                CheckTrayItem(_trayDisplay60, _trayDisplay60, _trayDisplayMax);
            }
            else
            {
                HighlightBtn(_btnMaxHz, ref _activeDisplayBtn);
                CheckTrayItem(_trayDisplayMax, _trayDisplay60, _trayDisplayMax);
            }

            LoadMemory();

            _gameSync = new GameSyncController();
            _gameSync.GameDetected += OnGameDetected;
            _gameSync.GameExited += OnGameExited;

            if (_gameSync.IsEnabled)
            {
                _switchGameSync.Checked = true;
                _lblGameSyncStatus.Text = "Active \u2014 Monitoring";
            }

            _timer.Interval = 2000;
            _timer.Tick += UpdateTelemetry;
            _timer.Start();

            this.Shown += (s, e) =>
            {
                PreRenderShell();
                if (Environment.CommandLine.Contains("-hidden")) HideApp();
            };
        }

        private static void EnableDoubleBuffer(Control control)
        {
            typeof(Control).GetProperty(
                "DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(control, true);
        }

        /// <summary>Main + curves only. Lighting is a separate attached window.</summary>
        private void ApplyShellViewport(int mainW, int rightReveal)
        {
            int h = _fixedClientHeight;
            int shellW = mainW + _curvesTargetW;

            _pnlShell.SuspendLayout();
            try
            {
                _pnlMain.SetBounds(0, 0, mainW, h);
                _pnlCurves.SetBounds(mainW, 0, _curvesTargetW, h);
                _pnlShell.SetBounds(0, 0, shellW, h);
                _pnlCurves.Visible = rightReveal > 0;
            }
            finally
            {
                _pnlShell.ResumeLayout(false);
            }
        }

        private void PrepareShellPanels()
        {
            EnsureCurvesControl();
            _curvesControl!.Dock = DockStyle.Fill;
            _curvesControl.Enabled = false;
            ApplyShellViewport(_formW, 0);
        }

        private void PreRenderShell()
        {
            if (_curvesControl == null) return;

            int shellW = _formW + _curvesTargetW;
            int h = _fixedClientHeight;

            SuspendLayout();
            try
            {
                _pnlCurves.Visible = false;
                _pnlMain.SetBounds(0, 0, _formW, h);
                _pnlCurves.SetBounds(_formW, 0, _curvesTargetW, h);
                _pnlShell.SetBounds(0, 0, shellW, h);

                _pnlShell.PerformLayout();
                _pnlMain.PerformLayout();
                _pnlCurves.PerformLayout();
                _curvesControl.PerformLayout();

                try
                {
                    using var bmp = new Bitmap(Math.Max(1, shellW), Math.Max(1, h));
                    _pnlShell.DrawToBitmap(bmp, new Rectangle(0, 0, shellW, h));
                }
                catch { }

                _shellAnimator?.ApplyImmediate(0);
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        private void SetCurvesPanelInteractive(bool enabled)
        {
            if (_curvesControl != null)
                _curvesControl.Enabled = enabled;
        }

        protected override void WndProc(ref Message m)
        {
            if (_globalHotkey?.ProcessWndProc(ref m) == true) return;

            if (m.Msg == WM_SHOWME)
            {
                ShowApp();
                return;
            }
            base.WndProc(ref m);
        }

        #endregion

        #region Display Control

        private int GetCurrentRefreshRate()
        {
            DEVMODE dm = new(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) ? dm.dmDisplayFrequency : 60;
        }

        private int GetMaxRefreshRate()
        {
            DEVMODE dm = new(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            int maxHz = 60, modeNum = 0;
            while (EnumDisplaySettings(null, modeNum, ref dm))
            {
                if (dm.dmDisplayFrequency > maxHz) maxHz = dm.dmDisplayFrequency;
                modeNum++;
            }
            return maxHz;
        }

        private void SetRefreshRate(int hz)
        {
            DEVMODE dm = new(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            {
                dm.dmDisplayFrequency = hz;
                dm.dmFields = DM_DISPLAYFREQUENCY;
                ChangeDisplaySettings(ref dm, CDS_UPDATEREGISTRY);
            }
        }

        #endregion

        #region System Tray

        private void SetupSystemTray()
        {
            try { _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _trayIcon.Icon = SystemIcons.Application; }

            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Text = AppInfo.DisplayName;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) => ShowApp();
            UpdateTrayHotkeyHint();
        }

        private void UpdateTrayHotkeyHint()
        {
            UpdateTrayTooltip(_lastTrayCpuMhz, _lastTrayGpuMhz);
        }

        private void UpdateTrayTooltip(int? cpuMhz, int? gpuMhz)
        {
            _lastTrayCpuMhz = cpuMhz;
            _lastTrayGpuMhz = gpuMhz;

            string hotkey = _globalHotkey?.HotkeyDisplayName ?? _appSettings.ToggleHotkeyDisplayName();
            string cpuTempTray = _cpuTemp > 0 ? $"{_cpuTemp}°C" : "N/A";
            string gpuTempTray = _gpuTemp > 0 ? $"{_gpuTemp}°C" : "N/A";
            string cpuClockTray = cpuMhz > 0 ? $"{cpuMhz} MHz" : "--";
            string gpuClockTray = gpuMhz > 0 ? $"{gpuMhz} MHz" : "--";
            string cpuFanTray = _cpuFanRpm > 0 ? $"{_cpuFanRpm}" : "N/A";
            string gpuFanTray = _gpuFanRpm > 0 ? $"{_gpuFanRpm}" : "N/A";

            _trayIcon.Text =
                $"{AppInfo.DisplayName} ({hotkey})\n" +
                $"CPU: {cpuTempTray} · {cpuClockTray}\n" +
                $"GPU: {gpuTempTray} · {gpuClockTray}\n" +
                $"Fans: {cpuFanTray} / {gpuFanTray} RPM";
        }

        private void BuildTrayMenu()
        {
            _trayMenu = new ContextMenuStrip();

            var powerMenu = new ToolStripMenuItem("  Power Mode"); 
            _trayPowerQuiet = new ToolStripMenuItem("Silent", null, (s, e) => ApplyPowerMode(0x00, _btnQuiet));
            _trayPowerBal = new ToolStripMenuItem("Balanced", null, (s, e) => ApplyPowerMode(0x01, _btnBalanced));
            _trayPowerPerf = new ToolStripMenuItem("Performance", null, (s, e) => ApplyPowerMode(0x04, _btnPerform));
            _trayPowerTurbo = new ToolStripMenuItem("Turbo", null, (s, e) => ApplyPowerMode(0x05, _btnTurbo));
            _trayPowerEco = new ToolStripMenuItem("Eco", null, (s, e) => ApplyPowerMode(0x06, _btnEco));
            powerMenu.DropDownItems.AddRange([_trayPowerQuiet, _trayPowerBal, _trayPowerPerf, _trayPowerTurbo, _trayPowerEco]);

            ToolStripMenuItem? graphicsMenu = null;
            if (_wmi.SupportsGraphicsMode)
            {
                graphicsMenu = new ToolStripMenuItem("  Graphics indicator");
                _trayGraphicsIntegrated = new ToolStripMenuItem("Integrated / Hybrid") { Enabled = false };
                _trayGraphicsDiscrete = new ToolStripMenuItem("Discrete") { Enabled = false };
                graphicsMenu.DropDownItems.AddRange([_trayGraphicsIntegrated, _trayGraphicsDiscrete]);
                graphicsMenu.DropDownOpening += (_, _) => RefreshGraphicsIndicators();
            }

            var fanMenu = new ToolStripMenuItem("  Fan Mode");
            _trayFanAuto = new ToolStripMenuItem("Auto", null, (s, e) => ApplyFanMode(0x01, _btnAutoFan));
            _trayFanMax = new ToolStripMenuItem("Max", null, (s, e) => ApplyFanMode(0x02, _btnMaxFan));
            _trayFanCustom = new ToolStripMenuItem("Custom", null, (s, e) => ApplyFanMode(0x03, _btnCustomFan));
            _trayFanAdvanced = new ToolStripMenuItem("Advanced", null, (s, e) => ApplyFanMode(FanCurveController.FanModeAdvanced, _btnAdvancedFan));
            fanMenu.DropDownItems.AddRange([_trayFanAuto, _trayFanMax, _trayFanCustom, _trayFanAdvanced]);

            var displayMenu = new ToolStripMenuItem("  Display");
            _trayDisplay60 = new ToolStripMenuItem("60 Hz", null, (s, e) => ApplyDisplayMode(60, _btn60Hz));
            _trayDisplayMax = new ToolStripMenuItem($"{_maxHz} Hz", null, (s, e) => ApplyDisplayMode(_maxHz, _btnMaxHz));
            displayMenu.DropDownItems.AddRange([_trayDisplay60, _trayDisplayMax]);

            var rgbMenu = new ToolStripMenuItem("  Keyboard RGB");
            _trayRgbStatic = new ToolStripMenuItem("Static", null, (s, e) => ApplyRgbModeFromDropdown(0));
            _trayRgbBreathe = new ToolStripMenuItem("Breathing", null, (s, e) => ApplyRgbModeFromDropdown(1));
            _trayRgbNeon = new ToolStripMenuItem("Neon", null, (s, e) => ApplyRgbModeFromDropdown(2));
            _trayRgbWave = new ToolStripMenuItem("Wave", null, (s, e) => ApplyRgbModeFromDropdown(3));
            _trayRgbShift = new ToolStripMenuItem("Shifting", null, (s, e) => ApplyRgbModeFromDropdown(4));
            _trayRgbZoom = new ToolStripMenuItem("Zoom", null, (s, e) => ApplyRgbModeFromDropdown(5));
            _trayRgbMeteor = new ToolStripMenuItem("Meteor", null, (s, e) => ApplyRgbModeFromDropdown(6));
            _trayRgbTwinkle = new ToolStripMenuItem("Twinkling", null, (s, e) => ApplyRgbModeFromDropdown(7));
            rgbMenu.DropDownItems.AddRange([_trayRgbStatic, _trayRgbBreathe, _trayRgbNeon, _trayRgbWave,
                                            _trayRgbShift, _trayRgbZoom, _trayRgbMeteor, _trayRgbTwinkle]);

            _trayBatteryMenu = new ToolStripMenuItem("  Battery Limit");
            _trayBatteryLimit80 = new ToolStripMenuItem("Limit to 80%", null, (s, e) => ApplyBatteryLimit(true));
            _trayBatteryLimit100 = new ToolStripMenuItem("Full Charge (100%)", null, (s, e) => ApplyBatteryLimit(false));
            _trayBatteryMenu.DropDownItems.AddRange([_trayBatteryLimit80, _trayBatteryLimit100]);

            _trayMenu.Items.Add(powerMenu);
            if (graphicsMenu != null)
                _trayMenu.Items.Add(graphicsMenu);
            _trayMenu.Items.Add(fanMenu);
            _trayMenu.Items.Add(displayMenu);
            _trayMenu.Items.Add(_trayBatteryMenu);
            _trayMenu.Items.Add(rgbMenu);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Open Dashboard", null, (s, e) => ShowApp());
            _trayMenu.Items.Add("Exit", null, (s, e) => { _isClosing = true; Application.Exit(); });
        }

        #endregion

        #region UI Building

        private void BuildUI()
        {
            this.Controls.Clear();
            this.BackColor = AppTheme.FormBackground;
            this.ForeColor = AppTheme.PrimaryText;

            _formW = S(UiSettings.FixedMainPanelWidth);
            // Taskbar / Start / Alt+Tab use Form.Text — must be set even for borderless UI.
            this.Text = AppInfo.DisplayName;
            this.ShowIcon = true;
            this.ShowInTaskbar = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = Size.Empty;
            this.MaximumSize = Size.Empty;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int lightingW = S(UiSettings.FixedLightingPanelWidth);
            int curvesW = S(UiSettings.FixedCurvesPanelWidth);
            _lightingTargetW = lightingW;
            _curvesTargetW = curvesW;

            _pnlShell = new Panel
            {
                BackColor = AppTheme.FormBackground,
                Location = Point.Empty
            };
            EnableDoubleBuffer(_pnlShell);
            Controls.Add(_pnlShell);

            _pnlMain = new Panel { BackColor = AppTheme.FormBackground };
            _pnlCurves = new Panel { BackColor = AppTheme.FormBackground };
            EnableDoubleBuffer(_pnlMain);
            EnableDoubleBuffer(_pnlCurves);
            _pnlShell.Controls.AddRange([_pnlMain, _pnlCurves]);

            int secAfterHeader = S(16);
            int secAfterBtn = S(16);
            int sepGap = S(12);
            int rowGap = S(20);

            int pad = S(24);
            int contentW = _formW - pad * 2;
            int gap = S(6);
            int btnH = S(34);
            int y = 0;

            _pnlBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, AutoScroll = true };
            _pnlMain.Controls.Add(_pnlBody);

            _pnlTitle = new Panel { Height = S(40), Dock = DockStyle.Top, BackColor = AppTheme.TitleBarBackground };
            _pnlTitle.MouseDown += TitleBar_MouseDown;
            _pnlMain.Controls.Add(_pnlTitle);
            _pnlTitle.Resize += (_, _) =>
            {
                int titlePad = S(24);
                _lblClose.Location = new Point(_pnlTitle.Width - titlePad - S(16), S(9));
                _lblMinimize.Location = new Point(_lblClose.Left - S(28), S(9));
            };
            var picIcon = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(S(16), S(16)), Location = new Point(pad - S(4), S(12)), BackColor = Color.Transparent };
            try { var extIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); if (extIcon != null) picIcon.Image = extIcon.ToBitmap(); } catch { }
            picIcon.MouseDown += TitleBar_MouseDown;
            _pnlTitle.Controls.Add(picIcon);

            _lblTitle = new Label { Text = AppInfo.DisplayName, ForeColor = AppTheme.PrimaryText, Font = FontTitle, AutoSize = true, Location = new Point(pad + S(20), S(11)), BackColor = Color.Transparent, Tag = "title" };
            _lblTitle.MouseDown += TitleBar_MouseDown;
            _pnlTitle.Controls.Add(_lblTitle);

            var captionFont = new Font("Segoe UI", 10f, FontStyle.Regular);
            _lblClose = new Label { Text = "\u2715", ForeColor = AppTheme.CaptionButton, Font = captionFont, AutoSize = true, Location = new Point(_formW - pad - S(16), S(9)), Cursor = Cursors.Hand, BackColor = Color.Transparent, Tag = "caption-close", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _lblMinimize = new Label { Text = "\u2014", ForeColor = AppTheme.CaptionButton, Font = captionFont, AutoSize = true, Location = new Point(_lblClose.Left - S(28), S(9)), Cursor = Cursors.Hand, BackColor = Color.Transparent, Tag = "caption-min", Anchor = AnchorStyles.Top | AnchorStyles.Right };

            _lblClose.Click += (s, e) => { this.Close(); };
            _lblMinimize.Click += (s, e) => { this.WindowState = FormWindowState.Minimized; };
            _lblClose.MouseEnter += (s, e) => _lblClose.ForeColor = AppTheme.CaptionCloseHover;
            _lblClose.MouseLeave += (s, e) => _lblClose.ForeColor = AppTheme.CaptionButton;
            _lblMinimize.MouseEnter += (s, e) => _lblMinimize.ForeColor = AppTheme.CaptionButtonHover;
            _lblMinimize.MouseLeave += (s, e) => _lblMinimize.ForeColor = AppTheme.CaptionButton;

            _pnlTitle.Controls.Add(_lblClose);
            _pnlTitle.Controls.Add(_lblMinimize);

            y = S(16);

            MakeLabel("CPU:", pad, y, FontBody, AppTheme.SecondaryText, "sub");
            _lblCpuTemp = MakeLabel("43°C", pad + S(34), y, FontBodyBold, AppTheme.PrimaryText, "value");

            MakeLabel("GPU:", _formW / 2 + S(10), y, FontBody, AppTheme.SecondaryText, "sub");
            _lblGpuTemp = MakeLabel("39°C", _formW / 2 + S(46), y, FontBodyBold, AppTheme.PrimaryText, "value");

            y += rowGap;
            MakeLabel("CPU clock:", pad, y, FontBody, AppTheme.SecondaryText, "sub");
            _lblCpuClock = MakeLabel("-- MHz", pad + S(74), y, FontBodyBold, AppTheme.PrimaryText, "value");

            MakeLabel("GPU clock:", _formW / 2 + S(10), y, FontBody, AppTheme.SecondaryText, "sub");
            _lblGpuClock = MakeLabel("-- MHz", _formW / 2 + S(84), y, FontBodyBold, AppTheme.PrimaryText, "value");

            y += rowGap;
            MakeLabel("CPU fan:", pad, y, FontBody, AppTheme.SecondaryText, "sub");
            _lblCpuFanRpm = MakeLabel("-- RPM", pad + S(62), y, FontBodyBold, AppTheme.PrimaryText, "value");

            MakeLabel("GPU fan:", _formW / 2 + S(10), y, FontBody, AppTheme.SecondaryText, "sub");
            _lblGpuFanRpm = MakeLabel("-- RPM", _formW / 2 + S(72), y, FontBodyBold, AppTheme.PrimaryText, "value");

            y += rowGap;
            MakeLabel("Fan mode:", pad, y, FontBody, AppTheme.SecondaryText, "sub");
            _lblFanStatus = MakeLabel("Auto", pad + S(74), y, FontBodyBold, AppTheme.PrimaryText, "value");

            MakeLabel("Power:", _formW / 2 + S(10), y, FontBody, AppTheme.SecondaryText, "sub");
            _lblPowerStatus = MakeLabel("Plugged In", _formW / 2 + S(56), y, FontBodyBold, AppTheme.PrimaryText, "value");

            y += S(22);
            AddSeparator(y);

            y += sepGap;
            MakeSectionHeader("POWER MODE", pad, y);
            
            y += secAfterHeader;
            int btnW = (contentW - 4 * gap) / 5;
            _btnQuiet = MakeButton("Silent", pad, y, btnW, btnH);
            _btnBalanced = MakeButton("Balanced", pad + (btnW + gap), y, btnW, btnH);
            _btnPerform = MakeButton("Perf", pad + (btnW + gap) * 2, y, btnW, btnH);
            _btnTurbo = MakeButton("Turbo", pad + (btnW + gap) * 3, y, btnW, btnH);
            _btnEco = MakeButton("Eco", pad + (btnW + gap) * 4, y, btnW, btnH);
            
            _btnQuiet.Click += (s, e) => ApplyPowerMode(0x00, _btnQuiet);
            _btnBalanced.Click += (s, e) => ApplyPowerMode(0x01, _btnBalanced);
            _btnPerform.Click += (s, e) => ApplyPowerMode(0x04, _btnPerform);
            _btnTurbo.Click += (s, e) => ApplyPowerMode(0x05, _btnTurbo);
            _btnEco.Click += (s, e) => ApplyPowerMode(0x06, _btnEco);

            y += btnH + secAfterBtn;
            if (_wmi.SupportsGraphicsMode)
            {
                MakeSectionHeader("GRAPHICS INDICATOR", pad, y);

                y += secAfterHeader;
                int gpuBtnW = (contentW - gap) / 2;
                _btnGpuIntegrated = MakeButton("Integrated", pad, y, gpuBtnW, btnH);
                _btnGpuDiscrete = MakeButton("Discrete", pad + gpuBtnW + gap, y, gpuBtnW, btnH);

                ConfigureGraphicsIndicator(_btnGpuIntegrated);
                ConfigureGraphicsIndicator(_btnGpuDiscrete);

                _graphicsToolTip = new ToolTip { ShowAlways = true, AutoPopDelay = 12000 };
                _graphicsToolTip.SetToolTip(_btnGpuIntegrated,
                    "Panel owned by Intel (Optimus / Hybrid).\nRead-only indicator — switch Display Mode in NVIDIA Control Panel.");
                _graphicsToolTip.SetToolTip(_btnGpuDiscrete,
                    "Panel owned by NVIDIA (NVIDIA GPU only / DDS discrete).\nRead-only indicator — switch Display Mode in NVIDIA Control Panel.");

                RefreshGraphicsIndicators();
                y += btnH + secAfterBtn;
            }

            MakeSectionHeader("FAN CONTROL", pad, y);
            
            y += secAfterHeader;
            int fanBtnW = (contentW - 3 * gap) / 4;
            _btnAutoFan = MakeButton("Auto", pad, y, fanBtnW, btnH);
            _btnMaxFan = MakeButton("Max", pad + (fanBtnW + gap), y, fanBtnW, btnH);
            _btnCustomFan = MakeButton("Custom", pad + (fanBtnW + gap) * 2, y, fanBtnW, btnH);
            _btnAdvancedFan = MakeButton("Advanced", pad + (fanBtnW + gap) * 3, y, fanBtnW, btnH);

            _btnAutoFan.Click += (s, e) => ApplyFanMode(0x01, _btnAutoFan);
            _btnMaxFan.Click += (s, e) => ApplyFanMode(0x02, _btnMaxFan);
            _btnCustomFan.Click += (s, e) => ApplyFanMode(0x03, _btnCustomFan);
            _btnAdvancedFan.Click += (s, e) => ApplyFanMode(FanCurveController.FanModeAdvanced, _btnAdvancedFan);

            y += btnH + S(12);
            int fanSliderW = (contentW - gap) / 2;
            int fanSliderH = S(28);
            int customFanBlockH = S(24) + fanSliderH;

            _pnlCustomFans = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(_formW, customFanBlockH),
                BackColor = Color.Transparent,
                Visible = false
            };
            _pnlBody.Controls.Add(_pnlCustomFans);

            _lblCpuFanHdr = MakeLabelIn(_pnlCustomFans, "CPU FAN: 50%", pad, 0, FontSectionHeader, AppTheme.SecondaryText, "sub");
            _lblGpuFanHdr = MakeLabelIn(_pnlCustomFans, "GPU FAN: 50%", _formW / 2 + S(10), 0, FontSectionHeader, AppTheme.SecondaryText, "sub");

            _cpuFanSlider = new PredatorSlider
            {
                Location = new Point(pad, S(24)),
                Size = new Size(fanSliderW, fanSliderH),
                Minimum = 0,
                Maximum = 100,
                Step = FanRpmMap.EcStepPercent,
                Value = 50,
                Enabled = false
            };
            _pnlCustomFans.Controls.Add(_cpuFanSlider);

            _gpuFanSlider = new PredatorSlider
            {
                Location = new Point(_formW / 2 + S(10), S(24)),
                Size = new Size(fanSliderW, fanSliderH),
                Minimum = 0,
                Maximum = 100,
                Step = FanRpmMap.EcStepPercent,
                Value = 50,
                Enabled = false
            };
            _pnlCustomFans.Controls.Add(_gpuFanSlider);

            _cpuFanSlider.ValueChanged += (s, e) =>
            {
                if (!_isUpdatingFanSliders)
                    _lblCpuFanHdr.Text = FormatFanSliderHeader("CPU FAN", _cpuFanSlider.Value, _cpuFanRpm, FanKind.Cpu);
            };
            _cpuFanSlider.ValueCommitted += (s, e) => ApplyCustomFanSpeeds();

            _gpuFanSlider.ValueChanged += (s, e) =>
            {
                if (!_isUpdatingFanSliders)
                    _lblGpuFanHdr.Text = FormatFanSliderHeader("GPU FAN", _gpuFanSlider.Value, _gpuFanRpm, FanKind.Gpu);
            };
            _gpuFanSlider.ValueCommitted += (s, e) => ApplyCustomFanSpeeds();

            int advancedBlockH = btnH + S(8);
            _pnlAdvancedFans = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(_formW, advancedBlockH),
                BackColor = Color.Transparent,
                Visible = false
            };
            _pnlBody.Controls.Add(_pnlAdvancedFans);

            _btnGraphicalSettings = new PredatorButton
            {
                Text = "Curves",
                Location = new Point(pad, 0),
                Size = new Size(contentW, btnH)
            };
            _btnGraphicalSettings.LeadingText = ">>>";
            _btnGraphicalSettings.Click += (_, _) => ToggleCurvesPanel();
            _pnlAdvancedFans.Controls.Add(_btnGraphicalSettings);

            y += customFanBlockH + S(10);
            MakeSectionHeader("DISPLAY REFRESH RATE", pad, y);
            
            y += secAfterHeader;
            int dispBtnW = (contentW - gap) / 2;
            _btn60Hz = MakeButton("60 Hz", pad, y, dispBtnW, btnH);
            _btnMaxHz = MakeButton($"{_maxHz} Hz (Max)", pad + dispBtnW + gap, y, dispBtnW, btnH);

            _btn60Hz.Click += (s, e) => ApplyDisplayMode(60, _btn60Hz);
            _btnMaxHz.Click += (s, e) => ApplyDisplayMode(_maxHz, _btnMaxHz);

            y += btnH + secAfterBtn;
            MakeSectionHeader("BATTERY CHARGE LIMIT", pad, y);

            y += secAfterHeader;
            int switchH = S(30);
            _lblBatteryStatus = MakeLabel("Full Charge (100%)", pad, y, FontBody, AppTheme.SecondaryText, "sub");
            CenterV(_lblBatteryStatus, y, switchH);

            _switchBatteryLimit = new PredatorSwitch
            {
                Location = new Point(_formW - pad - S(60), y),
                Size = new Size(S(60), switchH)
            };
            _pnlBody.Controls.Add(_switchBatteryLimit);

            _switchBatteryLimit.CheckedChanged += (s, e) =>
            {
                ApplyBatteryLimit(_switchBatteryLimit.Checked);
            };

            y += switchH + secAfterBtn;
            MakeSectionHeader("KEYBOARD LIGHTING", pad, y);

            y += secAfterHeader;
            _btnLightingSettings = MakeButton("Lighting Settings", pad, y, contentW, btnH);
            _btnLightingSettings.LeadingText = "<<<";
            _btnLightingSettings.Click += (_, _) => ToggleLightingSettings();

            y += btnH + secAfterBtn;
            AddSeparator(y);
            y += sepGap;
            MakeSectionHeader("GAME SYNC", pad, y);

            y += secAfterHeader;
            int syncSwitchH = S(30);
            _lblGameSyncStatus = MakeLabel("Disabled", pad, y, FontBody, AppTheme.SecondaryText, "sub");
            CenterV(_lblGameSyncStatus, y, syncSwitchH);

            _switchGameSync = new PredatorToggle
            {
                Location = new Point(_formW - pad - S(48), y),
                Size = new Size(S(48), syncSwitchH)
            };
            _pnlBody.Controls.Add(_switchGameSync);

            _switchGameSync.CheckedChanged += (s, e) =>
            {
                _gameSync.IsEnabled = _switchGameSync.Checked;
                _lblGameSyncStatus.Text = _switchGameSync.Checked ? "Active — Monitoring" : "Disabled";
            };

            y += syncSwitchH + S(8);
            _btnConfigureGames = MakeButton("🎮  Configure Executables", pad, y, contentW, btnH);
            _btnConfigureGames.Click += (s, e) =>
            {
                using var form = new GameSyncForm(_gameSync, _maxHz);
                form.ShowDialog(this);
            };

            _fixedClientHeight = S(40) + y + btnH + pad;
            _shellAnimator = new ShellPanelAnimator(this, _formW, _fixedClientHeight, curvesW, ApplyShellViewport);
            PrepareShellPanels();
            _shellAnimator.ApplyImmediate(0);
            this.Paint += Form_Paint;

            _pnlBody.PerformLayout();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        private void Form_Paint(object? sender, PaintEventArgs e)
        {
            using var pen = new Pen(AppTheme.Separator);
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            if (_pnlTitle != null)
                e.Graphics.DrawLine(pen, _pnlMain.Left, _pnlTitle.Bottom - 1, _pnlMain.Right, _pnlTitle.Bottom - 1);

            if (_shellAnimator != null && _shellAnimator.RightPanelWidth > 0)
                e.Graphics.DrawLine(pen, _pnlCurves.Left, 0, _pnlCurves.Left, ClientSize.Height);
        }

        private void ToggleLightingSettings()
        {
            if (_lightingPanelOpen && _lightingSideForm != null && !_lightingSideForm.IsDisposed)
            {
                _lightingSideForm.BeginSlideClose();
                return;
            }

            OpenLightingSideForm();
        }

        private void OpenLightingSideForm()
        {
            if (_lightingSideForm != null && !_lightingSideForm.IsDisposed)
            {
                _lightingSideForm.BeginSlideClose();
            }

            var settings = KeyboardColorSettings.FromWmi(_wmi);
            settings.FourZone = _keyboardColorSettings.FourZone;
            settings.SolidColor = _keyboardColorSettings.SolidColor;
            settings.Brightness = _keyboardColorSettings.Brightness;
            for (int i = 0; i < 4; i++)
                settings.ZoneColors[i] = _keyboardColorSettings.ZoneColors[i];

            _lightingSideForm = new LightingSideForm(
                settings,
                _currentRgbMode,
                _currentRgbSpeed,
                _lightingTargetW,
                _fixedClientHeight,
                _logoSettings);
            _lightingSideForm.SettingsLiveChanged += OnLightingSettingsLiveChanged;
            _lightingSideForm.SettingsFlushRequested += OnLightingSettingsFlushRequested;
            _lightingSideForm.ClosedByUser += (_, _) => OnLightingSideClosed();
            _lightingSideForm.FormClosed += (_, _) =>
            {
                if (_lightingSideForm != null)
                    OnLightingSideClosed();
            };

            _lightingPanelOpen = true;
            _btnLightingSettings.IsActive = true;
            _btnLightingSettings.Invalidate();
            _lightingSideForm.ShowBesideOwner(this);
        }

        private void ToggleCurvesPanel()
        {
            if (_shellAnimator == null) return;

            if (_curvesPanelOpen)
            {
                _curvesPanelOpen = false;
                _shellAnimator.AnimateRight(false);
                SetCurvesPanelInteractive(false);
                _btnGraphicalSettings.IsActive = false;
                _btnGraphicalSettings.Invalidate();
                return;
            }

            _curvesPanelOpen = true;
            _shellAnimator.AnimateRight(true);
            SetCurvesPanelInteractive(true);
            _curvesControl?.Invalidate(true);
            _btnGraphicalSettings.IsActive = true;
            _btnGraphicalSettings.Invalidate();
        }

        private void EnsureCurvesControl()
        {
            if (_curvesControl != null) return;

            _curvesControl = new FanCurvePanelControl(_fanCurve, _wmi);
            _curvesControl.CloseRequested += (_, _) => ToggleCurvesPanel();
            _pnlCurves.Controls.Add(_curvesControl);
        }

        private void OnLightingSideClosed()
        {
            if (_lightingSideForm != null && !_lightingSideForm.IsDisposed)
            {
                try { _liveLighting?.FlushNowAndSave(_lightingSideForm.CreateSnapshot()); }
                catch { }
            }

            _lightingPanelOpen = false;
            _lightingSideForm = null;
            _btnLightingSettings.IsActive = false;
            _btnLightingSettings.Invalidate();
        }

        private void OnLightingSettingsLiveChanged(LightingSettingsSnapshot snapshot)
        {
            if (_isGameSyncOverriding) return;

            _keyboardColorSettings = snapshot.Colors;
            _currentRgbMode = snapshot.RgbMode;
            _currentRgbSpeed = snapshot.Speed;
            _logoSettings = snapshot.Logo;
            CheckRgbTrayFromMode(snapshot.RgbMode);
            _btnLightingSettings.Invalidate();
            _liveLighting?.Push(snapshot);
        }

        private void OnLightingSettingsFlushRequested(LightingSettingsSnapshot snapshot)
        {
            if (_isGameSyncOverriding) return;

            _keyboardColorSettings = snapshot.Colors;
            _currentRgbMode = snapshot.RgbMode;
            _currentRgbSpeed = snapshot.Speed;
            _logoSettings = snapshot.Logo;
            CheckRgbTrayFromMode(snapshot.RgbMode);
            _btnLightingSettings.Invalidate();
            _liveLighting?.Flush(snapshot);
        }

        private void PersistLightingSnapshot(LightingSettingsSnapshot snapshot)
        {
            if (_isGameSyncOverriding) return;
            SaveState("RGB_Mode", snapshot.RgbMode);
            SaveState("RGB_Speed", snapshot.Speed);
            SaveKeyboardColorState(snapshot.Colors);
            SaveLogoState(snapshot.Logo);
        }

        private void ApplyLightingSnapshot(LightingSettingsSnapshot snapshot)
        {
            if (snapshot.RgbMode == 0)
                _wmi.ApplyKeyboardColorSettings(snapshot.Colors);
            else
            {
                _wmi.SetRgbMode(
                    snapshot.RgbMode,
                    _wmi.LastR, _wmi.LastG, _wmi.LastB,
                    snapshot.Colors.Brightness,
                    snapshot.MappedSpeed,
                    0);
            }

            _wmi.SetLogoLighting(snapshot.Logo);
        }

        private void SaveLogoState(LogoLightingSettings logo)
        {
            SaveState("Logo_Enabled", logo.Enabled ? 1 : 0);
            SaveState("Logo_R", logo.Color.R);
            SaveState("Logo_G", logo.Color.G);
            SaveState("Logo_B", logo.Color.B);
            SaveState("Logo_Brightness", logo.Brightness);
        }

        private void LoadLogoState(RegistryKey key)
        {
            _logoSettings.Enabled = (int)key.GetValue("Logo_Enabled", 1) == 1;
            int r = (int)key.GetValue("Logo_R", 0xFF);
            int g = (int)key.GetValue("Logo_G", 0x00);
            int b = (int)key.GetValue("Logo_B", 0x00);
            _logoSettings.Color = Color.FromArgb(
                Math.Clamp(r, 0, 255),
                Math.Clamp(g, 0, 255),
                Math.Clamp(b, 0, 255));
            _logoSettings.Brightness = (byte)Math.Clamp((int)key.GetValue("Logo_Brightness", 100), 0, 100);
        }

        private void SaveKeyboardColorState(KeyboardColorSettings settings)
        {
            SaveState("RGB_R", settings.SolidColor.R);
            SaveState("RGB_G", settings.SolidColor.G);
            SaveState("RGB_B", settings.SolidColor.B);
            SaveState("RGB_FourZone", settings.FourZone ? 1 : 0);
            SaveState("Brightness", settings.Brightness);
            for (int i = 0; i < 4; i++)
            {
                SaveState($"RGB_Z{i}R", settings.ZoneColors[i].R);
                SaveState($"RGB_Z{i}G", settings.ZoneColors[i].G);
                SaveState($"RGB_Z{i}B", settings.ZoneColors[i].B);
            }
        }

        private void LoadKeyboardColorState(RegistryKey key)
        {
            int savedR = (int)key.GetValue("RGB_R", 0);
            int savedG = (int)key.GetValue("RGB_G", 150);
            int savedB = (int)key.GetValue("RGB_B", 255);
            _keyboardColorSettings.SolidColor = Color.FromArgb(savedR, savedG, savedB);
            _keyboardColorSettings.FourZone = (int)key.GetValue("RGB_FourZone", 0) == 1;
            for (int i = 0; i < 4; i++)
            {
                int zr = (int)key.GetValue($"RGB_Z{i}R", savedR);
                int zg = (int)key.GetValue($"RGB_Z{i}G", savedG);
                int zb = (int)key.GetValue($"RGB_Z{i}B", savedB);
                _keyboardColorSettings.ZoneColors[i] = Color.FromArgb(zr, zg, zb);
            }
        }

        private void MakeSectionHeader(string label, int x, int y)
        {
            MakeLabel(label, x, y, FontSectionHeader, AppTheme.SectionHeader, "section");
        }

        private Label MakeLabel(string text, int x, int y, Font font, Color color, string? tag = null)
        {
            var lbl = new Label
            {
                Text = text, Location = new Point(x, y), AutoSize = true, Font = font, ForeColor = color, BackColor = Color.Transparent, Tag = tag
            };
            _pnlBody.Controls.Add(lbl);
            return lbl;
        }

        private Label MakeLabelIn(Control parent, string text, int x, int y, Font font, Color color, string? tag = null)
        {
            var lbl = new Label
            {
                Text = text, Location = new Point(x, y), AutoSize = true, Font = font, ForeColor = color, BackColor = Color.Transparent, Tag = tag
            };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private PredatorButton MakeButton(string text, int x, int y, int width, int height)
        {
            var btn = new PredatorButton { Text = text, Location = new Point(x, y), Size = new Size(width, height) };
            _pnlBody.Controls.Add(btn);
            return btn;
        }

        private void AddSeparator(int y)
        {
            int pad = S(24);
            _pnlBody.Controls.Add(new Panel { Location = new Point(pad, y), Size = new Size(_formW - pad * 2, 1), BackColor = AppTheme.Separator, Tag = "separator" });
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

        private void ApplyTheme()
        {
            if (_lblTitle == null) return;

            this.BackColor = AppTheme.FormBackground;
            this.ForeColor = AppTheme.PrimaryText;
            _pnlTitle.BackColor = AppTheme.TitleBarBackground;
            _lblTitle.ForeColor = AppTheme.PrimaryText;
            _lblMinimize.ForeColor = AppTheme.CaptionButton;
            _lblClose.ForeColor = AppTheme.CaptionButton;

            foreach (Control control in this.Controls)
                ApplyThemeToControl(control);

            if (_pnlCustomFans != null)
            {
                foreach (Control control in _pnlCustomFans.Controls)
                    ApplyThemeToControl(control);
            }

            _lblCpuTemp.ForeColor = AppTheme.TempColor(_cpuTemp);
            _lblGpuTemp.ForeColor = AppTheme.TempColor(_gpuTemp);
            _trayMenu.Renderer = new ToolStripSystemRenderer();
            Invalidate(true);
        }

        private static void ApplyThemeToControl(Control control)
        {
            switch (control.Tag as string)
            {
                case "section":
                    control.ForeColor = AppTheme.SectionHeader;
                    break;
                case "sub":
                    control.ForeColor = AppTheme.SecondaryText;
                    break;
                case "value":
                    control.ForeColor = AppTheme.PrimaryText;
                    break;
                case "separator":
                    control.BackColor = AppTheme.Separator;
                    break;
            }

            foreach (Control child in control.Controls)
                ApplyThemeToControl(child);
        }

        private void CenterV(Label lbl, int controlY, int controlH)
        {
            lbl.Location = new Point(lbl.Left, controlY + (controlH - lbl.Height) / 2);
        }

        #endregion

        #region Action Handlers

        private void ApplyPowerMode(byte mode, PredatorButton btn)
        {
            _wmi.SetPowerMode(mode);
            HighlightBtn(btn, ref _activePowerBtn);
            SaveState("Power", mode);
            _fanCurve.SetPowerModeProfile(mode);

            _lblPowerStatus.Text = mode switch
            {
                0x00 => "Silent",
                0x04 => "Performance",
                0x05 => "Turbo",
                0x06 => "Eco",
                _ => "Balanced"
            };

            var trayItem = mode switch
            {
                0x00 => _trayPowerQuiet,
                0x04 => _trayPowerPerf,
                0x05 => _trayPowerTurbo,
                0x06 => _trayPowerEco,
                _ => _trayPowerBal
            };
            CheckTrayItem(trayItem, _trayPowerQuiet, _trayPowerBal, _trayPowerPerf, _trayPowerTurbo, _trayPowerEco);
        }

        private static void ConfigureGraphicsIndicator(PredatorButton btn)
        {
            btn.Cursor = Cursors.Default;
            btn.TabStop = false;
            btn.HoverEnabled = false;
        }

        /// <summary>
        /// Highlights Integrated or Discrete from live panel-owner detection.
        /// </summary>
        private void RefreshGraphicsIndicators()
        {
            if (_btnGpuIntegrated == null || _btnGpuDiscrete == null)
                return;

            int mode = _wmi.GetGraphicsMode();
            PredatorButton? active = mode switch
            {
                WmiController.GraphicsModeIntegrated => _btnGpuIntegrated,
                WmiController.GraphicsModeDiscrete => _btnGpuDiscrete,
                _ => null
            };

            if (!ReferenceEquals(_activeGraphicsBtn, active))
            {
                if (active != null)
                    HighlightBtn(active, ref _activeGraphicsBtn);
                else if (_activeGraphicsBtn != null)
                {
                    _activeGraphicsBtn.IsActive = false;
                    _activeGraphicsBtn = null;
                }
            }

            if (_trayGraphicsIntegrated == null || _trayGraphicsDiscrete == null)
                return;

            _trayGraphicsIntegrated.Checked = mode == WmiController.GraphicsModeIntegrated;
            _trayGraphicsDiscrete.Checked = mode == WmiController.GraphicsModeDiscrete;
        }

        private void ApplyFanMode(byte mode, PredatorButton btn)
        {
            _fanCurve.Stop();

            if (mode == 0x03)
            {
                if (_wmi.SupportsCustomFanSpeed)
                    ApplyCustomFanSpeeds();
                else
                    _wmi.SetFanBehavior(0x03);
                UpdateCustomFanPanel(true);
                UpdateAdvancedFanPanel(false);
            }
            else if (mode == FanCurveController.FanModeAdvanced)
            {
                _fanCurve.Start();
                UpdateCustomFanPanel(false);
                UpdateAdvancedFanPanel(true);
            }
            else
            {
                _wmi.SetFanBehavior(mode);
                UpdateCustomFanPanel(false);
                UpdateAdvancedFanPanel(false);
            }

            HighlightBtn(btn, ref _activeFanBtn);
            SaveState("Fan", mode);

            _lblFanStatus.Text = mode switch
            {
                0x02 => "Max",
                0x03 => "Custom",
                FanCurveController.FanModeAdvanced => "Advanced",
                _ => "Auto"
            };

            var trayItem = mode switch
            {
                0x01 => _trayFanAuto,
                0x02 => _trayFanMax,
                0x03 => _trayFanCustom,
                FanCurveController.FanModeAdvanced => _trayFanAdvanced,
                _ => _trayFanAuto
            };
            CheckTrayItem(trayItem, _trayFanAuto, _trayFanMax, _trayFanCustom, _trayFanAdvanced);
        }

        private static string FormatFanSliderHeader(string name, int percent, int liveRpm, FanKind kind)
        {
            int expected = FanRpmMap.EstimateRpm(kind, percent);
            string rpmText = liveRpm > 0
                ? $" · {liveRpm} RPM"
                : $" · ~{expected} RPM";
            return $"{name}: {percent}%{rpmText}";
        }

        private void UpdateFanRpmDisplay()
        {
            _lblCpuFanRpm.Text = _cpuFanRpm > 0 ? $"{_cpuFanRpm} RPM" : "-- RPM";
            _lblGpuFanRpm.Text = _gpuFanRpm > 0 ? $"{_gpuFanRpm} RPM" : "-- RPM";

            if (_pnlCustomFans != null && _pnlCustomFans.Visible && _wmi.SupportsCustomFanSpeed)
            {
                _lblCpuFanHdr.Text = FormatFanSliderHeader("CPU FAN", _cpuFanSlider.Value, _cpuFanRpm, FanKind.Cpu);
                _lblGpuFanHdr.Text = FormatFanSliderHeader("GPU FAN", _gpuFanSlider.Value, _gpuFanRpm, FanKind.Gpu);
            }
        }

        private void ApplyCustomFanSpeeds()
        {
            if (_activeFanBtn != _btnCustomFan || !_wmi.SupportsCustomFanSpeed) return;

            byte cpu = FanRpmMap.QuantizePercent(_cpuFanSlider.Value);
            byte gpu = FanRpmMap.QuantizePercent(_gpuFanSlider.Value);
            _wmi.SetCustomFanSpeeds(cpu, gpu);

            SaveState("FanCpu", cpu);
            SaveState("FanGpu", gpu);
            UpdateFanRpmDisplay();
        }

        private void UpdateAdvancedFanPanel(bool visible)
        {
            if (_pnlAdvancedFans == null) return;
            _pnlAdvancedFans.Visible = visible;
            _btnGraphicalSettings.Enabled = visible;
        }

        private void UpdateCustomFanPanel(bool visible)
        {
            if (_pnlCustomFans == null) return;

            bool canControl = visible && _wmi.SupportsCustomFanSpeed;
            _pnlCustomFans.Visible = visible;
            _cpuFanSlider.Enabled = canControl;
            _gpuFanSlider.Enabled = canControl;
            _lblCpuFanHdr.Enabled = canControl;
            _lblGpuFanHdr.Enabled = canControl;

            if (visible && !canControl)
            {
                _lblCpuFanHdr.Text = "CPU FAN: use PredatorSense profile";
                _lblGpuFanHdr.Text = "GPU FAN: use PredatorSense profile";
            }
            else if (canControl)
            {
                UpdateFanRpmDisplay();
            }
        }

        private void SetFanSliderValues(int cpu, int gpu)
        {
            _isUpdatingFanSliders = true;
            try
            {
                _cpuFanSlider.Value = Math.Clamp(cpu, 0, 100);
                _gpuFanSlider.Value = Math.Clamp(gpu, 0, 100);
                _lblCpuFanHdr.Text = FormatFanSliderHeader("CPU FAN", _cpuFanSlider.Value, _cpuFanRpm, FanKind.Cpu);
                _lblGpuFanHdr.Text = FormatFanSliderHeader("GPU FAN", _gpuFanSlider.Value, _gpuFanRpm, FanKind.Gpu);
            }
            finally
            {
                _isUpdatingFanSliders = false;
            }
        }

        private void ApplyDisplayMode(int hz, PredatorButton btn)
        {
            SetRefreshRate(hz);
            HighlightBtn(btn, ref _activeDisplayBtn);
            CheckTrayItem(hz <= 60 ? _trayDisplay60 : _trayDisplayMax, _trayDisplay60, _trayDisplayMax);
        }

        private void ApplyBatteryLimit(bool limit)
        {
            if (_isUpdatingBattery) return;
            _isUpdatingBattery = true;

            try
            {
                if (_wmi.SetBatteryChargeLimit(limit))
                {
                    if (_switchBatteryLimit.Checked != limit)
                        _switchBatteryLimit.Checked = limit;

                    _lblBatteryStatus.Text = limit ? "Limit to 80% (Health)" : "Full Charge (100%)";
                    CheckTrayItem(limit ? _trayBatteryLimit80 : _trayBatteryLimit100, _trayBatteryLimit80, _trayBatteryLimit100);
                    SaveState("BatteryLimit", limit ? 1 : 0);
                }
                else
                {
                    _switchBatteryLimit.Checked = !limit;
                }
            }
            finally
            {
                _isUpdatingBattery = false;
            }
        }

        private void ApplyRgbModeFromDropdown(int mode)
        {
            _currentRgbMode = mode;
            var snapshot = new LightingSettingsSnapshot
            {
                Colors = _keyboardColorSettings,
                RgbMode = mode,
                Speed = _currentRgbSpeed,
                Logo = _logoSettings
            };
            ApplyLightingSnapshot(snapshot);
            SaveState("RGB_Mode", mode);
            CheckRgbTrayFromMode(mode);
            _btnLightingSettings.Invalidate();
        }

        private void CheckRgbTrayFromMode(int mode)
        {
            var active = mode switch
            {
                0 => _trayRgbStatic,
                1 => _trayRgbBreathe,
                2 => _trayRgbNeon,
                3 => _trayRgbWave,
                4 => _trayRgbShift,
                5 => _trayRgbZoom,
                6 => _trayRgbMeteor,
                _ => _trayRgbTwinkle
            };
            CheckTrayItem(active, _trayRgbStatic, _trayRgbBreathe, _trayRgbNeon, _trayRgbWave,
                          _trayRgbShift, _trayRgbZoom, _trayRgbMeteor, _trayRgbTwinkle);
        }

        #endregion

        #region Game Sync Handlers

        private DashboardSnapshot CaptureCurrentState()
        {
            return new DashboardSnapshot
            {
                PowerMode = GetCurrentPowerByte(),
                FanMode = GetCurrentFanByte(),
                RefreshRate = GetCurrentRefreshRate(),
                BatteryLimit = _switchBatteryLimit.Checked ? 1 : 0,
                RgbMode = _currentRgbMode,
                RgbBrightness = _keyboardColorSettings.Brightness,
                RgbSpeed = _currentRgbSpeed,
                RgbR = _wmi.LastR,
                RgbG = _wmi.LastG,
                RgbB = _wmi.LastB,
            };
        }

        private byte GetCurrentPowerByte()
        {
            if (_activePowerBtn == _btnQuiet) return 0x00;
            if (_activePowerBtn == _btnPerform) return 0x04;
            if (_activePowerBtn == _btnTurbo) return 0x05;
            if (_activePowerBtn == _btnEco) return 0x06;
            return 0x01; 
        }

        private byte GetCurrentFanByte()
        {
            if (_activeFanBtn == _btnMaxFan) return 0x02;
            if (_activeFanBtn == _btnCustomFan) return 0x03;
            if (_activeFanBtn == _btnAdvancedFan) return FanCurveController.FanModeAdvanced;
            return 0x01;
        }

        private PredatorButton PowerByteToBtn(byte mode) => mode switch
        {
            0x00 => _btnQuiet,
            0x04 => _btnPerform,
            0x05 => _btnTurbo,
            0x06 => _btnEco,
            _ => _btnBalanced
        };

        private PredatorButton FanByteToBtn(byte mode) => mode switch
        {
            0x02 => _btnMaxFan,
            0x03 => _btnCustomFan,
            FanCurveController.FanModeAdvanced => _btnAdvancedFan,
            _ => _btnAutoFan
        };

        private async void OnGameDetected(GameProfile profile)
        {
            if (InvokeRequired) { Invoke(() => OnGameDetected(profile)); return; }

            _isGameSyncOverriding = true;
            _lblGameSyncStatus.Text = $"Active \u2014 {profile.DisplayName}";

            _gameSync.SetPreGameSnapshot(CaptureCurrentState());

            ApplyPowerMode(profile.PowerMode, PowerByteToBtn(profile.PowerMode));
            ApplyFanMode(profile.FanMode, FanByteToBtn(profile.FanMode));

            if (profile.RefreshRate >= 0)
            {
                int hz = profile.RefreshRate;
                SetRefreshRate(hz);
                HighlightBtn(hz <= 60 ? _btn60Hz : _btnMaxHz, ref _activeDisplayBtn);
                CheckTrayItem(hz <= 60 ? _trayDisplay60 : _trayDisplayMax, _trayDisplay60, _trayDisplayMax);
            }

            if (profile.BatteryLimit >= 0)
                ApplyBatteryLimit(profile.BatteryLimit == 1);

            await Task.Delay(500);
            if (IsDisposed) return;

            if (profile.RgbMode >= 0)
            {
                int mode = profile.RgbMode;
                byte bright = profile.RgbBrightness >= 0
                    ? (byte)profile.RgbBrightness
                    : _keyboardColorSettings.Brightness;
                byte speed = profile.RgbSpeed >= 0
                    ? (byte)Math.Clamp(Math.Round(profile.RgbSpeed * 9.0 / 100.0), 1, 9)
                    : MapSpeed(_currentRgbSpeed);

                if (mode == 0 && profile.RgbFourZone == 1)
                {
                    var colorSettings = new KeyboardColorSettings
                    {
                        FourZone = true,
                        Brightness = bright,
                        SolidColor = Color.FromArgb(
                            profile.RgbR >= 0 ? profile.RgbR : _wmi.LastR,
                            profile.RgbG >= 0 ? profile.RgbG : _wmi.LastG,
                            profile.RgbB >= 0 ? profile.RgbB : _wmi.LastB)
                    };
                    for (int i = 0; i < 4; i++)
                    {
                        int zr = profile.RgbZoneR[i] >= 0 ? profile.RgbZoneR[i] : colorSettings.SolidColor.R;
                        int zg = profile.RgbZoneG[i] >= 0 ? profile.RgbZoneG[i] : colorSettings.SolidColor.G;
                        int zb = profile.RgbZoneB[i] >= 0 ? profile.RgbZoneB[i] : colorSettings.SolidColor.B;
                        colorSettings.ZoneColors[i] = Color.FromArgb(zr, zg, zb);
                    }
                    _wmi.ApplyKeyboardColorSettings(colorSettings);
                    _keyboardColorSettings = colorSettings;
                }
                else
                {
                    byte r = profile.RgbR >= 0 ? (byte)profile.RgbR : _wmi.LastR;
                    byte g = profile.RgbG >= 0 ? (byte)profile.RgbG : _wmi.LastG;
                    byte b = profile.RgbB >= 0 ? (byte)profile.RgbB : _wmi.LastB;
                    _wmi.SetRgbMode(mode, r, g, b, bright, speed, 0);
                }

                _currentRgbMode = mode;
                if (profile.RgbBrightness >= 0)
                    _keyboardColorSettings.Brightness = bright;
                if (profile.RgbSpeed >= 0)
                    _currentRgbSpeed = profile.RgbSpeed;
                CheckRgbTrayFromMode(mode);
                _btnLightingSettings.Invalidate();
            }

            _isGameSyncOverriding = false;
        }

        private async void OnGameExited(DashboardSnapshot snap)
        {
            if (InvokeRequired) { Invoke(() => OnGameExited(snap)); return; }

            _isGameSyncOverriding = true;
            _lblGameSyncStatus.Text = "Active \u2014 Monitoring";

            ApplyPowerMode(snap.PowerMode, PowerByteToBtn(snap.PowerMode));
            ApplyFanMode(snap.FanMode, FanByteToBtn(snap.FanMode));

            SetRefreshRate(snap.RefreshRate);
            HighlightBtn(snap.RefreshRate <= 60 ? _btn60Hz : _btnMaxHz, ref _activeDisplayBtn);
            CheckTrayItem(snap.RefreshRate <= 60 ? _trayDisplay60 : _trayDisplayMax, _trayDisplay60, _trayDisplayMax);

            ApplyBatteryLimit(snap.BatteryLimit == 1);

            await Task.Delay(500);
            if (IsDisposed) return;

            _currentRgbMode = snap.RgbMode;
            _currentRgbSpeed = snap.RgbSpeed;
            _keyboardColorSettings.Brightness = (byte)snap.RgbBrightness;
            _wmi.SetRgbMode(snap.RgbMode, (byte)snap.RgbR, (byte)snap.RgbG, (byte)snap.RgbB,
                            (byte)snap.RgbBrightness,
                            (byte)Math.Clamp(Math.Round(snap.RgbSpeed * 9.0 / 100.0), 1, 9), 0);
            CheckRgbTrayFromMode(snap.RgbMode);
            _btnLightingSettings.Invalidate();

            _isGameSyncOverriding = false;
        }

        #endregion

        #region State Persistence

        private void SaveState(string name, int value)
        {
            if (_isGameSyncOverriding) return; 
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                key.SetValue(name, value);
            }
            catch { }
        }

        private void LoadMemory()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                int savedPower = (int)key.GetValue("Power", 0x01);
                int savedFan = (int)key.GetValue("Fan", 0x01);
                int savedFanCpu = (int)key.GetValue("FanCpu", 50);
                int savedFanGpu = (int)key.GetValue("FanGpu", 50);
                SetFanSliderValues(savedFanCpu, savedFanGpu);
                int savedRgbMode = (int)key.GetValue("RGB_Mode", 3);
                int savedBrightness = (int)key.GetValue("Brightness", 100);
                int savedSpeed = (int)key.GetValue("RGB_Speed", 50);

                int savedR = (int)key.GetValue("RGB_R", 0);
                int savedG = (int)key.GetValue("RGB_G", 150);
                int savedB = (int)key.GetValue("RGB_B", 255);
                LoadKeyboardColorState(key);
                LoadLogoState(key);

                _keyboardColorSettings.Brightness = (byte)Math.Clamp(savedBrightness, 0, 100);
                _currentRgbSpeed = Math.Clamp(savedSpeed, 1, 100);
                _currentRgbMode = Math.Clamp(savedRgbMode, 0, 7);

                var (powerMode, powerBtn) = savedPower switch
                {
                    0x00 => ((byte)0x00, _btnQuiet),
                    0x04 => ((byte)0x04, _btnPerform),
                    0x05 => ((byte)0x05, _btnTurbo),
                    0x06 => ((byte)0x06, _btnEco),
                    _ => ((byte)0x01, _btnBalanced)
                };
                ApplyPowerMode(powerMode, powerBtn);

                if (_wmi.SupportsGraphicsMode && _btnGpuIntegrated != null)
                    RefreshGraphicsIndicators();

                var (fanMode, fanBtn) = savedFan switch
                {
                    0x02 => ((byte)0x02, _btnMaxFan),
                    0x03 => ((byte)0x03, _btnCustomFan),
                    FanCurveController.FanModeAdvanced => (FanCurveController.FanModeAdvanced, _btnAdvancedFan),
                    _ => ((byte)0x01, _btnAutoFan)
                };
                ApplyFanMode(fanMode, fanBtn);

                int clampedMode = _currentRgbMode;
                if (clampedMode == 0)
                {
                    _wmi.ApplyKeyboardColorSettings(_keyboardColorSettings);
                    CheckRgbTrayFromMode(0);
                }
                else
                {
                    ApplyRgbModeFromDropdown(clampedMode);
                }

                _wmi.SetLogoLighting(_logoSettings);

                if (_wmi.IsBatteryControlSupported())
                {
                    int savedBatteryLimit = (int)key.GetValue("BatteryLimit", 0);
                    bool limitEnabled = savedBatteryLimit == 1;

                    _wmi.SetBatteryChargeLimit(limitEnabled);

                    _isUpdatingBattery = true;
                    _switchBatteryLimit.Checked = limitEnabled;
                    _lblBatteryStatus.Text = limitEnabled ? "Limit to 80% (Health)" : "Full Charge (100%)";
                    CheckTrayItem(limitEnabled ? _trayBatteryLimit80 : _trayBatteryLimit100, _trayBatteryLimit80, _trayBatteryLimit100);
                    _isUpdatingBattery = false;
                }
                else
                {
                    _isUpdatingBattery = true;
                    _switchBatteryLimit.Checked = false;
                    _switchBatteryLimit.Enabled = false;
                    _lblBatteryStatus.Text = "Not Supported";
                    _lblBatteryStatus.ForeColor = AppTheme.SecondaryText;
                    _trayBatteryLimit80.Enabled = false;
                    _trayBatteryLimit100.Enabled = false;
                    _trayBatteryMenu.Enabled = false;
                    _isUpdatingBattery = false;
                }
            }
            catch { }
        }

        private void RegisterStartup()
        {
            try
            {
                // requireAdministrator + HKCU\Run cannot autostart (UAC suppressed at logon).
                StartupRegistrar.RegisterHiddenAtLogon();
            }
            catch { }
        }

        private void SetupPredatorKey()
        {
            if (_globalHotkey == null)
            {
                _globalHotkey = new GlobalHotkeyListener(this, _appSettings);
                _globalHotkey.HotkeyPressed += (_, _) => ToggleApp();
                _globalHotkey.Start();
                UpdateTrayHotkeyHint();
            }

            if (_predatorKey != null) return;
            _predatorKey = new PredatorKeyListener(this, _appSettings);
            _predatorKey.KeyPressed += (_, _) => ToggleApp();
            _predatorKey.Start();
        }

        #endregion

        #region Telemetry & Power Rules

        private void UpdateTelemetry(object? sender, EventArgs e)
        {
            bool currentlyPluggedIn = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
            if (_isPluggedIn != currentlyPluggedIn)
            {
                _isPluggedIn = currentlyPluggedIn;
                ApplyPowerRules(currentlyPluggedIn);
            }

            _cpuTemp = _wmi.CpuTemp;
            _gpuTemp = _wmi.GpuTemp;
            _cpuFanRpm = _wmi.CpuFanRpm;
            _gpuFanRpm = _wmi.GpuFanRpm;

            _lblCpuTemp.Text = _cpuTemp > 0 ? $"{_cpuTemp}°C" : "--°C";
            _lblGpuTemp.Text = _gpuTemp > 0 ? $"{_gpuTemp}°C" : "--°C";
            _lblCpuTemp.ForeColor = AppTheme.TempColor(_cpuTemp);
            _lblGpuTemp.ForeColor = AppTheme.TempColor(_gpuTemp);

            int? cpuMhz = _clockReader.GetCpuFrequencyMhz();
            _lblCpuClock.Text = cpuMhz > 0 ? $"{cpuMhz} MHz" : "-- MHz";

            int graphicsMode = _wmi.SupportsGraphicsMode ? _wmi.GetGraphicsMode() : -1;
            bool discreteGpuReadingAllowed = graphicsMode != WmiController.GraphicsModeIntegrated;
            bool requireGpuPowerCheck = graphicsMode != WmiController.GraphicsModeDiscrete;
            int? gpuMhz = _clockReader.GetDiscreteGpuFrequencyMhz(
                discreteGpuReadingAllowed,
                requireGpuPowerCheck);
            _lblGpuClock.Text = gpuMhz > 0 ? $"{gpuMhz} MHz" : "-- MHz";

            if (_wmi.SupportsGraphicsMode)
                RefreshGraphicsIndicators();

            UpdateFanRpmDisplay();
            UpdateTrayTooltip(cpuMhz, gpuMhz);
        }

        private void ApplyPowerRules(bool pluggedIn)
        {
            if (pluggedIn)
            {
                _btnPerform.Enabled = true;
                _btnTurbo.Enabled = true;
                _btnEco.Enabled = false;
                _trayPowerPerf.Enabled = true;
                _trayPowerTurbo.Enabled = true;
                _trayPowerEco.Enabled = false;

                if (_activePowerBtn == _btnEco)
                    ApplyPowerMode(0x01, _btnBalanced);
            }
            else
            {
                _btnPerform.Enabled = false;
                _btnTurbo.Enabled = false;
                _btnEco.Enabled = true;
                _trayPowerPerf.Enabled = false;
                _trayPowerTurbo.Enabled = false;
                _trayPowerEco.Enabled = true;

                ApplyPowerMode(0x06, _btnEco);
            }
        }

        #endregion

        #region UI Helpers

        private static byte MapSpeed(int sliderPercent) =>
            (byte)Math.Clamp(Math.Round(sliderPercent * 9.0 / 100.0), 1, 9);

        private void HighlightBtn(PredatorButton btn, ref PredatorButton? tracker)
        {
            if (tracker != null) tracker.IsActive = false;
            btn.IsActive = true;
            tracker = btn;
        }

        private static void CheckTrayItem(ToolStripMenuItem active, params ToolStripMenuItem[] group)
        {
            foreach (var item in group) item.Checked = false;
            active.Checked = true;
        }

        private void ShowApp()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            ForceForeground();
            if (_lightingSideForm != null && !_lightingSideForm.IsDisposed && _lightingPanelOpen)
            {
                _lightingSideForm.Show();
                _lightingSideForm.SyncToOwner();
            }
        }

        /// <summary>
        /// RegisterHotKey (Ctrl+Alt+P) is allowed to steal focus; the Predator LL-hook is not.
        /// AttachThreadInput + brief Alt pulse unlocks SetForegroundWindow in that case.
        /// </summary>
        private void ForceForeground()
        {
            if (!IsHandleCreated || IsDisposed) return;

            IntPtr hWnd = Handle;
            ShowWindow(hWnd, SwRestore);

            IntPtr foreground = GetForegroundWindow();
            if (foreground == hWnd)
            {
                Activate();
                return;
            }

            uint thisThread = GetCurrentThreadId();
            uint foreThread = foreground != IntPtr.Zero
                ? GetWindowThreadProcessId(foreground, IntPtr.Zero)
                : 0;
            bool attached = false;
            if (foreThread != 0 && foreThread != thisThread)
                attached = AttachThreadInput(thisThread, foreThread, true);

            try
            {
                // Windows quirk: a synthetic Alt keypress allows SetForegroundWindow.
                keybd_event(VkMenu, 0, KeyeventfExtendedkey, UIntPtr.Zero);
                keybd_event(VkMenu, 0, KeyeventfExtendedkey | KeyeventfKeyup, UIntPtr.Zero);

                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                Activate();
                Focus();
            }
            finally
            {
                if (attached)
                    AttachThreadInput(thisThread, foreThread, false);
            }
        }

        private bool IsAppForeground()
        {
            if (!IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized)
                return false;
            IntPtr fg = GetForegroundWindow();
            return fg == Handle || (_lightingSideForm != null && !_lightingSideForm.IsDisposed && fg == _lightingSideForm.Handle);
        }

        private void ToggleApp()
        {
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                ShowApp();
                return;
            }

            // Visible but buried behind other windows → bring forward (don't hide).
            if (!IsAppForeground())
            {
                ShowApp();
                return;
            }

            HideApp();
        }

        private void HideApp()
        {
            if (_lightingSideForm != null && !_lightingSideForm.IsDisposed)
                _lightingSideForm.Hide();
            this.Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_isClosing)
            {
                e.Cancel = true;
                HideApp();
            }
            else
            {
                if (_lightingSideForm != null && !_lightingSideForm.IsDisposed)
                    _lightingSideForm.Close();
                _liveLighting?.Dispose();
                _shellAnimator?.Dispose();
                _trayIcon.Visible = false;
                _timer.Stop();
                _globalHotkey?.Dispose();
                _predatorKey?.Dispose();
                _gameSync.Dispose();
                _fanCurve.Dispose();
                AppTheme.Changed -= OnThemeChanged;
                _appMutex.Dispose();
                base.OnFormClosing(e);
            }
        }

        #endregion
    }
}