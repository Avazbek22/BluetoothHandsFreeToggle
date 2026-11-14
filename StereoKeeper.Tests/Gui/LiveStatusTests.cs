using StereoKeeper.Core;
using StereoKeeper.Gui.Model;
using Xunit;
using static StereoKeeper.Tests.Gui.Fixtures;

namespace StereoKeeper.Tests.Gui;

public sealed class LiveStatusTests
{
    [Fact]
    public void ConnectedHeadsetWithFreeMicrophoneIsStereo()
    {
        var status = LiveStatus.Evaluate(Services(), IdleHeadset());

        Assert.Equal(HeadsetPhase.Stereo, status.Phase);
        Assert.Equal(HeadsetName, status.DeviceName);
        Assert.False(status.HardModeActive);
    }

    [Fact]
    public void AppRecordingFromHeadsetIsCallMode()
    {
        var status = LiveStatus.Evaluate(Services(), HeadsetInUse("Discord"));

        Assert.Equal(HeadsetPhase.CallMode, status.Phase);
        Assert.Equal(HeadsetName, status.DeviceName);
    }

    [Fact]
    public void CallModeFallsBackToTheMicrophoneNameWhenNoPlaybackEndpointIsKnown()
    {
        var microphone = MicrophoneDiagnostics.Analyze(new MicrophoneProbeResult(
            [HeadsetMicrophone],
            [Client(HeadsetMicrophone.Id, 7, "Zoom")],
            null));

        var status = LiveStatus.Evaluate(Services(), microphone);

        Assert.Equal(HeadsetPhase.CallMode, status.Phase);
        Assert.Equal(HeadsetMicrophone.Name, status.DeviceName);
    }

    [Fact]
    public void NoBluetoothDeviceMeansNoHeadset()
    {
        var status = LiveStatus.Evaluate(Services(), NoHeadset());

        Assert.Equal(HeadsetPhase.NoHeadset, status.Phase);
        Assert.Null(status.DeviceName);
    }

    [Fact]
    public void MissingMicrophoneDiagnosisStillShowsTheServices()
    {
        var status = LiveStatus.Evaluate(Services(), microphone: null);

        Assert.Equal(HeadsetPhase.NoHeadset, status.Phase);
        Assert.True(status.HasServices);
    }

    [Fact]
    public void DisabledServiceIsHardModeEvenWhileAnAppHoldsTheMicrophone()
    {
        var services = new List<ServiceSnapshot>
        {
            Service("BthHFSrv", ServiceRunState.Stopped, ServiceStartType.Disabled),
            Service("BTAGService", ServiceRunState.Running)
        };

        var status = LiveStatus.Evaluate(services, HeadsetInUse("Discord"));

        Assert.Equal(HeadsetPhase.Hard, status.Phase);
        Assert.True(status.HardModeActive);
    }

    [Fact]
    public void PendingServiceIsHungOnlyWhenTheCallerSaysItHasBeenStuck()
    {
        var services = new List<ServiceSnapshot>
        {
            Service("BthHFSrv"),
            Service("BTAGService", ServiceRunState.StopPending)
        };

        var fresh = LiveStatus.Evaluate(services, IdleHeadset());
        var stuck = LiveStatus.Evaluate(services, IdleHeadset(), new HashSet<string> { "BTAGService" });

        Assert.Equal(HeadsetPhase.Stereo, fresh.Phase);
        Assert.Equal(HeadsetPhase.Hung, stuck.Phase);
        Assert.Equal("BTAGService", stuck.HungService?.ServiceName);
    }

    [Fact]
    public void HungNameForARunningServiceIsIgnored()
    {
        var status = LiveStatus.Evaluate(Services(), IdleHeadset(), new HashSet<string> { "BTAGService" });

        Assert.Equal(HeadsetPhase.Stereo, status.Phase);
    }

    [Fact]
    public void FailedQueryIsUnavailableWithTheReason()
    {
        var services = new List<ServiceSnapshot>
        {
            Service("BthHFSrv", querySucceeded: false, note: "Access is denied."),
            Service("BTAGService")
        };

        var status = LiveStatus.Evaluate(services, IdleHeadset());

        Assert.Equal(HeadsetPhase.Unavailable, status.Phase);
        Assert.Equal("Access is denied.", status.Error);
    }

    [Fact]
    public void NoServicesAtAllIsServicesMissing()
    {
        var services = new List<ServiceSnapshot>
        {
            Service("BthHFSrv", exists: false),
            Service("BTAGService", exists: false)
        };

        var status = LiveStatus.Evaluate(services, IdleHeadset());

        Assert.Equal(HeadsetPhase.ServicesMissing, status.Phase);
        Assert.False(status.HasServices);
        Assert.False(status.HardModeActive);
    }

    [Fact]
    public void LoadingHasNoServices()
    {
        Assert.Equal(HeadsetPhase.Loading, LiveStatus.Loading.Phase);
        Assert.False(LiveStatus.Loading.HasServices);
    }
}
