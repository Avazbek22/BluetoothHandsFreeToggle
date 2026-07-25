using System.Globalization;
using BluetoothHandsFreeToggle.Localization;
using BluetoothHandsFreeToggle.Windows;

namespace BluetoothHandsFreeToggle.Core;

public sealed class ToggleEngine
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OperationLockTimeout = TimeSpan.FromSeconds(3);

    private readonly IServiceManager _services;
    private readonly IBackupStore _backup;
    private readonly string _mutexName;

    public ToggleEngine()
        : this(new WindowsServiceManager(), new BackupStore())
    {
    }

    public ToggleEngine(
        IServiceManager services,
        IBackupStore backup,
        string mutexName = "BluetoothHandsFreeToggle.Operation.v2")
    {
        _services = services;
        _backup = backup;
        _mutexName = mutexName;
    }

    public ToggleReport GetStatusReport()
    {
        var snapshots = GetSnapshots();
        var lines = new List<string>();
        var errors = new List<string>();

        AddQueryErrors(snapshots, errors);

        var backup = _backup.Load();
        if (!backup.Success)
        {
            errors.Add(backup.Error!);
        }
        else if (backup.Found)
        {
            lines.Add(Text.Format(
                "status.backupAvailable",
                backup.Backup!.CreatedUtc.LocalDateTime.ToString(
                    "G",
                    CultureInfo.CurrentCulture)));
        }
        else
        {
            lines.Add(Text.Get("status.backupMissing"));
        }

        return BuildReport(
            Text.Get("title.status"),
            Text.Get("title.statusWithErrors"),
            snapshots,
            lines,
            errors);
    }

    public ToggleReport SoftResetHandsFree()
        => RunExclusive(Text.Get("title.softFailed"), SoftResetCore);

    public ToggleReport HardDisableHandsFree()
        => RunExclusive(Text.Get("title.hardFailed"), HardDisableCore);

    public ToggleReport RestoreHandsFree()
        => RunExclusive(Text.Get("title.restoreFailed"), RestoreCore);

    private ToggleReport SoftResetCore()
    {
        var before = GetSnapshots();
        var lines = new List<string>
        {
            Text.Get("soft.intro"),
            Text.Get("soft.preserveMicrophone"),
            Text.Get("soft.closeMicrophoneApps")
        };
        var errors = new List<string>();
        var warnings = new List<string>();

        AddQueryErrors(before, errors);
        if (errors.Count > 0)
            return BuildReport(
                Text.Get("title.softComplete"),
                Text.Get("title.softFailed"),
                before,
                lines,
                errors,
                warnings);

        var present = before.Where(snapshot => snapshot.Exists).ToList();
        if (present.Count == 0)
        {
            errors.Add(Text.Get("service.noneFound"));
            return BuildReport(
                Text.Get("title.softComplete"),
                Text.Get("title.softFailed"),
                before,
                lines,
                errors,
                warnings);
        }

        foreach (var snapshot in present)
        {
            if (snapshot.StartType is ServiceStartType.Disabled)
            {
                errors.Add(Text.Format(
                    "soft.disabledUseRestore",
                    snapshot.FriendlyName));
                continue;
            }

            if (snapshot.RunState is ServiceRunState.Stopped)
            {
                lines.Add(Text.Format("soft.alreadyStopped", snapshot.FriendlyName));
                continue;
            }

            if (snapshot.RunState is ServiceRunState.Unknown)
            {
                errors.Add(Text.Format("service.stateUnknown", snapshot.FriendlyName));
                continue;
            }

            if (!_services.TryStopService(snapshot.ServiceName, ServiceTimeout, out var stopError))
            {
                errors.Add(Text.Format(
                    "service.stopFailed",
                    snapshot.FriendlyName,
                    stopError));
                continue;
            }

            if (!_services.TryStartService(snapshot.ServiceName, ServiceTimeout, out var startError))
            {
                errors.Add(Text.Format(
                    "soft.restartFailed",
                    snapshot.FriendlyName,
                    startError));
                continue;
            }

            lines.Add(Text.Format("soft.restarted", snapshot.FriendlyName));
        }

        var after = GetSnapshots();
        AddQueryErrors(after, errors);
        VerifySoftResult(before, after, errors);

        return BuildReport(
            Text.Get("title.softComplete"),
            Text.Get("title.softPartial"),
            after,
            lines,
            errors,
            warnings);
    }

    private ToggleReport HardDisableCore()
    {
        var before = GetSnapshots();
        var lines = new List<string>
        {
            Text.Get("hard.intro"),
            Text.Get("hard.microphoneUnavailable")
        };
        var errors = new List<string>();
        var warnings = new List<string>();

        AddQueryErrors(before, errors);
        if (errors.Count > 0)
            return BuildReport(
                Text.Get("title.hardComplete"),
                Text.Get("title.hardFailed"),
                before,
                lines,
                errors,
                warnings);

        if (before.All(snapshot => !snapshot.Exists))
        {
            errors.Add(Text.Get("service.noneFound"));
            return BuildReport(
                Text.Get("title.hardComplete"),
                Text.Get("title.hardFailed"),
                before,
                lines,
                errors,
                warnings);
        }

        var backup = _backup.Load();
        if (!backup.Success)
        {
            errors.Add(backup.Error!);
            errors.Add(Text.Get("hard.noConfigurationChanged"));
            return BuildReport(
                Text.Get("title.hardComplete"),
                Text.Get("title.hardFailed"),
                before,
                lines,
                errors,
                warnings);
        }

        if (backup.Found)
        {
            lines.Add(Text.Format("hard.existingBackup", _backup.BackupPath));
        }
        else
        {
            var save = _backup.Save(before);
            if (!save.Success)
            {
                errors.Add(save.Error!);
                errors.Add(Text.Get("hard.noConfigurationChangedNoBackup"));
                return BuildReport(
                    Text.Get("title.hardComplete"),
                    Text.Get("title.hardFailed"),
                    before,
                    lines,
                    errors,
                    warnings);
            }

            lines.Add(Text.Format("hard.originalSaved", _backup.BackupPath));
        }

        foreach (var snapshot in before.Where(snapshot => snapshot.Exists))
        {
            if (!_services.TryStopService(snapshot.ServiceName, ServiceTimeout, out var stopError))
                errors.Add(Text.Format(
                    "service.stopFailed",
                    snapshot.FriendlyName,
                    stopError));

            if (!_services.TrySetStartType(
                    snapshot.ServiceName,
                    ServiceStartType.Disabled,
                    out var startupError))
            {
                errors.Add(Text.Format(
                    "service.disableStartupFailed",
                    snapshot.FriendlyName,
                    startupError));
                continue;
            }

            lines.Add(Text.Format("service.startupDisabled", snapshot.FriendlyName));
        }

        var after = GetSnapshots();
        AddQueryErrors(after, errors);
        VerifyHardResult(after, errors);

        return BuildReport(
            Text.Get("title.hardComplete"),
            Text.Get("title.hardPartial"),
            after,
            lines,
            errors,
            warnings);
    }

    private ToggleReport RestoreCore()
    {
        var before = GetSnapshots();
        var lines = new List<string>
        {
            Text.Get("restore.intro")
        };
        var errors = new List<string>();
        var warnings = new List<string>();

        AddQueryErrors(before, errors);
        if (errors.Count > 0)
            return BuildReport(
                Text.Get("title.restoreComplete"),
                Text.Get("title.restoreFailed"),
                before,
                lines,
                errors,
                warnings);

        if (before.All(snapshot => !snapshot.Exists))
        {
            errors.Add(Text.Get("service.noneFound"));
            return BuildReport(
                Text.Get("title.restoreComplete"),
                Text.Get("title.restoreFailed"),
                before,
                lines,
                errors,
                warnings);
        }

        var load = _backup.Load();
        if (!load.Success)
        {
            errors.Add(load.Error!);
            errors.Add(Text.Get("restore.unreadableBackup"));
            return BuildReport(
                Text.Get("title.restoreComplete"),
                Text.Get("title.restoreFailed"),
                before,
                lines,
                errors,
                warnings);
        }

        if (!load.Found)
        {
            warnings.Add(Text.Get("restore.missingBackup"));
        }
        else
        {
            lines.Add(Text.Format("restore.loaded", _backup.BackupPath));
        }

        var desiredStates = new Dictionary<string, DesiredServiceState>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in before.Where(snapshot => snapshot.Exists))
        {
            var desired = ResolveDesiredState(snapshot, load.Backup, warnings);
            desiredStates[snapshot.ServiceName] = desired;

            if (!_services.TrySetStartType(snapshot.ServiceName, desired.StartType, out var startupError))
            {
                errors.Add(Text.Format(
                    "restore.startupFailed",
                    snapshot.FriendlyName,
                    startupError));
                continue;
            }

            var runStateChanged = desired.ShouldRun
                ? _services.TryStartService(snapshot.ServiceName, ServiceTimeout, out var runError)
                : _services.TryStopService(snapshot.ServiceName, ServiceTimeout, out runError);

            if (!runStateChanged)
            {
                var action = Text.Get(desired.ShouldRun
                    ? "restore.actionStart"
                    : "restore.actionStop");
                errors.Add(Text.Format(
                    "restore.actionFailed",
                    snapshot.FriendlyName,
                    action,
                    runError));
                continue;
            }

            var stateText = ServiceStateText.Get(desired.ShouldRun
                ? ServiceRunState.Running
                : ServiceRunState.Stopped);
            lines.Add(Text.Format(
                "restore.stateRestored",
                snapshot.FriendlyName,
                ServiceStateText.Get(desired.StartType),
                stateText));
        }

        var after = GetSnapshots();
        AddQueryErrors(after, errors);
        VerifyRestoreResult(after, desiredStates, errors);

        if (load.Found && errors.Count == 0)
        {
            var delete = _backup.Delete();
            if (!delete.Success)
                errors.Add(delete.Error!);
            else
                lines.Add(Text.Get("restore.backupRemoved"));
        }

        return BuildReport(
            Text.Get("title.restoreComplete"),
            Text.Get("title.restorePartial"),
            after,
            lines,
            errors,
            warnings);
    }

    private ToggleReport RunExclusive(string failureTitle, Func<ToggleReport> operation)
    {
        Mutex? mutex = null;
        var acquired = false;

        try
        {
            mutex = new Mutex(initiallyOwned: false, _mutexName);

            try
            {
                acquired = mutex.WaitOne(OperationLockTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                return ToggleReport.Failed(
                    failureTitle,
                    [Text.Get("operation.alreadyRunning")]);
            }

            return operation();
        }
        catch (Exception ex)
        {
            return ToggleReport.Failed(
                failureTitle,
                [Text.Format("operation.unexpectedError", ex)]);
        }
        finally
        {
            if (acquired)
                mutex?.ReleaseMutex();

            mutex?.Dispose();
        }
    }

    private List<ServiceSnapshot> GetSnapshots()
        => Targets.Services.Select(GetSnapshotSafe).ToList();

    private ServiceSnapshot GetSnapshotSafe(TargetService target)
    {
        try
        {
            return _services.GetSnapshot(target);
        }
        catch (Exception ex)
        {
            return new ServiceSnapshot(
                target.ServiceName,
                target.FriendlyName,
                Exists: false,
                QuerySucceeded: false,
                RunState: ServiceRunState.Unknown,
                StartType: ServiceStartType.Unknown,
                NativeStartValue: null,
                Note: ex.Message);
        }
    }

    private static DesiredServiceState ResolveDesiredState(
        ServiceSnapshot current,
        BackupFile? backup,
        List<string> warnings)
    {
        if (backup is null ||
            !backup.Services.TryGetValue(current.ServiceName, out var original) ||
            original is null)
            return new DesiredServiceState(ServiceStartType.Manual, ShouldRun: true);

        if (!original.Exists)
        {
            warnings.Add(Text.Format(
                "restore.serviceOriginallyAbsent",
                current.FriendlyName));
            return new DesiredServiceState(ServiceStartType.Manual, ShouldRun: true);
        }

        var startType = original.StartType switch
        {
            ServiceStartType.Auto => ServiceStartType.Auto,
            ServiceStartType.Manual => ServiceStartType.Manual,
            _ => ServiceStartType.Manual
        };

        if (startType != original.StartType)
        {
            warnings.Add(Text.Format(
                "restore.startupNotRestorable",
                current.FriendlyName,
                ServiceStateText.Get(original.StartType)));
        }

        var shouldRun = original.RunState is
            ServiceRunState.Running or
            ServiceRunState.StartPending or
            ServiceRunState.ContinuePending or
            ServiceRunState.Paused or
            ServiceRunState.PausePending;

        return new DesiredServiceState(startType, shouldRun);
    }

    private static void VerifySoftResult(
        IReadOnlyCollection<ServiceSnapshot> before,
        IReadOnlyCollection<ServiceSnapshot> after,
        List<string> errors)
    {
        var afterMap = after.ToDictionary(snapshot => snapshot.ServiceName, StringComparer.OrdinalIgnoreCase);

        foreach (var original in before.Where(snapshot => snapshot.Exists && snapshot.QuerySucceeded))
        {
            if (!afterMap.TryGetValue(original.ServiceName, out var current) || !current.QuerySucceeded)
                continue;

            if (current.StartType != original.StartType)
            {
                errors.Add(Text.Format(
                    "verify.softStartupChanged",
                    original.FriendlyName,
                    ServiceStateText.Get(original.StartType),
                    ServiceStateText.Get(current.StartType)));
            }

            var wasActive = original.RunState is not ServiceRunState.Stopped;
            if (wasActive &&
                original.StartType is not ServiceStartType.Disabled &&
                current.RunState is not ServiceRunState.Running)
            {
                errors.Add(Text.Format(
                    "verify.softExpectedRunning",
                    original.FriendlyName,
                    ServiceStateText.Get(current.RunState)));
            }
        }
    }

    private static void VerifyHardResult(
        IEnumerable<ServiceSnapshot> after,
        List<string> errors)
    {
        foreach (var snapshot in after.Where(snapshot => snapshot.Exists && snapshot.QuerySucceeded))
        {
            if (snapshot.StartType is not ServiceStartType.Disabled)
                errors.Add(Text.Format(
                    "verify.hardExpectedDisabled",
                    snapshot.FriendlyName,
                    ServiceStateText.Get(snapshot.StartType)));

            if (snapshot.RunState is not ServiceRunState.Stopped)
                errors.Add(Text.Format(
                    "verify.hardExpectedStopped",
                    snapshot.FriendlyName,
                    ServiceStateText.Get(snapshot.RunState)));
        }
    }

    private static void VerifyRestoreResult(
        IEnumerable<ServiceSnapshot> after,
        IReadOnlyDictionary<string, DesiredServiceState> desiredStates,
        List<string> errors)
    {
        foreach (var snapshot in after.Where(snapshot => snapshot.Exists && snapshot.QuerySucceeded))
        {
            if (!desiredStates.TryGetValue(snapshot.ServiceName, out var desired))
                continue;

            if (snapshot.StartType != desired.StartType)
            {
                errors.Add(Text.Format(
                    "verify.restoreExpectedStartup",
                    snapshot.FriendlyName,
                    ServiceStateText.Get(desired.StartType),
                    ServiceStateText.Get(snapshot.StartType)));
            }

            var expectedRunState = desired.ShouldRun
                ? ServiceRunState.Running
                : ServiceRunState.Stopped;

            if (snapshot.RunState != expectedRunState)
            {
                errors.Add(Text.Format(
                    "verify.restoreExpectedState",
                    snapshot.FriendlyName,
                    ServiceStateText.Get(expectedRunState),
                    ServiceStateText.Get(snapshot.RunState)));
            }
        }
    }

    private static void AddQueryErrors(
        IEnumerable<ServiceSnapshot> snapshots,
        List<string> errors)
    {
        foreach (var snapshot in snapshots.Where(snapshot => !snapshot.QuerySucceeded))
        {
            errors.Add(Text.Format(
                "service.statusQueryFailed",
                snapshot.FriendlyName,
                snapshot.Note ?? Text.Get("service.unknownError")));
        }
    }

    private static ToggleReport BuildReport(
        string successTitle,
        string failureTitle,
        List<ServiceSnapshot> snapshots,
        IEnumerable<string> lines,
        List<string> errors,
        IEnumerable<string>? warnings = null)
    {
        var output = new List<string>(lines);
        if (warnings is not null)
            output.AddRange(warnings.Select(warning => $"[WARN] {warning}"));
        output.AddRange(errors.Select(error => $"[ERROR] {error}"));

        return new ToggleReport(
            errors.Count == 0 ? successTitle : failureTitle,
            errors.Count == 0,
            snapshots,
            output);
    }

    private sealed record DesiredServiceState(ServiceStartType StartType, bool ShouldRun);
}

