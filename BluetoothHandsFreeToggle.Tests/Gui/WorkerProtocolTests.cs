using System.IO.Pipes;
using System.Text;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using Xunit;
using static BluetoothHandsFreeToggle.Tests.Gui.Fixtures;

namespace BluetoothHandsFreeToggle.Tests.Gui;

public sealed class WorkerProtocolTests
{
    [Fact]
    public void ProgressEventRoundTrips()
    {
        var original = new OperationEvent(
            OperationEventKind.Waiting,
            "Waiting for Bluetooth Audio Gateway (HFP) to stop... 4 s",
            TimeSpan.FromSeconds(4.25),
            OperationStep.WaitingStop,
            "Bluetooth Audio Gateway (HFP)");

        var line = WorkerProtocol.Serialize(new WorkerMessage(original, null));
        var message = WorkerProtocol.TryDeserialize(line);

        Assert.Equal(original, message?.Event);
        Assert.Null(message?.Report);
        Assert.DoesNotContain('\n', line);
        Assert.Contains("\"WaitingStop\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportRoundTripsWithSnapshotsAndLines()
    {
        var original = Report(false, rebootRequired: true, "[WARN] Restart Windows.", "Zeile mit Umlauten: äöü — ok");

        var message = WorkerProtocol.TryDeserialize(WorkerProtocol.Serialize(new WorkerMessage(null, original)));

        var report = Assert.IsType<ToggleReport>(message?.Report);
        Assert.Equal(original.Title, report.Title);
        Assert.False(report.Success);
        Assert.True(report.RebootRequired);
        Assert.Equal(original.Lines, report.Lines);
        Assert.Equal(original.Snapshots, report.Snapshots);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"Event\":{\"Kind\":\"Nonsense\",\"Message\":\"x\"}}")]
    public void UnreadableLinesAreIgnored(string line) => Assert.Null(WorkerProtocol.TryDeserialize(line));

    [Fact]
    public void OversizedLineIsRejectedWithoutParsing()
    {
        var line = "{\"Report\":null,\"x\":\"" + new string('a', WorkerProtocol.MaxLineLength) + "\"}";

        Assert.Null(WorkerProtocol.TryDeserialize(line));
    }

    [Fact]
    public async Task ReadPassesProgressOnAndReturnsTheReport()
    {
        var first = new OperationEvent(OperationEventKind.Step, "Stopping", Step: OperationStep.Stopping, Service: "A");
        var second = new OperationEvent(OperationEventKind.Success, "Started", Step: OperationStep.Starting, Service: "A");
        var text = string.Join('\n',
            WorkerProtocol.Serialize(new WorkerMessage(first, null)),
            "garbage",
            WorkerProtocol.Serialize(new WorkerMessage(second, null)),
            WorkerProtocol.Serialize(new WorkerMessage(null, Report(true))));
        var received = new List<OperationEvent>();

        var report = await WorkerProtocol.ReadAsync(new StringReader(text), received.Add);

        Assert.Equal([first, second], received);
        Assert.True(report?.Success);
    }

    [Fact]
    public async Task WorkerThatEndsWithoutAReportYieldsNothing()
    {
        var line = WorkerProtocol.Serialize(new WorkerMessage(new OperationEvent(OperationEventKind.Step, "x"), null));

        var report = await WorkerProtocol.ReadAsync(new StringReader(line), _ => { });

        Assert.Null(report);
    }

    /// <summary>The same pipe setup the tray app and the elevated worker use, minus the UAC prompt.</summary>
    [Fact]
    public async Task MessagesSurviveACurrentUserOnlyNamedPipe()
    {
        var pipeName = $"BluetoothHandsFreeToggle.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var worker = Task.Run(() =>
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(5000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            for (var second = 1; second <= 3; second++)
            {
                writer.WriteLine(WorkerProtocol.Serialize(new WorkerMessage(
                    new OperationEvent(OperationEventKind.Waiting, $"{second}", TimeSpan.FromSeconds(second), OperationStep.WaitingStart),
                    null)));
            }

            writer.WriteLine(WorkerProtocol.Serialize(new WorkerMessage(null, Report(true, false, "Готово"))));
        });

        await server.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(server, new UTF8Encoding(false));
        var received = new List<OperationEvent>();

        var report = await WorkerProtocol.ReadAsync(reader, received.Add).WaitAsync(TimeSpan.FromSeconds(10));
        await worker;

        Assert.Equal([1d, 2d, 3d], received.Select(e => e.Elapsed!.Value.TotalSeconds));
        Assert.Equal(["Готово"], report?.Lines);
    }
}
