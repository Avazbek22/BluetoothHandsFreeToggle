namespace BluetoothHandsFreeToggle.Localization;

public static class AppLanguageCode
{
    public static string ToCode(AppLanguage language)
        => language switch
        {
            AppLanguage.English => "en",
            AppLanguage.Russian => "ru",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

    public static bool TryParse(string? value, out AppLanguage language)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "en":
            case "en-us":
            case "en-gb":
            case "english":
                language = AppLanguage.English;
                return true;

            case "ru":
            case "ru-ru":
            case "russian":
            case "русский":
                language = AppLanguage.Russian;
                return true;

            default:
                language = default;
                return false;
        }
    }
}
