using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    internal sealed class GpuControlService
    {
        private readonly GpuPnpController _pnp = new();
        private readonly DdsControl _dds = new();
        private readonly object _gate = new();
        private bool _busy;

        public bool IsBusy
        {
            get { lock (_gate) return _busy; }
        }

        public sealed class ApplyResult
        {
            public bool Ok { get; init; }
            public string Detail { get; init; } = "";
            public GpuStatus Before { get; init; } = new();
            public GpuStatus After { get; init; } = new();
        }

        public GpuStatus GetStatus()
        {
            GpuDeviceState device = _pnp.GetDeviceState();
            GpuAceSnapshot ace = GpuAceReader.Read();
            var owner = GraphicsMuxDetector.DetectPanelOwner();
            // Invalidate detector cache after switches — DetectPanelOwner uses TTL.
            // Caller should Invalidate after apply; here we still use current cache.
            GpuDisplayMode display = device == GpuDeviceState.IgpuOnly
                ? GpuDisplayMode.Unknown
                : GpuAceReader.InferDisplayMode(ace, owner);

            string nvDisplay = owner == GraphicsMuxDetector.PanelOwner.Discrete ? "Enabled" : "Disabled";
            string ownerKind = owner switch
            {
                GraphicsMuxDetector.PanelOwner.Discrete => "NVIDIA",
                GraphicsMuxDetector.PanelOwner.Integrated => "Intel",
                _ => "?"
            };

            return new GpuStatus
            {
                DeviceState = device,
                DisplayMode = display,
                PanelOwner = owner,
                Ace = ace,
                MuxSignature = $"owner={ownerKind}|nv_display={nvDisplay}",
                Detail = $"device={device}; display={display}; {ace.Summary}"
            };
        }

        public ApplyResult ApplyDevice(GpuDeviceState target)
        {
            if (target is not (GpuDeviceState.IgpuOnly or GpuDeviceState.Hybrid))
                return new ApplyResult { Ok = false, Detail = "Unsupported device target" };

            if (!TryEnterBusy())
                return new ApplyResult { Ok = false, Detail = "GPU switch already in progress" };

            try
            {
                GpuStatus before = GetStatus();
                bool enable = target == GpuDeviceState.Hybrid;
                var pnp = _pnp.SetNvidiaDisplayEnabled(enable);
                if (!pnp.Ok)
                    return new ApplyResult { Ok = false, Detail = pnp.Detail, Before = before, After = GetStatus() };

                var svc = _pnp.SetNvDisplayService(enable ? "Restart" : "Stop");
                Thread.Sleep(2000);
                GraphicsMuxDetector.InvalidateCache();
                GpuStatus after = GetStatus();
                bool ok = after.DeviceState == target || pnp.Ok;
                return new ApplyResult
                {
                    Ok = ok,
                    Detail = $"PnP {pnp.Detail}; service {svc.Detail}",
                    Before = before,
                    After = after
                };
            }
            finally
            {
                ExitBusy();
            }
        }

        public ApplyResult ApplyDisplayMode(GpuDisplayMode target)
        {
            if (target is GpuDisplayMode.Unknown)
                return new ApplyResult { Ok = false, Detail = "Unsupported display mode" };

            if (!TryEnterBusy())
                return new ApplyResult { Ok = false, Detail = "GPU switch already in progress" };

            try
            {
                GpuStatus before = GetStatus();
                if (before.DeviceState != GpuDeviceState.Hybrid)
                {
                    var restore = _pnp.SetNvidiaDisplayEnabled(true);
                    if (!restore.Ok)
                    {
                        return new ApplyResult
                        {
                            Ok = false,
                            Detail = "DISPLAY MODE needs Hybrid (dGPU on): " + restore.Detail,
                            Before = before,
                            After = GetStatus()
                        };
                    }

                    _pnp.SetNvDisplayService("Restart");
                    // Endurance tears down UXD; wait before AppSync COM.
                    if (!_dds.EnsureUxdHealthy(TimeSpan.FromSeconds(45), out string uxdDetail))
                    {
                        return new ApplyResult
                        {
                            Ok = false,
                            Detail = "Hybrid restored but UXD not ready: " + uxdDetail,
                            Before = before,
                            After = GetStatus()
                        };
                    }

                    Thread.Sleep(2000);
                    GraphicsMuxDetector.InvalidateCache();
                }

                var dds = _dds.SetDisplayMode(target);
                GraphicsMuxDetector.InvalidateCache();
                GpuStatus after = GetStatus();
                bool modeOk = after.DisplayMode == target || dds.Ok;
                return new ApplyResult
                {
                    Ok = modeOk,
                    Detail = dds.Detail,
                    Before = before,
                    After = after
                };
            }
            finally
            {
                ExitBusy();
            }
        }

        public Task<GpuStatus> GetStatusAsync() =>
            Task.Run(GetStatus);

        public Task<ApplyResult> ApplyDeviceAsync(GpuDeviceState target) =>
            Task.Run(() => ApplyDevice(target));

        public Task<ApplyResult> ApplyDisplayModeAsync(GpuDisplayMode target) =>
            Task.Run(() => ApplyDisplayMode(target));

        private bool TryEnterBusy()
        {
            lock (_gate)
            {
                if (_busy) return false;
                _busy = true;
                return true;
            }
        }

        private void ExitBusy()
        {
            lock (_gate) _busy = false;
        }
    }
}
