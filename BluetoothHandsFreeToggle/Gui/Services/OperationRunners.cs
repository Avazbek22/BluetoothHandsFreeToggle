using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Gui.Services;

/// <summary>Used when the tray app itself already runs as administrator.</summary>
public sealed class InProcessOperationRunner(ToggleEngine engine) : IOperationRunner
{
    public Task<OperationOutcome> RunAsync(
        OperationKind operation,
        Action<OperationEvent> onEvent,
        Action? onElevated = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onEvent);

        return Task.Run(() =>
        {
            onElevated?.Invoke();
            var progress = new CallbackProgress(onEvent);
            var report = operation switch
            {
                OperationKind.Soft => engine.SoftResetHandsFree(progress),
                OperationKind.Hard => engine.HardDisableHandsFree(progress),
                _ => engine.RestoreHandsFree(progress)
            };
            return OperationOutcome.Completed(report);
        }, cancellationToken);
    }

    private sealed class CallbackProgress(Action<OperationEvent> callback) : IProgress<OperationEvent>
    {
        public void Report(OperationEvent value) => callback(value);
    }
}

/// <summary>
/// Starts this executable as an elevated worker through UAC and reads its progress and report
/// from a named pipe that only the current user can open.
/// </summary>
public sealed class ElevatedOperationRunner(string executablePath) : IOperationRunner
{
    private const int ErrorCancelled = 1223;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public async Task<OperationOutcome> RunAsync(
        OperationKind operation,
        Action<OperationEvent> onEvent,
        Action? onElevated = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onEvent);

        var pipeName = $"BluetoothHandsFreeToggle.{Environment.ProcessId}.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var arguments = LaunchOptions.FormatWorkerArguments(
            operation,
            pipeName,
            AppLanguageCode.ToCode(Text.CurrentLanguage));

        Process? process;
        try
        {
            // ShellExecuteEx blocks until the UAC prompt is answered, so keep it off the UI thread.
            process = await Task.Run(
                () => Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                }),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            return OperationOutcome.Cancelled;
        }
        catch (Win32Exception exception)
        {
            return OperationOutcome.Failed(exception.Message);
        }

        if (process is null)
            return OperationOutcome.Failed(Text.Get("gui.error.workerNotStarted"));

        using (process)
        {
            onElevated?.Invoke();

            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(ConnectTimeout);
            var connect = server.WaitForConnectionAsync(connectTimeout.Token);
            var exited = process.WaitForExitAsync(connectTimeout.Token);

            try
            {
                if (await Task.WhenAny(connect, exited).ConfigureAwait(false) != connect)
                {
                    connectTimeout.Cancel();
                    return OperationOutcome.Failed(Text.Get("gui.error.workerNoAnswer"));
                }

                await connect.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return OperationOutcome.Failed(Text.Get("gui.error.workerNoAnswer"));
            }

            using var reader = new StreamReader(server, new UTF8Encoding(false));
            var report = await WorkerProtocol.ReadAsync(reader, onEvent, cancellationToken).ConfigureAwait(false);

            return report is null
                ? OperationOutcome.Failed(Text.Get("gui.error.workerNoAnswer"))
                : OperationOutcome.Completed(report);
        }
    }
}
