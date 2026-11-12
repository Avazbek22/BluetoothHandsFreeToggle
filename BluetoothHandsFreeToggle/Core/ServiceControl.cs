using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Core;

public enum ServiceActionOutcome
{
    AlreadyInState,
    Completed,

    /// <summary>Completed, but only after terminating the hung service host process.</summary>
    Recovered,

    /// <summary>The service is hung and could not be recovered safely; a Windows restart is required.</summary>
    RebootRequired,

    Failed
}

/// <param name="Error">Localized reason for <see cref="ServiceActionOutcome.Failed"/> or <see cref="ServiceActionOutcome.RebootRequired"/>.</param>
/// <param name="RecoveryNote">Localized note when a hung host process was terminated (may accompany any outcome).</param>
public sealed record ServiceActionResult(
    ServiceActionOutcome Outcome,
    string? Error,
    string? RecoveryNote)
{
    public bool Succeeded => Outcome is
        ServiceActionOutcome.AlreadyInState or
        ServiceActionOutcome.Completed or
        ServiceActionOutcome.Recovered;
}

/// <summary>
/// Drives a single service to Stopped or Running the way the SCM protocol intends:
/// it follows the service's checkpoint and wait hint instead of a fixed timeout, tells a slow
/// transition from a hung one, and recovers a hung service by terminating its host process
/// only when that process hosts nothing but supported HFP services.
/// Every loop is bounded, and a service is never force-terminated twice in one action.
/// </summary>
public sealed class ServiceControl
{
    private const int MaxIterations = 6;

    private static readonly string[] HostAllowList =
        Targets.Services.Select(target => target.ServiceName).ToArray();

    private readonly IServiceManager _services;
    private readonly IOperationClock _clock;
    private readonly ServiceTimings _timings;
    private readonly IProgress<OperationEvent>? _progress;

    public ServiceControl(
        IServiceManager services,
        IOperationClock clock,
        ServiceTimings timings,
        IProgress<OperationEvent>? progress)
    {
        _services = services;
        _clock = clock;
        _timings = timings;
        _progress = progress;
    }

    public ServiceActionResult EnsureStopped(TargetService target)
    {
        Report(OperationEventKind.Step, Text.Format("progress.stopping", target.FriendlyName));
        var session = new Session(target);
        var lastState = ServiceRunState.Unknown;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var query = _services.QueryStatus(target.ServiceName);
            if (!query.Success)
                return session.Fail(QueryFailed(target, query.Error));

            var status = query.Status!;
            lastState = status.RunState;
            WaitResult wait;

            switch (status.RunState)
            {
                case ServiceRunState.Stopped:
                    return session.Finish(changedAnything: iteration > 0);

                case ServiceRunState.Running:
                case ServiceRunState.Paused:
                    var stop = _services.SendStop(target.ServiceName);
                    if (stop.Outcome is ServiceCommandOutcome.AlreadyInState)
                        continue;

                    if (stop.Outcome is ServiceCommandOutcome.Failed)
                        return session.Fail(DescribeStopFailure(target, stop));

                    wait = WaitFor(target, ServiceRunState.Stopped, "progress.waitingStop");
                    break;

                case ServiceRunState.StopPending:
                    wait = WaitFor(target, ServiceRunState.Stopped, "progress.waitingStop");
                    break;

                case ServiceRunState.StartPending:
                case ServiceRunState.ContinuePending:
                case ServiceRunState.PausePending:
                    // A pending service cannot accept a stop request; let it settle first.
                    wait = WaitFor(target, null, "progress.waitingSettle");
                    break;

                default:
                    return session.Fail(Text.Format("service.stateUnknown", target.FriendlyName));
            }

            var failure = HandleWait(session, wait);
            if (failure is not null)
                return failure;
        }

