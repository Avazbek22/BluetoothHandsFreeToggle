namespace BluetoothHandsFreeToggle.Core;

/// <summary>
/// Gathers raw facts about microphones and the apps recording from them. Implementations must
/// never open an audio stream: opening the headset microphone is exactly what switches the headset
/// to Hands-Free.
/// </summary>
public interface IMicrophoneProbe
{
    MicrophoneProbeResult Probe();
}

/// <summary>An active audio capture endpoint (a microphone Windows can record from).</summary>
public sealed record CaptureDevice(
    string Id,
    string Name,
    bool IsBluetooth,
    bool IsDefault,
    bool IsDefaultCommunications);

/// <summary>The identity of a running process, resolved to the app a person would recognise.</summary>
public sealed record AppIdentity(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    string? PackageFamilyName);

/// <summary>A process that is recording from the device <paramref name="DeviceId"/> right now.</summary>
public sealed record CaptureClient(string DeviceId, AppIdentity App);

/// <param name="Error">
/// Set when the recording apps of at least one headset microphone could not be read, so a
/// "nobody is using it" answer would not be reliable.
/// </param>
public sealed record MicrophoneProbeResult(
    IReadOnlyList<CaptureDevice> Devices,
    IReadOnlyList<CaptureClient> Clients,
    string? Error)
{
    /// <summary>
    /// Names of the Bluetooth audio devices that are connected right now (they have an active
    /// playback or recording endpoint), for example "HUAWEI FreeBuds Pro 4".
    /// </summary>
    public IReadOnlyList<string> Headsets { get; init; } = [];

    public static MicrophoneProbeResult Failed(string error) => new([], [], error);
}

public enum MicrophoneVerdict
{
    /// <summary>It could not be determined whether an app is using the headset microphone.</summary>
    Unavailable,

    /// <summary>No Bluetooth headset microphone is present (not connected, or Hands-Free is off).</summary>
    NoBluetoothMicrophone,

    /// <summary>A Bluetooth headset microphone is present and no app is recording from it.</summary>
    Idle,

    /// <summary>Apps are recording from the headset microphone, which keeps the headset in Hands-Free.</summary>
    InUse
}

/// <summary>One app (possibly several processes, such as a browser) and the microphones it records from.</summary>
public sealed record MicrophoneApp(
    string Name,
    string? ExecutablePath,
    IReadOnlyList<int> ProcessIds,
    IReadOnlyList<string> DeviceNames);

public sealed record MicrophoneDiagnosis(
    MicrophoneVerdict Verdict,
    IReadOnlyList<CaptureDevice> BluetoothMicrophones,
    IReadOnlyList<MicrophoneApp> BluetoothApps,
    IReadOnlyList<MicrophoneApp> OtherApps,
    string? Error)
{
    /// <summary>
    /// A headset microphone that Windows gives to apps set to the default input device: such apps
    /// switch the headset to Hands-Free as soon as they start recording.
    /// </summary>
    public CaptureDevice? DefaultBluetoothMicrophone
        => BluetoothMicrophones.FirstOrDefault(device => device.IsDefault || device.IsDefaultCommunications);

    /// <summary>Connected Bluetooth audio devices, see <see cref="MicrophoneProbeResult.Headsets"/>.</summary>
    public IReadOnlyList<string> Headsets { get; init; } = [];

    public static MicrophoneDiagnosis Unavailable(string error)
        => new(MicrophoneVerdict.Unavailable, [], [], [], error);
}

public static class MicrophoneDiagnostics
{
    public static MicrophoneDiagnosis Analyze(MicrophoneProbeResult probe)
    {
        var devicesById = probe.Devices
            .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var bluetoothMicrophones = probe.Devices.Where(device => device.IsBluetooth).ToList();

        var bluetoothClients = new List<CaptureClient>();
        var otherClients = new List<CaptureClient>();
        foreach (var client in probe.Clients)
        {
            if (devicesById.TryGetValue(client.DeviceId, out var device) && device.IsBluetooth)
                bluetoothClients.Add(client);
            else
                otherClients.Add(client);
        }

        var bluetoothApps = GroupByApp(bluetoothClients, devicesById);
        var otherApps = GroupByApp(otherClients, devicesById);

        // A confirmed headset-microphone user is a definite answer even if another headset's
        // sessions could not be read; "nobody" is only trusted when nothing failed.
        var verdict = bluetoothApps.Count > 0 ? MicrophoneVerdict.InUse
            : probe.Error is not null ? MicrophoneVerdict.Unavailable
            : bluetoothMicrophones.Count == 0 ? MicrophoneVerdict.NoBluetoothMicrophone
            : MicrophoneVerdict.Idle;

        return new MicrophoneDiagnosis(
            verdict,
            bluetoothMicrophones,
            bluetoothApps,
            otherApps,
            probe.Error)
        {
            Headsets = probe.Headsets
        };
    }

    private static List<MicrophoneApp> GroupByApp(
        IEnumerable<CaptureClient> clients,
        Dictionary<string, CaptureDevice> devicesById)
    {
        // Browsers and Electron apps record from helper processes; report them as one app.
        return clients
            .GroupBy(client => client.App.ExecutablePath ?? client.App.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MicrophoneApp(
                group.First().App.Name,
                group.First().App.ExecutablePath,
                group.Select(client => client.App.ProcessId).Distinct().Order().ToList(),
                group
                    .Select(client => devicesById.TryGetValue(client.DeviceId, out var device)
                        ? device.Name
                        : client.DeviceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
