using FalconBMS.Launcher.Models;
using Microsoft.Win32;
using System.Windows;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Applies the launcher theme at startup and when the user changes it manually.
/// Auto mode reads the current Windows app theme on application start only.
/// </summary>
public static class ThemeService
{
    private const string SharedDictionaryPath = "Styles/Theme.Shared.xaml";
    private const string LightDictionaryPath = "Styles/Theme.Light.xaml";
    private const string DarkDictionaryPath = "Styles/Theme.Dark.xaml";

    public static void ApplySavedThemeOnStartup()
    {
        var savedMode = NormalizeThemeMode(Properties.Settings.Default.LauncherThemeMode);
        ApplyTheme(savedMode, saveSetting: false);
    }

    public static void ApplyTheme(string themeMode, bool saveSetting = true)
    {
        var normalizedMode = NormalizeThemeMode(themeMode);
        var effectiveMode = ResolveEffectiveThemeMode(normalizedMode);

        EnsureThemeDictionariesLoaded();
        ReplaceActiveThemeDictionary(effectiveMode);

        if (saveSetting &&
            !string.Equals(Properties.Settings.Default.LauncherThemeMode, normalizedMode, StringComparison.Ordinal))
        {
            Properties.Settings.Default.LauncherThemeMode = normalizedMode;
            Properties.Settings.Default.Save();
        }

        DebugDiagnosticsService.Info($"Launcher theme applied. SavedMode={normalizedMode}, EffectiveMode={effectiveMode}");
    }

    public static string NormalizeThemeMode(string? themeMode)
    {
        if (string.Equals(themeMode, LauncherThemeModes.Light, StringComparison.OrdinalIgnoreCase))
            return LauncherThemeModes.Light;

        if (string.Equals(themeMode, LauncherThemeModes.Dark, StringComparison.OrdinalIgnoreCase))
            return LauncherThemeModes.Dark;

        return LauncherThemeModes.Auto;
    }

    private static string ResolveEffectiveThemeMode(string themeMode)
    {
        if (string.Equals(themeMode, LauncherThemeModes.Light, StringComparison.Ordinal))
            return LauncherThemeModes.Light;

        if (string.Equals(themeMode, LauncherThemeModes.Dark, StringComparison.Ordinal))
            return LauncherThemeModes.Dark;

        return IsWindowsAppThemeLight() ? LauncherThemeModes.Light : LauncherThemeModes.Dark;
    }

    private static bool IsWindowsAppThemeLight()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);

            var value = personalizeKey?.GetValue("AppsUseLightTheme");
            if (value is int intValue)
                return intValue != 0;
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, "Failed reading Windows theme. Defaulting to Light.");
        }

        return true;
    }

    private static void EnsureThemeDictionariesLoaded()
    {
        var appResources = Application.Current.Resources;
        var dictionaries = appResources.MergedDictionaries;

        if (!ContainsDictionary(dictionaries, SharedDictionaryPath))
        {
            dictionaries.Insert(0, new ResourceDictionary
            {
                Source = new Uri(SharedDictionaryPath, UriKind.Relative)
            });
        }

        if (!ContainsDictionary(dictionaries, LightDictionaryPath) &&
            !ContainsDictionary(dictionaries, DarkDictionaryPath))
        {
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(LightDictionaryPath, UriKind.Relative)
            });
        }
    }

    private static void ReplaceActiveThemeDictionary(string effectiveMode)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var activeThemePath = string.Equals(effectiveMode, LauncherThemeModes.Dark, StringComparison.Ordinal)
            ? DarkDictionaryPath
            : LightDictionaryPath;

        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (IsThemeDictionaryPath(source))
            {
                if (!PathMatches(source, activeThemePath))
                {
                    dictionaries[i] = new ResourceDictionary
                    {
                        Source = new Uri(activeThemePath, UriKind.Relative)
                    };
                }

                return;
            }
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(activeThemePath, UriKind.Relative)
        });
    }

    private static bool ContainsDictionary(IList<ResourceDictionary> dictionaries, string path)
    {
        foreach (var dictionary in dictionaries)
        {
            var source = dictionary.Source?.OriginalString;
            if (PathMatches(source, path))
                return true;
        }

        return false;
    }

    private static bool IsThemeDictionaryPath(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return PathMatches(source, LightDictionaryPath) || PathMatches(source, DarkDictionaryPath);
    }

    private static bool PathMatches(string? source, string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return source.EndsWith(expectedPath, StringComparison.OrdinalIgnoreCase);
    }
}