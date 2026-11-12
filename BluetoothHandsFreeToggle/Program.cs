using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Gui;
using BluetoothHandsFreeToggle.Localization;
using BluetoothHandsFreeToggle.Ui;

namespace BluetoothHandsFreeToggle;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);

        return options.Mode switch
        {
            LaunchMode.ElevatedWorker => ElevatedWorker.Run(options),
            LaunchMode.Console => RunConsole(),
            _ => RunGui(args, options)
        };
    }

    /// <summary>Used by the tray app, the XAML previewer and the headless UI tests.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<GuiApp>().UsePlatformDetect();

    private static int RunGui(string[] args, LaunchOptions options)
    {
        InitializeLanguage();

        // A second launch (Start menu, desktop shortcut) just opens the flyout of the running app.
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.SignalRunningInstance();
            return 0;
        }

        GuiApp.LaunchOptions = options;
        GuiApp.Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    private static int RunConsole()
    {
        // The app is a GUI executable, so the classic menu gets a console window of its own.
        _ = AllocConsole();

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            InitializeLanguage();
            Console.Title = AppInfo.AppDisplayName;

            var appInfo = AppInfo.Create();

            // Interactive mode asks for elevation once. If UAC is cancelled, status remains available.
            if (!appInfo.IsAdministrator)
            {
                var startedElevated = AdminHelper.TryRelaunchAsAdministrator(
                    appInfo.ExePath,
                    LaunchOptions.ConsoleArgument);
                if (startedElevated)
                    return 0;

                appInfo = AppInfo.Create();
            }

            new MenuLoop(appInfo, new ToggleEngine()).Run();
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleHelpers.WriteError(Text.Get("program.fatalError"));
            Console.Error.WriteLine(ex);

            if (!Console.IsInputRedirected && Environment.UserInteractive)
                ConsoleHelpers.Pause();

            return 1;
        }
    }

    private static void InitializeLanguage()
    {
        var languagePreferenceStore = new FileLanguagePreferenceStore();
        var startupLanguage = AppLanguageResolver.ResolveStartupLanguage(languagePreferenceStore);
        Text.Initialize(new LocalizationService(languagePreferenceStore, startupLanguage));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
