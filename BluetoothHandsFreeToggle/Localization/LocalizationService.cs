using System.Globalization;
using System.Text.Json;

namespace BluetoothHandsFreeToggle.Localization;

public sealed class LocalizationService
{
    private static readonly Lazy<
        IReadOnlyDictionary<AppLanguage, IReadOnlyDictionary<string, string>>> CachedResources =
        new(LoadResources);

    private readonly ILanguagePreferenceStore _preferenceStore;

    public LocalizationService(
        ILanguagePreferenceStore preferenceStore,
        AppLanguage initialLanguage)
    {
        _preferenceStore = preferenceStore
                           ?? throw new ArgumentNullException(nameof(preferenceStore));
        _ = AppLanguageCatalog.Get(initialLanguage);
        CurrentLanguage = initialLanguage;
    }

    public AppLanguage CurrentLanguage { get; private set; }

    public void SetLanguage(AppLanguage language)
    {
        _ = AppLanguageCatalog.Get(language);
        CurrentLanguage = language;
        _preferenceStore.Save(language);
    }

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var resources = CachedResources.Value;
        if (resources[CurrentLanguage].TryGetValue(key, out var localizedValue))
            return localizedValue;

        if (resources[AppLanguage.English].TryGetValue(key, out var fallbackValue))
            return fallbackValue;

        return $"[[{key}]]";
    }

    public string Format(string key, params object?[] arguments)
        => string.Format(
            AppLanguageCatalog.Get(CurrentLanguage).Culture,
            Get(key),
            arguments);

    private static Dictionary<
        AppLanguage,
        IReadOnlyDictionary<string, string>> LoadResources()
        => AppLanguageCatalog.All.ToDictionary(
            definition => definition.Language,
            definition => (IReadOnlyDictionary<string, string>)
                LoadLanguageResources(definition.ResourceCode));

    private static Dictionary<string, string> LoadLanguageResources(string languageCode)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(
                $".Localization.Resources.{languageCode}.json",
                StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new InvalidOperationException(
                $"Localization resource '{languageCode}.json' was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException(
                               $"Localization resource '{languageCode}.json' could not be opened.");

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
               ?? throw new InvalidOperationException(
                   $"Localization resource '{languageCode}.json' is invalid.");
    }
}
