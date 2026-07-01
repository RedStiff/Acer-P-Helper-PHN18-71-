using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class GameSyncForm : ResizableBorderlessForm
    {
        #region Theme

        private static readonly Font FontTitle = new("Segoe UI", 11f, FontStyle.Bold);
        private static readonly Font FontSection = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontBody = new("Segoe UI", 9.5f, FontStyle.Regular);

        private Panel _pnlTitle = null!;
        private Panel _pnlBody = null!;
        private Panel _pnlEmpty = null!;
        private Label _lblTitleBar = null!, _lblClose = null!;
        private Label _lblSectionApps = null!;
        private ResponsiveLayoutHelper? _responsiveLayout;

        private const int DesignClientWidth = 780;
        private const int DesignClientHeight = 700;

        #endregion

        #region Fields

        private readonly GameSyncController _controller;
        private readonly int _maxHz;

        private ListBox _lstProfiles = null!;
        private Panel _pnlEditor = null!;

        private Label _lblExeName = null!;
        private PredatorDropDown _cboPower = null!, _cboFan = null!, _cboRefresh = null!;
        private PredatorDropDown _cboBattery = null!, _cboRgbMode = null!;
        private PredatorSlider _trkBrightness = null!, _trkSpeed = null!;
        private Label _lblBrightVal = null!, _lblSpeedVal = null!;
        private PredatorButton _btnColorPick = null!;
        private KeyboardColorSettings _keyboardColorSettings = new();
        private PredatorButton _btnSave = null!, _btnRemove = null!, _btnAdd = null!, _btnPick = null!;

        private GameProfile? _editingProfile;

        private static readonly string[] PowerModeNames = { "Quiet", "Balanced", "Performance", "Turbo", "Eco" };
        private static readonly byte[] PowerModeValues = { 0x00, 0x01, 0x04, 0x05, 0x06 };
        private static readonly string[] FanModeNames = { "Auto", "Max", "Custom", "Advanced" };
        private static readonly byte[] FanModeValues = { 0x01, 0x02, 0x03, FanCurveController.FanModeAdvanced };
        private static readonly string[] RgbModeNames = { "Don't Change", "Static", "Breathing", "Neon", "Wave", "Shifting", "Zoom", "Meteor", "Twinkling" };

        #endregion

        #region Constructor

        public GameSyncForm(GameSyncController controller, int maxHz)
        {
            _controller = controller;
            _maxHz = maxHz;
            _keyboardColorSettings.SolidColor = AppTheme.Accent;

            var ui = UiSettings.Load();

            this.Text = "Game Sync Configuration";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = AppTheme.FormBackground;
            this.ForeColor = AppTheme.PrimaryText;
            this.ClientSize = new Size(DesignClientWidth, DesignClientHeight);
            this.DoubleBuffered = true;
            this.ShowInTaskbar = false;
            BorderlessResizeHelper.ApplyTo(this, UiSettings.MinGameSyncWidth, UiSettings.MinGameSyncHeight);
            try { this.Icon = new Icon("appicon.ico"); } catch { }

            BuildUI();

            _pnlBody.PerformLayout();
            _responsiveLayout = new ResponsiveLayoutHelper(this, new Size(DesignClientWidth, DesignClientHeight));
            _responsiveLayout.Snapshot(_pnlBody);

            ClientSize = ui.GetGameSyncClientSize();
            LayoutTitleBar(24);

            ApplyTheme();
            AppTheme.Changed += OnThemeChanged;
            PopulateList();
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        #endregion

        #region Window Dragging

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); }
        }

        #endregion

        #region UI Building

        private void BuildUI()
        {
            int pad = 24;
            int titleH = 42;
            int bodyH = DesignClientHeight - titleH;
            int contentY = pad;
            int listW = 220;
            int listAreaH = bodyH - pad * 2;
            int btnAddH = 34;
            int listH = listAreaH - 24 - 26 - (btnAddH * 3) - 28;
            int editorX = pad + listW + pad;
            int editorW = DesignClientWidth - editorX - pad;
            int editorH = listAreaH;

            _pnlBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            Controls.Add(_pnlBody);

            _pnlTitle = new Panel { Height = titleH, Dock = DockStyle.Top, BackColor = AppTheme.TitleBarBackground };
            _pnlTitle.MouseDown += TitleBar_MouseDown;
            Controls.Add(_pnlTitle);

            _lblTitleBar = new Label
            {
                Text = "Game Sync Configuration",
                Font = FontTitle,
                ForeColor = AppTheme.PrimaryText,
                AutoSize = true,
                Location = new Point(pad, 11),
                BackColor = Color.Transparent
            };
            _lblTitleBar.MouseDown += TitleBar_MouseDown;
            _pnlTitle.Controls.Add(_lblTitleBar);

            var captionFont = new Font("Segoe UI", 10f, FontStyle.Regular);
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

            _lblSectionApps = MakeLabelInBody("CONFIGURED APPS", pad, contentY, FontSection, AppTheme.SectionHeader, "section");

            _lstProfiles = new ListBox
            {
                Location = new Point(pad, contentY + 24),
                Size = new Size(listW, listH),
                BackColor = AppTheme.PanelBackground,
                ForeColor = AppTheme.PrimaryText,
                Font = FontBody,
                BorderStyle = BorderStyle.FixedSingle,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 32
            };
            _lstProfiles.DrawItem += LstProfiles_DrawItem;
            _lstProfiles.SelectedIndexChanged += LstProfiles_SelectedIndexChanged;
            _pnlBody.Controls.Add(_lstProfiles);

            _btnAdd = MakeButtonInBody("\uff0b  Browse for .exe", pad, _lstProfiles.Bottom + 8, listW, btnAddH);
            _btnAdd.Click += BtnAdd_Click;

            _btnPick = MakeButtonInBody("⚡  Pick Running Process", pad, _lstProfiles.Bottom + 8 + btnAddH + 6, listW, btnAddH);
            _btnPick.Click += BtnPickRunning_Click;

            _pnlEmpty = new Panel
            {
                Location = new Point(editorX, contentY),
                Size = new Size(editorW, editorH),
                BackColor = Color.Transparent
            };
            _pnlEmpty.Controls.Add(new Label
            {
                Text = "Select an application from the list\nor add a new one to get started.",
                ForeColor = AppTheme.SecondaryText,
                Font = FontBody,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            });
            _pnlBody.Controls.Add(_pnlEmpty);

            _pnlEditor = new Panel
            {
                Location = new Point(editorX, contentY),
                Size = new Size(editorW, editorH),
                BackColor = AppTheme.FormBackground,
                Visible = false
            };
            _pnlBody.Controls.Add(_pnlEditor);

            int ey = 0;
            int sectionGap = 50;

            _lblExeName = new Label
            {
                Text = "",
                Font = FontTitle,
                ForeColor = AppTheme.Accent,
                AutoSize = true,
                Location = new Point(0, ey),
                MaximumSize = new Size(editorW, 0),
                BackColor = Color.Transparent
            };
            _pnlEditor.Controls.Add(_lblExeName);
            ey += 34;

            _pnlEditor.Controls.Add(new Panel
            {
                Location = new Point(0, ey),
                Size = new Size(editorW, 1),
                BackColor = AppTheme.Separator,
                Tag = "separator"
            });
            ey += 20;

            MakeLabelIn(_pnlEditor, "POWER MODE", 0, ey, FontSection, AppTheme.SectionHeader, "section");
            ey += 24;
            _cboPower = MakeComboIn(_pnlEditor, PowerModeNames, 0, ey, editorW);
            ey += sectionGap;

            MakeLabelIn(_pnlEditor, "FAN MODE", 0, ey, FontSection, AppTheme.SectionHeader, "section");
            ey += 24;
            _cboFan = MakeComboIn(_pnlEditor, FanModeNames, 0, ey, editorW);
            ey += sectionGap;

            MakeLabelIn(_pnlEditor, "REFRESH RATE", 0, ey, FontSection, AppTheme.SectionHeader, "section");
            ey += 24;
            _cboRefresh = MakeComboIn(_pnlEditor, new[] { "Don't Change", "60 Hz", $"{_maxHz} Hz (Max)" }, 0, ey, editorW);
            ey += sectionGap;

            MakeLabelIn(_pnlEditor, "BATTERY CHARGE LIMIT", 0, ey, FontSection, AppTheme.SectionHeader, "section");
            ey += 24;
            _cboBattery = MakeComboIn(_pnlEditor, new[] { "Don't Change", "Full Charge (100%)", "Limit to 80% (Health)" }, 0, ey, editorW);
            ey += sectionGap;

            MakeLabelIn(_pnlEditor, "RGB KEYBOARD", 0, ey, FontSection, AppTheme.SectionHeader, "section");
            ey += 24;
            _cboRgbMode = MakeComboIn(_pnlEditor, RgbModeNames, 0, ey, editorW);
            ey += sectionGap;

            int halfW = (editorW - 20) / 2;

            MakeLabelIn(_pnlEditor, "BRIGHTNESS", 0, ey, FontSection, AppTheme.SectionHeader, "section");
            _lblBrightVal = MakeLabelIn(_pnlEditor, "100%", halfW - 40, ey, FontBody, AppTheme.SecondaryText, "sub");
            MakeLabelIn(_pnlEditor, "EFFECT SPEED", halfW + 20, ey, FontSection, AppTheme.SectionHeader, "section");
            _lblSpeedVal = MakeLabelIn(_pnlEditor, "50%", editorW - 40, ey, FontBody, AppTheme.SecondaryText, "sub");
            ey += 24;

            _trkBrightness = MakeSliderIn(_pnlEditor, 0, ey, halfW, 0, 100, 100);
            _trkBrightness.ValueChanged += (_, _) => _lblBrightVal.Text = $"{_trkBrightness.Value}%";
            _trkSpeed = MakeSliderIn(_pnlEditor, halfW + 20, ey, halfW, 1, 100, 50);
            _trkSpeed.ValueChanged += (_, _) => _lblSpeedVal.Text = $"{_trkSpeed.Value}%";
            ey += sectionGap;

            MakeLabelIn(_pnlEditor, "COLOR CUSTOMIZATION", 0, ey, FontSection, AppTheme.SectionHeader, "section");
            ey += 24;

            _btnColorPick = new PredatorButton { Text = "    Customize Keyboard Color", Location = new Point(0, ey), Size = new Size(editorW, 36) };
            _btnColorPick.Paint += (_, pe) =>
            {
                var g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int cy = _btnColorPick.Height / 2;
                int cx = _btnColorPick.Width / 2 - 70;
                var previewColor = _keyboardColorSettings.FourZone
                    ? _keyboardColorSettings.ZoneColors[0]
                    : _keyboardColorSettings.SolidColor;
                using var brush = new SolidBrush(previewColor);
                g.FillEllipse(brush, cx - 6, cy - 6, 12, 12);
                using var glowBrush = new SolidBrush(Color.FromArgb(100, previewColor));
                g.FillEllipse(glowBrush, cx - 8, cy - 8, 16, 16);
            };
            _btnColorPick.Click += (_, _) => OpenKeyboardColorEditor();
            _pnlEditor.Controls.Add(_btnColorPick);

            ey += sectionGap;

            int btnW = (editorW - 16) / 2;
            _btnSave = new PredatorButton { Text = "💾  Save Profile", Location = new Point(0, ey), Size = new Size(btnW, 38) };
            _btnSave.Click += BtnSave_Click;
            _pnlEditor.Controls.Add(_btnSave);

            _btnRemove = new PredatorButton { Text = "🗑  Remove", Location = new Point(btnW + 12, ey), Size = new Size(btnW, 38) };
            _btnRemove.Click += BtnRemove_Click;
            _pnlEditor.Controls.Add(_btnRemove);

            Paint += (_, e) =>
            {
                using var pen = new Pen(AppTheme.Separator);
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                e.Graphics.DrawLine(pen, 0, _pnlTitle.Bottom - 1, ClientSize.Width, _pnlTitle.Bottom - 1);
            };
        }

        private void LayoutTitleBar(int pad)
        {
            if (_lblClose == null || _pnlTitle == null) return;
            _lblClose.Location = new Point(_pnlTitle.Width - pad - _lblClose.Width - 4, 9);
        }

        #endregion

        #region UI Helpers

        private Label MakeLabelInBody(string text, int x, int y, Font font, Color color, string? tag = null)
        {
            var lbl = new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = font, ForeColor = color, BackColor = Color.Transparent, Tag = tag };
            _pnlBody.Controls.Add(lbl);
            return lbl;
        }

        private PredatorButton MakeButtonInBody(string text, int x, int y, int w, int h)
        {
            var btn = new PredatorButton { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            _pnlBody.Controls.Add(btn);
            return btn;
        }

        private Label MakeLabelIn(Panel parent, string text, int x, int y, Font font, Color color, string? tag = null)
        {
            var lbl = new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = font, ForeColor = color, BackColor = Color.Transparent, Tag = tag };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private PredatorDropDown MakeComboIn(Panel parent, string[] items, int x, int y, int w)
        {
            var dd = new PredatorDropDown
            {
                Location = new Point(x, y),
                Size = new Size(w, 34)
            };
            foreach (var item in items) dd.Items.Add(item);
            if (dd.Items.Count > 0) dd.SelectedIndex = 0;
            parent.Controls.Add(dd);
            return dd;
        }

        private PredatorSlider MakeSliderIn(Panel parent, int x, int y, int w, int min, int max, int val)
        {
            var slider = new PredatorSlider
            {
                Location = new Point(x, y),
                Size = new Size(w, 28),
                Minimum = min,
                Maximum = max,
                Value = val
            };
            parent.Controls.Add(slider);
            return slider;
        }

        private void ApplyTheme()
        {
            this.BackColor = AppTheme.FormBackground;
            this.ForeColor = AppTheme.PrimaryText;
            _pnlTitle.BackColor = AppTheme.TitleBarBackground;
            _lblTitleBar.ForeColor = AppTheme.PrimaryText;
            _lblClose.ForeColor = AppTheme.CaptionButton;
            _lstProfiles.BackColor = AppTheme.PanelBackground;
            _lstProfiles.ForeColor = AppTheme.PrimaryText;
            _pnlEditor.BackColor = AppTheme.FormBackground;
            _lblExeName.ForeColor = AppTheme.Accent;
            ApplyThemeToControl(this);
            _lstProfiles.Invalidate();
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
                case "separator":
                    control.BackColor = AppTheme.Separator;
                    break;
            }

            foreach (Control child in control.Controls)
                ApplyThemeToControl(child);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UiSettings.SaveGameSyncWindowSize(ClientSize);
            AppTheme.Changed -= OnThemeChanged;
            base.OnFormClosed(e);
        }



        #endregion

        #region List Drawing

        private void LstProfiles_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();

            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var bgBrush = new SolidBrush(selected ? AppTheme.ListSelectionBackground : AppTheme.PanelBackground);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

            string text = _lstProfiles.Items[e.Index]?.ToString() ?? "";
            using var textBrush = new SolidBrush(selected ? AppTheme.Accent : AppTheme.PrimaryText);
            var textRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString(text, FontBody, textBrush, textRect, sf);

            if (selected)
            {
                using var accentPen = new Pen(AppTheme.Accent, 2f);
                e.Graphics.DrawLine(accentPen, e.Bounds.X, e.Bounds.Y, e.Bounds.X, e.Bounds.Bottom);
            }
        }

        #endregion

        #region Populate & Select

        private void PopulateList()
        {
            _lstProfiles.Items.Clear();
            foreach (var p in _controller.Profiles)
            {
                _lstProfiles.Items.Add(string.IsNullOrEmpty(p.DisplayName) ? p.ExecutableName : p.DisplayName);
            }
        }

        private void LstProfiles_SelectedIndexChanged(object? sender, EventArgs e)
        {
            int idx = _lstProfiles.SelectedIndex;
            if (idx < 0 || idx >= _controller.Profiles.Count)
            {
                _pnlEditor.Visible = false;
                _pnlEmpty.Visible = true;
                _editingProfile = null;
                return;
            }

            _pnlEmpty.Visible = false;
            _editingProfile = _controller.Profiles[idx];
            LoadProfileToEditor(_editingProfile);
            _pnlEditor.Visible = true;
        }

        private void LoadProfileToEditor(GameProfile p)
        {
            _lblExeName.Text = p.ExecutableName;
            _lblExeName.MaximumSize = new Size(Math.Max(120, _pnlEditor.Width), 0);

            int powerIdx = Array.IndexOf(PowerModeValues, p.PowerMode);
            _cboPower.SelectedIndex = powerIdx >= 0 ? powerIdx : 1;

            int fanIdx = Array.IndexOf(FanModeValues, p.FanMode);
            _cboFan.SelectedIndex = fanIdx >= 0 ? fanIdx : 0;

            if (p.RefreshRate == -1) _cboRefresh.SelectedIndex = 0;
            else if (p.RefreshRate <= 60) _cboRefresh.SelectedIndex = 1;
            else _cboRefresh.SelectedIndex = 2;

            if (p.BatteryLimit == -1) _cboBattery.SelectedIndex = 0;
            else if (p.BatteryLimit == 0) _cboBattery.SelectedIndex = 1;
            else _cboBattery.SelectedIndex = 2;

            if (p.RgbMode == -1) _cboRgbMode.SelectedIndex = 0;
            else _cboRgbMode.SelectedIndex = Math.Clamp(p.RgbMode + 1, 0, RgbModeNames.Length - 1);

            _trkBrightness.Value = p.RgbBrightness >= 0 ? Math.Clamp(p.RgbBrightness, 0, 100) : 100;
            _trkSpeed.Value = p.RgbSpeed >= 0 ? Math.Clamp(p.RgbSpeed, 1, 100) : 50;

            int r = p.RgbR >= 0 ? Math.Clamp(p.RgbR, 0, 255) : 0;
            int g = p.RgbG >= 0 ? Math.Clamp(p.RgbG, 0, 255) : 200;
            int b = p.RgbB >= 0 ? Math.Clamp(p.RgbB, 0, 255) : 160;
            _keyboardColorSettings.SolidColor = Color.FromArgb(r, g, b);
            _keyboardColorSettings.FourZone = p.RgbFourZone == 1;
            for (int i = 0; i < 4; i++)
            {
                int zr = p.RgbZoneR[i] >= 0 ? p.RgbZoneR[i] : r;
                int zg = p.RgbZoneG[i] >= 0 ? p.RgbZoneG[i] : g;
                int zb = p.RgbZoneB[i] >= 0 ? p.RgbZoneB[i] : b;
                _keyboardColorSettings.ZoneColors[i] = Color.FromArgb(zr, zg, zb);
            }
            _btnColorPick.Invalidate();
        }

        private void OpenKeyboardColorEditor()
        {
            var settings = new KeyboardColorSettings
            {
                FourZone = _keyboardColorSettings.FourZone,
                SolidColor = _keyboardColorSettings.SolidColor,
                Brightness = (byte)Math.Clamp(_trkBrightness.Value, 1, 100)
            };
            for (int i = 0; i < 4; i++)
                settings.ZoneColors[i] = _keyboardColorSettings.ZoneColors[i];

            if (!KeyboardColorForm.TryShow(this, settings, out var result)) return;

            _keyboardColorSettings = result;
            _trkBrightness.Value = result.Brightness;
            _lblBrightVal.Text = $"{result.Brightness}%";
            _btnColorPick.Invalidate();
        }

        private GameProfile EditorToProfile()
        {
            var p = new GameProfile
            {
                ExecutableName = _editingProfile?.ExecutableName ?? "",
                DisplayName = _editingProfile?.DisplayName ?? "",
                PowerMode = PowerModeValues[_cboPower.SelectedIndex],
                FanMode = FanModeValues[_cboFan.SelectedIndex],
            };

            p.RefreshRate = _cboRefresh.SelectedIndex switch
            {
                1 => 60,
                2 => _maxHz,
                _ => -1
            };

            p.BatteryLimit = _cboBattery.SelectedIndex switch
            {
                1 => 0,
                2 => 1,
                _ => -1
            };

            p.RgbMode = _cboRgbMode.SelectedIndex == 0 ? -1 : _cboRgbMode.SelectedIndex - 1;
            p.RgbBrightness = _trkBrightness.Value;
            p.RgbSpeed = _trkSpeed.Value;
            p.RgbR = _keyboardColorSettings.SolidColor.R;
            p.RgbG = _keyboardColorSettings.SolidColor.G;
            p.RgbB = _keyboardColorSettings.SolidColor.B;
            p.RgbFourZone = _keyboardColorSettings.FourZone ? 1 : 0;
            for (int i = 0; i < 4; i++)
            {
                p.RgbZoneR[i] = _keyboardColorSettings.ZoneColors[i].R;
                p.RgbZoneG[i] = _keyboardColorSettings.ZoneColors[i].G;
                p.RgbZoneB[i] = _keyboardColorSettings.ZoneColors[i].B;
            }

            return p;
        }

        #endregion

        #region Actions

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            try
            {
                using var ofd = new OpenFileDialog
                {
                    Title = "Select an Application",
                    Filter = "Executables (*.exe)|*.exe",
                    CheckFileExists = true
                };

                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    string exeName = Path.GetFileName(ofd.FileName);
                    string displayName = Path.GetFileNameWithoutExtension(ofd.FileName);
                    TryAddProfile(exeName, displayName);
                }
            }
            catch
            {
                MessageBox.Show(this,
                    "Could not browse to that location.\n\nYou can find the process name in Task Manager while the game is running, and use 'Pick Running Process' to add it.",
                    "Browse Failed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnPickRunning_Click(object? sender, EventArgs e)
        {
            var systemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "svchost", "csrss", "smss", "wininit", "winlogon", "services", "lsass",
                "fontdrvhost", "dwm", "conhost", "dllhost", "taskhostw", "sihost",
                "ctfmon", "spoolsv", "SearchIndexer", "WmiPrvSE", "RuntimeBroker",
                "ShellExperienceHost", "StartMenuExperienceHost", "TextInputHost",
                "SecurityHealthSystray", "SecurityHealthService", "MsMpEng",
                "NisSrv", "SgrmBroker", "audiodg", "PredatorControlApp", "System",
                "Registry", "Idle", "Memory Compression"
            };

            List<string> allProcs;
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    try
                    {
                        string name = p.ProcessName;
                        if (!systemNames.Contains(name))
                            names.Add(name + ".exe");
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                allProcs = names.OrderBy(n => n).ToList();
                GC.Collect(0, GCCollectionMode.Optimized);
            }
            catch
            {
                allProcs = new List<string>();
            }

            if (allProcs.Count == 0)
            {
                MessageBox.Show(this, "No running processes found.", "Pick Process",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var dlgBg = AppTheme.FormBackground;
            var dlgTitle = AppTheme.TitleBarBackground;
            var dlgBorder = AppTheme.DialogBorder;
            var dlgText = AppTheme.PrimaryText;
            var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            var bodyFont = new Font("Segoe UI", 9.25f);

            using var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition   = FormStartPosition.CenterParent,
                ClientSize      = new Size(360, 480),
                BackColor       = dlgBg,
                ForeColor       = dlgText
            };

            bool dragging = false; Point dragStart = Point.Empty;

            var pnlTitle = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = dlgTitle };
            pnlTitle.MouseDown += (s, ev) => { if (ev.Button == MouseButtons.Left) { dragging = true; dragStart = ev.Location; } };
            pnlTitle.MouseMove += (s, ev) => { if (dragging) dlg.Location = new Point(dlg.Left + ev.X - dragStart.X, dlg.Top + ev.Y - dragStart.Y); };
            pnlTitle.MouseUp   += (s, ev) => dragging = false;
            dlg.Controls.Add(pnlTitle);

            var lblTitle = new Label
            {
                Text = "Pick a Running Process",
                Font = titleFont, ForeColor = dlgText, AutoSize = true,
                Location = new Point(14, 11), BackColor = Color.Transparent
            };
            lblTitle.MouseDown += (s, ev) => { if (ev.Button == MouseButtons.Left) { dragging = true; dragStart = ev.Location; } };
            pnlTitle.Controls.Add(lblTitle);

            var lblCloseDlg = new Label
            {
                Text = "\u2715", ForeColor = AppTheme.CaptionButton,
                Font = new Font("Segoe UI", 10f), AutoSize = true,
                Cursor = Cursors.Hand, BackColor = Color.Transparent,
                Location = new Point(dlg.ClientSize.Width - 24, 10)
            };
            lblCloseDlg.Click += (s, ev) => dlg.Close();
            lblCloseDlg.MouseEnter += (s, ev) => lblCloseDlg.ForeColor = AppTheme.CaptionCloseHover;
            lblCloseDlg.MouseLeave += (s, ev) => lblCloseDlg.ForeColor = AppTheme.CaptionButton;
            pnlTitle.Controls.Add(lblCloseDlg);

            dlg.Paint += (s, pe) =>
            {
                using var pen = new Pen(dlgBorder, 1f);
                pe.Graphics.DrawRectangle(pen, 0, 0, dlg.Width - 1, dlg.Height - 1);
            };

            var pnlSearch = new Panel
            {
                Location = new Point(14, 50), Size = new Size(332, 30),
                BackColor = AppTheme.InputBorder
            };
            dlg.Controls.Add(pnlSearch);

            var txtSearch = new TextBox
            {
                Location = new Point(1, 1), Size = new Size(330, 28),
                BackColor = AppTheme.InputBackground, ForeColor = dlgText,
                Font = bodyFont, BorderStyle = BorderStyle.None,
                PlaceholderText = "Search..."
            };
            pnlSearch.Controls.Add(txtSearch);

            var lstProcs = new ListBox
            {
                Location = new Point(14, 90), Size = new Size(332, 340),
                BackColor = AppTheme.PanelBackground, ForeColor = dlgText,
                Font = bodyFont, BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.One
            };
            dlg.Controls.Add(lstProcs);

            void RefreshList(string filter)
            {
                lstProcs.BeginUpdate();
                lstProcs.Items.Clear();
                var filtered = string.IsNullOrWhiteSpace(filter)
                    ? allProcs
                    : allProcs.Where(p => p.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                foreach (var p in filtered) lstProcs.Items.Add(p);
                lstProcs.EndUpdate();
            }

            RefreshList("");
            txtSearch.TextChanged += (s, ev) => RefreshList(txtSearch.Text);

            var btnAdd2 = new PredatorButton
            {
                Text = "Add Selected", Location = new Point(14, 438), Size = new Size(332, 34)
            };
            dlg.Controls.Add(btnAdd2);

            DialogResult result = DialogResult.Cancel;
            string? picked = null;

            void DoAdd()
            {
                if (lstProcs.SelectedItem is string sel)
                {
                    picked = sel;
                    result = DialogResult.OK;
                    dlg.Close();
                }
            }

            btnAdd2.Click     += (s, ev) => DoAdd();
            lstProcs.DoubleClick += (s, ev) => DoAdd();
            txtSearch.KeyDown += (s, ke) =>
            {
                if (ke.KeyCode == Keys.Down && lstProcs.Items.Count > 0)
                {
                    lstProcs.SelectedIndex = 0;
                    lstProcs.Focus();
                    ke.Handled = true;
                }
                else if (ke.KeyCode == Keys.Escape) dlg.Close();
            };
            lstProcs.KeyDown += (s, ke) =>
            {
                if (ke.KeyCode == Keys.Enter) { ke.SuppressKeyPress = true; DoAdd(); }
                else if (ke.KeyCode == Keys.Escape) dlg.Close();
            };

            dlg.ShowDialog(this);

            if (result == DialogResult.OK && picked != null)
            {
                string displayName = Path.GetFileNameWithoutExtension(picked);
                TryAddProfile(picked, displayName);
            }
        }

        private void TryAddProfile(string exeName, string displayName)
        {
            foreach (var existing in _controller.Profiles)
            {
                if (existing.ExecutableName.Equals(exeName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, $"\"{exeName}\" is already configured.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            var profile = new GameProfile
            {
                ExecutableName = exeName,
                DisplayName = displayName,
                PowerMode = 0x01,
                FanMode = 0x01,
                RefreshRate = -1,
                BatteryLimit = -1,
                RgbMode = -1,
                RgbBrightness = -1,
                RgbSpeed = -1,
                RgbR = -1,
                RgbG = -1,
                RgbB = -1,
            };

            _controller.AddProfile(profile);
            PopulateList();
            _lstProfiles.SelectedIndex = _lstProfiles.Items.Count - 1;
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            if (_editingProfile == null) return;

            var updated = EditorToProfile();
            _controller.UpdateProfile(updated);

            int idx = _lstProfiles.SelectedIndex;
            PopulateList();
            if (idx >= 0 && idx < _lstProfiles.Items.Count)
                _lstProfiles.SelectedIndex = idx;
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            if (_editingProfile == null) return;

            var result = MessageBox.Show(this,
                $"Remove \"{_editingProfile.ExecutableName}\" from Game Sync?",
                "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                _controller.RemoveProfile(_editingProfile.ExecutableName);
                _editingProfile = null;
                _pnlEditor.Visible = false;
                _pnlEmpty.Visible = true;
                PopulateList();
            }
        }

        #endregion
    }
}
