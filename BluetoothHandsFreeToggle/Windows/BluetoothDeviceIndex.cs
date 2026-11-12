using System.Runtime.InteropServices;

namespace BluetoothHandsFreeToggle.Windows;

/// <summary>
/// Finds the device containers of Bluetooth devices. Windows groups every devnode of one physical
/// device (its Bluetooth profiles, audio endpoints, HID functions) into a container, so an audio
/// endpoint whose container holds a Bluetooth devnode belongs to a Bluetooth device. This holds
/// for the classic stack and for audio-offload hardware (for example Intel Smart Sound), where the
/// endpoint itself is enumerated by the audio DSP rather than by the Bluetooth stack.
/// </summary>
internal static class BluetoothDeviceIndex
{
    /// <summary>The computer itself. Local Bluetooth radio services live here and must not count.</summary>
    public static readonly Guid ComputerContainerId = new("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

    private static readonly string[] BluetoothEnumerators = ["BTHENUM", "BTHLE", "BTHLEDEVICE", "BTHHFENUM"];

    private static readonly DevPropKey ContainerIdKey =
        new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

    // DEVPKEY_NAME: the name Windows shows for the devnode in Settings and Device Manager.
    private static readonly DevPropKey NameKey =
        new(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);

    private const int CrSuccess = 0;
    private const int CrBufferSmall = 0x1A;
    private const int CmGetIdListFilterEnumerator = 0x1;
    private const int CmLocateDevnodeNormal = 0x0;
    private const int DevPropTypeGuid = 0xD;
    private const int DevPropTypeString = 0x12;

    public static HashSet<Guid> GetBluetoothContainerIds()
        => GetBluetoothContainers().Keys.ToHashSet();

    /// <summary>
    /// Maps every Bluetooth device container to the device's own name (for example
    /// "HUAWEI FreeBuds Pro 4"), or <c>null</c> when Windows reports none.
    /// </summary>
    public static Dictionary<Guid, string?> GetBluetoothContainers()
    {
        var containers = new Dictionary<Guid, string?>();

        foreach (var enumerator in BluetoothEnumerators)
        {
            foreach (var instanceId in GetDeviceIds(enumerator))
            {
                if (!TryLocate(instanceId, out var devInst) ||
                    !TryGetContainerId(devInst, out var containerId) ||
                    !IsDeviceContainer(containerId))
                {
                    continue;
                }

                containers.TryAdd(containerId, null);

                // Profile devnodes have names like "Headset Gateway" or "AVRCP Transport X"; only the
                // device node itself (BTHENUM\DEV_<address>, BTHLE\DEV_<address>) carries the plain name.
                if (containers[containerId] is null &&
                    IsDeviceNode(instanceId) &&
                    TryGetName(devInst) is { } name)
                {
                    containers[containerId] = name;
                }
            }
        }

        return containers;
    }

    private static bool IsDeviceNode(string instanceId)
    {
        var separator = instanceId.IndexOf('\\', StringComparison.Ordinal);
        return separator > 0 &&
               instanceId.AsSpan(separator + 1).StartsWith("DEV_", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDeviceContainer(Guid containerId)
        => containerId != Guid.Empty && containerId != ComputerContainerId;

    private static List<string> GetDeviceIds(string enumerator)
    {
        // The list can grow between the size query and the read (a device connecting); retry.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_Size(out var length, enumerator, CmGetIdListFilterEnumerator) != CrSuccess ||
                length <= 1)
            {
                return [];
            }

            var buffer = new char[length];
            var result = CM_Get_Device_ID_List(enumerator, buffer, length, CmGetIdListFilterEnumerator);
            if (result == CrBufferSmall)
                continue;

            if (result != CrSuccess)
                return [];

            return new string(buffer)
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        return [];
    }

    // Only present devices can be located with CM_LOCATE_DEVNODE_NORMAL; absent ones are skipped.
    private static bool TryLocate(string instanceId, out int devInst)
        => CM_Locate_DevNode(out devInst, instanceId, CmLocateDevnodeNormal) == CrSuccess;

    private static string? TryGetName(int devInst)
    {
        var buffer = new byte[512];
        var size = buffer.Length;
        var result = CM_Get_DevNode_Property(devInst, in NameKey, out var type, buffer, ref size, 0);
        if (result == CrBufferSmall && size > buffer.Length)
        {
            buffer = new byte[size];
            result = CM_Get_DevNode_Property(devInst, in NameKey, out type, buffer, ref size, 0);
        }

        if (result != CrSuccess || type != DevPropTypeString || size < 2)
            return null;

        var name = System.Text.Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0').Trim();
        return name.Length == 0 ? null : name;
    }

    private static bool TryGetContainerId(int devInst, out Guid containerId)
    {
        containerId = Guid.Empty;

        var buffer = new byte[16];
        var size = buffer.Length;
        if (CM_Get_DevNode_Property(devInst, in ContainerIdKey, out var type, buffer, ref size, 0) != CrSuccess ||
            type != DevPropTypeGuid ||
            size != 16)
        {
            return false;
        }

        containerId = new Guid(buffer);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DevPropKey(Guid FormatId, int PropertyId);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_ID_List_SizeW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_Device_ID_List_Size(out int length, string? filter, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_ID_ListW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_Device_ID_List(string? filter, [Out] char[] buffer, int length, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Locate_DevNode(out int devInst, string deviceId, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_DevNode_PropertyW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_DevNode_Property(
        int devInst,
        in DevPropKey propertyKey,
        out int propertyType,
        [Out] byte[] buffer,
        ref int bufferSize,
        int flags);
}
