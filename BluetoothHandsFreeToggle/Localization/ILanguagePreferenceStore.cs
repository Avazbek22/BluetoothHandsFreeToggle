namespace BluetoothHandsFreeToggle.Localization;

public interface ILanguagePreferenceStore
{
    AppLanguage? Load();
    void Save(AppLanguage language);
}
