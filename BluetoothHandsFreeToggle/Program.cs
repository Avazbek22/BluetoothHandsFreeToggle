using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Gui;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);

        return options.Mode == LaunchMode.ElevatedWorker
            ? ElevatedWorker.Run(options)
            : RunGui(args, options);
    }

    /// <summary>Used by the tray app, the XAML previewer and the headless UI tests.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<GuiApp>().UsePlatformDetect();

    private static int RunGui(string[] args, LaunchOptions options)
    {
        InstallCrashLogging();
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

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // No graphics, no tray (a locked-down or broken system): say so instead of vanishing.
            CrashLog.Write("The tray app could not run", exception);
            ShowStartupError(exception);
            return 1;
        }
    }

    private static void InstallCrashLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("Unhandled exception, the app stops", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    private static void ShowStartupError(Exception exception)
    {
        string title;
        string body;
        try
        {
            title = Text.Get("gui.error.startup.title");
            body = Text.Format("gui.error.startup.body", exception.Message, CrashLog.FilePath);
        }
        catch (Exception textFailure) when (textFailure is not OutOfMemoryException)
        {
            title = "HandsFree Toggle could not start";
            body = $"{exception.Message}\n\n{CrashLog.FilePath}";
        }

        _ = MessageBoxW(IntPtr.Zero, body, title, MbOk | MbIconError | MbSetForeground);
    }

    private static void InitializeLanguage()
    {
        var languagePreferenceStore = new FileLanguagePreferenceStore();
        var startupLanguage = AppLanguageResolver.ResolveStartupLanguage(languagePreferenceStore);
        Text.Initialize(new LocalizationService(languagePreferenceStore, startupLanguage));
    }

    private const uint MbOk = 0x0;
    private const uint MbIconError = 0x10;
    private const uint MbSetForeground = 0x10000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
