using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Gui;
using BluetoothHandsFreeToggle.Gui.Model;
using BluetoothHandsFreeToggle.Gui.Services;
using BluetoothHandsFreeToggle.Gui.ViewModels;
using BluetoothHandsFreeToggle.Localization;
using Xunit;
using static BluetoothHandsFreeToggle.Tests.Gui.Fixtures;

namespace BluetoothHandsFreeToggle.Tests.Gui;

[Collection(PanelTests.Name)]
public sealed class FlyoutViewModelTests
{
    private const string HandsFree = "Bluetooth Hands-Free Service (HFP)";

    [Fact]
    public Task StartsWithAQuietLoadingState() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create();

        Assert.Equal(FlyoutMode.Live, panel.Mode);
        Assert.Equal("Checking the headset…", panel.Title);
        Assert.False(panel.HasPrimary);
        Assert.False(panel.HardSwitchEnabled);
        Assert.Equal("HandsFree Toggle — checking…", panel.TrayState.Tooltip);
    });

    [Fact]
    public Task NoHeadsetWaitsAndStillOffersARestart() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Empty());

        Assert.False(panel.HasDevice);
        Assert.Equal("No headset connected", panel.Title);
        Assert.Equal("Waiting", panel.CenterText);
        Assert.Equal("Restart HFP", panel.PrimaryText);
        Assert.False(panel.PrimaryIsMain);
        Assert.Equal(ContextKind.MicNone, panel.Context);
        Assert.Equal("Connect a headset to see which apps use its microphone.", panel.MicDescription);
        Assert.Null(panel.TrayState.Dot);
    });

    [Fact]
    public Task StereoShowsTheHeadsetAndAFreeMicrophone() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());

        Assert.Equal(HeadsetName, panel.DeviceTitle);
        Assert.Equal("Stereo", panel.ChipText);
        Assert.Equal("Sound is fine", panel.Title);
        Assert.Equal(ContextKind.MicFree, panel.Context);
        Assert.True(panel.ShowDefaultMicrophone);
        Assert.Equal(Tone.Green, panel.TrayState.Dot);
    });

    [Fact]
    public Task CallModeNamesTheAppsAndMakesTheFixTheMainAction() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(CallMode("Discord", "Google Chrome"));

        Assert.Equal("Call", panel.ChipText);
        Assert.Equal("Headset is in call mode", panel.Title);
        Assert.Equal("The sound is squeezed to mono because Discord and Google Chrome are using the headset microphone.", panel.Description);
        Assert.Equal("Close Discord and Google Chrome or choose another microphone in them, otherwise Windows turns call mode back on.", panel.MicDescription);
        Assert.Equal("Fix sound", panel.PrimaryText);
        Assert.True(panel.PrimaryIsMain);
        Assert.True(panel.PrimaryShowsShield);
        Assert.Equal(ContextKind.MicBusy, panel.Context);
        Assert.Equal(["Discord", "Google Chrome"], panel.MicApps.Select(app => app.Name));
        Assert.Equal(["D", "G"], panel.MicApps.Select(app => app.Initial));
        Assert.Equal(Tone.Red, panel.TrayState.Dot);
    });

    [Theory]
    [InlineData(AppLanguage.English, "Discord is using", "Discord, Google Chrome and Zoom are using")]
    [InlineData(AppLanguage.Russian, "Discord использует", "Discord, Google Chrome и Zoom используют")]
    public Task VerbAgreesWithTheNumberOfApps(AppLanguage language, string one, string many) => Ui.RunAsync(() =>
    {
        Text.SetLanguage(language);
        try
        {
            var single = new PanelHarness().Create(CallMode("Discord"));
            var several = new PanelHarness().Create(CallMode("Discord", "Google Chrome", "Zoom"));

            Assert.Contains(one, single.Description, StringComparison.Ordinal);
            Assert.Contains(many, several.Description, StringComparison.Ordinal);
        }
        finally
        {
            Text.SetLanguage(AppLanguage.English);
        }
    });

    [Fact]
    public Task HardModeHasNoFixButtonAndShowsTheMicrophoneAsOff() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Hard());

        Assert.True(panel.HardSwitchOn);
        Assert.True(panel.HardSwitchEnabled);
        Assert.False(panel.HasPrimary);
        Assert.Equal(ContextKind.MicOff, panel.Context);
        Assert.Equal("off", panel.MicStatusText);
    });

    [Fact]
    public Task HungServiceIsNamedAndFixable() => Ui.RunAsync(() =>
    {
        var services = new List<ServiceSnapshot> { Service("BthHFSrv"), Service("BTAGService", ServiceRunState.StopPending) };
        var panel = new PanelHarness().Create(LiveStatus.Evaluate(services, IdleHeadset(), new HashSet<string> { "BTAGService" }));

        Assert.Equal("HFP service is not responding", panel.Title);
        Assert.Equal("Bluetooth Audio Gateway is stuck. Fixing restarts it safely.", panel.Description);
        Assert.True(panel.PrimaryIsMain);
        Assert.Equal(Tone.Amber, panel.TrayState.Dot);
    });

    [Fact]
    public Task MissingServicesDisableEveryAction() => Ui.RunAsync(() =>
    {
        var services = new List<ServiceSnapshot> { Service("BthHFSrv", exists: false), Service("BTAGService", exists: false) };
        var panel = new PanelHarness().Create(LiveStatus.Evaluate(services, NoHeadset()));

        Assert.False(panel.HasPrimary);
        Assert.False(panel.HardSwitchEnabled);
        Assert.False(panel.SoftResetCommand.CanExecute(null));
    });

    [Fact]
    public Task WaitingForUacKeepsThePanelBusyAndShowsThePlan() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        harness.Runner.PassesUac = false;
        harness.Runner.HoldUntilReleased = true;
        var panel = harness.Create(CallMode("Discord"));

        var run = panel.RunAsync(OperationKind.Soft);

        Assert.Equal(FlyoutMode.AwaitingPermission, panel.Mode);
        Assert.True(panel.IsBusy);
        Assert.Equal("UAC", panel.CenterText);
        Assert.Equal("Confirm the Windows administrator prompt.", panel.Description);
        Assert.Equal(ContextKind.Steps, panel.Context);
        Assert.Equal(5, panel.Steps.Count);
        Assert.All(panel.Steps, step => Assert.True(step.IsPending));
        Assert.False(panel.HardSwitchEnabled);
        Assert.False(panel.SoftResetCommand.CanExecute(null));
        Assert.Equal("HandsFree Toggle — working…", panel.TrayState.Tooltip);

        harness.Runner.Outcome = OperationOutcome.Cancelled;
        harness.Runner.Release();
        await run;
    });

    [Fact]
    public Task DecliningUacGoesBackToTheLiveStatusWithANotice() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        harness.Runner.PassesUac = false;
        harness.Runner.Outcome = OperationOutcome.Cancelled;
        var panel = harness.Create(CallMode("Discord"));

        await panel.RunAsync(OperationKind.Soft);

        Assert.Equal(FlyoutMode.Live, panel.Mode);
        Assert.Equal("Headset is in call mode", panel.Title);
        Assert.True(panel.HasNotice);
        Assert.Equal("Cancelled: changing services needs administrator permission.", panel.Notice);
    });

    [Fact]
    public Task ProgressDrivesTheChecklistTheRingAndTheDescription() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        harness.Runner.HoldUntilReleased = true;
        harness.Runner.Events.Add(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Stopping, Service: HandsFree));
        harness.Runner.Events.Add(new OperationEvent(
            OperationEventKind.Waiting, "", TimeSpan.FromSeconds(2), OperationStep.WaitingStop, HandsFree));
        var panel = harness.Create(CallMode("Discord"));

        var run = panel.RunAsync(OperationKind.Soft);
        await Task.Yield();

        Assert.Equal(FlyoutMode.Working, panel.Mode);
        Assert.True(panel.Steps[0].IsActive);
        Assert.Equal("Waiting for the service to stop · 2 s", panel.Description);
        Assert.Equal("elapsed", panel.CenterSubtitle);
        Assert.InRange(panel.RingValue, 0, 0.2);

        harness.Runner.Release();
        await run;

        Assert.Equal(FlyoutMode.Done, panel.Mode);
        Assert.All(panel.Steps, step => Assert.True(step.IsDone));
    });

    [Fact]
    public Task SuccessfulFixShowsDoneUntilClosed() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        var panel = harness.Create(CallMode("Discord"));
        var refreshRequests = 0;
        panel.StatusRefreshRequested += (_, _) => refreshRequests++;

        await panel.RunAsync(OperationKind.Soft);
        panel.ApplyStatus(Stereo());

        Assert.Equal(FlyoutMode.Done, panel.Mode);
        Assert.Equal("Done", panel.Title);
        Assert.Equal("The service restarted. The sound is back in stereo.", panel.Description);
        Assert.Equal("Close", panel.PrimaryText);
        Assert.Equal(1, refreshRequests);

        panel.PrimaryCommand.Execute(null);

        Assert.Equal(FlyoutMode.Live, panel.Mode);
        Assert.Equal("Sound is fine", panel.Title);
    });

    [Fact]
    public Task FixThatCannotWinAgainstARecordingAppSaysSo() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        var panel = harness.Create(CallMode("Discord"));

        await panel.RunAsync(OperationKind.Soft);

        Assert.Equal(FlyoutMode.Done, panel.Mode);
        Assert.Contains("Discord is still using the headset microphone", panel.Description, StringComparison.Ordinal);
    });

    [Fact]
    public Task HidingThePanelDismissesAFinishedResult() => Ui.RunAsync(async () =>
    {
        var panel = new PanelHarness().Create(Stereo());
        await panel.RunAsync(OperationKind.Soft);

        panel.OnHidden();

        Assert.Equal(FlyoutMode.Live, panel.Mode);
    });

    [Fact]
    public Task RebootRequiredExplainsWhyAndOffersTheReport() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        harness.Runner.Outcome = OperationOutcome.Completed(Report(
            true,
            rebootRequired: true,
            "[OK] Startup type set to Disabled.",
            "[WARN] Bluetooth Audio Gateway (HFP) is still running.",
            "[WARN] Restart Windows to finish."));
        var panel = harness.Create(Stereo());

        panel.ToggleHardCommand.Execute(null);
        await Task.Yield();
        await Task.Yield();

        Assert.Equal([OperationKind.Hard], harness.Runner.Calls);
        Assert.Equal(FlyoutMode.RebootRequired, panel.Mode);
        Assert.Equal("Restart required", panel.Title);
        Assert.Equal("HFP startup is already disabled. Restart Windows to finish Hard mode.", panel.Description);
        Assert.Equal(ContextKind.Info, panel.Context);
        Assert.Equal(["Bluetooth Audio Gateway (HFP) is still running.", "Restart Windows to finish."], panel.InfoLines);
        Assert.True(panel.CanCopyReport);
        Assert.Equal(Tone.Amber, panel.TrayState.Dot);
    });

    [Fact]
    public Task FailedOperationShowsTheErrorsAndCopiesTheFullReport() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        harness.Runner.Outcome = OperationOutcome.Completed(Report(false, false, "[ERROR] BthHFSrv did not stop within 60 s."));
        var panel = harness.Create(CallMode("Discord"));

        await panel.RunAsync(OperationKind.Soft);

        Assert.Equal(FlyoutMode.Failed, panel.Mode);
        Assert.Equal("Could not restart HFP", panel.Title);
        Assert.Equal(["BthHFSrv did not stop within 60 s."], panel.InfoLines);
        Assert.Equal("Copy report", panel.CopyReportText);

        panel.CopyReportCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("BthHFSrv did not stop within 60 s.", harness.Shell.Clipboard, StringComparison.Ordinal);
        Assert.Equal("Copied", panel.CopyReportText);
    });

    [Fact]
    public Task WorkerThatCouldNotStartIsAFailureWithItsMessage() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        harness.Runner.PassesUac = false;
        harness.Runner.Outcome = OperationOutcome.Failed("The file is not a valid application.");
        var panel = harness.Create(Hard());

        await panel.RunAsync(OperationKind.Restore);

        Assert.Equal(FlyoutMode.Failed, panel.Mode);
        Assert.Equal("Could not restore HFP", panel.Title);
        Assert.Equal(["The file is not a valid application."], panel.InfoLines);
    });

    [Fact]
    public Task RunnerExceptionIsShownInsteadOfCrashing() => Ui.RunAsync(async () =>
    {
        var panel = new FlyoutViewModel(new ThrowingRunner(), new FakeAutostart(), new MemorySettingsStore(), new FakeShell());
        panel.ApplyStatus(Stereo());

        await panel.RunAsync(OperationKind.Soft);

        Assert.Equal(FlyoutMode.Failed, panel.Mode);
        Assert.Equal(["pipe broke"], panel.InfoLines);
    });

    [Fact]
    public Task HardSwitchTurnsHardModeOffWhenItIsOn() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        var panel = harness.Create(Hard());

        panel.ToggleHardCommand.Execute(null);
        await Task.Yield();
        await Task.Yield();

        Assert.Equal([OperationKind.Restore], harness.Runner.Calls);
        Assert.Equal(FlyoutMode.Done, panel.Mode);
        Assert.Equal("HFP restored", panel.Title);
    });

    [Fact]
    public Task SecondOperationIsIgnoredWhileOneRuns() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        harness.Runner.HoldUntilReleased = true;
        var panel = harness.Create(Stereo());

        var first = panel.RunAsync(OperationKind.Soft);
        await panel.RunAsync(OperationKind.Hard);
        harness.Runner.Release();
        await first;

        Assert.Equal([OperationKind.Soft], harness.Runner.Calls);
    });

    [Fact]
    public Task SwitchToCallModeRaisesOneNotificationNamingTheApp() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        var notifications = new List<(string Title, string Body)>();
        panel.NotificationRequested += (_, notification) => notifications.Add(notification);

        panel.ApplyStatus(CallMode("Discord"));
        panel.ApplyStatus(CallMode("Discord"));

        var notification = Assert.Single(notifications);
        Assert.Equal("Headset switched to call mode", notification.Title);
        Assert.Equal("Discord is using the headset microphone, so the sound is mono.", notification.Body);
    });

    [Fact]
    public Task AppJoiningAnOngoingCallIsReported() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(CallMode("Discord"));
        var notifications = new List<(string Title, string Body)>();
        panel.NotificationRequested += (_, notification) => notifications.Add(notification);

        panel.ApplyStatus(CallMode("Discord", "Zoom"));

        var notification = Assert.Single(notifications);
        Assert.Equal("Zoom started recording from the headset microphone.", notification.Body);
    });

    [Fact]
    public Task NotificationsFollowTheSettings() => Ui.RunAsync(() =>
    {
        var harness = new PanelHarness();
        var panel = harness.Create(Stereo());
        var notifications = new List<(string Title, string Body)>();
        panel.NotificationRequested += (_, notification) => notifications.Add(notification);

        panel.NotifyCallMode = false;
        panel.ApplyStatus(CallMode("Discord"));
        panel.NotifyMicrophone = false;
        panel.ApplyStatus(Stereo());
        panel.ApplyStatus(CallMode("Zoom"));

        var notification = Assert.Single(notifications);
        Assert.Equal("Headset microphone in use", notification.Title);
        Assert.False(harness.Settings.Stored.NotifyCallMode);
        Assert.False(harness.Settings.Stored.NotifyMicrophone);
        Assert.Equal(2, harness.Settings.Saves);
    });

    [Fact]
    public Task NoNotificationsWhileAnOperationRuns() => Ui.RunAsync(async () =>
    {
        var harness = new PanelHarness();
        harness.Runner.HoldUntilReleased = true;
        var panel = harness.Create(Stereo());
        var notifications = 0;
        panel.NotificationRequested += (_, _) => notifications++;

        var run = panel.RunAsync(OperationKind.Soft);
        panel.ApplyStatus(CallMode("Discord"));
        harness.Runner.Release();
        await run;

        Assert.Equal(0, notifications);
    });

    [Fact]
    public Task AutostartSwitchShowsWhatTheRegistryReallyHolds() => Ui.RunAsync(() =>
    {
        var harness = new PanelHarness();
        var panel = harness.Create(Stereo());

        panel.AutostartEnabled = true;
        Assert.True(panel.AutostartEnabled);

        harness.Autostart.Blocked = true;
        panel.AutostartEnabled = false;

        Assert.True(panel.AutostartEnabled);
        Assert.Equal([true, false], harness.Autostart.Requests);
    });

    [Fact]
    public Task HelpReturnsToThePageItWasOpenedFrom() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());

        panel.ShowSettingsCommand.Execute(null);
        panel.ShowHelpCommand.Execute(null);
        Assert.True(panel.IsHelpPage);

        panel.BackCommand.Execute(null);
        Assert.True(panel.IsSettingsPage);

        panel.BackCommand.Execute(null);
        Assert.True(panel.IsMainPage);

        panel.ShowHelpCommand.Execute(null);
        panel.BackCommand.Execute(null);
        Assert.True(panel.IsMainPage);
    });

    [Fact]
    public Task ReopenedPanelStartsOnTheMainPage() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        panel.ShowSettingsCommand.Execute(null);

        panel.OnHidden();

        Assert.True(panel.IsMainPage);
    });

    [Fact]
    public Task LinksAndQuitGoThroughTheShell() => Ui.RunAsync(() =>
    {
        var harness = new PanelHarness();
        var panel = harness.Create(CallMode("Discord"));

        panel.OpenSourceCommand.Execute(null);
        panel.OpenSupportCommand.Execute(null);
        panel.OpenSoundSettingsCommand.Execute(null);
        panel.QuitCommand.Execute(null);

        Assert.Equal([FlyoutViewModel.GitHubUrl, AppInfo.SupportUrl], harness.Shell.OpenedUrls);
        Assert.Equal(1, harness.Shell.SoundSettingsOpened);
        Assert.True(harness.Shell.QuitRequested);
    });

    [Fact]
    public Task LanguageSwitchRelabelsThePanelAtOnce() => Ui.RunAsync(() =>
    {
        try
        {
            var panel = new PanelHarness().Create(CallMode("Discord"));
            var languageChanges = 0;
            panel.LanguageChanged += (_, _) => languageChanges++;

            panel.SelectLanguageCommand.Execute(panel.Languages.Single(option => option.Language == AppLanguage.Russian));

            Assert.Equal("Настройки", panel.L.Settings);
            Assert.Equal("Гарнитура в режиме звонка", panel.Title);
            Assert.Equal("Исправить звук", panel.PrimaryText);
            Assert.Equal("Русский", panel.CurrentLanguageName);
            Assert.True(panel.Languages.Single(option => option.Language == AppLanguage.Russian).IsCurrent);
            Assert.Equal(1, languageChanges);
        }
        finally
        {
            Text.SetLanguage(AppLanguage.English);
        }
    });

    private sealed class ThrowingRunner : IOperationRunner
    {
        public Task<OperationOutcome> RunAsync(
            OperationKind operation,
            Action<OperationEvent> onEvent,
            Action? onElevated = null,
            CancellationToken cancellationToken = default)
            => throw new IOException("pipe broke");
    }
}
