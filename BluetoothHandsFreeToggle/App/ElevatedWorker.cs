using System.IO.Pipes;
using System.Text;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.App;

/// <summary>
/// The short-lived elevated process the tray app starts through UAC. It runs exactly the one
/// operation named on its command line and streams progress and the report back over a named
/// pipe created by the tray app.
/// </summary>
public static class ElevatedWorker
{
    public const int ExitSuccess = 0;
    public const int ExitOperationFailed = 1;
    public const int ExitBadArguments = 2;
    public const int ExitNotAdministrator = 3;
    public const int ExitNoTrayApp = 4;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    public static int Run(LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var language = AppLanguageCode.TryParse(options.LanguageCode, out var parsed)
            ? parsed
            : AppLanguage.English;
        Text.Initialize(new LocalizationService(new FixedLanguageStore(language), language));

        return Run(options, AdminHelper.IsAdministrator, Execute);
    }

    /// <param name="isAdministrator">Checked before anything else is done.</param>
    /// <param name="execute">Runs the operation; replaceable in tests.</param>
    /// <param name="connectTimeout">How long to wait for the tray app's pipe.</param>
    internal static int Run(
        LaunchOptions options,
        Func<bool> isAdministrator,
        Func<OperationKind, IProgress<OperationEvent>, ToggleReport> execute,
        TimeSpan? connectTimeout = null)
    {
        if (options.Operation is not { } operation || string.IsNullOrWhiteSpace(options.PipeName))
            return ExitBadArguments;

        if (!isAdministrator())
            return ExitNotAdministrator;

        NamedPipeClientStream pipe;
        StreamWriter writer;
        try
        {
            pipe = new NamedPipeClientStream(".", options.PipeName, PipeDirection.Out);
            pipe.Connect((int)(connectTimeout ?? ConnectTimeout).TotalMilliseconds);
            writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // The tray app went away; there is nobody to report to, so nothing is changed.
            CrashLog.Write("The elevated worker could not reach the tray app", exception);
            return ExitNoTrayApp;
        }

        using (pipe)
        using (writer)
        {
            var sink = new PipeSink(writer);
            ToggleReport report;
            try
            {
                report = execute(operation, sink);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The engine reports its own failures; anything that still escapes becomes a report
                // so the panel shows the reason instead of "no answer".
                CrashLog.Write("The elevated worker failed", exception);
                report = ToggleReport.Failed(
                    Text.Get(TitleKey(operation)),
                    [Text.Format("operation.unexpectedError", exception.Message)]);
            }

            sink.Write(new WorkerMessage(null, report));
            return report.Success ? ExitSuccess : ExitOperationFailed;
        }
    }

    private static ToggleReport Execute(OperationKind operation, IProgress<OperationEvent> progress)
    {
        var engine = new ToggleEngine();
        return operation switch
        {
            OperationKind.Soft => engine.SoftResetHandsFree(progress),
            OperationKind.Hard => engine.HardDisableHandsFree(progress),
            _ => engine.RestoreHandsFree(progress)
        };
    }

    private static string TitleKey(OperationKind operation) => operation switch
    {
        OperationKind.Soft => "gui.failed.soft",
        OperationKind.Hard => "gui.failed.hard",
        _ => "gui.failed.restore"
    };

    private sealed class PipeSink(StreamWriter writer) : IProgress<OperationEvent>
    {
        private readonly Lock _gate = new();
        private bool _broken;

        public void Report(OperationEvent value) => Write(new WorkerMessage(value, null));

        public void Write(WorkerMessage message)
        {
            lock (_gate)
            {
                if (_broken)
                    return;

                try
                {
                    writer.WriteLine(WorkerProtocol.Serialize(message));
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    // Keep the operation running to completion even if the tray app closed.
                    _broken = true;
                }
            }
        }
    }

    private sealed class FixedLanguageStore(AppLanguage language) : ILanguagePreferenceStore
    {
        public AppLanguage? Load() => language;

        public void Save(AppLanguage language)
        {
        }
    }
}
