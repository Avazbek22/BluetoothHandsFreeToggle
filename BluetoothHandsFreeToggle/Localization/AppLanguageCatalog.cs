using System.Globalization;

namespace BluetoothHandsFreeToggle.Localization;

public sealed record AppLanguageDefinition(
    AppLanguage Language,
    string Code,
    string NativeName,
    string EnglishName)
{
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Code);
    public string ResourceCode => Language switch
    {
        AppLanguage.English => "en",
        AppLanguage.Russian => "ru",
        _ => Code
    };
}

public static class AppLanguageCatalog
{
    private static readonly AppLanguageDefinition[] Definitions =
    [
        new(AppLanguage.English, "en-US", "English", "English"),
        new(AppLanguage.Russian, "ru-RU", "Русский", "Russian"),
        new(AppLanguage.ChineseSimplified, "zh-CN", "简体中文", "Chinese (Simplified)"),
        new(AppLanguage.ChineseTraditional, "zh-TW", "繁體中文", "Chinese (Traditional)"),
        new(AppLanguage.German, "de-DE", "Deutsch", "German"),
        new(AppLanguage.French, "fr-FR", "Français", "French"),
        new(AppLanguage.SpanishSpain, "es-ES", "Español (España)", "Spanish (Spain)"),
        new(AppLanguage.SpanishLatinAmerica, "es-419", "Español (Latinoamérica)", "Spanish (Latin America)"),
        new(AppLanguage.PortugueseBrazil, "pt-BR", "Português (Brasil)", "Portuguese (Brazil)"),
        new(AppLanguage.PortuguesePortugal, "pt-PT", "Português (Portugal)", "Portuguese (Portugal)"),
        new(AppLanguage.Japanese, "ja-JP", "日本語", "Japanese"),
        new(AppLanguage.Korean, "ko-KR", "한국어", "Korean"),
        new(AppLanguage.Polish, "pl-PL", "Polski", "Polish"),
        new(AppLanguage.Turkish, "tr-TR", "Türkçe", "Turkish"),
        new(AppLanguage.Italian, "it-IT", "Italiano", "Italian"),
        new(AppLanguage.Thai, "th-TH", "ไทย", "Thai"),
        new(AppLanguage.Ukrainian, "uk-UA", "Українська", "Ukrainian"),
        new(AppLanguage.Czech, "cs-CZ", "Čeština", "Czech"),
        new(AppLanguage.Dutch, "nl-NL", "Nederlands", "Dutch"),
        new(AppLanguage.Swedish, "sv-SE", "Svenska", "Swedish"),
        new(AppLanguage.Danish, "da-DK", "Dansk", "Danish"),
        new(AppLanguage.NorwegianBokmal, "nb-NO", "Norsk bokmål", "Norwegian Bokmål"),
        new(AppLanguage.Finnish, "fi-FI", "Suomi", "Finnish"),
        new(AppLanguage.Hungarian, "hu-HU", "Magyar", "Hungarian"),
        new(AppLanguage.Romanian, "ro-RO", "Română", "Romanian"),
        new(AppLanguage.Greek, "el-GR", "Ελληνικά", "Greek"),
        new(AppLanguage.Bulgarian, "bg-BG", "Български", "Bulgarian"),
        new(AppLanguage.Indonesian, "id-ID", "Bahasa Indonesia", "Indonesian"),
        new(AppLanguage.Malay, "ms-MY", "Bahasa Melayu", "Malay"),
        new(AppLanguage.Vietnamese, "vi-VN", "Tiếng Việt", "Vietnamese")
    ];

    private static readonly Dictionary<AppLanguage, AppLanguageDefinition> ByLanguage =
        Definitions.ToDictionary(definition => definition.Language);

    private static readonly IReadOnlyList<AppLanguageDefinition> PublicDefinitions =
        Array.AsReadOnly(Definitions);

    private static readonly Dictionary<string, AppLanguage> ByCode =
        BuildCodeMap();

    public static IReadOnlyList<AppLanguageDefinition> All => PublicDefinitions;

