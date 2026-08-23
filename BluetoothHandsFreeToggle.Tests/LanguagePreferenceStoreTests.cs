using BluetoothHandsFreeToggle.Localization;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

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
                "BluetoothHandsFreeToggle.Localization.").FullName;
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
