using System.Text.Json;
using System.Text.Json.Serialization;
using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.App;

/// <summary>
/// One line of the elevated worker's output: progress events while the operation runs, then
/// exactly one report. The worker only writes; it never reads commands from the pipe.
/// </summary>
public sealed record WorkerMessage(OperationEvent? Event, ToggleReport? Report);

[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WorkerMessage))]
internal sealed partial class WorkerJsonContext : JsonSerializerContext;

public static class WorkerProtocol
{
    /// <summary>Longest accepted line; a report is a few kilobytes.</summary>
    public const int MaxLineLength = 1024 * 1024;

    public static string Serialize(WorkerMessage message)
        => JsonSerializer.Serialize(message, WorkerJsonContext.Default.WorkerMessage);

    public static WorkerMessage? TryDeserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLineLength)
            return null;

        try
        {
            return JsonSerializer.Deserialize(line, WorkerJsonContext.Default.WorkerMessage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the worker's lines until it closes the pipe, passing progress on as it arrives.
    /// Unreadable lines are skipped.
    /// </summary>
    /// <returns>The report, or <c>null</c> when the worker ended without sending one.</returns>
    public static async Task<ToggleReport?> ReadAsync(
        TextReader reader,
        Action<OperationEvent> onEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(onEvent);

        ToggleReport? report = null;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var message = TryDeserialize(line);
            if (message?.Event is { } progress)
                onEvent(progress);
            else if (message?.Report is { } received)
                report = received;
        }

        return report;
    }
}
