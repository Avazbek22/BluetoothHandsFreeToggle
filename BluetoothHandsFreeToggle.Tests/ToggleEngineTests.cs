using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed class ToggleEngineTests
{
    [Fact]
    public void SoftResetRestartsRunningServiceWithoutChangingStartup()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);

        var report = engine.SoftResetHandsFree();

        Assert.True(report.Success);
        Assert.Equal(1, services.StopCalls);
        Assert.Equal(1, services.StartCalls);
        Assert.Equal(0, services.SetStartTypeCalls);
        Assert.Equal(ServiceStartType.Manual, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
        Assert.Equal(0, backup.SaveCalls);
    }

    [Fact]
    public void SoftResetDoesNotStartServiceThatWasAlreadyStopped()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Stopped, ServiceStartType.Manual);
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.True(report.Success);
        Assert.Equal(0, services.StopCalls);
        Assert.Equal(0, services.StartCalls);
        Assert.Equal(ServiceRunState.Stopped, services.Btag.RunState);
    }

    [Fact]
    public void SoftResetReportsFailureWhenServiceIsDisabled()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Stopped, ServiceStartType.Disabled);
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.False(report.Success);
        Assert.Contains(report.Lines, line => line.Contains("Soft mode cannot", StringComparison.Ordinal));
        Assert.Equal(0, services.StartCalls);
        Assert.Equal(0, services.SetStartTypeCalls);
    }

    [Fact]
    public void HardModeDoesNotChangeAnythingWhenBackupCannotBeSaved()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Auto);
        var backup = new MemoryBackupStore { SaveError = "disk full" };
        var engine = CreateEngine(services, backup);

        var report = engine.HardDisableHandsFree();

        Assert.False(report.Success);
        Assert.Equal(0, services.StopCalls);
        Assert.Equal(0, services.SetStartTypeCalls);
        Assert.Equal(ServiceStartType.Auto, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
    }

    [Fact]
    public void HardModeDoesNotChangeAnythingWhenBackupStorageCannotBePrepared()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Auto);
        var backup = new MemoryBackupStore { PrepareError = "unsafe backup path" };
        var engine = CreateEngine(services, backup);

        var report = engine.HardDisableHandsFree();

        Assert.False(report.Success);
        Assert.Equal(0, services.StopCalls);
        Assert.Equal(0, services.SetStartTypeCalls);
        Assert.Equal(0, backup.SaveCalls);
        Assert.Equal(ServiceStartType.Auto, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
    }

    [Fact]
    public void HardModeSecondCallPreservesFirstOriginalState()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Auto);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);

        var first = engine.HardDisableHandsFree();
        var second = engine.HardDisableHandsFree();

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, backup.SaveCalls);
        var original = Assert.IsType<BackupFile>(backup.Stored);
        Assert.Equal(ServiceStartType.Auto, original.Services["BTAGService"].StartType);
        Assert.Equal(ServiceRunState.Running, original.Services["BTAGService"].RunState);
    }

    [Fact]
    public void HardModeReportsFailureWhenFinalServiceStateIsNotStopped()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Auto);
        services.StopError = "access denied";
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.HardDisableHandsFree();

        Assert.False(report.Success);
        Assert.Contains(report.Lines, line => line.Contains("access denied", StringComparison.Ordinal));
        Assert.Equal(ServiceStartType.Disabled, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
    }

    [Fact]
    public void HardModeDoesNotChangeServiceWithUnsupportedOriginalStartupType()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Unknown);
        services.Btag = services.Btag with { NativeStartValue = 1 };
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);

        var report = engine.HardDisableHandsFree();

        Assert.False(report.Success);
        Assert.Equal(0, services.StopCalls);
        Assert.Equal(0, services.SetStartTypeCalls);
        Assert.Equal(0, backup.SaveCalls);
        Assert.Contains(report.Lines, line => line.Contains("not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void RestoreReturnsOriginalStartupAndRunStateThenDeletesBackup()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Auto);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);
        Assert.True(engine.HardDisableHandsFree().Success);

        var report = engine.RestoreHandsFree();

        Assert.True(report.Success);
        Assert.Equal(ServiceStartType.Auto, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
        Assert.Null(backup.Stored);
        Assert.Equal(1, backup.DeleteCalls);
    }

    [Fact]
    public void RestoreWithoutBackupUsesManualAndRunningRecoveryDefaults()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Stopped, ServiceStartType.Disabled);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);

        var report = engine.RestoreHandsFree();

        Assert.True(report.Success);
        Assert.Equal(ServiceStartType.Manual, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
        Assert.Equal(0, backup.DeleteCalls);
    }

    [Fact]
    public void RestoreKeepsOriginallyStoppedServiceStopped()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Stopped, ServiceStartType.Manual);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);
        Assert.True(engine.HardDisableHandsFree().Success);

        var report = engine.RestoreHandsFree();

        Assert.True(report.Success);
        Assert.Equal(ServiceStartType.Manual, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Stopped, services.Btag.RunState);
        Assert.Equal(0, services.StartCalls);
        Assert.Null(backup.Stored);
    }

    [Fact]
    public void RestoreKeepsOriginallyDisabledServiceDisabled()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Stopped, ServiceStartType.Disabled);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);
        Assert.True(engine.HardDisableHandsFree().Success);
        services.StartTypeChanges.Clear();

        var report = engine.RestoreHandsFree();

        Assert.True(report.Success);
        Assert.Equal(ServiceStartType.Disabled, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Stopped, services.Btag.RunState);
        Assert.Equal([ServiceStartType.Disabled], services.StartTypeChanges);
        Assert.Null(backup.Stored);
    }

    [Fact]
    public void RestoreCanRecreateOriginallyRunningAndDisabledState()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Disabled);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);
        Assert.True(engine.HardDisableHandsFree().Success);
        services.StartTypeChanges.Clear();

        var report = engine.RestoreHandsFree();

        Assert.True(report.Success);
        Assert.Equal(ServiceStartType.Disabled, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
        Assert.Equal(
            [ServiceStartType.Manual, ServiceStartType.Disabled],
            services.StartTypeChanges);
        Assert.Null(backup.Stored);
    }

    [Fact]
    public void RestoreDoesNotGuessStateForPresentServiceMissingFromBackup()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Stopped, ServiceStartType.Disabled);
        var backup = new MemoryBackupStore
        {
            Stored = new BackupFile(1, DateTimeOffset.UtcNow, [])
        };
        var engine = CreateEngine(services, backup);

        var report = engine.RestoreHandsFree();

        Assert.False(report.Success);
        Assert.Equal(ServiceStartType.Disabled, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Stopped, services.Btag.RunState);
        Assert.Equal(0, services.SetStartTypeCalls);
        Assert.NotNull(backup.Stored);
        Assert.Equal(0, backup.DeleteCalls);
    }

    [Fact]
    public void RestoreDoesNotApplyUnsupportedStartupStateFromBackup()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Stopped, ServiceStartType.Disabled);
        var backup = new MemoryBackupStore
        {
            Stored = new BackupFile(
                1,
                DateTimeOffset.UtcNow,
                new Dictionary<string, BackupServiceState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BTAGService"] = new(
                        Exists: true,
                        RunState: ServiceRunState.Running,
                        StartType: ServiceStartType.Unknown)
                })
        };
        var engine = CreateEngine(services, backup);

        var report = engine.RestoreHandsFree();

        Assert.False(report.Success);
        Assert.Equal(ServiceStartType.Disabled, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Stopped, services.Btag.RunState);
        Assert.Equal(0, services.SetStartTypeCalls);
        Assert.NotNull(backup.Stored);
        Assert.Equal(0, backup.DeleteCalls);
    }

    [Fact]
    public void RestoreLeavesNewlyAppearedServiceUnchangedWhenItWasOriginallyAbsent()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Auto);
        var backup = new MemoryBackupStore
        {
            Stored = new BackupFile(
                1,
                DateTimeOffset.UtcNow,
                new Dictionary<string, BackupServiceState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BTAGService"] = new(
                        Exists: false,
                        ServiceRunState.NotPresent,
                        ServiceStartType.NotPresent)
                })
        };
        var engine = CreateEngine(services, backup);

        var report = engine.RestoreHandsFree();

        Assert.True(report.Success);
        Assert.Equal(ServiceStartType.Auto, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
        Assert.Equal(0, services.SetStartTypeCalls);
        Assert.Contains(report.Lines, line => line.Contains("left unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void RestorePreservesBackupWhenOriginallyPresentServiceIsCurrentlyMissing()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Stopped, ServiceStartType.Disabled);
        var backup = new MemoryBackupStore
        {
            Stored = new BackupFile(
                1,
                DateTimeOffset.UtcNow,
                new Dictionary<string, BackupServiceState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BTAGService"] = new(
                        Exists: true,
                        ServiceRunState.Running,
                        ServiceStartType.Manual),
                    ["BthHFSrv"] = new(
                        Exists: true,
                        ServiceRunState.Running,
                        ServiceStartType.Manual)
                })
        };
        var engine = CreateEngine(services, backup);

        var report = engine.RestoreHandsFree();

        Assert.False(report.Success);
        Assert.NotNull(backup.Stored);
        Assert.Equal(0, backup.DeleteCalls);
        Assert.Contains(report.Lines, line => line.Contains("currently missing", StringComparison.Ordinal));
    }

    [Fact]
    public void RestorePreservesBackupWhenStartingServiceFails()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);
        Assert.True(engine.HardDisableHandsFree().Success);
        services.StartError = "service dependency failed";

        var report = engine.RestoreHandsFree();

        Assert.False(report.Success);
        Assert.NotNull(backup.Stored);
        Assert.Equal(0, backup.DeleteCalls);
    }

    [Fact]
    public void StatusReportsQueryErrorInsteadOfPretendingServiceIsMissing()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.Btag = services.Btag with
        {
            Exists = false,
            QuerySucceeded = false,
            RunState = ServiceRunState.Unknown,
            StartType = ServiceStartType.Unknown,
            Note = "SCM unavailable"
        };
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.GetStatusReport();

        Assert.False(report.Success);
        Assert.Contains(report.Lines, line => line.Contains("SCM unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void StatusDoesNotPrepareOrModifyBackupStorage()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Stopped, ServiceStartType.Manual);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);

        var report = engine.GetStatusReport();

        Assert.True(report.Success);
        Assert.Equal(0, backup.PrepareCalls);
        Assert.Equal(0, backup.SaveCalls);
        Assert.Equal(0, backup.DeleteCalls);
    }

    // ===== Hung-service recovery =====

    [Fact]
    public void SoftResetRecoversServiceThatHangsWhileStopping()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.HangOnStop = true;
        var clock = new FakeClock();
        var progress = new RecordingProgress();
        var engine = CreateEngine(services, new MemoryBackupStore(), clock);

        var report = engine.SoftResetHandsFree(progress);

        Assert.True(report.Success);
        Assert.False(report.RebootRequired);
        Assert.Equal(1, services.TerminateCalls);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
        Assert.Equal(ServiceStartType.Manual, services.Btag.StartType);
        Assert.Contains(report.Lines, line => line.Contains("was terminated to recover it", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Lines, line => line.StartsWith("[ERROR]", StringComparison.Ordinal));

        // Hang is detected from the wait hint (10 s), not from a long fixed timeout.
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(12));
        Assert.Contains(progress.Events, e => e.Kind is OperationEventKind.Waiting);
        Assert.Contains(progress.Events, e => e.Kind is OperationEventKind.Warning &&
                                              e.Message.Contains("PID 4000", StringComparison.Ordinal));
        Assert.Contains(progress.Events, e => e.Kind is OperationEventKind.Success);
    }

    [Fact]
    public void SoftResetRecoversServiceAlreadyStuckInStopPending()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.StopPending, ServiceStartType.Manual);
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.True(report.Success);
        Assert.Equal(0, services.StopCalls);
        Assert.Equal(1, services.TerminateCalls);
        Assert.Equal(1, services.StartCalls);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
    }

    [Fact]
    public void SoftResetNeverTerminatesSharedServiceHost()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.HangOnStop = true;
        services.OtherHostedServices.Add("AudioSrv");
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.False(report.Success);
        Assert.True(report.RebootRequired);
        Assert.Equal("SOFT RESET: RESTART REQUIRED", report.Title);
        Assert.Equal(0, services.ProcessesTerminated);
        Assert.Equal(0, services.StartCalls);
        Assert.Equal(ServiceStartType.Manual, services.Btag.StartType);
        Assert.Contains(report.Lines, line => line.Contains("AudioSrv", StringComparison.Ordinal));
        Assert.Contains(report.Lines, line => line.Contains("Restart Windows", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Lines, line => line.StartsWith("[ERROR]", StringComparison.Ordinal));
    }

    [Fact]
    public void FailedTerminationRequiresRestartAndExplainsWhy()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.HangOnStop = true;
        services.TerminationOverride = HostTerminationResult.Failed("Access is denied.");
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.True(report.RebootRequired);
        Assert.Contains(report.Lines, line => line.Contains("Access is denied.", StringComparison.Ordinal));
        Assert.Equal(ServiceRunState.StopPending, services.Btag.RunState);
    }

    [Fact]
    public void ServiceThatKeepsHangingIsNotTerminatedEndlessly()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.HangOnStop = true;
        services.HangAgainAfterTermination = true;
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.True(report.RebootRequired);
        // One recovery while stopping, one while starting, then it gives up.
        Assert.Equal(2, services.ProcessesTerminated);
        Assert.Equal(2, services.StartCalls);
        Assert.Contains(report.Lines, line => line.Contains("again", StringComparison.Ordinal));
    }

    [Fact]
    public void SoftResetHandlesServiceRestartedByRecoveryActionsAfterTermination()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.HangOnStop = true;
        services.RecoveryRestartsAfterTermination = true;
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.True(report.Success);
        Assert.Equal(1, services.ProcessesTerminated);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
    }

    [Fact]
    public void SlowServiceThatReportsProgressIsNeverTerminated()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.WaitHint = TimeSpan.FromSeconds(2);
        services.StopProgressPolls = 120; // ~30 s of advancing checkpoints
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.True(report.Success);
        Assert.Equal(0, services.TerminateCalls);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
    }

    [Fact]
    public void StopRefusedBecauseOfDependentServicesIsExplained()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.StopError = "dependent services are running";
        services.StopErrorCode = Win32ErrorCodes.DependentServicesRunning;
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.False(report.Success);
        Assert.Contains(report.Lines, line => line.Contains("depend on this service", StringComparison.Ordinal));
        Assert.Equal(0, services.TerminateCalls);
    }

    [Fact]
    public void HardModeDisablesStartupBeforeStoppingService()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.HardDisableHandsFree();

        Assert.True(report.Success);
        Assert.Equal(["SetStartType:Disabled", "Stop"], services.Calls);
    }

    [Fact]
    public void HardModeRecoversHungServiceAndCompletes()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.HangOnStop = true;
        services.RecoveryRestartsAfterTermination = true; // must not matter: startup is already Disabled
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);

        var report = engine.HardDisableHandsFree();

        Assert.True(report.Success);
        Assert.Equal(1, services.ProcessesTerminated);
        Assert.Equal(ServiceStartType.Disabled, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Stopped, services.Btag.RunState);
        var original = Assert.IsType<BackupFile>(backup.Stored);
        Assert.Equal(ServiceRunState.Running, original.Services["BTAGService"].RunState);
        Assert.Equal(ServiceStartType.Manual, original.Services["BTAGService"].StartType);
    }

    [Fact]
    public void HardModeWithSharedHostKeepsStartupDisabledAndRequiresRestart()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.HangOnStop = true;
        services.OtherHostedServices.Add("AudioSrv");
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);

        var report = engine.HardDisableHandsFree();

        Assert.True(report.RebootRequired);
        Assert.Equal("HARD MODE: RESTART REQUIRED", report.Title);
        Assert.Equal(ServiceStartType.Disabled, services.Btag.StartType);
        Assert.NotNull(backup.Stored);
        Assert.DoesNotContain(report.Lines, line => line.StartsWith("[ERROR]", StringComparison.Ordinal));
    }

    [Fact]
    public void HardModeBacksUpStableStateAfterStartTransitionFinishes()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.StartPending, ServiceStartType.Manual);
        services.PendingPollsRemaining = 3;
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);

        var report = engine.HardDisableHandsFree();

        Assert.True(report.Success);
        var original = Assert.IsType<BackupFile>(backup.Stored);
        Assert.Equal(ServiceRunState.Running, original.Services["BTAGService"].RunState);
    }

    [Fact]
    public void RestoreRecoversHungDisabledServiceFromLegacyStopPendingBackup()
    {
        // The exact state left behind by v2.2: hung in StopPending, Disabled, backup recorded StopPending.
        var services = FakeServiceManager.WithBtag(ServiceRunState.StopPending, ServiceStartType.Disabled);
        services.ProcessId = 3000;
        var backup = new MemoryBackupStore
        {
            Stored = new BackupFile(
                1,
                DateTimeOffset.UtcNow,
                new Dictionary<string, BackupServiceState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BTAGService"] = new(true, ServiceRunState.StopPending, ServiceStartType.Manual),
                    ["BthHFSrv"] = new(false, ServiceRunState.NotPresent, ServiceStartType.NotPresent)
                })
        };
        var engine = CreateEngine(services, backup);

        var report = engine.RestoreHandsFree();

        Assert.True(report.Success);
        Assert.Equal(1, services.ProcessesTerminated);
        Assert.Equal(ServiceStartType.Manual, services.Btag.StartType);
        Assert.Equal(ServiceRunState.Running, services.Btag.RunState);
        Assert.Null(backup.Stored);
        Assert.Contains(report.Lines, line => line.Contains("transitional state", StringComparison.Ordinal));
    }

    [Fact]
    public void RestoreReportsServiceThatExitsImmediatelyAfterStartAndKeepsBackup()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);
        Assert.True(engine.HardDisableHandsFree().Success);
        services.ExitImmediatelyOnStart = true;

        var report = engine.RestoreHandsFree();

        Assert.False(report.Success);
        Assert.Equal(1, services.StartCalls);
        Assert.Contains(report.Lines, line => line.Contains("stopped right after starting", StringComparison.Ordinal) &&
                                              line.Contains("1067", StringComparison.Ordinal));
        Assert.NotNull(backup.Stored);
    }

    [Fact]
    public void RestoreKeepsBackupWhenRestartIsRequired()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var backup = new MemoryBackupStore();
        var engine = CreateEngine(services, backup);
        Assert.True(engine.HardDisableHandsFree().Success);
        services.Btag = services.Btag with { RunState = ServiceRunState.StopPending };
        services.OtherHostedServices.Add("AudioSrv");

        var report = engine.RestoreHandsFree();

        Assert.True(report.RebootRequired);
        Assert.Equal("RESTORE: RESTART REQUIRED", report.Title);
        Assert.Equal(ServiceStartType.Manual, services.Btag.StartType);
        Assert.NotNull(backup.Stored);
        Assert.Equal(0, backup.DeleteCalls);
    }

    [Fact]
    public void StatusExplainsTransitionalStateWithoutTouchingServices()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.StopPending, ServiceStartType.Manual);
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.GetStatusReport();

        Assert.True(report.Success);
        Assert.Contains(report.Lines, line => line.StartsWith("[WARN]", StringComparison.Ordinal) &&
                                              line.Contains("transitional state", StringComparison.Ordinal));
        Assert.Empty(services.Calls);
    }

    [Fact]
    public void HostWithRebootRecoveryActionIsNeverTerminated()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        services.HangOnStop = true;
        services.TerminationOverride = HostTerminationResult.UnsafeRecoveryAction(["BTAGService"]);
        var engine = CreateEngine(services, new MemoryBackupStore());

        var report = engine.SoftResetHandsFree();

        Assert.True(report.RebootRequired);
        Assert.Equal(0, services.ProcessesTerminated);
        Assert.Contains(report.Lines, line => line.Contains("restart the computer or run a program", StringComparison.Ordinal));
    }

    [Fact]
    public void StatusNamesAppsThatHoldTheHeadsetMicrophone()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var engine = CreateEngine(
            services,
            new MemoryBackupStore(),
            microphones: FakeMicrophoneProbe.HeadsetUsedBy("Discord", "Zoom"));

        var report = engine.GetStatusReport();

        Assert.Contains(report.Lines, line =>
            line.StartsWith("[WARN]", StringComparison.Ordinal) &&
            line.Contains("Discord, Zoom", StringComparison.Ordinal) &&
            line.Contains(Headset.Name, StringComparison.Ordinal));
        Assert.Contains(report.Lines, line => line.Contains("default microphone", StringComparison.Ordinal));
    }

    [Fact]
    public void SoftResetWarnsThatAnAppWillReopenHandsFreeButStillResets()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var microphones = FakeMicrophoneProbe.HeadsetUsedBy("Discord");
        var engine = CreateEngine(services, new MemoryBackupStore(), microphones: microphones);

        var report = engine.SoftResetHandsFree();

        Assert.True(report.Success);
        Assert.Equal(1, services.StopCalls);
        Assert.Equal(1, services.StartCalls);
        Assert.Equal(1, microphones.Calls);
        Assert.Contains(report.Lines, line =>
            line.StartsWith("[WARN]", StringComparison.Ordinal) &&
            line.Contains("Discord", StringComparison.Ordinal));
    }

    [Fact]
    public void SoftResetConfirmsFreeHeadsetMicrophoneInsteadOfGenericAdvice()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var engine = CreateEngine(services, new MemoryBackupStore(), microphones: FakeMicrophoneProbe.HeadsetUsedBy());

        var report = engine.SoftResetHandsFree();

        Assert.Contains(Text.Get("mic.soft.idle"), report.Lines);
        Assert.DoesNotContain(Text.Get("soft.closeMicrophoneApps"), report.Lines);
    }

    [Fact]
    public void FailingMicrophoneProbeNeverBreaksAnOperation()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var microphones = new FakeMicrophoneProbe(() => throw new InvalidOperationException("audio stack broken"));
        var engine = CreateEngine(services, new MemoryBackupStore(), microphones: microphones);

        var status = engine.GetStatusReport();
        var soft = engine.SoftResetHandsFree();

        Assert.Contains(status.Lines, line => line.Contains("audio stack broken", StringComparison.Ordinal));
        Assert.True(soft.Success);
        Assert.Contains(Text.Get("soft.closeMicrophoneApps"), soft.Lines);
    }

    [Fact]
    public void HardModeNamesAppsThatLoseTheHeadsetMicrophone()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var engine = CreateEngine(
            services,
            new MemoryBackupStore(),
            microphones: FakeMicrophoneProbe.HeadsetUsedBy("Microsoft Teams"));

        var report = engine.HardDisableHandsFree();

        Assert.True(report.Success);
        Assert.Contains(report.Lines, line =>
            line.StartsWith("[WARN]", StringComparison.Ordinal) &&
            line.Contains("Microsoft Teams", StringComparison.Ordinal));
    }

    [Fact]
    public void EngineWithoutMicrophoneProbeKeepsGenericAdvice()
    {
        var services = FakeServiceManager.WithBtag(ServiceRunState.Running, ServiceStartType.Manual);
        var engine = CreateEngine(services, new MemoryBackupStore());

        Assert.Null(engine.DiagnoseMicrophone());
        Assert.Contains(Text.Get("soft.closeMicrophoneApps"), engine.SoftResetHandsFree().Lines);
    }

    private static readonly CaptureDevice Headset = new("headset", "Headset (WH-1000XM5)", true, false, true);

    private static ToggleEngine CreateEngine(
        IServiceManager services,
        IBackupStore backup,
        FakeClock? clock = null,
        IMicrophoneProbe? microphones = null)
        => new(
            services,
            backup,
            $"BluetoothHandsFreeToggle.Tests.{Guid.NewGuid():N}",
            clock ?? new FakeClock(),
            microphones: microphones);

    private sealed class FakeMicrophoneProbe(Func<MicrophoneProbeResult> probe) : IMicrophoneProbe
    {
        public int Calls { get; private set; }

        public static FakeMicrophoneProbe HeadsetUsedBy(params string[] apps)
            => new(() => new MicrophoneProbeResult(
                [Headset],
                apps.Select((app, index) => new CaptureClient(
                    Headset.Id,
                    new AppIdentity(1000 + index, app, $@"C:\Apps\{app}.exe", null))).ToList(),
                null));

        public MicrophoneProbeResult Probe()
        {
            Calls++;
            return probe();
        }
    }

    private sealed class FakeClock : IOperationClock
    {
        public TimeSpan Elapsed { get; private set; }
        public void Sleep(TimeSpan duration) => Elapsed += duration;
    }

    private sealed class RecordingProgress : IProgress<OperationEvent>
    {
        public List<OperationEvent> Events { get; } = [];
        public void Report(OperationEvent value) => Events.Add(value);
    }

    /// <summary>
    /// In-memory model of one SCM-managed service, including the failure modes seen in the field:
    /// hanging in StopPending/StartPending, slow-but-progressing transitions, exiting right after
    /// start, shared service hosts, and SCM recovery actions restarting a terminated service.
    /// </summary>
    private sealed class FakeServiceManager : IServiceManager
    {
        private int _nextProcessId = 4100;

        public required ServiceSnapshot Btag { get; set; }
        public int ProcessId { get; set; } = 4000;
        public int CheckPoint { get; set; }
        public TimeSpan WaitHint { get; set; } = TimeSpan.FromSeconds(10);
        public int ExitCode { get; set; }

        public string? StopError { get; set; }
        public int StopErrorCode { get; set; } = Win32ErrorCodes.AccessDenied;
        public string? StartError { get; set; }
        public string? SetStartTypeError { get; set; }

        public bool HangOnStop { get; set; }
        public bool HangOnStart { get; set; }
        public bool ExitImmediatelyOnStart { get; set; }
        public bool HangAgainAfterTermination { get; set; }
        public bool RecoveryRestartsAfterTermination { get; set; }
        public int StopProgressPolls { get; set; }
        public int? PendingPollsRemaining { get; set; }
        public HostTerminationResult? TerminationOverride { get; set; }
        public List<string> OtherHostedServices { get; } = [];

        public int StopCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int SetStartTypeCalls { get; private set; }
        public int TerminateCalls { get; private set; }
        public int ProcessesTerminated { get; private set; }
        public List<ServiceStartType> StartTypeChanges { get; } = [];
        public List<string> Calls { get; } = [];

        public static FakeServiceManager WithBtag(
            ServiceRunState runState,
            ServiceStartType startType)
            => new()
            {
                Btag = new ServiceSnapshot(
                    "BTAGService",
                    "Bluetooth Audio Gateway (HFP)",
                    Exists: true,
                    QuerySucceeded: true,
                    RunState: runState,
                    StartType: startType,
                    NativeStartValue: ToNativeStartType(startType),
                    Note: null)
            };

        public ServiceSnapshot GetSnapshot(TargetService target)
        {
            if (IsBtag(target.ServiceName))
                return Btag;

            return new ServiceSnapshot(
                target.ServiceName,
                target.FriendlyName,
                Exists: false,
                QuerySucceeded: true,
                RunState: ServiceRunState.NotPresent,
                StartType: ServiceStartType.NotPresent,
                NativeStartValue: null,
                Note: null);
        }

        public ServiceQueryResult QueryStatus(string serviceName)
        {
            if (!IsBtag(serviceName))
                return ServiceQueryResult.Failed("service does not exist");

            AdvancePendingTransition();
            return ServiceQueryResult.Ok(new ServiceStatus(
                Btag.RunState,
                Btag.RunState is ServiceRunState.Stopped ? 0 : ProcessId,
                CheckPoint,
                WaitHint,
                ExitCode,
                0));
        }

        public ServiceCommandResult SendStop(string serviceName)
        {
            StopCalls++;
            Calls.Add("Stop");
            if (StopError is not null)
                return ServiceCommandResult.Failed(StopErrorCode, StopError);

            switch (Btag.RunState)
            {
                case ServiceRunState.Stopped:
                    return ServiceCommandResult.AlreadyInState;
                case ServiceRunState.Running:
                case ServiceRunState.Paused:
                    break;
                default:
                    return ServiceCommandResult.NotAccepted;
            }

            CheckPoint = 0;
            if (HangOnStop)
            {
                SetRunState(ServiceRunState.StopPending);
            }
            else if (StopProgressPolls > 0)
            {
                SetRunState(ServiceRunState.StopPending);
                PendingPollsRemaining = StopProgressPolls;
            }
            else
            {
                SetRunState(ServiceRunState.Stopped);
            }

            return ServiceCommandResult.Accepted;
        }

        public ServiceCommandResult SendStart(string serviceName)
        {
            StartCalls++;
            Calls.Add("Start");
            if (StartError is not null)
                return ServiceCommandResult.Failed(1, StartError);

            if (Btag.StartType is ServiceStartType.Disabled)
                return ServiceCommandResult.Failed(Win32ErrorCodes.ServiceDisabled, "service is disabled");

            if (Btag.RunState is ServiceRunState.Running or ServiceRunState.StartPending)
                return ServiceCommandResult.AlreadyInState;

            if (Btag.RunState is not ServiceRunState.Stopped)
                return ServiceCommandResult.Failed(Win32ErrorCodes.ServiceCannotAcceptControl, "busy");

            ProcessId = _nextProcessId++;
            CheckPoint = 0;
            if (ExitImmediatelyOnStart)
                ExitCode = 1067;
            else
                SetRunState(HangOnStart ? ServiceRunState.StartPending : ServiceRunState.Running);

            return ServiceCommandResult.Accepted;
        }

        public bool TrySetStartType(
            string serviceName,
            ServiceStartType startType,
            out string? errorMessage)
        {
            SetStartTypeCalls++;
            Calls.Add($"SetStartType:{startType}");
            errorMessage = SetStartTypeError;
            if (errorMessage is not null)
                return false;

            StartTypeChanges.Add(startType);
            Btag = Btag with
            {
                StartType = startType,
                NativeStartValue = ToNativeStartType(startType)
            };
            return true;
        }

        public HostTerminationResult TerminateHungServiceHost(
            string serviceName,
            int expectedProcessId,
            IReadOnlyCollection<string> allowedServices)
        {
            TerminateCalls++;
            Calls.Add("Terminate");
            Assert.Contains("BTAGService", allowedServices);

            if (expectedProcessId != ProcessId || !ServiceRunStates.IsPending(Btag.RunState))
                return HostTerminationResult.StateChanged;

            if (OtherHostedServices.Count > 0)
                return HostTerminationResult.SharedHost(OtherHostedServices.ToList());

            if (TerminationOverride is not null)
                return TerminationOverride;

            ProcessesTerminated++;
            HangOnStop = HangAgainAfterTermination;
            HangOnStart = HangAgainAfterTermination;
            PendingPollsRemaining = null;

            if (RecoveryRestartsAfterTermination && Btag.StartType is not ServiceStartType.Disabled)
            {
                ProcessId = _nextProcessId++;
                SetRunState(ServiceRunState.Running);
            }
            else
            {
                SetRunState(ServiceRunState.Stopped);
            }

            return HostTerminationResult.Terminated;
        }

        private void AdvancePendingTransition()
        {
            if (PendingPollsRemaining is not { } remaining)
                return;

            if (remaining > 0)
            {
                PendingPollsRemaining = remaining - 1;
                CheckPoint++;
                return;
            }

            PendingPollsRemaining = null;
            SetRunState(Btag.RunState is ServiceRunState.StopPending
                ? ServiceRunState.Stopped
                : ServiceRunState.Running);
        }

        private void SetRunState(ServiceRunState runState)
            => Btag = Btag with { RunState = runState };

        private static bool IsBtag(string serviceName)
            => serviceName.Equals("BTAGService", StringComparison.OrdinalIgnoreCase);

        private static int? ToNativeStartType(ServiceStartType startType)
            => startType switch
            {
                ServiceStartType.Auto => 2,
                ServiceStartType.Manual => 3,
                ServiceStartType.Disabled => 4,
                _ => null
            };
    }

    private sealed class MemoryBackupStore : IBackupStore
    {
        public string BackupPath => "memory://backup.json";
        public BackupFile? Stored { get; set; }
        public string? LoadError { get; set; }
        public string? PrepareError { get; set; }
        public string? SaveError { get; set; }
        public string? DeleteError { get; set; }
        public int SaveCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int PrepareCalls { get; private set; }

        public BackupWriteResult Prepare()
        {
            PrepareCalls++;
            return PrepareError is null
                ? BackupWriteResult.Ok()
                : BackupWriteResult.Failed(PrepareError);
        }

        public BackupLoadResult Load()
        {
            if (LoadError is not null)
                return BackupLoadResult.Failed(LoadError);

            return Stored is null
                ? BackupLoadResult.Missing()
                : BackupLoadResult.Loaded(Stored);
        }

        public BackupWriteResult Save(IEnumerable<ServiceSnapshot> snapshots)
        {
            SaveCalls++;
            if (SaveError is not null)
                return BackupWriteResult.Failed(SaveError);

            Stored = new BackupFile(
                1,
                DateTimeOffset.UtcNow,
                snapshots.ToDictionary(
                    snapshot => snapshot.ServiceName,
                    snapshot => new BackupServiceState(
                        snapshot.Exists,
                        snapshot.RunState,
                        snapshot.StartType),
                    StringComparer.OrdinalIgnoreCase));

            return BackupWriteResult.Ok();
        }

        public BackupWriteResult Delete()
        {
            DeleteCalls++;
            if (DeleteError is not null)
                return BackupWriteResult.Failed(DeleteError);

            Stored = null;
            return BackupWriteResult.Ok();
        }
    }
}
