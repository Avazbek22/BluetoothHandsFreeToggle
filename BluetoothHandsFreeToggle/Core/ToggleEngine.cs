using System.Globalization;
using BluetoothHandsFreeToggle.Localization;
using BluetoothHandsFreeToggle.Windows;

namespace BluetoothHandsFreeToggle.Core;

public sealed class ToggleEngine
{
    private static readonly TimeSpan OperationLockTimeout = TimeSpan.FromSeconds(3);

    private readonly IServiceManager _services;
    private readonly IBackupStore _backup;
    private readonly string _mutexName;
    private readonly IOperationClock _clock;
    private readonly ServiceTimings _timings;
    private readonly IMicrophoneProbe? _microphones;

    public ToggleEngine()
        : this(new WindowsServiceManager(), new BackupStore(), microphones: new WindowsMicrophoneProbe())
    {
    }

    public ToggleEngine(
        IServiceManager services,
        IBackupStore backup,
        string mutexName = @"Global\BluetoothHandsFreeToggle.Operation",
        IOperationClock? clock = null,
        ServiceTimings? timings = null,
        IMicrophoneProbe? microphones = null)
    {
        _services = services;
        _backup = backup;
        _mutexName = mutexName;
        _clock = clock ?? new SystemOperationClock();
        _timings = timings ?? ServiceTimings.Default;
        _microphones = microphones;
    }

    /// <summary>
    /// Finds the apps recording from the Bluetooth headset microphone: while one does, Windows keeps
    /// the headset in Hands-Free and a Soft reset cannot last. Read-only; takes tens of milliseconds
    /// and is bounded by a timeout. Returns <c>null</c> when no microphone probe is configured.
    /// </summary>
    public MicrophoneDiagnosis? DiagnoseMicrophone()
    {
        if (_microphones is null)
            return null;

        try
        {
            return MicrophoneDiagnostics.Analyze(_microphones.Probe());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return MicrophoneDiagnosis.Unavailable(exception.Message);
        }
    }

    public ToggleReport GetStatusReport()
    {
        var snapshots = GetSnapshots();
        var log = new OperationLog();

        AddQueryErrors(snapshots, log);

        var backup = _backup.Load();
        if (!backup.Success)
        {
            log.Errors.Add(backup.Error!);
        }
        else if (backup.Found)
        {
            log.Lines.Add(Text.Format(
                "status.backupAvailable",
                backup.Backup!.CreatedUtc.LocalDateTime.ToString(
                    "G",
                    CultureInfo.CurrentCulture)));
        }
        else
        {
            log.Lines.Add(Text.Get("status.backupMissing"));
        }

        foreach (var snapshot in snapshots.Where(snapshot =>
                     snapshot.Exists && ServiceRunStates.IsPending(snapshot.RunState)))
        {
            log.Warnings.Add(Text.Format(
                "status.pendingHint",
                snapshot.FriendlyName,
                ServiceStateText.Get(snapshot.RunState)));
        }

        AddMicrophoneStatus(DiagnoseMicrophone(), log);

        return BuildReport(ReportTitles.Status, snapshots, log);
    }

    public ToggleReport SoftResetHandsFree(IProgress<OperationEvent>? progress = null)
        => RunExclusive(ReportTitles.Soft, progress, SoftResetCore);

    public ToggleReport HardDisableHandsFree(IProgress<OperationEvent>? progress = null)
        => RunExclusive(ReportTitles.Hard, progress, HardDisableCore);

    public ToggleReport RestoreHandsFree(IProgress<OperationEvent>? progress = null)
        => RunExclusive(ReportTitles.Restore, progress, RestoreCore);

