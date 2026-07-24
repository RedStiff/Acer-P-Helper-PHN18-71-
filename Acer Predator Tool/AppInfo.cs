using System.Reflection;

namespace PredatorControlApp
{
    internal static class AppInfo
    {
        public const string DisplayName = "Acer Predator Tool";
        public const string ProductName = "Acer Predator Tool";
        public const string StartupRegistryValue = "AcerPredatorTool";

        /// <summary>Informational version from the assembly (csproj InformationalVersion).</summary>
        public static string Version
        {
            get
            {
                var info = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                    return info.Split('+')[0];
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
            }
        }

        public static string DisplayNameWithVersion => DisplayName + " " + Version;
    }
}
