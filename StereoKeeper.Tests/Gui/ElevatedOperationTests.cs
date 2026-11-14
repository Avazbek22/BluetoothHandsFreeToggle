using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using StereoKeeper.App;
using StereoKeeper.Core;
using StereoKeeper.Gui.Services;
using Xunit;
using static StereoKeeper.Tests.Gui.Fixtures;

namespace StereoKeeper.Tests.Gui;

/// <summary>
/// The tray app and its elevated worker, end to end over a real named pipe. The "worker" runs
/// inside the test process, which the runner accepts because the launch hands back this process.
/// </summary>
public sealed class ElevatedOperationTests
{
    private static readonly OperationEvent Stopping = new(
        OperationEventKind.Step, "Stopping BTAGService", null, OperationStep.Stopping, "Bluetooth Audio Gateway (HFP)");

    private static readonly OperationEvent Starting = new(
        OperationEventKind.Step, "Starting BTAGService", null, OperationStep.Starting, "Bluetooth Audio Gateway (HFP)");

    [Fact]
    public void WorkerRejectsMissingArguments()
    {
        var executed = false;

        var exitCode = ElevatedWorker.Run(
            new LaunchOptions(LaunchMode.ElevatedWorker),
            () => true,
            (_, _) =>
            {
                executed = true;
                return Report(true);
            });

        Assert.Equal(ElevatedWorker.ExitBadArguments, exitCode);
        Assert.False(executed);
    }

    [Fact]
    public void WorkerRefusesToRunWithoutAdministratorRights()
    {
        var executed = false;

        var exitCode = ElevatedWorker.Run(
            WorkerOptions("never-created"),
            () => false,
            (_, _) =>
            {
                executed = true;
                return Report(true);
            });

        Assert.Equal(ElevatedWorker.ExitNotAdministrator, exitCode);
        Assert.False(executed);
    }

    [Fact]
    public void WorkerChangesNothingWhenTheTrayAppIsGone()
    {
        var executed = false;

        var exitCode = ElevatedWorker.Run(
            WorkerOptions($"StereoKeeper.Tests.{Guid.NewGuid():N}"),
            () => true,
            (_, _) =>
            {
                executed = true;
                return Report(true);
            },
            TimeSpan.FromMilliseconds(200));

        Assert.Equal(ElevatedWorker.ExitNoTrayApp, exitCode);
        Assert.False(executed);
    }

    [Fact]
    public async Task ProgressAndReportTravelFromTheWorkerToThePanel()
    {
        var workerExit = new TaskCompletionSource<int>();
        var runner = InProcessWorker(workerExit, (operation, progress) =>
        {
            Assert.Equal(OperationKind.Soft, operation);
            progress.Report(Stopping);
            progress.Report(Starting);
            return Report(true, rebootRequired: false, "[OK] Restarted.");
        });
        var events = new List<OperationEvent>();
        var elevated = 0;

        var outcome = await runner.RunAsync(OperationKind.Soft, events.Add, () => elevated++);

        Assert.Equal(OperationOutcomeKind.Completed, outcome.Kind);
        Assert.True(outcome.Report?.Success);
        Assert.Equal(["[OK] Restarted."], outcome.Report?.Lines);
        Assert.Equal([Stopping, Starting], events);
        Assert.Equal(1, elevated);
        Assert.Equal(ElevatedWorker.ExitSuccess, await workerExit.Task);
    }

    [Fact]
    public async Task AnUnexpectedWorkerErrorArrivesAsAFailedReport()
    {
        var workerExit = new TaskCompletionSource<int>();
        var runner = InProcessWorker(workerExit, (_, progress) =>
        {
            progress.Report(Stopping);
            throw new InvalidOperationException("Service database is locked");
        });

        var outcome = await runner.RunAsync(OperationKind.Hard, _ => { });

        Assert.Equal(OperationOutcomeKind.Completed, outcome.Kind);
        Assert.False(outcome.Report?.Success);
        Assert.Contains(outcome.Report!.Lines, line => line.Contains("Service database is locked", StringComparison.Ordinal));
        Assert.Equal(ElevatedWorker.ExitOperationFailed, await workerExit.Task);
    }

