using System.Windows;
using Microsoft.Win32;

namespace InvestmentStory.App.Infrastructure;

public static class ThemeManager
{
    private const string LightThemePath = "Themes/LightTheme.xaml";
    private const string DarkThemePath = "Themes/DarkTheme.xaml";

    public static void Apply(string themeMode)
    {
        var resolved = ResolveTheme(themeMode);
        var themePath = resolved.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? DarkThemePath
            : LightThemePath;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(x =>
            x.Source is not null &&
            (x.Source.OriginalString.EndsWith(LightThemePath, StringComparison.OrdinalIgnoreCase) ||
             x.Source.OriginalString.EndsWith(DarkThemePath, StringComparison.OrdinalIgnoreCase)));

        var replacement = new ResourceDictionary
        {
            Source = new Uri(themePath, UriKind.Relative)
        };

        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
            return;
        }

        var index = dictionaries.IndexOf(existing);
        dictionaries.RemoveAt(index);
        dictionaries.Insert(index, replacement);
    }

    private static string ResolveTheme(string themeMode)
    {
        if (themeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase) ||
            themeMode.Equals("ダーク", StringComparison.OrdinalIgnoreCase))
        {
            return "Dark";
        }

        if (themeMode.Equals("System", StringComparison.OrdinalIgnoreCase) ||
            themeMode.Equals("Windowsの設定に従う", StringComparison.OrdinalIgnoreCase))
        {
            return IsWindowsLightTheme() ? "Light" : "Dark";
        }

        return "Light";
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return Convert.ToInt32(value) != 0;
        }
        catch
        {
            return true;
        }
    }
}
