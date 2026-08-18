namespace BluetoothHandsFreeToggle.Core;

public interface IServiceManager
{
    ServiceSnapshot GetSnapshot(TargetService target);
    bool TryStopService(string serviceName, TimeSpan timeout, out string? errorMessage);
    bool TryStartService(string serviceName, TimeSpan timeout, out string? errorMessage);
    bool TrySetStartType(string serviceName, ServiceStartType startType, out string? errorMessage);
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
