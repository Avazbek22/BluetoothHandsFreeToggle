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
            lines.Add($"Hard-mode backup: available ({backup.Backup!.CreatedUtc.LocalDateTime:G})");
        }
        else
        {
            lines.Add("Hard-mode backup: not present");
        }

        return BuildReport("STATUS", "STATUS WITH ERRORS", snapshots, lines, errors);
    }

    public ToggleReport SoftResetHandsFree()
        => RunExclusive("SOFT RESET FAILED", SoftResetCore);

    public ToggleReport HardDisableHandsFree()
        => RunExclusive("HARD MODE FAILED", HardDisableCore);

    public ToggleReport RestoreHandsFree()
        => RunExclusive("RESTORE FAILED", RestoreCore);

    private ToggleReport SoftResetCore()
    {
        var before = GetSnapshots();
        var lines = new List<string>
        {
            "Soft mode: restarting active HFP services.",
            "Startup configuration is not changed; the Bluetooth microphone remains available.",
            "Close applications that are actively using the headset microphone before running Soft mode."
        };
        var errors = new List<string>();
        var warnings = new List<string>();

        AddQueryErrors(before, errors);
        if (errors.Count > 0)
            return BuildReport("SOFT RESET COMPLETE", "SOFT RESET FAILED", before, lines, errors, warnings);

        var present = before.Where(snapshot => snapshot.Exists).ToList();
        if (present.Count == 0)
        {
            errors.Add("No supported HFP service was found on this system.");
            return BuildReport("SOFT RESET COMPLETE", "SOFT RESET FAILED", before, lines, errors, warnings);
        }

        foreach (var snapshot in present)
        {
            if (snapshot.StartType is ServiceStartType.Disabled)
            {
                errors.Add(
                    $"{snapshot.FriendlyName}: startup is Disabled; use Restore before Soft mode.");
                continue;
            }

            if (snapshot.RunState is ServiceRunState.Stopped)
            {
                lines.Add($"[OK] {snapshot.FriendlyName}: already stopped; startup configuration preserved.");
                continue;
            }

            if (snapshot.RunState is ServiceRunState.Unknown)
            {
                errors.Add($"{snapshot.FriendlyName}: current run state is unknown.");
                continue;
            }

            if (!_services.TryStopService(snapshot.ServiceName, ServiceTimeout, out var stopError))
            {
                errors.Add($"{snapshot.FriendlyName}: stop failed: {stopError}");
                continue;
            }

            if (!_services.TryStartService(snapshot.ServiceName, ServiceTimeout, out var startError))
            {
                errors.Add($"{snapshot.FriendlyName}: restart failed: {startError}");
                continue;
            }

            lines.Add($"[OK] {snapshot.FriendlyName}: restarted.");
        }

        var after = GetSnapshots();
        AddQueryErrors(after, errors);
        VerifySoftResult(before, after, errors);

        return BuildReport("SOFT RESET COMPLETE", "SOFT RESET PARTIALLY FAILED", after, lines, errors, warnings);
    }

    private ToggleReport HardDisableCore()
    {
        var before = GetSnapshots();
        var lines = new List<string>
        {
            "Hard mode: disabling HFP services to force high-quality playback.",
            "The Bluetooth headset microphone will be unavailable until Restore is used."
        };
        var errors = new List<string>();
        var warnings = new List<string>();

        AddQueryErrors(before, errors);
        if (errors.Count > 0)
            return BuildReport("HARD MODE COMPLETE", "HARD MODE FAILED", before, lines, errors, warnings);

        if (before.All(snapshot => !snapshot.Exists))
        {
            errors.Add("No supported HFP service was found on this system.");
            return BuildReport("HARD MODE COMPLETE", "HARD MODE FAILED", before, lines, errors, warnings);
        }

        var backup = _backup.Load();
        if (!backup.Success)
        {
            errors.Add(backup.Error!);
            errors.Add("No service configuration was changed.");
            return BuildReport("HARD MODE COMPLETE", "HARD MODE FAILED", before, lines, errors, warnings);
        }

        if (backup.Found)
        {
            lines.Add($"[OK] Existing original-state backup preserved: {_backup.BackupPath}");
        }
        else
        {
            var save = _backup.Save(before);
            if (!save.Success)
            {
                errors.Add(save.Error!);
                errors.Add("No service configuration was changed because a safe backup could not be created.");
                return BuildReport("HARD MODE COMPLETE", "HARD MODE FAILED", before, lines, errors, warnings);
            }

            lines.Add($"[OK] Original state saved: {_backup.BackupPath}");
        }

        foreach (var snapshot in before.Where(snapshot => snapshot.Exists))
        {
            if (!_services.TryStopService(snapshot.ServiceName, ServiceTimeout, out var stopError))
                errors.Add($"{snapshot.FriendlyName}: stop failed: {stopError}");

            if (!_services.TrySetStartType(
                    snapshot.ServiceName,
                    ServiceStartType.Disabled,
                    out var startupError))
            {
                errors.Add($"{snapshot.FriendlyName}: disabling startup failed: {startupError}");
                continue;
            }

            lines.Add($"[OK] {snapshot.FriendlyName}: startup disabled.");
        }

        var after = GetSnapshots();
        AddQueryErrors(after, errors);
        VerifyHardResult(after, errors);

        return BuildReport("HARD MODE COMPLETE", "HARD MODE PARTIALLY FAILED", after, lines, errors, warnings);
    }

    private ToggleReport RestoreCore()
    {
        var before = GetSnapshots();
        var lines = new List<string>
        {
            "Restore mode: enabling HFP and restoring the saved service state."
        };
        var errors = new List<string>();
        var warnings = new List<string>();

        AddQueryErrors(before, errors);
        if (errors.Count > 0)
            return BuildReport("RESTORE COMPLETE", "RESTORE FAILED", before, lines, errors, warnings);

        if (before.All(snapshot => !snapshot.Exists))
        {
            errors.Add("No supported HFP service was found on this system.");
            return BuildReport("RESTORE COMPLETE", "RESTORE FAILED", before, lines, errors, warnings);
        }

        var load = _backup.Load();
        if (!load.Success)
        {
            errors.Add(load.Error!);
            errors.Add("Restore stopped to avoid replacing an unreadable original-state backup.");
            return BuildReport("RESTORE COMPLETE", "RESTORE FAILED", before, lines, errors, warnings);
        }

        if (!load.Found)
        {
            warnings.Add("Original-state backup is missing; safe defaults (Manual + Running) will be used.");
        }
        else
        {
            lines.Add($"[OK] Loaded original state from {_backup.BackupPath}");
        }

        var desiredStates = new Dictionary<string, DesiredServiceState>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in before.Where(snapshot => snapshot.Exists))
        {
            var desired = ResolveDesiredState(snapshot, load.Backup, warnings);
            desiredStates[snapshot.ServiceName] = desired;

            if (!_services.TrySetStartType(snapshot.ServiceName, desired.StartType, out var startupError))
            {
                errors.Add($"{snapshot.FriendlyName}: restoring startup failed: {startupError}");
                continue;
            }

            var runStateChanged = desired.ShouldRun
                ? _services.TryStartService(snapshot.ServiceName, ServiceTimeout, out var runError)
                : _services.TryStopService(snapshot.ServiceName, ServiceTimeout, out runError);

            if (!runStateChanged)
            {
                var action = desired.ShouldRun ? "start" : "stop";
                errors.Add($"{snapshot.FriendlyName}: {action} failed: {runError}");
                continue;
            }

            var stateText = desired.ShouldRun ? "Running" : "Stopped";
            lines.Add(
                $"[OK] {snapshot.FriendlyName}: startup={desired.StartType}, state={stateText}.");
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
                lines.Add("[OK] Original-state backup removed after successful restore.");
        }

        return BuildReport("RESTORE COMPLETE", "RESTORE PARTIALLY FAILED", after, lines, errors, warnings);
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
                    ["Another BluetoothHandsFreeToggle operation is already running."]);
            }

            return operation();
        }
        catch (Exception ex)
        {
            return ToggleReport.Failed(failureTitle, [$"Unexpected error: {ex}"]);
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
            warnings.Add(
                $"{current.FriendlyName}: the service was absent when the backup was created; " +
                "Manual + Running recovery defaults will be used.");
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
            warnings.Add(
                $"{current.FriendlyName}: backup startup value {original.StartType} is not restorable; Manual will be used.");
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
                errors.Add(
                    $"{original.FriendlyName}: Soft mode unexpectedly changed startup from {original.StartType} to {current.StartType}.");
            }

            var wasActive = original.RunState is not ServiceRunState.Stopped;
            if (wasActive &&
                original.StartType is not ServiceStartType.Disabled &&
                current.RunState is not ServiceRunState.Running)
            {
                errors.Add($"{original.FriendlyName}: expected Running after Soft mode, got {current.RunState}.");
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
                errors.Add($"{snapshot.FriendlyName}: expected Disabled startup, got {snapshot.StartType}.");

            if (snapshot.RunState is not ServiceRunState.Stopped)
                errors.Add($"{snapshot.FriendlyName}: expected Stopped state, got {snapshot.RunState}.");
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
                errors.Add(
                    $"{snapshot.FriendlyName}: expected {desired.StartType} startup, got {snapshot.StartType}.");
            }

            var expectedRunState = desired.ShouldRun
                ? ServiceRunState.Running
                : ServiceRunState.Stopped;

            if (snapshot.RunState != expectedRunState)
            {
                errors.Add(
                    $"{snapshot.FriendlyName}: expected {expectedRunState} state, got {snapshot.RunState}.");
            }
        }
    }

    private static void AddQueryErrors(
        IEnumerable<ServiceSnapshot> snapshots,
        List<string> errors)
    {
        foreach (var snapshot in snapshots.Where(snapshot => !snapshot.QuerySucceeded))
        {
            errors.Add(
                $"{snapshot.FriendlyName}: status query failed: {snapshot.Note ?? "unknown error"}");
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
            output.Add("Components:");
            foreach (var snapshot in Snapshots)
            {
                var exists = snapshot.Exists ? "Yes" : "No";
                var nativeStart = snapshot.NativeStartValue?.ToString() ?? "-";
                var note = string.IsNullOrWhiteSpace(snapshot.Note) ? "" : $" ({snapshot.Note})";
                var techId = snapshot.Exists ? $" [TechId: {snapshot.ServiceName}]" : "";

                output.Add(
                    $"- {snapshot.FriendlyName}: Exists={exists} | State={snapshot.RunState} | " +
                    $"Startup={snapshot.StartType} | StartCode={nativeStart}{techId}{note}");
            }
        }

        return output.ToArray();
    }

    public string ToPrettyText()
        => string.Join(Environment.NewLine, new[] { Title }.Concat(ToPrettyLines()));
}
