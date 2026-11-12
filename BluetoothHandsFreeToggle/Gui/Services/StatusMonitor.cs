using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Gui.Model;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Gui.Services;

/// <summary>
/// Checks the service and microphone state in the background: often while the panel is open,
/// less often while only the tray icon (and notifications) need it.
/// </summary>
/// <remarks>
/// The loop never touches UI objects (it runs on the thread pool) and never stops on an error:
/// a failed or hung check is reported as <see cref="HeadsetPhase.Unavailable"/> and the next check
/// follows on schedule. Results are posted to the synchronization context of the creating thread.
/// </remarks>
public sealed class StatusMonitor : IDisposable
{
    private readonly IStatusSource _source;
    private readonly Action<LiveStatus> _onStatus;
    private readonly SynchronizationContext? _ui;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, (ServiceRunState State, DateTime Since)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<LiveStatus>? _inFlight;
    private Task? _loop;
    private volatile bool _panelVisible;
    private volatile bool _disposed;

    public StatusMonitor(IStatusSource source, Action<LiveStatus> onStatus)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onStatus);

        _source = source;
        _onStatus = onStatus;
        _ui = SynchronizationContext.Current;
    }

    public TimeSpan VisibleInterval { get; init; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan HiddenInterval { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>How long a service may stay in a transitional state before the panel calls it hung.</summary>
    public TimeSpan HungAfter { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>A check that takes longer is reported as unavailable; it is not started twice.</summary>
    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Picks the check interval; safe to set from any thread.</summary>
    public bool PanelVisible
    {
        get => _panelVisible;
        set => _panelVisible = value;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    /// <summary>Checks again right away (the panel opened, an operation finished).</summary>
    public void RefreshNow() => Interlocked.Exchange(
        ref _wake,
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stop.Cancel();
        RefreshNow();

        // The loop only awaits; a check stuck in native code is abandoned rather than awaited.
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Stopping.
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var wake = Volatile.Read(ref _wake).Task;

            LiveStatus status;
            try
            {
                status = await CheckWithTimeoutAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                status = Unavailable(exception.Message);
            }

            Publish(status);

            var interval = _panelVisible ? VisibleInterval : HiddenInterval;
            await WaitAsync(wake, interval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Waits for <paramref name="task"/> or the timeout without leaving a timer behind.</summary>
    /// <returns>True when <paramref name="task"/> finished first.</returns>
    private static async Task<bool> WaitAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, timer.Token);
        var finished = await Task.WhenAny(task, delay).ConfigureAwait(false);
        await timer.CancelAsync().ConfigureAwait(false);
        return finished == task;
    }

    private async Task<LiveStatus> CheckWithTimeoutAsync(CancellationToken cancellationToken)
    {
        // A check that timed out earlier keeps running; wait for it instead of piling up threads.
        var check = _inFlight ??= Task.Run(Check, CancellationToken.None);
        var completed = await WaitAsync(check, CheckTimeout, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!completed)
            return Unavailable(Text.Get("gui.error.statusTimeout"));

        _inFlight = null;
        return await check.ConfigureAwait(false);
    }

    private void Publish(LiveStatus status)
    {
        if (_disposed)
            return;

        if (_ui is null)
        {
            Deliver(status);
            return;
        }

        _ui.Post(_ => Deliver(status), null);
    }

    private void Deliver(LiveStatus status)
    {
        if (_disposed)
            return;

        try
        {
            _onStatus(status);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A UI glitch must not stop status updates; it is logged for diagnosis.
            CrashLog.Write("Applying a status update failed", exception);
        }
    }

    private LiveStatus Check()
    {
        try
        {
            var services = _source.GetServices();
            var microphone = _source.DiagnoseMicrophone();
            return LiveStatus.Evaluate(services, microphone, TrackHung(services));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Unavailable(exception.Message);
        }
    }

    private static LiveStatus Unavailable(string error)
        => new(HeadsetPhase.Unavailable, [], null, null, null, error);

    private HashSet<string> TrackHung(IReadOnlyList<ServiceSnapshot> services)
    {
        var now = DateTime.UtcNow;
        var hung = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var service in services)
        {
            if (!service.Exists || !ServiceRunStates.IsPending(service.RunState))
            {
                _pending.Remove(service.ServiceName);
                continue;
            }

            if (!_pending.TryGetValue(service.ServiceName, out var entry) || entry.State != service.RunState)
            {
                _pending[service.ServiceName] = (service.RunState, now);
                continue;
            }

            if (now - entry.Since >= HungAfter)
                hung.Add(service.ServiceName);
        }

        return hung;
    }
}
