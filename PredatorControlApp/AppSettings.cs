using System.Text.Json;

namespace PredatorControlApp
{
    public class AppSettings
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;

        public const uint DefaultToggleModifiers = ModControl | ModAlt;
        public const uint DefaultToggleVk = 0x50; // P

        public const uint DefaultPredatorVk = 0xFF;
        public const uint DefaultPredatorScan = 0x75;

        public uint ToggleHotkeyModifiers { get; set; } = DefaultToggleModifiers;
        public uint ToggleHotkeyVk { get; set; } = DefaultToggleVk;

        public uint? PredatorKeyVk { get; set; } = DefaultPredatorVk;
        public uint? PredatorKeyScanCode { get; set; } = DefaultPredatorScan;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PredatorControl",
            "app_settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (settings != null) return settings;
                }
            }
            catch { }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch { }
        }

        public string ToggleHotkeyDisplayName()
        {
            var parts = new List<string>();
            uint mods = ToggleHotkeyModifiers;
            if ((mods & ModControl) != 0) parts.Add("Ctrl");
            if ((mods & ModAlt) != 0) parts.Add("Alt");
            if ((mods & ModShift) != 0) parts.Add("Shift");
            if ((mods & ModWin) != 0) parts.Add("Win");
            parts.Add(VkToDisplayName(ToggleHotkeyVk));
            return string.Join("+", parts);
        }

        private static string VkToDisplayName(uint vk) => vk switch
        {
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            _ => $"0x{vk:X}"
        };
    }
}
