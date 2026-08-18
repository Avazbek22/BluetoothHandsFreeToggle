using BluetoothHandsFreeToggle.Core;
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
        services.FailStartWhenDisabled = true;
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

    private static ToggleEngine CreateEngine(IServiceManager services, IBackupStore backup)
        => new(services, backup, $"BluetoothHandsFreeToggle.Tests.{Guid.NewGuid():N}");

    private sealed class FakeServiceManager : IServiceManager
    {
        public required ServiceSnapshot Btag { get; set; }
        public string? StopError { get; set; }
        public string? StartError { get; set; }
        public string? SetStartTypeError { get; set; }
        public bool FailStartWhenDisabled { get; set; }
        public int StopCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int SetStartTypeCalls { get; private set; }
        public List<ServiceStartType> StartTypeChanges { get; } = [];

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
            if (target.ServiceName.Equals("BTAGService", StringComparison.OrdinalIgnoreCase))
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

        public bool TryStopService(string serviceName, TimeSpan timeout, out string? errorMessage)
        {
            StopCalls++;
            errorMessage = StopError;
            if (errorMessage is not null)
                return false;

            Btag = Btag with { RunState = ServiceRunState.Stopped };
            return true;
        }

        public bool TryStartService(string serviceName, TimeSpan timeout, out string? errorMessage)
        {
            StartCalls++;
            errorMessage = StartError;
            if (errorMessage is not null)
                return false;

            if (FailStartWhenDisabled && Btag.StartType is ServiceStartType.Disabled)
            {
                errorMessage = "service is disabled";
                return false;
            }

            Btag = Btag with { RunState = ServiceRunState.Running };
            return true;
        }

        public bool TrySetStartType(
            string serviceName,
            ServiceStartType startType,
            out string? errorMessage)
        {
            SetStartTypeCalls++;
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
