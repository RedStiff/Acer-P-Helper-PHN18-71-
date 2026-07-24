using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;

namespace PredatorControlApp
{
    /// <summary>
    /// Live DDS / Advanced Optimus control.
    /// Product path: NvAppSyncProxy + App SessionFilter (COM; no NVIDIA App.exe).
    /// Legacy App inject/CDP disabled (UI blink / flaky).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class DdsControl
    {
        public const string UxdStartedEventName =
            @"Local\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57";

        /// <summary>Lab-only: re-enable NVIDIA App host/CDP. Product default is false.
        /// Not a const — keeps legacy code reachable for the compiler (avoids CS0162).</summary>
        private static readonly bool AllowLegacyNvidiaAppHost = false;

        private const int CdpPort = 9333;
        private const string NvDisplayService = "NVDisplay.ContainerLocalSystem";
        private const string NvContainerService = "NvContainerLocalSystem";

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        public sealed class Result
        {
            public bool Ok { get; init; }
            public string Detail { get; init; } = "";
            public string? GetPayload { get; init; }
            public string? SetPayload { get; init; }
            public GpuAceSnapshot AceBefore { get; init; } = new();
            public GpuAceSnapshot AceAfter { get; init; } = new();
        }

        public bool IsUxdServiceStarted()
        {
            IntPtr handle = OpenEvent(0x00100000, false, UxdStartedEventName);
            if (handle == IntPtr.Zero)
                return false;
            CloseHandle(handle);
            return true;
        }

        public bool EnsureUxdHealthy(TimeSpan timeout, out string detail)
        {
            if (IsUxdServiceStarted())
            {
                detail = "UXDServiceStarted present";
                return true;
            }

            try
            {
                RestartService(NvDisplayService);
                RestartService(NvContainerService);
            }
            catch (Exception ex)
            {
                detail = "UXD restart failed: " + ex.Message;
                return false;
            }

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (IsUxdServiceStarted())
                {
                    detail = "UXD recovered after service restart";
                    return true;
                }

                Thread.Sleep(400);
            }

            detail = "UXDServiceStarted still missing after restart";
            return false;
        }

        public Result SetDisplayMode(GpuDisplayMode mode, CancellationToken ct = default)
        {
            if (mode is GpuDisplayMode.Unknown)
                return new Result { Ok = false, Detail = "Unknown display mode" };

            bool automatic = mode == GpuDisplayMode.Auto;
            int muxState = mode == GpuDisplayMode.Nvidia ? 2 : 1;

            if (!EnsureUxdHealthy(TimeSpan.FromSeconds(30), out string uxdDetail))
                return new Result { Ok = false, Detail = uxdDetail };

            GpuAceSnapshot before = GpuAceReader.Read();

            if (!AllowLegacyNvidiaAppHost)
            {
                var com = DdsAppSync.TrySetMode(muxState, automatic);
                bool already = InferAceMode(before) == mode;
                GpuAceSnapshot after = already
                    ? GpuAceReader.Read()
                    : WaitForAceMode(before, mode, TimeSpan.FromSeconds(8), ct);
                bool modeHit = InferAceMode(after) == mode;
                bool ok = com.ComOk && (modeHit || already);
                return new Result
                {
                    Ok = ok,
                    Detail = "appsync: " + com.Detail + "; ACE " + before.Summary + " -> " + after.Summary
                             + (already ? " (already)" : ""),
                    AceBefore = before,
                    AceAfter = after
                };
            }

            // Legacy lab path (disabled): hidden host + NvCpl inject.
            string nativeDetail = "";
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                    Thread.Sleep(1000);

                if (!DdsNativeHost.TrySetMode(mode, out nativeDetail))
                    continue;

                bool already = InferAceMode(before) == mode;
                GpuAceSnapshot afterNative = already
                    ? GpuAceReader.Read()
                    : WaitForAceMode(before, mode, TimeSpan.FromSeconds(8), ct);
                bool apiOk = nativeDetail.Contains("OK|set=0", StringComparison.Ordinal);
                bool modeHit = InferAceMode(afterNative) == mode;
                bool ok = apiOk && (modeHit || already);
                if (!ok && attempt == 0)
                    continue; // retry once if ACE not settled

                return new Result
                {
                    Ok = ok,
                    Detail = "native: " + nativeDetail + "; ACE " + before.Summary + " -> " + afterNative.Summary
                             + (already ? " (already)" : ""),
                    AceBefore = before,
                    AceAfter = afterNative
                };
            }

            if (!EnsureCdpReady(TimeSpan.FromSeconds(50), out string cdpDetail, out bool launchedApp))
                return new Result
                {
                    Ok = false,
                    Detail = "native failed (" + nativeDetail + "); CDP: " + cdpDetail,
                    AceBefore = before
                };

