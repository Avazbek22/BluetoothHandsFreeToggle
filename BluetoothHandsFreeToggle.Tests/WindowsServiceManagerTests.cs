using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Windows;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed class WindowsServiceManagerTests
{
    [Fact]
    public void MissingServiceIsReportedAsAbsentInsteadOfQueryFailure()
    {
        var manager = new WindowsServiceManager();
        var target = new TargetService(
            $"BluetoothHandsFreeToggle.Tests.{Guid.NewGuid():N}",
            "Nonexistent test service");

        var snapshot = manager.GetSnapshot(target);

        Assert.True(snapshot.QuerySucceeded);
        Assert.False(snapshot.Exists);
        Assert.Equal(ServiceRunState.NotPresent, snapshot.RunState);
        Assert.Equal(ServiceStartType.NotPresent, snapshot.StartType);
    }

    [Fact]
    public void SupportedTargetQueriesCompleteWithoutServiceControlManagerErrors()
    {
        var manager = new WindowsServiceManager();

        var snapshots = Targets.Services.Select(manager.GetSnapshot).ToList();

        Assert.All(snapshots, snapshot => Assert.True(
            snapshot.QuerySucceeded,
            $"{snapshot.ServiceName}: {snapshot.Note}"));
    }

    [Fact]
    public void QueryStatusReadsProcessIdOfRunningSystemService()
    {
        var manager = new WindowsServiceManager();

        // RpcSs always runs and is readable without elevation; this validates the native struct layout.
        var result = manager.QueryStatus("RpcSs");

        Assert.True(result.Success, result.Error);
        Assert.Equal(ServiceRunState.Running, result.Status!.RunState);
        Assert.True(result.Status.ProcessId > 0);
    }

    [Fact]
    public void ServiceEnumerationFindsServicesHostedByProcess()
    {
        var status = new WindowsServiceManager().QueryStatus("RpcSs").Status!;

        var found = NativeServiceApi.TryGetServicesInProcess(status.ProcessId, out var names, out var error);

        Assert.True(found, error);
        Assert.Contains(names, name => name.Equals("RpcSs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QueryStatusOfMissingServiceFails()
    {
        var manager = new WindowsServiceManager();

        var result = manager.QueryStatus($"BluetoothHandsFreeToggle.Tests.{Guid.NewGuid():N}");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void HostTerminationRefusesOwnProcessBeforeOpeningIt()
    {
        var manager = new WindowsServiceManager();

        var result = manager.TerminateHungServiceHost(
            "BTAGService",
            Environment.ProcessId,
            ["BTAGService"]);

        Assert.Equal(HostTerminationOutcome.UnexpectedHost, result.Outcome);
    }

    [Fact]
    public void FailureActionsDetectRebootConfiguredForCriticalService()
    {
        // RpcSs is configured by Windows to reboot the computer when it crashes.
        var read = NativeServiceApi.TryGetFailureActions("RpcSs", out var actions, out var error);

        Assert.True(read, error);
        Assert.Contains(ServiceRecoveryAction.Reboot, actions);
    }

    [Fact]
    public void HostTerminationPrecheckRefusesRunningServiceWithoutOpeningItsProcess()
    {
        var manager = new WindowsServiceManager();
        var status = manager.QueryStatus("RpcSs").Status!;

        var result = manager.TerminateHungServiceHost("RpcSs", status.ProcessId, ["RpcSs"]);

        Assert.Equal(HostTerminationOutcome.StateChanged, result.Outcome);
        Assert.Equal(ServiceRunState.Running, manager.QueryStatus("RpcSs").Status!.RunState);
    }

    [Fact]
    public void UnsupportedStartupTypeIsRejectedBeforeCallingWindows()
    {
        var manager = new WindowsServiceManager();

        var changed = manager.TrySetStartType(
            $"BluetoothHandsFreeToggle.Tests.{Guid.NewGuid():N}",
            ServiceStartType.Unknown,
            out var error);

        Assert.False(changed);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
