using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Ui;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = AppInfo.AppName;

var appInfo = AppInfo.Create();
var cli = new CommandLine(args);

try
{
    // CLI mode (status/disable/enable and internal elevated child mode)
    if (cli.TryHandleNonInteractive(appInfo))
        return;

    // Interactive mode: ask for admin ONCE at startup.
    if (!appInfo.IsAdministrator)
    {
        var startedElevated = AdminHelper.TryRelaunchAsAdministrator(appInfo.ExePath, args);
        if (startedElevated)
            return;

        // User cancelled UAC -> continue in read-only mode (no more UAC popups).
        appInfo = AppInfo.Create(); // refresh just in case
    }

    var engine = new ToggleEngine();
    var menu = new MenuLoop(appInfo, engine);
    menu.Run();
}
catch (Exception ex)
{
    ConsoleHelpers.WriteError("FATAL ERROR");
    Console.WriteLine(ex);
    ConsoleHelpers.Pause();
}