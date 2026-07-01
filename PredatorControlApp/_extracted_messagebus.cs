using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PredatorControlApp
{
    /// <summary>
    /// NVIDIA App Message Bus (phase 3.4). JSON RPC envelope to CrimsonNative plugin modules.
    /// Requires MessageBus broadcaster (nvcontainer) and target plugin host (NVIDIA App for NvCplDisplay).
    /// </summary>
    internal static class NvidiaMessageBusClient
    {
        private static readonly string MessageBusDllPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA Corporation", "NVIDIA App", "MessageBus", "MessageBus.dll");

        private static readonly string MessageBusConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA Corporation", "NVIDIA App", "MessageBus");

        private const string ClientSystemName = "PredatorControl";
        private const string ClientModuleName = "MuxClient";

        private const int DefaultTimeoutMs = 30_000;

        public readonly record struct ProbeResult(
            bool DllPresent,
            bool Connected,
            int InitResult,
            string? Error)
        {
            public string Describe() =>
                $"dll={DllPresent} connected={Connected} init={InitResult} err={Error ?? "none"}";
        }

        public readonly record struct CommandResult(
            bool DllLoaded,
            bool Connected,
            bool Sent,
            int PostResult,
            string? ResponseJson,
            IReadOnlyList<string> Steps,
            string? Error)
        {
            public bool Ok => Sent && PostResult == 0;
        }

        public static ProbeResult Probe()
        {
            if (!File.Exists(MessageBusDllPath))
                return new ProbeResult(false, false, int.MinValue, "MessageBus.dll missing");

            var steps = new List<string>();
            if (!TryConnect(steps, out int init, out IntPtr bus, out string? error))
                return new ProbeResult(true, false, init, error);

            ReleaseBus(bus);
            return new ProbeResult(true, true, init, null);
        }

        /// <summary>
        /// Send JSON command to a CrimsonNative plugin module (e.g. NvCplDisplayPlugin).
        /// </summary>
        public static CommandResult TrySendCommand(
            string targetSystem,
            string targetModule,
            string commandType,
            object? parameters,
            int timeoutMs = DefaultTimeoutMs)
        {
            var steps = new List<string>();
            int postResult = int.MinValue;
            string? error = null;
            string? response = null;

            if (!File.Exists(MessageBusDllPath))
            {
                error = "MessageBus.dll missing";
                steps.Add(error);
                return new CommandResult(false, false, false, postResult, null, steps, error);
            }

            string requestJson = BuildEnvelope(targetSystem, targetModule, commandType, parameters);
            steps.Add($"envelope_len={requestJson.Length} type={commandType} target={targetSystem}:{targetModule}");

            if (!TryConnect(steps, out int init, out IntPtr bus, out error))
                return new CommandResult(true, false, false, postResult, null, steps, error);

            try
            {
                postResult = Native.PostMessage(bus, requestJson);
                steps.Add($"messageBusPostMessage rc={postResult}");
                if (postResult != 0)
                    error = $"messageBusPostMessage failed ({postResult})";
            }
            finally
            {
                ReleaseBus(bus);
            }

            return new CommandResult(true, true, postResult == 0, postResult, response, steps, error);
        }

        internal static string BuildEnvelope(
            string targetSystem,
            string targetModule,
            string commandType,
            object? parameters)
        {
            var envelope = new BusEnvelope
            {
                System = targetSystem,
                Module = targetModule,
                SourceSystem = ClientSystemName,
                SourceModule = ClientModuleName,
                ReferenceUniqueId = Guid.NewGuid().ToString("N"),
                Type = commandType,
                Payload = parameters
            };

            return JsonSerializer.Serialize(envelope, JsonOptions);
        }

        private static bool TryConnect(List<string> steps, out int initResult, out IntPtr busHandle, out string? error)
        {
            initResult = int.MinValue;
            busHandle = IntPtr.Zero;
            error = null;

            if (!NativeLibrary.TryLoad(MessageBusDllPath, out IntPtr module))
            {
                error = "LoadLibrary(MessageBus.dll) failed";
                steps.Add(error);
                return false;
            }

            try
            {
                if (!Native.TryGetExport(module, "getMessageBusInterfaceWithConfig", out IntPtr getProc))
                {
                    error = "getMessageBusInterfaceWithConfig export missing";
                    steps.Add(error);
                    return false;
                }

                var get = Marshal.GetDelegateForFunctionPointer<GetBusWithConfigDelegate>(getProc);
                busHandle = get(ClientSystemName, ClientModuleName, MessageBusConfigDir);
                initResult = busHandle != IntPtr.Zero ? 0 : 1;
                steps.Add($"getMessageBusInterfaceWithConfig({ClientSystemName}:{ClientModuleName}) handle=0x{busHandle.ToInt64():X}");

                if (busHandle == IntPtr.Zero)
                {
                    error = "getMessageBusInterfaceWithConfig returned null";
                    return false;
                }

                return true;
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }

        private static void ReleaseBus(IntPtr busHandle)
        {
            if (busHandle == IntPtr.Zero)
                return;

            if (!NativeLibrary.TryLoad(MessageBusDllPath, out IntPtr module))
                return;

            try
            {
                if (Native.TryGetExport(module, "releaseMessageBusInterface", out IntPtr relProc))
                {
                    var release = Marshal.GetDelegateForFunctionPointer<ReleaseBusDelegate>(relProc);
                    release(busHandle);
                }
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }

        private static class Native
        {
            public static bool TryGetExport(IntPtr module, string name, out IntPtr proc) =>
                NativeLibrary.TryGetExport(module, name, out proc) && proc != IntPtr.Zero;

            public static int PostMessage(IntPtr bus, string json)
            {
                if (!NativeLibrary.TryLoad(MessageBusDllPath, out IntPtr module))
                    return -1;

                try
                {
                    if (!TryGetExport(module, "messageBusPostMessage", out IntPtr postProc))
                        return -2;

                    var post = Marshal.GetDelegateForFunctionPointer<PostMessageDelegate>(postProc);
                    return post(bus, json);
                }
                finally
                {
                    NativeLibrary.Free(module);
                }
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate IntPtr GetBusWithConfigDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string systemName,
            [MarshalAs(UnmanagedType.LPStr)] string moduleName,
            [MarshalAs(UnmanagedType.LPStr)] string configPath);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ReleaseBusDelegate(IntPtr bus);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int PostMessageDelegate(IntPtr bus, [MarshalAs(UnmanagedType.LPStr)] string message);

        private sealed class BusEnvelope
        {
            [JsonPropertyName("system")]
            public string System { get; set; } = "";

            [JsonPropertyName("module")]
            public string Module { get; set; } = "";

            [JsonPropertyName("source_system")]
            public string SourceSystem { get; set; } = "";

            [JsonPropertyName("source_module")]
            public string SourceModule { get; set; } = "";

            [JsonPropertyName("reference_uniqueid")]
            public string ReferenceUniqueId { get; set; } = "";

            [JsonPropertyName("type")]
            public string Type { get; set; } = "";

            [JsonPropertyName("payload")]
            public object? Payload { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null
        };
    }
}
