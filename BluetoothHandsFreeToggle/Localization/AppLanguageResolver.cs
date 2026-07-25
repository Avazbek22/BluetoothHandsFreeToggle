using System.Globalization;

namespace BluetoothHandsFreeToggle.Localization;

public static class AppLanguageResolver
{
    public static AppLanguage ResolveStartupLanguage(ILanguagePreferenceStore preferenceStore)
    {
        ArgumentNullException.ThrowIfNull(preferenceStore);
        return preferenceStore.Load() ?? DetectSystemLanguage();
    }

    public static AppLanguage DetectSystemLanguage()
        => DetectFromCultures(
            CultureInfo.CurrentUICulture,
            CultureInfo.CurrentCulture,
            CultureInfo.InstalledUICulture);

    public static AppLanguage DetectFromCultures(params CultureInfo?[] cultures)
    {
        foreach (var culture in cultures)
        {
            if (string.Equals(
                    culture?.TwoLetterISOLanguageName,
                    "ru",
                    StringComparison.OrdinalIgnoreCase))
            {
                return AppLanguage.Russian;
            }
        }

        return AppLanguage.English;
    }
}
