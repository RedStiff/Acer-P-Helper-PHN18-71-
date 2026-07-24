using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    internal sealed class GpuPnpController
    {
        private const string NvidiaVen = "VEN_10DE";
        private const string NvDisplayService = "NVDisplay.ContainerLocalSystem";

        public sealed class DeviceInfo
        {
            public required string InstanceId { get; init; }
            public required string Name { get; init; }
            public required string Status { get; init; }
            public bool IsEnabled => string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase);
        }

        public sealed class Result
        {
            public bool Ok { get; init; }
            public string Detail { get; init; } = "";
            public IReadOnlyList<string> Changed { get; init; } = Array.Empty<string>();
        }

        public IReadOnlyList<DeviceInfo> GetNvidiaDisplayDevices()
        {
            var list = new List<DeviceInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, Status, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass = 'Display'");
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        string id = obj["PNPDeviceID"]?.ToString() ?? "";
                        if (!id.Contains(NvidiaVen, StringComparison.OrdinalIgnoreCase))
                            continue;

                        list.Add(new DeviceInfo
                        {
                            InstanceId = id,
                            Name = obj["Name"]?.ToString() ?? id,
                            Status = obj["Status"]?.ToString() ?? ""
                        });
                    }
                }
            }
            catch
            {
                // fall through — empty list
            }

            return list;
        }

        public GpuDeviceState GetDeviceState()
        {
            var devices = GetNvidiaDisplayDevices();
            if (devices.Count == 0)
                return GpuDeviceState.IgpuOnly;

            bool anyEnabled = devices.Any(d => d.IsEnabled);
            return anyEnabled ? GpuDeviceState.Hybrid : GpuDeviceState.IgpuOnly;
        }

        public Result SetNvidiaDisplayEnabled(bool enable)
        {
            var devices = GetNvidiaDisplayDevices();
            if (devices.Count == 0 && enable)
            {
                // Disabled devices still appear in Win32_PnPEntity with Status != OK.
                // If truly absent, nothing to enable.
                devices = GetNvidiaDisplayDevicesIncludingDisabled();
            }

            if (devices.Count == 0)
                return new Result { Ok = false, Detail = "No NVIDIA display PnP device found" };

            var changed = new List<string>();
            foreach (var device in devices)
            {
                if (enable && device.IsEnabled)
                {
                    changed.Add(device.InstanceId);
                    continue;
                }

                if (!enable && !device.IsEnabled)
                {
                    changed.Add(device.InstanceId);
                    continue;
                }

                try
                {
                    InvokePnpMethod(device.InstanceId, enable ? "Enable" : "Disable");
                    changed.Add(device.InstanceId);
                }
                catch (Exception ex)
                {
                    return new Result
                    {
                        Ok = false,
                        Detail = ex.Message,
                        Changed = changed
                    };
                }
            }

            return new Result
            {
                Ok = true,
                Detail = $"{changed.Count} device(s)",
                Changed = changed
            };
        }

        public Result SetNvDisplayService(string action)
        {
            // action: Stop | Restart
            try
            {
                using var sc = new ServiceController(NvDisplayService);
                if (string.Equals(action, "Stop", StringComparison.OrdinalIgnoreCase))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped &&
                        sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                }
                else if (string.Equals(action, "Restart", StringComparison.OrdinalIgnoreCase))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped &&
                        sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }

                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45));
                }
                else
                {
                    return new Result { Ok = false, Detail = $"Unknown action {action}" };
                }

                sc.Refresh();
                return new Result { Ok = true, Detail = sc.Status.ToString() };
            }
            catch (InvalidOperationException)
            {
                return new Result { Ok = false, Detail = $"{NvDisplayService} not found" };
            }
            catch (Exception ex)
            {
                return new Result { Ok = false, Detail = ex.Message };
            }
        }

        private IReadOnlyList<DeviceInfo> GetNvidiaDisplayDevicesIncludingDisabled()
        {
            // Broader query — some builds omit PNPClass when disabled.
            var list = new List<DeviceInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, Status, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%VEN_10DE%'");
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        string id = obj["PNPDeviceID"]?.ToString() ?? "";
                        string name = obj["Name"]?.ToString() ?? "";
                        // Prefer display adapters; skip audio/USB nvidia devices.
                        if (!id.Contains("DISPLAY", StringComparison.OrdinalIgnoreCase) &&
                            !name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) &&
                            !name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (id.Contains("HDAUDIO", StringComparison.OrdinalIgnoreCase) ||
                            id.Contains("USB", StringComparison.OrdinalIgnoreCase))
                            continue;

                        list.Add(new DeviceInfo
                        {
                            InstanceId = id,
                            Name = name,
                            Status = obj["Status"]?.ToString() ?? ""
                        });
                    }
                }
            }
            catch
            {
            }

            return list;
        }

        private static void InvokePnpMethod(string instanceId, string method)
        {
            string escaped = EscapeWmiString(instanceId);
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_PnPEntity WHERE PNPDeviceID = '{escaped}'");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    ManagementBaseObject result = obj.InvokeMethod(method, null!, null!);
                    int returnValue = Convert.ToInt32(result["ReturnValue"]);
                    if (returnValue != 0)
                        throw new InvalidOperationException($"{method} returned {returnValue} for {instanceId}");
                    return;
                }
            }

            throw new InvalidOperationException($"PnP device not found: {instanceId}");
        }

        private static string EscapeWmiString(string value)
        {
            var sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                if (c is '\\' or '\'')
                    sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
