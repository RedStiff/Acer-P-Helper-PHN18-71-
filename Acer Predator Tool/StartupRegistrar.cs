using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace PredatorControlApp
{
    /// <summary>
    /// Registers logon autostart. HKCU Run cannot launch requireAdministrator apps
    /// (Windows suppresses the UAC prompt at logon), so we use a logon scheduled task
    /// with highest privileges instead.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class StartupRegistrar
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string LegacyRunValueName = "PredatorControl";

        private const int TaskTriggerLogon = 9;
        private const int TaskActionExec = 0;
        private const int TaskCreateOrUpdate = 6;
        private const int TaskLogonInteractiveToken = 3;
        private const int TaskRunLevelHighest = 1;

        public static void RegisterHiddenAtLogon()
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                exePath = Application.ExecutablePath;

            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return;

            RemoveLegacyRunEntries();
            RegisterLogonTask(exePath, "-hidden");
        }

        private static void RemoveLegacyRunEntries()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key == null) return;

                key.DeleteValue(AppInfo.StartupRegistryValue, throwOnMissingValue: false);
                key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
            }
            catch
            {
                // Best-effort cleanup; scheduled task is the real autostart path.
            }
        }

        private static void RegisterLogonTask(string exePath, string arguments)
        {
            try
            {
                Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null)
                {
                    RegisterLogonTaskViaSchtasks(exePath, arguments);
                    return;
                }

                dynamic scheduler = Activator.CreateInstance(serviceType)
                    ?? throw new InvalidOperationException("Schedule.Service unavailable.");
                scheduler.Connect();

                dynamic folder = scheduler.GetFolder("\\");
                dynamic taskDefinition = scheduler.NewTask(0);

                taskDefinition.RegistrationInfo.Description =
                    $"{AppInfo.DisplayName} — start minimized to tray at logon.";
                taskDefinition.Principal.UserId = WindowsIdentity.GetCurrent().Name;
                taskDefinition.Principal.LogonType = TaskLogonInteractiveToken;
                taskDefinition.Principal.RunLevel = TaskRunLevelHighest;

                taskDefinition.Settings.Enabled = true;
                taskDefinition.Settings.StartWhenAvailable = true;
                taskDefinition.Settings.AllowDemandStart = true;
                taskDefinition.Settings.DisallowStartIfOnBatteries = false;
                taskDefinition.Settings.StopIfGoingOnBatteries = false;
                taskDefinition.Settings.ExecutionTimeLimit = "PT0S";
                taskDefinition.Settings.MultipleInstances = 2; // TASK_INSTANCES_IGNORE_NEW

                dynamic trigger = taskDefinition.Triggers.Create(TaskTriggerLogon);
                trigger.UserId = WindowsIdentity.GetCurrent().Name;
                trigger.Enabled = true;

                dynamic action = taskDefinition.Actions.Create(TaskActionExec);
                action.Path = exePath;
                action.Arguments = arguments;
                string? workDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrWhiteSpace(workDir))
                    action.WorkingDirectory = workDir;

                folder.RegisterTaskDefinition(
                    AppInfo.StartupRegistryValue,
                    taskDefinition,
                    TaskCreateOrUpdate,
                    null,
                    null,
                    TaskLogonInteractiveToken);
            }
            catch
            {
                RegisterLogonTaskViaSchtasks(exePath, arguments);
            }
        }

        private static void RegisterLogonTaskViaSchtasks(string exePath, string arguments)
        {
            // /TR expects one string: quoted exe path + args.
            string tr = $"\\\"{exePath}\\\" {arguments}";
            string args =
                $"/Create /TN \"{AppInfo.StartupRegistryValue}\" /TR \"{tr}\" " +
                "/SC ONLOGON /RL HIGHEST /F";

            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(15_000);
        }
    }
}
