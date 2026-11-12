using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Gui.Controls;
using BluetoothHandsFreeToggle.Gui.Model;
using BluetoothHandsFreeToggle.Gui.Services;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Gui.ViewModels;

public enum FlyoutPage
{
    Main,
    Settings,
    Help
}

/// <summary>What the panel shows on top of the live status.</summary>
public enum FlyoutMode
{
    Live,
    AwaitingPermission,
    Working,
    Done,
    RebootRequired,
    Failed
}

public enum ContextKind
{
    Steps,
    MicFree,
    MicBusy,
    MicOff,
    MicNone,
    MicUnknown,
    Info
}

public sealed record AppItem(string Name)
{
    public string Initial { get; } = string.IsNullOrEmpty(Name)
        ? "?"
        : StringInfo.GetNextTextElement(Name).ToUpper(CultureInfo.CurrentCulture);
}

public sealed record LanguageOption(AppLanguage Language, string NativeName, bool IsCurrent);

/// <summary>Tray icon look: the colour of the status dot and the tooltip text.</summary>
public sealed record TrayState(Tone? Dot, string Tooltip);

public sealed class FlyoutViewModel : ObservableObject
{
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(6);

    /// <summary>The same notification is not repeated sooner (an app opening and closing the microphone).</summary>
    private static readonly TimeSpan RepeatNotificationAfter = TimeSpan.FromMinutes(1);

    private readonly IOperationRunner _runner;
    private readonly IAutostart _autostart;
    private readonly IGuiSettingsStore _settingsStore;
    private readonly IShell _shell;
    private readonly GuiSettings _settings;
    private readonly SynchronizationContext? _ui;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private UiText _text = new();
    private LiveStatus _status = LiveStatus.Loading;
    private FlyoutPage _page;
    private FlyoutPage _helpReturnPage;
    private FlyoutMode _mode;
    private OperationSteps? _steps;
    private ToggleReport? _report;
    private string? _failure;
    private TimeSpan _operationStart;
    private TimeSpan _operationElapsed;
    private string? _notice;
    private long _noticeVersion;
    private bool _copied;
    private (string Title, string Body, TimeSpan At)? _lastNotification;
    private Func<Task>? _primaryAction;
    private bool _autostartEnabled;
    private IReadOnlyList<LanguageOption> _languages = [];

    public FlyoutViewModel(
        IOperationRunner runner,
        IAutostart autostart,
        IGuiSettingsStore settingsStore,
        IShell shell)
    {
        _runner = runner;
        _autostart = autostart;
        _settingsStore = settingsStore;
        _shell = shell;
        _settings = settingsStore.Load();
        _ui = SynchronizationContext.Current;
        _autostartEnabled = autostart.IsEnabled;
        _languages = BuildLanguages();

        PrimaryCommand = new RelayCommand(() => _ = _primaryAction?.Invoke());
        ToggleHardCommand = new RelayCommand(() => _ = ToggleHardAsync(), () => HardSwitchEnabled);
        SoftResetCommand = new RelayCommand(() => _ = RunAsync(OperationKind.Soft), () => !IsBusy && _status.HasServices);
        ShowSettingsCommand = new RelayCommand(() => Page = FlyoutPage.Settings);
        ShowHelpCommand = new RelayCommand(() =>
        {
            _helpReturnPage = Page == FlyoutPage.Help ? _helpReturnPage : Page;
            Page = FlyoutPage.Help;
        });
        BackCommand = new RelayCommand(() => Page = Page == FlyoutPage.Help ? _helpReturnPage : FlyoutPage.Main);
        OpenSupportCommand = new RelayCommand(() => _shell.OpenUrl(AppInfo.SupportUrl));
        OpenSourceCommand = new RelayCommand(() => _shell.OpenUrl(GitHubUrl));
        OpenSoundSettingsCommand = new RelayCommand(_shell.OpenSoundRecordingSettings);
        CopyReportCommand = new RelayCommand(() => _ = CopyReportAsync());
        QuitCommand = new RelayCommand(_shell.Quit);
        SelectLanguageCommand = new RelayCommand(parameter =>
        {
            if (parameter is LanguageOption option)
                SetLanguage(option.Language);
        });

        Refresh();
    }

