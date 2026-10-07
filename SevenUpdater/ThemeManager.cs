using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace SevenUpdater
{
    /// <summary>Switches between the Light and Dark palettes at runtime.</summary>
    public static class ThemeManager
    {
        public const string Light = "Light";
        public const string Dark = "Dark";

        private const string ThemeFolder = "/Themes/";
        private const string ControlsFile = "Controls.xaml";

        /// <summary>Bound to the sun/moon button in the title bar of every <see cref="ThemedWindow"/>.</summary>
        public static readonly RoutedUICommand ToggleThemeCommand = new RoutedUICommand("Toggle light/dark theme", "ToggleTheme", typeof(ThemeManager));

        /// <summary>App.xaml starts with the Light palette.</summary>
        public static string CurrentTheme { get; private set; } = Light;

        public static bool IsDark => CurrentTheme != Light;

        public static event Action<string> ThemeChanged;

        public static void Toggle()
        {
            Apply(IsDark ? Light : Dark);
        }

        /// <summary>Applies "Light", "Dark" or "DarkMint" (anything else falls back to Light).</summary>
        public static void Apply(string theme)
        {
            theme = Normalize(theme);
            if (theme == CurrentTheme || Application.Current == null)
            {
                return;
            }

            Collection<ResourceDictionary> dictionaries = Application.Current.Resources.MergedDictionaries;

            var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,{ThemeFolder}{theme}.xaml", UriKind.Absolute) };
            int paletteIndex = FindPaletteIndex(dictionaries);
            if (paletteIndex >= 0)
            {
                dictionaries[paletteIndex] = palette;
            }
            else
            {
                dictionaries.Insert(0, palette);
            }

            // Controls.xaml resolves a few colors once (the GroupBox layer brushes), so it is reloaded
            // after the palette; every implicit style and DynamicResource then refreshes automatically.
            int controlsIndex = FindIndex(dictionaries, ControlsFile);
            if (controlsIndex >= 0)
            {
                dictionaries[controlsIndex] = new ResourceDictionary { Source = new Uri($"pack://application:,,,{ThemeFolder}{ControlsFile}", UriKind.Absolute) };
            }

            CurrentTheme = theme;
            ThemeChanged?.Invoke(theme);
        }

        private static string Normalize(string theme)
        {
            if (string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase))
            {
                return Dark;
            }
            if (string.Equals(theme, "DarkMint", StringComparison.OrdinalIgnoreCase))
            {
                return "DarkMint";
            }
            return Light;
        }

        private static int FindPaletteIndex(Collection<ResourceDictionary> dictionaries)
        {
            foreach (string name in new[] { Light + ".xaml", Dark + ".xaml", "DarkMint.xaml" })
            {
                int index = FindIndex(dictionaries, name);
                if (index >= 0)
                {
                    return index;
                }
            }
            return -1;
        }

        private static int FindIndex(Collection<ResourceDictionary> dictionaries, string fileName)
        {
            for (int i = 0; i < dictionaries.Count; i++)
            {
                Uri source = dictionaries[i].Source;
                if (source != null && source.OriginalString.EndsWith(ThemeFolder + fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
