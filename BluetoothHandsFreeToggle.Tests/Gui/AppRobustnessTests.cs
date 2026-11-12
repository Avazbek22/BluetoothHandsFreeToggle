using System.Text;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Gui.Services;
using BluetoothHandsFreeToggle.Windows;
using Microsoft.Win32;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests.Gui;

public sealed class SingleInstanceTests
{
    private static string UniqueName() => $@"Local\BluetoothHandsFreeToggle.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void OnlyTheFirstInstanceGetsTheLockUntilItIsReleased()
    {
        var name = UniqueName();

        var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(name));

        first.Dispose();
        using var next = SingleInstance.TryAcquire(name);
        Assert.NotNull(next);
    }

    [Fact]
    public void ASecondLaunchAsksTheFirstToShowItsPanel()
    {
        var name = UniqueName();
        using var instance = SingleInstance.TryAcquire(name)!;
        using var shown = new SemaphoreSlim(0);
        instance.Listen(() => shown.Release());

        SingleInstance.SignalRunningInstance(name);
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)));

        SingleInstance.SignalRunningInstance(name);
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AFailingShowRequestDoesNotStopListening()
    {
        var name = UniqueName();
        using var instance = SingleInstance.TryAcquire(name)!;
        using var shown = new SemaphoreSlim(0);
        var calls = 0;
        instance.Listen(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("dispatcher shut down");
            shown.Release();
        });

        SingleInstance.SignalRunningInstance(name);
        SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, TimeSpan.FromSeconds(5));
        SingleInstance.SignalRunningInstance(name);

        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SignallingWithoutARunningInstanceIsHarmless()
        => SingleInstance.SignalRunningInstance(UniqueName());

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var instance = SingleInstance.TryAcquire(UniqueName())!;
        instance.Listen(() => { });

        instance.Dispose();
        instance.Dispose();
    }
}

public sealed class MicrophoneProbeGuardTests
{
    private static readonly MicrophoneProbeResult Empty = new([], [], null);

    [Fact]
    public void AHungProbeIsNotStartedAgainUntilItReturns()
    {
        using var release = new ManualResetEventSlim();
        var started = 0;
        var probe = new WindowsMicrophoneProbe(TimeSpan.FromMilliseconds(50), () =>
        {
            Interlocked.Increment(ref started);
            release.Wait();
            return Empty;
        });

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Contains("Windows Audio did not respond", probe.Probe().Error, StringComparison.Ordinal);
        Assert.Equal(1, Volatile.Read(ref started));

        release.Set();
        MicrophoneProbeResult? result = null;
        Assert.True(SpinWait.SpinUntil(
            () => (result = probe.Probe()).Error is null,
            TimeSpan.FromSeconds(5)));
        Assert.Same(Empty, result);
        Assert.Equal(2, Volatile.Read(ref started));
    }

    [Fact]
    public void AProbeThatThrowsIsReportedNotRethrown()
    {
        var probe = new WindowsMicrophoneProbe(TimeSpan.FromSeconds(5), () => throw new InvalidCastException("No such interface supported"));

        var result = probe.Probe();

        Assert.Equal("No such interface supported", result.Error);
        Assert.Empty(result.Devices);
    }

    [Fact]
    public void ConcurrentCallersGetAnswersWithoutPilingUpThreads()
    {
        var running = 0;
        var peak = 0;
        var probe = new WindowsMicrophoneProbe(TimeSpan.FromSeconds(5), () =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            Thread.Sleep(5);
            Interlocked.Decrement(ref running);
            return Empty;
        });

        Parallel.For(0, 40, _ => Assert.Null(probe.Probe().Error));

        Assert.Equal(1, peak);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}

