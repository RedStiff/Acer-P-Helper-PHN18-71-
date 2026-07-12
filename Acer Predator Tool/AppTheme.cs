using Microsoft.Win32;

namespace PredatorControlApp
{
    internal static class AppTheme
    {
        public static bool IsDark { get; private set; }

        public static event EventHandler? Changed;

        public static Color FormBackground { get; private set; }
        public static Color TitleBarBackground { get; private set; }
        public static Color PrimaryText { get; private set; }
        public static Color SecondaryText { get; private set; }
        public static Color SectionHeader { get; private set; }
        public static Color Separator { get; private set; }
        public static Color Accent { get; private set; }
        public static Color AccentMuted { get; private set; }
        public static Color PanelBackground { get; private set; }
        public static Color CaptionButton { get; private set; }
        public static Color CaptionButtonHover { get; private set; }
        public static Color CaptionCloseHover { get; private set; }

        public static Color ButtonBackground { get; private set; }
        public static Color ButtonHover { get; private set; }
        public static Color ButtonActiveBackground { get; private set; }
        public static Color ButtonDisabled { get; private set; }
        public static Color ButtonBorder { get; private set; }
        public static Color ButtonBorderHover { get; private set; }
        public static Color ButtonBorderDisabled { get; private set; }
        public static Color ButtonText { get; private set; }
        public static Color ButtonTextHover { get; private set; }
        public static Color ButtonTextDisabled { get; private set; }

        public static Color DropDownBackground { get; private set; }
        public static Color DropDownHover { get; private set; }
        public static Color DropDownPopupBackground { get; private set; }

        public static Color SliderTrack { get; private set; }
        public static Color SliderFill { get; private set; }
        public static Color SliderThumb { get; private set; }

        public static Color SwitchTrackOff { get; private set; }
        public static Color SwitchTrackOn { get; private set; }
        public static Color SwitchTrackDisabled { get; private set; }
        public static Color SwitchKnobOff { get; private set; }
        public static Color SwitchKnobOn { get; private set; }
        public static Color SwitchKnobDisabled { get; private set; }
        public static Color SwitchBorder { get; private set; }
        public static Color SwitchBorderHover { get; private set; }

        public static Color ListSelectionBackground { get; private set; }
        public static Color InputBackground { get; private set; }
        public static Color InputBorder { get; private set; }
        public static Color DialogBorder { get; private set; }

        public static void Initialize()
        {
            Refresh();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                Refresh();
        }

        private static bool _initialized;

        public static void Refresh()
        {
            IsDark = ReadSystemUsesDarkTheme();

            if (IsDark)
                ApplyDarkPalette();
            else
                ApplyLightPalette();

            if (_initialized)
                Changed?.Invoke(null, EventArgs.Empty);

            _initialized = true;
        }

        private static bool ReadSystemUsesDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object? value = key?.GetValue("AppsUseLightTheme");
                if (value is int lightTheme)
                    return lightTheme == 0;
            }
            catch { }

