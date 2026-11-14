using System.Text.Json;
using StereoKeeper.App;

namespace StereoKeeper.Localization;

public sealed class FileLanguagePreferenceStore(string? settingsFilePath = null, string? legacySettingsFilePath = null)
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

    // Versions up to 2.2 kept the language under the old name; it is read until the first save.
    private readonly string? _legacySettingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.LegacyName,
            "ui-settings.json")
        : string.IsNullOrWhiteSpace(legacySettingsFilePath) ? null : Path.GetFullPath(legacySettingsFilePath);

    public AppLanguage? Load()
        => Load(_settingsFilePath) ?? (_legacySettingsFilePath is null ? null : Load(_legacySettingsFilePath));

    private static AppLanguage? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var settings = JsonSerializer.Deserialize<LanguageSettings>(
                File.ReadAllText(path));

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