public sealed class CrashLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hft-log-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void AnEntryHasTheContextAndTheWholeException()
    {
        var path = Path.Combine(_folder, "logs", "app.log");

        CrashLog.WriteTo(path, "Applying a status update failed", new InvalidOperationException("boom"), CrashLog.MaxBytes);

        var text = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("Applying a status update failed", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: boom", text, StringComparison.Ordinal);
        Assert.Contains(AppInfo.AppDisplayVersion, text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLogNeverGrowsPastItsLimit()
    {
        var path = Path.Combine(_folder, "app.log");
        const long limit = 2_000;

        for (var entry = 0; entry < 200; entry++)
            CrashLog.WriteTo(path, $"entry {entry} " + new string('x', 80), null, limit);

        Assert.True(new FileInfo(path).Length < limit + 200);
        Assert.True(File.Exists(Path.Combine(_folder, "app.old.log")));
        Assert.Contains("entry 199", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void WritingNeverThrows()
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, "a-file");
        File.WriteAllText(file, "");

        CrashLog.WriteTo(Path.Combine(file, "logs", "app.log"), "parent is a file", null, CrashLog.MaxBytes);
        CrashLog.WriteTo("bad\0path", "invalid path", null, CrashLog.MaxBytes);
        CrashLog.WriteTo(@"C:\", "a root", null, CrashLog.MaxBytes);
    }

    [Fact]
    public void ParallelWritersDoNotLoseOrBreakEntries()
    {
        var path = Path.Combine(_folder, "app.log");

        Parallel.For(0, 100, entry => CrashLog.WriteTo(path, $"parallel {entry}", null, CrashLog.MaxBytes));

        var lines = File.ReadAllLines(path);
        Assert.Equal(100, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^\d{4}-\d\d-\d\d .* parallel \d+$", line));
    }
}

public sealed class FileGuiSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hft-settings-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_folder, "gui-settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void AMissingFileGivesTheDefaults()
    {
        var settings = new FileGuiSettingsStore(FilePath).Load();

        Assert.True(settings.NotifyCallMode);
        Assert.True(settings.NotifyMicrophone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"NotifyCallMode\":\"yes\"}")]
    [InlineData("\0\0\0")]
    public void ADamagedFileGivesTheDefaults(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, content);

        var settings = new FileGuiSettingsStore(FilePath).Load();

        Assert.True(settings.NotifyCallMode);
        Assert.True(settings.NotifyMicrophone);
    }

    [Fact]
    public void SettingsSurviveARestartAndLeaveNoTemporaryFile()
    {
        new FileGuiSettingsStore(FilePath).Save(new GuiSettings { NotifyCallMode = false, NotifyMicrophone = true });

        var settings = new FileGuiSettingsStore(FilePath).Load();

        Assert.False(settings.NotifyCallMode);
        Assert.True(settings.NotifyMicrophone);
        Assert.Equal(["gui-settings.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public void AnUnwritableLocationIsIgnored()
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, "a-file");
        File.WriteAllText(file, "");
        var store = new FileGuiSettingsStore(Path.Combine(file, "gui-settings.json"));

        store.Save(new GuiSettings { NotifyCallMode = false });

        Assert.True(store.Load().NotifyCallMode);
    }

    [Fact]
    public void ALockedFileIsIgnored()
    {
        var store = new FileGuiSettingsStore(FilePath);
        store.Save(new GuiSettings { NotifyMicrophone = false });

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Save(new GuiSettings { NotifyMicrophone = true });
            Assert.True(store.Load().NotifyMicrophone);
        }

        Assert.False(store.Load().NotifyMicrophone);
    }
}

public sealed class RegistryAutostartTests : IDisposable
{
    private const string TestRoot = @"Software\BluetoothHandsFreeToggle.Tests";
    private const string AppPath = @"C:\Program Files\HandsFree Toggle\BluetoothHandsFreeToggle.exe";

    private static readonly byte[] DisabledInTaskManager = [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] EnabledInTaskManager = [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private readonly string _root = $@"{TestRoot}\{Guid.NewGuid():N}";

    private string RunPath => _root + @"\Run";
    private string ApprovedPath => _root + @"\StartupApproved";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(TestRoot, writable: false);
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
            Registry.CurrentUser.DeleteSubKey(TestRoot, throwOnMissingSubKey: false);
    }

    [Fact]
    public void EnablingWritesAQuotedTrayCommand()
    {
        var autostart = Create(AppPath);

        Assert.False(autostart.IsEnabled);
        Assert.True(autostart.TrySetEnabled(true));

        Assert.True(autostart.IsEnabled);
        Assert.Equal($"\"{AppPath}\" {LaunchOptions.TrayArgument}", RunValue());
    }

    [Fact]
    public void DisablingRemovesTheEntry()
    {
        var autostart = Create(AppPath);
        autostart.TrySetEnabled(true);

        Assert.True(autostart.TrySetEnabled(false));

        Assert.False(autostart.IsEnabled);
        Assert.Null(RunValue());
        Assert.True(autostart.TrySetEnabled(false));
    }

    [Fact]
    public void AnEntryDisabledInTaskManagerCountsAsOffUntilEnabledAgain()
    {
        var autostart = Create(AppPath);
        autostart.TrySetEnabled(true);
        SetApproved(DisabledInTaskManager);
        Assert.False(autostart.IsEnabled);

        SetApproved(EnabledInTaskManager);
        Assert.True(autostart.IsEnabled);

        SetApproved(DisabledInTaskManager);
        Assert.True(autostart.TrySetEnabled(true));
        Assert.True(autostart.IsEnabled);
    }

    [Fact]
    public void RefreshFollowsAMovedAppButNeverTurnsAutostartOn()
    {
        Create(@"D:\Old\BluetoothHandsFreeToggle.exe").TrySetEnabled(true);
        var moved = Create(AppPath);

        moved.RefreshPath();
        Assert.Equal(moved.Command, RunValue());

        moved.TrySetEnabled(false);
        moved.RefreshPath();
        Assert.Null(RunValue());
    }

    [Fact]
    public void AnUnexpectedValueTypeIsTreatedAsOff()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunPath))
            run.SetValue(AppInfo.AppName, 1, RegistryValueKind.DWord);

        Assert.False(Create(AppPath).IsEnabled);
    }

    private RegistryAutostart Create(string executablePath) => new(executablePath, RunPath, ApprovedPath);

    private string? RunValue()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunPath);
        return run?.GetValue(AppInfo.AppName) as string;
    }

    private void SetApproved(byte[] state)
    {
        using var approved = Registry.CurrentUser.CreateSubKey(ApprovedPath);
        approved.SetValue(AppInfo.AppName, state, RegistryValueKind.Binary);
    }
}
