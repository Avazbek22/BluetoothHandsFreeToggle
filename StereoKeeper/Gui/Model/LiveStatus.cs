using StereoKeeper.Core;

namespace StereoKeeper.Gui.Model;

/// <summary>What the headset is doing right now, as the panel and the tray icon show it.</summary>
public enum HeadsetPhase
{
    /// <summary>The first status check has not finished yet.</summary>
    Loading,

    /// <summary>No Bluetooth audio device is connected.</summary>
    NoHeadset,

    /// <summary>A headset is connected and nothing is using its microphone: A2DP stereo.</summary>
    Stereo,

    /// <summary>An app records from the headset microphone, so Windows keeps it in HFP (mono).</summary>
    CallMode,

    /// <summary>Hard mode disabled HFP: always stereo, no headset microphone.</summary>
    Hard,

    /// <summary>An HFP service has been stuck in a transitional state for a while.</summary>
    Hung,

    /// <summary>This Windows has no supported HFP service.</summary>
    ServicesMissing,

    /// <summary>The service state could not be read.</summary>
    Unavailable
}

public sealed record LiveStatus(
    HeadsetPhase Phase,
    IReadOnlyList<ServiceSnapshot> Services,
    MicrophoneDiagnosis? Microphone,
    string? DeviceName,
    ServiceSnapshot? HungService,
    string? Error)
{
    public static LiveStatus Loading { get; } = new(HeadsetPhase.Loading, [], null, null, null, null);

    public bool HardModeActive => Services.Any(service => service.Exists && service.StartType is ServiceStartType.Disabled);

    public bool HasServices => Services.Any(service => service.Exists);

    /// <summary>
    /// True when <paramref name="other"/> would look exactly the same in the panel and the tray, so
    /// a repeated check with no news can be skipped without touching the UI.
    /// </summary>
    public bool IsEquivalentTo(LiveStatus? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return Phase == other.Phase &&
               DeviceName == other.DeviceName &&
               Error == other.Error &&
               HungService?.ServiceName == other.HungService?.ServiceName &&
               Services.SequenceEqual(other.Services) &&
               SameMicrophone(Microphone, other.Microphone);
    }

    private static bool SameMicrophone(MicrophoneDiagnosis? left, MicrophoneDiagnosis? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.Verdict == right.Verdict &&
               left.Error == right.Error &&
               left.Headsets.SequenceEqual(right.Headsets) &&
               left.BluetoothMicrophones.SequenceEqual(right.BluetoothMicrophones) &&
               SameApps(left.BluetoothApps, right.BluetoothApps) &&
               SameApps(left.OtherApps, right.OtherApps);
    }

    // Process ids are left out on purpose: a browser tab restarting is not news for the panel.
    private static bool SameApps(IReadOnlyList<MicrophoneApp> left, IReadOnlyList<MicrophoneApp> right)
        => left.Count == right.Count &&
           left.Zip(right).All(pair =>
               pair.First.Name == pair.Second.Name &&
               pair.First.DeviceNames.SequenceEqual(pair.Second.DeviceNames));

    /// <param name="services">The HFP services, as <see cref="ToggleEngine.GetServiceSnapshots"/> returns them.</param>
    /// <param name="microphone">The microphone diagnosis, or <c>null</c> when it is not available.</param>
    /// <param name="hungServiceNames">
    /// Services that have stayed in a transitional state across several checks. A single pending
    /// reading is normal (a headset connecting starts the service), so the caller tracks duration.
    /// </param>
    public static LiveStatus Evaluate(
        IReadOnlyList<ServiceSnapshot> services,
        MicrophoneDiagnosis? microphone,
        IReadOnlySet<string>? hungServiceNames = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var deviceName = microphone switch
        {
            { Headsets.Count: > 0 } => microphone.Headsets[0],
            { BluetoothMicrophones.Count: > 0 } => microphone.BluetoothMicrophones[0].Name,
            _ => null
        };

        var failed = services.FirstOrDefault(service => !service.QuerySucceeded);
        if (failed is not null)
        {
            return new LiveStatus(
                HeadsetPhase.Unavailable, services, microphone, deviceName, null,
                failed.Note ?? failed.FriendlyName);
        }

        var present = services.Where(service => service.Exists).ToList();
        if (present.Count == 0)
            return new LiveStatus(HeadsetPhase.ServicesMissing, services, microphone, deviceName, null, null);

        if (present.Any(service => service.StartType is ServiceStartType.Disabled))
            return new LiveStatus(HeadsetPhase.Hard, services, microphone, deviceName, null, null);

        var hung = present.FirstOrDefault(service =>
            ServiceRunStates.IsPending(service.RunState) &&
            hungServiceNames?.Contains(service.ServiceName) == true);
        if (hung is not null)
            return new LiveStatus(HeadsetPhase.Hung, services, microphone, deviceName, hung, null);

        if (microphone?.Verdict is MicrophoneVerdict.InUse)
            return new LiveStatus(HeadsetPhase.CallMode, services, microphone, deviceName ?? (microphone.BluetoothApps[0].DeviceNames is { Count: > 0 } names ? names[0] : null), null, null);

        return deviceName is null
            ? new LiveStatus(HeadsetPhase.NoHeadset, services, microphone, null, null, null)
            : new LiveStatus(HeadsetPhase.Stereo, services, microphone, deviceName, null, null);
    }
}
