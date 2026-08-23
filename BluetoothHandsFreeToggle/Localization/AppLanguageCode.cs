namespace BluetoothHandsFreeToggle.Localization;

public static class AppLanguageCode
{
    public static string ToCode(AppLanguage language)
        => AppLanguageCatalog.Get(language).Code;

    public static bool TryParse(string? value, out AppLanguage language)
        => AppLanguageCatalog.TryParse(value, out language);
}