            try
            {
                string wsUrl = GetPageWebSocketUrlAsync(ct).GetAwaiter().GetResult();
                using var session = CdpSession.ConnectAsync(wsUrl, ct).GetAwaiter().GetResult();

                // Fresh launch needs plugin warm-up; reused CDP is usually ready immediately.
                int cefWaitRounds = launchedApp ? 25 : 8;
                WaitForCefQueryAsync(session, cefWaitRounds, ct).GetAwaiter().GetResult();

                string? getPayload = null;
                int getAttempts = launchedApp ? 8 : 3;
                for (int attempt = 0; attempt < getAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    var get = EvaluateCefQueryAsync(session, BuildGetJs(), ct).GetAwaiter().GetResult();
                    getPayload = get.Raw;
                    if (get.Ok)
                        break;
                    Thread.Sleep(launchedApp ? 800 : 350);
                }

                string setJs = BuildSetJs(automatic, muxState);
                string? setPayload = null;
                bool setOk = false;
                int setAttempts = launchedApp ? 6 : 3;
                for (int attempt = 0; attempt < setAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    var set = EvaluateCefQueryAsync(session, setJs, ct).GetAwaiter().GetResult();
                    setPayload = set.Raw;
                    if (set.Ok)
                    {
                        setOk = true;
                        break;
                    }
                    Thread.Sleep(launchedApp ? 800 : 350);
                }

                GpuAceSnapshot after = WaitForAceChange(before, TimeSpan.FromSeconds(8), ct);

                bool aceHit = before.Ok && after.Ok && before.Summary != after.Summary;
                bool modeHit = GpuAceReader.InferDisplayMode(after, GraphicsMuxDetector.DetectPanelOwner()) == mode;

                bool ok = setOk && (aceHit || modeHit);
                string setSnippet = string.IsNullOrEmpty(setPayload)
                    ? ""
                    : " set=" + Truncate(setPayload, 240);
                string detail = ok
                    ? $"DDS HIT {before.Summary} -> {after.Summary}"
                    : $"DDS NO_HIT setOk={setOk} aceHit={aceHit} ({uxdDetail}; {cdpDetail}){setSnippet}";

                return new Result
                {
                    Ok = ok,
                    Detail = detail,
                    GetPayload = getPayload,
                    SetPayload = setPayload,
                    AceBefore = before,
                    AceAfter = after
                };
            }
            catch (Exception ex)
            {
                return new Result
                {
                    Ok = false,
                    Detail = ex.Message,
                    AceBefore = before,
                    AceAfter = GpuAceReader.Read()
                };
            }
        }

        public Result GetDisplayState(CancellationToken ct = default)
        {
            GpuAceSnapshot ace = GpuAceReader.Read();
            if (!IsUxdServiceStarted())
                return new Result { Ok = false, Detail = "UXD not running", AceBefore = ace, AceAfter = ace };

            if (!IsCdpUp())
                return new Result { Ok = true, Detail = "CDP down; ACE only", AceBefore = ace, AceAfter = ace };

            try
            {
                string wsUrl = GetPageWebSocketUrlAsync(ct).GetAwaiter().GetResult();
                using var session = CdpSession.ConnectAsync(wsUrl, ct).GetAwaiter().GetResult();
                var get = EvaluateCefQueryAsync(session, BuildGetJs(), ct).GetAwaiter().GetResult();
                return new Result
                {
                    Ok = get.Ok,
                    Detail = get.Ok ? "GetDDSState ok" : get.Error,
                    GetPayload = get.Raw,
                    AceBefore = ace,
                    AceAfter = ace
                };
            }
            catch (Exception ex)
            {
                return new Result { Ok = false, Detail = ex.Message, AceBefore = ace, AceAfter = ace };
            }
        }

        private bool EnsureCdpReady(TimeSpan timeout, out string detail, out bool launchedApp)
        {
            launchedApp = false;
            if (IsCdpUp())
            {
                detail = $"CDP already on :{CdpPort}";
                return true;
            }

            // Mux switch often restarts CEF briefly — wait before killing/relaunching.
            if (WaitForCdp(TimeSpan.FromSeconds(10)))
            {
                detail = $"CDP recovered on :{CdpPort}";
                return true;
            }

            // Do not KillAll while our native helper pipe is alive — that destroys the preferred host.
            if (DdsNativeHost.IsHelperAlive())
            {
                detail = "native helper alive — skip CDP KillAll";
                return false;
            }

            // Interactive desktop + SW_HIDE. Do not use a private desktop (steals single-instance App).
            NvidiaAppCdpHost.KillAll();
            Thread.Sleep(800);

            if (!NvidiaAppCdpHost.TryLaunch(out string launchDetail))
            {
                detail = launchDetail;
                return false;
            }

            launchedApp = true;
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (IsCdpUp())
                {
                    detail = $"CDP ready on :{CdpPort} ({launchDetail})";
                    return true;
                }
                Thread.Sleep(300);
            }

            detail = $"CDP did not come up on :{CdpPort} ({launchDetail})";
            return false;
        }

        private static bool WaitForCdp(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (IsCdpUp())
                    return true;
                Thread.Sleep(250);
            }
            return false;
        }

        private static bool IsCdpUp()
        {
            try
            {
                using var response = Http.GetAsync($"http://127.0.0.1:{CdpPort}/json").GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static GpuAceSnapshot WaitForAceChange(
            GpuAceSnapshot before, TimeSpan timeout, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;
            GpuAceSnapshot latest = GpuAceReader.Read();
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                latest = GpuAceReader.Read();
                if (before.Ok && latest.Ok && before.Summary != latest.Summary)
                    return latest;
                Thread.Sleep(250);
            }
            return latest;
        }

        /// <summary>Wait until ACE infers the requested display mode (or timeout).</summary>
        private static GpuAceSnapshot WaitForAceMode(
            GpuAceSnapshot before, GpuDisplayMode mode, TimeSpan timeout, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;
            GpuAceSnapshot latest = GpuAceReader.Read();
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                latest = GpuAceReader.Read();
                if (InferAceMode(latest) == mode)
                    return latest;
                if (before.Ok && latest.Ok && before.Summary != latest.Summary)
                {
                    Thread.Sleep(300);
                    return GpuAceReader.Read();
                }

                Thread.Sleep(250);
            }

            return latest;
        }

        private static GpuDisplayMode InferAceMode(GpuAceSnapshot ace) =>
            GpuAceReader.InferDisplayMode(ace, GraphicsMuxDetector.PanelOwner.Unknown);

        private static async Task<string> GetPageWebSocketUrlAsync(CancellationToken ct)
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{CdpPort}/json", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            string? fallback = null;
            foreach (var target in doc.RootElement.EnumerateArray())
            {
                if (!target.TryGetProperty("type", out var type) || type.GetString() != "page")
                    continue;
                if (!target.TryGetProperty("webSocketDebuggerUrl", out var ws))
                    continue;
                string? url = ws.GetString();
                if (string.IsNullOrEmpty(url))
                    continue;

                string title = target.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                if (title.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    return url;
                fallback ??= url;
            }

            if (fallback != null)
                return fallback;

            throw new InvalidOperationException("No CDP page target");
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value;
            return value[..max] + "…";
        }

        private static async Task WaitForCefQueryAsync(CdpSession session, int rounds, CancellationToken ct)
        {
            for (int i = 0; i < rounds; i++)
            {
                ct.ThrowIfCancellationRequested();
                string? type = await session.EvaluateAsync("typeof window.cefQuery", awaitPromise: false, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(type) && type.Contains("function", StringComparison.Ordinal))
                    return;
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
        }

        private sealed class CefQueryResult
        {
            public bool Ok { get; init; }
            public string Raw { get; init; } = "";
            public string Error { get; init; } = "";
        }

        private static async Task<CefQueryResult> EvaluateCefQueryAsync(
            CdpSession session, string expression, CancellationToken ct)
        {
            string? valueJson = await session.EvaluateAsync(expression, awaitPromise: true, ct)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(valueJson))
                return new CefQueryResult { Ok = false, Error = "empty CDP result" };

            try
            {
                using var doc = JsonDocument.Parse(valueJson);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("ok", out var okEl) &&
                    okEl.ValueKind == JsonValueKind.True)
                {
                    string raw = root.TryGetProperty("r", out var r) ? r.ToString() : valueJson;
                    return new CefQueryResult { Ok = true, Raw = raw };
                }

                string err = root.TryGetProperty("err", out var e) ? e.ToString() : valueJson;
                return new CefQueryResult { Ok = false, Raw = valueJson, Error = err };
            }
            catch
            {
                return new CefQueryResult { Ok = false, Raw = valueJson, Error = "parse failed" };
            }
        }

        private sealed class CdpSession : IDisposable
        {
            private readonly ClientWebSocket _ws;
            private int _nextId = 1;

            private CdpSession(ClientWebSocket ws) => _ws = ws;

            public static async Task<CdpSession> ConnectAsync(string wsUrl, CancellationToken ct)
            {
                var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
                var session = new CdpSession(ws);
                await session.SendAsync("Runtime.enable", null, ct).ConfigureAwait(false);
                return session;
            }

            public async Task<string?> EvaluateAsync(string expression, bool awaitPromise, CancellationToken ct)
            {
                int evalId = await SendAsync("Runtime.evaluate", new Dictionary<string, object?>
                {
                    ["expression"] = expression,
                    ["awaitPromise"] = awaitPromise,
                    ["returnByValue"] = true,
                    ["userGesture"] = true
                }, ct).ConfigureAwait(false);

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(35);
                while (DateTime.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    using var message = await ReceiveJsonAsync(ct).ConfigureAwait(false);
                    if (!message.RootElement.TryGetProperty("id", out var mid) || mid.GetInt32() != evalId)
                        continue;

                    if (message.RootElement.TryGetProperty("result", out var result) &&
                        result.TryGetProperty("result", out var inner))
                    {
                        if (inner.TryGetProperty("value", out var value))
                        {
                            return value.ValueKind == JsonValueKind.String
                                ? value.GetString()
                                : value.GetRawText();
                        }

                        if (inner.TryGetProperty("subtype", out var subtype) &&
                            subtype.GetString() == "error" &&
                            inner.TryGetProperty("description", out var errDesc))
                        {
                            return "{\"ok\":false,\"err\":" + JsonSerializer.Serialize(errDesc.GetString()) + "}";
                        }

                        if (inner.TryGetProperty("description", out var desc))
                            return desc.GetString();
                    }

                    if (message.RootElement.TryGetProperty("error", out var cdpErr))
                    {
                        return "{\"ok\":false,\"err\":" + JsonSerializer.Serialize(cdpErr.GetRawText()) + "}";
                    }

                    return message.RootElement.GetRawText();
                }

                throw new System.TimeoutException("CDP evaluate timeout");
            }

            private async Task<int> SendAsync(string method, object? parameters, CancellationToken ct)
            {
                int id = _nextId++;
                var payload = new Dictionary<string, object?> { ["id"] = id, ["method"] = method };
                if (parameters != null)
                    payload["params"] = parameters;
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                return id;
            }

            private async Task<JsonDocument> ReceiveJsonAsync(CancellationToken ct)
            {
                var buffer = new byte[64 * 1024];
                using var ms = new MemoryStream();
                while (true)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    WebSocketReceiveResult result = await _ws.ReceiveAsync(segment, ct).ConfigureAwait(false);
                    ms.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                        break;
                }

                ms.Position = 0;
                return await JsonDocument.ParseAsync(ms, cancellationToken: ct).ConfigureAwait(false);
            }

            public void Dispose()
            {
                try
                {
                    if (_ws.State == WebSocketState.Open)
                        _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                            .GetAwaiter().GetResult();
                }
                catch { /* ignore */ }
                _ws.Dispose();
            }
        }

        private static string BuildGetJs() =>
            "(()=>new Promise(res=>{const q={command:'QUERY_IPC_EXTENSION_MESSAGE'," +
            "system:'CrimsonNative',module:'NvCplDisplayPlugin',method:'GetDDSState'," +
            "payload:{}};if(!window.cefQuery)return res({error:'no_cefQuery'});" +
            "window.cefQuery({request:JSON.stringify(q),persistent:false," +
            "onSuccess:r=>res({ok:true,r}),onFailure:(c,e)=>res({ok:false,code:c,err:String(e)})});" +
            "setTimeout(()=>res({error:'timeout'}),12000)}))()";

        private static string BuildSetJs(bool automatic, int muxState)
        {
            // Match working tools/inject_dds/_hit_setdds.py exactly.
            string autoJs = automatic ? "true" : "false";
            return "(()=>new Promise(res=>{const q={command:'QUERY_IPC_EXTENSION_MESSAGE'," +
                   "system:'CrimsonNative',module:'NvCplDisplayPlugin',method:'SetDDSState'," +
                   "payload:{bIsAutomatic:" + autoJs + ",MuxState:" + muxState + "}};" +
                   "if(!window.cefQuery)return res({error:'no_cefQuery'});" +
                   "window.cefQuery({request:JSON.stringify(q),persistent:false," +
                   "onSuccess:r=>res({ok:true,r}),onFailure:(c,e)=>res({ok:false,code:c,err:String(e)})});" +
                   "setTimeout(()=>res({error:'timeout'}),25000)}))()";
        }

        private static void RestartService(string name)
        {
            using var sc = new ServiceController(name);
            if (sc.Status != ServiceControllerStatus.Stopped &&
                sc.Status != ServiceControllerStatus.StopPending)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45));
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenEvent(uint desiredAccess, bool inheritHandle, string name);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
