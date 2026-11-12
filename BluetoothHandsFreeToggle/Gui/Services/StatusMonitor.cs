using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Gui.Model;

namespace BluetoothHandsFreeToggle.Gui.Services;

/// <summary>
/// Checks the service and microphone state in the background: often while the panel is open,
/// less often while only the tray icon (and notifications) need it.
/// </summary>
public sealed class StatusMonitor : IDisposable
{
    /// <summary>How long a service may stay in a transitional state before the panel calls it hung.</summary>
    public static readonly TimeSpan HungAfter = TimeSpan.FromSeconds(8);

    private readonly IStatusSource _source;
    private readonly Action<LiveStatus> _onStatus;
    private readonly Func<bool> _isPanelVisible;
    private readonly SynchronizationContext? _ui;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, (ServiceRunState State, DateTime Since)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _loop;

    public StatusMonitor(IStatusSource source, Action<LiveStatus> onStatus, Func<bool> isPanelVisible)
    {
        _source = source;
        _onStatus = onStatus;
        _isPanelVisible = isPanelVisible;
        _ui = SynchronizationContext.Current;
    }

    public TimeSpan VisibleInterval { get; init; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan HiddenInterval { get; init; } = TimeSpan.FromSeconds(4);

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    /// <summary>Checks again right away (the panel opened, an operation finished).</summary>
    public void RefreshNow() => Interlocked.Exchange(
        ref _wake,
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    public void Dispose()
    {
        _stop.Cancel();
        RefreshNow();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Stopping.
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var wake = Volatile.Read(ref _wake).Task;
            var status = Check();
            if (_ui is null)
                _onStatus(status);
            else
                _ui.Post(_ => _onStatus(status), null);

            var interval = _isPanelVisible() ? VisibleInterval : HiddenInterval;
            try
            {
                await Task.WhenAny(wake, Task.Delay(interval, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
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
            return new LiveStatus(HeadsetPhase.Unavailable, [], null, null, null, exception.Message);
        }
    }

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