    public const string GitHubUrl = "https://github.com/Avazbek22/BluetoothHandsFreeToggle";

    /// <summary>Raised on the UI thread when the status warrants a Windows notification.</summary>
    public event EventHandler<(string Title, string Body)>? NotificationRequested;

    /// <summary>Raised on the UI thread whenever the tray icon or its tooltip should change.</summary>
    public event EventHandler<TrayState>? TrayStateChanged;

    /// <summary>Raised after the user picked another language.</summary>
    public event EventHandler? LanguageChanged;

    public RelayCommand PrimaryCommand { get; }
    public RelayCommand ToggleHardCommand { get; }
    public RelayCommand SoftResetCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand ShowHelpCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand OpenSupportCommand { get; }
    public RelayCommand OpenSourceCommand { get; }
    public RelayCommand OpenSoundSettingsCommand { get; }
    public RelayCommand CopyReportCommand { get; }
    public RelayCommand QuitCommand { get; }
    public RelayCommand SelectLanguageCommand { get; }

    public UiText L
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public LiveStatus Status => _status;

    public FlyoutMode Mode
    {
        get => _mode;
        private set
        {
            if (SetProperty(ref _mode, value))
            {
                ToggleHardCommand.NotifyCanExecuteChanged();
                SoftResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy => Mode is FlyoutMode.AwaitingPermission or FlyoutMode.Working;

    public FlyoutPage Page
    {
        get => _page;
        set
        {
            if (SetProperty(ref _page, value))
            {
                OnPropertyChanged(nameof(IsMainPage));
                OnPropertyChanged(nameof(IsSettingsPage));
                OnPropertyChanged(nameof(IsHelpPage));
            }
        }
    }

    public bool IsMainPage => Page == FlyoutPage.Main;
    public bool IsSettingsPage => Page == FlyoutPage.Settings;
    public bool IsHelpPage => Page == FlyoutPage.Help;

    // ----- Device card -----
    public bool HasDevice { get; private set; }
    public string DeviceTitle { get; private set; } = "";
    public string DeviceSubtitle { get; private set; } = "";
    public bool HasChip { get; private set; }
    public string ChipText { get; private set; } = "";
    public IBrush ChipForeground { get; private set; } = Palette.Foreground(Tone.Muted);
    public IBrush ChipBackground { get; private set; } = Palette.ChipBackground(Tone.Muted);

    // ----- Ring -----
    public double RingValue { get; private set; }
    public IBrush RingBrush { get; private set; } = Palette.Ring(Tone.Muted);
    public IBrush RingIconBrush { get; private set; } = Palette.Foreground(Tone.Muted);
    public Geometry? RingIcon { get; private set; }
    public bool HasRingIcon => RingIcon is not null;
    public string CenterText { get; private set; } = "";
    public double CenterFontSize { get; private set; } = 20;
    public string CenterSubtitle { get; private set; } = "";

    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";

    // ----- Primary button -----
    public bool HasPrimary { get; private set; }
    public string PrimaryText { get; private set; } = "";
    public bool PrimaryIsMain { get; private set; }
    public bool PrimaryShowsShield { get; private set; }

    public bool HasNotice => _notice is not null;
    public string Notice => _notice ?? "";

    // ----- Hard mode switch -----
    public bool HardSwitchOn { get; private set; }
    public bool HardSwitchEnabled => !IsBusy && _status.HasServices && _status.Phase is not HeadsetPhase.Unavailable;

    // ----- Context card -----
    public ContextKind Context { get; private set; }
    public bool IsStepsContext => Context is ContextKind.Steps;
    public bool IsMicContext => Context is not (ContextKind.Steps or ContextKind.Info);
    public bool IsInfoContext => Context is ContextKind.Info;
    public ObservableCollection<StepItem> Steps { get; } = [];

    public Geometry MicIcon { get; private set; } = Icons.Mic;
    public IBrush MicIconBrush { get; private set; } = Palette.Foreground(Tone.Light);
    public string MicStatusText { get; private set; } = "";
    public IBrush MicStatusBrush { get; private set; } = Palette.Foreground(Tone.Muted);
    public string MicDescription { get; private set; } = "";
    public bool HasMicDescription => MicDescription.Length > 0;
    public IReadOnlyList<AppItem> MicApps { get; private set; } = [];
    public bool HasMicApps => MicApps.Count > 0;
    public bool ShowDefaultMicrophone { get; private set; }

    public string InfoTitle { get; private set; } = "";
    public IBrush InfoTitleBrush { get; private set; } = Palette.Foreground(Tone.Amber);
    public IReadOnlyList<string> InfoLines { get; private set; } = [];
    public bool CanCopyReport { get; private set; }
    public string CopyReportText => _copied ? Text.Get("gui.copied") : L.CopyReport;

    // ----- Settings -----
    public bool AutostartEnabled
    {
        get => _autostartEnabled;
        set
        {
            if (value != _autostartEnabled)
                _ = _autostart.TrySetEnabled(value);

            // Re-read: the registry is the truth (for example, a policy may block the write).
            _autostartEnabled = _autostart.IsEnabled;
            OnPropertyChanged();
        }
    }

    public bool NotifyCallMode
    {
        get => _settings.NotifyCallMode;
        set
        {
            if (_settings.NotifyCallMode == value)
                return;

            _settings.NotifyCallMode = value;
            _settingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    public bool NotifyMicrophone
    {
        get => _settings.NotifyMicrophone;
        set
        {
            if (_settings.NotifyMicrophone == value)
                return;

            _settings.NotifyMicrophone = value;
            _settingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    public string CurrentLanguageName { get; private set; } = Text.CurrentLanguageDefinition.NativeName;

    public IReadOnlyList<LanguageOption> Languages => _languages;

    private static List<LanguageOption> BuildLanguages() => AppLanguageCatalog.All
        .Select(definition => new LanguageOption(
            definition.Language,
            definition.NativeName,
            definition.Language == Text.CurrentLanguage))
        .ToList();

    public TrayState TrayState { get; private set; } = new(null, "HandsFree Toggle");

    /// <summary>Applies a fresh status check. Call on the UI thread.</summary>
    public void ApplyStatus(LiveStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        // Most checks bring no news: leave the UI alone instead of re-binding every few seconds.
        if (status.IsEquivalentTo(_status))
        {
            _status = status;
            return;
        }

        var previous = _status;
        _status = status;
        OnPropertyChanged(nameof(Status));
        ToggleHardCommand.NotifyCanExecuteChanged();
        SoftResetCommand.NotifyCanExecuteChanged();

        if (Mode == FlyoutMode.Live)
            RaiseNotifications(previous, status);

        Refresh();
    }

    /// <summary>The panel was hidden: a finished result has been seen, show live status next time.</summary>
    public void OnHidden()
    {
        _autostartEnabled = _autostart.IsEnabled;

        if (Mode == FlyoutMode.Done)
            Mode = FlyoutMode.Live;

        // Like the system flyouts, the panel always reopens on its main page.
        Page = FlyoutPage.Main;

        Refresh();
    }

    /// <summary>Advances the working state (elapsed time, ring). Called by a UI timer while busy.</summary>
    public void Tick()
    {
        if (Mode == FlyoutMode.Working)
            Refresh();
    }

    public Task RunAsync(OperationKind operation) => RunOperationAsync(operation);

    public void SetLanguage(AppLanguage language)
    {
        if (language == Text.CurrentLanguage)
            return;

        Text.SetLanguage(language);
        _languages = BuildLanguages();
        CurrentLanguageName = Text.CurrentLanguageDefinition.NativeName;
        L = new UiText();
        OnPropertyChanged(nameof(CurrentLanguageName));
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(CopyReportText));
        Refresh();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task ToggleHardAsync()
        => RunOperationAsync(_status.HardModeActive ? OperationKind.Restore : OperationKind.Hard);

    private async Task RunOperationAsync(OperationKind operation)
    {
        if (IsBusy)
            return;

        _steps = new OperationSteps(operation, _status.Services);
        _report = null;
        _failure = null;
        _copied = false;
        ClearNotice();
        Steps.Clear();
        foreach (var item in _steps.Items)
            Steps.Add(item);

        _steps.Items.CollectionChanged += OnStepsChanged;
        Mode = FlyoutMode.AwaitingPermission;
        Refresh();

        var steps = _steps;
        OperationOutcome outcome;
        try
        {
            outcome = await _runner.RunAsync(
                operation,
                operationEvent => Post(() =>
                {
                    steps.Apply(operationEvent, _clock.Elapsed);
                    Refresh();
                }),
                () => Post(() =>
                {
                    _operationStart = _clock.Elapsed;
                    steps.Begin(_clock.Elapsed);
                    Mode = FlyoutMode.Working;
                    Refresh();
                })).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            outcome = OperationOutcome.Failed(exception.Message);
        }

        // Progress callbacks are posted; let the ones already queued run before the result.
        await Task.Yield();

        steps.Items.CollectionChanged -= OnStepsChanged;
        _operationElapsed = Mode == FlyoutMode.Working ? _clock.Elapsed - _operationStart : TimeSpan.Zero;

        switch (outcome.Kind)
        {
            case OperationOutcomeKind.Cancelled:
                Mode = FlyoutMode.Live;
                ShowNotice(Text.Get("gui.uacCancelled"));
                break;

            case OperationOutcomeKind.Failed:
                _failure = outcome.Error;
                Mode = FlyoutMode.Failed;
                break;

            default:
                steps.Complete(_clock.Elapsed);
                _report = outcome.Report;
                Mode = _report!.RebootRequired ? FlyoutMode.RebootRequired
                    : !_report.Success ? FlyoutMode.Failed
                    : operation == OperationKind.Hard ? FlyoutMode.Live
                    : FlyoutMode.Done;
                break;
        }

        Refresh();
        StatusRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the live status should be checked right away (after an operation).</summary>
    public event EventHandler? StatusRefreshRequested;

    private void OnStepsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_steps is null)
            return;

        Steps.Clear();
        foreach (var item in _steps.Items)
            Steps.Add(item);
    }

    private Task DismissResultAsync()
    {
        Mode = FlyoutMode.Live;
        _report = null;
        _failure = null;
        Refresh();
        return Task.CompletedTask;
    }

    private async Task CopyReportAsync()
    {
        var text = _report?.ToPrettyText() ?? _failure;
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            await _shell.CopyTextAsync(text).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The clipboard stayed locked by another app; the button simply does not confirm.
            CrashLog.Write("Copying the report failed", exception);
            return;
        }

        _copied = true;
        OnPropertyChanged(nameof(CopyReportText));
    }

    private void Notify(string title, string body)
    {
        var now = _clock.Elapsed;
        if (_lastNotification is { } last &&
            last.Title == title &&
            last.Body == body &&
            now - last.At < RepeatNotificationAfter)
        {
            return;
        }

        _lastNotification = (title, body, now);
        NotificationRequested?.Invoke(this, (title, body));
    }

    private void ShowNotice(string text)
    {
        _notice = text;
        var version = ++_noticeVersion;
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(Notice));

        _ = Task.Delay(NoticeDuration).ContinueWith(
            _ => Post(() =>
            {
                if (_noticeVersion == version)
                    ClearNotice();
            }),
            TaskScheduler.Default);
    }

    private void ClearNotice()
    {
        if (_notice is null)
            return;

        _notice = null;
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(Notice));
    }

    private void Post(Action action)
    {
        if (_ui is null)
            action();
        else
            _ui.Post(_ => action(), null);
    }

    private void RaiseNotifications(LiveStatus previous, LiveStatus current)
    {
        var apps = AppNames(current);

        if (current.Phase == HeadsetPhase.CallMode &&
            previous.Phase is HeadsetPhase.Stereo or HeadsetPhase.NoHeadset)
        {
            if (_settings.NotifyCallMode)
            {
                Notify(Text.Get("gui.notify.call.title"),
                    _settings.NotifyMicrophone && apps.Count > 0
                        ? FormatApps("gui.notify.call.bodyApps", "gui.notify.call.bodyApps.many", apps)
                        : Text.Get("gui.notify.call.body"));
            }
            else if (_settings.NotifyMicrophone && apps.Count > 0)
            {
                Notify(Text.Get("gui.notify.mic.title"),
                    FormatApps("gui.notify.mic.body", "gui.notify.mic.body.many", apps));
            }

            return;
        }

        if (current.Phase == HeadsetPhase.CallMode &&
            previous.Phase == HeadsetPhase.CallMode &&
            _settings.NotifyMicrophone)
        {
            var before = previous.Microphone?.BluetoothApps.Select(app => app.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var added = current.Microphone!.BluetoothApps
                .Select(app => app.Name)
                .Where(name => !before.Contains(name))
                .ToList();
            if (added.Count > 0)
            {
                Notify(Text.Get("gui.notify.mic.title"),
                    FormatApps("gui.notify.mic.body", "gui.notify.mic.body.many", added));
            }
        }
    }

    private static List<string> AppNames(LiveStatus status)
        => status.Microphone is { } microphone ? AppNames(microphone.BluetoothApps) : [];

    private static List<string> AppNames(IEnumerable<MicrophoneApp> apps)
        => apps.Select(app => app.Name).ToList();

    /// <summary>
    /// Fills a sentence about apps, picking the singular or plural wording so that the verb agrees
    /// ("Discord is using...", "Discord and Zoom are using...").
    /// </summary>
    private static string FormatApps(string singleKey, string manyKey, List<string> apps)
    {
        if (apps.Count <= 1)
            return Text.Format(singleKey, apps.Count == 1 ? apps[0] : "");

        var list = Text.Format("gui.list.last", string.Join(", ", apps.Take(apps.Count - 1)), apps[^1]);
        return Text.Format(manyKey, list);
    }

    /// <summary>Recomputes every derived property from the status and the mode.</summary>
    private void Refresh()
    {
        var status = _status;
        var now = _clock.Elapsed;

        // Device card.
        HasDevice = status.DeviceName is not null;
        DeviceTitle = status.DeviceName ?? (status.Phase == HeadsetPhase.Loading
            ? Text.Get("gui.device.checking")
            : Text.Get("gui.device.none"));
        DeviceSubtitle = HasDevice ? Text.Get("gui.device.connected") : Text.Get("gui.device.noneHint");
        if (status.Phase == HeadsetPhase.Loading)
            DeviceSubtitle = "";

        var view = Mode switch
        {
            FlyoutMode.AwaitingPermission => AwaitingView(),
            FlyoutMode.Working => WorkingView(now),
            FlyoutMode.Done => DoneView(),
            FlyoutMode.RebootRequired => RebootView(),
            FlyoutMode.Failed => FailedView(),
            _ => LiveView(status)
        };

        HasChip = view.Chip is not null;
        ChipText = view.Chip ?? "";
        ChipForeground = Palette.Foreground(view.ChipTone);
        ChipBackground = Palette.ChipBackground(view.ChipTone);

        RingValue = view.Ring;
        RingBrush = Palette.Ring(view.RingTone);
        RingIconBrush = Palette.Foreground(view.RingTone == Tone.Red ? Tone.Red : view.RingTone);
        RingIcon = view.Icon;
        CenterText = view.Center;
        CenterFontSize = view.Icon is null ? 32 : 20;
        CenterSubtitle = view.CenterSub;
        Title = view.Title;
        Description = view.Description;

        HasPrimary = view.Primary is not null;
        PrimaryText = view.Primary?.Text ?? "";
        PrimaryIsMain = view.Primary?.Main ?? false;
        PrimaryShowsShield = view.Primary?.Shield ?? false;
        _primaryAction = view.Primary?.Action;

        HardSwitchOn = Mode switch
        {
            FlyoutMode.AwaitingPermission or FlyoutMode.Working => _steps?.Operation == OperationKind.Restore,
            _ => status.HardModeActive
        };

        // Keep the same list objects when nothing changed, so the item views are not rebuilt.
        var previousApps = MicApps;
        var previousLines = InfoLines;
        BuildContext(status, view);
        if (!ReferenceEquals(MicApps, previousApps) && MicApps.SequenceEqual(previousApps))
            MicApps = previousApps;
        if (!ReferenceEquals(InfoLines, previousLines) && InfoLines.SequenceEqual(previousLines))
            InfoLines = previousLines;
        UpdateTray(status);
        OnAllPropertiesChanged();
    }

    private void BuildContext(LiveStatus status, StateView view)
    {
        MicApps = [];
        ShowDefaultMicrophone = false;
        MicDescription = "";
        InfoLines = [];
        CanCopyReport = false;

        if (Mode is FlyoutMode.AwaitingPermission or FlyoutMode.Working)
        {
            Context = ContextKind.Steps;
            return;
        }

        if (Mode == FlyoutMode.RebootRequired && _report is { } rebootReport)
        {
            Context = ContextKind.Info;
            InfoTitle = Text.Get("gui.reboot.what");
            InfoTitleBrush = Palette.Foreground(Tone.Amber);
            InfoLines = ReportLines(rebootReport, "[WARN] ", 3);
            CanCopyReport = true;
            return;
        }

        if (Mode == FlyoutMode.Failed)
        {
            Context = ContextKind.Info;
            InfoTitle = Text.Get("gui.failed.what");
            InfoTitleBrush = Palette.Foreground(Tone.Red);
            InfoLines = _report is { } failedReport
                ? ReportLines(failedReport, "[ERROR] ", 3)
                : [_failure ?? Text.Get("service.unknownError")];
            CanCopyReport = true;
            return;
        }

        _ = view;
        var microphone = status.Microphone;
        MicIcon = Icons.Mic;
        MicIconBrush = Palette.Foreground(Tone.Light);

        if (status.Phase == HeadsetPhase.Hard)
        {
            Context = ContextKind.MicOff;
            MicIcon = Icons.MicOff;
            SetMicStatus("gui.mic.off", Tone.Muted);
            MicDescription = Text.Get("gui.mic.off.desc");
            return;
        }

        if (microphone is null || microphone.Verdict == MicrophoneVerdict.Unavailable)
        {
            Context = ContextKind.MicUnknown;
            SetMicStatus("gui.mic.unknown", Tone.Muted);
            MicDescription = status.Phase == HeadsetPhase.Loading
                ? Text.Get("gui.device.checking")
                : Text.Format("gui.mic.unknown.desc", microphone?.Error ?? Text.Get("service.unknownError"));
            return;
        }

        ShowDefaultMicrophone = microphone.DefaultBluetoothMicrophone is not null;

        switch (microphone.Verdict)
        {
            case MicrophoneVerdict.InUse:
                Context = ContextKind.MicBusy;
                MicIconBrush = Palette.Foreground(Tone.Red);
                SetMicStatus("gui.mic.busy", Tone.Red);
                MicApps = microphone.BluetoothApps.Take(3).Select(app => new AppItem(app.Name)).ToList();
                MicDescription = FormatApps("gui.mic.busy.hint", "gui.mic.busy.hint.many", AppNames(microphone.BluetoothApps));
                break;

            case MicrophoneVerdict.Idle:
                Context = ContextKind.MicFree;
                SetMicStatus("gui.mic.free", Tone.Green);
                MicDescription = microphone.OtherApps.Count > 0
                    ? FormatApps("gui.mic.free.other", "gui.mic.free.other.many", AppNames(microphone.OtherApps))
                    : Text.Get("gui.mic.free.desc");
                break;

            default:
                Context = ContextKind.MicNone;
                SetMicStatus("gui.mic.none", Tone.Muted);
                MicDescription = Text.Get(status.DeviceName is null ? "gui.mic.none.noHeadset" : "gui.mic.none.desc");
                break;
        }
    }

    private void SetMicStatus(string key, Tone tone)
    {
        MicStatusText = Text.Get(key);
        MicStatusBrush = Palette.Foreground(tone);
    }

    private static List<string> ReportLines(ToggleReport report, string prefix, int max)
    {
        var lines = report.Lines
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..])
            .Distinct(StringComparer.Ordinal)
            .Take(max)
            .ToList();

        return lines.Count > 0 ? lines : [report.Title];
    }

    private void UpdateTray(LiveStatus status)
    {
        var (dot, key) = Mode switch
        {
            FlyoutMode.AwaitingPermission or FlyoutMode.Working => ((Tone?)Tone.Red, "gui.tooltip.working"),
            FlyoutMode.RebootRequired => (Tone.Amber, "gui.tooltip.reboot"),
            _ => status.Phase switch
            {
                HeadsetPhase.Stereo => (Tone.Green, "gui.tooltip.stereo"),
                HeadsetPhase.CallMode => (Tone.Red, "gui.tooltip.call"),
                HeadsetPhase.Hard => (Tone.Light, "gui.tooltip.hard"),
                HeadsetPhase.Hung => (Tone.Amber, "gui.tooltip.hung"),
                HeadsetPhase.Loading => (null, "gui.tooltip.loading"),
                HeadsetPhase.ServicesMissing => (null, "gui.tooltip.missing"),
                HeadsetPhase.Unavailable => (Tone.Amber, "gui.tooltip.unavailable"),
                _ => (null, "gui.tooltip.none")
            }
        };

        var state = new TrayState(dot, $"HandsFree Toggle — {Text.Get(key)}");
        if (state != TrayState)
        {
            TrayState = state;
            TrayStateChanged?.Invoke(this, state);
        }
    }

    // ----- Views of each state -----

    private sealed record PrimaryButton(string Text, bool Main, bool Shield, Func<Task> Action);

    private sealed record StateView(
        string? Chip,
        Tone ChipTone,
        Tone RingTone,
        double Ring,
        Geometry? Icon,
        string Center,
        string CenterSub,
        string Title,
        string Description,
        PrimaryButton? Primary);

    private PrimaryButton SoftButton(bool main) => new(
        Text.Get(main ? "gui.action.fix" : "gui.action.restart"),
        main,
        Shield: true,
        () => RunOperationAsync(OperationKind.Soft));

    private StateView LiveView(LiveStatus status)
    {
        switch (status.Phase)
        {
            case HeadsetPhase.Stereo:
                return new StateView(
                    Text.Get("gui.chip.stereo"), Tone.Green, Tone.Green, 1, Icons.Check,
                    Text.Get("gui.ring.stereo"), "A2DP",
                    Text.Get("gui.stereo.title"), Text.Get("gui.stereo.desc"),
                    SoftButton(main: false));

            case HeadsetPhase.CallMode:
                return new StateView(
                    Text.Get("gui.chip.call"), Tone.Red, Tone.Red, 1, Icons.Mic,
                    Text.Get("gui.ring.mono"), Text.Get("gui.ring.monoSub"),
                    Text.Get("gui.call.title"),
                    FormatApps("gui.call.desc", "gui.call.desc.many", AppNames(status)),
                    SoftButton(main: true));

            case HeadsetPhase.Hard:
                return new StateView(
                    Text.Get("gui.chip.hard"), Tone.Light, Tone.Light, 1, Icons.Lock,
                    "HFP", Text.Get("gui.ring.blockedSub"),
                    Text.Get("gui.hard.title"), Text.Get("gui.hard.desc"),
                    null);

            case HeadsetPhase.Hung:
                return new StateView(
                    Text.Get("gui.chip.hung"), Tone.Amber, Tone.Amber, 1, Icons.Alert,
                    "HFP", Text.Get("gui.ring.hungSub"),
                    Text.Get("gui.hung.title"),
                    Text.Format("gui.hung.desc", OperationSteps.ShortServiceName(status.HungService!.FriendlyName)),
                    SoftButton(main: true));

            case HeadsetPhase.ServicesMissing:
                return new StateView(
                    Text.Get("gui.chip.missing"), Tone.Muted, Tone.Muted, 0, Icons.Alert,
                    "HFP", Text.Get("gui.ring.missingSub"),
                    Text.Get("gui.missing.title"), Text.Get("gui.missing.desc"),
                    null);

            case HeadsetPhase.Unavailable:
                return new StateView(
                    Text.Get("gui.chip.unavailable"), Tone.Amber, Tone.Amber, 1, Icons.Alert,
                    "HFP", Text.Get("gui.ring.unavailableSub"),
                    Text.Get("gui.unavailable.title"),
                    Text.Format("gui.unavailable.desc", status.Error ?? Text.Get("service.unknownError")),
                    null);

            case HeadsetPhase.Loading:
                return new StateView(
                    null, Tone.Muted, Tone.Muted, 0, Icons.Headphones,
                    "", "",
                    Text.Get("gui.loading.title"), "",
                    null);

            default:
                return new StateView(
                    null, Tone.Muted, Tone.Muted, 0, Icons.Bluetooth,
                    Text.Get("gui.ring.waiting"), Text.Get("gui.ring.waitingSub"),
                    Text.Get("gui.none.title"), Text.Get("gui.none.desc"),
                    SoftButton(main: false));
        }
    }

    private string OperationTitle() => Text.Get(_steps?.Operation switch
    {
        OperationKind.Hard => "gui.working.hard",
        OperationKind.Restore => "gui.working.restore",
        _ => "gui.working.soft"
    });

    private StateView AwaitingView() => new(
        Text.Get("gui.chip.working"), Tone.Red, Tone.Red, 0, Icons.Shield,
        "UAC", Text.Get("gui.ring.uacSub"),
        OperationTitle(), Text.Get("gui.working.uac"),
        null);

    private StateView WorkingView(TimeSpan now)
    {
        var elapsed = now - _operationStart;
        var progress = _steps?.EstimateProgress(now) ?? 0;
        return new StateView(
            Text.Get("gui.chip.working"), Tone.Red, Tone.Red, progress, null,
            Seconds(elapsed), Text.Get("gui.ring.elapsedSub"),
            OperationTitle(),
            _steps?.Active?.Detail ?? Text.Get("gui.working.finishing"),
            null);
    }

    private StateView DoneView()
    {
        var restore = _steps?.Operation == OperationKind.Restore;
        var description = restore
            ? Text.Get("gui.done.restore.desc")
            : _status.Phase == HeadsetPhase.CallMode
                ? FormatApps("gui.done.soft.micBusy", "gui.done.soft.micBusy.many", AppNames(_status))
                : Text.Get("gui.done.soft.desc");

        return new StateView(
            Text.Get("gui.chip.done"), Tone.Green, Tone.Green, 1, Icons.Check,
            Seconds(_operationElapsed), Text.Get("gui.ring.doneSub"),
            Text.Get(restore ? "gui.done.restore.title" : "gui.done.soft.title"),
            description,
            new PrimaryButton(Text.Get("gui.action.close"), Main: false, Shield: false, DismissResultAsync));
    }

    private StateView RebootView() => new(
        Text.Get("gui.chip.reboot"), Tone.Amber, Tone.Amber, 1, Icons.Restart,
        Text.Get("gui.ring.pause"), Text.Get("gui.ring.pauseSub"),
        Text.Get("gui.reboot.title"),
        Text.Get(_steps?.Operation switch
        {
            OperationKind.Hard => "gui.reboot.hard",
            OperationKind.Restore => "gui.reboot.restore",
            _ => "gui.reboot.soft"
        }),
        new PrimaryButton(Text.Get("gui.action.gotIt"), Main: false, Shield: false, DismissResultAsync));

    private StateView FailedView()
    {
        var titleKey = _steps?.Operation switch
        {
            OperationKind.Hard => "gui.failed.hard",
            OperationKind.Restore => "gui.failed.restore",
            _ => "gui.failed.soft"
        };

        return new StateView(
            Text.Get("gui.chip.error"), Tone.Red, Tone.Red, 1, Icons.Alert,
            Text.Get("gui.ring.error"), Text.Get("gui.ring.errorSub"),
            Text.Get(titleKey),
            Text.Get("gui.failed.desc"),
            new PrimaryButton(Text.Get("gui.action.close"), Main: false, Shield: false, DismissResultAsync));
    }

    private static string Seconds(TimeSpan elapsed)
        => Text.Format("gui.ring.seconds", Math.Max(1, (int)Math.Round(elapsed.TotalSeconds)));
}
