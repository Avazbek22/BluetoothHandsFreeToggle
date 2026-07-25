using System.Text;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;
using BluetoothHandsFreeToggle.Ui;

try
{
    Console.OutputEncoding = Encoding.UTF8;

    var languagePreferenceStore = new FileLanguagePreferenceStore();
    var startupLanguage = AppLanguageResolver.ResolveStartupLanguage(languagePreferenceStore);
    Text.Initialize(new LocalizationService(languagePreferenceStore, startupLanguage));

    Console.Title = AppInfo.AppName;

    var appInfo = AppInfo.Create();

    // Interactive mode asks for elevation once. If UAC is cancelled, status remains available.
    if (!appInfo.IsAdministrator)
    {
        var startedElevated = AdminHelper.TryRelaunchAsAdministrator(appInfo.ExePath);
        if (startedElevated)
            return;

        appInfo = AppInfo.Create();
    }

    new MenuLoop(appInfo, new ToggleEngine()).Run();
}
catch (Exception ex)
{
    Environment.ExitCode = 1;
    ConsoleHelpers.WriteError(Text.Get("program.fatalError"));
    Console.Error.WriteLine(ex);

    if (!Console.IsInputRedirected && Environment.UserInteractive)
        ConsoleHelpers.Pause();
}
