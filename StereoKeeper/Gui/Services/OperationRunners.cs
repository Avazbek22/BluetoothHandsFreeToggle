using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using StereoKeeper.App;
using StereoKeeper.Core;
using StereoKeeper.Localization;

namespace StereoKeeper.Gui.Services;

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
/// from a named pipe.
/// </summary>
/// <remarks>
/// The pipe admits the current user and the Administrators group: a standard user who types an
/// administrator's password in the UAC prompt gets a worker running as that other account. Only
/// the worker process this runner started is accepted as the client.
/// </remarks>
public sealed class ElevatedOperationRunner : IOperationRunner
{
    private const int ErrorCancelled = 1223;

    private readonly string _executablePath;
    private readonly Func<string, string, Process?> _launch;

    public ElevatedOperationRunner(string executablePath)
        : this(executablePath, LaunchElevated)
    {
    }

    /// <param name="launch">Starts the worker (executable, arguments); replaceable in tests.</param>
    internal ElevatedOperationRunner(string executablePath, Func<string, string, Process?> launch)
    {
        _executablePath = executablePath;
        _launch = launch;
    }

    /// <summary>How long the worker may take to connect after UAC was answered.</summary>
    internal TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public async Task<OperationOutcome> RunAsync(
        OperationKind operation,
        Action<OperationEvent> onEvent,
        Action? onElevated = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onEvent);

        var pipeName = $"StereoKeeper.{Environment.ProcessId}.{Guid.NewGuid():N}";
        NamedPipeServerStream server;
        try
        {
            server = CreatePipe(pipeName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationOutcome.Failed(exception.Message);
        }

        await using (server.ConfigureAwait(false))
        {
            var arguments = LaunchOptions.FormatWorkerArguments(
                operation,
                pipeName,
                AppLanguageCode.ToCode(Text.CurrentLanguage));

            Process? process;
            try
            {
                // ShellExecuteEx blocks until the UAC prompt is answered, so keep it off the UI thread.
                process = await Task.Run(() => _launch(_executablePath, arguments), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
            {
                return OperationOutcome.Cancelled;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
            {
                return OperationOutcome.Failed(exception.Message);
            }

            if (process is null)
                return OperationOutcome.Failed(Text.Get("gui.error.workerNotStarted"));

            using (process)
                return await ReadWorkerAsync(server, process, onEvent, onElevated, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<OperationOutcome> ReadWorkerAsync(
        NamedPipeServerStream server,
        Process process,
        Action<OperationEvent> onEvent,
        Action? onElevated,
        CancellationToken cancellationToken)
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
                await connectTimeout.CancelAsync().ConfigureAwait(false);
                return OperationOutcome.Failed(Text.Get("gui.error.workerNoAnswer"));
            }

            await connect.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationOutcome.Failed(Text.Get("gui.error.workerNoAnswer"));
        }

        if (!IsClient(server, process))
            return OperationOutcome.Failed(Text.Get("gui.error.workerNoAnswer"));

        try
        {
            using var reader = new StreamReader(server, new UTF8Encoding(false));
            var report = await WorkerProtocol.ReadAsync(reader, onEvent, cancellationToken).ConfigureAwait(false);
            return report is null
                ? OperationOutcome.Failed(Text.Get("gui.error.workerNoAnswer"))
                : OperationOutcome.Completed(report);
        }
        catch (IOException exception)
        {
            return OperationOutcome.Failed(exception.Message);
        }
    }

    private static NamedPipeServerStream CreatePipe(string pipeName)
    {
        var security = new PipeSecurity();
        if (WindowsIdentity.GetCurrent().User is { } user)
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    /// <summary>True when the connected client is the worker process that was started.</summary>
    private static bool IsClient(NamedPipeServerStream server, Process process)
    {
        try
        {
            return GetNamedPipeClientProcessId(server.SafePipeHandle, out var clientId) &&
                   clientId == (uint)process.Id;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static Process? LaunchElevated(string executablePath, string arguments)
        => Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas"
        });

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);
}
