using Avalonia;
using Avalonia.Headless;
using StereoKeeper.App;
using StereoKeeper.Core;
using StereoKeeper.Gui;
using StereoKeeper.Gui.Model;
using StereoKeeper.Gui.Services;
using StereoKeeper.Gui.ViewModels;
using Xunit;

namespace StereoKeeper.Tests.Gui;

/// <summary>
/// The panel tests share one headless Avalonia app and switch the global UI language, so they run
/// on their own, after the parallel tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelTests
{
    public const string Name = "Avalonia panel";
}

/// <summary>Entry point for <see cref="HeadlessUnitTestSession"/>: the real app and theme, rendered by Skia off-screen.</summary>
public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<GuiApp>()
        .UseSkia()
        .UseHarfBuzz()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal static class Ui
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp)));

    /// <summary>Runs <paramref name="test"/> on the Avalonia UI thread.</summary>
    public static Task RunAsync(Func<Task> test) => Session.Value.Dispatch(
        async () =>
        {
            await test().ConfigureAwait(true);
            return true;
        },
        CancellationToken.None);

    public static Task RunAsync(Action test) => Session.Value.Dispatch(test, CancellationToken.None);
}

internal static class Fixtures
{
    public const string HeadsetName = "WH-1000XM5";

    public static readonly CaptureDevice HeadsetMicrophone =
        new("bt-headset", $"Headset ({HeadsetName})", IsBluetooth: true, IsDefault: true, IsDefaultCommunications: true);

    public static readonly CaptureDevice UsbMicrophone =
        new("usb", "Microphone (fifine)", IsBluetooth: false, IsDefault: false, IsDefaultCommunications: false);

    public static ServiceSnapshot Service(
        string name,
        ServiceRunState runState = ServiceRunState.Running,
        ServiceStartType startType = ServiceStartType.Manual,
        bool exists = true,
        bool querySucceeded = true,
        string? note = null)
    {
        var friendlyName = name == "BTAGService" ? "Bluetooth Audio Gateway (HFP)" : "Bluetooth Hands-Free Service (HFP)";
        return new ServiceSnapshot(
            name,
            friendlyName,
            exists,
            querySucceeded,
            exists ? runState : ServiceRunState.NotPresent,
            exists ? startType : ServiceStartType.NotPresent,
            exists ? 3 : null,
            note);
    }

    public static List<ServiceSnapshot> Services(
        ServiceRunState runState = ServiceRunState.Running,
        ServiceStartType startType = ServiceStartType.Manual)
        => [Service("BthHFSrv", runState, startType), Service("BTAGService", runState, startType)];

    public static MicrophoneDiagnosis IdleHeadset(params CaptureClient[] otherClients) => MicrophoneDiagnostics.Analyze(
        new MicrophoneProbeResult([HeadsetMicrophone, UsbMicrophone], otherClients, null) { Headsets = [HeadsetName] });

    public static MicrophoneDiagnosis HeadsetInUse(params string[] apps) => MicrophoneDiagnostics.Analyze(
        new MicrophoneProbeResult(
            [HeadsetMicrophone, UsbMicrophone],
            apps.Select((app, index) => Client(HeadsetMicrophone.Id, 100 + index, app)).ToList(),
            null) { Headsets = [HeadsetName] });

    public static MicrophoneDiagnosis NoHeadset() => MicrophoneDiagnostics.Analyze(new MicrophoneProbeResult([UsbMicrophone], [], null));

    /// <summary>Hard mode removes the headset microphone, but the headset still plays.</summary>
    public static MicrophoneDiagnosis HeadsetWithoutMicrophone() => MicrophoneDiagnostics.Analyze(
        new MicrophoneProbeResult([UsbMicrophone], [], null) { Headsets = [HeadsetName] });

    public static CaptureClient Client(string deviceId, int processId, string name)
        => new(deviceId, new AppIdentity(processId, name, $@"C:\Apps\{name}.exe", null));

    public static LiveStatus Stereo() => LiveStatus.Evaluate(Services(), IdleHeadset());

