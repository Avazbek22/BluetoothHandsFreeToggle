using System.Globalization;
using BluetoothHandsFreeToggle.Localization;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void LanguageCatalogContainsThirtyUniqueLanguagesAndCodes()
    {
        Assert.Equal(30, AppLanguageCatalog.All.Count);
        Assert.Equal(
            AppLanguageCatalog.All.Count,
            AppLanguageCatalog.All.Select(item => item.Language).Distinct().Count());
        Assert.Equal(
            AppLanguageCatalog.All.Count,
            AppLanguageCatalog.All.Select(item => item.Code).Distinct(
                StringComparer.OrdinalIgnoreCase).Count());

        foreach (var definition in AppLanguageCatalog.All)
        {
            Assert.Equal(definition.Code, definition.Culture.Name);
            Assert.True(AppLanguageCode.TryParse(definition.Code, out var parsed));
            Assert.Equal(definition.Language, parsed);
            Assert.Equal(definition.Code, AppLanguageCode.ToCode(parsed));
            Assert.Equal(
                definition.Language,
                AppLanguageResolver.DetectFromCultures(definition.Culture));
        }
    }

    [Theory]
    [InlineData("ru-RU", AppLanguage.Russian)]
    [InlineData("de-AT", AppLanguage.German)]
    [InlineData("es-ES", AppLanguage.SpanishSpain)]
    [InlineData("es-MX", AppLanguage.SpanishLatinAmerica)]
    [InlineData("pt-BR", AppLanguage.PortugueseBrazil)]
    [InlineData("pt-PT", AppLanguage.PortuguesePortugal)]
    [InlineData("zh-HK", AppLanguage.ChineseTraditional)]
    [InlineData("zh-SG", AppLanguage.ChineseSimplified)]
    [InlineData("nb-NO", AppLanguage.NorwegianBokmal)]
    public void DetectFromCulturesMapsSupportedRegionalCultures(
        string cultureName,
        AppLanguage expected)
    {
        var language = AppLanguageResolver.DetectFromCultures(
            new CultureInfo(cultureName));

        Assert.Equal(expected, language);
    }

    [Fact]
    public void DetectFromCulturesFallsBackToEnglish()
    {
        var language = AppLanguageResolver.DetectFromCultures(
            new CultureInfo("ar-SA"),
            new CultureInfo("he-IL"));

        Assert.Equal(AppLanguage.English, language);
    }

    [Fact]
    public void DetectFromCulturesUsesFirstSupportedWindowsPreference()
    {
        var language = AppLanguageResolver.DetectFromCultures(
            new CultureInfo("ar-SA"),
            new CultureInfo("pt-BR"),
            new CultureInfo("de-DE"));

        Assert.Equal(AppLanguage.PortugueseBrazil, language);
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
    public void SetLanguagePersistsAnyCatalogLanguage()
    {
        var store = new MemoryLanguagePreferenceStore();
        var service = new LocalizationService(store, AppLanguage.English);

        service.SetLanguage(AppLanguage.Japanese);

        Assert.Equal(AppLanguage.Japanese, service.CurrentLanguage);
        Assert.Equal(AppLanguage.Japanese, store.SavedLanguage);
    }

    [Fact]
    public void SetLanguageRejectsUnknownEnumValueWithoutChangingSelection()
    {
        var store = new MemoryLanguagePreferenceStore();
        var service = new LocalizationService(store, AppLanguage.English);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.SetLanguage((AppLanguage)999));
        Assert.Equal(AppLanguage.English, service.CurrentLanguage);
        Assert.Null(store.SavedLanguage);
    }

    private sealed class MemoryLanguagePreferenceStore : ILanguagePreferenceStore
    {
        public AppLanguage? SavedLanguage { get; private set; }

        public AppLanguage? Load() => SavedLanguage;

        public void Save(AppLanguage language)
            => SavedLanguage = language;
    }
}
