using System.Diagnostics;

namespace StereoKeeper.Core;

public enum OperationEventKind
{
    /// <summary>A new step started (stopping, starting, changing startup...).</summary>
    Step,

    /// <summary>A periodic "still waiting" update; UIs may overwrite the previous one in place.</summary>
    Waiting,

    Warning,
    Success
}

/// <summary>What an operation is doing, so a UI can show its own short step labels.</summary>
public enum OperationStep
{
    None,
    Stopping,
    Starting,
    Disabling,
    SettingStartup,
    WaitingStop,
    WaitingStart,
    WaitingSettle,
    NotResponding,
    Terminated
}

/// <summary>
/// Live feedback emitted while an operation runs. Reports are delivered synchronously on the
/// operation thread; the panel marshals them to its UI thread itself.
/// </summary>
/// <param name="Message">The full localized message, as the report shows it.</param>
/// <param name="Step">The structured step behind <paramref name="Message"/>.</param>
/// <param name="Service">The friendly name of the service the step applies to.</param>
public sealed record OperationEvent(
    OperationEventKind Kind,
    string Message,
    TimeSpan? Elapsed = null,
    OperationStep Step = OperationStep.None,
    string? Service = null);

/// <summary>Monotonic time source, replaceable in tests so hang detection runs instantly.</summary>
public interface IOperationClock
{
    TimeSpan Elapsed { get; }
    void Sleep(TimeSpan duration);
}

public sealed class SystemOperationClock : IOperationClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public void Sleep(TimeSpan duration) => Thread.Sleep(duration);
}

/// <param name="PollInterval">How often the service status is polled while waiting.</param>
/// <param name="MinimumStall">Shortest time without checkpoint progress before a service is considered hung.</param>
/// <param name="MaximumStall">Longest time without progress, even if the service reports a larger wait hint.</param>
/// <param name="MaximumTransition">Hard cap for one transition, even while the checkpoint keeps advancing.</param>
/// <param name="StateChangeGrace">How long a just-sent command may take to become visible in the SCM status.</param>
/// <param name="ProgressInterval">How often "still waiting" progress is reported.</param>
/// <param name="TerminationSettle">How long to wait for the SCM to register a terminated host process.</param>
public sealed record ServiceTimings(
    TimeSpan PollInterval,
    TimeSpan MinimumStall,
    TimeSpan MaximumStall,
    TimeSpan MaximumTransition,
    TimeSpan StateChangeGrace,
    TimeSpan ProgressInterval,
    TimeSpan TerminationSettle)
{
    public static ServiceTimings Default { get; } = new(
        PollInterval: TimeSpan.FromMilliseconds(250),
        MinimumStall: TimeSpan.FromSeconds(5),
        MaximumStall: TimeSpan.FromSeconds(15),
        MaximumTransition: TimeSpan.FromSeconds(60),
        StateChangeGrace: TimeSpan.FromSeconds(1),
        ProgressInterval: TimeSpan.FromSeconds(1),
        TerminationSettle: TimeSpan.FromSeconds(10));
}