        return session.Fail(UnexpectedState(target, ServiceRunState.Stopped, lastState));
    }

    public ServiceActionResult EnsureRunning(TargetService target)
    {
        Report(OperationEventKind.Step, Text.Format("progress.starting", target.FriendlyName));
        var session = new Session(target);
        var startAttempts = 0;
        var recoveredSinceLastStart = false;
        var lastState = ServiceRunState.Unknown;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var query = _services.QueryStatus(target.ServiceName);
            if (!query.Success)
                return session.Fail(QueryFailed(target, query.Error));

            var status = query.Status!;
            lastState = status.RunState;
            WaitResult wait;

            switch (status.RunState)
            {
                case ServiceRunState.Running:
                    return session.Finish(changedAnything: iteration > 0);

                case ServiceRunState.Stopped:
                    // A start was already sent and the service ended up Stopped again without being
                    // force-recovered in between: it exited on its own, so retrying would just loop.
                    if (startAttempts > 0 && !recoveredSinceLastStart)
                    {
                        return session.Fail(Text.Format(
                            "service.exitedDuringStart",
                            target.FriendlyName,
                            status.Win32ExitCode,
                            status.ServiceSpecificExitCode));
                    }

                    var start = _services.SendStart(target.ServiceName);
                    if (start.Outcome is ServiceCommandOutcome.Failed)
                        return session.Fail(DescribeStartFailure(target, start));

                    startAttempts++;
                    recoveredSinceLastStart = false;
                    wait = WaitFor(target, ServiceRunState.Running, "progress.waitingStart");
                    break;

                case ServiceRunState.StopPending:
                    wait = WaitFor(target, ServiceRunState.Stopped, "progress.waitingStop");
                    break;

                case ServiceRunState.StartPending:
                case ServiceRunState.ContinuePending:
                    wait = WaitFor(target, ServiceRunState.Running, "progress.waitingStart");
                    break;

                case ServiceRunState.PausePending:
                    wait = WaitFor(target, null, "progress.waitingSettle");
                    break;

                case ServiceRunState.Paused:
                    // Paused is never a healthy state for an HFP service; cycle it through Stopped.
                    var stop = _services.SendStop(target.ServiceName);
                    if (stop.Outcome is ServiceCommandOutcome.Failed)
                        return session.Fail(DescribeStopFailure(target, stop));

                    wait = WaitFor(target, ServiceRunState.Stopped, "progress.waitingStop");
                    break;

                default:
                    return session.Fail(Text.Format("service.stateUnknown", target.FriendlyName));
            }

            var recoveriesBefore = session.Recoveries;
            var failure = HandleWait(session, wait);
            if (failure is not null)
                return failure;

            if (session.Recoveries > recoveriesBefore)
                recoveredSinceLastStart = true;
        }

        return session.Fail(UnexpectedState(target, ServiceRunState.Running, lastState));
    }

    /// <summary>
    /// Waits for an in-flight transition (StartPending, ContinuePending, PausePending) to finish
    /// without sending any command, so a backup can record a stable state.
    /// A hung transition is left untouched here.
    /// </summary>
    public ServiceStatus? WaitForStableState(TargetService target)
    {
        var wait = WaitFor(target, null, "progress.waitingSettle");
        return wait.Status;
    }

    /// <returns>A terminal result, or <c>null</c> when the caller should re-evaluate the state.</returns>
    private ServiceActionResult? HandleWait(Session session, WaitResult wait)
    {
        switch (wait.Outcome)
        {
            case WaitOutcome.Reached:
            case WaitOutcome.Unexpected:
                return null;

            case WaitOutcome.QueryFailed:
                return session.Fail(QueryFailed(session.Target, wait.Error));

            case WaitOutcome.Hung:
                return Recover(session, wait.Status!, wait.Stalled);

            default:
                throw new InvalidOperationException($"Unsupported wait outcome {wait.Outcome}.");
        }
    }

    private ServiceActionResult? Recover(Session session, ServiceStatus hung, TimeSpan stalled)
    {
        var target = session.Target;
        var processId = hung.ProcessId;

        // Never terminate twice: a service that hangs again right after recovery needs a restart.
        if (session.Recoveries > 0)
            return session.RequireReboot(Text.Format("service.hungAgain", target.FriendlyName));

        if (processId <= 0)
        {
            return session.RequireReboot(Text.Format(
                "service.hungTerminateFailed",
                target.FriendlyName,
                processId,
                Text.Get("service.unknownError")));
        }

        Report(
            OperationEventKind.Warning,
            Text.Format("progress.notResponding", target.FriendlyName, WholeSeconds(stalled), processId));

        var termination = _services.TerminateHungServiceHost(
            target.ServiceName,
            processId,
            HostAllowList);

        switch (termination.Outcome)
        {
            case HostTerminationOutcome.Terminated:
                session.Recoveries++;
                session.RecoveryNote = Text.Format("service.recovered", target.FriendlyName, processId);
                Report(
                    OperationEventKind.Success,
                    Text.Format("progress.terminated", target.FriendlyName, processId));
                WaitUntilHostExitIsRegistered(target, processId);
                return null;

            case HostTerminationOutcome.StateChanged:
                // The service finished its transition on its own just before termination.
                return null;

            case HostTerminationOutcome.SharedHost:
                return session.RequireReboot(Text.Format(
                    "service.hungSharedHost",
                    target.FriendlyName,
                    processId,
                    string.Join(", ", termination.OtherServices)));

            case HostTerminationOutcome.UnexpectedHost:
                return session.RequireReboot(Text.Format(
                    "service.hungUnexpectedHost",
                    target.FriendlyName,
                    processId));

            case HostTerminationOutcome.UnsafeRecoveryAction:
                return session.RequireReboot(Text.Format(
                    "service.hungUnsafeRecovery",
                    target.FriendlyName,
                    processId,
                    string.Join(", ", termination.OtherServices)));

            default:
                return session.RequireReboot(Text.Format(
                    "service.hungTerminateFailed",
                    target.FriendlyName,
                    processId,
                    termination.Error ?? Text.Get("service.unknownError")));
        }
    }

    private void WaitUntilHostExitIsRegistered(TargetService target, int terminatedProcessId)
    {
        var started = _clock.Elapsed;
        while (_clock.Elapsed - started < _timings.TerminationSettle)
        {
            var query = _services.QueryStatus(target.ServiceName);
            if (!query.Success)
                return;

            var status = query.Status!;
            if (!status.IsPending || status.ProcessId != terminatedProcessId)
                return;

            _clock.Sleep(_timings.PollInterval);
        }
    }

    /// <param name="target">Desired stable state, or <c>null</c> for "any non-pending state".</param>
    private WaitResult WaitFor(TargetService service, ServiceRunState? target, string waitingKey)
    {
        var started = _clock.Elapsed;
        var lastProgressAt = started;
        var nextReportAt = started + _timings.ProgressInterval;
        ServiceStatus? previous = null;

        while (true)
        {
            var query = _services.QueryStatus(service.ServiceName);
            if (!query.Success)
                return WaitResult.QueryFailed(query.Error);

            var status = query.Status!;
            var now = _clock.Elapsed;

            var reached = target is { } desired
                ? status.RunState == desired
                : !status.IsPending;
            if (reached)
                return WaitResult.Reached(status);

            if (!status.IsPending)
            {
                // The SCM may need a moment to publish the effect of a command we just sent.
                if (now - started >= _timings.StateChangeGrace)
                    return WaitResult.Unexpected(status);
            }
            else if (previous is null ||
                     previous.RunState != status.RunState ||
                     previous.CheckPoint != status.CheckPoint ||
                     previous.ProcessId != status.ProcessId)
            {
                lastProgressAt = now;
            }

            previous = status;

            if (status.IsPending)
            {
                var stalled = now - lastProgressAt;
                var stallLimit = Clamp(status.WaitHint, _timings.MinimumStall, _timings.MaximumStall);
                var total = now - started;

                if (stalled >= stallLimit)
                    return WaitResult.Hung(status, stalled);

                if (total >= _timings.MaximumTransition)
                    return WaitResult.Hung(status, total);
            }

            if (now >= nextReportAt)
            {
                var elapsed = now - started;
                Report(
                    OperationEventKind.Waiting,
                    Text.Format(waitingKey, service.FriendlyName, WholeSeconds(elapsed)),
                    elapsed);
                nextReportAt = now + _timings.ProgressInterval;
            }

            _clock.Sleep(_timings.PollInterval);
        }
    }

    private void Report(OperationEventKind kind, string message, TimeSpan? elapsed = null)
        => _progress?.Report(new OperationEvent(kind, message, elapsed));

    private static string QueryFailed(TargetService target, string? error)
        => Text.Format(
            "service.statusQueryFailed",
            target.FriendlyName,
            error ?? Text.Get("service.unknownError"));

    private static string UnexpectedState(
        TargetService target,
        ServiceRunState expected,
        ServiceRunState actual)
        => Text.Format(
            "service.unexpectedState",
            target.FriendlyName,
            ServiceStateText.Get(expected),
            ServiceStateText.Get(actual));

    private static string DescribeStopFailure(TargetService target, ServiceCommandResult result)
        => result.ErrorCode switch
        {
            Win32ErrorCodes.DependentServicesRunning
                => Text.Format("service.dependentsRunning", target.FriendlyName),
            Win32ErrorCodes.InvalidServiceControl
                => Text.Format("service.stopNotAccepted", target.FriendlyName),
            _ => Text.Format(
                "service.stopFailed",
                target.FriendlyName,
                result.Error ?? Text.Get("service.unknownError"))
        };

    private static string DescribeStartFailure(TargetService target, ServiceCommandResult result)
        => result.ErrorCode == Win32ErrorCodes.ServiceDisabled
            ? Text.Format("service.startDisabled", target.FriendlyName)
            : Text.Format(
                "service.startFailed",
                target.FriendlyName,
                result.Error ?? Text.Get("service.unknownError"));

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
        => value < minimum ? minimum : value > maximum ? maximum : value;

    private static int WholeSeconds(TimeSpan value)
        => (int)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero);

    private sealed class Session(TargetService target)
    {
        public TargetService Target { get; } = target;
        public int Recoveries { get; set; }
        public string? RecoveryNote { get; set; }

        public ServiceActionResult Finish(bool changedAnything)
        {
            var outcome = Recoveries > 0
                ? ServiceActionOutcome.Recovered
                : changedAnything
                    ? ServiceActionOutcome.Completed
                    : ServiceActionOutcome.AlreadyInState;
            return new ServiceActionResult(outcome, null, RecoveryNote);
        }

        public ServiceActionResult Fail(string error)
            => new(ServiceActionOutcome.Failed, error, RecoveryNote);

        public ServiceActionResult RequireReboot(string reason)
            => new(ServiceActionOutcome.RebootRequired, reason, RecoveryNote);
    }

    private enum WaitOutcome
    {
        Reached,
        Unexpected,
        Hung,
        QueryFailed
    }

    private sealed record WaitResult(
        WaitOutcome Outcome,
        ServiceStatus? Status,
        TimeSpan Stalled,
        string? Error)
    {
        public static WaitResult Reached(ServiceStatus status) => new(WaitOutcome.Reached, status, TimeSpan.Zero, null);
        public static WaitResult Unexpected(ServiceStatus status) => new(WaitOutcome.Unexpected, status, TimeSpan.Zero, null);
        public static WaitResult Hung(ServiceStatus status, TimeSpan stalled) => new(WaitOutcome.Hung, status, stalled, null);
        public static WaitResult QueryFailed(string? error) => new(WaitOutcome.QueryFailed, null, TimeSpan.Zero, error);
    }
}
