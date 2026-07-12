using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Keeps live keyboard feedback without blocking the UI thread.
    /// UI callers push snapshots freely; hardware applies are coalesced and run off-thread.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class LiveLightingApplier : IDisposable
    {
        private const int HardwareIntervalMs = 45;
        private const int SaveDebounceMs = 400;

        private readonly Control _syncContext;
        private readonly Action<LightingSettingsSnapshot> _applyHardware;
        private readonly Action<LightingSettingsSnapshot> _saveState;

        private readonly System.Windows.Forms.Timer _hardwareTimer;
        private readonly System.Windows.Forms.Timer _saveTimer;

        private LightingSettingsSnapshot? _pendingHardware;
        private LightingSettingsSnapshot? _pendingSave;
        private LightingSettingsSnapshot? _inFlight;
        private bool _hardwareBusy;
        private bool _disposed;

        public LiveLightingApplier(
            Control syncContext,
            Action<LightingSettingsSnapshot> applyHardware,
            Action<LightingSettingsSnapshot> saveState)
        {
            _syncContext = syncContext;
            _applyHardware = applyHardware;
            _saveState = saveState;

            _hardwareTimer = new System.Windows.Forms.Timer { Interval = HardwareIntervalMs };
            _hardwareTimer.Tick += (_, _) => OnHardwareTick();

            _saveTimer = new System.Windows.Forms.Timer { Interval = SaveDebounceMs };
            _saveTimer.Tick += (_, _) => OnSaveTick();
        }

        /// <summary>Push a live change (e.g. while dragging the color marker).</summary>
        public void Push(LightingSettingsSnapshot snapshot)
        {
            if (_disposed) return;
            _pendingHardware = snapshot;
            _pendingSave = snapshot;

            if (!_hardwareBusy && !_hardwareTimer.Enabled)
                _hardwareTimer.Start();

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Flush hardware immediately (mouse-up / panel close) and schedule save.</summary>
        public void Flush(LightingSettingsSnapshot snapshot)
        {
            if (_disposed) return;
            _pendingHardware = snapshot;
            _pendingSave = snapshot;
            _hardwareTimer.Stop();
            TryStartHardwareApply();

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void FlushNowAndSave(LightingSettingsSnapshot snapshot)
        {
            if (_disposed) return;
            _pendingHardware = snapshot;
            _pendingSave = snapshot;
            _hardwareTimer.Stop();
            TryStartHardwareApply();
            OnSaveTick();
        }

        private void OnHardwareTick()
        {
            _hardwareTimer.Stop();
            TryStartHardwareApply();
        }

        private void TryStartHardwareApply()
        {
            if (_disposed || _hardwareBusy) return;
            if (_pendingHardware == null) return;

            _inFlight = _pendingHardware;
            _pendingHardware = null;
            _hardwareBusy = true;

            var snapshot = _inFlight;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    _applyHardware(snapshot);
                }
                catch { }
                finally
                {
                    if (!_disposed)
                    {
                        try
                        {
                            if (_syncContext.IsHandleCreated && !_syncContext.IsDisposed)
                            {
                                _syncContext.BeginInvoke(() =>
                                {
                                    _hardwareBusy = false;
                                    _inFlight = null;
                                    if (_pendingHardware != null)
                                        TryStartHardwareApply();
                                });
                            }
                            else
                            {
                                _hardwareBusy = false;
                                _inFlight = null;
                            }
                        }
                        catch
                        {
                            _hardwareBusy = false;
                            _inFlight = null;
                        }
                    }
                }
            });
        }

        private void OnSaveTick()
        {
            _saveTimer.Stop();
            var snapshot = _pendingSave;
            _pendingSave = null;
            if (snapshot == null) return;
            try { _saveState(snapshot); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _hardwareTimer.Stop();
            _hardwareTimer.Dispose();
            _saveTimer.Stop();
            _saveTimer.Dispose();
        }
    }
}
