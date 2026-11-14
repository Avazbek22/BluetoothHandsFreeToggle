namespace StereoKeeper.Core;

public enum ServiceRunState
{
    NotPresent,
    Running,
    Stopped,
    StartPending,
    StopPending,
    Paused,
    PausePending,
    ContinuePending,
    Unknown
}

public enum ServiceStartType
{
    NotPresent,
    Auto,
    Manual,
    Disabled,
    Unknown
}

public sealed record ServiceSnapshot(
    string ServiceName,
    string FriendlyName,
    bool Exists,
    bool QuerySucceeded,
    ServiceRunState RunState,
    ServiceStartType StartType,
    int? NativeStartValue,
    string? Note);

/// <summary>
/// Live Service Control Manager status, including the progress fields
/// (checkpoint and wait hint) used to tell a slow transition from a hung one.
/// </summary>
public sealed record ServiceStatus(
    ServiceRunState RunState,
    int ProcessId,
    int CheckPoint,
    TimeSpan WaitHint,
    int Win32ExitCode,
    int ServiceSpecificExitCode)
{
    public bool IsPending => ServiceRunStates.IsPending(RunState);
}

public static class ServiceRunStates
{
    public static bool IsPending(ServiceRunState state)
        => state is
            ServiceRunState.StartPending or
            ServiceRunState.StopPending or
            ServiceRunState.ContinuePending or
            ServiceRunState.PausePending;

    /// <summary>
    /// A service that was in any state other than Stopped had been started, so Restore
    /// returns it to Running. This includes a hung StopPending state recorded by older versions.
    /// </summary>
    public static bool WasActive(ServiceRunState state)
        => state is not (
            ServiceRunState.Stopped or
            ServiceRunState.NotPresent or
            ServiceRunState.Unknown);
}
