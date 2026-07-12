using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Listens for the Acer Predator hardware key via a low-level keyboard hook.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class PredatorKeyListener : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeydown = 0x0100;
        private const int WmSyskeydown = 0x0104;
        private const uint LlkhfInjected = 0x10;
        private static readonly TimeSpan KeyDebounce = TimeSpan.FromMilliseconds(450);

        private readonly Control _messageTarget;
        private readonly AppSettings _settings;
        private readonly LowLevelKeyboardProc _hookProc;

        private IntPtr _hookHandle;
        private bool _started;
        private DateTime _lastKeyUtc = DateTime.MinValue;

        public event EventHandler? KeyPressed;

        public PredatorKeyListener(Control messageTarget, AppSettings settings)
        {
            _messageTarget = messageTarget;
            _settings = settings;
            _hookProc = OnLowLevelKey;
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
        }

        private IntPtr OnLowLevelKey(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown))
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                if ((data.flags & LlkhfInjected) == 0 && IsPredatorKey(data.vkCode, data.scanCode))
                {
                    RaiseKeyPressed();
                    return (IntPtr)1;
                }
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private bool IsPredatorKey(uint vkCode, uint scanCode)
        {
            uint vk = _settings.PredatorKeyVk ?? AppSettings.DefaultPredatorVk;
            uint scan = _settings.PredatorKeyScanCode ?? AppSettings.DefaultPredatorScan;
            return vkCode == vk && scanCode == scan;
        }

        private void RaiseKeyPressed()
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastKeyUtc < KeyDebounce)
                return;

            _lastKeyUtc = now;
            if (_messageTarget.IsDisposed) return;

            if (_messageTarget.InvokeRequired)
                _messageTarget.BeginInvoke(() => KeyPressed?.Invoke(this, EventArgs.Empty));
            else
                KeyPressed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (!_started) return;

            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }

            _started = false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
