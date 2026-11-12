using BluetoothHandsFreeToggle.Core;
using static BluetoothHandsFreeToggle.Windows.CoreAudioInterop;

namespace BluetoothHandsFreeToggle.Windows;

/// <summary>
/// Finds which apps are recording from which microphone by reading the WASAPI audio sessions of
/// every active capture endpoint. All shared and exclusive capture goes through Windows Audio, so
/// sessions see every recording app together with the exact device and process.
/// The probe only reads state: it never creates an audio client, so it cannot itself switch a
/// headset to Hands-Free.
/// </summary>
/// <remarks>
/// The privacy usage log in the registry (ConsentStore LastUsedTimeStart/Stop) is deliberately not
/// used: recent Windows 11 builds no longer update it, so it would report stale "in use" entries.
/// </remarks>
public sealed class WindowsMicrophoneProbe : IMicrophoneProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _timeout;

    public WindowsMicrophoneProbe(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? DefaultTimeout;
    }

    public MicrophoneProbeResult Probe()
    {
        // A dedicated MTA thread keeps COM independent of the caller (an STA UI thread works too),
        // and the timeout keeps a hung Windows Audio service from freezing the caller.
        MicrophoneProbeResult? result = null;
        var thread = new Thread(() => result = ProbeCore())
        {
            IsBackground = true,
            Name = "Microphone diagnostics"
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();

        if (!thread.Join(_timeout))
        {
            return MicrophoneProbeResult.Failed(
                $"Windows Audio did not respond within {_timeout.TotalSeconds:0} s.");
        }

        return result ?? MicrophoneProbeResult.Failed("The microphone probe returned no result.");
    }

    private static MicrophoneProbeResult ProbeCore()
    {
        var comInitialized = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
        try
        {
            var identities = new ProcessIdentityResolver();

            DeviceProbe devices;
            try
            {
                devices = ProbeDevices();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                devices = DeviceProbe.Failed(exception.Message);
            }

            var clients = devices.Clients
                .Distinct()
                .Select(client => new CaptureClient(client.DeviceId, identities.Resolve(client.ProcessId)))
                .ToList();

            return new MicrophoneProbeResult(devices.Devices, clients, devices.Error)
            {
                Headsets = devices.Headsets
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // This runs on a worker thread: an escaping exception would terminate the process.
            return MicrophoneProbeResult.Failed(exception.Message);
        }
        finally
        {
            if (comInitialized >= 0)
                CoUninitialize();
        }
    }

    private static DeviceProbe ProbeDevices()
    {
        var hr = CoCreateInstance(
            in MMDeviceEnumeratorClsid,
            IntPtr.Zero,
            ClsctxAll,
            typeof(IMMDeviceEnumerator).GUID,
            out var rawEnumerator);
        if (hr < 0)
            return DeviceProbe.Failed(FormatHResult("CoCreateInstance(MMDeviceEnumerator)", hr));

        using var enumerator = ComRef<IMMDeviceEnumerator>.TryWrap(rawEnumerator);
        if (enumerator is null)
            return DeviceProbe.Failed("IMMDeviceEnumerator is not available.");

        var defaultId = TryGetDefaultDeviceId(enumerator.Value, RoleConsole);
        var defaultCommunicationsId = TryGetDefaultDeviceId(enumerator.Value, RoleCommunications);

        hr = enumerator.Value.EnumAudioEndpoints(DataFlowCapture, DeviceStateActive, out var rawCollection);
        if (hr < 0)
            return DeviceProbe.Failed(FormatHResult("IMMDeviceEnumerator.EnumAudioEndpoints", hr));

        using var collection = ComRef<IMMDeviceCollection>.TryWrap(rawCollection);
        if (collection is null)
            return DeviceProbe.Failed("IMMDeviceCollection is not available.");

        hr = collection.Value.GetCount(out var count);
        if (hr < 0)
            return DeviceProbe.Failed(FormatHResult("IMMDeviceCollection.GetCount", hr));

        var bluetoothContainers = BluetoothDeviceIndex.GetBluetoothContainers();
        var devices = new List<CaptureDevice>();
        var clients = new List<RawClient>();
        var errors = new List<string>();
        var headsets = new List<string>();

        // A connected headset always has an active playback endpoint (A2DP or Hands-Free), also
        // while Hard mode keeps its microphone switched off.
        CollectBluetoothPlayback(enumerator.Value, bluetoothContainers, headsets);

        for (uint index = 0; index < count; index++)
        {
            if (collection.Value.Item(index, out var rawDevice) < 0)
                continue;

            using var device = ComRef<IMMDevice>.TryWrap(rawDevice);
            if (device is null)
                continue;

            var id = TryGetId(device.Value);
            if (id is null)
                continue;

            var (name, containerId) = ReadProperties(device.Value);
            var isBluetooth = containerId is { } container && bluetoothContainers.ContainsKey(container);
            if (isBluetooth)
                AddHeadset(headsets, bluetoothContainers[containerId!.Value] ?? name);

            devices.Add(new CaptureDevice(
                id,
                name ?? id,
                isBluetooth,
                string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
                string.Equals(id, defaultCommunicationsId, StringComparison.OrdinalIgnoreCase)));

            var sessionError = CollectActiveClients(device.Value, id, clients);

            // Only a headset microphone whose users are unknown makes the answer uncertain.
            if (sessionError is not null && isBluetooth)
                errors.Add($"{name ?? id}: {sessionError}");
        }

        return new DeviceProbe(devices, clients, errors.Count > 0 ? string.Join("; ", errors) : null)
        {
            Headsets = headsets
        };
    }

    private static void CollectBluetoothPlayback(
        IMMDeviceEnumerator enumerator,
        Dictionary<Guid, string?> bluetoothContainers,
        List<string> headsets)
    {
        if (bluetoothContainers.Count == 0 ||
            enumerator.EnumAudioEndpoints(DataFlowRender, DeviceStateActive, out var rawCollection) < 0)
        {
            return;
        }

        using var collection = ComRef<IMMDeviceCollection>.TryWrap(rawCollection);
        if (collection is null || collection.Value.GetCount(out var count) < 0)
            return;

        for (uint index = 0; index < count; index++)
        {
            if (collection.Value.Item(index, out var rawDevice) < 0)
                continue;

            using var device = ComRef<IMMDevice>.TryWrap(rawDevice);
            if (device is null)
                continue;

            var (name, containerId) = ReadProperties(device.Value);
            if (containerId is { } container && bluetoothContainers.TryGetValue(container, out var deviceName))
                AddHeadset(headsets, deviceName ?? name);
        }
    }

    private static void AddHeadset(List<string> headsets, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !headsets.Contains(name, StringComparer.OrdinalIgnoreCase))
            headsets.Add(name);
    }

    private static string? CollectActiveClients(IMMDevice device, string deviceId, List<RawClient> clients)
    {
        var hr = device.Activate(typeof(IAudioSessionManager2).GUID, ClsctxAll, IntPtr.Zero, out var rawManager);
        if (hr < 0)
            return FormatHResult("IMMDevice.Activate(IAudioSessionManager2)", hr);

        using var manager = ComRef<IAudioSessionManager2>.TryWrap(rawManager);
        if (manager is null)
            return "IAudioSessionManager2 is not available.";

        hr = manager.Value.GetSessionEnumerator(out var rawSessions);
        if (hr < 0)
            return FormatHResult("IAudioSessionManager2.GetSessionEnumerator", hr);

        using var sessions = ComRef<IAudioSessionEnumerator>.TryWrap(rawSessions);
        if (sessions is null)
            return "IAudioSessionEnumerator is not available.";

        hr = sessions.Value.GetCount(out var count);
        if (hr < 0)
            return FormatHResult("IAudioSessionEnumerator.GetCount", hr);

        for (var index = 0; index < count; index++)
        {
            if (sessions.Value.GetSession(index, out var rawSession) < 0)
                continue;

            using var session = ComRef<IAudioSessionControl2>.TryWrap(rawSession);
            if (session is null)
                continue;

            // Only Active sessions are recording now; Inactive ones may belong to a closed stream.
            if (session.Value.GetState(out var state) < 0 || state != AudioSessionStateActive)
                continue;

            if (session.Value.IsSystemSoundsSession() == SOk)
                continue;

            hr = session.Value.GetProcessId(out var processId);
            if ((hr != SOk && hr != AudclntSNoSingleProcess) ||
                processId == 0 ||
                processId == (uint)Environment.ProcessId)
            {
                continue;
            }

            clients.Add(new RawClient(deviceId, (int)processId));
        }

        return null;
    }

    private static string? TryGetDefaultDeviceId(IMMDeviceEnumerator enumerator, int role)
    {
        // E_NOTFOUND simply means there is no microphone for this role.
        if (enumerator.GetDefaultAudioEndpoint(DataFlowCapture, role, out var rawDevice) < 0)
            return null;

        using var device = ComRef<IMMDevice>.TryWrap(rawDevice);
        return device is null ? null : TryGetId(device.Value);
    }

    private static string? TryGetId(IMMDevice device)
        => device.GetId(out var rawId) < 0 ? null : TakeString(rawId);

    private static (string? Name, Guid? ContainerId) ReadProperties(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out var rawStore) < 0)
            return (null, null);

        using var store = ComRef<IPropertyStore>.TryWrap(rawStore);
        if (store is null)
            return (null, null);

        return (TryGetString(store.Value, DeviceFriendlyName), TryGetGuid(store.Value, DeviceContainerId));
    }

    private sealed record RawClient(string DeviceId, int ProcessId);

    private sealed record DeviceProbe(List<CaptureDevice> Devices, List<RawClient> Clients, string? Error)
    {
        public List<string> Headsets { get; init; } = [];

        public static DeviceProbe Failed(string error) => new([], [], error);
    }
}