    private ToggleReport SoftResetCore(ServiceControl control, IProgress<OperationEvent>? progress)
    {
        var before = GetSnapshots();
        var log = new OperationLog();
        log.Lines.Add(Text.Get("soft.intro"));
        log.Lines.Add(Text.Get("soft.preserveMicrophone"));

        // Checked before the restart: stopping the service removes the headset microphone.
        AddMicrophoneSoftNotes(DiagnoseMicrophone(), log);

        AddQueryErrors(before, log);
        if (log.Errors.Count > 0)
            return BuildReport(ReportTitles.Soft, before, log, beforeAnyChange: true);

        var present = before.Where(snapshot => snapshot.Exists).ToList();
        if (present.Count == 0)
        {
            log.Errors.Add(Text.Get("service.noneFound"));
            return BuildReport(ReportTitles.Soft, before, log, beforeAnyChange: true);
        }

        foreach (var snapshot in present)
        {
            if (snapshot.StartType is ServiceStartType.Disabled)
            {
                log.Errors.Add(Text.Format(
                    "soft.disabledUseRestore",
                    snapshot.FriendlyName));
                continue;
            }

            if (snapshot.RunState is ServiceRunState.Stopped)
            {
                log.Lines.Add(Text.Format("soft.alreadyStopped", snapshot.FriendlyName));
                continue;
            }

            if (snapshot.RunState is ServiceRunState.Unknown or ServiceRunState.NotPresent)
            {
                log.Errors.Add(Text.Format("service.stateUnknown", snapshot.FriendlyName));
                continue;
            }

            // A service already stuck in StopPending is exactly the "stuck HFP" case:
            // EnsureStopped recovers it and EnsureRunning brings it back.
            var target = ToTarget(snapshot);
            if (!log.Apply(snapshot, control.EnsureStopped(target)))
                continue;

            if (!log.Apply(snapshot, control.EnsureRunning(target)))
                continue;

            log.Lines.Add(Text.Format("soft.restarted", snapshot.FriendlyName));
        }

        var after = GetSnapshots();
        AddQueryErrors(after, log);
        VerifySoftResult(before, after, log);

        return BuildReport(ReportTitles.Soft, after, log);
    }

    private ToggleReport HardDisableCore(ServiceControl control, IProgress<OperationEvent>? progress)
    {
        var before = GetSnapshots();
        var log = new OperationLog();
        log.Lines.Add(Text.Get("hard.intro"));
        log.Lines.Add(Text.Get("hard.microphoneUnavailable"));

        var microphone = DiagnoseMicrophone();

        AddQueryErrors(before, log);
        if (log.Errors.Count > 0)
            return BuildReport(ReportTitles.Hard, before, log, beforeAnyChange: true);

        if (before.All(snapshot => !snapshot.Exists))
        {
            log.Errors.Add(Text.Get("service.noneFound"));
            return BuildReport(ReportTitles.Hard, before, log, beforeAnyChange: true);
        }

        foreach (var snapshot in before.Where(snapshot => snapshot.Exists))
        {
            if (IsRestorableStartType(snapshot.StartType))
                continue;

            log.Errors.Add(Text.Format(
                "hard.unsupportedStartup",
                snapshot.FriendlyName,
                ServiceStateText.Get(snapshot.StartType),
                snapshot.NativeStartValue?.ToString(CultureInfo.InvariantCulture) ?? "-"));
        }

        if (log.Errors.Count > 0)
        {
            log.Errors.Add(Text.Get("hard.noConfigurationChanged"));
            return BuildReport(ReportTitles.Hard, before, log, beforeAnyChange: true);
        }

        // Let in-flight start/continue/pause transitions finish so the backup records a stable
        // state. StopPending is not waited for: the service is about to be stopped anyway, and
        // Restore treats a recorded StopPending as "was running".
        if (before.Any(snapshot => snapshot.Exists &&
                                   snapshot.RunState is not ServiceRunState.StopPending &&
                                   ServiceRunStates.IsPending(snapshot.RunState)))
        {
            foreach (var snapshot in before.Where(snapshot =>
                         snapshot.Exists &&
                         snapshot.RunState is not ServiceRunState.StopPending &&
                         ServiceRunStates.IsPending(snapshot.RunState)))
            {
                _ = control.WaitForStableState(ToTarget(snapshot));
            }

            before = GetSnapshots();
            AddQueryErrors(before, log);
            if (log.Errors.Count > 0)
            {
                log.Errors.Add(Text.Get("hard.noConfigurationChanged"));
                return BuildReport(ReportTitles.Hard, before, log, beforeAnyChange: true);
            }
        }

        var prepare = _backup.Prepare();
        if (!prepare.Success)
        {
            log.Errors.Add(prepare.Error!);
            log.Errors.Add(Text.Get("hard.noConfigurationChanged"));
            return BuildReport(ReportTitles.Hard, before, log, beforeAnyChange: true);
        }

        var backup = _backup.Load();
        if (!backup.Success)
        {
            log.Errors.Add(backup.Error!);
            log.Errors.Add(Text.Get("hard.noConfigurationChanged"));
            return BuildReport(ReportTitles.Hard, before, log, beforeAnyChange: true);
        }

        if (backup.Found)
        {
            log.Lines.Add(Text.Format("hard.existingBackup", _backup.BackupPath));
        }
        else
        {
            var save = _backup.Save(before);
            if (!save.Success)
            {
                log.Errors.Add(save.Error!);
                log.Errors.Add(Text.Get("hard.noConfigurationChangedNoBackup"));
                return BuildReport(ReportTitles.Hard, before, log, beforeAnyChange: true);
            }

            log.Lines.Add(Text.Format("hard.originalSaved", _backup.BackupPath));
        }

        if (microphone?.Verdict is MicrophoneVerdict.InUse)
            log.Warnings.Add(Text.Format("mic.hard.inUse", JoinAppNames(microphone.BluetoothApps)));

        foreach (var snapshot in before.Where(snapshot => snapshot.Exists))
        {
            // Disable first: a disabled service cannot be restarted by its device triggers or by
            // SCM failure actions while it is being stopped, which also makes forced recovery final.
            progress?.Report(new OperationEvent(
                OperationEventKind.Step,
                Text.Format("progress.disabling", snapshot.FriendlyName)));

            if (_services.TrySetStartType(
                    snapshot.ServiceName,
                    ServiceStartType.Disabled,
                    out var startupError))
            {
                log.Lines.Add(Text.Format("service.startupDisabled", snapshot.FriendlyName));
            }
            else
            {
                log.Errors.Add(Text.Format(
                    "service.disableStartupFailed",
                    snapshot.FriendlyName,
                    startupError));
            }

            // Stop even if disabling failed: playback quality improves until the next trigger start.
            _ = log.Apply(snapshot, control.EnsureStopped(ToTarget(snapshot)));
        }

        var after = GetSnapshots();
        AddQueryErrors(after, log);
        VerifyHardResult(after, log);

        return BuildReport(ReportTitles.Hard, after, log);
    }

