using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Gui;
using BluetoothHandsFreeToggle.Gui.Services;
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
        try
        {
            var engine = new ToggleEngine();
            IOperationRunner runner = AdminHelper.IsAdministrator()
                ? new InProcessOperationRunner(engine)
                : new ElevatedOperationRunner(Environment.ProcessPath ?? AppInfo.AppName + ".exe");

            new StartupRecovery(new MessageBoxDialog(), runner, engine.GetServiceSnapshots)
                .Report(exception, CrashLog.FilePath);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            CrashLog.Write("The startup error could not be explained", failure);
            _ = MessageBoxW(
                IntPtr.Zero,
                $"{exception.Message}\n\n{CrashLog.FilePath}",
                "HandsFree Toggle could not start",
                MbOk | MbIconError | MbSetForeground);
        }
    }

    private static void InitializeLanguage()
    {
        var languagePreferenceStore = new FileLanguagePreferenceStore();
        var startupLanguage = AppLanguageResolver.ResolveStartupLanguage(languagePreferenceStore);
        Text.Initialize(new LocalizationService(languagePreferenceStore, startupLanguage));
    }

    private const uint MbOk = 0x0;
    private const uint MbYesNo = 0x4;
    private const uint MbIconError = 0x10;
    private const uint MbIconWarning = 0x30;
    private const uint MbIconInformation = 0x40;
    private const uint MbSetForeground = 0x10000;
    private const int IdYes = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    private sealed class MessageBoxDialog : IStartupDialog
    {
        public void Show(string title, string body, StartupDialogIcon icon)
            => _ = MessageBoxW(IntPtr.Zero, body, title, MbOk | MbSetForeground | icon switch
            {
                StartupDialogIcon.Warning => MbIconWarning,
                StartupDialogIcon.Information => MbIconInformation,
                _ => MbIconError
            });

        public bool Ask(string title, string body)
            => MessageBoxW(IntPtr.Zero, body, title, MbYesNo | MbIconWarning | MbSetForeground) == IdYes;
    }
}
