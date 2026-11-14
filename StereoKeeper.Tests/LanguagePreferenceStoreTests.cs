using StereoKeeper.Localization;
using Xunit;

namespace StereoKeeper.Tests;

public sealed class LanguagePreferenceStoreTests
{
    [Fact]
    public void LoadReturnsNullWhenSettingsDoNotExist()
    {
        using var directory = new TemporaryDirectory();
        var store = new FileLanguagePreferenceStore(
            Path.Combine(directory.Path, "ui-settings.json"));

        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveAndLoadRoundTripUsesLanguageCode()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "ui-settings.json");
        var store = new FileLanguagePreferenceStore(settingsPath);

        store.Save(AppLanguage.PortugueseBrazil);

        Assert.Equal(AppLanguage.PortugueseBrazil, store.Load());
        Assert.Contains("\"pt-BR\"", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void TheLanguageSavedUnderTheOldNameIsKeptUntilTheNextSave()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "StereoKeeper", "ui-settings.json");
        var legacyPath = Path.Combine(directory.Path, "BluetoothHandsFreeToggle", "ui-settings.json");
        new FileLanguagePreferenceStore(legacyPath).Save(AppLanguage.German);
        var store = new FileLanguagePreferenceStore(settingsPath, legacyPath);

        Assert.Equal(AppLanguage.German, store.Load());

        store.Save(AppLanguage.Russian);

        Assert.Equal(AppLanguage.Russian, store.Load());
        Assert.True(File.Exists(settingsPath));
    }

    [Fact]
    public void InvalidSettingsDoNotBreakStartup()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "ui-settings.json");
        File.WriteAllText(settingsPath, "{ not-json");

        var language = new FileLanguagePreferenceStore(settingsPath).Load();

        Assert.Null(language);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory(
                "StereoKeeper.Localization.").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
