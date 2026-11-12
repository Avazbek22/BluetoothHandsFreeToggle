namespace BluetoothHandsFreeToggle.Core;

/// <summary>
/// Thin, non-blocking access to the Windows Service Control Manager.
/// All waiting, hang detection and recovery decisions live in <see cref="ServiceControl"/>
/// so they can be tested deterministically.
/// </summary>
public interface IServiceManager
{
    ServiceSnapshot GetSnapshot(TargetService target);

    ServiceQueryResult QueryStatus(string serviceName);

    /// <summary>Sends SERVICE_CONTROL_STOP and returns immediately.</summary>
    ServiceCommandResult SendStop(string serviceName);

    /// <summary>Calls StartService and returns immediately.</summary>
    ServiceCommandResult SendStart(string serviceName);

    bool TrySetStartType(string serviceName, ServiceStartType startType, out string? errorMessage);

    /// <summary>
    /// Terminates the process hosting a hung service, but only after re-verifying, while holding
    /// a handle to that process, that the service is still pending in <paramref name="expectedProcessId"/>,
    /// that every service hosted by the process is in <paramref name="allowedServices"/>, and that the
    /// process is the standard Windows service host.
    /// </summary>
    HostTerminationResult TerminateHungServiceHost(
        string serviceName,
        int expectedProcessId,
        IReadOnlyCollection<string> allowedServices);
}

public static class Win32ErrorCodes
{
    public const int AccessDenied = 5;
    public const int DependentServicesRunning = 1051;
    public const int InvalidServiceControl = 1052;
    public const int ServiceAlreadyRunning = 1056;
    public const int ServiceDisabled = 1058;
    public const int ServiceDoesNotExist = 1060;
    public const int ServiceCannotAcceptControl = 1061;
    public const int ServiceNotActive = 1062;
}

public sealed record ServiceQueryResult(ServiceStatus? Status, string? Error)
{
    public bool Success => Status is not null;

    public static ServiceQueryResult Ok(ServiceStatus status) => new(status, null);
    public static ServiceQueryResult Failed(string error) => new(null, error);
}

public enum ServiceCommandOutcome
{
    /// <summary>Windows accepted the request; the transition may still be in progress.</summary>
    Accepted,

    /// <summary>The service is already in the requested state (ERROR_SERVICE_NOT_ACTIVE / ALREADY_RUNNING).</summary>
    AlreadyInState,

    /// <summary>The service is mid-transition and cannot accept the request (ERROR_SERVICE_CANNOT_ACCEPT_CTRL).</summary>
    NotAccepted,

    Failed
}

public sealed record ServiceCommandResult(
    ServiceCommandOutcome Outcome,
    int ErrorCode,
    string? Error)
{
    public static ServiceCommandResult Accepted { get; } = new(ServiceCommandOutcome.Accepted, 0, null);
    public static ServiceCommandResult AlreadyInState { get; } = new(ServiceCommandOutcome.AlreadyInState, 0, null);
    public static ServiceCommandResult NotAccepted { get; } = new(ServiceCommandOutcome.NotAccepted, 0, null);

    public static ServiceCommandResult Failed(int errorCode, string error)
        => new(ServiceCommandOutcome.Failed, errorCode, error);
}

public enum HostTerminationOutcome
{
    Terminated,

    /// <summary>The service is no longer hung in that process (it finished, or its PID changed).</summary>
    StateChanged,

    /// <summary>The process also hosts services outside the allow-list, so it was left alone.</summary>
    SharedHost,

    /// <summary>The process is not %SystemRoot%\System32\svchost.exe, so it was left alone.</summary>
    UnexpectedHost,

    Failed
}

public sealed record HostTerminationResult(
    HostTerminationOutcome Outcome,
    IReadOnlyList<string> OtherServices,
    string? Error)
{
    public static HostTerminationResult Terminated { get; } = new(HostTerminationOutcome.Terminated, [], null);
    public static HostTerminationResult StateChanged { get; } = new(HostTerminationOutcome.StateChanged, [], null);

    public static HostTerminationResult SharedHost(IReadOnlyList<string> otherServices)
        => new(HostTerminationOutcome.SharedHost, otherServices, null);

    public static HostTerminationResult UnexpectedHost(string detail)
        => new(HostTerminationOutcome.UnexpectedHost, [], detail);

    public static HostTerminationResult Failed(string error)
        => new(HostTerminationOutcome.Failed, [], error);
}

public interface IBackupStore
{
    string BackupPath { get; }
    BackupWriteResult Prepare();
    BackupLoadResult Load();
    BackupWriteResult Save(IEnumerable<ServiceSnapshot> snapshots);
    BackupWriteResult Delete();
}

public sealed record BackupServiceState(
    bool Exists,
    ServiceRunState RunState,
    ServiceStartType StartType);

public sealed record BackupFile(
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    Dictionary<string, BackupServiceState> Services);

public sealed record BackupLoadResult(BackupFile? Backup, string? Error)
{
    public bool Found => Backup is not null;
    public bool Success => Error is null;

    public static BackupLoadResult Missing() => new(null, null);
    public static BackupLoadResult Loaded(BackupFile backup) => new(backup, null);
    public static BackupLoadResult Failed(string error) => new(null, error);
}

public sealed record BackupWriteResult(bool Success, string? Error)
{
    public static BackupWriteResult Ok() => new(true, null);
    public static BackupWriteResult Failed(string error) => new(false, error);
}
