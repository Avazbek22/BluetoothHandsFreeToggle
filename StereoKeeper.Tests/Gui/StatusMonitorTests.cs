using System.Diagnostics;
using System.Threading.Channels;
using StereoKeeper.Core;
using StereoKeeper.Gui.Model;
using StereoKeeper.Gui.Services;
using Xunit;
using static StereoKeeper.Tests.Gui.Fixtures;

namespace StereoKeeper.Tests.Gui;

public sealed class StatusMonitorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task PublishesTheEvaluatedStatus()
    {
        var sink = new StatusSink();
        using var monitor = Create(new FakeSource(), sink.Add);
        monitor.Start();

        var status = await sink.NextAsync();

        Assert.Equal(HeadsetPhase.Stereo, status.Phase);
        Assert.Equal(HeadsetName, status.DeviceName);
    }

    [Fact]
    public async Task AFailingSourceIsReportedAsUnavailableAndPollingContinues()
    {
        var source = new FakeSource
        {
            ServicesFor = call => call <= 2 ? throw new InvalidOperationException("SCM unavailable") : Services()
        };
        var sink = new StatusSink();
        using var monitor = Create(source, sink.Add, visible: true);
        monitor.Start();

        var first = await sink.NextAsync();
        var recovered = await sink.NextAsync(status => status.Phase != HeadsetPhase.Unavailable);

        Assert.Equal(HeadsetPhase.Unavailable, first.Phase);
        Assert.Equal("SCM unavailable", first.Error);
        Assert.Equal(HeadsetPhase.Stereo, recovered.Phase);
    }

    [Fact]
    public async Task AFailingStatusHandlerDoesNotStopUpdates()
    {
        var sink = new StatusSink();
        var calls = 0;
        using var monitor = Create(new FakeSource(), status =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("binding failed");
            sink.Add(status);
        }, visible: true);
        monitor.Start();

        await sink.NextAsync();
        await sink.NextAsync();

        Assert.True(Volatile.Read(ref calls) >= 3);
    }

    [Fact]
    public async Task AHungCheckIsReportedAndNotStartedAgain()
    {
        using var release = new ManualResetEventSlim();
        var source = new FakeSource { Gate = release };
        var sink = new StatusSink();
        using var monitor = Create(source, sink.Add, visible: true, checkTimeout: TimeSpan.FromMilliseconds(80));
        monitor.Start();

        var first = await sink.NextAsync();
        var second = await sink.NextAsync();

        Assert.Equal(HeadsetPhase.Unavailable, first.Phase);
        Assert.Equal(HeadsetPhase.Unavailable, second.Phase);
        Assert.False(string.IsNullOrWhiteSpace(first.Error));
        Assert.Equal(1, source.Calls);

        release.Set();
        var recovered = await sink.NextAsync(status => status.Phase != HeadsetPhase.Unavailable);
        Assert.Equal(HeadsetPhase.Stereo, recovered.Phase);
    }

    [Fact]
    public async Task RefreshNowChecksRightAwayEvenWithALongInterval()
    {
        var source = new FakeSource();
        var sink = new StatusSink();
        using var monitor = Create(source, sink.Add, interval: TimeSpan.FromHours(1));
        monitor.Start();
        await sink.NextAsync();

        monitor.RefreshNow();
        await sink.NextAsync();

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task TheOpenPanelIsCheckedOftenTheHiddenOneRarely()
    {
        var source = new FakeSource();
        var sink = new StatusSink();
        using var monitor = new StatusMonitorFactory(source, sink.Add)
        {
            VisibleInterval = Quick,
            HiddenInterval = TimeSpan.FromHours(1)
        }.Create();
        monitor.Start();
        await sink.NextAsync();

        await Task.Delay(300);
        Assert.Equal(1, source.Calls);

        monitor.PanelVisible = true;
        monitor.RefreshNow();
        await WaitUntilAsync(() => source.Calls >= 5);
    }

    [Fact]
    public async Task AServiceStuckInATransitionBecomesHung()
    {
        var source = new FakeSource { ServicesFor = _ => Services(ServiceRunState.StartPending) };
        var sink = new StatusSink();
        using var monitor = Create(source, sink.Add, visible: true, hungAfter: TimeSpan.FromMilliseconds(150));
        monitor.Start();

        var first = await sink.NextAsync();
        var hung = await sink.NextAsync(status => status.Phase == HeadsetPhase.Hung);

        Assert.NotEqual(HeadsetPhase.Hung, first.Phase);
        Assert.NotNull(hung.HungService);
    }

    [Fact]
    public async Task DisposeReturnsQuicklyAndStopsUpdatesEvenWhileACheckHangs()
    {
        using var release = new ManualResetEventSlim();
        var source = new FakeSource { Gate = release };
        var delivered = 0;
        var monitor = Create(source, _ => Interlocked.Increment(ref delivered), visible: true);
        monitor.Start();
        await WaitUntilAsync(() => source.Calls == 1);

        var stopwatch = Stopwatch.StartNew();
        monitor.Dispose();
        stopwatch.Stop();
        var deliveredAtDispose = Volatile.Read(ref delivered);

        release.Set();
        await Task.Delay(300);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Dispose took {stopwatch.Elapsed}");
        Assert.Equal(deliveredAtDispose, Volatile.Read(ref delivered));
        monitor.Dispose();
    }

    [Fact]
    public async Task ResultsArePostedToTheContextOfTheCreatingThread()
    {
        var context = new RecordingContext();
        var sink = new StatusSink();
        StatusMonitor monitor;

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            monitor = new StatusMonitor(new FakeSource(), sink.Add);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using (monitor)
        {
            monitor.Start();
            await sink.NextAsync();
        }

        Assert.True(context.Posts >= 1);
    }

    [Fact]
    public void PanelVisibilityCanBeSetFromAnyThread()
    {
        using var monitor = Create(new FakeSource(), _ => { });

        Parallel.For(0, 1000, index => monitor.PanelVisible = index % 2 == 0);
        monitor.PanelVisible = true;

        Assert.True(monitor.PanelVisible);
    }

    private static StatusMonitor Create(
        IStatusSource source,
        Action<LiveStatus> onStatus,
        bool visible = false,
        TimeSpan? interval = null,
        TimeSpan? checkTimeout = null,
        TimeSpan? hungAfter = null)
    {
        var monitor = new StatusMonitorFactory(source, onStatus)
        {
            VisibleInterval = interval ?? Quick,
            HiddenInterval = interval ?? Quick,
            CheckTimeout = checkTimeout ?? TimeSpan.FromSeconds(15),
            HungAfter = hungAfter ?? TimeSpan.FromSeconds(8)
        }.Create();
        monitor.PanelVisible = visible;
        return monitor;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < Patience, "The condition was not met in time.");
            await Task.Delay(10);
        }
    }

    /// <summary>Creates the monitor without the xUnit synchronization context, so results arrive inline.</summary>
    private sealed class StatusMonitorFactory(IStatusSource source, Action<LiveStatus> onStatus)
    {
        public TimeSpan VisibleInterval { get; init; } = Quick;
        public TimeSpan HiddenInterval { get; init; } = Quick;
        public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(15);
        public TimeSpan HungAfter { get; init; } = TimeSpan.FromSeconds(8);

        public StatusMonitor Create()
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                return new StatusMonitor(source, onStatus)
                {
                    VisibleInterval = VisibleInterval,
                    HiddenInterval = HiddenInterval,
                    CheckTimeout = CheckTimeout,
                    HungAfter = HungAfter
                };
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }
    }

    private sealed class FakeSource : IStatusSource
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Func<int, IReadOnlyList<ServiceSnapshot>> ServicesFor { get; init; } = _ => Services();

        public ManualResetEventSlim? Gate { get; init; }

        public IReadOnlyList<ServiceSnapshot> GetServices()
        {
            var call = Interlocked.Increment(ref _calls);
            Gate?.Wait();
            return ServicesFor(call);
        }

        public MicrophoneDiagnosis? DiagnoseMicrophone() => IdleHeadset();
    }

    private sealed class StatusSink
    {
        private readonly Channel<LiveStatus> _statuses = Channel.CreateUnbounded<LiveStatus>();

        public void Add(LiveStatus status) => _statuses.Writer.TryWrite(status);

        public async Task<LiveStatus> NextAsync(Func<LiveStatus, bool>? until = null)
        {
            using var timeout = new CancellationTokenSource(Patience);
            while (true)
            {
                var status = await _statuses.Reader.ReadAsync(timeout.Token);
                if (until is null || until(status))
                    return status;
            }
        }
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            base.Post(d, state);
        }
    }
}