            return false;
        }

        private static void ApplyLightPalette()
        {
            FormBackground = Color.FromArgb(243, 243, 243);
            TitleBarBackground = Color.FromArgb(255, 255, 255);
            PrimaryText = Color.FromArgb(32, 32, 32);
            SecondaryText = Color.FromArgb(96, 96, 96);
            SectionHeader = Color.FromArgb(64, 64, 64);
            Separator = Color.FromArgb(229, 229, 229);
            Accent = Color.FromArgb(0, 120, 212);
            AccentMuted = Color.FromArgb(0, 99, 177);
            PanelBackground = Color.FromArgb(255, 255, 255);
            CaptionButton = Color.FromArgb(96, 96, 96);
            CaptionButtonHover = Color.FromArgb(32, 32, 32);
            CaptionCloseHover = Color.FromArgb(196, 43, 28);

            ButtonBackground = Color.FromArgb(255, 255, 255);
            ButtonHover = Color.FromArgb(243, 243, 243);
            ButtonActiveBackground = Color.FromArgb(237, 246, 252);
            ButtonDisabled = Color.FromArgb(243, 243, 243);
            ButtonBorder = Color.FromArgb(209, 209, 209);
            ButtonBorderHover = Color.FromArgb(173, 173, 173);
            ButtonBorderDisabled = Color.FromArgb(229, 229, 229);
            ButtonText = Color.FromArgb(32, 32, 32);
            ButtonTextHover = Color.FromArgb(32, 32, 32);
            ButtonTextDisabled = Color.FromArgb(161, 161, 161);

            DropDownBackground = Color.FromArgb(255, 255, 255);
            DropDownHover = Color.FromArgb(243, 243, 243);
            DropDownPopupBackground = Color.FromArgb(255, 255, 255);

            SliderTrack = Color.FromArgb(209, 209, 209);
            SliderFill = Color.FromArgb(0, 120, 212);
            SliderThumb = Color.FromArgb(0, 120, 212);

            SwitchTrackOff = Color.FromArgb(209, 209, 209);
            SwitchTrackOn = Color.FromArgb(0, 120, 212);
            SwitchTrackDisabled = Color.FromArgb(229, 229, 229);
            SwitchKnobOff = Color.FromArgb(255, 255, 255);
            SwitchKnobOn = Color.FromArgb(255, 255, 255);
            SwitchKnobDisabled = Color.FromArgb(243, 243, 243);
            SwitchBorder = Color.FromArgb(173, 173, 173);
            SwitchBorderHover = Color.FromArgb(96, 96, 96);

            ListSelectionBackground = Color.FromArgb(237, 246, 252);
            InputBackground = Color.FromArgb(255, 255, 255);
            InputBorder = Color.FromArgb(209, 209, 209);
            DialogBorder = Color.FromArgb(209, 209, 209);
        }

        private static void ApplyDarkPalette()
        {
            FormBackground = Color.FromArgb(32, 32, 32);
            TitleBarBackground = Color.FromArgb(28, 28, 28);
            PrimaryText = Color.FromArgb(255, 255, 255);
            SecondaryText = Color.FromArgb(173, 173, 173);
            SectionHeader = Color.FromArgb(200, 200, 200);
            Separator = Color.FromArgb(61, 61, 61);
            Accent = Color.FromArgb(96, 205, 255);
            AccentMuted = Color.FromArgb(76, 194, 255);
            PanelBackground = Color.FromArgb(43, 43, 43);
            CaptionButton = Color.FromArgb(200, 200, 200);
            CaptionButtonHover = Color.FromArgb(255, 255, 255);
            CaptionCloseHover = Color.FromArgb(255, 104, 104);

            ButtonBackground = Color.FromArgb(50, 50, 50);
            ButtonHover = Color.FromArgb(62, 62, 62);
            ButtonActiveBackground = Color.FromArgb(38, 59, 78);
            ButtonDisabled = Color.FromArgb(40, 40, 40);
            ButtonBorder = Color.FromArgb(85, 85, 85);
            ButtonBorderHover = Color.FromArgb(110, 110, 110);
            ButtonBorderDisabled = Color.FromArgb(55, 55, 55);
            ButtonText = Color.FromArgb(220, 220, 220);
            ButtonTextHover = Color.FromArgb(255, 255, 255);
            ButtonTextDisabled = Color.FromArgb(110, 110, 110);

            DropDownBackground = Color.FromArgb(50, 50, 50);
            DropDownHover = Color.FromArgb(62, 62, 62);
            DropDownPopupBackground = Color.FromArgb(43, 43, 43);

            SliderTrack = Color.FromArgb(85, 85, 85);
            SliderFill = Color.FromArgb(96, 205, 255);
            SliderThumb = Color.FromArgb(96, 205, 255);

            SwitchTrackOff = Color.FromArgb(85, 85, 85);
            SwitchTrackOn = Color.FromArgb(0, 99, 177);
            SwitchTrackDisabled = Color.FromArgb(55, 55, 55);
            SwitchKnobOff = Color.FromArgb(220, 220, 220);
            SwitchKnobOn = Color.FromArgb(255, 255, 255);
            SwitchKnobDisabled = Color.FromArgb(110, 110, 110);
            SwitchBorder = Color.FromArgb(110, 110, 110);
            SwitchBorderHover = Color.FromArgb(150, 150, 150);

            ListSelectionBackground = Color.FromArgb(38, 59, 78);
            InputBackground = Color.FromArgb(43, 43, 43);
            InputBorder = Color.FromArgb(85, 85, 85);
            DialogBorder = Color.FromArgb(85, 85, 85);
        }

        public static Color TempColor(int temp) => temp switch
        {
            <= 0 => SecondaryText,
            < 55 => Accent,
            < 72 => Color.FromArgb(255, 185, 0),
            < 87 => Color.FromArgb(255, 140, 0),
            _ => Color.FromArgb(232, 17, 35)
        };
    }
}
