using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using StereoKeeper.App;
using StereoKeeper.Core;
using StereoKeeper.Gui.Services;
using StereoKeeper.Gui.Tray;
using StereoKeeper.Gui.ViewModels;
using StereoKeeper.Localization;

namespace StereoKeeper.Gui;

/// <summary>Wires the tray icon, the panel, the status monitor and the single-instance signal together.</summary>
public sealed class TrayController : IShell, IDisposable
{
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(350);

    private const int MenuOpen = 1;
    private const int MenuFix = 2;
    private const int MenuHard = 3;
    private const int MenuSettings = 4;
    private const int MenuHelp = 5;
    private const int MenuExit = 6;

    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly FlyoutViewModel _viewModel;
    private readonly FlyoutWindow _window;
    private readonly NotifyIcon _icon;
    private readonly StatusMonitor _monitor;
    private readonly DispatcherTimer _ticker;
    private bool _disposed;

    public TrayController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;

        var executablePath = Environment.ProcessPath ?? AppInfo.AppName + ".exe";
        var engine = new ToggleEngine();
        IOperationRunner runner = AdminHelper.IsAdministrator()
            ? new InProcessOperationRunner(engine)
            : new ElevatedOperationRunner(executablePath);

        var autostart = new RegistryAutostart(executablePath);
        autostart.RefreshPath();

        _viewModel = new FlyoutViewModel(runner, autostart, new FileGuiSettingsStore(), this, new EngineDiagnosticsSource(engine));
        _monitor = new StatusMonitor(new EngineStatusSource(engine), _viewModel.ApplyStatus);
        _window = new FlyoutWindow { DataContext = _viewModel };
        _window.PanelHidden += (_, _) =>
        {
            _monitor.PanelVisible = false;
            _viewModel.OnHidden();
        };

        _icon = new NotifyIcon();
        _icon.Activated += (_, anchor) => TogglePanel(anchor);
        _icon.ContextMenuRequested += (_, point) => ShowMenu(point);
        _icon.BalloonClicked += (_, _) => ShowPanel();
        _icon.ThemeChanged += (_, _) => UpdateIcon(_viewModel.TrayState);
        _icon.SetBalloonIcon(TrayIconRenderer.RenderNotificationPng(64), 64);

        _viewModel.TrayStateChanged += (_, state) => UpdateIcon(state);
        _viewModel.NotificationRequested += (_, notification) =>
        {
            if (!_window.IsShown)
                _icon.ShowNotification(notification.Title, notification.Body);
        };

        _ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => _viewModel.Tick());
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FlyoutViewModel.Mode) or "")
                _ticker.IsEnabled = _viewModel.Mode == FlyoutMode.Working;
        };

        _viewModel.StatusRefreshRequested += (_, _) => _monitor.RefreshNow();

        UpdateIcon(_viewModel.TrayState);
    }

    public void Start(bool showPanel, SingleInstance? instance)
    {
        _monitor.Start();
        instance?.Listen(() => Dispatcher.UIThread.Post(() => ShowPanel()));

        if (showPanel)
            ShowPanel();
    }

    public void ShowPanel(FlyoutPage page = FlyoutPage.Main)
    {
        // A second launch may still be signalled while the app is exiting.
        if (_disposed)
            return;

        if (page != FlyoutPage.Main)
            _viewModel.Page = page;

        _window.ShowPanel(_icon.GetIconRect());
        _monitor.PanelVisible = true;
        _monitor.RefreshNow();
    }

    private void TogglePanel(PixelRect? anchor)
    {
        if (_window.IsShown)
        {
            _window.HideAnimated();
            return;
        }

        // Clicking the icon while the panel is open first deactivates (hides) it; don't reopen.
        if (DateTime.UtcNow - _window.LastHiddenUtc < ReopenGuard)
            return;

        _window.ShowPanel(anchor);
        _monitor.PanelVisible = true;
        _monitor.RefreshNow();
    }

    private void ShowMenu(PixelPoint point)
    {
        var busy = _viewModel.IsBusy;
        var items = new List<TrayMenuItem>
        {
            new(MenuOpen, Text.Get("gui.tray.open"), IsDefault: true),
            TrayMenuItem.Separator,
            new(MenuFix, Text.Get("gui.action.fix"), Enabled: _viewModel.SoftResetCommand.CanExecute(null)),
            new(MenuHard, Text.Get("gui.hardCard.title"),
                Enabled: !busy && _viewModel.HardSwitchEnabled,
                Checked: _viewModel.Status.HardModeActive),
            TrayMenuItem.Separator,
            new(MenuSettings, Text.Get("gui.settings")),
            new(MenuHelp, Text.Get("gui.help")),
            TrayMenuItem.Separator,
            new(MenuExit, Text.Get("gui.tray.exit"))
        };

        switch (_icon.ShowMenu(items, point))
        {
            case MenuOpen:
                ShowPanel();
                break;
            case MenuFix:
                ShowPanel();
                _viewModel.SoftResetCommand.Execute(null);
                break;
            case MenuHard:
                ShowPanel();
                _viewModel.ToggleHardCommand.Execute(null);
                break;
            case MenuSettings:
                ShowPanel(FlyoutPage.Settings);
                break;
            case MenuHelp:
                _viewModel.ShowHelpCommand.Execute(null);
                ShowPanel();
                break;
            case MenuExit:
                Quit();
                break;
        }
    }

    private void UpdateIcon(TrayState state)
    {
        if (_disposed)
            return;

        try
        {
            var size = TrayIconRenderer.SmallIconSize;
            Color? dot = state.Dot is { } tone ? Palette.ToneColor(tone) : null;
            _icon.Update(TrayIconRenderer.RenderPng(size, dot), size, state.Tooltip);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Keep the previous icon; the next status change tries again.
            CrashLog.Write("Updating the tray icon failed", exception);
        }
    }

    // ----- IShell -----

    public void OpenUrl(string url) => ShellCommands.OpenUrl(url);

    public void OpenSoundRecordingSettings()
    {
        _window.HideAnimated();
        ShellCommands.OpenSoundRecordingSettings();
    }

    public async Task CopyTextAsync(string text)
    {
        if (_window.Clipboard is not { } clipboard)
            return;

        // Another app may hold the clipboard open for a moment; try a few times.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await clipboard.SetTextAsync(text);
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException && attempt < 3)
            {
                await Task.Delay(120);
            }
        }
    }

    public void Quit()
    {
        Dispose();
        _lifetime.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _ticker.Stop();
        _monitor.Dispose();
        _icon.Dispose();
        _window.Close();
    }
}