    public static AppLanguageDefinition Get(AppLanguage language)
        => ByLanguage.TryGetValue(language, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(language), language, null);

    public static bool TryParse(string? value, out AppLanguage language)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            ByCode.TryGetValue(value.Trim(), out language))
        {
            return true;
        }

        language = default;
        return false;
    }

    public static bool TryResolveCulture(CultureInfo? culture, out AppLanguage language)
    {
        if (culture is null || string.IsNullOrWhiteSpace(culture.Name))
        {
            language = default;
            return false;
        }

        if (ByCode.TryGetValue(culture.Name, out language))
            return true;

        var languageCode = culture.TwoLetterISOLanguageName;
        switch (languageCode.ToLowerInvariant())
        {
            case "zh":
                language = IsTraditionalChinese(culture.Name)
                    ? AppLanguage.ChineseTraditional
                    : AppLanguage.ChineseSimplified;
                return true;
            case "es":
                language = culture.Name.Equals("es-ES", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.SpanishSpain
                    : AppLanguage.SpanishLatinAmerica;
                return true;
            case "pt":
                language = culture.Name.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.PortugueseBrazil
                    : AppLanguage.PortuguesePortugal;
                return true;
            case "no":
            case "nb":
            case "nn":
                language = AppLanguage.NorwegianBokmal;
                return true;
            default:
                return ByCode.TryGetValue(languageCode, out language);
        }
    }

    private static Dictionary<string, AppLanguage> BuildCodeMap()
    {
        var result = new Dictionary<string, AppLanguage>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Definitions)
        {
            result.Add(definition.Code, definition.Language);
            result.TryAdd(definition.EnglishName, definition.Language);
            result.TryAdd(definition.NativeName, definition.Language);
        }

        AddNeutral(result, "en", AppLanguage.English);
        AddNeutral(result, "ru", AppLanguage.Russian);
        AddNeutral(result, "zh", AppLanguage.ChineseSimplified);
        AddNeutral(result, "de", AppLanguage.German);
        AddNeutral(result, "fr", AppLanguage.French);
        AddNeutral(result, "es", AppLanguage.SpanishSpain);
        AddNeutral(result, "pt", AppLanguage.PortuguesePortugal);
        AddNeutral(result, "ja", AppLanguage.Japanese);
        AddNeutral(result, "ko", AppLanguage.Korean);
        AddNeutral(result, "pl", AppLanguage.Polish);
        AddNeutral(result, "tr", AppLanguage.Turkish);
        AddNeutral(result, "it", AppLanguage.Italian);
        AddNeutral(result, "th", AppLanguage.Thai);
        AddNeutral(result, "uk", AppLanguage.Ukrainian);
        AddNeutral(result, "cs", AppLanguage.Czech);
        AddNeutral(result, "nl", AppLanguage.Dutch);
        AddNeutral(result, "sv", AppLanguage.Swedish);
        AddNeutral(result, "da", AppLanguage.Danish);
        AddNeutral(result, "nb", AppLanguage.NorwegianBokmal);
        AddNeutral(result, "no", AppLanguage.NorwegianBokmal);
        AddNeutral(result, "fi", AppLanguage.Finnish);
        AddNeutral(result, "hu", AppLanguage.Hungarian);
        AddNeutral(result, "ro", AppLanguage.Romanian);
        AddNeutral(result, "el", AppLanguage.Greek);
        AddNeutral(result, "bg", AppLanguage.Bulgarian);
        AddNeutral(result, "id", AppLanguage.Indonesian);
        AddNeutral(result, "ms", AppLanguage.Malay);
        AddNeutral(result, "vi", AppLanguage.Vietnamese);

        result.TryAdd("english", AppLanguage.English);
        result.TryAdd("russian", AppLanguage.Russian);
        result.TryAdd("русский", AppLanguage.Russian);
        return result;
    }

    private static void AddNeutral(
        Dictionary<string, AppLanguage> target,
        string code,
        AppLanguage language)
        => target.Add(code, language);

    private static bool IsTraditionalChinese(string cultureName)
        => cultureName.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
           cultureName.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) ||
           cultureName.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
           cultureName.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);
}