    private ToggleReport RestoreCore(ServiceControl control, IProgress<OperationEvent>? progress)
    {
        var before = GetSnapshots();
        var log = new OperationLog();
        log.Lines.Add(Text.Get("restore.intro"));

        AddQueryErrors(before, log);
        if (log.Errors.Count > 0)
            return BuildReport(ReportTitles.Restore, before, log, beforeAnyChange: true);

        if (before.All(snapshot => !snapshot.Exists))
        {
            log.Errors.Add(Text.Get("service.noneFound"));
            return BuildReport(ReportTitles.Restore, before, log, beforeAnyChange: true);
        }

        var prepare = _backup.Prepare();
        if (!prepare.Success)
        {
            log.Errors.Add(prepare.Error!);
            return BuildReport(ReportTitles.Restore, before, log, beforeAnyChange: true);
        }

        var load = _backup.Load();
        if (!load.Success)
        {
            log.Errors.Add(load.Error!);
            log.Errors.Add(Text.Get("restore.unreadableBackup"));
            return BuildReport(ReportTitles.Restore, before, log, beforeAnyChange: true);
        }

        if (!load.Found)
            log.Warnings.Add(Text.Get("restore.missingBackup"));
        else
            log.Lines.Add(Text.Format("restore.loaded", _backup.BackupPath));

        var desiredStates = new Dictionary<string, DesiredServiceState>(StringComparer.OrdinalIgnoreCase);

        if (load.Backup is not null)
        {
            var currentByName = before.ToDictionary(
                snapshot => snapshot.ServiceName,
                StringComparer.OrdinalIgnoreCase);

            foreach (var (serviceName, original) in load.Backup.Services)
            {
                if (!original.Exists ||
                    !currentByName.TryGetValue(serviceName, out var current) ||
                    current.Exists)
                {
                    continue;
                }

                log.Errors.Add(Text.Format(
                    "restore.serviceCurrentlyMissing",
                    current.FriendlyName));
            }
        }

        foreach (var snapshot in before.Where(snapshot => snapshot.Exists))
        {
            var resolution = ResolveDesiredState(snapshot, load.Backup);
            if (resolution.Error is not null)
            {
                log.Errors.Add(resolution.Error);
                continue;
            }

            if (resolution.Warning is not null)
                log.Warnings.Add(resolution.Warning);

            if (resolution.Desired is null)
                continue;

            var desired = resolution.Desired;
            desiredStates[snapshot.ServiceName] = desired;
            ApplyDesiredState(control, progress, snapshot, desired, log);
        }

        var after = GetSnapshots();
        AddQueryErrors(after, log);
        VerifyRestoreResult(after, desiredStates, log);

        // The backup is the only record of the original state: keep it until everything is verified.
        if (load.Found && log.Errors.Count == 0 && !log.RebootRequired)
        {
            var delete = _backup.Delete();
            if (!delete.Success)
                log.Errors.Add(delete.Error!);
            else
                log.Lines.Add(Text.Get("restore.backupRemoved"));
        }

        return BuildReport(ReportTitles.Restore, after, log);
    }