public sealed record ToggleReport(
    string Title,
    bool Success,
    List<ServiceSnapshot> Snapshots,
    List<string> Lines)
{
    public static ToggleReport Failed(
        string title,
        IEnumerable<string> lines,
        IEnumerable<ServiceSnapshot>? snapshots = null)
        => new(title, false, snapshots?.ToList() ?? [], lines.Select(line => $"[ERROR] {line}").ToList());

    public string[] ToPrettyLines()
    {
        var output = new List<string>(Lines);

        if (Snapshots.Count > 0)
        {
            output.Add("");
            output.Add(Text.Get("report.components"));
            foreach (var snapshot in Snapshots)
            {
                var exists = snapshot.Exists
                    ? Text.Get("common.yes")
                    : Text.Get("common.no");
                var nativeStart = snapshot.NativeStartValue?.ToString(
                    CultureInfo.InvariantCulture) ?? "-";
                var note = string.IsNullOrWhiteSpace(snapshot.Note) ? "" : $" ({snapshot.Note})";
                var techId = snapshot.Exists
                    ? Text.Format("report.techId", snapshot.ServiceName)
                    : "";

                output.Add(Text.Format(
                    "report.componentLine",
                    snapshot.FriendlyName,
                    exists,
                    ServiceStateText.Get(snapshot.RunState),
                    ServiceStateText.Get(snapshot.StartType),
                    nativeStart,
                    techId,
                    note));
            }
        }

        return output.ToArray();
    }

    public string ToPrettyText()
        => string.Join(Environment.NewLine, new[] { Title }.Concat(ToPrettyLines()));
}
