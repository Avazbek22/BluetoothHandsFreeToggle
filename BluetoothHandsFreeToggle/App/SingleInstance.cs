using System.Runtime.InteropServices;

namespace BluetoothHandsFreeToggle.App;

/// <summary>
/// Keeps one tray app per user session. A second launch (Start menu, desktop shortcut, autostart
/// while the app already runs) signals the first instance to open its panel and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\BluetoothHandsFreeToggle.Tray";
    private const string ShowEventName = @"Local\BluetoothHandsFreeToggle.Tray.Show";

    // ASFW_ANY: lets the running instance take the foreground for the panel it is asked to show.
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _listener;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        return new SingleInstance(mutex, showEvent);
    }

    public static void SignalRunningInstance()
    {
        _ = AllowSetForegroundWindow(AsfwAny);

        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The first instance is still starting or already exiting.
        }
    }

    /// <summary>Calls <paramref name="onShowRequested"/> (on a background thread) for every second launch.</summary>
    public void Listen(Action onShowRequested)
    {
        ArgumentNullException.ThrowIfNull(onShowRequested);

        _listener = new Thread(() =>
        {
            var handles = new[] { _showEvent, _stop.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
                onShowRequested();
        })
        {
            IsBackground = true,
            Name = "Single instance listener"
        };
        _listener.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Join(TimeSpan.FromSeconds(1));
        _showEvent.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from a different thread than the owner: the handle is closed below anyway.
        }

        _mutex.Dispose();
        _stop.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
