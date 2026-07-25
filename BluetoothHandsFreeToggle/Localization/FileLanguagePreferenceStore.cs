using System.Text.Json;
using BluetoothHandsFreeToggle.App;

namespace BluetoothHandsFreeToggle.Localization;

public sealed class FileLanguagePreferenceStore(string? settingsFilePath = null)
    : ILanguagePreferenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName,
            "ui-settings.json")
        : Path.GetFullPath(settingsFilePath);

    public AppLanguage? Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
                return null;

            var settings = JsonSerializer.Deserialize<LanguageSettings>(
                File.ReadAllText(_settingsFilePath));

            return AppLanguageCode.TryParse(settings?.Language, out var language)
                ? language
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(AppLanguage language)
    {
        string? temporaryPath = null;

        try
        {
            var directoryPath = Path.GetDirectoryName(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(directoryPath))
                return;

            Directory.CreateDirectory(directoryPath);

            var settings = new LanguageSettings(AppLanguageCode.ToCode(language));
            var json = JsonSerializer.Serialize(settings, SerializerOptions);

            temporaryPath = Path.Combine(
                directoryPath,
                $".ui-settings.{Guid.NewGuid():N}.tmp");

            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsFilePath, overwrite: true);
            temporaryPath = null;
        }
        catch
        {
            // Language persistence is best-effort and must not block recovery operations.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup of an incomplete settings file.
                }
            }
        }
    }

    private sealed record LanguageSettings(string Language);
}
