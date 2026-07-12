using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Separate lighting window that slides out from under the main form to the left
    /// and stays position/visibility-bound to it.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class LightingSideForm : BorderlessForm
    {
        private const int SlideDurationMs = 220;
        private const uint SwpNomove = 0x0002;
        private const uint SwpNosize = 0x0001;
        private const uint SwpNoactivate = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private readonly LightingPanelControl _panel;
        private Form? _ownerForm;
        private System.Windows.Forms.Timer? _slideTimer;
        private DateTime _slideStart;
        private int _slideFromLeft;
        private int _slideToLeft;
        private bool _slideClosing;
        private bool _eventsAttached;

        public event Action<LightingSettingsSnapshot>? SettingsLiveChanged;
        public event Action<LightingSettingsSnapshot>? SettingsFlushRequested;
        public event EventHandler? ClosedByUser;

        public LightingSideForm(
            KeyboardColorSettings colors,
            int rgbMode,
            int speed,
            int width,
            int height,
            LogoLightingSettings? logo = null)
        {
            Text = "Lighting";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            ClientSize = new Size(width, height);
            MinimumSize = Size;
            MaximumSize = Size;

            _panel = new LightingPanelControl(colors, rgbMode, speed, logo)
            {
                Dock = DockStyle.Fill
            };
            _panel.SettingsLiveChanged += snapshot => SettingsLiveChanged?.Invoke(snapshot);
            _panel.SettingsFlushRequested += snapshot => SettingsFlushRequested?.Invoke(snapshot);
            _panel.CloseRequested += (_, _) => BeginSlideClose();
            Controls.Add(_panel);

            AppTheme.Changed += OnThemeChanged;
        }

        public LightingSettingsSnapshot CreateSnapshot() => _panel.CreateSnapshot();

        protected override bool ShowWithoutActivation => true;

        public void LoadFrom(
            KeyboardColorSettings colors,
            int rgbMode,
            int speed,
            LogoLightingSettings? logo = null) =>
            _panel.LoadFrom(colors, rgbMode, speed, logo);

        public void ShowBesideOwner(Form owner)
        {
            _ownerForm = owner;
            _slideClosing = false;

            ClientSize = new Size(ClientSize.Width, owner.ClientSize.Height);
            MinimumSize = ClientSize;
            MaximumSize = ClientSize;

            AlignHiddenUnderOwner();
            Show();
            SendBehindOwner();
            AttachOwnerEvents();
            StartSlide(toOpen: true);
        }

        public void BeginSlideClose()
        {
            if (_slideClosing) return;
            _slideClosing = true;
            StartSlide(toOpen: false);
        }

        public void SyncToOwner()
        {
            if (_ownerForm == null || _ownerForm.IsDisposed || IsDisposed) return;
            if (_slideTimer?.Enabled == true) return;

            Top = _ownerForm.Top;
            Left = _ownerForm.Left - Width;
            if (Height != _ownerForm.Height)
            {
                ClientSize = new Size(ClientSize.Width, _ownerForm.ClientSize.Height);
                MinimumSize = ClientSize;
                MaximumSize = ClientSize;
                Left = _ownerForm.Left - Width;
            }
        }

        private void AttachOwnerEvents()
        {
            if (_ownerForm == null || _eventsAttached) return;
            _ownerForm.LocationChanged += OnOwnerMoved;
            _ownerForm.SizeChanged += OnOwnerMoved;
            _ownerForm.VisibleChanged += OnOwnerVisibleChanged;
            _ownerForm.Activated += OnOwnerActivated;
            _ownerForm.FormClosing += OnOwnerClosing;
            Activated += OnPanelActivated;
            _eventsAttached = true;
        }

        private void DetachOwnerEvents()
        {
            if (_ownerForm == null || !_eventsAttached) return;
            _ownerForm.LocationChanged -= OnOwnerMoved;
            _ownerForm.SizeChanged -= OnOwnerMoved;
            _ownerForm.VisibleChanged -= OnOwnerVisibleChanged;
            _ownerForm.Activated -= OnOwnerActivated;
            _ownerForm.FormClosing -= OnOwnerClosing;
            Activated -= OnPanelActivated;
            _eventsAttached = false;
        }

        private void OnOwnerMoved(object? sender, EventArgs e)
        {
            if (_ownerForm == null) return;

            if (_slideTimer?.Enabled == true)
            {
                int remaining = Left - _slideToLeft;
                _slideToLeft = TargetOpenLeft();
                _slideFromLeft = _slideToLeft + remaining;
                Left = _slideFromLeft;
                Top = _ownerForm.Top;
                return;
            }

            SyncToOwner();
        }

        private void OnOwnerVisibleChanged(object? sender, EventArgs e)
        {
            if (_ownerForm == null || IsDisposed) return;
            if (!_ownerForm.Visible || _ownerForm.WindowState == FormWindowState.Minimized)
                Hide();
            else if (!_slideClosing)
            {
                Show();
                SyncToOwner();
                SendBehindOwner();
            }
        }

        private void OnOwnerActivated(object? sender, EventArgs e)
        {
            if (_slideClosing || _slideTimer?.Enabled == true || !Visible) return;
            BringWithOwner();
        }

        private void OnPanelActivated(object? sender, EventArgs e)
        {
            if (_slideClosing || _slideTimer?.Enabled == true) return;
            BringWithOwner();
        }

        private void OnOwnerClosing(object? sender, FormClosingEventArgs e) => BeginSlideClose();

        private int TargetOpenLeft() =>
            _ownerForm == null ? Left : _ownerForm.Left - Width;

        private void AlignHiddenUnderOwner()
        {
            if (_ownerForm == null) return;
            Top = _ownerForm.Top;
            Left = _ownerForm.Left;
        }

        private void StartSlide(bool toOpen)
        {
            if (_ownerForm == null)
            {
                if (!toOpen) Close();
                return;
            }

            _slideFromLeft = Left;
            _slideToLeft = toOpen ? TargetOpenLeft() : _ownerForm.Left;
            _slideStart = DateTime.UtcNow;

            _slideTimer?.Stop();
            _slideTimer?.Dispose();
            _slideTimer = new System.Windows.Forms.Timer { Interval = 8 };
            _slideTimer.Tick += (_, _) => OnSlideTick(toOpen);
            _slideTimer.Start();
            OnSlideTick(toOpen);
        }

        private void OnSlideTick(bool toOpen)
        {
            if (_ownerForm == null || _ownerForm.IsDisposed)
            {
                StopSlide();
                if (!toOpen) Close();
                return;
            }

            SendBehindOwner();
            Top = _ownerForm.Top;

            double t = Math.Min(1.0, (DateTime.UtcNow - _slideStart).TotalMilliseconds / SlideDurationMs);
            float eased = 1f - MathF.Pow(1f - (float)t, 3f);
            Left = (int)Math.Round(_slideFromLeft + (_slideToLeft - _slideFromLeft) * eased);

            if (t < 1.0) return;

            StopSlide();
            Left = _slideToLeft;
            Top = _ownerForm.Top;

            if (toOpen)
            {
                BringWithOwner();
            }
            else
            {
                DetachOwnerEvents();
                ClosedByUser?.Invoke(this, EventArgs.Empty);
                Hide();
                Close();
            }
        }

        private void StopSlide()
        {
            if (_slideTimer == null) return;
            _slideTimer.Stop();
            _slideTimer.Dispose();
            _slideTimer = null;
        }

        private void SendBehindOwner()
        {
            if (_ownerForm == null || _ownerForm.IsDisposed || IsDisposed) return;
            if (!IsHandleCreated || !_ownerForm.IsHandleCreated) return;
            SetWindowPos(Handle, _ownerForm.Handle, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        }

        private void BringWithOwner()
        {
            if (_ownerForm == null || _ownerForm.IsDisposed || IsDisposed) return;
            if (!Visible || !IsHandleCreated || !_ownerForm.IsHandleCreated) return;

            uint flags = SwpNomove | SwpNosize | SwpNoactivate;
            SetWindowPos(_ownerForm.Handle, IntPtr.Zero, 0, 0, 0, 0, flags);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, flags);
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            BackColor = AppTheme.FormBackground;
            ForeColor = AppTheme.PrimaryText;
            Invalidate(true);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopSlide();
            DetachOwnerEvents();
            AppTheme.Changed -= OnThemeChanged;
            base.OnFormClosed(e);
        }
    }
}
