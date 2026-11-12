using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using BluetoothHandsFreeToggle.Gui.ViewModels;

namespace BluetoothHandsFreeToggle.Gui;

/// <summary>
/// The tray panel: a borderless, always-on-top window that slides up next to the notification
/// area like the Windows 11 system flyouts and closes when it loses focus.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    public const double PanelWidth = 360;
    public const double PanelHeight = 760;
    private const double ShadowMargin = 20;
    private const double ScreenGap = 12;

    private static readonly Easing EnterEasing = new SplineEasing(0.1, 0.9, 0.2, 1);

    private FlyoutViewModel? _viewModel;
    private bool _hiding;
    private int _showVersion;

    public FlyoutWindow()
    {
        InitializeComponent();

        Deactivated += (_, _) =>
        {
            // The UAC prompt takes the focus while the panel waits for permission: stay open.
            if (_viewModel?.Mode is FlyoutMode.AwaitingPermission)
                return;

            HideAnimated();
        };

        Root.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, Root))
                HideAnimated();
        };
    }

    /// <summary>When the panel was last hidden, so a tray click that caused the hide does not reopen it.</summary>
    public DateTime LastHiddenUtc { get; private set; } = DateTime.MinValue;

    public bool IsShown => IsVisible && !_hiding;

    public event EventHandler? PanelHidden;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as FlyoutViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        NativeWindowStyle.MakeToolWindow(this);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || e.Handled)
            return;

        e.Handled = true;
        if (_viewModel is { IsMainPage: false })
            _viewModel.BackCommand.Execute(null);
        else
            HideAnimated();
    }

    /// <summary>Shows the panel above the tray icon (or the notification area of the primary screen).</summary>
    public void ShowPanel(PixelRect? anchor)
    {
        var version = ++_showVersion;
        _hiding = false;
        PlaceNear(anchor);

        if (!IsVisible)
        {
            Card.Opacity = 0;
            Show();
        }

        Activate();
        _ = AnimateCardAsync(fromOpacity: 0, toOpacity: 1, fromOffset: 24, toOffset: 0, 260, EnterEasing, version);
    }

    public async void HideAnimated()
    {
        if (!IsVisible || _hiding)
            return;

        _hiding = true;
        var version = ++_showVersion;
        LastHiddenUtc = DateTime.UtcNow;
        await AnimateCardAsync(Card.Opacity, 0, 0, 12, 140, new CubicEaseIn(), version);

        if (version != _showVersion)
            return;

        Hide();
        _hiding = false;
        LastHiddenUtc = DateTime.UtcNow;
        PanelHidden?.Invoke(this, EventArgs.Empty);
    }

    private async Task AnimateCardAsync(
        double fromOpacity,
        double toOpacity,
        double fromOffset,
        double toOffset,
        int milliseconds,
        Easing easing,
        int version)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(OpacityProperty, fromOpacity),
                        new Setter(TranslateTransform.YProperty, fromOffset)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(OpacityProperty, toOpacity),
                        new Setter(TranslateTransform.YProperty, toOffset)
                    }
                }
            }
        };

        await animation.RunAsync(Card);
        if (version == _showVersion)
            Card.Opacity = toOpacity;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FlyoutViewModel.Page) || _viewModel is null)
            return;

        Control page = _viewModel.Page switch
        {
            FlyoutPage.Settings => SettingsPage,
            FlyoutPage.Help => HelpPage,
            _ => MainPage
        };

        // Forward navigation slides in from the right, going back slides in from the left.
        var offset = _viewModel.Page == FlyoutPage.Main ? -16 : 16;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, (double)offset) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 0d) }
                }
            }
        };
        _ = animation.RunAsync(page);
    }

    private void PlaceNear(PixelRect? anchor)
    {
        var screen = anchor is { } rect
            ? Screens.ScreenFromPoint(rect.Center) ?? Screens.Primary
            : Screens.Primary;
        if (screen is null)
            return;

        var scaling = screen.Scaling;
        var area = screen.WorkingArea;
        var bounds = screen.Bounds;

        var panelHeight = Math.Min(PanelHeight, (area.Height / scaling) - (2 * ScreenGap));
        Width = PanelWidth + (2 * ShadowMargin);
        Height = panelHeight + (2 * ShadowMargin);

        var right = (int)Math.Round((ScreenGap + PanelWidth + ShadowMargin) * scaling);
        var down = (int)Math.Round((ScreenGap + panelHeight + ShadowMargin) * scaling);
        var inset = (int)Math.Round((ScreenGap - ShadowMargin) * scaling);

        var taskbarOnLeft = area.X > bounds.X;
        var taskbarOnTop = area.Y > bounds.Y && area.Bottom >= bounds.Bottom;

        var x = taskbarOnLeft ? area.X + inset : area.Right - right;
        var y = taskbarOnTop ? area.Y + inset : area.Bottom - down;
        Position = new PixelPoint(x, y);
    }

    private static class NativeWindowStyle
    {
        private const int GwlExStyle = -20;
        private const long WsExToolWindow = 0x00000080;
        private const long WsExAppWindow = 0x00040000;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpDoNotRound = 1;

        /// <summary>Keeps the panel out of Alt+Tab and stops Windows 11 from rounding the transparent frame.</summary>
        public static void MakeToolWindow(TopLevel window)
        {
            if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { } handle)
                return;

            var hwnd = handle.Handle;
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            style = (style | WsExToolWindow) & ~WsExAppWindow;
            _ = SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));

            var preference = DwmwcpDoNotRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("dwmapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
