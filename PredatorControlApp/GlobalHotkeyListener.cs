using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    internal sealed class GlobalHotkeyListener : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int HotkeyIdToggle = 0xB703;

        private readonly Control _messageTarget;
        private readonly AppSettings _settings;
        private bool _registered;

        public event EventHandler? HotkeyPressed;

        public string HotkeyDisplayName => _settings.ToggleHotkeyDisplayName();

        public GlobalHotkeyListener(Control messageTarget, AppSettings settings)
        {
            _messageTarget = messageTarget;
            _settings = settings;
        }

        public void Start()
        {
            if (_messageTarget.IsHandleCreated)
                Register();
            else
                _messageTarget.HandleCreated += (_, _) => Register();
        }

        public bool ProcessWndProc(ref Message m)
        {
            if (m.Msg != WmHotkey || m.WParam.ToInt32() != HotkeyIdToggle)
                return false;

            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void Register()
        {
            if (_registered || _messageTarget.IsDisposed || !_messageTarget.IsHandleCreated)
                return;

            Unregister();
            _registered = RegisterHotKey(
                _messageTarget.Handle,
                HotkeyIdToggle,
                _settings.ToggleHotkeyModifiers,
                _settings.ToggleHotkeyVk);
        }

        private void Unregister()
        {
            if (!_messageTarget.IsHandleCreated || _messageTarget.IsDisposed)
                return;

            UnregisterHotKey(_messageTarget.Handle, HotkeyIdToggle);
            _registered = false;
        }

        public void Dispose() => Unregister();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
