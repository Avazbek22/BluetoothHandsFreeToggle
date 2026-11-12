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
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    public static int Run(LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Operation is not { } operation || string.IsNullOrWhiteSpace(options.PipeName))
            return 2;

        var language = AppLanguageCode.TryParse(options.LanguageCode, out var parsed)
            ? parsed
            : AppLanguage.English;
        Text.Initialize(new LocalizationService(new FixedLanguageStore(language), language));

        if (!AdminHelper.IsAdministrator())
            return 3;

        try
        {
            using var pipe = new NamedPipeClientStream(".", options.PipeName, PipeDirection.Out);
            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            var sink = new PipeSink(writer);

            var engine = new ToggleEngine();
            var report = operation switch
            {
                OperationKind.Soft => engine.SoftResetHandsFree(sink),
                OperationKind.Hard => engine.HardDisableHandsFree(sink),
                _ => engine.RestoreHandsFree(sink)
            };

            sink.Write(new WorkerMessage(null, report));
            return report.Success ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // The tray app went away; there is nobody to report to.
            return 4;
        }
    }

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
                catch (IOException)
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
