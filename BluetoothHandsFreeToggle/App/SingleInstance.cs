using System.Runtime.InteropServices;

namespace BluetoothHandsFreeToggle.App;

/// <summary>
/// Keeps one tray app per user session. A second launch (Start menu, desktop shortcut, autostart
/// while the app already runs) signals the first instance to open its panel and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string DefaultName = @"Local\BluetoothHandsFreeToggle.Tray";

    // ASFW_ANY: lets the running instance take the foreground for the panel it is asked to show.
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _listener;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    /// <returns>The instance, or <c>null</c> when another tray app already runs in this session.</returns>
    public static SingleInstance? TryAcquire() => TryAcquire(DefaultName);

    public static void SignalRunningInstance() => SignalRunningInstance(DefaultName);

    internal static SingleInstance? TryAcquire(string name)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // The mutex exists but belongs to an instance started as administrator: it is running.
            return null;
        }

        try
        {
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
            return new SingleInstance(mutex, showEvent);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            return null;
        }
    }

    internal static void SignalRunningInstance(string name)
    {
        _ = AllowSetForegroundWindow(AsfwAny);

        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(name + ".Show");
            showEvent.Set();
        }
        catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            // The first instance is still starting, already exiting, or runs as administrator
            // (a non-elevated process may not signal it). Nothing else to do.
        }
    }

    /// <summary>Calls <paramref name="onShowRequested"/> (on a background thread) for every second launch.</summary>
    public void Listen(Action onShowRequested)
    {
        ArgumentNullException.ThrowIfNull(onShowRequested);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _listener = new Thread(() =>
        {
            var handles = new[] { _showEvent, _stop.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                try
                {
                    onShowRequested();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // An escaping exception would end the process from this background thread.
                    CrashLog.Write("Showing the panel for a second launch failed", exception);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Single instance listener"
        };
        _listener.Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
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
