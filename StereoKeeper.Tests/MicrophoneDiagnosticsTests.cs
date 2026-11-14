using StereoKeeper.Core;
using Xunit;

namespace StereoKeeper.Tests;

public sealed class MicrophoneDiagnosticsTests
{
    private static readonly CaptureDevice Headset = new("headset", "Headset (WH-1000XM5)", true, false, false);
    private static readonly CaptureDevice UsbMicrophone = new("usb", "Microphone (fifine)", false, true, true);

    [Fact]
    public void AppRecordingFromHeadsetMicrophoneIsReportedAsCause()
    {
        var diagnosis = Analyze([Headset, UsbMicrophone], Client("headset", 100, "Discord", @"C:\Apps\Discord.exe"));

        Assert.Equal(MicrophoneVerdict.InUse, diagnosis.Verdict);
        var app = Assert.Single(diagnosis.BluetoothApps);
        Assert.Equal("Discord", app.Name);
        Assert.Equal([100], app.ProcessIds);
        Assert.Equal([Headset.Name], app.DeviceNames);
        Assert.Empty(diagnosis.OtherApps);
    }

    [Fact]
    public void HelperProcessesOfOneExecutableAreReportedAsOneApp()
    {
        var diagnosis = Analyze(
            [Headset],
            Client("headset", 300, "Google Chrome", @"C:\Chrome\chrome.exe"),
            Client("headset", 200, "Google Chrome", @"C:\CHROME\Chrome.exe"));

        var app = Assert.Single(diagnosis.BluetoothApps);
        Assert.Equal([200, 300], app.ProcessIds);
    }

    [Fact]
    public void AppsRecordingFromOtherMicrophonesDoNotAffectTheHeadset()
    {
        var diagnosis = Analyze([Headset, UsbMicrophone], Client("usb", 100, "OBS Studio", @"C:\OBS\obs64.exe"));

        Assert.Equal(MicrophoneVerdict.Idle, diagnosis.Verdict);
        Assert.Empty(diagnosis.BluetoothApps);
        Assert.Equal("OBS Studio", Assert.Single(diagnosis.OtherApps).Name);
    }

    [Fact]
    public void ClientOfUnknownDeviceIsNotTreatedAsHeadsetUser()
    {
        var diagnosis = Analyze([Headset], Client("vanished", 100, "Zoom", null));

        Assert.Equal(MicrophoneVerdict.Idle, diagnosis.Verdict);
        Assert.Equal(["vanished"], Assert.Single(diagnosis.OtherApps).DeviceNames);
    }

    [Fact]
    public void WithoutBluetoothMicrophoneVerdictIsNoBluetoothMicrophone()
    {
        var diagnosis = Analyze([UsbMicrophone], Client("usb", 100, "Discord", null));

        Assert.Equal(MicrophoneVerdict.NoBluetoothMicrophone, diagnosis.Verdict);
        Assert.Null(diagnosis.DefaultBluetoothMicrophone);
    }

    [Fact]
    public void IncompleteProbeNeverClaimsTheHeadsetIsFree()
    {
        var diagnosis = MicrophoneDiagnostics.Analyze(new MicrophoneProbeResult([Headset], [], "sessions unavailable"));

        Assert.Equal(MicrophoneVerdict.Unavailable, diagnosis.Verdict);
        Assert.Equal("sessions unavailable", diagnosis.Error);
    }

    [Fact]
    public void ConfirmedHeadsetUserWinsOverIncompleteProbe()
    {
        var diagnosis = MicrophoneDiagnostics.Analyze(new MicrophoneProbeResult(
            [Headset],
            [Client("headset", 100, "Discord", null)],
            "another headset could not be read"));

        Assert.Equal(MicrophoneVerdict.InUse, diagnosis.Verdict);
    }

    [Fact]
    public void FailedProbeIsUnavailable()
    {
        var diagnosis = MicrophoneDiagnostics.Analyze(MicrophoneProbeResult.Failed("timeout"));

        Assert.Equal(MicrophoneVerdict.Unavailable, diagnosis.Verdict);
        Assert.Empty(diagnosis.BluetoothMicrophones);
    }

    [Fact]
    public void HeadsetSetAsDefaultCommunicationsMicrophoneIsDetected()
    {
        var defaultHeadset = Headset with { IsDefaultCommunications = true };
        var usb = UsbMicrophone with { IsDefault = false, IsDefaultCommunications = false };

        var diagnosis = Analyze([defaultHeadset, usb]);

        Assert.Equal(MicrophoneVerdict.Idle, diagnosis.Verdict);
        Assert.Equal(defaultHeadset, diagnosis.DefaultBluetoothMicrophone);
    }

    private static MicrophoneDiagnosis Analyze(IReadOnlyList<CaptureDevice> devices, params CaptureClient[] clients)
        => MicrophoneDiagnostics.Analyze(new MicrophoneProbeResult(devices, clients, null));

    private static CaptureClient Client(string deviceId, int processId, string name, string? path)
        => new(deviceId, new AppIdentity(processId, name, path, null));
}
