using System.Text.Json;

namespace PredatorControlApp
{
    public class UiSettings
    {
        public const int DefaultCurvesWidth = 920;
        public const int DefaultCurvesHeight = 820;
        public const int MinCurvesWidth = 760;
        public const int MinCurvesHeight = 720;

        public const int DefaultGameSyncWidth = 780;
        public const int DefaultGameSyncHeight = 700;
        public const int MinGameSyncWidth = 640;
        public const int MinGameSyncHeight = 520;

        public const int DefaultKeyboardColorWidth = 840;
        public const int DefaultKeyboardColorHeight = 816;
        public const int MinKeyboardColorWidth = 700;
        public const int MinKeyboardColorHeight = 672;

        public int CurvesWindowWidth { get; set; } = DefaultCurvesWidth;
        public int CurvesWindowHeight { get; set; } = DefaultCurvesHeight;
        public int GameSyncWindowWidth { get; set; } = DefaultGameSyncWidth;
        public int GameSyncWindowHeight { get; set; } = DefaultGameSyncHeight;
        public int KeyboardColorWindowWidth { get; set; } = DefaultKeyboardColorWidth;
        public int KeyboardColorWindowHeight { get; set; } = DefaultKeyboardColorHeight;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PredatorControl",
            "ui_settings.json");

        public static UiSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath));
                    if (settings != null) return settings;
                }
            }
            catch { }
            return new UiSettings();
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

        public static void SaveCurvesWindowSize(Size clientSize)
        {
            var settings = Load();
            settings.CurvesWindowWidth = Math.Max(MinCurvesWidth, clientSize.Width);
            settings.CurvesWindowHeight = Math.Max(MinCurvesHeight, clientSize.Height);
            settings.Save();
        }

        public Size GetCurvesClientSize() => new(
            Math.Max(MinCurvesWidth, CurvesWindowWidth),
            Math.Max(MinCurvesHeight, CurvesWindowHeight));

        public static void SaveGameSyncWindowSize(Size clientSize)
        {
            var settings = Load();
            settings.GameSyncWindowWidth = Math.Max(MinGameSyncWidth, clientSize.Width);
            settings.GameSyncWindowHeight = Math.Max(MinGameSyncHeight, clientSize.Height);
            settings.Save();
        }

        public Size GetGameSyncClientSize() => new(
            Math.Max(MinGameSyncWidth, GameSyncWindowWidth),
            Math.Max(MinGameSyncHeight, GameSyncWindowHeight));

        public static void SaveKeyboardColorWindowSize(Size clientSize)
        {
            var settings = Load();
            settings.KeyboardColorWindowWidth = Math.Max(MinKeyboardColorWidth, clientSize.Width);
            settings.KeyboardColorWindowHeight = Math.Max(MinKeyboardColorHeight, clientSize.Height);
            settings.Save();
        }

        public Size GetKeyboardColorClientSize() => new(
            Math.Max(MinKeyboardColorWidth, KeyboardColorWindowWidth),
            Math.Max(MinKeyboardColorHeight, KeyboardColorWindowHeight));
    }
}
