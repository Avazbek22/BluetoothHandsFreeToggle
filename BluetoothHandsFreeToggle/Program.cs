using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Ui;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = "BluetoothHandsFreeToggle";

var appInfo = AppInfo.Create();
var cli = new CommandLine(args);

try
{
    // Headless commands (status/disable/enable), including elevated child mode with IPC.
    if (cli.TryHandleNonInteractive(appInfo))
        return;

    // Interactive menu
    var engine = new ToggleEngine();
    var menu = new MenuLoop(appInfo, engine);
    menu.Run();
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("Fatal error:");
    Console.WriteLine(ex);
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(true);
}