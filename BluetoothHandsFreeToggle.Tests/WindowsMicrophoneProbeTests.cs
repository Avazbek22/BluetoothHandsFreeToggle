using System.Diagnostics;
using System.Runtime.InteropServices;
using BluetoothHandsFreeToggle.Windows;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed class WindowsMicrophoneProbeTests
{
    [Fact]
    public void ProbeCompletesQuicklyWithoutListingItsOwnProcess()
    {
        var stopwatch = Stopwatch.StartNew();

        // Machines without audio hardware (CI runners) return no devices or an error, never an exception.
        var result = new WindowsMicrophoneProbe().Probe();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6));
        Assert.All(result.Devices, device => Assert.False(string.IsNullOrWhiteSpace(device.Id)));
        Assert.All(result.Clients, client => Assert.NotEqual(Environment.ProcessId, client.App.ProcessId));
        Assert.All(result.Clients, client => Assert.Contains(result.Devices, device => device.Id == client.DeviceId));
        Assert.True(result.Devices.Count(device => device.IsDefault) <= 1);
    }

    [Fact]
    public void BluetoothIndexNeverTreatsTheComputerItselfAsBluetoothDevice()
    {
        // Local radio services (for example Bluetooth COM ports) belong to the computer's container;
        // counting it would mark built-in microphones as Bluetooth.
        var containers = BluetoothDeviceIndex.GetBluetoothContainerIds();

        Assert.DoesNotContain(BluetoothDeviceIndex.ComputerContainerId, containers);
        Assert.DoesNotContain(Guid.Empty, containers);
    }

    [Fact]
    public void PropVariantMatchesNativeLayout()
    {
        // PROPVARIANT is 16 bytes on 32-bit Windows and 24 bytes on x64 and ARM64.
        Assert.Equal(IntPtr.Size == 8 ? 24 : 16, Marshal.SizeOf<PropVariant>());
        Assert.Equal(8, Marshal.OffsetOf<PropVariant>(nameof(PropVariant.Value)).ToInt32());
    }

    [Fact]
    public void IdentityOfRunningProcessIncludesExecutableAndReadableName()
    {
        var identity = new ProcessIdentityResolver().Resolve(Environment.ProcessId);

        Assert.Equal(Environment.ProcessId, identity.ProcessId);
        Assert.Equal(Environment.ProcessPath, identity.ExecutablePath, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(identity.Name));
    }

    [Fact]
    public void IdentityOfExitedProcessFallsBackWithoutThrowing()
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        process.WaitForExit();

        var identity = new ProcessIdentityResolver().Resolve(process.Id);

        Assert.False(string.IsNullOrWhiteSpace(identity.Name));
    }

    [Fact]
    public void ParentProcessSnapshotContainsCurrentProcess()
    {
        var parents = NativeProcessApi.GetParentProcessIds();

        Assert.True(parents.TryGetValue(Environment.ProcessId, out var parentId));
        Assert.True(parentId > 0);
    }
}
