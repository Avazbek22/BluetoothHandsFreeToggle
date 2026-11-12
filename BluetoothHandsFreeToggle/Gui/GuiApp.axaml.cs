using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BluetoothHandsFreeToggle.App;

namespace BluetoothHandsFreeToggle.Gui;

public sealed partial class GuiApp : Application
{
    /// <summary>Set by <c>Program</c> before the app starts.</summary>
    internal static LaunchOptions LaunchOptions { get; set; } = new(LaunchMode.Gui);

    internal static SingleInstance? Instance { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The headless UI tests run without a lifetime: they create the panel themselves.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A tray app has no window to show an error in; one failed click or binding must not
            // take the icon away. The error is logged and the app keeps running.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                CrashLog.Write("Unhandled UI exception", e.Exception);
                e.Handled = true;
            };

            var controller = new TrayController(desktop);
            desktop.Exit += (_, _) => controller.Dispose();
            controller.Start(showPanel: !LaunchOptions.StartHidden, Instance);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