    [Fact]
    public async Task DecliningUacIsACancellationNotAFailure()
    {
        var runner = new ElevatedOperationRunner("app.exe", (_, _) => throw new Win32Exception(1223));

        var outcome = await runner.RunAsync(OperationKind.Soft, _ => { });

        Assert.Equal(OperationOutcomeKind.Cancelled, outcome.Kind);
    }

    [Fact]
    public async Task AWorkerThatCannotStartIsReported()
    {
        var missing = new ElevatedOperationRunner("app.exe", (_, _) => throw new Win32Exception(2, "The system cannot find the file specified."));
        var nothing = new ElevatedOperationRunner("app.exe", (_, _) => null);

        var notFound = await missing.RunAsync(OperationKind.Soft, _ => { });
        var noProcess = await nothing.RunAsync(OperationKind.Soft, _ => { });

        Assert.Equal(OperationOutcomeKind.Failed, notFound.Kind);
        Assert.Contains("cannot find", notFound.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OperationOutcomeKind.Failed, noProcess.Kind);
        Assert.False(string.IsNullOrWhiteSpace(noProcess.Error));
    }

    [Fact]
    public async Task AWorkerThatExitsWithoutAnsweringFailsQuickly()
    {
        var runner = new ElevatedOperationRunner("app.exe", (_, _) => StartHidden("/c exit 0"))
        {
            ConnectTimeout = TimeSpan.FromSeconds(20)
        };
        var stopwatch = Stopwatch.StartNew();

        var outcome = await runner.RunAsync(OperationKind.Soft, _ => { });

        Assert.Equal(OperationOutcomeKind.Failed, outcome.Kind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task AReportFromAnotherProcessIsNotTrusted()
    {
        int? workerId = null;
        Task? forger = null;
        var runner = new ElevatedOperationRunner("app.exe", (_, arguments) =>
        {
            // The started worker idles; this test process connects in its place.
            var worker = StartHidden("/c ping -n 30 127.0.0.1 >nul");
            workerId = worker.Id;
            var pipeName = LaunchOptions.Parse(arguments.Split(' ')).PipeName!;
            forger = Task.Run(() => SendForgedReport(pipeName));
            return worker;
        })
        {
            ConnectTimeout = TimeSpan.FromSeconds(20)
        };

        try
        {
            var outcome = await runner.RunAsync(OperationKind.Hard, _ => { });

            Assert.Equal(OperationOutcomeKind.Failed, outcome.Kind);
            Assert.Null(outcome.Report);
        }
        finally
        {
            KillQuietly(workerId);
            if (forger is not null)
                await forger;
        }
    }

    private static LaunchOptions WorkerOptions(string pipeName)
        => new(LaunchMode.ElevatedWorker, Operation: OperationKind.Soft, PipeName: pipeName, LanguageCode: "en");

    /// <summary>A runner whose "elevated" worker is <see cref="ElevatedWorker"/> running on a thread of this process.</summary>
    private static ElevatedOperationRunner InProcessWorker(
        TaskCompletionSource<int> exit,
        Func<OperationKind, IProgress<OperationEvent>, ToggleReport> execute)
        => new("app.exe", (executable, arguments) =>
        {
            Assert.Equal("app.exe", executable);
            var options = LaunchOptions.Parse(arguments.Split(' '));
            _ = Task.Run(() =>
            {
                try
                {
                    exit.SetResult(ElevatedWorker.Run(options, () => true, execute, TimeSpan.FromSeconds(10)));
                }
                catch (Exception exception)
                {
                    exit.SetException(exception);
                }
            });
            return Process.GetCurrentProcess();
        })
        {
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

    private static void SendForgedReport(string pipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(10_000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(WorkerProtocol.Serialize(new WorkerMessage(null, Report(true))));
        }
        catch (IOException)
        {
            // The runner hung up on the stranger, as it should.
        }
        catch (TimeoutException)
        {
        }
    }

    private static Process StartHidden(string arguments)
        => Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

    private static void KillQuietly(int? processId)
    {
        if (processId is not { } id)
            return;

        try
        {
            using var process = Process.GetProcessById(id);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }
}