    public static LiveStatus CallMode(params string[] apps) => LiveStatus.Evaluate(Services(), HeadsetInUse(apps));

    public static LiveStatus Hard() => LiveStatus.Evaluate(
        Services(ServiceRunState.Stopped, ServiceStartType.Disabled),
        HeadsetWithoutMicrophone());

    public static LiveStatus Empty() => LiveStatus.Evaluate(Services(), NoHeadset());

    public static ToggleReport Report(bool success, bool rebootRequired = false, params string[] lines)
        => new("report", success, Services(), lines.ToList(), rebootRequired);
}

internal sealed class FakeAutostart : IAutostart
{
    public bool IsEnabled { get; set; }

    public List<bool> Requests { get; } = [];

    /// <summary>Simulates a policy that blocks the registry write.</summary>
    public bool Blocked { get; set; }

    public bool TrySetEnabled(bool enabled)
    {
        Requests.Add(enabled);
        if (Blocked)
            return false;

        IsEnabled = enabled;
        return true;
    }
}

internal sealed class MemorySettingsStore : IGuiSettingsStore
{
    public GuiSettings Stored { get; private set; } = new();

    public int Saves { get; private set; }

    public GuiSettings Load() => new() { NotifyCallMode = Stored.NotifyCallMode, NotifyMicrophone = Stored.NotifyMicrophone };

    public void Save(GuiSettings settings)
    {
        Saves++;
        Stored = new GuiSettings { NotifyCallMode = settings.NotifyCallMode, NotifyMicrophone = settings.NotifyMicrophone };
    }
}

internal sealed class FakeShell : IShell
{
    public List<string> OpenedUrls { get; } = [];

    public string? Clipboard { get; private set; }

    public int SoundSettingsOpened { get; private set; }

    public bool QuitRequested { get; private set; }

    public void OpenUrl(string url) => OpenedUrls.Add(url);

    public void OpenSoundRecordingSettings() => SoundSettingsOpened++;

    public Task CopyTextAsync(string text)
    {
        Clipboard = text;
        return Task.CompletedTask;
    }

    public void Quit() => QuitRequested = true;
}

internal sealed class FakeDiagnostics : IDiagnosticsSource
{
    public string Report { get; set; } = "StereoKeeper v3.0";

    public Exception? Failure { get; set; }

    public string Collect() => Failure is null ? Report : throw Failure;
}

/// <summary>
/// Plays a scripted operation: optionally passes UAC, reports the given progress, then waits for
/// <see cref="Release"/> (when <see cref="HoldUntilReleased"/> is set) before returning the outcome.
/// </summary>
internal sealed class ScriptedRunner : IOperationRunner
{
    private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<OperationKind> Calls { get; } = [];

    public bool PassesUac { get; set; } = true;

    public bool HoldUntilReleased { get; set; }

    public List<OperationEvent> Events { get; } = [];

    public OperationOutcome Outcome { get; set; } = OperationOutcome.Completed(Fixtures.Report(true));

    public void Release() => _release.TrySetResult();

    public async Task<OperationOutcome> RunAsync(
        OperationKind operation,
        Action<OperationEvent> onEvent,
        Action? onElevated = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(operation);
        if (PassesUac)
            onElevated?.Invoke();

        foreach (var operationEvent in Events)
            onEvent(operationEvent);

        if (HoldUntilReleased)
        {
            await _release.Task.ConfigureAwait(true);
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return Outcome;
    }
}

internal sealed class PanelHarness
{
    public ScriptedRunner Runner { get; } = new();
    public FakeAutostart Autostart { get; } = new();
    public MemorySettingsStore Settings { get; } = new();
    public FakeShell Shell { get; } = new();
    public FakeDiagnostics Diagnostics { get; } = new();

    public FlyoutViewModel Create(LiveStatus? status = null)
    {
        var viewModel = new FlyoutViewModel(Runner, Autostart, Settings, Shell, Diagnostics);
        if (status is not null)
            viewModel.ApplyStatus(status);
        return viewModel;
    }
}