    private ToggleReport RunExclusive(
        ReportTitles titles,
        IProgress<OperationEvent>? progress,
        Func<ServiceControl, IProgress<OperationEvent>?, ToggleReport> operation)
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
                    Text.Get(titles.FailedKey),
                    [Text.Get("operation.alreadyRunning")]);
            }

            var control = new ServiceControl(_services, _clock, _timings, progress);
            return operation(control, progress);
        }
        catch (Exception ex)
        {
            return ToggleReport.Failed(
                Text.Get(titles.FailedKey),
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

    private void ApplyDesiredState(
        ServiceControl control,
        IProgress<OperationEvent>? progress,
        ServiceSnapshot snapshot,
        DesiredServiceState desired,
        OperationLog log)
    {
        if (desired.StartType is ServiceStartType.Disabled && desired.ShouldRun)
        {
            RestoreRunningDisabledState(control, progress, snapshot, desired, log);
            return;
        }

        ReportStartupChange(progress, snapshot, desired.StartType);
        if (!_services.TrySetStartType(snapshot.ServiceName, desired.StartType, out var startupError))
        {
            log.Errors.Add(Text.Format(
                "restore.startupFailed",
                snapshot.FriendlyName,
                startupError));
            return;
        }

        var target = ToTarget(snapshot);
        var result = desired.ShouldRun
            ? control.EnsureRunning(target)
            : control.EnsureStopped(target);

        if (log.Apply(snapshot, result))
            AddRestoredStateLine(desired, log);
    }

    /// <summary>
    /// Windows cannot start a Disabled service, so the original "Running + Disabled" state is
    /// recreated by temporarily allowing Manual startup, starting it, and disabling it again.
    /// </summary>
    private void RestoreRunningDisabledState(
        ServiceControl control,
        IProgress<OperationEvent>? progress,
        ServiceSnapshot snapshot,
        DesiredServiceState desired,
        OperationLog log)
    {
        ReportStartupChange(progress, snapshot, ServiceStartType.Manual);
        if (!_services.TrySetStartType(
                snapshot.ServiceName,
                ServiceStartType.Manual,
                out var temporaryStartupError))
        {
            log.Errors.Add(Text.Format(
                "restore.temporaryStartupFailed",
                snapshot.FriendlyName,
                temporaryStartupError));
            return;
        }

        var started = log.Apply(snapshot, control.EnsureRunning(ToTarget(snapshot)));

        ReportStartupChange(progress, snapshot, ServiceStartType.Disabled);
        var disabled = _services.TrySetStartType(
            snapshot.ServiceName,
            ServiceStartType.Disabled,
            out var disabledStartupError);

        if (!disabled)
        {
            log.Errors.Add(Text.Format(
                "restore.startupFailed",
                snapshot.FriendlyName,
                disabledStartupError));
        }

        if (started && disabled)
            AddRestoredStateLine(desired, log);
    }

    private static void ReportStartupChange(
        IProgress<OperationEvent>? progress,
        ServiceSnapshot snapshot,
        ServiceStartType startType)
        => progress?.Report(new OperationEvent(
            OperationEventKind.Step,
            Text.Format(
                "progress.settingStartup",
                snapshot.FriendlyName,
                ServiceStateText.Get(startType))));

    private static void AddRestoredStateLine(DesiredServiceState desired, OperationLog log)
    {
        var stateText = ServiceStateText.Get(desired.ShouldRun
            ? ServiceRunState.Running
            : ServiceRunState.Stopped);
        log.Lines.Add(Text.Format(
            "restore.stateRestored",
            desired.FriendlyName,
            ServiceStateText.Get(desired.StartType),
            stateText));
    }

    private static DesiredStateResolution ResolveDesiredState(
        ServiceSnapshot current,
        BackupFile? backup)
    {
        if (backup is null)
        {
            return DesiredStateResolution.Resolved(new DesiredServiceState(
                current.FriendlyName,
                ServiceStartType.Manual,
                ShouldRun: true));
        }

        if (!backup.Services.TryGetValue(current.ServiceName, out var original) ||
            original is null)
        {
            return DesiredStateResolution.Failed(Text.Format(
                "restore.backupEntryMissing",
                current.FriendlyName));
        }

        if (!original.Exists)
        {
            return DesiredStateResolution.Skipped(Text.Format(
                "restore.serviceOriginallyAbsent",
                current.FriendlyName));
        }

        if (!IsRestorableStartType(original.StartType))
        {
            return DesiredStateResolution.Failed(Text.Format(
                "restore.unsupportedStartup",
                current.FriendlyName,
                ServiceStateText.Get(original.StartType)));
        }

        var shouldRun = ServiceRunStates.WasActive(original.RunState);
        var desired = new DesiredServiceState(current.FriendlyName, original.StartType, shouldRun);

        // Older versions could record a transitional state (for example a hung StopPending).
        string? warning = null;
        if (ServiceRunStates.IsPending(original.RunState))
        {
            warning = Text.Format(
                "restore.transitionalState",
                current.FriendlyName,
                ServiceStateText.Get(original.RunState),
                ServiceStateText.Get(shouldRun ? ServiceRunState.Running : ServiceRunState.Stopped));
        }

        return DesiredStateResolution.Resolved(desired, warning);
    }

    private static void AddMicrophoneStatus(MicrophoneDiagnosis? diagnosis, OperationLog log)
    {
        if (diagnosis is null)
            return;

        switch (diagnosis.Verdict)
        {
            case MicrophoneVerdict.InUse:
                log.Warnings.Add(Text.Format(
                    "mic.status.inUse",
                    JoinAppNames(diagnosis.BluetoothApps),
                    string.Join(", ", diagnosis.BluetoothApps
                        .SelectMany(app => app.DeviceNames)
                        .Distinct(StringComparer.OrdinalIgnoreCase))));
                break;

            case MicrophoneVerdict.Idle:
                log.Lines.Add(Text.Format(
                    "mic.status.idle",
                    string.Join(", ", diagnosis.BluetoothMicrophones.Select(device => device.Name))));
                break;

            case MicrophoneVerdict.NoBluetoothMicrophone:
                log.Lines.Add(Text.Get("mic.status.notDetected"));
                break;

            default:
                log.Lines.Add(Text.Format(
                    "mic.unavailable",
                    diagnosis.Error ?? Text.Get("service.unknownError")));
                break;
        }

        if (diagnosis.DefaultBluetoothMicrophone is { } defaultMicrophone)
            log.Lines.Add(Text.Format("mic.defaultDevice", defaultMicrophone.Name));

        if (diagnosis.OtherApps.Count > 0)
            log.Lines.Add(Text.Format("mic.status.otherDevice", JoinAppNames(diagnosis.OtherApps)));
    }

    private static void AddMicrophoneSoftNotes(MicrophoneDiagnosis? diagnosis, OperationLog log)
    {
        switch (diagnosis?.Verdict)
        {
            case MicrophoneVerdict.InUse:
                log.Warnings.Add(Text.Format("mic.soft.inUse", JoinAppNames(diagnosis.BluetoothApps)));
                break;

            case MicrophoneVerdict.Idle:
                log.Lines.Add(Text.Get("mic.soft.idle"));
                break;

            case MicrophoneVerdict.NoBluetoothMicrophone:
                break;

            default:
                // Unknown: fall back to the general advice.
                log.Lines.Add(Text.Get("soft.closeMicrophoneApps"));
                break;
        }
    }

    private static string JoinAppNames(IEnumerable<MicrophoneApp> apps)
        => string.Join(", ", apps.Select(app => app.Name));

    private static bool IsRestorableStartType(ServiceStartType startType)
        => startType is
            ServiceStartType.Auto or
            ServiceStartType.Manual or
            ServiceStartType.Disabled;

    private static TargetService ToTarget(ServiceSnapshot snapshot)
        => new(snapshot.ServiceName, snapshot.FriendlyName);

    private static void VerifySoftResult(
        IReadOnlyCollection<ServiceSnapshot> before,
        IReadOnlyCollection<ServiceSnapshot> after,
        OperationLog log)
    {
        var afterMap = after.ToDictionary(snapshot => snapshot.ServiceName, StringComparer.OrdinalIgnoreCase);

        foreach (var original in before.Where(snapshot => snapshot.Exists && snapshot.QuerySucceeded))
        {
            if (!afterMap.TryGetValue(original.ServiceName, out var current) || !current.QuerySucceeded)
                continue;

            if (current.StartType != original.StartType)
            {
                log.Errors.Add(Text.Format(
                    "verify.softStartupChanged",
                    original.FriendlyName,
                    ServiceStateText.Get(original.StartType),
                    ServiceStateText.Get(current.StartType)));
            }

            if (log.IsRebootPending(original.ServiceName))
                continue;

            var wasActive = original.RunState is not ServiceRunState.Stopped;
            if (wasActive &&
                original.StartType is not ServiceStartType.Disabled &&
                current.RunState is not ServiceRunState.Running)
            {
                log.Errors.Add(Text.Format(
                    "verify.softExpectedRunning",
                    original.FriendlyName,
                    ServiceStateText.Get(current.RunState)));
            }
        }
    }

    private static void VerifyHardResult(IEnumerable<ServiceSnapshot> after, OperationLog log)
    {
        foreach (var snapshot in after.Where(snapshot => snapshot.Exists && snapshot.QuerySucceeded))
        {
            if (snapshot.StartType is not ServiceStartType.Disabled)
            {
                log.Errors.Add(Text.Format(
                    "verify.hardExpectedDisabled",
                    snapshot.FriendlyName,
                    ServiceStateText.Get(snapshot.StartType)));
            }

            if (snapshot.RunState is not ServiceRunState.Stopped &&
                !log.IsRebootPending(snapshot.ServiceName))
            {
                log.Errors.Add(Text.Format(
                    "verify.hardExpectedStopped",
                    snapshot.FriendlyName,
                    ServiceStateText.Get(snapshot.RunState)));
            }
        }
    }

    private static void VerifyRestoreResult(
        IEnumerable<ServiceSnapshot> after,
        IReadOnlyDictionary<string, DesiredServiceState> desiredStates,
        OperationLog log)
    {
        var afterByName = after.ToDictionary(
            snapshot => snapshot.ServiceName,
            StringComparer.OrdinalIgnoreCase);

        foreach (var (serviceName, desired) in desiredStates)
        {
            if (!afterByName.TryGetValue(serviceName, out var snapshot) || !snapshot.Exists)
            {
                log.Errors.Add(Text.Format(
                    "verify.restoreServiceMissing",
                    desired.FriendlyName));
                continue;
            }

            if (!snapshot.QuerySucceeded)
                continue;

            if (snapshot.StartType != desired.StartType)
            {
                log.Errors.Add(Text.Format(
                    "verify.restoreExpectedStartup",
                    snapshot.FriendlyName,
                    ServiceStateText.Get(desired.StartType),
                    ServiceStateText.Get(snapshot.StartType)));
            }

            if (log.IsRebootPending(serviceName))
                continue;

            var expectedRunState = desired.ShouldRun
                ? ServiceRunState.Running
                : ServiceRunState.Stopped;

            if (snapshot.RunState != expectedRunState)
            {
                log.Errors.Add(Text.Format(
                    "verify.restoreExpectedState",
                    snapshot.FriendlyName,
                    ServiceStateText.Get(expectedRunState),
                    ServiceStateText.Get(snapshot.RunState)));
            }
        }
    }

    private static void AddQueryErrors(IEnumerable<ServiceSnapshot> snapshots, OperationLog log)
    {
        foreach (var snapshot in snapshots.Where(snapshot => !snapshot.QuerySucceeded))
        {
            log.Errors.Add(Text.Format(
                "service.statusQueryFailed",
                snapshot.FriendlyName,
                snapshot.Note ?? Text.Get("service.unknownError")));
        }
    }

    private static ToggleReport BuildReport(
        ReportTitles titles,
        List<ServiceSnapshot> snapshots,
        OperationLog log,
        bool beforeAnyChange = false)
    {
        var output = new List<string>(log.Lines);
        output.AddRange(log.Warnings.Select(warning => $"[WARN] {warning}"));
        output.AddRange(log.Errors.Select(error => $"[ERROR] {error}"));

        if (log.RebootRequired && titles.RebootNoteKey is not null)
            output.Add($"[WARN] {Text.Get(titles.RebootNoteKey)}");

        var titleKey = log.Errors.Count > 0
            ? beforeAnyChange ? titles.FailedKey : titles.PartialKey
            : log.RebootRequired && titles.RebootTitleKey is not null
                ? titles.RebootTitleKey
                : titles.CompleteKey;

        return new ToggleReport(
            Text.Get(titleKey),
            log.Errors.Count == 0 && !log.RebootRequired,
            snapshots,
            output,
            log.RebootRequired);
    }

    private sealed class OperationLog
    {
        private readonly HashSet<string> _rebootPendingServices = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Lines { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];
        public bool RebootRequired => _rebootPendingServices.Count > 0;

        public bool IsRebootPending(string serviceName) => _rebootPendingServices.Contains(serviceName);

        /// <returns><c>true</c> when the service reached the requested state.</returns>
        public bool Apply(ServiceSnapshot snapshot, ServiceActionResult result)
        {
            if (result.RecoveryNote is not null && !Warnings.Contains(result.RecoveryNote))
                Warnings.Add(result.RecoveryNote);

            switch (result.Outcome)
            {
                case ServiceActionOutcome.RebootRequired:
                    _rebootPendingServices.Add(snapshot.ServiceName);
                    Warnings.Add(result.Error!);
                    return false;

                case ServiceActionOutcome.Failed:
                    Errors.Add(result.Error!);
                    return false;

                default:
                    return true;
            }
        }
    }

    private sealed record ReportTitles(
        string CompleteKey,
        string PartialKey,
        string FailedKey,
        string? RebootTitleKey,
        string? RebootNoteKey)
    {
        public static ReportTitles Status { get; } = new(
            "title.status", "title.statusWithErrors", "title.statusWithErrors", null, null);

        public static ReportTitles Soft { get; } = new(
            "title.softComplete", "title.softPartial", "title.softFailed",
            "title.softRebootRequired", "reboot.soft");

        public static ReportTitles Hard { get; } = new(
            "title.hardComplete", "title.hardPartial", "title.hardFailed",
            "title.hardRebootRequired", "reboot.hard");

        public static ReportTitles Restore { get; } = new(
            "title.restoreComplete", "title.restorePartial", "title.restoreFailed",
            "title.restoreRebootRequired", "reboot.restore");
    }

    private sealed record DesiredServiceState(
        string FriendlyName,
        ServiceStartType StartType,
        bool ShouldRun);

    private sealed record DesiredStateResolution(
        DesiredServiceState? Desired,
        string? Warning,
        string? Error)
    {
        public static DesiredStateResolution Resolved(DesiredServiceState desired, string? warning = null)
            => new(desired, warning, null);

        public static DesiredStateResolution Skipped(string warning)
            => new(null, warning, null);

        public static DesiredStateResolution Failed(string error)
            => new(null, null, error);
    }
}

public sealed record ToggleReport(
    string Title,
    bool Success,
    List<ServiceSnapshot> Snapshots,
    List<string> Lines,
    bool RebootRequired = false)
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
