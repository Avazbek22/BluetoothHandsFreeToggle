using System.Globalization;
using BluetoothHandsFreeToggle.Localization;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void DetectFromCulturesReturnsRussianWhenCultureChainContainsRussian()
    {
        var language = AppLanguageResolver.DetectFromCultures(
            new CultureInfo("en-US"),
            new CultureInfo("ru-RU"));

        Assert.Equal(AppLanguage.Russian, language);
    }

    [Fact]
    public void DetectFromCulturesFallsBackToEnglish()
    {
        var language = AppLanguageResolver.DetectFromCultures(
            new CultureInfo("de-DE"),
            new CultureInfo("fr-FR"));

        Assert.Equal(AppLanguage.English, language);
    }

    [Fact]
    public void GetReturnsSelectedLanguageAndMarksMissingKeys()
    {
        var store = new MemoryLanguagePreferenceStore();
        var service = new LocalizationService(store, AppLanguage.Russian);

        Assert.Equal("Сменить язык", service.Get("menu.changeLanguage"));
        Assert.Equal("[[missing.key]]", service.Get("missing.key"));

        service.SetLanguage(AppLanguage.English);

        Assert.Equal("Change language", service.Get("menu.changeLanguage"));
        Assert.Equal(AppLanguage.English, store.SavedLanguage);
    }

    [Fact]
    public void ToggleLanguagePersistsSelection()
    {
        var store = new MemoryLanguagePreferenceStore();
        var service = new LocalizationService(store, AppLanguage.English);

        var selected = service.ToggleLanguage();

        Assert.Equal(AppLanguage.Russian, selected);
        Assert.Equal(AppLanguage.Russian, store.SavedLanguage);
    }

    private sealed class MemoryLanguagePreferenceStore : ILanguagePreferenceStore
    {
        public AppLanguage? SavedLanguage { get; private set; }

        public AppLanguage? Load() => SavedLanguage;

        public void Save(AppLanguage language)
            => SavedLanguage = language;
    }
}
