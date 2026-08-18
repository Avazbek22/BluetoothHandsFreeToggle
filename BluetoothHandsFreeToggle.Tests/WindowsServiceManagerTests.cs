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
